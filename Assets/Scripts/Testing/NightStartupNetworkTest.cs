#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Collections;
using Networking;
public sealed class NightStartupNetworkTest:MonoBehaviour {
 string role;NetworkManager manager;RelayPartyManager relay;int checks;bool remoteReady,earlyHeartbeat,missingTransitionCamera;float nextReport;
 const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Bootstrap(){var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-nightStartupTest");if(index<0)return;var root=new GameObject("Night startup regression");DontDestroyOnLoad(root);root.AddComponent<NightStartupNetworkTest>().role=args[index+1];}
 IEnumerator Start(){
  Application.runInBackground=true;Application.targetFrameRate=45;QualitySettings.vSyncCount=0;yield return null;yield return null;
  manager=NetworkManager.Singleton;relay=FindFirstObjectByType<RelayPartyManager>();
  Check(manager!=null&&relay!=null,"actual MainMenu manager and lobby controller exist");
  manager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17868,"127.0.0.1");
  typeof(RelayPartyManager).GetMethod("ConfigureLobbyConnectionApproval",Private).Invoke(relay,new object[]{manager});
  Check(role=="host"?manager.StartHost():manager.StartClient(),"real lobby transport starts");
  manager.CustomMessagingManager.RegisterNamedMessageHandler("StartupReport",Report);manager.CustomMessagingManager.RegisterNamedMessageHandler("StartupDone",Done);
  if(role=="host")StartCoroutine(Run());yield return new WaitForSecondsRealtime(100);Fail("startup timeout");
 }
 void Update(){
  if(manager==null||!manager.IsListening)return;
  if(NightTransition.BlocksGameplay&&Camera.allCamerasCount==0)missingTransitionCamera=true;
  var local=manager.LocalClient?.PlayerObject;var monster=FindFirstObjectByType<BlindLungeMonster>();
  if(monster!=null&&(NightTransition.BlocksGameplay||local==null)&&monster.HeartbeatAudible)earlyHeartbeat=true;
  if(role=="client"&&manager.IsConnectedClient&&Time.realtimeSinceStartup>=nextReport){nextReport=Time.realtimeSinceStartup+.1f;var p=local==null?null:local.GetComponent<PlayerController>();bool ready=SceneManager.GetActiveScene().name=="Night 3"&&!NightTransition.BlocksGameplay&&p!=null&&p.playerCamera!=null&&p.playerCamera.GetComponent<Camera>().isActiveAndEnabled;
   using(var w=new FastBufferWriter(16,Allocator.Temp)){w.WriteValueSafe(ready);w.WriteValueSafe(earlyHeartbeat);w.WriteValueSafe(missingTransitionCamera);manager.CustomMessagingManager.SendNamedMessage("StartupReport",NetworkManager.ServerClientId,w);}
  }
 }
 void Report(ulong sender,FastBufferReader r){r.ReadValueSafe(out remoteReady);r.ReadValueSafe(out bool early);if(early)Fail("client heartbeat leaked during loading");r.ReadValueSafe(out bool missing);if(missing)Fail("client had no rendering camera during transition");}
 void Done(ulong sender,FastBufferReader r){File.WriteAllText("Logs/NightStartup-client-PASS.txt","PASS actual MainMenu -> Night 3: owning player camera renders and heartbeat silent during transition.");Application.Quit();}
 IEnumerator Run(){
  yield return Wait(()=>manager.ConnectedClientsList.Count==2,"two lobby peers connected",25);
  foreach(var peer in manager.ConnectedClientsList)Check(peer.PlayerObject==null,"lobby has no prematurely spawned player");
  typeof(RelayPartyManager).GetField("playtestStartNightThree",Private).SetValue(relay,true);typeof(RelayPartyManager).GetField("matchStarted",Private).SetValue(relay,true);
  Check((bool)typeof(RelayPartyManager).GetMethod("LoadMultiplayerSceneAsHost",Private).Invoke(relay,new object[]{manager}),"actual Start Match scene-loading path accepts Night 3");
  yield return Wait(()=>SceneManager.GetActiveScene().name=="Night 3"&&manager.LocalClient?.PlayerObject!=null&&!NightTransition.BlocksGameplay,"host finishes Night 3 spawn and curtain",45);
  yield return Wait(()=>remoteReady,"client owns an active rendering camera after Night 3 load",15);
  var player=manager.LocalClient.PlayerObject.GetComponent<PlayerController>();Check(player.playerCamera!=null&&player.playerCamera.GetComponent<Camera>().isActiveAndEnabled,"host owns an active rendering camera");
  Check(!earlyHeartbeat,"host heartbeat silent before player/transition readiness");
  var monster=FindFirstObjectByType<BlindLungeMonster>();Check(monster!=null&&monster.IsSpawned,"Night 3 monster synchronized");
  Check(!missingTransitionCamera,"black loading camera covers scene/player camera handoff");
  var life=player.GetComponent<OxygenSystem>();Check(life.IsInsideBunkerZone(),"actual starting spawn is inside bunker");Check(!monster.HeartbeatAudible,"bunker spawn has no monster heartbeat");
  Vector3 bunker=player.transform.position;var motor=player.GetComponent<CharacterController>();monster.hearingRange=0;
  Vector3 outside=monster.transform.position+Vector3.right*12+Vector3.up*2;
  motor.enabled=false;player.transform.position=outside;motor.enabled=true;player.GetComponent<OwnerNetworkTransform>().Teleport(outside,player.transform.rotation,player.transform.localScale);
  yield return Wait(()=>monster.HeartbeatAudible,"heartbeat becomes audible near monster outside bunker",2);
  motor.enabled=false;player.transform.position=bunker;motor.enabled=true;player.GetComponent<OwnerNetworkTransform>().Teleport(bunker,player.transform.rotation,player.transform.localScale);
  yield return Wait(()=>!monster.HeartbeatAudible,"returning to bunker stops heartbeat",2);
  yield return new WaitForSecondsRealtime(1);Check(remoteReady,"client camera remains enabled after scene startup");
  using(var w=new FastBufferWriter(1,Allocator.Temp))manager.CustomMessagingManager.SendNamedMessageToAll("StartupDone",w);
  File.WriteAllText("Logs/NightStartup-host-PASS.txt","PASS "+checks+" actual MainMenu -> Night 3 startup checks: scene synchronization, player spawn, local/remote cameras, loading heartbeat suppression and bunker silence.");yield return new WaitForSecondsRealtime(.5f);Application.Quit();
 }
 IEnumerator Wait(Func<bool> condition,string message,float timeout){float end=Time.realtimeSinceStartup+timeout;while(!condition()&&Time.realtimeSinceStartup<end)yield return null;Check(condition(),message);}
 void Check(bool condition,string message){if(!condition){Fail(message);throw new Exception(message);}checks++;Debug.Log("[Night Startup] PASS "+message);}
 void Fail(string message){File.WriteAllText("Logs/NightStartup-"+role+"-FAIL.txt",message);Debug.LogError(message);Application.Quit(2);}
}
#endif
