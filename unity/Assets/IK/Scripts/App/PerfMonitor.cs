using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;
using IK.Settings;
using IK.UI;

namespace IK.App {
    /// <summary>
    /// dev.7 frame statistics. Runs first every frame (execution order -1000) and closes the
    /// previous frame: wall time, logic and draw milliseconds reported by the fight
    /// (<see cref="AddLogic"/> / <see cref="AddDraw"/>), managed allocations
    /// ("GC Allocated In Frame") and, where the device supports it, the CPU main / render
    /// thread and GPU times from <see cref="FrameTimingManager"/>.
    /// Feeds the FPS overlay (Options → Video → Show FPS) and the benchmark.
    /// Allocation free in the steady state.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class PerfMonitor : MonoBehaviour {
        public static PerfMonitor Instance { get; private set; }

        public struct Frame {
            public float Ms, LogicMs, DrawMs, MainMs, RenderMs, GpuMs;
            public long Alloc;
            /// <summary>Managed heap growth inside the fight's own code this frame (logic + draw).</summary>
            public long FightAlloc;
            public int Ticks;
        }

        static double logicAcc, drawAcc;
        static int tickAcc;
        static long fightAllocAcc;
        /// <summary>Managed heap bytes the fight code added (negative deltas = a GC ran; ignored).</summary>
        public static void AddFightAlloc(long bytes) { if (bytes > 0) fightAllocAcc += bytes; }
        public static long HeapUsed => UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
        static readonly Stopwatch watch = Stopwatch.StartNew();

        /// <summary>Milliseconds since start-up (high resolution).</summary>
        public static double Now => watch.Elapsed.TotalMilliseconds;
        public static void AddLogic(double ms) { logicAcc += ms; tickAcc++; }
        public static void AddDraw(double ms) { drawAcc += ms; }

        /// <summary>Last 240 frames (overlay).</summary>
        public readonly Frame[] Ring = new Frame[240];
        public int RingCount { get; private set; }
        int ringPos;

        /// <summary>When set, every frame is also appended here (benchmark).</summary>
        public List<Frame> Recording;
        public int GcCount0 { get; private set; }

        ProfilerRecorder allocRecorder;
        readonly FrameTiming[] timings = new FrameTiming[1];
        bool timingSupported;

        Text overlay;
        RectTransform overlayRoot;
        float overlayTimer;
        double lastFrameStart;

        void Awake() {
            Instance = this;
            try { allocRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"); }
            catch (Exception) { }
            timingSupported = FrameTimingManager.IsFeatureEnabled();
            lastFrameStart = Now;
        }

        void OnDestroy() {
            if (allocRecorder.Valid) allocRecorder.Dispose();
            if (Instance == this) Instance = null;
        }

        void Update() {
            double now = Now;
            var f = new Frame {
                Ms = (float)(now - lastFrameStart),
                LogicMs = (float)logicAcc,
                DrawMs = (float)drawAcc,
                Ticks = tickAcc,
                Alloc = allocRecorder.Valid ? allocRecorder.LastValue : -1,
                FightAlloc = fightAllocAcc,
            };
            fightAllocAcc = 0;
            lastFrameStart = now;
            logicAcc = drawAcc = 0; tickAcc = 0;
            if (timingSupported) {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0) {
                    f.MainMs = (float)timings[0].cpuMainThreadFrameTime;
                    f.RenderMs = (float)timings[0].cpuRenderThreadFrameTime;
                    f.GpuMs = (float)timings[0].gpuFrameTime;
                }
            }
            Ring[ringPos] = f;
            ringPos = (ringPos + 1) % Ring.Length;
            if (RingCount < Ring.Length) RingCount++;
            if (Recording != null) Recording.Add(f);
            GcCount0 = GC.CollectionCount(0);
            UpdateOverlay();
        }

        // ------------------------------------------------------------------ overlay

        void UpdateOverlay() {
            var s = SettingsStore.Current;
            bool want = s != null && s.showFps;
            if (want && overlay == null) BuildOverlay();
            if (overlayRoot != null && overlayRoot.gameObject.activeSelf != want) overlayRoot.gameObject.SetActive(want);
            if (!want) return;
            overlayTimer -= Time.unscaledDeltaTime;
            if (overlayTimer > 0f) return;
            overlayTimer = 0.5f;
            overlay.text = OverlayText();
        }

        /// <summary>"60 fps · 16.6 ms · logic 0.4 · draw 1.1" over the last 60 frames.</summary>
        public string OverlayText() {
            int n = Mathf.Min(60, RingCount);
            if (n == 0) return "";
            double ms = 0, logic = 0, draw = 0, worst = 0; long alloc = 0;
            for (int i = 0; i < n; i++) {
                var f = Ring[(ringPos - 1 - i + Ring.Length) % Ring.Length];
                ms += f.Ms; logic += f.LogicMs; draw += f.DrawMs; worst = Math.Max(worst, f.Ms);
                if (f.Alloc > 0) alloc += f.Alloc;
            }
            double fps = ms > 0 ? 1000.0 * n / ms : 0;
            return string.Format("{0:0} fps  {1:0.0} ms (max {2:0})  logic {3:0.00}  draw {4:0.00}  gc {5:0.0} KB/f",
                                 fps, ms / n, worst, logic / n, draw / n, alloc / 1024.0 / n);
        }

        void BuildOverlay() {
            var canvas = UIKit.CreateCanvas("PerfCanvas", 100);
            DontDestroyOnLoad(canvas.gameObject);
            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = false;
            overlayRoot = UIKit.Panel(canvas.transform, "Perf", new Vector2(0f, 1f), new Vector2(0f, 1f),
                                      new Vector2(8, -40), new Vector2(760, -6), new Color(0f, 0f, 0f, 0.55f));
            overlayRoot.GetComponent<Image>().raycastTarget = false;
            overlay = UIKit.Text(overlayRoot, "fps", new Vector2(0f, 0.5f), new Vector2(380, 0), new Vector2(740, 30),
                                 "", 20, TextAnchor.MiddleLeft, new Color(0.6f, 1f, 0.6f, 1f));
            overlay.raycastTarget = false;
        }
    }
}
