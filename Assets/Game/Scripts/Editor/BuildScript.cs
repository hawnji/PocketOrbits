using System;
using System.IO;
using System.Linq;
using PocketOrbits.Core.Levels;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PocketOrbits.Editor
{
    /// <summary>
    /// Command-line builds. Example:
    ///   Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod PocketOrbits.Editor.BuildScript.BuildAndroid
    /// Fails (exit code 1) if level validation fails or the player build reports errors.
    /// </summary>
    public static class BuildScript
    {
        public const string AndroidOutput = "Builds/Android/PocketOrbits.apk";

        [MenuItem("Pocket Orbits/Build Android APK", priority = 20)]
        public static void BuildAndroidMenu() => BuildAndroidInternal(exitOnFinish: false);

        public static void BuildAndroid() => BuildAndroidInternal(exitOnFinish: Application.isBatchMode);

        static void BuildAndroidInternal(bool exitOnFinish)
        {
            int code = 0;
            try
            {
                if (!File.Exists(ProjectSetup.ScenePath)) ProjectSetup.Run(interactive: false);
                var report = LevelTools.ValidateAll(runSolutions: true);
                if (!report.Ok)
                {
                    Debug.LogError("[PocketOrbits] Level validation failed, build aborted:\n" + report);
                    code = 1;
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(AndroidOutput));
                var options = new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    locationPathName = AndroidOutput,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None,
                };
                EditorUserBuildSettings.buildAppBundle = false;
                BuildReport result = BuildPipeline.BuildPlayer(options);
                var summary = result.summary;
                Debug.Log("[PocketOrbits] Android build " + summary.result + ": " + summary.outputPath + " (" + summary.totalSize / (1024 * 1024) + " MB, " + summary.totalErrors + " errors, " + summary.totalTime + ")");
                if (summary.result != BuildResult.Succeeded) code = 1;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                code = 1;
            }
            finally
            {
                if (exitOnFinish) EditorApplication.Exit(code);
            }
        }
    }

    /// <summary>Editor menu for the level validation utility (same checks as the automated tests).</summary>
    public static class LevelTools
    {
        public const string LevelsFolder = "Assets/Game/Data/Resources/Levels";

        public static ValidationReport ValidateAll(bool runSolutions)
        {
            var levels = Directory.GetFiles(LevelsFolder, "*.json").OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => LevelJson.Parse(File.ReadAllText(p), Path.GetFileName(p))).ToList();
            return LevelValidator.ValidateCatalog(levels, runSolutions);
        }

        [MenuItem("Pocket Orbits/Validate Levels", priority = 10)]
        public static void ValidateMenu()
        {
            try
            {
                var report = ValidateAll(runSolutions: true);
                if (report.Ok) Debug.Log("[PocketOrbits] All levels valid; every stored solution verified." + (report.Warnings.Count > 0 ? "\n" + string.Join("\n", report.Warnings) : ""));
                else Debug.LogError("[PocketOrbits] Level validation failed:\n" + report);
            }
            catch (FormatException e)
            {
                Debug.LogError("[PocketOrbits] " + e.Message);
            }
        }

        /// <summary>Batch entry point for CI: -executeMethod PocketOrbits.Editor.LevelTools.ValidateBatch</summary>
        public static void ValidateBatch()
        {
            var report = ValidateAll(runSolutions: true);
            Debug.Log(report.Ok ? "[PocketOrbits] Levels OK" : "[PocketOrbits] Level validation failed:\n" + report);
            EditorApplication.Exit(report.Ok ? 0 : 1);
        }
    }
}
