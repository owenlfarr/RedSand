using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// One atomic, server-written team snapshot. Clients never roll or award resources.
public struct DrillSnapshot : INetworkSerializable, IEquatable<DrillSnapshot>
{
    public bool Enabled;
    public int Metal, Gold, Plasma;
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Enabled);
        serializer.SerializeValue(ref Metal);
        serializer.SerializeValue(ref Gold);
        serializer.SerializeValue(ref Plasma);
    }
    public bool Equals(DrillSnapshot other) => Enabled == other.Enabled && Metal == other.Metal && Gold == other.Gold && Plasma == other.Plasma;
    public static void Roll(ref DrillSnapshot state, System.Random random)
    {
        if (!state.Enabled) return;
        if (random.Next(30) == 0 && state.Metal < int.MaxValue) state.Metal++;
        if (random.Next(80) == 0 && state.Gold < int.MaxValue) state.Gold++;
        if (random.Next(120) == 0 && state.Plasma < int.MaxValue) state.Plasma++;
    }
}

[RequireComponent(typeof(NetworkObject))]
public sealed class DrillSystem : NetworkBehaviour
{
    public Transform drillButton;
    public Collider buttonCollider;
    public TMP_Text statusText, materialsText;
    public float interactionDistance = 3f;
    public NetworkVariable<DrillSnapshot> Team = new NetworkVariable<DrillSnapshot>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public DrillSnapshot Current => Online ? Team.Value : offline;
    public bool PowerUnavailable => (PowerSystem.Instance != null && PowerSystem.Instance.IsPowerOut) ||
        (SolarFlareSystem.Instance != null && SolarFlareSystem.Instance.IsFlareActive);

    DrillSnapshot offline;
    System.Random random = new System.Random();
    float elapsed, nextRequest;
    DrillSessionStockpile stockpile;
    PlayerController localPlayer;
    TMP_Text prompt;
    GameObject promptCanvas;
    bool Online => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    bool Authority => !Online || (IsSpawned && IsServer);

    public override void OnNetworkSpawn()
    {
        elapsed = 0f;
        offline = default;
        if (IsServer)
        {
            stockpile = NetworkManager.GetComponent<DrillSessionStockpile>();
            if (stockpile == null) stockpile = NetworkManager.gameObject.AddComponent<DrillSessionStockpile>();
            var state = stockpile.Materials;
            state.Enabled = false;
            Team.Value = state;
            stockpile.Changed += StockpileChanged;
        }
        Team.OnValueChanged += StateChanged;
        RefreshScreen();
    }
    public override void OnNetworkDespawn()
    {
        Team.OnValueChanged -= StateChanged;
        if (stockpile != null) stockpile.Changed -= StockpileChanged;
        elapsed = 0f;
        if (promptCanvas != null) promptCanvas.SetActive(false);
    }
    public override void OnDestroy()
    {
        Team.OnValueChanged -= StateChanged;
        if (stockpile != null) stockpile.Changed -= StockpileChanged;
        if (promptCanvas != null) Destroy(promptCanvas);
        base.OnDestroy();
    }
    void Start() { RefreshScreen(); }
    void StateChanged(DrillSnapshot before, DrillSnapshot after) { RefreshScreen(); }
    void StockpileChanged(DrillSnapshot saved)
    {
        if (!IsServer || !IsSpawned) return;
        var state = Team.Value;
        state.Metal = saved.Metal; state.Gold = saved.Gold; state.Plasma = saved.Plasma;
        Team.Value = state;
    }

