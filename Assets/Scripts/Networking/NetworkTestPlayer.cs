using UnityEngine;
using Unity.Netcode;

namespace Networking
{
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkTestPlayer : NetworkBehaviour
    {
        [SerializeField] private float moveSpeed = 4f;

        private void Update()
        {
            if (!IsOwner)
            {
                return;
            }

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 input = new Vector3(h, 0f, v);
            if (input.sqrMagnitude > 1f)
            {
                input.Normalize();
            }

            if (input.sqrMagnitude > 0f)
            {
                SubmitMoveServerRpc(input, Time.deltaTime);
            }
        }

        [ServerRpc]
        private void SubmitMoveServerRpc(Vector3 input, float deltaTime)
        {
            Vector3 move = input * moveSpeed * deltaTime;
            transform.position += move;

            if (input.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(input.normalized, Vector3.up);
            }
        }
    }
}
