using UnityEngine;
using IK.Core;
using IK.Settings;

namespace IK.App {
    /// <summary>
    /// dev.7: applies Options → Video. Before dev.7 only the FPS cap did anything.
    /// <list type="bullet">
    /// <item>FPS cap: <c>Application.targetFrameRate</c> (30 / 60). The game logic always runs at
    /// 60 ticks per second (MUGEN timing), so 30 FPS draws every second tick.</item>
    /// <item>Render scale: on the phone the back buffer is resized with
    /// <c>Screen.SetResolution</c> and the system scaler stretches it to the panel. This lowers
    /// GPU fill cost and heat; 100 % is the native resolution.</item>
    /// <item>Pixel filter: Sharp = point sampling (raw pixels), Smooth = bilinear, Crisp = pixel
    /// art anti-aliasing (bilinear only across texel borders, done in IK/UIPalFx): square
    /// pixels without the uneven widths point sampling gives at non-integer scales.</item>
    /// </list>
    /// </summary>
    public static class RenderQuality {
        static readonly int PixelAAId = Shader.PropertyToID("_IKPixelAA");
        static int appliedScale = 100;

        public static void Apply(GameSettings s) {
            if (s == null) return;
            if (!Benchmark.Running) Application.targetFrameRate = s.fpsCap;
            QualitySettings.vSyncCount = 0;
            ApplyFilter(s.pixelFilter);
            ApplyRenderScale(s.renderScale);
        }

        public static void ApplyFilter(PixelFilter f) {
            MugenAssetCache.Filter = f == PixelFilter.Sharp ? FilterMode.Point : FilterMode.Bilinear;
            Shader.SetGlobalFloat(PixelAAId, f == PixelFilter.Crisp ? 1f : 0f);
            IK.UI.StageRenderer.Crisp = f == PixelFilter.Crisp;
        }

        /// <summary>Native panel size (the back buffer may already be scaled down).</summary>
        public static Vector2Int NativeSize() {
            var d = Display.main;
            int w = d != null && d.systemWidth > 0 ? d.systemWidth : Screen.width;
            int h = d != null && d.systemHeight > 0 ? d.systemHeight : Screen.height;
            return new Vector2Int(Mathf.Max(w, h), Mathf.Min(w, h));   // landscape
        }

        public static Vector2Int ScaledSize(Vector2Int native, int percent) {
            percent = Mathf.Clamp(percent, 50, 100);
            return new Vector2Int(Mathf.Max(320, Mathf.RoundToInt(native.x * percent / 100f)),
                                  Mathf.Max(240, Mathf.RoundToInt(native.y * percent / 100f)));
        }

        static void ApplyRenderScale(int percent) {
#if UNITY_ANDROID && !UNITY_EDITOR
            percent = Mathf.Clamp(percent, 50, 100);
            if (percent == appliedScale && (percent == 100 || Screen.width != NativeSize().x)) return;
            var size = ScaledSize(NativeSize(), percent);
            Screen.SetResolution(size.x, size.y, FullScreenMode.FullScreenWindow);
            appliedScale = percent;
#else
            appliedScale = percent;
#endif
        }
    }
}
