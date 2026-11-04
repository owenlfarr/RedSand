using Networking;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(10000)]
public sealed class ReviveAudioGate : MonoBehaviour
{
    public bool IsSilenced { get; private set; }
    public static bool IsSilencingLocalPlayer => NetworkManager.Singleton != null &&
        NetworkManager.Singleton.GetComponent<ReviveAudioGate>() is ReviveAudioGate gate && gate.IsSilenced;
    NetworkManager manager;
    ReviveSnapshot job;
    float previousVolume;
    int capturedVolumeRevision;
    bool previousPause;
    GameObject canvas;
    TMP_Text countdown;
    VoiceChatManager voice;
    bool serverFinished;
    public static ReviveAudioGate Get(NetworkManager manager)
    {
        var gate = manager.GetComponent<ReviveAudioGate>();
        return gate != null ? gate : manager.gameObject.AddComponent<ReviveAudioGate>();
    }
    void Awake() { manager = GetComponent<NetworkManager>(); voice = GetComponent<VoiceChatManager>(); }
    public void Begin(ReviveSnapshot state)
    {
        if (manager.LocalClientId != state.Target) return;
        if (IsSilenced && job.EndsAt == state.EndsAt) { serverFinished = false; return; }
        manager.LocalClient?.PlayerObject?.GetComponent<PlayerDeathSpectator>()?.ReleaseDeathAudioForRevive();
        if (!IsSilenced) { previousVolume = AudioListener.volume; previousPause = AudioListener.pause; capturedVolumeRevision = PlayerSettingsUI.VolumeRevision; }
        job = state; IsSilenced = true; serverFinished = false;
        voice?.SetReviveMuted(true);
        if (canvas == null) BuildCountdown();
        canvas.SetActive(true);
        ApplySilence();
    }
    void LateUpdate()
    {
        if (!IsSilenced) return;
        if (!manager.IsListening) { End(); return; }
        var player = manager.LocalClient?.PlayerObject;
        bool acknowledged = player != null && player.TryGetComponent<NetworkOxygenState>(out var oxygen) &&
            oxygen.LastReviveCompletedAt.Value >= job.EndsAt;
        if ((serverFinished || acknowledged) && manager.ServerTime.Time >= job.EndsAt && player != null && player.IsSpawned &&
            player.NetworkObjectId != job.OldPlayerObject)
        { End(); return; }
        ApplySilence();
        int seconds = Mathf.Max(0, Mathf.CeilToInt((float)(job.EndsAt - manager.ServerTime.Time)));
        countdown.text = "REVIVAL IN " + seconds + "\nRECONSTRUCTION IN PROGRESS";
    }
    void ApplySilence() { AudioListener.volume = 0f; AudioListener.pause = true; }
    public void ObserveCompletion() { if (IsSilenced) serverFinished = true; }
    void End()
    {
        if (!IsSilenced) return;
        IsSilenced = false;
        if (capturedVolumeRevision != PlayerSettingsUI.VolumeRevision) previousVolume = PlayerSettingsUI.PreferredVolume;
        AudioListener.volume = previousVolume; AudioListener.pause = previousPause;
        voice?.SetReviveMuted(false);
        if (canvas != null) canvas.SetActive(false);
        var player = manager != null && manager.IsListening ? manager.LocalClient?.PlayerObject : null;
        if (player != null && player.NetworkObjectId != job.OldPlayerObject)
            player.GetComponent<PlayerDeathSpectator>()?.ContinueDeathMuteAfterRevive(previousVolume, previousPause);
    }
    void BuildCountdown()
    {
        canvas = new GameObject("Revive countdown (local victim)", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvas.transform.SetParent(transform, false);
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.GetComponent<Canvas>().sortingOrder = 26000;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f);
        var obj = new GameObject("Countdown", typeof(RectTransform), typeof(TextMeshProUGUI)); obj.transform.SetParent(canvas.transform, false);
        countdown = obj.GetComponent<TextMeshProUGUI>();
        countdown.rectTransform.sizeDelta = new Vector2(1000f, 120f); countdown.rectTransform.anchoredPosition = new Vector2(0f, -310f);
        countdown.fontSize = 30f; countdown.alignment = TextAlignmentOptions.Center; countdown.raycastTarget = false;
    }
    void OnDestroy() { End(); if (canvas != null) Destroy(canvas); }
}
