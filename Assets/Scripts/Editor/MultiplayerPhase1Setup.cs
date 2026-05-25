#if UNITY_EDITOR
using System.Collections.Generic;
using Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using System;

public static class MultiplayerPhase1Setup
{
    private const string ScenePath = "Assets/Scenes/MultiplayerTest.unity";
    private const string PrefabPath = "Assets/Prefabs/NetworkTestPlayer.prefab";
    private const string RealScenePath = "Assets/Scenes/Night 1.unity";
    private const string RealPrefabPath = "Assets/Prefabs/NetworkRealPlayer.prefab";
    private const string Night1MpTestScenePath = "Assets/Scenes/Night1_MultiplayerTest.unity";
    private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";

    [MenuItem("Tools/Networking/Setup Phase 1 Test Scene")]
    public static void RunSetupFromMenu()
    {
        SetupAll();
    }

    [MenuItem("Tools/Networking/Switch MultiplayerTest To NetworkTestPlayer")]
    public static void SwitchToNetworkTestPlayer()
    {
        SetupAll();
        AssignPlayerPrefabInTestScene(PrefabPath);
    }

    [MenuItem("Tools/Networking/Switch MultiplayerTest To NetworkRealPlayer")]
    public static void SwitchToNetworkRealPlayer()
    {
        SetupAll();
        var realPrefab = EnsureNetworkRealPlayerPrefab();
        AssignPlayerPrefabInTestScene(AssetDatabase.GetAssetPath(realPrefab));
    }

    [MenuItem("Tools/Networking/Setup Phase 3 Night1 Multiplayer Test")]
    public static void SetupPhase3Night1MultiplayerTest()
    {
        SetupAll();
        var realPrefab = EnsureNetworkRealPlayerPrefab();
        EnsureNight1MultiplayerTestScene(realPrefab);
        EnsureBuildSettingsForPhase3();
        EnsureMenuRelayFlow(realPrefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("Tools/Networking/Setup Main Menu Relay Flow")]
    public static void SetupMainMenuRelayFlow()
    {
        SetupAll();
        var realPrefab = EnsureNetworkRealPlayerPrefab();
        EnsureNight1MultiplayerTestScene(realPrefab);
        EnsureMenuRelayFlow(realPrefab);
        EnsureBuildSettingsForMainMenuFlow();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [InitializeOnLoadMethod]
    private static void EnsureSetupOnLoad()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Scripts/Networking"))
            {
                return;
            }

            SetupAll();
            var realPrefab = EnsureNetworkRealPlayerPrefab();
            EnsureNight1MultiplayerTestScene(realPrefab);
            EnsureMenuRelayFlow(realPrefab);
            EnsureBuildSettingsForMainMenuFlow();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        };
    }

    private static void SetupAll()
    {
        EnsureFolders();
        var playerPrefab = EnsureNetworkTestPlayerPrefab();
        EnsureMultiplayerTestScene(playerPrefab);
        EnsureBuildSettings();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static GameObject EnsureNetworkRealPlayerPrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(RealPrefabPath);
        if (existing != null)
        {
            EnsureNetworkComponentsOnPrefab(existing);
            return existing;
        }

        var scene = EditorSceneManager.OpenScene(RealScenePath, OpenSceneMode.Single);
        GameObject sourcePlayer = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if (t.CompareTag("Player"))
                {
                    sourcePlayer = t.gameObject;
                    break;
                }
            }

