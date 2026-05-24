using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using Networking;

public class SimpleMenuController : MonoBehaviour
{
    [Header("Panels")]
    public GameObject mainMenuPanel;
    public GameObject howToPlayPanel;

    [Header("Fade")]
    public Image blackOverlay;

    [Header("Settings")]
    public string nextSceneName = "MultiplayerLobby";
    public float fadeDuration = 2f;

    private bool hasStarted = false;

    void Start()
    {
        if (blackOverlay != null)
        {
            blackOverlay.color = new Color(0, 0, 0, 0);
        }

        ShowMainMenu();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && howToPlayPanel != null && howToPlayPanel.activeSelf)
        {
            ShowMainMenu();
        }
    }

    public void ShowMainMenu()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (howToPlayPanel != null) howToPlayPanel.SetActive(false);
    }

    public void ShowHowToPlay()
    {
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
        StartCoroutine(FadeAndLoadScene());
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    IEnumerator FadeAndLoadScene()
    {
        if (blackOverlay != null)
        {
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

        SceneManager.LoadScene(nextSceneName);
    }
}
