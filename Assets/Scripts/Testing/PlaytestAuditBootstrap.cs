#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// Only the isolated audit build contains the launcher scene. Normal launches
// have no audit arguments and never enter any of the regression fixtures.
public static class PlaytestAuditBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Start()
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-auditScene");
        if (at >= 0 && at + 1 < args.Length) SceneManager.LoadScene(args[at + 1]);
    }
}
#endif
