using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;

public class IgnoreMonsterFixed : MonoBehaviour
{
    public static IgnoreMonsterFixed Instance { get; private set; }

    [Header("Spawn Settings")]
    [Tooltip("Minimum distance from player to spawn")]
    public float minSpawnDistance = 10f;

    [Tooltip("Maximum distance from player to spawn")]
    public float maxSpawnDistance = 25f;

    [Tooltip("How often to attempt spawn (in seconds)")]
    public float spawnInterval = 120f;

    [Tooltip("Time before it leaves if ignored (in seconds)")]
    public float presenceDuration = 15f;

    [Header("Look Detection")]
    [Tooltip("How long player can look before death (in seconds)")]
    public float maxLookTime = 2f;

    [Tooltip("Field of view angle to detect looking")]
    public float detectionAngle = 30f;

    [Tooltip("Max distance player can see it from")]
    public float maxViewDistance = 50f;

    [Header("Audio")]
    [Tooltip("Sound when it appears (subtle warning)")]
    public AudioClip appearSound;

    [Tooltip("Sound while being looked at (intensity builds)")]
    public AudioClip lookingSound;

    [Tooltip("Sound when it kills you")]
    public AudioClip killSound;

    [Tooltip("Sound when it leaves successfully ignored")]
    public AudioClip leaveSound;

    [Header("Death Settings")]
    public Texture2D ignoreDeathImage;
    public string deathMessage = "YOU LOOKED";

    [Header("Radar Deception")]
    [Tooltip("Fake distance to show on radar (constant)")]
    public float fakeRadarDistance = 75f;

    private Transform playerTransform;
    private Camera playerCamera;
    private bool isPresent = false;
    private float presenceTimer = 0f;
    private float lookTimer = 0f;
    private float spawnTimer = 0f;
    private bool hasKilled = false;
    private AudioSource audioSource;
    private AudioSource lookingAudioSource;
    private bool wasLookingLastFrame = false;
    private Renderer monsterRenderer;
    private Transform rootTransform;
    private Collider[] monsterColliders;

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

        rootTransform = transform.root;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 0.8f;
        audioSource.maxDistance = 50f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;

        lookingAudioSource = gameObject.AddComponent<AudioSource>();
        lookingAudioSource.spatialBlend = 0f;
        lookingAudioSource.loop = true;

        monsterRenderer = GetComponent<Renderer>();
        if (monsterRenderer == null)
        {
            monsterRenderer = GetComponentInChildren<Renderer>();
        }
        
        if (monsterRenderer != null)
        {
            monsterRenderer.enabled = false;
            Debug.Log("<color=purple>[The Ignore]</color> Renderer found and hidden initially");
        }
        else
        {
            Debug.LogWarning("<color=yellow>[The Ignore]</color> No renderer found on this GameObject or children!");
        }

