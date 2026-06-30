using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Fts.EditorTools
{
    /// <summary>
    /// Headless desktop / Steam build entry points (Roadmap 6.5), invoked from
    /// tools/build-desktop.ps1 via
    /// -executeMethod Fts.EditorTools.DesktopBuilder.BuildWindows (or BuildMac/BuildLinux).
    ///
    /// Builds the scenes enabled in Build Settings to the path given by
    /// -buildOutput and exits with code 1 on failure so the shell / CI can
    /// detect a broken build. Mirrors the 6.3 WebGLBuilder / 6.4 MobileBuilder.
    ///
    /// "Steam-ready" means a clean standalone player; the Steamworks SDK
    /// (overlay/achievements/cloud) is a later integration once an App ID exists,
    /// so nothing here depends on Steam native libraries.
    /// </summary>
    public static class DesktopBuilder
    {
        public static void BuildWindows()
        {
            string outputPath = ArgValue("-buildOutput") ?? "builds/windows/FootballTeamSimulator.exe";
            Run(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, outputPath, isDirectory: false, label: "Windows x64");
        }

        /// <summary>
        /// Builds a macOS .app bundle. Buildable from Windows, but code-signing /
        /// notarization for distribution still requires a Mac.
        /// </summary>
        public static void BuildMac()
        {
            string outputPath = ArgValue("-buildOutput") ?? "builds/mac/FootballTeamSimulator.app";
            Run(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, outputPath, isDirectory: false, label: "macOS");
        }

        public static void BuildLinux()
        {
            string outputPath = ArgValue("-buildOutput") ?? "builds/linux/FootballTeamSimulator.x86_64";
            Run(BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone, outputPath, isDirectory: false, label: "Linux x64");
        }

        private static void Run(
            BuildTarget target, BuildTargetGroup group, string outputPath, bool isDirectory, string label)
        {
            string[] scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("[DesktopBuilder] No enabled scenes in Build Settings. Add the Boot scene and retry.");
                EditorApplication.Exit(1);
                return;
            }

            // Make sure the parent folder exists (the player and its *_Data sit alongside the exe).
            string dir = isDirectory ? outputPath : Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                targetGroup = group,
                options = BuildOptions.None
            };

            Debug.Log($"[DesktopBuilder] Building {label}: {scenes.Length} scene(s) -> {outputPath}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                double mb = summary.totalSize / (1024.0 * 1024.0);
                Debug.Log($"[DesktopBuilder] SUCCESS - {label}, {mb:F1} MB at {summary.outputPath}");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[DesktopBuilder] FAILED - {label}: {summary.result}, {summary.totalErrors} error(s).");
                EditorApplication.Exit(1);
            }
        }

        private static string[] EnabledScenes()
        {
            var list = new List<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (s.enabled)
                    list.Add(s.path);
            }

            return list.ToArray();
        }

        private static string ArgValue(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == flag)
                    return args[i + 1];
            }

            return null;
        }
    }
}
