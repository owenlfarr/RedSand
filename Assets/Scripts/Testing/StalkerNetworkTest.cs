using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

// Opt-in standalone fixture; never installed in production scenes.
public sealed class StalkerNetworkTest : NetworkBehaviour
{
    public NetworkVariable<int> Stage=new NetworkVariable<int>();
    SandStalker whisper,burrow;string role;int prior=-1,checks;float nextReport;bool signalled;
    readonly Dictionary<ulong,Report> reports=new Dictionary<ulong,Report>();
    struct Report {public int phase,warnings,deaths,burrowPhase,burrowWarnings;public Vector3 position;public bool motor,smoke,digging,cube;}
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    IEnumerator Start(){
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-stalkerTest");if(i<0)yield break;
        role=args[i+1];Application.runInBackground=true;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;
        NetworkManager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17857,"127.0.0.1");yield return null;
        if(role=="host")NetworkManager.StartHost();else NetworkManager.StartClient();
        yield return new WaitForSecondsRealtime(180);Fail("Timeout");
    }
    public override void OnNetworkSpawn(){if(role==null)return;var monsters=FindObjectsByType<SandStalker>(FindObjectsSortMode.None);whisper=monsters.Single(m=>!m.burrow);burrow=monsters.Single(m=>m.burrow);if(IsServer)StartCoroutine(Run());}
    void Update(){
        if(role==null||!IsSpawned||whisper==null||NetworkManager.LocalClient?.PlayerObject==null)return;
        var obj=NetworkManager.LocalClient.PlayerObject;var player=obj.GetComponent<PlayerController>();var body=obj.GetComponent<CharacterController>();var movement=obj.GetComponent<NetworkPlayerMovementState>();
        if(prior!=Stage.Value){prior=Stage.Value;signalled=false;}
        Vector3 pos=role=="host"?new Vector3(-80,0,-80):role=="late"?new Vector3(150,0,0):new Vector3(0,0,18);
        if(Stage.Value==4&&role=="host")pos=new Vector3(3,0,18);
        if(Stage.Value==7&&role=="client")pos=new Vector3(0,0,2);
        if(Stage.Value>=8&&Stage.Value<=11&&role=="client")pos=new Vector3(80,Stage.Value==9?1.1f:0,14);
        bool lit=role=="client"&&(Stage.Value==2||Stage.Value==6);
        Quaternion rotation=Quaternion.identity;
        if(lit){Vector3 delta=whisper.transform.position+Vector3.up-(pos+Vector3.up*1.6f);rotation=Quaternion.LookRotation(delta);}
        float pitch=rotation.eulerAngles.x;if(pitch>180)pitch-=360;
        typeof(PlayerController).GetField("verticalRotation",Private).SetValue(player,pitch);
        body.enabled=false;obj.transform.SetPositionAndRotation(pos,Quaternion.Euler(0,rotation.eulerAngles.y,0));
        if(obj.IsOwner)obj.GetComponent<Networking.OwnerNetworkTransform>().Teleport(pos,obj.transform.rotation,obj.transform.localScale);
        body.enabled=true;
        movement.CameraPitch.Value=pitch;movement.IsGrounded.Value=Stage.Value!=9;
        if(!signalled){signalled=true;obj.GetComponent<OxygenSystem>().SetFlashlightEnabled(lit);}
        if(!IsServer&&Time.time>=nextReport){nextReport=Time.time+.08f;ReportServerRpc((int)whisper.CurrentPhase,whisper.LocalWarnings,whisper.LocalDeaths,whisper.transform.position,whisper.GetComponent<CharacterController>().enabled,(int)burrow.CurrentPhase,burrow.LocalWarnings,burrow.smoke!=null&&burrow.smoke.isPlaying,burrow.BurrowingSource.isPlaying,burrow.visual.activeSelf);}
    }
    [ServerRpc(RequireOwnership=false)]void ReportServerRpc(int phase,int warnings,int deaths,Vector3 position,bool motor,int burrowPhase,int burrowWarnings,bool smoke,bool digging,bool cube,ServerRpcParams rpc=default){reports[rpc.Receive.SenderClientId]=new Report{phase=phase,warnings=warnings,deaths=deaths,position=position,motor=motor,burrowPhase=burrowPhase,burrowWarnings=burrowWarnings,smoke=smoke,digging=digging,cube=cube};}
    void Set(object obj,string field,object value)=>obj.GetType().GetField(field,Private).SetValue(obj,value);
    void Reset(SandStalker monster,Vector3 position,PlayerController victim=null){
        var body=monster.GetComponent<CharacterController>();body.enabled=false;monster.transform.position=position+Vector3.up*.05f;body.enabled=true;
        Set(monster,"home",monster.transform.position);Set(monster,"destination",monster.transform.position);Set(monster,"dormantUntil",0d);Set(monster,"target",victim);Set(monster,"phase",victim==null?SandStalker.Phase.Roaming:SandStalker.Phase.Chasing);Set(monster,"until",0f);Set(monster,"nextScan",0f);Set(monster,"strikePending",false);Set(monster,"dodged",false);Set(monster,"nextHandoff",Time.time+4);monster.DormantUntil.Value=0;Physics.SyncTransforms();
    }
    void Park(SandStalker monster){Set(monster,"phase",SandStalker.Phase.Dormant);Set(monster,"until",Time.time+1000);Set(monster,"dormantUntil",NetworkManager.ServerTime.Time+1000);Set(monster,"target",null);}
    IEnumerator Run(){
        Park(whisper);Park(burrow);
        yield return Wait(()=>NetworkManager.ConnectedClientsList.Count>=2,"client joins",30);
        var client=NetworkManager.ConnectedClientsList.First(c=>c.ClientId!=NetworkManager.LocalClientId);ulong id=client.ClientId;
        yield return new WaitForSeconds(.7f);
        var victim=client.PlayerObject.GetComponent<PlayerController>();var life=client.PlayerObject.GetComponent<NetworkOxygenState>();
        Stage.Value=1;Reset(whisper,Vector3.zero);
        yield return Wait(()=>whisper.CurrentPhase==SandStalker.Phase.Chasing,"Whisper sees remote player and chases after warning",4);
        yield return Wait(()=>reports.ContainsKey(id)&&reports[id].warnings>0&&reports[id].phase==(int)SandStalker.Phase.Chasing,"client hears warning and sees chase",3);
        Check(whisper.GetComponent<CharacterController>().enabled&&!reports[id].motor,"only host owns the monster motor");
        Check(Vector3.Distance(reports[id].position,whisper.Position.Value)<2,"client follows replicated position");
        Stage.Value=2;
        yield return Wait(()=>whisper.CurrentPhase==SandStalker.Phase.Retreating,"remote flashlight RPC and replicated aim stun Whisper",3);
        int warning=whisper.Cue.Value;Stage.Value=3;
        yield return Wait(()=>whisper.Cue.Value>warning,"Whisper retreats out of view and warns again from another angle",9);
        Check(Mathf.Abs(whisper.transform.position.x)>5,"retreat changes attack angle");
        Park(whisper);Stage.Value=4;yield return new WaitForSeconds(.6f);whisper.speed=1;Reset(whisper,new Vector3(0,0,8),victim);
        yield return Wait(()=>whisper.TargetId.Value==NetworkManager.LocalClientId,"Whisper passes to nearby teammate",6);
        Check(!life.IsDead.Value,"handoff does not kill original target");
        whisper.speed=10.5f;Park(whisper);Stage.Value=5;yield return new WaitForSeconds(.5f);life.IsInBunker.Value=true;Reset(whisper,Vector3.zero,victim);
        yield return Wait(()=>whisper.CurrentPhase==SandStalker.Phase.Dormant,"bunker breaks pursuit",2);Check(!life.IsDead.Value,"bunker target survives");life.IsInBunker.Value=false;
        Stage.Value=6;var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(0,1,9);wall.transform.localScale=new Vector3(12,4,1);Reset(whisper,Vector3.zero,victim);
        yield return new WaitForSeconds(.8f);Check(whisper.CurrentPhase==SandStalker.Phase.Chasing,"flashlight cannot stun through a wall");UnityEngine.Object.Destroy(wall);Park(whisper);
        Stage.Value=7;yield return new WaitForSeconds(.5f);Reset(whisper,Vector3.zero,victim);
        yield return Wait(()=>life.IsDead.Value,"Whisper host resolves client contact death",3);
        yield return Wait(()=>reports[id].deaths==1,"death presentation reaches only victim",3);Check(whisper.LocalDeaths==0,"host does not receive client death presentation");
        Park(whisper);life.IsDead.Value=false;client.PlayerObject.GetComponent<NetworkPlayerMovementState>().MovementEnabled.Value=true;
        Stage.Value=8;yield return new WaitForSeconds(.5f);Reset(burrow,new Vector3(80,0,0));
        yield return Wait(()=>burrow.CurrentPhase==SandStalker.Phase.Warning,"Burrow warns when remote player spotted",3);
        yield return Wait(()=>burrow.CurrentPhase==SandStalker.Phase.Chasing,"Burrow enters underground approach",3);
        yield return Wait(()=>reports[id].burrowWarnings>0&&reports[id].smoke&&reports[id].digging&&!reports[id].cube,"client sees sand trail, hears digging, and hidden cube",2);
        Stage.Value=9;
        yield return Wait(()=>burrow.CurrentPhase==SandStalker.Phase.Dormant,"Burrow strike ends in dormancy",3);
        Check(!life.IsDead.Value,"remote timed jump avoids strike");Check(burrow.DormantUntil.Value-NetworkManager.ServerTime.Time>29,"successful dodge grants thirty seconds");
        Stage.Value=10;File.WriteAllText("Logs/Stalker-latejoin.ready","ready");double ends=burrow.DormantUntil.Value;
        yield return Wait(()=>NetworkManager.ConnectedClientsList.Count>=3,"late client joins",25);
        var late=NetworkManager.ConnectedClientsList.First(c=>c.ClientId!=id&&c.ClientId!=NetworkManager.LocalClientId);
        yield return Wait(()=>reports.ContainsKey(late.ClientId)&&reports[late.ClientId].burrowPhase==(int)SandStalker.Phase.Dormant&&!reports[late.ClientId].cube,"late join sees dormant Burrow with no visible cube",5);
        yield return Wait(()=>reports[late.ClientId].warnings==0,"late join does not replay historic Whisper warnings",3);
        while(NetworkManager.ServerTime.Time<ends-.2)yield return null;
        Check(burrow.CurrentPhase==SandStalker.Phase.Dormant&&!life.IsDead.Value,"no attacks before full thirty seconds expire");
        Stage.Value=11;
        yield return Wait(()=>burrow.CurrentPhase==SandStalker.Phase.Warning||burrow.CurrentPhase==SandStalker.Phase.Chasing,"Burrow respawns after thirty seconds",3);
        yield return Wait(()=>life.IsDead.Value,"grounded remote player is killed by Burrow strike",4);
        Check(burrow.CurrentPhase==SandStalker.Phase.Dormant,"Burrow disappears after attack");
        int before=whisper.Cue.Value;Park(burrow);Stage.Value=12;Reset(whisper,new Vector3(150,0,-10),late.PlayerObject.GetComponent<PlayerController>());
        NetworkManager.DisconnectClient(late.ClientId);yield return new WaitForSeconds(.8f);
        Check(whisper.CurrentPhase==SandStalker.Phase.Dormant&&whisper.CurrentTarget==null,"disconnect safely releases target");
        FinishClientRpc();File.WriteAllText("Logs/Stalker-host-PASS.txt","PASS "+checks+" real NGO authority, flashlight, retreat, handoff, bunker, obstruction, death, timed jump, full cooldown, late-join and disconnect assertions.");
        yield return new WaitForSeconds(.5f);Application.Quit();
    }
    IEnumerator Wait(Func<bool> condition,string message,float seconds){float limit=Time.realtimeSinceStartup+seconds;while(!condition()&&Time.realtimeSinceStartup<limit)yield return null;Check(condition(),message);}
    void Check(bool okay,string message){if(!okay){Fail(message);throw new Exception(message);}checks++;Debug.Log("[Stalker Test] PASS "+message);}
    void Fail(string message){File.WriteAllText("Logs/Stalker-"+role+"-FAIL.txt",message);Debug.LogError(message);Application.Quit(2);}
    [ClientRpc]void FinishClientRpc(){if(IsServer)return;File.WriteAllText("Logs/Stalker-"+role+"-PASS.txt","PASS: connected peer received authoritative encounter states, cues and targeted deaths.");Application.Quit();}
}
