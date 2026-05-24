using UnityEngine;

public class OxygenSystemSetupHelper : MonoBehaviour
{
    [Header("Quick Setup")]
    [Tooltip("Use context menu to create oxygen UI")]
    public bool setupInstructions = true;

    [ContextMenu("Create UI")]
    void CreateOxygenUI()
    {
        GameObject canvasObj = GameObject.Find("OxygenCanvas");
        if (canvasObj == null)
        {
            canvasObj = new GameObject("OxygenCanvas");
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

        GameObject oxygenBarContainer = CreateOxygenBar(targetCanvas.transform);
        GameObject oxygenText = CreateTextElement(targetCanvas.transform, "OxygenText", 
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20, -20), new Vector2(200, 50), "O2: 100%");
        
        GameObject deathScreen = CreateDeathScreen(targetCanvas.transform);

        OxygenSystem oxygenSystem = GetComponent<OxygenSystem>();
        if (oxygenSystem != null)
        {
            oxygenSystem.oxygenText = oxygenText.GetComponent<TMPro.TextMeshProUGUI>();
            oxygenSystem.oxygenBarFill = oxygenBarContainer.transform.Find("Fill").GetComponent<UnityEngine.UI.Image>();
            oxygenSystem.deathScreen = deathScreen;
        }

        deathScreen.SetActive(false);

        Debug.Log("Oxygen UI created! Now create bunker zones with 'Bunker' tag.");
    }

    GameObject CreateOxygenBar(Transform parent)
    {
        GameObject barContainer = new GameObject("OxygenBarContainer");
        barContainer.transform.SetParent(parent);
        
        RectTransform containerRect = barContainer.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 1f);
        containerRect.anchorMax = new Vector2(0.5f, 1f);
        containerRect.pivot = new Vector2(0.5f, 1f);
        containerRect.anchoredPosition = new Vector2(0, -20);
        containerRect.sizeDelta = new Vector2(400, 30);

        GameObject background = new GameObject("Background");
        background.transform.SetParent(barContainer.transform);
        RectTransform bgRect = background.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        UnityEngine.UI.Image bgImage = background.AddComponent<UnityEngine.UI.Image>();
        bgImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(barContainer.transform);
        RectTransform fillRect = fill.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        UnityEngine.UI.Image fillImage = fill.AddComponent<UnityEngine.UI.Image>();
        fillImage.color = Color.green;
        fillImage.type = UnityEngine.UI.Image.Type.Filled;
        fillImage.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;

        return barContainer;
    }

    GameObject CreateDeathScreen(Transform parent)
    {
        GameObject deathScreen = new GameObject("DeathScreen");
        deathScreen.transform.SetParent(parent, false);
        
        RectTransform rect = deathScreen.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.localScale = Vector3.one;

        UnityEngine.UI.Image bgImage = deathScreen.AddComponent<UnityEngine.UI.Image>();
        bgImage.color = new Color(0, 0, 0, 0.9f);

        GameObject deathText = CreateTextElement(deathScreen.transform, "DeathText", 
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 200), 
            "<b>OXYGEN DEPLETED</b>\n\nYou have suffocated");

        TMPro.TextMeshProUGUI textComponent = deathText.GetComponent<TMPro.TextMeshProUGUI>();
        if (textComponent != null)
        {
            textComponent.fontSize = 36;
            textComponent.color = Color.red;
        }

        return deathScreen;
    }

    GameObject CreateTextElement(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, 
        Vector2 anchoredPos, Vector2 sizeDelta, string defaultText)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent);
        
        RectTransform rectTransform = textObj.AddComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = new Vector2(anchorMax.x, anchorMax.y);
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
