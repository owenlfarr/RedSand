#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
[InitializeOnLoad] public static class NightThreeChecks
{
 const string Armed="NightThreeChecks.Armed", Previous="NightThreeChecks.Previous";
 static double deadline,arrived;
 static NightThreeChecks(){EditorApplication.delayCall+=Begin;EditorApplication.playModeStateChanged+=Changed;EditorApplication.update+=Tick;}
 static void Begin(){
  if(!File.Exists("Logs/NightThree.request")||EditorApplication.isPlayingOrWillChangePlaymode)return;
  File.Delete("Logs/NightThree.request");
  try {
   new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
   File.WriteAllText("Logs/NightThreeIdentityResult.txt","PASS: all enabled build scenes have valid, unique NGO identities.");
   for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty){File.WriteAllText("Logs/NightThreePlayResult.txt","SKIPPED: scene has unsaved edits");return;}
   SessionState.SetString(Previous,SceneManager.GetActiveScene().path);
   EditorSceneManager.OpenScene("Assets/Scenes/Night 2.unity");
   SessionState.SetBool(Armed,true);EditorApplication.isPlaying=true;
  }catch(Exception e){File.WriteAllText("Logs/NightThreePlayResult.txt","FAIL "+e);}
 }
 static void Changed(PlayModeStateChange state){
  if(!SessionState.GetBool(Armed,false))return;
  if(state==PlayModeStateChange.EnteredPlayMode){
   deadline=EditorApplication.timeSinceStartup+45;arrived=0;
   var clock=UnityEngine.Object.FindFirstObjectByType<NightTimeManager>();
   clock.useCustomEndingTime=true;clock.endingSequenceTriggerTime=2;
  }
  if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Armed,false);EditorApplication.delayCall+=()=>{string path=SessionState.GetString(Previous,"");if(!string.IsNullOrEmpty(path))EditorSceneManager.OpenScene(path);};}
 }
 static void Tick(){
  if(!SessionState.GetBool(Armed,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling||deadline==0)return;
  if(SceneManager.GetActiveScene().name=="Night 3"){
   if(arrived==0){arrived=EditorApplication.timeSinceStartup;return;}if(EditorApplication.timeSinceStartup-arrived<2)return;
   var player=UnityEngine.Object.FindFirstObjectByType<PlayerController>();
   var power=UnityEngine.Object.FindFirstObjectByType<PowerSystem>();
   var flare=UnityEngine.Object.FindFirstObjectByType<SolarFlareSystem>();
   bool pass=player!=null&&player.isActiveAndEnabled&&power!=null&&Mathf.Approximately(power.outageCheckInterval,55)&&Mathf.Approximately(power.outageChance,.2f)&&flare!=null&&Mathf.Approximately(flare.flareChance,.21f);
   File.WriteAllText("Logs/NightThreePlayResult.txt",(pass?"PASS":"FAIL")+": actual Night 2 completion loaded Night 3; active player="+(player!=null&&player.isActiveAndEnabled)+"; Night 3 power/flare settings="+(power!=null&&flare!=null));
   deadline=0;EditorApplication.isPlaying=false;
  }else if(EditorApplication.timeSinceStartup>deadline){File.WriteAllText("Logs/NightThreePlayResult.txt","FAIL: transition timed out");deadline=0;EditorApplication.isPlaying=false;}
 }
}
#endif
