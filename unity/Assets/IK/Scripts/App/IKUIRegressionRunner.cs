#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using IK.App;
using IK.Core;
using IK.Input;
using IK.Settings;
using IK.UI;

namespace IK.EditorTools {
    /// <summary>
    /// Rendered UI + synthetic pointer/layout fixture (docs/TOUCH_AND_SETTINGS.md §6,
    /// cases 8-11). This is NOT physical-device touch QA: it drives uGUI with synthetic
    /// <see cref="PointerEventData"/>, renders real frames through llvmpipe and compares
    /// pixels. Started by <c>IK.EditorTools.IKUIRegression.Run</c> with IK_UI_FIXTURE=1.
    /// </summary>
    public class IKUIRegressionRunner : MonoBehaviour {
        [Serializable]
        class Report {
            public string kind = "Rendered editor UI; synthetic pointer/layout tests. NOT physical-device touch QA.";
            public string screen;
            public List<string> passed = new List<string>();
            public List<string> errors = new List<string>();
        }

        readonly Report report = new Report();
        static string Dir => Environment.GetEnvironmentVariable("IK_UI_DIR") ?? "Builds/validation/dev1/";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            if (Environment.GetEnvironmentVariable("IK_UI_FIXTURE") != "1") return;
            Environment.SetEnvironmentVariable("IK_UI_FIXTURE", null);
            var go = new GameObject("IKUIRegression");
            DontDestroyOnLoad(go);
            go.AddComponent<IKUIRegressionRunner>();
        }

        void Check(bool value, string label) {
            if (value) report.passed.Add(label); else report.errors.Add(label);
            Debug.Log($"[IK UI] {(value ? "PASS" : "FAIL")}: {label}");
        }

        void CaptureError(string text, string stack, LogType type) {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                report.errors.Add("log: " + text);
        }

