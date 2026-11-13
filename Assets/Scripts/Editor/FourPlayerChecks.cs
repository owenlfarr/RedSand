#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Networking;

// Run in the isolated verification project; never saves authored gameplay scenes.
public static class FourPlayerChecks
{
    public static async void Run()
    {
        Directory.CreateDirectory("Logs");
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
            foreach (var entry in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                var scene = EditorSceneManager.OpenScene(entry.path);
                var roots = scene.GetRootGameObjects();
                if (scene.name != "Night1_MultiplayerTest" && !new[] {"Night 2", "Night 3", "Night 4", "Night 5"}.Contains(scene.name)) continue;
                int starts = roots.SelectMany(g => g.GetComponentsInChildren<NetworkStartPosition>(true)).Count();
                if (starts < RelayPartyManager.LobbyPlayerLimit) throw new Exception(entry.path + " has only " + starts + " spawn positions");
            }
            CrewStressChecks.Run();
            JumpscareLayoutChecks.Run();
            await LobbyBrowserChecks.RunTestsAsync();
            foreach (string result in new[] { "Logs/CrewStressChecks.txt", "Logs/LobbyBrowserLifecycleResults.txt" })
                if (!File.ReadAllText(result).StartsWith("PASS")) throw new Exception(File.ReadAllText(result));
            EditorApplication.delayCall += Build;
        }
        catch (Exception e) { File.WriteAllText("Logs/FourPlayerBuildResult.txt", "FAIL " + e); Debug.LogException(e); EditorApplication.Exit(1); }
    }
    public static void BuildRelease()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        PlaytestAuditTools.BuildRelease();
        if (!File.ReadAllText("Logs/PlaytestReleaseBuildResult.txt").StartsWith("Succeeded errors=0")) throw new Exception(File.ReadAllText("Logs/PlaytestReleaseBuildResult.txt"));
    }
    static void Build()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            LobbyBrowserChecks.BuildIntegration();
            if (!File.ReadAllText("Logs/LobbyBrowserBuildResult.txt").StartsWith("Succeeded errors=0")) throw new Exception(File.ReadAllText("Logs/LobbyBrowserBuildResult.txt"));
            ReviveSetup.BuildTest();
            if (!File.ReadAllText("Logs/ReviveBuildResult.txt").StartsWith("Succeeded errors=0")) throw new Exception(File.ReadAllText("Logs/ReviveBuildResult.txt"));
            DeathPresentationChecks.Build();
            if (!File.ReadAllText("Logs/DeathPresentationBuildResult.txt").StartsWith("Succeeded errors=0")) throw new Exception(File.ReadAllText("Logs/DeathPresentationBuildResult.txt"));
            File.WriteAllText("Logs/FourPlayerBuildResult.txt", "PASS: all campaign scenes have four spawn markers; canonical network identities, four-row UTF-8 stress capacity, lobby lifecycle checks, all production-scene development build and four-peer revive and death fixture builds.");
        }
        catch (Exception e) { File.WriteAllText("Logs/FourPlayerBuildResult.txt", "FAIL " + e); Debug.LogException(e); EditorApplication.Exit(1); return; }
        EditorApplication.Exit(0);
    }
}
#endif
