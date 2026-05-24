using UnityEngine;
using UnityEngine.Rendering;

public class FixLightingSettings : MonoBehaviour
{
    [Header("Lighting Fix Options")]
    [Tooltip("Increase ambient intensity slightly")]
    public float ambientIntensity = 0.1f;
    
    [Tooltip("Disable fog completely")]
    public bool disableFog = true;
    
    [Tooltip("Reduce fog density instead of disabling")]
    public float fogDensity = 0.001f;
    
    [Header("Apply Fix")]
    public bool applyNow = false;

    void Start()
    {
        ApplyFix();
    }

    void OnValidate()
    {
        if (applyNow)
        {
            applyNow = false;
            ApplyFix();
        }
    }

    void ApplyFix()
    {
        Debug.Log("=== FIXING LIGHTING SETTINGS ===");
        
        RenderSettings.ambientIntensity = ambientIntensity;
        Debug.Log($"✓ Set ambient intensity to {ambientIntensity} (was 0)");
        
        if (disableFog)
        {
            RenderSettings.fog = false;
            Debug.Log("✓ Disabled fog completely");
        }
        else
        {
            RenderSettings.fogDensity = fogDensity;
            Debug.Log($"✓ Reduced fog density to {fogDensity}");
        }
        
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.1f, 0.1f, 0.1f);
        Debug.Log("✓ Set flat ambient light (dark gray)");
        
        Debug.Log("=== LIGHTING FIX COMPLETE ===");
        Debug.Log("The zero ambient + black fog was causing rendering conflicts!");
    }
}
