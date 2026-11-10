using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public sealed class AstronautAnimationNetworkTest:NetworkBehaviour
{
    public NetworkVariable<int> Stage=new NetworkVariable<int>();
    string role;int checks,previous=-1;bool launched;float vertical,nextReport;Vector3 basePosition;
    readonly Dictionary<ulong,Report> reports=new Dictionary<ulong,Report>();
    struct Report{public string ownClip,hostClip;public bool jump,air,observedJump;public float phase,height,observedPhase;public double landed;public uint sequence;}
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    IEnumerator Start(){var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-animationTest");if(at<0)yield break;role=args[at+1];Application.runInBackground=true;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;NetworkManager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17867,"127.0.0.1");yield return null;if(role=="host")NetworkManager.StartHost();else NetworkManager.StartClient();yield return new WaitForSecondsRealtime(130);Fail("timeout");}
    public override void OnNetworkSpawn(){if(role!=null&&IsServer)StartCoroutine(Run());}
    static Vector2 Direction(int stage)=>stage==1||stage==3?Vector2.left:stage==2||stage==4?Vector2.right:stage==5||stage==8?Vector2.down:stage==6||stage==7?Vector2.up:Vector2.zero;
    static bool Sprint(int stage)=>stage==3||stage==4||stage==5||stage==7;
    static string Expected(int stage,bool host=false){if(host){if(stage==1)stage=2;else if(stage==2)stage=1;else if(stage==3)stage=4;else if(stage==4)stage=3;}return stage==1?"WalkLeft":stage==2?"WalkRight":stage==3?"RunLeft":stage==4?"RunRight":stage==5?"RunBackward":stage==6?"Walk":stage==7?"Sprint":stage==8?"WalkBack":"Idle";}
    static string Clip(PlayerBodyAnimator body){if(body==null)return "missing";var clips=body.bodyAnimator.GetCurrentAnimatorClipInfo(0);return clips.Length==0?"none":clips.OrderByDescending(c=>c.weight).First().clip.name;}
    void Update(){
        if(role==null||!IsSpawned||NetworkManager.LocalClient?.PlayerObject==null)return;
        var obj=NetworkManager.LocalClient.PlayerObject;var player=obj.GetComponent<PlayerController>();var motor=obj.GetComponent<CharacterController>();var movement=obj.GetComponent<NetworkPlayerMovementState>();var body=obj.GetComponent<PlayerBodyAnimator>();
        if(previous!=Stage.Value){previous=Stage.Value;launched=false;vertical=0;basePosition=new Vector3(role=="host"?6:role=="late"?15:-6,motor.height*.5f*obj.transform.lossyScale.y-motor.center.y*obj.transform.lossyScale.y+.03f,0);}
        bool physicsJump=role=="client"&&(Stage.Value==9||Stage.Value==10||Stage.Value==11);
        if(!physicsJump){motor.enabled=false;obj.transform.position=basePosition;motor.enabled=true;motor.Move(Vector3.down*.06f);Vector2 input=Direction(Stage.Value);if(role=="host")input.x=-input.x;
            movement.Animation.Value=new PlayerAnimationSnapshot{Move=input,Sprint=Sprint(Stage.Value)};
        }else{
            if(!launched){motor.enabled=false;obj.transform.position=basePosition+(Stage.Value==10?Vector3.up*3:Vector3.zero);motor.enabled=true;motor.Move(Vector3.down*.04f);vertical=Stage.Value==10?0:Mathf.Sqrt((Stage.Value==9?20:8)*-2*player.gravity);launched=true;typeof(PlayerController).GetField("animationTakeoff",Private).SetValue(player,Stage.Value!=10);}
            vertical+=player.gravity*Time.deltaTime;motor.Move(Vector3.up*vertical*Time.deltaTime);if(motor.isGrounded&&vertical<0)vertical=-2;
            typeof(PlayerController).GetField("controller",Private).SetValue(player,motor);typeof(PlayerController).GetField("movementState",Private).SetValue(player,movement);typeof(PlayerController).GetField("oxygenSystem",Private).SetValue(player,obj.GetComponent<OxygenSystem>());typeof(PlayerController).GetField("velocity",Private).SetValue(player,new Vector3(0,vertical,0));typeof(PlayerController).GetMethod("PublishAnimation",Private).Invoke(player,null);
        }
        obj.GetComponent<Networking.OwnerNetworkTransform>().Teleport(obj.transform.position,obj.transform.rotation,obj.transform.localScale);
        if(!IsServer&&Time.unscaledTime>=nextReport){nextReport=Time.unscaledTime+.06f;PlayerBodyAnimator hostBody=null;foreach(var actor in NetworkManager.SpawnManager.SpawnedObjectsList)if(actor!=null&&actor.IsPlayerObject&&actor.OwnerClientId==NetworkManager.ServerClientId)hostBody=actor.GetComponent<PlayerBodyAnimator>();var other=NetworkManager.SpawnManager.SpawnedObjectsList.FirstOrDefault(o=>o.IsPlayerObject&&o.OwnerClientId!=NetworkManager.LocalClientId&&o.OwnerClientId!=NetworkManager.ServerClientId);var observed=other==null?null:other.GetComponent<PlayerBodyAnimator>();var snapshot=movement.Animation.Value;ReportServerRpc(Clip(body),Clip(hostBody),body.IsPlayingJump,body.JumpPhase,snapshot.Airborne,obj.transform.position.y,snapshot.LandedAt,snapshot.JumpSequence,observed!=null&&observed.IsPlayingJump,observed==null?0:observed.JumpPhase);}
    }
    [ServerRpc(RequireOwnership=false)]void ReportServerRpc(string own,string host,bool jump,float phase,bool air,float height,double landed,uint sequence,bool observedJump,float observedPhase,ServerRpcParams rpc=default){reports[rpc.Receive.SenderClientId]=new Report{ownClip=own,hostClip=host,jump=jump,phase=phase,air=air,height=height,landed=landed,sequence=sequence,observedJump=observedJump,observedPhase=observedPhase};}
    PlayerBodyAnimator Body(ulong owner){foreach(var obj in NetworkManager.SpawnManager.SpawnedObjectsList)if(obj!=null&&obj.IsPlayerObject&&obj.OwnerClientId==owner)return obj.GetComponent<PlayerBodyAnimator>();return null;}
    IEnumerator Run(){
        yield return Wait(()=>NetworkManager.ConnectedClientsList.Count>=2&&reports.Count>0,"owner/client connected and reporting",25);ulong id=reports.Keys.First();var remote=NetworkManager.ConnectedClients[id].PlayerObject.GetComponent<NetworkPlayerMovementState>();
        Check(remote.Animation.CanClientWrite(id)&&!remote.Animation.CanClientWrite(NetworkManager.LocalClientId),"animation snapshot is owner-written");
        for(int stage=1;stage<=8;stage++){Stage.Value=stage;yield return Wait(()=>reports[id].ownClip==Expected(stage)&&reports[id].hostClip==Expected(stage,true)&&Clip(Body(id))==Expected(stage),"both peers show correct "+Expected(stage)+" clip",4);}
        var a=new PlayerAnimationSnapshot{Airborne=true,Takeoff=true,JumpSequence=1,JumpedAt=10};Check(AstronautJumpTiming.Sample(a,50,2.3666667f,out var hold)&&Mathf.Abs(hold-AstronautJumpTiming.AirHold)<.0001f,"long airtime holds jump phase without looping");a.Takeoff=false;Check(AstronautJumpTiming.Sample(a,10,2.3666667f,out hold)&&hold==AstronautJumpTiming.AirHold,"walking off a ledge starts in airborne pose");a.Suppressed=true;Check(!AstronautJumpTiming.Sample(a,12,2.3666667f,out _),"movement suppression cancels jump presentation");
        Stage.Value=9;yield return Wait(()=>reports[id].air&&reports[id].jump&&Mathf.Abs(reports[id].phase-AstronautJumpTiming.AirHold)<.005f,"actual controller jump reaches airborne hold on owning peer",4);
        yield return Wait(()=>Body(id).IsPlayingJump&&Mathf.Abs(Body(id).JumpPhase-AstronautJumpTiming.AirHold)<.005f,"observer reaches same airborne hold",2);
        var bone=Body(id).bodyAnimator.transform.Find("Bip001/Bip001 Pelvis/Bip001 L Thigh/Bip001 L Calf");if(bone==null)bone=Body(id).bodyAnimator.transform.Find("Bip001");Quaternion pose=bone.localRotation;yield return new WaitForSecondsRealtime(.25f);Check(Quaternion.Angle(pose,bone.localRotation)<.1f,"airborne bone pose stays frozen while body remains airborne");
        File.WriteAllText("Logs/Animation-latejoin.ready","ready");
        yield return Wait(()=>NetworkManager.ConnectedClientsList.Count>=3,"late observer joins",10);var late=NetworkManager.ConnectedClientsList.First(c=>c.ClientId!=id&&c.ClientId!=NetworkManager.LocalClientId);
        yield return Wait(()=>reports.ContainsKey(late.ClientId)&&reports[late.ClientId].observedJump&&Mathf.Abs(reports[late.ClientId].observedPhase-AstronautJumpTiming.AirHold)<.005f,"late observer receives current airborne pose without replaying takeoff",4);
        // Snapshot history survives landing; no network trigger can be lost during synchronization.
        yield return Wait(()=>!reports[id].air&&reports[id].landed>0,"landing comes from CharacterController ground contact",6);
        yield return Wait(()=>reports[id].jump&&reports[id].phase>=AstronautJumpTiming.Landing&&reports[id].phase<AstronautJumpTiming.RecoveryEnd,"owner plays landing section immediately after contact",1);
        yield return Wait(()=>Body(id).IsPlayingJump&&Body(id).JumpPhase>=AstronautJumpTiming.Landing,"observer plays landing section",1);
        yield return Wait(()=>!reports[id].jump&&Clip(Body(id))=="Idle","landing recovery returns to locomotion",3);
        Check(!Body(id).bodyAnimator.applyRootMotion,"jump clip cannot move the network character root");
        Stage.Value=10;yield return Wait(()=>reports[id].air&&reports[id].jump,"ledge fall uses airborne animation",3);Check(Mathf.Abs(reports[id].phase-AstronautJumpTiming.AirHold)<.01f,"ledge fall skips takeoff wind-up");yield return Wait(()=>!reports[id].air&&reports[id].landed>0,"ledge fall registers landing",4);yield return Wait(()=>!reports[id].jump,"ledge landing recovery completes",3);
        uint old=reports[id].sequence;Stage.Value=11;yield return Wait(()=>reports[id].air&&reports[id].sequence>old,"new jump after landing has a fresh sequence",3);yield return Wait(()=>!reports[id].air,"second jump lands",6);yield return Wait(()=>!reports[id].jump,"second recovery does not stick or loop",3);
        FinishClientRpc();File.WriteAllText("Logs/Animation-host-PASS.txt","PASS "+checks+" directional walk/run, owner authority, actual controller jump, held bone pose, landing timing, late synchronization, ledge fall and repeated-jump assertions.");yield return new WaitForSecondsRealtime(.5f);Application.Quit();
    }
    IEnumerator Wait(Func<bool> condition,string text,float seconds){float end=Time.realtimeSinceStartup+seconds;while(!condition()&&Time.realtimeSinceStartup<end)yield return null;Check(condition(),text);}
    void Check(bool okay,string text){if(!okay){Fail(text);throw new Exception(text);}checks++;Debug.Log("[Animation] PASS "+text);}
    void Fail(string text){File.WriteAllText("Logs/Animation-"+role+"-FAIL.txt",text);Debug.LogError(text);Application.Quit(2);}
    [ClientRpc]void FinishClientRpc(){if(IsServer)return;File.WriteAllText("Logs/Animation-"+role+"-PASS.txt","PASS: directional and time-synchronized jump animation observed over real NGO transport.");Application.Quit();}
}
