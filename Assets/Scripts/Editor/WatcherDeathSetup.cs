#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WatcherDeathSetup
{
    const string Prefab = "Assets/Prefabs/PowerStationWatcher.prefab";
    const string ImagePath = "Assets/Art/Watcher/WatcherDeath.png";
    const string SoundPath = "Assets/Audio/Watcher/WatcherDeath.wav";

    [MenuItem("Tools/Watcher/Render Death Art Previews %#&j")]
    public static void RenderPreviews()
    {
        try
        {
            Directory.CreateDirectory("Logs/WatcherDeathArt");
            Render("Logs/WatcherDeathArt/front.png", false, false);
            Render("Logs/WatcherDeathArt/back.png", true, false);
            Render("Logs/WatcherDeathArt/close.png", false, true);
            File.WriteAllText("Logs/WatcherDeathArtResult.txt", "PASS native model rendered for front/back/close review.");
        }
        catch (Exception exception) { File.WriteAllText("Logs/WatcherDeathArtResult.txt", "FAIL " + exception); Debug.LogException(exception); }
    }
    static void Render(string path, bool back, bool close)
    {
        var preview = new PreviewRenderUtility();
        try
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab).GetComponent<PowerStationWatcher>();
            var model = UnityEngine.Object.Instantiate(source.visual);
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            preview.AddSingleGO(model);
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) { renderer.enabled = true; bounds.Encapsulate(renderer.bounds); }
            Vector3 focus = close ? new Vector3(bounds.center.x, bounds.max.y - bounds.size.y * 0.12f, bounds.center.z) : bounds.center;
            float distance = bounds.size.y * (close ? 0.27f : 1.3f);
            var camera = preview.camera;
            camera.fieldOfView = close ? 52f : 38f;
            camera.nearClipPlane = 0.01f; camera.farClipPlane = distance + bounds.size.y * 4f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.transform.position = focus + (back ? Vector3.back : Vector3.forward) * distance;
            camera.transform.LookAt(focus);
            preview.ambientColor = new Color(0.08f, 0.08f, 0.09f);
            preview.lights[0].type = LightType.Directional;
            preview.lights[0].intensity = close ? 2.6f : 1.8f;
            preview.lights[0].color = new Color(0.7f, 0.85f, 1f);
            preview.lights[0].transform.rotation = camera.transform.rotation * Quaternion.Euler(-22f, -30f, 0f);
            preview.lights[1].type = LightType.Directional;
            preview.lights[1].intensity = close ? 1.5f : 0.8f;
            preview.lights[1].color = new Color(1f, 0.25f, 0.12f);
            preview.lights[1].transform.rotation = camera.transform.rotation * Quaternion.Euler(15f, 70f, 0f);
            preview.BeginStaticPreview(new Rect(0f, 0f, 1920f, 1080f));
            preview.Render(true);
            var texture = preview.EndStaticPreview();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }
        finally { preview.Cleanup(); }
    }

    [MenuItem("Tools/Watcher/Install Reviewed Death Jumpscare %#&k")]
    public static void Install()
    {
        var previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            Directory.CreateDirectory("Assets/Art/Watcher");
            File.Copy("Logs/WatcherDeathArt/close.png", ImagePath, true);
            AssetDatabase.Refresh();
            var importer = (TextureImporter)AssetImporter.GetAtPath(ImagePath);
            importer.textureType = TextureImporterType.Default; importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp; importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var image = AssetDatabase.LoadAssetAtPath<Texture2D>(ImagePath);
            var sound = AssetDatabase.LoadAssetAtPath<AudioClip>(SoundPath);
            if (image == null || sound == null) throw new InvalidOperationException("Death art or audio missing.");
            var root = PrefabUtility.LoadPrefabContents(Prefab);
            try { Assign(root.GetComponent<PowerStationWatcher>(), image, sound); PrefabUtility.SaveAsPrefabAsset(root, Prefab); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Night 2.unity", OpenSceneMode.Single);
            var watcher = scene.GetRootGameObjects().SelectMany(obj => obj.GetComponentsInChildren<PowerStationWatcher>(true)).Single();
            Assign(watcher, image, sound);
            EditorUtility.SetDirty(watcher);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
            File.WriteAllText("Logs/WatcherDeathInstallResult.txt", "PASS native Watcher close-up and death audio assigned in Night 2 and reusable prefab; 2-second victim-only jumpscare; scene identity validation passed.");
        }
        catch (Exception exception) { File.WriteAllText("Logs/WatcherDeathInstallResult.txt", "FAIL " + exception); Debug.LogException(exception); }
        finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
    }
    static void Assign(PowerStationWatcher watcher, Texture2D image, AudioClip sound)
    {
        watcher.deathJumpscareImage = image; watcher.deathJumpscareSound = sound; watcher.deathJumpscareDuration = 2f;
    }
}
#endif
