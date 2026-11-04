#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public static class DrillSetup
{
    const string Night1 = "Assets/Scenes/Night1_MultiplayerTest.unity", Night2 = "Assets/Scenes/Night 2.unity";
    const string Prefab = "Assets/Prefabs/DrillStation.prefab";
    public const string Test1 = "Assets/_Project/Tests/DrillNetworkTest.unity", Test2 = "Assets/_Project/Tests/DrillTransferTest.unity";

    [MenuItem("Tools/Drill/Install Saved Night 1 Setup")]
    public static void InstallNight1()
    {
        try
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != Night1)
                throw new InvalidOperationException("Open Night1_MultiplayerTest in Edit Mode first.");
            var scene = SceneManager.GetActiveScene();
            if (Find<DrillSystem>(scene) != null) throw new InvalidOperationException("Drill already installed; no duplicate created.");
            var model = Named(scene, "drill");
            var screen = Named(scene, "drill screen");
            var button = Named(scene, "drill button");
            var root = new GameObject("Drill Station");
            Undo.RegisterCreatedObjectUndo(root, "Install drill system");
            root.transform.position = model.transform.position;
            Undo.SetTransformParent(model.transform, root.transform, "Preserve placed drill");
            Undo.SetTransformParent(button.transform, root.transform, "Preserve drill button");
            // These are the two adjacent pieces of the user's button housing/stand.
            foreach (string name in new[] { "Cube (16)", "Cube (18)" })
            {
                var stand = Named(scene, name);
                if (Vector3.Distance(stand.transform.position, button.transform.position) < 2f)
                    Undo.SetTransformParent(stand.transform, root.transform, "Keep drill switch housing");
            }
            root.AddComponent<NetworkObject>();
            var drill = root.AddComponent<DrillSystem>();
            drill.drillButton = button.transform;
            drill.buttonCollider = button.GetComponent<Collider>();
            BuildDisplay(drill, screen.transform, Named(scene, "BunkerZone").transform);
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/DrillSetupResult.txt", "PASS Night 1: saved placed geometry, switch housing, world display, scene NetworkObject and reusable prefab.");
            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.FrameSelected();
        }
        catch (Exception exception) { File.WriteAllText("Logs/DrillSetupResult.txt", "FAIL " + exception); Debug.LogException(exception); }
    }

    [MenuItem("Tools/Drill/Copy Verified Setup To Night 2")]
    public static void CopyNight2()
    {
        var previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            if (!File.Exists("Logs/DrillNetwork-host-PASS.txt")) throw new InvalidOperationException("Run the network regression before copying.");
            var sourceScene = EditorSceneManager.OpenScene(Night1, OpenSceneMode.Single);
            Vector3 bunker = Named(sourceScene, "BunkerZone").transform.position;
            var scene = EditorSceneManager.OpenScene(Night2, OpenSceneMode.Single);
            if (Find<DrillSystem>(scene) != null) throw new InvalidOperationException("Night 2 already has a drill; no duplicate created.");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab), scene);
            root.transform.position += Named(scene, "BunkerZone").transform.position - bunker;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/DrillNight2Result.txt", "PASS: copied tested Night 1 station to corresponding Night 2 bunker position.");
            Validate();
        }
        catch (Exception exception) { File.WriteAllText("Logs/DrillNight2Result.txt", "FAIL " + exception); Debug.LogException(exception); }
        finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
    }

    static void BuildDisplay(DrillSystem drill, Transform screen, Transform bunker)
    {
        var obj = new GameObject("Drill wall display", typeof(RectTransform), typeof(Canvas));
        var rect = obj.GetComponent<RectTransform>();
        rect.SetParent(drill.transform, false);
        Vector3 normal = screen.right * (Vector3.Dot(bunker.position - screen.position, screen.right) >= 0f ? 1f : -1f);
        rect.position = screen.position + normal * (screen.lossyScale.x * 0.5f + 0.008f);
        rect.rotation = Quaternion.LookRotation(-normal, screen.up);
        rect.sizeDelta = new Vector2(1000f, 650f);
        rect.localScale = new Vector3(Mathf.Abs(screen.lossyScale.z) * 0.91f / 1000f, Mathf.Abs(screen.lossyScale.y) * 0.9f / 650f, 0.001f);
        obj.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var bg = new GameObject("Screen background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(rect, false);
        var bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = bgRect.offsetMax = Vector2.zero;
        bg.GetComponent<Image>().color = new Color(0.015f, 0.025f, 0.03f, 1f);
        bg.GetComponent<Image>().raycastTarget = false;
        Label(rect, "BUNKER / RESOURCE EXTRACTION", new Vector2(0f, 265f), new Vector2(880f, 55f), 27f, new Color(0.55f, 0.65f, 0.65f));
        drill.statusText = Label(rect, "DRILL: DISABLED", new Vector2(0f, 165f), new Vector2(880f, 85f), 51f, new Color(1f, 0.39f, 0.24f));
        drill.materialsText = Label(rect, "METAL = 0\nGOLD = 0\nPLASMA = 0", new Vector2(0f, -27f), new Vector2(880f, 270f), 55f, new Color(0.85f, 0.93f, 0.9f));
        Label(rect, "TEAM STOCKPILE / SHARED MATERIALS", new Vector2(0f, -222f), new Vector2(880f, 50f), 24f, new Color(0.55f, 0.65f, 0.65f));
        Label(rect, "[E] AT SWITCH TO ENABLE / DISABLE", new Vector2(0f, -280f), new Vector2(880f, 42f), 24f, new Color(0.85f, 0.93f, 0.9f));
    }
    static TMP_Text Label(Transform parent, string value, Vector2 position, Vector2 size, float fontSize, Color color)
    {
        var obj = new GameObject(value.Split('\n')[0], typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        var text = obj.GetComponent<TextMeshProUGUI>();
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Space_Mono/SpaceMono-Bold SDF.asset");
        text.rectTransform.anchoredPosition = position; text.rectTransform.sizeDelta = size;
        text.text = value; text.fontSize = fontSize; text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false; text.richText = false;
        return text;
    }

    [MenuItem("Tools/Drill/Validate Both Nights")]
    public static void Validate()
    {
        var previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (var path in new[] { Night1, Night2 })
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var drills = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<DrillSystem>(true)).ToArray();
                if (drills.Length != 1 || drills[0].GetComponent<NetworkObject>() == null || drills[0].buttonCollider == null ||
                    drills[0].drillButton == null || drills[0].statusText == null || drills[0].materialsText == null ||
                    drills[0].statusText.GetComponentInParent<Canvas>().renderMode != RenderMode.WorldSpace)
                    throw new Exception("Invalid drill wiring in " + path);
            }
            File.WriteAllText("Logs/DrillSceneValidation.txt", "PASS both nights have exactly one networked drill, valid button/collider and world-space status/material display.");
        }
        catch (Exception exception) { File.WriteAllText("Logs/DrillSceneValidation.txt", "FAIL " + exception); Debug.LogException(exception); }
        finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
    }

    [MenuItem("Tools/Drill/Build Network Regression %#&d")]
    public static void BuildTest()
    {
        var previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            Directory.CreateDirectory("Assets/_Project/Tests");
            Directory.CreateDirectory("Logs/DrillTestBuild");
            for (int i = 0; i < 2; i++)
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.transform.position = new Vector3(0f, -0.5f, 0f); ground.transform.localScale = new Vector3(100f, 1f, 100f);
                var station = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
                station.transform.position = new Vector3(0f, 3.5f, 0f);
                var drill = station.GetComponent<DrillSystem>();
                var power = new GameObject("Test power", typeof(NetworkObject)).AddComponent<PowerSystem>();
                power.outageChance = 0f; power.outageCheckInterval = 100000f;
                power.whiteLights = power.redLights = Array.Empty<Light>();
                var radar = new GameObject("Test radar", typeof(NetworkObject)).AddComponent<RadarSystem>();
                radar.transform.position = drill.drillButton.position;
                radar.objectiveTransform = drill.drillButton; radar.cooldownTime = 30f;
                var defense = new GameObject("Test defense", typeof(NetworkObject)).AddComponent<DefenseSystem>();
                defense.transform.position = drill.drillButton.position;
                defense.objectiveTransform = drill.drillButton; defense.respawnPoints = new[] { drill.transform };
                if (i == 0)
                {
                    var go = new GameObject("Regression NetworkManager");
                    var transport = go.AddComponent<UnityTransport>();
                    var manager = go.AddComponent<NetworkManager>();
                    manager.NetworkConfig.NetworkTransport = transport;
                    manager.NetworkConfig.PlayerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NetworkRealPlayer.prefab");
                    manager.NetworkConfig.EnableSceneManagement = true;
                }
                new GameObject("Drill regression", typeof(NetworkObject)).AddComponent<DrillNetworkTest>();
                EditorSceneManager.SaveScene(scene, i == 0 ? Test1 : Test2);
            }
            AssetDatabase.SaveAssets();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { Test1, Test2 }, target = BuildTarget.StandaloneWindows64,
                locationPathName = "Logs/DrillTestBuild/DrillTest.exe", options = BuildOptions.Development });
            File.WriteAllText("Logs/DrillBuildResult.txt", report.summary.result + " errors=" + report.summary.totalErrors);
        }
        catch (Exception exception) { File.WriteAllText("Logs/DrillBuildResult.txt", "FAIL " + exception); Debug.LogException(exception); }
        finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
    }
    static T Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).FirstOrDefault();
    static GameObject Named(Scene scene, string name) => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
        .FirstOrDefault(t => t.name == name)?.gameObject ?? throw new InvalidOperationException("Missing labeled object: " + name);

    [MenuItem("Tools/Drill/Inspect Both Installed Scenes %#&v")]
    public static void InspectScenes()
    {
        var previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var hash = typeof(NetworkObject).Assembly.GetType("Unity.Netcode.XXHash").GetMethod("Hash32",
                BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string) }, null);
            foreach (var path in new[] { Night1, Night2 })
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var drill = Find<DrillSystem>(scene);
                var network = drill.GetComponent<NetworkObject>();
                uint expected = (uint)hash.Invoke(null, new object[] { GlobalObjectId.GetGlobalObjectIdSlow(network).ToString() });
                uint actual = (uint)new SerializedObject(network).FindProperty("GlobalObjectIdHash").longValue;
                if (actual != expected) throw new Exception("Drill scene network identity mismatch: " + path);
                GameObject actor = new GameObject("Temporary drill reach inspection");
                var control = actor.AddComponent<PlayerController>();
                actor.transform.position = drill.drillButton.position + Vector3.forward * 1.8f + Vector3.up * 0.6f;
                Physics.SyncTransforms();
                typeof(DrillSystem).GetField("localPlayer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(drill, control);
                drill.RequestEnabled(true);
                if (!drill.Current.Enabled || drill.statusText.text != "DRILL: ENABLED")
                    throw new Exception("Placed switch is not usable in " + path);
                var screen = drill.statusText.GetComponentInParent<Canvas>().transform;
                var view = new GameObject("Temporary drill render camera").AddComponent<Camera>();
                view.transform.position = screen.position - screen.forward * 6.3f;
                view.transform.LookAt(screen.position - Vector3.up * 0.4f, Vector3.up);
                view.fieldOfView = 42f; view.nearClipPlane = 0.03f;
                view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = Color.black;
                var target = new RenderTexture(1280, 960, 24);
                view.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                foreach (var text in drill.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
                view.Render();
                var previousRT = RenderTexture.active;
                RenderTexture.active = target;
                var image = new Texture2D(1280, 960, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0); image.Apply();
                File.WriteAllBytes(path == Night1 ? "Logs/DrillNight1.png" : "Logs/DrillNight2.png", image.EncodeToPNG());
                RenderTexture.active = previousRT;
                view.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(view.gameObject);
                drill.RequestEnabled(false);
                if (drill.Current.Enabled || drill.statusText.text != "DRILL: DISABLED")
                    throw new Exception("Placed switch cannot stop in " + path);
                UnityEngine.Object.DestroyImmediate(actor);
            }
            new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
            File.WriteAllText("Logs/DrillPlacementInspection.txt", "PASS both real scenes: authored switch usable with actual surrounding colliders, enable/disable reflected on wall; rendered enabled screens; per-scene NGO identity and all enabled scene identity preflight passed. Edit Mode inspection did not save temporary actors or state.");
        }
        catch (Exception exception) { File.WriteAllText("Logs/DrillPlacementInspection.txt", "FAIL " + exception); Debug.LogException(exception); }
        finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
    }
}
#endif
