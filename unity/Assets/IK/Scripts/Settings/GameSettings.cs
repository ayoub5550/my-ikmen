using System;
using System.Collections.Generic;
using UnityEngine;

namespace IK.Settings {
    public enum DirectionMode { DPad = 0, FloatingStick = 1, FixedStick = 2 }
    public enum OnScreenControls { Auto = 0, Always = 1, Never = 2 }
    public enum Haptics { Off = 0, Light = 1, Strong = 2 }
    public enum Language { System = 0, Arabic = 1, English = 2 }
    public enum PixelFilter { Sharp = 0, Smooth = 1 }

    /// <summary>
    /// Every persisted setting (docs/TOUCH_AND_SETTINGS.md §5). Key names mirror Ikemen's
    /// config.ini where one exists, so a config import/export stays possible later.
    /// Defaults here are the spec's defaults; <see cref="Clamp"/> is the single place that
    /// enforces ranges and is unit-tested.
    /// </summary>
    [Serializable]
    public class GameSettings {
        public const int CurrentSchema = 2;
        public int schemaVersion = CurrentSchema;

        // ---- Controls ----
        public DirectionMode directionMode = DirectionMode.DPad;
        public float buttonSize = 1.0f;            // 0.6 … 1.6 (100 % default)
        public float controlsOpacity = 0.55f;      // 0.2 … 1.0
        public bool slideToPress = true;
        public bool macroButtons = false;
        public bool showDW = false;
        public Haptics haptics = Haptics.Light;
        public OnScreenControls onScreenControls = OnScreenControls.Auto;
        public bool buttonAssist = true;           // Input.ButtonAssist
        public int socdResolution = 4;             // Input.SOCDResolution (0..4)
        public float stickSensitivity = 0.5f;      // ControllerStickSensitivity
        public float stickDeadZone = 0.25f;
        public string layoutPreset = "Default";
        public int layoutSlot = 0;                 // three save slots
        public List<ControlLayout> layoutSlots = new List<ControlLayout>();

        // ---- Game ----
        public int difficulty = 5;                 // Options.Difficulty 1..8
        public int life = 100;                     // Options.Life 10..300 %
        public int roundTime = 99;                 // Options.Time: 99/60/30/-1 (infinite)
        public int roundsToWin = 2;                // Options.Match.Wins 1..5
        public int gameSpeed = 0;                  // Options.GameSpeed -9..9
        public bool autoGuard = false;             // Options.AutoGuard

        // ---- Audio ----
        public int masterVolume = 100;             // MasterVolume
        public int bgmVolume = 75;                 // BGMVolume
        public int sfxVolume = 80;                 // WavVolume

        // ---- Video ----
        public int fpsCap = 60;                    // 30 or 60
        public int renderScale = 100;              // 50..100 %
        public PixelFilter pixelFilter = PixelFilter.Sharp;
        public bool showFps = false;

        // ---- Language ----
        public Language language = Language.System;

        public GameSettings Clone() => JsonUtility.FromJson<GameSettings>(JsonUtility.ToJson(this));

        /// <summary>Clamp every value into its documented range. Always call after loading.</summary>
        public GameSettings Clamp() {
            buttonSize = Mathf.Clamp(buttonSize, 0.6f, 1.6f);
            controlsOpacity = Mathf.Clamp(controlsOpacity, 0.2f, 1f);
            socdResolution = Mathf.Clamp(socdResolution, 0, 4);
            stickSensitivity = Mathf.Clamp(stickSensitivity, 0.1f, 1f);
            stickDeadZone = Mathf.Clamp(stickDeadZone, 0.05f, 0.6f);
            difficulty = Mathf.Clamp(difficulty, 1, 8);
            life = Mathf.Clamp(life, 10, 300);
            if (roundTime != 99 && roundTime != 60 && roundTime != 30 && roundTime != -1) roundTime = 99;
            roundsToWin = Mathf.Clamp(roundsToWin, 1, 5);
            gameSpeed = Mathf.Clamp(gameSpeed, -9, 9);
            masterVolume = Mathf.Clamp(masterVolume, 0, 100);
            bgmVolume = Mathf.Clamp(bgmVolume, 0, 100);
            sfxVolume = Mathf.Clamp(sfxVolume, 0, 100);
            fpsCap = fpsCap <= 45 ? 30 : 60;
            renderScale = Mathf.Clamp(renderScale, 50, 100);
            layoutSlot = Mathf.Clamp(layoutSlot, 0, 2);
            if (Array.IndexOf(ControlLayout.PresetNames, layoutPreset) < 0) layoutPreset = "Default";
            while (layoutSlots.Count < 3) layoutSlots.Add(null);
            if (layoutSlots.Count > 3) layoutSlots.RemoveRange(3, layoutSlots.Count - 3);
            return this;
        }

        /// <summary>The layout in the active slot, or the selected preset when the slot is empty.</summary>
        public ControlLayout ActiveLayout() {
            Clamp();
            var slot = layoutSlots[layoutSlot];
            if (slot != null && slot.controls != null && slot.controls.Count > 0) return slot;
            return ControlLayout.Preset(layoutPreset);
        }

        public void SaveLayout(ControlLayout layout) {
            Clamp();
            layoutSlots[layoutSlot] = layout.Clone();
        }

        public void ResetLayout() {
            Clamp();
            layoutSlots[layoutSlot] = null;
        }

        /// <summary>
        /// Migration from an older on-disk schema. v1 stored opacity as a 0..100 int in
        /// <c>controlsOpacity</c> and had no layout slots.
        /// </summary>
        public static GameSettings Migrate(GameSettings s, int fromSchema) {
            if (s == null) return new GameSettings();
            if (fromSchema < 2) {
                if (s.controlsOpacity > 1.01f) s.controlsOpacity /= 100f;
                if (s.layoutSlots == null) s.layoutSlots = new List<ControlLayout>();
            }
            s.schemaVersion = CurrentSchema;
            return s.Clamp();
        }
    }
}
