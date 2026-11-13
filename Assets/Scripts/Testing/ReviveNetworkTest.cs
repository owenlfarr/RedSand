#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Networking;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(11000)]
public sealed class ReviveNetworkTest : NetworkBehaviour
{
    static string role, run;
    static int assertions, mutedFrames;
    readonly HashSet<ulong> ready = new HashSet<ulong>();
    int verifyId;
    ReviveStation Station => FindFirstObjectByType<ReviveStation>();
    DrillSystem Drill => FindFirstObjectByType<DrillSystem>();
    ReviveSession Session => NetworkManager.GetComponent<ReviveSession>();
    IEnumerator Start()
    {
        var args=Environment.GetCommandLineArgs(); int at=Array.IndexOf(args,"-reviveTest"); if(at<0) yield break;
        role=args[at+1]; run=args[at+2];
        Application.targetFrameRate=60; QualitySettings.vSyncCount=0; Application.runInBackground=true;
        var manager=Unity.Netcode.NetworkManager.Singleton;
        if(!manager.IsListening)
        {
            DontDestroyOnLoad(manager.gameObject); manager.NetworkConfig.ConnectionApproval=false;
            if(manager.GetComponent<ReviveTestInputFreeze>()==null)manager.gameObject.AddComponent<ReviveTestInputFreeze>();
            manager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17877,"127.0.0.1");
            if(role=="host") manager.StartHost(); else manager.StartClient();
        }
        float deadline=Time.realtimeSinceStartup+180f;
        while(Time.realtimeSinceStartup<deadline) yield return null;
        Fail("timeout");
    }
    public override void OnNetworkSpawn()
    {
        if(IsServer) StartCoroutine(gameObject.scene.name=="ReviveTransferTest" ? Transfer() : Run());
    }
    void LateUpdate()
    {
        if(!IsSpawned || Station==null || !Station.State.Value.Active) return;
        var job=Station.State.Value;
        var player=NetworkManager.LocalClient?.PlayerObject;
        if(player==null) return;
        if(job.Target==NetworkManager.LocalClientId && NetworkManager.ServerTime.Time<job.EndsAt-0.05)
        {
            if(!player.GetComponent<NetworkOxygenState>().IsDead.Value) Fail("respawn happened before 15 seconds");
            var gate=NetworkManager.GetComponent<ReviveAudioGate>();
            if(gate==null || !gate.IsSilenced || AudioListener.volume!=0f || !AudioListener.pause) Fail("victim audio leaked during paid countdown");
            if(!NetworkManager.GetComponent<VoiceChatManager>().ReviveMuted) Fail("voice not muted during countdown");
            mutedFrames++;
        }
    }
    IEnumerator Run()
    {
        yield return Wait(()=>NetworkManager.ConnectedClientsIds.Count==4,30f,"host plus three real clients connected");
        yield return new WaitForSecondsRealtime(1f);
        ulong remote=NetworkManager.ConnectedClientsIds.Max(), other=NetworkManager.ConnectedClientsIds.Where(id=>id!=0).OrderBy(id=>id).Skip(1).First();
        File.WriteAllText("Logs/Revive-"+run+"-remote.txt",remote.ToString());
        File.WriteAllText("Logs/Revive-"+run+"-other.txt",other.ToString());
        foreach (ulong id in NetworkManager.ConnectedClientsIds) MoveClientRpc(id, new Vector3(40 + id * 3,1.6f,0));
        yield return new WaitForSecondsRealtime(0.6f);
        yield return Place(0,true);
        yield return Place(other,true);
        GivePlasma(1);
        CommandClientRpc(0,"activate"); yield return new WaitForSecondsRealtime(0.3f);
        Check(!Session.Current.Active && Drill.Current.Plasma==1,"no fallen crew cannot consume plasma");
        Kill(remote,true);
        yield return Wait(()=>Session.Current.Target==remote,3f,"dead teammate becomes selectable");
        Kill(other,false);
        CommandClientRpc(0,"cycle");yield return new WaitForSecondsRealtime(0.3f);
        Check(Session.Current.Target==other,"Q selects second fallen teammate globally");
        yield return VerifyAll("selected teammate replicated",false,1);
        CommandClientRpc(0,"cycle");yield return new WaitForSecondsRealtime(0.3f);
        Check(Session.Current.Target==remote,"Q wraps to first fallen teammate");
        ulong extra = NetworkManager.ConnectedClientsIds.First(id => id != 0 && id != remote && id != other);
        Kill(extra, false);
        CommandClientRpc(0,"cycle"); yield return new WaitForSecondsRealtime(0.3f);
        Check(Session.Current.Target == extra, "Q reaches third fallen teammate in four-player party");
        CommandClientRpc(0,"cycle"); yield return new WaitForSecondsRealtime(0.3f);
        Check(Session.Current.Target == other, "Q cycles all three fallen teammates");
        CommandClientRpc(0,"cycle"); yield return new WaitForSecondsRealtime(0.3f);
        Check(Session.Current.Target == remote, "Q wraps to fourth player");
        var oldExtra = NetworkManager.ConnectedClients[extra].PlayerObject; oldExtra.Despawn(true);
        Instantiate(NetworkManager.NetworkConfig.PlayerPrefab, new Vector3(30,1.6f,0), Quaternion.identity).GetComponent<NetworkObject>().SpawnAsPlayerObject(extra,true);
        yield return new WaitForSecondsRealtime(0.5f);
        CommandClientRpc(0,"stale");yield return new WaitForSecondsRealtime(0.2f);
        Check(!Session.Current.Active&&Drill.Current.Plasma==1,"stale target selection cannot spend plasma on another teammate");
        var oldOther=NetworkManager.ConnectedClients[other].PlayerObject;oldOther.Despawn(true);
        var resetOther=Instantiate(NetworkManager.NetworkConfig.PlayerPrefab,new Vector3(6f,1.6f,0f),Quaternion.identity);
        resetOther.GetComponent<NetworkObject>().SpawnAsPlayerObject(other,true);
        yield return new WaitForSecondsRealtime(0.5f);yield return Place(other,true);
        GivePlasma(0);
        CommandClientRpc(0,"activate"); yield return new WaitForSecondsRealtime(0.3f);
        Check(!Session.Current.Active,"insufficient plasma rejects revival");
        GivePlasma(1);
        yield return Place(0,false);
        CommandClientRpc(0,"activate"); yield return new WaitForSecondsRealtime(0.3f);
        Check(!Session.Current.Active && Drill.Current.Plasma==1,"distant sender cannot spend team plasma");
        yield return Place(0,true);
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
        var actor=NetworkManager.ConnectedClients[0].PlayerObject;
        Vector3 eye=actor.transform.position+Vector3.up*1.5f,target=Station.consoleCollider.ClosestPoint(eye);
        wall.transform.position=Vector3.Lerp(eye,target,0.5f); wall.transform.rotation=Quaternion.LookRotation(target-eye); wall.transform.localScale=new Vector3(2f,2f,0.1f); Physics.SyncTransforms();
        CommandClientRpc(0,"activate"); yield return new WaitForSecondsRealtime(0.3f);
        Check(!Session.Current.Active && Drill.Current.Plasma==1,"wall blocks paid activation");
        Destroy(wall); yield return null;
        CommandClientRpc(0,"activate"); CommandClientRpc(other,"activate");
        yield return Wait(()=>Session.Current.Active,3f,"valid station request begins revival");
        double deadline=Session.Current.EndsAt;
        Check(Math.Abs(deadline-NetworkManager.ServerTime.Time-15)<0.15,"server schedules a full 15-second countdown");
        Check(Drill.Current.Plasma==0,"simultaneous activation spends exactly one shared plasma");
        yield return VerifyAll("paid countdown replicated",true,0);
        Kill(0,false); // Operator death must not cancel a paid job.
        yield return new WaitForSecondsRealtime(0.3f);
        Check(Session.Current.Active && Session.Current.EndsAt==deadline,"operator death does not interrupt revive");
        Call(FindFirstObjectByType<PowerSystem>(),"TriggerPowerOutage");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(Session.Current.Active && Session.Current.EndsAt==deadline,"power outage does not interrupt revive");
        FindFirstObjectByType<PowerSystem>().RestorePower();
        Time.timeScale=0f;yield return new WaitForSecondsRealtime(0.5f);Time.timeScale=1f;
        Check(Session.Current.Active&&Session.Current.EndsAt==deadline,"scaled-time pause cannot reset or interrupt countdown");
        MoveClientRpc(other,new Vector3(5f,1.6f,0f)); yield return new WaitForSecondsRealtime(0.6f);
        CommandClientRpc(other,"radar"); CommandClientRpc(other,"defense");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(FindFirstObjectByType<RadarSystem>().IsOnCooldownNetwork.Value,"actual radar activation accepted during countdown");
        Check(FindFirstObjectByType<DefenseSystem>().IsOnCooldownNetwork.Value,"actual defense activation accepted during countdown");
        Check(Session.Current.Active && Session.Current.EndsAt==deadline,"radar and defense leave countdown unchanged");
        yield return VerifyAll("radar and defense on all four peers",true,0);
        CommandClientRpc(other,"activate");
        Check(Drill.Current.Plasma==0,"repeated requests do not double charge");
        yield return Wait(()=>Alive(remote),20f,"remote revived after uninterrupted countdown");
        Check(NetworkManager.ServerTime.Time>=deadline,"no early server respawn");
        Check(NetworkManager.ServerTime.Time<deadline+0.5,"revive completes on time despite scaled pause");
        Check(Mathf.Abs(NetworkManager.ConnectedClients[remote].PlayerObject.transform.position.x-12f)<0.2f,"remote initially spawned at configured respawn marker");
        yield return new WaitForSecondsRealtime(0.7f);
        yield return VerifyAll("remote fresh body and restored sound",false,0);
        Check(Alive(remote) && NetworkManager.ConnectedClients[remote].PlayerObject.GetComponent<NetworkPlayerMovementState>().MovementEnabled.Value,"remote movement re-enabled");
        var revived=NetworkManager.ConnectedClients[remote].PlayerObject;
        Check(revived.GetComponent<NetworkOxygenState>().CurrentOxygen.Value>revived.GetComponent<OxygenSystem>().maxOxygen-3f,"revive replenishes oxygen");

        // Revive the host across an actual network scene transfer.
        GivePlasma(2);
        yield return Place(remote,true);
        yield return Wait(()=>Session.Current.Target==0,3f,"fallen host selectable");
        // Force the progression roster to treat the host as eliminated; revival must update it.
        var progression=NetworkManager.GetComponent<NetworkLevelProgression>();
        var roster=(HashSet<ulong>)typeof(NetworkLevelProgression).GetField("survivors",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(progression);
        foreach (ulong id in NetworkManager.ConnectedClientsIds) if (id != 0) roster.Add(id);
        typeof(NetworkLevelProgression).GetField("hasRoster",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(progression,true);
        CommandClientRpc(remote,"activate");
        yield return Wait(()=>Session.Current.Active,3f,"remote teammate starts host revival");
        yield return VerifyAll("host countdown and single plasma spend",true,1);
        File.WriteAllText("Logs/Revive-"+run+"-deadline.txt",Session.Current.EndsAt.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
        yield return new WaitForSecondsRealtime(1f);
        Check(NetworkManager.SceneManager.LoadScene("ReviveTransferTest",LoadSceneMode.Single)==SceneEventProgressStatus.Started,"network scene transfer started during revival");
    }
    IEnumerator Transfer()
    {
        yield return Wait(()=>Station!=null && Station.IsSpawned && Session!=null && Session.Current.Active,10f,"paid job survives network scene transfer");
        yield return new WaitForSecondsRealtime(0.8f);
        // This isolated fixture loads directly through NGO; mirror the real
        // NetworkLevelProgression scene-complete player replacement pass.
        int slot=0;
        foreach(var client in NetworkManager.ConnectedClientsList.ToArray())
        {
            if(client.PlayerObject!=null)continue;
            var actor=Instantiate(NetworkManager.NetworkConfig.PlayerPrefab,new Vector3(40f+3f*slot++,1.6f,10f),Quaternion.identity);
            actor.GetComponent<NetworkObject>().SpawnAsPlayerObject(client.ClientId,true);
        }
        yield return new WaitForSecondsRealtime(0.3f);
        Check(NetworkManager.ConnectedClientsList.All(c=>c.PlayerObject!=null),"all connected players have objects after scene loading");
        double deadline=double.Parse(File.ReadAllText("Logs/Revive-"+run+"-deadline.txt"),System.Globalization.CultureInfo.InvariantCulture);
        Check(Session.Current.EndsAt==deadline,"scene change does not reset 15-second timer");
        yield return Wait(()=>Alive(0),20f,"host revived after transfer");
        Check(NetworkManager.ServerTime.Time>=deadline,"host revive obeys deadline");
        Check(NetworkManager.ServerTime.Time<deadline+0.5,"scene transfer does not delay completed revival");
        Check(Mathf.Abs(NetworkManager.ConnectedClients[0].PlayerObject.transform.position.x-22f)<0.2f,"host initially uses destination scene respawn marker");
        yield return new WaitForSecondsRealtime(0.7f);
        Check(!NetworkManager.GetComponent<NetworkLevelProgression>().IsEliminated(0),"host restored to progression survivor roster");
        yield return VerifyAll("host healthy and audio restored after transfer",false,1);

        ulong other=ulong.Parse(File.ReadAllText("Logs/Revive-"+run+"-other.txt"));
        Kill(other,false);
        yield return Place(0,true);
        yield return Wait(()=>Session.Current.Target==other,3f,"next dead teammate selectable");
        CommandClientRpc(0,"activate");
        yield return Wait(()=>Session.Current.Active,3f,"disconnect refund case started");
        Check(Drill.Current.Plasma==0,"refund case reserves plasma");
        yield return new WaitForSecondsRealtime(0.8f);
        DisconnectClientRpc(new ClientRpcParams {Send=new ClientRpcSendParams {TargetClientIds=new[]{other}}});
        yield return Wait(()=>NetworkManager.ConnectedClientsIds.Count==3 && !Session.Current.Active,5f,"departed target clears impossible revival");
        Check(Drill.Current.Plasma==1,"departed target refunds plasma once");
        yield return VerifyAll("refund replicated and no stuck job",false,1);
        ulong remaining = NetworkManager.ConnectedClientsIds.First(id => id != 0);
        Kill(remaining, false);
        yield return Place(0, true);
        yield return Wait(() => Session.Current.Target == remaining, 3f, "station removal target selected");
        CommandClientRpc(0, "activate");
        yield return Wait(() => Session.Current.Active, 3f, "station removal job paid");
        double completion = Session.Current.EndsAt;
        yield return VerifyAll("station removal countdown replicated", true, 0);
        Station.NetworkObject.Despawn(true);
        yield return Wait(() => Alive(remaining), 20f, "revival completes without its console");
        Check(NetworkManager.ConnectedClients[remaining].PlayerObject.GetComponent<NetworkOxygenState>().LastReviveCompletedAt.Value == completion, "completion acknowledgement independent of console");
        yield return new WaitForSecondsRealtime(0.8f);
        ready.Clear(); verifyId++;
        VerifyStationlessClientRpc(verifyId);
        yield return Wait(() => ready.Count == NetworkManager.ConnectedClientsIds.Count, 6f, "all peers recover without console");
        FinishClientRpc();
        File.WriteAllText("Logs/Revive-"+run+"-host-PASS.txt",assertions+" assertions; local muted frames="+mutedFrames);
        yield return new WaitForSecondsRealtime(0.5f); Application.Quit(0);
    }
    [ClientRpc] void VerifyStationlessClientRpc(int id)
    {
        Check(!ReviveAudioGate.IsSilencingLocalPlayer, "stationless completion releases audio gate");
        Check(AudioListener.volume > 0.9f && !AudioListener.pause, "stationless completion restores game sound");
        Check(!NetworkManager.GetComponent<VoiceChatManager>().ReviveMuted, "stationless completion restores voice");
        ReadyServerRpc(id);
    }
    bool Alive(ulong id)=>NetworkManager.ConnectedClients.TryGetValue(id,out var c)&&c.PlayerObject!=null&&!c.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value;
    void Kill(ulong id,bool oxygen)
    {
        var state=NetworkManager.ConnectedClients[id].PlayerObject.GetComponent<NetworkOxygenState>();
        state.UseBlackDeathIntro.Value=oxygen; state.IsDead.Value=true;
    }
    void GivePlasma(int amount)
    {
        var inventory=NetworkManager.GetComponent<DrillSessionStockpile>(); var state=inventory.Materials; state.Plasma=amount; inventory.Materials=state;
    }
    IEnumerator Place(ulong id,bool near)
    {
        Vector3 pos=Station.consoleCollider.bounds.center+Vector3.back*(near?2.2f:20f)+Vector3.right*(id==0?-0.8f:0.8f); pos.y=1.6f;
        MoveClientRpc(id,pos); yield return new WaitForSecondsRealtime(0.6f);
        if(near) Check(Station.CanUse(NetworkManager.ConnectedClients[id].PlayerObject.gameObject),"real console reach for "+id);
    }
    [ClientRpc] void MoveClientRpc(ulong id,Vector3 position)
    {
        if(NetworkManager.LocalClientId!=id)return;
        var player=NetworkManager.LocalClient.PlayerObject; var motor=player.GetComponent<CharacterController>(); motor.enabled=false; player.transform.position=position;
        var control=player.GetComponent<PlayerController>(); control.gravity=0f;control.walkSpeed=control.sprintSpeed=0f;
        typeof(PlayerController).GetField("velocity",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(control,Vector3.zero); motor.enabled=true;
    }
    [ClientRpc] void CommandClientRpc(ulong id,string action)
    {
        if(NetworkManager.LocalClientId!=id)return;
        if(action=="activate")Station.ActivateServerRpc(Station.State.Value.Target);
        if(action=="stale")Station.ActivateServerRpc(ulong.MaxValue);
        if(action=="cycle")Station.CycleServerRpc();
        if(action=="radar")Call(FindFirstObjectByType<RadarSystem>(),"RequestRadarScanServerRpc",default(ServerRpcParams));
        if(action=="defense")Call(FindFirstObjectByType<DefenseSystem>(),"RequestActivateDefenseServerRpc",default(ServerRpcParams));
    }
    IEnumerator VerifyAll(string label,bool active,int plasma)
    {
        ready.Clear();verifyId++;VerifyClientRpc(verifyId,label,active,plasma,Station.State.Value.Target);
        yield return Wait(()=>ready.Count==NetworkManager.ConnectedClientsIds.Count,6f,"all peers: "+label);
    }
    [ClientRpc] void VerifyClientRpc(int id,string label,bool active,int plasma,ulong selected){StartCoroutine(Verify(id,label,active,plasma,selected));}
    IEnumerator Verify(int id,string label,bool active,int plasma,ulong selected)
    {
        yield return new WaitForSecondsRealtime(0.25f);
        Check(Station.State.Value.Active==active,"station state: "+label); Check(Drill.Current.Plasma==plasma,"shared plasma: "+label);
        Check(Station.State.Value.Target==selected,"selected teammate: "+label);
        if (label == "radar and defense on all four peers")
        {
            Check(FindFirstObjectByType<RadarSystem>().IsOnCooldownNetwork.Value, "fourth receives radar state");
            Check(FindFirstObjectByType<DefenseSystem>().IsOnCooldownNetwork.Value, "fourth receives defense state");
        }
        Check(Station.display.text.Contains(active?"REVIVING":"REVIVE"),"wall display: "+label);
        Check(NetworkManager.LocalClient.PlayerObject!=null,"local player exists: "+label);
        if(!active && NetworkManager.LocalClient.PlayerObject!=null && !NetworkManager.LocalClient.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value)
        {
            var player=NetworkManager.LocalClient.PlayerObject;
            Check(AudioListener.volume>0.9f&&!AudioListener.pause,"sound restored: "+label);
            Check(!NetworkManager.GetComponent<VoiceChatManager>().ReviveMuted,"voice restored: "+label);
            Check(player.GetComponent<PlayerController>().enabled&&player.GetComponent<CharacterController>().enabled,"fresh player controls: "+label);
            Check(FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(a=>a.enabled)==1,"one local audio listener: "+label);
        }
        ReadyServerRpc(id);
    }
    [ServerRpc(RequireOwnership=false)] void ReadyServerRpc(int id,ServerRpcParams rpc=default){if(id==verifyId)ready.Add(rpc.Receive.SenderClientId);}
    [ClientRpc] void DisconnectClientRpc(ClientRpcParams rpc=default){StartCoroutine(Disconnect());}
    IEnumerator Disconnect()
    {
        NetworkManager.Shutdown();yield return new WaitForSecondsRealtime(0.3f);
        Check(AudioListener.volume>0.9f&&!AudioListener.pause,"disconnect releases local revive silence");
        File.WriteAllText("Logs/Revive-"+run+"-"+role+"-PASS.txt",assertions+" assertions; muted frames="+mutedFrames);Application.Quit(0);
    }
    [ClientRpc] void FinishClientRpc()
    {
        if(IsServer)return;
        Check(mutedFrames>100 || NetworkManager.LocalClientId != ulong.Parse(File.ReadAllText("Logs/Revive-"+run+"-remote.txt")),"target experienced sustained silent countdown");
        File.WriteAllText("Logs/Revive-"+run+"-"+role+"-PASS.txt",assertions+" assertions; muted frames="+mutedFrames);Application.Quit(0);
    }
    static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);
    IEnumerator Wait(Func<bool> condition,float seconds,string label)
    {
        float deadline=Time.realtimeSinceStartup+seconds;while(!condition()&&Time.realtimeSinceStartup<deadline)yield return null;Check(condition(),label);
    }
    void Check(bool value,string label){if(!value)Fail(label);assertions++;Debug.Log("[REVIVE TEST] PASS "+role+" "+label);}
    void Fail(string label){File.WriteAllText("Logs/Revive-"+run+"-"+role+"-FAIL.txt",label);Debug.LogError("[REVIVE TEST] FAIL "+role+" "+label);Application.Quit(1);throw new Exception(label);}
}

[DefaultExecutionOrder(-20000)]
public sealed class ReviveTestInputFreeze : MonoBehaviour
{
    ulong frozen=ulong.MaxValue;
    void Update()
    {
        var player=Unity.Netcode.NetworkManager.Singleton?.LocalClient?.PlayerObject;
        if(player==null||!player.IsSpawned||player.NetworkObjectId==frozen)return;
        frozen=player.NetworkObjectId;
        var control=player.GetComponent<PlayerController>();
        control.walkSpeed=control.sprintSpeed=control.gravity=0f;
        typeof(PlayerController).GetField("velocity",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(control,Vector3.zero);
    }
}
#endif
