using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Triggers when the night ends. Teleports the player inside the bunker,
/// kills all lights, plays an audio clip, reveals eyes after 5 seconds,
/// then loads the next scene. Player can look freely but cannot move.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class BunkerEndingSequence : NetworkBehaviour
{
    [Header("Scene Settings")]
    [Tooltip("Name of the scene to load after the ending plays out")]
    public string nextSceneName = "WinScene";

    [Header("Teleport Settings")]
    [Tooltip("Where to teleport the player inside the bunker for the ending")]
    public Transform bunkerEndingPosition;

    [Header("Lighting")]
    [Tooltip("Root GameObject that contains all lights inside the bunker (e.g. 'lights REd', 'Lights White'). All Light components in children will be disabled.")]
    public GameObject[] lightRoots;

    [Header("Eyes / Jump Object")]
    [Tooltip("GameObject (e.g. a sprite or mesh with eyes) that is disabled at start and enabled after eyesRevealDelay")]
    public GameObject eyesObject;

    [Tooltip("Seconds after entering the bunker before the eyes appear")]
    public float eyesRevealDelay = 5f;

    [Header("Audio")]
    [Tooltip("Audio clip that plays when the lights go out")]
    public AudioClip endingAmbientAudio;

    [Tooltip("Volume of the ending audio")]
    [Range(0f, 1f)]
    public float endingAudioVolume = 1f;

    [Header("Timing")]
    [Tooltip("Seconds to wait after eyes appear before loading the next scene")]
    public float delayAfterEyes = 3f;

    [Tooltip("Fade-to-black duration before loading next scene")]
    public float fadeDuration = 1.5f;

    [Header("Ending Cards")]
    [Tooltip("Seconds to wait on a black screen before the first text card appears")]
    public float preTextDelay = 4f;

    [Tooltip("How long each text card takes to fade in")]
    public float textFadeInDuration = 1.5f;

    [Tooltip("How long each text card is held fully visible before the next one starts")]
    public float textHoldDuration = 2.5f;

    [Tooltip("How long each text card takes to fade out before the next one fades in")]
    public float textFadeOutDuration = 1f;

    [Tooltip("Text lines shown one after another on the black end screen")]
    public string[] endingLines = new string[]
    {
        "I survived till the day.",
        "The radiation is too strong to go out in the day.",
        "They will be back again tonight."
    };

    // ── Internals ──────────────────────────────────────────────────────────
    private bool sequenceStarted = false;
    private bool sequenceCoroutineStarted = false;
    private AudioSource audioSource;
    private Canvas overlayCanvas;
    private Image fadeImage;

    public NetworkVariable<bool> SequenceStartedNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsActiveNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>True once the ending sequence has been triggered. Used by other systems (e.g. IgnoreMonster) to suppress conflicting behaviour.</summary>
    public static bool IsActive { get; private set; }

    void Start()
    {
        IsActive = false;
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.spatialBlend = 0f;
        audioSource.playOnAwake = false;

        if (eyesObject != null)
        {
            eyesObject.SetActive(false);
        }

        BuildFadeCanvas();
    }

    void OnDestroy()
    {
        if (IsActive)
        {
            IsActive = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        SequenceStartedNetwork.OnValueChanged += OnSequenceStartedNetworkChanged;

        if (IsServer)
        {
            SequenceStartedNetwork.Value = sequenceStarted;
            IsActiveNetwork.Value = IsActive;
        }

        if (SequenceStartedNetwork.Value)
        {
            StartEndingLocally();
        }
    }

    public override void OnNetworkDespawn()
    {
        SequenceStartedNetwork.OnValueChanged -= OnSequenceStartedNetworkChanged;
    }

    void Update()
    {
        // Ending is triggered externally by NightTimeManager via TriggerEnding().
    }

    /// <summary>Kick off the ending from external code if needed.</summary>
    public void TriggerEnding()
    {
        if (IsNetworkSessionActive() && IsServer)
        {
            if (sequenceStarted) return;
            sequenceStarted = true;
            IsActive = true;
            SequenceStartedNetwork.Value = true;
            IsActiveNetwork.Value = true;
            if (NetworkGameState.Instance != null)
            {
                NetworkGameState.Instance.SetRestartInProgress(true);
            }
            TriggerEndingClientRpc();
            StartEndingLocally();
            return;
        }

        StartEndingLocally();
    }

    IEnumerator RunEndingSequence()
    {
        // ── 1. Freeze player movement, allow look ──────────────────────────
        PlayerController pc = ResolveLocalPlayerController();
        if (pc != null)
        {
            pc.enabled = false;
        }

        // Re-enable only the camera look so the player can look around.
        CameraLookOnly lookOnly = null;
        if (pc != null)
        {
            lookOnly = pc.gameObject.GetComponent<CameraLookOnly>();
            if (lookOnly == null)
            {
                lookOnly = pc.gameObject.AddComponent<CameraLookOnly>();
            }
            lookOnly.playerCamera = pc.playerCamera;
            lookOnly.mouseSensitivity = pc.mouseSensitivity;
            lookOnly.verticalLookLimit = pc.verticalLookLimit;
            lookOnly.enabled = true;
        }

        // ── 2. Teleport player into the bunker ─────────────────────────────
        if (bunkerEndingPosition != null && pc != null)
        {
            TeleportLocalPlayerToEndingSpot(pc);
            yield return null;
        }

        yield return new WaitForSeconds(0.1f);

        // ── 3. Kill all lights ─────────────────────────────────────────────
        TurnOffAllLights();

        // ── 4. Play ambient audio ──────────────────────────────────────────
        if (endingAmbientAudio != null && audioSource != null)
        {
            audioSource.clip = endingAmbientAudio;
            audioSource.volume = endingAudioVolume;
            audioSource.loop = false;
            audioSource.Play();
        }

        // ── 5. Wait then reveal eyes ───────────────────────────────────────
        yield return new WaitForSeconds(eyesRevealDelay);

        if (eyesObject != null)
        {
            eyesObject.SetActive(true);
            Debug.Log("<color=red>[BunkerEnding]</color> Eyes revealed.");
        }

        // ── 6. Brief pause after eyes appear ──────────────────────────────
        yield return new WaitForSeconds(delayAfterEyes);

        // ── 7. Fade to black then show ending cards ────────────────────────
        if (lookOnly != null)
        {
            lookOnly.enabled = false;
        }

        yield return StartCoroutine(FadeToBlack());

        yield return new WaitForSeconds(preTextDelay);

        yield return StartCoroutine(RunEndingCards());

        if (!string.IsNullOrEmpty(nextSceneName))
        {
            Time.timeScale = 1f;

            if (IsNetworkSessionActive())
            {
                if (IsServer)
                {
                    NetworkManager.Singleton.SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);
                }
            }
            else
            {
                SceneManager.LoadScene(nextSceneName);
            }
        }
    }

    void TurnOffAllLights()
    {
        if (lightRoots == null || lightRoots.Length == 0)
        {
            // Fall back: disable every Light in the scene.
            Light[] all = FindObjectsOfType<Light>();
            foreach (Light l in all)
            {
                l.enabled = false;
            }
            Debug.Log($"<color=yellow>[BunkerEnding]</color> Turned off {all.Length} lights (scene-wide fallback).");
            return;
        }

        int count = 0;
        foreach (GameObject root in lightRoots)
        {
            if (root == null) continue;
            Light[] lights = root.GetComponentsInChildren<Light>(includeInactive: true);
            foreach (Light l in lights)
            {
                l.enabled = false;
                count++;
            }
        }
        Debug.Log($"<color=yellow>[BunkerEnding]</color> Turned off {count} lights.");
    }

    void BuildFadeCanvas()
    {
        GameObject canvasGO = new GameObject("BunkerEndingFadeCanvas");
        overlayCanvas = canvasGO.AddComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = 9999;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        canvasGO.AddComponent<GraphicRaycaster>();

        GameObject imgGO = new GameObject("FadeImage");
        imgGO.transform.SetParent(canvasGO.transform, false);
        fadeImage = imgGO.AddComponent<Image>();
        fadeImage.color = new Color(0f, 0f, 0f, 0f);

        RectTransform rt = imgGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    IEnumerator FadeToBlack()
    {
        if (fadeImage == null) yield break;

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Clamp01(elapsed / fadeDuration);
            fadeImage.color = new Color(0f, 0f, 0f, alpha);
            yield return null;
        }
        fadeImage.color = Color.black;
    }

    IEnumerator RunEndingCards()
    {
        if (endingLines == null || endingLines.Length == 0) yield break;

        // Build a TMP label on top of the black fade canvas.
        GameObject textGO = new GameObject("EndingCardText");
        textGO.transform.SetParent(overlayCanvas.transform, false);

        TextMeshProUGUI label = textGO.AddComponent<TextMeshProUGUI>();
        label.fontSize = 52;
        label.fontStyle = FontStyles.Italic;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1f, 1f, 1f, 0f);

        RectTransform rt = textGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.1f, 0.4f);
        rt.anchorMax = new Vector2(0.9f, 0.6f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        foreach (string line in endingLines)
        {
            label.text = line;
            yield return StartCoroutine(FadeTextAlpha(label, 0f, 1f, textFadeInDuration));
            yield return new WaitForSeconds(textHoldDuration);
            yield return StartCoroutine(FadeTextAlpha(label, 1f, 0f, textFadeOutDuration));
        }

        Destroy(textGO);
    }

    IEnumerator FadeTextAlpha(TextMeshProUGUI label, float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            Color c = label.color;
            c.a = Mathf.Lerp(from, to, t);
            label.color = c;
            yield return null;
        }
        Color final = label.color;
        final.a = to;
        label.color = final;
    }

    void OnDrawGizmosSelected()
    {
        if (bunkerEndingPosition != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(bunkerEndingPosition.position, 0.5f);
            Gizmos.DrawLine(transform.position, bunkerEndingPosition.position);
        }
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    private void StartEndingLocally()
    {
        if (sequenceCoroutineStarted) return;

        sequenceStarted = true;
        sequenceCoroutineStarted = true;
        IsActive = true;
        StartCoroutine(RunEndingSequence());
    }

    private void OnSequenceStartedNetworkChanged(bool previousValue, bool currentValue)
    {
        if (currentValue)
        {
            StartEndingLocally();
        }
    }

    [ClientRpc]
    private void TriggerEndingClientRpc()
    {
        StartEndingLocally();
    }

    private PlayerController ResolveLocalPlayerController()
    {
        if (IsNetworkSessionActive() &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.LocalClient != null &&
            NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            return NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
        }

        return FindObjectOfType<PlayerController>();
    }

    private void TeleportLocalPlayerToEndingSpot(PlayerController pc)
    {
        CharacterController cc = pc.GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.enabled = false;
        }

        pc.transform.SetPositionAndRotation(bunkerEndingPosition.position, bunkerEndingPosition.rotation);

        if (pc.playerCamera != null)
        {
            pc.playerCamera.localPosition = pc.cameraStartPos;
            pc.playerCamera.localRotation = Quaternion.identity;
        }

        if (cc != null)
        {
            cc.enabled = true;
        }
    }
}
