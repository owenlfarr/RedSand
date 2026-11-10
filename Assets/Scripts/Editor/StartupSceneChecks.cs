#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class StartupSceneChecks {
 [MenuItem("Tools/Networking/Repair Scene Network IDs")]
 public static void Repair(){
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play Mode before repairing scene IDs.");
  for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new Exception("Save open scene edits before repairing scene IDs.");
  var previous=EditorSceneManager.GetSceneManagerSetup();try{RepairScenes();}finally{EditorSceneManager.RestoreSceneManagerSetup(previous);}
 }
 public static void RepairAndBuild(){RepairScenes();ValidateGuard();Build();}
 public static void ValidateAndBuild(){new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);ValidateGuard();Build();}
 static void RepairScenes(){
  Directory.CreateDirectory("Logs");var output=new System.Text.StringBuilder();
  foreach(var entry in EditorBuildSettings.scenes.Where(s=>s.enabled)){
   var scene=EditorSceneManager.OpenScene(entry.path);
   foreach(var root in scene.GetRootGameObjects())foreach(var obj in root.GetComponentsInChildren<NetworkObject>(true)){
    var id=GlobalObjectId.GetGlobalObjectIdSlow(obj);var serialized=new SerializedObject(obj);output.AppendLine(entry.path+" | "+obj.name+" | "+id+" | hash="+serialized.FindProperty("GlobalObjectIdHash").longValue);
    typeof(NetworkObject).GetMethod("OnValidate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,null);
    if(PrefabUtility.IsPartOfPrefabInstance(obj))PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
    EditorUtility.SetDirty(obj);
   }
   EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  }
  AssetDatabase.SaveAssets();File.WriteAllText("Logs/StartupSceneIdentities.txt",output.ToString());
  new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
 }
 static void ValidateGuard(){
  string path="Assets/Scenes/Night 3.unity";byte[] original=File.ReadAllBytes(path);
  try{string yaml=File.ReadAllText(path);string stale=System.Text.RegularExpressions.Regex.Replace(yaml,@"(propertyPath: GlobalObjectIdHash\s+value: )\d+","${1}61371443");if(stale==yaml)throw new Exception("Regression requires an in-scene network prefab override");File.WriteAllText(path,stale);bool rejected=false;try{new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);}catch(UnityEditor.Build.BuildFailedException){rejected=true;}if(!rejected)throw new Exception("Build guard accepted a stale prefab network ID");}
  finally{File.WriteAllBytes(path,original);}
  new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);File.WriteAllText("Logs/StartupIdentityGuardResult.txt","PASS valid saved scenes accepted; stale prefab scene identity rejected.");
 }
 static void Build(){
  var report=UnityEditor.BuildPipeline.BuildPlayer(new UnityEditor.BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),target=UnityEditor.BuildTarget.StandaloneWindows64,locationPathName="Logs/StartupRelease/RedSand.exe",options=UnityEditor.BuildOptions.Development});
  if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Startup build failed");
  File.WriteAllText("Logs/StartupBuildResult.txt","PASS canonical scene/prefab network IDs saved; all production scenes built without errors.");
 }
}
#endif
