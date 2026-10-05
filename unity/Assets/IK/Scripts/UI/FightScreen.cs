using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using IK.Core;
using IK.Input;

namespace IK.UI {
    /// <summary>
    /// The fight. Since dev.5 it runs any match the front end asks for
    /// (<see cref="StartMatch"/>): any two characters, any stage, CPU players driven by
    /// <see cref="CpuAI"/>, and it draws everything the engine has on the field — players,
    /// helpers, explods, hit sparks, projectiles and afterimages — with MUGEN's blend modes and
    /// palette effects (IK/UIPalFx), the superpause darkening and EnvShake. Sounds come from
    /// each character's .snd and from the screenpack's fight.snd.
    ///
    /// Opened without a setup (dev.4 path, the rendered fixture) it is KFM vs KFM with
    /// player 2 as the training dummy.
    /// </summary>
    public class FightScreen : MonoBehaviour {
        public RectTransform Root { get; private set; }
        public Action onBack;

        /// <summary>Player 1's character (dev.4 API; see <see cref="Characters"/> for both).</summary>
        public MugenCharacter Character { get; private set; }
        public readonly MugenCharacter[] Characters = new MugenCharacter[2];
        public CnsFile States { get; private set; }
        public CmdFile Commands { get; private set; }
        public StageDefinition Stage { get; private set; }
        public FightDef Fight { get; private set; }
        /// <summary>`fight.sff`, the screenpack's HUD artwork.</summary>
        public SffFile FightSprites { get; private set; }
        public FightHud Hud => hud;
        public FightEngine Engine { get; private set; }
        public string LoadError { get; private set; }
        public double LoadMilliseconds { get; private set; }
        public int Ticks { get; private set; }
        public MatchSetup Setup { get; private set; }
        public readonly CpuAI[] Ai = new CpuAI[2];
        public bool PausedByPlayer { get; private set; }
        /// <summary>Sprites drawn in the last frame (rendered checks).</summary>
        public int DrawnSprites { get; private set; }

        /// <summary>What player 2 does in the dev.4 dummy fight / training: 0 stand, 1 guard,
        /// 2 jump, 3 walk towards player 1, 4 CPU.</summary>
        public int DummyMode;

        // shared data, loaded once
        static FightDef sharedFight;
        static SffFile sharedFightSff, fxSff;
        static AirFile fxAir;
        static SndFile fightSnd, commonSnd;

        readonly Dictionary<object, MugenAssetCache> caches = new Dictionary<object, MugenAssetCache>();
        MugenAssetCache hudCache;
        AudioSource oneShot;
        readonly Dictionary<long, AudioSource> channels = new Dictionary<long, AudioSource>();

        RectTransform stageRect, clsnLayer, bgBack, bgFront, spriteLayer, overlayLayer, pausePanel;
        Image floorLine, darken;
        StageRenderer stageRenderer;
        FightHud hud;
        readonly List<Graphic> fallbackHud = new List<Graphic>();
        readonly List<Image> spritePool = new List<Image>();
        readonly List<Image> clsnPool = new List<Image>();
        readonly Image[] lifeFill = new Image[2];
        readonly Image[] lifeMid = new Image[2];
        readonly Image[] powerFill = new Image[2];
        readonly Text[] winLabels = new Text[2];
        readonly List<GameObject> devButtons = new List<GameObject>();
        Text announce, debugLine, timer;
        bool showBoxes;
        Action<MatchResult> onMatchEnd;
        bool resultSent;
        int trainingIdle;
        Shader palShader;

        public float Scale { get; private set; } = 2.6f;
        public float FloorY { get; private set; } = 92f;
        public float ViewWidth { get; private set; } = 320f;
        public float ViewHeight { get; private set; } = 240f;

        static string L(string ar, string en) => Loc.Arabic ? Loc.Shape(ar) : en;

        // ------------------------------------------------------------------ build

