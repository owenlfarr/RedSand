#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
[InitializeOnLoad] public static class EyeFacingCheck {
 static EyeFacingCheck(){EditorApplication.delayCall += Run;}
 static void Run(){
  if(!File.Exists("Logs/EyeFacing.request") || EditorApplication.isPlayingOrWillChangePlaymode)return;
  File.Delete("Logs/EyeFacing.request");
  var eye=Object.FindFirstObjectByType<EyeMonster>(); if(eye==null)return;
  var clone=Object.Instantiate(eye.gameObject); clone.hideFlags=HideFlags.HideAndDontSave;
  var log=new StringBuilder();
  try {
   var e=clone.GetComponent<EyeMonster>();
   log.AppendLine("Root="+clone.transform.eulerAngles+" visual="+e.visual.transform.localEulerAngles);
   foreach(var a in clone.GetComponentsInChildren<Animator>(true)) {
    log.AppendLine("Animator="+a.name+" enabled="+a.enabled+" human="+a.isHuman+" avatar="+a.avatar+" controller="+a.runtimeAnimatorController);
    if(!a.enabled || !a.isHuman)continue;
    a.Rebind();
    foreach(var state in new[]{"Roam","Chase"}) foreach(float phase in new[]{0f,0.25f,0.5f,0.75f}) {
     a.Play(state,0,phase);a.Update(0);
     typeof(EyeMonster).GetField("animators",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(e,clone.GetComponentsInChildren<Animator>(true));
     typeof(EyeMonster).GetMethod("AlignVisualFacing",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(e,null);
     var up=(a.GetBoneTransform(HumanBodyBones.Head).position-a.GetBoneTransform(HumanBodyBones.Hips).position).normalized;
     var side=a.GetBoneTransform(HumanBodyBones.RightUpperArm).position-a.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
     var forward=Vector3.Cross(side,up).normalized;
     float upright=Vector3.Dot(up,clone.transform.up), facing=Vector3.Dot(forward,clone.transform.forward);
     log.AppendLine(state+" phase="+phase+" upright="+upright+" forward="+facing+((upright>0.99f&&facing>0.99f)?" PASS":" FAIL"));
    }
   }
  } finally {Object.DestroyImmediate(clone); File.WriteAllText("Logs/EyeFacingResult.txt",log.ToString());}
 }
}
#endif
