using UnityEngine;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class LockerInteractionNew : NetworkBehaviour
{
    public static LockerInteractionNew Instance { get; private set; }

    [Header("Locker Camera")]
    [Tooltip("Reference camera positioned where player should look when hiding")]
    public Transform lockerCameraTransform;

    [Header("Interaction Settings")]
    [Tooltip("Distance to interact with locker")]
    public float interactionDistance = 3f;

    [Header("Hide Settings")]
    [Tooltip("Field of view when hiding (narrower = peeking through crack)")]
    public float hideFOV = 40f;

    [Header("UI References")]
    public TextMeshProUGUI interactionPromptText;

    [Header("Audio")]
    public AudioClip doorCloseSound;
    public AudioClip doorOpenSound;

    public NetworkVariable<ulong> HiddenClientIdNetwork = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> DoorOpenNetwork = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private bool isPlayerNearLocker = false;
    private bool isPlayerHidden = false;
    private Transform playerTransform;
    private Transform playerCamera;
    private CharacterController playerController;
    private PlayerController playerControllerScript;
    private Camera cam;
    private Vector3 originalCameraLocalPosition;
    private Quaternion originalCameraLocalRotation;
    private float originalFOV;
    private AudioSource audioSource;

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
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    void Start()
    {
        RefreshLocalPlayerReference();

        if (playerTransform == null)
        {
            Debug.LogError("<color=red>[Locker]</color> No player found with tag 'Player'!");
        }
        else if (playerControllerScript == null)
        {
            Debug.LogError("<color=red>[Locker]</color> PlayerController not found!");
        }

        if (lockerCameraTransform == null)
        {
            Transform foundCamera = transform.Find("Camera");
            if (foundCamera != null)
            {
                lockerCameraTransform = foundCamera;
                Debug.Log($"<color=cyan>[Locker]</color> Auto-found locker camera: {foundCamera.name}");
            }
            else
            {
                Debug.LogError("<color=red>[Locker]</color> No locker camera assigned! Create a camera as child of locker and assign it.");
            }
        }

        if (lockerCameraTransform != null)
        {
            Camera lockerCam = lockerCameraTransform.GetComponent<Camera>();
            if (lockerCam != null)
            {
                lockerCam.enabled = false;
            }

            AudioListener listener = lockerCameraTransform.GetComponent<AudioListener>();
            if (listener != null)
            {
                listener.enabled = false;
            }
        }

        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(false);
        }

        Debug.Log($"<color=green>[Locker]</color> Locker interaction system ready at position {transform.position}");
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            HiddenClientIdNetwork.Value = ulong.MaxValue;
            DoorOpenNetwork.Value = true;
        }
    }

    void Update()
    {
        RefreshLocalPlayerReference();
        CheckPlayerProximity();

        bool eKeyPressed = false;

#if ENABLE_INPUT_SYSTEM
        eKeyPressed = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
        eKeyPressed = Input.GetKeyDown(INTERACTION_KEY);
#endif

        if (isPlayerHidden && eKeyPressed)
        {
            Debug.Log($"<color=cyan>[Locker]</color> E key pressed while hidden - exiting locker!");
            RequestExitLocker();
            return;
        }

        if (isPlayerNearLocker && eKeyPressed && !isPlayerHidden)
        {
            Debug.Log($"<color=cyan>[Locker]</color> E key accepted! Triggering interaction...");
            RequestEnterLocker();
        }
    }

    void CheckPlayerProximity()
    {
        if (playerTransform == null)
        {
            return;
        }

        float distance = Vector3.Distance(playerTransform.position, transform.position);
        bool wasNear = isPlayerNearLocker;
        isPlayerNearLocker = distance <= interactionDistance;

        if (isPlayerNearLocker && !wasNear && !isPlayerHidden)
        {
            ShowInteractionPrompt();
        }
        else if (!isPlayerNearLocker && wasNear)
        {
            HideInteractionPrompt();
        }

        if (isPlayerNearLocker && interactionPromptText != null && !isPlayerHidden)
        {
            interactionPromptText.text = $"Press E to hide in locker\n({distance:F1}m)";
        }
    }

    void RequestEnterLocker()
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                EnterLockerServer(NetworkManager.Singleton.LocalClientId);
            }
            else
            {
                RequestEnterLockerServerRpc();
            }
            return;
        }

        EnterLockerLocal();
    }

    void RequestExitLocker()
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                ExitLockerServer(NetworkManager.Singleton.LocalClientId);
            }
            else
            {
                RequestExitLockerServerRpc();
            }
            return;
        }

        ExitLockerLocal(true);
    }

    void EnterLockerLocal()
    {
        Debug.Log("<color=cyan>[Locker]</color> Entering locker...");

        RefreshLocalPlayerReference();

        if (playerCamera == null || lockerCameraTransform == null)
        {
            Debug.LogError("<color=red>[Locker]</color> Camera not found!");
            return;
        }

        originalCameraLocalPosition = playerCamera.localPosition;
        originalCameraLocalRotation = playerCamera.localRotation;

        if (cam != null)
        {
            originalFOV = cam.fieldOfView;
            cam.fieldOfView = hideFOV;
        }

        playerCamera.position = lockerCameraTransform.position;
        playerCamera.rotation = lockerCameraTransform.rotation;

        if (!IsNetworkSessionActive() && playerController != null)
        {
            playerController.enabled = false;
        }

        if (!IsNetworkSessionActive() && playerControllerScript != null)
        {
            playerControllerScript.enabled = false;
        }

        isPlayerHidden = true;

        if (doorCloseSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(doorCloseSound);
        }

        HideInteractionPrompt();
        Debug.Log("<color=green>[Locker]</color> Camera locked at locker camera position");
    }

    void ExitLockerLocal(bool shouldPlaySound)
    {
        Debug.Log("<color=cyan>[Locker]</color> Exiting locker...");

        if (RushMonsterEvent.Instance != null && RushMonsterEvent.Instance.IsRushPassing())
        {
            Debug.Log("<color=red>[Locker]</color> Player exited during Rush - DEATH!");
            RestorePlayerFromLocker();
            isPlayerHidden = false;

            if (IsNetworkSessionActive() && IsServer)
            {
                RushMonsterEvent.Instance.KillPlayerForExiting();
            }
            else
            {
                RushMonsterEvent.Instance.KillPlayerForExiting();
            }

            return;
        }

        RestorePlayerFromLocker();
        isPlayerHidden = false;

        if (shouldPlaySound && doorOpenSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(doorOpenSound);
        }

        Debug.Log("<color=green>[Locker]</color> Player exited - camera and controls restored");
    }

    void RestorePlayerFromLocker()
    {
        RefreshLocalPlayerReference();

        if (playerCamera == null)
        {
            Debug.LogError("<color=red>[Locker]</color> Camera not found!");
            return;
        }

        playerCamera.localPosition = originalCameraLocalPosition;
        playerCamera.localRotation = originalCameraLocalRotation;

        if (cam != null)
        {
            cam.fieldOfView = originalFOV;
        }

        if (!IsNetworkSessionActive() && playerController != null)
        {
            playerController.enabled = true;
        }

        if (!IsNetworkSessionActive() && playerControllerScript != null)
        {
            playerControllerScript.enabled = true;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestEnterLockerServerRpc(ServerRpcParams serverRpcParams = default)
    {
        if (!IsServer)
        {
            return;
        }

        EnterLockerServer(serverRpcParams.Receive.SenderClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestExitLockerServerRpc(ServerRpcParams serverRpcParams = default)
    {
        if (!IsServer)
        {
            return;
        }

        ExitLockerServer(serverRpcParams.Receive.SenderClientId);
    }

    void EnterLockerServer(ulong clientId)
    {
        if (HiddenClientIdNetwork.Value != ulong.MaxValue && HiddenClientIdNetwork.Value != clientId)
        {
            return;
        }

        HiddenClientIdNetwork.Value = clientId;
        DoorOpenNetwork.Value = false;
        SetPlayerMovementEnabled(clientId, false);

        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        };

        EnterLockerClientRpc(clientRpcParams);
    }

    void ExitLockerServer(ulong clientId)
    {
        if (HiddenClientIdNetwork.Value != clientId)
        {
            return;
        }

        if (RushMonsterEvent.Instance != null && RushMonsterEvent.Instance.IsRushPassing())
        {
            RushMonsterEvent.Instance.KillPlayerForExiting(clientId);
            HiddenClientIdNetwork.Value = ulong.MaxValue;
            DoorOpenNetwork.Value = true;
            SetPlayerMovementEnabled(clientId, true);
            return;
        }

        HiddenClientIdNetwork.Value = ulong.MaxValue;
        DoorOpenNetwork.Value = true;
        SetPlayerMovementEnabled(clientId, true);

        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        };

        ExitLockerClientRpc(clientRpcParams);
    }

    [ClientRpc]
    private void EnterLockerClientRpc(ClientRpcParams clientRpcParams = default)
    {
        EnterLockerLocal();
    }

    [ClientRpc]
    private void ExitLockerClientRpc(ClientRpcParams clientRpcParams = default)
    {
        ExitLockerLocal(true);
    }

    public bool IsPlayerHidden()
    {
        if (IsNetworkSessionActive())
        {
            if (NetworkManager.Singleton != null)
            {
                return HiddenClientIdNetwork.Value == NetworkManager.Singleton.LocalClientId;
            }

            return false;
        }

        return isPlayerHidden;
    }

    public bool IsClientHidden(ulong clientId)
    {
        if (!IsNetworkSessionActive())
        {
            return isPlayerHidden;
        }

        return HiddenClientIdNetwork.Value == clientId;
    }

    private void SetPlayerMovementEnabled(ulong clientId, bool enabled)
    {
        if (!IsServer || NetworkManager.Singleton == null || NetworkManager.Singleton.ConnectedClients == null)
        {
            return;
        }

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
        {
            return;
        }

        NetworkPlayerMovementState movementState = client.PlayerObject.GetComponent<NetworkPlayerMovementState>();
        if (movementState != null)
        {
            movementState.MovementEnabled.Value = enabled;
        }
    }

    public void ForceExitLocker()
    {
        if (isPlayerHidden)
        {
            RequestExitLocker();
        }
    }

    void ShowInteractionPrompt()
    {
        if (interactionPromptText != null)
        {
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

    void RefreshLocalPlayerReference()
    {
        if (IsNetworkSessionActive() && NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            playerTransform = NetworkManager.Singleton.LocalClient.PlayerObject.transform;
            playerController = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<CharacterController>();
            playerControllerScript = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();

            if (playerControllerScript != null)
            {
                playerCamera = playerControllerScript.playerCamera;
                if (playerCamera != null)
                {
                    cam = playerCamera.GetComponent<Camera>();
                }
            }

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
            playerController = player.GetComponent<CharacterController>();
            playerControllerScript = player.GetComponent<PlayerController>();

            if (playerControllerScript != null)
            {
                playerCamera = playerControllerScript.playerCamera;
                if (playerCamera != null)
                {
                    cam = playerCamera.GetComponent<Camera>();
                    Debug.Log($"<color=cyan>[Locker]</color> Found player: {player.name}, Camera: {playerCamera.name}");
                }
            }
        }
    }

    bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionDistance);

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.5f);

        if (lockerCameraTransform != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(lockerCameraTransform.position, 0.3f);
            Gizmos.DrawLine(lockerCameraTransform.position, lockerCameraTransform.position + lockerCameraTransform.forward * 2f);
        }
    }
}
