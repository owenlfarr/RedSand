using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

namespace Networking
{
    public class RelayPartyManager : MonoBehaviour
    {
        [Header("Menu Scene")]
        [SerializeField] private string menuSceneName = "MainMenu";

        [Header("Scene")]
        [SerializeField] private string multiplayerSceneName = "Night1_MultiplayerTest";
        [SerializeField] private int maxConnections = 3;
        [SerializeField] private string relayConnectionType = "dtls";

        [Header("UI Bindings (Optional)")]
        [SerializeField] private Button createLobbyButton;
        [SerializeField] private Button joinLobbyButton;
        [SerializeField] private Button startGameButton;
        [SerializeField] private Button backButton;
        [SerializeField] private Button leaveLobbyButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button settingsBackButton;
        [SerializeField] private TMP_InputField joinCodeInputField;
        [SerializeField] private TMP_InputField usernameInputField;
        [SerializeField] private TMP_Text lobbyCodeText;
        [SerializeField] private Button lobbyCodeDisplayButton;
        [SerializeField] private bool showLegacyOnGUI = false;
        [SerializeField] private TMP_Text loadingIndicatorText;
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private Slider sensitivitySlider;
        [SerializeField] private TMP_Text sensitivityValueText;

        [Header("Lobby Astronauts (Optional)")]
        [SerializeField] private GameObject player1Visual;
        [SerializeField] private GameObject player2Visual;
        [SerializeField] private GameObject player3Visual;
        [SerializeField] private TextMeshPro player1NameText;
        [SerializeField] private TextMeshPro player2NameText;
        [SerializeField] private TextMeshPro player3NameText;
        [SerializeField] private bool disableAstronautAnimatorsInMenu = false;
        [SerializeField] private bool floatAstronautsInMenu = true;
        [SerializeField] private Vector2 astronautFloatBounds = new Vector2(1.25f, 0.65f);
        [SerializeField] private Vector2 astronautFloatSpeedRange = new Vector2(0.18f, 0.34f);
        [SerializeField] private float astronautFloatBobAmplitude = 0.08f;
        [SerializeField] private float astronautFloatBobSpeed = 1.4f;
        [SerializeField] private Vector2 astronautRotationSpeedRange = new Vector2(8f, 16f);

        [Header("UI Runtime State")]
        [SerializeField] private string latestJoinCode = "";
        [SerializeField] private string statusText = "Idle";
        [SerializeField] private VoiceChatManager voiceChatManager;

        private string joinCodeInput = "";
        private bool isBusy;
        private bool matchStarted;
        private bool lobbyCreated;
        private Allocation pendingHostAllocation;
        private bool hasPendingHostAllocation;
        private float loadingAnimTimer;
        private int loadingDots;
        private int nextManualSpawnIndex;
        private Coroutine spawnPlayersAfterLoadCoroutine;
        private Coroutine sendLobbyNameWhenReadyCoroutine;
        private bool sentInitialNameAfterConnect;
        private readonly GameObject[] astronautVisualSlots = new GameObject[3];
        private readonly Vector3[] astronautBaseLocalPositions = new Vector3[3];
        private readonly Quaternion[] astronautBaseLocalRotations = new Quaternion[3];
        private readonly Vector2[] astronautFloatOffsets = new Vector2[3];
        private readonly Vector2[] astronautFloatVelocities = new Vector2[3];
        private readonly float[] astronautRotationOffsets = new float[3];
        private readonly float[] astronautRotationSpeeds = new float[3];
        private readonly bool[] astronautFloatInitialized = new bool[3];
        private readonly Dictionary<ulong, string> lobbyPlayerNames = new Dictionary<ulong, string>();
        private string cachedLocalUserName = "";
        private bool lobbyNameMessagesRegistered;
        private string lastSentLocalUserName = "";

        private const string LobbyNameUpdateMessage = "RelayPartyNameUpdate";
        private const string LobbyNameBroadcastMessage = "RelayPartyNameBroadcast";
        private const int HardLobbyPlayerLimit = 3;
        private const string MouseSensitivityPrefsKey = "MouseSensitivity";

        // Kept as a fallback if an older scene or inspector state has no player prefab at match start.
        private GameObject _lobbyPlayerPrefab;

