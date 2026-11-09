#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;
[InitializeOnLoad]public static class BlindLungeNetworkChecks
{
 static BlindLungeNetworkChecks(){EditorApplication.delayCall+=Requested;}
 static void Requested(){if(!File.Exists("Logs/BlindNetworkBuild.request")||EditorApplication.isPlayingOrWillChangePlaymode)return;File.Delete("Logs/BlindNetworkBuild.request");Build();}
 [MenuItem("Tools/Blind Lunge/Build Multiplayer Checks")]
 public static void Build(){var previous=SceneManager.GetActiveScene();Scene test=default;try{
  test=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(test);
  var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Ground";floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(1000,1,1000);
  var actor=new GameObject("Blind network test player");actor.AddComponent<NetworkObject>();actor.AddComponent<Networking.OwnerNetworkTransform>();actor.AddComponent<NetworkPlayerMovementState>();actor.AddComponent<NetworkOxygenState>();var body=actor.AddComponent<CharacterController>();body.height=2;body.radius=.4f;body.center=Vector3.up;
  var player=actor.AddComponent<PlayerController>();player.enabled=false;var view=new GameObject("Owner view");view.transform.SetParent(actor.transform,false);view.transform.localPosition=Vector3.up*1.6f;view.AddComponent<Camera>().enabled=false;player.playerCamera=view.transform;actor.AddComponent<RockThrower>();
  var prefab=PrefabUtility.SaveAsPrefabAsset(actor,"Assets/_Project/Tests/BlindNetworkPlayer.prefab");UnityEngine.Object.DestroyImmediate(actor);
  var root=new GameObject("NetworkManager");var transport=root.AddComponent<UnityTransport>();var manager=root.AddComponent<NetworkManager>();manager.NetworkConfig.NetworkTransport=transport;manager.NetworkConfig.PlayerPrefab=prefab;manager.NetworkConfig.ConnectionApproval=false;manager.NetworkConfig.EnableSceneManagement=true;manager.NetworkConfig.Prefabs.Add(new NetworkPrefab{Prefab=prefab});
  var monster=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BlindLungeMonster.prefab"),test);monster.transform.position=Vector3.zero;
  var smallRock=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SmallRockPickupSetup.PrefabPath),test);smallRock.transform.position=new Vector3(80,.12f,1);smallRock.GetComponent<SmallRockPickup>().pickupId=0;
  new GameObject("Multiplayer checks",typeof(NetworkObject),typeof(BlindLungeNetworkTest));
  var clock=new GameObject("PA network clock",typeof(NetworkObject),typeof(NightTimeManager)).GetComponent<NightTimeManager>();clock.enabled=false;
  Directory.CreateDirectory("Assets/_Project/Tests/BlindPA");AssetDatabase.Refresh();
  EditorSceneManager.SaveScene(test,"Assets/_Project/Tests/BlindPA/Night 3.unity");AssetDatabase.SaveAssets();Directory.CreateDirectory("Logs/BlindNetworkBuild");
  var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{test.path},target=BuildTarget.StandaloneWindows64,locationPathName="Logs/BlindNetworkBuild/BlindNetworkTest.exe",options=BuildOptions.Development});
  File.WriteAllText("Logs/BlindNetworkBuildResult.txt",report.summary.result+" errors="+report.summary.totalErrors);
 }catch(Exception e){File.WriteAllText("Logs/BlindNetworkBuildResult.txt","FAIL "+e);Debug.LogException(e);}finally{if(test.IsValid()&&test.isLoaded)EditorSceneManager.CloseScene(test,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}}
}
#endif
