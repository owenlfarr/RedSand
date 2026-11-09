#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.SceneManagement;
[InitializeOnLoad] public static class NightQuickTestSetup
{
 const string Request="Logs/NightQuickTest.request";
 static NightQuickTestSetup(){EditorApplication.delayCall+=Requested;}
 static void Requested(){if(!File.Exists(Request)||EditorApplication.isPlayingOrWillChangePlaymode)return;File.Delete(Request);Apply();}
 [Serializable]public class Entry{public string scene;public bool custom;public float duration;public string next;}
 [Serializable]public class Backup{public Entry[] entries;}
 const string BackupPath="Assets/_Project/Night1QuickTestBackup.json";
 [MenuItem("Tools/Night Testing/Enable Five Second Night 1")]
 public static void Apply(){try{
 var notes=new List<string>();DumpConsole();
 var active=SceneManager.GetActiveScene();var originals=new List<Entry>();
 foreach(string path in new[]{"Assets/Scenes/Night 1.unity","Assets/Scenes/Night1_MultiplayerTest.unity"}){
 var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.isLoaded;if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
 var clock=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<NightTimeManager>(true)).Single();
 originals.Add(new Entry{scene=path,custom=clock.useCustomEndingTime,duration=clock.endingSequenceTriggerTime,next=clock.nextSceneName});
 clock.useCustomEndingTime=true;clock.endingSequenceTriggerTime=5;clock.nextSceneName="Night 2";EditorUtility.SetDirty(clock);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);notes.Add(path+": 5 seconds -> Night 2");if(opened)EditorSceneManager.CloseScene(scene,true);
 }
 if(!File.Exists(BackupPath))File.WriteAllText(BackupPath,JsonUtility.ToJson(new Backup{entries=originals.ToArray()},true));
 Directory.CreateDirectory("Logs/TerrainErrorBackup");
 foreach(string guid in AssetDatabase.FindAssets("t:TerrainData")){
 string path=AssetDatabase.GUIDToAssetPath(guid);var data=AssetDatabase.LoadAssetAtPath<TerrainData>(path);var prototypes=data.treePrototypes;if(!prototypes.Any(p=>p.prefab==null))continue;
 File.Copy(path,"Logs/TerrainErrorBackup/"+guid+".asset",true);
 int[] remap=new int[prototypes.Length];int next=0;for(int i=0;i<remap.Length;i++)remap[i]=prototypes[i].prefab==null?-1:next++;
 var instances=data.treeInstances.Where(t=>t.prototypeIndex>=0&&t.prototypeIndex<remap.Length&&remap[t.prototypeIndex]>=0).Select(t=>{t.prototypeIndex=remap[t.prototypeIndex];return t;}).ToArray();
 data.treeInstances=Array.Empty<TreeInstance>();data.treePrototypes=prototypes.Where(p=>p.prefab!=null).ToArray();data.SetTreeInstances(instances,false);EditorUtility.SetDirty(data);notes.Add("Repaired missing tree prototype: "+path);
 }
 AssetDatabase.SaveAssets();if(active.isLoaded)SceneManager.SetActiveScene(active);new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
 File.WriteAllText("Logs/NightQuickTestResult.txt","PASS\n"+string.Join("\n",notes));
 }catch(Exception e){File.WriteAllText("Logs/NightQuickTestResult.txt","FAIL "+e);Debug.LogException(e);}}
 [MenuItem("Tools/Night Testing/Restore Normal Night 1")]
 public static void Restore(){if(!File.Exists(BackupPath)||EditorApplication.isPlayingOrWillChangePlaymode)return;var backup=JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath));foreach(var entry in backup.entries){var scene=SceneManager.GetSceneByPath(entry.scene);bool opened=!scene.isLoaded;if(opened)scene=EditorSceneManager.OpenScene(entry.scene,OpenSceneMode.Additive);var clock=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<NightTimeManager>(true)).Single();clock.useCustomEndingTime=entry.custom;clock.endingSequenceTriggerTime=entry.duration;clock.nextSceneName=entry.next;EditorUtility.SetDirty(clock);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);if(opened)EditorSceneManager.CloseScene(scene,true);}AssetDatabase.SaveAssets();Debug.Log("[Night Testing] Original Night 1 timing restored.");}
 static void DumpConsole(){var assembly=typeof(Editor).Assembly;var logs=assembly.GetType("UnityEditor.LogEntries");var entryType=assembly.GetType("UnityEditor.LogEntry");var flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;var count=(int)logs.GetMethod("GetCount",flags).Invoke(null,null);var entry=Activator.CreateInstance(entryType);var text=new List<string>();logs.GetMethod("StartGettingEntries",flags).Invoke(null,null);try{for(int i=Mathf.Max(0,count-70);i<count;i++){logs.GetMethod("GetEntryInternal",flags).Invoke(null,new[]{(object)i,entry});text.Add(entryType.GetField("mode").GetValue(entry)+": "+entryType.GetField("message").GetValue(entry));}}finally{logs.GetMethod("EndGettingEntries",flags).Invoke(null,null);}File.WriteAllText("Logs/CurrentUnityConsole.txt",string.Join("\n",text));}
}
#endif
