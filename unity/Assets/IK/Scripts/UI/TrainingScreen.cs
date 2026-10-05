using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using IK.Core;
using IK.Input;

namespace IK.UI {
    /// <summary>
    /// dev.3 gate screen: the character is actually played. A <see cref="Fighter"/> runs the
    /// real .cmd / .cns state machine at a fixed 60 ticks per second, fed by the dev.1
    /// <see cref="InputRouter"/> (touch, gamepad or keyboard), and what is drawn is the state
    /// the engine is in — animation element, axis, offsets and collision boxes straight from
    /// the .air.
    ///
    /// There is no opponent yet (dev.4): HitDefs are counted, not applied. The HUD exists so
    /// a human can see the engine's internals on the phone — state number, animation element,
    /// velocity, control flag and the last command the buffer matched.
    /// </summary>
    public class TrainingScreen : MonoBehaviour {
        public RectTransform Root { get; private set; }
        public Action onBack;

        public MugenCharacter Character { get; private set; }
        public Fighter Fighter { get; private set; }
        public CnsFile States { get; private set; }
        public CmdFile Commands { get; private set; }
        public string LoadError { get; private set; }
        public double LoadMilliseconds { get; private set; }
        /// <summary>Logic ticks run since the screen was opened.</summary>
        public int Ticks { get; private set; }
        /// <summary>The last command the buffer matched, for the HUD and the fixture.</summary>
        public string LastCommand { get; private set; } = "";
        /// <summary>The HUD label, so the rendered fixture can check its exact pixels.</summary>
        public RectTransform HudRect => hud != null ? (RectTransform)hud.transform : null;

        MugenAssetCache cache;
        AudioSource audioSource;

        Image sprite;
        RectTransform stage, clsnLayer;
        Text title, stats, hud, hud2, hint;
        Image powerFill;
        readonly List<Image> clsnPool = new List<Image>();

        bool showBoxes;
        bool showHud = true;

        /// <summary>Pixels per MUGEN unit (the character's localcoord is 320x240).</summary>
        public float Scale { get; private set; } = 2.6f;
        /// <summary>How far the character may walk from the centre before being held there.</summary>
        public const float StageHalfWidth = 220f;
        const float FloorY = 92f;

        // ------------------------------------------------------------------ build

        public void Build(RectTransform parent) {
            Root = UIKit.Panel(parent, "Training", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                               new Color(0.07f, 0.08f, 0.11f, 1f));

            stage = UIKit.Panel(Root, "stage", new Vector2(0f, 0f), new Vector2(1f, 1f),
                                new Vector2(0, 0), new Vector2(0, -70));
            UIKit.Panel(stage, "floor", new Vector2(0f, 0f), new Vector2(1f, 0f),
                        new Vector2(0, FloorY - 4), new Vector2(0, FloorY), new Color(1f, 1f, 1f, 0.2f));

            var spriteGo = new GameObject("sprite", typeof(RectTransform));
            spriteGo.transform.SetParent(stage, false);
            sprite = spriteGo.AddComponent<Image>();
            sprite.raycastTarget = false;
            sprite.preserveAspect = true;
            var srt = (RectTransform)spriteGo.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0f);
            srt.pivot = new Vector2(0.5f, 0.5f);

