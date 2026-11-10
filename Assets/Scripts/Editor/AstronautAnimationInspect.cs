#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
public static class AstronautAnimationInspect
{
 public static void Run(){
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NetworkRealPlayer.prefab"));var body=root.GetComponent<PlayerBodyAnimator>();var animator=body.bodyAnimator;
  var result=new StringBuilder();result.AppendLine("Player avatar: "+animator.avatar+" human="+animator.avatar.isHuman+" valid="+animator.avatar.isValid);
  foreach(string file in new[]{"Jumping","Left Strafe Walking","Left Strafe","Right Strafe Walking","Right Strafe","Running Backward"}){
   string path="Assets/Art/Animations/Astronaut/"+file+".fbx";var clip=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
   var bindings=AnimationUtility.GetCurveBindings(clip);int matched=bindings.Count(b=>b.path==""||animator.transform.Find(b.path)!=null);
   result.AppendLine(file+" duration="+clip.length+" fps="+clip.frameRate+" bindings="+bindings.Length+" matched="+matched+" paths="+string.Join(",",bindings.Select(b=>b.path).Distinct().Take(4)));
   if(file=="Jumping"){
    foreach(var binding in bindings.Where(b=>b.path=="Bip001"&&b.propertyName.StartsWith("m_LocalPosition"))){var curve=AnimationUtility.GetEditorCurve(clip,binding);result.AppendLine(binding.path+" "+binding.propertyName+" "+string.Join(",",curve.keys.Select(k=>$"{k.time:F3}:{k.value:F3}")));}
    var transforms=animator.GetComponentsInChildren<Transform>();var feet=transforms.Where(t=>t.name.ToLowerInvariant().Contains("foot")).ToArray();
    for(int i=0;i<=30;i++){float time=clip.length*i/30;clip.SampleAnimation(animator.gameObject,time);result.AppendLine($"sample {time:F3} "+string.Join(" ",feet.Select(f=>$"{f.name}:{animator.transform.InverseTransformPoint(f.position).y:F3}")));}
   }
  }
  Directory.CreateDirectory("Logs");File.WriteAllText("Logs/AstronautAnimationInspection.txt",result.ToString());
 }
}
#endif
