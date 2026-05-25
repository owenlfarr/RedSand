using UnityEngine;

[ExecuteAlways]
public class SendSunDirectionToAtmosphere : MonoBehaviour
{
    public Light sun;
    public Material atmosphereMaterial;

    void Update()
    {
        if (sun == null || atmosphereMaterial == null)
            return;

        // Direction from the planet toward the sun.
        Vector3 directionToSun = -sun.transform.forward;

        atmosphereMaterial.SetVector("_SunDirection", directionToSun);
    }
}