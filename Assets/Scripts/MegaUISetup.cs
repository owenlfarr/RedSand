using UnityEngine;
using UnityEngine.UI;

public class MegaUISetup : MonoBehaviour
{
    [Header("Mega UI Setup")]
    [Tooltip("Click the context menu (three dots) and select 'Create All Game UI' to set up everything!")]
    public bool instructions = true;

    [ContextMenu("Create All Game UI")]
    void CreateAllGameUI()
    {
        Debug.Log("=== MEGA UI SETUP STARTED ===");
        
        GameObject mainCanvas = CreateOrGetCanvas("GameUICanvas");
        Canvas canvas = mainCanvas.GetComponent<Canvas>();
        
        CreateClockUI(canvas.transform);
        CreatePowerUI(canvas.transform);
        CreateOxygenUI(canvas.transform);
        CreateRadarUI(canvas.transform);
        CreateDefenseUI(canvas.transform);
        CreateDefenseCooldownUI(canvas.transform);
        
        Debug.Log("=== MEGA UI SETUP COMPLETE ===");
        Debug.Log("All UI elements created and assigned! Check each system component to verify.");
    }

    GameObject CreateOrGetCanvas(string canvasName)
    {
        GameObject canvasObj = GameObject.Find(canvasName);
        if (canvasObj == null)
        {
            canvasObj = new GameObject(canvasName);
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;
            
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            
            canvasObj.AddComponent<GraphicRaycaster>();
            
            Debug.Log($"Created new canvas: {canvasName}");
        }
        else
        {
            Debug.Log($"Using existing canvas: {canvasName}");
        }
        
        return canvasObj;
    }

    void CreateClockUI(Transform canvasTransform)
    {
        Debug.Log("Creating Clock UI...");
        
        GameObject clockText = CreateTextElement(canvasTransform, "ClockText", 
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -70), new Vector2(200, 50), 
            "10:00 PM", TMPro.TextAlignmentOptions.Center);
        
        TMPro.TextMeshProUGUI textComponent = clockText.GetComponent<TMPro.TextMeshProUGUI>();
        if (textComponent != null)
        {
            textComponent.fontSize = 32;
            textComponent.fontStyle = TMPro.FontStyles.Bold;
        }