        IEnumerator Start() {
            Directory.CreateDirectory(Dir);
            report.screen = Screen.width + "x" + Screen.height;
            Application.logMessageReceived += CaptureError;
            // isolate the fixture from a developer's real settings file
            SettingsStore.UseFile(Path.Combine(Path.GetTempPath(), "ik-ui-fixture.json"));
            SettingsStore.Reset();
            yield return null; yield return null;

            var app = IKApp.Instance;
            Check(app != null, "App boots and builds its canvases");
            if (app == null) { Finish(); yield break; }

            // ---- 8. menu -> settings -> every page -> back ----
            Check(app.Menu.Visible, "Main menu visible at start");
            var options = app.Menu.Root.Find("Options").GetComponent<Button>();
            options.onClick.Invoke();
            yield return null;
            Check(app.Settings.Visible && !app.Menu.Visible, "Settings button opens the settings screen");

            foreach (var page in new[] { "Controls", "Game", "Audio", "Video", "Language", "About" }) {
                app.Settings.Root.Find("Page" + page).GetComponent<Button>().onClick.Invoke();
                yield return null;
                Check(app.Settings.CurrentPage == page, "Settings page opens: " + page);
            }
            app.Settings.ShowPage("Controls");
            yield return null;

            // change a setting through the UI and prove it survives a rebuild (scene reload equivalent)
            var opacityRow = FindRow(app.Settings.Root, Loc.T("ctl.opacity"));
            Check(opacityRow != null, "Opacity row exists");
            if (opacityRow != null) {
                var slider = opacityRow.Find("slider").GetComponent<Slider>();
                slider.value = 0.9f;
                yield return null;
                Check(Mathf.Abs(SettingsStore.Current.controlsOpacity - 0.9f) < 0.01f, "Slider writes the setting");
            }
            var sizeRow = FindRow(app.Settings.Root, Loc.T("ctl.buttonSize"));
            if (sizeRow != null) sizeRow.Find("plus").GetComponent<Button>().onClick.Invoke();
            yield return null;
            SettingsStore.Flush();
            float savedSize = SettingsStore.Current.buttonSize;
            SettingsStore.UseFile(SettingsStore.Path);      // force a fresh load from disk
            Check(Mathf.Abs(SettingsStore.Current.buttonSize - savedSize) < 0.001f &&
                  Mathf.Abs(SettingsStore.Current.controlsOpacity - 0.9f) < 0.01f,
                  "Settings changed through the UI persist after a reload");

            app.Settings.Root.Find("Back").GetComponent<Button>().onClick.Invoke();
            yield return null;
            Check(app.Menu.Visible, "Back returns to the main menu");

            CaptureFrame(Dir + "menu.png");
            CheckLabelPixels(Dir + "menu.png", new RectInt(Screen.width / 2 - 220, Screen.height / 4, 440, 140),
                             "Main menu labels render (Arabic)");

            // ---- 9. every on-screen button ----
            app.Menu.Root.Find("InputTest").GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            Check(app.Current == Screen_.InputTest && app.Touch.Visible, "Input test shows the on-screen controls");

            var router = app.Router;
            var ids = new[] { ControlId.LP, ControlId.MP, ControlId.HP, ControlId.LK, ControlId.MK, ControlId.HK, ControlId.Start, ControlId.Pause };
            foreach (var id in ids) {
                var button = app.Touch.Get(id);
                if (button == null) { Check(false, "button exists: " + id); continue; }
                var p = new PointerEventData(EventSystem.current) { pointerId = 1 };
                button.OnPointerDown(p);
                router.Tick(); router.Tick();            // assist delays a press by one tick
                bool bit = Bit(router.Current, id);
                Check(button.IsHeld && bit, id + ": press sets the right InputFrame bit");
                button.OnPointerUp(p);
                router.Tick(); router.Tick();
                Check(!button.IsHeld && !Bit(router.Current, id), id + ": release clears the bit");
            }

            var lp = app.Touch.Get(ControlId.LP);
            var p1 = new PointerEventData(EventSystem.current) { pointerId = 1 };
            var p2 = new PointerEventData(EventSystem.current) { pointerId = 2 };
            lp.OnPointerDown(p1);
            lp.OnPointerDown(p2);
            lp.OnPointerUp(p2);
            Check(lp.IsHeld && lp.pointerId == 1, "A second finger cannot steal or release a held button");
            var mp = app.Touch.Get(ControlId.MP);
            mp.OnPointerDown(p2);
            router.Tick(); router.Tick();
            Check(router.Current.x && router.Current.y, "Two buttons at once produce x+y in the same tick");
            lp.OnPointerUp(p1); mp.OnPointerUp(p2);

            lp.OnPointerDown(p1);
            app.Touch.SetVisible(false);
            Check(!lp.IsHeld, "Hiding the controls releases every button");
            app.Touch.SetVisible(true);

            lp.OnPointerDown(p1);
            router.ReleaseAll();
            Check(!lp.IsHeld, "Focus loss / ReleaseAll clears held buttons");

            Canvas.ForceUpdateCanvases();
            yield return null;
            CaptureFrame(Dir + "controls.png");
            lp.Press(7);
            Canvas.ForceUpdateCanvases();
            yield return null;
            CaptureFrame(Dir + "controls-pressed.png");
            CheckPressedPixels(app, ControlId.LP);
            lp.Release();

            // ---- 10. layout editor ----
            app.Show(Screen_.Settings);
            yield return null;
            app.Settings.Root.Find("Page" + "Controls").GetComponent<Button>().onClick.Invoke();
            var editRow = FindRow(app.Settings.Root, "");
            app.Show(Screen_.Layout);
            yield return null;
            var editor = app.Layout;
            var before = editor.Working.Get(ControlId.HP).position;
            var expected = ControlLayout.Snap(before + new Vector2(0, 24), LayoutEditor.Grid);
            editor.Select(ControlId.HP);
            editor.Move(ControlId.HP, new Vector2(0, 24));
            Check(editor.Working.Get(ControlId.HP).position == expected,
                  "Layout editor drag moves a control on the 8-unit grid");
            string saveError;
            Check(editor.CanSave(out saveError), "Edited layout is valid" + (saveError != null ? ": " + saveError : ""));
            editor.Save();
            yield return null;
            var reloaded = SettingsStore.Current.ActiveLayout().Get(ControlId.HP).position;
            Check(reloaded == expected, "Saved layout is restored after reload");
            // an overlapping layout must be refused, not silently saved
            app.Show(Screen_.Layout);
            yield return null;
            app.Layout.Select(ControlId.HP);
            app.Layout.Move(ControlId.HP, app.Layout.Working.Get(ControlId.MP).position -
                                          app.Layout.Working.Get(ControlId.HP).position);
            string overlapError;
            Check(!app.Layout.CanSave(out overlapError), "Overlapping layout is refused: " + overlapError);
            app.Layout.Save();
            Check(SettingsStore.Current.ActiveLayout().Get(ControlId.HP).position == expected,
                  "A refused layout does not overwrite the saved one");
            app.Layout.ResetDefault();
            app.Layout.Save();
            Check(SettingsStore.Current.ActiveLayout().Get(ControlId.HP).position == ControlLayout.Default().Get(ControlId.HP).position,
                  "Reset restores the default layout");

            // ---- 12. dev.2: the character viewer and the MUGEN loaders ----
            app.Show(Screen_.Main);
            yield return null;
            app.Menu.Root.Find("Viewer").GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            var viewer = app.Viewer;
            Check(app.Current == Screen_.Viewer && viewer.Visible, "Menu opens the character viewer");
            Check(viewer.Character != null, "Character loads from Resources" +
                  (viewer.LoadError != null ? ": " + viewer.LoadError : ""));
            if (viewer.Character != null) {
                var chr = viewer.Character;
                Check(chr.DisplayName == "Kung Fu Man" && chr.Author == "Elecbyte",
                      "DEF header read: " + chr.DisplayName + " / " + chr.Author);
                Check(chr.Sff.Sprites.Count == 281 && chr.Sff.Palettes.Count == 16,
                      "SFF read on device path: " + chr.Sff.Sprites.Count + " sprites, " +
                      chr.Sff.Palettes.Count + " palettes");
                Check(chr.Air != null && chr.Air.Actions.Count == 117,
                      "AIR read on device path: " + (chr.Air != null ? chr.Air.Actions.Count : 0) + " actions");
                Check(chr.Snd != null && chr.Snd.Entries.Count == 12,
                      "SND read on device path: " + (chr.Snd != null ? chr.Snd.Entries.Count : 0) + " sounds");
                Check(viewer.LoadMilliseconds < 8000, "Character loads in " +
                      Mathf.RoundToInt((float)viewer.LoadMilliseconds) + " ms");

                Canvas.ForceUpdateCanvases();
                yield return null;
                CaptureFrame(Dir + "viewer.png");
                Check(CountSpritePixels(Dir + "viewer.png") > 1500, "The character sprite is actually drawn");

                // the standing action must advance exactly as the .air times it
                var stand = chr.Air.Get(0);
                stand.Reset();
                Check(stand.CurrentElement == 0, "Action 0 starts on its first element");
                for (int i = 0; i < 10; i++) stand.Tick();
                Check(stand.CurrentElement == 1, "Element 1 after its 10 ticks (got " + stand.CurrentElement + ")");
                for (int i = 0; i < 7; i++) stand.Tick();
                Check(stand.CurrentElement == 2, "Element 2 after 7 more ticks (got " + stand.CurrentElement + ")");

                viewer.StepAction(1);
                yield return null;
                Check(viewer.CurrentAction != 0, "Next steps to another action (" + viewer.CurrentAction + ")");
                viewer.StepAction(-1);
                yield return null;
                Check(viewer.CurrentAction == 0, "Prev steps back to action 0");

                // a palette swap must change what is on screen (paused, so only the palette moves)
                viewer.SetPlayingForTests(false);
                viewer.SelectActionNumber(0);
                Canvas.ForceUpdateCanvases();
                yield return null;
                CaptureFrame(Dir + "viewer-pal0.png");
                viewer.CyclePaletteForTests();
                Canvas.ForceUpdateCanvases();
                yield return null;
                CaptureFrame(Dir + "viewer-pal1.png");
                Check(DifferentPixels(Dir + "viewer-pal0.png", Dir + "viewer-pal1.png") > 400,
                      "Cycling the SFF palette repaints the character");
                viewer.ResetPaletteForTests();

                // collision boxes come from the .air and must be drawable
                viewer.ShowBoxesForTests(true);
                Canvas.ForceUpdateCanvases();
                yield return null;
                CaptureFrame(Dir + "viewer-boxes.png");
                Check(DifferentPixels(Dir + "viewer-pal0.png", Dir + "viewer-boxes.png") > 400,
                      "Clsn boxes from the .air are drawn over the sprite");
                viewer.ShowBoxesForTests(false);
                viewer.SetPlayingForTests(true);

                // every playable action resolves a sprite for at least one element
                int broken = 0;
                foreach (var no in chr.PlayableActions()) {
                    var a = chr.Air.Get(no);
                    bool any = false;
                    foreach (var f in a.Frames) if (chr.SpriteOf(f) != null) { any = true; break; }
                    if (!any) broken++;
                }
                Check(broken == 0, "Every playable action resolves its sprites (broken=" + broken + ")");
            }
            app.Show(Screen_.Main);
            yield return null;

            // ---- 13. dev.3: the training screen actually plays the character ----
            app.Menu.Root.Find("Training").GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            var training = app.Training;
            Check(app.Current == Screen_.Training && training.Visible, "Menu opens the training screen");
            Check(training.Fighter != null, "Fight engine starts from Resources" +
                  (training.LoadError != null ? ": " + training.LoadError : ""));
            Check(app.Touch.Visible, "Training shows the on-screen controls");

            if (training.Fighter != null) {
                var fighter = training.Fighter;
                Check(training.States.States.Count == 59,
                      "CNS + [Statedef -1] loaded on the device path: " + training.States.States.Count + " states");
                Check(training.Commands.Commands.Count == 37,
                      "CMD loaded on the device path: " + training.Commands.Commands.Count + " commands");
                Check(fighter.StateNo == 0 && fighter.AnimNo == 0 && fighter.Ctrl,
                      "The character starts standing with control");

                Canvas.ForceUpdateCanvases();
                yield return null;
                CaptureFrame(Dir + "training-idle.png");
                Check(CountSpritePixels(Dir + "training-idle.png") > 1500,
                      "The fighter is drawn on the training stage");

                // walking: feed the engine the same frames the touch layer would
                var right = new InputFrame { R = true };
                for (int i = 0; i < 12; i++) training.Feed(right);
                Check(fighter.StateNo == 20 && fighter.AnimNo == 20,
                      "Holding forward walks (state " + fighter.StateNo + ", anim " + fighter.AnimNo + ")");
                Check(Mathf.Abs(fighter.PosX - 2.4f * 12f) < 0.01f,
                      "Walked 12 ticks at walk.fwd = 2.4 (x=" + fighter.PosX.ToString("0.00") + ")");
                Canvas.ForceUpdateCanvases();
                yield return null;
                CaptureFrame(Dir + "training-walk.png");
                Check(DifferentPixels(Dir + "training-idle.png", Dir + "training-walk.png") > 400,
                      "Walking moves the character on screen");

                for (int i = 0; i < 12; i++) training.Feed(new InputFrame());
                Check(fighter.StateNo == 0, "Releasing the direction returns to standing");

                // punching: the state, the HitDef and the HUD
                training.Feed(new InputFrame { x = true });
                Check(fighter.StateNo == 200 && fighter.Move == MoveType.Attack,
                      "The light punch button enters state 200");
                Canvas.ForceUpdateCanvases();
                yield return null;
                CaptureFrame(Dir + "training-punch.png");
                Check(DifferentPixels(Dir + "training-idle.png", Dir + "training-punch.png") > 600,
                      "The punch animation is drawn");
                for (int i = 0; i < 20; i++) training.Feed(new InputFrame());
                Check(fighter.HitDefCount == 1, "The HitDef of AnimElem = 3 fired once");
                Check(training.LastCommand == "x", "The HUD reports the matched command (" + training.LastCommand + ")");

                // jumping, driven only through input frames
                for (int i = 0; i < 6; i++) training.Feed(new InputFrame { U = true });
                Check(fighter.StateNo == 50 && fighter.Type == StateType.Air,
                      "Up jumps into the air state (" + fighter.StateNo + ")");
                Canvas.ForceUpdateCanvases();
                yield return null;
                CaptureFrame(Dir + "training-jump.png");
                Check(DifferentPixels(Dir + "training-idle.png", Dir + "training-jump.png") > 600,
                      "The jump is drawn above the floor");
                for (int i = 0; i < 90; i++) training.Feed(new InputFrame());
                Check(fighter.StateNo == 0 && Mathf.Abs(fighter.PosY) < 0.01f,
                      "The jump lands back on the ground");

                // a quarter-circle special through the same input path
                training.ResetFighter();
                fighter = training.Fighter;
                for (int i = 0; i < 3; i++) training.Feed(new InputFrame { D = true });
                for (int i = 0; i < 3; i++) training.Feed(new InputFrame { D = true, R = true });
                for (int i = 0; i < 2; i++) training.Feed(new InputFrame { R = true });
                training.Feed(new InputFrame { R = true, x = true });
                Check(fighter.StateNo == 1000,
                      "Quarter-circle forward + x is the Kung Fu Palm (state " + fighter.StateNo + ")");
                Check(training.LastCommand == "QCF_x", "The matched command is QCF_x (" + training.LastCommand + ")");

                // collision boxes over the fighter
                training.ShowBoxesForTests(true);
                Canvas.ForceUpdateCanvases();
                yield return null;
                CaptureFrame(Dir + "training-boxes.png");
                training.ShowBoxesForTests(false);
                Canvas.ForceUpdateCanvases();
                yield return null;
                CaptureFrame(Dir + "training-nobox.png");
                Check(DifferentPixels(Dir + "training-boxes.png", Dir + "training-nobox.png") > 300,
                      "Clsn boxes are drawn over the fighter");

                // the HUD text must really be rendered, not just set: measure the pixels
                // inside the label's own rectangle, whatever the fixture resolution is
                Canvas.ForceUpdateCanvases();
                yield return null;
                CaptureFrame(Dir + "training-hud.png");
                CheckLabelPixels(Dir + "training-hud.png", PixelRect(training.HudRect),
                                 "Training HUD text renders");

                training.ResetFighter();
                Check(training.Fighter.StateNo == 0 && training.Ticks == 0, "Reset restarts the fighter");

                // 600 ticks of scripted play must not drift into an unknown trigger
                var script = new[] {
                    new InputFrame { R = true }, new InputFrame { L = true }, new InputFrame { D = true },
                    new InputFrame { U = true }, new InputFrame { x = true }, new InputFrame { y = true },
                    new InputFrame { a = true }, new InputFrame { D = true, x = true }, new InputFrame()
                };
                for (int i = 0; i < 600; i++) training.Feed(script[(i / 7) % script.Length]);
                Check(training.Fighter.UnknownTriggers.Count == 0,
                      "No unknown trigger after 600 ticks of play");
                Check(training.Fighter.UnknownControllers.Count == 0,
                      "No unrecognised state controller after 600 ticks of play");
            }
            app.Show(Screen_.Main);
            yield return null;

            // ---- 11. labels render in both languages ----
            app.Show(Screen_.Main);
            yield return null;
            foreach (var language in new[] { Language.Arabic, Language.English }) {
                SettingsStore.Current.language = language;
                Loc.Apply(language);
                app.Menu.SetVisible(false);
                DestroyImmediate(app.Menu.Root.gameObject);
                app.Menu.Build((RectTransform)app.UiCanvas.transform);
                app.Show(Screen_.Main);
                Canvas.ForceUpdateCanvases();
                yield return null;
                string path = Dir + "menu-" + language + ".png";
                CaptureFrame(path);
                CheckLabelPixels(path, new RectInt(Screen.width / 2 - 260, Screen.height / 4, 520, 200),
                                 "Labels render in " + language);
            }

            // Layout validity at the real target screens. The editor game view is 640x480 in
            // batch mode, so the geometry is checked against computed safe areas instead of
            // whatever the fixture happens to render at (that is a deliberate separation).
            foreach (var target in new[] {
                new Vector4(1280, 720, 0, 0), new Vector4(2400, 1080, 90, 0),
                new Vector4(1024, 768, 0, 0), new Vector4(2560, 1080, 0, 0) }) {
                float scale = ControlLayout.RefHeight / target.y;
                var safe = new Rect(0, 0, (target.x - target.z) * scale, ControlLayout.RefHeight);
                foreach (var preset in ControlLayout.PresetNames) {
                    string err;
                    Check(ControlLayout.Preset(preset).Validate(safe, SettingsStore.Current.buttonSize, out err),
                          "Layout " + preset + " valid at " + target.x + "x" + target.y +
                          (target.z > 0 ? " (punch-hole inset)" : "") + (err != null ? ": " + err : ""));
                }
            }

            Finish();
        }

