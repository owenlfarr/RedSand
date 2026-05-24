using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using Networking;

public class MainMenuController : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Main menu panel")]
    public GameObject mainMenuPanel;

    [Tooltip("How to play panel")]
    public GameObject howToPlayPanel;

    [Tooltip("Text prompt at bottom of screen")]
    public TextMeshProUGUI promptText;

    [Tooltip("Black overlay image for fade effect")]
    public Image blackOverlay;

    [Header("Settings")]
    [Tooltip("Scene to load after fade (can be scene name or build index)")]
    public string nextSceneName = "MultiplayerLobby";

    [Tooltip("How long the fade to black takes")]
    public float fadeDuration = 2f;

    [Tooltip("How fast the text fades out")]
    public float textFadeDuration = 1f;

    private bool hasStarted = false;
    private bool showingHowToPlay = false;

    void Start()
    {
        if (blackOverlay != null)
        {
            blackOverlay.color = new Color(0, 0, 0, 0);
        }

        ShowMainMenu();

        Debug.Log("<color=cyan>[Main Menu]</color> Menu initialized");
    }

    void Update()
    {
        if (showingHowToPlay && Input.GetKeyDown(KeyCode.Escape))
        {
            ShowMainMenu();
        }
    }

    public void ShowMainMenu()
    {
        showingHowToPlay = false;
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (howToPlayPanel != null) howToPlayPanel.SetActive(false);
    }

    public void ShowHowToPlay()
    {
        showingHowToPlay = true;
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (howToPlayPanel != null) howToPlayPanel.SetActive(true);
    }

    public void StartGame()
    {
        if (hasStarted) return;
        RelayPartyManager relay = FindObjectOfType<RelayPartyManager>();
        if (relay != null)
        {
            if (relay.HasActiveHostParty)
            {
                hasStarted = true;
                relay.StartMatch();
            }
            else
            {
                _ = relay.CreateParty();
            }
            return;
        }
        hasStarted = true;
        StartCoroutine(StartGameSequence());
    }

    public void QuitGame()
    {
        Debug.Log("<color=cyan>[Main Menu]</color> Quitting game");
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    IEnumerator StartGameSequence()
    {
        Debug.Log("<color=green>[Main Menu]</color> Starting game sequence");

        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);

        yield return StartCoroutine(FadeToBlack());

        Debug.Log($"<color=cyan>[Main Menu]</color> Loading scene: {nextSceneName}");
        SceneManager.LoadScene(nextSceneName);
    }

    IEnumerator FadeToBlack()
    {
        if (blackOverlay == null) yield break;

        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(0f, 1f, elapsed / fadeDuration);
            blackOverlay.color = new Color(0, 0, 0, alpha);
            yield return null;
        }

        blackOverlay.color = Color.black;
    }
}
