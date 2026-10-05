#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IK.EditorTools {
    /// <summary>
    /// Project configuration, scene creation and the Android build, all from batch mode:
    /// IL2CPP, ARM64, minSdk 23, targetSdk 36, landscape, package com.ayoub.fistforge (Fist Forge).
    /// Nothing here depends on the Editor GUI, so the whole milestone is reproducible with
    /// one command (see TESTING.md).
    /// </summary>
    public static class IKBuildPipeline {
        public const string MainScene = "Assets/IK/Scenes/Main.unity";
        public const string PackageName = "com.ayoub.fistforge";
        public const string ProductName = "Fist Forge";
        public const string IconPath = "Assets/IK/Resources/brand/fist-forge-icon.png";

        static string Env(string key, string fallback) {
            var v = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        [MenuItem("Fist Forge/1. Create scene")]
        public static void CreateScene() {
            Directory.CreateDirectory("Assets/IK/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("MainCamera", typeof(Camera));
            cameraGo.tag = "MainCamera";
            var cam = cameraGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.035f, 0.05f);
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cameraGo.transform.position = new Vector3(0, 0, -10);

            // IKApp boots itself through RuntimeInitializeOnLoadMethod, but an explicit
            // object in the scene makes the intent visible in the editor as well.
            new GameObject("IKApp", typeof(IK.App.IKApp));

            EditorSceneManager.SaveScene(scene, MainScene);
            Debug.Log("[IK] scene created: " + MainScene);
        }

        [MenuItem("Fist Forge/2. Configure player settings")]
        public static void ConfigurePlayerSettings() {
            PlayerSettings.companyName = "Ayoub Teke";
            PlayerSettings.productName = ProductName;
            PlayerSettings.applicationIdentifier = PackageName;
            PlayerSettings.bundleVersion = Env("IK_VERSION", "1.0.0");
            PlayerSettings.Android.bundleVersionCode = int.Parse(Env("IK_VERSION_CODE", "1"));
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)int.Parse(Env("IK_TARGET_SDK", "36"));
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_Standard_2_0);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.useAnimatedAutorotation = true;
            PlayerSettings.Android.renderOutsideSafeArea = true;   // we handle the safe area ourselves
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.gpuSkinning = true;
            PlayerSettings.MTRendering = true;
            PlayerSettings.enableFrameTimingStats = true;      // dev.7: CPU/GPU frame times in the benchmark
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, ManagedStrippingLevel.Low);
            // dev.7: fastest IL2CPP code (longer build): Master config + speed-optimised generation
            PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Android, Il2CppCompilerConfiguration.Master);
            PlayerSettings.SetIl2CppCodeGeneration(UnityEditor.Build.NamedBuildTarget.Android, UnityEditor.Build.Il2CppCodeGeneration.OptimizeSpeed);
            PlayerSettings.stripEngineCode = true;
            ApplyIcons();
            QualitySettings.vSyncCount = 0;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScene, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[IK] player settings configured: " + PackageName + " v" + PlayerSettings.bundleVersion +
                      " targetSdk=" + PlayerSettings.Android.targetSdkVersion + " il2cpp/arm64");
        }

        /// <summary>
        /// Fist Forge launcher icon: imported as an uncompressed Texture2D (IKIconImport) and
        /// set as the default icon plus every Android legacy (and round) icon slot, so the
        /// launcher really shows it. Adaptive slots are left empty: Android then uses legacy.
        /// </summary>
        public static void ApplyIcons() {
            AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceUpdate);
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null) { Debug.LogError("[IK] icon missing: " + IconPath); return; }
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
            var android = UnityEditor.Build.NamedBuildTarget.Android;
            foreach (var kind in new[] { UnityEditor.Android.AndroidPlatformIconKind.Legacy, UnityEditor.Android.AndroidPlatformIconKind.Round }) {
                var slots = PlayerSettings.GetPlatformIcons(android, kind);
                foreach (var s in slots) s.SetTexture(icon);
                PlayerSettings.SetPlatformIcons(android, kind, slots);
            }
            Debug.Log("[IK] icons set: " + IconPath);
        }

        [MenuItem("Fist Forge/3. Build Android APK")]
        public static void BuildAndroid() {
            if (!File.Exists(MainScene)) CreateScene();
            ConfigurePlayerSettings();
            Directory.CreateDirectory("Builds");
            string apk = Path.GetFullPath(Env("IK_APK", "Builds/fist-forge.apk"));
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            var options = new BuildPlayerOptions {
                scenes = new[] { MainScene },
                locationPathName = apk,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[IK] build {summary.result} size={summary.totalSize / 1048576f:0.0} MB time={summary.totalTime} output={summary.outputPath}");
            foreach (var step in report.steps)
                foreach (var msg in step.messages)
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        Debug.LogError("[IK build] " + msg.content);
            if (summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        /// <summary>
        /// dev.7 test player for Linux (Mono, development off): runs under xvfb at any resolution
        /// (`-screen-width 2400 -screen-height 1080`), unlike the batch editor whose game view is
        /// 640x480. Used for real-resolution screenshots (`-ikshots dir`) and a player benchmark
        /// (`-ikbench out.json`) — see TESTING.md §5. Not shipped.
        /// </summary>
        public static void BuildLinux() {
            if (!File.Exists(MainScene)) CreateScene();
            ConfigurePlayerSettings();
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = false;
            PlayerSettings.runInBackground = true;          // xvfb has no focus; a paused player never renders
            string exe = Path.GetFullPath(Env("IK_LINUX", "Builds/linux/fistforge.x86_64"));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { MainScene }, locationPathName = exe,
                target = BuildTarget.StandaloneLinux64, targetGroup = BuildTargetGroup.Standalone, options = BuildOptions.None,
            });
            Debug.Log($"[IK] linux build {report.summary.result} size={report.summary.totalSize / 1048576f:0.0} MB output={exe}");
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        /// <summary>Compile-only entry point used by the first gate in TESTING.md.</summary>
        [MenuItem("Fist Forge/0. Validate project")]
        public static void Validate() {
            ConfigurePlayerSettings();
            var missing = new List<string>();
            if (!File.Exists(MainScene)) missing.Add(MainScene);
            if (Resources.Load<Font>("fonts/Amiri-Regular") == null) missing.Add("Resources/fonts/Amiri-Regular");
            if (missing.Count > 0) {
                Debug.LogError("[IK] missing: " + string.Join(", ", missing));
                EditorApplication.Exit(1);
            }
            Debug.Log("[IK] validate ok");
        }
    }
}
#endif
