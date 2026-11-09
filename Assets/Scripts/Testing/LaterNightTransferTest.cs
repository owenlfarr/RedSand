#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Collections;
using Networking;

// Opt-in development regression against the real campaign scenes and transfer code.
public sealed class LaterNightTransferTest : MonoBehaviour
{
    GameObject originalPrefab;bool repeatStarted;NetworkManager manager;NetworkLevelProgression progression;string role;int checks;string remoteScene;int remoteMonsters;bool remoteDead;bool capturedEight,capturedBlank,capturedNight;float nextReport;int remoteCovers,remoteEight,remoteNight;bool remoteBlocked,remoteHasPlayer;string remoteDiagnostic;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap(){var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-laterNightTransferTest");if(at<0)return;var go=new GameObject("Later night transfer audit");DontDestroyOnLoad(go);go.AddComponent<LaterNightTransferTest>().role=args[at+1];}
    IEnumerator Start(){
        File.Delete("Logs/LaterNightTransfer-"+role+"-PASS.txt");if(role=="client")File.Delete("Logs/LaterNightTransfer-client-menu.ready");
        Application.runInBackground=true;Application.targetFrameRate=45;QualitySettings.vSyncCount=0;
        yield return null;manager=NetworkManager.Singleton;
        if(manager==null){Fail("Fixture manager missing");yield break;}
        foreach(var monster in FindObjectsByType<SandStalker>(FindObjectsSortMode.None))monster.enabled=false;
        originalPrefab=manager.NetworkConfig.PlayerPrefab;progression=manager.gameObject.AddComponent<NetworkLevelProgression>();
        manager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17858,"127.0.0.1");
        if(role=="host")manager.StartHost();else manager.StartClient();
        manager.CustomMessagingManager.RegisterNamedMessageHandler("LaterNightReport",Report);
        manager.CustomMessagingManager.RegisterNamedMessageHandler("LaterNightDone",Done);
        if(role=="host")StartCoroutine(Run());
        yield return new WaitForSecondsRealtime(220);Fail("Transfer timeout");
    }
    void Update(){
        if(role=="host"&&Array.IndexOf(Environment.GetCommandLineArgs(),"-transitionScreenshots")>=0){
            var curtain=NightTransition.Instance;
            if(!capturedEight&&curtain.VisibleTitle=="8 AM"&&curtain.FullyBlack){capturedEight=true;ScreenCapture.CaptureScreenshot("Logs/Transition8AM.png");}
            if(capturedEight&&!capturedBlank&&curtain.VisibleTitle==""&&curtain.FullyBlack){capturedBlank=true;ScreenCapture.CaptureScreenshot("Logs/TransitionPlainBlack.png");}
            if(!capturedNight&&curtain.VisibleTitle=="NIGHT 4"&&curtain.FullyBlack){capturedNight=true;ScreenCapture.CaptureScreenshot("Logs/TransitionNight4.png");}
        }
        if(role=="client"&&!repeatStarted&&SceneManager.GetActiveScene().name=="MainMenu"&&NightTransition.Instance.CompletedCovers>=6&&!NightTransition.BlocksGameplay){repeatStarted=true;File.WriteAllText("Logs/LaterNightTransfer-client-menu.ready","all five nights, final 8 AM, and covered menu return observed");StartCoroutine(RepeatClient());return;}
        if(manager==null||!manager.IsListening)return;
        if(manager.IsServer){foreach(var client in manager.ConnectedClientsList)if(client.PlayerObject!=null){var life=client.PlayerObject.GetComponent<NetworkOxygenState>();if(life!=null&&life.IsSpawned)life.IsInBunker.Value=true;}}
        else if(manager.IsConnectedClient&&Time.realtimeSinceStartup>=nextReport){
            nextReport=Time.realtimeSinceStartup+.15f;using(var writer=new FastBufferWriter(1024,Allocator.Temp)){
                writer.WriteValueSafe(SceneManager.GetActiveScene().name);writer.WriteValueSafe(FindObjectsByType<SandStalker>(FindObjectsSortMode.None).Length);
                var local=manager.LocalClient?.PlayerObject;writer.WriteValueSafe(local!=null&&local.GetComponent<NetworkOxygenState>().IsDead.Value);writer.WriteValueSafe(local!=null);writer.WriteValueSafe("clock="+(NightTimeManager.Instance!=null&&NightTimeManager.Instance.IsSpawned)+" title="+NightTransition.Instance.VisibleTitle+" scale="+Time.timeScale);writer.WriteValueSafe(NightTransition.Instance.CompletedCovers);writer.WriteValueSafe(NightTransition.Instance.EightAmCards);writer.WriteValueSafe(NightTransition.Instance.NightCards);writer.WriteValueSafe(NightTransition.BlocksGameplay);
                manager.CustomMessagingManager.SendNamedMessage("LaterNightReport",NetworkManager.ServerClientId,writer);
            }
        }
    }
    void Report(ulong sender,FastBufferReader reader){reader.ReadValueSafe(out remoteScene);reader.ReadValueSafe(out remoteMonsters);reader.ReadValueSafe(out remoteDead);reader.ReadValueSafe(out remoteHasPlayer);reader.ReadValueSafe(out remoteDiagnostic);reader.ReadValueSafe(out remoteCovers);reader.ReadValueSafe(out remoteEight);reader.ReadValueSafe(out remoteNight);reader.ReadValueSafe(out remoteBlocked);}
    void Done(ulong sender,FastBufferReader reader){if(role=="host")return;File.WriteAllText("Logs/LaterNightTransfer-client-PASS.txt","PASS: all five real nights, synchronized black/8 AM/night cards, spectator persistence, final menu return and a fresh-session fade handshake.");Application.Quit();}
    IEnumerator Run(){
        yield return Wait(()=>manager.ConnectedClientsList.Count==2,"two peers connected",30);
        yield return Wait(()=>remoteHasPlayer,"initial client finishes spawning",10);
        // Make initial auto-spawned actors scene-owned, matching the actual campaign's player lifetime.
        foreach(var client in manager.ConnectedClientsList){client.PlayerObject.DestroyWithScene=true;SceneManager.MoveGameObjectToScene(client.PlayerObject.gameObject,SceneManager.GetActiveScene());}
        yield return new WaitForSecondsRealtime(.5f);
        progression.TransferToLevel("Night1_MultiplayerTest");
        yield return Arrive("Night1_MultiplayerTest",0,false);
        Check(NightTransition.Instance.CompletedCovers==1&&NightTransition.Instance.EightAmCards==0&&remoteCovers==1&&remoteEight==0,"menu-style fade covers all peers without an 8 AM card");
        var firstClock=FindFirstObjectByType<NightTimeManager>();firstClock.useCustomEndingTime=true;firstClock.endingSequenceTriggerTime=firstClock.TotalElapsedTimeNetwork.Value+1;
        yield return Arrive("Night 2",0,false);
        firstClock=FindFirstObjectByType<NightTimeManager>();firstClock.useCustomEndingTime=true;firstClock.endingSequenceTriggerTime=firstClock.TotalElapsedTimeNetwork.Value+1;
        yield return Arrive("Night 3",0,false);
        foreach(var client in manager.ConnectedClientsList)if(client.ClientId!=manager.LocalClientId)client.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value=true;
        var clock=FindFirstObjectByType<NightTimeManager>();clock.useCustomEndingTime=true;clock.endingSequenceTriggerTime=clock.TotalElapsedTimeNetwork.Value+2;
        yield return Arrive("Night 4",1,true);
        Check(FindFirstObjectByType<RadarSystem>().UsesRemaining==12,"Night 4 host has twelve radar scans");
        Check(FindFirstObjectByType<PAScreecher>()!=null,"Night 4 retains PA");
        clock=FindFirstObjectByType<NightTimeManager>();clock.useCustomEndingTime=true;clock.endingSequenceTriggerTime=clock.TotalElapsedTimeNetwork.Value+2;
        yield return Arrive("Night 5",2,true);
        Check(FindFirstObjectByType<RadarSystem>().UsesRemaining==12,"Night 5 host has twelve radar scans");
        Check(FindFirstObjectByType<BlindLungeMonster>()!=null&&FindFirstObjectByType<PAScreecher>()!=null,"Night 5 retains lunge and PA");
        Check(string.IsNullOrEmpty(FindFirstObjectByType<NightTimeManager>().nextSceneName),"Night 5 completes the campaign");
        Check(NightTransition.Instance.CompletedCovers==5&&remoteCovers==5,"all five scene loads waited for peer black-screen acknowledgements");
        Check(NightTransition.Instance.EightAmCards==4&&remoteEight==4,"all four night transfers show 8 AM");
        Check(NightTransition.Instance.NightCards==5&&remoteNight==5,"all five arrival cards identify their night");
        clock=FindFirstObjectByType<NightTimeManager>();clock.useCustomEndingTime=true;clock.endingSequenceTriggerTime=clock.TotalElapsedTimeNetwork.Value+1;
        yield return Wait(()=>SceneManager.GetActiveScene().name=="MainMenu"&&!NightTransition.BlocksGameplay,"Night 5 completion returns host to menu under black",35);
        yield return Wait(()=>File.Exists("Logs/LaterNightTransfer-client-menu.ready"),"client returns to menu with final 8 AM card",20);
        Check(NightTransition.Instance.CompletedCovers==6&&NightTransition.Instance.EightAmCards==5,"six complete black gates and five 8 AM cards");
        yield return new WaitForSecondsRealtime(1);
        manager=NetworkManager.Singleton;manager.NetworkConfig.PlayerPrefab=originalPrefab;manager.NetworkConfig.ConnectionApproval=false;
        manager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17858,"127.0.0.1");Check(manager.StartHost(),"new party starts after campaign shutdown");
        manager.CustomMessagingManager.RegisterNamedMessageHandler("LaterNightReport",Report);manager.CustomMessagingManager.RegisterNamedMessageHandler("LaterNightDone",Done);
        yield return Wait(()=>manager.ConnectedClientsList.Count==2,"client reconnects for a fresh party",20);
        yield return NightTransition.Instance.CoverForLoad("Night1_MultiplayerTest",false);
        Check(manager.ConnectedClientsList.Count==2&&NightTransition.Instance.AllPeersCovered,"fresh-session token is accepted by both peers after prior six transitions");
        NightTransition.Instance.Cancel();yield return new WaitForSecondsRealtime(.3f);
        using(var writer=new FastBufferWriter(1,Allocator.Temp))manager.CustomMessagingManager.SendNamedMessageToAll("LaterNightDone",writer);
        yield return Wait(()=>File.Exists("Logs/LaterNightTransfer-client-PASS.txt"),"reconnected client finishes regression",5);
        File.WriteAllText("Logs/LaterNightTransfer-host-PASS.txt","PASS "+checks+" assertions: real Night 1 -> 2 -> 3 -> 4 -> 5 network loading and synchronized fade/title/8 AM cards, remote monster counts, survivor/spectator persistence and twelve radar scans.");
        yield return new WaitForSecondsRealtime(.6f);Application.Quit();
    }
    IEnumerator RepeatClient(){
        yield return new WaitForSecondsRealtime(4);
        manager=NetworkManager.Singleton;manager.NetworkConfig.PlayerPrefab=originalPrefab;manager.NetworkConfig.ConnectionApproval=false;
        manager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17858,"127.0.0.1");manager.StartClient();
        manager.CustomMessagingManager.RegisterNamedMessageHandler("LaterNightReport",Report);manager.CustomMessagingManager.RegisterNamedMessageHandler("LaterNightDone",Done);
    }
    IEnumerator Arrive(string scene,int monsters,bool dead){
        yield return Wait(()=>SceneManager.GetActiveScene().name==scene&&!progression.IsTransferring&&manager.LocalClient?.PlayerObject!=null,"host arrives "+scene,45);
        yield return Wait(()=>remoteScene==scene&&remoteMonsters==monsters&&remoteDead==dead&&remoteHasPlayer&&!remoteBlocked&&!NightTransition.BlocksGameplay,"client agrees "+scene+" monster count and death state",15);
        Check(!manager.LocalClient.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value,"living host remains playable in "+scene);
        Check(FindObjectsByType<SandStalker>(FindObjectsSortMode.None).Length==monsters,"host monster count "+scene);
        foreach(var monster in FindObjectsByType<SandStalker>(FindObjectsSortMode.None))Check(monster.IsSpawned&&monster.IsServer,"new monster spawned on host "+scene);
    }
    IEnumerator Wait(Func<bool> okay,string label,float seconds){float end=Time.realtimeSinceStartup+seconds;while(!okay()&&Time.realtimeSinceStartup<end)yield return null;Check(okay(),label);}
    void Check(bool okay,string label){if(!okay){Fail(label);throw new Exception(label);}checks++;Debug.Log("[Later Transfer] PASS "+label);}
    void Fail(string label){label+=" | remote="+remoteScene+" dead="+remoteDead+" blocked="+remoteBlocked+" covers="+remoteCovers+" nightCards="+remoteNight+" hasPlayer="+remoteHasPlayer+" "+remoteDiagnostic+" localScene="+SceneManager.GetActiveScene().name+" localBlocked="+NightTransition.BlocksGameplay;File.WriteAllText("Logs/LaterNightTransfer-"+role+"-FAIL.txt",label);Debug.LogError(label);Application.Quit(2);}
}
#endif
