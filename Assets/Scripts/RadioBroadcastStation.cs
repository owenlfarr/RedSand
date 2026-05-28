using Networking;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class RadioBroadcastStation : MonoBehaviour
{
    [Header("Interaction")]
    public float interactionDistance = 4f;
    public LayerMask interactionMask = ~0;
    public TextMeshProUGUI interactionPromptText;

    private const KeyCode InteractionKey = KeyCode.E;
    private const string RadioObjectName = "Radio";
    private VoiceChatManager voiceChatManager;
    private Transform localCameraTransform;
    private bool isLookingAtRadio;
    private bool isBroadcasting;
    private bool ownsPrompt;

    private void Start()
    {
        voiceChatManager = FindObjectOfType<VoiceChatManager>(true);
        EnsureCollider();
        EnsurePrompt();
        HidePrompt();
    }

    private void OnDisable()
    {
        StopBroadcasting();
        HidePrompt();
    }

    private void Update()
    {
        if (!IsRadioCube())
        {
            StopBroadcasting();
            HidePrompt();
            return;
        }

        RefreshLocalCamera();
        CheckLookTarget();
        UpdateInteraction();
    }

    private bool IsRadioCube()
    {
        return string.Equals(gameObject.name, RadioObjectName, System.StringComparison.Ordinal);
    }

    private void EnsureCollider()
    {
        if (GetComponentInChildren<Collider>() != null)
        {
            return;
        }

        gameObject.AddComponent<BoxCollider>();
    }

    private void EnsurePrompt()
    {
        if (interactionPromptText != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("RadioBroadcastPromptCanvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9000;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject textObject = new GameObject("RadioBroadcastPromptText");
        textObject.transform.SetParent(canvasObject.transform, false);

        interactionPromptText = textObject.AddComponent<TextMeshProUGUI>();
        interactionPromptText.alignment = TextAlignmentOptions.Center;
        interactionPromptText.fontSize = 28f;
        interactionPromptText.color = Color.white;
        interactionPromptText.text = "Press E to Speak";

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, -120f);
        rect.sizeDelta = new Vector2(500f, 60f);

        ownsPrompt = true;
    }

    private void RefreshLocalCamera()
    {
        if (localCameraTransform != null && localCameraTransform.gameObject.activeInHierarchy)
        {
            return;
        }

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening &&
            NetworkManager.Singleton.LocalClient?.PlayerObject != null)
        {
            PlayerController player = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
            if (player != null && player.playerCamera != null)
            {
                localCameraTransform = player.playerCamera;
                return;
            }
        }

        if (Camera.main != null)
        {
            localCameraTransform = Camera.main.transform;
        }
    }

    private void CheckLookTarget()
    {
        isLookingAtRadio = false;

        if (localCameraTransform == null)
        {
            return;
        }

        if (!Physics.Raycast(localCameraTransform.position, localCameraTransform.forward, out RaycastHit hit, interactionDistance, interactionMask, QueryTriggerInteraction.Collide))
        {
            return;
        }

        isLookingAtRadio = hit.collider != null &&
            (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform));
    }

    private void UpdateInteraction()
    {
        if (!isLookingAtRadio || !IsRadioCube())
        {
            StopBroadcasting();
            HidePrompt();
            return;
        }

        if (Input.GetKey(InteractionKey))
        {
            ShowPrompt("Broadcasting...");
            StartBroadcasting();
        }
        else
        {
            StopBroadcasting();
            ShowPrompt("Press E to Speak");
        }
    }

    private void StartBroadcasting()
    {
        if (isBroadcasting)
        {
            return;
        }

        if (voiceChatManager == null)
        {
            voiceChatManager = FindObjectOfType<VoiceChatManager>(true);
        }

        voiceChatManager?.BeginRadioBroadcast();
        isBroadcasting = true;
    }

    private void StopBroadcasting()
    {
        if (!isBroadcasting)
        {
            return;
        }

        voiceChatManager?.EndRadioBroadcast();
        isBroadcasting = false;
    }

    private void ShowPrompt(string text)
    {
        if (interactionPromptText == null)
        {
            return;
        }

        interactionPromptText.text = text;
        if (ownsPrompt && interactionPromptText.transform.parent != null)
        {
            interactionPromptText.transform.parent.gameObject.SetActive(true);
        }

        interactionPromptText.gameObject.SetActive(true);
    }

    private void HidePrompt()
    {
        if (interactionPromptText == null)
        {
            return;
        }

        interactionPromptText.gameObject.SetActive(false);

        if (ownsPrompt && interactionPromptText.transform.parent != null)
        {
            interactionPromptText.transform.parent.gameObject.SetActive(false);
        }
    }
}
