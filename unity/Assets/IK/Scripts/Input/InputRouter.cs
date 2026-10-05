using System.Collections.Generic;
using UnityEngine;
using IK.Settings;

namespace IK.Input {
    public enum InputDevice { Touch, Gamepad, Keyboard }

    /// <summary>
    /// The single place input becomes game input. MUGEN logic runs at a fixed 60 ticks per
    /// second regardless of render FPS, so this component accumulates render-frame events
    /// (latching "pressed since the last tick", so a 1-frame tap is never lost), then once
    /// per tick ORs touch + gamepad + keyboard, applies SOCD resolution and button assist,
    /// and publishes an immutable <see cref="InputFrame"/>.
    /// </summary>
    public class InputRouter : MonoBehaviour {
        public static InputRouter Instance { get; private set; }
        public const float TickRate = 60f;

        public InputFrame Current { get; private set; }
        public InputFrame Previous { get; private set; }
        public int TickCount { get; private set; }
        public InputDevice LastDevice { get; private set; } = InputDevice.Touch;
        public System.Action<InputFrame> OnTick;
        /// <summary>Set while a menu/pause owns the input (button assist is disabled, like Ikemen).</summary>
        public bool paused;
        /// <summary>
        /// False while a screen owns the display (menus, settings, layout editor): the device
        /// auto-hide logic must never bring the on-screen controls back on top of a menu.
        /// </summary>
        public bool allowTouchControls = true;

        public TouchControls touch;
        public GamepadInput gamepad;

        readonly InputLogic.Socd socd = new InputLogic.Socd();
        readonly InputLogic.ButtonAssist assist = new InputLogic.ButtonAssist();
        readonly List<InputFrame> history = new List<InputFrame>(64);
        GameSettings settings;
        InputFrame latched;
        float accumulator;

        public IReadOnlyList<InputFrame> History => history;

        void Awake() {
            Instance = this;
            settings = SettingsStore.Current;
        }

        public void Configure(GameSettings s) {
            settings = s;
            socd.Reset();
            assist.Reset();
        }

        void Update() {
            if (settings == null) settings = SettingsStore.Current;
            // 1. latch everything seen between ticks (touch taps shorter than a tick count)
            latched = latched.Or(SampleRaw());
            // 2. advance logic ticks
            accumulator += SnapDelta(Time.unscaledDeltaTime, TickRate);
            float step = 1f / TickRate;
            int guard = 0;
            while (accumulator >= step && guard++ < 8) {
                accumulator -= step;
                Tick();
            }
            UpdateDeviceVisibility();
            SettingsStore.Tick();
        }

        /// <summary>
        /// dev.7 frame pacing: a frame time within 2 ms of a whole number of display
        /// frames at the tick rate (1/60, 2/60 …, or 1/120 on a 120 Hz panel) is treated as exactly
        /// that. Without it the jitter of a 16.4 / 16.9 ms frame makes the accumulator run 0 or 2
        /// logic ticks in some frames — a visible stutter in a game that moves every tick.
        /// </summary>
        public static float SnapDelta(float dt, float tickRate) {
            if (dt <= 0f) return 0f;
            float step = 1f / tickRate;
            const float tolerance = 0.002f;
            for (int k = 1; k <= 4; k++) {
                float target = step * k;
                if (Mathf.Abs(dt - target) < tolerance) return target;
            }
            float half = step * 0.5f;                     // 120 Hz panels
            if (Mathf.Abs(dt - half) < tolerance * 0.5f) return half;
            return Mathf.Min(dt, 0.25f);
        }

        InputFrame SampleRaw() {
            var f = new InputFrame();
            if (touch != null) f = f.Or(touch.Sample());
            if (gamepad != null) f = f.Or(gamepad.Sample());
            return f;
        }

        /// <summary>Runs one 60 Hz logic tick. Exposed so tests can drive it deterministically.</summary>
        public void Tick() {
            var raw = latched.Or(SampleRaw());
            latched = new InputFrame();

            bool U = raw.U, D = raw.D, L = raw.L, R = raw.R;
            socd.Resolve(settings != null ? settings.socdResolution : 4, ref U, ref D, ref L, ref R);
            raw.U = U; raw.D = D; raw.L = L; raw.R = R;

            var buttons = assist.Check(raw.Buttons(), settings == null || settings.buttonAssist, paused);
            raw = raw.WithButtons(buttons);

            Previous = Current;
            Current = raw;
            TickCount++;
            if (raw.Any || (history.Count > 0 && history[history.Count - 1].Any)) {
                history.Add(raw);
                if (history.Count > 60) history.RemoveAt(0);
            }
            OnTick?.Invoke(raw);
        }

        void UpdateDeviceVisibility() {
            if (touch == null || settings == null) return;
            if (!allowTouchControls) {
                if (touch.Visible) touch.SetVisible(false);
                return;
            }
            if (gamepad != null && gamepad.ActiveThisFrame) LastDevice = gamepad.LastDevice;
            else if (UnityEngine.Input.touchCount > 0 || UnityEngine.Input.GetMouseButton(0)) LastDevice = InputDevice.Touch;

            switch (settings.onScreenControls) {
                case OnScreenControls.Always: if (!touch.Visible) touch.SetVisible(true); break;
                case OnScreenControls.Never: if (touch.Visible) touch.SetVisible(false); break;
                default:
                    bool show = LastDevice == InputDevice.Touch;
                    if (touch.Visible != show) touch.SetVisible(show);
                    break;
            }
        }

        public void ReleaseAll() {
            latched = new InputFrame();
            Current = new InputFrame();
            socd.Reset();
            assist.Reset();
            if (touch != null) touch.ReleaseAll();
        }

        void OnApplicationFocus(bool focus) { if (!focus) ReleaseAll(); }
        void OnApplicationPause(bool p) { if (p) { ReleaseAll(); SettingsStore.Flush(); } }
        void OnApplicationQuit() { SettingsStore.Flush(); }
    }
}
