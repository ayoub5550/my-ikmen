using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using IK.Core;

namespace IK.UI {
    /// <summary>
    /// dev.2 gate screen: loads a real MUGEN character out of Resources with the loaders in
    /// <see cref="IK.Core"/> and plays its actions at 60 Hz, exactly as the engine times them.
    /// Everything on screen is proof that a loader works — the sprite (SFF), the element
    /// timing and flip flags (AIR), the collision boxes (AIR Clsn), the palette list (SFF
    /// palettes) and the sound button (SND).
    /// </summary>
    public class CharViewer : MonoBehaviour {
        public RectTransform Root { get; private set; }
        public Action onBack;

        public MugenCharacter Character { get; private set; }
        public string LoadError { get; private set; }
        public double LoadMilliseconds { get; private set; }

        MugenAssetCache cache;
        AudioSource audioSource;
        readonly List<int> actions = new List<int>();
        int actionIndex;
        MugenAnimation anim;
        int paletteIndex;

        Image sprite;
        RectTransform stage, clsnLayer;
        Text title, info, hint;
        Button playButton;
        readonly List<Image> clsnPool = new List<Image>();

        bool playing = true;
        bool showBoxes;
        float speed = 1f;
        float accumulator;

        public const float Tick = 1f / 60f;
        /// <summary>Pixels per MUGEN unit; the character is drawn in its localcoord space.</summary>
        public float Scale { get; private set; } = 3f;   // 240 localcoord units = the 720-unit canvas

        // ------------------------------------------------------------------ build

        public void Build(RectTransform parent) {
            Root = UIKit.Panel(parent, "CharViewer", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                               new Color(0.07f, 0.08f, 0.11f, 1f));

            stage = UIKit.Panel(Root, "stage", new Vector2(0f, 0f), new Vector2(1f, 1f),
                                new Vector2(40, 90), new Vector2(-40, -120));
            var floor = UIKit.Panel(stage, "floor", new Vector2(0f, 0f), new Vector2(1f, 0f),
                                    new Vector2(0, 70), new Vector2(0, 74), new Color(1f, 1f, 1f, 0.18f));
            floor.gameObject.name = "floor";

            var spriteGo = new GameObject("sprite", typeof(RectTransform));
            spriteGo.transform.SetParent(stage, false);
            sprite = spriteGo.AddComponent<Image>();
            sprite.raycastTarget = false;
            sprite.preserveAspect = true;
            var srt = (RectTransform)spriteGo.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0f);
            srt.pivot = new Vector2(0.5f, 0.5f);

            clsnLayer = UIKit.Panel(stage, "clsn", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            title = UIKit.Text(Root, "title", new Vector2(0.5f, 1f), new Vector2(0, -44), new Vector2(1100, 44),
                               "", 30, TextAnchor.MiddleCenter, Skin.Accent);
            info = UIKit.Text(Root, "info", new Vector2(0f, 1f), new Vector2(330, -86), new Vector2(640, 32),
                              "", 20, TextAnchor.MiddleLeft, Skin.Text);
            hint = UIKit.Text(Root, "hint", new Vector2(0.5f, 0f), new Vector2(0, 26), new Vector2(1180, 30),
                              Loc.T("viewer.hint"), 20, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.65f));

