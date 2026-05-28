using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using Unity.Netcode;

public class SolarFlareSystem : NetworkBehaviour
{
    public static SolarFlareSystem Instance { get; private set; }

    [Header("Solar Flare Settings")]
    [Tooltip("Check interval for solar flare (60 seconds = 1 minute)")]
    public float checkInterval = 60f;

    [Tooltip("Chance of solar flare (0.125 = 1 in 8 chance)")]
    [Range(0f, 1f)]
    public float flareChance = 0.125f;

    [Header("Visual Effects")]
    [Tooltip("Orange color for ambient light during flare")]
    public Color flareColor = new Color(1f, 0.5f, 0f);

    [Tooltip("Normal ambient color (set to black/dark for night)")]
    public Color normalAmbientColor = new Color(0.1f, 0.1f, 0.1f);

    [Tooltip("Skybox material for flare (New Material 1)")]
    public Material flareSkyboxMaterial;

    [Tooltip("Skybox material for normal (New Material 4)")]
    public Material normalSkyboxMaterial;

    [Tooltip("Time for sky to fade to orange")]
    public float skyFadeDuration = 2f;

    [Header("Camera Shake")]
    [Tooltip("Duration of camera shake")]
    public float shakeDuration = 1f;

    [Tooltip("Intensity of camera shake")]
    public float shakeIntensity = 0.15f;

    [Header("Fix Point")]
    [Tooltip("Transform of the repair point outside")]
    public Transform fixPointTransform;

    [Tooltip("Distance to interact with fix point")]
    public float interactionDistance = 3f;

    [Tooltip("Time to hold E to repair (seconds)")]
    public float repairTime = 5f;

    [Header("Systems to Disable")]
    public RadarSystem radarSystem;
    public DefenseSystem defenseSystem;
    public PowerSystem powerSystem;

    [Header("Lights")]
    [Tooltip("Bunker lights that turn off during flare")]
    public Light[] bunkerLights;

    [Header("UI References")]
    [Tooltip("Text showing solar flare warning")]
    public TextMeshProUGUI warningText;

    [Tooltip("Interaction prompt at fix point")]
    public TextMeshProUGUI interactionPromptText;

    [Tooltip("Repair progress bar (optional)")]
    public Image repairProgressBar;

    [Header("Audio")]
    public AudioClip bangSound;
    public AudioClip warningSound;
    public AudioClip repairCompleteSound;

    [Tooltip("Volume of bang sound")]
    [Range(0f, 1f)]
    public float bangVolume = 1f;

    private bool isFlareActive = false;
    private bool isRepairing = false;
    private float repairProgress = 0f;
    private float nextCheckTime;
    private Transform playerTransform;
    private Camera mainCamera;
    private AudioSource audioSource;
    private bool isPlayerNearFixPoint = false;
    private Vector3 shakeOffset = Vector3.zero;
    private PlayerController playerController;
    private CompassPointer compassPointer;

    public NetworkVariable<bool> IsFlareActiveNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsRepairingNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<float> RepairProgressNetwork = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private const KeyCode INTERACTION_KEY = KeyCode.E;

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
        RefreshLocalPlayerReferences();
        mainCamera = Camera.main;

        if (playerTransform == null && !IsNetworkSessionActive())
        {
            Debug.LogError("<color=red>[Solar Flare]</color> Player not found!");
        }
        else
        {
            playerController = playerTransform.GetComponent<PlayerController>();
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.priority = 128;

        compassPointer = FindObjectOfType<CompassPointer>();
        if (compassPointer == null)
        {
            Debug.LogWarning("<color=yellow>[Solar Flare]</color> CompassPointer not found in scene. Arrow guidance will not work.");
        }

        nextCheckTime = Time.time + checkInterval;

        if (warningText != null)
        {
            warningText.gameObject.SetActive(false);
        }

        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(false);
        }

        if (repairProgressBar != null)
        {
            repairProgressBar.fillAmount = 0f;
            repairProgressBar.gameObject.SetActive(false);
        }

        normalAmbientColor = RenderSettings.ambientLight;

