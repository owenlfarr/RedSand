using UnityEngine;

public class DefenseSystemSetupHelper : MonoBehaviour
{
    [Header("Quick Setup")]
    [Tooltip("Click the context menu (three dots) and select 'Create Basic UI' to auto-generate UI")]
    public bool setupInstructions = true;

    [ContextMenu("Create Basic UI")]
    void CreateBasicUI()
    {
        GameObject canvasObj = GameObject.Find("DefenseCanvas");
        if (canvasObj == null)
        {
            canvasObj = new GameObject("DefenseCanvas");
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

        GameObject interactionPrompt = CreateTextElement(targetCanvas.transform, "DefenseInteractionPrompt", 
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 100), new Vector2(400, 50), "Press E to activate defense");
        
        GameObject defensePanel = CreatePanel(targetCanvas.transform, "DefensePanel", 
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 400));
        
        GameObject defenseText = CreateTextElement(defensePanel.transform, "DefenseResultsText", 
            new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-40, -40), "Defense results will appear here");
        
        GameObject cooldownDisplay = CreateTextElement(targetCanvas.transform, "DefenseCooldownText", 
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 70), new Vector2(400, 50), "Defense Cooldown: 0s");

        DefenseSystem defense = GetComponent<DefenseSystem>();
        if (defense != null)
        {
            defense.interactionPromptText = interactionPrompt.GetComponent<TMPro.TextMeshProUGUI>();
            defense.defenseResultsText = defenseText.GetComponent<TMPro.TextMeshProUGUI>();
            defense.defensePanel = defensePanel;
            defense.cooldownText = cooldownDisplay.GetComponent<TMPro.TextMeshProUGUI>();
        }

        defensePanel.SetActive(false);

        Debug.Log("Defense UI created successfully! Assign the Objective Transform and 4 Respawn Points in the inspector.");
    }

    [ContextMenu("Create Respawn Points")]
    void CreateRespawnPoints()
    {
        GameObject respawnParent = new GameObject("RespawnPoints");
        respawnParent.transform.position = transform.position;

        Transform[] respawnPoints = new Transform[4];
        Vector3[] offsets = new Vector3[]
        {
            new Vector3(30, 0, 0),
            new Vector3(-30, 0, 0),
            new Vector3(0, 0, 30),
            new Vector3(0, 0, -30)
        };

        for (int i = 0; i < 4; i++)
        {
            GameObject respawnPoint = new GameObject($"RespawnPoint_{i + 1}");
            respawnPoint.transform.SetParent(respawnParent.transform);
            respawnPoint.transform.position = transform.position + offsets[i];
            respawnPoints[i] = respawnPoint.transform;

            GameObject visualIndicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visualIndicator.name = "Visual";
            visualIndicator.transform.SetParent(respawnPoint.transform);
            visualIndicator.transform.localPosition = Vector3.zero;
            visualIndicator.transform.localScale = new Vector3(2, 0.1f, 2);
            
            Renderer renderer = visualIndicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = Color.green;
            }

            Collider collider = visualIndicator.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }
        }

        DefenseSystem defense = GetComponent<DefenseSystem>();
        if (defense != null)
        {
            defense.respawnPoints = respawnPoints;
        }

        Debug.Log("Created 4 respawn points around the defense system. Adjust positions as needed!");
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
        image.color = new Color(0.2f, 0, 0, 0.8f);
        
        return panelObj;
    }
}
