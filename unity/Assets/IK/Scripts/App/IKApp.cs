using UnityEngine;
using UnityEngine.UI;
using IK.Core;
using IK.Input;
using IK.Settings;
using IK.UI;

namespace IK.App {
    /// <summary>
    /// Every screen of the app. Since dev.5 the game boots into <see cref="Title"/>; the
    /// dev.1-dev.4 screens (input test, viewer, training room, the dummy fight) live in the
    /// Developer menu (<see cref="Main"/>), reached from Options → Developer.
    /// </summary>
    public enum Screen_ { Main, InputTest, Settings, Layout, Viewer, Training, Fight,
                          Title, Select, Versus, Victory, Continue, Results, Credits }

    /// <summary>
    /// The application shell: builds every screen from code, owns the canvases and the
    /// 60 Hz <see cref="InputRouter"/>, wires the Android back button, and drives the game
    /// modes through <see cref="GameFlow"/> (title → select → VS → fight → victory /
    /// continue / results → ...). The fight itself is <see cref="FightScreen"/>, started
    /// through <see cref="FightLauncher"/>.
    /// </summary>
    public class IKApp : MonoBehaviour {
        public static IKApp Instance { get; private set; }

        public Canvas TouchCanvas { get; private set; }
        public Canvas UiCanvas { get; private set; }
        public TouchControls Touch { get; private set; }
        public InputRouter Router { get; private set; }
        public GamepadInput Gamepad { get; private set; }
        /// <summary>The Developer menu (the dev.1-dev.4 title screen).</summary>
        public MainMenu Menu { get; private set; }
        public SettingsMenu Settings { get; private set; }
        public LayoutEditor Layout { get; private set; }
        public CharViewer Viewer { get; private set; }
        public TrainingScreen Training { get; private set; }
        public FightScreen Fight { get; private set; }
        public InputDisplay Display { get; private set; }
        public TitleScreen Title { get; private set; }
        public SelectScreen Select { get; private set; }
        /// <summary>dev.6 background music.</summary>
        public MusicPlayer Music { get; private set; }
        public VersusScreen Versus { get; private set; }
        public VictoryScreen Victory { get; private set; }
        public ContinueScreen ContinueMenu { get; private set; }
        public ResultsScreen Results { get; private set; }
        public CreditsScreen Credits { get; private set; }
        public GameFlow Flow { get; private set; } = new GameFlow();
        public Screen_ Current { get; private set; } = Screen_.Title;
        /// <summary>True while the fight screen plays a match started by the flow.</summary>
        public bool InFlowMatch { get; private set; }

        Screen_ settingsReturn = Screen_.Title;

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
            Music = gameObject.AddComponent<MusicPlayer>();
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
            Menu.onSettings = () => OpenSettings(Screen_.Main);
            Menu.onViewer = () => Show(Screen_.Viewer);
            Menu.onTraining = () => Show(Screen_.Training);
            Menu.onFight = () => { Fight.ClearMatch(); InFlowMatch = false; Show(Screen_.Fight); };
            Menu.onBack = () => Show(Screen_.Title);

            Settings = gameObject.AddComponent<SettingsMenu>();
            Settings.Build(root);
            Settings.onBack = () => Show(settingsReturn);
            Settings.onEditLayout = () => Show(Screen_.Layout);
            Settings.onDeveloper = () => Show(Screen_.Main);
            Settings.onChanged = RebuildTouch;

            Viewer = gameObject.AddComponent<CharViewer>();
            Viewer.Build(root);
            Viewer.onBack = () => Show(Screen_.Main);

            Training = gameObject.AddComponent<TrainingScreen>();
            Training.Build(root);
            Training.onBack = () => Show(Screen_.Main);

            Fight = gameObject.AddComponent<FightScreen>();
            Fight.Build(root);
            Fight.onBack = () => Show(InFlowMatch ? Screen_.Title : Screen_.Main);

            Layout = gameObject.AddComponent<LayoutEditor>();
            Layout.Build(root);
            Layout.onClose = () => Show(Screen_.Settings);
            Layout.onSaved = RebuildTouch;

            BuildFrontEnd(root);
            Show(Screen_.Title);
        }

        // ------------------------------------------------------------------ front end

