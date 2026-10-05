using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IK.Settings;

namespace IK.UI {
    /// <summary>
    /// The settings screen: a page strip (Controls / Game / Audio / Video / Language /
    /// About) and big touch rows (≥96 reference units). Every change is applied live and
    /// persisted immediately (debounced by <see cref="SettingsStore"/>).
    /// </summary>
    public class SettingsMenu : MonoBehaviour {
        public RectTransform Root { get; private set; }
        public string CurrentPage { get; private set; } = "Controls";
        public Action onBack;
        public Action onEditLayout;
        public Action onDeveloper;            // dev.5: the dev.1-dev.4 screens (input test, viewer, old training)
        public Action onChanged;              // so the touch layer can rebuild live

        static readonly string[] Pages = { "Controls", "Game", "Audio", "Video", "Language", "About" };
        readonly Dictionary<string, RectTransform> pageRoots = new Dictionary<string, RectTransform>();
        readonly Dictionary<string, Button> pageButtons = new Dictionary<string, Button>();
        readonly Dictionary<string, ScrollRect> pageScrolls = new Dictionary<string, ScrollRect>();
        RectTransform content;
        GameSettings S => SettingsStore.Current;
        float rowY;

        public void Build(RectTransform parent) {
            Root = UIKit.Panel(parent, "Settings", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                               new Color(0.04f, 0.05f, 0.08f, 1f));   // dev.8: opaque (the screen behind showed through)
            UIKit.Text(Root, "title", new Vector2(0.5f, 1f), new Vector2(0, -44), new Vector2(600, 56),
                       Loc.T("menu.settings"), 36, TextAnchor.MiddleCenter, Skin.Highlight);

            // page strip
            float x = -((Pages.Length - 1) * 0.5f) * 186f;
            foreach (var page in Pages) {
                string p = page;
                var b = UIKit.Button(Root, "Page" + p, new Vector2(0.5f, 1f), new Vector2(x, -112), new Vector2(176, 62),
                                     Loc.T("page." + p.ToLowerInvariant()), () => ShowPage(p), 22);
                pageButtons[p] = b;
                x += 186f;
            }

            UIKit.Button(Root, "Back", new Vector2(0f, 1f), new Vector2(110, -44), new Vector2(180, 64),
                         Loc.T("common.back"), () => onBack?.Invoke(), 26);
            UIKit.Button(Root, "Developer", new Vector2(1f, 1f), new Vector2(-110, -44), new Vector2(180, 64),
                         Loc.T("fe.developer"), () => onDeveloper?.Invoke(), 24);

            content = UIKit.Panel(Root, "Content", new Vector2(0.08f, 0.04f), new Vector2(0.92f, 0.80f),
                                  Vector2.zero, Vector2.zero);

            // dev.8: every page scrolls (rows ran off the bottom of the screen on Controls / Game / Video)
            var rows = new Dictionary<string, RectTransform>();
            foreach (var page in Pages) {
                var pr = UIKit.Panel(content, page, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                pr.gameObject.AddComponent<RectMask2D>();
                var hit = pr.gameObject.AddComponent<Image>();          // drag anywhere on the page
                hit.color = new Color(0, 0, 0, 0);
                var inner = UIKit.Panel(pr, "Rows", new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
                inner.pivot = new Vector2(0.5f, 1f);
                var sr = pr.gameObject.AddComponent<ScrollRect>();
                sr.content = inner; sr.viewport = pr;
                sr.horizontal = false; sr.vertical = true;
                sr.movementType = ScrollRect.MovementType.Clamped;
                sr.inertia = true; sr.decelerationRate = 0.12f;
                sr.scrollSensitivity = 40f;
                pageRoots[page] = pr;
                pageScrolls[page] = sr;
                rows[page] = inner;
                pr.gameObject.SetActive(false);
            }
            BuildPage(rows["Controls"], BuildControls);
            BuildPage(rows["Game"], BuildGame);
            BuildPage(rows["Audio"], BuildAudio);
            BuildPage(rows["Video"], BuildVideo);
            BuildPage(rows["Language"], BuildLanguage);
            BuildPage(rows["About"], BuildAbout);
            ShowPage("Controls");
        }

        void BuildPage(RectTransform rows, Action<RectTransform> build) {
            build(rows);
            rows.sizeDelta = new Vector2(0f, Mathf.Max(0f, -rowY + 16f));
        }

        /// <summary>Scrolls the current page so that its row <paramref name="index"/> (0-based) is visible.</summary>
        public void ScrollToRow(int index) {
            if (!pageScrolls.TryGetValue(CurrentPage, out var sr) || sr.content == null) return;
            float top = 58f - RowHeight * 0.5f + index * (RowHeight + 8f) - 8f;
            float view = sr.viewport.rect.height, h = sr.content.rect.height;
            if (h <= view) return;
            var p = sr.content.anchoredPosition;
            p.y = Mathf.Clamp(top, 0f, h - view);
            sr.content.anchoredPosition = p;
        }

        /// <summary>Scroll offset (reference units) of the current page.</summary>
        public float ScrollY => pageScrolls.TryGetValue(CurrentPage, out var sr) && sr.content != null ? sr.content.anchoredPosition.y : 0f;

        /// <summary>Height of the current page's rows and of its visible window (rendered checks).</summary>
        public Vector2 PageExtent => pageScrolls.TryGetValue(CurrentPage, out var sr) && sr.content != null
            ? new Vector2(sr.content.rect.height, sr.viewport.rect.height) : Vector2.zero;

        public void ShowPage(string page) {
            CurrentPage = page;
            if (pageScrolls.TryGetValue(page, out var srp) && srp.content != null) srp.content.anchoredPosition = Vector2.zero;
            foreach (var kv in pageRoots) kv.Value.gameObject.SetActive(kv.Key == page);
            foreach (var kv in pageButtons) {
                var img = kv.Value.GetComponent<Image>();
                img.color = kv.Key == page ? new Color(1f, 0.88f, 0.7f) : Color.white;
            }
        }

        public void SetVisible(bool v) { if (Root != null) Root.gameObject.SetActive(v); }
        public bool Visible => Root != null && Root.gameObject.activeSelf;

        // ---------- row helpers ----------
        const float RowHeight = 96f;
        /// <summary>Anchor / position of a row control: right side in English, left side in Arabic.</summary>
        static Vector2 CA => Loc.Arabic ? new Vector2(0f, 0.5f) : new Vector2(1f, 0.5f);
        // dev.8: the control group spans x = -602..-10 from the right edge (prev/minus -560,
        // value/slider -355, next/plus -150, slider value -55); Arabic shifts the same group
        // to the left edge (+612) so it never overlaps the right-aligned label (-470..-30).
        static Vector2 CP(float x, float y) => new Vector2(Loc.Arabic ? x + 612f : x, y);

        void StartRows() { rowY = -58f; }

        RectTransform Row(RectTransform page, string label) {
            var row = UIKit.Panel(page, "row:" + label, new Vector2(0, 1), new Vector2(1, 1),
                                  new Vector2(0, rowY - RowHeight * 0.5f), new Vector2(0, rowY + RowHeight * 0.5f),
                                  new Color(1, 1, 1, 0.05f));
            // dev.8: right-to-left rows in Arabic (label on the right, controls on the left)
            if (Loc.Arabic)
                UIKit.Text(row, "label", new Vector2(1f, 0.5f), new Vector2(-250, 0), new Vector2(440, 48),
                           label, 26, TextAnchor.MiddleRight);
            else
                UIKit.Text(row, "label", new Vector2(0f, 0.5f), new Vector2(250, 0), new Vector2(440, 48),
                           label, 26, TextAnchor.MiddleLeft);
            rowY -= RowHeight + 8f;
            return row;
        }

        void Choice(RectTransform page, string label, string[] options, Func<int> get, Action<int> set) {
            var row = Row(page, label);
            var value = UIKit.Text(row, "value", CA, CP(-355, 0), new Vector2(260, 48),
                                   options[Mathf.Clamp(get(), 0, options.Length - 1)], 26);
            UIKit.Button(row, "prev", CA, CP(-560, 0), new Vector2(84, 68), "<", () => {
                int v = (get() - 1 + options.Length) % options.Length;
                set(v); UIKit.SetText(value, options[v]); Changed();
            }, 28);
            UIKit.Button(row, "next", CA, CP(-150, 0), new Vector2(84, 68), ">", () => {
                int v = (get() + 1) % options.Length;
                set(v); UIKit.SetText(value, options[v]); Changed();
            }, 28);
        }

        void Switch(RectTransform page, string label, Func<bool> get, Action<bool> set) {
            Choice(page, label, new[] { Loc.T("common.off"), Loc.T("common.on") },
                   () => get() ? 1 : 0, v => set(v == 1));
        }

        void SliderRow(RectTransform page, string label, float min, float max, Func<float> get, Action<float> set,
                       Func<float, string> format, float step) {
            var row = Row(page, label);
            var value = UIKit.Text(row, "value", CA, CP(-55, 0), new Vector2(96, 48),
                                   format(get()), 26);
            var slider = UIKit.Slider(row, "slider", CA, CP(-355, 0), new Vector2(290, 36),
                                      min, max, get(), v => { set(v); UIKit.SetText(value, format(get())); Changed(); });
            UIKit.Button(row, "minus", CA, CP(-560, 0), new Vector2(84, 68), "-", () => {
                slider.value = Mathf.Clamp(slider.value - step, min, max);
            }, 28);
            UIKit.Button(row, "plus", CA, CP(-150, 0), new Vector2(84, 68), "+", () => {
                slider.value = Mathf.Clamp(slider.value + step, min, max);
            }, 28);
        }

        void ResetRow(RectTransform page, Action reset) {
            var row = Row(page, "");
            UIKit.Button(row, "reset", CA, CP(-150, 0), new Vector2(280, 68),
                         Loc.T("common.reset"), () => { reset(); Rebuild(); Changed(); }, 24);
        }

        void Changed() {
            SettingsStore.MarkChanged();
            SettingsStore.Flush();          // settings must survive an immediate kill
            onChanged?.Invoke();
        }

        /// <summary>Rebuilds the pages in place (after a reset or a language change).</summary>
        public void Rebuild() {
            var parent = (RectTransform)Root.parent;
            string page = CurrentPage;
            DestroyImmediate(Root.gameObject);
            Build(parent);
            ShowPage(page);
        }

        // ---------- pages ----------
        void BuildControls(RectTransform page) {
            StartRows();
            Choice(page, Loc.T("ctl.style"), new[] { Loc.T("ctl.styleModern"), Loc.T("ctl.styleClassic") },
                   () => S.buttonStyle, v => S.buttonStyle = v);
            Choice(page, Loc.T("ctl.directionMode"),
                   new[] { Loc.T("ctl.dpad"), Loc.T("ctl.floating"), Loc.T("ctl.fixed") },
                   () => (int)S.directionMode, v => S.directionMode = (DirectionMode)v);
            SliderRow(page, Loc.T("ctl.buttonSize"), 0.6f, 1.6f, () => S.buttonSize, v => S.buttonSize = v,
                      v => Mathf.RoundToInt(v * 100) + " %", 0.05f);
            SliderRow(page, Loc.T("ctl.opacity"), 0.2f, 1f, () => S.controlsOpacity, v => S.controlsOpacity = v,
                      v => Mathf.RoundToInt(v * 100) + " %", 0.05f);
            Switch(page, Loc.T("ctl.slide"), () => S.slideToPress, v => S.slideToPress = v);
            Switch(page, Loc.T("ctl.macro"), () => S.macroButtons, v => S.macroButtons = v);
            Switch(page, Loc.T("ctl.showDW"), () => S.showDW, v => S.showDW = v);
            Choice(page, Loc.T("ctl.haptics"), new[] { Loc.T("common.off"), Loc.T("ctl.light"), Loc.T("ctl.strong") },
                   () => (int)S.haptics, v => S.haptics = (Haptics)v);
            Choice(page, Loc.T("ctl.onScreen"), new[] { Loc.T("ctl.auto"), Loc.T("ctl.always"), Loc.T("ctl.never") },
                   () => (int)S.onScreenControls, v => S.onScreenControls = (OnScreenControls)v);
            Switch(page, Loc.T("ctl.buttonAssist"), () => S.buttonAssist, v => S.buttonAssist = v);
            Choice(page, Loc.T("ctl.socd"), new[] { "0", "1", "2", "3", "4" },
                   () => S.socdResolution, v => S.socdResolution = v);
            SliderRow(page, Loc.T("ctl.sensitivity"), 0.1f, 1f, () => S.stickSensitivity, v => S.stickSensitivity = v,
                      v => v.ToString("0.00"), 0.05f);
            SliderRow(page, Loc.T("ctl.deadzone"), 0.05f, 0.6f, () => S.stickDeadZone, v => S.stickDeadZone = v,
                      v => v.ToString("0.00"), 0.05f);
            Choice(page, Loc.T("ctl.preset"), ControlLayout.PresetNames,
                   () => Mathf.Max(0, Array.IndexOf(ControlLayout.PresetNames, S.layoutPreset)),
                   v => { S.layoutPreset = ControlLayout.PresetNames[v]; S.ResetLayout(); });
            Choice(page, Loc.T("ctl.slot"), new[] { "1", "2", "3" }, () => S.layoutSlot, v => S.layoutSlot = v);
            var row = Row(page, "");
            UIKit.Button(row, "editLayout", CA, CP(-150, 0), new Vector2(280, 68),
                         Loc.T("ctl.editLayout"), () => onEditLayout?.Invoke(), 24);
            ResetRow(page, () => {
                var d = new GameSettings();
                S.buttonStyle = d.buttonStyle;
                S.directionMode = d.directionMode; S.buttonSize = d.buttonSize; S.controlsOpacity = d.controlsOpacity;
                S.slideToPress = d.slideToPress; S.macroButtons = d.macroButtons; S.showDW = d.showDW;
                S.haptics = d.haptics; S.onScreenControls = d.onScreenControls; S.buttonAssist = d.buttonAssist;
                S.socdResolution = d.socdResolution; S.stickSensitivity = d.stickSensitivity;
                S.stickDeadZone = d.stickDeadZone; S.layoutPreset = d.layoutPreset; S.ResetLayout();
            });
        }

        void BuildGame(RectTransform page) {
            StartRows();
            SliderRow(page, Loc.T("game.difficulty"), 1, 8, () => S.difficulty, v => S.difficulty = Mathf.RoundToInt(v),
                      v => Mathf.RoundToInt(v).ToString(), 1);
            SliderRow(page, Loc.T("game.life"), 10, 300, () => S.life, v => S.life = Mathf.RoundToInt(v),
                      v => Mathf.RoundToInt(v) + " %", 10);
            Choice(page, Loc.T("game.time"), new[] { "99", "60", "30", "∞" },
                   () => S.roundTime == 99 ? 0 : S.roundTime == 60 ? 1 : S.roundTime == 30 ? 2 : 3,
                   v => S.roundTime = v == 0 ? 99 : v == 1 ? 60 : v == 2 ? 30 : -1);
            SliderRow(page, Loc.T("game.wins"), 1, 5, () => S.roundsToWin, v => S.roundsToWin = Mathf.RoundToInt(v),
                      v => Mathf.RoundToInt(v).ToString(), 1);
            SliderRow(page, Loc.T("game.speed"), -9, 9, () => S.gameSpeed, v => S.gameSpeed = Mathf.RoundToInt(v),
                      v => Mathf.RoundToInt(v).ToString(), 1);
            Switch(page, Loc.T("game.autoGuard"), () => S.autoGuard, v => S.autoGuard = v);
            ResetRow(page, () => {
                var d = new GameSettings();
                S.difficulty = d.difficulty; S.life = d.life; S.roundTime = d.roundTime;
                S.roundsToWin = d.roundsToWin; S.gameSpeed = d.gameSpeed; S.autoGuard = d.autoGuard;
            });
        }

        void BuildAudio(RectTransform page) {
            StartRows();
            SliderRow(page, Loc.T("audio.master"), 0, 100, () => S.masterVolume, v => { S.masterVolume = Mathf.RoundToInt(v); AudioListener.volume = S.masterVolume / 100f; },
                      v => Mathf.RoundToInt(v) + " %", 5);
            SliderRow(page, Loc.T("audio.bgm"), 0, 100, () => S.bgmVolume, v => S.bgmVolume = Mathf.RoundToInt(v),
                      v => Mathf.RoundToInt(v) + " %", 5);
            SliderRow(page, Loc.T("audio.sfx"), 0, 100, () => S.sfxVolume, v => S.sfxVolume = Mathf.RoundToInt(v),
                      v => Mathf.RoundToInt(v) + " %", 5);
            ResetRow(page, () => {
                var d = new GameSettings();
                S.masterVolume = d.masterVolume; S.bgmVolume = d.bgmVolume; S.sfxVolume = d.sfxVolume;
            });
        }

        void BuildVideo(RectTransform page) {
            StartRows();
            Choice(page, Loc.T("video.fps"), new[] { "30", "60" }, () => S.fpsCap == 30 ? 0 : 1,
                   v => { S.fpsCap = v == 0 ? 30 : 60; Application.targetFrameRate = S.fpsCap; });
            SliderRow(page, Loc.T("video.renderScale"), 50, 100, () => S.renderScale, v => S.renderScale = Mathf.RoundToInt(v),
                      v => Mathf.RoundToInt(v) + " %", 10);
            Choice(page, Loc.T("video.filter"), new[] { Loc.T("video.sharp"), Loc.T("video.smooth"), Loc.T("video.crisp") },
                   () => (int)S.pixelFilter, v => S.pixelFilter = (PixelFilter)v);
            Switch(page, Loc.T("video.showFps"), () => S.showFps, v => S.showFps = v);
            var bench = Row(page, Loc.T("video.benchmark"));
            UIKit.Button(bench, "benchmark", CA, CP(-150, 0), new Vector2(280, 68),
                         Loc.T("video.run"), () => IK.App.IKApp.Instance?.RunBenchmark(), 24);
            ResetRow(page, () => {
                var d = new GameSettings();
                S.fpsCap = d.fpsCap; S.renderScale = d.renderScale; S.pixelFilter = d.pixelFilter; S.showFps = d.showFps;
            });
        }

        void BuildLanguage(RectTransform page) {
            StartRows();
            Choice(page, Loc.T("page.language"),
                   new[] { Loc.T("lang.system"), Loc.T("lang.arabic"), Loc.T("lang.english") },
                   () => (int)S.language,
                   v => { S.language = (Language)v; Loc.Apply(S.language); });
            var row = Row(page, "");
            UIKit.Button(row, "apply", CA, CP(-150, 0), new Vector2(280, 68),
                         Loc.T("common.save"), () => { Changed(); Rebuild(); }, 24);
        }

        void BuildAbout(RectTransform page) {
            StartRows();
            UIKit.Text(page, "version", new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(760, 44),
                       "my-ikmen " + Application.version, 26);
            UIKit.Text(page, "credits", new Vector2(0.5f, 1f), new Vector2(0, -210), new Vector2(980, 220),
                       Loc.Arabic
                         ? "محرّك Ikemen GO (MIT) · رسوم الـ screenpack برخصة CC BY 3.0\nالفنانون: Ohmga Shironeko, SuperFromND, President Devon,\nRurouni, Shiyo Kakuge, Cylia Margatroid, Miguel Young\nخط Amiri برخصة OFL"
                         : "Ikemen GO engine (MIT) · Screenpack art CC BY 3.0\nOhmga Shironeko, SuperFromND, President Devon,\nRurouni, Shiyo Kakuge, Cylia Margatroid, Miguel Young\nAmiri font (OFL)",
                       22, TextAnchor.UpperCenter);
        }
    }
}
