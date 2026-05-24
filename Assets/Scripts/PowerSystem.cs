using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using Unity.Netcode;

public class PowerSystem : NetworkBehaviour
{
    public static PowerSystem Instance { get; private set; }

    [Header("Power Outage Settings")]
    [Tooltip("Check interval for power outage (60 seconds = 1 minute)")]
    public float outageCheckInterval = 60f;
    
    [Tooltip("Chance of power outage (0.2 = 1 in 5 chance)")]
    [Range(0f, 1f)]
    public float outageChance = 0.2f;

    [Header("Light Groups")]
    [Tooltip("White lights that turn off during power outage")]
    public Light[] whiteLights;
    
    [Tooltip("Red emergency lights that turn on during power outage")]
    public Light[] redLights;

    [Header("Power Box Interaction")]
    [Tooltip("The power box GameObject")]
    public Transform powerBoxTransform;
    
    [Tooltip("Distance to interact with power box")]
    public float interactionDistance = 3f;

    [Header("UI References")]
    public TextMeshProUGUI powerStatusText;
    public TextMeshProUGUI interactionPromptText;

    [Header("Audio (Optional)")]
    public AudioClip powerOutSound;
    public AudioClip powerRestoreSound;

    private bool isPowerOut = false;
    private float nextOutageCheckTime;
    private Transform playerTransform;
    private bool isPlayerNearPowerBox = false;
    private AudioSource audioSource;

    public NetworkVariable<bool> IsPowerOutNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<float> NextOutageCheckTimeNetwork = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private const KeyCode INTERACTION_KEY = KeyCode.E;

    public bool IsPowerOut => IsNetworkSessionActive() ? IsPowerOutNetwork.Value : isPowerOut;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        RefreshLocalPlayerTransform();

        if (playerTransform == null && !IsNetworkSessionActive())
        {
            Debug.LogError("PowerSystem: Player with tag 'Player' not found!");
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null && (powerOutSound != null || powerRestoreSound != null))
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        nextOutageCheckTime = Time.time + outageCheckInterval;
        
