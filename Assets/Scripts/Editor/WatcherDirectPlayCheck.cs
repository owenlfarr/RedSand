#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Netcode;
using System.IO;

[InitializeOnLoad]
public static class WatcherDirectPlayCheck
{
    const string Armed = "Watcher.DirectPlayCheck.Armed";
    static bool measuring;
    static double started;
    static Vector3 startPosition;

    static WatcherDirectPlayCheck() { EditorApplication.update += Tick; }

    [MenuItem("Tools/Watcher/Test Direct Night 2 Play")]
    public static void Run()
    {
        if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Night 2")
            throw new System.InvalidOperationException("Open Night 2 in Edit Mode first.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        SessionState.SetBool(Armed, true);
        measuring = false;
        EditorApplication.isPlaying = true;
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Armed, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isPaused) return;
        var watcher = Object.FindFirstObjectByType<PowerStationWatcher>();
        if (watcher == null) return;
        if (!measuring)
        {
            startPosition = watcher.transform.position;
            started = EditorApplication.timeSinceStartup;
            measuring = true;
            return;
        }
        if (EditorApplication.timeSinceStartup - started < 5d) return;
        bool offline = NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
        float travelled = Vector3.Distance(startPosition, watcher.transform.position);
        bool pass = offline && !watcher.IsSpawned && watcher.GetComponent<CharacterController>().enabled && travelled > 0.3f;
        string result = (pass ? "PASS" : "FAIL") + " direct Night 2 scene Play, no host/client started; movement=" + travelled.ToString("F3") + "m; start=" + startPosition + "; end=" + watcher.transform.position;
        File.WriteAllText("Logs/WatcherDirectPlayResult.txt", result);
        Debug.Log("[Watcher Direct Play Test] " + result);
        SessionState.SetBool(Armed, false);
        measuring = false;
        EditorApplication.isPlaying = false;
    }
}
#endif
