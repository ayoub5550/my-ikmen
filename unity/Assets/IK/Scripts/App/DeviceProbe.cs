using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using IK.Core;
using IK.Settings;
using IK.UI;

namespace IK.App {
    /// <summary>
    /// dev.8 device probe. Runs only when the app is launched by a Firebase Test Lab game-loop test
    /// (intent action <c>com.google.intent.action.TEST_LOOP</c>) or with <c>-ikprobe</c> on a
    /// desktop player. It records what a robo crawl cannot reach: whether the motif art decodes on
    /// the device (title background), a real CPU-vs-CPU match at the normal 60 Hz pacing (stage
    /// background, frame-time spread, logic ticks per frame, fighters stuck in one state), the
    /// settings pages, and the benchmark. Output: <c>persistentDataPath/probe/</c> (probe.log +
    /// PNGs) and "[IK PROBE]" lines in logcat. Quits the app at the end (ends the game loop).
    /// </summary>
    public class DeviceProbe : MonoBehaviour {
        const string TestLoop = "com.google.intent.action.TEST_LOOP";
        string dir;
        /// <summary>True while the probe runs: the Android back key and touches are ignored (a robo crawl taps at random).</summary>
        public static bool Active { get; private set; }
        readonly StringBuilder log = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            if (!Requested()) return;
            var go = new GameObject("IKDeviceProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<DeviceProbe>();
        }

        static bool Requested() {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ikprobe") >= 0) return true;
            if (Application.version.Contains("probe")) return true;   // diagnostic APK flavour (IK_VERSION=...-probe)
#if UNITY_ANDROID && !UNITY_EDITOR
            try {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = activity.Call<AndroidJavaObject>("getIntent")) {
                    return intent != null && intent.Call<string>("getAction") == TestLoop;
                }
            } catch (Exception e) { Debug.Log("[IK PROBE] intent check failed: " + e.Message); }
#endif
            return false;
        }

        void Log(string line) {
            string l = string.Format("{0,7:0.00}s {1}", Time.realtimeSinceStartup, line);
            log.AppendLine(l);
            Debug.Log("[IK PROBE] " + l);
            try { File.WriteAllText(Path.Combine(dir, "probe.log"), log.ToString()); } catch (Exception) { }
        }

        IEnumerator Shot(string name) {
            yield return new WaitForEndOfFrame();
            try {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                Destroy(tex);
            } catch (Exception e) { Log("shot " + name + " failed: " + e.Message); }
        }

        IEnumerator Start() {
            Active = true;
            dir = Path.Combine(Application.persistentDataPath, "probe");
            Directory.CreateDirectory(dir);
            Application.logMessageReceived += (msg, stack, type) => {
                if (type == LogType.Exception || type == LogType.Error)
                    log.AppendLine("  !! " + type + ": " + msg + "\n" + stack);
            };
            Log("device=" + SystemInfo.deviceModel + " gpu=" + SystemInfo.graphicsDeviceName + " api=" +
                SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceVersion + " screen=" + Screen.width + "x" +
                Screen.height + "@" + Screen.currentResolution.refreshRateRatio.value.ToString("0.#") + " version=" + Application.version);
            for (int i = 0; i < 30 && IKApp.Instance == null; i++) yield return null;
            var app = IKApp.Instance;
            if (app == null) { Log("no IKApp"); Quit(); yield break; }
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null) es.enabled = false;              // random robo taps must not drive the menus
            SettingsStore.Current.onScreenControls = IK.Settings.OnScreenControls.Never;   // in memory only
            app.RebuildTouch();

            // 1. title: does the motif art (system.sff, PNG sprites) decode on this device?
            double t0 = PerfMonitor.Now;
            yield return new WaitForSecondsRealtime(1f);
            Log("title after 1 s: sff index " + MotifAssets.SpritesIndexMs.ToString("0") + " ms, bg elements=" +
                (app.Title != null && app.Title.View != null ? app.Title.View.DrawnBackgroundElements : -1) +
                ", sprite decode error=" + (IK.Core.SffFile.LastDecodeError ?? "none"));
            yield return Shot("01-title-1s");
            float waited = 0f;
            while (!MotifAssets.Ready && waited < 30f) { yield return new WaitForSecondsRealtime(0.25f); waited += 0.25f; }
            Log("motif ready=" + MotifAssets.Ready + " after " + (PerfMonitor.Now - t0).ToString("0") + " ms, error=" +
                (MotifAssets.LoadError ?? "none") + ", sprites=" + (MotifAssets.Motif.Sprites != null ? "loaded" : "null"));
            yield return new WaitForSecondsRealtime(1.5f);
            Log("title bg elements=" + (app.Title != null && app.Title.View != null ? app.Title.View.DrawnBackgroundElements : -1));
            yield return Shot("02-title-ready");

            // 2. settings pages
            foreach (var page in new[] { "Controls", "Game", "Audio", "Video", "Language", "About" }) {
                app.OpenSettingsPage(page);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("03-settings-" + page.ToLowerInvariant());
            }
            app.Show(Screen_.Title);
            yield return new WaitForSecondsRealtime(0.5f);

