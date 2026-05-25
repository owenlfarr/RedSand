using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using Networking;

public class MenuController : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI promptText;
    public Image blackOverlay;
    public GameObject howToPlayPanel;

    [Header("Settings")]
    public string gameSceneName = "Night 1";
    public float fadeDuration = 2f;
    public KeyCode howToPlayKey = KeyCode.H;

    private bool hasStarted = false;
    private bool showingHowToPlay = false;

    void Start()
    {
        if (blackOverlay != null)
        {
            blackOverlay.color = new Color(0, 0, 0, 0);
        }

        if (howToPlayPanel != null)
        {
            howToPlayPanel.SetActive(false);
        }

        if (promptText != null)
        {
            promptText.gameObject.SetActive(true);
        }
    }

    void Update()
    {
        if (hasStarted) return;

        if (Input.GetKeyDown(howToPlayKey))
        {
            ToggleHowToPlay();
        }

        if (Input.GetKeyDown(KeyCode.Space) && !showingHowToPlay)
        {
            StartGame();
        }

        if (Input.GetKeyDown(KeyCode.Escape) && showingHowToPlay)
        {
            ToggleHowToPlay();
        }
    }

    void ToggleHowToPlay()
    {
        showingHowToPlay = !showingHowToPlay;

        if (howToPlayPanel != null)
        {
            howToPlayPanel.SetActive(showingHowToPlay);
        }

        if (promptText != null)
        {
            promptText.gameObject.SetActive(!showingHowToPlay);
        }
    }

    void StartGame()
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

        if (SceneManager.GetActiveScene().name == "MainMenu")
        {
            Debug.LogWarning("[MenuController] No RelayPartyManager found in MainMenu; blocking direct scene load.");
            return;
        }

        hasStarted = true;
        StartCoroutine(FadeAndLoadScene());
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

        SceneManager.LoadScene(gameSceneName);
    }
}