        void BuildFrontEnd(RectTransform root) {
            Title = gameObject.AddComponent<TitleScreen>();
            Title.Build(root);
            Title.onSelect = OnTitle;

            Select = gameObject.AddComponent<SelectScreen>();
            Select.Build(root);
            Select.onBack = () => Show(Screen_.Title);
            Select.onDone = (p1, pal1, p2, pal2, stage, level) => {
                if (GameFlow.PicksDifficulty(Flow.Mode)) Flow.Difficulty = level;
                Flow.SetTeams(Select.Teams, Select.TeamSize, Select.P1Partners, Select.P2Partners);
                Go(Flow.Selected(p1, pal1, p2, pal2, stage));
            };

            Versus = gameObject.AddComponent<VersusScreen>();
            Versus.Build(root);
            Versus.onDone = () => Go(Flow.VersusDone());

            Victory = gameObject.AddComponent<VictoryScreen>();
            Victory.Build(root);
            Victory.onDone = () => Go(Flow.VictoryDone());

            ContinueMenu = gameObject.AddComponent<ContinueScreen>();
            ContinueMenu.Build(root);
            ContinueMenu.onAnswer = yes => Go(Flow.ContinueAnswered(yes));

            Results = gameObject.AddComponent<ResultsScreen>();
            Results.Build(root);
            Results.onDone = () => Go(Flow.ResultsDone());

            Credits = gameObject.AddComponent<CreditsScreen>();
            Credits.Build(root);
            Credits.onBack = () => Show(Screen_.Title);
        }

        void OnTitle(string id) {
            switch (id) {
                case "arcade": StartMode(GameMode.Arcade); break;
                case "versus": StartMode(GameMode.Versus); break;
                case "teamarcade": StartMode(GameMode.Arcade, true); break;
                case "teamversus": StartMode(GameMode.Versus, true); break;
                case "timeattack": StartMode(GameMode.TimeAttack); break;
                case "training": StartMode(GameMode.Training); break;
                case "survival": StartMode(GameMode.Survival); break;
                case "watch": StartMode(GameMode.Watch); break;
                case "options": OpenSettings(Screen_.Title); break;
                case "credits": Show(Screen_.Credits); break;
                case "exit": Application.Quit(); break;
            }
        }

        void OpenSettings(Screen_ back) {
            settingsReturn = back;
            Show(Screen_.Settings);
        }

        /// <summary>Starts a game mode from the title: the select screen of that mode.</summary>
        public void StartMode(GameMode mode, bool team = false) {
            var s = SettingsStore.Current;
            Flow.Difficulty = s.difficulty;
            Flow.RoundsToWin = s.roundsToWin;
            Flow.RoundTime = s.roundTime;
            Go(Flow.Start(mode, MotifAssets.Roster, team));
        }

        /// <summary>Moves to the screen of a flow step.</summary>
        public void Go(FlowStep step) {
            switch (step) {
                case FlowStep.Title:
                    InFlowMatch = false;
                    Show(Screen_.Title);
                    break;
                case FlowStep.Select:
                    InFlowMatch = false;
                    Select.Begin(Flow.Mode, Flow.Roster, Flow.Difficulty, Flow.TeamGame);
                    Show(Screen_.Select);
                    break;
                case FlowStep.Versus:
                    Versus.Begin(Flow.Current);
                    Show(Screen_.Versus);
                    break;
                case FlowStep.Fight:
                    StartFight();
                    break;
                case FlowStep.Victory:
                    InFlowMatch = false;
                    Victory.Begin(Flow.Current, Flow.LastResult);
                    Show(Screen_.Victory);
                    break;
                case FlowStep.Continue:
                    InFlowMatch = false;
                    Show(Screen_.Continue);
                    break;
                case FlowStep.GameOver:
                    Results.Begin(ResultsScreen.Kind.GameOver, Flow.Wins, Flow.Current != null ? Flow.Current.Players[0] : null);
                    Show(Screen_.Results);
                    break;
                case FlowStep.WinScreen:
                    InFlowMatch = false;
                    Results.Begin(ResultsScreen.Kind.Win, Flow.Wins, Flow.Current != null ? Flow.Current.Players[0] : null);
                    Show(Screen_.Results);
                    break;
                case FlowStep.TimeAttackResults:
                    InFlowMatch = false;
                    Results.Begin(ResultsScreen.Kind.TimeAttack, Flow.TotalTicks, Flow.Current != null ? Flow.Current.Players[0] : null);
                    Show(Screen_.Results);
                    break;
                case FlowStep.SurvivalResults:
                    InFlowMatch = false;
                    Results.Begin(ResultsScreen.Kind.Survival, Flow.Wins, Flow.Current != null ? Flow.Current.Players[0] : null);
                    Show(Screen_.Results);
                    break;
            }
        }

        void StartFight() {
            bool endedAlready = false;
            InFlowMatch = true;
            bool started = FightLauncher.Start(Fight, Flow.Current, r => {
                endedAlready = true;
                InFlowMatch = false;
                Go(Flow.MatchEnded(r));
            });
            if (!started) { InFlowMatch = false; Go(Flow.MatchEnded(new MatchResult { Aborted = true })); return; }
            if (!endedAlready) Show(Screen_.Fight);
        }

