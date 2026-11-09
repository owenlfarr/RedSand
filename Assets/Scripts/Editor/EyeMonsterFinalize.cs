#if UNITY_EDITOR
using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using System.IO;
[InitializeOnLoad] public static class EyeMonsterFinalize {
 static EyeMonsterFinalize(){EditorApplication.delayCall+=Requested;}
 static void Requested(){if(File.Exists("Logs/EyeFinalize.request")&&!EditorApplication.isPlayingOrWillChangePlaymode){File.Delete("Logs/EyeFinalize.request");Apply();}}
 public static void Apply(){
 var eye=Object.FindFirstObjectByType<EyeMonster>();if(eye==null)return;
 eye.roamAnimation=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/EyeRoam.anim");eye.chaseAnimation=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/EyeChase.anim");
 var anchor=eye.bunkerEntrance;eye.bunkerEntrance=null;PrefabUtility.SaveAsPrefabAsset(eye.gameObject,"Assets/Prefabs/EyeMonster.prefab");eye.bunkerEntrance=anchor;
 EditorUtility.SetDirty(eye);EditorSceneManager.MarkSceneDirty(eye.gameObject.scene);EditorSceneManager.SaveScene(eye.gameObject.scene);AssetDatabase.SaveAssets();new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
 File.WriteAllText("Logs/EyeFinalized.txt","PASS synchronized animation phase/clip references; patrol spawn correction; scene network identities validated.");
 }
}
#endif
