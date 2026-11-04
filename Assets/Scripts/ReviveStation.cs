using TMPro;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public sealed class ReviveStation : NetworkBehaviour
{
    public Collider consoleCollider;
    public Transform respawnPoint;
    public TMP_Text display;
    public float interactionDistance = 4f;
    public NetworkVariable<ReviveSnapshot> State = new NetworkVariable<ReviveSnapshot>(new ReviveSnapshot { Target = ulong.MaxValue },
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    ReviveSession session;
    PlayerController local;
    string lastDisplay;
    DrillSystem drill;
    public override void OnNetworkSpawn()
    {
        if (IsServer) { session = ReviveSession.Get(NetworkManager); session.Publish(); }
        State.OnValueChanged += Changed;
        Changed(default, State.Value);
    }
    public override void OnNetworkDespawn() { State.OnValueChanged -= Changed; }
    public override void OnDestroy() { State.OnValueChanged -= Changed; base.OnDestroy(); }
    void Changed(ReviveSnapshot old, ReviveSnapshot current)
    {
        if (current.Active && NetworkManager != null && NetworkManager.LocalClientId == current.Target)
            ReviveAudioGate.Get(NetworkManager).Begin(current);
        else if (NetworkManager != null) NetworkManager.GetComponent<ReviveAudioGate>()?.ObserveCompletion();
        RefreshDisplay();
    }
    void Update()
    {
        RefreshDisplay();
        if (!IsSpawned || NetworkManager.LocalClient?.PlayerObject == null) return;
        if (local == null) local = NetworkManager.LocalClient.PlayerObject.GetComponent<PlayerController>();
        if (local == null || local.playerCamera == null || Cursor.lockState != CursorLockMode.Locked ||
            !CanUse(local.gameObject) || !Physics.Raycast(local.playerCamera.position, local.playerCamera.forward, out var hit,
                interactionDistance, ~0, QueryTriggerInteraction.Ignore) || !IsConsoleHit(hit.collider)) return;
        if (Input.GetKeyDown(KeyCode.Q)) CycleServerRpc();
        if (Input.GetKeyDown(KeyCode.E)) ActivateServerRpc(State.Value.Target);
    }
    public void RefreshDisplay()
    {
        if (display == null) return;
        var state = State.Value;
        string text;
        if (state.Active)
        {
            int seconds = Mathf.Max(0, Mathf.CeilToInt((float)(state.EndsAt - NetworkManager.ServerTime.Time)));
            text = "REVIVING " + state.Name + "\n" + seconds + " SECONDS";
        }
        else if (state.Target == ulong.MaxValue) text = "REVIVE STATION\nNO FALLEN CREW / COST: 1 PLASMA";
        else
        {
            if (drill == null) drill = FindFirstObjectByType<DrillSystem>();
            bool funded = drill != null && drill.Current.Plasma > 0;
            text = "REVIVE " + state.Name + (funded ? "\n[E] 1 PLASMA   [Q] NEXT CREWMATE" : "\nNEED 1 PLASMA   [Q] NEXT CREWMATE");
        }
        if (text == lastDisplay) return;
        lastDisplay = text; display.text = text;
    }
    [ServerRpc(RequireOwnership = false)]
    public void ActivateServerRpc(ulong expectedTarget, ServerRpcParams rpc = default)
    {
        if (!ValidSender(rpc.Receive.SenderClientId)) return;
        session.TryStart(this, expectedTarget);
    }
    [ServerRpc(RequireOwnership = false)]
    public void CycleServerRpc(ServerRpcParams rpc = default)
    {
        if (!ValidSender(rpc.Receive.SenderClientId)) return;
        session.Cycle();
    }
    bool ValidSender(ulong id) => IsServer && NetworkManager.ConnectedClients.TryGetValue(id, out var client) &&
        client.PlayerObject != null && CanUse(client.PlayerObject.gameObject);
    public bool IsConsoleHit(Collider hit) => hit != null && consoleCollider != null &&
        (hit == consoleCollider || hit.transform.IsChildOf(consoleCollider.transform));
    public bool CanUse(GameObject player)
    {
        if (consoleCollider == null || !consoleCollider.enabled || !player.activeInHierarchy ||
            !player.TryGetComponent<PlayerController>(out var controller) || !controller.enabled ||
            !player.TryGetComponent<NetworkOxygenState>(out var oxygen) || oxygen.IsDead.Value) return false;
        if (player.TryGetComponent<NetworkPlayerMovementState>(out var movement) && !movement.MovementEnabled.Value) return false;
        Vector3 eye = player.transform.position + Vector3.up * 1.5f;
        Vector3 target = consoleCollider.ClosestPoint(eye);
        float distance = Vector3.Distance(eye, target);
        if (Vector3.Distance(player.transform.position, target) > interactionDistance) return false;
        foreach (var hit in Physics.RaycastAll(eye, (target-eye).normalized, distance + 0.02f, ~0, QueryTriggerInteraction.Ignore))
            if (!IsConsoleHit(hit.collider) && !hit.transform.IsChildOf(player.transform) && hit.distance < distance - 0.02f) return false;
        return true;
    }
}
