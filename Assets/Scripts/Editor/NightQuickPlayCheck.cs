#if UNITY_EDITOR
using System;using System.IO;using System.Reflection;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.SceneManagement;
[InitializeOnLoad]public static class NightQuickPlayCheck {
 const string Armed="NightQuickPlayCheck.Armed";static double deadline;static double arrived;
 static NightQuickPlayCheck(){EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=Changed;EditorApplication.delayCall+=Begin;}
 static void Begin(){if(!File.Exists("Logs/NightQuickPlay.request")||EditorApplication.isPlayingOrWillChangePlaymode)return;File.Delete("Logs/NightQuickPlay.request");if(SceneManager.GetActiveScene().isDirty){File.WriteAllText("Logs/NightQuickPlayResult.txt","SKIPPED: current scene has unsaved edits");return;}typeof(Editor).Assembly.GetType("UnityEditor.LogEntries").GetMethod("Clear",BindingFlags.Static|BindingFlags.Public).Invoke(null,null);EditorSceneManager.OpenScene("Assets/Scenes/Night 1.unity");SessionState.SetBool(Armed,true);EditorApplication.isPlaying=true;}
 static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Armed,false))return;if(state==PlayModeStateChange.EnteredPlayMode)deadline=EditorApplication.timeSinceStartup+25;if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Armed,false);EditorApplication.delayCall+=()=>EditorSceneManager.OpenScene("Assets/Scenes/Night 2.unity");}}
 static void Tick(){if(!SessionState.GetBool(Armed,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling||deadline==0)return;if(SceneManager.GetActiveScene().name=="Night 2"){if(arrived==0){arrived=EditorApplication.timeSinceStartup;return;}if(EditorApplication.timeSinceStartup-arrived<3)return;File.WriteAllText("Logs/NightQuickPlayResult.txt","PASS actual Night 1 -> Night 2 transition; Eye Monster present="+(UnityEngine.Object.FindFirstObjectByType<EyeMonster>()!=null));EditorApplication.isPlaying=false;deadline=0;}else if(EditorApplication.timeSinceStartup>deadline){File.WriteAllText("Logs/NightQuickPlayResult.txt","FAIL five-second night did not reach Night 2");EditorApplication.isPlaying=false;deadline=0;}}
}
#endif

