#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.SceneManagement;
[InitializeOnLoad] public static class NavigationMeshReadabilityFix {
 static NavigationMeshReadabilityFix(){EditorApplication.delayCall+=Run;}
 static void Run(){if(!File.Exists("Logs/NavigationReadability.request")||EditorApplication.isPlayingOrWillChangePlaymode)return;File.Delete("Logs/NavigationReadability.request");try{
 var paths=new HashSet<string>();var unreadable=new HashSet<string>();var previous=SceneManager.GetActiveScene();
 foreach(var path in new[]{"Assets/Scenes/Night 1.unity","Assets/Scenes/Night1_MultiplayerTest.unity","Assets/Scenes/Night 2.unity"}){
 var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.isLoaded;if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
 foreach(var source in TerrainRockNavigation.Sources(scene,out _)){if(source.sourceObject is Mesh mesh&&!mesh.isReadable){string asset=AssetDatabase.GetAssetPath(mesh);if(AssetImporter.GetAtPath(asset) is ModelImporter)paths.Add(asset);else unreadable.Add(asset+":"+mesh.name);}}
 if(opened)EditorSceneManager.CloseScene(scene,true);
 }
 Directory.CreateDirectory("Logs/NavigationMeshBackup");foreach(var path in paths){var importer=(ModelImporter)AssetImporter.GetAtPath(path);File.Copy(path+".meta","Logs/NavigationMeshBackup/"+AssetDatabase.AssetPathToGUID(path)+".meta",true);importer.isReadable=true;importer.SaveAndReimport();}
 AssetDatabase.SaveAssets();if(previous.isLoaded)SceneManager.SetActiveScene(previous);
 File.WriteAllText("Logs/NavigationReadabilityResult.txt","PASS enabled runtime mesh reading:\n"+string.Join("\n",paths)+"\nNon-imported unreadable meshes:\n"+string.Join("\n",unreadable));
 }catch(Exception e){File.WriteAllText("Logs/NavigationReadabilityResult.txt","FAIL "+e);Debug.LogException(e);}}
}
#endif
