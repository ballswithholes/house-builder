// Command-line builds (CI / cloud runs). Example:
//   Unity -batchmode -quit -nographics -projectPath <project> \
//     -executeMethod Lanternvale.EditorTools.LanternvaleBuild.BuildLinuxPlayer -buildPath Build/Linux/Lanternvale.x86_64
// Configures URP with the 2D renderer (when URP is installed), creates the game scene, and builds a
// windowed 1600x900 player. Exits with code 1 when anything fails so scripts can detect it.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Lanternvale.EditorTools
{
    public static class LanternvaleBuild
    {
        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        public static void BuildLinuxPlayer() => Build(BuildTarget.StandaloneLinux64, Arg("-buildPath") ?? "Build/Linux/Lanternvale.x86_64");

        public static void BuildWindowsPlayer() => Build(BuildTarget.StandaloneWindows64, Arg("-buildPath") ?? "Build/Windows/Lanternvale.exe");

        public static void BuildMacPlayer() => Build(BuildTarget.StandaloneOSX, Arg("-buildPath") ?? "Build/Mac/Lanternvale.app");

        static void Build(BuildTarget target, string path)
        {
            try
            {
                bool urp = LanternvaleMenu.ConfigureUrp2D(interactive: false);
                Debug.Log(urp ? "[Lanternvale] Build: URP 2D renderer configured." : "[Lanternvale] Build: no URP — the player uses the unlit fallback lighting.");
                LanternvaleShaderIncludes.Ensure();
                var scene = LanternvaleMenu.EnsureGameScene();

                PlayerSettings.productName = "Lanternvale";
                PlayerSettings.companyName = "Lanternvale";
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.defaultScreenWidth = 1600;
                PlayerSettings.defaultScreenHeight = 900;
                PlayerSettings.runInBackground = true;
                PlayerSettings.resizableWindow = true;

                var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { scene },
                    locationPathName = path,
                    target = target,
                    options = BuildOptions.None,
                });
                var s = report.summary;
                Debug.Log($"[Lanternvale] Build {s.result}: {s.outputPath} ({s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors, {s.totalTime})");
                if (s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
            }
            catch (Exception e)
            {
                Debug.LogError("[Lanternvale] Build failed: " + e);
                EditorApplication.Exit(1);
            }
        }
    }
}
