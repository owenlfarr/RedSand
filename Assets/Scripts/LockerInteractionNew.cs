using UnityEngine;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Unity.Netcode;
using System.Collections.Generic;

[RequireComponent(typeof(NetworkObject))]
public class LockerInteractionNew : NetworkBehaviour
{
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

    public NetworkVariable<ulong> Locker0HiddenClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<ulong> Locker1HiddenClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<ulong> Locker2HiddenClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<ulong> Locker3HiddenClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private const int MaxNetworkedLockers = 4;
    private bool isPlayerNearLocker = false;
    private bool isPlayerHidden = false;
    private int currentLockerIndex = -1;
    private int nearbyLockerIndex = -1;
    private Transform currentLockerTransform;
    private Transform currentLockerCameraTransform;
    private Transform playerTransform;
    private Transform playerCamera;
    private CharacterController playerController;
    private PlayerController playerControllerScript;
    private Camera cam;
    private Vector3 originalCameraLocalPosition;
    private Quaternion originalCameraLocalRotation;
    private float originalFOV;
    private AudioSource audioSource;
    private readonly Dictionary<Renderer, bool> rendererOriginalState = new Dictionary<Renderer, bool>();
    private readonly Dictionary<ulong, bool> appliedHiddenClientIds = new Dictionary<ulong, bool>();
    private readonly List<Transform> managedLockers = new List<Transform>();

    private const KeyCode INTERACTION_KEY = KeyCode.E;

