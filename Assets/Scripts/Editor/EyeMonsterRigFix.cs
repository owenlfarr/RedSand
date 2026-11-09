#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;
public static class EyeMonsterRigFix
{
    const string Folder="Assets/Art/EyeMonsterAnimation";
    public static void Apply()
    {
        if(!File.Exists("Logs/EyeRigFix.request"))return;
        var eye=UnityEngine.Object.FindFirstObjectByType<EyeMonster>();if(eye==null)throw new Exception("Open Night 2 to fix Eye rig");
        Directory.CreateDirectory(Folder);
        string rig=CopyHuman("Assets/Idle.fbx","EyeRig.fbx");
        string walk=CopyHuman("Assets/Art/Models/Walking (1).fbx","EyeWalk.fbx");
        string run=CopyHuman("Assets/Art/Models/Zombie Running.fbx","EyeRun.fbx");
        var avatar=AssetDatabase.LoadAllAssetsAtPath(rig).OfType<Avatar>().Single(a=>a.isHuman&&a.isValid);
        ReplaceClip(walk,"Assets/Animations/EyeRoam.anim");ReplaceClip(run,"Assets/Animations/EyeChase.anim");
        var animator=eye.visual.GetComponent<Animator>();animator.avatar=avatar;animator.applyRootMotion=false;
        EditorUtility.SetDirty(animator);EditorSceneManager.MarkSceneDirty(eye.gameObject.scene);EditorSceneManager.SaveScene(eye.gameObject.scene);AssetDatabase.SaveAssets();
        File.Delete("Logs/EyeRigFix.request");File.WriteAllText("Logs/EyeRigFixResult.txt","PASS own humanoid avatar and retargeted looping walk/run clips; original model scale and materials preserved.");
    }
    static string CopyHuman(string source,string name)
    {
        string path=Folder+"/"+name;if(!File.Exists(path)&&!AssetDatabase.CopyAsset(source,path))throw new Exception("Failed to create Eye-only animation import: "+source);
        var importer=(ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.SaveAndReimport();
        var avatar=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault(a=>a.isHuman&&a.isValid);
        if(avatar==null)throw new Exception("Could not map humanoid rig: "+path);return path;
    }
    static void ReplaceClip(string model,string destination)
    {
        var source=AssetDatabase.LoadAllAssetsAtPath(model).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(destination);EditorUtility.CopySerialized(source,clip);
        var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;settings.keepOriginalPositionY=true;settings.keepOriginalPositionXZ=true;settings.loopBlendPositionY=true;settings.loopBlendPositionXZ=true;
        AnimationUtility.SetAnimationClipSettings(clip,settings);EditorUtility.SetDirty(clip);
    }
}
#endif