            // 3. a real watch match at the normal pacing (the InputRouter drives it)
            yield return Match(app, "stage0-720.def", "kfm720", "kfm_zss", 40f, "04-match720");
            yield return Match(app, "stage0.def", "kfm", "kfm", 20f, "05-match240");

            // 4. the benchmark (uncapped)
            bool done = false;
            Benchmark.Result res = null;
            Benchmark.Run(app, 1200, r => { res = r; done = true; });
            float w = 0f;
            while (!done && w < 120f) { yield return new WaitForSecondsRealtime(0.5f); w += 0.5f; }
            Log("benchmark " + (res != null ? JsonUtility.ToJson(res) : "failed/timeout"));
            Log("done");
            yield return new WaitForSecondsRealtime(1f);
            Active = false;
            if (es != null) es.enabled = true;
            if (!Application.version.Contains("probe")) Quit();   // a robo run would just relaunch the app
            else app.Show(Screen_.Title);
        }

        IEnumerator Match(IKApp app, string stage, string c1, string c2, float seconds, string tag) {
            var setup = new MatchSetup { Mode = GameMode.Watch, StageDef = stage, RoundsToWin = 2, RoundTime = 99 };
            setup.Players[0].CharGroup = "chars/" + c1; setup.Players[0].CharDef = c1 + ".def"; setup.Players[0].AiLevel = 6; setup.Players[0].Palette = 1;
            setup.Players[1].CharGroup = "chars/" + c2; setup.Players[1].CharDef = c2 + ".def"; setup.Players[1].AiLevel = 6; setup.Players[1].Palette = 2;
            int ends = 0;
            Action<MatchResult> onEnd = null;
            onEnd = r => { ends++; app.Fight.StartMatch(setup, onEnd); };
            double t0 = PerfMonitor.Now;
            app.ShowFightForBenchmark(setup, onEnd);
            var fight = app.Fight;
            Log(tag + " load " + (PerfMonitor.Now - t0).ToString("0") + " ms, error=" + (fight.LoadError ?? "none"));
            if (fight.Engine == null) { app.EndBenchmark(); yield break; }

            int frames = 0, zeroTick = 0, multiTick = 0, lastTicks = fight.Ticks;
            float minDt = 1f, maxDt = 0f, sumDt = 0f;
            int over20 = 0, over34 = 0;
            int[] sameState = new int[2]; int[] lastState = { -1, -1 }; int[] longest = new int[2]; int[] longestState = new int[2];
            float next = 0f, elapsed = 0f, nextShot = 1f; int shot = 0;
            while (elapsed < seconds) {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                elapsed += dt; frames++;
                if (frames > 10) {
                    minDt = Mathf.Min(minDt, dt); maxDt = Mathf.Max(maxDt, dt); sumDt += dt;
                    if (dt > 0.020f) over20++;
                    if (dt > 0.034f) over34++;
                    int d = fight.Ticks - lastTicks;
                    if (d == 0) zeroTick++; else if (d > 1) multiTick++;
                }
                lastTicks = fight.Ticks;
                var e = fight.Engine;
                if (e == null) break;
                for (int i = 0; i < 2; i++) {
                    var f = e.Players[i];
                    if (f == null) continue;
                    if (f.StateNo == lastState[i]) sameState[i]++; else { sameState[i] = 0; lastState[i] = f.StateNo; }
                    if (sameState[i] > longest[i] && f.StateNo != 0) { longest[i] = sameState[i]; longestState[i] = f.StateNo; }
                }
                if (elapsed >= next) {
                    next += 1f;
                    var a = e.Players[0]; var b = e.Players[1];
                    Log(string.Format("{0} t={1} p1 st={2} mv={3} ctrl={4} x={5:0} y={6:0} life={7} | p2 st={8} mv={9} ctrl={10} x={11:0} y={12:0} life={13} | cam={14:0.0},{15:0.0} stageEl={16} sprites={17}",
                        tag, fight.Ticks, a.StateNo, a.Move, a.Ctrl, a.PosX, a.PosY, a.Life, b.StateNo, b.Move, b.Ctrl, b.PosX, b.PosY, b.Life,
                        e.Camera != null ? e.Camera.X : 0f, e.Camera != null ? e.Camera.Y : 0f, fight.StageElements, fight.DrawnSprites));
                }
                if (elapsed >= nextShot && shot < 6) { nextShot += seconds / 6f; yield return Shot(tag + "-" + (shot++)); }
            }
            int n = Mathf.Max(1, frames - 10);
            Log(string.Format("{0} summary frames={1} avgDt={2:0.00}ms min={3:0.00} max={4:0.00} >20ms={5} >34ms={6} zeroTickFrames={7} multiTickFrames={8} matchEnds={9} longestNonIdleState p1={10}@{11}f p2={12}@{13}f",
                tag, frames, sumDt / n * 1000f, minDt * 1000f, maxDt * 1000f, over20, over34, zeroTick, multiTick, ends, longestState[0], longest[0], longestState[1], longest[1]));
            app.EndBenchmark();
            yield return new WaitForSecondsRealtime(0.5f);
        }

        void Quit() {
#if UNITY_ANDROID && !UNITY_EDITOR
            try {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    activity.Call("finish");
            } catch (Exception) { }
#endif
            Application.Quit();
        }
    }
}
