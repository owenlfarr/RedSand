using UnityEngine;
using UnityEngine.UI;

/// <summary>Applies consistent fit-to-screen scaling to scene and runtime overlays.</summary>
[DefaultExecutionOrder(10000)]
public sealed class ScreenUiLayout : MonoBehaviour
{
    private float nextScan;
    private int lastWidth;
    private int lastHeight;

    private void OnEnable() { ApplyScaling(); }

    private void LateUpdate()
    {
        if (Time.unscaledTime < nextScan && lastWidth == Screen.width && lastHeight == Screen.height) return;
        lastWidth = Screen.width;
        lastHeight = Screen.height;
        nextScan = Time.unscaledTime + 0.25f;
        ApplyScaling();
    }

    private void ApplyScaling()
    {
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // Preserve world-space signs, camera consoles, and inherited child canvases.
            if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) continue;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            // Preserve an authored reference size, including the main-menu composition.
            if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        }
    }
}
