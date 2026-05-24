using UnityEngine;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class LockerInteraction : MonoBehaviour
{
    public static LockerInteraction Instance { get; private set; }

    [Header("Interaction Settings")]
    [Tooltip("Distance to interact with locker")]
    public float interactionDistance = 3f;

    [Tooltip("Tag for the locker")]
    public string lockerTag = "Locker";

    [Header("Locker Parts")]
    [Tooltip("The door transform to animate")]
    public Transform lockerDoor;

    [Tooltip("Door open rotation (local Euler angles)")]
    public Vector3 doorOpenRotation = new Vector3(0, -90, 0);

    [Tooltip("Door closed rotation (local Euler angles)")]
    public Vector3 doorClosedRotation = new Vector3(0, 0, 0);

    [Tooltip("Door animation speed")]
    public float doorSpeed = 2f;

    [Header("Hide Position")]
    [Tooltip("Position offset inside locker when hiding (in world space)")]
    public Vector3 hidePositionOffset = new Vector3(0, 0, -0.5f);

    [Header("UI References")]
    public TextMeshProUGUI interactionPromptText;

    [Header("Audio")]
    public AudioClip doorOpenSound;
    public AudioClip doorCloseSound;

    private bool isPlayerNearLocker = false;
    private bool isPlayerHidden = false;
    private Transform playerTransform;
    private CharacterController playerController;
    private Vector3 originalPlayerPosition;
    private bool isDoorOpen = false;
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
            return;
        }

        if (lockerTag != "Locker")
        {
            gameObject.tag = lockerTag;
        }
        else
        {
            gameObject.tag = "Locker";
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;

        if (lockerDoor == null)
        {
            Transform doorChild = transform.Find("door");
            if (doorChild != null)
            {
                lockerDoor = doorChild;
            }
        }
    }

    void Start()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
            playerController = player.GetComponent<CharacterController>();
            Debug.Log($"<color=cyan>[Locker]</color> Found player: {player.name}");
        }
        else
        {
            Debug.LogError("<color=red>[Locker]</color> No player found with tag 'Player'!");
        }

        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning("<color=yellow>[Locker]</color> No interaction prompt text assigned!");
        }

        if (lockerDoor != null)
        {
            lockerDoor.localEulerAngles = doorClosedRotation;
            Debug.Log($"<color=cyan>[Locker]</color> Locker door found: {lockerDoor.name}");
        }
        else
        {
            Debug.LogWarning("<color=yellow>[Locker]</color> No locker door assigned!");
        }

        Debug.Log($"<color=green>[Locker]</color> Locker interaction system ready at position {transform.position}");
    }

    void Update()
    {
        CheckPlayerProximity();

        bool eKeyPressed = false;
        
        #if ENABLE_INPUT_SYSTEM
        eKeyPressed = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
        #else
        eKeyPressed = Input.GetKeyDown(INTERACTION_KEY);
        #endif

        if (eKeyPressed)
        {
            Debug.Log($"<color=yellow>[Locker]</color> E pressed! Near locker: {isPlayerNearLocker}, Hidden: {isPlayerHidden}");
        }

        if (isPlayerHidden && eKeyPressed)
        {
            Debug.Log($"<color=cyan>[Locker]</color> E key pressed while hidden - exiting locker!");
            ExitLocker();
            return;
        }

        if (isPlayerNearLocker && eKeyPressed && !isPlayerHidden)
        {
            Debug.Log($"<color=cyan>[Locker]</color> E key accepted! Triggering interaction...");
            EnterLocker();
        }
    }

    void CheckPlayerProximity()
    {
        if (playerTransform == null) return;

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

    void EnterLocker()
    {
        Debug.Log("<color=cyan>[Locker]</color> Entering locker...");
        
        if (playerController != null)
        {
            playerController.enabled = false;
        }

        originalPlayerPosition = playerTransform.position;

        Vector3 hidePosition = transform.position + hidePositionOffset;
        playerTransform.position = hidePosition;

        isPlayerHidden = true;
        isDoorOpen = false;

        if (doorCloseSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(doorCloseSound);
        }

        HideInteractionPrompt();

        if (playerController != null)
        {
            playerController.enabled = true;
        }

        Debug.Log("<color=green>[Locker]</color> Player is now hiding in locker");
    }

    void ExitLocker()
    {
        Debug.Log("<color=cyan>[Locker]</color> Exiting locker...");
        
        if (playerController != null)
        {
            playerController.enabled = false;
        }

        playerTransform.position = originalPlayerPosition;

        isPlayerHidden = false;
        isDoorOpen = true;

        if (doorOpenSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(doorOpenSound);
        }

        StartCoroutine(CloseDoorAfterDelay(2f));

        if (playerController != null)
        {
            playerController.enabled = true;
        }

        Debug.Log("<color=green>[Locker]</color> Player exited locker");
    }

    System.Collections.IEnumerator CloseDoorAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        isDoorOpen = false;
    }

    void AnimateDoor(Vector3 targetRotation)
    {
        if (lockerDoor == null) return;

        lockerDoor.localRotation = Quaternion.Slerp(
            lockerDoor.localRotation,
            Quaternion.Euler(targetRotation),
            Time.deltaTime * doorSpeed
        );
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

    public bool IsPlayerHidden()
    {
        return isPlayerHidden;
    }

    public void ForceExitLocker()
    {
        if (isPlayerHidden)
        {
            ExitLocker();
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionDistance);
        
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.5f);
    }
}
