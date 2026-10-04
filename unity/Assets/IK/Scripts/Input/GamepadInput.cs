using UnityEngine;
using IK.Settings;

namespace IK.Input {
    /// <summary>
    /// Gamepad and hardware keyboard → <see cref="InputFrame"/>. Defaults mirror
    /// <c>[Joystick_P1]</c> / <c>[Keys_P1]</c> in Ikemen's <c>defaultConfig.ini</c>
    /// (arrows + z x c / a s d, gamepad A B RT / X Y RB).
    ///
    /// Implementation note (documented choice, see docs/DEV1.md): touch uses
    /// <c>UnityEngine.Input.touches</c> through uGUI, and gamepads/keyboards use the Input
    /// System package when it is present (<c>ENABLE_INPUT_SYSTEM</c>), falling back to the
    /// legacy axis/button API otherwise, so the build never depends on package resolution.
    /// </summary>
    public class GamepadInput : MonoBehaviour {
        public bool ActiveThisFrame { get; private set; }
        public InputDevice LastDevice { get; private set; } = InputDevice.Gamepad;

        GameSettings settings;
        void Awake() { settings = SettingsStore.Current; }
        public void Configure(GameSettings s) { settings = s; }

        public InputFrame Sample() {
            var f = new InputFrame();
            ActiveThisFrame = false;
            float dead = settings != null ? settings.stickDeadZone : 0.25f;

#if ENABLE_INPUT_SYSTEM && IK_USE_INPUT_SYSTEM
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null) {
                Vector2 stick = pad.leftStick.ReadValue();
                var dpad = pad.dpad.ReadValue();
                if (dpad.sqrMagnitude > stick.sqrMagnitude) stick = dpad;
                InputLogic.Quantise8(stick, dead, out f.U, out f.D, out f.L, out f.R);
                f.analog = stick;
                f.a = pad.buttonSouth.isPressed;                       // A  -> light kick
                f.b = pad.buttonEast.isPressed;                        // B  -> medium kick
                f.c = pad.rightTrigger.ReadValue() > 0.5f;             // RT -> heavy kick
                f.x = pad.buttonWest.isPressed;                        // X  -> light punch
                f.y = pad.buttonNorth.isPressed;                       // Y  -> medium punch
                f.z = pad.rightShoulder.isPressed;                     // RB -> heavy punch
                f.d = pad.leftShoulder.isPressed;
                f.w = pad.leftTrigger.ReadValue() > 0.5f;
                f.s = pad.startButton.isPressed;
                f.m = pad.selectButton.isPressed;
                if (f.Any) { ActiveThisFrame = true; LastDevice = InputDevice.Gamepad; }
            }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null) {
                var k = new InputFrame {
                    U = kb.upArrowKey.isPressed, D = kb.downArrowKey.isPressed,
                    L = kb.leftArrowKey.isPressed, R = kb.rightArrowKey.isPressed,
                    a = kb.zKey.isPressed, b = kb.xKey.isPressed, c = kb.cKey.isPressed,
                    x = kb.aKey.isPressed, y = kb.sKey.isPressed, z = kb.dKey.isPressed,
                    s = kb.enterKey.isPressed, d = kb.qKey.isPressed, w = kb.wKey.isPressed,
                    m = kb.escapeKey.isPressed,
                };
                if (k.Any) { ActiveThisFrame = true; LastDevice = InputDevice.Keyboard; }
                f = f.Or(k);
            }
#else
            // Legacy input manager (also what the editor fixture uses).
            Vector2 stick = new Vector2(AxisRaw("Horizontal"), AxisRaw("Vertical"));
            bool U, D, L, R;
            InputLogic.Quantise8(stick, dead, out U, out D, out L, out R);
            f.U = U; f.D = D; f.L = L; f.R = R; f.analog = stick;
            f.a = Key(KeyCode.Z) || Key(KeyCode.JoystickButton0);
            f.b = Key(KeyCode.X) || Key(KeyCode.JoystickButton1);
            f.c = Key(KeyCode.C) || Key(KeyCode.JoystickButton7);
            f.x = Key(KeyCode.A) || Key(KeyCode.JoystickButton2);
            f.y = Key(KeyCode.S) || Key(KeyCode.JoystickButton3);
            f.z = Key(KeyCode.D) || Key(KeyCode.JoystickButton5);
            f.s = Key(KeyCode.Return) || Key(KeyCode.JoystickButton7);
            f.d = Key(KeyCode.Q) || Key(KeyCode.JoystickButton4);
            f.w = Key(KeyCode.W) || Key(KeyCode.JoystickButton6);
            f.m = Key(KeyCode.Escape) || Key(KeyCode.JoystickButton8);
            if (f.Any) {
                ActiveThisFrame = true;
                LastDevice = UnityEngine.Input.GetJoystickNames().Length > 0 && stick.sqrMagnitude > 0.01f
                    ? InputDevice.Gamepad : InputDevice.Keyboard;
            }
#endif
            return f;
        }

#if !(ENABLE_INPUT_SYSTEM && IK_USE_INPUT_SYSTEM)
        static bool Key(KeyCode code) {
            try { return UnityEngine.Input.GetKey(code); } catch { return false; }
        }
        static float AxisRaw(string axis) {
            try { return UnityEngine.Input.GetAxisRaw(axis); } catch { return 0f; }
        }
#endif
    }
}
