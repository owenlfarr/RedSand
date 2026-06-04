using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class RushMonsterEvent : NetworkBehaviour
{
    public static RushMonsterEvent Instance { get; private set; }

    [Header("Event Probability")]
    [Tooltip("Chance of event triggering (0.1 = 1 in 10 chance)")]
    [Range(0f, 1f)]
    public float eventChance = 0.1f;

    [Tooltip("Check interval in seconds")]
    public float checkInterval = 30f;

    [Header("Event Timing")]
    [Tooltip("Duration of light flickering in seconds")]
    public float flickerDuration = 3f;

    [Tooltip("Time player has to hide in locker")]
    public float hideTimeLimit = 5f;

    [Tooltip("Duration lights stay off when player is safe")]
    public float blackoutDuration = 5f;

    [Header("Flicker Settings")]
    [Tooltip("Minimum time between flickers")]
    public float flickerMinDelay = 0.05f;

    [Tooltip("Maximum time between flickers")]
    public float flickerMaxDelay = 0.3f;

    [Header("References")]
    [Tooltip("Tag for bunker zone")]
    public string bunkerZoneTag = "Bunker";

    [Tooltip("White lights to flicker")]
    public Light[] whiteLights;

    [Tooltip("Red emergency lights")]
    public Light[] redLights;

    [Header("Audio")]
    public AudioClip flickerSound;
    public AudioClip rushApproachingSound;
    public AudioClip rushPassSound;
    public AudioClip deathSound;

    [Header("Jumpscare")]
    [Tooltip("Image texture to show when Rush kills player (drag any image file)")]
    public Texture2D rushJumpscareImage;

    [Tooltip("Message to show on death")]
    public string rushDeathMessage = "YOU DIDN'T HIDE";

    [Tooltip("Black screen duration before image shows")]
    public float blackScreenDuration = 3f;

    [Tooltip("How long to show jumpscare image")]
    public float imageDisplayDuration = 4f;

    public NetworkVariable<bool> IsEventActiveNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsRushPassingNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private bool isEventActive = false;
    private bool isRushPassing = false;
    private Transform playerTransform;
    private OxygenSystem oxygenSystem;
    private AudioSource audioSource;
    private float nextCheckTime;
    private bool[] originalWhiteLightStates;
    private bool[] originalRedLightStates;
    private RushMonsterUI rushUI;
    private Canvas gameCanvas;
    private GameObject jumpscarePanel;
    private Image jumpscareImageUI;
    private TextMeshProUGUI jumpscareTextUI;

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

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
    }

    void Start()
    {
        RefreshLocalPlayerReference();
        rushUI = FindObjectOfType<RushMonsterUI>();

        DiscoverLights();

        StoreOriginalLightStates();
        nextCheckTime = Time.time + checkInterval;

        Debug.Log("<color=purple>[Rush Monster]</color> System initialized");
    }

    /// <summary>Finds the scene lights by name if the arrays are empty.</summary>
    void DiscoverLights()
    {
        if (whiteLights == null || whiteLights.Length == 0)
        {
            GameObject whiteLightParent = GameObject.Find("Lights White");
            if (whiteLightParent != null)
            {
                whiteLights = whiteLightParent.GetComponentsInChildren<Light>();
            }
        }

        if (redLights == null || redLights.Length == 0)
        {
            GameObject redLightParent = GameObject.Find("lights REd");
            if (redLightParent != null)
            {
                redLights = redLightParent.GetComponentsInChildren<Light>();
            }
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Re-run light discovery so clients have valid references after network spawn.
        DiscoverLights();
        StoreOriginalLightStates();

        if (IsServer)
        {
            IsEventActiveNetwork.Value = isEventActive;
            IsRushPassingNetwork.Value = isRushPassing;
        }
    }

    void Update()
    {
        if (playerTransform == null || oxygenSystem == null)
        {
            RefreshLocalPlayerReference();
        }

        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                UpdateServerEventLoop();
            }
            else
            {
                isEventActive = IsEventActiveNetwork.Value;
                isRushPassing = IsRushPassingNetwork.Value;
            }

            return;
        }

        UpdateOfflineEventLoop();
    }

    void UpdateServerEventLoop()
    {
        if (isEventActive)
        {
            return;
        }

        if (Time.time >= nextCheckTime)
        {
            nextCheckTime = Time.time + checkInterval;
            TryTriggerEventServer();
        }
    }

    void UpdateOfflineEventLoop()
    {
        if (isEventActive) return;

        CheckPlayerInBunkerOffline();

        if (Time.time >= nextCheckTime)
        {
            nextCheckTime = Time.time + checkInterval;
            TryTriggerEventOffline();
        }
    }

    void StoreOriginalLightStates()
    {
        if (whiteLights != null)
        {
            originalWhiteLightStates = new bool[whiteLights.Length];
            for (int i = 0; i < whiteLights.Length; i++)
            {
                originalWhiteLightStates[i] = whiteLights[i] != null && whiteLights[i].enabled;
            }
        }

        if (redLights != null)
        {
            originalRedLightStates = new bool[redLights.Length];
            for (int i = 0; i < redLights.Length; i++)
            {
                originalRedLightStates[i] = redLights[i] != null && redLights[i].enabled;
            }
        }
    }

    void CheckPlayerInBunkerOffline()
    {
        if (playerTransform == null)
        {
            return;
        }

        bool inBunker = false;
        Collider[] hits = Physics.OverlapSphere(playerTransform.position, 1f);
        foreach (var hit in hits)
        {
            if (hit != null && hit.CompareTag(bunkerZoneTag))
            {
                inBunker = true;
                break;
            }
        }

        if (oxygenSystem != null && oxygenSystem.IsInBunker() != inBunker)
        {
            Debug.Log($"[Rush Monster] Offline bunker check mismatch. Oxygen says {oxygenSystem.IsInBunker()}, overlap says {inBunker}");
        }
    }

    void TryTriggerEventOffline()
    {
        if (PowerSystem.Instance == null || PowerSystem.Instance.IsPowerOut)
        {
            return;
        }

        if (!IsLocalPlayerInBunker())
        {
            return;
        }

        if (Random.value <= eventChance)
        {
            StartCoroutine(ExecuteRushEventOffline());
        }
    }

    void TryTriggerEventServer()
    {
        if (PowerSystem.Instance == null || PowerSystem.Instance.IsPowerOut)
        {
            return;
        }

        if (!AnyPlayerInBunkerServer())
        {
            return;
        }

        if (Random.value <= eventChance)
        {
            StartCoroutine(ExecuteRushEventServer());
        }
    }

    bool AnyPlayerInBunkerServer()
    {
        if (NetworkManager.Singleton == null)
        {
            return false;
        }

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
            {
                continue;
            }

            OxygenSystem oxygen = client.PlayerObject.GetComponent<OxygenSystem>();
            if (oxygen != null && oxygen.IsInBunker())
            {
                return true;
            }
        }

        return false;
    }

    IEnumerator ExecuteRushEventOffline()
    {
        isEventActive = true;
        Debug.Log("<color=red>[Rush Monster]</color> EVENT TRIGGERED!");

        if (IsLocalPlayerInBunker() && rushApproachingSound != null)
        {
            audioSource.PlayOneShot(rushApproachingSound);
        }

        if (IsLocalPlayerInBunker() && rushUI != null)
        {
            rushUI.ShowWarning("something is coming...");
        }

        yield return StartCoroutine(FlickerLightsLocal());

        bool playerHid = IsAnyLockerHiddenForLocalPlayer();
        bool playerInBunker = IsLocalPlayerInBunker();
        if (!playerInBunker)
        {
            if (rushUI != null)
            {
                rushUI.HideWarning();
            }

            RestoreLightsLocal();
            FinishOfflineEvent();
            yield break;
        }

        if (playerHid)
        {
            if (rushPassSound != null)
            {
                audioSource.PlayOneShot(rushPassSound);
            }

            if (rushUI != null)
            {
                rushUI.ShowWarning("IT'S PASSING...");
            }

            SetAllLightsLocal(false);
            isRushPassing = true;
            yield return new WaitForSeconds(blackoutDuration);
            isRushPassing = false;

            if (rushUI != null)
            {
                rushUI.HideWarning();
            }

            RestoreLightsLocal();
        }
        else
        {
            KillPlayerOffline();
        }

        FinishOfflineEvent();
    }

    IEnumerator ExecuteRushEventServer()
    {
        isEventActive = true;
        IsEventActiveNetwork.Value = true;

        RushWarningClientRpc("something is coming...");

        FlickerClientRpc();

        yield return new WaitForSeconds(hideTimeLimit);

        BlackoutClientRpc();

        List<ulong> playersToKill = new List<ulong>();
        if (NetworkManager.Singleton != null)
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null)
                {
                    continue;
                }

                bool inBunker = IsClientInBunkerServer(client.ClientId);
                bool hidden = IsClientHiddenInAnyLocker(client.ClientId);
                if (inBunker && !hidden)
                {
                    playersToKill.Add(client.ClientId);
                }
            }
        }

        if (playersToKill.Count > 0)
        {
            foreach (ulong clientId in playersToKill)
            {
                MarkClientDeadServer(clientId);
                KillPlayerClientRpc(clientId);
            }
        }
        else
        {
            isRushPassing = true;
            IsRushPassingNetwork.Value = true;
            RushPassClientRpc();
        }

        yield return new WaitForSeconds(blackoutDuration);
        isRushPassing = false;
        IsRushPassingNetwork.Value = false;
        RestoreLightsClientRpc();

        isEventActive = false;
        IsEventActiveNetwork.Value = false;
        nextCheckTime = Time.time + checkInterval;
    }

    [ClientRpc]
    private void RushWarningClientRpc(string message)
    {
        if (!IsLocalPlayerInBunker())
        {
            return;
        }

        if (rushApproachingSound != null)
        {
            audioSource.PlayOneShot(rushApproachingSound);
        }

        if (rushUI != null)
        {
            rushUI.ShowWarning(message);
        }
    }

    [ClientRpc]
    private void FlickerClientRpc()
    {
        if (!IsLocalPlayerInBunker())
        {
            return;
        }

        StartCoroutine(FlickerLightsLocal());
    }

    [ClientRpc]
    private void RushPassClientRpc()
    {
        if (!IsLocalPlayerInBunker())
        {
            return;
        }

        if (rushPassSound != null)
        {
            audioSource.PlayOneShot(rushPassSound);
        }

        if (rushUI != null)
        {
            rushUI.ShowWarning("IT'S PASSING...");
        }
    }

    [ClientRpc]
    private void BlackoutClientRpc()
    {
        if (!IsLocalPlayerInBunker())
        {
            return;
        }

        SetAllLightsLocal(false);
    }

    [ClientRpc]
    private void RestoreLightsClientRpc()
    {
        if (rushUI != null)
        {
            rushUI.HideWarning();
        }

        RestoreLightsLocal();
    }

    [ClientRpc]
    private void KillPlayerClientRpc(ulong targetClientId, ClientRpcParams clientRpcParams = default)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId != targetClientId)
        {
            return;
        }

        KillPlayerLocal(false);
    }

    public bool IsRushPassing()
    {
        return IsNetworkSessionActive() ? IsRushPassingNetwork.Value : isRushPassing;
    }

    public void KillPlayerForExiting()
    {
        if (IsNetworkSessionActive() && IsServer)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
            {
                KillPlayerForExiting(NetworkManager.Singleton.LocalClientId);
            }
            return;
        }

        KillPlayerLocal(true);
    }

    public void KillPlayerForExiting(ulong clientId)
    {
        if (!IsServer)
        {
            return;
        }

        if (!IsClientInBunkerServer(clientId))
        {
            return;
        }

        KillPlayerClientRpc(clientId);
    }

    IEnumerator FlickerLightsLocal()
    {
        if (!IsLocalPlayerInBunker())
        {
            yield break;
        }

        int flickerCount = 0;
        int maxFlickers = 10;

        while (flickerCount < maxFlickers)
        {
            if (!IsLocalPlayerInBunker())
            {
                RestoreLightsLocal();
                yield break;
            }

            foreach (Light light in whiteLights)
            {
                if (light != null)
                {
                    light.enabled = false;
                }
            }

            if (flickerSound != null && Random.value > 0.6f)
            {
                audioSource.PlayOneShot(flickerSound, 0.3f);
            }

            yield return new WaitForSeconds(Random.Range(0.05f, 0.15f));

            foreach (Light light in whiteLights)
            {
                if (light != null)
                {
                    light.enabled = PowerSystem.Instance == null || !PowerSystem.Instance.IsPowerOut;
                }
            }

            yield return new WaitForSeconds(Random.Range(0.05f, 0.2f));
            flickerCount++;
        }

        foreach (Light light in whiteLights)
        {
            if (light != null)
            {
                light.enabled = false;
            }
        }

        yield return new WaitForSeconds(0.5f);

        foreach (Light light in whiteLights)
        {
            if (light != null)
            {
                light.enabled = PowerSystem.Instance == null || !PowerSystem.Instance.IsPowerOut;
            }
        }
    }

    void SetAllLightsLocal(bool state)
    {
        if (whiteLights != null)
        {
            foreach (Light light in whiteLights)
            {
                if (light != null)
                {
                    light.enabled = state;
                }
            }
        }

        if (redLights != null)
        {
            foreach (Light light in redLights)
            {
                if (light != null)
                {
                    light.enabled = state;
                }
            }
        }
    }

    void RestoreLightsLocal()
    {
        if (PowerSystem.Instance != null && PowerSystem.Instance.IsPowerOut)
        {
            PowerSystem.Instance.ApplyCurrentPowerState();
            return;
        }

        if (whiteLights != null && originalWhiteLightStates != null)
        {
            for (int i = 0; i < whiteLights.Length && i < originalWhiteLightStates.Length; i++)
            {
                if (whiteLights[i] != null)
                {
                    whiteLights[i].enabled = originalWhiteLightStates[i];
                }
            }
        }

        if (redLights != null && originalRedLightStates != null)
        {
            for (int i = 0; i < redLights.Length && i < originalRedLightStates.Length; i++)
            {
                if (redLights[i] != null)
                {
                    redLights[i].enabled = originalRedLightStates[i];
                }
            }
        }
    }

    void KillPlayerOffline()
    {
        KillPlayerLocal(true);
    }

    void KillPlayerLocal(bool restartScene)
    {
        if (!IsLocalPlayerInBunker())
        {
            return;
        }

        if (IsAnyLockerHiddenForLocalPlayer())
        {
            return;
        }

        if (oxygenSystem != null)
        {
            if (JumpscareSystem.Instance != null && rushJumpscareImage != null)
            {
                JumpscareSystem.Instance.TriggerJumpscareWithTexture(rushJumpscareImage, rushDeathMessage, deathSound);
            }
            else
            {
                StartCoroutine(ShowBuiltInJumpscare(restartScene));
            }

            oxygenSystem.MarkDeadFromMonster();
        }

        Debug.Log("<color=red>[Rush Monster]</color> Player has been killed by the entity");
    }

    IEnumerator ShowBuiltInJumpscare(bool restartScene)
    {
        CreateJumpscareUI();

        if (jumpscarePanel != null)
        {
            jumpscarePanel.SetActive(true);
        }

        if (jumpscareImageUI != null)
        {
            jumpscareImageUI.color = Color.black;
        }

        if (deathSound != null)
        {
            audioSource.PlayOneShot(deathSound);
        }

        yield return new WaitForSecondsRealtime(blackScreenDuration);

        if (jumpscareImageUI != null && rushJumpscareImage != null)
        {
            Rect rect = new Rect(0, 0, rushJumpscareImage.width, rushJumpscareImage.height);
            Sprite newSprite = Sprite.Create(rushJumpscareImage, rect, new Vector2(0.5f, 0.5f));
            jumpscareImageUI.sprite = newSprite;
            jumpscareImageUI.color = Color.white;
        }

        yield return new WaitForSecondsRealtime(imageDisplayDuration);

        if (jumpscareTextUI != null && !string.IsNullOrEmpty(rushDeathMessage))
        {
            jumpscareTextUI.text = rushDeathMessage;
            jumpscareTextUI.gameObject.SetActive(true);
        }

        yield return new WaitForSecondsRealtime(2f);

        if (restartScene)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    void CreateJumpscareUI()
    {
        if (jumpscarePanel != null) return;

        if (gameCanvas == null)
        {
            gameCanvas = GameObject.Find("GameUICanvas")?.GetComponent<Canvas>();
            if (gameCanvas == null)
            {
                gameCanvas = FindObjectOfType<Canvas>();
            }
        }

        if (gameCanvas == null)
        {
            Debug.LogError("<color=red>[Rush Monster]</color> No Canvas found for jumpscare!");
            return;
        }

        jumpscarePanel = new GameObject("RushJumpscarePanel");
        jumpscarePanel.transform.SetParent(gameCanvas.transform, false);
        RectTransform panelRect = jumpscarePanel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        GameObject imageObj = new GameObject("JumpscareImage");
        imageObj.transform.SetParent(jumpscarePanel.transform, false);
        RectTransform imageRect = imageObj.AddComponent<RectTransform>();
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = Vector2.zero;
        imageRect.offsetMax = Vector2.zero;
        jumpscareImageUI = imageObj.AddComponent<Image>();
        jumpscareImageUI.color = Color.black;

        GameObject textObj = new GameObject("JumpscareText");
        textObj.transform.SetParent(jumpscarePanel.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.15f);
        textRect.anchorMax = new Vector2(0.5f, 0.15f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = new Vector2(1200, 80);
        jumpscareTextUI = textObj.AddComponent<TextMeshProUGUI>();
        jumpscareTextUI.fontSize = 48;
        jumpscareTextUI.fontStyle = FontStyles.Bold | FontStyles.Italic;
        jumpscareTextUI.alignment = TextAlignmentOptions.Center;
        jumpscareTextUI.color = new Color(0.6f, 0.05f, 0.05f, 1f);
        jumpscareTextUI.characterSpacing = 15f;
        jumpscareTextUI.gameObject.SetActive(false);

        jumpscarePanel.SetActive(false);
    }

    void RefreshLocalPlayerReference()
    {
        if (IsNetworkSessionActive() && NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            playerTransform = NetworkManager.Singleton.LocalClient.PlayerObject.transform;
            oxygenSystem = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<OxygenSystem>();
            return;
        }

        if (playerTransform != null)
        {
            return;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
            oxygenSystem = player.GetComponent<OxygenSystem>();
        }
    }

    public bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    private bool IsAnyLockerHiddenForLocalPlayer()
    {
        LockerInteractionNew[] lockers = FindObjectsOfType<LockerInteractionNew>(true);
        foreach (var locker in lockers)
        {
            if (locker != null && locker.IsPlayerHidden())
            {
                return true;
            }
        }

        return false;
    }

    private bool IsClientHiddenInAnyLocker(ulong clientId)
    {
        LockerInteractionNew[] lockers = FindObjectsOfType<LockerInteractionNew>(true);
        foreach (var locker in lockers)
        {
            if (locker != null && locker.IsClientHidden(clientId))
            {
                return true;
            }
        }

        return false;
    }

    private void MarkClientDeadServer(ulong clientId)
    {
        if (!IsServer || NetworkManager.Singleton == null || !NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
        {
            return;
        }

        OxygenSystem targetOxygen = client.PlayerObject.GetComponent<OxygenSystem>();
        if (targetOxygen != null)
        {
            targetOxygen.MarkDeadFromMonster();
        }
    }

    private bool IsLocalPlayerInBunker()
    {
        RefreshLocalPlayerReference();

        if (oxygenSystem != null)
        {
            return oxygenSystem.IsInBunker();
        }

        if (playerTransform == null)
        {
            return false;
        }

        Collider[] hits = Physics.OverlapSphere(playerTransform.position, 1f);
        foreach (Collider hit in hits)
        {
            if (hit != null && hit.CompareTag(bunkerZoneTag))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsClientInBunkerServer(ulong clientId)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
        {
            return false;
        }

        OxygenSystem oxygen = client.PlayerObject.GetComponent<OxygenSystem>();
        if (oxygen != null)
        {
            return oxygen.IsInBunker();
        }

        Collider[] hits = Physics.OverlapSphere(client.PlayerObject.transform.position, 1f);
        foreach (Collider hit in hits)
        {
            if (hit != null && hit.CompareTag(bunkerZoneTag))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsEventActive()
    {
        return IsNetworkSessionActive() ? IsEventActiveNetwork.Value : isEventActive;
    }

    private void FinishOfflineEvent()
    {
        isEventActive = false;
        nextCheckTime = Time.time + checkInterval;
    }
}