        ClockUI clockUI = FindObjectOfType<ClockUI>();
        if (clockUI != null)
        {
            clockUI.timeText = textComponent;
            Debug.Log("Clock UI assigned to ClockUI!");
        }
        else
        {
            Debug.LogWarning("ClockUI not found in scene. Create ClockUI component on NightTimeManager.");
        }
    }

    void CreatePowerUI(Transform canvasTransform)
    {
        Debug.Log("Creating Power System UI...");
        
        GameObject powerStatus = CreateTextElement(canvasTransform, "PowerStatusText", 
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20, -20), new Vector2(300, 50), 
            "POWER: ONLINE", TMPro.TextAlignmentOptions.Left);
        
        GameObject interactionPrompt = CreateTextElement(canvasTransform, "PowerBoxPrompt", 
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 100), new Vector2(400, 50), 
            "Press E to restore power", TMPro.TextAlignmentOptions.Center);

        PowerSystem powerSystem = FindObjectOfType<PowerSystem>();
        if (powerSystem != null)
        {
            powerSystem.powerStatusText = powerStatus.GetComponent<TMPro.TextMeshProUGUI>();
            powerSystem.interactionPromptText = interactionPrompt.GetComponent<TMPro.TextMeshProUGUI>();
            Debug.Log("Power UI assigned to PowerSystem!");
        }
        else
        {
            Debug.LogWarning("PowerSystem not found in scene. Create PowerSystem component first.");
        }

        interactionPrompt.SetActive(false);
    }

    void CreateOxygenUI(Transform canvasTransform)
    {
        Debug.Log("Creating Oxygen System UI...");
        
        GameObject oxygenBarContainer = CreateOxygenBar(canvasTransform);
        
        GameObject oxygenText = CreateTextElement(canvasTransform, "OxygenText", 
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20, -20), new Vector2(200, 50), 
            "O2: 100%", TMPro.TextAlignmentOptions.Right);
        
        GameObject deathScreen = CreateDeathScreen(canvasTransform);

        OxygenSystem oxygenSystem = FindObjectOfType<OxygenSystem>();
        if (oxygenSystem != null)
        {
            oxygenSystem.oxygenText = oxygenText.GetComponent<TMPro.TextMeshProUGUI>();
            oxygenSystem.oxygenBarFill = oxygenBarContainer.transform.Find("Fill").GetComponent<Image>();
            oxygenSystem.deathScreen = deathScreen;
            Debug.Log("Oxygen UI assigned to OxygenSystem!");
        }
        else
        {
            Debug.LogWarning("OxygenSystem not found in scene. Create OxygenSystem component first.");
        }

        deathScreen.SetActive(false);
    }

    void CreateRadarUI(Transform canvasTransform)
    {
        Debug.Log("Creating Radar System UI...");
        
        RadarSystem[] radarSystems = FindObjectsOfType<RadarSystem>();
        
        if (radarSystems.Length == 0)
        {
            Debug.LogWarning("RadarSystem not found in scene. Create RadarSystem component first.");
            return;
        }

        for (int i = 0; i < radarSystems.Length; i++)
        {
            string suffix = radarSystems.Length > 1 ? $"_{i}" : "";
            
            GameObject interactionPrompt = CreateTextElement(canvasTransform, $"RadarInteractionPrompt{suffix}", 
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 100), new Vector2(400, 50), 
                "Press E to scan radar", TMPro.TextAlignmentOptions.Center);
            
            TMPro.TextMeshProUGUI promptText = interactionPrompt.GetComponent<TMPro.TextMeshProUGUI>();
            promptText.fontSize = 24;
            promptText.fontStyle = TMPro.FontStyles.Bold;
            promptText.color = Color.cyan;
            
            GameObject radarPanel = CreatePanel(canvasTransform, $"RadarPanel{suffix}", 
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 500), 
                new Color(0, 0.2f, 0.3f, 0.9f));
            
            GameObject titleBG = CreatePanel(radarPanel.transform, "TitleBar",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -25), new Vector2(700, 50),
                new Color(0, 0.4f, 0.6f, 1f));
            
            GameObject titleText = CreateTextElement(titleBG.transform, "Title",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680, 40),
                "RADAR SCAN RESULTS", TMPro.TextAlignmentOptions.Center);
            
            TMPro.TextMeshProUGUI title = titleText.GetComponent<TMPro.TextMeshProUGUI>();
            title.fontSize = 28;
            title.fontStyle = TMPro.FontStyles.Bold;
            title.color = Color.white;
            
            GameObject radarText = CreateTextElement(radarPanel.transform, "RadarResultsText", 
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -25), new Vector2(660, 420), 
                "Scanning for threats...", TMPro.TextAlignmentOptions.Center);
            
            TMPro.TextMeshProUGUI results = radarText.GetComponent<TMPro.TextMeshProUGUI>();
            results.fontSize = 22;
            results.color = Color.cyan;
            results.alignment = TMPro.TextAlignmentOptions.Center;
            
            GameObject cooldownDisplay = CreateTextElement(canvasTransform, $"RadarCooldownText{suffix}", 
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 45 + (i * 25)), new Vector2(300, 40), 
                "", TMPro.TextAlignmentOptions.Center);
            
            TMPro.TextMeshProUGUI cooldown = cooldownDisplay.GetComponent<TMPro.TextMeshProUGUI>();
            cooldown.fontSize = 18;
            cooldown.color = new Color(0f, 0.8f, 1f);
            cooldown.fontStyle = TMPro.FontStyles.Bold;

            radarSystems[i].interactionPromptText = promptText;
            radarSystems[i].radarResultsText = results;
            radarSystems[i].radarPanel = radarPanel;
            radarSystems[i].cooldownText = cooldown;

            radarPanel.SetActive(false);
            interactionPrompt.SetActive(false);
            cooldownDisplay.SetActive(false);
        }
        
        Debug.Log($"Radar UI created for {radarSystems.Length} RadarSystem(s)!");
    }

    void CreateDefenseUI(Transform canvasTransform)
    {
        Debug.Log("Creating Defense System UI...");
        
        DefenseSystem[] defenseSystems = FindObjectsOfType<DefenseSystem>();
        
        if (defenseSystems.Length == 0)
        {
            Debug.LogWarning("DefenseSystem not found in scene. Create DefenseSystem component first.");
            return;
        }

        for (int i = 0; i < defenseSystems.Length; i++)
        {
            string suffix = defenseSystems.Length > 1 ? $"_{i}" : "";
            
            GameObject interactionPrompt = CreateTextElement(canvasTransform, $"DefenseInteractionPrompt{suffix}", 
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 50), new Vector2(400, 50), 
                "Press E to activate defense", TMPro.TextAlignmentOptions.Center);
            
            TMPro.TextMeshProUGUI promptText = interactionPrompt.GetComponent<TMPro.TextMeshProUGUI>();
            promptText.fontSize = 24;
            promptText.fontStyle = TMPro.FontStyles.Bold;
            promptText.color = new Color(1f, 0.5f, 0f);
            
            GameObject defensePanel = CreatePanel(canvasTransform, $"DefensePanel{suffix}", 
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 500), 
                new Color(0.3f, 0.1f, 0f, 0.9f));
            
            GameObject titleBG = CreatePanel(defensePanel.transform, "TitleBar",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -25), new Vector2(700, 50),
                new Color(0.6f, 0.2f, 0f, 1f));
            
            GameObject titleText = CreateTextElement(titleBG.transform, "Title",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680, 40),
                "DEFENSE SYSTEM ACTIVATED", TMPro.TextAlignmentOptions.Center);
            
            TMPro.TextMeshProUGUI title = titleText.GetComponent<TMPro.TextMeshProUGUI>();
            title.fontSize = 28;
            title.fontStyle = TMPro.FontStyles.Bold;
            title.color = Color.white;
            
            GameObject defenseText = CreateTextElement(defensePanel.transform, "DefenseResultsText", 
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -25), new Vector2(660, 420), 
                "Initializing defense protocols...", TMPro.TextAlignmentOptions.Center);
            
            TMPro.TextMeshProUGUI results = defenseText.GetComponent<TMPro.TextMeshProUGUI>();
            results.fontSize = 22;
            results.color = new Color(1f, 0.7f, 0f);
            results.alignment = TMPro.TextAlignmentOptions.Center;
            results.enableWordWrapping = true;
            
            GameObject cooldownDisplay = CreateTextElement(canvasTransform, $"DefenseCooldownText{suffix}", 
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 20 + (i * 25)), new Vector2(300, 40), 
                "", TMPro.TextAlignmentOptions.Center);
            
            TMPro.TextMeshProUGUI cooldown = cooldownDisplay.GetComponent<TMPro.TextMeshProUGUI>();
            cooldown.fontSize = 18;
            cooldown.color = new Color(1f, 0.6f, 0f);
            cooldown.fontStyle = TMPro.FontStyles.Bold;

            defenseSystems[i].interactionPromptText = promptText;
            defenseSystems[i].defenseResultsText = results;
            defenseSystems[i].defensePanel = defensePanel;
            defenseSystems[i].cooldownText = cooldown;

            defensePanel.SetActive(false);
            interactionPrompt.SetActive(false);
            cooldownDisplay.SetActive(false);
        }
        
        Debug.Log($"Defense UI created for {defenseSystems.Length} DefenseSystem(s)!");
    }

    void CreateDefenseCooldownUI(Transform canvasTransform)
    {
        Debug.Log("Creating Defense Cooldown Manager UI...");
        
        GameObject cooldownManager = new GameObject("DefenseCooldownManager");
        cooldownManager.transform.SetParent(canvasTransform);
        
        RectTransform managerRect = cooldownManager.AddComponent<RectTransform>();
        managerRect.anchorMin = new Vector2(0.5f, 0f);
        managerRect.anchorMax = new Vector2(0.5f, 0f);
        managerRect.pivot = new Vector2(0.5f, 0f);
        managerRect.anchoredPosition = new Vector2(0, 120);
        managerRect.sizeDelta = new Vector2(400, 200);
        
        GameObject cooldownText = CreateTextElement(cooldownManager.transform, "AllDefensesCooldownText", 
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(400, 200), 
            "", TMPro.TextAlignmentOptions.Center);
        
        DefenseCooldownUI cooldownUI = cooldownManager.AddComponent<DefenseCooldownUI>();
        cooldownUI.cooldownDisplayText = cooldownText.GetComponent<TMPro.TextMeshProUGUI>();
        cooldownUI.cooldownContainer = managerRect;
        
        Debug.Log("Defense Cooldown Manager created!");
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
        Image bgImage = background.AddComponent<Image>();
        bgImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(barContainer.transform);
        RectTransform fillRect = fill.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(1f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = Color.green;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;

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

        Image bgImage = deathScreen.AddComponent<Image>();
        bgImage.color = new Color(0, 0, 0, 0.9f);

        GameObject deathText = CreateTextElement(deathScreen.transform, "DeathText", 
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 200), 
            "<b>OXYGEN DEPLETED</b>\n\nYou have suffocated", TMPro.TextAlignmentOptions.Center);

        TMPro.TextMeshProUGUI textComponent = deathText.GetComponent<TMPro.TextMeshProUGUI>();
        if (textComponent != null)
        {
            textComponent.fontSize = 36;
            textComponent.color = Color.red;
        }

        return deathScreen;
    }

    GameObject CreateTextElement(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, 
        Vector2 anchoredPos, Vector2 sizeDelta, string defaultText, TMPro.TextAlignmentOptions alignment)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent);
        
        RectTransform rectTransform = textObj.AddComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = new Vector2(anchorMin.x == anchorMax.x ? anchorMin.x : 0.5f, 
                                          anchorMin.y == anchorMax.y ? anchorMax.y : 0.5f);
        rectTransform.anchoredPosition = anchoredPos;
        rectTransform.sizeDelta = sizeDelta;
        
        TMPro.TextMeshProUGUI text = textObj.AddComponent<TMPro.TextMeshProUGUI>();
        text.text = defaultText;
        text.fontSize = 24;
        text.alignment = alignment;
        text.color = Color.white;
        
        return textObj;
    }

    GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, 
        Vector2 anchoredPos, Vector2 sizeDelta, Color bgColor)
    {
        GameObject panelObj = new GameObject(name);
        panelObj.transform.SetParent(parent);
        
        RectTransform rectTransform = panelObj.AddComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPos;
        rectTransform.sizeDelta = sizeDelta;
        
        Image image = panelObj.AddComponent<Image>();
        image.color = bgColor;
        
        return panelObj;
    }
}
