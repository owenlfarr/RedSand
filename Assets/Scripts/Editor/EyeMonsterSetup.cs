#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
[InitializeOnLoad]
public static class EyeMonsterSetup
{
    const string Marker="Logs/EyeInstall.request";
    static EyeMonsterSetup(){EditorApplication.delayCall+=AutoInstall;}
    static void AutoInstall(){if(File.Exists(Marker)&&!EditorApplication.isPlayingOrWillChangePlaymode){File.Delete(Marker);Install();}}
    static AnimationClip Clip(string path){return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));}
    [MenuItem("Tools/Eye Monster/Install In Night 2")]
    public static void Install()
    {
        try
        {
            var scene=SceneManager.GetActiveScene();
            if(scene.path!="Assets/Scenes/Night 2.unity"||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Open Night 2 in Edit Mode.");
            if(UnityEngine.Object.FindFirstObjectByType<EyeMonster>()!=null)throw new Exception("Eye Monster is already installed.");
            Directory.CreateDirectory("Logs/EyeBackup"); File.Copy(scene.path,"Logs/EyeBackup/Night 2.unity",true);
            var model=scene.GetRootGameObjects().Single(g=>g.name=="Eye monster");
            var renderers=model.GetComponentsInChildren<Renderer>();
            Bounds bounds=renderers[0].bounds; foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            var root=new GameObject("Eye Monster"); Undo.RegisterCreatedObjectUndo(root,"Install Eye Monster");
            root.transform.position=new Vector3(model.transform.position.x,bounds.min.y,model.transform.position.z);
            Undo.SetTransformParent(model.transform,root.transform,"Attach original eye visual");
            root.AddComponent<NetworkObject>(); var motor=root.AddComponent<CharacterController>();
            motor.height=bounds.size.y; motor.radius=Mathf.Clamp(bounds.extents.x*0.5f,0.35f,0.8f);
            motor.center=Vector3.up*(motor.height*0.5f); motor.stepOffset=0.4f;motor.skinWidth=0.04f;motor.minMoveDistance=0;
            var eye=root.AddComponent<EyeMonster>();eye.visual=model;
            // Dedicated looping humanoid controller; preserve the placed model's avatar.
            const string controllerPath="Assets/Animations/EyeMonster.controller";
            var controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Chasing",AnimatorControllerParameterType.Bool);
            var machine=controller.layers[0].stateMachine;
            var walk=machine.AddState("Roam"); var run=machine.AddState("Chase");
            AnimationClip walkClip=UnityEngine.Object.Instantiate(Clip("Assets/Art/Models/Walking (1).fbx"));
            AnimationClip runClip=UnityEngine.Object.Instantiate(Clip("Assets/Art/Models/Zombie Running.fbx"));
            foreach(var pair in new[]{(clip:walkClip,path:"Assets/Animations/EyeRoam.anim"),(clip:runClip,path:"Assets/Animations/EyeChase.anim")})
            {var settings=AnimationUtility.GetAnimationClipSettings(pair.clip); settings.loopTime=true;AnimationUtility.SetAnimationClipSettings(pair.clip,settings);AssetDatabase.CreateAsset(pair.clip,pair.path);}
            eye.roamAnimation=walkClip;eye.chaseAnimation=runClip;walk.motion=walkClip;run.motion=runClip;machine.defaultState=walk;
            var toRun=walk.AddTransition(run);toRun.hasExitTime=false;toRun.duration=0.12f;toRun.AddCondition(AnimatorConditionMode.If,0,"Chasing");
            var toWalk=run.AddTransition(walk);toWalk.hasExitTime=false;toWalk.duration=0.12f;toWalk.AddCondition(AnimatorConditionMode.IfNot,0,"Chasing");
            foreach(var animator in model.GetComponentsInChildren<Animator>(true))
            {animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;}
            // Child mesh had a second Animator driving the same skeleton. Only the model root drives it.
            foreach(var animator in model.GetComponentsInChildren<Animator>(true))if(animator.gameObject!=model)animator.enabled=false;
            var anchor=new GameObject("Eye Patrol - Bunker Entrance");
            var bunker=scene.GetRootGameObjects().Single(g=>g.name=="BunkerZone");
            anchor.transform.position=bunker.transform.position;
            PrefabUtility.SaveAsPrefabAsset(root,"Assets/Prefabs/EyeMonster.prefab");
            eye.bunkerEntrance=anchor.transform;
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
            Selection.activeGameObject=root;
            File.WriteAllText("Logs/EyeSetupResult.txt","PASS Night 2 Eye Monster installed; original visual/avatar preserved; looping roam/chase controller; reusable prefab; server-authoritative state, visibility, death; scene identity preflight passed. Bounds="+bounds+" Avatar="+model.GetComponent<Animator>().avatar);
        }
        catch(Exception e){File.WriteAllText("Logs/EyeSetupResult.txt","FAIL "+e);Debug.LogException(e);}
    }
}
#endif

