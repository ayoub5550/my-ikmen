using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using IK.Core;
using IK.Input;
using IK.Settings;
using IK.UI;

namespace IK.App {
    /// <summary>
    /// dev.7 performance benchmark (Options → Video → Benchmark, and the batch entry
    /// <c>IK.EditorTools.IKBench.Run</c>). Plays a fixed CPU-vs-CPU match — KFM 720 against
    /// KFM ZSS on the 720p stage, both at AI level 8, rounds without a timer — advancing exactly
    /// one logic tick per rendered frame with the frame-rate cap removed, so the frame rate
    /// measures how much work a frame costs instead of the 60 Hz cap. Records every frame through
    /// <see cref="PerfMonitor"/> and reports averages, percentiles, hitches and allocations.
    /// The result is shown on screen, logged as "[IK BENCH] {json}" and saved to
    /// <c>persistentDataPath/benchmark.json</c>.
    /// </summary>
    public class Benchmark : MonoBehaviour {
        [Serializable]
        public class Result {
            public string version, device, gpu, api, resolution;
            public int refreshRate, frames, warmupFrames, matchRestarts, maxSprites, gcCollections;
            public float loadMs, seconds;
            public float avgFps, low1Fps, avgMs, p50Ms, p95Ms, p99Ms, maxMs;
            public int hitches25, hitches50;
            public float logicAvgMs, logicP99Ms, logicMaxMs, drawAvgMs, drawP99Ms, drawMaxMs;
            public float mainAvgMs, renderAvgMs, gpuAvgMs;
            public float allocKBPerFrame, allocKBMaxFrame, fightAllocKBPerFrame;
            public float monoHeapMB, totalAllocatedMB, textureMB;
            public int textures;
            public int prewarmSprites; public float prewarmMs; public bool prewarmDone;
        }

        public static bool Running { get; private set; }
        public Result Last { get; private set; }
        public Action<Result> onDone;
        public int Frames = 1800;
        public int Warmup = 120;

        RectTransform panel;

        public static Benchmark Run(IKApp app, int frames = 1800, Action<Result> onDone = null) {
            if (Running || app == null) return null;
            var b = app.gameObject.GetComponent<Benchmark>();
            if (b == null) b = app.gameObject.AddComponent<Benchmark>();
            b.Frames = frames;
            b.onDone = onDone;
            b.StartCoroutine(b.Play(app));
            return b;
        }

        public static MatchSetup BenchSetup() {
            var m = new MatchSetup { Mode = GameMode.Watch, StageDef = "stage0-720.def", RoundsToWin = 0, RoundTime = -1 };
            m.Players[0].CharGroup = "chars/kfm720"; m.Players[0].CharDef = "kfm720.def"; m.Players[0].AiLevel = 8; m.Players[0].Palette = 1;
            m.Players[1].CharGroup = "chars/kfm_zss"; m.Players[1].CharDef = "kfm_zss.def"; m.Players[1].AiLevel = 8; m.Players[1].Palette = 2;
            return m;
        }

        IEnumerator Play(IKApp app) {
            Running = true;
            var settings = SettingsStore.Current;
            int savedCap = Application.targetFrameRate;
            var mon = PerfMonitor.Instance;
            var fight = app.Fight;
            int restarts = 0;
            int maxSprites = 0;
            Action<MatchResult> onEnd = null;
            onEnd = r => { restarts++; fight.StartMatch(BenchSetup(), onEnd); };

            double t0 = PerfMonitor.Now;
            app.ShowFightForBenchmark(BenchSetup(), onEnd);
            float loadMs = (float)(PerfMonitor.Now - t0);
            if (fight.Engine == null) {
                Debug.LogError("[IK BENCH] match failed to load: " + fight.LoadError);
                Finish(app, savedCap, null);
                yield break;
            }
            fight.ExternalDrive = true;
            Application.targetFrameRate = -1;      // uncapped (a phone still stops at its refresh rate)
            QualitySettings.vSyncCount = 0;

            for (int i = 0; i < Warmup; i++) { fight.Feed(new InputFrame()); yield return null; }
            var frames = new List<PerfMonitor.Frame>(Frames + 8);
            int gc0 = GC.CollectionCount(0);
            if (mon != null) mon.Recording = frames;
            double start = PerfMonitor.Now;
            yield return null;                      // the first recorded frame is a whole frame
            frames.Clear();
            while (frames.Count < Frames) {
                fight.Feed(new InputFrame());
                if (fight.DrawnSprites > maxSprites) maxSprites = fight.DrawnSprites;
                yield return null;
            }
            if (mon != null) mon.Recording = null;
            double seconds = (PerfMonitor.Now - start) / 1000.0;
            var res = Summarise(frames);
            res.loadMs = loadMs;
            res.seconds = (float)seconds;
            res.warmupFrames = Warmup;
            res.matchRestarts = restarts;
            res.maxSprites = maxSprites;
            res.gcCollections = GC.CollectionCount(0) - gc0;
            res.prewarmSprites = fight.PrewarmedSprites;
            res.prewarmMs = (float)fight.PrewarmMilliseconds;
            res.prewarmDone = fight.PrewarmDone;
            Finish(app, savedCap, res);
        }

