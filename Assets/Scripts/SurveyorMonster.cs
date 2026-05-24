using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;
using Unity.Netcode;
using System.Collections.Generic;

[RequireComponent(typeof(NetworkObject))]
public class SurveyorMonster : NetworkBehaviour
{
    public static SurveyorMonster Instance { get; private set; }

    [Header("Behavior Settings")]
    [Tooltip("The objective it's approaching (usually the bunker/cube)")]
    public Transform objectiveTransform;

    [Tooltip("Starting distance from objective")]
    public float startingDistance = 150f;

    [Tooltip("Distance at which Surveyor kills the player")]
    public float killDistance = 10f;

    [Tooltip("Safe distance - Surveyor won't get closer when radar is checked")]
    public float safeDistance = 40f;

    [Header("Movement Speed")]
    [Tooltip("Speed when player is NOT checking radar (fast advance)")]
    public float aggressiveSpeed = 15f;

    [Tooltip("Speed when player IS checking radar (slow retreat)")]
    public float retreatSpeed = 3f;

    [Tooltip("How long after radar scan before Surveyor advances again")]
    public float radarGracePeriod = 30f;

    [Tooltip("Time before Surveyor starts advancing (starts dormant)")]
    public float activationDelay = 60f;

    [Tooltip("How long player has been NOT checking radar before aggressive mode")]
    public float neglectThreshold = 60f;

    [Header("Audio")]
    public AudioClip movementSound;
    public AudioClip proximitySound;
    public AudioClip killSound;

    [Header("Jumpscare")]
    [Tooltip("Image to show on death (can be static/watching eyes)")]
    public Texture2D surveyorJumpscareImage;

    [Tooltip("Death message")]
    public string deathMessage = "IT WAS WATCHING";

    public NetworkVariable<float> CurrentDistanceNetwork = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsRetreatingNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<float> TimeSinceLastRadarCheckNetwork = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private float currentDistance;
    private float timeSinceLastRadarCheck = 0f;
    private bool isRetreating = false;
    private float retreatTimer = 0f;
    private AudioSource audioSource;
    private AudioSource movementAudioSource;
    private bool hasKilled = false;
    private bool isCurrentlyMoving = false;
    private bool wasPowerOut = false;
    private Transform playerTransform;

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
        audioSource.spatialBlend = 0f;
        audioSource.playOnAwake = false;

