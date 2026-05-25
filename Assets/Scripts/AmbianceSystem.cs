using UnityEngine;
using System.Collections;
using Unity.Netcode;

public class AmbianceSystem : MonoBehaviour
{
    [Header("Audio Clips")]
    [Tooltip("Ambient sound when inside the bunker")]
    public AudioClip bunkerAmbiance;

    [Tooltip("Ambient sound when outside on the surface")]
    public AudioClip surfaceAmbiance;

    [Header("Volume Settings")]
    [Tooltip("Volume of bunker ambiance (0-1)")]
    [Range(0f, 1f)]
    public float bunkerVolume = 0.5f;

    [Tooltip("Volume of surface ambiance (0-1)")]
    [Range(0f, 1f)]
    public float surfaceVolume = 0.5f;

    [Header("Settings")]
    [Tooltip("Tag used to identify bunker zones")]
    public string bunkerZoneTag = "Bunker";

    [Tooltip("How far to check for bunker zones")]
    public float bunkerCheckRadius = 1f;

    [Tooltip("Time to crossfade between ambiances (in seconds)")]
    public float crossfadeDuration = 2f;

    private AudioSource bunkerAudioSource;
    private AudioSource surfaceAudioSource;
    private Transform playerTransform;
    private bool isInBunker = false;
    private bool wasInBunkerLastFrame = false;
    private Coroutine crossfadeCoroutine;
    private bool audioInitialized;

    void Start()
    {
        bunkerAudioSource = gameObject.AddComponent<AudioSource>();
        bunkerAudioSource.clip = bunkerAmbiance;
        bunkerAudioSource.loop = true;
        bunkerAudioSource.volume = 0f;
        bunkerAudioSource.spatialBlend = 0f;
        bunkerAudioSource.playOnAwake = false;

        surfaceAudioSource = gameObject.AddComponent<AudioSource>();
        surfaceAudioSource.clip = surfaceAmbiance;
        surfaceAudioSource.loop = true;
        surfaceAudioSource.volume = 0f;
        surfaceAudioSource.spatialBlend = 0f;
        surfaceAudioSource.playOnAwake = false;

        if (bunkerAmbiance != null)
        {
            bunkerAudioSource.Play();
        }

        if (surfaceAmbiance != null)
        {
            surfaceAudioSource.Play();
        }
    }

    void TryInitializeForLocalPlayer()
    {
        if (audioInitialized || playerTransform == null)
        {
            return;
        }

        bool startInBunker = CheckIfInBunker();
        if (startInBunker)
        {
            bunkerAudioSource.volume = bunkerVolume;
            surfaceAudioSource.volume = 0f;
            isInBunker = true;
            wasInBunkerLastFrame = true;
            Debug.Log("<color=cyan>[Ambiance]</color> Started in bunker - playing bunker ambiance");
        }
        else
        {
            bunkerAudioSource.volume = 0f;
            surfaceAudioSource.volume = surfaceVolume;
            isInBunker = false;
            wasInBunkerLastFrame = false;
            Debug.Log("<color=cyan>[Ambiance]</color> Started on surface - playing surface ambiance");
        }

        audioInitialized = true;
    }

    void RefreshLocalPlayerReference()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.LocalClient != null)
        {
            var localPlayerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (localPlayerObject != null)
            {
                playerTransform = localPlayerObject.transform;
            }
            return;
        }

        if (playerTransform == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
            }
        }
    }

    void Update()
    {
        RefreshLocalPlayerReference();
        if (playerTransform == null) return;
        TryInitializeForLocalPlayer();
        if (!audioInitialized) return;

        bool currentlyInBunker = CheckIfInBunker();

        if (currentlyInBunker && !wasInBunkerLastFrame)
        {
            isInBunker = true;
            wasInBunkerLastFrame = true;
            Debug.Log("<color=cyan>[Ambiance]</color> Entered bunker - switching to bunker ambiance");
            StartCrossfade(true);
        }
        else if (!currentlyInBunker && wasInBunkerLastFrame)
        {
            isInBunker = false;
            wasInBunkerLastFrame = false;
            Debug.Log("<color=orange>[Ambiance]</color> Left bunker - switching to surface ambiance");
            StartCrossfade(false);
        }
    }

    bool CheckIfInBunker()
    {
        Collider[] hitColliders = Physics.OverlapSphere(playerTransform.position, bunkerCheckRadius);
        foreach (var hitCollider in hitColliders)
        {
            if (hitCollider.CompareTag(bunkerZoneTag))
            {
                return true;
            }
        }
        return false;
    }

    void StartCrossfade(bool toBunker)
    {
        if (crossfadeCoroutine != null)
        {
            StopCoroutine(crossfadeCoroutine);
        }
        crossfadeCoroutine = StartCoroutine(CrossfadeAmbiance(toBunker));
    }

    IEnumerator CrossfadeAmbiance(bool toBunker)
    {
        float elapsed = 0f;
        float startBunkerVolume = bunkerAudioSource.volume;
        float startSurfaceVolume = surfaceAudioSource.volume;

        float targetBunkerVolume = toBunker ? bunkerVolume : 0f;
        float targetSurfaceVolume = toBunker ? 0f : surfaceVolume;

        while (elapsed < crossfadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / crossfadeDuration;

            bunkerAudioSource.volume = Mathf.Lerp(startBunkerVolume, targetBunkerVolume, t);
            surfaceAudioSource.volume = Mathf.Lerp(startSurfaceVolume, targetSurfaceVolume, t);

            yield return null;
        }

        bunkerAudioSource.volume = targetBunkerVolume;
        surfaceAudioSource.volume = targetSurfaceVolume;

        Debug.Log($"<color=green>[Ambiance]</color> Crossfade complete - Bunker: {bunkerAudioSource.volume:F2}, Surface: {surfaceAudioSource.volume:F2}");
    }

    void OnDrawGizmosSelected()
    {
        if (playerTransform != null)
        {
            Gizmos.color = isInBunker ? Color.cyan : Color.yellow;
            Gizmos.DrawWireSphere(playerTransform.position, bunkerCheckRadius);
        }
    }
}
