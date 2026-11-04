using System;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Networking;

public struct ReviveSnapshot : INetworkSerializable, IEquatable<ReviveSnapshot>
{
    public bool Active;
    public ulong Target, OldPlayerObject;
    public double EndsAt;
    public FixedString128Bytes Name;
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Active); serializer.SerializeValue(ref Target);
        serializer.SerializeValue(ref OldPlayerObject); serializer.SerializeValue(ref EndsAt); serializer.SerializeValue(ref Name);
    }
    public bool Equals(ReviveSnapshot other) => Active == other.Active && Target == other.Target && OldPlayerObject == other.OldPlayerObject && EndsAt == other.EndsAt && Name.Equals(other.Name);
}

// The paid job belongs to the persistent session, not the console/operator/power systems.
public sealed class ReviveSession : MonoBehaviour
{
    public const float Duration = 15f;
    public ReviveSnapshot Current { get; private set; }
    readonly System.Collections.Generic.Dictionary<ulong, double> completed = new System.Collections.Generic.Dictionary<ulong, double>();
    public double CompletedAt(ulong clientId) => completed.TryGetValue(clientId, out var time) ? time : 0;
    NetworkManager manager;
    DrillSessionStockpile inventory;
    Vector3 paidSpawnPosition;
    Quaternion paidSpawnRotation;
    void Awake()
    {
        manager = GetComponent<NetworkManager>();
        Current = new ReviveSnapshot { Target = ulong.MaxValue };
        manager.OnServerStopped += Stopped;
    }
    public static ReviveSession Get(NetworkManager manager)
    {
        var session = manager.GetComponent<ReviveSession>();
        return session != null ? session : manager.gameObject.AddComponent<ReviveSession>();
    }
    public ulong[] DeadPlayers() => manager.ConnectedClientsList.Where(client => client.PlayerObject != null &&
        client.PlayerObject.TryGetComponent<NetworkOxygenState>(out var state) && state.IsDead.Value)
        .Select(client => client.ClientId).OrderBy(id => id).ToArray();
    void Select(ulong id)
    {
        var state = Current; state.Target = id;
        var name = manager.GetComponent<RelayPartyManager>()?.GetPlayerDisplayName(id) ?? "Player " + (id + 1);
        state.Name = new FixedString128Bytes(id == ulong.MaxValue ? "" : name.Substring(0, Math.Min(24, name.Length)));
        Current = state; Publish();
    }
    public void Cycle()
    {
        if (!manager.IsServer || Current.Active) return;
        var dead = DeadPlayers();
        if (dead.Length == 0) { Select(ulong.MaxValue); return; }
        int index = Array.IndexOf(dead, Current.Target);
        Select(dead[(index + 1) % dead.Length]);
    }
    public bool TryStart(ReviveStation station, ulong expectedTarget)
    {
        if (!manager.IsServer || Current.Active || station == null || station.respawnPoint == null ||
            manager.NetworkConfig.PlayerPrefab == null || !manager.NetworkConfig.PlayerPrefab.TryGetComponent<NetworkObject>(out _) ||
            !manager.NetworkConfig.PlayerPrefab.TryGetComponent<NetworkOxygenState>(out _)) return false;
        var dead = DeadPlayers();
        if (expectedTarget != Current.Target || !dead.Contains(expectedTarget)) return false;
        if (inventory == null) inventory = manager.GetComponent<DrillSessionStockpile>();
        if (inventory == null || !inventory.TrySpendPlasma()) return false;
        var state = Current;
        state.Active = true; state.EndsAt = manager.ServerTime.Time + Duration;
        state.OldPlayerObject = manager.ConnectedClients[state.Target].PlayerObject.NetworkObjectId;
        paidSpawnPosition = station.respawnPoint.position; paidSpawnRotation = station.respawnPoint.rotation;
        Current = state; Publish();
        return true;
    }
    void Update()
    {
        if (!manager.IsServer || !manager.IsListening) return;
        if (Current.Active)
        {
            if (!manager.ConnectedClients.ContainsKey(Current.Target))
            {
                // A departed client cannot be revived; return its reserved resource.
                if (inventory != null) inventory.RefundPlasma();
                Current = new ReviveSnapshot { Target = ulong.MaxValue }; Publish();
            }
            else if (manager.ServerTime.Time >= Current.EndsAt) Complete();
        }
        else
        {
            var dead = DeadPlayers();
            if (!dead.Contains(Current.Target) && (dead.Length > 0 || Current.Target != ulong.MaxValue)) Select(dead.Length == 0 ? ulong.MaxValue : dead[0]);
        }
    }
    void Complete()
    {
        var progression = manager.GetComponent<NetworkLevelProgression>();
        if (progression != null && progression.IsTransferring) return;
        var station = FindObjectsByType<ReviveStation>(FindObjectsSortMode.None).FirstOrDefault(s => s.IsSpawned && s.respawnPoint != null);
        var target = Current.Target;
        if (!manager.ConnectedClients.TryGetValue(target, out var client)) return;
        var point = station != null ? station.respawnPoint : FindObjectsByType<NetworkStartPosition>(FindObjectsSortMode.None)
            .OrderBy(p => p.name, StringComparer.Ordinal).FirstOrDefault()?.transform;
        var replacement = Instantiate(manager.NetworkConfig.PlayerPrefab, point != null ? point.position : paidSpawnPosition,
            point != null ? point.rotation : paidSpawnRotation);
        if (client.PlayerObject != null) client.PlayerObject.Despawn(true);
        progression?.MarkRevived(target);
        completed[target] = Current.EndsAt;
        replacement.GetComponent<NetworkObject>().SpawnAsPlayerObject(target, true);
        Debug.Log("[Revive] Respawned client " + target + " at " + replacement.transform.position + ".");
        Current = new ReviveSnapshot { Target = ulong.MaxValue };
        Publish();
        NetworkGameState.Instance?.RefreshPlayerCounts();
    }
    public void Publish()
    {
        foreach (var station in FindObjectsByType<ReviveStation>(FindObjectsSortMode.None))
            if (station.IsSpawned && station.IsServer) station.State.Value = Current;
    }
    void Stopped(bool host) { completed.Clear(); Current = new ReviveSnapshot { Target = ulong.MaxValue }; }
    void OnDestroy() { if (manager != null) manager.OnServerStopped -= Stopped; }
}
