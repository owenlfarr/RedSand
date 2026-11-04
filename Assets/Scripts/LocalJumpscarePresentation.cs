using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>Victim-local, timed presentation above the death fade. Never changes network/death state.</summary>
public sealed class LocalJumpscarePresentation : MonoBehaviour
{
    Canvas canvas;
    Image image;
    TMP_Text label;
    AudioSource sound;
    Sprite ownedSprite;
    public bool IsShowing { get; private set; }
    public float EndsAt { get; private set; }
    public bool ImageIsVisible => IsShowing && canvas != null && canvas.gameObject.activeInHierarchy && image.color.a >= 0.99f;

    public static LocalJumpscarePresentation Show(Texture2D texture, AudioClip clip, float duration, string message = "", Sprite sprite = null)
    {
        GameObject local = null;
        var manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening)
            local = manager.LocalClient != null && manager.LocalClient.PlayerObject != null ? manager.LocalClient.PlayerObject.gameObject : null;
        else
        {
            foreach (var candidate in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                if (candidate.gameObject.activeInHierarchy) { local = candidate.gameObject; break; }
        }
        if (local == null) return null; // Do not leave a global image behind during scene unload.
        var presentation = local.GetComponent<LocalJumpscarePresentation>();
        if (presentation == null) presentation = local.AddComponent<LocalJumpscarePresentation>();
        if (!presentation.IsShowing) presentation.StartCoroutine(presentation.Present(texture, sprite, clip, duration, message));
        return presentation;
    }

    IEnumerator Present(Texture2D texture, Sprite sprite, AudioClip clip, float duration, string message)
    {
        EnsureCanvas();
        IsShowing = true;
        EndsAt = Time.unscaledTime + Mathf.Max(0.1f, duration);
        if (sprite != null) image.sprite = sprite;
        else if (texture != null)
        {
            ownedSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            image.sprite = ownedSprite;
        }
        else image.sprite = null;
        image.color = image.sprite != null ? Color.white : Color.black;
        label.text = message ?? "";
        label.gameObject.SetActive(!string.IsNullOrEmpty(message));
        canvas.gameObject.SetActive(true);
        if (clip != null) sound.PlayOneShot(clip);
        try
        {
            while (Time.unscaledTime < EndsAt) yield return null;
        }
        finally { Hide(); }
    }

    void EnsureCanvas()
    {
        if (canvas != null) return;
        var obj = new GameObject("Local monster jumpscare", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        // Keep the overlay out of the player/ragdoll hierarchy. Overlay canvases
        // still inherit parent rotation/scale, which makes the scare drift as
        // the dying body tumbles. This component owns cleanup in OnDestroy.
        canvas = obj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 25000; // Above DeathSpectatorOverlay; never fades back in underneath it.
        obj.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        obj.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f, 1080f);
        obj.GetComponent<CanvasScaler>().screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var picture = new GameObject("Image", typeof(RectTransform), typeof(Image));
        picture.transform.SetParent(obj.transform, false);
        image = picture.GetComponent<Image>();
        image.raycastTarget = false;
        var rect = image.rectTransform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var text = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI));
        text.transform.SetParent(obj.transform, false);
        label = text.GetComponent<TextMeshProUGUI>();
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(.5f, .5f);
        label.rectTransform.sizeDelta = new Vector2(1200f, 180f);
        label.rectTransform.anchoredPosition = new Vector2(0f, -320f);
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 40f;
        label.raycastTarget = false;
        label.richText = false;
        sound = gameObject.AddComponent<AudioSource>();
        sound.playOnAwake = false;
        sound.spatialBlend = 0f;
        sound.ignoreListenerPause = true;
    }

    void Hide()
    {
        IsShowing = false;
        if (canvas != null) canvas.gameObject.SetActive(false);
        if (image != null) image.sprite = null;
        if (ownedSprite != null) Destroy(ownedSprite);
        ownedSprite = null;
    }
    void OnDisable() { StopAllCoroutines(); Hide(); }
    void OnDestroy() { if (canvas != null) Destroy(canvas.gameObject); }
}
