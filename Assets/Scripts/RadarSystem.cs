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

    private Transform playerTransform;
    private float lastScanTime = -999f;
    private bool isPlayerInRange = false;
    private AudioSource audioSource;

    private const KeyCode INTERACTION_KEY = KeyCode.E;

    void Start()
    {
        RefreshLocalPlayerReference();

        if (playerTransform == null)
        {
            Debug.LogError("<color=red>[Radar]</color> Player with tag 'Player' not found!");
        }
        else
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
            LastScanTimeNetwork.Value = lastScanTime;
            IsOnCooldownNetwork.Value = IsOnCooldown();
        }

        SyncLocalStateFromNetwork();
    }

    void Update()
    {
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
            else if (Input.GetKeyDown(INTERACTION_KEY))
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
                interactionPromptText.text = $"Press {INTERACTION_KEY} to scan radar";
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

    void TryActivateRadarOffline()
    {
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
        lastScanTime = Time.time;
    }

    void TryActivateRadarForClient(ulong clientId)
    {
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
        lastScanTime = Time.time;
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
        return distance <= interactionDistance;
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

        if (SurveyorMonster.Instance != null && IsServer)
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
        return Time.time - scanTime < effectiveCooldown;
    }

    float GetRemainingCooldown()
    {
        float effectiveCooldown = GetEffectiveCooldownTime();
        float scanTime = IsNetworkSessionActive() ? LastScanTimeNetwork.Value : lastScanTime;
        float remaining = effectiveCooldown - (Time.time - scanTime);
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

        if (IsOnCooldown())
        {
            float remaining = GetRemainingCooldown();
            cooldownText.text = $"Radar: {remaining:F1}s";
            cooldownText.color = new Color(0f, 0.8f, 1f);
            cooldownText.fontSize = 18;
            cooldownText.fontStyle = TMPro.FontStyles.Bold;
            cooldownText.alignment = TMPro.TextAlignmentOptions.Center;
            cooldownText.gameObject.SetActive(true);
        }
        else
        {
            cooldownText.gameObject.SetActive(false);
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
        bool flareActive = SolarFlareSystem.Instance != null && SolarFlareSystem.Instance.IsFlareActiveNetwork.Value;
        return powerOut || flareActive;
    }

    bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    void SyncLocalStateFromNetwork()
    {
        lastScanTime = LastScanTimeNetwork.Value;
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
