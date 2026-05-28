using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class DefenseSystem : NetworkBehaviour
{
    [Header("Defense Settings")]
    [Tooltip("Name of this defense system (e.g., 'Left Door', 'Right Door')")]
    public string defenseName = "Defense";

    [Tooltip("The objective that needs protection")]
    public Transform objectiveTransform;

    [Tooltip("Distance within which player can interact with defense system")]
    public float interactionDistance = 3f;

    [Tooltip("Zombies within this distance of objective will be teleported")]
    public float threatRadius = 20f;

    [Tooltip("Cooldown time in seconds (90 seconds = 1 minute 30 seconds)")]
    public float cooldownTime = 90f;

    [Tooltip("Time zombie stays disabled before respawning (30 seconds)")]
    public float disableTime = 30f;

    [Tooltip("Tag to identify monsters")]
    public string monsterTag = "Enemy";

    [Header("Respawn Points")]
    [Tooltip("Set 4 respawn point transforms where zombies will teleport")]
    public Transform[] respawnPoints = new Transform[4];

    [Header("UI References")]
    public TextMeshProUGUI interactionPromptText;
    public TextMeshProUGUI defenseResultsText;
    public GameObject defensePanel;

    [Tooltip("Legacy - Leave empty to use centralized DefenseCooldownUI")]
    public TextMeshProUGUI cooldownText;

    [Header("Audio (Optional)")]
    public AudioClip activationSound;
    public AudioClip cooldownSound;

    public NetworkVariable<float> LastActivationTimeNetwork = new NetworkVariable<float>(
        -999f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsOnCooldownNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Transform playerTransform;
    private float lastActivationTime = -999f;
    private bool isPlayerInRange = false;
    private AudioSource audioSource;
    private readonly List<GameObject> currentlyDisabledZombies = new List<GameObject>();

    private const KeyCode INTERACTION_KEY = KeyCode.E;

    void Start()
    {
        RefreshLocalPlayerReference();

        if (playerTransform == null)
        {
            Debug.LogError("DefenseSystem: Player with tag 'Player' not found!");
        }

        if (objectiveTransform == null)
        {
            Debug.LogError("DefenseSystem: Objective Transform not assigned!");
        }

        if (respawnPoints.Length != 4)
        {
            Debug.LogWarning("DefenseSystem: Should have exactly 4 respawn points!");
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null && (activationSound != null || cooldownSound != null))
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(false);
        }

        if (defensePanel != null)
        {
            defensePanel.SetActive(false);
        }

        if (cooldownText != null)
        {
            cooldownText.gameObject.SetActive(false);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            LastActivationTimeNetwork.Value = lastActivationTime;
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

        if (IsDefenseLockedOut())
        {
            ForceHidePrompt();
        }

        CheckPlayerDistance();

        if (isPlayerInRange && !IsDefenseLockedOut())
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
                        TryActivateDefenseForClient(NetworkManager.Singleton.LocalClientId);
                    }
                    else
                    {
                        RequestActivateDefenseServerRpc();
                    }
                }
                else
                {
                    TryActivateDefenseOffline();
                }
            }
        }

        UpdateCooldownDisplay();

        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                LastActivationTimeNetwork.Value = lastActivationTime;
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
        Vector3 defenseCenter = GetDefenseCenter();
        float distance = Vector3.Distance(playerTransform.position, defenseCenter);
        bool wasInRange = isPlayerInRange;
        isPlayerInRange = distance <= interactionDistance;

        if (isPlayerInRange && !wasInRange)
        {
            ShowInteractionPrompt();
        }
        else if (!isPlayerInRange && wasInRange)
        {
            HideInteractionPrompt();
        }
    }

    Vector3 GetDefenseCenter()
    {
        Vector3 defenseCenter = transform.position;

        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            defenseCenter = renderer.bounds.center;
        }

        return defenseCenter;
    }

    void ShowInteractionPrompt()
    {
        if (interactionPromptText != null)
        {
            if (!IsOnCooldown() && !IsDefenseLockedOut())
            {
                interactionPromptText.text = $"Press {INTERACTION_KEY} to activate defense";
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
            interactionPromptText.text = $"{defenseName} on cooldown: {remainingTime:F1}s";
            interactionPromptText.gameObject.SetActive(true);
        }
    }

    bool IsOnCooldown()
    {
        float effectiveCooldown = GetEffectiveCooldownTime();
        float activationTime = IsNetworkSessionActive() ? LastActivationTimeNetwork.Value : lastActivationTime;
        return GetCooldownClockTime() - activationTime < effectiveCooldown;
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
        lastActivationTime = -999f;
        if (IsServer)
        {
            LastActivationTimeNetwork.Value = -999f;
            IsOnCooldownNetwork.Value = false;
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

    void TryActivateDefenseOffline()
    {
        if (IsOnCooldown())
        {
            PlayCooldownSound();
            return;
        }

        ActivateDefenseOffline();
        lastActivationTime = Time.time;
    }

    void TryActivateDefenseForClient(ulong clientId)
    {
        if (!CanClientActivate(clientId))
        {
            DefenseResultsClientRpc($"<b>{defenseName}</b>\n\nActivation failed.\nStay closer to the console.");
            return;
        }

        if (IsOnCooldown())
        {
            PlayCooldownSound();
            DefenseResultsClientRpc($"{defenseName} on cooldown.\nPlease wait {GetRemainingCooldown():F1}s.");
            return;
        }

        ActivateDefenseServer(clientId);
    }

    bool CanClientActivate(ulong clientId)
    {
        if (!IsNetworkSessionActive())
        {
            return true;
        }

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
        {
            return false;
        }

        float distance = Vector3.Distance(client.PlayerObject.transform.position, GetDefenseCenter());
        return distance <= interactionDistance;
    }

    void ActivateDefenseServer(ulong clientId)
    {
        if (!IsServer)
        {
            return;
        }

        if (IsDefenseLockedOut())
        {
            DefenseResultsClientRpc($"{defenseName} unavailable during outage.");
            return;
        }

        ActivateDefenseInternal(isNetworkSession: true);
        lastActivationTime = GetCooldownClockTime();
        LastActivationTimeNetwork.Value = lastActivationTime;
        IsOnCooldownNetwork.Value = true;
    }

    void ActivateDefenseOffline()
    {
        ActivateDefenseInternal(isNetworkSession: false);
    }

    void ActivateDefenseInternal(bool isNetworkSession)
    {
        if (objectiveTransform == null)
        {
            Debug.LogWarning("DefenseSystem: Cannot activate - objective not set!");
            return;
        }

        if (respawnPoints.Length == 0)
        {
            Debug.LogWarning("DefenseSystem: Cannot activate - no respawn points set!");
            return;
        }

        GameObject[] monsters = GameObject.FindGameObjectsWithTag(monsterTag);
        if (monsters.Length == 0)
        {
            DisplayDefenseResults("No threats detected.", isNetworkSession);
            return;
        }

        int affectedCount = 0;
        List<GameObject> threateningZombies = new List<GameObject>();

        foreach (GameObject monster in monsters)
        {
            if (monster == null || !monster.activeInHierarchy)
            {
                continue;
            }

            float distance = Vector3.Distance(monster.transform.position, objectiveTransform.position);
            if (distance <= threatRadius)
            {
                threateningZombies.Add(monster);
                affectedCount++;
            }
        }

        if (affectedCount == 0)
        {
            DisplayDefenseResults("DEFENSE ACTIVATED\n\nNo threats within range.\nObjective is secure.", isNetworkSession);
        }
        else
        {
            foreach (GameObject zombie in threateningZombies)
            {
                if (zombie == null)
                {
                    continue;
                }

                ZombieAI ai = zombie.GetComponent<ZombieAI>();
                if (ai != null && ai.IsNetworkSessionActive() && ai.IsServer)
                {
                    ai.ServerNeutralizeForDefense(GetRandomRespawnPoint(), disableTime);
                }
                else if (ai != null && !ai.IsNetworkSessionActive())
                {
                    StartCoroutine(DisableAndRespawnZombieOffline(zombie));
                }
            }

            DisplayDefenseResults($"<b>DEFENSE ACTIVATED</b>\n\n{affectedCount} threat(s) neutralized!\nRelocating enemies...", isNetworkSession);
        }

        if (audioSource != null && activationSound != null)
        {
            audioSource.PlayOneShot(activationSound);
        }

        if (isNetworkSession)
        {
            DefenseResultsClientRpc($"<b>DEFENSE ACTIVATED</b>\n\n{affectedCount} threat(s) neutralized!");
        }
    }

    IEnumerator DisableAndRespawnZombieOffline(GameObject zombie)
    {
        if (zombie == null)
        {
            yield break;
        }

        currentlyDisabledZombies.Add(zombie);

        CharacterController controller = zombie.GetComponent<CharacterController>();
        ZombieAI ai = zombie.GetComponent<ZombieAI>();
        Animator animator = zombie.GetComponent<Animator>();

        if (controller != null) controller.enabled = false;
        if (ai != null) ai.enabled = false;
        if (animator != null) animator.enabled = false;

        Renderer[] renderers = zombie.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            renderer.enabled = false;
        }

        yield return new WaitForSeconds(disableTime);

        if (zombie == null)
        {
            currentlyDisabledZombies.Remove(zombie);
            yield break;
        }

        Transform selectedRespawnPoint = GetRandomRespawnPoint();
        if (selectedRespawnPoint != null)
        {
            if (controller != null)
            {
                controller.enabled = false;
                zombie.transform.position = selectedRespawnPoint.position;
                zombie.transform.rotation = selectedRespawnPoint.rotation;
                yield return null;
                controller.enabled = true;
            }
            else
            {
                zombie.transform.position = selectedRespawnPoint.position;
                zombie.transform.rotation = selectedRespawnPoint.rotation;
            }
        }

        foreach (Renderer renderer in renderers)
        {
            renderer.enabled = true;
        }

        if (ai != null)
        {
            ai.enabled = true;
            ai.ResetToWander();
        }
        if (animator != null) animator.enabled = true;

        currentlyDisabledZombies.Remove(zombie);
    }

    Transform GetRandomRespawnPoint()
    {
        List<Transform> validRespawnPoints = new List<Transform>();

        foreach (Transform point in respawnPoints)
        {
            if (point != null)
            {
                validRespawnPoints.Add(point);
            }
        }

        if (validRespawnPoints.Count == 0)
        {
            Debug.LogWarning("DefenseSystem: No valid respawn points available!");
            return null;
        }

        int randomIndex = Random.Range(0, validRespawnPoints.Count);
        return validRespawnPoints[randomIndex];
    }

    void DisplayDefenseResults(string message, bool isNetworkSession)
    {
        if (defenseResultsText != null)
        {
            defenseResultsText.text = message;
            defenseResultsText.enableWordWrapping = true;
            defenseResultsText.overflowMode = TextOverflowModes.Overflow;
        }

        if (defensePanel != null)
        {
            defensePanel.SetActive(true);
            CancelInvoke(nameof(HideDefenseResults));
            Invoke(nameof(HideDefenseResults), 5f);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestActivateDefenseServerRpc(ServerRpcParams serverRpcParams = default)
    {
        if (!IsServer)
        {
            return;
        }

        TryActivateDefenseForClient(serverRpcParams.Receive.SenderClientId);
    }

    [ClientRpc]
    private void DefenseResultsClientRpc(string message)
    {
        if (defenseResultsText != null)
        {
            defenseResultsText.text = message;
            defenseResultsText.enableWordWrapping = true;
            defenseResultsText.overflowMode = TextOverflowModes.Overflow;
        }

        if (defensePanel != null)
        {
            defensePanel.SetActive(true);
            CancelInvoke(nameof(HideDefenseResults));
            Invoke(nameof(HideDefenseResults), 5f);
        }

        if (audioSource != null && activationSound != null)
        {
            audioSource.PlayOneShot(activationSound);
        }
    }

    void HideDefenseResults()
    {
        if (defensePanel != null)
        {
            defensePanel.SetActive(false);
        }
    }

    void PlayCooldownSound()
    {
        if (audioSource != null && cooldownSound != null)
        {
            audioSource.PlayOneShot(cooldownSound);
        }
    }

    void UpdateCooldownDisplay()
    {
        if (IsOnCooldown())
        {
            float remaining = GetRemainingCooldown();

            if (DefenseCooldownUI.Instance != null)
            {
                DefenseCooldownUI.Instance.RegisterDefense(this, defenseName, remaining);
            }
            else if (cooldownText != null)
            {
                cooldownText.text = $"Defense: {remaining:F1}s";
                cooldownText.color = new Color(1f, 0.6f, 0f);
                cooldownText.fontSize = 18;
                cooldownText.fontStyle = TMPro.FontStyles.Bold;
                cooldownText.alignment = TMPro.TextAlignmentOptions.Center;
                cooldownText.gameObject.SetActive(true);
            }
        }
        else
        {
            if (DefenseCooldownUI.Instance != null)
            {
                DefenseCooldownUI.Instance.UnregisterDefense(this);
            }
            else if (cooldownText != null)
            {
                cooldownText.gameObject.SetActive(false);
            }
        }
    }

    public float GetRemainingCooldown()
    {
        float effectiveCooldown = GetEffectiveCooldownTime();
        float activationTime = IsNetworkSessionActive() ? LastActivationTimeNetwork.Value : lastActivationTime;
        float remaining = effectiveCooldown - (GetCooldownClockTime() - activationTime);
        return Mathf.Max(0f, remaining);
    }

    bool IsDefenseLockedOut()
    {
        bool powerOut = PowerSystem.Instance != null && PowerSystem.Instance.IsPowerOut;
        bool flareActive = SolarFlareSystem.Instance != null && SolarFlareSystem.Instance.IsFlareActiveNetwork.Value;
        return powerOut || flareActive;
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
        lastActivationTime = LastActivationTimeNetwork.Value;
    }

    void OnDrawGizmosSelected()
    {
        Vector3 center = transform.position;
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            center = renderer.bounds.center;
        }

        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(center, interactionDistance);

        if (objectiveTransform != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(objectiveTransform.position, threatRadius);

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(center, objectiveTransform.position);
        }

        if (respawnPoints != null)
        {
            foreach (Transform respawnPoint in respawnPoints)
            {
                if (respawnPoint != null)
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawWireSphere(respawnPoint.position, 1f);
                    Gizmos.DrawLine(center, respawnPoint.position);
                }
            }
        }
    }
}