        void Finish() {
            Application.logMessageReceived -= CaptureError;
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Dir + "ui-regression-" + Screen.width + "x" + Screen.height + ".json",
                              JsonUtility.ToJson(report, true));
            Debug.Log($"[IK UI] checks={report.passed.Count} errors={report.errors.Count}");
            EditorApplication.Exit(report.errors.Count == 0 ? 0 : 1);
        }

        static bool Bit(InputFrame f, ControlId id) {
            switch (id) {
                case ControlId.LP: return f.x;
                case ControlId.MP: return f.y;
                case ControlId.HP: return f.z;
                case ControlId.LK: return f.a;
                case ControlId.MK: return f.b;
                case ControlId.HK: return f.c;
                case ControlId.Start: return f.s;
                case ControlId.Pause: return f.m;
                case ControlId.D: return f.d;
                case ControlId.W: return f.w;
            }
            return false;
        }

        /// <summary>Screen-pixel rectangle of a uGUI element, for pixel checks.</summary>
        static RectInt PixelRect(RectTransform rt) {
            if (rt == null) return new RectInt(0, 0, 0, 0);
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            int x0 = Mathf.FloorToInt(Mathf.Min(corners[0].x, corners[1].x, corners[2].x));
            int y0 = Mathf.FloorToInt(Mathf.Min(corners[0].y, corners[1].y, corners[2].y));
            int x1 = Mathf.CeilToInt(Mathf.Max(corners[0].x, corners[2].x, corners[3].x));
            int y1 = Mathf.CeilToInt(Mathf.Max(corners[0].y, corners[2].y, corners[3].y));
            return new RectInt(x0, y0, Mathf.Max(1, x1 - x0), Mathf.Max(1, y1 - y0));
        }

        static Transform FindRow(RectTransform root, string label) {
            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == "row:" + label) return rt;
            return null;
        }

        void CheckPressedPixels(IKApp app, ControlId id) {
            var normal = new Texture2D(2, 2);
            var pressed = new Texture2D(2, 2);
            normal.LoadImage(File.ReadAllBytes(Dir + "controls.png"));
            pressed.LoadImage(File.ReadAllBytes(Dir + "controls-pressed.png"));
            var rt = app.Touch.Get(id).GetComponent<RectTransform>();
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(corners[0].x), 0, normal.width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(corners[0].y), 0, normal.height - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(corners[2].x), 0, normal.width - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(corners[2].y), 0, normal.height - 1);
            int changed = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++) {
                    var a = normal.GetPixel(x, y);
                    var b = pressed.GetPixel(x, y);
                    if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 0.15f) changed++;
                }
            Check(changed > 300, id + " pressed state is visible (changed pixels=" + changed + ")");
            DestroyImmediate(normal); DestroyImmediate(pressed);
        }

        /// <summary>Pixels inside the viewer stage that are not the flat background.</summary>
        int CountSpritePixels(string path) {
            var image = new Texture2D(2, 2);
            image.LoadImage(File.ReadAllBytes(path));
            var bg = new Color(0.07f, 0.08f, 0.11f);
            int count = 0;
            for (int y = image.height / 8; y < image.height * 7 / 8; y++)
                for (int x = image.width / 4; x < image.width * 3 / 4; x++) {
                    var c = image.GetPixel(x, y);
                    if (Mathf.Abs(c.r - bg.r) + Mathf.Abs(c.g - bg.g) + Mathf.Abs(c.b - bg.b) > 0.12f) count++;
                }
            DestroyImmediate(image);
            return count;
        }

        /// <summary>How many pixels differ between two captures.</summary>
        int DifferentPixels(string a, string b) {
            var ia = new Texture2D(2, 2); ia.LoadImage(File.ReadAllBytes(a));
            var ib = new Texture2D(2, 2); ib.LoadImage(File.ReadAllBytes(b));
            int n = 0;
            int w = Mathf.Min(ia.width, ib.width), h = Mathf.Min(ia.height, ib.height);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) {
                    var ca = ia.GetPixel(x, y);
                    var cb = ib.GetPixel(x, y);
                    if (Mathf.Abs(ca.r - cb.r) + Mathf.Abs(ca.g - cb.g) + Mathf.Abs(ca.b - cb.b) > 0.1f) n++;
                }
            DestroyImmediate(ia); DestroyImmediate(ib);
            return n;
        }

        void CheckLabelPixels(string path, RectInt region, string name) {
            var image = new Texture2D(2, 2);
            image.LoadImage(File.ReadAllBytes(path));
            int light = 0;
            for (int y = Mathf.Max(0, region.yMin); y < Mathf.Min(image.height, region.yMax); y++)
                for (int x = Mathf.Max(0, region.xMin); x < Mathf.Min(image.width, region.xMax); x++) {
                    var c = image.GetPixel(x, y);
                    if (c.r > 0.5f && c.g > 0.45f && c.b > 0.3f) light++;
                }
            Check(light > 150, name + " (light pixels=" + light + ")");
            DestroyImmediate(image);
        }

        static void CaptureFrame(string path) {
            var camera = Camera.main;
            var canvases = FindObjectsOfType<Canvas>();
            var modes = new RenderMode[canvases.Length];
            var cameras = new Camera[canvases.Length];
            var distances = new float[canvases.Length];
            var previous = RenderTexture.active;
            var previousTarget = camera != null ? camera.targetTexture : null;
            int w = Screen.width, h = Screen.height;
            var target = new RenderTexture(w, h, 24);
            var image = new Texture2D(w, h, TextureFormat.RGB24, false);
            try {
                if (camera != null) camera.targetTexture = target;
                for (int i = 0; i < canvases.Length; i++) {
                    modes[i] = canvases[i].renderMode;
                    cameras[i] = canvases[i].worldCamera;
                    distances[i] = canvases[i].planeDistance;
                    canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                    canvases[i].worldCamera = camera;
                    canvases[i].planeDistance = 0.5f;
                }
                Canvas.ForceUpdateCanvases();
                if (camera != null) camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            } finally {
                for (int i = 0; i < canvases.Length; i++) {
                    canvases[i].renderMode = modes[i];
                    canvases[i].worldCamera = cameras[i];
                    canvases[i].planeDistance = distances[i];
                }
                if (camera != null) camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                DestroyImmediate(target);
                DestroyImmediate(image);
            }
        }
    }
}
#endif
