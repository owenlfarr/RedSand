#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using UnityEditor;
public static class PartyReadyAudioChecks {
 public static void Build(){
  AssetDatabase.Refresh();int count=0;
  foreach(var folder in new[]{"Bunker","Surface"})foreach(var name in new[]{"Step1","Step2","Step3","Step4","Jump","Land"}){
   var clip=Resources.Load<AudioClip>("PlayerMovement/"+folder+"/"+name);if(clip==null||clip.channels!=1||clip.length<.1f)throw new Exception("Movement audio missing/invalid: "+folder+"/"+name);count++;
  }
  new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
  File.WriteAllText("Logs/PartyReadyAudioAssets.txt","PASS "+count+" mono movement sound assets and production network scene identities.");
  StartupSceneChecks.ValidateAndBuild();
 }
}
#endif
