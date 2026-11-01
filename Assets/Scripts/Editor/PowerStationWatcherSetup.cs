#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public static class PowerStationWatcherSetup
{
    const string Night2 = "Assets/Scenes/Night 2.unity";
    const string Prefab = "Assets/Prefabs/PowerStationWatcher.prefab";
    const string TestScene = "Assets/_Project/Tests/WatcherNetworkTest.unity";

    [MenuItem("Tools/Watcher/Install Night 2 Watcher")]
    public static void Install()
    {
        try
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before setup.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != Night2) throw new InvalidOperationException("Open Night 2 before setup.");
            var existing = UnityEngine.Object.FindFirstObjectByType<PowerStationWatcher>();
            if (existing != null) { Debug.Log("[Watcher Setup] Already installed."); return; }
            GameObject model = scene.GetRootGameObjects().FirstOrDefault(obj => obj.name == "ManThing (1)");
            if (model == null) throw new InvalidOperationException("The placed ManThing (1) was not found. No scene was changed.");
            var station = scene.GetRootGameObjects().FirstOrDefault(obj => obj.name == "Powerbox");
            if (station == null) throw new InvalidOperationException("Powerbox was not found.");
            GameObject root = new GameObject("Power Station Watcher");
            Undo.RegisterCreatedObjectUndo(root, "Install power station watcher");
            root.transform.position = model.transform.position;
            Undo.SetTransformParent(model.transform, root.transform, "Attach placed ManThing visual");
            model.SetActive(true);
            root.AddComponent<NetworkObject>();
            var motor = root.AddComponent<CharacterController>();
            motor.height = 3f;
            motor.radius = 0.45f;
            motor.center = new Vector3(0f, 1.5f, 0f);
            motor.skinWidth = 0.04f;
            motor.stepOffset = 0.4f;
            motor.slopeLimit = 40f;
            var watcher = root.AddComponent<PowerStationWatcher>();
            watcher.visual = model;
            watcher.eyeHeight = 2.4f;
            watcher.patrolHalfExtents = new Vector2(55f, 35f);
            // Keep prefab reusable from Night 2 onward without a scene reference.
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            watcher.patrolCenter = station.transform;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Validate();
            File.WriteAllText("Logs/WatcherSetupResult.txt", "PASS: placed ManThing (1) attached in Night 2; prefab saved; patrol centered on Powerbox; Night 1 unchanged.");
            Debug.Log("[Watcher Setup] Installed on the placed ManThing (1). Cyan gizmo shows adjustable 110m x 70m station patrol bounds.");
        }
        catch (Exception exception)
        {
            File.WriteAllText("Logs/WatcherSetupResult.txt", exception.ToString());
            Debug.LogException(exception);
        }
    }

    [MenuItem("Tools/Watcher/Fit Placed Model Collider")]
    public static void FitCollider()
    {
        var watcher = UnityEngine.Object.FindFirstObjectByType<PowerStationWatcher>();
        if (watcher == null || EditorApplication.isPlaying) return;
        var renderers = watcher.visual.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        Transform model = watcher.visual.transform;
        // Rebase the motor at the model's feet while preserving the user's visual pose/scale.
        model.SetParent(null, true);
        watcher.transform.position = new Vector3(watcher.transform.position.x, bounds.min.y, watcher.transform.position.z);
        model.SetParent(watcher.transform, true);
        var motor = watcher.GetComponent<CharacterController>();
        motor.height = bounds.size.y;
        motor.radius = Mathf.Clamp(bounds.extents.x * 0.3f, 0.4f, 0.8f);
        motor.center = Vector3.up * motor.height * 0.5f;
        motor.minMoveDistance = 0f;
        watcher.eyeHeight = bounds.size.y * 0.88f;
        Transform station = watcher.patrolCenter;
        watcher.patrolCenter = null;
        PrefabUtility.SaveAsPrefabAsset(watcher.gameObject, Prefab);
        watcher.patrolCenter = station;
        EditorSceneManager.MarkSceneDirty(watcher.gameObject.scene);
        EditorSceneManager.SaveScene(watcher.gameObject.scene);
        AssetDatabase.SaveAssets();
        InspectModel();
        Validate();
        Debug.Log("[Watcher Setup] Motor fitted to original 3D model, preserving its world pose and scale.");
    }
    [MenuItem("Tools/Watcher/Inspect Placed Model")]
    public static void InspectModel()
    {
        var watcher = UnityEngine.Object.FindFirstObjectByType<PowerStationWatcher>();
        if (watcher == null) return;
        var renderers = watcher.visual.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        Physics.SyncTransforms();
        Vector3 origin = watcher.transform.position + Vector3.up * 30f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 100f, ~0, QueryTriggerInteraction.Ignore);
        string ground = string.Join("\n", hits.Where(hit => !hit.transform.IsChildOf(watcher.transform)).Select(hit => hit.collider.name + " at " + hit.point));
        File.WriteAllText("Logs/WatcherModelInspection.txt", "Root: " + watcher.transform.position + "\nModel bounds: " + bounds + "\nGround hits:\n" + ground);
        Debug.Log("[Watcher Setup] Model inspection written.");
    }
    [MenuItem("Tools/Watcher/Validate Scene Wiring")]
    public static void Validate()
    {
        string guid = AssetDatabase.AssetPathToGUID("Assets/Scripts/PowerStationWatcher.cs");
        string night2 = File.ReadAllText(Night2);
        string night1 = File.ReadAllText("Assets/Scenes/Night1_MultiplayerTest.unity");
        if (!night2.Contains("guid: " + guid) || night1.Contains("guid: " + guid))
            throw new InvalidOperationException("Watcher must exist in Night 2 only.");
        new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
        Debug.Log("[Watcher Setup] Night 2 wiring and all enabled scene NetworkObject identities passed.");
    }

    [MenuItem("Tools/Watcher/Build Local Multiplayer Test")]
    public static void BuildTest()
    {
        var previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            Directory.CreateDirectory("Assets/_Project/Tests");
            Directory.CreateDirectory("Logs/WatcherTestBuild");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Test ground";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(400f, 1f, 400f);
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var camera = new GameObject("Test Camera", typeof(Camera), typeof(AudioListener));
            camera.transform.position = new Vector3(4f, 3f, -7f);
            camera.transform.LookAt(new Vector3(0f, 1.5f, 5f));
            // Minimal actors use the actual gameplay death/movement/network transform components.
            var actor = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            actor.name = "Watcher Test Player";
            actor.GetComponent<CapsuleCollider>().center = Vector3.up;
            actor.AddComponent<NetworkObject>();
            actor.AddComponent<Networking.OwnerNetworkTransform>();
            actor.AddComponent<NetworkPlayerMovementState>();
            actor.AddComponent<NetworkOxygenState>();
            var actorPrefab = PrefabUtility.SaveAsPrefabAsset(actor, "Assets/_Project/Tests/WatcherTestPlayer.prefab");
            UnityEngine.Object.DestroyImmediate(actor);
            var managerObject = new GameObject("Test NetworkManager");
            var transport = managerObject.AddComponent<UnityTransport>();
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.PlayerPrefab = actorPrefab;
            manager.NetworkConfig.EnableSceneManagement = true;
            manager.NetworkConfig.ConnectionApproval = false;
            manager.NetworkConfig.ForceSamePrefabs = true;
            manager.NetworkConfig.TickRate = 30;
            manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = actorPrefab });
            var monsterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            if (monsterPrefab == null) throw new InvalidOperationException("Install Night 2 watcher first.");
            // A scene instance of the real monster prefab exercises NGO scene synchronization.
            var monster = (GameObject)PrefabUtility.InstantiatePrefab(monsterPrefab);
            monster.transform.position = Vector3.zero;
            monster.transform.rotation = Quaternion.identity;
            monster.GetComponent<PowerStationWatcher>().patrolCenter = null;
            var coordinator = new GameObject("Watcher test coordinator");
            coordinator.AddComponent<NetworkObject>();
            coordinator.AddComponent<WatcherNetworkTest>();
            EditorSceneManager.SaveScene(scene, TestScene);
            AssetDatabase.SaveAssets();
            // Validate test scene IDs as well as production scenes without changing build settings.
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { TestScene },
                locationPathName = "Logs/WatcherTestBuild/WatcherTest.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            File.WriteAllText("Logs/WatcherTestBuildResult.txt", report.summary.result + " errors=" + report.summary.totalErrors + " duration=" + report.summary.totalTime);
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Watcher test build failed.");
            Debug.Log("[Watcher Test] Local multiplayer test build ready.");
        }
        catch (Exception exception)
        {
            File.WriteAllText("Logs/WatcherTestBuildResult.txt", exception.ToString());
            Debug.LogException(exception);
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
    }
}
#endif