        void Finish(IKApp app, int savedCap, Result res) {
            Application.targetFrameRate = savedCap;
            app.Fight.ExternalDrive = false;
            app.EndBenchmark();
            Running = false;
            Last = res;
            if (res != null) {
                string json = JsonUtility.ToJson(res);
                Debug.Log("[IK BENCH] " + json);
                try { File.WriteAllText(Path.Combine(Application.persistentDataPath, "benchmark.json"), JsonUtility.ToJson(res, true)); }
                catch (Exception) { }
                if (onDone == null) ShowResult(app, res);
            }
            onDone?.Invoke(res);
        }

        public static Result Summarise(List<PerfMonitor.Frame> frames) {
            var r = new Result {
                version = Application.version,
                device = SystemInfo.deviceModel,
                gpu = SystemInfo.graphicsDeviceName,
                api = SystemInfo.graphicsDeviceType.ToString(),
                resolution = Screen.width + "x" + Screen.height,
                refreshRate = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value),
                frames = frames.Count,
            };
            int n = frames.Count;
            if (n == 0) return r;
            var ms = new float[n]; var logic = new float[n]; var draw = new float[n];
            double sum = 0, lsum = 0, dsum = 0, msum = 0, rsum = 0, gsum = 0; long asum = 0, amax = 0;
            int mainN = 0, renderN = 0, gpuN = 0; long fsum = 0;
            for (int i = 0; i < n; i++) {
                var f = frames[i];
                ms[i] = f.Ms; logic[i] = f.LogicMs; draw[i] = f.DrawMs;
                sum += f.Ms; lsum += f.LogicMs; dsum += f.DrawMs;
                if (f.MainMs > 0) { msum += f.MainMs; mainN++; }
                if (f.RenderMs > 0) { rsum += f.RenderMs; renderN++; }
                if (f.GpuMs > 0) { gsum += f.GpuMs; gpuN++; }
                if (f.Alloc > 0) { asum += f.Alloc; amax = Math.Max(amax, f.Alloc); }
                fsum += f.FightAlloc;
                if (f.Ms > 25f) r.hitches25++;
                if (f.Ms > 50f) r.hitches50++;
            }
            Array.Sort(ms); Array.Sort(logic); Array.Sort(draw);
            r.avgMs = (float)(sum / n);
            r.avgFps = r.avgMs > 0 ? 1000f / r.avgMs : 0;
            r.p50Ms = Pct(ms, 0.50f); r.p95Ms = Pct(ms, 0.95f); r.p99Ms = Pct(ms, 0.99f); r.maxMs = ms[n - 1];
            // 1 % low: average frame rate of the slowest 1 % of frames
            int worst = Math.Max(1, n / 100);
            double wsum = 0; for (int i = n - worst; i < n; i++) wsum += ms[i];
            r.low1Fps = (float)(1000.0 / (wsum / worst));
            r.logicAvgMs = (float)(lsum / n); r.logicP99Ms = Pct(logic, 0.99f); r.logicMaxMs = logic[n - 1];
            r.drawAvgMs = (float)(dsum / n); r.drawP99Ms = Pct(draw, 0.99f); r.drawMaxMs = draw[n - 1];
            r.mainAvgMs = mainN > 0 ? (float)(msum / mainN) : 0;
            r.renderAvgMs = renderN > 0 ? (float)(rsum / renderN) : 0;
            r.gpuAvgMs = gpuN > 0 ? (float)(gsum / gpuN) : 0;
            r.allocKBPerFrame = (float)(asum / 1024.0 / n);
            r.allocKBMaxFrame = amax / 1024f;
            r.fightAllocKBPerFrame = (float)(fsum / 1024.0 / n);
            r.monoHeapMB = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576f;
            r.totalAllocatedMB = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576f;
            long texBytes = 0; int texCount = 0;
            foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>()) {
                texBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t); texCount++;
            }
            r.textureMB = texBytes / 1048576f;
            r.textures = texCount;
            return r;
        }

        static float Pct(float[] sorted, float p) {
            if (sorted.Length == 0) return 0;
            int i = Mathf.Clamp(Mathf.CeilToInt(p * sorted.Length) - 1, 0, sorted.Length - 1);
            return sorted[i];
        }

        /// <summary>Human readable report (also used by the on-screen panel).</summary>
        public static string Describe(Result r) {
            var sb = new StringBuilder();
            sb.AppendFormat("{0} · {1} · {2} · {3} Hz\n", r.device, r.gpu, r.resolution, r.refreshRate);
            sb.AppendFormat("FPS {0:0} (1% low {1:0}) · frame {2:0.0} ms (p95 {3:0.0}, p99 {4:0.0}, max {5:0})\n",
                            r.avgFps, r.low1Fps, r.avgMs, r.p95Ms, r.p99Ms, r.maxMs);
            sb.AppendFormat("Logic {0:0.00} ms (max {1:0.0}) · Draw {2:0.00} ms (max {3:0.0})\n",
                            r.logicAvgMs, r.logicMaxMs, r.drawAvgMs, r.drawMaxMs);
            if (r.mainAvgMs > 0 || r.gpuAvgMs > 0)
                sb.AppendFormat("CPU main {0:0.0} ms · render {1:0.0} ms · GPU {2:0.0} ms\n", r.mainAvgMs, r.renderAvgMs, r.gpuAvgMs);
            sb.AppendFormat("Hitches >25 ms: {0} · >50 ms: {1} · GC: {2} · alloc {3:0.00} KB/frame (fight {4:0.00})\n",
                            r.hitches25, r.hitches50, r.gcCollections, r.allocKBPerFrame, r.fightAllocKBPerFrame);
            sb.AppendFormat("Memory {0:0} MB · textures {1} ({2:0} MB) · load {3:0} ms · {4} frames",
                            r.totalAllocatedMB, r.textures, r.textureMB, r.loadMs, r.frames);
            return sb.ToString();
        }

        void ShowResult(IKApp app, Result r) {
            if (panel != null) Destroy(panel.gameObject);
            panel = UIKit.Panel(app.UiCanvas.transform, "BenchmarkResult", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                                new Color(0.03f, 0.04f, 0.06f, 0.94f));
            panel.GetComponent<Image>().raycastTarget = true;
            UIKit.Text(panel, "title", new Vector2(0.5f, 1f), new Vector2(0, -70), new Vector2(1100, 70),
                       Loc.T("bench.title"), 44, TextAnchor.MiddleCenter, Skin.Accent);
            var body = UIKit.Text(panel, "body", new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(1180, 380),
                                  Describe(r), 24, TextAnchor.MiddleCenter, Skin.Text);
            body.font = Skin.LabelFont;
            string verdict = r.low1Fps >= 58f ? Loc.T("bench.great") : r.low1Fps >= 45f ? Loc.T("bench.ok") : Loc.T("bench.low");
            UIKit.Text(panel, "verdict", new Vector2(0.5f, 0f), new Vector2(0, 170), new Vector2(1100, 50),
                       verdict, 30, TextAnchor.MiddleCenter, Skin.Highlight);
            UIKit.Button(panel, "OK", new Vector2(0.5f, 0f), new Vector2(0, 80), new Vector2(300, 74),
                         Loc.T("common.ok"), () => { Destroy(panel.gameObject); app.OpenSettingsPage("Video"); }, 28);
        }

        // batch entry: editor (IK.EditorTools.IKBench.Run sets IK_BENCH=1, IK_BENCH_OUT) or the
        // Linux test player (`-ikbench out.json [-ikframes n]`): run, write the JSON, quit.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BenchBoot() {
            string outPath = Arg("-ikbench");
            if (Environment.GetEnvironmentVariable("IK_BENCH") == "1") {
                Environment.SetEnvironmentVariable("IK_BENCH", null);
                outPath = Environment.GetEnvironmentVariable("IK_BENCH_OUT") ?? "Builds/validation/bench.json";
            }
            if (string.IsNullOrEmpty(outPath)) return;
            var go = new GameObject("IKBenchDriver");
            DontDestroyOnLoad(go);
            go.AddComponent<BenchDriver>().outPath = outPath;
        }

        /// <summary>Value after a command-line flag, or null.</summary>
        public static string Arg(string flag) {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == flag) return args[i + 1];
            return null;
        }

        public static void Quit(int code) {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }

        class BenchDriver : MonoBehaviour {
            public string outPath;
            IEnumerator Start() {
                for (int i = 0; i < 10 && IKApp.Instance == null; i++) yield return null;
                yield return null; yield return null;
                SettingsStore.UseFile(Path.Combine(Path.GetTempPath(), "ik-bench-settings.json"));
                SettingsStore.Reset();
                string filter = Environment.GetEnvironmentVariable("IK_BENCH_FILTER") ?? Arg("-ikfilter");
                if (!string.IsNullOrEmpty(filter)) RenderQuality.ApplyFilter((PixelFilter)int.Parse(filter));
                string fr = Environment.GetEnvironmentVariable("IK_BENCH_FRAMES") ?? Arg("-ikframes");
                int frames = int.TryParse(fr, out var n) ? n : 1800;
                bool done = false;
                Run(IKApp.Instance, frames, r => {
                    try {
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
                        File.WriteAllText(outPath, r != null ? JsonUtility.ToJson(r, true) : "{}");
                        if (r != null) Debug.Log("[IK BENCH] " + Describe(r).Replace("\n", " | "));
                    } catch (Exception e) { Debug.LogError("[IK BENCH] write failed: " + e); }
                    done = true;
                });
                while (!done) yield return null;
                Quit(0);
            }
        }
    }
}