        if (normalSkyboxMaterial == null)
        {
            normalSkyboxMaterial = RenderSettings.skybox;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            isFlareActive = false;
            isRepairing = false;
            repairProgress = 0f;
            nextCheckTime = Time.time + checkInterval;
            IsFlareActiveNetwork.Value = false;
            IsRepairingNetwork.Value = false;
            RepairProgressNetwork.Value = 0f;
        }

        SyncLocalStateFromNetwork();
    }

    void Update()
    {
        if (IsNetworkSessionActive())
        {
            RefreshLocalPlayerReferences();

            if (!IsServer)
            {
                CheckPlayerProximityToFixPoint();
                HandleRepair();
                SyncLocalStateFromNetwork();
                ApplyShakeToCamera();
                return;
            }

            CheckPlayerProximityToFixPoint();
            if (!isFlareActive && Time.time >= nextCheckTime)
            {
                CheckForSolarFlareServer();
            }

            if (isFlareActive)
            {
                HandleRepair();
                HandleRepairServer();
            }

            PushNetworkStateFromLocal();
            ApplyShakeToCamera();
            return;
        }

        if (!isFlareActive && Time.time >= nextCheckTime)
        {
            CheckForSolarFlare();
        }

        if (isFlareActive)
        {
            CheckPlayerProximityToFixPoint();
            HandleRepair();
        }

        ApplyShakeToCamera();
    }

    void ApplyShakeToCamera()
    {
        if (playerController != null)
        {
            playerController.externalCameraOffset = shakeOffset;
        }
    }

    void CheckForSolarFlare()
    {
        nextCheckTime = Time.time + checkInterval;

        if (IsNetworkSessionActive() && !IsServer)
        {
            return;
        }

        if (PowerSystem.Instance != null && PowerSystem.Instance.IsPowerOut)
        {
            Debug.Log("<color=cyan>[Solar Flare]</color> Skipping check - power outage is active");
            return;
        }

        float roll = Random.Range(0f, 1f);
        if (roll <= flareChance)
        {
            TriggerSolarFlare();
        }
        else
        {
            Debug.Log($"<color=cyan>[Solar Flare]</color> Check rolled {roll:F2} - No flare (need <= {flareChance:F2})");
        }
    }

    void TriggerSolarFlare()
    {
        if (IsNetworkSessionActive() && !IsServer)
        {
            return;
        }

        Debug.Log("<color=orange>[Solar Flare]</color> SOLAR FLARE INCOMING!");
        isFlareActive = true;
        isRepairing = false;
        repairProgress = 0f;

        if (IsNetworkSessionActive())
        {
            IsFlareActiveNetwork.Value = true;
            IsRepairingNetwork.Value = false;
            RepairProgressNetwork.Value = 0f;
            if (NetworkGameState.Instance != null)
            {
                NetworkGameState.Instance.SetSolarFlareActive(true);
                NetworkGameState.Instance.SetRadarAvailable(false);
                NetworkGameState.Instance.SetDefenseAvailable(false);
            }
            TriggerSolarFlareClientRpc();
        }

        if (compassPointer != null && fixPointTransform != null)
        {
            compassPointer.SetTarget(fixPointTransform, "REPAIR NEEDED");
            Debug.Log("<color=orange>[Solar Flare]</color> Compass now pointing to fix point");
        }

        if (!IsNetworkSessionActive())
        {
            StartCoroutine(SolarFlareSequence());
        }
    }

    IEnumerator SolarFlareSequence()
    {
        if (warningSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(warningSound);
        }

        if (warningText != null)
        {
            warningText.gameObject.SetActive(true);
            warningText.text = "⚠ SOLAR FLARE WARNING ⚠";
        }

        float exposureChangeRate = 2f;
        float fadeDuration = Mathf.Abs(8f - 0.92f) / exposureChangeRate;

        StartCoroutine(FadeSkyToOrange());

        if (mainCamera != null)
        {
            StartCoroutine(ShakeCamera());
        }

        yield return new WaitForSeconds(fadeDuration);

        if (bangSound != null && audioSource != null)
        {
            audioSource.clip = bangSound;
            audioSource.volume = bangVolume;
            audioSource.Play();
            Debug.Log($"<color=orange>[Solar Flare]</color> Playing bang sound - Duration: {bangSound.length}s");
        }

        StartCoroutine(RestoreSkyColor());

        DisableAllSystems();

        if (warningText != null)
        {
            warningText.text = "⚠ SYSTEMS OFFLINE - REPAIR SOLAR PANEL ⚠";
        }

        Debug.Log("<color=red>[Solar Flare]</color> All systems disabled! Go to fix point to repair.");
    }

    IEnumerator FadeSkyToOrange()
    {
        Color startAmbientColor = RenderSettings.ambientLight;

        float startExposure = 0.92f;
        if (RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_Exposure"))
        {
            startExposure = RenderSettings.skybox.GetFloat("_Exposure");
        }

        if (flareSkyboxMaterial != null)
        {
            RenderSettings.skybox = flareSkyboxMaterial;
            Debug.Log($"<color=orange>[Solar Flare]</color> Switched to FLARE skybox material (will fade from {startExposure} to 8.0)");
        }
        else
        {
            Debug.LogWarning("<color=red>[Solar Flare]</color> Flare skybox material is NULL!");
        }

        float targetExposure = 8f;
        float exposureChangeRate = 2f;
        float fadeDuration = Mathf.Abs(targetExposure - startExposure) / exposureChangeRate;
        float elapsed = 0f;

        Debug.Log($"<color=orange>[Solar Flare]</color> Fade duration calculated: {fadeDuration:F2}s (from {startExposure:F2} to {targetExposure:F2})");

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;

            RenderSettings.ambientLight = Color.Lerp(startAmbientColor, flareColor, t);

            if (RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_Exposure"))
            {
                float exposure = Mathf.Lerp(startExposure, targetExposure, t);
                RenderSettings.skybox.SetFloat("_Exposure", exposure);
            }

            DynamicGI.UpdateEnvironment();
            yield return null;
        }

        RenderSettings.ambientLight = flareColor;
        
        if (RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_Exposure"))
        {
            RenderSettings.skybox.SetFloat("_Exposure", targetExposure);
            Debug.Log("<color=orange>[Solar Flare]</color> Sky fade complete - Exposure set to 8.0");
        }
        
        DynamicGI.UpdateEnvironment();
    }

    IEnumerator ShakeCamera()
    {
        float elapsed = 0f;
        float exposureChangeRate = 2f;
        float fadeDuration = Mathf.Abs(8f - 0.92f) / exposureChangeRate;
        float shakeDur = Mathf.Min(shakeDuration, fadeDuration);

        while (elapsed < shakeDur)
        {
            float x = Random.Range(-1f, 1f) * shakeIntensity;
            float y = Random.Range(-1f, 1f) * shakeIntensity;

            shakeOffset = new Vector3(x, y, 0);

            elapsed += Time.deltaTime;
            yield return null;
        }

        shakeOffset = Vector3.zero;
    }

    void DisableAllSystems()
    {
        // Only hide the interaction prompts — do NOT disable the components.
        // RadarSystem and DefenseSystem already gate access via IsFlareActiveNetwork.Value,
        // so disabling the components would break their Update() loops on all clients.
        if (radarSystem != null)
        {
            radarSystem.ForceHidePrompt();
            Debug.Log("<color=red>[Solar Flare]</color> Radar locked out");
        }

        if (defenseSystem != null)
        {
            defenseSystem.ForceHidePrompt();
            Debug.Log("<color=red>[Solar Flare]</color> Defense locked out");
        }

        // PowerSystem does not need to be disabled; it independently checks flare state.

        foreach (Light light in bunkerLights)
        {
            if (light != null)
            {
                light.enabled = false;
            }
        }

        Debug.Log("<color=red>[Solar Flare]</color> Bunker lights disabled");
    }

    void CheckPlayerProximityToFixPoint()
    {
        if (playerTransform == null || fixPointTransform == null) return;

        float distance = Vector3.Distance(playerTransform.position, fixPointTransform.position);

        if (distance <= interactionDistance)
        {
            if (!isPlayerNearFixPoint)
            {
                isPlayerNearFixPoint = true;

                if (interactionPromptText != null)
                {
                    interactionPromptText.gameObject.SetActive(true);
                    interactionPromptText.text = $"Hold [E] to Repair Solar Panel ({repairTime:F0}s)";
                }

                Debug.Log("<color=cyan>[Solar Flare]</color> Player near fix point");
            }
        }
        else
        {
            if (isPlayerNearFixPoint)
            {
                isPlayerNearFixPoint = false;
                isRepairing = false;
                repairProgress = 0f;

                if (interactionPromptText != null)
                {
                    interactionPromptText.gameObject.SetActive(false);
                }

                if (repairProgressBar != null)
                {
                    repairProgressBar.gameObject.SetActive(false);
                    repairProgressBar.fillAmount = 0f;
                }

                Debug.Log("<color=yellow>[Solar Flare]</color> Player left fix point");
            }
        }
    }

    void HandleRepair()
    {
        if (!isPlayerNearFixPoint)
        {
            if (IsNetworkSessionActive() && isRepairing)
            {
                if (IsServer)
                {
                    isRepairing = false;
                    IsRepairingNetwork.Value = false;
                    RepairProgressNetwork.Value = 0f;
                }
                else
                {
                    SetRepairingServerRpc(false);
                }
            }

            return;
        }

        if (IsNetworkSessionActive())
        {
            if (Input.GetKey(INTERACTION_KEY))
            {
                if (!isRepairing)
                {
                    if (IsServer)
                    {
                        isRepairing = true;
                        IsRepairingNetwork.Value = true;
                    }
                    else
                    {
                        SetRepairingServerRpc(true);
                    }
                }
            }
            else if (isRepairing)
            {
                if (IsServer)
                {
                    isRepairing = false;
                    IsRepairingNetwork.Value = false;
                }
                else
                {
                    SetRepairingServerRpc(false);
                }
            }

            UpdateRepairUIFromState();
            return;
        }

        if (Input.GetKey(INTERACTION_KEY))
        {
            if (!isRepairing)
            {
                isRepairing = true;
                if (repairProgressBar != null)
                {
                    repairProgressBar.gameObject.SetActive(true);
                }
                Debug.Log("<color=cyan>[Solar Flare]</color> Repair started");
            }

            repairProgress += Time.deltaTime;

            float progressPercent = repairProgress / repairTime;

            if (repairProgressBar != null)
            {
                repairProgressBar.fillAmount = progressPercent;
            }

            if (compassPointer != null)
            {
                compassPointer.UpdateRepairPercentage(progressPercent);
            }

            if (interactionPromptText != null)
            {
                float remaining = Mathf.Max(0, repairTime - repairProgress);
                interactionPromptText.text = $"Repairing... {remaining:F1}s";
            }

            if (repairProgress >= repairTime)
            {
                CompleteRepair();
            }
        }
        else
        {
            if (isRepairing)
            {
                isRepairing = false;
                repairProgress = 0f;

                if (repairProgressBar != null)
                {
                    repairProgressBar.fillAmount = 0f;
                }

                if (compassPointer != null)
                {
                    compassPointer.UpdateRepairPercentage(0f);
                }

                if (interactionPromptText != null)
                {
                    interactionPromptText.text = $"Hold [E] to Repair Solar Panel ({repairTime:F0}s)";
                }

                Debug.Log("<color=yellow>[Solar Flare]</color> Repair cancelled");
            }
        }
    }

    void CompleteRepair()
    {
        if (IsNetworkSessionActive() && !IsServer)
        {
            return;
        }

        Debug.Log("<color=green>[Solar Flare]</color> Repair complete! Systems restored.");

        isFlareActive = false;
        isRepairing = false;
        repairProgress = 0f;
        isPlayerNearFixPoint = false;

        if (IsNetworkSessionActive())
        {
            IsFlareActiveNetwork.Value = false;
            IsRepairingNetwork.Value = false;
            RepairProgressNetwork.Value = 0f;
            if (NetworkGameState.Instance != null)
            {
                NetworkGameState.Instance.SetSolarFlareActive(false);
                NetworkGameState.Instance.SetRadarAvailable(true);
                NetworkGameState.Instance.SetDefenseAvailable(true);
            }
            RepairCompleteClientRpc();
            return;
        }

        if (repairCompleteSound != null)
        {
            audioSource.PlayOneShot(repairCompleteSound);
        }

        if (radarSystem != null)
        {
            radarSystem.enabled = true;
        }

        if (defenseSystem != null)
        {
            defenseSystem.enabled = true;
        }

        if (powerSystem != null)
        {
            powerSystem.enabled = true;
        }

        foreach (Light light in bunkerLights)
        {
            if (light != null)
            {
                light.enabled = true;
            }
        }

        if (warningText != null)
        {
            warningText.gameObject.SetActive(false);
        }

        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(false);
        }

        if (repairProgressBar != null)
        {
            repairProgressBar.gameObject.SetActive(false);
            repairProgressBar.fillAmount = 0f;
        }

        if (compassPointer != null)
        {
            compassPointer.ClearTargetOverride();
            Debug.Log("<color=green>[Solar Flare]</color> Compass returned to default target");
        }
    }

    private void CheckForSolarFlareServer()
    {
        if (!IsServer)
        {
            return;
        }

        CheckForSolarFlare();
    }

    private void HandleRepairServer()
    {
        if (!IsServer || !isFlareActive)
        {
            return;
        }

        if (IsRepairingNetwork.Value)
        {
            repairProgress = RepairProgressNetwork.Value + Time.deltaTime;
            RepairProgressNetwork.Value = repairProgress;

            float progressPercent = repairProgress / repairTime;
            if (compassPointer != null)
            {
                compassPointer.UpdateRepairPercentage(progressPercent);
            }

            if (repairProgress >= repairTime)
            {
                CompleteRepair();
            }
        }
        else if (RepairProgressNetwork.Value != 0f)
        {
            RepairProgressNetwork.Value = 0f;
            repairProgress = 0f;
        }

        UpdateRepairUIFromState();
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetRepairingServerRpc(bool value, ServerRpcParams serverRpcParams = default)
    {
        if (!IsServer || !IsNetworkSessionActive())
        {
            return;
        }

        if (!IsFlareActiveNetwork.Value && !isFlareActive)
        {
            return;
        }

        if (!IsPlayerNearFixPoint(serverRpcParams.Receive.SenderClientId))
        {
            Debug.LogWarning($"<color=red>[Solar Flare]</color> Ignored repair request from client {serverRpcParams.Receive.SenderClientId} because they are not near the fix point.");
            return;
        }

        isRepairing = value;
        IsRepairingNetwork.Value = value;
        if (!value)
        {
            RepairProgressNetwork.Value = 0f;
            repairProgress = 0f;
        }
    }

    [ClientRpc]
    private void TriggerSolarFlareClientRpc()
    {
        if (compassPointer != null && fixPointTransform != null)
        {
            compassPointer.SetTarget(fixPointTransform, "REPAIR NEEDED");
        }

        StartCoroutine(SolarFlareSequence());
    }

    [ClientRpc]
    private void RepairCompleteClientRpc()
    {
        if (repairCompleteSound != null)
        {
            audioSource.PlayOneShot(repairCompleteSound);
        }

        // Do NOT re-enable components — they were never disabled in the multiplayer path.
        // Systems gate access via IsFlareActiveNetwork.Value which is already cleared by the server.

        foreach (Light light in bunkerLights)
        {
            if (light != null)
            {
                light.enabled = true;
            }
        }

        if (warningText != null)
        {
            warningText.gameObject.SetActive(false);
        }

        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(false);
        }

        if (repairProgressBar != null)
        {
            repairProgressBar.gameObject.SetActive(false);
            repairProgressBar.fillAmount = 0f;
        }

        if (compassPointer != null)
        {
            compassPointer.ClearTargetOverride();
            Debug.Log("<color=green>[Solar Flare]</color> Compass returned to default target");
        }

        isFlareActive = false;
        isRepairing = false;
        repairProgress = 0f;
        isPlayerNearFixPoint = false;
    }

    private void UpdateRepairUIFromState()
    {
        float progress = IsNetworkSessionActive() ? RepairProgressNetwork.Value : repairProgress;
        bool repairing = IsNetworkSessionActive() ? IsRepairingNetwork.Value : isRepairing;

        if (repairProgressBar != null)
        {
            if (repairing)
            {
                repairProgressBar.gameObject.SetActive(true);
                repairProgressBar.fillAmount = progress / repairTime;
            }
            else if (!IsNetworkSessionActive() || progress <= 0f)
            {
                repairProgressBar.gameObject.SetActive(false);
                repairProgressBar.fillAmount = 0f;
            }
        }

        if (compassPointer != null)
        {
            compassPointer.UpdateRepairPercentage(progress / repairTime);
        }

        if (interactionPromptText != null && isPlayerNearFixPoint)
        {
            if (repairing)
            {
                float remaining = Mathf.Max(0, repairTime - progress);
                interactionPromptText.text = $"Repairing... {remaining:F1}s";
            }
            else
            {
                interactionPromptText.text = $"Hold [E] to Repair Solar Panel ({repairTime:F0}s)";
            }
        }
    }

    private bool IsPlayerNearFixPoint(ulong clientId)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.ConnectedClients == null)
        {
            return false;
        }

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null || fixPointTransform == null)
        {
            return false;
        }

        float distance = Vector3.Distance(client.PlayerObject.transform.position, fixPointTransform.position);
        return distance <= interactionDistance;
    }

    private void RefreshLocalPlayerReferences()
    {
        if (!IsNetworkSessionActive())
        {
            playerTransform = GameObject.FindGameObjectWithTag("Player")?.transform;
        }
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            playerTransform = NetworkManager.Singleton.LocalClient.PlayerObject.transform;
        }

        if (playerTransform != null)
        {
            playerController = playerTransform.GetComponent<PlayerController>();
        }
    }

    private void SyncLocalStateFromNetwork()
    {
        if (!IsNetworkSessionActive())
        {
            return;
        }

        isFlareActive = IsFlareActiveNetwork.Value;
        isRepairing = IsRepairingNetwork.Value;
        repairProgress = RepairProgressNetwork.Value;

        UpdateRepairUIFromState();
    }

    private void PushNetworkStateFromLocal()
    {
        if (!IsServer)
        {
            return;
        }

        IsFlareActiveNetwork.Value = isFlareActive;
        IsRepairingNetwork.Value = isRepairing;
        RepairProgressNetwork.Value = repairProgress;
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    IEnumerator RestoreSkyColor()
    {
        Debug.Log("<color=cyan>[Solar Flare]</color> Starting sky restoration to dark");
        
        Color startAmbientColor = RenderSettings.ambientLight;

        float startExposure = 8f;
        if (RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_Exposure"))
        {
            startExposure = RenderSettings.skybox.GetFloat("_Exposure");
        }

        if (normalSkyboxMaterial != null)
        {
            RenderSettings.skybox = normalSkyboxMaterial;
            Debug.Log($"<color=cyan>[Solar Flare]</color> Switched to NORMAL skybox material (will fade from {startExposure} to 0.92)");
        }
        else
        {
            Debug.LogWarning("<color=red>[Solar Flare]</color> Normal skybox material is NULL!");
        }

        float targetExposure = 0.92f;
        float exposureChangeRate = 2f;
        float fadeDuration = Mathf.Abs(startExposure - targetExposure) / exposureChangeRate;
        float elapsed = 0f;

        Debug.Log($"<color=cyan>[Solar Flare]</color> Fade duration calculated: {fadeDuration:F2}s (from {startExposure:F2} to {targetExposure:F2})");

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;

            RenderSettings.ambientLight = Color.Lerp(startAmbientColor, normalAmbientColor, t);

            if (RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_Exposure"))
            {
                float exposure = Mathf.Lerp(startExposure, targetExposure, t);
                RenderSettings.skybox.SetFloat("_Exposure", exposure);
            }

            DynamicGI.UpdateEnvironment();
            yield return null;
        }

        RenderSettings.ambientLight = normalAmbientColor;

        if (RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_Exposure"))
        {
            RenderSettings.skybox.SetFloat("_Exposure", targetExposure);
            Debug.Log("<color=cyan>[Solar Flare]</color> Sky restoration complete - Exposure set to 0.92 (dark)");
        }
        
        DynamicGI.UpdateEnvironment();
    }

    void OnDrawGizmosSelected()
    {
        if (fixPointTransform != null)
        {
            Gizmos.color = isFlareActive ? Color.red : Color.green;
            Gizmos.DrawWireSphere(fixPointTransform.position, interactionDistance);
            Gizmos.DrawLine(fixPointTransform.position, fixPointTransform.position + Vector3.up * 3f);
        }
    }
}
