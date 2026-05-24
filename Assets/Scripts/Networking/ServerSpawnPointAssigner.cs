using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Networking
{
    public class ServerSpawnPointAssigner : MonoBehaviour
    {
        [SerializeField] private Transform[] spawnPoints;

        private int nextSpawnIndex;

        private void OnEnable()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            }
        }

        private void OnDisable()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                return;
            }

            StartCoroutine(PlaceWhenReady(clientId));
        }

        private IEnumerator PlaceWhenReady(ulong clientId)
        {
            int maxFrames = 120;
            while (maxFrames-- > 0)
            {
                if (NetworkManager.Singleton != null &&
                    NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) &&
                    client.PlayerObject != null)
                {
                    if (spawnPoints != null && spawnPoints.Length > 0)
                    {
                        var point = spawnPoints[nextSpawnIndex % spawnPoints.Length];
                        nextSpawnIndex++;

                        if (point != null)
                        {
                            client.PlayerObject.transform.SetPositionAndRotation(point.position, point.rotation);
                        }
                    }
                    yield break;
                }

                yield return null;
            }
        }

        public void SetSpawnPoints(Transform[] points)
        {
            spawnPoints = points;
            nextSpawnIndex = 0;
        }
    }
}
