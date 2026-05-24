using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.SceneManagement;

public class RushMonsterUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject warningPanel;
    public TextMeshProUGUI warningText;
    public GameObject hidePromptPanel;
    public TextMeshProUGUI hidePromptText;
    public GameObject rushDeathScreen;
    public TextMeshProUGUI rushDeathText;

    [Header("Warning Animation")]
    public Color warningColor = Color.white;
    private Coroutine hideTimerCoroutine;

    void Start()
    {
        CreateUIIfNeeded();

        if (warningPanel != null) warningPanel.SetActive(false);
        if (hidePromptPanel != null) hidePromptPanel.SetActive(false);
        if (rushDeathScreen != null) rushDeathScreen.SetActive(false);
    }

    void CreateUIIfNeeded()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("<color=red>[Rush UI]</color> No canvas found!");
            return;
        }

        Transform canvasTransform = canvas.transform;

        if (warningPanel == null)
        {
            warningPanel = CreateWarningPanel(canvasTransform);
        }

        if (hidePromptPanel == null)
        {
            hidePromptPanel = CreateHidePromptPanel(canvasTransform);
        }

        if (rushDeathScreen == null)
        {
            rushDeathScreen = CreateRushDeathScreen(canvasTransform);
        }
    }

    GameObject CreateWarningPanel(Transform parent)
    {
        GameObject panel = new GameObject("RushWarningPanel");
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.85f);
        rect.anchorMax = new Vector2(0.5f, 0.85f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(400, 50);

        GameObject textObj = new GameObject("WarningText");
        textObj.transform.SetParent(panel.transform, false);

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = Vector2.zero;

        warningText = textObj.AddComponent<TextMeshProUGUI>();
        warningText.text = "something is coming...";
        warningText.fontSize = 18;
        warningText.alignment = TextAlignmentOptions.Center;
        warningText.color = Color.white;
        warningText.fontStyle = FontStyles.Normal;

        return panel;
    }

    GameObject CreateHidePromptPanel(Transform parent)
    {
        GameObject panel = new GameObject("RushHidePromptPanel");
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(600, 150);

        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0.1f, 0, 0, 0.9f);

        GameObject textObj = new GameObject("HidePromptText");
        textObj.transform.SetParent(panel.transform, false);

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(-20, -20);

        hidePromptText = textObj.AddComponent<TextMeshProUGUI>();
        hidePromptText.text = "HIDE IN THE LOCKER!\nTIME REMAINING: 5.0s";
        hidePromptText.fontSize = 32;
        hidePromptText.alignment = TextAlignmentOptions.Center;
        hidePromptText.color = Color.white;
        hidePromptText.fontStyle = FontStyles.Bold;

        return panel;
    }

    GameObject CreateRushDeathScreen(Transform parent)
    {
        GameObject deathScreen = new GameObject("RushDeathScreen");
        deathScreen.transform.SetParent(parent, false);

        RectTransform rect = deathScreen.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;

        Image bg = deathScreen.AddComponent<Image>();
        bg.color = new Color(0.1f, 0, 0, 0.95f);

        GameObject textObj = new GameObject("RushDeathText");
        textObj.transform.SetParent(deathScreen.transform, false);

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(800, 300);

        rushDeathText = textObj.AddComponent<TextMeshProUGUI>();
        rushDeathText.text = "<b>YOU WERE CAUGHT</b>\n\nThe entity found you...\n\n<size=24>Press R to restart</size>";
        rushDeathText.fontSize = 42;
        rushDeathText.alignment = TextAlignmentOptions.Center;
        rushDeathText.color = Color.red;
        rushDeathText.fontStyle = FontStyles.Bold;

        return deathScreen;
    }

    void Update()
    {
        if (rushDeathScreen != null && rushDeathScreen.activeSelf)
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                RestartLevel();
            }
        }
    }

    public void ShowWarning(string message)
    {
        if (warningPanel != null && warningText != null)
        {
            warningText.text = message;
            warningPanel.SetActive(true);
        }
    }

    public void HideWarning()
    {
        if (warningPanel != null)
        {
            warningPanel.SetActive(false);
        }
    }

    public void ShowHidePrompt(float timeLimit)
    {
        if (hidePromptPanel != null)
        {
            hidePromptPanel.SetActive(true);

            if (hideTimerCoroutine != null)
            {
                StopCoroutine(hideTimerCoroutine);
            }

            hideTimerCoroutine = StartCoroutine(UpdateHideTimer(timeLimit));
        }
    }

    public void HideHidePrompt()
    {
        if (hidePromptPanel != null)
        {
            hidePromptPanel.SetActive(false);
        }

        if (hideTimerCoroutine != null)
        {
            StopCoroutine(hideTimerCoroutine);
            hideTimerCoroutine = null;
        }
    }

    IEnumerator UpdateHideTimer(float timeLimit)
    {
        float elapsed = 0f;

        while (elapsed < timeLimit)
        {
            float remaining = timeLimit - elapsed;

            if (hidePromptText != null)
            {
                hidePromptText.text = $"HIDE IN THE LOCKER!\nTIME REMAINING: {remaining:F1}s";

                if (remaining <= 2f)
                {
                    hidePromptText.color = Color.red;
                }
                else if (remaining <= 3f)
                {
                    hidePromptText.color = Color.yellow;
                }
            }

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    public void ShowDeathScreen()
    {
        if (rushDeathScreen != null)
        {
            rushDeathScreen.SetActive(true);
        }
    }

    void RestartLevel()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