        public void Build(RectTransform parent) {
            Root = UIKit.Panel(parent, "Fight", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                               new Color(0.05f, 0.06f, 0.09f, 1f));

            stageRect = UIKit.Panel(Root, "stage", new Vector2(0f, 0f), new Vector2(1f, 1f),
                                    new Vector2(0, 0), new Vector2(0, -60));
            bgBack = UIKit.Panel(stageRect, "bgBack", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            floorLine = UIKit.Image(stageRect, "floor", new Vector2(0.5f, 0f), new Vector2(0, FloorY - 2),
                                    new Vector2(4000, 4), null, new Color(1f, 1f, 1f, 0.18f));
            darken = UIKit.Image(stageRect, "superpauseDarken", new Vector2(0.5f, 0.5f), Vector2.zero,
                                 new Vector2(6000, 4000), null, new Color(0f, 0f, 0f, 0.55f));
            darken.raycastTarget = false;
            darken.enabled = false;
            spriteLayer = UIKit.Panel(stageRect, "sprites", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            bgFront = UIKit.Panel(stageRect, "bgFront", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            overlayLayer = UIKit.Panel(stageRect, "onTop", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            clsnLayer = UIKit.Panel(stageRect, "clsn", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            palShader = Resources.Load<Shader>("shaders/UIPalFx");

            // fallback bars (only when the motif HUD fails to load)
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
                         Loc.T("common.back"), OnBackPressed, 22);
            devButtons.Add(UIKit.Button(Root, "Rematch", new Vector2(1f, 1f), new Vector2(-90, -140), new Vector2(170, 54),
                         Loc.T("fight.rematch"), Rematch, 22).gameObject);
            devButtons.Add(UIKit.Button(Root, "Dummy", new Vector2(1f, 1f), new Vector2(-270, -140), new Vector2(170, 54),
                         Loc.T("fight.dummy"), CycleDummy, 22).gameObject);
            devButtons.Add(UIKit.Button(Root, "Boxes", new Vector2(1f, 1f), new Vector2(-450, -140), new Vector2(150, 54),
                         Loc.T("viewer.boxes"), () => { showBoxes = !showBoxes; Redraw(); }, 22).gameObject);
            UIKit.Button(Root, "Pause", new Vector2(0.5f, 1f), new Vector2(0, -110), new Vector2(110, 48),
                         "II", TogglePause, 24);

            // pause menu
            pausePanel = UIKit.Panel(Root, "PauseMenu", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                                     new Color(0f, 0f, 0f, 0.7f));
            UIKit.Text(pausePanel, "title", new Vector2(0.5f, 0.5f), new Vector2(0, 170), new Vector2(600, 70),
                       L("إيقاف مؤقت", "Paused"), 48, TextAnchor.MiddleCenter, Skin.Accent);
            UIKit.Button(pausePanel, "Resume", new Vector2(0.5f, 0.5f), new Vector2(0, 70), new Vector2(380, 70),
                         L("متابعة", "Resume"), TogglePause, 28);
            UIKit.Button(pausePanel, "Restart", new Vector2(0.5f, 0.5f), new Vector2(0, -15), new Vector2(380, 70),
                         L("إعادة المباراة", "Restart match"), () => { TogglePause(); Rematch(); }, 28);
            UIKit.Button(pausePanel, "Boxes", new Vector2(0.5f, 0.5f), new Vector2(0, -100), new Vector2(380, 70),
                         L("صناديق التصادم", "Collision boxes"), () => { showBoxes = !showBoxes; Redraw(); }, 28);
            UIKit.Button(pausePanel, "Exit", new Vector2(0.5f, 0.5f), new Vector2(0, -185), new Vector2(380, 70),
                         L("الخروج من القتال", "Exit fight"), ExitFight, 28);
            pausePanel.gameObject.SetActive(false);

            oneShot = gameObject.AddComponent<AudioSource>();
            oneShot.playOnAwake = false;

            SetVisible(false);
        }

        void OnBackPressed() {
            if (Setup != null) { TogglePause(); return; }
            onBack?.Invoke();
        }

        public void TogglePause() {
            PausedByPlayer = !PausedByPlayer;
            if (pausePanel != null) {
                pausePanel.gameObject.SetActive(PausedByPlayer);
                pausePanel.SetAsLastSibling();
            }
        }

        void ExitFight() {
            PausedByPlayer = false;
            if (pausePanel != null) pausePanel.gameObject.SetActive(false);
            StopAllSounds();
            if (onMatchEnd != null && !resultSent) {
                resultSent = true;
                var cb = onMatchEnd;
                onMatchEnd = null;
                cb(new MatchResult { Aborted = true, Ticks = Ticks, RoundsWon = new[] { Engine?.Wins[0] ?? 0, Engine?.Wins[1] ?? 0 } });
            } else onBack?.Invoke();
        }

        // ------------------------------------------------------------------ loading

        static void LoadShared(IResourceSource data) {
            if (data == null) return;
            if (sharedFight == null) {
                try {
                    var bytes = data.Read("fight.def");
                    if (bytes != null) sharedFight = FightDef.Load(bytes, data);
                } catch (Exception e) { UnityEngine.Debug.LogWarning("[IK] fight.def load failed: " + e.Message); }
            }
            if (fxAir == null) {
                try {
                    var air = data.Read("fightfx.air");
                    var sff = data.Read("fightfx.sff");
                    if (air != null && sff != null) { fxAir = AirFile.Parse(air); fxSff = SffFile.Load(sff, false); }
                } catch (Exception e) { UnityEngine.Debug.LogWarning("[IK] fightfx load failed: " + e.Message); }
            }
            if (fightSnd == null) {
                try { var b = data.Read("fight.snd"); if (b != null) fightSnd = SndFile.Load(b); }
                catch (Exception e) { UnityEngine.Debug.LogWarning("[IK] fight.snd load failed: " + e.Message); }
            }
            if (commonSnd == null) {
                try { var b = data.Read("common.snd"); if (b != null) commonSnd = SndFile.Load(b); }
                catch (Exception) { }
            }
        }

        class LoadedChar { public MugenCharacter Chr; public CnsFile States; public CmdFile Cmd; }
        static readonly Dictionary<string, LoadedChar> charCache = new Dictionary<string, LoadedChar>();

        static LoadedChar LoadChar(IResourceSource src, string key, string defFile) {
            LoadedChar lc;
            if (charCache.TryGetValue(key, out lc)) return lc;
            var chr = MugenCharacter.Load(src, defFile);
            var states = CnsFile.Parse(src.Read(chr.CnsFile));
            // extra state files ([Files] st, st1..st9, stcommon) after the main one
            var files = chr.Def["files"];
            if (files != null) {
                foreach (var k in new[] { "st", "st1", "st2", "st3", "st4", "st5", "st6", "st7", "st8", "st9" }) {
                    string f = MugenDef.Unquote(files.Get(k));
                    if (string.IsNullOrEmpty(f) || string.Equals(f, chr.CnsFile, StringComparison.OrdinalIgnoreCase)) continue;
                    var b = src.Read(f);
                    if (b != null) { var extra = CnsFile.Parse(b); foreach (var s in extra.States) if (states.Get(s.No) == null) { states.States.Add(s); states.ByNumber[s.No] = s; } }
                }
            }
            var cmd = CmdFile.Parse(src.Read(chr.CmdFile));
            states.Merge(cmd.States);
            lc = new LoadedChar { Chr = chr, States = states, Cmd = cmd };
            charCache[key] = lc;
            return lc;
        }

        /// <summary>
        /// dev.4 entry point: KFM vs KFM with the training dummy. The rendered fixture passes
        /// disk sources; on the device everything comes from Resources.
        /// </summary>
        public bool EnsureLoaded(IResourceSource charSource = null, IResourceSource stageSource = null,
                                 IResourceSource dataSource = null, string defFile = "kfm.def",
                                 string stageFile = "kfm.def") {
            if (Engine != null || LoadError != null) return Engine != null;
            var src = charSource ?? new ResourcesSource("chars/kfm");
            return Load(new[] { src, src }, new[] { "kfm:" + defFile + ":" + (charSource == null ? "res" : "disk"), "kfm:" + defFile + ":" + (charSource == null ? "res" : "disk") },
                        new[] { defFile, defFile }, stageSource, stageFile, dataSource, null);
        }

        bool Load(IResourceSource[] charSrc, string[] keys, string[] defs, IResourceSource stageSource, string stageFile,
                  IResourceSource dataSource, MatchSetup setup) {
            var watch = Stopwatch.StartNew();
            LoadError = null;
            try {
                ClearDrawing();
                var loaded = new LoadedChar[2];
                for (int i = 0; i < 2; i++) {
                    loaded[i] = LoadChar(charSrc[i], keys[i], defs[i]);
                    Characters[i] = loaded[i].Chr;
                }
                Character = Characters[0];
                States = loaded[0].States;
                Commands = loaded[0].Cmd;

                Stage = null;
                if (stageSource != null) {
                    try { Stage = StageDefinition.Load(stageSource, stageFile, true); }
                    catch (Exception e) { UnityEngine.Debug.LogWarning("[IK] stage load failed: " + e.Message); }
                }
                LoadShared(dataSource);
                Fight = sharedFight;

                if (stageRenderer != null) { stageRenderer.Dispose(); stageRenderer = null; }
                if (Stage != null && Stage.Sprites != null && bgBack != null) {
                    var stageCache = new MugenAssetCache();
                    caches[Stage] = stageCache;
                    stageRenderer = new StageRenderer(bgBack, bgFront, Stage, stageCache);
                }

                if (hud == null && Fight != null) {
                    try {
                        if (sharedFightSff == null) {
                            var sffBytes = Fight.ReadSffBytes();
                            if (sffBytes != null) sharedFightSff = SffFile.Load(sffBytes, false);
                        }
                        FightSprites = sharedFightSff;
                        if (FightSprites != null) {
                            hudCache = hudCache ?? new MugenAssetCache();
                            hud = new FightHud(Root, Fight, FightSprites, hudCache, 1f);
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
                foreach (var g in fallbackHud) if (g != null) g.enabled = hud == null;

                var p1 = new Fighter(loaded[0].Chr, loaded[0].States, loaded[0].Cmd);
                var p2 = new Fighter(loaded[1].Chr, loaded[1].States, loaded[1].Cmd);
                p1.PaletteNo = setup != null ? Math.Max(1, setup.Players[0].Palette) : 1;
                p2.PaletteNo = setup != null ? Math.Max(1, setup.Players[1].Palette) : 2;
                if (setup == null || (keys[0] == keys[1] && p1.PaletteNo == p2.PaletteNo)) p2.PaletteNo = p1.PaletteNo == 1 ? 2 : 1;
                if (setup != null) { p1.AiLevel = setup.Players[0].AiLevel; p2.AiLevel = setup.Players[1].AiLevel; }
                if (setup != null) {
                    // the engine reads these in its constructor (StartRound)
                }
                Engine = new FightEngine(p1, p2, Stage, Fight);
                Engine.FxAir = fxAir;
                Engine.FxSff = fxSff;
                if (setup != null) {
                    Engine.MatchNo = setup.MatchNo;
                    if (setup.Mode == GameMode.Training || setup.RoundsToWin <= 0) {
                        Engine.RoundsToWin = 99;
                        Engine.TimerCount = -1;
                    } else {
                        Engine.RoundsToWin = setup.RoundsToWin;
                        Engine.TimerCount = setup.RoundTime;
                    }
                    Engine.StartRound(1);
                    if (setup.P1StartLife.HasValue) p1.Life = Mathf.Clamp(setup.P1StartLife.Value, 1, p1.LifeMax);
                }
                for (int i = 0; i < 2; i++) {
                    var f = Engine.Players[i];
                    Ai[i] = f.AiLevel > 0 ? new CpuAI(f, f.AiLevel, 11 + i) : null;
                }
                Ticks = 0;
                resultSent = false;
            } catch (Exception e) {
                LoadError = e.Message;
                Engine = null;
                UnityEngine.Debug.LogError("[IK] fight load failed: " + e);
            }
            watch.Stop();
            LoadMilliseconds = watch.Elapsed.TotalMilliseconds;
            return Engine != null;
        }

        /// <summary>
        /// Starts a match chosen by the front end. Calls <paramref name="onEnd"/> once, after
        /// the winner's pose (or when the player exits from the pause menu, with Aborted).
        /// </summary>
        public void StartMatch(MatchSetup setup, Action<MatchResult> onEnd) {
            Setup = setup;
            onMatchEnd = onEnd;
            PausedByPlayer = false;
            if (pausePanel != null) pausePanel.gameObject.SetActive(false);
            StopAllSounds();
            var srcs = new IResourceSource[2];
            var keys = new string[2];
            var defs = new string[2];
            for (int i = 0; i < 2; i++) {
                var p = setup.Players[i];
                srcs[i] = new ResourcesSource(p.CharGroup);
                keys[i] = p.CharGroup + ":" + p.CharDef;
                defs[i] = p.CharDef;
            }
            Engine = null;
            LoadError = null;
            Load(srcs, keys, defs, new ResourcesSource("stages"), setup.StageDef, new ResourcesSource("data"), setup);
            DummyMode = setup.Players[1].AiLevel > 0 ? 4 : 0;
            bool training = setup.Mode == GameMode.Training;
            foreach (var b in devButtons) b.SetActive(training);
            Root.gameObject.SetActive(true);
            if (hud != null) hud.SetVisible(true);
            Redraw();
            if (Engine == null && onEnd != null) {
                resultSent = true;
                onMatchEnd = null;
                onEnd(new MatchResult { Aborted = true });
            }
        }

        public void Rematch() {
            if (Engine == null) return;
            Engine.Wins[0] = Engine.Wins[1] = 0;
            Engine.MatchOver = false;
            Engine.StartRound(1);
            if (Setup != null && Setup.P1StartLife.HasValue) Engine.P1.Life = Mathf.Clamp(Setup.P1StartLife.Value, 1, Engine.P1.LifeMax);
            Ticks = 0;
            resultSent = false;
            Redraw();
        }

        public void CycleDummy() {
            DummyMode = (DummyMode + 1) % 5;
            if (DummyMode == 4 && Engine != null && Ai[1] == null) Ai[1] = new CpuAI(Engine.P2, 4, 12);
        }

        // ------------------------------------------------------------------ logic

        /// <summary>One logic tick, called by <see cref="InputRouter"/> at 60 Hz.</summary>
        public void Feed(InputFrame frame) {
            if (Engine == null || PausedByPlayer) return;
            var keys = new CmdKey[2];
            for (int i = 0; i < 2; i++) {
                var f = Engine.Players[i];
                if (Ai[i] != null && (i == 0 || DummyMode == 4 || Setup != null && Setup.Players[1].AiLevel > 0))
                    keys[i] = Ai[i].Tick(f, Engine.Players[1 - i], Engine);
                else if (i == 0) keys[i] = TrainingScreen.ToCmdKey(frame, f.Facing);
                else keys[i] = DummyInput();
            }
            Engine.Tick(keys[0], keys[1]);
            Ticks++;
            if (Setup != null && Setup.Mode == GameMode.Training) TrainingRefill();
            PlaySounds();
            if (Engine.Vibrate > 0 && Ticks % 30 == 0) {
#if UNITY_ANDROID && !UNITY_EDITOR
                Handheld.Vibrate();
#endif
            }
            Redraw();
            CheckMatchEnd();
        }

        void TrainingRefill() {
            for (int i = 0; i < 2; i++) {
                var f = Engine.Players[i];
                if (f.Move == MoveType.BeingHit) { trainingIdle = 0; continue; }
            }
            if (++trainingIdle > 90) {
                foreach (var f in Engine.Players) { f.Life = f.LifeMax; f.KO = false; }
                trainingIdle = 0;
            }
        }

        void CheckMatchEnd() {
            if (resultSent || onMatchEnd == null) return;
            if (!Engine.MatchOver || Engine.State != RoundState.WinPose || Engine.StateTime < Engine.WinPoseTime) return;
            resultSent = true;
            var r = new MatchResult {
                Winner = Engine.Wins[0] == Engine.Wins[1] ? -1 : Engine.Wins[0] > Engine.Wins[1] ? 0 : 1,
                RoundsWon = new[] { Engine.Wins[0], Engine.Wins[1] },
                P1LifeLeft = Engine.P1.Life,
                Ticks = Ticks,
            };
            var cb = onMatchEnd;
            onMatchEnd = null;
            StopAllSounds();
            cb(r);
        }

        /// <summary>The training dummy: stand, guard, jump or walk towards player 1.</summary>
        CmdKey DummyInput() {
            switch (DummyMode) {
                case 1: return CmdKey.B;
                case 2: return (Ticks % 40) < 3 ? CmdKey.U : CmdKey.None;
                case 3: return CmdKey.F;
                default: return CmdKey.None;
            }
        }

        // ------------------------------------------------------------------ sound

        MugenAssetCache CacheFor(object key) {
            if (key == null) return null;
            MugenAssetCache c;
            if (!caches.TryGetValue(key, out c)) { c = new MugenAssetCache(); caches[key] = c; }
            return c;
        }

        void PlaySounds() {
            float vol = IK.Settings.SettingsStore.Current.sfxVolume / 100f;
            foreach (var ch in Engine.StopChannels) {
                foreach (var kv in channels) {
                    long owner = kv.Key >> 16;
                    if (owner == (ch.Key != null ? ch.Key.Id : 0) && (ch.Value < 0 || (kv.Key & 0xffff) == ch.Value) && kv.Value != null) kv.Value.Stop();
                }
            }
            foreach (var s in Engine.Sounds) {
                SndFile snd = s.Common ? (fightSnd ?? commonSnd) : (s.Owner != null && s.Owner.Character != null ? s.Owner.Character.Snd : null);
                if (snd == null) continue;
                var entry = snd.Get(s.Group, s.Index);
                if (entry == null && s.Common && commonSnd != null) { snd = commonSnd; entry = snd.Get(s.Group, s.Index); }
                if (entry == null) continue;
                var clip = CacheFor(snd).ClipFor(entry);
                if (clip == null) continue;
                if (s.Channel >= 0 && s.Owner != null) {
                    long key = ((long)s.Owner.Id << 16) | (uint)(s.Channel & 0xffff);
                    AudioSource src;
                    if (!channels.TryGetValue(key, out src) || src == null) {
                        src = gameObject.AddComponent<AudioSource>();
                        src.playOnAwake = false;
                        channels[key] = src;
                    }
                    if (s.LowPriority && src.isPlaying) continue;
                    src.clip = clip;
                    src.volume = Mathf.Clamp01(s.Volume) * vol;
                    src.pitch = s.FreqMul > 0 ? s.FreqMul : 1f;
                    src.panStereo = Mathf.Clamp(s.Pan, -1f, 1f);
                    src.loop = s.Loop;
                    src.Play();
                } else {
                    oneShot.PlayOneShot(clip, Mathf.Clamp01(s.Volume) * vol);
                }
            }
        }

        void StopAllSounds() {
            foreach (var kv in channels) if (kv.Value != null) kv.Value.Stop();
            if (oneShot != null) oneShot.Stop();
        }

        // ------------------------------------------------------------------ drawing

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

        void ClearDrawing() {
            foreach (var img in spritePool) if (img != null) img.gameObject.SetActive(false);
        }

        public void Redraw() {
            if (Engine == null) return;
            UpdateViewMetrics();
            float camera = Engine.Camera != null ? Engine.Camera.X : 0f;
            float cameraY = Engine.Camera != null ? Engine.Camera.Y : 0f;
            float shake = Engine.ShakeY * Scale;
            if (stageRenderer != null) {
                stageRenderer.Scale = Scale;
                stageRenderer.FloorY = FloorY + shake;
                stageRenderer.Draw(camera, cameraY, ViewWidth, ViewHeight);
            }
            darken.enabled = Engine.SuperPauseTime > 0 && Engine.SuperPauseDarken;

            int used = 0;
            foreach (var item in Engine.DrawList()) {
                var f = item as Fighter;
                if (f != null) {
                    // afterimage trail first, behind the character
                    if (f.After.Frames.Count > 0) {
                        foreach (var kv in f.After.Visible()) {
                            var ai = kv.Value;
                            float k = 1f - kv.Key / (float)Mathf.Max(2, f.After.Length / Mathf.Max(1, f.After.FrameGap) + 1);
                            var fx = AfterImageFx(f.After, kv.Key);
                            used = DrawSprite(used, spriteLayer, SpriteOwnerChar(f), PaletteOf(f), ai.Frame, ai.PosX, ai.PosY,
                                              ai.Facing, 1, 1f, 1f, ai.Angle, f.After.Trans, (int)(200 * k), 255, fx, camera, false);
                        }
                    }
                    var frame = f.Anim != null ? f.Anim.CurrentFrame : null;
                    var pfx = CombinedFx(f.PalFx, f != Engine.SuperPauseOwner);
                    TransType tt = f.TransOn ? f.TransMode : TransType.Default;
                    used = DrawSprite(used, spriteLayer, SpriteOwnerChar(f), PaletteOf(f), frame,
                                      f.PosX + f.DrawOffsetX * f.Facing, f.PosY + f.DrawOffsetY, f.Facing, 1,
                                      f.AngleDrawOn ? f.DrawScaleX : 1f, f.AngleDrawOn ? f.DrawScaleY : 1f,
                                      f.AngleDrawOn ? f.DrawAngle : 0f, tt, f.TransSrc, f.TransDst, pfx, camera, false);
                    continue;
                }
                var e = item as Explod;
                if (e != null) {
                    var frame = e.Anim != null ? e.Anim.CurrentFrame : null;
                    var chr = e.Source == SpriteSource.FightFx ? null : SpriteOwnerChar(e.Owner);
                    var pfx = CombinedFx(e.PalFx.Active ? e.PalFx : (e.OwnPal || e.Owner == null ? null : e.Owner.PalFx), !e.IsSystem && e.Owner != Engine.SuperPauseOwner);
                    float x = e.ScreenSpace ? e.PosX + camera : e.PosX;
                    used = DrawSprite(used, e.OnTop ? overlayLayer : spriteLayer, chr, chr != null ? PaletteOf(e.Owner) : null, frame,
                                      x, e.PosY, e.Facing, e.VFacing, e.ScaleX, e.ScaleY, e.Angle, e.Trans, e.AlphaSrc, e.AlphaDst,
                                      pfx, camera, e.ScreenSpace);
                    continue;
                }
                var p = item as Projectile;
                if (p != null) {
                    var frame = p.Anim != null ? p.Anim.CurrentFrame : null;
                    var src = p.State == Projectile.Phase.Flying ? p.Source : p.State == Projectile.Phase.Hit ? p.HitSource :
                              p.State == Projectile.Phase.Canceled ? p.CancelSource : p.RemSource;
                    var chr = src == SpriteSource.FightFx ? null : SpriteOwnerChar(p.Owner);
                    used = DrawSprite(used, spriteLayer, chr, chr != null ? PaletteOf(p.Owner) : null, frame, p.PosX, p.PosY,
                                      p.Facing, 1, p.ScaleX, p.ScaleY, 0f, TransType.Default, 255, 0,
                                      CombinedFx(p.PalFx.Active ? p.PalFx : null, true), camera, false);
                }
            }
            for (int i = used; i < spritePool.Count; i++) if (spritePool[i].gameObject.activeSelf) spritePool[i].gameObject.SetActive(false);
            DrawnSprites = used;

            DrawBoxes(camera);
            if (hud != null) hud.Draw(Engine);
            DrawBars();
            DrawAnnouncement();
        }

        MugenCharacter SpriteOwnerChar(Fighter f) {
            if (f == null) return null;
            if (f.AnimFromOwner != null && f.AnimFromOwner.Character != null) return f.AnimFromOwner.Character;
            return f.Character;
        }

        static uint[] PaletteOf(Fighter f) {
            var chr = f != null ? f.Character : null;
            if (chr == null || chr.Sff == null || chr.Sff.Palettes.Count == 0) return null;
            int pal = Mathf.Clamp((f.Root.PaletteNo) - 1, 0, chr.Sff.Palettes.Count - 1);
            return chr.Sff.Palettes[pal];
        }

        readonly float[] fxAdd = new float[3], fxMul = new float[3], fxAdd2 = new float[3], fxMul2 = new float[3];

        struct Fx { public Vector4 Add, Mul; public float Sat; public bool Invert; public bool Any; }

        Fx CombinedFx(PalFx own, bool darkened) {
            var r = new Fx { Add = Vector4.zero, Mul = Vector4.one, Sat = 1f };
            float sat; bool inv;
            if (own != null && own.Active) {
                own.Current(fxAdd, fxMul, out sat, out inv);
                r.Add = new Vector4(fxAdd[0], fxAdd[1], fxAdd[2], 0);
                r.Mul = new Vector4(fxMul[0], fxMul[1], fxMul[2], 1);
                r.Sat = sat; r.Invert = inv; r.Any = true;
            }
            if (Engine.AllPalFx.Active) {
                Engine.AllPalFx.Current(fxAdd2, fxMul2, out sat, out inv);
                r.Add += new Vector4(fxAdd2[0], fxAdd2[1], fxAdd2[2], 0);
                r.Mul = Vector4.Scale(r.Mul, new Vector4(fxMul2[0], fxMul2[1], fxMul2[2], 1));
                r.Sat *= sat; r.Invert ^= inv; r.Any = true;
            }
            if (darkened && Engine.SuperPauseTime > 0 && Engine.SuperPauseDarken) {
                r.Mul = Vector4.Scale(r.Mul, new Vector4(0.55f, 0.55f, 0.55f, 1)); r.Any = true;
            }
            return r;
        }

        static Fx AfterImageFx(AfterImage a, int step) {
            var r = new Fx { Sat = 1f, Any = true };
            float m0 = Mathf.Pow(a.PalMulF[0], step), m1 = Mathf.Pow(a.PalMulF[1], step), m2 = Mathf.Pow(a.PalMulF[2], step);
            r.Add = new Vector4(a.PalBright[0] / 256f + a.PalAdd[0] * step / 256f,
                                a.PalBright[1] / 256f + a.PalAdd[1] * step / 256f,
                                a.PalBright[2] / 256f + a.PalAdd[2] * step / 256f, 0);
            r.Mul = new Vector4(m0, m1, m2, 1);
            return r;
        }

        int DrawSprite(int used, RectTransform layer, MugenCharacter chr, uint[] pal, AnimFrame frame,
                       float wx, float wy, int facing, int vfacing, float sx, float sy, float angle,
                       TransType trans, int alphaSrc, int alphaDst, Fx fx, float camera, bool screenSpace) {
            if (frame == null || frame.Group < 0) return used;
            SffFile sff = chr != null ? chr.Sff : fxSff;
            if (sff == null) return used;
            var spr = sff.Get(frame.Group, frame.Number);
            if (spr == null) return used;
            if (chr == null || spr.Raw || spr.OwnPalette != null) pal = null;
            var unitySprite = CacheFor(sff).SpriteFor(sff, spr, pal);
            if (unitySprite == null) return used;

            Image img;
            if (used < spritePool.Count) img = spritePool[used];
            else {
                var go = new GameObject("spr" + used, typeof(RectTransform));
                img = go.AddComponent<Image>();
                img.raycastTarget = false;
                var rt0 = (RectTransform)go.transform;
                rt0.anchorMin = rt0.anchorMax = new Vector2(0.5f, 0f);
                if (palShader != null) img.material = new Material(palShader);
                spritePool.Add(img);
            }
            if (img.transform.parent != layer) img.transform.SetParent(layer, false);
            img.transform.SetAsLastSibling();
            img.gameObject.SetActive(true);
            img.sprite = unitySprite;
            var rt = img.rectTransform;
            rt.sizeDelta = new Vector2(spr.Width * Scale, spr.Height * Scale);
            rt.pivot = new Vector2(spr.Width > 0 ? (float)spr.X / spr.Width : 0.5f,
                                   spr.Height > 0 ? 1f - (float)spr.Y / spr.Height : 0.5f);
            float shake = Engine.ShakeY * Scale;
            float x = (wx - camera + frame.Xoffset * facing * sx) * Scale;
            float y = screenSpace ? (stageRect.rect.height - (wy + frame.Yoffset * sy) * Scale)
                                  : FloorY + shake - (wy + frame.Yoffset * sy) * Scale;
            rt.anchoredPosition = new Vector2(x, y);
            rt.localScale = new Vector3(frame.Hscale * Mathf.Abs(frame.Xscale) * facing * sx,
                                        frame.Vscale * Mathf.Abs(frame.Yscale) * vfacing * sy, 1f);
            float ang = angle + frame.Angle;
            rt.localRotation = ang != 0f ? Quaternion.Euler(0, 0, ang * facing) : Quaternion.identity;

            // blend mode: the element's own trans unless a controller overrides it
            var t = trans != TransType.Default ? trans : frame.Trans;
            float alpha = 1f;
            int src = 5, dst = 10, op = 0; bool premul = false;   // SrcAlpha, OneMinusSrcAlpha, Add
            switch (t) {
                case TransType.Add:
                case TransType.Add1: {
                    int s = trans != TransType.Default ? alphaSrc : frame.SrcAlpha;
                    int d = trans != TransType.Default ? alphaDst : frame.DstAlpha;
                    if (t == TransType.Add1) { s = 255; d = 128; }
                    if (s >= 255 && (d == 0 || d >= 255)) { src = 1; dst = 1; premul = true; alpha = 1f; }   // One, One
                    else if (d >= 200 || d == 0) { src = 1; dst = 1; premul = true; alpha = Mathf.Clamp01(s / 256f); }
                    else { src = 5; dst = 10; alpha = Mathf.Clamp01(s / 256f); }
                    break;
                }
                case TransType.Sub: src = 1; dst = 1; op = 2; premul = true; break;   // ReverseSubtract
                case TransType.None: break;
                default:
                    if (trans != TransType.Default && alphaSrc < 255) alpha = Mathf.Clamp01(alphaSrc / 256f);
                    break;
            }
            img.color = new Color(1f, 1f, 1f, alpha);
            var mat = img.material;
            if (mat != null && mat.shader == palShader) {
                mat.SetFloat("_SrcBlend", src);
                mat.SetFloat("_DstBlend", dst);
                mat.SetFloat("_BlendOp", op);
                mat.SetFloat("_Premul", premul ? 1f : 0f);
                mat.SetVector("_Add", fx.Any ? fx.Add : Vector4.zero);
                mat.SetVector("_Mul", fx.Any ? fx.Mul : Vector4.one);
                mat.SetFloat("_Sat", fx.Any ? fx.Sat : 1f);
                mat.SetFloat("_Invert", fx.Any && fx.Invert ? 1f : 0f);
            }
            return used + 1;
        }

        void DrawBars() {
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
            if (debugLine != null) {
                bool dev = Setup == null || Setup.Mode == GameMode.Training;
                debugLine.enabled = dev;
                if (dev)
                    UIKit.SetText(debugLine, string.Format(
                        "P1 {0}/{1} st{2} | P2 {3}/{4} st{5} | round {6} ({7}) | hits {8} | objs {9}",
                        Engine.P1.Life, Engine.P1.LifeMax, Engine.P1.StateNo,
                        Engine.P2.Life, Engine.P2.LifeMax, Engine.P2.StateNo,
                        Engine.RoundNo, Engine.State, Engine.P1.HitCount,
                        Engine.Chars.Count + Engine.Explods.Count + Engine.Projectiles.Count));
            }
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
                foreach (var f in Engine.Chars) {
                    if (f == null || f.Destroyed) continue;
                    used = DrawBoxList(f.WorldClsn(2), new Color(0.3f, 0.6f, 1f, 0.35f), camera, used);
                    used = DrawBoxList(f.WorldClsn(1), new Color(1f, 0.3f, 0.3f, 0.45f), camera, used);
                }
                foreach (var p in Engine.Projectiles)
                    used = DrawBoxList(FightEngine.BoxesOf(p.Anim, 1, p.PosX, p.PosY, p.Facing, p.ScaleX, p.ScaleY),
                                       new Color(1f, 0.5f, 0.1f, 0.45f), camera, used);
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
            if (!on) { StopAllSounds(); return; }
            if (Setup == null) {
                foreach (var b in devButtons) b.SetActive(true);
                EnsureLoaded(null, new ResourcesSource("stages"), new ResourcesSource("data"));
            }
            Redraw();
        }

        /// <summary>Leaves a front-end match so the dev.4 dummy fight can be opened again.</summary>
        public void ClearMatch() {
            Setup = null;
            onMatchEnd = null;
            Engine = null;
            LoadError = null;
            Ai[0] = Ai[1] = null;
        }

        public bool Visible => Root != null && Root.gameObject.activeSelf;

        void OnDestroy() {
            if (hud != null) hud.Dispose();
            if (stageRenderer != null) stageRenderer.Dispose();
            foreach (var c in caches.Values) c.Dispose();
            if (hudCache != null) hudCache.Dispose();
        }

        public void ShowBoxesForTests(bool on) { showBoxes = on; Redraw(); }
    }
}
