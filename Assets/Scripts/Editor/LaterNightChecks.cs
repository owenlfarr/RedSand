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

// Run in the verification project, without replacing the user's open scene.
public static class LaterNightChecks
{
    static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
    public static void InstallAndBuild()
    {
        try {
            Directory.CreateDirectory("Logs");
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            CreateNight(4); CreateNight(5);
            var third = EditorSceneManager.OpenScene("Assets/Scenes/Night 3.unity");
            foreach (var clock in Components<NightTimeManager>(third)) clock.nextSceneName = "Night 4";
            EditorSceneManager.MarkSceneDirty(third); EditorSceneManager.SaveScene(third);
            var scenes = EditorBuildSettings.scenes.ToList();
            foreach (int n in new[] {4,5}) { string path = $"Assets/Scenes/Night {n}.unity"; if (!scenes.Any(s => s.path == path)) scenes.Add(new EditorBuildSettingsScene(path, true)); }
            EditorBuildSettings.scenes = scenes.ToArray(); AssetDatabase.SaveAssets();
            ValidateScenes(); RenderPlaceholders(); RenderTransitionCards(); BuildFixture();
            NightThreeFeatureChecks.Run(); BlindLungeSetup.Check();
            foreach(var check in new[]{"Logs/NightThreeFeaturesResult.txt","Logs/BlindLungeChecksResult.txt"}) if(!File.ReadAllText(check).StartsWith("PASS"))throw new Exception(File.ReadAllText(check));
            BlindLungeNetworkChecks.Build(); RadarWearNetworkChecks.Build();
            foreach(var check in new[]{"Logs/BlindNetworkBuildResult.txt","Logs/RadarWearBuildResult.txt"}) if(!File.ReadAllText(check).StartsWith("Succeeded errors=0"))throw new Exception(File.ReadAllText(check));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(), target = BuildTarget.StandaloneWindows64, locationPathName = "Logs/LaterNightsRelease/RedSand.exe", options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0) throw new Exception("Production build: " + report.summary.result + " errors=" + report.summary.totalErrors);
            File.WriteAllText("Logs/LaterNightsBuildResult.txt", "PASS: Nights 3/4/5 scene configuration and network identities, multiplayer fixture build, all production scenes build, zero errors.");
        } catch (Exception e) { File.WriteAllText("Logs/LaterNightsBuildResult.txt", "FAIL " + e); throw; }
    }
    public static void RebuildFixtureOnly(){EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");ValidateScenes();BuildFixture();}
    public static void RebuildTransferAndRelease()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");ValidateScenes();BuildFixture();
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),target=BuildTarget.StandaloneWindows64,locationPathName="Logs/LaterNightsRelease/RedSand.exe",options=BuildOptions.Development});
        if(report.summary.result!=BuildResult.Succeeded||report.summary.totalErrors!=0)throw new Exception("Final production build failed");
        File.WriteAllText("Logs/LaterNightsBuildResult.txt","PASS: final gameplay and regression sources built for every production scene and the transfer/encounter fixture; zero errors; canonical network scene IDs validated.");
    }
    static Material MakeMaterial(string name, Color color, bool transparent)
    {
        string path = "Assets/Materials/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, path); }
        mat.color = color;
        if (transparent) { mat.SetFloat("_Mode",3); mat.SetInt("_SrcBlend",1); mat.SetInt("_DstBlend",10); mat.SetInt("_ZWrite",0); mat.DisableKeyword("_ALPHABLEND_ON"); mat.EnableKeyword("_ALPHAPREMULTIPLY_ON"); mat.renderQueue=3000; }
        EditorUtility.SetDirty(mat); return mat;
    }
    public static SandStalker AddMonster(bool burrow, Vector3 position)
    {
        var root = new GameObject(burrow ? "Burrow Monster" : "Whisper Monster"); root.tag="Enemy"; root.transform.position = position;
        root.AddComponent<NetworkObject>();
        var body = root.AddComponent<CharacterController>(); body.height=2; body.center=Vector3.up; body.radius=.45f; body.stepOffset=.4f; body.skinWidth=.04f;
        var monster = root.AddComponent<SandStalker>(); monster.burrow=burrow; monster.speed=burrow?18:10.5f;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name=burrow?"Burrow cube placeholder":"Whisper translucent cube placeholder";
        UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>()); cube.transform.SetParent(root.transform,false); cube.transform.localPosition=Vector3.up; cube.transform.localScale=new Vector3(.95f,2,.95f);
        cube.GetComponent<Renderer>().sharedMaterial=MakeMaterial(burrow?"BurrowCube":"WhisperCube",burrow?new Color(.43f,.27f,.15f):new Color(.65f,.8f,.85f,.35f),!burrow); monster.visual=cube;
        if (burrow) {
            var trail = new GameObject("Approaching sand smoke"); trail.transform.SetParent(root.transform,false);trail.transform.localPosition=Vector3.up*.15f;
            var ps=trail.AddComponent<ParticleSystem>(); var main=ps.main; main.playOnAwake=false; main.startLifetime=1.5f; main.startSpeed=.8f;main.startSize=new ParticleSystem.MinMaxCurve(.35f,.8f);main.startColor=new Color(.64f,.51f,.35f,.55f);main.simulationSpace=ParticleSystemSimulationSpace.World; main.maxParticles=180;
            var emission=ps.emission;emission.rateOverTime=45;var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=18;shape.radius=.35f;trail.transform.localRotation=Quaternion.Euler(-90,0,0);
            var mat=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BurrowSmoke.mat");if(mat==null){mat=new Material(Shader.Find("Particles/Standard Unlit"));mat.SetFloat("_Mode",2);mat.SetInt("_SrcBlend",5);mat.SetInt("_DstBlend",10);mat.SetInt("_ZWrite",0);mat.EnableKeyword("_ALPHABLEND_ON");mat.renderQueue=3000;AssetDatabase.CreateAsset(mat,"Assets/Materials/BurrowSmoke.mat");}
            const string texturePath="Assets/Materials/BurrowSmoke.png";
            if(!File.Exists(texturePath)){var texture=new Texture2D(64,64,TextureFormat.RGBA32,false);for(int y=0;y<64;y++)for(int x=0;x<64;x++){float radius=new Vector2((x-31.5f)/31.5f,(y-31.5f)/31.5f).magnitude;float alpha=Mathf.Pow(Mathf.Clamp01(1-radius),1.4f)*(.5f+.5f*Mathf.PerlinNoise(x*.13f,y*.13f));texture.SetPixel(x,y,new Color(1,1,1,alpha));}texture.Apply();File.WriteAllBytes(texturePath,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(texturePath);}
            mat.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);EditorUtility.SetDirty(mat);
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=mat; ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); monster.smoke=ps;
        }
        return monster;
    }
    static void CreateNight(int number)
    {
        string path=$"Assets/Scenes/Night {number}.unity";
        if (File.Exists(path)) File.Copy("Assets/Scenes/Night 3.unity", path, true);
        else if (!AssetDatabase.CopyAsset("Assets/Scenes/Night 3.unity",path)) throw new Exception("Cannot copy Night 3");
        AssetDatabase.Refresh();var scene=EditorSceneManager.OpenScene(path);
        // Always tune from the unchanged Night 3 baseline so reruns cannot compound.
        float scale=number==4?1.05f:1.1025f;
        foreach(var ai in Components<ZombieAI>(scene)){ai.roamSpeed*=scale;ai.chaseSpeed*=scale;ai.stalkSpeed*=scale;ai.wanderJitter/=scale;ai.chaseMemoryTime*=Mathf.Sqrt(scale);}
        foreach(var ai in Components<BlindLungeMonster>(scene)){ai.roamSpeed*=scale;ai.lungeSpeed*=scale;ai.windupSeconds/=Mathf.Sqrt(scale);}
        foreach(var ai in Components<EyeMonster>(scene)){ai.roamSpeed*=scale;ai.chaseSpeed*=scale;}
        foreach(var ai in Components<PowerStationWatcher>(scene)){ai.roamSpeed*=scale;ai.reactionSeconds/=Mathf.Sqrt(scale);}
        foreach(var ai in Components<SurveyorMonster>(scene)){ai.aggressiveSpeed*=scale;ai.neglectThreshold/=Mathf.Sqrt(scale);}
        foreach(var rush in Components<RushMonsterEvent>(scene))rush.eventChance=Mathf.Min(1,rush.eventChance+(number-3)*.01f);
        foreach(var power in Components<PowerSystem>(scene))power.outageChance=number==4?.23f:.26f;
        foreach(var solar in Components<SolarFlareSystem>(scene))solar.flareChance+=.01f*(number-3);
        foreach(var clock in Components<NightTimeManager>(scene))clock.nextSceneName=number==4?"Night 5":"";
        Vector3 origin=Components<BlindLungeMonster>(scene).First().transform.position;
        var whisper=AddMonster(false,origin+new Vector3(-20,0,25));if(number==5)whisper.speed*=1.05f;
        PrefabUtility.SaveAsPrefabAsset(whisper.gameObject,"Assets/Prefabs/WhisperMonster.prefab");
        if(number==5){var burrow=AddMonster(true,origin+new Vector3(25,0,-20));PrefabUtility.SaveAsPrefabAsset(burrow.gameObject,"Assets/Prefabs/BurrowMonster.prefab");}
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }
    static void Assert(bool okay,string message){if(!okay)throw new Exception(message);}
    public static void ValidateScenes()
    {
        new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
        int checks=0;
        foreach(int n in new[]{1,2,3,4,5}){
            string path=n==1?"Assets/Scenes/Night1_MultiplayerTest.unity":$"Assets/Scenes/Night {n}.unity";
            var scene=EditorSceneManager.OpenScene(path);
            Assert(Components<BlindLungeMonster>(scene).Length==(n>=3?1:0),"Lunge restricted to nights three onward");checks++;
            Assert(PAScreecher.SupportsNight(scene.name)==(n>=3),"PA night gating");checks++;
            var monsters=Components<SandStalker>(scene);
            Assert(monsters.Count(m=>!m.burrow)==(n>=4?1:0)&&monsters.Count(m=>m.burrow)==(n>=5?1:0),"new monster scene gating");checks++;
            if(n>=3){Assert(Components<NightTimeManager>(scene).Single().nextSceneName==(n==5?"":$"Night {n+1}"),"progression wiring");checks++;}
            if(n>=4){Assert(RadarWearRules.Capacity(n)==12,"twelve scans");checks++;Assert(Mathf.Approximately(Components<PowerSystem>(scene).Single().outageChance,n==4?.23f:.26f),"outage tuning");checks++;}
            foreach(var monster in monsters){Assert(monster.visual!=null&&monster.visual.GetComponent<MeshRenderer>()!=null&&(!monster.burrow||monster.smoke!=null),"cube and smoke assigned");checks++;}
        }
        File.WriteAllText("Logs/LaterNightsSceneResult.txt","PASS "+checks+" scene assertions plus canonical NGO scene identity preflight for every enabled scene.");
    }
    static void RenderPlaceholders()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        RenderSettings.ambientLight=new Color(.35f,.35f,.4f);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(0,-.2f,0);floor.transform.localScale=new Vector3(20,.4f,20);
        floor.GetComponent<Renderer>().sharedMaterial=MakeMaterial("StalkerPreviewGround",new Color(.3f,.22f,.14f),false);
        AddMonster(false,new Vector3(-2,0,0));var burrow=AddMonster(true,new Vector3(2,0,0));
        var light=new GameObject("Preview light",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(45,-35,0);
        var camera=new GameObject("Preview camera",typeof(Camera)).GetComponent<Camera>();camera.transform.position=new Vector3(0,3,-10);camera.transform.LookAt(new Vector3(0,1,0));camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.04f,.055f);
        var rt=new RenderTexture(1280,720,24);camera.targetTexture=rt;
        Action<string> render=name=>{camera.Render();RenderTexture.active=rt;var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();File.WriteAllBytes("Logs/"+name+".png",texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);RenderTexture.active=null;};
        render("LaterNightsCubes");burrow.visual.SetActive(false);burrow.smoke.Simulate(1.2f,true,true);render("LaterNightsBurrowSmoke");
        camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);
    }
    static void RenderTransitionCards()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var camera=new GameObject("Card preview camera",typeof(Camera)).GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        var overlay=new GameObject("Card preview").AddComponent<NightTransition>();
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        typeof(NightTransition).GetMethod("Build",flags).Invoke(overlay,null);
        var canvas=overlay.GetComponentInChildren<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
        var rt=new RenderTexture(1280,720,24);camera.targetTexture=rt;
        typeof(NightTransition).GetMethod("SetBlack",flags).Invoke(overlay,new object[]{1f});
        foreach(var card in new[]{new[]{"8 AM","Transition8AM"},new[]{"","TransitionPlainBlack"},new[]{"NIGHT 4","TransitionNight4"}}){
            typeof(NightTransition).GetMethod("SetTitle",flags).Invoke(overlay,new object[]{card[0],1f});
            Canvas.ForceUpdateCanvases();foreach(var text in overlay.GetComponentsInChildren<TMPro.TMP_Text>())text.ForceMeshUpdate();camera.Render();RenderTexture.active=rt;
            var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();File.WriteAllBytes("Logs/"+card[1]+".png",texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);RenderTexture.active=null;
        }
        camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);
    }
    static void BuildFixture()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Ground";floor.transform.position=Vector3.down*.5f;floor.transform.localScale=new Vector3(1000,1,1000);
        var actor=new GameObject("Stalker test player",typeof(NetworkObject),typeof(Networking.OwnerNetworkTransform),typeof(NetworkPlayerMovementState),typeof(NetworkOxygenState));
        var body=actor.AddComponent<CharacterController>();body.height=2;body.radius=.4f;body.center=Vector3.up;
        var player=actor.AddComponent<PlayerController>();player.enabled=false;
        var camera=new GameObject("Owner view",typeof(Camera));camera.transform.SetParent(actor.transform,false);camera.transform.localPosition=Vector3.up*1.6f;camera.GetComponent<Camera>().enabled=false;player.playerCamera=camera.transform;
        camera.AddComponent<Light>().type=LightType.Spot;camera.GetComponent<Light>().range=25;camera.GetComponent<Light>().spotAngle=44;
        actor.AddComponent<OxygenSystem>().enabled=false;
        var prefab=PrefabUtility.SaveAsPrefabAsset(actor,"Assets/_Project/Tests/StalkerNetworkPlayer.prefab");UnityEngine.Object.DestroyImmediate(actor);
        var root=new GameObject("NetworkManager");var transport=root.AddComponent<UnityTransport>();var manager=root.AddComponent<NetworkManager>();manager.NetworkConfig.NetworkTransport=transport;manager.NetworkConfig.PlayerPrefab=prefab;manager.NetworkConfig.ConnectionApproval=false;manager.NetworkConfig.EnableSceneManagement=true;manager.NetworkConfig.Prefabs.Add(new NetworkPrefab{Prefab=prefab});
        AddMonster(false,Vector3.zero);AddMonster(true,new Vector3(80,0,0));
        new GameObject("Stalker checks",typeof(NetworkObject),typeof(StalkerNetworkTest));
        EditorSceneManager.SaveScene(scene,"Assets/_Project/Tests/StalkerNetworkTest.unity");AssetDatabase.SaveAssets();
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene.path,"Assets/Scenes/Night1_MultiplayerTest.unity","Assets/Scenes/Night 2.unity","Assets/Scenes/Night 3.unity","Assets/Scenes/Night 4.unity","Assets/Scenes/Night 5.unity","Assets/Scenes/MainMenu.unity"},target=BuildTarget.StandaloneWindows64,locationPathName="Logs/StalkerNetworkBuild/StalkerTest.exe",options=BuildOptions.Development});
        if(report.summary.result!=BuildResult.Succeeded||report.summary.totalErrors!=0)throw new Exception("Fixture build failed");
    }
}
#endif
