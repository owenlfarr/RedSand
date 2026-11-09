using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject),typeof(CharacterController))]
public sealed class BlindLungeMonster : NetworkBehaviour
{
    public static BlindLungeMonster Instance {get;private set;}
    public GameObject visual;
    public float hearingRange=32f, lungeDistance=10f, lungeSpeed=32f, windupSeconds=.35f, recoverySeconds=1.25f;
    public enum Phase {Listening,Windup,Lunging,Recovering}
    public NetworkVariable<Vector3> Position=new NetworkVariable<Vector3>();
    public NetworkVariable<float> Yaw=new NetworkVariable<float>();
    public NetworkVariable<int> State=new NetworkVariable<int>();
    CharacterController motor;
    AudioSource heartbeat;
    AudioClip beat,impact;
    Vector3 heardPosition,lungeDirection;
    float memoryUntil,phaseUntil,remaining,verticalSpeed,priorityUntil;
    Phase phase;
    bool heard,previouslyNetworked,offlineReady;
    readonly Dictionary<ulong,Vector3> lastPositions=new Dictionary<ulong,Vector3>();
    readonly Dictionary<ulong,float> footsteps=new Dictionary<ulong,float>();
    readonly Dictionary<ulong,bool> grounded=new Dictionary<ulong,bool>();
    public const float SearchDuration=1f, SearchCooldown=2.5f;
    public static bool SearchSucceeds(float roll)=>roll>=0&&roll<.1f;
    readonly Dictionary<ulong,float> nextSearch=new Dictionary<ulong,float>();
    readonly Dictionary<ulong,float> nextVoice=new Dictionary<ulong,float>();
    readonly List<Flight> flights=new List<Flight>();
    readonly Dictionary<int,DistractionRockVisual> rockVisuals=new Dictionary<int,DistractionRockVisual>();
    int nextRockId;
    bool Online=>NetworkManager.Singleton!=null&&NetworkManager.Singleton.IsListening;
    bool Authority=>!Online||IsServer;
    double Now=>Online?NetworkManager.Singleton.ServerTime.Time:Time.timeAsDouble;
    public Phase CurrentPhase=>Online&&IsSpawned?(Phase)State.Value:phase;
    public int LocalRockLaunches {get;private set;}
    public int LocalRockImpacts {get;private set;}
    public int LocalDeathPresentations {get;private set;}
    public int LocalSearchReplies {get;private set;}
    public int LastLocalSearchResult {get;private set;}
    public int RejectedSearchRequests {get;private set;}
    public bool HasHeardNoise=>heard;
    public Vector3 LastHeardPosition=>heardPosition;
    sealed class Flight {public int id;public ulong owner;public Vector3 origin,velocity,previous;public double start;}
    void Awake(){
        Instance=this;motor=GetComponent<CharacterController>();motor.enabled=false;
        var audio=new GameObject("Faint heartbeat");audio.transform.SetParent(transform,false);audio.transform.localPosition=Vector3.up;
        heartbeat=audio.AddComponent<AudioSource>();heartbeat.playOnAwake=false;heartbeat.loop=true;heartbeat.spatialBlend=1;
        heartbeat.volume=.11f;heartbeat.minDistance=2;heartbeat.maxDistance=26;heartbeat.dopplerLevel=0;heartbeat.rolloffMode=AudioRolloffMode.Linear;
        const int rate=22050;var samples=new float[rate];
        for(int i=0;i<samples.Length;i++){float t=(float)i/rate;float a=t<.16f?Mathf.Sin(2*Mathf.PI*65*t)*Mathf.Exp(-t*26):0;float b=t>=.22f&&t<.4f?Mathf.Sin(2*Mathf.PI*58*(t-.22f))*Mathf.Exp(-(t-.22f)*28)*.65f:0;samples[i]=(a+b)*.7f;}
        beat=AudioClip.Create("Blind lunge heartbeat",samples.Length,1,rate,false);beat.SetData(samples,0);heartbeat.clip=beat;
        var data=new float[rate/8];var random=new System.Random(721);for(int i=0;i<data.Length;i++)data[i]=(float)(random.NextDouble()*2-1)*Mathf.Exp(-i/(rate*.025f))*.6f;
        impact=AudioClip.Create("Small rock impact",data.Length,1,rate,false);impact.SetData(data,0);
    }
    public override void OnNetworkSpawn(){
        previouslyNetworked=true;motor.enabled=IsServer;
        if(IsServer){SnapToGround();Position.Value=transform.position;Yaw.Value=transform.eulerAngles.y;
            NetworkManager.SceneManager.OnSynchronizeComplete+=SynchronizeRocks;
            NetworkManager.OnClientDisconnectCallback+=ForgetPlayer;
        }else transform.SetPositionAndRotation(Position.Value,Quaternion.Euler(0,Yaw.Value,0));
        heartbeat.time=(float)(NetworkManager.ServerTime.Time%beat.length);heartbeat.Play();
    }
    void SynchronizeRocks(ulong id){
        if(!IsServer||id==NetworkManager.LocalClientId)return;
        var target=new ClientRpcParams{Send=new ClientRpcSendParams{TargetClientIds=new[]{id}}};
        foreach(var flight in flights)SpawnRockClientRpc(flight.id,flight.origin,flight.velocity,flight.start,target);
    }
    void ForgetPlayer(ulong id){lastPositions.Remove(id);footsteps.Remove(id);grounded.Remove(id);nextSearch.Remove(id);nextVoice.Remove(id);}
    public override void OnNetworkDespawn(){
        if(NetworkManager!=null){if(NetworkManager.SceneManager!=null)NetworkManager.SceneManager.OnSynchronizeComplete-=SynchronizeRocks;NetworkManager.OnClientDisconnectCallback-=ForgetPlayer;}
        motor.enabled=false;heartbeat.Stop();flights.Clear();
        foreach(var v in rockVisuals.Values)if(v!=null)Destroy(v.gameObject);rockVisuals.Clear();
    }
    void Update(){
        if(Online){if(!IsSpawned)return;if(!IsServer){phase=(Phase)State.Value;transform.SetPositionAndRotation(Vector3.Lerp(transform.position,Position.Value,1-Mathf.Exp(-25*Time.deltaTime)),Quaternion.Euler(0,Yaw.Value,0));return;}}
        else {if(previouslyNetworked)return;if(!offlineReady){motor.enabled=true;SnapToGround();heartbeat.Play();offlineReady=true;}}
        ObserveMovement();TickFlights();TickMovement();
        if(Online){Position.Value=transform.position;Yaw.Value=transform.eulerAngles.y;State.Value=(int)phase;}
    }
    public bool HearNoise(Vector3 point,float audibleRadius,bool distraction=false){
        if(!Authority||!Finite(point)||Time.time<priorityUntil&&!distraction)return false;
        float radius=Mathf.Min(hearingRange,Mathf.Max(0,audibleRadius));
        // Solid structures muffle noise, but sight never determines whether a player is hunted.
        Vector3 ray=point+Vector3.up*.6f-(transform.position+Vector3.up);
        foreach(var hit in Physics.RaycastAll(transform.position+Vector3.up,ray.normalized,ray.magnitude,~0,QueryTriggerInteraction.Ignore)){
            if(hit.transform.IsChildOf(transform)||hit.collider.GetComponentInParent<PlayerController>()!=null)continue;
            radius*=.25f;break;
        }
        if((point-transform.position).sqrMagnitude>radius*radius)return false;
        heard=true;heardPosition=point;memoryUntil=Time.time+8;
        if(distraction)priorityUntil=Time.time+1.2f;
        return true;
    }
    public static Vector3 LungeEndpoint(Vector3 origin,Vector3 noise,float maximum){Vector3 delta=Vector3.ProjectOnPlane(noise-origin,Vector3.up);return origin+Vector3.ClampMagnitude(delta,maximum);}
    void TickMovement(){
        if(phase==Phase.Listening&&heard&&Time.time<=memoryUntil){phase=Phase.Windup;phaseUntil=Time.time+windupSeconds;}
        if(phase==Phase.Windup&&Time.time>=phaseUntil){
            Vector3 end=LungeEndpoint(transform.position,heardPosition,lungeDistance);Vector3 delta=end-transform.position;
            remaining=delta.magnitude;lungeDirection=delta.normalized;
            if(remaining>.2f){transform.rotation=Quaternion.LookRotation(lungeDirection);phase=Phase.Lunging;}else{heard=false;phase=Phase.Recovering;phaseUntil=Time.time+recoverySeconds;}
        }
        verticalSpeed=motor.isGrounded?-2:Mathf.Max(-45,verticalSpeed-25*Time.deltaTime);
        Vector3 previous=transform.position;float travel=phase==Phase.Lunging?Mathf.Min(remaining,lungeSpeed*Time.deltaTime):0;
        motor.Move((lungeDirection*travel)+Vector3.up*verticalSpeed*Time.deltaTime);
        if(phase==Phase.Lunging){
            float moved=Vector3.ProjectOnPlane(transform.position-previous,Vector3.up).magnitude;remaining-=moved;
            if(remaining<=.03f||travel>.01f&&moved<travel*.15f){phase=Phase.Recovering;phaseUntil=Time.time+recoverySeconds;}
        }
        if(phase==Phase.Recovering&&Time.time>=phaseUntil){
            if(Time.time>memoryUntil||Vector3.ProjectOnPlane(heardPosition-transform.position,Vector3.up).magnitude<1)heard=false;
            phase=Phase.Listening;
        }
        if(Time.time>memoryUntil&&phase==Phase.Listening)heard=false;
    }
    void SnapToGround(){
        foreach(var t in Terrain.activeTerrains){var start=t.transform.position;var size=t.terrainData.size;Vector3 pos=transform.position;if(pos.x<start.x||pos.x>start.x+size.x||pos.z<start.z||pos.z>start.z+size.z)continue;
            bool enabled=motor.enabled;motor.enabled=false;pos.y=t.SampleHeight(pos)+start.y+.05f;transform.position=pos;motor.enabled=enabled;break;}
    }
    void ObserveMovement(){
        if(Online){foreach(var client in NetworkManager.ConnectedClientsList){var obj=client.PlayerObject;if(!Living(obj))continue;ObservePlayer(client.ClientId,obj.GetComponent<PlayerController>());}}
        else {var p=FindFirstObjectByType<PlayerController>();if(p!=null&&(p.GetComponent<OxygenSystem>()==null||!p.GetComponent<OxygenSystem>().IsDead()))ObservePlayer(0,p);}
    }
    void ObservePlayer(ulong id,PlayerController player){
        if(player==null)return;Vector3 pos=player.transform.position;
        if(lastPositions.TryGetValue(id,out var previous)){
            float move=Vector3.ProjectOnPlane(pos-previous,Vector3.up).magnitude;
            footsteps.TryGetValue(id,out float distance);distance+=move;
            if(player.GetNetworkGrounded()&&distance>=(player.IsSprinting()?2.4f:1.7f)&&move<5){HearNoise(pos,player.IsSprinting()?32:12);distance=0;}
            footsteps[id]=distance;
        }
        bool onGround=player.GetNetworkGrounded();
        if(grounded.TryGetValue(id,out bool wasGrounded)&&!wasGrounded&&onGround)HearNoise(pos,26);
        grounded[id]=onGround;
        lastPositions[id]=pos;
    }
    void OnControllerColliderHit(ControllerColliderHit hit){
        if(!Authority||phase!=Phase.Lunging)return;
        var player=hit.collider.GetComponentInParent<PlayerController>();if(player==null)return;
        if(Online){var obj=player.GetComponent<NetworkObject>();if(!Living(obj))return;var life=player.GetComponent<NetworkOxygenState>();life.UseBlackDeathIntro.Value=false;life.IsDead.Value=true;KillOwnerClientRpc(new ClientRpcParams{Send=new ClientRpcSendParams{TargetClientIds=new[]{obj.OwnerClientId}}});}
        else player.GetComponent<OxygenSystem>()?.MarkDeadFromMonster();
    }
    [ClientRpc]void KillOwnerClientRpc(ClientRpcParams rpc=default){LocalDeathPresentations++;NetworkManager.LocalClient?.PlayerObject?.GetComponent<OxygenSystem>()?.MarkDeadFromMonster();}
    bool Living(NetworkObject obj)=>obj!=null&&obj.IsSpawned&&obj.TryGetComponent<NetworkOxygenState>(out var life)&&!life.IsDead.Value;
    bool TryPlayer(ulong id,out PlayerController player){player=null;if(!Online)return false;if(!NetworkManager.ConnectedClients.TryGetValue(id,out var c)||!Living(c.PlayerObject))return false;player=c.PlayerObject.GetComponent<PlayerController>();return player!=null;}
    public static bool Finite(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)&&!float.IsInfinity(v.x)&&!float.IsInfinity(v.y)&&!float.IsInfinity(v.z);
    public static bool GroundInReach(PlayerController player,Vector3 direction){
        if(player==null||!Finite(direction)||direction.sqrMagnitude<.5f||direction.normalized.y>-.2f)return false;
        var body=player.GetComponent<CharacterController>();
        Vector3 feet=body!=null?player.transform.TransformPoint(body.center-Vector3.up*body.height*.5f):player.transform.position;
        Vector3 eye=player.playerCamera!=null?player.playerCamera.position:player.transform.position+Vector3.up*1.5f;
        RaycastHit? nearest=null;
        foreach(var hit in Physics.RaycastAll(eye,direction.normalized,Mathf.Clamp(Vector3.Distance(eye,feet)+3,3,15),~0,QueryTriggerInteraction.Ignore)){
            if(hit.transform.IsChildOf(player.transform))continue;if(!nearest.HasValue||hit.distance<nearest.Value.distance)nearest=hit;
        }
        return nearest.HasValue&&nearest.Value.normal.y>.45f&&Vector3.Distance(nearest.Value.point,feet)<=3&&nearest.Value.collider.GetComponentInParent<PlayerController>()==null;
    }
    public void RequestSearch(PlayerController player,Vector3 aim){if(Online)SearchRockServerRpc(aim);else BeginSearch(0,player,aim);}
    [ServerRpc(RequireOwnership=false)]void SearchRockServerRpc(Vector3 aim,ServerRpcParams rpc=default){if(TryPlayer(rpc.Receive.SenderClientId,out var p))BeginSearch(rpc.Receive.SenderClientId,p,aim);}
    int Count(PlayerController p){var state=p.GetComponent<NetworkPlayerMovementState>();return Online&&state!=null?state.CarriedDistractionRocks.Value:p.GetComponent<RockThrower>().OfflineCount;}
    void SetCount(PlayerController p,int value){var state=p.GetComponent<NetworkPlayerMovementState>();if(Online&&state!=null)state.CarriedDistractionRocks.Value=value;else p.GetComponent<RockThrower>().OfflineCount=value;}
    void BeginSearch(ulong id,PlayerController player,Vector3 aim){
        if(!Authority)return;
        if(nextSearch.TryGetValue(id,out float next)&&Time.time<next){RejectedSearchRequests++;SearchReply(id,player,3);return;}
        if(Count(player)>0){SearchReply(id,player,4);return;}
        if(!GroundInReach(player,aim)){SearchReply(id,player,2);return;}
        nextSearch[id]=Time.time+SearchCooldown;StartCoroutine(Search(id,player));
    }
    IEnumerator Search(ulong id,PlayerController player){
        yield return new WaitForSeconds(SearchDuration);
        if(player==null||Online&&!TryPlayer(id,out player))yield break;
        var life=player.GetComponent<OxygenSystem>();if(life!=null&&life.IsDead())yield break;
        bool found=SearchSucceeds(Random.value);if(found)SetCount(player,1);SearchReply(id,player,found?1:0);
    }
    void SearchReply(ulong id,PlayerController player,int result){if(Online)SearchReplyClientRpc(result,new ClientRpcParams{Send=new ClientRpcSendParams{TargetClientIds=new[]{id}}});else player.GetComponent<RockThrower>()?.ShowResult(result);}
    [ClientRpc]void SearchReplyClientRpc(int result,ClientRpcParams rpc=default){LocalSearchReplies++;LastLocalSearchResult=result;NetworkManager.LocalClient?.PlayerObject?.GetComponent<RockThrower>()?.ShowResult(result);}
    public void RequestThrow(PlayerController player,Vector3 aim){if(Online)ThrowRockServerRpc(aim);else Throw(0,player,aim);}
    [ServerRpc(RequireOwnership=false)]void ThrowRockServerRpc(Vector3 aim,ServerRpcParams rpc=default){if(TryPlayer(rpc.Receive.SenderClientId,out var p))Throw(rpc.Receive.SenderClientId,p,aim);}
    public static Vector3 ThrowVelocity(Vector3 aim){aim=aim.normalized;aim.y=Mathf.Max(.22f,aim.y+.25f);return aim.normalized*26;}
    public static Vector3 RockPosition(Vector3 origin,Vector3 velocity,float age)=>origin+velocity*age+Vector3.down*(9*age*age);
    void Throw(ulong id,PlayerController player,Vector3 aim){
        if(!Authority||!Finite(aim)||aim.sqrMagnitude<.5f||Count(player)<1)return;
        SetCount(player,0);Vector3 origin=player.playerCamera!=null?player.playerCamera.position:player.transform.position+Vector3.up*1.5f;Vector3 velocity=ThrowVelocity(aim);int rock=++nextRockId;double start=Now;
        flights.Add(new Flight{id=rock,owner=id,origin=origin,velocity=velocity,previous=origin,start=start});
        if(Online)SpawnRockClientRpc(rock,origin,velocity,start);else SpawnRock(rock,origin,velocity,start);
    }
    [ClientRpc]void SpawnRockClientRpc(int id,Vector3 origin,Vector3 velocity,double start,ClientRpcParams rpc=default)=>SpawnRock(id,origin,velocity,start);
    void SpawnRock(int id,Vector3 origin,Vector3 velocity,double start){if(rockVisuals.ContainsKey(id))return;LocalRockLaunches++;var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name="Thrown small rock";Destroy(go.GetComponent<Collider>());go.transform.localScale=new Vector3(.16f,.12f,.14f);var v=go.AddComponent<DistractionRockVisual>();v.Initialize(origin,velocity,start,Online);rockVisuals[id]=v;}
    void TickFlights(){
        for(int i=flights.Count-1;i>=0;i--){var flight=flights[i];float age=(float)(Now-flight.start);Vector3 point=RockPosition(flight.origin,flight.velocity,age),delta=point-flight.previous;RaycastHit? nearest=null;
            foreach(var hit in Physics.SphereCastAll(flight.previous,.08f,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore)){
                var p=hit.collider.GetComponentInParent<PlayerController>();if(p!=null&&(!Online||p.OwnerClientId==flight.owner))continue;
                if(!nearest.HasValue||hit.distance<nearest.Value.distance)nearest=hit;
            }
            if(nearest.HasValue||age>6){Vector3 landing=nearest.HasValue?nearest.Value.point:point;
                if(nearest.HasValue){HearNoise(landing,40,true);if(Online)LandRockClientRpc(flight.id,landing);else LandRock(flight.id,landing);}else{if(Online)RemoveRockClientRpc(flight.id);else RemoveRock(flight.id);}
                flights.RemoveAt(i);
            }else flight.previous=point;
        }
    }
    [ClientRpc]void LandRockClientRpc(int id,Vector3 point)=>LandRock(id,point);
    [ClientRpc]void RemoveRockClientRpc(int id)=>RemoveRock(id);
    void RemoveRock(int id){if(rockVisuals.TryGetValue(id,out var v)&&v!=null)Destroy(v.gameObject);rockVisuals.Remove(id);}
    void LandRock(int id,Vector3 point){LocalRockImpacts++;if(rockVisuals.TryGetValue(id,out var v)&&v!=null)v.Land(point);rockVisuals.Remove(id);AudioSource.PlayClipAtPoint(impact,point,.5f);}
    public void ReportLocalSpeech(){if(Online&&!IsSpawned)return;if(Online)SpeechServerRpc();else{var p=FindFirstObjectByType<PlayerController>();if(p!=null)HearNoise(p.transform.position,26);}}
    [ServerRpc(RequireOwnership=false)]void SpeechServerRpc(ServerRpcParams rpc=default){ulong id=rpc.Receive.SenderClientId;if(nextVoice.TryGetValue(id,out float next)&&Time.time<next)return;nextVoice[id]=Time.time+.5f;if(TryPlayer(id,out var p))HearNoise(p.transform.position,26);}
    void OnDisable(){if(heartbeat!=null)heartbeat.Stop();}
    public override void OnDestroy(){if(Instance==this)Instance=null;foreach(var v in rockVisuals.Values)if(v!=null)Destroy(v.gameObject);if(beat!=null)Destroy(beat);if(impact!=null)Destroy(impact);base.OnDestroy();}
    void OnDrawGizmosSelected(){Gizmos.color=Color.yellow;Gizmos.DrawWireSphere(transform.position,hearingRange);Gizmos.color=Color.red;Gizmos.DrawLine(transform.position,transform.position+transform.forward*lungeDistance);}
}

public sealed class DistractionRockVisual:MonoBehaviour
{
    Vector3 origin,velocity;double start;bool online,landed;
    public void Initialize(Vector3 from,Vector3 speed,double time,bool network){origin=from;velocity=speed;start=time;online=network;transform.position=from;Destroy(gameObject,10);}
    void Update(){if(landed)return;double now=online&&NetworkManager.Singleton!=null?NetworkManager.Singleton.ServerTime.Time:Time.timeAsDouble;transform.position=BlindLungeMonster.RockPosition(origin,velocity,(float)(now-start));transform.Rotate(new Vector3(80,140,60)*Time.deltaTime);}
    public void Land(Vector3 point){landed=true;transform.position=point+Vector3.up*.08f;Destroy(gameObject,5);}
}
