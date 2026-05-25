using System;
using System.Threading.Tasks;
using UnityEngine;
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
        [SerializeField] private TMP_InputField joinCodeInputField;
        [SerializeField] private TMP_Text lobbyCodeText;
        [SerializeField] private Button lobbyCodeDisplayButton;
        [SerializeField] private bool showLegacyOnGUI = false;
        [SerializeField] private TMP_Text loadingIndicatorText;

        [Header("Lobby Astronauts (Optional)")]
        [SerializeField] private GameObject player1Visual;
        [SerializeField] private GameObject player2Visual;
        [SerializeField] private GameObject player3Visual;
        [SerializeField] private TextMeshPro player1NameText;
        [SerializeField] private TextMeshPro player2NameText;
        [SerializeField] private TextMeshPro player3NameText;
        [SerializeField] private bool disableAstronautAnimatorsInMenu = false;

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
            AutoBindMenuUI();
            WireMenuUI();
            SetLobbyUIState(false);
            RefreshLobbyCodeUI();
            AutoBindLobbyVisuals();
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
            UpdateLobbyAstronautVisuals();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
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
                GUI.enabled = true;
            }
            else if (isClientLobby)
            {
                GUILayout.Label("Relay Party");
                GUILayout.Label($"Status: {statusText}");
                GUILayout.Label("Waiting for host to start the game...");
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
                    allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
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
                pendingHostAllocation = allocation;
                hasPendingHostAllocation = true;

                statusText = $"Party created. Join Code: {latestJoinCode}. Waiting for host to start the game.";
                lobbyCreated = true;
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
            if (nm != null && !nm.IsHost)
            {
                if (!TryStartPendingHost(nm))
                {
                    RefreshLobbyCodeUI();
                    return;
                }
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
            RefreshLobbyCodeUI();

            try
            {
                if (!await EnsureServicesReady())
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(joinCode))
                {
                    statusText = "Join code is empty.";
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

                JoinAllocation joinAllocation;
                try
                {
                    joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode.Trim().ToUpperInvariant());
                }
                catch (Exception ex)
                {
                    statusText = $"Invalid join code / join failed: {ex.Message}";
                    return;
                }

                transport.SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, relayConnectionType));

                if (!nm.StartClient())
                {
                    statusText = "StartClient failed.";
                    RefreshLobbyCodeUI();
                    return;
                }

                statusText = "Joined party. Waiting for host scene sync...";
                _ = TryJoinVoiceSafely(joinCode);
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
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                nm.Shutdown();
            }

            latestJoinCode = string.Empty;
            statusText = "Idle";
            matchStarted = false;
            lobbyCreated = false;
            hasPendingHostAllocation = false;
            pendingHostAllocation = null;
            _ = TryLeaveVoiceSafely();
            SetLobbyUIState(false);
            RefreshLobbyCodeUI();
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

            if (nm.NetworkConfig == null || nm.NetworkConfig.PlayerPrefab == null)
            {
                statusText = "Error: NetworkManager player prefab is missing.";
                return false;
            }

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

            nm.SceneManager.LoadScene(multiplayerSceneName, LoadSceneMode.Single);
            return true;
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
                statusText = "No pending lobby allocation. Create Lobby first.";
                return false;
            }

            if (nm.IsListening)
            {
                nm.Shutdown();
            }

            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(pendingHostAllocation, relayConnectionType));
            if (!nm.StartHost())
            {
                statusText = "StartHost failed.";
                return false;
            }

            hasPendingHostAllocation = false;
            pendingHostAllocation = null;
            return true;
        }

        private void AutoBindMenuUI()
        {
            if (SceneManager.GetActiveScene().name != menuSceneName)
            {
                return;
            }

            if (createLobbyButton == null)
            {
                createLobbyButton = FindButtonByNameContains("create");
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
                    if (allInputs.Length > 0)
                    {
                        joinCodeInputField = allInputs[0];
                    }
                }
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
                createLobbyButton.onClick.RemoveAllListeners();
                createLobbyButton.onClick.RemoveListener(CreateLobbyFromUI);
                createLobbyButton.onClick.AddListener(CreateLobbyFromUI);
            }

            if (joinLobbyButton != null)
            {
                joinLobbyButton.onClick.RemoveAllListeners();
                joinLobbyButton.onClick.RemoveListener(JoinLobbyFromUI);
                joinLobbyButton.onClick.AddListener(JoinLobbyFromUI);
            }

            if (startGameButton != null)
            {
                startGameButton.onClick.RemoveAllListeners();
                startGameButton.onClick.RemoveListener(StartMatchFromUI);
                startGameButton.onClick.AddListener(StartMatchFromUI);
            }

            if (backButton != null)
            {
                backButton.onClick.RemoveAllListeners();
                backButton.onClick.RemoveListener(BackFromUI);
                backButton.onClick.AddListener(BackFromUI);
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

        private void OnJoinCodeChanged(string value)
        {
            joinCodeInput = value;
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
            bool showHostControls = created;

            if (createLobbyButton != null)
            {
                createLobbyButton.gameObject.SetActive(showCreateAndJoin);
            }

            if (joinLobbyButton != null)
            {
                joinLobbyButton.gameObject.SetActive(showCreateAndJoin);
            }

            if (joinCodeInputField != null)
            {
                joinCodeInputField.gameObject.SetActive(showCreateAndJoin);
            }

            if (lobbyCodeDisplayButton != null)
            {
                lobbyCodeDisplayButton.gameObject.SetActive(showHostControls);
            }

            if (lobbyCodeText != null)
            {
                lobbyCodeText.gameObject.SetActive(showHostControls);
            }

            if (startGameButton != null)
            {
                startGameButton.gameObject.SetActive(showHostControls);
            }

            if (backButton != null)
            {
                backButton.gameObject.SetActive(showHostControls);
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
                current.transform.localPosition = new Vector3(0f, -0.2f, 0f);
                current.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                current.transform.localScale = Vector3.one * 0.18f;
                return current;
            }

            Transform existing = anchor.transform.Find(tagName);
            if (existing != null)
            {
                existing.localPosition = new Vector3(0f, -0.2f, 0f);
                existing.localRotation = Quaternion.Euler(0f, 180f, 0f);
                existing.localScale = Vector3.one * 0.18f;
                return existing.GetComponent<TextMeshPro>();
            }

            var go = new GameObject(tagName);
            go.transform.SetParent(anchor.transform, false);
            go.transform.localPosition = new Vector3(0f, -0.2f, 0f);
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
            int playerCount = GetLobbyPlayerCount();
            string localName = GetLocalUserName();

            SetAstronautSlot(player1Visual, player1NameText, playerCount >= 1, localName);
            SetAstronautSlot(player2Visual, player2NameText, playerCount >= 2, "Player2");
            SetAstronautSlot(player3Visual, player3NameText, playerCount >= 3, "Player3");
        }

        private int GetLobbyPlayerCount()
        {
            var nm = NetworkManager.Singleton;
            bool joinedAsClient = nm != null && nm.IsClient && !nm.IsHost && nm.IsListening;
            bool inLobbyState = lobbyCreated || HasActiveHostParty || joinedAsClient;

            // Before lobby creation/join, always show only local player.
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
            string playerPrefName = PlayerPrefs.GetString("PlayerName", string.Empty);
            if (!string.IsNullOrWhiteSpace(playerPrefName))
            {
                return playerPrefName.Trim();
            }

            if (UnityServices.State == ServicesInitializationState.Initialized)
            {
                try
                {
                    if (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
                    {
                        if (!string.IsNullOrWhiteSpace(AuthenticationService.Instance.PlayerName))
                        {
                            return AuthenticationService.Instance.PlayerName;
                        }

                        string playerId = AuthenticationService.Instance.PlayerId;
                        if (!string.IsNullOrWhiteSpace(playerId))
                        {
                            return $"Player_{playerId.Substring(0, Mathf.Min(6, playerId.Length))}";
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

            try
            {
                await voiceChatManager.JoinLobbyVoiceAsync(channelCode);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RelayPartyManager] Voice join failed (non-blocking): {ex.Message}");
            }
        }

        private async Task TryLeaveVoiceSafely()
        {
            if (voiceChatManager == null)
            {
                return;
            }

            try
            {
                await voiceChatManager.LeaveCurrentChannelAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RelayPartyManager] Voice leave failed (non-blocking): {ex.Message}");
            }
        }
    }
}
