using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.PostProcessing;
using System.Collections;

public class GameIntro : MonoBehaviour
{
    [Header("Intro Settings")]
    [Tooltip("Duration of black screen fade in seconds")]
    public float blackFadeDuration = 2f;
    
    [Tooltip("Duration of blur unblur in seconds")]
    public float blurDuration = 3f;
    
    [Tooltip("Maximum blur intensity at start")]
    public float maxBlurIntensity = 5f;

    [Header("References")]
    public PostProcessVolume postProcessVolume;
    public Image blackScreenImage;

    private DepthOfField depthOfField;
    private bool introComplete = false;

    void Start()
    {
        if (blackScreenImage == null)
        {
            GameObject canvas = GameObject.Find("GameUICanvas");
            if (canvas != null)
            {
                CreateBlackScreen(canvas.transform);
            }
        }

        if (postProcessVolume != null && postProcessVolume.profile.TryGetSettings(out depthOfField))
        {
            depthOfField.active = true;
        }

        StartCoroutine(PlayIntroSequence());
    }

    void CreateBlackScreen(Transform canvasTransform)
    {
        GameObject blackScreenObj = new GameObject("IntroBlackScreen");
        blackScreenObj.transform.SetParent(canvasTransform, false);
        blackScreenObj.transform.SetAsLastSibling();

        RectTransform rect = blackScreenObj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.localScale = Vector3.one;

        blackScreenImage = blackScreenObj.AddComponent<Image>();
        blackScreenImage.color = Color.black;
        blackScreenImage.raycastTarget = false;

        Debug.Log("<color=cyan>[Intro]</color> Black screen created and stretched to fill screen");
    }

    IEnumerator PlayIntroSequence()
    {
        if (blackScreenImage != null)
        {
            blackScreenImage.color = Color.black;
        }

        if (depthOfField != null)
        {
            depthOfField.focusDistance.value = 0.1f;
            depthOfField.aperture.value = maxBlurIntensity;
        }

        yield return new WaitForSeconds(1f);

        float elapsedTime = 0f;
        while (elapsedTime < blackFadeDuration)
        {
            elapsedTime += Time.deltaTime;
            float alpha = 1f - (elapsedTime / blackFadeDuration);

            if (blackScreenImage != null)
            {
                Color color = blackScreenImage.color;
                color.a = alpha;
                blackScreenImage.color = color;
            }

            yield return null;
        }

        if (blackScreenImage != null)
        {
            blackScreenImage.gameObject.SetActive(false);
        }

        elapsedTime = 0f;
        while (elapsedTime < blurDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / blurDuration;
            float blurAmount = Mathf.Lerp(maxBlurIntensity, 0f, progress);

            if (depthOfField != null)
            {
                depthOfField.aperture.value = blurAmount;
            }

            yield return null;
        }

        if (depthOfField != null)
        {
            depthOfField.active = false;
        }

        introComplete = true;
        Debug.Log("<color=green>[Intro]</color> Intro sequence complete");
    }

    public bool IsIntroComplete()
    {
        return introComplete;
    }
}
