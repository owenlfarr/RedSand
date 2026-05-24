using UnityEngine;

public class RadarSystemSetupHelper : MonoBehaviour
{
    [Header("Quick Setup")]
    [Tooltip("Click the context menu (three dots) and select 'Create Basic UI' to auto-generate UI")]
    public bool setupInstructions = true;

    [ContextMenu("Create Basic UI")]
    void CreateBasicUI()
    {
        GameObject canvasObj = GameObject.Find("RadarCanvas");
        if (canvasObj == null)
        {
            canvasObj = new GameObject("RadarCanvas");
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

        GameObject interactionPrompt = CreateTextElement(targetCanvas.transform, "InteractionPrompt", 
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 100), new Vector2(400, 50), "Press E to scan");
        
        GameObject radarPanel = CreatePanel(targetCanvas.transform, "RadarPanel", 
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 400));
        
        GameObject radarText = CreateTextElement(radarPanel.transform, "RadarResultsText", 
            new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-40, -40), "Scan results will appear here");
        
        GameObject cooldownDisplay = CreateTextElement(targetCanvas.transform, "CooldownText", 
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 20), new Vector2(300, 50), "Cooldown: 0s");

        RadarSystem radar = GetComponent<RadarSystem>();
        if (radar != null)
        {
            radar.interactionPromptText = interactionPrompt.GetComponent<TMPro.TextMeshProUGUI>();
            radar.radarResultsText = radarText.GetComponent<TMPro.TextMeshProUGUI>();
            radar.radarPanel = radarPanel;
            radar.cooldownText = cooldownDisplay.GetComponent<TMPro.TextMeshProUGUI>();
        }

        radarPanel.SetActive(false);

        Debug.Log("Radar UI created successfully! Assign the Objective Transform in the inspector.");
    }

    GameObject CreateTextElement(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, 
        Vector2 anchoredPos, Vector2 sizeDelta, string defaultText)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent);
        
        RectTransform rectTransform = textObj.AddComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPos;
        rectTransform.sizeDelta = sizeDelta;
        
        TMPro.TextMeshProUGUI text = textObj.AddComponent<TMPro.TextMeshProUGUI>();
        text.text = defaultText;
        text.fontSize = 24;
        text.alignment = TMPro.TextAlignmentOptions.Center;
        text.color = Color.white;
        
        return textObj;
    }

    GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, 
        Vector2 anchoredPos, Vector2 sizeDelta)
    {
        GameObject panelObj = new GameObject(name);
        panelObj.transform.SetParent(parent);
        
        RectTransform rectTransform = panelObj.AddComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPos;
        rectTransform.sizeDelta = sizeDelta;
        
        UnityEngine.UI.Image image = panelObj.AddComponent<UnityEngine.UI.Image>();
        image.color = new Color(0, 0, 0, 0.8f);
        
        return panelObj;
    }
}
