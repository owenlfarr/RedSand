using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

namespace Networking
{
    [DisallowMultipleComponent]
    public class PersistentNetworkManager : MonoBehaviour
    {
        private static PersistentNetworkManager _instance;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            var networkManager = GetComponent<NetworkManager>();
            if (networkManager != null)
            {
                networkManager.NetworkConfig.EnableSceneManagement = true;
            }
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            RegisterDisconnectCallback(true);
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            RegisterDisconnectCallback(false);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RegisterDisconnectCallback(true);
        }

        private void RegisterDisconnectCallback(bool register)
        {
            var networkManager = GetComponent<NetworkManager>();
            if (networkManager == null)
            {
                return;
            }

            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;

            if (register)
            {
                networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            var networkManager = GetComponent<NetworkManager>();
            if (networkManager == null || networkManager.IsServer)
            {
                return;
            }

            if (clientId == NetworkManager.ServerClientId)
            {
                networkManager.Shutdown();

                if (SceneManager.GetActiveScene().name != "MainMenu")
                {
                    SceneManager.LoadScene("MainMenu");
                }
            }
        }
    }
}
