using UnityEngine;
using Unity.Netcode;

public class NetworkOxygenState : NetworkBehaviour
{
    public NetworkVariable<float> CurrentOxygen = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsInBunker = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsDead = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public void Initialize(float maxOxygen)
    {
        if (!IsServer)
        {
            return;
        }

        CurrentOxygen.Value = maxOxygen;
        IsInBunker.Value = false;
        IsDead.Value = false;
    }

    [ServerRpc(RequireOwnership = false)]
    public void SetInBunkerServerRpc(bool value, ServerRpcParams serverRpcParams = default)
    {
        if (!IsServer)
        {
            return;
        }

        if (!IsSpawned || OwnerClientId != serverRpcParams.Receive.SenderClientId)
        {
            Debug.LogWarning($"[NetworkOxygenState] Ignored bunker state from client {serverRpcParams.Receive.SenderClientId} on owner {OwnerClientId}.");
            return;
        }

        IsInBunker.Value = value;
    }
}
