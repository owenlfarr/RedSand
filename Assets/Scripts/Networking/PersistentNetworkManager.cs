using UnityEngine;
using Unity.Netcode;

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
    }
}
