#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class WatcherStaticShaderCheck
{
    [MenuItem("Tools/Watcher/Validate Fine Static Shader")]
    public static void Run()
    {
        Shader shader = Resources.Load<Shader>("WatcherScreenStatic");
        if (shader == null || !shader.isSupported) throw new System.Exception("Fine static shader is missing or unsupported.");
        Material material = new Material(shader);
        RenderTexture target = RenderTexture.GetTemporary(512, 288, 0);
        RenderTexture previous = RenderTexture.active;
        try
        {
            Graphics.Blit(Texture2D.whiteTexture, target, material);
            var errors = ShaderUtil.GetShaderMessages(shader).Where(message => message.severity.ToString() == "Error").ToArray();
            if (errors.Length > 0) throw new System.Exception(string.Join("\n", errors.Select(error => error.message)));
            File.WriteAllText("Logs/WatcherStaticShaderCheck.txt", "PASS: fine-grain UI shader rendered on " + SystemInfo.graphicsDeviceType + " with no shader compile errors.");
            Debug.Log("[Watcher] Fine static shader render/compile check passed.");
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(material);
        }
    }
}
#endif
