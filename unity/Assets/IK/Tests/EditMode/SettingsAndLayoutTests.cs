using System.IO;
using NUnit.Framework;
using UnityEngine;
using IK.Settings;
using IK.UI;

namespace IK.Tests {
    /// <summary>
    /// docs/TOUCH_AND_SETTINGS.md §6, EditMode cases 6 and 7: settings defaults, clamping,
    /// JSON round-trip and migration; layout presets valid at every target aspect ratio.
    /// Plus the Arabic shaper, which decides whether any Arabic label can render at all.
    /// </summary>
    public class SettingsAndLayoutTests {
        // ---- 6. settings -----------------------------------------------------------
        [Test]
        public void Defaults_match_the_specification() {
            var s = new GameSettings().Clamp();
            Assert.AreEqual(DirectionMode.DPad, s.directionMode);
            Assert.AreEqual(1.0f, s.buttonSize, 0.001f);
            Assert.AreEqual(0.55f, s.controlsOpacity, 0.001f);
            Assert.IsTrue(s.slideToPress);
            Assert.IsFalse(s.macroButtons);
            Assert.IsFalse(s.showDW);
            Assert.AreEqual(Haptics.Light, s.haptics);
            Assert.AreEqual(OnScreenControls.Auto, s.onScreenControls);
            Assert.IsTrue(s.buttonAssist);
            Assert.AreEqual(4, s.socdResolution);
            Assert.AreEqual(0.5f, s.stickSensitivity, 0.001f);
            Assert.AreEqual(0.25f, s.stickDeadZone, 0.001f);
            Assert.AreEqual(5, s.difficulty);
            Assert.AreEqual(100, s.life);
            Assert.AreEqual(99, s.roundTime);
            Assert.AreEqual(2, s.roundsToWin);
            Assert.AreEqual(0, s.gameSpeed);
            Assert.IsFalse(s.autoGuard);
            Assert.AreEqual(100, s.masterVolume);
            Assert.AreEqual(75, s.bgmVolume);
            Assert.AreEqual(80, s.sfxVolume);
            Assert.AreEqual(60, s.fpsCap);
            Assert.AreEqual(100, s.renderScale);
            Assert.AreEqual(PixelFilter.Sharp, s.pixelFilter);
            Assert.IsFalse(s.showFps);
        }

        [Test]
        public void Out_of_range_values_are_clamped() {
            var s = new GameSettings {
                buttonSize = 9f, controlsOpacity = -2f, socdResolution = 11, difficulty = 99,
                life = 1, roundTime = 7, roundsToWin = 0, gameSpeed = 50, masterVolume = 500,
                renderScale = 5, fpsCap = 144, layoutSlot = 9, layoutPreset = "nonsense"
            }.Clamp();
            Assert.AreEqual(1.6f, s.buttonSize, 0.001f);
            Assert.AreEqual(0.2f, s.controlsOpacity, 0.001f);
            Assert.AreEqual(4, s.socdResolution);
            Assert.AreEqual(8, s.difficulty);
            Assert.AreEqual(10, s.life);
            Assert.AreEqual(99, s.roundTime);
            Assert.AreEqual(1, s.roundsToWin);
            Assert.AreEqual(9, s.gameSpeed);
            Assert.AreEqual(100, s.masterVolume);
            Assert.AreEqual(50, s.renderScale);
            Assert.AreEqual(60, s.fpsCap);
            Assert.AreEqual(2, s.layoutSlot);
            Assert.AreEqual("Default", s.layoutPreset);
        }

        [Test]
        public void Json_round_trip_preserves_every_value() {
            var s = new GameSettings {
                directionMode = DirectionMode.FloatingStick, buttonSize = 1.25f, controlsOpacity = 0.8f,
                slideToPress = false, macroButtons = true, showDW = true, haptics = Haptics.Strong,
                onScreenControls = OnScreenControls.Always, buttonAssist = false, socdResolution = 2,
                difficulty = 7, life = 150, roundTime = 30, roundsToWin = 3, gameSpeed = -2,
                autoGuard = true, masterVolume = 50, bgmVolume = 10, sfxVolume = 90,
                fpsCap = 30, renderScale = 70, pixelFilter = PixelFilter.Smooth, showFps = true,
                language = Language.English, layoutPreset = "Compact", layoutSlot = 2
            }.Clamp();
            var back = SettingsStore.RoundTrip(s);
            Assert.AreEqual(JsonUtility.ToJson(s), JsonUtility.ToJson(back));
        }

        [Test]
        public void Saved_layout_survives_a_round_trip() {
            var s = new GameSettings().Clamp();
            var layout = ControlLayout.Default();
            layout.Get(ControlId.LP).position = new Vector2(-400, 300);
            layout.Get(ControlId.LP).size = 150;
            s.SaveLayout(layout);
            var back = SettingsStore.RoundTrip(s);
            Assert.AreEqual(new Vector2(-400, 300), back.ActiveLayout().Get(ControlId.LP).position);
            Assert.AreEqual(150f, back.ActiveLayout().Get(ControlId.LP).size, 0.01f);
        }

