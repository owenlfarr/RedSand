using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

// Authored scene players are disabled for multiplayer. Restore one for direct/offline level travel.
public static class OfflineNightPlayer
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "Night 1" && scene.name != "Night1_MultiplayerTest" && scene.name != "Night 2" && scene.name != "Night 3") return;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) return;
        PlayerController candidate = null;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var player in root.GetComponentsInChildren<PlayerController>(true))
        {
            if (player.gameObject.activeInHierarchy) return;
            if (candidate == null) candidate = player;
        }
        if (candidate == null) return;
        for (Transform parent = candidate.transform; parent != null; parent = parent.parent)
            parent.gameObject.SetActive(true);
    }
}
