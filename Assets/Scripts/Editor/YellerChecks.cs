#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

public static class YellerChecks
{
    public static Yeller Create(Vector3 position,string name="Yeller"){
        var go=new GameObject(name,typeof(NetworkObject));go.transform.position=position;var bug=go.AddComponent<Yeller>();
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name="Yeller cube placeholder";UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());cube.transform.SetParent(go.transform,false);cube.transform.localScale=Vector3.one*.3f;
        const string materialPath="Assets/Materials/YellerCube.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);if(material==null){material=new Material(Shader.Find("Standard"));material.color=new Color(.95f,.5f,.1f);AssetDatabase.CreateAsset(material,materialPath);}cube.GetComponent<Renderer>().sharedMaterial=material;bug.visual=cube;return bug;
    }
    public static void InstallAndBuild(){
        try{
            Directory.CreateDirectory("Logs");EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/Night 5.unity");
            foreach(var root in scene.GetRootGameObjects())if(root.name.StartsWith("Yeller - rock ")&&root.GetComponent<Yeller>()==null)UnityEngine.Object.DestroyImmediate(root);
            var existing=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Yeller>(true)).ToArray();
            if(existing.Length==0){
                var eye=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<EyeMonster>(true)).First();Vector3 center=eye.bunkerEntrance!=null?eye.bunkerEntrance.position:eye.transform.position;
                var placements=new System.Collections.Generic.List<Vector3>();
                foreach(var terrain in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Terrain>(true))){
                    var data=terrain.terrainData;var prototypes=data.treePrototypes;
                    foreach(var tree in data.treeInstances.OrderBy(t=>(terrain.transform.position+Vector3.Scale(t.position,data.size)-center).sqrMagnitude)){
                        var prefab=prototypes[tree.prototypeIndex].prefab;if(prefab==null||!(prefab.name.ToLowerInvariant().Contains("rock")||prefab.name.StartsWith("MarsTerrain")))continue;
                        Vector3 rock=terrain.transform.position+Vector3.Scale(tree.position,data.size);float distance=Vector3.ProjectOnPlane(rock-center,Vector3.up).magnitude;if(distance<25||distance>180)continue;
                        var renderer=prefab.GetComponentInChildren<MeshRenderer>();float radius=renderer!=null?new Vector2(renderer.bounds.extents.x,renderer.bounds.extents.z).magnitude*tree.widthScale:1;
                        Vector3 toward=Vector3.ProjectOnPlane(center-rock,Vector3.up).normalized;Vector3 position=rock+toward*(radius+.35f);position.y=terrain.SampleHeight(position)+terrain.transform.position.y+.22f;
                        if(placements.Any(p=>Vector3.Distance(p,position)<18))continue;
                        placements.Add(position);Create(position,"Yeller - rock "+placements.Count);if(placements.Count>=8)break;
                    }
                    if(placements.Count>=8)break;
                }
                if(placements.Count==0)throw new Exception("No suitable painted rocks found; no arbitrary floor placements were made.");
                var bug=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Yeller>(true)).First();PrefabUtility.SaveAsPrefabAsset(bug.gameObject,"Assets/Prefabs/Yeller.prefab");
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            }
            new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
            File.WriteAllText("Logs/YellerSceneResult.txt","PASS: Yellers placed beside painted rocks in Night 5; original scene identities retained and all enabled scene IDs validated.");
            BuildFixture();
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),target=BuildTarget.StandaloneWindows64,locationPathName="Logs/YellerRelease/RedSand.exe",options=BuildOptions.Development});
            if(report.summary.result!=BuildResult.Succeeded||report.summary.totalErrors!=0)throw new Exception("Production build failed: "+report.summary.result);
            File.WriteAllText("Logs/YellerBuildResult.txt","PASS: fixture and all production scenes built, zero errors, canonical network identities valid.");
        }catch(Exception e){File.WriteAllText("Logs/YellerBuildResult.txt","FAIL "+e);throw;}
    }
    public static void RepairPlacementsAndBuild(){
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/Night 5.unity");int removed=0;
        foreach(var root in scene.GetRootGameObjects())if(root.name.StartsWith("Yeller - rock ")&&root.GetComponent<Yeller>()==null){UnityEngine.Object.DestroyImmediate(root);removed++;}
        var bugs=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Yeller>(true)).ToArray();
        if(bugs.Length!=8||scene.GetRootGameObjects().Count(g=>g.name.StartsWith("Yeller - rock "))!=8)throw new Exception("Expected exactly eight valid Yeller rock placements");
        foreach(var bug in bugs)if(bug.visual==null||GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(bug.gameObject)>0)throw new Exception("Missing Yeller script/visual");
        PrefabUtility.SaveAsPrefabAsset(bugs[0].gameObject,"Assets/Prefabs/Yeller.prefab");EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        if(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Yeller.prefab").GetComponent<Yeller>()==null)throw new Exception("Invalid Yeller prefab");
        new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
        File.WriteAllText("Logs/YellerSceneResult.txt","PASS: exactly eight valid Yeller rock placements, cube references/prefab valid, no missing Yeller scripts; original scene IDs retained. Removed "+removed+" stale import placeholders.");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),target=BuildTarget.StandaloneWindows64,locationPathName="Logs/YellerRelease/RedSand.exe",options=BuildOptions.Development});
        if(report.summary.result!=BuildResult.Succeeded||report.summary.totalErrors!=0)throw new Exception("Production build failed");
        File.WriteAllText("Logs/YellerBuildResult.txt","PASS: repaired Night 5 and all production scenes built with zero errors; network identities valid; exactly eight valid Yellers.");
    }
    public static void RebuildFixture(){EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");BuildFixture();}
    static void Body(GameObject go){var motor=go.AddComponent<CharacterController>();motor.height=2;motor.center=Vector3.up;motor.radius=.4f;}
    static void BuildFixture(){
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Ground";floor.transform.position=Vector3.down*.5f;floor.transform.localScale=new Vector3(500,1,500);
        var actor=new GameObject("Yeller test player",typeof(NetworkObject),typeof(Networking.OwnerNetworkTransform),typeof(NetworkPlayerMovementState),typeof(NetworkOxygenState));Body(actor);var player=actor.AddComponent<PlayerController>();player.enabled=false;var camera=new GameObject("Owner view",typeof(Camera),typeof(AudioListener));camera.tag="MainCamera";camera.transform.SetParent(actor.transform,false);camera.transform.localPosition=Vector3.up*1.6f;player.playerCamera=camera.transform;actor.AddComponent<OxygenSystem>().enabled=false;
        var prefab=PrefabUtility.SaveAsPrefabAsset(actor,"Assets/_Project/Tests/YellerPlayer.prefab");UnityEngine.Object.DestroyImmediate(actor);
        var root=new GameObject("NetworkManager");var transport=root.AddComponent<UnityTransport>();var manager=root.AddComponent<NetworkManager>();manager.NetworkConfig.NetworkTransport=transport;manager.NetworkConfig.PlayerPrefab=prefab;manager.NetworkConfig.ConnectionApproval=false;manager.NetworkConfig.EnableSceneManagement=true;manager.NetworkConfig.Prefabs.Add(new NetworkPrefab{Prefab=prefab});
        Create(new Vector3(0,.2f,0),"Primary Yeller");Create(new Vector3(20,.2f,0),"Second Yeller");
        var zombieRoot=new GameObject("Motion",typeof(NetworkObject));Body(zombieRoot);var zombie=zombieRoot.AddComponent<ZombieAI>();zombie.chaseSpeed=.5f;zombie.roamSpeed=0;zombie.stalkSpeed=0;zombie.enabled=false;zombieRoot.transform.position=new Vector3(-25,0,0);
        var eyeRoot=new GameObject("Eye",typeof(NetworkObject));Body(eyeRoot);var eye=eyeRoot.AddComponent<EyeMonster>();eye.chaseSpeed=.5f;eye.roamSpeed=0;eye.enabled=false;eyeRoot.transform.position=new Vector3(-22,0,-10);
        var blindRoot=new GameObject("Lunge",typeof(NetworkObject));Body(blindRoot);var blind=blindRoot.AddComponent<BlindLungeMonster>();blind.lungeSpeed=.5f;blind.roamSpeed=0;blind.enabled=false;blindRoot.transform.position=new Vector3(-20,0,10);
        var watcherRoot=new GameObject("Watcher",typeof(NetworkObject));Body(watcherRoot);var watcher=watcherRoot.AddComponent<PowerStationWatcher>();watcher.roamSpeed=.25f;watcher.enabled=false;watcherRoot.transform.position=new Vector3(-30,0,15);
        var objective=new GameObject("Surveyor objective");objective.transform.position=new Vector3(0,0,80);var surveyor=new GameObject("Surveyor",typeof(NetworkObject),typeof(SurveyorMonster)).GetComponent<SurveyorMonster>();surveyor.objectiveTransform=objective.transform;surveyor.startingDistance=65;surveyor.aggressiveSpeed=.5f;surveyor.enabled=false;surveyor.transform.position=new Vector3(30,0,0);
        foreach(bool burrow in new[]{false,true}){var stalker=LaterNightChecks.AddMonster(burrow,burrow?new Vector3(25,0,15):new Vector3(25,0,-15));stalker.speed=.5f;stalker.enabled=false;}
        new GameObject("Yeller multiplayer checks",typeof(NetworkObject),typeof(YellerNetworkTest));
        EditorSceneManager.SaveScene(scene,"Assets/_Project/Tests/YellerNetworkTest.unity");AssetDatabase.SaveAssets();
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene.path},target=BuildTarget.StandaloneWindows64,locationPathName="Logs/YellerNetworkBuild/YellerTest.exe",options=BuildOptions.Development});
        if(report.summary.result!=BuildResult.Succeeded||report.summary.totalErrors!=0)throw new Exception("Yeller fixture build failed");
    }
}
#endif
