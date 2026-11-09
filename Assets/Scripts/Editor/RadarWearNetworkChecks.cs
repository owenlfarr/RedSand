#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
[InitializeOnLoad]public static class RadarWearNetworkChecks
{
 static RadarWearNetworkChecks(){EditorApplication.delayCall+=Requested;}
 static void Requested(){if(!File.Exists("Logs/RadarWearBuild.request")||EditorApplication.isPlayingOrWillChangePlaymode)return;File.Delete("Logs/RadarWearBuild.request");Build();}
 [MenuItem("Tools/Night Testing/Build Radar Wear Network Checks")]
 public static void Build(){Scene previous=SceneManager.GetActiveScene(),test=default;try{
  test=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(test);
  var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=Vector3.down*.5f;floor.transform.localScale=new Vector3(1000,1,1000);
  var actor=new GameObject("Radar test player",typeof(NetworkObject),typeof(Networking.OwnerNetworkTransform),typeof(NetworkPlayerMovementState),typeof(NetworkOxygenState));var body=actor.AddComponent<CharacterController>();body.height=2;body.radius=.4f;body.center=Vector3.up;var player=actor.AddComponent<PlayerController>();var camera=new GameObject("Owner camera",typeof(Camera));camera.transform.SetParent(actor.transform,false);camera.transform.localPosition=Vector3.up*1.6f;player.playerCamera=camera.transform;
  var prefab=PrefabUtility.SaveAsPrefabAsset(actor,"Assets/_Project/Tests/RadarWearPlayer.prefab");UnityEngine.Object.DestroyImmediate(actor);
  var root=new GameObject("NetworkManager");var transport=root.AddComponent<UnityTransport>();var manager=root.AddComponent<NetworkManager>();manager.NetworkConfig.NetworkTransport=transport;manager.NetworkConfig.PlayerPrefab=prefab;manager.NetworkConfig.ConnectionApproval=false;manager.NetworkConfig.EnableSceneManagement=true;manager.NetworkConfig.Prefabs.Add(new NetworkPrefab{Prefab=prefab});
  var radar=new GameObject("Radar",typeof(NetworkObject),typeof(RadarSystem)).GetComponent<RadarSystem>();radar.objectiveTransform=radar.transform;
  var solar=new GameObject("Solar station",typeof(NetworkObject),typeof(SolarFlareSystem)).GetComponent<SolarFlareSystem>();solar.transform.position=new Vector3(30,0,0);solar.fixPointTransform=solar.transform;solar.repairTime=.4f;solar.enabled=false;
  var drill=new GameObject("Drill",typeof(NetworkObject),typeof(DrillSystem)).GetComponent<DrillSystem>();drill.enabled=false;
  new GameObject("Radar multiplayer checks",typeof(NetworkObject),typeof(RadarWearNetworkTest));
  EditorSceneManager.SaveScene(test,"Assets/_Project/Tests/RadarWearNetworkTest.unity");AssetDatabase.SaveAssets();Directory.CreateDirectory("Logs/RadarWearBuild");
  var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{test.path},target=BuildTarget.StandaloneWindows64,locationPathName="Logs/RadarWearBuild/RadarWearTest.exe",options=BuildOptions.Development});File.WriteAllText("Logs/RadarWearBuildResult.txt",result.summary.result+" errors="+result.summary.totalErrors);
 }catch(Exception e){File.WriteAllText("Logs/RadarWearBuildResult.txt","FAIL "+e);Debug.LogException(e);}finally{if(test.IsValid()&&test.isLoaded)EditorSceneManager.CloseScene(test,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}}
}
#endif