            clsnLayer = UIKit.Panel(stage, "clsn", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // between the Back button and the right-hand buttons; the right-hand buttons
            // move inwards on narrow screens, so the title stays short and the counts go
            // to their own line at the bottom left
            title = UIKit.Text(Root, "title", new Vector2(0f, 1f), new Vector2(360, -40), new Vector2(330, 40),
                               "", 24, TextAnchor.MiddleLeft, Skin.Accent);
            hud = UIKit.Text(Root, "hud", new Vector2(0f, 1f), new Vector2(330, -76), new Vector2(620, 30),
                             "", 20, TextAnchor.MiddleLeft, Skin.Text);
            hud2 = UIKit.Text(Root, "hud2", new Vector2(0f, 1f), new Vector2(330, -106), new Vector2(620, 30),
                              "", 20, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.72f));
            stats = UIKit.Text(Root, "stats", new Vector2(0f, 0f), new Vector2(330, 60), new Vector2(620, 28),
                               "", 18, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.55f));
            hint = UIKit.Text(Root, "hint", new Vector2(0.5f, 0f), new Vector2(0, 22), new Vector2(1180, 28),
                              Loc.T("training.hint"), 19, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.6f));

            var powerBack = UIKit.Panel(Root, "powerBack", new Vector2(0f, 1f), new Vector2(0f, 1f),
                                        new Vector2(330, -142), new Vector2(630, -126), new Color(1, 1, 1, 0.12f));
            powerFill = UIKit.Panel(powerBack, "powerFill", new Vector2(0f, 0f), new Vector2(0f, 1f),
                                    Vector2.zero, new Vector2(0, 0), Skin.Accent).GetComponent<Image>();

            UIKit.Button(Root, "Back", new Vector2(0f, 1f), new Vector2(90, -40), new Vector2(150, 58),
                         Loc.T("common.back"), () => onBack?.Invoke(), 23);
            UIKit.Button(Root, "Reset", new Vector2(1f, 1f), new Vector2(-90, -40), new Vector2(150, 58),
                         Loc.T("training.reset"), ResetFighter, 23);
            UIKit.Button(Root, "Boxes", new Vector2(1f, 1f), new Vector2(-250, -40), new Vector2(150, 58),
                         Loc.T("viewer.boxes"), ToggleBoxes, 23);
            UIKit.Button(Root, "Hud", new Vector2(1f, 1f), new Vector2(-410, -40), new Vector2(150, 58),
                         Loc.T("training.hud"), ToggleHud, 23);

            audioSource = gameObject.GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;

            SetVisible(false);
        }

        // ------------------------------------------------------------------ loading

        /// <summary>
        /// Loads the character and its state files once. On device everything comes from
        /// Resources as TextAssets (`chars/kfm/kfm_cns.bytes`), in the editor the same code
        /// path works with a <see cref="FileSource"/>.
        /// </summary>
        public bool EnsureLoaded(IResourceSource source = null, string defFile = "kfm.def") {
            if (Fighter != null || LoadError != null) return Fighter != null;
            var watch = Stopwatch.StartNew();
            try {
                var src = source ?? new ResourcesSource("chars/kfm");
                Character = MugenCharacter.Load(src, defFile);
                cache = new MugenAssetCache();

                var cnsBytes = src.Read(Character.CnsFile);
                if (cnsBytes == null) throw new System.IO.FileNotFoundException("cns not found: " + Character.CnsFile);
                States = CnsFile.Parse(cnsBytes);

                var cmdBytes = src.Read(Character.CmdFile);
                if (cmdBytes == null) throw new System.IO.FileNotFoundException("cmd not found: " + Character.CmdFile);
                Commands = CmdFile.Parse(cmdBytes);
                States.Merge(Commands.States);          // [Statedef -1] drives the commands

                Fighter = new Fighter(Character, States, Commands);
            } catch (Exception e) {
                LoadError = e.Message;
                UnityEngine.Debug.LogError("[IK] training load failed: " + e);
            }
            watch.Stop();
            LoadMilliseconds = watch.Elapsed.TotalMilliseconds;
            RefreshTitle();
            return Fighter != null;
        }

        void RefreshTitle() {
            if (title == null) return;
            if (Fighter == null) {
                UIKit.SetText(title, Loc.T("training.failed") + (LoadError != null ? ": " + LoadError : ""));
                return;
            }
            UIKit.SetText(title, Character.DisplayName);
            UIKit.SetText(stats, string.Format(Loc.T("training.title"),
                                               Character.Author,
                                               States.States.Count,
                                               Commands.Commands.Count));
        }

        public void ResetFighter() {
            if (Character == null || States == null) return;
            Fighter = new Fighter(Character, States, Commands);
            Ticks = 0;
            LastCommand = "";
            Redraw();
        }

        void ToggleBoxes() { showBoxes = !showBoxes; Redraw(); }
        void ToggleHud() {
            showHud = !showHud;
            if (hud != null) hud.gameObject.SetActive(showHud);
            if (hud2 != null) hud2.gameObject.SetActive(showHud);
        }

        // Hooks for the rendered UI fixture, doing what the buttons do.
        public void ShowBoxesForTests(bool on) { showBoxes = on; Redraw(); }

        // ------------------------------------------------------------------ logic

        /// <summary>
        /// One logic tick. Called from <see cref="InputRouter"/> at exactly 60 Hz, so the
        /// fight runs at MUGEN speed whatever the rendering frame rate is.
        /// </summary>
        public void Feed(InputFrame frame) {
            if (Fighter == null) return;
            Fighter.SetInput(ToCmdKey(frame, Fighter.Facing));
            Fighter.Tick();
            Ticks++;

            if (Fighter.Commands.JustCompleted.Count > 0) {
                // several commands can match one tick (`x` and `blocking` and `QCF_x` all end
                // on the same press): the interesting one is the longest motion, so rank by
                // how many steps the command needed.
                string best = null;
                int bestSteps = -1;
                foreach (var name in Fighter.Commands.JustCompleted) {
                    if (name.StartsWith("hold")) continue;
                    int steps = 0;
                    foreach (var c in Fighter.Commands.Commands)
                        if (c.Name == name && c.Steps.Count > steps) steps = c.Steps.Count;
                    if (steps > bestSteps || (steps == bestSteps && best != null && name.Length > best.Length)) {
                        best = name;
                        bestSteps = steps;
                    }
                }
                if (best != null) LastCommand = best;
            }

            // keep the character on the stage (dev.4 brings the real stage and camera)
            if (Fighter.PosX > StageHalfWidth) Fighter.PosX = StageHalfWidth;
            if (Fighter.PosX < -StageHalfWidth) Fighter.PosX = -StageHalfWidth;

            if (!string.IsNullOrEmpty(Fighter.LastSound)) PlaySound(Fighter.LastSound);
        }

        string lastPlayedSound;
        void PlaySound(string value) {
            if (value == lastPlayedSound) return;
            lastPlayedSound = value;
            if (Character == null || Character.Snd == null) return;
            var parts = MugenDef.SplitCsv(value);
            if (parts.Length < 2) return;
            var entry = Character.Snd.Get(MugenDef.Atoi(parts[0]), MugenDef.Atoi(parts[1]));
            if (entry == null) return;
            var clip = cache.ClipFor(entry);
            if (clip != null) audioSource.PlayOneShot(clip, Settings.SettingsStore.Current.sfxVolume / 100f);
        }

        /// <summary>
        /// Turns one input frame into the engine's key bits. L/R are screen directions, B/F
        /// are facing-relative — the same conversion Ikemen does before matching commands.
        /// </summary>
        public static CmdKey ToCmdKey(InputFrame f, int facing) {
            var k = CmdKey.None;
            if (f.U) k |= CmdKey.U;
            if (f.D) k |= CmdKey.D;
            bool back = facing >= 0 ? f.L : f.R;
            bool fwd = facing >= 0 ? f.R : f.L;
            if (back) k |= CmdKey.B;
            if (fwd) k |= CmdKey.F;
            if (f.a) k |= CmdKey.a;
            if (f.b) k |= CmdKey.b;
            if (f.c) k |= CmdKey.c;
            if (f.x) k |= CmdKey.x;
            if (f.y) k |= CmdKey.y;
            if (f.z) k |= CmdKey.z;
            if (f.s) k |= CmdKey.s;
            if (f.d) k |= CmdKey.d;
            if (f.w) k |= CmdKey.w;
            if (f.m) k |= CmdKey.m;
            return k;
        }

        void Update() {
            if (Root == null || !Root.gameObject.activeSelf) return;
            Redraw();
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>Draws the fighter's current animation element and the HUD.</summary>
        public void Redraw() {
            if (Fighter == null || sprite == null) return;
            var anim = Fighter.Anim;
            var frame = anim != null ? anim.CurrentFrame : null;
            var spr = Character.SpriteOf(frame);
            var rt = (RectTransform)sprite.transform;

            if (spr == null) {
                sprite.enabled = false;
            } else {
                uint[] pal = null;
                if (!spr.Raw && spr.OwnPalette == null && Character.Sff.Palettes.Count > 0)
                    pal = Character.Sff.Palettes[0];
                var unitySprite = cache.SpriteFor(Character.Sff, spr, pal);
                sprite.enabled = unitySprite != null;
                sprite.sprite = unitySprite;
                rt.sizeDelta = new Vector2(spr.Width * Scale, spr.Height * Scale);
                rt.pivot = new Vector2(spr.Width > 0 ? (float)spr.X / spr.Width : 0.5f,
                                       spr.Height > 0 ? 1f - (float)spr.Y / spr.Height : 0.5f);
                float x = (Fighter.PosX + frame.Xoffset * Fighter.Facing) * Scale;
                float y = FloorY - (Fighter.PosY + frame.Yoffset) * Scale;
                rt.anchoredPosition = new Vector2(x, y);
                rt.localScale = new Vector3(frame.Hscale * Mathf.Abs(frame.Xscale) * Fighter.Facing,
                                            frame.Vscale * Mathf.Abs(frame.Yscale), 1f);
            }

            DrawBoxes(frame);

            if (showHud) {
                UIKit.SetText(hud, string.Format(Loc.T("training.state"),
                                                 Fighter.StateNo, Fighter.StateTime,
                                                 Fighter.AnimNo, anim != null ? anim.CurrentElement + 1 : 0,
                                                 anim != null ? anim.Frames.Count : 0,
                                                 Fighter.Ctrl ? 1 : 0));
                UIKit.SetText(hud2, string.Format(Loc.T("training.vel"),
                                                  Fighter.VelX, Fighter.VelY,
                                                  Fighter.PosX, Fighter.PosY,
                                                  Fighter.Power, Fighter.HitDefCount,
                                                  string.IsNullOrEmpty(LastCommand) ? "—" : LastCommand));
            }
            if (powerFill != null) {
                var prt = (RectTransform)powerFill.transform;
                prt.anchorMax = new Vector2(Mathf.Clamp01(Fighter.Power / 3000f), 1f);
            }
        }

        void DrawBoxes(AnimFrame frame) {
            int used = 0;
            if (showBoxes && frame != null) {
                used += DrawBoxSet(frame.Clsn2, new Color(0.35f, 0.65f, 1f, 0.22f), used);
                used += DrawBoxSet(frame.Clsn1, new Color(1f, 0.35f, 0.3f, 0.32f), used);
            }
            for (int i = used; i < clsnPool.Count; i++) clsnPool[i].gameObject.SetActive(false);
        }

        int DrawBoxSet(List<float[]> boxes, Color color, int start) {
            if (boxes == null) return 0;
            for (int i = 0; i < boxes.Count; i++) {
                var img = BoxAt(start + i);
                var b = boxes[i];
                var rt = (RectTransform)img.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0f, 1f);
                float left = Fighter.Facing >= 0 ? b[0] : -b[2];
                rt.anchoredPosition = new Vector2((Fighter.PosX + left) * Scale,
                                                  FloorY - (Fighter.PosY + b[1]) * Scale);
                rt.sizeDelta = new Vector2((b[2] - b[0]) * Scale, (b[3] - b[1]) * Scale);
                img.color = color;
                img.gameObject.SetActive(true);
            }
            return boxes.Count;
        }

        Image BoxAt(int i) {
            while (clsnPool.Count <= i) {
                var go = new GameObject("box" + clsnPool.Count, typeof(RectTransform));
                go.transform.SetParent(clsnLayer, false);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                clsnPool.Add(img);
            }
            return clsnPool[i];
        }

        public void SetVisible(bool v) {
            if (Root == null) return;
            Root.gameObject.SetActive(v);
            if (v) {
                EnsureLoaded();
                RefreshTitle();
                Redraw();
            }
        }

        public bool Visible => Root != null && Root.gameObject.activeSelf;

        void OnDestroy() { cache?.Dispose(); }
    }
}
