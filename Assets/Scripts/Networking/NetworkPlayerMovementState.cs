using UnityEngine;
using Unity.Netcode;

public class NetworkPlayerMovementState : NetworkBehaviour
{
    public NetworkVariable<int> CarriedDistractionRocks = new NetworkVariable<int>();

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
    public void SetMovementEnabledServerRpc(ulong targetClientId, bool enabled, ServerRpcParams rpcParams = default)
    {
        if (!IsServer)
        {
            return;
        }

        // World systems set MovementEnabled directly on the host. Clients may only
        // request changes for their own living character, never escape a locker.
        if (!IsSpawned || OwnerClientId != targetClientId ||
            rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[NetworkPlayerMovementState] Ignored movement toggle for client {targetClientId} on owner {OwnerClientId}.");
            return;
        }

        if (TryGetComponent<NetworkOxygenState>(out var oxygen) && oxygen.IsDead.Value) return;
        foreach (var locker in FindObjectsOfType<LockerInteractionNew>())
            if (locker.IsClientHidden(OwnerClientId)) return;
        MovementEnabled.Value = enabled;
    }
}
