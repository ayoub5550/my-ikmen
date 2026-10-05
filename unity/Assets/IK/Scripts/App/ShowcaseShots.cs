using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using IK.Input;
using IK.Settings;

namespace IK.App {
    /// <summary>
    /// dev.7: real-resolution screenshots for review, taken by the Linux test player
    /// (`ikmen.x86_64 -screen-width 2400 -screen-height 1080 -ikshots dir`). The batch editor's
    /// game view is always 640x480, so this is the only way to see the controls at the Poco F3's
    /// 2400x1080 before an APK. Captures the modern and classic button styles, idle and pressed,
    /// over a real match, and the three pixel filters. Does nothing without the flag.
    /// </summary>
    public class ShowcaseShots : MonoBehaviour {
        string dir;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            string d = Benchmark.Arg("-ikshots");
            if (string.IsNullOrEmpty(d)) return;
            var go = new GameObject("IKShowcase");
            DontDestroyOnLoad(go);
            go.AddComponent<ShowcaseShots>().dir = d;
        }

        IEnumerator Shot(string name) {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(dir, name), tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log("[IK SHOT] " + name + " " + Screen.width + "x" + Screen.height);
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        IEnumerator Start() {
            Directory.CreateDirectory(dir);
            for (int i = 0; i < 10 && IKApp.Instance == null; i++) yield return null;
            var app = IKApp.Instance;
            SettingsStore.UseFile(Path.Combine(Path.GetTempPath(), "ik-shots-settings.json"));
            SettingsStore.Reset();
            var s = SettingsStore.Current;
            s.onScreenControls = OnScreenControls.Always;
            yield return Frames(30);

            foreach (int style in new[] { 0, 1 }) {
                string tag = style == 0 ? "modern" : "classic";
                s.buttonStyle = style;
                app.RebuildTouch();
                app.Show(Screen_.InputTest);
                yield return Frames(20);
                yield return Shot("controls-" + tag + ".png");
                Press(app, true);
                yield return Frames(4);
                yield return Shot("controls-" + tag + "-pressed.png");
                Press(app, false);
            }

            // a real match with the controls on top (player 1 human, player 2 CPU)
            s.buttonStyle = 0;
            app.RebuildTouch();
            var setup = Benchmark.BenchSetup();
            setup.Players[0].AiLevel = 0;
            setup.Mode = IK.Core.GameMode.Versus;
            app.ShowFightForBenchmark(setup, r => { });
            yield return new WaitForSecondsRealtime(5f);
            yield return Shot("fight-modern.png");
            Press(app, true);
            yield return Frames(3);
            yield return Shot("fight-modern-pressed.png");
            Press(app, false);
            foreach (var f in new[] { PixelFilter.Sharp, PixelFilter.Smooth, PixelFilter.Crisp }) {
                s.pixelFilter = f;
                RenderQuality.Apply(s);
                app.Fight.Redraw();
                yield return Frames(3);
                yield return Shot("filter-" + f.ToString().ToLowerInvariant() + ".png");
            }
            s.pixelFilter = PixelFilter.Sharp;
            s.buttonStyle = 1;
            app.RebuildTouch();
            yield return Frames(5);
            yield return Shot("fight-classic.png");
            s.showFps = true;
            s.buttonStyle = 0;
            app.RebuildTouch();
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("fight-fps-overlay.png");
            Benchmark.Quit(0);
        }

        static void Press(IKApp app, bool on) {
            var t = app.Touch;
            foreach (var id in new[] { ControlId.LP, ControlId.MK }) { var b = t.Get(id); if (b != null) b.SetHeld(on); }
            if (t.Direction != null) t.Direction.Simulate(on ? new Vector2(0.75f, 0.75f) : Vector2.zero);
        }
    }
}
