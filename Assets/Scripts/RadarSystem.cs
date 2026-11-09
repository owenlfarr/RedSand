using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class RadarSystem : NetworkBehaviour
{
    [Header("Radar Settings")]
    [Tooltip("The objective that monsters are approaching")]
    public Transform objectiveTransform;

    [Tooltip("Distance within which player can interact with radar")]
    public float interactionDistance = 3f;

    [Tooltip("Cooldown time in seconds")]
    public float cooldownTime = 20f;

    [Tooltip("Tag to identify monsters (e.g., 'Enemy', 'Monster')")]
    public string monsterTag = "Enemy";

    [Header("UI References")]
    public TextMeshProUGUI interactionPromptText;
    public TextMeshProUGUI radarResultsText;
    public GameObject radarPanel;
    public TextMeshProUGUI cooldownText;

    [Header("Audio (Optional)")]
    public AudioClip radarScanSound;
    public AudioClip cooldownSound;

    public NetworkVariable<float> LastScanTimeNetwork = new NetworkVariable<float>(
        -999f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsOnCooldownNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<int> UsesRemainingNetwork = new NetworkVariable<int>();
    public NetworkVariable<ulong> MaintenanceOwnerNetwork = new NetworkVariable<ulong>(ulong.MaxValue);
    public NetworkVariable<float> MaintenanceProgressNetwork = new NetworkVariable<float>();
    int offlineUses;
    ulong offlineRepairOwner=ulong.MaxValue;
    float offlineRepairProgress,lastRepairHeartbeat;
    DrillSystem repairDrill;
    SolarFlareSystem repairStation;
    public int NightNumber=>RadarWearRules.NightNumber(gameObject.scene.name);
    public int UseCapacity=>RadarWearRules.Capacity(NightNumber);
    public int RepairMetalCost=>RadarWearRules.MetalCost(NightNumber);
    public int UsesRemaining=>IsNetworkSessionActive()?UsesRemainingNetwork.Value:offlineUses;
    public bool NeedsMaintenance=>UsesRemaining<UseCapacity;
    public ulong MaintenanceOwner=>IsNetworkSessionActive()?MaintenanceOwnerNetwork.Value:offlineRepairOwner;
    public float MaintenanceProgress=>IsNetworkSessionActive()?MaintenanceProgressNetwork.Value:offlineRepairProgress;
    public float MaintenanceSeconds=>repairStation!=null?Mathf.Max(.1f,repairStation.repairTime):5f;
    public int AvailableRepairMetal {get {ResolveRepairStation();return repairDrill!=null?repairDrill.Current.Metal:0;}}
    void Awake(){offlineUses=UseCapacity;}
    void ResolveRepairStation(){if(repairStation==null)repairStation=FindFirstObjectByType<SolarFlareSystem>();if(repairDrill==null)repairDrill=FindFirstObjectByType<DrillSystem>();}

    private Transform playerTransform;
    private float lastScanTime = -999f;
    private bool isPlayerInRange = false;
    private AudioSource audioSource;

    private const KeyCode INTERACTION_KEY = KeyCode.E;

    void Start()
    {
        if(GetComponent<RadarRepairStation>()==null)gameObject.AddComponent<RadarRepairStation>();
        ResolveRepairStation();
        RefreshLocalPlayerReference();

        if (playerTransform == null && !IsNetworkSessionActive())
        {
            Debug.LogError("<color=red>[Radar]</color> Player with tag 'Player' not found!");
        }
        else if (playerTransform != null)
        {
            Debug.Log($"<color=green>[Radar]</color> Player found at {playerTransform.position}");
        }

        if (objectiveTransform == null)
        {
            Debug.LogError("<color=red>[Radar]</color> Objective Transform not assigned!");
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null && (radarScanSound != null || cooldownSound != null))
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(false);
        }

        if (radarPanel != null)
        {
            radarPanel.SetActive(false);
        }

        if (cooldownText != null)
        {
            cooldownText.gameObject.SetActive(false);
        }

        Debug.Log($"<color=cyan>[Radar]</color> System initialized at position {transform.position}, interaction range: {interactionDistance}m");
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            UsesRemainingNetwork.Value=UseCapacity;
            MaintenanceOwnerNetwork.Value=ulong.MaxValue;
            MaintenanceProgressNetwork.Value=0;
            LastScanTimeNetwork.Value = lastScanTime;
            IsOnCooldownNetwork.Value = IsOnCooldown();
        }

        SyncLocalStateFromNetwork();
    }

    void Update()
    {
        if (IsNetworkSessionActive() && !IsSpawned) return;
        if(!IsNetworkSessionActive()||IsServer)TickMaintenance();
        RefreshLocalPlayerReference();

        if (playerTransform == null)
        {
            return;
        }

        if (IsRadarLockedOut())
        {
            ForceHidePrompt();
        }

        CheckPlayerDistance();

        if (isPlayerInRange && !IsRadarLockedOut())
        {
            if (IsOnCooldown())
            {
                UpdateInteractionPromptDuringCooldown();
            }
            else if (!PlayerSettingsUI.IsGameMenuOpen && Input.GetKeyDown(INTERACTION_KEY))
            {
                if (IsNetworkSessionActive())
                {
                    if (IsServer)
                    {
                        TryActivateRadarForClient(NetworkManager.Singleton.LocalClientId);
                    }
                    else
                    {
                        RequestRadarScanServerRpc();
                    }
                }
                else
                {
                    TryActivateRadarOffline();
                }
            }
        }

        UpdateCooldownDisplay();

        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                LastScanTimeNetwork.Value = lastScanTime;
                IsOnCooldownNetwork.Value = IsOnCooldown();
            }
            else
            {
                SyncLocalStateFromNetwork();
            }
        }
    }

    void CheckPlayerDistance()
    {
        Vector3 radarCenter = GetRadarCenter();

        float distance = Vector3.Distance(playerTransform.position, radarCenter);
        bool wasInRange = isPlayerInRange;
        isPlayerInRange = distance <= interactionDistance;

        if (isPlayerInRange && !wasInRange)
        {
            Debug.Log($"<color=green>[Radar]</color> Player entered range! Distance: {distance:F2}m");
            ShowInteractionPrompt();
        }
        else if (!isPlayerInRange && wasInRange)
        {
            Debug.Log($"<color=yellow>[Radar]</color> Player left range. Distance: {distance:F2}m");
            HideInteractionPrompt();
        }
    }

    Vector3 GetRadarCenter()
    {
        Vector3 radarCenter = transform.position;

        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            radarCenter = renderer.bounds.center;
        }

        return radarCenter;
    }

    void ShowInteractionPrompt()
    {
        if (interactionPromptText != null)
        {
            if (!IsOnCooldown() && !IsRadarLockedOut())
            {
                interactionPromptText.text = $"Press {INTERACTION_KEY} to scan radar ({UsesRemaining}/{UseCapacity} uses)";
                interactionPromptText.gameObject.SetActive(true);
            }
            else
            {
                interactionPromptText.gameObject.SetActive(false);
            }
        }
    }

    void UpdateInteractionPromptDuringCooldown()
    {
        if (interactionPromptText != null && IsOnCooldown())
        {
            float remainingTime = GetRemainingCooldown();
            interactionPromptText.text = $"Radar on cooldown: {remainingTime:F1}s";
            interactionPromptText.gameObject.SetActive(true);
        }
    }

    void HideInteractionPrompt()
    {
        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(false);
        }
    }

    public void ForceHidePrompt()
    {
        isPlayerInRange = false;
        HideInteractionPrompt();
    }

    public void RequestScan()
    {
        if(IsNetworkSessionActive()){if(IsSpawned)RequestRadarScanServerRpc();}
        else TryActivateRadarOffline();
    }

    void TryActivateRadarOffline()
    {
        if(IsRadarLockedOut() || objectiveTransform==null)return;
        if (IsOnCooldown())
        {
            Debug.Log($"<color=yellow>[Radar]</color> Still on cooldown: {GetRemainingCooldown():F1}s remaining");
            if (audioSource != null && cooldownSound != null)
            {
                audioSource.PlayOneShot(cooldownSound);
            }
            return;
        }

        PerformRadarScanOffline();
        offlineUses=Mathf.Max(0,offlineUses-1);
        lastScanTime = Time.time;
    }

    void TryActivateRadarForClient(ulong clientId)
    {
        if (IsRadarLockedOut() || objectiveTransform==null) return;
        if (!CanClientScan(clientId))
        {
            RadarResultsClientRpc("Radar scan failed.");
            return;
        }

        if (IsOnCooldown())
        {
            RadarResultsClientRpc($"Radar on cooldown: {GetRemainingCooldown():F1}s");
            if (audioSource != null && cooldownSound != null)
            {
                audioSource.PlayOneShot(cooldownSound);
            }
            return;
        }

        PerformRadarScanServer();
        UsesRemainingNetwork.Value=Mathf.Max(0,UsesRemainingNetwork.Value-1);
        lastScanTime = GetCooldownClockTime();
        LastScanTimeNetwork.Value = lastScanTime;
        IsOnCooldownNetwork.Value = true;
    }

    bool CanClientScan(ulong clientId)
    {
        if (!IsNetworkSessionActive())
        {
            return true;
        }

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
        {
            return false;
        }

        float distance = Vector3.Distance(client.PlayerObject.transform.position, GetRadarCenter());
        return distance <= interactionDistance &&
            (!client.PlayerObject.TryGetComponent<NetworkOxygenState>(out var oxygen) || !oxygen.IsDead.Value);
    }

    void PerformRadarScanOffline()
    {
        PerformRadarScanInternal(false);
    }

    void PerformRadarScanServer()
    {
        if (!IsServer)
        {
            return;
        }

        PerformRadarScanInternal(true);
        RadarResultsClientRpc(lastRadarResult);
    }

    string lastRadarResult = "No threats detected.";

    void PerformRadarScanInternal(bool isNetworkSession)
    {
        if (objectiveTransform == null)
        {
            Debug.LogWarning("<color=yellow>[Radar]</color> Cannot scan - objective not set!");
            lastRadarResult = "Radar unavailable.";
            DisplayRadarResults(lastRadarResult);
            return;
        }

        Debug.Log($"<color=cyan>[Radar]</color> Scanning for enemies with tag '{monsterTag}'...");
        DrillSystem.CancelForScene(gameObject.scene);

        GameObject[] monsters = GameObject.FindGameObjectsWithTag(monsterTag);
        Debug.Log($"<color=cyan>[Radar]</color> Found {monsters.Length} enemies");

        if (monsters.Length == 0)
        {
            lastRadarResult = "No threats detected.";
            DisplayRadarResults(lastRadarResult);
            return;
        }

        List<MonsterDistanceInfo> monsterDistances = new List<MonsterDistanceInfo>();

        foreach (GameObject monster in monsters)
        {
            if (monster == null)
            {
                continue;
            }

            ZombieAI zombieAI = monster.GetComponent<ZombieAI>();
            if (zombieAI != null && zombieAI.IsNetworkSessionActive() && zombieAI.IsNeutralized())
            {
                monsterDistances.Add(new MonsterDistanceInfo
                {
                    monsterName = monster.name,
                    distance = -1f,
                    isNeutralized = true
                });
                continue;
            }

            float distance = Vector3.Distance(monster.transform.position, objectiveTransform.position);
            string monsterName = monster.name;

            if (monster.GetComponent<IgnoreMonster>() != null)
            {
                IgnoreMonster ignoreMonster = monster.GetComponent<IgnoreMonster>();
                if (ignoreMonster.IsCurrentlyPresent())
                {
                    distance = ignoreMonster.GetFakeDistance();
                    monsterName = "Unknown Signal";
                    Debug.Log($"<color=cyan>[Radar]</color> Unknown Signal shows constant {distance:F1}m (deception)");
                }
            }

            monsterDistances.Add(new MonsterDistanceInfo
            {
                monsterName = monsterName,
                distance = distance,
                isNeutralized = false
            });
        }

        monsterDistances = monsterDistances.OrderBy(m => m.isNeutralized ? float.MaxValue : m.distance).ToList();

        string resultsText = $"<b>RADAR SCAN COMPLETE</b>\n\n";
        resultsText += $"Threats detected: {monsterDistances.Count}\n";
        resultsText += $"Distance to objective:\n\n";

        foreach (var info in monsterDistances)
        {
            if (info.isNeutralized)
            {
                resultsText += $"- {info.monsterName}: -- [UNDETECTED]\n";
                continue;
            }

            string threatLevel = GetThreatLevel(info.distance);

            if (info.monsterName.Contains("Surveyor") || info.monsterName.Contains("Survayer"))
            {
                resultsText += $"- {info.monsterName}: ??? [{threatLevel}]\n";
            }
            else
            {
                resultsText += $"- {info.monsterName}: {info.distance:F1}m [{threatLevel}]\n";
            }
        }

        lastRadarResult = resultsText;
        DisplayRadarResults(resultsText);

        if (audioSource != null && radarScanSound != null)
        {
            audioSource.PlayOneShot(radarScanSound);
        }

        if (SurveyorMonster.Instance != null && (!isNetworkSession || IsServer))
        {
            SurveyorMonster.Instance.OnRadarChecked();
        }
    }

    string GetThreatLevel(float distance)
    {
        if (distance < 10f) return "CRITICAL";
        if (distance < 20f) return "HIGH";
        if (distance < 40f) return "MEDIUM";
        return "LOW";
    }

    void DisplayRadarResults(string message)
    {
        if (radarResultsText != null)
        {
            radarResultsText.text = message;
        }

        if (radarPanel != null)
        {
            radarPanel.SetActive(true);
            CancelInvoke(nameof(HideRadarResults));
            Invoke(nameof(HideRadarResults), 5f);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestRadarScanServerRpc(ServerRpcParams serverRpcParams = default)
    {
        if (!IsServer)
        {
            return;
        }

        TryActivateRadarForClient(serverRpcParams.Receive.SenderClientId);
    }

    [ClientRpc]
    private void RadarResultsClientRpc(string message)
    {
        DisplayRadarResults(message);

        if (audioSource != null && radarScanSound != null)
        {
            audioSource.PlayOneShot(radarScanSound);
        }
    }

    void HideRadarResults()
    {
        if (radarPanel != null)
        {
            radarPanel.SetActive(false);
        }
    }

    bool IsOnCooldown()
    {
        float effectiveCooldown = GetEffectiveCooldownTime();
        float scanTime = IsNetworkSessionActive() ? LastScanTimeNetwork.Value : lastScanTime;
        return GetCooldownClockTime() - scanTime < effectiveCooldown;
    }

    float GetRemainingCooldown()
    {
        float effectiveCooldown = GetEffectiveCooldownTime();
        float scanTime = IsNetworkSessionActive() ? LastScanTimeNetwork.Value : lastScanTime;
        float remaining = effectiveCooldown - (GetCooldownClockTime() - scanTime);
        return Mathf.Max(0f, remaining);
    }

    float GetEffectiveCooldownTime()
    {
        float multiplier = 1f;
        if (PowerSystem.Instance != null)
        {
            multiplier = PowerSystem.Instance.GetCooldownMultiplier();
        }
        return cooldownTime * multiplier;
    }

    public void ResetCooldown()
    {
        lastScanTime = -999f;
        if (IsServer)
        {
            LastScanTimeNetwork.Value = -999f;
            IsOnCooldownNetwork.Value = false;
        }
    }

    void UpdateCooldownDisplay()
    {
        if (cooldownText == null)
        {
            return;
        }

        if(UsesRemaining<=0){
            cooldownText.text=$"RADAR WORN OUT / 0/{UseCapacity} uses\nRepair at solar station: {RepairMetalCost} metal";
            cooldownText.color=new Color(1,.35f,.25f);cooldownText.gameObject.SetActive(true);return;
        }
        if (IsOnCooldown())
        {
            float remaining = GetRemainingCooldown();
            cooldownText.text = $"Radar: {remaining:F1}s / {UsesRemaining}/{UseCapacity} uses";
            cooldownText.color = new Color(0f, 0.8f, 1f);
            cooldownText.fontSize = 18;
            cooldownText.fontStyle = TMPro.FontStyles.Bold;
            cooldownText.alignment = TMPro.TextAlignmentOptions.Center;
            cooldownText.gameObject.SetActive(true);
        }
        else
        {
            cooldownText.text=$"Radar: {UsesRemaining}/{UseCapacity} uses";
            cooldownText.color=Color.white;cooldownText.gameObject.SetActive(true);
        }
    }

    void RefreshLocalPlayerReference()
    {
        if (IsNetworkSessionActive() && NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            playerTransform = NetworkManager.Singleton.LocalClient.PlayerObject.transform;
            return;
        }

        if (playerTransform != null)
        {
            return;
        }

        GameObject localPlayer = GameObject.FindGameObjectWithTag("Player");
        if (localPlayer != null)
        {
            playerTransform = localPlayer.transform;
        }
    }

    bool IsRadarLockedOut()
    {
        bool powerOut = PowerSystem.Instance != null && PowerSystem.Instance.IsPowerOut;
        bool flareActive = SolarFlareSystem.Instance != null && SolarFlareSystem.Instance.IsFlareActive;
        return powerOut || flareActive || UsesRemaining<=0;
    }

    bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    private float GetCooldownClockTime()
    {
        if (IsNetworkSessionActive() && NetworkManager.Singleton != null)
        {
            return (float)NetworkManager.Singleton.ServerTime.Time;
        }

        return Time.time;
    }

    void SyncLocalStateFromNetwork()
    {
        lastScanTime = LastScanTimeNetwork.Value;
    }

    public bool CanRepairAtStation(PlayerController player)
    {
        ResolveRepairStation();
        if(player==null||!player.enabled||!player.gameObject.activeInHierarchy||repairStation==null||repairStation.fixPointTransform==null||repairStation.IsFlareActive)return false;
        if(IsNetworkSessionActive()){
            var life=player.GetComponent<NetworkOxygenState>();var movement=player.GetComponent<NetworkPlayerMovementState>();
            if(life==null||life.IsDead.Value||movement!=null&&!movement.MovementEnabled.Value)return false;
        }else if(player.GetComponent<OxygenSystem>()?.IsDead()==true)return false;
        return Vector3.Distance(player.transform.position,repairStation.fixPointTransform.position)<=repairStation.interactionDistance;
    }
    public void RequestMaintenance(bool held)
    {
        if(IsNetworkSessionActive()){if(IsSpawned)MaintainServerRpc(held);}
        else {
            RefreshLocalPlayerReference();
            SetMaintenance(0,playerTransform!=null?playerTransform.GetComponent<PlayerController>():null,held);
        }
    }
    [ServerRpc(RequireOwnership=false)]void MaintainServerRpc(bool held,ServerRpcParams rpc=default)
    {
        if(!IsServer)return;
        ulong id=rpc.Receive.SenderClientId;
        var player=NetworkManager.ConnectedClients.TryGetValue(id,out var peer)&&peer.PlayerObject!=null?peer.PlayerObject.GetComponent<PlayerController>():null;
        SetMaintenance(id,player,held);
    }
    void SetMaintenance(ulong id,PlayerController player,bool held)
    {
        if(!held){if(MaintenanceOwner==id)CancelMaintenance();return;}
        if(!CanRepairAtStation(player)||!NeedsMaintenance||AvailableRepairMetal<RepairMetalCost)return;
        if(MaintenanceOwner!=ulong.MaxValue&&MaintenanceOwner!=id)return;
        if(IsNetworkSessionActive())MaintenanceOwnerNetwork.Value=id;else offlineRepairOwner=id;
        lastRepairHeartbeat=Time.time;
    }
    void CancelMaintenance()
    {
        if(IsNetworkSessionActive()){MaintenanceOwnerNetwork.Value=ulong.MaxValue;MaintenanceProgressNetwork.Value=0;}
        else {offlineRepairOwner=ulong.MaxValue;offlineRepairProgress=0;}
    }
    void TickMaintenance()
    {
        if(MaintenanceOwner==ulong.MaxValue)return;
        PlayerController player=null;
        if(IsNetworkSessionActive()){
            if(NetworkManager.ConnectedClients.TryGetValue(MaintenanceOwner,out var peer)&&peer.PlayerObject!=null)player=peer.PlayerObject.GetComponent<PlayerController>();
        }else {RefreshLocalPlayerReference();player=playerTransform!=null?playerTransform.GetComponent<PlayerController>():null;}
        if(Time.time-lastRepairHeartbeat>.65f||!CanRepairAtStation(player)||!NeedsMaintenance||AvailableRepairMetal<RepairMetalCost){CancelMaintenance();return;}
        float progress=MaintenanceProgress+Time.deltaTime;
        if(IsNetworkSessionActive())MaintenanceProgressNetwork.Value=progress;else offlineRepairProgress=progress;
        if(progress<MaintenanceSeconds)return;
        // Atomic host-side debit: two repairers cannot spend twice or repair for free.
        if(repairDrill!=null&&repairDrill.TrySpendMetal(RepairMetalCost)){
            if(IsNetworkSessionActive())UsesRemainingNetwork.Value=UseCapacity;else offlineUses=UseCapacity;
        }
        CancelMaintenance();
    }

    void OnDrawGizmosSelected()
    {
        Vector3 center = transform.position;
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            center = renderer.bounds.center;
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(center, interactionDistance);

        if (objectiveTransform != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(center, objectiveTransform.position);
            Gizmos.DrawWireSphere(objectiveTransform.position, 1f);
        }
    }

    private class MonsterDistanceInfo
    {
        public string monsterName;
        public float distance;
        public bool isNeutralized;
    }
}
