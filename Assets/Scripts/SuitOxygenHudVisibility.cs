using UnityEngine;
using UnityEngine.UI;

/// <summary>Presentation only: keeps the existing oxygen UI updating while hidden.</summary>
public sealed class SuitOxygenHudVisibility : MonoBehaviour
{
    [SerializeField] private Image oxygenFill;
    [SerializeField] private RectTransform[] suitElements;
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.35f;
    private CanvasGroup[] groups;
    private float opacity;

    private void Awake()
    {
        groups = new CanvasGroup[suitElements.Length];
        for (int i = 0; i < suitElements.Length; i++)
        {
            if (suitElements[i] == null) continue;
            CanvasGroup group = suitElements[i].GetComponent<CanvasGroup>();
            if (group == null) group = suitElements[i].gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            groups[i] = group;
        }
    }

    private void LateUpdate()
    {
        // The existing oxygen system owns this fill on both host and client.
        // Read its exact full state rather than rounding the displayed percentage.
        float target = oxygenFill != null && oxygenFill.fillAmount < 1f ? 1f : 0f;
        opacity = Mathf.MoveTowards(opacity, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeDuration));
        foreach (CanvasGroup group in groups)
            if (group != null) group.alpha = opacity;
    }
}
