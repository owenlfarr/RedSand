using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
[DefaultExecutionOrder(-1000)]public sealed class BlindLungeNetworkTest:NetworkBehaviour
{
 public NetworkVariable<int> Stage=new NetworkVariable<int>();
 BlindLungeMonster monster;string role;int previous=-1,checks;float nextReport,stageStart;bool threw;
 readonly Dictionary<ulong,Report> reports=new Dictionary<ulong,Report>();
 struct Report{public int launches,impacts,replies,result,rocks,deaths,phase;public Vector3 position;public bool pa;public float volume;}
 IEnumerator Start(){var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-blindTest");if(index<0)yield break;role=args[index+1];Application.runInBackground=true;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;
  FindFirstObjectByType<NightTimeManager>().gameObject.AddComponent<PAScreecher>();
  NetworkManager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17849,"127.0.0.1");yield return null;
  if(role=="host")NetworkManager.StartHost();else NetworkManager.StartClient();
  yield return new WaitForSecondsRealtime(110);File.WriteAllText("Logs/Blind-"+role+"-FAIL.txt","Timeout");Application.Quit(2);
 }
 public override void OnNetworkSpawn(){monster=FindFirstObjectByType<BlindLungeMonster>();if(IsServer)StartCoroutine(Run());}
 void Update(){if(!IsSpawned||monster==null||NetworkManager.LocalClient?.PlayerObject==null)return;var obj=NetworkManager.LocalClient.PlayerObject;var player=obj.GetComponent<PlayerController>();var movement=obj.GetComponent<NetworkPlayerMovementState>();
  if(previous!=Stage.Value){previous=Stage.Value;stageStart=Time.time;threw=false;}
  Vector3 pos=role=="host"?new Vector3(-80,0,-80):role=="late"?new Vector3(160,0,0):new Vector3(80,0,0);
  if(Stage.Value==1&&role=="client")pos=new Vector3(Mathf.Min(4,(Time.time-stageStart)*8),0,18);
  if(Stage.Value==6&&role=="client")pos=new Vector3(0,0,6);
  if(Stage.Value==7&&role=="host")pos=new Vector3(20,0,6);
  obj.transform.position=pos;
  if(IsOwner||obj.IsOwner){movement.IsGrounded.Value=true;movement.IsSprinting.Value=Stage.Value==1;}
  if(role=="client"&&Stage.Value==2&&!threw){threw=true;monster.RequestSearch(player,Vector3.down);monster.RequestSearch(player,Vector3.down);}
  if(role=="client"&&Stage.Value==3&&!threw&&movement.CarriedDistractionRocks.Value==1){threw=true;monster.RequestThrow(player,Vector3.forward+Vector3.up*.6f);}
  if(role=="client"&&Stage.Value==4&&!threw){threw=true;monster.ReportLocalSpeech();}
  if(!IsServer&&Time.time>=nextReport){nextReport=Time.time+.08f;ReportServerRpc(monster.LocalRockLaunches,monster.LocalRockImpacts,monster.LocalSearchReplies,monster.LastLocalSearchResult,movement.CarriedDistractionRocks.Value,monster.LocalDeathPresentations,(int)monster.CurrentPhase,monster.transform.position,FindFirstObjectByType<PAScreecher>().IsActive,AudioListener.volume);}
 }
 [ServerRpc(RequireOwnership=false)]void ReportServerRpc(int launches,int impacts,int replies,int result,int rocks,int deaths,int phase,Vector3 position,bool pa,float volume,ServerRpcParams rpc=default){reports[rpc.Receive.SenderClientId]=new Report{launches=launches,impacts=impacts,replies=replies,result=result,rocks=rocks,deaths=deaths,phase=phase,position=position,pa=pa,volume=volume};}
 void ResetMonster(Vector3 position){var motor=monster.GetComponent<CharacterController>();motor.enabled=false;monster.transform.position=position+Vector3.up*.05f;motor.enabled=true;var flags=BindingFlags.Instance|BindingFlags.NonPublic;typeof(BlindLungeMonster).GetField("heard",flags).SetValue(monster,false);typeof(BlindLungeMonster).GetField("phase",flags).SetValue(monster,BlindLungeMonster.Phase.Listening);typeof(BlindLungeMonster).GetField("priorityUntil",flags).SetValue(monster,0f);typeof(BlindLungeMonster).GetField("memoryUntil",flags).SetValue(monster,0f);Physics.SyncTransforms();}
 IEnumerator Run(){
  yield return Wait(()=>NetworkManager.ConnectedClientsList.Count>=2,"initial client joins",25);
  var other=NetworkManager.ConnectedClientsList.First(c=>c.ClientId!=NetworkManager.LocalClientId);ulong clientId=other.ClientId;
  yield return new WaitForSeconds(.5f);
  foreach(var peer in NetworkManager.ConnectedClientsList){var p=peer.PlayerObject.GetComponent<PlayerController>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;typeof(PlayerController).GetField("controller",flags).SetValue(p,p.GetComponent<CharacterController>());typeof(PlayerController).GetField("movementState",flags).SetValue(p,p.GetComponent<NetworkPlayerMovementState>());}
  Check(monster.IsServer&&monster.GetComponent<CharacterController>().enabled,"host owns monster motor");
  ResetMonster(Vector3.zero);Stage.Value=1;yield return new WaitForSeconds(1.25f);
  Check(monster.HasHeardNoise&&monster.transform.position.z>5,"remote sprint footsteps trigger host lunge");
  yield return Wait(()=>reports.ContainsKey(clientId)&&reports[clientId].phase!=(int)BlindLungeMonster.Phase.Listening,"client sees authoritative lunge phase",3);
  Check(Vector3.Distance(reports[clientId].position,monster.Position.Value)<2,"client follows replicated monster position");
  ResetMonster(Vector3.zero);Stage.Value=2;yield return new WaitForSeconds(1.6f);
  Check(monster.RejectedSearchRequests>=1,"host rejects repeated search during 2.5 second cooldown");
  yield return Wait(()=>reports.ContainsKey(clientId)&&reports[clientId].replies>=2&&reports[clientId].result<=1,"search result returns only to requesting client",3);
  Check(monster.LocalSearchReplies==0,"client search result never appears on host HUD");
  Check(reports[clientId].rocks==other.PlayerObject.GetComponent<NetworkPlayerMovementState>().CarriedDistractionRocks.Value,"rock inventory replicated from server");
  other.PlayerObject.GetComponent<NetworkPlayerMovementState>().CarriedDistractionRocks.Value=1;
  ResetMonster(new Vector3(80,0,23));Stage.Value=3;
  yield return Wait(()=>monster.LocalRockImpacts>=1,"client throw lands on authoritative host",6);
  yield return Wait(()=>reports[clientId].launches==1&&reports[clientId].impacts==1,"client and host share one projectile and one impact",3);
  Check(monster.LocalRockLaunches==1&&other.PlayerObject.GetComponent<NetworkPlayerMovementState>().CarriedDistractionRocks.Value==0,"throw spends exactly one server-owned rock");
  Check(monster.HasHeardNoise&&monster.LastHeardPosition.z>30,"rock impact distracts host monster away from thrower");
  ResetMonster(new Vector3(80,0,16));Stage.Value=4;
  yield return Wait(()=>monster.HasHeardNoise,"client speech report attracts host monster",3);
  var pa=FindFirstObjectByType<PAScreecher>();
  typeof(PAScreecher).GetField("nightlyEventLimit",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(pa,3);
  Check(pa.Trigger(15),"host starts replicated PA event");
  yield return Wait(()=>pa.IsActive&&reports[clientId].pa&&reports[clientId].volume<.05f,"PA takeover muffles host and joined client",3);
  ResetMonster(new Vector3(40,0,20));Stage.Value=5;File.WriteAllText("Logs/Blind-latejoin.ready","ready");
  yield return Wait(()=>NetworkManager.ConnectedClientsList.Count>=3,"late client joins running session",30);
  var late=NetworkManager.ConnectedClientsList.First(c=>c.ClientId!=NetworkManager.LocalClientId&&c.ClientId!=clientId);
  yield return Wait(()=>reports.ContainsKey(late.ClientId)&&Vector3.Distance(reports[late.ClientId].position,monster.Position.Value)<.3f,"late join receives current monster position",6);
  yield return Wait(()=>reports[late.ClientId].pa&&reports[late.ClientId].volume<.05f,"late join receives ongoing PA takeover",3);
  FindFirstObjectByType<NightTimeManager>().PAScreechEndsAt.Value=0;
  yield return Wait(()=>!pa.IsActive&&!reports[clientId].pa&&!reports[late.ClientId].pa&&reports[clientId].volume>.9f&&reports[late.ClientId].volume>.9f,"PA expiry restores hearing on all peers",3);
  ResetMonster(Vector3.zero);Stage.Value=6;yield return new WaitForSeconds(.4f);monster.HearNoise(new Vector3(0,0,6),32,true);
  yield return Wait(()=>other.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value,"host detects lunge contact with joined player",4);
  yield return Wait(()=>reports[clientId].deaths==1,"joined victim receives death RPC exactly once",3);
  Check(!NetworkManager.LocalClient.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value&&!late.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value,"victim death leaves other players alive");
  ResetMonster(new Vector3(20,0,0));Stage.Value=7;yield return new WaitForSeconds(.4f);monster.HearNoise(new Vector3(20,0,6),32,true);
  yield return Wait(()=>NetworkManager.LocalClient.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value,"host player can also die to lunge",4);
  Check(monster.LocalDeathPresentations==1,"host victim receives presentation once");
  Check(reports[late.ClientId].deaths==0,"late survivor never receives another player's death presentation");
  FinishClientRpc();File.WriteAllText("Logs/Blind-host-PASS.txt","PASS "+checks+" host/client/late-join assertions.");yield return new WaitForSeconds(.8f);Application.Quit();
 }
 IEnumerator Wait(Func<bool> condition,string message,float seconds){float until=Time.realtimeSinceStartup+seconds;while(!condition()&&Time.realtimeSinceStartup<until)yield return null;Check(condition(),message);}
 void Check(bool okay,string message){if(!okay){File.WriteAllText("Logs/Blind-"+role+"-FAIL.txt",message);Debug.LogError(message);Application.Quit(2);throw new Exception(message);}checks++;Debug.Log("[Blind Net Test] PASS "+message);}
 [ClientRpc]void FinishClientRpc(){if(IsServer)return;bool pass=role=="late"?monster.LocalDeathPresentations==0:monster.LocalDeathPresentations==1&&monster.LocalRockLaunches==1&&monster.LocalRockImpacts==1&&monster.LocalSearchReplies>=2;File.WriteAllText("Logs/Blind-"+role+(pass?"-PASS.txt":"-FAIL.txt"),pass?"PASS actual connected client synchronization, owner-specific replies/deaths and scene state.":"Client observations incomplete");Application.Quit(pass?0:2);}
}
