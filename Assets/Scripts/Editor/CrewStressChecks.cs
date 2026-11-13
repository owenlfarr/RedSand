#if UNITY_EDITOR
using System;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static class CrewStressChecks
{
    [InitializeOnLoadMethod]
    private static void Requested()
    {
        if (File.Exists("Logs/RunCrewStressChecks.request")) EditorApplication.delayCall += Run;
        if (File.Exists("Logs/InspectCrewStress.request")) EditorApplication.delayCall += Inspect;
        if (File.Exists("Logs/RenderCrewStress.request")) EditorApplication.delayCall += RenderScreens;
    }

    [MenuItem("Tools/Crew Stress/Render Saved Screens")]
    public static async void RenderScreens()
    {
        File.Delete("Logs/RenderCrewStress.request");
        try
        {
            int count = 0;
            foreach (var path in new[] { "Assets/Scenes/Night 2.unity", "Assets/Scenes/Night1_MultiplayerTest.unity" })
            {
                var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene(path);
                try
                {
                    CrewStressMonitor monitor = null;
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        var found = root.GetComponentInChildren<CrewStressMonitor>();
                        if (found != null) monitor = found;
                    }
                    if (monitor == null) throw new Exception("Missing serialized monitor in " + path);
                    monitor.BuildDisplays();
                    // Let newly created preview canvases register with the
                    // editor before rendering a second isolated scene.
                    await System.Threading.Tasks.Task.Delay(150);
                    foreach (var face in monitor.GetComponentsInChildren<UnityEngine.Canvas>())
                    {
                        foreach (var text in face.GetComponentsInChildren<TMPro.TMP_Text>()) text.ForceMeshUpdate(true, true);
                        UnityEngine.Canvas.ForceUpdateCanvases();
                        var view = new GameObject("Stress screen verification camera").AddComponent<Camera>();
                        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(view.gameObject, scene);
                        view.scene = scene;
                        Vector3 normal = -face.transform.forward;
                        view.transform.position = face.transform.position + normal * 7;
                        view.transform.LookAt(face.transform.position, Vector3.up);
                        view.fieldOfView = 35; view.nearClipPlane = .03f;
                        view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = Color.black;
                        var target = new RenderTexture(1280, 720, 24);
                        var previous = RenderTexture.active;
                        try
                        {
                            view.targetTexture = target;
                            UnityEngine.Canvas.ForceUpdateCanvases();
                            foreach (var text in face.GetComponentsInChildren<TMPro.TMP_Text>()) text.ForceMeshUpdate(true, true);
                            UnityEngine.Canvas.ForceUpdateCanvases();
                            view.Render(); view.Render(); RenderTexture.active = target;
                            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                            File.WriteAllBytes("Logs/CrewStressScreen" + (++count) + ".png", image.EncodeToPNG());
                            UnityEngine.Object.DestroyImmediate(image);
                        }
                        finally
                        {
                            RenderTexture.active = previous; view.targetTexture = null;
                            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(view.gameObject);
                        }
                    }
                }
                finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
            }
            File.WriteAllText("Logs/CrewStressRenderResult.txt", "PASS serialized monitor found in both nights; rendered " + count + " screen faces in isolated preview scenes. No gameplay scenes saved by verification.");
        }
        catch (Exception error) { File.WriteAllText("Logs/CrewStressRenderResult.txt", "FAIL " + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Crew Stress/Inspect Live Screen")]
    public static void Inspect()
    {
        File.Delete("Logs/InspectCrewStress.request");
        var result = new System.Text.StringBuilder();
        result.AppendLine("Playing=" + EditorApplication.isPlaying);
        foreach (var radio in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (radio.name != "Radio") continue;
            result.AppendLine("Radio " + radio.position + " scale=" + radio.lossyScale + " monitor=" + radio.GetComponent<CrewStressMonitor>());
            foreach (var child in radio.GetComponentsInChildren<Transform>(true))
                result.AppendLine(child.name + " pos=" + child.position + " forward=" + child.forward + " scale=" + child.lossyScale + " active=" + child.gameObject.activeInHierarchy);
        }
        foreach (var camera in Camera.allCameras) result.AppendLine("Camera " + camera.name + " pos=" + camera.transform.position + " forward=" + camera.transform.forward + " mask=" + camera.cullingMask);
        File.WriteAllText("Logs/CrewStressLiveInspection.txt", result.ToString());
    }

    [MenuItem("Tools/Crew Stress/Check Thresholds")]
    public static void Run()
    {
        const string request = "Logs/RunCrewStressChecks.request";
        if (File.Exists(request)) File.Delete(request);
        try
        {
            int assertions = 0;
            foreach (float range in new[] { 10f, 30f, 55f })
            {
                Check(range + 30.01f, range, false, 0); assertions++;
                Check(range + 30f, range, false, 1); assertions++;
                Check(range + 20.01f, range, false, 1); assertions++;
                Check(range + 20f, range, false, 2); assertions++;
                Check(range, range, false, 2); assertions++;
                Check(0, range, false, 2); assertions++;
                Check(range + 100, range, true, 3); assertions++;
            }
            string worstName = new string('\u754c', 24);
            string row = worstName + "  <color=#FF5555>STRESSED 3/3</color>";
            var snapshot = new FixedString512Bytes(string.Join("\n\n", System.Linq.Enumerable.Repeat(row, Networking.RelayPartyManager.LobbyPlayerLimit)));
            if (snapshot.Length > snapshot.Capacity) throw new Exception("Stress snapshot exceeds capacity");
            assertions++;
            foreach (var path in new[] { "Assets/Scenes/Night1_MultiplayerTest.unity", "Assets/Scenes/Night 2.unity" })
            {
                if (!File.ReadAllText(path).Replace("\r\n", "\n").Contains("m_Name: Radio\n")) throw new Exception("Missing broadcast screen in " + path);
                assertions++;
            }
            File.WriteAllText("Logs/CrewStressChecks.txt", "PASS " + assertions + " assertions: threshold boundaries, chase priority, UTF-8 capacity, both night screens present.");
            Debug.Log("[CrewStress] Checks passed: " + assertions);
        }
        catch (Exception error)
        {
            File.WriteAllText("Logs/CrewStressChecks.txt", "FAIL " + error);
            Debug.LogException(error);
        }
    }

    private static void Check(float distance, float range, bool chased, int expected)
    {
        int actual = CrewStressMonitor.StressLevel(distance, range, chased);
        if (actual != expected) throw new Exception($"Stress at {distance}m / range {range} / chased={chased}: {actual}, expected {expected}");
    }
}
#endif