        movementAudioSource = gameObject.AddComponent<AudioSource>();
        movementAudioSource.spatialBlend = 0f;
        movementAudioSource.playOnAwake = false;
        movementAudioSource.loop = true;
    }

    void Start()
    {
        if (objectiveTransform == null)
        {
            GameObject cube = GameObject.Find("Cube");
            if (cube != null)
            {
                objectiveTransform = cube.transform;
            }
        }

        if (objectiveTransform != null)
        {
            currentDistance = startingDistance;
            Vector3 directionAway = (transform.position - objectiveTransform.position).normalized;
            if (directionAway == Vector3.zero)
            {
                directionAway = Random.onUnitSphere;
                directionAway.y = 0;
            }

            transform.position = objectiveTransform.position + directionAway * currentDistance;
            Debug.Log($"<color=purple>[Surveyor]</color> Initialized at {currentDistance}m from objective. It is watching...");
        }
        else
        {
            Debug.LogError("<color=red>[Surveyor]</color> No objective found!");
        }

        gameObject.tag = "Enemy";
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            CurrentDistanceNetwork.Value = currentDistance;
            IsRetreatingNetwork.Value = isRetreating;
            TimeSinceLastRadarCheckNetwork.Value = timeSinceLastRadarCheck;
        }
        else
        {
            ApplyNetworkStateToClient();
        }
    }

    void Update()
    {
        if (objectiveTransform == null || hasKilled)
        {
            return;
        }

        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                UpdateServerState();
                PushNetworkStateFromServer();
            }
            else
            {
                ApplyNetworkStateToClient();
                UpdateMovementSound();
                CheckProximitySound();
            }

            return;
        }

        UpdateOfflineState();
    }

    void UpdateServerState()
    {
        bool isPowerOut = PowerSystem.Instance != null && PowerSystem.Instance.IsPowerOut;

        if (isPowerOut)
        {
            if (!wasPowerOut)
            {
                Debug.Log("<color=yellow>[Surveyor]</color> Power is out - it cannot advance without power...");
                wasPowerOut = true;
            }
        }
        else
        {
            if (wasPowerOut)
            {
                timeSinceLastRadarCheck = 0f;
                Debug.Log("<color=green>[Surveyor]</color> Power restored - activation timer reset!");
                wasPowerOut = false;
            }

            timeSinceLastRadarCheck += Time.deltaTime;
        }

        if (isRetreating)
        {
            retreatTimer -= Time.deltaTime;
            if (retreatTimer <= 0f)
            {
                isRetreating = false;
                Debug.Log("<color=yellow>[Surveyor]</color> Grace period over. Resuming watch...");
            }
        }

        UpdatePosition();
        CheckProximitySound();

        if (currentDistance <= killDistance)
        {
            KillPlayerServer();
        }
    }

    void UpdateOfflineState()
    {
        bool isPowerOut = PowerSystem.Instance != null && PowerSystem.Instance.IsPowerOut;

        if (isPowerOut)
        {
            if (!wasPowerOut)
            {
                Debug.Log("<color=yellow>[Surveyor]</color> Power is out - it cannot advance without power...");
                wasPowerOut = true;
            }
        }
        else
        {
            if (wasPowerOut)
            {
                timeSinceLastRadarCheck = 0f;
                Debug.Log("<color=green>[Surveyor]</color> Power restored - activation timer reset!");
                wasPowerOut = false;
            }

            timeSinceLastRadarCheck += Time.deltaTime;
        }

        if (isRetreating)
        {
            retreatTimer -= Time.deltaTime;
            if (retreatTimer <= 0f)
            {
                isRetreating = false;
                Debug.Log("<color=yellow>[Surveyor]</color> Grace period over. Resuming watch...");
            }
        }

        UpdatePosition();
        CheckProximitySound();

        if (currentDistance <= killDistance)
        {
            KillPlayerOffline();
        }
    }

    void UpdatePosition()
    {
        float previousDistance = currentDistance;
        bool wasMoving = isCurrentlyMoving;

        bool isPowerOut = PowerSystem.Instance != null && PowerSystem.Instance.IsPowerOut;

        if (isPowerOut)
        {
            isCurrentlyMoving = false;
            UpdateMovementSound();
            return;
        }

        if (timeSinceLastRadarCheck < activationDelay)
        {
            isCurrentlyMoving = false;
            UpdateMovementSound();
            return;
        }

        if (isRetreating)
        {
            currentDistance += retreatSpeed * Time.deltaTime;
            currentDistance = Mathf.Min(currentDistance, safeDistance);
            isCurrentlyMoving = false;
        }
        else
        {
            if (timeSinceLastRadarCheck >= activationDelay && !wasMoving)
            {
                Debug.Log("<color=red>[Surveyor]</color> You haven't checked in a full minute. It begins to advance...");
            }

            float speedMultiplier = 1f;
            if (timeSinceLastRadarCheck > neglectThreshold + activationDelay)
            {
                speedMultiplier = 2f;
                if (Random.value < 0.01f)
                {
                    Debug.Log("<color=red>[Surveyor]</color> It's been too long. It advances rapidly...");
                }
            }

            currentDistance -= aggressiveSpeed * speedMultiplier * Time.deltaTime;
            currentDistance = Mathf.Max(currentDistance, 0f);
            isCurrentlyMoving = true;
        }

        UpdateMovementSound();

        if (currentDistance != previousDistance)
        {
            Vector3 directionAway = (transform.position - objectiveTransform.position).normalized;
            if (directionAway == Vector3.zero)
            {
                directionAway = Random.onUnitSphere;
                directionAway.y = 0;
                directionAway.Normalize();
            }

            transform.position = objectiveTransform.position + directionAway * currentDistance;
        }
    }

    void UpdateMovementSound()
    {
        if (movementSound == null || movementAudioSource == null)
        {
            return;
        }

        if (isCurrentlyMoving && !movementAudioSource.isPlaying)
        {
            movementAudioSource.clip = movementSound;
            movementAudioSource.volume = 0.3f;
            movementAudioSource.Play();
            Debug.Log("<color=cyan>[Surveyor]</color> Movement sound started - it's advancing...");
        }
        else if (!isCurrentlyMoving && movementAudioSource.isPlaying)
        {
            movementAudioSource.Stop();
        }
    }

    public void OnRadarChecked()
    {
        if (IsNetworkSessionActive() && !IsServer)
        {
            return;
        }

        timeSinceLastRadarCheck = 0f;

        if (currentDistance < safeDistance)
        {
            isRetreating = true;
            retreatTimer = radarGracePeriod;
            Debug.Log($"<color=cyan>[Surveyor]</color> Radar checked! Distance: {currentDistance:F1}m - It retreats for now...");
        }
        else
        {
            Debug.Log($"<color=green>[Surveyor]</color> Radar checked! Distance: {currentDistance:F1}m - It maintains position.");
        }

        if (IsNetworkSessionActive() && IsServer)
        {
            TimeSinceLastRadarCheckNetwork.Value = timeSinceLastRadarCheck;
            IsRetreatingNetwork.Value = isRetreating;
        }
    }

    void CheckProximitySound()
    {
        if (proximitySound == null || audioSource == null)
        {
            return;
        }

        if (currentDistance <= safeDistance && !audioSource.isPlaying)
        {
            audioSource.clip = proximitySound;
            audioSource.loop = true;
            audioSource.volume = Mathf.Clamp01(1f - (currentDistance / safeDistance));
            audioSource.Play();
        }
        else if (currentDistance > safeDistance && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
        else if (audioSource.isPlaying)
        {
            audioSource.volume = Mathf.Clamp01(1f - (currentDistance / safeDistance));
        }
    }

    void KillPlayerServer()
    {
        if (hasKilled)
        {
            return;
        }

        hasKilled = true;
        Debug.Log("<color=red>[Surveyor]</color> It got too close. You never saw it coming...");

        if (movementAudioSource != null && movementAudioSource.isPlaying)
        {
            movementAudioSource.Stop();
        }

        if (killSound != null && audioSource != null)
        {
            audioSource.Stop();
            audioSource.loop = false;
            audioSource.PlayOneShot(killSound);
        }

        List<ulong> targetClients = new List<ulong>();
        if (NetworkManager.Singleton != null)
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject != null)
                {
                    targetClients.Add(client.ClientId);
                }
            }
        }

        if (targetClients.Count == 0 && playerTransform != null)
        {
            PlayerController playerController = playerTransform.GetComponent<PlayerController>();
            if (playerController != null)
            {
                playerController.enabled = false;
            }

            OxygenSystem oxygenSystem = playerTransform.GetComponent<OxygenSystem>();
            if (oxygenSystem != null)
            {
                oxygenSystem.enabled = false;
            }

            StartCoroutine(ShowSurveyorDeath(true));
        }
        else
        {
            ShowSurveyorDeathClientRpc();
        }

    }

    void KillPlayerOffline()
    {
        hasKilled = true;
        Debug.Log("<color=red>[Surveyor]</color> It got too close. You never saw it coming...");

        if (movementAudioSource != null && movementAudioSource.isPlaying)
        {
            movementAudioSource.Stop();
        }

        if (killSound != null && audioSource != null)
        {
            audioSource.Stop();
            audioSource.loop = false;
            audioSource.PlayOneShot(killSound);
        }

        PlayerController playerController = playerTransform != null ? playerTransform.GetComponent<PlayerController>() : FindObjectOfType<PlayerController>();
        if (playerController != null)
        {
            playerController.enabled = false;
        }

        OxygenSystem oxygenSystem = playerTransform != null ? playerTransform.GetComponent<OxygenSystem>() : FindObjectOfType<OxygenSystem>();
        if (oxygenSystem != null)
        {
            oxygenSystem.enabled = false;
        }

        StartCoroutine(ShowSurveyorDeath(true));
        Time.timeScale = 0f;
    }

    [ClientRpc]
    private void ShowSurveyorDeathClientRpc()
    {
        StartCoroutine(ShowSurveyorDeath(false));
    }

    IEnumerator ShowSurveyorDeath(bool restartScene)
    {
        Time.timeScale = 0f;

        Canvas blackCanvas = new GameObject("SurveyorDeathCanvas").AddComponent<Canvas>();
        blackCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        blackCanvas.sortingOrder = 9999;

        CanvasScaler scaler = blackCanvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        GameObject blackPanel = new GameObject("BlackScreen");
        blackPanel.transform.SetParent(blackCanvas.transform, false);

        UnityEngine.UI.Image blackImage = blackPanel.AddComponent<UnityEngine.UI.Image>();
        blackImage.color = Color.black;

        RectTransform rt = blackPanel.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        GameObject textObj = new GameObject("DeathText");
        textObj.transform.SetParent(blackCanvas.transform, false);

        TextMeshProUGUI deathText = textObj.AddComponent<TextMeshProUGUI>();
        deathText.text = deathMessage;
        deathText.fontSize = 48;
        deathText.color = Color.white;
        deathText.alignment = TextAlignmentOptions.Center;

        RectTransform textRt = textObj.GetComponent<RectTransform>();
        textRt.anchorMin = new Vector2(0.5f, 0.5f);
        textRt.anchorMax = new Vector2(0.5f, 0.5f);
        textRt.sizeDelta = new Vector2(800, 200);
        textRt.anchoredPosition = Vector2.zero;

        yield return new WaitForSecondsRealtime(3f);

        if (restartScene)
        {
            Time.timeScale = 1f;
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }
        else
        {
            Time.timeScale = 0f;
        }
    }

    public float GetCurrentDistance()
    {
        return IsNetworkSessionActive() ? CurrentDistanceNetwork.Value : currentDistance;
    }

    public bool IsInDangerZone()
    {
        return GetCurrentDistance() < safeDistance;
    }

    void PushNetworkStateFromServer()
    {
        CurrentDistanceNetwork.Value = currentDistance;
        IsRetreatingNetwork.Value = isRetreating;
        TimeSinceLastRadarCheckNetwork.Value = timeSinceLastRadarCheck;
    }

    void ApplyNetworkStateToClient()
    {
        currentDistance = CurrentDistanceNetwork.Value;
        isRetreating = IsRetreatingNetwork.Value;
        timeSinceLastRadarCheck = TimeSinceLastRadarCheckNetwork.Value;

        if (objectiveTransform != null)
        {
            Vector3 directionAway = (transform.position - objectiveTransform.position).normalized;
            if (directionAway == Vector3.zero)
            {
                directionAway = Random.onUnitSphere;
                directionAway.y = 0;
                directionAway.Normalize();
            }

            transform.position = objectiveTransform.position + directionAway * currentDistance;
        }
    }

    bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    void OnDrawGizmosSelected()
    {
        if (objectiveTransform == null) return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(objectiveTransform.position, killDistance);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(objectiveTransform.position, safeDistance);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(objectiveTransform.position, startingDistance);

        if (Application.isPlaying)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(objectiveTransform.position, transform.position);
            Gizmos.DrawWireSphere(transform.position, 2f);
        }
    }
}
