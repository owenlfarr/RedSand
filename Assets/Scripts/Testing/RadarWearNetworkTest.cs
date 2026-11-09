using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
[DefaultExecutionOrder(-1000)]public sealed class RadarWearNetworkTest:NetworkBehaviour
{
 public NetworkVariable<int> Stage=new NetworkVariable<int>();
 RadarSystem radar;DrillSystem drill;string role;int prior=-1,checks;float stageAt,nextHold,nextReport;bool acted;
 readonly Dictionary<ulong,Vector2Int> reports=new Dictionary<ulong,Vector2Int>();
 IEnumerator Start(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-radarTest");if(i<0)yield break;role=args[i+1];Application.runInBackground=true;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;NetworkManager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17851,"127.0.0.1");yield return null;if(role=="host")NetworkManager.StartHost();else NetworkManager.StartClient();yield return new WaitForSecondsRealtime(110);File.WriteAllText("Logs/Radar-"+role+"-FAIL.txt","Timeout");Application.Quit(2);}
 public override void OnNetworkSpawn(){radar=FindFirstObjectByType<RadarSystem>();drill=FindFirstObjectByType<DrillSystem>();if(IsServer)StartCoroutine(Run());}
 void Update(){if(!IsSpawned||radar==null||NetworkManager.LocalClient?.PlayerObject==null)return;
  if(prior!=Stage.Value){prior=Stage.Value;stageAt=Time.time;acted=false;}
  var obj=NetworkManager.LocalClient.PlayerObject;
  bool repair=Stage.Value>=3&&Stage.Value<=7;Vector3 pos=role=="host"?new Vector3(80,0,0):Vector3.zero;if(repair&&role=="client")pos=new Vector3(30,0,0);if(Stage.Value==5&&role=="host")pos=new Vector3(31,0,0);if(role=="late")pos=new Vector3(90,0,90);if(Vector3.Distance(obj.transform.position,pos)>.2f){
   var body=obj.GetComponent<CharacterController>();body.enabled=false;obj.transform.position=pos;
   obj.GetComponent<Networking.OwnerNetworkTransform>().Teleport(pos,obj.transform.rotation,obj.transform.localScale);
   body.enabled=true;
  }
  if(role=="client"&&Stage.Value==1&&!acted){acted=true;radar.RequestScan();radar.RequestScan();}
  if(role=="client"&&Stage.Value==2&&!acted){acted=true;for(int n=0;n<25;n++)radar.RequestScan();}
  bool hold=repair&&((role=="client"&&(Stage.Value<=5||Time.time-stageAt<.3f))||(role=="host"&&Stage.Value==5));
  if(hold&&Time.time>=nextHold){nextHold=Time.time+.12f;radar.RequestMaintenance(true);}
  if(role=="client"&&Stage.Value==6&&!acted&&Time.time-stageAt>=.3f){acted=true;radar.RequestMaintenance(false);}
  if(!IsServer&&Time.time>=nextReport){nextReport=Time.time+.08f;ReportServerRpc(radar.UsesRemaining,drill.Current.Metal);}
 }
 [ServerRpc(RequireOwnership=false)]void ReportServerRpc(int uses,int metal,ServerRpcParams rpc=default){reports[rpc.Receive.SenderClientId]=new Vector2Int(uses,metal);}
 void Funds(int amount){var stock=NetworkManager.GetComponent<DrillSessionStockpile>();var value=stock.Materials;value.Metal=amount;stock.Materials=value;}
 IEnumerator Run(){
  foreach(int n in Enumerable.Range(1,6))Check(RadarWearRules.Capacity(n)==(n>=4?12:20-2*n),"night "+n+" scan capacity");
  Check(RadarWearRules.MetalCost(1)==10&&RadarWearRules.MetalCost(4)==10&&RadarWearRules.MetalCost(5)==15&&RadarWearRules.MetalCost(6)==15,"repair price switches after night four");
  Check(RadarWearRules.NightNumber("Night1_MultiplayerTest")==1&&RadarWearRules.NightNumber("Night 3")==3&&RadarWearRules.Capacity(999)==12,"legacy names and later-night twelve-scan limit");
  yield return Wait(()=>NetworkManager.ConnectedClientsList.Count>=2,"client joins",25);yield return new WaitForSeconds(.5f);
  var client=NetworkManager.ConnectedClientsList.First(c=>c.ClientId!=NetworkManager.LocalClientId);Funds(12);
  Check(radar.UsesRemaining==18,"starts with full uses");Stage.Value=1;yield return new WaitForSeconds(.5f);Check(radar.UsesRemaining==17,"successful scan spends one use; cooldown rejection spends none");
  radar.cooldownTime=0;Stage.Value=2;yield return new WaitForSeconds(.6f);Check(radar.UsesRemaining==0,"radar breaks at zero and cannot scan below zero");
  yield return Wait(()=>reports.ContainsKey(client.ClientId)&&reports[client.ClientId].x==0,"worn state reaches client",3);
  Stage.Value=3;yield return new WaitForSeconds(1);
  var solar=FindFirstObjectByType<SolarFlareSystem>();
  File.WriteAllText("Logs/RadarRepairDiagnostic.txt","uses="+radar.UsesRemaining+" owner="+radar.MaintenanceOwner+" progress="+radar.MaintenanceProgress+" seconds="+radar.MaintenanceSeconds+" funds="+drill.Current.Metal+" available="+radar.AvailableRepairMetal+" canRepair="+radar.CanRepairAtStation(client.PlayerObject.GetComponent<PlayerController>())+" player="+client.PlayerObject.transform.position+" playerEnabled="+client.PlayerObject.GetComponent<PlayerController>().enabled+" alive="+!client.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value+" movement="+client.PlayerObject.GetComponent<NetworkPlayerMovementState>().MovementEnabled.Value+" station="+(solar!=null?solar.transform.position.ToString():"null")+" flare="+(solar!=null&&solar.IsFlareActive));
  yield return Wait(()=>radar.UsesRemaining==18,"joined player repairs at solar station",6);
  Check(drill.Current.Metal==2,"repair deducts ten team metal exactly once");
  yield return Wait(()=>reports[client.ClientId]==new Vector2Int(18,2),"repair and metal spend reach client",3);
  radar.UsesRemainingNetwork.Value=0;Stage.Value=4;yield return new WaitForSeconds(.8f);Check(radar.UsesRemaining==0&&drill.Current.Metal==2,"insufficient metal cannot repair or spend");
  Funds(25);Stage.Value=5;yield return Wait(()=>radar.UsesRemaining==18,"simultaneous repair requests complete once",4);yield return new WaitForSeconds(.3f);Check(drill.Current.Metal==15,"concurrent host/client repair charges once");
  radar.UsesRemainingNetwork.Value=0;FindFirstObjectByType<SolarFlareSystem>().repairTime=2.5f;Stage.Value=6;yield return new WaitForSeconds(1);Check(radar.MaintenanceOwner==ulong.MaxValue&&radar.MaintenanceProgress==0&&drill.Current.Metal==15,"release cancels repair without charge");
  Stage.Value=7;yield return new WaitForSeconds(1.2f);Check(radar.MaintenanceOwner==ulong.MaxValue&&radar.UsesRemaining==0&&drill.Current.Metal==15,"missing hold heartbeat cancels without charge");
  Stage.Value=8;File.WriteAllText("Logs/Radar-latejoin.ready","ready");yield return Wait(()=>NetworkManager.ConnectedClientsList.Count>=3,"late player joins",30);var late=NetworkManager.ConnectedClientsList.First(c=>c.ClientId!=0&&c.ClientId!=client.ClientId);
  yield return Wait(()=>reports.ContainsKey(late.ClientId)&&reports[late.ClientId]==new Vector2Int(0,15),"late join sees worn radar and remaining metal",6);
  FinishClientRpc();File.WriteAllText("Logs/Radar-host-PASS.txt","PASS "+checks+" wear, pricing, authority, repair, race, cancellation and late-join assertions.");yield return new WaitForSeconds(.8f);Application.Quit();
 }
 IEnumerator Wait(Func<bool> okay,string message,float seconds){float limit=Time.realtimeSinceStartup+seconds;while(!okay()&&Time.realtimeSinceStartup<limit)yield return null;Check(okay(),message);}
 void Check(bool okay,string message){if(!okay){File.WriteAllText("Logs/Radar-"+role+"-FAIL.txt",message);Debug.LogError(message);Application.Quit(2);throw new Exception(message);}checks++;Debug.Log("[Radar Test] PASS "+message);}
 [ClientRpc]void FinishClientRpc(){if(IsServer)return;bool okay=radar.UsesRemaining==0&&drill.Current.Metal==15;File.WriteAllText("Logs/Radar-"+role+(okay?"-PASS.txt":"-FAIL.txt"),okay?"PASS connected client observes authoritative radar wear, repairs and team metal.":"state mismatch");Application.Quit(okay?0:2);}
}
