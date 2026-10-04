using UnityEngine;
using UnityEngine.UI;
using IK.Input;
using IK.Settings;
using IK.UI;

namespace IK.App {
    public enum Screen_ { Main, InputTest, Settings, Layout, Viewer }

    /// <summary>
    /// dev.1 application shell: builds every screen from code, owns the canvases and the
    /// 60 Hz <see cref="InputRouter"/>, and wires the Android back button.
    /// There is no fight engine yet — the Input Test screen is the gate where the touch
    /// layer is judged (docs/TOUCH_AND_SETTINGS.md §6).
    /// </summary>
    public class IKApp : MonoBehaviour {
        public static IKApp Instance { get; private set; }

        public Canvas TouchCanvas { get; private set; }
        public Canvas UiCanvas { get; private set; }
        public TouchControls Touch { get; private set; }
        public InputRouter Router { get; private set; }
        public GamepadInput Gamepad { get; private set; }
        public MainMenu Menu { get; private set; }
        public SettingsMenu Settings { get; private set; }
        public LayoutEditor Layout { get; private set; }
        public CharViewer Viewer { get; private set; }
        public InputDisplay Display { get; private set; }
        public Screen_ Current { get; private set; } = Screen_.Main;

        void Awake() {
            Instance = this;
            var s = SettingsStore.Current;
            Loc.Apply(s.language);
            Application.targetFrameRate = s.fpsCap;
            QualitySettings.vSyncCount = 0;
            UnityEngine.Screen.orientation = ScreenOrientation.LandscapeLeft;
            UnityEngine.Screen.sleepTimeout = SleepTimeout.NeverSleep;
            AudioListener.volume = s.masterVolume / 100f;

            TouchCanvas = UIKit.CreateCanvas("TouchCanvas", 5);
            UiCanvas = UIKit.CreateCanvas("UICanvas", 10);
            DontDestroyOnLoad(gameObject);

            var touchGo = new GameObject("TouchControls");
            touchGo.transform.SetParent(TouchCanvas.transform, false);
            Touch = touchGo.AddComponent<TouchControls>();
            Touch.Build((RectTransform)TouchCanvas.transform, s);

            Gamepad = gameObject.AddComponent<GamepadInput>();
            Router = gameObject.AddComponent<InputRouter>();
            Router.touch = Touch;
            Router.gamepad = Gamepad;
            Router.Configure(s);
            Router.OnTick += OnTick;

            var root = (RectTransform)UiCanvas.transform;

            var displayGo = new GameObject("Display");
            displayGo.transform.SetParent(root, false);
            Display = displayGo.AddComponent<InputDisplay>();
            Display.Build(root);

            Menu = gameObject.AddComponent<MainMenu>();
            Menu.Build(root);
            Menu.onInputTest = () => Show(Screen_.InputTest);
            Menu.onSettings = () => Show(Screen_.Settings);
            Menu.onViewer = () => Show(Screen_.Viewer);

            Settings = gameObject.AddComponent<SettingsMenu>();
            Settings.Build(root);
            Settings.onBack = () => Show(Screen_.Main);
            Settings.onEditLayout = () => Show(Screen_.Layout);
            Settings.onChanged = RebuildTouch;

            Viewer = gameObject.AddComponent<CharViewer>();
            Viewer.Build(root);
            Viewer.onBack = () => Show(Screen_.Main);

            Layout = gameObject.AddComponent<LayoutEditor>();
            Layout.Build(root);
            Layout.onClose = () => Show(Screen_.Settings);
            Layout.onSaved = RebuildTouch;

            Show(Screen_.Main);
        }

        /// <summary>Rebuilds the on-screen controls after a settings or layout change.</summary>
        public void RebuildTouch() {
            var s = SettingsStore.Current;
            Loc.Apply(s.language);
            Application.targetFrameRate = s.fpsCap;
            AudioListener.volume = s.masterVolume / 100f;
            Touch.Build((RectTransform)TouchCanvas.transform, s);
            Router.Configure(s);
            Router.allowTouchControls = Current == Screen_.InputTest;
            Touch.SetVisible(Router.allowTouchControls);
        }

        public void Show(Screen_ screen) {
            Current = screen;
            Menu.SetVisible(screen == Screen_.Main);
            Settings.SetVisible(screen == Screen_.Settings);
            Layout.SetVisible(screen == Screen_.Layout);
            Viewer.SetVisible(screen == Screen_.Viewer);
            Display.gameObject.SetActive(screen == Screen_.InputTest);
            bool wantTouch = screen == Screen_.InputTest;
            Router.allowTouchControls = wantTouch;
            Touch.SetVisible(wantTouch);
            Router.paused = screen != Screen_.InputTest;   // button assist is match-only, like Ikemen
            if (screen == Screen_.Layout) Layout.Refresh();
        }

        void OnTick(InputFrame frame) {
            if (Current == Screen_.InputTest) Display.Feed(frame, Router.TickCount);
        }

        void Update() {
            // Android back button: leave the current screen, pause in a match (dev.4+).
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) {
                if (Current == Screen_.Layout) Show(Screen_.Settings);
                else if (Current != Screen_.Main) Show(Screen_.Main);
            }
            if (Current == Screen_.InputTest && Touch != null && !Touch.Visible &&
                SettingsStore.Current.onScreenControls != OnScreenControls.Never &&
                Router.LastDevice == InputDevice.Touch)
                Touch.SetVisible(true);
        }

        /// <summary>Entry point: builds the app in any scene, so no prefab can go missing.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            if (Instance != null) return;
            var go = new GameObject("IKApp");
            go.AddComponent<IKApp>();
        }
    }
}
