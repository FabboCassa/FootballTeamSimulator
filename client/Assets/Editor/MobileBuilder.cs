using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Fts.EditorTools
{
    /// <summary>
    /// Headless mobile build entry points (Roadmap 6.4), invoked from
    /// tools/build-android.ps1 via
    /// -executeMethod Fts.EditorTools.MobileBuilder.BuildAndroid.
    ///
    /// Builds the scenes enabled in Build Settings to the path given by
    /// -buildOutput and exits with code 1 on failure so the shell / CI can
    /// detect a broken build. Mirrors the 6.3 WebGLBuilder.
    ///
    /// Android (APK by default, AAB with -aab) is buildable on Windows once
    /// "Android Build Support" (with the OpenJDK/SDK/NDK modules) is installed
    /// in Unity Hub for this editor version. iOS only EXPORTS an Xcode project
    /// (BuildiOS) — turning it into an .ipa still requires a Mac + Xcode.
    /// </summary>
    public static class MobileBuilder
    {
        public static void BuildAndroid()
        {
            bool aab = HasFlag("-aab");
            string defaultName = aab ? "FootballTeamSimulator.aab" : "FootballTeamSimulator.apk";
            string outputPath = ArgValue("-buildOutput") ?? ("builds/android/" + defaultName);

            EditorUserBuildSettings.buildAppBundle = aab;

            Run(
                BuildTarget.Android,
                BuildTargetGroup.Android,
                outputPath,
                isDirectory: false,
                label: aab ? "Android (AAB)" : "Android (APK)");
        }

        /// <summary>
        /// Exports a Unity-generated Xcode project (NOT an .ipa). Run this from a
        /// Mac with Xcode to produce a device build; on Windows it still exports
        /// the project so the settings can be reviewed.
        /// </summary>
        public static void BuildiOS()
        {
            string outputPath = ArgValue("-buildOutput") ?? "builds/ios";
            Run(
                BuildTarget.iOS,
                BuildTargetGroup.iOS,
                outputPath,
                isDirectory: true,
                label: "iOS (Xcode project)");
        }

        private static void Run(
            BuildTarget target, BuildTargetGroup group, string outputPath, bool isDirectory, string label)
        {
            string[] scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("[MobileBuilder] No enabled scenes in Build Settings. Add the Boot scene and retry.");
                EditorApplication.Exit(1);
                return;
            }

            // For a file output (APK/AAB) make sure the parent folder exists;
            // for a directory output (Xcode project) make the folder itself.
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

            Debug.Log($"[MobileBuilder] Building {label}: {scenes.Length} scene(s) → {outputPath}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                double mb = summary.totalSize / (1024.0 * 1024.0);
                Debug.Log($"[MobileBuilder] SUCCESS — {label}, {mb:F1} MB at {summary.outputPath}");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[MobileBuilder] FAILED — {label}: {summary.result}, {summary.totalErrors} error(s).");
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

        private static bool HasFlag(string flag)
        {
            foreach (string a in Environment.GetCommandLineArgs())
            {
                if (a == flag)
                    return true;
            }

            return false;
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
