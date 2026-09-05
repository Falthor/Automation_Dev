using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Build entry point usable from the Editor menu or from the command line.
    /// Fails loudly: a build that produced errors must not exit with code 0.
    /// </summary>
    public static class BuildCommand
    {
        const string DefaultOutputDir = "Builds/Windows";
        const string ExecutableName = "Automation.exe";

        [MenuItem("Build/Windows 64 (Development)")]
        public static void BuildWindowsDevelopmentMenu() => Run(true);

        [MenuItem("Build/Windows 64 (Release)")]
        public static void BuildWindowsReleaseMenu() => Run(false);

        /// <summary>Called from the command line with -executeMethod.</summary>
        public static void BuildWindowsDevelopment() => RunBatch(true);

        /// <summary>Called from the command line with -executeMethod.</summary>
        public static void BuildWindowsRelease() => RunBatch(false);

        static void RunBatch(bool development)
        {
            var code = Run(development) ? 0 : 1;
            EditorApplication.Exit(code);
        }

        static bool Run(bool development)
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("Build aborted: no scene enabled in Build Settings.");
                return false;
            }

            // Order matters: MainMenu must load first, Bootstrap second.
            var first = Path.GetFileNameWithoutExtension(scenes[0]);
            if (!string.Equals(first, "MainMenu", StringComparison.OrdinalIgnoreCase))
                Debug.LogWarning($"Scene 0 is '{first}', expected 'MainMenu'. Check Build Settings order.");

            var outputDir = ResolveOutputDir();
            Directory.CreateDirectory(outputDir);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outputDir, ExecutableName),
                target = BuildTarget.StandaloneWindows64,
                options = development
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None
            };

            Debug.Log($"Building {scenes.Length} scene(s) to {options.locationPathName} " +
                      $"({(development ? "development" : "release")})");

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"Build FAILED: {summary.result}, {summary.totalErrors} error(s).");
                return false;
            }

            Debug.Log($"Build succeeded in {summary.totalTime.TotalSeconds:F1}s, " +
                      $"{summary.totalSize / (1024 * 1024)} MB, {summary.totalWarnings} warning(s).");
            return true;
        }

        static string ResolveOutputDir()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == "-buildOutput")
                    return args[i + 1];

            return Path.Combine(Directory.GetCurrentDirectory(), DefaultOutputDir);
        }
    }
}