        [Test]
        public void Migration_from_schema_1_rescales_opacity() {
            var old = new GameSettings { schemaVersion = 1, controlsOpacity = 55f };
            var migrated = GameSettings.Migrate(old, 1);
            Assert.AreEqual(GameSettings.CurrentSchema, migrated.schemaVersion);
            Assert.AreEqual(0.55f, migrated.controlsOpacity, 0.001f);
        }

        [Test]
        public void Settings_file_round_trips_through_disk() {
            string path = Path.Combine(Path.GetTempPath(), "ik-settings-test.json");
            if (File.Exists(path)) File.Delete(path);
            SettingsStore.UseFile(path);
            SettingsStore.Current.difficulty = 8;
            SettingsStore.Current.controlsOpacity = 0.42f;
            SettingsStore.MarkChanged();
            SettingsStore.Flush();
            Assert.IsTrue(File.Exists(path));
            SettingsStore.UseFile(path);                 // forces a reload
            Assert.AreEqual(8, SettingsStore.Current.difficulty);
            Assert.AreEqual(0.42f, SettingsStore.Current.controlsOpacity, 0.001f);
            File.Delete(path);
            SettingsStore.UseFile(null);
        }

        // ---- 7. layouts at every target aspect --------------------------------------
        static Rect SafeRef(int width, int height, float insetLeft = 0f) {
            // the canvas matches height, so reference height is always 720
            float scale = ControlLayout.RefHeight / height;
            return new Rect(0, 0, (width - insetLeft) * scale, height * scale);
        }

        [TestCase(1280, 720, 0f, TestName = "16:9")]
        [TestCase(2400, 1080, 90f, TestName = "20:9 Poco F3 with punch-hole inset")]
        [TestCase(1024, 768, 0f, TestName = "4:3 tablet")]
        [TestCase(2560, 1080, 0f, TestName = "21:9")]
        public void Every_preset_is_valid_on_every_target_screen(int width, int height, float inset) {
            var safe = SafeRef(width, height, inset);
            foreach (var preset in ControlLayout.PresetNames) {
                var layout = ControlLayout.Preset(preset);
                string error;
                Assert.IsTrue(layout.Validate(safe, 1f, out error),
                              preset + " at " + width + "x" + height + ": " + error);
                ControlId offender;
                Assert.IsFalse(layout.HitsHud(safe, 1f, out offender),
                               preset + ": " + offender + " covers the HUD zone");
            }
        }

        [Test]
        public void Overlapping_controls_are_refused() {
            var layout = ControlLayout.Default();
            layout.Get(ControlId.LP).position = layout.Get(ControlId.MP).position;
            string error;
            Assert.IsFalse(layout.Validate(SafeRef(1280, 720), 1f, out error));
            StringAssert.Contains("overlaps", error);
        }

        [Test]
        public void A_control_pushed_outside_the_safe_area_is_refused() {
            var layout = ControlLayout.Default();
            layout.Get(ControlId.HP).position = new Vector2(80, 310);   // off the right edge
            string error;
            Assert.IsFalse(layout.Validate(SafeRef(1280, 720), 1f, out error));
            StringAssert.Contains("safe area", error);
        }

        [Test]
        public void Targets_never_fall_below_the_minimum_size() {
            var layout = ControlLayout.Compact();
            var safe = SafeRef(1024, 768);
            foreach (var c in layout.controls) {
                if (!c.visible) continue;
                var r = layout.RectOf(c, safe, 0.6f);
                Assert.GreaterOrEqual(r.width, ControlLayout.MinTarget - 0.01f, c.id.ToString());
            }
        }

        // ---- Arabic shaping (needed by rendered case 11) ----------------------------
        [Test]
        public void Arabic_text_is_joined_and_reordered() {
            // "الإعدادات" must become presentation forms, reversed for a LTR renderer
            string shaped = ArabicShaper.Shape("الإعدادات");
            Assert.IsFalse(shaped.Contains("\u0627"), "no isolated base letters remain");
            foreach (char c in shaped)
                Assert.IsTrue(c >= '\uFE70' && c <= '\uFEFF', "every glyph is a presentation form: " + (int)c);
            Assert.AreEqual('\uFE8D', shaped[shaped.Length - 1], "alef (first letter) ends up last for a LTR renderer");
        }

        [Test]
        public void Lam_alef_becomes_one_ligature() {
            string shaped = ArabicShaper.Shape("لا");
            Assert.AreEqual(1, shaped.Length);
            Assert.AreEqual('\uFEFB', shaped[0]);
        }

        [Test]
        public void Latin_and_numbers_stay_left_to_right() {
            Assert.AreEqual("dev.1", ArabicShaper.Shape("dev.1"));
            string mixed = ArabicShaper.Shape("إكمن dev.1");
            StringAssert.Contains("dev.1", mixed);
        }

        [Test]
        public void A_space_between_an_arabic_and_a_latin_word_survives() {
            string shaped = ArabicShaper.Shape("موارد screenpack برخصة");
            StringAssert.Contains(" screenpack ", shaped);
        }

        [Test]
        public void English_strings_are_untouched() {
            Assert.AreEqual("Settings", ArabicShaper.Shape("Settings"));
        }
    }
}
