using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class SCameraNightVisionInteraction : MonoBehaviour
{
    [Header("Scene Objects")]
    public Camera sCamera;
    public Light nightVisionLight;
    public Transform interactionTarget;

    [Header("Interaction")]
    public float interactionDistance = 4f;
    public KeyCode interactionKey = KeyCode.E;
    public string promptText = "Press E to view";
    public TextMeshProUGUI interactionPromptText;

    [Header("Night Vision")]
    public float nightVisionDuration = 4f;
    public bool onlyRenderForSCamera = true;

    private Transform playerTransform;
    private bool playerInRange;
    private bool ownsPrompt;
    private float nightVisionEndTime = -1f;

    private void OnEnable()
    {
        Camera.onPreCull += HandleCameraPreCull;
        Camera.onPostRender += HandleCameraPostRender;
        RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
    }

    private void Start()
    {
        AutoAssignSceneObjects();
        EnsurePrompt();
        SetNightVisionLight(false);
        HidePrompt();
    }

    private void OnDisable()
    {
        Camera.onPreCull -= HandleCameraPreCull;
        Camera.onPostRender -= HandleCameraPostRender;
        RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
        SetNightVisionLight(false);
        HidePrompt();
    }

    private void Update()
    {
        AutoAssignSceneObjects();
        RefreshPlayerReference();
        UpdateRangeAndPrompt();
        UpdateNightVisionTimer();
    }

    private void AutoAssignSceneObjects()
    {
        if (interactionTarget == null)
        {
            GameObject interactObject = FindObjectByName("interact");
            if (interactObject == null)
            {
                interactObject = FindObjectByName("interatact");
            }

            interactionTarget = interactObject != null ? interactObject.transform : transform;
        }

        if (sCamera == null)
        {
            GameObject cameraObject = FindObjectByName("SCamera");
            if (cameraObject == null)
            {
                cameraObject = FindObjectByName("Scamera");
            }

            if (cameraObject != null)
            {
                sCamera = cameraObject.GetComponentInChildren<Camera>(true);
            }
        }

        if (nightVisionLight == null)
        {
            GameObject lightObject = FindObjectByName("night vision light");
            if (lightObject != null)
            {
                nightVisionLight = lightObject.GetComponentInChildren<Light>(true);
            }
        }

        if (nightVisionLight == null && sCamera != null)
        {
            nightVisionLight = sCamera.GetComponentInChildren<Light>(true);
        }
    }

    private void EnsurePrompt()
    {
        if (interactionPromptText != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("SCameraNightVisionPromptCanvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9000;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject textObject = new GameObject("SCameraNightVisionPromptText");
        textObject.transform.SetParent(canvasObject.transform, false);

        interactionPromptText = textObject.AddComponent<TextMeshProUGUI>();
        interactionPromptText.alignment = TextAlignmentOptions.Center;
        interactionPromptText.fontSize = 28f;
        interactionPromptText.color = Color.white;

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, -120f);
        rect.sizeDelta = new Vector2(500f, 60f);

        ownsPrompt = true;
    }

    private void RefreshPlayerReference()
    {
        if (playerTransform != null && playerTransform.gameObject.activeInHierarchy)
        {
            return;
        }

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        playerTransform = playerObject != null ? playerObject.transform : null;
    }

    private void UpdateRangeAndPrompt()
    {
        playerInRange = playerTransform != null &&
            Vector3.Distance(playerTransform.position, GetInteractionPosition()) <= interactionDistance;

        if (!playerInRange)
        {
            HidePrompt();
            return;
        }

        ShowPrompt(IsNightVisionActive() ? "Viewing..." : promptText);

        if (Input.GetKeyDown(interactionKey))
        {
            nightVisionEndTime = Time.time + nightVisionDuration;
            SetNightVisionLight(!onlyRenderForSCamera || sCamera == null);
        }
    }

    private Vector3 GetInteractionPosition()
    {
        Transform target = interactionTarget != null ? interactionTarget : transform;

        Renderer targetRenderer = target.GetComponent<Renderer>();
        if (targetRenderer != null)
        {
            return targetRenderer.bounds.center;
        }

        Collider targetCollider = target.GetComponent<Collider>();
        if (targetCollider != null)
        {
            return targetCollider.bounds.center;
        }

        return target.position;
    }

    private void UpdateNightVisionTimer()
    {
        if (nightVisionEndTime > 0f && Time.time >= nightVisionEndTime)
        {
            nightVisionEndTime = -1f;
            SetNightVisionLight(false);
        }
    }

    private bool IsNightVisionActive()
    {
        return nightVisionEndTime > Time.time;
    }

    private void HandleCameraPreCull(Camera renderingCamera)
    {
        ApplyCameraScopedLight(renderingCamera);
    }

    private void HandleCameraPostRender(Camera renderingCamera)
    {
        HideAfterSCameraRender(renderingCamera);
    }

    private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
    {
        ApplyCameraScopedLight(renderingCamera);
    }

    private void HandleEndCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
    {
        HideAfterSCameraRender(renderingCamera);
    }

    private void ApplyCameraScopedLight(Camera renderingCamera)
    {
        if (!onlyRenderForSCamera || sCamera == null)
        {
            return;
        }

        SetNightVisionLight(IsNightVisionActive() && renderingCamera == sCamera);
    }

    private void HideAfterSCameraRender(Camera renderingCamera)
    {
        if (!onlyRenderForSCamera || sCamera == null || renderingCamera != sCamera)
        {
            return;
        }

        SetNightVisionLight(false);
    }

    private void SetNightVisionLight(bool active)
    {
        if (nightVisionLight != null)
        {
            nightVisionLight.enabled = active;
        }
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

    private static GameObject FindObjectByName(string objectName)
    {
        GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject sceneObject in objects)
        {
            if (sceneObject == null || !sceneObject.scene.IsValid())
            {
                continue;
            }

            if (string.Equals(sceneObject.name, objectName, System.StringComparison.OrdinalIgnoreCase))
            {
                return sceneObject;
            }
        }

        return null;
    }
}
