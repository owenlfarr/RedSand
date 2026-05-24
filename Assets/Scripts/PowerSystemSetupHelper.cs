using UnityEngine;

public class PowerSystemSetupHelper : MonoBehaviour
{
    [Header("Quick Setup")]
    [Tooltip("Use context menu options to quickly set up the power system")]
    public bool setupInstructions = true;

    [ContextMenu("Create UI")]
    void CreateBasicUI()
    {
        GameObject canvasObj = GameObject.Find("PowerCanvas");
        if (canvasObj == null)
        {
            canvasObj = new GameObject("PowerCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            
            UnityEngine.UI.CanvasScaler scaler = canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            
            canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        Canvas targetCanvas = canvasObj.GetComponent<Canvas>();

        GameObject powerStatus = CreateTextElement(targetCanvas.transform, "PowerStatusText", 
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20, -20), new Vector2(300, 50), "POWER: ONLINE");
        
        GameObject interactionPrompt = CreateTextElement(targetCanvas.transform, "PowerBoxPrompt", 
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 100), new Vector2(400, 50), "Press E to restore power");

        PowerSystem powerSystem = GetComponent<PowerSystem>();
        if (powerSystem != null)
        {
            powerSystem.powerStatusText = powerStatus.GetComponent<TMPro.TextMeshProUGUI>();
            powerSystem.interactionPromptText = interactionPrompt.GetComponent<TMPro.TextMeshProUGUI>();
        }

        interactionPrompt.SetActive(false);

        Debug.Log("Power System UI created! Assign white lights, red lights, and power box transform.");
    }

    [ContextMenu("Assign Lights from Scene")]
    void AssignLightsFromScene()
    {
        Light[] allLights = FindObjectsOfType<Light>();
        
        System.Collections.Generic.List<Light> whiteLightsList = new System.Collections.Generic.List<Light>();
        System.Collections.Generic.List<Light> redLightsList = new System.Collections.Generic.List<Light>();

        GameObject whiteLightsParent = GameObject.Find("Lights White");
        if (whiteLightsParent != null)
        {
            Light[] whites = whiteLightsParent.GetComponentsInChildren<Light>();
            whiteLightsList.AddRange(whites);
        }

        GameObject redLightsParent = GameObject.Find("lights REd");
        if (redLightsParent != null)
        {
            Light[] reds = redLightsParent.GetComponentsInChildren<Light>();
            redLightsList.AddRange(reds);
        }

        PowerSystem powerSystem = GetComponent<PowerSystem>();
        if (powerSystem != null)
        {
            powerSystem.whiteLights = whiteLightsList.ToArray();
            powerSystem.redLights = redLightsList.ToArray();
            
            Debug.Log($"Assigned {whiteLightsList.Count} white lights and {redLightsList.Count} red lights!");
        }
    }

    GameObject CreateTextElement(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, 
        Vector2 anchoredPos, Vector2 sizeDelta, string defaultText)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent);
        
        RectTransform rectTransform = textObj.AddComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = new Vector2(anchorMin.x, anchorMax.y);
        rectTransform.anchoredPosition = anchoredPos;
        rectTransform.sizeDelta = sizeDelta;
        
        TMPro.TextMeshProUGUI text = textObj.AddComponent<TMPro.TextMeshProUGUI>();
        text.text = defaultText;
        text.fontSize = 24;
        text.alignment = TMPro.TextAlignmentOptions.Center;
        text.color = Color.white;
        
        return textObj;
    }
}
