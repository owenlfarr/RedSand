using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Networking
{
    // Lives with the persistent NetworkManager, beyond the outgoing scene/player objects.
    public class NetworkLevelProgression : MonoBehaviour
    {
        public static NetworkLevelProgression Instance { get; private set; }
        private readonly HashSet<ulong> survivors = new HashSet<ulong>();
        private NetworkManager manager;
        private string destination;
        private string pendingDestination;
        private bool transferring;
        private bool hasRoster;
        private Coroutine spawnRoutine;

        private void Awake()
        {
            Instance = this;
            manager = GetComponent<NetworkManager>();
        }

        public bool IsEliminated(ulong clientId) => hasRoster && !survivors.Contains(clientId);
        public bool IsTransferring => transferring;
        public void MarkRevived(ulong clientId)
        {
            if (manager != null && manager.IsServer) survivors.Add(clientId);
        }

        public void TransferToLevel(string sceneName)
        {
            if (manager == null || !manager.IsServer || transferring) return;
            if (manager.SceneManager == null || !manager.NetworkConfig.EnableSceneManagement ||
                manager.NetworkConfig.PlayerPrefab == null ||
                manager.NetworkConfig.PlayerPrefab.GetComponent<NetworkObject>() == null ||
                manager.NetworkConfig.PlayerPrefab.GetComponent<NetworkOxygenState>() == null)
            {
                Debug.LogError("[Level Progression] Transfer requires network scene management and a player prefab with NetworkObject and NetworkOxygenState.");
                return;
            }

            survivors.Clear();
            foreach (var client in manager.ConnectedClientsList)
            {
                var state = client.PlayerObject != null
                    ? client.PlayerObject.GetComponent<NetworkOxygenState>() : null;
                if (state != null && !state.IsDead.Value) survivors.Add(client.ClientId);
            }

            if (survivors.Count == 0)
            {
                if (manager.GetComponent<ReviveSession>()?.Current.Active == true) pendingDestination = sceneName;
                Debug.Log("[Level Progression] No survivors; waiting for any paid revival before advancing.");
                return;
            }

            pendingDestination = null;
            destination = sceneName;
            hasRoster = true;
            transferring = true;
            Debug.Log($"[Level Progression] Loading {sceneName} for {survivors.Count} survivor(s).");
            manager.SceneManager.OnLoadEventCompleted += OnLevelLoaded;
            if (NetworkGameState.Instance != null) NetworkGameState.Instance.SetRestartInProgress(true);
            var status = manager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                manager.SceneManager.OnLoadEventCompleted -= OnLevelLoaded;
                transferring = false;
                hasRoster = false;
                if (NetworkGameState.Instance != null) NetworkGameState.Instance.SetRestartInProgress(false);
                Debug.LogError($"[Level Progression] Could not load {sceneName}: {status}");
            }
        }

        private void OnLevelLoaded(string sceneName, LoadSceneMode mode,
            List<ulong> completed, List<ulong> timedOut)
        {
            if (sceneName != destination) return;
            Debug.Log($"[Level Progression] {sceneName} network load completed: {completed.Count} ready, {timedOut.Count} timed out.");
            manager.SceneManager.OnLoadEventCompleted -= OnLevelLoaded;
            foreach (ulong clientId in timedOut)
                if (clientId != manager.LocalClientId && manager.ConnectedClients.ContainsKey(clientId))
                    manager.DisconnectClient(clientId, "Loading the next level timed out.");
            spawnRoutine = StartCoroutine(SpawnLevelPlayers());
        }

        private IEnumerator SpawnLevelPlayers()
        {
            yield return null;
            if (manager == null || !manager.IsServer) yield break;
            var points = FindObjectsOfType<NetworkStartPosition>();
            System.Array.Sort(points, (a, b) => string.CompareOrdinal(a.name, b.name));
            int index = 0;
            foreach (ulong clientId in new List<ulong>(manager.ConnectedClientsIds))
            {
                if (!manager.ConnectedClients.TryGetValue(clientId, out var client)) continue;
                if (client.PlayerObject != null) continue;
                var player = Instantiate(manager.NetworkConfig.PlayerPrefab);
                if (points.Length > 0)
                {
                    var point = points[index++ % points.Length].transform;
                    player.transform.SetPositionAndRotation(point.position, point.rotation);
                }

                // Dead party members retain a dead player object for the existing spectator
                // system. Only the captured survivors become playable in the next level.
                // NetworkOxygenState initializes from the roster in OnNetworkSpawn.
                // Writing NetworkVariables before Spawn has no network owner yet.
                player.GetComponent<NetworkObject>().SpawnAsPlayerObject(client.ClientId, true);
                Debug.Log($"[Level Progression] Spawned client {clientId} in {destination} at {player.transform.position}; spectator={IsEliminated(clientId)}.");
            }
            yield return null;
            if (NetworkGameState.Instance != null)
            {
                NetworkGameState.Instance.SetRestartInProgress(false);
                NetworkGameState.Instance.RefreshPlayerCounts();
            }
            transferring = false;
            spawnRoutine = null;
        }

        private void Update()
        {
            if (manager != null && manager.IsServer && manager.IsListening && !transferring &&
                pendingDestination != null && manager.GetComponent<ReviveSession>()?.Current.Active != true)
            {
                string next = pendingDestination;
                pendingDestination = null;
                TransferToLevel(next);
            }
            // A new party or a return to the menu starts a fresh campaign roster.
            if (manager != null && (!manager.IsListening || SceneManager.GetActiveScene().name == "MainMenu"))
            {
                if (manager.SceneManager != null)
                    manager.SceneManager.OnLoadEventCompleted -= OnLevelLoaded;
                if (spawnRoutine != null) StopCoroutine(spawnRoutine);
                spawnRoutine = null;
                hasRoster = false;
                pendingDestination = null;
                survivors.Clear();
                transferring = false;
            }
        }

        private void OnDestroy()
        {
            if (manager != null && manager.SceneManager != null)
                manager.SceneManager.OnLoadEventCompleted -= OnLevelLoaded;
            if (Instance == this) Instance = null;
        }
    }
}
