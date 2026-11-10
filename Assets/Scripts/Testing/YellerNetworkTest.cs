using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public sealed class YellerNetworkTest:NetworkBehaviour
{
    public NetworkVariable<int> Stage=new NetworkVariable<int>();
    Yeller primary,second;string role;int checks;float nextReport;readonly Dictionary<ulong,Report> reports=new Dictionary<ulong,Report>();
    struct Report{public byte phase;public ulong target;public double end;public Vector3 position,surveyor;public bool sound;public int starts;}
    readonly Dictionary<Component,float> before=new Dictionary<Component,float>();
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    IEnumerator Start(){var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-yellerTest");if(at<0)yield break;role=args[at+1];if(role=="host")foreach(var bug in FindObjectsByType<Yeller>(FindObjectsSortMode.None))bug.enabled=false;Application.runInBackground=true;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;NetworkManager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17863,"127.0.0.1");yield return null;if(role=="host")NetworkManager.StartHost();else NetworkManager.StartClient();yield return new WaitForSecondsRealtime(110);Fail("timeout");}
    public override void OnNetworkSpawn(){if(role==null)return;primary=FindObjectsByType<Yeller>(FindObjectsSortMode.None).Single(y=>y.name=="Primary Yeller");second=FindObjectsByType<Yeller>(FindObjectsSortMode.None).Single(y=>y.name=="Second Yeller");if(IsServer)StartCoroutine(Run());else foreach(var enemy in Enemies())((Behaviour)enemy).enabled=true;}
    void Update(){
        if(role==null||!IsSpawned||primary==null||NetworkManager.LocalClient?.PlayerObject==null)return;
        var obj=NetworkManager.LocalClient.PlayerObject;Vector3 pos=role=="host"?new Vector3(80,0,80):role=="late"?new Vector3(100,0,100):new Vector3(4,0,0);
        if(role=="client")pos=Stage.Value==1?new Vector3(1.6f,0,0):Stage.Value==2?new Vector3(1.4f,0,0):Stage.Value>=3&&Stage.Value<=5?new Vector3(1.4f,0,3):new Vector3(4,0,0);
        if(Stage.Value==6&&role=="host")pos=new Vector3(8,0,8);
        if(Stage.Value>=7&&role=="late")pos=new Vector3(12,0,12);
        var motor=obj.GetComponent<CharacterController>();motor.enabled=false;obj.transform.position=pos;if(obj.IsOwner)obj.GetComponent<Networking.OwnerNetworkTransform>().Teleport(pos,obj.transform.rotation,obj.transform.localScale);motor.enabled=true;
        if(!IsServer&&Time.unscaledTime>=nextReport){nextReport=Time.unscaledTime+.08f;ReportServerRpc(primary.Current.Phase,primary.Current.Player,primary.Current.EndsAt,primary.transform.position,primary.ScreamSource.isPlaying,primary.LocalScreamStarts,FindFirstObjectByType<SurveyorMonster>().transform.position);}
    }
    [ServerRpc(RequireOwnership=false)]void ReportServerRpc(byte phase,ulong target,double end,Vector3 position,bool sound,int starts,Vector3 surveyor,ServerRpcParams rpc=default){reports[rpc.Receive.SenderClientId]=new Report{phase=phase,target=target,end=end,position=position,sound=sound,starts=starts,surveyor=surveyor};}
    Component[] Enemies()=>FindObjectsByType<ZombieAI>(FindObjectsSortMode.None).Cast<Component>().Concat(FindObjectsByType<EyeMonster>(FindObjectsSortMode.None)).Concat(FindObjectsByType<BlindLungeMonster>(FindObjectsSortMode.None)).Concat(FindObjectsByType<PowerStationWatcher>(FindObjectsSortMode.None)).Concat(FindObjectsByType<SurveyorMonster>(FindObjectsSortMode.None)).Concat(FindObjectsByType<SandStalker>(FindObjectsSortMode.None)).ToArray();
    void Reset(Yeller bug,Vector3 position){bug.transform.position=position;bug.Snapshot.Value=new YellerState{Player=ulong.MaxValue,Position=position};}
    IEnumerator Run(){
        yield return Wait(()=>NetworkManager.ConnectedClientsList.Count==2,"joined player",25);yield return new WaitForSecondsRealtime(.5f);
        var client=NetworkManager.ConnectedClientsList.First(c=>c.ClientId!=NetworkManager.LocalClientId);ulong id=client.ClientId;
        Check(Mathf.Abs(Yeller.AttachRange-1.524f)<.0001f&&Yeller.ScreamSeconds==15,"five-foot range and fifteen-second duration");
        Stage.Value=1;yield return new WaitForSecondsRealtime(.3f);primary.enabled=second.enabled=true;yield return new WaitForSecondsRealtime(.6f);Check(primary.Current.Phase==0,"outside five feet does not attach");
        Stage.Value=2;yield return Wait(()=>primary.IsScreaming,"inside range attaches on host",3);double end=primary.Current.EndsAt;
        Check(primary.Current.Player==id&&end-NetworkManager.ServerTime.Time>14.5,"attachment records victim and immutable deadline");
        yield return Wait(()=>reports.ContainsKey(id)&&reports[id].phase==1&&reports[id].target==id&&reports[id].sound,"victim sees attachment and hears looping scream",3);
        Check(Math.Abs(reports[id].end-end)<.001&&primary.ScreamSource.spatialBlend==1,"client shares host deadline and positional audio");
        Reset(second,new Vector3(1.5f,.2f,0));yield return new WaitForSecondsRealtime(.3f);Check(second.Current.Phase==0,"same player cannot stack two Yellers");Reset(second,new Vector3(50,.2f,50));
        Stage.Value=3;yield return new WaitForSecondsRealtime(.4f);
        yield return Wait(()=>Vector3.Distance(reports[id].position,primary.transform.position)<.2,"attached cube follows remote player",3);
        foreach(var enemy in Enemies())((Behaviour)enemy).enabled=true;
        yield return new WaitForSecondsRealtime(.6f);
        var victim=client.PlayerObject.GetComponent<PlayerController>();
        foreach(var enemy in Enemies())before[enemy]=Vector3.Distance(enemy.transform.position,victim.transform.position);
        yield return new WaitForSecondsRealtime(1.2f);
        foreach(var enemy in Enemies())Check(Vector3.Distance(enemy.transform.position,victim.transform.position)<before[enemy]-.2f,enemy.GetType().Name+(enemy is SandStalker stalker&&stalker.burrow?" Burrow":"")+" runs toward screaming player");
        Check(FindFirstObjectByType<ZombieAI>().IsChasingNetwork.Value,"motion pursuit state replicates");
        Check(FindFirstObjectByType<EyeMonster>().Target.Value==id,"Eye targets the Yeller victim");
        Check(FindFirstObjectByType<SurveyorMonster>().YellerMoving.Value,"Surveyor enters explicit replicated lure movement");
        yield return Wait(()=>Vector3.Distance(reports[id].surveyor,FindFirstObjectByType<SurveyorMonster>().transform.position)<.3f,"Surveyor position agrees on client",3);
        File.WriteAllText("Logs/Yeller-latejoin.ready","ready");
        yield return Wait(()=>NetworkManager.ConnectedClientsList.Count==3,"late join during scream",8);
        var late=NetworkManager.ConnectedClientsList.First(c=>c.ClientId!=id&&c.ClientId!=NetworkManager.LocalClientId);
        yield return Wait(()=>reports.ContainsKey(late.ClientId)&&reports[late.ClientId].sound&&Math.Abs(reports[late.ClientId].end-end)<.001,"late join receives current scream without restarting timer",4);
        while(NetworkManager.ServerTime.Time<end-.1)yield return null;
        Check(primary.Current.Phase==1&&primary.IsScreaming,"scream lasts the full fifteen seconds");
        yield return Wait(()=>primary.Current.Phase==2&&!primary.ScreamSource.isPlaying,"host expires scream and spends bug",2);
        yield return Wait(()=>!reports[id].sound&&!reports[late.ClientId].sound&&reports[id].phase==2,"joined and late clients stop the scream",3);
        foreach(var enemy in Enemies()){Check(!Yeller.TryAttract(enemy,out _,out _),enemy.GetType().Name+" has no expired forced target");((Behaviour)enemy).enabled=false;if(enemy is PowerStationWatcher watcher){watcher.State.Value=(int)PowerStationWatcher.Phase.Roaming;watcher.Target.Value=ulong.MaxValue;}}
        Check(FindObjectsByType<SandStalker>(FindObjectsSortMode.None).All(s=>(ulong)s.TargetId.Value==id||s.CurrentPhase==SandStalker.Phase.Roaming),"stalkers resume normal perception without stale ownership");
        // Solid rock/wall between a fresh bug and a player blocks attachment.
        Stage.Value=6;Reset(second,new Vector3(8,.2f,9.4f));var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(8,1,8.7f);wall.transform.localScale=new Vector3(2,3,.25f);Physics.SyncTransforms();yield return new WaitForSecondsRealtime(.6f);Check(second.Current.Phase==0,"attachment does not pass through rock or bunker walls");UnityEngine.Object.Destroy(wall);
        yield return Wait(()=>second.IsScreaming,"host player can receive an attachment",3);NetworkManager.LocalClient.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value=true;
        yield return Wait(()=>second.Current.Phase==2,"victim death cancels attachment safely",2);
        Stage.Value=7;Reset(second,new Vector3(12,.2f,12));yield return Wait(()=>second.IsScreaming&&second.Current.Player==late.ClientId,"another living player can trigger a fresh bug",3);
        NetworkManager.DisconnectClient(late.ClientId);yield return Wait(()=>second.Current.Phase==2,"disconnect clears attached player and attraction",3);
        Check(!client.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value,"non-contact lure test did not kill original victim");
        FinishClientRpc();File.WriteAllText("Logs/Yeller-host-PASS.txt","PASS "+checks+" attachment boundary, server deadline, movement/audio replication, all seven moving archetypes, expiry, late join, occlusion, death and disconnect assertions.");yield return new WaitForSecondsRealtime(.5f);Application.Quit();
    }
    IEnumerator Wait(Func<bool> condition,string text,float seconds){float limit=Time.realtimeSinceStartup+seconds;while(!condition()&&Time.realtimeSinceStartup<limit)yield return null;Check(condition(),text);}
    void Check(bool okay,string text){if(!okay){Fail(text);throw new Exception(text);}checks++;Debug.Log("[Yeller] PASS "+text);}
    void Fail(string text){File.WriteAllText("Logs/Yeller-"+role+"-FAIL.txt",text);Debug.LogError(text);Application.Quit(2);}
    [ClientRpc]void FinishClientRpc(){if(IsServer)return;File.WriteAllText("Logs/Yeller-"+role+"-PASS.txt","PASS: live client observed authoritative attachment, movement, scream timing and expiry.");Application.Quit();}
}
