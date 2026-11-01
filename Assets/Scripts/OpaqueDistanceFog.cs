using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Final camera-depth fog: distant pixels contain no recoverable scene detail.</summary>
[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
public sealed class OpaqueDistanceFog : MonoBehaviour
{
    [SerializeField, Min(0f)] private float fogStart = 35f;
    [SerializeField, Min(1f)] private float fogEnd = 70f;
    private Material material;
    private Camera view;

    private void OnEnable()
    {
        view = GetComponent<Camera>();
        view.depthTextureMode |= DepthTextureMode.Depth;
        Shader shader = Resources.Load<Shader>("OpaqueDistanceFog");
        if (shader != null && shader.isSupported)
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        else
            Debug.LogError("Opaque distance fog shader is missing or unsupported.", this);
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        string scene = SceneManager.GetActiveScene().name;
        if (material == null || (scene != "Night1_MultiplayerTest" && scene != "Night 2" && scene != "Night 1"))
        {
            Graphics.Blit(source, destination);
            return;
        }
        material.SetFloat("_FogStart", fogStart);
        material.SetFloat("_FogEnd", Mathf.Max(fogStart + 1f, fogEnd));
        // Pure black must remain the same for geometry and sky, with no gray haze.
        material.SetColor("_FogColor", Color.black);
        Graphics.Blit(source, destination, material);
    }

    private void OnDisable()
    {
        if (material != null) Destroy(material);
        material = null;
    }
}