            UIKit.Button(Root, "Back", new Vector2(0f, 1f), new Vector2(90, -44), new Vector2(150, 62),
                         Loc.T("common.back"), () => onBack?.Invoke(), 24);
            UIKit.Button(Root, "Prev", new Vector2(0f, 0f), new Vector2(100, 82), new Vector2(170, 70),
                         Loc.T("viewer.prev"), () => StepAction(-1), 24);
            playButton = UIKit.Button(Root, "Play", new Vector2(0f, 0f), new Vector2(285, 82), new Vector2(160, 70),
                                      Loc.T("viewer.pause"), TogglePlay, 24);
            UIKit.Button(Root, "Next", new Vector2(0f, 0f), new Vector2(465, 82), new Vector2(170, 70),
                         Loc.T("viewer.next"), () => StepAction(1), 24);
            UIKit.Button(Root, "Speed", new Vector2(1f, 0f), new Vector2(-465, 82), new Vector2(170, 70),
                         "×1", CycleSpeed, 24);
            UIKit.Button(Root, "Palette", new Vector2(1f, 0f), new Vector2(-285, 82), new Vector2(170, 70),
                         Loc.T("viewer.palette"), CyclePalette, 24);
            UIKit.Button(Root, "Boxes", new Vector2(1f, 0f), new Vector2(-105, 82), new Vector2(170, 70),
                         Loc.T("viewer.boxes"), ToggleBoxes, 24);
            UIKit.Button(Root, "Sound", new Vector2(1f, 1f), new Vector2(-90, -44), new Vector2(150, 62),
                         Loc.T("viewer.sound"), PlaySound, 24);

            audioSource = gameObject.GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;

            SetVisible(false);
        }

        /// <summary>Loads the character once, the first time the screen is shown.</summary>
        public bool EnsureLoaded(IResourceSource source = null, string defFile = "kfm.def") {
            if (Character != null || LoadError != null) return Character != null;
            var watch = Stopwatch.StartNew();
            try {
                Character = MugenCharacter.Load(source ?? new ResourcesSource("chars/kfm"), defFile);
                cache = new MugenAssetCache();
                actions.Clear();
                actions.AddRange(Character.PlayableActions());
                actionIndex = 0;
                SelectAction(0);
            } catch (Exception e) {
                LoadError = e.Message;
                UnityEngine.Debug.LogError("[IK] character load failed: " + e);
            }
            watch.Stop();
            LoadMilliseconds = watch.Elapsed.TotalMilliseconds;
            RefreshTitle();
            return Character != null;
        }

        void RefreshTitle() {
            if (title == null) return;
            if (Character == null) {
                UIKit.SetText(title, Loc.T("viewer.failed") + (LoadError != null ? ": " + LoadError : ""));
                return;
            }
            int sounds = Character.Snd != null ? Character.Snd.Entries.Count : 0;
            UIKit.SetText(title, string.Format("{0} — {1}", Character.DisplayName, Character.Author));
            UIKit.SetText(hint, string.Format(Loc.T("viewer.stats"),
                                              Character.Sff.Sprites.Count, Character.Sff.Palettes.Count,
                                              Character.Air != null ? Character.Air.Order.Count : 0,
                                              sounds, Mathf.RoundToInt((float)LoadMilliseconds)));
        }

        // ------------------------------------------------------------------ playback

        public void SelectAction(int index) {
            if (actions.Count == 0) return;
            actionIndex = ((index % actions.Count) + actions.Count) % actions.Count;
            anim = Character.Air.Get(actions[actionIndex]);
            anim.Reset();
            accumulator = 0f;
            Redraw();
        }

        public void StepAction(int delta) => SelectAction(actionIndex + delta);

        /// <summary>Action number currently on screen (-1 when nothing is loaded).</summary>
        public int CurrentAction => anim != null ? anim.No : -1;

        /// <summary>Jumps to an action by its .air number; used by the UI fixture.</summary>
        public bool SelectActionNumber(int no) {
            int i = actions.IndexOf(no);
            if (i < 0) return false;
            SelectAction(i);
            return true;
        }

        // Hooks for the rendered UI fixture (IKUIRegressionRunner) — they do exactly what
        // the on-screen buttons do, without faking pointer events on every control.
        public void CyclePaletteForTests() => CyclePalette();
        public void ResetPaletteForTests() { paletteIndex = 0; Redraw(); }
        public void ShowBoxesForTests(bool on) { showBoxes = on; Redraw(); }
        public void SetPlayingForTests(bool on) { playing = on; }

        void TogglePlay() {
            playing = !playing;
            UIKit.SetText(playButton.GetComponentInChildren<Text>(), Loc.T(playing ? "viewer.pause" : "viewer.play"));
            if (!playing) Redraw();
        }

