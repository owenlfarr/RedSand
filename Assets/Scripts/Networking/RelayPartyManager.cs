using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        [Header("Scene")]
        [SerializeField] private string multiplayerSceneName = "Night1_MultiplayerTest";
        [SerializeField] private int maxConnections = 3;
        [SerializeField] private string relayConnectionType = "dtls";

        [Header("UI Runtime State")]
        [SerializeField] private string latestJoinCode = "";
        [SerializeField] private string statusText = "Idle";

        private string joinCodeInput = "";
        private bool isBusy;
        private bool matchStarted;

        public bool HasActiveHostParty
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm != null && nm.IsHost && nm.IsListening && !matchStarted && !string.IsNullOrEmpty(latestJoinCode);
            }
        }

        private async void Start()
        {
            await EnsureServicesReady();
        }

        private void OnGUI()
        {
            if (SceneManager.GetActiveScene().name != "Menu" || matchStarted)
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

                if (!nm.StartHost())
                {
                    statusText = "StartHost failed.";
                    return;
                }

                statusText = $"Party created. Join Code: {latestJoinCode}. Waiting for host to start the game.";
            }
            catch (Exception ex)
            {
                statusText = $"Create party error: {ex.Message}";
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
            if (!LoadMultiplayerSceneAsHost(nm))
            {
                return;
            }

            matchStarted = true;
            statusText = "Starting game...";
        }

        public async Task JoinParty(string joinCode)
        {
            if (isBusy) return;
            isBusy = true;
            statusText = "Joining party...";

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
                    return;
                }

                statusText = "Joined party. Waiting for host scene sync...";
            }
            catch (Exception ex)
            {
                statusText = $"Join party error: {ex.Message}";
            }
            finally
            {
                isBusy = false;
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
    }
}
