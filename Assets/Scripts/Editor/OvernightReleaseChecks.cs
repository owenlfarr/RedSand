#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

// Run in an isolated checkout: builds test fixtures without disturbing an open editor.
public static class OvernightReleaseChecks
{
    public static void RebuildBlind()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        BlindLungeNetworkChecks.Build();
    }
    public static void Run()
    {
        Directory.CreateDirectory("Logs");
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
            NightThreeFeatureChecks.Run();
            BlindLungeSetup.Check();
            foreach (var path in new[] { "Logs/NightThreeFeaturesResult.txt", "Logs/BlindLungeChecksResult.txt" })
                if (!File.ReadAllText(path).StartsWith("PASS")) throw new Exception(File.ReadAllText(path));
            BlindLungeNetworkChecks.Build();
            RadarWearNetworkChecks.Build();
            EyeMonsterChecks.Build();
            foreach (var path in new[] { "Logs/BlindNetworkBuildResult.txt", "Logs/RadarWearBuildResult.txt", "Logs/EyeBuildResult.txt" })
                if (!File.ReadAllText(path).StartsWith("Succeeded errors=0")) throw new Exception(path + ": " + File.ReadAllText(path));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                target = BuildTarget.StandaloneWindows64,
                locationPathName = "Logs/OvernightRelease/RedSand.exe",
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
                throw new Exception("Production build: " + report.summary.result + " errors=" + report.summary.totalErrors);
            File.WriteAllText("Logs/OvernightReleaseResult.txt", "PASS: scene network identities, core checks, three fresh network test builds and all production build scenes.");
        }
        catch (Exception e)
        {
            File.WriteAllText("Logs/OvernightReleaseResult.txt", "FAIL " + e);
            throw;
        }
    }
}
#endif