        public bool HasActiveHostParty
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return !matchStarted && !string.IsNullOrEmpty(latestJoinCode) && (hasPendingHostAllocation || (nm != null && nm.IsHost && nm.IsListening));
            }
        }

        private async void Start()
        {
            NormalizeLegacySceneTarget();
            DisableLegacyMenuStartScripts();
            if (voiceChatManager == null)
            {
                voiceChatManager = FindObjectOfType<VoiceChatManager>(true);
            }
            EnsureMenuEventSystem();
            AutoBindMenuUI();
            EnsureUsernameInputExists();
            EnsureLeaveLobbyButtonExists();
            EnsureSettingsTabExists();
            WireMenuUI();
            WireUsernameInput();
            WireSettingsUI();
            SetLobbyUIState(false);
            RefreshLobbyCodeUI();
            AutoBindLobbyVisuals();
            InitializeAstronautFloatSlots(true);
            EnsureLoadingIndicatorExists();
            UpdateLobbyAstronautVisuals();
            await EnsureServicesReady();
        }

        private void Update()
        {
            if (SceneManager.GetActiveScene().name != menuSceneName || matchStarted)
            {
                return;
            }

            UpdateLoadingIndicator();
            RegisterLobbyNameMessagesIfReady();
            SendInitialNameAfterClientConnect();
            SyncLocalNameIfNeeded();
            UpdateMenuAstronautFloating();
            UpdateLobbyAstronautVisuals();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            var nm = NetworkManager.Singleton;
            if (nm != null && nm.SceneManager != null)
            {
                nm.SceneManager.OnLoadEventCompleted -= OnNetworkSceneLoadCompleted;
            }

            if (spawnPlayersAfterLoadCoroutine != null)
            {
                StopCoroutine(spawnPlayersAfterLoadCoroutine);
                spawnPlayersAfterLoadCoroutine = null;
            }

            if (sendLobbyNameWhenReadyCoroutine != null)
            {
                StopCoroutine(sendLobbyNameWhenReadyCoroutine);
                sendLobbyNameWhenReadyCoroutine = null;
            }

            UnregisterLobbyNameMessages();
        }

        private void OnGUI()
        {
            if (!showLegacyOnGUI || SceneManager.GetActiveScene().name != menuSceneName || matchStarted)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(20f, 20f, 560f, 360f), GUI.skin.box);
            GUILayout.Label("Multiplayer");
            GUILayout.Space(6f);

            var nm = NetworkManager.Singleton;
            bool isHostLobby = HasActiveHostParty;
            bool isClientLobby = nm != null && nm.IsClient && !nm.IsHost && nm.IsListening;

            if (isHostLobby)
            {
                GUILayout.Label("Relay Party");
                GUILayout.Label($"Party Code: {latestJoinCode}");
                GUILayout.Label($"Status: {statusText}");
                int count = nm.ConnectedClients != null ? nm.ConnectedClients.Count : 0;
                GUILayout.Label($"Connected Players: {count}");

                GUI.enabled = !isBusy;
                if (GUILayout.Button("Start Game", GUILayout.Height(30f)))
                {
                    StartMatch();
                }
                if (GUILayout.Button("Leave Lobby", GUILayout.Height(30f)))
                {
                    LeaveLobbyFromUI();
                }
                GUI.enabled = true;
            }
            else if (isClientLobby)
            {
                GUILayout.Label("Relay Party");
                GUILayout.Label($"Status: {statusText}");
                GUILayout.Label("Waiting for host to start the game...");
                if (GUILayout.Button("Leave Lobby", GUILayout.Height(30f)))
                {
                    LeaveLobbyFromUI();
                }
            }
            else
            {
                GUILayout.Label("Create Party / Join Party (Relay)");

                GUI.enabled = !isBusy;
                if (GUILayout.Button("Create Party", GUILayout.Height(30f)))
                {
                    _ = CreateParty();
                }

                GUILayout.Space(6f);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Join Code", GUILayout.Width(100f));
                joinCodeInput = GUILayout.TextField(joinCodeInput ?? string.Empty, 16, GUILayout.Width(180f));
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Join Party", GUILayout.Height(30f)))
                {
                    _ = JoinParty(joinCodeInput);
                }
                GUI.enabled = true;

                GUILayout.Space(10f);
                GUILayout.Label($"Status: {statusText}");
            }

            GUILayout.EndArea();
        }

        public async Task CreateParty()
        {
            if (isBusy) return;
            isBusy = true;
            statusText = "Creating party...";
            ResetLobbyRosterState();
            RefreshLobbyCodeUI();

            try
            {
                if (!await EnsureServicesReady())
                {
                    return;
                }

                var nm = NetworkManager.Singleton;
                if (!ValidateNetworkManager(nm, out var transport))
                {
                    return;
                }

                if (nm.IsListening)
                {
                    nm.Shutdown();
                }

                Allocation allocation;
                try
                {
                    allocation = await RelayService.Instance.CreateAllocationAsync(GetRelayConnectionLimit());
                }
                catch (Exception ex)
                {
                    statusText = $"Relay allocation failed: {ex.Message}";
                    return;
                }

                try
                {
                    latestJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                }
                catch (Exception ex)
                {
                    statusText = $"Get join code failed: {ex.Message}";
                    return;
                }

                transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, relayConnectionType));

                ConfigureLobbyConnectionApproval(nm);

                // StartHost IMMEDIATELY so the Relay server has an active host endpoint.
                // Without this, joining clients get "invalid code" because the Relay has nobody to route to.
                if (!nm.StartHost())
                {
                    statusText = "StartHost failed — check Relay / NetworkManager settings.";
                    return;
                }

                // Allocation is now live — clear the pending state.
                pendingHostAllocation = allocation;
                hasPendingHostAllocation = false;

                statusText = $"Lobby ready! Code: {latestJoinCode}";
                lobbyCreated = true;

                // Subscribe once — unsubscribe first to prevent duplicates if CreateParty is called again.
                NetworkManager.Singleton.OnClientConnectedCallback -= OnLobbyClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnLobbyClientDisconnected;
                NetworkManager.Singleton.OnClientConnectedCallback += OnLobbyClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnLobbyClientDisconnected;

                QueueSendLocalNameWhenReady(true);
                _ = TryJoinVoiceSafely(latestJoinCode);
                SetLobbyUIState(true);
                RefreshLobbyCodeUI();
            }
            catch (Exception ex)
            {
                statusText = $"Create party error: {ex.Message}";
                RefreshLobbyCodeUI();
            }
            finally
            {
                isBusy = false;
            }
        }

        public void StartMatch()
        {
            if (isBusy)
            {
                return;
            }

            var nm = NetworkManager.Singleton;

            // Host is started during CreateParty() so it should already be hosting.
            // TryStartPendingHost is a safety net in case the flow was called out of order.
            if (nm == null || !nm.IsHost)
            {
                if (!TryStartPendingHost(nm))
                {
                    RefreshLobbyCodeUI();
                    return;
                }

                nm = NetworkManager.Singleton;
            }

            if (!LoadMultiplayerSceneAsHost(nm))
            {
                return;
            }

            matchStarted = true;
            statusText = "Starting game...";
            RefreshLobbyCodeUI();
        }

        public async Task JoinParty(string joinCode)
        {
            if (isBusy) return;
            isBusy = true;
            statusText = "Joining party...";
            ResetLobbyRosterState();
            RefreshLobbyCodeUI();

            try
            {
                if (!await EnsureServicesReady())
                {
                    return;
                }

                // Normalise the code exactly as the host stored it.
                string normalizedCode = (joinCode ?? string.Empty).Trim().ToUpperInvariant();

                if (string.IsNullOrWhiteSpace(normalizedCode))
                {
                    statusText = "Join code is empty.";
                    return;
                }

                var nm = NetworkManager.Singleton;
                if (!ValidateNetworkManager(nm, out var transport))
                {
                    return;
                }

                // Tear down any stale connection before starting a new client.
                if (nm.IsListening)
                {
                    nm.Shutdown();
                    await System.Threading.Tasks.Task.Delay(100);
                }

                JoinAllocation joinAllocation;
                try
                {
                    joinAllocation = await RelayService.Instance.JoinAllocationAsync(normalizedCode);
                }
                catch (Exception ex)
                {
                    statusText = $"Invalid join code: {ex.Message}";
                    return;
                }

                transport.SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, relayConnectionType));
                nm.NetworkConfig.ConnectionApproval = true;

                if (!nm.StartClient())
                {
                    statusText = "StartClient failed. Try again.";
                    RefreshLobbyCodeUI();
                    return;
                }

                // Keep astronaut visuals updated as others connect.
                // Unsubscribe first to prevent duplicates if JoinParty is called multiple times.
                nm.OnClientConnectedCallback -= OnLobbyClientConnected;
                nm.OnClientDisconnectCallback -= OnLobbyClientDisconnected;
                nm.OnClientConnectedCallback += OnLobbyClientConnected;
                nm.OnClientDisconnectCallback += OnLobbyClientDisconnected;

                statusText = "Joining lobby...";
                QueueSendLocalNameWhenReady(true);
                _ = TryJoinVoiceSafely(normalizedCode);
                RefreshLobbyCodeUI();
            }
            catch (Exception ex)
            {
                statusText = $"Join party error: {ex.Message}";
                RefreshLobbyCodeUI();
            }
            finally
            {
                isBusy = false;
            }
        }

        public string LatestJoinCode => latestJoinCode;

        public string StatusText => statusText;

        public void CreateLobbyFromUI()
        {
            _ = CreateParty();
        }

        public void JoinLobbyFromUI()
        {
            string code = ReadJoinCodeFromUI();
            Debug.Log($"[RelayPartyManager] JoinLobbyFromUI clicked. Code='{code}'.");
            _ = JoinParty(code);
        }

        public void StartMatchFromUI()
        {
            StartMatch();
        }

        public void BackFromUI()
        {
            LeaveLobbyFromUI();
        }

        public void LeaveLobbyFromUI()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnClientConnectedCallback -= OnLobbyClientConnected;
                nm.OnClientDisconnectCallback -= OnLobbyClientDisconnected;
                nm.ConnectionApprovalCallback -= OnLobbyConnectionApproval;
                UnregisterLobbyNameMessages();

                if (nm.IsListening)
                {
                    nm.Shutdown();
                }
            }

            latestJoinCode = string.Empty;
            statusText = "Idle";
            matchStarted = false;
            lobbyCreated = false;
            hasPendingHostAllocation = false;
            pendingHostAllocation = null;
            ResetLobbyRosterState();
            _ = TryLeaveVoiceSafely();
            SetLobbyUIState(false);
            RefreshLobbyCodeUI();
            UpdateLobbyAstronautVisuals();
        }

        private void OnLobbyClientConnected(ulong clientId)
        {
            RegisterLobbyNameMessagesIfReady();
            var nm = NetworkManager.Singleton;
            if (nm != null && clientId == nm.LocalClientId)
            {
                statusText = nm.IsServer ? statusText : "Joined! Waiting for host to start...";
                SetLobbyUIState(true);
                RefreshLobbyCodeUI();
                sentInitialNameAfterConnect = false;
                QueueSendLocalNameWhenReady(true);
            }

            if (nm != null && nm.IsServer)
            {
                if (!lobbyPlayerNames.ContainsKey(clientId))
                {
                    lobbyPlayerNames[clientId] = GetFallbackLobbyPlayerName(clientId);
                }

                BroadcastLobbyName(clientId, lobbyPlayerNames[clientId]);
                BroadcastAllLobbyNamesToClient(clientId);
                BroadcastAllLobbyNames();
            }

            UpdateLobbyAstronautVisuals();
        }

        private void OnLobbyClientDisconnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            bool localClientWasDisconnected = nm != null && clientId == nm.LocalClientId && !nm.IsServer;
            bool hostDisconnectedClient = nm != null && !nm.IsServer && clientId == NetworkManager.ServerClientId;

            lobbyPlayerNames.Remove(clientId);

            if (nm != null && nm.IsServer)
            {
                BroadcastLobbyName(clientId, string.Empty);
            }

            if ((localClientWasDisconnected || hostDisconnectedClient) && !matchStarted)
            {
                string reason = nm != null ? nm.DisconnectReason : string.Empty;
                latestJoinCode = string.Empty;
                statusText = string.IsNullOrWhiteSpace(reason) ? "Host left the lobby." : reason;
                lobbyCreated = false;
                hasPendingHostAllocation = false;
                pendingHostAllocation = null;
                ResetLobbyRosterState();
                UnregisterLobbyNameMessages();
                _ = TryLeaveVoiceSafely();
                SetLobbyUIState(false);
                RefreshLobbyCodeUI();
            }

            UpdateLobbyAstronautVisuals();
        }

        private void ResetLobbyRosterState()
        {
            lobbyPlayerNames.Clear();
            lastSentLocalUserName = string.Empty;
            sentInitialNameAfterConnect = false;

            if (sendLobbyNameWhenReadyCoroutine != null)
            {
                StopCoroutine(sendLobbyNameWhenReadyCoroutine);
                sendLobbyNameWhenReadyCoroutine = null;
            }
        }

        private async Task<bool> EnsureServicesReady()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    await UnityServices.InitializeAsync();
                }
            }
            catch (Exception ex)
            {
                statusText = $"Unity Services initialization failed: {ex.Message}";
                return false;
            }

            try
            {
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }
            }
            catch (Exception ex)
            {
                statusText = $"Anonymous sign-in failed: {ex.Message}";
                return false;
            }

            return true;
        }

        private bool ValidateNetworkManager(NetworkManager nm, out UnityTransport transport)
        {
            transport = null;

            if (nm == null)
            {
                statusText = "Error: NetworkManager is missing.";
                return false;
            }

            transport = nm.GetComponent<UnityTransport>();
            if (transport == null)
            {
                statusText = "Error: UnityTransport is missing.";
                return false;
            }

            // Note: PlayerPrefab may be intentionally null during the lobby phase
            // (we null it to prevent spawning in the MainMenu). Do not validate it here.

            return true;
        }

        private bool LoadMultiplayerSceneAsHost(NetworkManager nm)
        {
            if (nm == null)
            {
                statusText = "Error: NetworkManager is missing.";
                return false;
            }

            if (!nm.IsHost)
            {
                statusText = "Only host can start the match.";
                return false;
            }

            if (!nm.NetworkConfig.EnableSceneManagement)
            {
                statusText = "Error: Network scene management is disabled.";
                return false;
            }

            if (!RestoreGamePlayerPrefab(nm))
            {
                statusText = "Error: Network player prefab is missing.";
                return false;
            }

            if (nm.SceneManager != null)
            {
                nm.SceneManager.OnLoadEventCompleted -= OnNetworkSceneLoadCompleted;
                nm.SceneManager.OnLoadEventCompleted += OnNetworkSceneLoadCompleted;
            }

            nm.SceneManager.LoadScene(multiplayerSceneName, LoadSceneMode.Single);
            return true;
        }

        private bool RestoreGamePlayerPrefab(NetworkManager nm)
        {
            if (nm == null || nm.NetworkConfig == null)
            {
                return false;
            }

            if (nm.NetworkConfig.PlayerPrefab == null && _lobbyPlayerPrefab != null)
            {
                nm.NetworkConfig.PlayerPrefab = _lobbyPlayerPrefab;
                _lobbyPlayerPrefab = null;
            }

            return nm.NetworkConfig.PlayerPrefab != null;
        }

        private void OnNetworkSceneLoadCompleted(string sceneName, LoadSceneMode loadSceneMode, System.Collections.Generic.List<ulong> clientsCompleted, System.Collections.Generic.List<ulong> clientsTimedOut)
        {
            if (!string.Equals(sceneName, multiplayerSceneName, StringComparison.Ordinal))
            {
                return;
            }

            var nm = NetworkManager.Singleton;
            if (nm != null && nm.SceneManager != null)
            {
                nm.SceneManager.OnLoadEventCompleted -= OnNetworkSceneLoadCompleted;
            }

            if (nm == null || !nm.IsServer)
            {
                return;
            }

            if (spawnPlayersAfterLoadCoroutine != null)
            {
                StopCoroutine(spawnPlayersAfterLoadCoroutine);
            }

            spawnPlayersAfterLoadCoroutine = StartCoroutine(SpawnMissingPlayersAfterSceneLoad());
        }

        private IEnumerator SpawnMissingPlayersAfterSceneLoad()
        {
            yield return null;

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || !RestoreGamePlayerPrefab(nm))
            {
                spawnPlayersAfterLoadCoroutine = null;
                yield break;
            }

            foreach (ulong clientId in nm.ConnectedClientsIds)
            {
                if (!nm.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject != null)
                {
                    continue;
                }

                var player = Instantiate(nm.NetworkConfig.PlayerPrefab);
                ApplyManualSpawnPoint(player.transform);

                var networkObject = player.GetComponent<NetworkObject>();
                if (networkObject == null)
                {
                    Debug.LogError("[RelayPartyManager] Network player prefab is missing a NetworkObject component.");
                    Destroy(player);
                    continue;
                }

                networkObject.SpawnAsPlayerObject(clientId, true);
                Debug.Log($"[RelayPartyManager] Spawned missing player object for client {clientId} after scene load.");
            }

            spawnPlayersAfterLoadCoroutine = null;
        }

        private void ApplyManualSpawnPoint(Transform playerTransform)
        {
            if (playerTransform == null)
            {
                return;
            }

            var spawnPoints = UnityEngine.Object.FindObjectsOfType<NetworkStartPosition>(true);
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                return;
            }

            var spawnPoint = spawnPoints[nextManualSpawnIndex % spawnPoints.Length];
            nextManualSpawnIndex++;

            if (spawnPoint != null)
            {
                playerTransform.SetPositionAndRotation(spawnPoint.transform.position, spawnPoint.transform.rotation);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.name, multiplayerSceneName, StringComparison.Ordinal))
            {
                return;
            }

            DisableNonNetworkPlayersInMultiplayerScene();
        }

        private void DisableNonNetworkPlayersInMultiplayerScene()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                return;
            }

            var players = UnityEngine.Object.FindObjectsOfType<PlayerController>(true);
            foreach (var player in players)
            {
                if (player == null)
                {
                    continue;
                }

                var networkObject = player.GetComponent<NetworkObject>() ?? player.GetComponentInParent<NetworkObject>();
                if (networkObject == null)
                {
                    player.gameObject.SetActive(false);
                }
            }
        }

        private bool TryStartPendingHost(NetworkManager nm)
        {
            if (nm == null)
            {
                statusText = "Error: NetworkManager is missing.";
                return false;
            }

            if (nm.IsListening && nm.IsHost)
            {
                return true;
            }

            if (!ValidateNetworkManager(nm, out var transport))
            {
                return false;
            }

            if (!hasPendingHostAllocation || pendingHostAllocation == null)
            {
                statusText = "No pending lobby. Create a lobby first.";
                return false;
            }

            // Shut down any stale transport state before starting host.
            if (nm.IsListening)
            {
                nm.Shutdown();
            }

            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(pendingHostAllocation, relayConnectionType));
            ConfigureLobbyConnectionApproval(nm);

            if (!nm.StartHost())
            {
                statusText = "StartHost failed — check Relay allocation and Unity project settings.";
                return false;
            }

            hasPendingHostAllocation = false;
            pendingHostAllocation = null;
            return true;
        }

        private int GetLobbyPlayerLimit()
        {
            return HardLobbyPlayerLimit;
        }

        private int GetRelayConnectionLimit()
        {
            return GetLobbyPlayerLimit();
        }

        private void ConfigureLobbyConnectionApproval(NetworkManager nm)
        {
            if (nm == null)
            {
                return;
            }

            nm.NetworkConfig.ConnectionApproval = true;
            nm.ConnectionApprovalCallback -= OnLobbyConnectionApproval;
            nm.ConnectionApprovalCallback += OnLobbyConnectionApproval;
        }

        private void OnLobbyConnectionApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            var nm = NetworkManager.Singleton;
            int connectedPlayers = nm != null && nm.ConnectedClientsIds != null ? nm.ConnectedClientsIds.Count : 0;
            bool hasRoom = connectedPlayers < GetLobbyPlayerLimit();

            response.Approved = hasRoom;
            response.CreatePlayerObject = false;
            response.PlayerPrefabHash = null;
            response.Position = null;
            response.Rotation = null;
            response.Pending = false;
            response.Reason = hasRoom ? string.Empty : "Lobby is full. Create a new lobby.";
        }

        private void AutoBindMenuUI()
        {
            if (SceneManager.GetActiveScene().name != menuSceneName)
            {
                return;
            }

            if (createLobbyButton == null || IsLobbyCodeDisplayName(createLobbyButton.name))
            {
                createLobbyButton = FindButtonByExactName("CreateLobby")
                    ?? FindButtonByExactName("Create Lobby")
                    ?? FindButtonByNameContains("create lobby");
            }

            if (joinLobbyButton == null)
            {
                joinLobbyButton = FindButtonByNameContains("join lobby");
                if (joinLobbyButton == null)
                {
                    joinLobbyButton = FindButtonByNameContains("join");
                }
            }

            if (startGameButton == null)
            {
                startGameButton = FindButtonByNameContains("start");
            }

            if (backButton == null)
            {
                backButton = FindButtonByNameContains("back");
            }

            if (leaveLobbyButton == null)
            {
                leaveLobbyButton = FindButtonByNameContains("leave lobby")
                    ?? FindButtonByNameContains("leave");
            }

            if (settingsButton == null)
            {
                settingsButton = FindButtonByNameContains("settings")
                    ?? FindButtonByNameContains("setting");
            }

            if (joinCodeInputField == null)
            {
                joinCodeInputField = FindObjectByNameContains<TMP_InputField>("join code");
                if (joinCodeInputField == null)
                {
                    joinCodeInputField = FindObjectByNameContains<TMP_InputField>("joinlobbyinput");
                }
                if (joinCodeInputField == null)
                {
                    joinCodeInputField = FindObjectByNameContains<TMP_InputField>("join lobby input");
                }
                if (joinCodeInputField == null)
                {
                    var allInputs = GetAllSceneObjectsOfType<TMP_InputField>();
                    foreach (TMP_InputField input in allInputs)
                    {
                        if (input == null || IsUsernameInputName(input.name))
                        {
                            continue;
                        }

                        joinCodeInputField = input;
                        break;
                    }
                }
            }

            if (usernameInputField == null)
            {
                usernameInputField = FindObjectByNameContains<TMP_InputField>("username")
                    ?? FindObjectByNameContains<TMP_InputField>("user name")
                    ?? FindObjectByNameContains<TMP_InputField>("playername")
                    ?? FindObjectByNameContains<TMP_InputField>("player name")
                    ?? FindObjectByNameContains<TMP_InputField>("name input");
            }

            if (lobbyCodeText == null)
            {
                lobbyCodeText = FindTextByNameContains("code");
            }

            if (lobbyCodeDisplayButton == null)
            {
                lobbyCodeDisplayButton = FindButtonByNameContains("code");
            }

            if (loadingIndicatorText == null)
            {
                loadingIndicatorText = FindTextByNameContains("loading");
            }
        }

        private void WireMenuUI()
        {
            if (createLobbyButton != null)
            {
                createLobbyButton.onClick.RemoveListener(CreateLobbyFromUI);
                createLobbyButton.onClick.AddListener(CreateLobbyFromUI);
                createLobbyButton.interactable = true;
            }

            if (joinLobbyButton != null)
            {
                joinLobbyButton.onClick.RemoveListener(JoinLobbyFromUI);
                joinLobbyButton.onClick.AddListener(JoinLobbyFromUI);
                joinLobbyButton.interactable = true;
            }

            if (startGameButton != null)
            {
                startGameButton.onClick.RemoveListener(StartMatchFromUI);
                startGameButton.onClick.AddListener(StartMatchFromUI);
                startGameButton.interactable = true;
            }

            if (backButton != null)
            {
                backButton.onClick.RemoveListener(BackFromUI);
                backButton.onClick.AddListener(BackFromUI);
                backButton.interactable = true;
            }

            if (leaveLobbyButton != null)
            {
                leaveLobbyButton.onClick.RemoveListener(LeaveLobbyFromUI);
                leaveLobbyButton.onClick.AddListener(LeaveLobbyFromUI);
                leaveLobbyButton.interactable = true;
            }

            if (settingsButton != null)
            {
                settingsButton.onClick.RemoveListener(OpenSettingsFromUI);
                settingsButton.onClick.AddListener(OpenSettingsFromUI);
                settingsButton.interactable = true;
            }

            if (joinCodeInputField != null)
            {
                joinCodeInputField.onValueChanged.RemoveListener(OnJoinCodeChanged);
                joinCodeInputField.onValueChanged.AddListener(OnJoinCodeChanged);
            }

            if (lobbyCodeDisplayButton != null)
            {
                lobbyCodeDisplayButton.interactable = false;
            }
        }

        private void WireUsernameInput()
        {
            if (usernameInputField == null)
            {
                return;
            }

            string currentName = GetLocalUserName();
            usernameInputField.SetTextWithoutNotify(currentName);
            cachedLocalUserName = currentName;
            usernameInputField.onValueChanged.RemoveListener(OnUsernameChanged);
            usernameInputField.onValueChanged.AddListener(OnUsernameChanged);
        }

        private void OnJoinCodeChanged(string value)
        {
            joinCodeInput = value;
        }

        private void OnUsernameChanged(string value)
        {
            string cleanName = SanitizePlayerName(value);
            cachedLocalUserName = string.IsNullOrWhiteSpace(cleanName) ? "You" : cleanName;

            if (!string.IsNullOrWhiteSpace(cleanName))
            {
                PlayerPrefs.SetString("PlayerName", cleanName);
                PlayerPrefs.Save();
            }

            QueueSendLocalNameWhenReady(true);
            UpdateLobbyAstronautVisuals();
        }

        private void EnsureUsernameInputExists()
        {
            if (usernameInputField != null || SceneManager.GetActiveScene().name != menuSceneName)
            {
                return;
            }

            Canvas canvas = FindObjectByNameContains<Canvas>("canvas");
            if (canvas == null)
            {
                var canvases = GetAllSceneObjectsOfType<Canvas>();
                if (canvases.Length > 0)
                {
                    canvas = canvases[0];
                }
            }

            if (canvas == null)
            {
                return;
            }

            GameObject inputObject = new GameObject("UsernameInput");
            inputObject.transform.SetParent(canvas.transform, false);

            RectTransform inputRect = inputObject.AddComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0f, 1f);
            inputRect.anchorMax = new Vector2(0f, 1f);
            inputRect.pivot = new Vector2(0f, 1f);
            inputRect.anchoredPosition = new Vector2(28f, -28f);
            inputRect.sizeDelta = new Vector2(260f, 42f);

            Image inputBackground = inputObject.AddComponent<Image>();
            inputBackground.color = new Color(0f, 0f, 0f, 0.45f);

            usernameInputField = inputObject.AddComponent<TMP_InputField>();
            usernameInputField.characterLimit = 16;

            GameObject textObject = new GameObject("Text");
            textObject.transform.SetParent(inputObject.transform, false);
            RectTransform textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 5f);
            textRect.offsetMax = new Vector2(-12f, -5f);
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = 22f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.text = "";

            GameObject placeholderObject = new GameObject("Placeholder");
            placeholderObject.transform.SetParent(inputObject.transform, false);
            RectTransform placeholderRect = placeholderObject.AddComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(12f, 5f);
            placeholderRect.offsetMax = new Vector2(-12f, -5f);
            TextMeshProUGUI placeholder = placeholderObject.AddComponent<TextMeshProUGUI>();
            placeholder.fontSize = 22f;
            placeholder.color = new Color(1f, 1f, 1f, 0.45f);
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            placeholder.text = "Username";

            usernameInputField.textComponent = text;
            usernameInputField.placeholder = placeholder;
            usernameInputField.targetGraphic = inputBackground;
        }

        private void EnsureLeaveLobbyButtonExists()
        {
            if (leaveLobbyButton != null || SceneManager.GetActiveScene().name != menuSceneName)
            {
                return;
            }

            Canvas canvas = FindObjectByNameContains<Canvas>("canvas");
            if (canvas == null)
            {
                var canvases = GetAllSceneObjectsOfType<Canvas>();
                if (canvases.Length > 0)
                {
                    canvas = canvases[0];
                }
            }

            if (canvas == null)
            {
                return;
            }

            GameObject buttonObject = new GameObject("LeaveLobbyButton");
            buttonObject.transform.SetParent(canvas.transform, false);

            RectTransform buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 1f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.pivot = new Vector2(1f, 1f);
            buttonRect.anchoredPosition = new Vector2(-28f, -28f);
            buttonRect.sizeDelta = new Vector2(180f, 42f);

            Image background = buttonObject.AddComponent<Image>();
            background.color = new Color(0.12f, 0.12f, 0.12f, 0.82f);

            leaveLobbyButton = buttonObject.AddComponent<Button>();
            leaveLobbyButton.targetGraphic = background;

            GameObject labelObject = new GameObject("Text");
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
            label.text = "Leave Lobby";
            label.fontSize = 22f;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
        }

        private void EnsureSettingsTabExists()
        {
            if (SceneManager.GetActiveScene().name != menuSceneName)
            {
                return;
            }

            Canvas canvas = FindObjectByNameContains<Canvas>("canvas");
            if (canvas == null)
            {
                var canvases = GetAllSceneObjectsOfType<Canvas>();
                if (canvases.Length > 0)
                {
                    canvas = canvases[0];
                }
            }

            if (canvas == null)
            {
                return;
            }

            if (canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }

            if (settingsButton == null)
            {
                settingsButton = CreateMenuButton(canvas.transform, "SettingsButton", "Settings", new Vector2(0f, 1f), new Vector2(180f, 42f), new Vector2(118f, -82f), TextAlignmentOptions.Center);
            }

            if (settingsPanel != null)
            {
                return;
            }

            settingsPanel = new GameObject("SettingsPanel");
            settingsPanel.transform.SetParent(canvas.transform, false);

            RectTransform panelRect = settingsPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(560f, 360f);

            Image panelBackground = settingsPanel.AddComponent<Image>();
            panelBackground.color = new Color(0.015f, 0.025f, 0.025f, 0.96f);

            Outline outline = settingsPanel.AddComponent<Outline>();
            outline.effectColor = new Color(0.65f, 0.1f, 0.08f, 0.95f);
            outline.effectDistance = new Vector2(2f, -2f);

            TextMeshProUGUI title = CreateMenuText(settingsPanel.transform, "Title", "SETTINGS", 34f, Color.white, TextAlignmentOptions.Center);
            SetMenuRect(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(500f, 52f));

            TextMeshProUGUI label = CreateMenuText(settingsPanel.transform, "SensitivityLabel", "MOUSE SENSITIVITY", 20f, new Color(0.65f, 0.95f, 1f), TextAlignmentOptions.Left);
            SetMenuRect(label.gameObject, new Vector2(0.5f, 0.5f), new Vector2(-95f, 55f), new Vector2(290f, 36f));

            sensitivityValueText = CreateMenuText(settingsPanel.transform, "SensitivityValue", "", 20f, Color.white, TextAlignmentOptions.Right);
            SetMenuRect(sensitivityValueText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(190f, 55f), new Vector2(110f, 36f));

            GameObject sliderObject = new GameObject("SensitivitySlider");
            sliderObject.transform.SetParent(settingsPanel.transform, false);
            SetMenuRect(sliderObject, new Vector2(0.5f, 0.5f), new Vector2(0f, 5f), new Vector2(420f, 32f));

            Image sliderBackground = sliderObject.AddComponent<Image>();
            sliderBackground.color = new Color(0.04f, 0.07f, 0.08f, 0.95f);

            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderObject.transform, false);
            RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = new Vector2(10f, 8f);
            fillAreaRect.offsetMax = new Vector2(-10f, -8f);

            GameObject fillObject = new GameObject("Fill");
            fillObject.transform.SetParent(fillArea.transform, false);
            RectTransform fillRect = fillObject.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            Image fillImage = fillObject.AddComponent<Image>();
            fillImage.color = new Color(0.65f, 0.1f, 0.08f, 1f);

            GameObject handleObject = new GameObject("Handle");
            handleObject.transform.SetParent(sliderObject.transform, false);
            SetMenuRect(handleObject, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22f, 42f));
            Image handleImage = handleObject.AddComponent<Image>();
            handleImage.color = new Color(0.9f, 0.95f, 0.92f, 1f);

            sensitivitySlider = sliderObject.AddComponent<Slider>();
            sensitivitySlider.minValue = 0.25f;
            sensitivitySlider.maxValue = 8f;
            sensitivitySlider.targetGraphic = handleImage;
            sensitivitySlider.fillRect = fillRect;
            sensitivitySlider.handleRect = handleImage.rectTransform;

            settingsBackButton = CreateMenuButton(settingsPanel.transform, "SettingsBackButton", "Back", new Vector2(0.5f, 0.5f), new Vector2(220f, 46f), new Vector2(0f, -105f), TextAlignmentOptions.Center);
            settingsPanel.SetActive(false);
        }

        private void WireSettingsUI()
        {
            if (settingsBackButton != null)
            {
                settingsBackButton.onClick.RemoveListener(CloseSettingsFromUI);
                settingsBackButton.onClick.AddListener(CloseSettingsFromUI);
                settingsBackButton.interactable = true;
            }

            if (sensitivitySlider != null)
            {
                sensitivitySlider.onValueChanged.RemoveListener(OnSensitivityChanged);
                sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
                sensitivitySlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(MouseSensitivityPrefsKey, 2f));
                OnSensitivityChanged(sensitivitySlider.value);
            }
        }

        public void OpenSettingsFromUI()
        {
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(true);
            }
        }

        public void CloseSettingsFromUI()
        {
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(false);
            }
        }

        private void OnSensitivityChanged(float value)
        {
            float rounded = Mathf.Round(value * 100f) / 100f;
            PlayerPrefs.SetFloat(MouseSensitivityPrefsKey, rounded);
            PlayerPrefs.Save();

            if (sensitivityValueText != null)
            {
                sensitivityValueText.text = rounded.ToString("0.00");
            }
        }

        private static Button CreateMenuButton(Transform parent, string name, string labelText, Vector2 anchor, Vector2 size, Vector2 position, TextAlignmentOptions alignment)
        {
            GameObject buttonObject = new GameObject(name);
            buttonObject.transform.SetParent(parent, false);
            SetMenuRect(buttonObject, anchor, position, size);

            Image background = buttonObject.AddComponent<Image>();
            background.color = new Color(0.055f, 0.095f, 0.1f, 0.95f);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;

            TextMeshProUGUI label = CreateMenuText(buttonObject.transform, "Text", labelText, 22f, Color.white, alignment);
            SetMenuRect(label.gameObject, new Vector2(0.5f, 0.5f), Vector2.zero, size);

            return button;
        }

        private static TextMeshProUGUI CreateMenuText(Transform parent, string name, string text, float fontSize, Color color, TextAlignmentOptions alignment)
        {
            GameObject textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            TextMeshProUGUI textComponent = textObject.AddComponent<TextMeshProUGUI>();
            textComponent.text = text;
            textComponent.fontSize = fontSize;
            textComponent.color = color;
            textComponent.alignment = alignment;
            textComponent.fontStyle = FontStyles.Bold;
            textComponent.enableWordWrapping = false;
            return textComponent;
        }

        private static void SetMenuRect(GameObject target, Vector2 anchor, Vector2 position, Vector2 size)
        {
            RectTransform rect = target.GetComponent<RectTransform>();
            if (rect == null)
            {
                rect = target.AddComponent<RectTransform>();
            }

            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private void EnsureMenuEventSystem()
        {
            if (SceneManager.GetActiveScene().name != menuSceneName)
            {
                return;
            }

            if (FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }

        private string ReadJoinCodeFromUI()
        {
            string code = joinCodeInputField != null ? joinCodeInputField.text : string.Empty;
            if (!string.IsNullOrWhiteSpace(code))
            {
                return code.Trim();
            }

            var fallbackField = FindObjectByNameContains<TMP_InputField>("joinlobbyinput")
                ?? FindObjectByNameContains<TMP_InputField>("join lobby input")
                ?? FindObjectByNameContains<TMP_InputField>("join code");

            if (fallbackField != null && !string.IsNullOrWhiteSpace(fallbackField.text))
            {
                joinCodeInputField = fallbackField;
                return fallbackField.text.Trim();
            }

            return string.IsNullOrWhiteSpace(joinCodeInput) ? string.Empty : joinCodeInput.Trim();
        }

        private void RefreshLobbyCodeUI()
        {
            if (lobbyCodeText == null)
            {
                // Fall back to the button label if text wasn't wired.
                UpdateLobbyCodeButtonLabel(string.IsNullOrEmpty(latestJoinCode) ? "" : latestJoinCode);
                return;
            }

            if (string.IsNullOrEmpty(latestJoinCode))
            {
                lobbyCodeText.text = "";
                UpdateLobbyCodeButtonLabel("");
                return;
            }

            lobbyCodeText.text = latestJoinCode;
            UpdateLobbyCodeButtonLabel(latestJoinCode);
        }

        private void NormalizeLegacySceneTarget()
        {
            if (string.Equals(multiplayerSceneName, "Night 1", StringComparison.OrdinalIgnoreCase))
            {
                multiplayerSceneName = "Night1_MultiplayerTest";
            }
        }

        private void UpdateLobbyCodeButtonLabel(string value)
        {
            if (lobbyCodeDisplayButton == null)
            {
                return;
            }

            TMP_Text label = lobbyCodeDisplayButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = value;
            }
        }

        private void SetLobbyUIState(bool created)
        {
            bool showCreateAndJoin = !created;
            var nm = NetworkManager.Singleton;
            bool showOnlyLeave = created && nm != null && nm.IsListening;
            bool isHostLobby = false;

            if (createLobbyButton != null)
            {
                createLobbyButton.gameObject.SetActive(showCreateAndJoin && !showOnlyLeave);
            }

            if (joinLobbyButton != null)
            {
                joinLobbyButton.gameObject.SetActive(showCreateAndJoin && !showOnlyLeave);
            }

            if (joinCodeInputField != null)
            {
                joinCodeInputField.gameObject.SetActive(showCreateAndJoin && !showOnlyLeave);
            }

            if (lobbyCodeDisplayButton != null)
            {
                lobbyCodeDisplayButton.gameObject.SetActive(isHostLobby);
            }

            if (lobbyCodeText != null)
            {
                lobbyCodeText.gameObject.SetActive(isHostLobby);
            }

            if (startGameButton != null)
            {
                startGameButton.gameObject.SetActive(isHostLobby && !showOnlyLeave);
            }

            if (backButton != null)
            {
                backButton.gameObject.SetActive(isHostLobby && !showOnlyLeave);
            }

            if (leaveLobbyButton != null)
            {
                leaveLobbyButton.gameObject.SetActive(created);
            }

            if (settingsButton != null)
            {
                settingsButton.gameObject.SetActive(!showOnlyLeave);
            }

            if (settingsPanel != null && showOnlyLeave)
            {
                settingsPanel.SetActive(false);
            }
        }

        private void AutoBindLobbyVisuals()
        {
            if (SceneManager.GetActiveScene().name != menuSceneName)
            {
                return;
            }

            if (player1Visual == null)
            {
                var t = FindObjectByNameContains<Transform>("player1");
                if (t != null) player1Visual = t.gameObject;
            }

            if (player2Visual == null)
            {
                var t = FindObjectByNameContains<Transform>("player2");
                if (t != null) player2Visual = t.gameObject;
            }

            if (player3Visual == null)
            {
                var t = FindObjectByNameContains<Transform>("player3");
                if (t != null) player3Visual = t.gameObject;
            }

            player1NameText = EnsureNameTag(player1Visual, player1NameText, "Player1NameTag");
            player2NameText = EnsureNameTag(player2Visual, player2NameText, "Player2NameTag");
            player3NameText = EnsureNameTag(player3Visual, player3NameText, "Player3NameTag");
            InitializeAstronautFloatSlots(false);

            if (disableAstronautAnimatorsInMenu)
            {
                DisableAstronautAnimator(player1Visual);
                DisableAstronautAnimator(player2Visual);
                DisableAstronautAnimator(player3Visual);
            }
        }

        private TextMeshPro EnsureNameTag(GameObject anchor, TextMeshPro current, string tagName)
        {
            if (anchor == null)
            {
                return current;
            }

            if (current != null)
            {
                current.transform.localPosition = new Vector3(0f, -0.45f, 0f);
                current.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                current.transform.localScale = Vector3.one * 0.18f;
                return current;
            }

            Transform existing = anchor.transform.Find(tagName);
            if (existing != null)
            {
                existing.localPosition = new Vector3(0f, -0.45f, 0f);
                existing.localRotation = Quaternion.Euler(0f, 180f, 0f);
                existing.localScale = Vector3.one * 0.18f;
                return existing.GetComponent<TextMeshPro>();
            }

            var go = new GameObject(tagName);
            go.transform.SetParent(anchor.transform, false);
            go.transform.localPosition = new Vector3(0f, -0.45f, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = Vector3.one * 0.18f;

            var text = go.AddComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 7.5f;
            text.text = string.Empty;
            text.color = Color.white;
            text.enableAutoSizing = false;
            return text;
        }

        private void EnsureLoadingIndicatorExists()
        {
            if (loadingIndicatorText != null || SceneManager.GetActiveScene().name != menuSceneName)
            {
                return;
            }

            Canvas canvas = FindObjectByNameContains<Canvas>("canvas");
            if (canvas == null)
            {
                var anyCanvas = GetAllSceneObjectsOfType<Canvas>();
                if (anyCanvas.Length > 0)
                {
                    canvas = anyCanvas[0];
                }
            }

            if (canvas == null)
            {
                return;
            }

            var go = new GameObject("LoadingIndicator");
            go.transform.SetParent(canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-20f, 20f);
            rect.sizeDelta = new Vector2(420f, 60f);

            loadingIndicatorText = go.AddComponent<TextMeshProUGUI>();
            loadingIndicatorText.alignment = TextAlignmentOptions.BottomRight;
            loadingIndicatorText.fontSize = 22f;
            loadingIndicatorText.color = new Color(0.8f, 0.95f, 1f, 0.9f);
            loadingIndicatorText.text = string.Empty;
        }

        private void UpdateLoadingIndicator()
        {
            if (loadingIndicatorText == null)
            {
                return;
            }

            loadingAnimTimer += Time.deltaTime;
            if (loadingAnimTimer >= 0.4f)
            {
                loadingAnimTimer = 0f;
                loadingDots = (loadingDots + 1) % 4;
            }

            string dots = new string('.', loadingDots);
            string prefix = isBusy ? "LOADING" : "READY";
            loadingIndicatorText.text = $"{prefix}{dots}  {statusText}";
        }

        private void UpdateLobbyAstronautVisuals()
        {
            var nm = NetworkManager.Singleton;
            bool isConnected = nm != null && nm.IsListening;
            bool isClientLobby = nm != null && nm.IsClient && !nm.IsHost && nm.IsConnectedClient;
            bool inActiveLobby = isConnected && (lobbyCreated || HasActiveHostParty || isClientLobby);
            string localName = GetLocalUserName();

            if (inActiveLobby)
            {
                RegisterLobbyNameMessagesIfReady();
                ulong localClientId = nm.LocalClientId;
                lobbyPlayerNames[localClientId] = localName;
            }

            List<ulong> lobbyClientIds = inActiveLobby ? GetOrderedLobbyClientIds() : new List<ulong>();
            int playerCount = inActiveLobby ? Mathf.Clamp(lobbyClientIds.Count, 1, 3) : 1;

            string slot1Name = localName;
            string slot2Name = "Player 2";
            string slot3Name = "Player 3";

            if (inActiveLobby && lobbyClientIds.Count > 0)
            {
                slot1Name = GetLobbyDisplayName(lobbyClientIds[0], "Player 1");
                if (lobbyClientIds.Count > 1)
                {
                    slot2Name = GetLobbyDisplayName(lobbyClientIds[1], "Player 2");
                }

                if (lobbyClientIds.Count > 2)
                {
                    slot3Name = GetLobbyDisplayName(lobbyClientIds[2], "Player 3");
                }
            }

            SetAstronautSlot(player1Visual, player1NameText, playerCount >= 1, slot1Name);
            SetAstronautSlot(player2Visual, player2NameText, playerCount >= 2, slot2Name);
            SetAstronautSlot(player3Visual, player3NameText, playerCount >= 3, slot3Name);
        }

        private void InitializeAstronautFloatSlots(bool force)
        {
            astronautVisualSlots[0] = player1Visual;
            astronautVisualSlots[1] = player2Visual;
            astronautVisualSlots[2] = player3Visual;

            for (int i = 0; i < astronautVisualSlots.Length; i++)
            {
                GameObject visual = astronautVisualSlots[i];
                if (visual == null)
                {
                    astronautFloatInitialized[i] = false;
                    continue;
                }

                if (!force && astronautFloatInitialized[i])
                {
                    continue;
                }

                astronautBaseLocalPositions[i] = visual.transform.localPosition;
                astronautBaseLocalRotations[i] = visual.transform.localRotation;
                astronautFloatOffsets[i] = Vector2.zero;
                astronautRotationOffsets[i] = 0f;

                float speed = Mathf.Lerp(astronautFloatSpeedRange.x, astronautFloatSpeedRange.y, (i + 1f) / astronautVisualSlots.Length);
                float xDirection = i % 2 == 0 ? 1f : -1f;
                float yDirection = i == 1 ? -1f : 1f;
                astronautFloatVelocities[i] = new Vector2(speed * xDirection, speed * 0.72f * yDirection);
                float rotationSpeed = Mathf.Lerp(astronautRotationSpeedRange.x, astronautRotationSpeedRange.y, i / Mathf.Max(1f, astronautVisualSlots.Length - 1f));
                astronautRotationSpeeds[i] = rotationSpeed * (i % 2 == 0 ? 1f : -1f);
                astronautFloatInitialized[i] = true;
            }
        }

        private void UpdateMenuAstronautFloating()
        {
            if (!floatAstronautsInMenu || SceneManager.GetActiveScene().name != menuSceneName)
            {
                return;
            }

            InitializeAstronautFloatSlots(false);

            for (int i = 0; i < astronautVisualSlots.Length; i++)
            {
                GameObject visual = astronautVisualSlots[i];
                if (visual == null || !astronautFloatInitialized[i])
                {
                    continue;
                }

                Vector2 offset = astronautFloatOffsets[i] + astronautFloatVelocities[i] * Time.deltaTime;
                Vector2 velocity = astronautFloatVelocities[i];

                if (Mathf.Abs(offset.x) > astronautFloatBounds.x)
                {
                    offset.x = Mathf.Sign(offset.x) * astronautFloatBounds.x;
                    velocity.x = -velocity.x;
                }

                if (Mathf.Abs(offset.y) > astronautFloatBounds.y)
                {
                    offset.y = Mathf.Sign(offset.y) * astronautFloatBounds.y;
                    velocity.y = -velocity.y;
                }

                astronautFloatOffsets[i] = offset;
                astronautFloatVelocities[i] = velocity;

                float bob = Mathf.Sin((Time.time + i * 0.73f) * astronautFloatBobSpeed) * astronautFloatBobAmplitude;
                Vector3 basePosition = astronautBaseLocalPositions[i];
                visual.transform.localPosition = basePosition + new Vector3(offset.x, offset.y + bob, 0f);

                astronautRotationOffsets[i] = Mathf.Repeat(astronautRotationOffsets[i] + astronautRotationSpeeds[i] * Time.deltaTime, 360f);
                visual.transform.localRotation = astronautBaseLocalRotations[i] * Quaternion.Euler(0f, astronautRotationOffsets[i], 0f);
            }

            KeepNameTagsReadable();
        }

        private void KeepNameTagsReadable()
        {
            KeepNameTagReadable(player1NameText);
            KeepNameTagReadable(player2NameText);
            KeepNameTagReadable(player3NameText);
        }

        private static void KeepNameTagReadable(TextMeshPro nameText)
        {
            if (nameText == null)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Transform textTransform = nameText.transform;
            Vector3 directionToCamera = camera.transform.position - textTransform.position;
            if (directionToCamera.sqrMagnitude < 0.0001f)
            {
                return;
            }

            textTransform.rotation = Quaternion.LookRotation(-directionToCamera.normalized, camera.transform.up);
        }

        private void RegisterLobbyNameMessagesIfReady()
        {
            var nm = NetworkManager.Singleton;
            if (lobbyNameMessagesRegistered || nm == null || nm.CustomMessagingManager == null || !nm.IsListening)
            {
                return;
            }

            nm.CustomMessagingManager.RegisterNamedMessageHandler(LobbyNameUpdateMessage, OnLobbyNameUpdateMessage);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(LobbyNameBroadcastMessage, OnLobbyNameBroadcastMessage);
            lobbyNameMessagesRegistered = true;
        }

        private void UnregisterLobbyNameMessages()
        {
            var nm = NetworkManager.Singleton;
            if (!lobbyNameMessagesRegistered || nm == null || nm.CustomMessagingManager == null)
            {
                return;
            }

            nm.CustomMessagingManager.UnregisterNamedMessageHandler(LobbyNameUpdateMessage);
            nm.CustomMessagingManager.UnregisterNamedMessageHandler(LobbyNameBroadcastMessage);
            lobbyNameMessagesRegistered = false;
        }

        private void SyncLocalNameIfNeeded()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                return;
            }

            string localName = GetLocalUserName();
            if (string.Equals(localName, lastSentLocalUserName, StringComparison.Ordinal))
            {
                return;
            }

            QueueSendLocalNameWhenReady(false);
        }

        private void SendInitialNameAfterClientConnect()
        {
            var nm = NetworkManager.Singleton;
            if (sentInitialNameAfterConnect || nm == null || !nm.IsClient || nm.IsHost || !nm.IsConnectedClient)
            {
                return;
            }

            sentInitialNameAfterConnect = true;
            QueueSendLocalNameWhenReady(true);
        }

        private void QueueSendLocalNameWhenReady(bool force)
        {
            if (force)
            {
                lastSentLocalUserName = string.Empty;
            }

            if (sendLobbyNameWhenReadyCoroutine != null)
            {
                return;
            }

            sendLobbyNameWhenReadyCoroutine = StartCoroutine(SendLocalNameWhenReadyRoutine());
        }

        private IEnumerator SendLocalNameWhenReadyRoutine()
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                var nm = NetworkManager.Singleton;
                RegisterLobbyNameMessagesIfReady();

                if (CanSendLobbyNameNow(nm))
                {
                    SendLocalNameToLobby();
                    sendLobbyNameWhenReadyCoroutine = null;
                    yield break;
                }

                yield return new WaitForSecondsRealtime(0.1f);
            }

            sendLobbyNameWhenReadyCoroutine = null;
        }

        private static bool CanSendLobbyNameNow(NetworkManager nm)
        {
            if (nm == null || !nm.IsListening || nm.CustomMessagingManager == null)
            {
                return false;
            }

            return nm.IsServer || nm.IsConnectedClient;
        }

        private void SendLocalNameToLobby()
        {
            var nm = NetworkManager.Singleton;
            if (!CanSendLobbyNameNow(nm))
            {
                return;
            }

            RegisterLobbyNameMessagesIfReady();
            if (!lobbyNameMessagesRegistered)
            {
                return;
            }

            string localName = GetLocalUserName();
            lastSentLocalUserName = localName;
            lobbyPlayerNames[nm.LocalClientId] = localName;

            if (nm.IsServer)
            {
                BroadcastLobbyName(nm.LocalClientId, localName);
                BroadcastAllLobbyNames();
                return;
            }

            if (nm.CustomMessagingManager == null)
            {
                return;
            }

            FixedString64Bytes fixedName = localName;
            using FastBufferWriter writer = new FastBufferWriter(80, Allocator.Temp);
            writer.WriteValueSafe(fixedName);
            nm.CustomMessagingManager.SendNamedMessage(LobbyNameUpdateMessage, NetworkManager.ServerClientId, writer);
        }

        private void OnLobbyNameUpdateMessage(ulong senderClientId, FastBufferReader reader)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer)
            {
                return;
            }

            reader.ReadValueSafe(out FixedString64Bytes fixedName);
            string cleanName = SanitizePlayerName(fixedName.ToString());
            if (string.IsNullOrWhiteSpace(cleanName))
            {
                cleanName = GetFallbackLobbyPlayerName(senderClientId);
            }

            lobbyPlayerNames[senderClientId] = cleanName;
            BroadcastLobbyName(senderClientId, cleanName);
            BroadcastAllLobbyNames();
            UpdateLobbyAstronautVisuals();
        }

        private void OnLobbyNameBroadcastMessage(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out ulong clientId);
            reader.ReadValueSafe(out FixedString64Bytes fixedName);
            string cleanName = SanitizePlayerName(fixedName.ToString());

            if (string.IsNullOrWhiteSpace(cleanName))
            {
                lobbyPlayerNames.Remove(clientId);
            }
            else
            {
                lobbyPlayerNames[clientId] = cleanName;
            }

            UpdateLobbyAstronautVisuals();
        }

        private void BroadcastAllLobbyNamesToClient(ulong targetClientId)
        {
            foreach (var pair in lobbyPlayerNames)
            {
                BroadcastLobbyName(pair.Key, pair.Value, targetClientId);
            }
        }

        private void BroadcastAllLobbyNames()
        {
            foreach (var pair in lobbyPlayerNames)
            {
                BroadcastLobbyName(pair.Key, pair.Value);
            }
        }

        private void BroadcastLobbyName(ulong clientId, string displayName)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || nm.ConnectedClientsIds == null)
            {
                return;
            }

            foreach (ulong targetClientId in nm.ConnectedClientsIds)
            {
                BroadcastLobbyName(clientId, displayName, targetClientId);
            }
        }

        private void BroadcastLobbyName(ulong clientId, string displayName, ulong targetClientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.CustomMessagingManager == null)
            {
                return;
            }

            FixedString64Bytes fixedName = SanitizePlayerName(displayName);
            using FastBufferWriter writer = new FastBufferWriter(96, Allocator.Temp);
            writer.WriteValueSafe(clientId);
            writer.WriteValueSafe(fixedName);
            nm.CustomMessagingManager.SendNamedMessage(LobbyNameBroadcastMessage, targetClientId, writer);
        }

        private List<ulong> GetOrderedLobbyClientIds()
        {
            List<ulong> ids = new List<ulong>();
            var nm = NetworkManager.Singleton;

            if (nm != null && nm.IsListening && nm.ConnectedClientsIds != null)
            {
                foreach (ulong clientId in nm.ConnectedClientsIds)
                {
                    if (!ids.Contains(clientId))
                    {
                        ids.Add(clientId);
                    }
                }
            }

            foreach (ulong clientId in lobbyPlayerNames.Keys)
            {
                if (!ids.Contains(clientId))
                {
                    ids.Add(clientId);
                }
            }

            if (nm != null && nm.IsListening && !ids.Contains(nm.LocalClientId))
            {
                ids.Add(nm.LocalClientId);
            }

            if (ids.Count == 0)
            {
                ids.Add(0);
            }

            ids.Sort();
            return ids;
        }

        private string GetFallbackLobbyPlayerName(ulong clientId)
        {
            List<ulong> ids = GetOrderedLobbyClientIds();
            if (!ids.Contains(clientId))
            {
                ids.Add(clientId);
                ids.Sort();
            }

            int playerNumber = Mathf.Clamp(ids.IndexOf(clientId) + 1, 1, GetLobbyPlayerLimit());
            return $"Player {playerNumber}";
        }

        private string GetLobbyDisplayName(ulong clientId, string fallback)
        {
            if (lobbyPlayerNames.TryGetValue(clientId, out string displayName) && !string.IsNullOrWhiteSpace(displayName))
            {
                return displayName;
            }

            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening && clientId == nm.LocalClientId)
            {
                return GetLocalUserName();
            }

            return fallback;
        }

        private int GetLobbyPlayerCount()
        {
            var nm = NetworkManager.Singleton;
            bool joinedAsClient = nm != null && nm.IsClient && !nm.IsHost && nm.IsListening;
            bool inLobbyState = lobbyCreated || HasActiveHostParty || joinedAsClient;

            if (!inLobbyState)
            {
                return 1;
            }

            if (nm == null || !nm.IsListening)
            {
                return 1;
            }

            int count = 0;
            if (nm.ConnectedClientsIds != null)
            {
                count = nm.ConnectedClientsIds.Count;
            }
            else if (nm.ConnectedClients != null)
            {
                count = nm.ConnectedClients.Count;
            }

            return Mathf.Clamp(count, 1, 3);
        }

        private static void DisableAstronautAnimator(GameObject visual)
        {
            if (visual == null)
            {
                return;
            }

            var animator = visual.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.enabled)
            {
                animator.enabled = false;
            }
        }

        private static void SetAstronautSlot(GameObject visual, TextMeshPro nameText, bool active, string displayName)
        {
            if (visual != null && visual.activeSelf != active)
            {
                visual.SetActive(active);
            }

            if (nameText != null)
            {
                if (nameText.gameObject.activeSelf != active)
                {
                    nameText.gameObject.SetActive(active);
                }

                if (active)
                {
                    nameText.text = displayName;
                }
            }
        }

        private string GetLocalUserName()
        {
            if (!string.IsNullOrWhiteSpace(cachedLocalUserName))
            {
                return cachedLocalUserName;
            }

            string playerPrefName = PlayerPrefs.GetString("PlayerName", string.Empty);
            if (!string.IsNullOrWhiteSpace(playerPrefName))
            {
                cachedLocalUserName = SanitizePlayerName(playerPrefName);
                return cachedLocalUserName;
            }

            if (UnityServices.State == ServicesInitializationState.Initialized)
            {
                try
                {
                    if (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
                    {
                        if (!string.IsNullOrWhiteSpace(AuthenticationService.Instance.PlayerName))
                        {
                            cachedLocalUserName = SanitizePlayerName(AuthenticationService.Instance.PlayerName);
                            return cachedLocalUserName;
                        }

                        string playerId = AuthenticationService.Instance.PlayerId;
                        if (!string.IsNullOrWhiteSpace(playerId))
                        {
                            cachedLocalUserName = $"Player_{playerId.Substring(0, Mathf.Min(6, playerId.Length))}";
                            return cachedLocalUserName;
                        }
                    }
                }
                catch
                {
                    // Services/auth might still be spinning up; fall back below.
                }
            }

            return "You";
        }

        private static string SanitizePlayerName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            if (trimmed.Length > 16)
            {
                trimmed = trimmed.Substring(0, 16);
            }

            return trimmed;
        }

        private static bool IsUsernameInputName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return false;
            }

            string lowerName = objectName.ToLowerInvariant();
            return lowerName.Contains("username") ||
                lowerName.Contains("user name") ||
                lowerName.Contains("playername") ||
                lowerName.Contains("player name") ||
                lowerName.Contains("name input");
        }

        private static bool IsLobbyCodeDisplayName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return false;
            }

            string lowerName = objectName.ToLowerInvariant();
            return lowerName.Contains("created lobby code") ||
                lowerName.Contains("lobby code") ||
                lowerName.Contains("code display");
        }

        private void DisableLegacyMenuStartScripts()
        {
            if (SceneManager.GetActiveScene().name != menuSceneName)
            {
                return;
            }

            var oldMenu = FindObjectOfType<MenuController>(true);
            if (oldMenu != null)
            {
                oldMenu.enabled = false;
            }

            var mainMenu = FindObjectOfType<MainMenuController>(true);
            if (mainMenu != null)
            {
                mainMenu.enabled = false;
            }

            var simpleMenu = FindObjectOfType<SimpleMenuController>(true);
            if (simpleMenu != null)
            {
                simpleMenu.enabled = false;
            }
        }

        private static Button FindButtonByNameContains(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            token = token.ToLowerInvariant();
            Button[] buttons = GetAllSceneObjectsOfType<Button>();
            foreach (Button button in buttons)
            {
                if (button != null && button.name.ToLowerInvariant().Contains(token))
                {
                    return button;
                }
            }
            return null;
        }

        private static Button FindButtonByExactName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            Button[] buttons = GetAllSceneObjectsOfType<Button>();
            foreach (Button button in buttons)
            {
                if (button != null && string.Equals(button.name, objectName, StringComparison.OrdinalIgnoreCase))
                {
                    return button;
                }
            }

            return null;
        }

        private static TMP_Text FindTextByNameContains(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            token = token.ToLowerInvariant();
            TMP_Text[] texts = GetAllSceneObjectsOfType<TMP_Text>();
            foreach (TMP_Text text in texts)
            {
                if (text != null && text.name.ToLowerInvariant().Contains(token))
                {
                    return text;
                }
            }
            return null;
        }

        private static T FindObjectByNameContains<T>(string token) where T : Component
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            token = token.ToLowerInvariant();
            T[] objects = GetAllSceneObjectsOfType<T>();
            foreach (T obj in objects)
            {
                if (obj != null && obj.name.ToLowerInvariant().Contains(token))
                {
                    return obj;
                }
            }
            return null;
        }

        private static T[] GetAllSceneObjectsOfType<T>() where T : Component
        {
            T[] all = Resources.FindObjectsOfTypeAll<T>();
            var list = new System.Collections.Generic.List<T>(all.Length);
            foreach (T item in all)
            {
                if (item == null)
                {
                    continue;
                }

                if (item.gameObject.scene.IsValid())
                {
                    list.Add(item);
                }
            }
            return list.ToArray();
        }

        private async Task TryJoinVoiceSafely(string channelCode)
        {
            if (voiceChatManager == null)
            {
                return;
            }

            // VoiceChatManager.JoinLobbyVoiceAsync handles all exceptions internally
            // and is non-fatal — no wrapper needed.
            await voiceChatManager.JoinLobbyVoiceAsync(channelCode);
        }

        private async Task TryLeaveVoiceSafely()
        {
            if (voiceChatManager == null)
            {
                return;
            }

            // VoiceChatManager.LeaveCurrentChannelAsync handles all exceptions internally.
            await voiceChatManager.LeaveCurrentChannelAsync();
        }
    }
}
