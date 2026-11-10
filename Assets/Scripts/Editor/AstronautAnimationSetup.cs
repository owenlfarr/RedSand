#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

public static class AstronautAnimationSetup
{
    public static bool Busy {get;private set;}
    public static bool Available=>File.Exists("Assets/Art/Animations/Astronaut/Jumping.fbx");
    const string ControllerPath="Assets/New Animator Controller.controller";
    const string Output="Assets/Animations/Player";
    static AnimationClip Extract(string source,string name,bool loop){
        var importer=(ModelImporter)AssetImporter.GetAtPath(source);
        var avatar=AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Models/astronaut skinned.fbx").OfType<Avatar>().First();
        if(importer.animationType!=ModelImporterAnimationType.Generic||importer.avatarSetup!=ModelImporterAvatarSetup.CopyFromOther||importer.sourceAvatar!=avatar){importer.animationType=ModelImporterAnimationType.Generic;importer.avatarSetup=ModelImporterAvatarSetup.CopyFromOther;importer.sourceAvatar=avatar;importer.SaveAndReimport();}
        var original=AssetDatabase.LoadAllAssetsAtPath(source).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
        var clip=UnityEngine.Object.Instantiate(original);clip.name=name;var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;settings.loopBlend=loop;AnimationUtility.SetAnimationClipSettings(clip,settings);
        // The CharacterController owns world motion; remove clip translation that would slide/double-jump the mesh.
        foreach(var binding in AnimationUtility.GetCurveBindings(clip).Where(b=>b.path=="Bip001"&&b.propertyName.StartsWith("m_LocalPosition."))){
            var curve=AnimationUtility.GetEditorCurve(clip,binding);var keys=curve.keys;
            float baseline=curve.Evaluate(0);bool vertical=binding.propertyName.EndsWith(".y");
            for(int i=0;i<keys.Length;i++){
                if(!vertical){keys[i].value=baseline;keys[i].inTangent=keys[i].outTangent=0;}
                else if(!loop&&keys[i].value>baseline){keys[i].value=baseline;keys[i].inTangent=keys[i].outTangent=0;}
            }
            curve.keys=keys;AnimationUtility.SetEditorCurve(clip,binding,curve);
        }
        // These source strafes carry a +/-90 degree heading offset relative to the astronaut rig.
        float heading=name.EndsWith("Left")?90:name.EndsWith("Right")?-90:0;
        if(heading!=0){
            var bindings=new EditorCurveBinding[4];var curves=new AnimationCurve[4];var result=new AnimationCurve[4];
            for(int axis=0;axis<4;axis++){bindings[axis]=EditorCurveBinding.FloatCurve("Bip001",typeof(Transform),"m_LocalRotation."+"xyzw"[axis]);curves[axis]=AnimationUtility.GetEditorCurve(clip,bindings[axis]);result[axis]=new AnimationCurve();}
            if(curves.Any(c=>c==null))throw new Exception("Strafe root rotation is missing: "+name);
            Quaternion correction=Quaternion.Euler(0,heading,0);
            int frames=Mathf.CeilToInt(clip.length*clip.frameRate);
            for(int frame=0;frame<=frames;frame++){float time=Mathf.Min(clip.length,frame/clip.frameRate);Quaternion q=correction*new Quaternion(curves[0].Evaluate(time),curves[1].Evaluate(time),curves[2].Evaluate(time),curves[3].Evaluate(time));q.Normalize();for(int axis=0;axis<4;axis++)result[axis].AddKey(time,q[axis]);}
            for(int axis=0;axis<4;axis++){for(int key=0;key<result[axis].length;key++)result[axis].SmoothTangents(key,0);AnimationUtility.SetEditorCurve(clip,bindings[axis],result[axis]);}clip.EnsureQuaternionContinuity();
        }
        string path=Output+"/"+name+".anim";var saved=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(saved==null){AssetDatabase.CreateAsset(clip,path);saved=clip;}else{EditorUtility.CopySerialized(clip,saved);UnityEngine.Object.DestroyImmediate(clip);EditorUtility.SetDirty(saved);}
        return saved;
    }
    [MenuItem("Tools/Player Animations/Install Directional and Jump Animations")]
    public static void Install(){
        if(Busy)return;Busy=true;
        try{
            Directory.CreateDirectory(Output);AssetDatabase.Refresh();
            var leftWalk=Extract("Assets/Art/Animations/Astronaut/Left Strafe Walking.fbx","WalkLeft",true);
            var rightWalk=Extract("Assets/Art/Animations/Astronaut/Right Strafe Walking.fbx","WalkRight",true);
            var leftRun=Extract("Assets/Art/Animations/Astronaut/Left Strafe.fbx","RunLeft",true);
            var rightRun=Extract("Assets/Art/Animations/Astronaut/Right Strafe.fbx","RunRight",true);
            var backRun=Extract("Assets/Art/Animations/Astronaut/Running Backward.fbx","RunBackward",true);
            var jumpClip=Extract("Assets/Art/Animations/Astronaut/Jumping.fbx","AstronautJump",false);
            var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Idle.anim");var walk=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Walk.anim");var back=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/WalkBack.anim");var run=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Sprint.anim");
            if(idle==null||walk==null||back==null||run==null)throw new Exception("Original locomotion clips are missing");
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);if(controller==null)throw new Exception("Existing player controller missing");
            var sm=controller.layers[0].stateMachine;
            foreach(var state in sm.states)sm.RemoveState(state.state);
            controller.parameters=Array.Empty<AnimatorControllerParameter>();
            foreach(string parameter in new[]{"velocityX","velocityZ","gait","jumpTime"})controller.AddParameter(parameter,AnimatorControllerParameterType.Float);
            var locomotion=sm.AddState("Locomotion",new Vector3(300,100));var jump=sm.AddState("Jump",new Vector3(300,300));
            BlendTree Direction(string name,AnimationClip forward,AnimationClip backward,AnimationClip left,AnimationClip right){
                var tree=new BlendTree{name=name,blendType=BlendTreeType.FreeformDirectional2D,blendParameter="velocityX",blendParameterY="velocityZ",useAutomaticThresholds=false};AssetDatabase.AddObjectToAsset(tree,controller);
                tree.AddChild(idle,Vector2.zero);tree.AddChild(forward,Vector2.up);tree.AddChild(backward,Vector2.down);tree.AddChild(left,Vector2.left);tree.AddChild(right,Vector2.right);return tree;
            }
            var walkTree=Direction("Walking directions",walk,back,leftWalk,rightWalk);var runTree=Direction("Running directions",run,backRun,leftRun,rightRun);
            var gait=new BlendTree{name="Walk and run",blendType=BlendTreeType.Simple1D,blendParameter="gait",useAutomaticThresholds=false};AssetDatabase.AddObjectToAsset(gait,controller);gait.AddChild(walkTree,0);gait.AddChild(runTree,1);
            locomotion.motion=gait;sm.defaultState=locomotion;
            jump.motion=jumpClip;jump.timeParameter="jumpTime";jump.timeParameterActive=true;jump.writeDefaultValues=false;locomotion.writeDefaultValues=false;
            var layers=controller.layers;layers[0].defaultWeight=1;controller.layers=layers;EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
            Validate();RenderPoses();
        }finally{Busy=false;}
    }
    public static void Validate(){
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NetworkRealPlayer.prefab");var body=prefab.GetComponent<PlayerBodyAnimator>();var animator=body.bodyAnimator;
        if(animator.runtimeAnimatorController!=AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath))throw new Exception("Player prefab uses a different animator controller");
        var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab);var target=model.GetComponent<PlayerBodyAnimator>().bodyAnimator;int count=0;
        foreach(var clip in AssetDatabase.LoadAllAssetsAtPath(ControllerPath).OfType<BlendTree>().SelectMany(t=>t.children).Select(c=>c.motion).OfType<AnimationClip>().Concat(new[]{AssetDatabase.LoadAssetAtPath<AnimationClip>(Output+"/AstronautJump.anim")}).Distinct()){
            var curves=AnimationUtility.GetCurveBindings(clip);if(curves.Length==0)throw new Exception("Empty clip: "+clip.name);
            foreach(var binding in curves)if(binding.path!=""&&target.transform.Find(binding.path)==null)throw new Exception("Unbound bone in "+clip.name+": "+binding.path);
            count++;
        }
        if(count!=10)throw new Exception("Expected ten original/new motion clips, found "+count);
        UnityEngine.Object.DestroyImmediate(model);Directory.CreateDirectory("Logs");File.WriteAllText("Logs/AstronautAnimationAssetResult.txt","PASS: ten motion clips bind to the existing generic skeleton; directional walk/run blend trees and controlled jump state installed in-place, original controller GUID preserved.");
    }
    public static void RenderPoses(){
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NetworkRealPlayer.prefab"));var animator=root.GetComponent<PlayerBodyAnimator>().bodyAnimator;
        foreach(var camera in root.GetComponentsInChildren<Camera>(true))camera.enabled=false;
        foreach(var listener in root.GetComponentsInChildren<AudioListener>(true))listener.enabled=false;
        var cameraObject=new GameObject("Animation preview",typeof(Camera));var preview=cameraObject.GetComponent<Camera>();preview.clearFlags=CameraClearFlags.SolidColor;preview.backgroundColor=new Color(.07f,.08f,.1f);preview.orthographic=true;
        var light=new GameObject("Preview light",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(35,-25,0);RenderSettings.ambientLight=Color.gray;
        var clips=new[]{"WalkLeft","WalkRight","RunLeft","RunRight","RunBackward","AstronautJump"};
        foreach(string name in clips){var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Output+"/"+name+".anim");var samples=name=="AstronautJump"?new[]{AstronautJumpTiming.Takeoff,AstronautJumpTiming.AirHold,AstronautJumpTiming.Landing,AstronautJumpTiming.RecoveryEnd}:new[]{.25f};
            foreach(float phase in samples){clip.SampleAnimation(animator.gameObject,clip.length*phase);var renderers=animator.GetComponentsInChildren<SkinnedMeshRenderer>();Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                preview.orthographicSize=Mathf.Max(1,bounds.extents.y*1.25f);preview.transform.position=bounds.center+new Vector3(3,1,5).normalized*bounds.size.y*3;preview.transform.LookAt(bounds.center);
                var previews=new System.Collections.Generic.List<GameObject>();foreach(var renderer in renderers){var baked=new Mesh();renderer.BakeMesh(baked);var visible=new GameObject("Baked preview",typeof(MeshFilter),typeof(MeshRenderer));visible.transform.SetPositionAndRotation(renderer.transform.position,renderer.transform.rotation);visible.transform.localScale=renderer.transform.lossyScale;visible.GetComponent<MeshFilter>().sharedMesh=baked;visible.GetComponent<MeshRenderer>().sharedMaterials=renderer.sharedMaterials;renderer.enabled=false;previews.Add(visible);}
                Bounds visibleBounds=previews[0].GetComponent<MeshRenderer>().bounds;foreach(var visible in previews)visibleBounds.Encapsulate(visible.GetComponent<MeshRenderer>().bounds);preview.orthographicSize=Mathf.Max(.1f,visibleBounds.extents.y*1.25f);preview.transform.position=visibleBounds.center+new Vector3(3,1,5).normalized*visibleBounds.size.y*3;preview.transform.LookAt(visibleBounds.center);
                var rt=new RenderTexture(512,512,24);preview.targetTexture=rt;preview.Render();RenderTexture.active=rt;var texture=new Texture2D(512,512,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,512,512),0,0);texture.Apply();File.WriteAllBytes("Logs/Astronaut-"+name+"-"+phase.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+".png",texture.EncodeToPNG());RenderTexture.active=null;preview.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(texture);foreach(var visible in previews){UnityEngine.Object.DestroyImmediate(visible.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(visible);}foreach(var renderer in renderers)renderer.enabled=true;
            }
        }
    }
    public static void InstallAndBuild(){
        try{EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");Install();BuildFixture();new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),target=BuildTarget.StandaloneWindows64,locationPathName="Logs/AstronautRelease/RedSand.exe",options=BuildOptions.Development});if(report.summary.result!=BuildResult.Succeeded||report.summary.totalErrors!=0)throw new Exception("Production build failed");File.WriteAllText("Logs/AstronautAnimationBuildResult.txt","PASS: animation fixture and all production scenes built with zero errors; scene identities valid.");
        }catch(Exception e){Directory.CreateDirectory("Logs");File.WriteAllText("Logs/AstronautAnimationBuildResult.txt","FAIL "+e);throw;}
    }
    public static void RebuildFixture(){EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");RenderPoses();BuildFixture();}
    static void BuildFixture(){
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=Vector3.down*.5f;floor.transform.localScale=new Vector3(200,1,200);
        var actor=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NetworkRealPlayer.prefab"));actor.name="Animation test player";actor.GetComponent<PlayerController>().enabled=false;actor.GetComponent<OxygenSystem>().enabled=false;
        var prefab=PrefabUtility.SaveAsPrefabAsset(actor,"Assets/_Project/Tests/AnimationNetworkPlayer.prefab");UnityEngine.Object.DestroyImmediate(actor);
        var root=new GameObject("NetworkManager");var transport=root.AddComponent<UnityTransport>();var manager=root.AddComponent<NetworkManager>();manager.NetworkConfig.NetworkTransport=transport;manager.NetworkConfig.PlayerPrefab=prefab;manager.NetworkConfig.ConnectionApproval=false;manager.NetworkConfig.EnableSceneManagement=true;manager.NetworkConfig.Prefabs.Add(new NetworkPrefab{Prefab=prefab});
        new GameObject("Astronaut animation checks",typeof(NetworkObject),typeof(AstronautAnimationNetworkTest));
        var scene=SceneManager.GetActiveScene();EditorSceneManager.SaveScene(scene,"Assets/_Project/Tests/AstronautAnimationNetworkTest.unity");AssetDatabase.SaveAssets();
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene.path},target=BuildTarget.StandaloneWindows64,locationPathName="Logs/AnimationNetworkBuild/AnimationTest.exe",options=BuildOptions.Development});if(report.summary.result!=BuildResult.Succeeded||report.summary.totalErrors!=0)throw new Exception("Animation fixture build failed");
    }
}
#endif
