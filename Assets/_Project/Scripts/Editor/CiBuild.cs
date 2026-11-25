using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TNLorry.EditorTools
{
    /// <summary>
    /// Headless build entry point for CI (section X.2).
    ///
    /// This exists because the content seeder and the scene generator are Editor menu items.
    /// Without running them first, a CI build produces a technically valid APK containing no
    /// scenes and no data — it installs, launches to a black screen, and tells you nothing.
    /// So the order here matters: settings, then content, then scenes, then build.
    ///
    /// Invoked by GameCI as <c>buildMethod: TNLorry.EditorTools.CiBuild.AndroidDevelopment</c>,
    /// or by hand:
    ///   Unity -quit -batchmode -projectPath . \
    ///         -executeMethod TNLorry.EditorTools.CiBuild.AndroidDevelopment
    /// </summary>
    public static class CiBuild
    {
        const string DefaultOutputDir = "build/Android";
        const string DefaultApkName = "TNLorrySimulator.apk";

        [MenuItem("TN Lorry/Build APK (development)", priority = 40)]
        public static void AndroidDevelopment() => Run(development: true);

        [MenuItem("TN Lorry/Build APK (release)", priority = 41)]
        public static void AndroidRelease() => Run(development: false);

        static void Run(bool development)
        {
            try
            {
                Log("Preparing project...");

                // 0. Android toolchain paths. The JDK/SDK/NDK can all be installed and the
                //    build will still fail with "JDK not found", because a fresh editor has no
                //    EditorPrefs telling it to use its own embedded copies. Nothing else sets
                //    these in batch mode, so CI must.
                ConfigureAndroidTools();

                // 1. Player settings. Must happen before the build and before scene generation,
                //    because the generated scenes are laid out for landscape.
                PlayableSceneBuilder.ApplyAndroidSettings();

                // 2. Content. The seeder is idempotent — it matches assets by id and updates in
                //    place — so re-running it on every CI build is safe.
                Log("Seeding Phase 1 content...");
                Phase1ContentSeeder.Seed();
                Phase1ContentSeeder.Validate();

                // 3. Scenes. Also regenerated every build, and registered in Build Settings.
                Log("Generating playable scenes...");
                PlayableSceneBuilder.BuildAll();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // 4. Build.
                var scenes = EnabledScenes();
                if (scenes.Length == 0)
                {
                    Fail("No enabled scenes in Build Settings after generation. " +
                         "An APK without scenes is not worth shipping.");
                    return;
                }

                string outputPath = ResolveOutputPath();
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                Log("Building " + scenes.Length + " scene(s) -> " + outputPath +
                    (development ? " (development)" : " (release)"));

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    // Development: a debug keystore is generated automatically, so CI needs no
                    // signing secrets. A real release build needs the keystore ones.
                    options = development
                        ? BuildOptions.Development | BuildOptions.AllowDebugging
                        : BuildOptions.None
                };

                var report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;

                if (summary.result != BuildResult.Succeeded)
                {
                    Fail("Build " + summary.result + " with " + summary.totalErrors + " error(s).");
                    return;
                }

                Log("Build succeeded: " + outputPath + " (" +
                    (summary.totalSize / (1024f * 1024f)).ToString("0.0") + " MB, " +
                    summary.totalWarnings + " warning(s))");

                // In batch mode Unity does not exit on its own unless -quit was passed, and a
                // zero exit code is what tells CI the step passed.
                if (IsBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                // An uncaught exception in batch mode can leave Unity hanging until the job
                // times out, which reads as a mysterious CI failure rather than a build error.
                Fail("Unhandled exception: " + e);
            }
        }

        /// <summary>
        /// Points Unity at the JDK, SDK, NDK and Gradle that ship with the Android module.
        ///
        /// "JDK not found" with a JDK plainly on disk is the symptom this fixes. Unity decides
        /// where to look from EditorPrefs, and on a machine where nobody has ever opened the
        /// Preferences window those keys do not exist, so it looks nowhere. Both the
        /// use-embedded booleans and the explicit paths are written: the booleans are what
        /// Unity actually honours, the paths make the resolved location visible in the log and
        /// cover editor versions that read them directly.
        /// </summary>
        static void ConfigureAndroidTools()
        {
            // <editor>/Data/PlaybackEngines/AndroidPlayer
            string androidPlayer = Path.Combine(EditorApplication.applicationContentsPath,
                                                "PlaybackEngines", "AndroidPlayer");

            if (!Directory.Exists(androidPlayer))
            {
                Debug.LogError("[CiBuild] Android module not found at " + androidPlayer +
                               ". Install it with: unity install-modules --editor-version " +
                               "<ver> -m android --child-modules --reinstall");
                return;
            }

            string jdk = Path.Combine(androidPlayer, "OpenJDK");
            string sdk = Path.Combine(androidPlayer, "SDK");
            string ndk = Path.Combine(androidPlayer, "NDK");
            string gradle = Path.Combine(androidPlayer, "Tools", "gradle");

            // Prefer the embedded toolchain. Mixing a system JDK with Unity's NDK is a
            // well-known source of Gradle failures, so do not go looking for one.
            EditorPrefs.SetBool("JdkUseEmbedded", true);
            EditorPrefs.SetBool("SdkUseEmbedded", true);
            EditorPrefs.SetBool("NdkUseEmbedded", true);
            EditorPrefs.SetBool("GradleUseEmbedded", true);

            if (Directory.Exists(jdk))    EditorPrefs.SetString("JdkPath", jdk);
            if (Directory.Exists(sdk))    EditorPrefs.SetString("AndroidSdkRoot", sdk);
            if (Directory.Exists(ndk))    EditorPrefs.SetString("AndroidNdkRoot", ndk);
            if (Directory.Exists(gradle)) EditorPrefs.SetString("GradlePath", gradle);

            Log("Android tools: jdk=" + Directory.Exists(jdk) +
                " sdk=" + Directory.Exists(sdk) +
                " ndk=" + Directory.Exists(ndk) +
                " gradle=" + Directory.Exists(gradle));

            // A missing java executable here means the module reported success without
            // extracting, which the Unity CLI does do. Fail loudly rather than at link time.
            string java = Path.Combine(jdk, "bin", "java.exe");
            if (!File.Exists(java) && !File.Exists(Path.Combine(jdk, "bin", "java")))
                Debug.LogError("[CiBuild] No java executable under " + jdk +
                               ". Re-run the module install with --reinstall.");
        }

        static string[] EnabledScenes()
        {
            var scenes = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene != null && scene.enabled) scenes.Add(scene.path);
            return scenes.ToArray();
        }

        /// <summary>
        /// GameCI passes <c>-customBuildPath</c>; honour it so the action's own artifact
        /// upload finds the APK where it expects to.
        /// </summary>
        static string ResolveOutputPath()
        {
            var args = Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-customBuildPath") continue;

                string path = args[i + 1];
                if (string.IsNullOrEmpty(path)) break;

                // GameCI may hand over a directory or a full file path.
                return path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
                    ? path
                    : Path.Combine(path, DefaultApkName);
            }

            return Path.Combine(DefaultOutputDir, DefaultApkName);
        }

        static bool IsBatchMode => Application.isBatchMode;

        static void Log(string message) => Debug.Log("[CiBuild] " + message);

        static void Fail(string message)
        {
            Debug.LogError("[CiBuild] " + message);
            if (IsBatchMode) EditorApplication.Exit(1);
        }
    }
}
