using UnityEngine;
using UnityEngine.UI;

public sealed class WatcherStaticEffect : MonoBehaviour
{
    public float Exposure { get; private set; }
    float requested, fadeDuration = 2f, fadeFrom, fadeStarted;
    Texture2D noise;
    Material noiseMaterial;
    Color32[] pixels;
    CanvasGroup group;
    float nextNoise;
    AudioSource audioSource;
    AudioClip generatedCue;
    System.Random random = new System.Random();

    void Awake()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        var obj = new GameObject("Signal noise", typeof(RectTransform), typeof(RawImage));
        obj.transform.SetParent(transform, false);
        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = obj.GetComponent<RawImage>();
        Shader shader = Resources.Load<Shader>("WatcherScreenStatic");
        if (shader != null && shader.isSupported)
        {
            noiseMaterial = new Material(shader);
            image.material = noiseMaterial;
            image.texture = Texture2D.whiteTexture;
        }
        else
        {
            // A smoother fallback for devices that cannot run the procedural shader.
            noise = new Texture2D(640, 360, TextureFormat.RGBA32, false);
            noise.filterMode = FilterMode.Bilinear;
            noise.wrapMode = TextureWrapMode.Repeat;
            pixels = new Color32[noise.width * noise.height];
            image.texture = noise;
        }
        image.raycastTarget = false;
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        group.alpha = 0f;
    }

    public void SetExposure(float value, float fadeSeconds)
    {
        if (value == 0f && requested > 0f) { fadeFrom = Exposure; fadeStarted = Time.unscaledTime; }
        requested = Mathf.Clamp01(value);
        fadeDuration = Mathf.Max(0.01f, fadeSeconds);
    }

    public void PlaySpotCue(AudioClip cue, float volume)
    {
        if (cue == null)
        {
            if (generatedCue == null)
            {
                const int samples = 13230;
                float[] data = new float[samples];
                for (int i = 0; i < samples; i++)
                {
                    float t = i / 22050f;
                    float envelope = Mathf.Sin(Mathf.PI * i / samples);
                    data[i] = (Mathf.Sin(2f * Mathf.PI * (95f * t + 22f * t * t)) * 0.6f + ((float)random.NextDouble() * 2f - 1f) * 0.2f) * envelope;
                }
                generatedCue = AudioClip.Create("Watcher quiet signal", samples, 1, 22050, false);
                generatedCue.SetData(data, 0);
            }
            cue = generatedCue;
        }
        audioSource.PlayOneShot(cue, volume);
    }

    void Update()
    {
        Exposure = requested > 0f ? requested : Mathf.Lerp(fadeFrom, 0f, Mathf.Clamp01((Time.unscaledTime - fadeStarted) / fadeDuration));
        group.alpha = Exposure;
        if (noise == null || Exposure <= 0f || Time.unscaledTime < nextNoise) return;
        nextNoise = Time.unscaledTime + 1f / 18f;
        for (int i = 0; i < pixels.Length; i++)
        {
            byte value = (byte)random.Next(0, 256);
            pixels[i] = new Color32(value, value, value, 255);
        }
        noise.SetPixels32(pixels);
        noise.Apply(false, false);
    }

    void OnDestroy()
    {
        if (noise != null) Destroy(noise);
        if (noiseMaterial != null) Destroy(noiseMaterial);
        if (generatedCue != null) Destroy(generatedCue);
    }
}
