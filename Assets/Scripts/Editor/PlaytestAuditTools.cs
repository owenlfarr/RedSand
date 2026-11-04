#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlaytestAuditTools
{
    [InitializeOnLoadMethod]
    private static void Requested()
    {
        if (File.Exists("Logs/BuildPlaytestAudit.request")) EditorApplication.delayCall += Build;
        else if (File.Exists("Logs/BuildPlaytestRelease.request")) EditorApplication.delayCall += BuildRelease;
    }

    [MenuItem("Tools/Playtest Audit/Build Playtest Player")]
    public static void BuildRelease()
    {
        File.Delete("Logs/BuildPlaytestRelease.request");
        try
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play Mode and save the current scene before building.");
            Directory.CreateDirectory("Logs/PlaytestRelease");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None,
                locationPathName = "Logs/PlaytestRelease/Red Sand.exe"
            });
            File.WriteAllText("Logs/PlaytestReleaseBuildResult.txt", report.summary.result + " errors=" + report.summary.totalErrors + " time=" + report.summary.totalTime);
        }
        catch (Exception error) { File.WriteAllText("Logs/PlaytestReleaseBuildResult.txt", "FAIL " + error); Debug.LogException(error); }
    }

    private static void BuildAfterPlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= BuildAfterPlayMode;
        EditorApplication.delayCall += Build;
    }

    [MenuItem("Tools/Playtest Audit/Build Isolated Regression Player")]
    public static void Build()
    {
        File.Delete("Logs/BuildPlaytestAudit.request");
        try
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.playModeStateChanged += BuildAfterPlayMode;
                EditorApplication.isPlaying = false;
                return;
            }
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the current scene before building the audit.");
            const string launcher = "Assets/_Project/Tests/PlaytestAuditLauncher.unity";
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(scene, launcher);
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previous);
            AssetDatabase.SaveAssets();
            var scenes = new[] { launcher }.Concat(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path))
                .Concat(Directory.GetFiles("Assets/_Project/Tests", "*.unity").Select(p => p.Replace('\\', '/'))).Distinct().ToArray();
            Directory.CreateDirectory("Logs/PlaytestAuditBuild");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = scenes, target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development,
                locationPathName = "Logs/PlaytestAuditBuild/RedSandAudit.exe"
            });
            File.WriteAllText("Logs/PlaytestAuditBuildResult.txt", report.summary.result + " errors=" + report.summary.totalErrors + " time=" + report.summary.totalTime);
            CrewStressChecks.Run(); JumpscareLayoutChecks.Run(); LobbyBrowserChecks.RunTests();
            if (File.Exists("Logs/BuildPlaytestRelease.request")) EditorApplication.delayCall += BuildRelease;
        }
        catch (Exception error) { File.WriteAllText("Logs/PlaytestAuditBuildResult.txt", "FAIL " + error); Debug.LogException(error); }
    }
}
#endif

