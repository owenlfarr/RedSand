using UnityEngine;

public class FixEmissionOnDiffuse : MonoBehaviour
{
    [Header("Fix Materials")]
    public bool fixNow = false;

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
        Debug.Log("=== FIXING EMISSION ON MATERIALS ===");
        
        Material diffuseMat = Resources.Load<Material>("Assets/_Barking_Dog/3D Free Modular Kit/_Meshes/Materials/Diffuse_01");
        
        if (diffuseMat == null)
        {
            MeshRenderer[] renderers = FindObjectsOfType<MeshRenderer>();
            foreach (MeshRenderer renderer in renderers)
            {
                foreach (Material mat in renderer.sharedMaterials)
                {
                    if (mat != null && mat.name.Contains("Diffuse"))
                    {
                        if (mat.IsKeywordEnabled("_EMISSION"))
                        {
                            mat.DisableKeyword("_EMISSION");
                            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                            Debug.Log($"✓ Disabled emission on: {mat.name}", mat);
                        }
                    }
                }
            }
        }
        
        Debug.Log("=== FIX COMPLETE ===");
        Debug.Log("Diffuse materials should NOT have emission enabled!");
        Debug.Log("In Unity: Select 'Diffuse_01.mat' and UNCHECK the 'Emission' checkbox");
    }
}
