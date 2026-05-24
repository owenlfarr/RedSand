using UnityEngine;
using UnityEngine.Rendering;

public class CompleteLightingFix : MonoBehaviour
{
    [Header("Lighting Settings")]
    [Range(0f, 1f)]
    public float ambientIntensity = 0.15f;
    
    [Header("Material Settings")]
    [Range(0f, 2f)]
    public float normalMapStrength = 0.3f;
    
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
        Debug.Log("=== APPLYING COMPLETE LIGHTING FIX ===");
        
        RenderSettings.ambientIntensity = ambientIntensity;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(ambientIntensity, ambientIntensity, ambientIntensity);
        Debug.Log($"✓ Increased ambient light to {ambientIntensity} (surfaces won't go fully dark)");
        
        RenderSettings.fog = false;
        Debug.Log("✓ Disabled fog");
        
        MeshRenderer[] renderers = FindObjectsOfType<MeshRenderer>();
        int materialCount = 0;
        
        foreach (MeshRenderer renderer in renderers)
        {
            foreach (Material mat in renderer.sharedMaterials)
            {
                if (mat != null && mat.HasProperty("_BumpScale"))
                {
                    if (mat.GetFloat("_BumpScale") > normalMapStrength)
                    {
                        mat.SetFloat("_BumpScale", normalMapStrength);
                        materialCount++;
                    }
                }
            }
        }
        
        if (materialCount > 0)
        {
            Debug.Log($"✓ Reduced normal map strength on {materialCount} materials");
        }
        
        Light[] lights = FindObjectsOfType<Light>();
        foreach (Light light in lights)
        {
            if (light.type == LightType.Spot && light.shadows == LightShadows.Soft)
            {
                light.shadows = LightShadows.Hard;
                Debug.Log($"✓ Fixed {light.name}: Changed to Hard shadows", light);
            }
        }
        
        Debug.Log("=== COMPLETE FIX APPLIED ===");
        Debug.Log("Surfaces should now stay visible from all angles!");
    }
}
