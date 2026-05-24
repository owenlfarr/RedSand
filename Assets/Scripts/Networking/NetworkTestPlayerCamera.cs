using UnityEngine;
using Unity.Netcode;

namespace Networking
{
    public class NetworkTestPlayerCamera : NetworkBehaviour
    {
        private Camera _camera;
        private AudioListener _audioListener;

        private void Awake()
        {
            _camera = GetComponentInChildren<Camera>(true);
            _audioListener = GetComponentInChildren<AudioListener>(true);
        }

        public override void OnNetworkSpawn()
        {
            bool enable = IsOwner;

            if (_camera != null)
            {
                _camera.enabled = enable;
            }

            if (_audioListener != null)
            {
                _audioListener.enabled = enable;
            }
        }
    }
}
