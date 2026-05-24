using UnityEngine;

public class FixSpotLights : MonoBehaviour
{
    [Header("Apply the fix")]
    public bool fixNow = false;

    void Start()
    {
        ApplyFix();
    }

    void OnValidate()
    {
        if (fixNow)
        {
            fixNow = false;
            ApplyFix();
        }
    }

    void ApplyFix()
    {
        Light[] allLights = FindObjectsOfType<Light>(true);
        
        int fixedCount = 0;
        
        foreach (Light light in allLights)
        {
            if (light.type == LightType.Spot)
            {
                if (light.shadows == LightShadows.Soft)
                {
                    light.shadows = LightShadows.Hard;
                    Debug.Log($"✓ Fixed {light.name}: Soft shadows → Hard shadows", light);
                    fixedCount++;
                }
                
                if (light.bounceIntensity > 0)
                {
                    light.bounceIntensity = 0;
                    Debug.Log($"✓ Fixed {light.name}: Disabled indirect bounce (only for directional lights)", light);
                }
            }
        }
        
        Debug.Log($"=== FIXED {fixedCount} SPOT LIGHTS ===");
        Debug.Log("Spot lights with SOFT shadows cause rendering errors in Built-in RP!");
    }
}
