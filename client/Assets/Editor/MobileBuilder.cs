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

            // Release signing (Roadmap 10.2). Google Play refuses an upload signed with the
            // debug keystore, so tools/release-android.ps1 supplies the real one.
            //
            // The credentials arrive as ENVIRONMENT VARIABLES, not command-line arguments,
            // because Unity writes its full command line into the editor log - a password
            // passed as an argument would end up in client/builds/android-build.log.
            //
            // The settings are also RESTORED after the build, so the keystore path and
            // passwords can never be accidentally committed inside ProjectSettings.asset.
            string keystore = Environment.GetEnvironmentVariable("FTS_ANDROID_KEYSTORE");
            bool signing = !string.IsNullOrEmpty(keystore);

            bool prevUseCustom = PlayerSettings.Android.useCustomKeystore;
            string prevKeystoreName = PlayerSettings.Android.keystoreName;
            string prevKeystorePass = PlayerSettings.Android.keystorePass;
            string prevAliasName = PlayerSettings.Android.keyaliasName;
            string prevAliasPass = PlayerSettings.Android.keyaliasPass;

            if (signing)
            {
                if (!File.Exists(keystore))
                {
                    Debug.LogError($"[MobileBuilder] Keystore not found: {keystore}");
                    EditorApplication.Exit(1);
                    return;
                }

                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = Path.GetFullPath(keystore);
                PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable("FTS_ANDROID_KEYSTORE_PASS") ?? string.Empty;
                PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("FTS_ANDROID_KEYALIAS") ?? string.Empty;
                PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("FTS_ANDROID_KEYALIAS_PASS") ?? string.Empty;
                // Never log the passwords, only that signing is on.
                Debug.Log($"[MobileBuilder] Release signing enabled with alias '{PlayerSettings.Android.keyaliasName}'.");
            }

            bool ok;
            try
            {
                ok = Run(
                    BuildTarget.Android,
                    BuildTargetGroup.Android,
                    outputPath,
                    isDirectory: false,
                    label: aab ? "Android (AAB)" : "Android (APK)");
            }
            finally
            {
                // Restore BEFORE quitting: EditorApplication.Exit ends the process, so this
                // could not be done after it.
                if (signing)
                {
                    PlayerSettings.Android.keyaliasPass = prevAliasPass;
                    PlayerSettings.Android.keyaliasName = prevAliasName;
                    PlayerSettings.Android.keystorePass = prevKeystorePass;
                    PlayerSettings.Android.keystoreName = prevKeystoreName;
                    PlayerSettings.Android.useCustomKeystore = prevUseCustom;
                }
            }

            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>
        /// Exports a Unity-generated Xcode project (NOT an .ipa). Run this from a
        /// Mac with Xcode to produce a device build; on Windows it still exports
        /// the project so the settings can be reviewed.
        /// </summary>
        public static void BuildiOS()
        {
            string outputPath = ArgValue("-buildOutput") ?? "builds/ios";
            bool ok = Run(
                BuildTarget.iOS,
                BuildTargetGroup.iOS,
                outputPath,
                isDirectory: true,
                label: "iOS (Xcode project)");

            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>
        /// Runs the build and REPORTS the outcome; it deliberately does not quit the editor,
        /// so a caller can restore mutated PlayerSettings (release signing) before exiting.
        /// </summary>
        private static bool Run(
            BuildTarget target, BuildTargetGroup group, string outputPath, bool isDirectory, string label)
        {
            string[] scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("[MobileBuilder] No enabled scenes in Build Settings. Add the Boot scene and retry.");
                return false;
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
                return true;
            }

            Debug.LogError($"[MobileBuilder] FAILED — {label}: {summary.result}, {summary.totalErrors} error(s).");
            return false;
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
