using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class JumpscareSystem : NetworkBehaviour
{
    public static JumpscareSystem Instance { get; private set; }

    [Header("Jumpscare UI")]
    [Tooltip("Image to display during jumpscare")]
    public Image jumpscareImage;

    [Tooltip("Optional text to show with jumpscare")]
    public TextMeshProUGUI jumpscareText;

    [Header("Jumpscare Settings")]
    [Tooltip("Duration to show jumpscare image")]
    public float jumpscareDuration = 2f;

    [Tooltip("Fade in speed")]
    public float fadeInSpeed = 5f;

    [Tooltip("Delay before showing reset prompt")]
    public float resetPromptDelay = 1f;

    [Header("Camera Shake")]
    [Tooltip("Enable camera shake during jumpscare")]
    public bool enableCameraShake = true;

    [Tooltip("Shake intensity")]
    public float shakeIntensity = 0.5f;

    [Tooltip("Shake duration")]
    public float shakeDuration = 0.5f;

    [Header("Audio")]
    [Tooltip("Jumpscare sound effect")]
    public AudioClip jumpscareSound;

    [Tooltip("Volume for jumpscare sound")]
    [Range(0f, 1f)]
    public float jumpscareVolume = 1f;

    [Header("Player Control")]
    [Tooltip("Disable player movement during jumpscare")]
    public bool disablePlayerMovement = true;

    [Header("Reset Prompt")]
    [Tooltip("Text for reset prompt (e.g., 'Press R to Restart')")]
    public TextMeshProUGUI resetPromptText;

    [Tooltip("Key to press to reset/restart")]
    public KeyCode resetKey = KeyCode.R;

    private AudioSource audioSource;
    private bool isJumpscareActive = false;
    private Camera playerCamera;
    private Vector3 originalCameraPosition;
    private PlayerController playerController;
    private CharacterController characterController;
    public NetworkVariable<bool> JumpscareActiveNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

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

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void Start()
    {
        if (jumpscareImage != null)
        {
            jumpscareImage.gameObject.SetActive(false);
            Color c = jumpscareImage.color;
            c.a = 0;
            jumpscareImage.color = c;
        }

        if (jumpscareText != null)
        {
            jumpscareText.gameObject.SetActive(false);
        }

        if (resetPromptText != null)
        {
            resetPromptText.gameObject.SetActive(false);
        }

        Debug.Log("<color=red>[Jumpscare]</color> System initialized");
    }

    void Update()
    {
        RefreshLocalPlayerReference();

        if (isJumpscareActive && resetPromptText != null && resetPromptText.gameObject.activeSelf)
        {
            if (Input.GetKeyDown(resetKey))
            {
                if (IsNetworkSessionActive() && !IsServer)
                {
                    RequestResetSceneServerRpc();
                }
                else
                {
                    ResetScene();
                }
            }
        }
    }

    public void TriggerJumpscare(Sprite jumpscareSprite = null, string message = "", AudioClip customSound = null)
    {
        if (isJumpscareActive) return;

        StartCoroutine(JumpscareSequence(jumpscareSprite, null, message, customSound));
    }

    public void TriggerJumpscareWithTexture(Texture2D jumpscareTexture = null, string message = "", AudioClip customSound = null)
    {
        if (isJumpscareActive) return;

        StartCoroutine(JumpscareSequence(null, jumpscareTexture, message, customSound));
    }

    public void TriggerJumpscareForClient(ulong clientId, string message = "")
    {
        if (IsNetworkSessionActive() && IsServer)
        {
            ClientRpcParams clientRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { clientId }
                }
            };

            TriggerJumpscareClientRpc(message, clientRpcParams);
            return;
        }

        StartCoroutine(JumpscareSequence(null, null, message, null));
    }

    IEnumerator JumpscareSequence(Sprite sprite, Texture2D texture, string message, AudioClip customSound)
    {
        RefreshLocalPlayerReference();
        isJumpscareActive = true;
        if (IsNetworkSessionActive() && IsServer)
        {
            JumpscareActiveNetwork.Value = true;
        }

        Debug.Log("<color=red>[Jumpscare]</color> TRIGGERED!");

        if (disablePlayerMovement)
        {
            if (playerController != null) playerController.enabled = false;
            if (characterController != null) characterController.enabled = false;
        }

        if (playerCamera != null)
        {
            originalCameraPosition = playerCamera.transform.localPosition;
        }

        AudioClip soundToPlay = customSound != null ? customSound : jumpscareSound;
        if (soundToPlay != null)
        {
            audioSource.volume = jumpscareVolume;
            audioSource.PlayOneShot(soundToPlay);
        }

        if (jumpscareImage != null)
        {
            jumpscareImage.gameObject.SetActive(true);
            
            if (sprite != null)
            {
                jumpscareImage.sprite = sprite;
            }
            else if (texture != null)
            {
                Rect rect = new Rect(0, 0, texture.width, texture.height);
                jumpscareImage.sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f));
            }

            yield return StartCoroutine(FadeInImage());
        }

        if (!string.IsNullOrEmpty(message) && jumpscareText != null)
        {
            jumpscareText.text = message;
            jumpscareText.gameObject.SetActive(true);
        }

        if (enableCameraShake && playerCamera != null)
        {
            yield return StartCoroutine(ShakeCamera());
        }

        yield return new WaitForSeconds(jumpscareDuration);

        if (resetPromptText != null)
        {
            yield return new WaitForSeconds(resetPromptDelay);
            resetPromptText.gameObject.SetActive(true);
        }
    }

    [ClientRpc]
    private void TriggerJumpscareClientRpc(string message, ClientRpcParams clientRpcParams = default)
    {
        StartCoroutine(JumpscareSequence(null, null, message, null));
    }

    IEnumerator FadeInImage()
    {
        Color c = jumpscareImage.color;
        c.a = 0;
        jumpscareImage.color = c;

        while (c.a < 1f)
        {
            c.a += fadeInSpeed * Time.deltaTime;
            jumpscareImage.color = c;
            yield return null;
        }

        c.a = 1f;
        jumpscareImage.color = c;
    }

    IEnumerator ShakeCamera()
    {
        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            float x = Random.Range(-1f, 1f) * shakeIntensity;
            float y = Random.Range(-1f, 1f) * shakeIntensity;

            playerCamera.transform.localPosition = originalCameraPosition + new Vector3(x, y, 0);

            elapsed += Time.deltaTime;
            yield return null;
        }

        playerCamera.transform.localPosition = originalCameraPosition;
    }

    public void ResetScene()
    {
        Debug.Log("<color=yellow>[Jumpscare]</color> Restarting scene...");
        if (IsNetworkSessionActive())
        {
            if (!IsServer)
            {
                return;
            }

            JumpscareActiveNetwork.Value = false;
            if (NetworkGameState.Instance != null)
            {
                NetworkGameState.Instance.SetRestartInProgress(true);
            }
            Time.timeScale = 1f;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.LoadScene(SceneManager.GetActiveScene().name, LoadSceneMode.Single);
            }
            return;
        }

        JumpscareActiveNetwork.Value = false;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public bool IsJumpscareActive()
    {
        return isJumpscareActive;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestResetSceneServerRpc(ServerRpcParams serverRpcParams = default)
    {
        ResetScene();
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    private void RefreshLocalPlayerReference()
    {
        if (IsNetworkSessionActive() && NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            var localPlayerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (localPlayerObject == null)
            {
                return;
            }

            playerController = localPlayerObject.GetComponent<PlayerController>();
            characterController = localPlayerObject.GetComponent<CharacterController>();
            if (playerController != null && playerController.playerCamera != null)
            {
                playerCamera = playerController.playerCamera.GetComponent<Camera>();
            }
            return;
        }

        if (playerController == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerController = player.GetComponent<PlayerController>();
                characterController = player.GetComponent<CharacterController>();
                if (playerController != null && playerController.playerCamera != null)
                {
                    playerCamera = playerController.playerCamera.GetComponent<Camera>();
                }
            }
        }
    }
}