    void Awake()
    {
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

        RefreshManagedLockers();

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
            Locker0HiddenClientId.Value = ulong.MaxValue;
            Locker1HiddenClientId.Value = ulong.MaxValue;
            Locker2HiddenClientId.Value = ulong.MaxValue;
            Locker3HiddenClientId.Value = ulong.MaxValue;
        }
    }

    void Update()
    {
        ApplyNetworkHiddenVisualState();
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

        RefreshManagedLockers();

        float distance = float.MaxValue;
        int nearestLockerIndex = -1;
        for (int i = 0; i < managedLockers.Count && i < MaxNetworkedLockers; i++)
        {
            Transform locker = managedLockers[i];
            if (locker == null)
            {
                continue;
            }

            float candidateDistance = Vector3.Distance(playerTransform.position, locker.position);
            if (candidateDistance < distance)
            {
                distance = candidateDistance;
                nearestLockerIndex = i;
            }
        }

        bool wasNear = isPlayerNearLocker;
        isPlayerNearLocker = nearestLockerIndex >= 0 && distance <= interactionDistance;
        nearbyLockerIndex = isPlayerNearLocker ? nearestLockerIndex : -1;

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
            bool occupied = IsLockerOccupiedByAnotherClient(nearbyLockerIndex);
            interactionPromptText.text = occupied
                ? $"Locker occupied\n({distance:F1}m)"
                : $"Press E to hide in locker\n({distance:F1}m)";
        }
    }

    void RequestEnterLocker()
    {
        int lockerIndex = nearbyLockerIndex;
        if (lockerIndex < 0 || lockerIndex >= managedLockers.Count || lockerIndex >= MaxNetworkedLockers)
        {
            return;
        }

        if (IsLockerOccupiedByAnotherClient(lockerIndex))
        {
            return;
        }

        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                EnterLockerServer(NetworkManager.Singleton.LocalClientId, lockerIndex);
            }
            else
            {
                RequestEnterLockerServerRpc(lockerIndex);
            }
            return;
        }

        EnterLockerLocal(lockerIndex);
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

    void EnterLockerLocal(int lockerIndex)
    {
        Debug.Log("<color=cyan>[Locker]</color> Entering locker...");

        RefreshLocalPlayerReference();
        RefreshManagedLockers();

        if (lockerIndex < 0 || lockerIndex >= managedLockers.Count)
        {
            Debug.LogError("<color=red>[Locker]</color> Locker index not found!");
            return;
        }

        currentLockerIndex = lockerIndex;
        currentLockerTransform = managedLockers[lockerIndex];
        currentLockerCameraTransform = GetLockerCameraTransform(currentLockerTransform);

        if (playerCamera == null || currentLockerCameraTransform == null)
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

        playerCamera.position = currentLockerCameraTransform.position;
        playerCamera.rotation = currentLockerCameraTransform.rotation;

        if (!IsNetworkSessionActive() && playerController != null)
        {
            playerController.enabled = false;
        }

        if (!IsNetworkSessionActive() && playerControllerScript != null)
        {
            playerControllerScript.enabled = false;
        }

        isPlayerHidden = true;
        SetPlayerVisualHidden(playerTransform, true);

        if (doorCloseSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(doorCloseSound);
        }

        HideInteractionPrompt();
        Debug.Log("<color=green>[Locker]</color> Camera locked at locker camera position");
    }

    void ExitLockerLocal(bool shouldPlaySound, bool killIfRush = true)
    {
        Debug.Log("<color=cyan>[Locker]</color> Exiting locker...");

        if (killIfRush && RushMonsterEvent.Instance != null && RushMonsterEvent.Instance.IsRushPassing())
        {
            Debug.Log("<color=red>[Locker]</color> Player exited during Rush - DEATH!");
            RestoreExitedState();

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

        RestoreExitedState();

        if (shouldPlaySound && doorOpenSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(doorOpenSound);
        }

        Debug.Log("<color=green>[Locker]</color> Player exited - camera and controls restored");
    }

    void RestoreExitedState()
    {
        RestorePlayerFromLocker();
        isPlayerHidden = false;
        SetPlayerVisualHidden(playerTransform, false);
        currentLockerIndex = -1;
        currentLockerTransform = null;
        currentLockerCameraTransform = null;
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
    private void RequestEnterLockerServerRpc(int lockerIndex, ServerRpcParams serverRpcParams = default)
    {
        if (!IsServer)
        {
            return;
        }

        EnterLockerServer(serverRpcParams.Receive.SenderClientId, lockerIndex);
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

    void EnterLockerServer(ulong clientId, int lockerIndex)
    {
        if (lockerIndex < 0 || lockerIndex >= MaxNetworkedLockers)
        {
            return;
        }

        ulong hiddenClientId = GetHiddenClientIdForLocker(lockerIndex);
        if (hiddenClientId != ulong.MaxValue && hiddenClientId != clientId)
        {
            return;
        }

        if (IsClientHiddenInAnotherLocker(clientId, lockerIndex))
        {
            return;
        }

        SetHiddenClientIdForLocker(lockerIndex, clientId);
        SyncLegacyHiddenClientId();
        DoorOpenNetwork.Value = false;
        SetPlayerMovementEnabled(clientId, false);

        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        };

        EnterLockerClientRpc(lockerIndex, clientRpcParams);
    }

    void ExitLockerServer(ulong clientId)
    {
        int lockerIndex = GetLockerIndexForClient(clientId);
        if (lockerIndex < 0)
        {
            return;
        }

        if (RushMonsterEvent.Instance != null && RushMonsterEvent.Instance.IsRushPassing())
        {
            RushMonsterEvent.Instance.KillPlayerForExiting(clientId);
            SetHiddenClientIdForLocker(lockerIndex, ulong.MaxValue);
            SyncLegacyHiddenClientId();
            DoorOpenNetwork.Value = true;
            SetPlayerMovementEnabled(clientId, true);
            SendExitLockerClientRpc(clientId, false);
            return;
        }

        SetHiddenClientIdForLocker(lockerIndex, ulong.MaxValue);
        SyncLegacyHiddenClientId();
        DoorOpenNetwork.Value = true;
        SetPlayerMovementEnabled(clientId, true);

        SendExitLockerClientRpc(clientId, true);
    }

    void SendExitLockerClientRpc(ulong clientId, bool killIfRush)
    {
        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        };

        ExitLockerClientRpc(killIfRush, clientRpcParams);
    }

    [ClientRpc]
    private void EnterLockerClientRpc(int lockerIndex, ClientRpcParams clientRpcParams = default)
    {
        EnterLockerLocal(lockerIndex);
    }

    [ClientRpc]
    private void ExitLockerClientRpc(bool killIfRush, ClientRpcParams clientRpcParams = default)
    {
        ExitLockerLocal(true, killIfRush);
    }

    public bool IsPlayerHidden()
    {
        if (IsNetworkSessionActive())
        {
            if (NetworkManager.Singleton != null)
            {
                return IsClientHidden(NetworkManager.Singleton.LocalClientId);
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

        return GetLockerIndexForClient(clientId) >= 0;
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

    void ApplyNetworkHiddenVisualState()
    {
        if (!IsNetworkSessionActive() || NetworkManager.Singleton == null)
        {
            return;
        }

        Dictionary<ulong, bool> currentlyHidden = new Dictionary<ulong, bool>();
        for (int i = 0; i < MaxNetworkedLockers; i++)
        {
            ulong hiddenId = GetHiddenClientIdForLocker(i);
            if (hiddenId != ulong.MaxValue)
            {
                currentlyHidden[hiddenId] = true;
            }
        }

        List<ulong> previouslyHidden = new List<ulong>(appliedHiddenClientIds.Keys);
        foreach (ulong clientId in previouslyHidden)
        {
            if (!currentlyHidden.ContainsKey(clientId))
            {
                Transform prev = GetPlayerTransformByClientId(clientId);
                if (prev != null)
                {
                    SetPlayerVisualHidden(prev, false);
                }

                appliedHiddenClientIds.Remove(clientId);
            }
        }

        foreach (ulong clientId in currentlyHidden.Keys)
        {
            if (appliedHiddenClientIds.ContainsKey(clientId))
            {
                continue;
            }

            Transform nowHidden = GetPlayerTransformByClientId(clientId);
            if (nowHidden != null)
            {
                SetPlayerVisualHidden(nowHidden, true);
                appliedHiddenClientIds[clientId] = true;
            }
        }
    }

    Transform GetPlayerTransformByClientId(ulong clientId)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.ConnectedClients == null)
        {
            return null;
        }

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
        {
            return null;
        }

        return client.PlayerObject.transform;
    }

    void SetPlayerVisualHidden(Transform playerRoot, bool hidden)
    {
        if (playerRoot == null)
        {
            return;
        }

        Renderer[] renderers = playerRoot.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            if (!rendererOriginalState.ContainsKey(renderer))
            {
                rendererOriginalState[renderer] = renderer.enabled;
            }

            if (hidden)
            {
                renderer.enabled = false;
            }
            else if (rendererOriginalState.TryGetValue(renderer, out bool wasEnabled))
            {
                renderer.enabled = wasEnabled;
            }
        }
    }

    void RefreshManagedLockers()
    {
        managedLockers.Clear();

        LockerInteractionNew[] lockerManagers = FindObjectsOfType<LockerInteractionNew>(true);
        if (lockerManagers != null && lockerManagers.Length > 1)
        {
            managedLockers.Add(GetOwningLockerTransform());
            return;
        }

        GameObject[] lockerObjects = GameObject.FindGameObjectsWithTag("Locker");
        foreach (GameObject lockerObject in lockerObjects)
        {
            if (lockerObject != null && !managedLockers.Contains(lockerObject.transform))
            {
                managedLockers.Add(lockerObject.transform);
            }
        }

        if (managedLockers.Count == 0)
        {
            managedLockers.Add(GetOwningLockerTransform());
        }

        managedLockers.Sort((a, b) =>
        {
            int nameCompare = string.CompareOrdinal(a.name, b.name);
            if (nameCompare != 0)
            {
                return nameCompare;
            }

            int xCompare = a.position.x.CompareTo(b.position.x);
            if (xCompare != 0)
            {
                return xCompare;
            }

            return a.position.z.CompareTo(b.position.z);
        });
    }

    Transform GetOwningLockerTransform()
    {
        Transform candidate = transform;
        while (candidate != null)
        {
            if (candidate.CompareTag("Locker"))
            {
                return candidate;
            }

            candidate = candidate.parent;
        }

        return transform;
    }

    Transform GetLockerCameraTransform(Transform locker)
    {
        if (locker == null)
        {
            return null;
        }

        if (locker == transform && lockerCameraTransform != null)
        {
            return lockerCameraTransform;
        }

        Transform foundCamera = locker.Find("Camera");
        if (foundCamera != null)
        {
            return foundCamera;
        }

        GameObject fallback = new GameObject($"{locker.name}_LockerCameraRuntime");
        fallback.transform.SetParent(locker, false);
        fallback.transform.localPosition = new Vector3(0f, 1.55f, 0.12f);
        fallback.transform.localRotation = Quaternion.identity;
        return fallback.transform;
    }

    bool IsLockerOccupiedByAnotherClient(int lockerIndex)
    {
        if (!IsNetworkSessionActive() || NetworkManager.Singleton == null)
        {
            return false;
        }

        ulong hiddenClientId = GetHiddenClientIdForLocker(lockerIndex);
        return hiddenClientId != ulong.MaxValue && hiddenClientId != NetworkManager.Singleton.LocalClientId;
    }

    bool IsClientHiddenInAnotherLocker(ulong clientId, int allowedLockerIndex)
    {
        for (int i = 0; i < MaxNetworkedLockers; i++)
        {
            if (i != allowedLockerIndex && GetHiddenClientIdForLocker(i) == clientId)
            {
                return true;
            }
        }

        return false;
    }

    int GetLockerIndexForClient(ulong clientId)
    {
        for (int i = 0; i < MaxNetworkedLockers; i++)
        {
            if (GetHiddenClientIdForLocker(i) == clientId)
            {
                return i;
            }
        }

        return -1;
    }

    ulong GetHiddenClientIdForLocker(int lockerIndex)
    {
        switch (lockerIndex)
        {
            case 0:
                return Locker0HiddenClientId.Value;
            case 1:
                return Locker1HiddenClientId.Value;
            case 2:
                return Locker2HiddenClientId.Value;
            case 3:
                return Locker3HiddenClientId.Value;
            default:
                return ulong.MaxValue;
        }
    }

    void SetHiddenClientIdForLocker(int lockerIndex, ulong clientId)
    {
        switch (lockerIndex)
        {
            case 0:
                Locker0HiddenClientId.Value = clientId;
                break;
            case 1:
                Locker1HiddenClientId.Value = clientId;
                break;
            case 2:
                Locker2HiddenClientId.Value = clientId;
                break;
            case 3:
                Locker3HiddenClientId.Value = clientId;
                break;
        }
    }

    void SyncLegacyHiddenClientId()
    {
        HiddenClientIdNetwork.Value = ulong.MaxValue;
        for (int i = 0; i < MaxNetworkedLockers; i++)
        {
            ulong hiddenClientId = GetHiddenClientIdForLocker(i);
            if (hiddenClientId != ulong.MaxValue)
            {
                HiddenClientIdNetwork.Value = hiddenClientId;
                return;
            }
        }
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
