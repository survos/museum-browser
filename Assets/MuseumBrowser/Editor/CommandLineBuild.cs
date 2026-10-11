using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MuseumBrowser.EditorTools
{
    /// Builds from the command line, e.g.
    /// Unity -batchmode -quit -projectPath . -executeMethod MuseumBrowser.EditorTools.CommandLineBuild.MacOS
    /// The Web build uses Unity's own profile build instead (BuildPlayerWithProfileOptions from a
    /// script produced a player whose lit materials did not draw):
    /// Unity -batchmode -quit -projectPath . -activeBuildProfile "Assets/Settings/Build Profiles/Web - Desktop - Development.asset" -build Builds/WebDev
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
            Finish(report);
        }

        static void Finish(BuildReport report)
        {
            Debug.Log($"Build {report.summary.result}: {report.summary.outputPath} ({report.summary.totalErrors} errors)");
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
