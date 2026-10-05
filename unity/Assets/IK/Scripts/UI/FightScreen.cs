using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using IK.Core;
using IK.Input;

namespace IK.UI {
    /// <summary>
    /// dev.4 gate screen: a real fight. Two <see cref="Fighter"/>s run inside a
    /// <see cref="FightEngine"/> at a fixed 60 ticks per second; player 1 is driven by the
    /// dev.1 touch layer, player 2 by a small training dummy. The stage comes from the
    /// screenpack, the life and power bars are fed by the <see cref="LifeBarState"/> of
    /// `data/fight.def`, and the round flow runs from the announcement to the KO.
    ///
    /// The bars and announcements are drawn with the project's own UI primitives, not yet with
    /// the sprites of `fight.def`: the file is parsed and its numbers drive the logic, but the
    /// screenpack's own artwork is drawn in a later milestone. Said plainly so no one reads
    /// this screen as "the MUGEN HUD is done".
    /// </summary>
    public class FightScreen : MonoBehaviour {
        public RectTransform Root { get; private set; }
        public Action onBack;

        public MugenCharacter Character { get; private set; }
        public CnsFile States { get; private set; }
        public CmdFile Commands { get; private set; }
        public StageDefinition Stage { get; private set; }
        public FightDef Fight { get; private set; }
        /// <summary>`fight.sff`, the screenpack's HUD artwork.</summary>
        public SffFile FightSprites { get; private set; }
        /// <summary>The HUD drawn from `fight.def`; null when the motif could not be built.</summary>
        public FightHud Hud => hud;
        public FightEngine Engine { get; private set; }
        public string LoadError { get; private set; }
        public double LoadMilliseconds { get; private set; }
        public int Ticks { get; private set; }

        /// <summary>What player 2 does: 0 stand, 1 guard, 2 jump, 3 walk towards player 1.</summary>
        public int DummyMode;

        MugenAssetCache cache;
        AudioSource audioSource;

        RectTransform stageRect, clsnLayer, bgBack, bgFront;
        Image floorLine;
        StageRenderer stageRenderer;
        FightHud hud;
        readonly List<Graphic> fallbackHud = new List<Graphic>();
        readonly Image[] fighterSprites = new Image[2];
        readonly List<Image> clsnPool = new List<Image>();
        readonly Image[] lifeFill = new Image[2];
        readonly Image[] lifeMid = new Image[2];
        readonly Image[] powerFill = new Image[2];
        readonly Text[] winLabels = new Text[2];
        Text announce, debugLine, timer;
        bool showBoxes;

        /// <summary>Pixels per stage unit, derived from the stage's localcoord and the screen.</summary>
        public float Scale { get; private set; } = 2.6f;
        /// <summary>UI y of the floor (char y = 0) measured from the bottom of the stage rect.</summary>
        public float FloorY { get; private set; } = 92f;
        /// <summary>Visible area in stage units, used for background tiling.</summary>
        public float ViewWidth { get; private set; } = 320f;
        public float ViewHeight { get; private set; } = 240f;

        // ------------------------------------------------------------------ build