            if (sourcePlayer != null)
            {
                break;
            }
        }

        if (sourcePlayer == null)
        {
            throw new System.Exception("Could not find a GameObject tagged 'Player' in Assets/Scenes/Night 1.unity");
        }

        var clone = UnityEngine.Object.Instantiate(sourcePlayer);
        clone.name = "NetworkRealPlayer";

        EnsureNetworkComponentsOnObject(clone);
        var prefab = PrefabUtility.SaveAsPrefabAsset(clone, RealPrefabPath);
        UnityEngine.Object.DestroyImmediate(clone);

        return prefab;
    }

    private static void EnsureNetworkComponentsOnPrefab(GameObject prefab)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = prefab.name;
        EnsureNetworkComponentsOnObject(instance);
        PrefabUtility.SaveAsPrefabAsset(instance, RealPrefabPath);
        UnityEngine.Object.DestroyImmediate(instance);
    }

    private static void EnsureNetworkComponentsOnObject(GameObject go)
    {
        if (go.GetComponent<NetworkObject>() == null)
        {
            go.AddComponent<NetworkObject>();
        }

        var netTransform = go.GetComponent<NetworkTransform>();
        if (netTransform != null && netTransform.GetType() == typeof(NetworkTransform))
        {
            UnityEngine.Object.DestroyImmediate(netTransform, true);
        }

        if (go.GetComponent<OwnerNetworkTransform>() == null)
        {
            go.AddComponent<OwnerNetworkTransform>();
        }
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
        {
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        }
    }

    private static GameObject EnsureNetworkTestPlayerPrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
        {
            return existing;
        }

        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        root.name = "NetworkTestPlayer";
        root.transform.position = Vector3.zero;

        if (root.GetComponent<NetworkObject>() == null)
        {
            root.AddComponent<NetworkObject>();
        }

        if (root.GetComponent<NetworkTransform>() == null)
        {
            root.AddComponent<NetworkTransform>();
        }

        if (root.GetComponent<NetworkTestPlayer>() == null)
        {
            root.AddComponent<NetworkTestPlayer>();
        }

        if (root.GetComponent<NetworkTestPlayerCamera>() == null)
        {
            root.AddComponent<NetworkTestPlayerCamera>();
        }

        var camGo = new GameObject("OwnerCamera");
        camGo.transform.SetParent(root.transform);
        camGo.transform.localPosition = new Vector3(0f, 1.6f, -4f);
        camGo.transform.localRotation = Quaternion.identity;

        camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);

        return prefab;
    }

    private static void EnsureMultiplayerTestScene(GameObject playerPrefab)
    {
        Scene scene;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
        else
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        var existingLight = GameObject.Find("Directional Light");
        if (existingLight == null)
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        var nmGo = GameObject.Find("NetworkManager");
        if (nmGo == null)
        {
            nmGo = new GameObject("NetworkManager");
        }

        var nm = nmGo.GetComponent<NetworkManager>();
        if (nm == null)
        {
            nm = nmGo.AddComponent<NetworkManager>();
        }

        if (nmGo.GetComponent<UnityTransport>() == null)
        {
            nmGo.AddComponent<UnityTransport>();
        }

        if (nmGo.GetComponent<MultiplayerMenu>() == null)
        {
            nmGo.AddComponent<MultiplayerMenu>();
        }

        if (nmGo.GetComponent<PersistentNetworkManager>() == null)
        {
            nmGo.AddComponent<PersistentNetworkManager>();
        }

        if (nm.NetworkConfig == null)
        {
            nm.NetworkConfig = new NetworkConfig();
        }

        nm.NetworkConfig.PlayerPrefab = playerPrefab;

        EnsureGroundPlane();
        var points = EnsureSpawnPoints();
        EnsureSpawnSystem(nmGo, points);

        EditorUtility.SetDirty(nmGo);
        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    private static void EnsureGroundPlane()
    {
        var ground = GameObject.Find("Ground");
        if (ground == null)
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
        }

        ground.transform.position = Vector3.zero;
        ground.transform.rotation = Quaternion.identity;
        ground.transform.localScale = new Vector3(5f, 1f, 5f);

        if (ground.GetComponent<Collider>() == null)
        {
            ground.AddComponent<MeshCollider>();
        }
    }

    private static Transform[] EnsureSpawnPoints()
    {
        var parent = GameObject.Find("SpawnPoints");
        if (parent == null)
        {
            parent = new GameObject("SpawnPoints");
        }

        Vector3[] positions =
        {
            new Vector3(0f, 2f, 0f),
            new Vector3(3f, 2f, 0f),
            new Vector3(-3f, 2f, 0f),
            new Vector3(0f, 2f, 3f)
        };

        var points = new Transform[positions.Length];
        Type startPosType = GetNetworkStartPositionType();

        for (int i = 0; i < positions.Length; i++)
        {
            string name = $"SpawnPoint_{i}";
            var child = parent.transform.Find(name);
            if (child == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent.transform);
                child = go.transform;
            }

            child.position = positions[i];
            child.rotation = Quaternion.identity;
            points[i] = child;

            if (startPosType != null && child.GetComponent(startPosType) == null)
            {
                child.gameObject.AddComponent(startPosType);
            }
        }

        return points;
    }

    private static void EnsureSpawnSystem(GameObject networkManagerGo, Transform[] points)
    {
        Type startPosType = GetNetworkStartPositionType();
        if (startPosType != null)
        {
            var fallback = networkManagerGo.GetComponent<ServerSpawnPointAssigner>();
            if (fallback != null)
            {
                UnityEngine.Object.DestroyImmediate(fallback, true);
            }
            return;
        }

        var assigner = networkManagerGo.GetComponent<ServerSpawnPointAssigner>();
        if (assigner == null)
        {
            assigner = networkManagerGo.AddComponent<ServerSpawnPointAssigner>();
        }

        assigner.SetSpawnPoints(points);
    }

    private static Type GetNetworkStartPositionType()
    {
        Type t = Type.GetType("Unity.Netcode.NetworkStartPosition, Unity.Netcode.Runtime");
        if (t != null) return t;

        t = Type.GetType("Unity.Netcode.Components.NetworkStartPosition, Unity.Netcode.Components");
        return t;
    }

    private static void EnsureBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        bool hasMenu = false;
        bool hasNight = false;
        bool hasTest = false;

        foreach (var s in scenes)
        {
            if (s.path == MainMenuScenePath) hasMenu = true;
            if (s.path == "Assets/Scenes/Night 1.unity") hasNight = true;
            if (s.path == ScenePath) hasTest = true;
        }

        if (!hasMenu)
        {
            scenes.Add(new EditorBuildSettingsScene(MainMenuScenePath, true));
        }

        if (!hasNight)
        {
            scenes.Add(new EditorBuildSettingsScene("Assets/Scenes/Night 1.unity", true));
        }

        if (!hasTest)
        {
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void EnsureBuildSettingsForPhase3()
    {
        var ordered = new List<EditorBuildSettingsScene>();
        var all = new Dictionary<string, EditorBuildSettingsScene>();

        foreach (var s in EditorBuildSettings.scenes)
        {
            all[s.path] = new EditorBuildSettingsScene(s.path, true);
        }

        all[Night1MpTestScenePath] = new EditorBuildSettingsScene(Night1MpTestScenePath, true);
        all[MainMenuScenePath] = new EditorBuildSettingsScene(MainMenuScenePath, true);
        all["Assets/Scenes/Night 1.unity"] = new EditorBuildSettingsScene("Assets/Scenes/Night 1.unity", true);
        all[ScenePath] = new EditorBuildSettingsScene(ScenePath, true);

        ordered.Add(all[Night1MpTestScenePath]);
        ordered.Add(all[MainMenuScenePath]);
        ordered.Add(all["Assets/Scenes/Night 1.unity"]);

        foreach (var kvp in all)
        {
            if (kvp.Key == Night1MpTestScenePath || kvp.Key == MainMenuScenePath || kvp.Key == "Assets/Scenes/Night 1.unity")
            {
                continue;
            }
            ordered.Add(kvp.Value);
        }

        EditorBuildSettings.scenes = ordered.ToArray();
    }

    private static void EnsureBuildSettingsForMainMenuFlow()
    {
        var all = new Dictionary<string, EditorBuildSettingsScene>();
        foreach (var s in EditorBuildSettings.scenes)
        {
            all[s.path] = new EditorBuildSettingsScene(s.path, true);
        }

        all[MainMenuScenePath] = new EditorBuildSettingsScene(MainMenuScenePath, true);
        all[Night1MpTestScenePath] = new EditorBuildSettingsScene(Night1MpTestScenePath, true);
        all["Assets/Scenes/Night 1.unity"] = new EditorBuildSettingsScene("Assets/Scenes/Night 1.unity", true);

        var ordered = new List<EditorBuildSettingsScene>
        {
            all[MainMenuScenePath]
        };

        if (all.ContainsKey(Night1MpTestScenePath))
        {
            ordered.Add(all[Night1MpTestScenePath]);
        }
        else
        {
            ordered.Add(new EditorBuildSettingsScene(Night1MpTestScenePath, true));
        }

        if (all.ContainsKey("Assets/Scenes/Night 1.unity"))
        {
            ordered.Add(all["Assets/Scenes/Night 1.unity"]);
        }

        foreach (var kvp in all)
        {
            if (kvp.Key == MainMenuScenePath || kvp.Key == Night1MpTestScenePath || kvp.Key == "Assets/Scenes/Night 1.unity")
            {
                continue;
            }
            ordered.Add(kvp.Value);
        }

        EditorBuildSettings.scenes = ordered.ToArray();
    }

    private static void AssignPlayerPrefabInTestScene(string prefabPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            throw new System.Exception($"Player prefab not found at {prefabPath}");
        }

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var nmGo = GameObject.Find("NetworkManager");
        if (nmGo == null)
        {
            throw new System.Exception("NetworkManager GameObject not found in MultiplayerTest scene.");
        }

        var nm = nmGo.GetComponent<NetworkManager>();
        if (nm == null)
        {
            throw new System.Exception("NetworkManager component missing in MultiplayerTest scene.");
        }

        if (nm.NetworkConfig == null)
        {
            nm.NetworkConfig = new NetworkConfig();
        }

        nm.NetworkConfig.PlayerPrefab = prefab;
        EditorUtility.SetDirty(nmGo);
        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    private static void EnsureNight1MultiplayerTestScene(GameObject networkRealPlayerPrefab)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Night1MpTestScenePath) == null)
        {
            bool copied = AssetDatabase.CopyAsset(RealScenePath, Night1MpTestScenePath);
            if (!copied)
            {
                throw new Exception("Failed to duplicate Night 1 scene for multiplayer test.");
            }
        }

        var scene = EditorSceneManager.OpenScene(Night1MpTestScenePath, OpenSceneMode.Single);

        var nmGo = GameObject.Find("NetworkManager");
        if (nmGo == null)
        {
            nmGo = new GameObject("NetworkManager");
        }

        var nm = nmGo.GetComponent<NetworkManager>();
        if (nm == null)
        {
            nm = nmGo.AddComponent<NetworkManager>();
        }

        if (nmGo.GetComponent<UnityTransport>() == null)
        {
            nmGo.AddComponent<UnityTransport>();
        }

        if (nmGo.GetComponent<MultiplayerMenu>() == null)
        {
            nmGo.AddComponent<MultiplayerMenu>();
        }

        if (nm.NetworkConfig == null)
        {
            nm.NetworkConfig = new NetworkConfig();
        }
        nm.NetworkConfig.PlayerPrefab = networkRealPlayerPrefab;

        Vector3 basePos = Vector3.zero;
        var originalPlayer = FindFirstTaggedObject(scene, "Player");
        if (originalPlayer != null)
        {
            basePos = originalPlayer.transform.position;
            originalPlayer.SetActive(false);
        }

        var spawnPoints = EnsureNight1SpawnPoints(basePos);
        EnsureSpawnSystem(nmGo, spawnPoints);
        QuarantineSinglePlayerSystems(scene);
        EnsureAuthoritySceneSystems(scene);

        EditorUtility.SetDirty(nmGo);
        EditorSceneManager.SaveScene(scene, Night1MpTestScenePath);
    }

    private static Transform[] EnsureNight1SpawnPoints(Vector3 basePos)
    {
        var parent = GameObject.Find("MultiplayerSpawnPoints");
        if (parent == null)
        {
            parent = new GameObject("MultiplayerSpawnPoints");
        }

        Vector3[] offsets =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(3f, 0f, 0f),
            new Vector3(-3f, 0f, 0f),
            new Vector3(0f, 0f, 3f)
        };

        Type startPosType = GetNetworkStartPositionType();
        var points = new Transform[offsets.Length];

        for (int i = 0; i < offsets.Length; i++)
        {
            string name = $"SpawnPoint_{i}";
            var child = parent.transform.Find(name);
            if (child == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent.transform);
                child = go.transform;
            }

            Vector3 probe = basePos + offsets[i] + Vector3.up * 15f;
            Vector3 resolved = basePos + offsets[i] + Vector3.up * 2f;

            if (Physics.Raycast(probe, Vector3.down, out var hit, 100f))
            {
                resolved = hit.point + Vector3.up * 2f;
            }

            child.position = resolved;
            child.rotation = Quaternion.identity;
            points[i] = child;

            if (startPosType != null && child.GetComponent(startPosType) == null)
            {
                child.gameObject.AddComponent(startPosType);
            }
        }

        return points;
    }

    private static void EnsureAuthoritySceneSystems(Scene scene)
    {
        EnsureAuthorityComponent<ZombieAI>(scene, true);
        EnsureAuthorityComponent<SurveyorMonster>(scene, true);
        EnsureAuthorityComponent<RushMonsterEvent>(scene, true);
        EnsureAuthorityComponent<NightTimeManager>(scene, true);
        EnsureAuthorityComponent<PowerSystem>(scene, true);
        EnsureAuthorityComponent<SolarFlareSystem>(scene, true);
        EnsureAuthorityComponent<DefenseSystem>(scene, true);
        EnsureAuthorityComponent<RadarSystem>(scene, true);
        EnsureAuthorityComponent<LockerInteractionNew>(scene, true);
    }

    private static void EnsureAuthorityComponent<T>(Scene scene, bool enableComponent) where T : Behaviour
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var components = root.GetComponentsInChildren<T>(true);
            foreach (var component in components)
            {
                var go = component.gameObject;
                if (go.GetComponent<NetworkObject>() == null)
                {
                    go.AddComponent<NetworkObject>();
                }

                if (enableComponent)
                {
                    component.enabled = true;
                }

                EditorUtility.SetDirty(component);
                EditorUtility.SetDirty(go);
            }
        }
    }

    private static GameObject FindFirstTaggedObject(Scene scene, string tag)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.CompareTag(tag))
                {
                    return t.gameObject;
                }
            }
        }
        return null;
    }

    private static void QuarantineSinglePlayerSystems(Scene scene)
    {
        DisableAllOfType<OxygenSystem>(scene);
        DisableAllOfType<PowerSystem>(scene);
        DisableAllOfType<NightTimeManager>(scene);
        DisableAllOfType<DefenseSystem>(scene);
        DisableAllOfType<RadarSystem>(scene);
        DisableAllOfType<LockerInteractionNew>(scene);
        DisableAllOfType<WinScreen>(scene);
    }

    private static void DisableAllOfType<T>(Scene scene) where T : Behaviour
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var components = root.GetComponentsInChildren<T>(true);
            foreach (var component in components)
            {
                component.enabled = false;
                EditorUtility.SetDirty(component);
            }
        }
    }

    private static void EnsureMenuRelayFlow(GameObject networkRealPlayerPrefab)
    {
        var scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);

        var nmGo = GameObject.Find("NetworkManager");
        if (nmGo == null)
        {
            nmGo = new GameObject("NetworkManager");
        }

        var nm = nmGo.GetComponent<NetworkManager>();
        if (nm == null)
        {
            nm = nmGo.AddComponent<NetworkManager>();
        }

        var transport = nmGo.GetComponent<UnityTransport>();
        if (transport == null)
        {
            transport = nmGo.AddComponent<UnityTransport>();
        }

        if (nmGo.GetComponent<RelayPartyManager>() == null)
        {
            nmGo.AddComponent<RelayPartyManager>();
        }

        if (nmGo.GetComponent<VoiceChatManager>() == null)
        {
            nmGo.AddComponent<VoiceChatManager>();
        }

        if (nmGo.GetComponent<PersistentNetworkManager>() == null)
        {
            nmGo.AddComponent<PersistentNetworkManager>();
        }

        if (nmGo.GetComponent<MultiplayerMenu>() == null)
        {
            nmGo.AddComponent<MultiplayerMenu>();
        }

        if (nm.NetworkConfig == null)
        {
            nm.NetworkConfig = new NetworkConfig();
        }

        nm.NetworkConfig.PlayerPrefab = networkRealPlayerPrefab;
        nm.NetworkConfig.NetworkTransport = transport;
        nm.NetworkConfig.EnableSceneManagement = true;

        EditorUtility.SetDirty(nmGo);
        EditorSceneManager.SaveScene(scene, MainMenuScenePath);
    }
}
#endif