        SetPowerState(true);

        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(false);
        }

        UpdatePowerStatusUI();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            isPowerOut = false;
            nextOutageCheckTime = Time.time + outageCheckInterval;
            IsPowerOutNetwork.Value = false;
            NextOutageCheckTimeNetwork.Value = nextOutageCheckTime;
            SetPowerState(true);
            UpdatePowerStatusUI();
        }
        else
        {
            SyncLocalStateFromNetwork();
        }
    }

    void Update()
    {
        if (IsNetworkSessionActive())
        {
            RefreshLocalPlayerTransform();

            if (IsServer)
            {
                if (Time.time >= NextOutageCheckTimeNetwork.Value)
                {
                    CheckForPowerOutage();
                    nextOutageCheckTime = Time.time + outageCheckInterval;
                    NextOutageCheckTimeNetwork.Value = nextOutageCheckTime;
                }

                if (isPowerOut && playerTransform != null && powerBoxTransform != null)
                {
                    CheckPowerBoxInteraction();
                }

                IsPowerOutNetwork.Value = isPowerOut;
            }
            else
            {
                SyncLocalStateFromNetwork();
                if (playerTransform != null && powerBoxTransform != null && IsPowerOut)
                {
                    CheckPowerBoxInteraction();
                }
            }

            UpdatePowerStatusUI();
            return;
        }

        if (Time.time >= nextOutageCheckTime)
        {
            CheckForPowerOutage();
            nextOutageCheckTime = Time.time + outageCheckInterval;
        }

        if (isPowerOut && playerTransform != null && powerBoxTransform != null)
        {
            CheckPowerBoxInteraction();
        }
    }

    void CheckForPowerOutage()
    {
        if (IsNetworkSessionActive() && !IsServer)
        {
            return;
        }

        if (isPowerOut) return;

        float randomValue = Random.value;
        if (randomValue <= outageChance)
        {
            TriggerPowerOutage();
        }
    }

    void TriggerPowerOutage()
    {
        if (IsNetworkSessionActive() && !IsServer)
        {
            return;
        }

        isPowerOut = true;
        if (IsNetworkSessionActive())
        {
            IsPowerOutNetwork.Value = true;
            if (NetworkGameState.Instance != null)
            {
                NetworkGameState.Instance.SetPowerOut(true);
                NetworkGameState.Instance.SetRadarAvailable(false);
                NetworkGameState.Instance.SetDefenseAvailable(false);
            }
            PowerOutageClientRpc();
            return;
        }

        RadarSystem radar = FindObjectOfType<RadarSystem>();
        if (radar != null) radar.ForceHidePrompt();

        DefenseSystem defense = FindObjectOfType<DefenseSystem>();
        if (defense != null) defense.ForceHidePrompt();

        if (audioSource != null && powerOutSound != null)
        {
            audioSource.PlayOneShot(powerOutSound);
        }

        UpdatePowerStatusUI();

        Debug.Log("Power outage! Systems affected. Go to the power box to restore power.");
    }

    void CheckPowerBoxInteraction()
    {
        float distance = Vector3.Distance(playerTransform.position, powerBoxTransform.position);
        bool wasNearPowerBox = isPlayerNearPowerBox;
        isPlayerNearPowerBox = distance <= interactionDistance;

        if (isPlayerNearPowerBox && !wasNearPowerBox)
        {
            ShowPowerBoxPrompt();
        }
        else if (!isPlayerNearPowerBox && wasNearPowerBox)
        {
            HidePowerBoxPrompt();
        }

        if (isPlayerNearPowerBox && Input.GetKeyDown(INTERACTION_KEY))
        {
            RestorePower();
        }
    }

    void ShowPowerBoxPrompt()
    {
        if (interactionPromptText != null)
        {
            interactionPromptText.text = $"Press {INTERACTION_KEY} to restore power";
            interactionPromptText.gameObject.SetActive(true);
        }
    }

    void HidePowerBoxPrompt()
    {
        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(false);
        }
    }

    public void RestorePower()
    {
        if (IsNetworkSessionActive() && !IsServer)
        {
            RequestRestorePowerServerRpc();
            return;
        }

        if (!isPowerOut) return;

        isPowerOut = false;
        if (IsNetworkSessionActive())
        {
            IsPowerOutNetwork.Value = false;
            if (NetworkGameState.Instance != null)
            {
                NetworkGameState.Instance.SetPowerOut(false);
                NetworkGameState.Instance.SetRadarAvailable(true);
                NetworkGameState.Instance.SetDefenseAvailable(true);
            }
            ResetAllSystemCooldowns();
            PowerRestoreClientRpc();
            return;
        }

        ResetAllSystemCooldowns();

        if (audioSource != null && powerRestoreSound != null)
        {
            audioSource.PlayOneShot(powerRestoreSound);
        }

        HidePowerBoxPrompt();
        UpdatePowerStatusUI();

        Debug.Log("Power restored! All systems online. Cooldowns reset.");
    }

    void SetPowerState(bool powerOn)
    {
        foreach (Light light in whiteLights)
        {
            if (light != null)
            {
                light.enabled = powerOn;
            }
        }

        foreach (Light light in redLights)
        {
            if (light != null)
            {
                light.enabled = !powerOn;
            }
        }
    }

    void ResetAllSystemCooldowns()
    {
        RadarSystem radar = FindObjectOfType<RadarSystem>();
        if (radar != null)
        {
            radar.ResetCooldown();
        }

        DefenseSystem defense = FindObjectOfType<DefenseSystem>();
        if (defense != null)
        {
            defense.ResetCooldown();
        }
    }

    void UpdatePowerStatusUI()
    {
        if (powerStatusText != null)
        {
            if (IsPowerOut)
            {
                powerStatusText.text = "<color=red>POWER: OFFLINE</color>";
                powerStatusText.color = Color.red;
            }
            else
            {
                powerStatusText.text = "<color=green>POWER: ONLINE</color>";
                powerStatusText.color = Color.green;
            }
        }
    }

    public float GetCooldownMultiplier()
    {
        return IsPowerOut ? 2f : 1f;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestRestorePowerServerRpc(ServerRpcParams serverRpcParams = default)
    {
        if (!IsServer)
        {
            return;
        }

        if (!IsPlayerNearPowerBox(serverRpcParams.Receive.SenderClientId))
        {
            Debug.LogWarning($"PowerSystem: Ignored restore request from client {serverRpcParams.Receive.SenderClientId} because they are not near the power box.");
            return;
        }

        RestorePower();
    }

    [ClientRpc]
    private void PowerOutageClientRpc()
    {
        isPowerOut = true;
        SetPowerState(false);

        RadarSystem radar = FindObjectOfType<RadarSystem>();
        if (radar != null) radar.ForceHidePrompt();

        DefenseSystem defense = FindObjectOfType<DefenseSystem>();
        if (defense != null) defense.ForceHidePrompt();

        if (audioSource != null && powerOutSound != null)
        {
            audioSource.PlayOneShot(powerOutSound);
        }

        UpdatePowerStatusUI();
    }

    [ClientRpc]
    private void PowerRestoreClientRpc()
    {
        isPowerOut = false;
        SetPowerState(true);

        RadarSystem radar = FindObjectOfType<RadarSystem>();
        if (radar != null) radar.ResetCooldown();

        DefenseSystem defense = FindObjectOfType<DefenseSystem>();
        if (defense != null) defense.ResetCooldown();

        if (audioSource != null && powerRestoreSound != null)
        {
            audioSource.PlayOneShot(powerRestoreSound);
        }

        HidePowerBoxPrompt();
        UpdatePowerStatusUI();
    }

    private void SyncLocalStateFromNetwork()
    {
        if (!IsNetworkSessionActive())
        {
            return;
        }

        isPowerOut = IsPowerOutNetwork.Value;
        nextOutageCheckTime = NextOutageCheckTimeNetwork.Value;
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    private void RefreshLocalPlayerTransform()
    {
        if (!IsNetworkSessionActive())
        {
            if (playerTransform == null)
            {
                playerTransform = GameObject.FindGameObjectWithTag("Player")?.transform;
            }

            return;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            playerTransform = NetworkManager.Singleton.LocalClient.PlayerObject.transform;
        }
    }

    private bool IsPlayerNearPowerBox(ulong clientId)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.ConnectedClients == null)
        {
            return false;
        }

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null || powerBoxTransform == null)
        {
            return false;
        }

        float distance = Vector3.Distance(client.PlayerObject.transform.position, powerBoxTransform.position);
        return distance <= interactionDistance;
    }

    void OnDrawGizmosSelected()
    {
        if (powerBoxTransform != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(powerBoxTransform.position, interactionDistance);
        }
    }
}
