#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace IK.EditorTools {
    /// <summary>
    /// dev.7 batch benchmark: plays <see cref="IK.App.Benchmark"/> in play mode and writes the
    /// result JSON to IK_BENCH_OUT (TESTING.md §5). Editor numbers compare builds on the same
    /// machine; they are not phone numbers — run Options → Video → Benchmark on the device.
    /// </summary>
    public static class IKBench {
        public static void Run() {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Already playing");
            IKBuildPipeline.ConfigurePlayerSettings();
            Environment.SetEnvironmentVariable("IK_BENCH", "1");
            EditorSceneManager.OpenScene(IKBuildPipeline.MainScene, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
    }
}
#endif
