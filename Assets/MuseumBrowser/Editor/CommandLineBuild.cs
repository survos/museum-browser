using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MuseumBrowser.EditorTools
{
    /// Builds from the command line, e.g.
    /// Unity -batchmode -quit -projectPath . -executeMethod MuseumBrowser.EditorTools.CommandLineBuild.MacOS
    public static class CommandLineBuild
    {
        public static void MacOS()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Builds/macOS/MuseumBrowser.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development,
            });
            Debug.Log($"Build {report.summary.result}: {report.summary.outputPath} ({report.summary.totalErrors} errors)");
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
