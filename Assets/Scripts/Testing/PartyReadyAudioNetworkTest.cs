#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Collections;
using Networking;

[DefaultExecutionOrder(-200)]
public sealed class PartyReadyAudioNetworkTest:MonoBehaviour
{
    string role,delayScene="",disabledScene="";float delayUntil,nextReport;NetworkManager manager;RelayPartyManager relay;int checks;
    bool remoteReleased,remoteSpectating;string remoteScene="";int remoteSteps,remoteJumps,remoteLands;bool remoteBunker;
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Bootstrap(){var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-partyReadyAudioTest");if(index<0)return;var root=new GameObject("Party readiness and movement audio regression");DontDestroyOnLoad(root);root.AddComponent<PartyReadyAudioNetworkTest>().role=args[index+1];}
    IEnumerator Start(){
        Application.runInBackground=true;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;yield return null;yield return null;
        manager=NetworkManager.Singleton;relay=FindFirstObjectByType<RelayPartyManager>();Check(manager!=null&&relay!=null,"production menu exists");
        manager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17869,"127.0.0.1");
        typeof(RelayPartyManager).GetMethod("ConfigureLobbyConnectionApproval",Private).Invoke(relay,new object[]{manager});
        Check(role=="host"?manager.StartHost():manager.StartClient(),"lobby transport starts");
        manager.CustomMessagingManager.RegisterNamedMessageHandler("PartyAudioReport",Report);manager.CustomMessagingManager.RegisterNamedMessageHandler("PartyAudioDone",Done);
        if(role=="host")StartCoroutine(Run());yield return new WaitForSecondsRealtime(180);Fail("timeout");
    }
    void Update(){
        if(manager==null||!manager.IsListening)return;
        var local=manager.LocalClient?.PlayerObject;string scene=SceneManager.GetActiveScene().name;
        if(manager.IsServer&&disabledScene!=scene){
            disabledScene=scene;
            // Isolate presentation from encounters without changing production assets.
            foreach(var ai in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(ai is SandStalker||ai is BlindLungeMonster||ai is EyeMonster||ai is ZombieAI||ai is PowerStationWatcher||ai is SurveyorMonster||ai is PAScreecher)ai.enabled=false;
        }
        if(role=="client"&&scene=="Night 3"&&local!=null){
            var p=local.GetComponent<PlayerController>();if(delayScene!=scene){delayScene=scene;delayUntil=Time.realtimeSinceStartup+5;}
            if(p.playerCamera!=null)p.playerCamera.GetComponent<Camera>().enabled=Time.realtimeSinceStartup>=delayUntil;
        }
        if(role=="client"&&manager.IsConnectedClient&&Time.realtimeSinceStartup>=nextReport){
            nextReport=Time.realtimeSinceStartup+.08f;
            var observed=manager.SpawnManager.SpawnedObjectsList.FirstOrDefault(o=>o!=null&&o.IsPlayerObject&&o.OwnerClientId==NetworkManager.ServerClientId);
            var audio=observed==null?null:observed.GetComponent<PlayerSurfaceAudio>();
            using(var w=new FastBufferWriter(512,Allocator.Temp)){
                w.WriteValueSafe(scene);w.WriteValueSafe(!NightTransition.BlocksGameplay);w.WriteValueSafe(local!=null&&local.GetComponent<PlayerDeathSpectator>()?.IsSpectating==true);
                w.WriteValueSafe(audio==null?0:audio.StepCount);w.WriteValueSafe(audio==null?0:audio.JumpCount);w.WriteValueSafe(audio==null?0:audio.LandingCount);w.WriteValueSafe(audio!=null&&audio.LastWasBunker);
                manager.CustomMessagingManager.SendNamedMessage("PartyAudioReport",NetworkManager.ServerClientId,w);
            }
        }
    }
    void Report(ulong sender,FastBufferReader r){r.ReadValueSafe(out remoteScene);r.ReadValueSafe(out remoteReleased);r.ReadValueSafe(out remoteSpectating);r.ReadValueSafe(out remoteSteps);r.ReadValueSafe(out remoteJumps);r.ReadValueSafe(out remoteLands);r.ReadValueSafe(out remoteBunker);}
    void Done(ulong sender,FastBufferReader r){File.WriteAllText("Logs/PartyReadyAudio-client-PASS.txt","PASS slow-client readiness, remote movement audio and spectator synchronization.");Application.Quit();}
    PlayerController Player=>manager.LocalClient.PlayerObject.GetComponent<PlayerController>();
    PlayerSurfaceAudio Audio=>Player.GetComponent<PlayerSurfaceAudio>();
    NetworkPlayerMovementState Movement=>Player.GetComponent<NetworkPlayerMovementState>();
    IEnumerator Run(){
        yield return Wait(()=>manager.ConnectedClientsList.Count==2,"two lobby peers connected",25);
        typeof(RelayPartyManager).GetField("playtestStartNightThree",Private).SetValue(relay,true);typeof(RelayPartyManager).GetField("matchStarted",Private).SetValue(relay,true);
        Check((bool)typeof(RelayPartyManager).GetMethod("LoadMultiplayerSceneAsHost",Private).Invoke(relay,new object[]{manager}),"real Start Game loading path");
        yield return Wait(()=>SceneManager.GetActiveScene().name=="Night 3"&&manager.LocalClient?.PlayerObject!=null,"host player spawned",45);
        yield return new WaitForSecondsRealtime(2);
        Check(NightTransition.Instance.FullyBlack&&!NightTransition.Instance.AllPeersLoaded&&NightTransition.BlocksGameplay,"host remains fully black while client camera is deliberately unready");
        Check(!remoteReleased,"slow client has not revealed early");
        float clock=NightTimeManager.Instance.TotalElapsedTimeNetwork.Value;yield return new WaitForSecondsRealtime(.75f);
        Check(Mathf.Abs(NightTimeManager.Instance.TotalElapsedTimeNetwork.Value-clock)<.01f,"night clock waits with the party");
        yield return Wait(()=>!NightTransition.BlocksGameplay&&NightTransition.Instance.AllPeersLoaded,"host releases after client readiness acknowledgement",15);
        yield return Wait(()=>remoteScene=="Night 3"&&remoteReleased,"client releases same night",5);
        Check(Audio!=null&&Audio.BunkerSource.spatialBlend==1&&Audio.BunkerSource.GetComponent<AudioEchoFilter>()!=null&&Audio.SurfaceSource.GetComponent<AudioEchoFilter>()==null,"spatial bunker echo and dry surface sources installed");
        Player.enabled=false;yield return new WaitForSecondsRealtime(.3f);
        Vector3 bunker=Player.transform.position;Check(Player.GetComponent<OxygenSystem>().IsInsideBunkerZone(),"audio test starts in real bunker");
        yield return ExerciseSurface(true);
        var blind=FindFirstObjectByType<BlindLungeMonster>();Vector3 outside=blind.transform.position+new Vector3(8,0,8);
        foreach(var terrain in Terrain.activeTerrains){var origin=terrain.transform.position;var size=terrain.terrainData.size;if(outside.x>=origin.x&&outside.x<=origin.x+size.x&&outside.z>=origin.z&&outside.z<=origin.z+size.z){outside.y=terrain.SampleHeight(outside)+origin.y+1.1f;break;}}
        Teleport(outside);yield return new WaitForSecondsRealtime(.4f);Check(!Player.GetComponent<OxygenSystem>().IsInsideBunkerZone(),"audio test reaches exterior sand");
        yield return ExerciseSurface(false);
        int before=Audio.StepCount;Movement.Animation.Value=new PlayerAnimationSnapshot{Move=Vector2.right};yield return new WaitForSecondsRealtime(.75f);Check(Audio.StepCount==before,"walking input against a stationary character makes no footsteps");
        Teleport(bunker);yield return new WaitForSecondsRealtime(.15f);Check(Audio.StepCount==before,"teleport does not synthesize a footstep burst");
        var remote=manager.ConnectedClientsList.First(c=>c.ClientId!=manager.LocalClientId);remote.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value=true;
        manager.GetComponent<NetworkLevelProgression>().TransferToLevel("Night 4");
        yield return Wait(()=>SceneManager.GetActiveScene().name=="Night 4"&&manager.LocalClient?.PlayerObject!=null,"next-night players spawned",45);
        yield return new WaitForSecondsRealtime(1);
        Check(NightTransition.Instance.FullyBlack&&NightTransition.BlocksGameplay,"next night waits under black for remote spectator camera");
        yield return Wait(()=>!NightTransition.BlocksGameplay&&NightTransition.Instance.AllPeersLoaded,"next-night barrier completes",15);
        yield return Wait(()=>remoteScene=="Night 4"&&remoteReleased&&remoteSpectating,"dead connected peer reaches working spectator before reveal",5);
        using(var w=new FastBufferWriter(1,Allocator.Temp))manager.CustomMessagingManager.SendNamedMessageToAll("PartyAudioDone",w);
        File.WriteAllText("Logs/PartyReadyAudio-host-PASS.txt","PASS "+checks+" production start/next-night readiness, delayed camera, spectator, clock freeze, local/remote bunker and sand walking/running/jumping/landing audio, echo setup, idle and teleport assertions.");yield return new WaitForSecondsRealtime(.5f);Application.Quit();
    }
    IEnumerator ExerciseSurface(bool bunker){
        string label=bunker?"bunker":"surface";int own=Audio.StepCount,remote=remoteSteps;
        yield return Walk(false,3);yield return Wait(()=>remoteSteps>remote,label+" walking audible on observing client",3);
        int walked=Audio.StepCount-own;Check(walked>0&&Audio.LastWasBunker==bunker,label+" owner walking uses correct bank");Check(remoteBunker==bunker,label+" observer uses correct bank");
        own=Audio.StepCount;remote=remoteSteps;yield return Walk(true,3);yield return Wait(()=>remoteSteps>remote,label+" running audible on observing client",3);
        Check(Audio.StepCount-own>walked,label+" running has a faster step cadence than walking");
        int jumps=Audio.JumpCount,lands=Audio.LandingCount,observedJumps=remoteJumps,observedLands=remoteLands;
        yield return Jump();yield return Wait(()=>remoteJumps>observedJumps&&remoteLands>observedLands,label+" jump and landing heard by remote peer",3);
        Check(Audio.JumpCount==jumps+1&&Audio.LandingCount==lands+1,label+" jump/landing play once each");Check(Audio.LastWasBunker==bunker&&remoteBunker==bunker,label+" jump/landing retain correct surface sound");
        Movement.Animation.Value=new PlayerAnimationSnapshot();int count=Audio.StepCount;yield return new WaitForSecondsRealtime(.5f);Check(Audio.StepCount==count,label+" idle stays silent");
    }
    IEnumerator Walk(bool sprint,float duration){
        Vector3 start=Player.transform.position;float end=Time.realtimeSinceStartup+duration,phase=0;var motor=Player.GetComponent<CharacterController>();
        while(Time.realtimeSinceStartup<end){phase+=(sprint?9:4.5f)/.6f*Time.deltaTime;Vector3 target=start+new Vector3(Mathf.Sin(phase)*.6f,0,(Mathf.Cos(phase)-1)*.6f);
            motor.Move(Vector3.ProjectOnPlane(target-Player.transform.position,Vector3.up)+Vector3.down*2*Time.deltaTime);
            Movement.Animation.Value=new PlayerAnimationSnapshot{Move=Vector2.right,Sprint=sprint};yield return null;
        }
        Movement.Animation.Value=new PlayerAnimationSnapshot();yield return new WaitForSecondsRealtime(.2f);
    }
    IEnumerator Jump(){
        var motor=Player.GetComponent<CharacterController>();float vertical=Mathf.Sqrt(1.2f*-2*Player.gravity);double started=manager.ServerTime.Time;uint sequence=Movement.Animation.Value.JumpSequence+1;float end=Time.realtimeSinceStartup+4;int steps=Audio.StepCount;
        while(Time.realtimeSinceStartup<end){vertical+=Player.gravity*Time.deltaTime;var flags=motor.Move(Vector3.up*vertical*Time.deltaTime);if((flags&CollisionFlags.Above)!=0&&vertical>0)vertical=0;bool air=!motor.isGrounded||vertical>0;
            Movement.Animation.Value=new PlayerAnimationSnapshot{Airborne=air,Takeoff=true,JumpSequence=sequence,JumpedAt=started,LandedAt=air?0:manager.ServerTime.Time};
            yield return null;if(!air)break;
        }
        Check(Audio.StepCount==steps,"no grounded step sounds while airborne");yield return new WaitForSecondsRealtime(.25f);
    }
    void Teleport(Vector3 position){var motor=Player.GetComponent<CharacterController>();motor.enabled=false;Player.transform.position=position;motor.enabled=true;Player.GetComponent<OwnerNetworkTransform>().Teleport(position,Player.transform.rotation,Player.transform.localScale);}
    IEnumerator Wait(Func<bool> condition,string message,float seconds){float end=Time.realtimeSinceStartup+seconds;while(!condition()&&Time.realtimeSinceStartup<end)yield return null;Check(condition(),message);}
    void Check(bool condition,string message){if(!condition){Fail(message);throw new Exception(message);}checks++;Debug.Log("[Party Audio] PASS "+message);}
    void Fail(string message){File.WriteAllText("Logs/PartyReadyAudio-"+role+"-FAIL.txt",message);Debug.LogError(message);Application.Quit(2);}
}
#endif
