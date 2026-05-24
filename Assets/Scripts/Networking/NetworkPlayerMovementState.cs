using UnityEngine;
using Unity.Netcode;

public class NetworkPlayerMovementState : NetworkBehaviour
{
    public NetworkVariable<Vector2> MoveInput = new NetworkVariable<Vector2>(
        Vector2.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public NetworkVariable<float> CameraPitch = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public NetworkVariable<bool> IsSprinting = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public NetworkVariable<bool> CanSprint = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public NetworkVariable<float> SprintEnergy = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public NetworkVariable<bool> IsGrounded = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public NetworkVariable<bool> MovementEnabled = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    [ServerRpc(RequireOwnership = false)]
    public void SetMovementEnabledServerRpc(ulong targetClientId, bool enabled)
    {
        if (!IsServer)
        {
            return;
        }

        if (!IsSpawned || OwnerClientId != targetClientId)
        {
            Debug.LogWarning($"[NetworkPlayerMovementState] Ignored movement toggle for client {targetClientId} on owner {OwnerClientId}.");
            return;
        }

        MovementEnabled.Value = enabled;
    }
}
