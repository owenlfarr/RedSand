using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class NightSetupHelper : MonoBehaviour
{
    [ContextMenu("Create Night UI Canvas")]
    void CreateNightUICanvas()
    {
        GameObject canvasObj = new GameObject("NightUI_Canvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        
        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject clockPanel = new GameObject("ClockPanel");
        clockPanel.transform.SetParent(canvasObj.transform, false);
        
        Image panelImage = clockPanel.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.7f);
        
        RectTransform panelRect = clockPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1, 1);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.pivot = new Vector2(1, 1);
        panelRect.anchoredPosition = new Vector2(-20, -20);
        panelRect.sizeDelta = new Vector2(200, 80);

        GameObject timeTextObj = new GameObject("TimeText");
        timeTextObj.transform.SetParent(clockPanel.transform, false);
        
        TextMeshProUGUI timeText = timeTextObj.AddComponent<TextMeshProUGUI>();
        timeText.text = "10:00 PM";
        timeText.fontSize = 36;
        timeText.alignment = TextAlignmentOptions.Center;
        timeText.color = Color.white;
        
        RectTransform textRect = timeTextObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        ClockUI clockUI = clockPanel.AddComponent<ClockUI>();
        clockUI.timeText = timeText;

        GameObject cooldownPanel = new GameObject("CooldownPanel");
        cooldownPanel.transform.SetParent(canvasObj.transform, false);
        
        Image cooldownBG = cooldownPanel.AddComponent<Image>();
        cooldownBG.color = new Color(0, 0, 0, 0.7f);
        
        RectTransform cooldownRect = cooldownPanel.GetComponent<RectTransform>();
        cooldownRect.anchorMin = new Vector2(0, 1);
        cooldownRect.anchorMax = new Vector2(0, 1);
        cooldownRect.pivot = new Vector2(0, 1);
        cooldownRect.anchoredPosition = new Vector2(20, -20);
        cooldownRect.sizeDelta = new Vector2(300, 100);

        GameObject cooldownTextObj = new GameObject("CooldownText");
        cooldownTextObj.transform.SetParent(cooldownPanel.transform, false);
        
        TextMeshProUGUI cooldownText = cooldownTextObj.AddComponent<TextMeshProUGUI>();
        cooldownText.text = "";
        cooldownText.fontSize = 24;
        cooldownText.alignment = TextAlignmentOptions.TopLeft;
        cooldownText.color = Color.yellow;
        cooldownText.enableWordWrapping = false;
        cooldownText.overflowMode = TextOverflowModes.Overflow;
        
        RectTransform cooldownTextRect = cooldownTextObj.GetComponent<RectTransform>();
        cooldownTextRect.anchorMin = Vector2.zero;
        cooldownTextRect.anchorMax = Vector2.one;
        cooldownTextRect.offsetMin = new Vector2(10, 10);
        cooldownTextRect.offsetMax = new Vector2(-10, -10);

        DefenseCooldownUI cooldownUI = cooldownPanel.AddComponent<DefenseCooldownUI>();
        cooldownUI.cooldownDisplayText = cooldownText;
        cooldownUI.cooldownContainer = cooldownRect;

        Debug.Log("Night UI Canvas created successfully with centralized cooldown display!");
    }

    [ContextMenu("Create Night Manager")]
    void CreateNightManager()
    {
        GameObject managerObj = new GameObject("NightTimeManager");
        NightTimeManager manager = managerObj.AddComponent<NightTimeManager>();
        
        Debug.Log("NightTimeManager created successfully! Configure the 'Next Scene Name' field.");
    }

    [ContextMenu("Fix Defense Text Wrapping")]
    void FixDefenseTextWrapping()
    {
        DefenseSystem[] defenseSystems = FindObjectsOfType<DefenseSystem>();
        
        foreach (DefenseSystem defense in defenseSystems)
        {
            if (defense.defenseResultsText != null)
            {
                defense.defenseResultsText.enableWordWrapping = true;
                defense.defenseResultsText.overflowMode = TextOverflowModes.Overflow;
                
                RectTransform rect = defense.defenseResultsText.GetComponent<RectTransform>();
                if (rect != null && rect.sizeDelta.x < 400)
                {
                    rect.sizeDelta = new Vector2(500, rect.sizeDelta.y);
                }
            }
        }
        
        Debug.Log($"Fixed text wrapping for {defenseSystems.Length} defense systems!");
    }
}