        public void Build(RectTransform parent) {
            Root = UIKit.Panel(parent, "Fight", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                               new Color(0.05f, 0.06f, 0.09f, 1f));

            stageRect = UIKit.Panel(Root, "stage", new Vector2(0f, 0f), new Vector2(1f, 1f),
                                    new Vector2(0, 0), new Vector2(0, -60));
            // the stage's own backgrounds, behind and in front of the fighters (MUGEN layerno)
            bgBack = UIKit.Panel(stageRect, "bgBack", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            floorLine = UIKit.Image(stageRect, "floor", new Vector2(0.5f, 0f), new Vector2(0, FloorY - 2),
                                    new Vector2(4000, 4), null, new Color(1f, 1f, 1f, 0.18f));

            for (int i = 0; i < 2; i++) {
                var go = new GameObject("fighter" + (i + 1), typeof(RectTransform));
                go.transform.SetParent(stageRect, false);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                img.preserveAspect = true;
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                fighterSprites[i] = img;
            }
            bgFront = UIKit.Panel(stageRect, "bgFront", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            clsnLayer = UIKit.Panel(stageRect, "clsn", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // life and power bars, mirrored for player 2
            for (int i = 0; i < 2; i++) {
                bool left = i == 0;
                float x = left ? 40f : -40f;
                var anchor = new Vector2(left ? 0f : 1f, 1f);
                var back = UIKit.Image(Root, "lifeBack" + i, anchor,
                                       new Vector2(left ? x + 260f : x - 260f, -40f), new Vector2(520, 26), null, new Color(0f, 0f, 0f, 0.55f));
                var midGo = UIKit.Image(back.rectTransform, "lifeMid" + i, new Vector2(0.5f, 0.5f),
                                        Vector2.zero, new Vector2(516, 22), null, new Color(0.9f, 0.75f, 0.2f, 0.9f));
                var fillGo = UIKit.Image(back.rectTransform, "lifeFill" + i, new Vector2(0.5f, 0.5f),
                                         Vector2.zero, new Vector2(516, 22), null, new Color(0.25f, 0.8f, 0.35f, 1f));
                foreach (var img in new[] { midGo, fillGo }) {
                    var rt = img.rectTransform;
                    rt.anchorMin = new Vector2(left ? 0f : 1f, 0f);
                    rt.anchorMax = new Vector2(left ? 0f : 1f, 1f);
                    rt.pivot = new Vector2(left ? 0f : 1f, 0.5f);
                    rt.offsetMin = new Vector2(left ? 2f : -518f, 2f);
                    rt.offsetMax = new Vector2(left ? 518f : -2f, -2f);
                }
                lifeMid[i] = midGo;
                lifeFill[i] = fillGo;
                fallbackHud.Add(back); fallbackHud.Add(midGo); fallbackHud.Add(fillGo);

                var pback = UIKit.Image(Root, "powerBack" + i, anchor,
                                        new Vector2(left ? x + 160f : x - 160f, -74f), new Vector2(320, 14), null, new Color(0f, 0f, 0f, 0.5f));
                var pfill = UIKit.Image(pback.rectTransform, "powerFill" + i, new Vector2(0.5f, 0.5f),
                                        Vector2.zero, new Vector2(316, 10), null, Skin.Accent);
                var prt = pfill.rectTransform;
                prt.anchorMin = new Vector2(left ? 0f : 1f, 0f);
                prt.anchorMax = new Vector2(left ? 0f : 1f, 1f);
                prt.pivot = new Vector2(left ? 0f : 1f, 0.5f);
                prt.offsetMin = new Vector2(left ? 2f : -318f, 2f);
                prt.offsetMax = new Vector2(left ? 318f : -2f, -2f);
                powerFill[i] = pfill;
                fallbackHud.Add(pback); fallbackHud.Add(pfill);

                fallbackHud.Add(winLabels[i] = UIKit.Text(Root, "wins" + i, anchor,
                                          new Vector2(left ? x + 20f : x - 20f, -100f), new Vector2(160, 26),
                                          "", 20, left ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight,
                                          new Color(1f, 1f, 1f, 0.7f)));
            }

            timer = UIKit.Text(Root, "timer", new Vector2(0.5f, 1f), new Vector2(0, -44), new Vector2(160, 48),
                               "", 34, TextAnchor.MiddleCenter, Skin.Text);
            announce = UIKit.Text(Root, "announce", new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(900, 70),
                                  "", 46, TextAnchor.MiddleCenter, Skin.Accent);
            fallbackHud.Add(timer); fallbackHud.Add(announce);
            debugLine = UIKit.Text(Root, "hud", new Vector2(0f, 0f), new Vector2(330, 40), new Vector2(900, 26),
                             "", 18, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.55f));

            UIKit.Button(Root, "Back", new Vector2(0f, 1f), new Vector2(90, -140), new Vector2(150, 54),
                         Loc.T("common.back"), () => onBack?.Invoke(), 22);
            UIKit.Button(Root, "Rematch", new Vector2(1f, 1f), new Vector2(-90, -140), new Vector2(170, 54),
                         Loc.T("fight.rematch"), Rematch, 22);
            UIKit.Button(Root, "Dummy", new Vector2(1f, 1f), new Vector2(-270, -140), new Vector2(170, 54),
                         Loc.T("fight.dummy"), CycleDummy, 22);
            UIKit.Button(Root, "Boxes", new Vector2(1f, 1f), new Vector2(-450, -140), new Vector2(150, 54),
                         Loc.T("viewer.boxes"), () => { showBoxes = !showBoxes; Redraw(); }, 22);

            audioSource = gameObject.GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;

            SetVisible(false);
        }

        // ------------------------------------------------------------------ loading

        /// <summary>
        /// Loads the character twice (one instance per player), the stage and `fight.def`.
        /// On the device everything comes from Resources; in the editor and in the rendered
        /// fixture the same code reads from disk through a <see cref="FileSource"/>.
        /// </summary>
        public bool EnsureLoaded(IResourceSource charSource = null, IResourceSource stageSource = null,
                                 IResourceSource dataSource = null, string defFile = "kfm.def",
                                 string stageFile = "kfm.def") {
            if (Engine != null || LoadError != null) return Engine != null;
            var watch = Stopwatch.StartNew();
            try {
                var src = charSource ?? new ResourcesSource("chars/kfm");
                Character = MugenCharacter.Load(src, defFile);
                cache = new MugenAssetCache();

                States = CnsFile.Parse(src.Read(Character.CnsFile));
                Commands = CmdFile.Parse(src.Read(Character.CmdFile));
                States.Merge(Commands.States);

                if (stageSource != null) {
                    try { Stage = StageDefinition.Load(stageSource, stageFile, true); }
                    catch (Exception e) { UnityEngine.Debug.LogWarning("[IK] stage load failed: " + e.Message); }
                }
                if (dataSource != null) {
                    try {
                        var bytes = dataSource.Read("fight.def");
                        if (bytes != null) Fight = FightDef.Load(bytes, dataSource);
                    } catch (Exception e) { UnityEngine.Debug.LogWarning("[IK] fight.def load failed: " + e.Message); }
                }

                if (Stage != null && Stage.Sprites != null && bgBack != null)
                    stageRenderer = new StageRenderer(bgBack, bgFront, Stage, cache);

                // the screenpack's own HUD artwork from fight.sff; the motif is in its own
                // localcoord space (1280x720 for this one), hence scale 1 against UIKit's
                // 1280x720 reference canvas
                if (Fight != null) {
                    try {
                        var sffBytes = Fight.ReadSffBytes();
                        if (sffBytes != null) {
                            FightSprites = SffFile.Load(sffBytes, false);
                            hud = new FightHud(Root, Fight, FightSprites, cache, 1f);
                            if (!hud.Ready) {
                                UnityEngine.Debug.LogWarning("[IK] fight HUD not ready: " + hud.LoadError);
                                hud.Dispose();
                                hud = null;
                            }
                        }
                    } catch (Exception e) {
                        UnityEngine.Debug.LogWarning("[IK] fight HUD failed: " + e.Message);
                        hud = null;
                    }
                }
                // the programmer-art bars are only a fallback for a motif that fails to load
                foreach (var g in fallbackHud) if (g != null) g.enabled = hud == null;

                var p1 = new Fighter(Character, States, Commands);
                var p2 = new Fighter(Character, States, Commands);
                Engine = new FightEngine(p1, p2, Stage, Fight);
            } catch (Exception e) {
                LoadError = e.Message;
                UnityEngine.Debug.LogError("[IK] fight load failed: " + e);
            }
            watch.Stop();
            LoadMilliseconds = watch.Elapsed.TotalMilliseconds;
            return Engine != null;
        }

        public void Rematch() {
            if (Engine == null) return;
            Engine.Wins[0] = Engine.Wins[1] = 0;
            Engine.MatchOver = false;
            Engine.StartRound(1);
            Ticks = 0;
            Redraw();
        }

        public void CycleDummy() { DummyMode = (DummyMode + 1) % 4; }

        // ------------------------------------------------------------------ logic

        /// <summary>One logic tick, called by <see cref="InputRouter"/> at 60 Hz.</summary>
        public void Feed(InputFrame frame) {
            if (Engine == null) return;
            var p1 = TrainingScreen.ToCmdKey(frame, Engine.P1.Facing);
            Engine.Tick(p1, DummyInput());
            Ticks++;
            PlayPendingSounds();
            Redraw();
        }

        /// <summary>The training dummy: stand, guard, jump or walk towards player 1.</summary>
        CmdKey DummyInput() {
            switch (DummyMode) {
                case 1: return CmdKey.B;                       // hold back = guard
                case 2: return (Ticks % 40) < 3 ? CmdKey.U : CmdKey.None;
                case 3: return CmdKey.F;
                default: return CmdKey.None;
            }
        }

        void PlayPendingSounds() {
            if (audioSource == null || Character == null || Character.Snd == null) return;
            for (int i = 0; i < 2; i++) {
                var f = Engine.Players[i];
                if (f == null) continue;
                for (int s = 0; s < f.PendingSounds.Count; s++) {
                    var entry = Character.Snd.Get(f.PendingSounds[s][0], f.PendingSounds[s][1]);
                    var clip = entry != null ? cache.ClipFor(entry) : null;
                    if (clip != null) audioSource.PlayOneShot(clip);
                }
            }
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>
        /// Screen metrics for this frame: the stage is fitted by height so a 240-unit MUGEN
        /// screen fills the view, and the floor lands exactly on the stage's `zoffset`, which
        /// is what makes the backgrounds and the fighters share one coordinate system.
        /// </summary>
        void UpdateViewMetrics() {
            float h = stageRect != null ? stageRect.rect.height : 0f;
            float w = stageRect != null ? stageRect.rect.width : 0f;
            if (Stage != null && h > 10f) {
                int local = Stage.LocalCoord != null && Stage.LocalCoord[1] > 0 ? Stage.LocalCoord[1] : 240;
                Scale = Mathf.Clamp(h / local, 0.5f, 12f);
                FloorY = h - Stage.ZOffset * Scale;
            }
            ViewWidth = (w > 10f ? w : 960f) / Scale;
            ViewHeight = (h > 10f ? h : 624f) / Scale;
            if (floorLine != null) {
                floorLine.enabled = stageRenderer == null;
                floorLine.rectTransform.anchoredPosition = new Vector2(0, FloorY - 2);
            }
        }

        public void Redraw() {
            if (Engine == null) return;
            UpdateViewMetrics();
            float camera = Engine.Camera != null ? Engine.Camera.X : 0f;
            float cameraY = Engine.Camera != null ? Engine.Camera.Y : 0f;
            if (stageRenderer != null) {
                stageRenderer.Scale = Scale;
                stageRenderer.FloorY = FloorY;
                stageRenderer.Draw(camera, cameraY, ViewWidth, ViewHeight);
            }

            for (int i = 0; i < 2; i++) {
                var f = Engine.Players[i];
                var img = fighterSprites[i];
                if (f == null || img == null) continue;
                var frame = f.Anim != null ? f.Anim.CurrentFrame : null;
                var spr = Character.SpriteOf(frame);
                var rt = (RectTransform)img.transform;
                if (spr == null) { img.enabled = false; continue; }

                uint[] pal = null;
                if (!spr.Raw && spr.OwnPalette == null && Character.Sff.Palettes.Count > 0)
                    pal = Character.Sff.Palettes[Mathf.Min(i, Character.Sff.Palettes.Count - 1)];
                var unitySprite = cache.SpriteFor(Character.Sff, spr, pal);
                img.enabled = unitySprite != null;
                img.sprite = unitySprite;
                rt.sizeDelta = new Vector2(spr.Width * Scale, spr.Height * Scale);
                rt.pivot = new Vector2(spr.Width > 0 ? (float)spr.X / spr.Width : 0.5f,
                                       spr.Height > 0 ? 1f - (float)spr.Y / spr.Height : 0.5f);
                float x = (f.PosX - camera + frame.Xoffset * f.Facing) * Scale;
                float y = FloorY - (f.PosY + frame.Yoffset) * Scale;
                rt.anchoredPosition = new Vector2(x, y);
                rt.localScale = new Vector3(frame.Hscale * Mathf.Abs(frame.Xscale) * f.Facing,
                                            frame.Vscale * Mathf.Abs(frame.Yscale), 1f);
            }

            DrawBoxes(camera);
            if (hud != null) hud.Draw(Engine);
            DrawBars();
            DrawAnnouncement();
        }

        void DrawBars() {
            // when the real HUD is up these widgets are disabled; the values are still kept
            // up to date so the debug line and the fallback stay correct
            for (int i = 0; i < 2; i++) {
                var bar = Engine.Bars[i];
                var f = Engine.Players[i];
                if (bar == null || f == null) continue;
                float life = Mathf.Clamp01(bar.LifeFraction);
                float mid = Mathf.Clamp01(bar.MidLife);
                if (lifeFill[i] != null) lifeFill[i].rectTransform.localScale = new Vector3(life, 1f, 1f);
                if (lifeMid[i] != null) lifeMid[i].rectTransform.localScale = new Vector3(Mathf.Max(mid, life), 1f, 1f);
                if (powerFill[i] != null)
                    powerFill[i].rectTransform.localScale =
                        new Vector3(Mathf.Clamp01(f.Power / (float)f.PowerMax), 1f, 1f);
                if (winLabels[i] != null)
                    UIKit.SetText(winLabels[i], new string('\u25cf', Mathf.Clamp(Engine.Wins[i], 0, 5)));
            }
            if (timer != null)
                UIKit.SetText(timer, Engine.TimeLeft >= 0 ? Engine.TimeLeft.ToString() : "--");
            if (debugLine != null)
                UIKit.SetText(debugLine, string.Format(
                    "P1 {0}/{1} st{2} | P2 {3}/{4} st{5} | round {6} ({7}) | hits {8}",
                    Engine.P1.Life, Engine.P1.LifeMax, Engine.P1.StateNo,
                    Engine.P2.Life, Engine.P2.LifeMax, Engine.P2.StateNo,
                    Engine.RoundNo, Engine.State, Engine.P1.HitCount));
        }

        void DrawAnnouncement() {
            if (announce == null) return;
            string text = "";
            switch (Engine.State) {
                case RoundState.Announce:
                    text = string.Format(Loc.T("fight.round"), Engine.RoundNo);
                    if (Engine.StateTime > Engine.AnnounceTime / 2) text = Loc.T("fight.fight");
                    break;
                case RoundState.Over:
                    text = Loc.T("fight.ko");
                    break;
                case RoundState.WinPose:
                    text = Engine.RoundWinner == 3 ? Loc.T("fight.draw")
                         : string.Format(Loc.T("fight.wins"), Engine.RoundWinner);
                    break;
            }
            UIKit.SetText(announce, text);
        }

        void DrawBoxes(float camera) {
            int used = 0;
            if (showBoxes) {
                for (int i = 0; i < 2; i++) {
                    var f = Engine.Players[i];
                    if (f == null) continue;
                    used = DrawBoxList(f.WorldClsn(2), new Color(0.3f, 0.6f, 1f, 0.35f), camera, used);
                    used = DrawBoxList(f.WorldClsn(1), new Color(1f, 0.3f, 0.3f, 0.45f), camera, used);
                }
            }
            for (int i = used; i < clsnPool.Count; i++) clsnPool[i].gameObject.SetActive(false);
        }

        int DrawBoxList(List<float[]> boxes, Color color, float camera, int used) {
            for (int b = 0; b < boxes.Count; b++) {
                var box = boxes[b];
                Image img;
                if (used < clsnPool.Count) img = clsnPool[used];
                else {
                    img = UIKit.Image(clsnLayer, "box" + used, new Vector2(0.5f, 0f), Vector2.zero,
                                      new Vector2(10, 10), null, color);
                    clsnPool.Add(img);
                }
                img.gameObject.SetActive(true);
                img.color = color;
                var rt = img.rectTransform;
                float left = (box[0] - camera) * Scale, right = (box[2] - camera) * Scale;
                float top = FloorY - box[1] * Scale, bottom = FloorY - box[3] * Scale;
                rt.sizeDelta = new Vector2(Mathf.Abs(right - left), Mathf.Abs(top - bottom));
                rt.anchoredPosition = new Vector2((left + right) / 2f, (top + bottom) / 2f);
                used++;
            }
            return used;
        }

        public void SetVisible(bool on) {
            if (Root == null) return;
            Root.gameObject.SetActive(on);
            if (hud != null) hud.SetVisible(on);
            if (on) {
                // on the device everything is in Resources; the stage and fight.def are
                // optional, the fight runs without them (plain floor, engine-drawn bars)
                EnsureLoaded(null,
                             new ResourcesSource("stages"),
                             new ResourcesSource("data"));
                Redraw();
            }
        }

        public bool Visible => Root != null && Root.gameObject.activeSelf;

        void OnDestroy() {
            if (hud != null) hud.Dispose();
            if (stageRenderer != null) stageRenderer.Dispose();
            if (cache != null) cache.Dispose();
        }

        /// <summary>Hook for the rendered fixture.</summary>
        public void ShowBoxesForTests(bool on) { showBoxes = on; Redraw(); }
    }
}
