#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public static class DeathPresentationChecks
{
    [MenuItem("Tools/Death Presentation/Build Network Regression %#&b")]
    public static void Build()
    {
        var previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            Directory.CreateDirectory("Assets/_Project/Tests");
            Directory.CreateDirectory("Logs/DeathPresentationTestBuild");
            var night2 = EditorSceneManager.OpenScene("Assets/Scenes/Night 2.unity", OpenSceneMode.Additive);
            var source = night2.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ZombieAI>(true)).First();
            Texture2D texture = source.zombieJumpscareImage;
            AudioClip sound = source.zombieJumpscareSound;
            EditorSceneManager.CloseScene(night2, true);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(400f, 1f, 400f);
            var managerObject = new GameObject("Death test network manager");
            var transport = managerObject.AddComponent<UnityTransport>();
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.ConnectionApproval = false;
            manager.NetworkConfig.PlayerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NetworkRealPlayer.prefab");
            manager.NetworkConfig.EnableSceneManagement = true;
            var coordinator = new GameObject("Death regression coordinator", typeof(NetworkObject));
            var test = coordinator.AddComponent<DeathPresentationNetworkTest>();
            test.scareTexture = texture;
            for (int i = 0; i < 2; i++)
            {
                var obj = new GameObject("Movement Detected " + (i + 1), typeof(NetworkObject), typeof(CharacterController));
                obj.transform.position = new Vector3(100f + i * 20f, 0f, 100f);
                var motor = obj.GetComponent<CharacterController>();
                motor.height = 2f; motor.center = Vector3.up; motor.radius = 0.4f;
                var ai = obj.AddComponent<ZombieAI>();
                ai.roamSpeed = ai.chaseSpeed = ai.stalkSpeed = 0f;
                ai.useFOVPriority = false; ai.detectionRadius = 0f;
                ai.zombieJumpscareImage = texture;
                ai.zombieJumpscareSound = sound;
                ai.jumpscareDisplayTime = 2f;
                if (i == 0) test.firstMonster = ai; else test.secondMonster = ai;
            }
            var shared = new GameObject("Shared jumpscare system", typeof(NetworkObject)).AddComponent<JumpscareSystem>();
            shared.jumpscareDuration = 2f;
            test.sharedSystem = shared;
            var rush = new GameObject("Rush presentation only", typeof(NetworkObject)).AddComponent<RushMonsterEvent>();
            rush.enabled = false; rush.rushJumpscareImage = texture; rush.imageDisplayDuration = 2f;
            test.rush = rush;
            var surveyor = new GameObject("Surveyor presentation only", typeof(NetworkObject)).AddComponent<SurveyorMonster>();
            surveyor.enabled = false; surveyor.surveyorJumpscareImage = texture;
            test.surveyor = surveyor;
            var watcherObject = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PowerStationWatcher.prefab"));
            watcherObject.transform.position = new Vector3(140f, 0f, 140f);
            test.watcher = watcherObject.GetComponent<PowerStationWatcher>();
            test.watcher.enabled = false; // Test drives the real validated encounter RPC explicitly.
            EditorSceneManager.SaveScene(scene, "Assets/_Project/Tests/DeathPresentationNetworkTest.unity");
            AssetDatabase.SaveAssets();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Tests/DeathPresentationNetworkTest.unity" },
                locationPathName = "Logs/DeathPresentationTestBuild/DeathPresentationTest.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            File.WriteAllText("Logs/DeathPresentationBuildResult.txt", report.summary.result + " errors=" + report.summary.totalErrors + " time=" + report.summary.totalTime);
        }
        catch (Exception exception)
        {
            File.WriteAllText("Logs/DeathPresentationBuildResult.txt", "FAIL " + exception);
            Debug.LogException(exception);
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
    }
}
#endif
