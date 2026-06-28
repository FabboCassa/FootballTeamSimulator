using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Fts.EditorTools
{
    /// <summary>
    /// Headless WebGL build entry point (Roadmap 6.3), invoked from
    /// tools/build-webgl.ps1 via -executeMethod Fts.EditorTools.WebGLBuilder.Build.
    /// Builds the scenes enabled in Build Settings to the path given by
    /// -buildOutput (default: client/builds/webgl) and exits with code 1 on
    /// failure so CI / the shell can detect a broken build.
    /// </summary>
    public static class WebGLBuilder
    {
        public static void Build()
        {
            string outputPath = ArgValue("-buildOutput") ?? "builds/webgl";

            string[] scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("[WebGLBuilder] No enabled scenes in Build Settings. Add the Boot scene and retry.");
                EditorApplication.Exit(1);
                return;
            }

            Directory.CreateDirectory(outputPath);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None
            };

            Debug.Log($"[WebGLBuilder] Building {scenes.Length} scene(s) → {outputPath}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                double mb = summary.totalSize / (1024.0 * 1024.0);
                Debug.Log($"[WebGLBuilder] SUCCESS — output {mb:F1} MB (uncompressed total) at {summary.outputPath}");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[WebGLBuilder] FAILED — {summary.result}, {summary.totalErrors} error(s).");
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
