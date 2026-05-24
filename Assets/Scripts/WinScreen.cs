using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

public class WinScreen : MonoBehaviour
{
    [Header("Win Screen Settings")]
    [Tooltip("Main win message text")]
    public string winMessage = "YOU BEAT THE EARLY ALPHA DEMO!";

    [Tooltip("Optional subtitle text")]
    public string subtitleMessage = "You survived until 8:00 AM";

    [Tooltip("Time to wait before showing win screen (seconds)")]
    public float delayBeforeShow = 1f;

    [Tooltip("Sound to play when winning")]
    public AudioClip winSound;

    [Tooltip("Volume of win sound (0-1)")]
    [Range(0f, 1f)]
    public float winSoundVolume = 1f;

    [Header("Testing")]
    [Tooltip("Press this key to test the win screen")]
    public KeyCode testKey = KeyCode.F9;

    [Header("Visual Settings")]
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.9f);
    public Color titleColor = Color.white;
    public Color subtitleColor = new Color(0.8f, 0.8f, 0.8f, 1f);

    [Tooltip("Font size for main title")]
    public int titleFontSize = 72;

    [Tooltip("Font size for subtitle")]
    public int subtitleFontSize = 36;

    private Canvas gameCanvas;
    private GameObject winPanel;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI subtitleText;
    private AudioSource audioSource;
    private bool hasShown = false;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    void Update()
    {
        if (hasShown) return;

        if (Input.GetKeyDown(testKey))
        {
            Debug.Log($"<color=cyan>[Win Screen]</color> Test key {testKey} pressed - showing win screen");
            hasShown = true;
            StartCoroutine(ShowWinScreen());
            return;
        }

        // Skip if BunkerEndingSequence is handling the transition.
        bool endingActive = FindObjectOfType<BunkerEndingSequence>() != null;
        if (!endingActive && NightTimeManager.Instance != null && NightTimeManager.Instance.CurrentHour == NightTimeManager.Instance.endHour)
        {
            hasShown = true;
            StartCoroutine(ShowWinScreen());
        }
    }

    IEnumerator ShowWinScreen()
    {
        yield return new WaitForSeconds(delayBeforeShow);

        CreateWinUI();

        if (winPanel != null)
        {
            winPanel.SetActive(true);
        }

        if (winSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(winSound, winSoundVolume);
        }

        Time.timeScale = 0f;

        PlayerController playerController = FindObjectOfType<PlayerController>();
        if (playerController != null)
        {
            playerController.enabled = false;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Debug.Log("<color=green>[Win Screen]</color> Congratulations! You survived the night!");
    }

    void CreateWinUI()
    {
        if (winPanel != null) return;

        gameCanvas = GameObject.Find("GameUICanvas")?.GetComponent<Canvas>();
        if (gameCanvas == null)
        {
            gameCanvas = FindObjectOfType<Canvas>();
        }

        if (gameCanvas == null)
        {
            Debug.LogError("<color=red>[Win Screen]</color> No Canvas found!");
            return;
        }

        winPanel = new GameObject("WinScreenPanel");
        winPanel.transform.SetParent(gameCanvas.transform, false);
        RectTransform panelRect = winPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image bgImage = winPanel.AddComponent<Image>();
        bgImage.color = backgroundColor;

        GameObject titleObj = new GameObject("TitleText");
        titleObj.transform.SetParent(winPanel.transform, false);
        RectTransform titleRect = titleObj.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.6f);
        titleRect.anchorMax = new Vector2(0.5f, 0.6f);
        titleRect.sizeDelta = new Vector2(1400, 200);
        titleRect.anchoredPosition = Vector2.zero;

        titleText = titleObj.AddComponent<TextMeshProUGUI>();
        titleText.text = winMessage;
        titleText.fontSize = titleFontSize;
        titleText.color = titleColor;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.fontStyle = FontStyles.Bold;

        GameObject subtitleObj = new GameObject("SubtitleText");
        subtitleObj.transform.SetParent(winPanel.transform, false);
        RectTransform subtitleRect = subtitleObj.AddComponent<RectTransform>();
        subtitleRect.anchorMin = new Vector2(0.5f, 0.4f);
        subtitleRect.anchorMax = new Vector2(0.5f, 0.4f);
        subtitleRect.sizeDelta = new Vector2(1200, 100);
        subtitleRect.anchoredPosition = Vector2.zero;

        subtitleText = subtitleObj.AddComponent<TextMeshProUGUI>();
        subtitleText.text = subtitleMessage;
        subtitleText.fontSize = subtitleFontSize;
        subtitleText.color = subtitleColor;
        subtitleText.alignment = TextAlignmentOptions.Center;

        winPanel.SetActive(false);

        Debug.Log("<color=green>[Win Screen]</color> Win UI created");
    }

    public void ShowWinScreenManually()
    {
        if (!hasShown)
        {
            hasShown = true;
            StartCoroutine(ShowWinScreen());
        }
    }
}
