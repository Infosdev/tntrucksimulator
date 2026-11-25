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
                Log("Preparing project…");

                // 1. Player settings. Must happen before the build and before scene generation,
                //    because the generated scenes are laid out for landscape.
                PlayableSceneBuilder.ApplyAndroidSettings();

                // 2. Content. The seeder is idempotent — it matches assets by id and updates in
                //    place — so re-running it on every CI build is safe.
                Log("Seeding Phase 1 content…");
                Phase1ContentSeeder.Seed();
                Phase1ContentSeeder.Validate();

                // 3. Scenes. Also regenerated every build, and registered in Build Settings.
                Log("Generating playable scenes…");
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
