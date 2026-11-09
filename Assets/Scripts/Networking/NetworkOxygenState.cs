using UnityEngine;
using Unity.Netcode;

public class NetworkOxygenState : NetworkBehaviour
{
    public NetworkVariable<double> LastReviveCompletedAt = new NetworkVariable<double>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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

    public NetworkVariable<bool> UseBlackDeathIntro = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        IsDead.OnValueChanged += OnDeathChanged;
        if (IsServer)
        {
            var oxygen = GetComponent<OxygenSystem>();
            Initialize(oxygen != null ? oxygen.maxOxygen : 100f);
            NetworkGameState.Instance?.RefreshPlayerCounts();
        }
    }

    public override void OnNetworkDespawn()
    {
        IsDead.OnValueChanged -= OnDeathChanged;
        if (IsServer) NetworkGameState.Instance?.RefreshPlayerCounts();
    }

    private void OnDeathChanged(bool previous, bool current)
    {
        if (!IsServer) return;
        if (current && TryGetComponent<NetworkPlayerMovementState>(out var movement))
        {
            movement.MovementEnabled.Value = false;
            movement.CarriedDistractionRocks.Value = 0;
        }
        NetworkGameState.Instance?.RefreshPlayerCounts();
    }

    public void Initialize(float maxOxygen)
    {
        if (!IsServer)
        {
            return;
        }

        LastReviveCompletedAt.Value = NetworkManager.GetComponent<ReviveSession>()?.CompletedAt(OwnerClientId) ?? 0;
        CurrentOxygen.Value = maxOxygen;
        IsInBunker.Value = false;
        bool eliminated = Networking.NetworkLevelProgression.Instance != null &&
            Networking.NetworkLevelProgression.Instance.IsEliminated(OwnerClientId);
        UseBlackDeathIntro.Value = !eliminated;
        IsDead.Value = eliminated;
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

        var oxygen = GetComponent<OxygenSystem>();
        IsInBunker.Value = oxygen != null && oxygen.IsInsideBunkerZone();
    }
}