        monsterColliders = rootTransform.GetComponentsInChildren<Collider>();
        Debug.Log($"<color=purple>[The Ignore]</color> Found {monsterColliders.Length} colliders in hierarchy");
    }

    void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            playerCamera = Camera.main;
            Debug.Log("<color=purple>[The Ignore]</color> Initialized. It watches from the void...");
        }
        else
        {
            Debug.LogError("<color=red>[The Ignore]</color> No player found!");
        }

        rootTransform.gameObject.tag = "Enemy";
        spawnTimer = spawnInterval;
    }

    void Update()
    {
        if (playerTransform == null || hasKilled) return;

        if (!isPresent)
        {
            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f)
            {
                TrySpawn();
                spawnTimer = spawnInterval;
            }
        }
        else
        {
            presenceTimer += Time.deltaTime;

            rootTransform.LookAt(new Vector3(playerTransform.position.x, rootTransform.position.y, playerTransform.position.z));

            bool isLookingAtIt = IsPlayerLookingAtMonster();

            if (isLookingAtIt)
            {
                lookTimer += Time.deltaTime;
                Debug.Log($"<color=red>[The Ignore]</color> LOOKING AT IT! Timer: {lookTimer:F2}/{maxLookTime}s");

                if (!wasLookingLastFrame && lookingSound != null && lookingAudioSource != null)
                {
                    lookingAudioSource.clip = lookingSound;
                    lookingAudioSource.volume = 0.3f;
                    lookingAudioSource.Play();
                    Debug.Log("<color=yellow>[The Ignore]</color> Started looking sound!");
                }

                if (lookTimer >= maxLookTime)
                {
                    KillPlayer();
                }
            }
            else
            {
                if (lookTimer > 0.1f)
                {
                    Debug.Log($"<color=green>[The Ignore]</color> Stopped looking. Timer reset from {lookTimer:F2}s");
                }
                lookTimer = 0f;
                
                if (wasLookingLastFrame && lookingAudioSource != null && lookingAudioSource.isPlaying)
                {
                    lookingAudioSource.Stop();
                }
            }

            wasLookingLastFrame = isLookingAtIt;

            if (presenceTimer >= presenceDuration)
            {
                Despawn();
            }
        }
    }

    void TrySpawn()
    {
        if (playerTransform == null) return;

        Vector2 randomCircle = Random.insideUnitCircle.normalized;
        float randomDistance = Random.Range(minSpawnDistance, maxSpawnDistance);
        Vector3 randomOffset = new Vector3(randomCircle.x, 0f, randomCircle.y) * randomDistance;
        Vector3 spawnPosition = playerTransform.position + randomOffset;

        RaycastHit hit;
        Vector3 rayStart = new Vector3(spawnPosition.x, playerTransform.position.y + 50f, spawnPosition.z);
        
        if (Physics.Raycast(rayStart, Vector3.down, out hit, 100f))
        {
            spawnPosition = hit.point + Vector3.up * 0.1f;
            Debug.Log($"<color=purple>[The Ignore]</color> Ground detected at Y: {hit.point.y}");
        }
        else
        {
            spawnPosition.y = playerTransform.position.y;
            Debug.LogWarning("<color=yellow>[The Ignore]</color> No ground found, using player Y height");
        }

        rootTransform.position = spawnPosition;
        rootTransform.LookAt(new Vector3(playerTransform.position.x, rootTransform.position.y, playerTransform.position.z));

        isPresent = true;
        presenceTimer = 0f;
        lookTimer = 0f;

        if (monsterRenderer != null)
        {
            monsterRenderer.enabled = true;
        }

        if (appearSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(appearSound);
        }

        Debug.Log($"<color=purple>[The Ignore]</color> It appears {randomDistance:F1}m away at position {rootTransform.position}. DO NOT LOOK.");
    }

    void Despawn()
    {
        isPresent = false;
        presenceTimer = 0f;
        lookTimer = 0f;

        if (monsterRenderer != null)
        {
            monsterRenderer.enabled = false;
        }

        if (lookingAudioSource != null && lookingAudioSource.isPlaying)
        {
            lookingAudioSource.Stop();
        }

        if (leaveSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(leaveSound);
        }

        Debug.Log("<color=green>[The Ignore]</color> You ignored it. It leaves, satisfied.");
    }

    bool IsPlayerLookingAtMonster()
    {
        if (playerCamera == null) return false;

        Vector3 monsterCenter = rootTransform.position + Vector3.up * 1.5f;
        Vector3 directionToMonster = (monsterCenter - playerCamera.transform.position).normalized;
        float distance = Vector3.Distance(playerCamera.transform.position, monsterCenter);

        if (distance > maxViewDistance)
            return false;

        float dotProduct = Vector3.Dot(playerCamera.transform.forward, directionToMonster);
        float angleToMonster = Mathf.Acos(Mathf.Clamp(dotProduct, -1f, 1f)) * Mathf.Rad2Deg;

        if (angleToMonster > detectionAngle)
            return false;

        RaycastHit hit;
        if (Physics.Raycast(playerCamera.transform.position, directionToMonster, out hit, distance + 5f))
        {
            if (hit.collider != null)
            {
                foreach (Collider col in monsterColliders)
                {
                    if (hit.collider == col)
                    {
                        Debug.Log($"<color=red>[The Ignore]</color> LOOKING! Hit: {hit.collider.gameObject.name}, Angle: {angleToMonster:F1}°, Timer: {lookTimer:F2}/{maxLookTime}s");
                        return true;
                    }
                }

                if (Random.value < 0.01f)
                {
                    Debug.Log($"<color=gray>[The Ignore]</color> Raycast hit {hit.collider.gameObject.name} at {hit.distance:F1}m (angle {angleToMonster:F1}°) instead of monster");
                }
            }
        }
        else
        {
            if (Random.value < 0.005f)
            {
                Debug.Log($"<color=gray>[The Ignore]</color> Raycast missed everything (angle {angleToMonster:F1}°, dist {distance:F1}m)");
            }
        }

        return false;
    }

    void KillPlayer()
    {
        if (hasKilled) return;
        hasKilled = true;

        Debug.Log("<color=red>[The Ignore]</color> You looked. You shouldn't have looked.");

        if (lookingAudioSource != null && lookingAudioSource.isPlaying)
        {
            lookingAudioSource.Stop();
        }

        if (killSound != null && audioSource != null)
        {
            audioSource.Stop();
            audioSource.PlayOneShot(killSound);
        }

        OxygenSystem oxygenSystem = FindObjectOfType<OxygenSystem>();
        if (oxygenSystem != null)
        {
            StartCoroutine(ShowIgnoreDeath());
            oxygenSystem.MarkDeadFromMonster();
        }
    }

    IEnumerator ShowIgnoreDeath()
    {
        Canvas deathCanvas = new GameObject("IgnoreDeathCanvas").AddComponent<Canvas>();
        deathCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        deathCanvas.sortingOrder = 9999;

        CanvasScaler scaler = deathCanvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        GameObject imageObj = new GameObject("DeathImage");
        imageObj.transform.SetParent(deathCanvas.transform, false);

        Image deathImageComponent = imageObj.AddComponent<Image>();
        
        if (ignoreDeathImage != null)
        {
            Sprite deathSprite = Sprite.Create(
                ignoreDeathImage,
                new Rect(0, 0, ignoreDeathImage.width, ignoreDeathImage.height),
                new Vector2(0.5f, 0.5f)
            );
            deathImageComponent.sprite = deathSprite;
        }
        else
        {
            deathImageComponent.color = Color.black;
        }

        RectTransform imageRt = imageObj.GetComponent<RectTransform>();
        imageRt.anchorMin = Vector2.zero;
        imageRt.anchorMax = Vector2.one;
        imageRt.sizeDelta = Vector2.zero;

        GameObject textObj = new GameObject("DeathText");
        textObj.transform.SetParent(deathCanvas.transform, false);

        TextMeshProUGUI deathText = textObj.AddComponent<TextMeshProUGUI>();
        deathText.text = deathMessage;
        deathText.fontSize = 72;
        deathText.color = Color.red;
        deathText.alignment = TextAlignmentOptions.Center;
        deathText.fontStyle = FontStyles.Bold;

        RectTransform textRt = textObj.GetComponent<RectTransform>();
        textRt.anchorMin = new Vector2(0.5f, 0.5f);
        textRt.anchorMax = new Vector2(0.5f, 0.5f);
        textRt.sizeDelta = new Vector2(1000, 200);
        textRt.anchoredPosition = Vector2.zero;

        Debug.Log("<color=red>[The Ignore]</color> Death screen displayed");

        yield return new WaitForSecondsRealtime(4f);

        Debug.Log("<color=red>[The Ignore]</color> Restarting scene...");

        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public bool IsCurrentlyPresent()
    {
        return isPresent;
    }

    public float GetFakeDistance()
    {
        return fakeRadarDistance;
    }

    public float GetPresenceTimeRemaining()
    {
        if (!isPresent) return 0f;
        return Mathf.Max(0f, presenceDuration - presenceTimer);
    }

    public float GetLookTimeRemaining()
    {
        if (!isPresent) return maxLookTime;
        return Mathf.Max(0f, maxLookTime - lookTimer);
    }

    void OnDrawGizmos()
    {
        if (!isPresent || playerCamera == null) return;

        if (rootTransform == null) rootTransform = transform.root;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(rootTransform.position, 1f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(rootTransform.position, rootTransform.position + rootTransform.forward * 3f);

        if (playerTransform != null)
        {
            Gizmos.color = IsPlayerLookingAtMonster() ? Color.red : Color.green;
            Gizmos.DrawLine(rootTransform.position, playerTransform.position);
        }
    }
}