    void Update()
    {
        if (Authority)
        {
            if (PowerUnavailable) StopDrilling();
            var state = Current;
            if (state.Enabled)
            {
                elapsed += Time.deltaTime;
                while (elapsed >= 1f)
                {
                    elapsed -= 1f;
                    DrillSnapshot.Roll(ref state, random);
                }
                Commit(state);
            }
        }
        UpdateInteraction();
    }
    void Commit(DrillSnapshot state)
    {
        if (!Authority) return;
        if (Current.Equals(state)) return;
        if (Online)
        {
            Team.Value = state;
            if (stockpile != null) stockpile.Materials = state;
        }
        else { offline = state; RefreshScreen(); }
    }
    public bool TrySpendMetal(int amount)
    {
        if(!Authority || amount<=0 || Current.Metal<amount)return false;
        if(Online)return stockpile!=null && stockpile.TrySpendMetal(amount);
        var state=offline;state.Metal-=amount;Commit(state);return true;
    }
    public void StopDrilling()
    {
        if (!Authority) return;
        var state = Current;
        state.Enabled = false;
        elapsed = 0f; // Interrupted/partial seconds never earn material.
        Commit(state);
    }
    public static void CancelForScene(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var drill in FindObjectsByType<DrillSystem>(FindObjectsSortMode.None))
            if (drill.gameObject.scene == scene) drill.StopDrilling();
    }

    public void RequestEnabled(bool enabled)
    {
        if (Online)
        {
            if (IsSpawned) SetEnabledServerRpc(enabled);
        }
        else if (localPlayer != null && CanUse(localPlayer.gameObject)) SetEnabled(enabled);
    }
    [ServerRpc(RequireOwnership = false)]
    void SetEnabledServerRpc(bool enabled, ServerRpcParams rpc = default)
    {
        if (!IsServer || !NetworkManager.ConnectedClients.TryGetValue(rpc.Receive.SenderClientId, out var client) ||
            client.PlayerObject == null || !CanUse(client.PlayerObject.gameObject)) return;
        SetEnabled(enabled);
    }
    void SetEnabled(bool enabled)
    {
        if (!Authority || (enabled && PowerUnavailable)) return;
        var state = Current;
        if (state.Enabled == enabled) return;
        state.Enabled = enabled;
        elapsed = 0f;
        Commit(state);
    }
    bool CanUse(GameObject player)
    {
        if (drillButton == null || buttonCollider == null || !buttonCollider.enabled || !player.activeInHierarchy) return false;
        var controller = player.GetComponent<PlayerController>();
        if (controller == null || !controller.enabled) return false;
        var oxygen = player.GetComponent<NetworkOxygenState>();
        if (Online && oxygen != null && oxygen.IsDead.Value) return false;
        var localOxygen = player.GetComponent<OxygenSystem>();
        if (!Online && localOxygen != null && localOxygen.IsDead()) return false;
        var movement = player.GetComponent<NetworkPlayerMovementState>();
        if (Online && movement != null && !movement.MovementEnabled.Value) return false;
        Vector3 eye = player.transform.position + Vector3.up * 1.5f;
        Vector3 target = buttonCollider.ClosestPoint(eye);
        if (Vector3.Distance(player.transform.position, target) > interactionDistance) return false;
        foreach (var hit in Physics.RaycastAll(eye, (target - eye).normalized, Vector3.Distance(eye, target) + 0.02f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == buttonCollider || hit.transform.IsChildOf(player.transform)) continue;
            if (hit.distance < Vector3.Distance(eye, target) - 0.02f) return false;
        }
        return true;
    }
    void UpdateInteraction()
    {
        if (localPlayer == null)
        {
            if (Online)
                localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject?.GetComponent<PlayerController>();
            else localPlayer = FindFirstObjectByType<PlayerController>();
        }
        bool aimed = false;
        if (localPlayer != null && localPlayer.playerCamera != null && CanUse(localPlayer.gameObject) &&
            Cursor.lockState == CursorLockMode.Locked && Time.timeScale > 0f && (!Online || IsSpawned))
        {
            var camera = localPlayer.playerCamera;
            if (Physics.Raycast(camera.position, camera.forward, out var hit, interactionDistance, ~0, QueryTriggerInteraction.Ignore))
                aimed = hit.collider == buttonCollider;
        }
        if (aimed && prompt == null) CreatePrompt();
        if (promptCanvas != null) promptCanvas.SetActive(aimed);
        if (!aimed) return;
        prompt.text = PowerUnavailable ? "DRILL / POWER UNAVAILABLE" : Current.Enabled ? "[E] DISABLE DRILL" : "[E] ENABLE DRILL";
        if (Input.GetKeyDown(KeyCode.E) && Time.unscaledTime >= nextRequest)
        {
            nextRequest = Time.unscaledTime + 0.25f;
            RequestEnabled(!Current.Enabled);
        }
    }
    public void RefreshScreen()
    {
        var state = Current;
        if (statusText != null)
        {
            statusText.text = state.Enabled ? "DRILL: ENABLED" : "DRILL: DISABLED";
            statusText.color = state.Enabled ? new Color(0.48f, 0.95f, 0.72f) : new Color(1f, 0.39f, 0.24f);
        }
        if (materialsText != null) materialsText.text = $"METAL = {state.Metal}\nGOLD = {state.Gold}\nPLASMA = {state.Plasma}";
    }
    void CreatePrompt()
    {
        promptCanvas = new GameObject("Drill interaction prompt", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = promptCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15;
        var scaler = promptCanvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        var label = new GameObject("Prompt", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(promptCanvas.transform, false);
        prompt = label.GetComponent<TextMeshProUGUI>();
        prompt.rectTransform.sizeDelta = new Vector2(900f, 60f);
        prompt.rectTransform.anchoredPosition = new Vector2(0f, -170f);
        prompt.fontSize = 24f;
        if (statusText != null) prompt.font = statusText.font;
        prompt.alignment = TextAlignmentOptions.Center;
        prompt.raycastTarget = false;
    }
}