        void CycleSpeed() {
            speed = speed >= 2f ? 0.25f : (speed >= 1f ? 2f : (speed >= 0.5f ? 1f : 0.5f));
            var b = Root.Find("Speed");
            if (b != null) UIKit.SetText(b.GetComponentInChildren<Text>(), "×" + speed.ToString("0.##"));
        }

        void CyclePalette() {
            if (Character == null || Character.Sff.Palettes.Count == 0) return;
            paletteIndex = (paletteIndex + 1) % Character.Sff.Palettes.Count;
            Redraw();
        }

        void ToggleBoxes() {
            showBoxes = !showBoxes;
            Redraw();
        }

        void PlaySound() {
            if (Character == null || Character.Snd == null || Character.Snd.Entries.Count == 0) return;
            var entry = Character.Snd.Entries[UnityEngine.Random.Range(0, Character.Snd.Entries.Count)];
            var clip = cache.ClipFor(entry);
            if (clip != null) audioSource.PlayOneShot(clip, Settings.SettingsStore.Current.sfxVolume / 100f);
        }

        void Update() {
            if (Root == null || !Root.gameObject.activeSelf || anim == null) return;
            if (playing) {
                accumulator += Time.unscaledDeltaTime * speed;
                int guard = 0;
                while (accumulator >= Tick && guard++ < 8) {
                    accumulator -= Tick;
                    anim.Tick();
                }
            }
            Redraw();
        }

        /// <summary>Draws the current element: sprite, axis offsets, flip flags and Clsn boxes.</summary>
        public void Redraw() {
            if (Character == null || anim == null || sprite == null) return;
            var frame = anim.CurrentFrame;
            var spr = Character.SpriteOf(frame);
            var rt = (RectTransform)sprite.transform;

            if (spr == null) {
                sprite.enabled = false;
            } else {
                uint[] pal = null;
                if (!spr.Raw && spr.OwnPalette == null && Character.Sff.Palettes.Count > 0)
                    pal = Character.Sff.Palettes[Mathf.Clamp(paletteIndex, 0, Character.Sff.Palettes.Count - 1)];
                var unitySprite = cache.SpriteFor(Character.Sff, spr, pal);
                sprite.enabled = unitySprite != null;
                sprite.sprite = unitySprite;
                rt.sizeDelta = new Vector2(spr.Width * Scale, spr.Height * Scale);
                // pivot sits on the MUGEN axis, so the element offsets move the whole sprite
                rt.pivot = new Vector2(spr.Width > 0 ? (float)spr.X / spr.Width : 0.5f,
                                       spr.Height > 0 ? 1f - (float)spr.Y / spr.Height : 0.5f);
                rt.anchoredPosition = new Vector2(frame.Xoffset * Scale, 74f - frame.Yoffset * Scale);
                rt.localScale = new Vector3(frame.Hscale * Mathf.Abs(frame.Xscale),
                                            frame.Vscale * Mathf.Abs(frame.Yscale), 1f);
            }

            DrawBoxes(frame);

            string sprLabel = spr != null ? $"{frame.Group},{frame.Number} {spr.Width}×{spr.Height}"
                                          : $"{frame.Group},{frame.Number} —";
            UIKit.SetText(info, string.Format(Loc.T("viewer.info"),
                                              anim.No, anim.CurrentElement + 1, anim.Frames.Count,
                                              sprLabel, frame.Time, frame.Clsn1.Count, frame.Clsn2.Count));
        }

        void DrawBoxes(AnimFrame frame) {
            int used = 0;
            if (showBoxes && frame != null) {
                used += DrawBoxSet(frame.Clsn2, new Color(0.35f, 0.65f, 1f, 0.22f), used);
                used += DrawBoxSet(frame.Clsn1, new Color(1f, 0.35f, 0.3f, 0.3f), used);
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
                rt.anchoredPosition = new Vector2(b[0] * Scale, 74f - b[1] * Scale);
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

        void OnDestroy() {
            cache?.Dispose();
        }
    }
}