        FrontEndScreen FrontEnd(Screen_ s) {
            switch (s) {
                case Screen_.Title: return Title;
                case Screen_.Select: return Select;
                case Screen_.Versus: return Versus;
                case Screen_.Victory: return Victory;
                case Screen_.Continue: return ContinueMenu;
                case Screen_.Results: return Results;
                case Screen_.Credits: return Credits;
            }
            return null;
        }

        // ------------------------------------------------------------------ screens

        /// <summary>Rebuilds the on-screen controls after a settings or layout change.</summary>
        public void RebuildTouch() {
            var s = SettingsStore.Current;
            Loc.Apply(s.language);
            Application.targetFrameRate = s.fpsCap;
            AudioListener.volume = s.masterVolume / 100f;
            Touch.Build((RectTransform)TouchCanvas.transform, s);
            Router.Configure(s);
            Router.allowTouchControls = Current == Screen_.InputTest || Current == Screen_.Training ||
                                        Current == Screen_.Fight;
            Touch.SetVisible(Router.allowTouchControls);
        }

        public void Show(Screen_ screen) {
            Current = screen;
            Menu.SetVisible(screen == Screen_.Main);
            Settings.SetVisible(screen == Screen_.Settings);
            Layout.SetVisible(screen == Screen_.Layout);
            Viewer.SetVisible(screen == Screen_.Viewer);
            Training.SetVisible(screen == Screen_.Training);
            Fight.SetVisible(screen == Screen_.Fight);
            foreach (var fe in new FrontEndScreen[] { Title, Select, Versus, Victory, ContinueMenu, Results, Credits })
                if (fe != null && FrontEnd(screen) != fe) fe.SetVisible(false);
            var cur = FrontEnd(screen);
            if (cur != null) { cur.SetVisible(true); cur.Root.SetAsLastSibling(); }
            Display.gameObject.SetActive(screen == Screen_.InputTest);
            bool wantTouch = screen == Screen_.InputTest || screen == Screen_.Training ||
                             screen == Screen_.Fight;
            Router.allowTouchControls = wantTouch;
            Touch.SetVisible(wantTouch);
            // button assist runs in a match and on the input-test screen, like Ikemen
            Router.paused = screen != Screen_.InputTest && screen != Screen_.Training &&
                            screen != Screen_.Fight;
            if (screen == Screen_.Layout) Layout.Refresh();
            if (Music != null) Music.Play(TrackFor(screen), screen != Screen_.Versus && screen != Screen_.Victory && screen != Screen_.Continue && screen != Screen_.Results);
        }

        /// <summary>dev.6: the music of each screen (system.def [Music] roles).</summary>
        public string TrackFor(Screen_ screen) {
            switch (screen) {
                case Screen_.Title: case Screen_.Credits: return "title";
                case Screen_.Select: return "select";
                case Screen_.Versus: return "versus";
                case Screen_.Victory: return "winner";
                case Screen_.Continue: return "continue";
                case Screen_.Results:
                    return Results != null && (Results.Mode == ResultsScreen.Kind.Win || Results.Mode == ResultsScreen.Kind.TimeAttack) ? "winner" : "";
                case Screen_.Fight: return MusicPlayer.ForStage(Flow.Current != null ? Flow.Current.StageDef : "kfm.def");
                case Screen_.Training: return "fight1";
                case Screen_.Settings: case Screen_.Layout: case Screen_.Main: return Music != null ? Music.Playing : "";
                default: return "";
            }
        }

        void OnTick(InputFrame frame) {
            if (Current == Screen_.InputTest) Display.Feed(frame, Router.TickCount);
            else if (Current == Screen_.Training) Training.Feed(frame);
            else if (Current == Screen_.Fight) Fight.Feed(frame);
            else {
                var fe = FrontEnd(Current);
                if (fe != null) fe.Feed(frame);
            }
        }

        void Update() {
            // Android back button: Back in menus, pause in a match.
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) Back();
            if ((Current == Screen_.InputTest || Current == Screen_.Training) && Touch != null && !Touch.Visible &&
                SettingsStore.Current.onScreenControls != OnScreenControls.Never &&
                Router.LastDevice == InputDevice.Touch)
                Touch.SetVisible(true);
        }

        /// <summary>What the Android back key does on each screen.</summary>
        public void Back() {
            switch (Current) {
                case Screen_.Title: break;
                case Screen_.Layout: Show(Screen_.Settings); break;
                case Screen_.Settings: Show(settingsReturn); break;
                case Screen_.Main: Show(Screen_.Title); break;
                case Screen_.Fight:
                    if (InFlowMatch || Fight.Setup != null) Fight.TogglePause();
                    else Show(Screen_.Main);
                    break;
                case Screen_.Select:
                case Screen_.Versus:
                case Screen_.Victory:
                case Screen_.Continue:
                case Screen_.Results:
                case Screen_.Credits:
                    FrontEnd(Current).OnMenuKey(MenuKey.Cancel);
                    break;
                default: Show(Screen_.Main); break;
            }
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
