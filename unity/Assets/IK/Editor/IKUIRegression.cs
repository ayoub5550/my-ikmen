#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace IK.EditorTools {
    /// <summary>Batch-mode entry point for the rendered UI fixture (see TESTING.md §3).</summary>
    public static class IKUIRegression {
        public static void Run() {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Already playing");
            IKBuildPipeline.ConfigurePlayerSettings();
            Environment.SetEnvironmentVariable("IK_UI_FIXTURE", "1");
            EditorSceneManager.OpenScene(IKBuildPipeline.MainScene, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
    }
}
#endif
