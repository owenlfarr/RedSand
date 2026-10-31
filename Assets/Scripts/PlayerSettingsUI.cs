using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Local display/audio preferences and persistent FPS display.</summary>
public sealed class PlayerSettingsUI : MonoBehaviour
{
    private const string Prefix = "RedSand.Settings.";
    private static PlayerSettingsUI instance;
    private readonly List<Vector2Int> resolutions = new List<Vector2Int>();
    private readonly int[] caps = { -1, 60, 90, 120, 144, 165, 240 };
    private readonly FullScreenMode[] modes = { FullScreenMode.FullScreenWindow, FullScreenMode.Windowed, FullScreenMode.ExclusiveFullScreen };
    private readonly string[] modeNames = { "BORDERLESS", "WINDOWED", "FULLSCREEN" };
    private RectTransform panel;
    private TMP_FontAsset font;
    private TMP_Text resolutionValue, modeValue, vsyncValue, capValue, fpsValue, volumeValue, notice, applyText, fpsText;
    private Button applyButton;
    private int selectedResolution, selectedMode, capIndex;
    private bool showFps, awaitingConfirmation;
    private float confirmDeadline, sampleTime;
    private int frameCount, previousWidth, previousHeight;
    private FullScreenMode previousMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (instance != null) return;
        instance = new GameObject("Local Player Settings").AddComponent<PlayerSettingsUI>();
        DontDestroyOnLoad(instance.gameObject);
        instance.LoadPreferences();
    }

    private void LoadPreferences()
    {
        foreach (Resolution r in Screen.resolutions)
        {
            Vector2Int size = new Vector2Int(r.width, r.height);
            if (!resolutions.Contains(size)) resolutions.Add(size);
        }
        Vector2Int current = new Vector2Int(Screen.width, Screen.height);
        if (!resolutions.Contains(current)) resolutions.Add(current);
        resolutions.Sort((a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        selectedResolution = resolutions.IndexOf(current);
        selectedMode = System.Array.IndexOf(modes, Screen.fullScreenMode);
        if (selectedMode < 0) selectedMode = 0;
        QualitySettings.vSyncCount = PlayerPrefs.GetInt(Prefix + "VSync", QualitySettings.vSyncCount) > 0 ? 1 : 0;
        int cap = PlayerPrefs.GetInt(Prefix + "FrameCap", -1);
        capIndex = System.Array.IndexOf(caps, cap);
        if (capIndex < 0) capIndex = 0;
        Application.targetFrameRate = caps[capIndex];
        AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "Volume", 1f));
        showFps = PlayerPrefs.GetInt(Prefix + "FPS", 0) != 0;
        if (PlayerPrefs.HasKey(Prefix + "Width"))
        {
            Vector2Int saved = new Vector2Int(PlayerPrefs.GetInt(Prefix + "Width"), PlayerPrefs.GetInt(Prefix + "Height"));
            int index = resolutions.IndexOf(saved);
            if (index >= 0)
            {
                selectedResolution = index;
                selectedMode = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "Mode", 0), 0, modes.Length - 1);
                Screen.SetResolution(saved.x, saved.y, modes[selectedMode]);
            }
        }
        CreateFpsOverlay();
    }

    public static void Extend(GameObject settingsPanel)
    {
        if (instance == null) Initialize();
        if (settingsPanel.transform.Find("DisplayOptions") != null) return;
        instance.Build(settingsPanel);
    }

    private void Build(GameObject settingsPanel)
    {
        panel = settingsPanel.GetComponent<RectTransform>();
        panel.sizeDelta = new Vector2(720f, 860f);
        TMP_Text existing = settingsPanel.GetComponentInChildren<TMP_Text>(true);
        font = existing != null ? existing.font : TMP_Settings.defaultFontAsset;
        MoveExisting("Title", new Vector2(0f, 382f), new Vector2(640f, 52f));
        MoveExisting("SensitivityLabel", new Vector2(-95f, 300f), new Vector2(390f, 36f));
        MoveExisting("SensitivityValue", new Vector2(240f, 300f), new Vector2(110f, 36f));
        MoveExisting("SensitivitySlider", new Vector2(0f, 250f), new Vector2(580f, 32f));
        MoveExisting("SettingsBackButton", new Vector2(0f, -370f), new Vector2(220f, 44f));
        GameObject content = new GameObject("DisplayOptions", typeof(RectTransform));
        content.transform.SetParent(panel, false);
        RectTransform root = content.GetComponent<RectTransform>();
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
        root.sizeDelta = panel.sizeDelta;
        Text(root, "Hint", "CLICK AN OPTION TO CHANGE IT", new Vector2(0f, 200f), new Vector2(600f, 25f), 13f, TextAlignmentOptions.Center);
        resolutionValue = Row(root, "RESOLUTION", 150f, CycleResolution);
        modeValue = Row(root, "DISPLAY MODE", 90f, () => { if (awaitingConfirmation) return; selectedMode = (selectedMode + 1) % modes.Length; Refresh(); });
        vsyncValue = Row(root, "VSYNC", 30f, () => { QualitySettings.vSyncCount = QualitySettings.vSyncCount == 0 ? 1 : 0; SaveInt("VSync", QualitySettings.vSyncCount); Refresh(); });
        capValue = Row(root, "FPS LIMIT", -30f, () => { capIndex = (capIndex + 1) % caps.Length; Application.targetFrameRate = caps[capIndex]; SaveInt("FrameCap", caps[capIndex]); Refresh(); });
        fpsValue = Row(root, "FPS COUNTER", -90f, () => { showFps = !showFps; SaveInt("FPS", showFps ? 1 : 0); Refresh(); });
        Text(root, "VolumeLabel", "GAME VOLUME", new Vector2(-150f, -150f), new Vector2(280f, 32f), 18f, TextAlignmentOptions.Left);
        volumeValue = Text(root, "VolumeValue", "", new Vector2(240f, -150f), new Vector2(100f, 32f), 18f, TextAlignmentOptions.Right);
        // Clone the already configured slider so its navigation, handle and fill remain familiar.
        Slider source = settingsPanel.transform.Find("SensitivitySlider").GetComponent<Slider>();
        Slider volume = Instantiate(source, root);
        volume.name = "GameVolumeSlider";
        volume.onValueChanged = new Slider.SliderEvent();
        volume.minValue = 0f; volume.maxValue = 1f;
        Rect(volume.gameObject, new Vector2(0f, -196f), new Vector2(580f, 30f));
        volume.SetValueWithoutNotify(AudioListener.volume);
        volume.onValueChanged.AddListener(value => { AudioListener.volume = value; PlayerPrefs.SetFloat(Prefix + "Volume", value); PlayerPrefs.Save(); Refresh(); });
        notice = Text(root, "DisplayNotice", "", new Vector2(0f, -260f), new Vector2(640f, 55f), 14f, TextAlignmentOptions.Center);
        applyButton = Button(root, "ApplyDisplay", new Vector2(0f, -320f), new Vector2(320f, 44f), ApplyDisplay, out applyText);
        Refresh();
    }

    private void MoveExisting(string name, Vector2 position, Vector2 size)
    {
        Transform child = panel.Find(name);
        if (child != null) Rect(child.gameObject, position, size);
    }

    private TMP_Text Row(Transform root, string label, float y, UnityEngine.Events.UnityAction action)
    {
        Text(root, label + "Label", label, new Vector2(-150f, y), new Vector2(280f, 34f), 18f, TextAlignmentOptions.Left);
        TMP_Text value;
        Button(root, label + "Button", new Vector2(160f, y), new Vector2(270f, 42f), action, out value);
        return value;
    }

    private Button Button(Transform root, string name, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action, out TMP_Text label)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        obj.transform.SetParent(root, false); Rect(obj, position, size);
        Image image = obj.GetComponent<Image>(); image.color = new Color(.12f, .085f, .055f, 1f);
        Button button = obj.GetComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
        ColorBlock colors = button.colors; colors.highlightedColor = new Color(1f, .75f, .45f); colors.pressedColor = new Color(.7f, .45f, .25f); button.colors = colors;
        label = Text(obj.transform, "Value", "", Vector2.zero, size - new Vector2(12f, 4f), 16f, TextAlignmentOptions.Center);
        return button;
    }

    private TMP_Text Text(Transform root, string name, string value, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform)); obj.transform.SetParent(root, false);
        Rect(obj, position, size);
        TextMeshProUGUI text = obj.AddComponent<TextMeshProUGUI>();
        text.font = font != null ? font : TMP_Settings.defaultFontAsset;
        text.text = value; text.fontSize = fontSize; text.alignment = alignment;
        text.color = new Color(1f, .68f, .36f); text.raycastTarget = false;
        return text;
    }

    private static void Rect(GameObject obj, Vector2 position, Vector2 size)
    {
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size;
    }

    private void CycleResolution()
    {
        if (awaitingConfirmation) return;
        selectedResolution = (selectedResolution + 1) % resolutions.Count; Refresh();
    }

    private void ApplyDisplay()
    {
        if (awaitingConfirmation)
        {
            awaitingConfirmation = false;
            Vector2Int size = resolutions[selectedResolution];
            PlayerPrefs.SetInt(Prefix + "Width", size.x); PlayerPrefs.SetInt(Prefix + "Height", size.y);
            SaveInt("Mode", selectedMode); Refresh(); return;
        }
        previousWidth = Screen.width; previousHeight = Screen.height; previousMode = Screen.fullScreenMode;
        Vector2Int target = resolutions[selectedResolution];
        Screen.SetResolution(target.x, target.y, modes[selectedMode]);
        awaitingConfirmation = true; confirmDeadline = Time.unscaledTime + 15f; Refresh();
    }

    private void RevertDisplay()
    {
        Screen.SetResolution(previousWidth, previousHeight, previousMode);
        selectedResolution = resolutions.IndexOf(new Vector2Int(previousWidth, previousHeight));
        if (selectedResolution < 0) { resolutions.Add(new Vector2Int(previousWidth, previousHeight)); selectedResolution = resolutions.Count - 1; }
        selectedMode = System.Array.IndexOf(modes, previousMode); if (selectedMode < 0) selectedMode = 0;
        awaitingConfirmation = false; Refresh();
    }

    private void Refresh()
    {
        if (resolutionValue == null) return;
        Vector2Int size = resolutions[selectedResolution]; resolutionValue.text = size.x + " x " + size.y;
        modeValue.text = modeNames[selectedMode]; vsyncValue.text = QualitySettings.vSyncCount > 0 ? "ON" : "OFF";
        capValue.text = (caps[capIndex] < 0 ? "UNLIMITED" : caps[capIndex].ToString()) + (QualitySettings.vSyncCount > 0 ? " (VSYNC)" : "");
        fpsValue.text = showFps ? "ON" : "OFF"; volumeValue.text = Mathf.RoundToInt(AudioListener.volume * 100f) + "%";
        applyText.text = awaitingConfirmation ? "KEEP DISPLAY CHANGES" : "APPLY DISPLAY";
        notice.text = awaitingConfirmation ? "Confirm display changes within 15 seconds." : "Display changes require Apply. Other options save immediately.\nVSync takes priority over the FPS limit.";
    }

    private void Update()
    {
        if (awaitingConfirmation)
        {
            if (panel == null || !panel.gameObject.activeInHierarchy || Time.unscaledTime >= confirmDeadline || Input.GetKeyDown(KeyCode.Escape)) RevertDisplay();
            else if (notice != null) notice.text = "KEEP CHANGES? Reverting in " + Mathf.CeilToInt(confirmDeadline - Time.unscaledTime) + "s\nPress Escape to revert.";
        }
        if (fpsText == null) return;
        fpsText.gameObject.SetActive(showFps);
        sampleTime += Time.unscaledDeltaTime; frameCount++;
        if (sampleTime >= .5f)
        {
            fpsText.text = Mathf.RoundToInt(frameCount / sampleTime) + " FPS";
            sampleTime = 0f; frameCount = 0;
        }
    }

    private void CreateFpsOverlay()
    {
        GameObject obj = new GameObject("FPS Overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        obj.transform.SetParent(transform, false);
        Canvas canvas = obj.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
        CanvasScaler scaler = obj.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        fpsText = Text(obj.transform, "FPS", "-- FPS", Vector2.zero, new Vector2(140f, 20f), 14f, TextAlignmentOptions.Right);
        RectTransform rect = fpsText.rectTransform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f); rect.anchoredPosition = new Vector2(-24f, -8f);
        fpsText.gameObject.SetActive(showFps);
    }

    private static void SaveInt(string key, int value) { PlayerPrefs.SetInt(Prefix + key, value); PlayerPrefs.Save(); }
    private void OnApplicationQuit() { if (awaitingConfirmation) RevertDisplay(); PlayerPrefs.Save(); }
}
