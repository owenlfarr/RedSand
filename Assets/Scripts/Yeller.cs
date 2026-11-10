using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public struct YellerState : INetworkSerializable, System.IEquatable<YellerState>
{
    public byte Phase; // 0 rock, 1 attached/screaming, 2 spent
    public ulong Player;
    public double EndsAt;
    public Vector3 Position;
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T:IReaderWriter { serializer.SerializeValue(ref Phase);serializer.SerializeValue(ref Player);serializer.SerializeValue(ref EndsAt);serializer.SerializeValue(ref Position); }
    public bool Equals(YellerState other)=>Phase==other.Phase&&Player==other.Player&&EndsAt==other.EndsAt&&Position==other.Position;
}

// One use per bug per night. No client-supplied attachment/position/duration RPCs.
[RequireComponent(typeof(NetworkObject))]
public sealed class Yeller : NetworkBehaviour
{
    public const float AttachRange=1.524f, ScreamSeconds=15;
    public GameObject visual;
    public AudioClip scream;
    public NetworkVariable<YellerState> Snapshot=new NetworkVariable<YellerState>();
    public YellerState Current=>Online&&IsSpawned?Snapshot.Value:offline;
    public bool IsScreaming=>Current.Phase==1&&Now<Current.EndsAt;
    public AudioSource ScreamSource=>source;
    public int LocalScreamStarts {get;private set;}
    public int LocalDeaths {get;private set;}
    static readonly List<Yeller> bugs=new List<Yeller>();
    AudioSource source;AudioClip generated;YellerState offline;
    bool previouslyNetworked, initialized;float nextScan;
    bool Online=>NetworkManager.Singleton!=null&&NetworkManager.Singleton.IsListening;
    double Now=>Online?NetworkManager.Singleton.ServerTime.Time:Time.realtimeSinceStartupAsDouble;
    void Awake(){
        bugs.Add(this);source=gameObject.AddComponent<AudioSource>();source.playOnAwake=false;source.loop=true;source.spatialBlend=1;source.dopplerLevel=0;source.minDistance=3;source.maxDistance=100;source.volume=.48f;source.rolloffMode=AudioRolloffMode.Linear;
        if(scream==null){const int rate=22050;var data=new float[rate];var random=new System.Random(5271);for(int i=0;i<rate;i++){float t=(float)i/rate;data[i]=(.3f*Mathf.Sin(2*Mathf.PI*(650*t+.5f*Mathf.Sin(2*Mathf.PI*9*t)))+.15f*Mathf.Sin(2*Mathf.PI*1300*t)+.16f*((float)random.NextDouble()*2-1))*(.7f+.3f*Mathf.Sin(t*2*Mathf.PI*5));}generated=AudioClip.Create("Yeller insect scream",rate,1,rate,false);generated.SetData(data,0);scream=generated;}source.clip=scream;
    }
    public override void OnNetworkSpawn(){previouslyNetworked=true;if(IsServer)Snapshot.Value=new YellerState{Player=ulong.MaxValue,Position=transform.position};Present();}
    public override void OnNetworkDespawn(){source.Stop();}
    void Update(){
        if(Online){if(!IsSpawned)return;if(IsServer)Tick();}
        else {if(previouslyNetworked)return;if(!initialized){offline=new YellerState{Player=ulong.MaxValue,Position=transform.position};initialized=true;}Tick();}
        Present();
    }
    void LateUpdate(){if((Online&&!IsSpawned)||(!Online&&previouslyNetworked))return;var state=Current;if(state.Phase==1&&TryPlayer(state.Player,out var player))transform.position=AttachmentPoint(player);else if(state.Phase==0)transform.position=state.Position;}
    static Vector3 AttachmentPoint(PlayerController player)=>player.transform.TransformPoint(new Vector3(.36f,1.2f,0));
    void Set(YellerState state){if(Online){if(IsServer&&IsSpawned)Snapshot.Value=state;}else offline=state;}
    void Tick(){
        var state=Current;
        if(state.Phase==1){
            if(Now>=state.EndsAt||!TryPlayer(state.Player,out var attached)){state.Phase=2;Set(state);return;}
            state.Position=AttachmentPoint(attached);Set(state);return;
        }
        if(state.Phase!=0||NightTransition.BlocksGameplay||Time.unscaledTime<nextScan)return;
        nextScan=Time.unscaledTime+.05f;
        PlayerController closest=null;float best=AttachRange;
        if(Online){foreach(var client in NetworkManager.ConnectedClientsList)Consider(client.PlayerObject!=null?client.PlayerObject.GetComponent<PlayerController>():null,ref closest,ref best);}
        else foreach(var player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))Consider(player,ref closest,ref best);
        if(closest==null)return;
        state.Phase=1;state.Player=Online?closest.OwnerClientId:0;state.EndsAt=Now+ScreamSeconds;state.Position=AttachmentPoint(closest);Set(state);
    }
    void Consider(PlayerController player,ref PlayerController closest,ref float best){
        if(!Living(player)||InBunker(player))return;
        foreach(var other in bugs)if(other!=null&&other!=this&&other.gameObject.scene==gameObject.scene&&other.IsScreaming&&other.TryPlayer(other.Current.Player,out var victim)&&victim==player)return;
        float distance=Vector3.Distance(transform.position,player.transform.position);
        if(distance>best||!ClearSight(transform.position,player.transform.position+Vector3.up,transform,player.transform))return;
        closest=player;best=distance;
    }
    bool TryPlayer(ulong id,out PlayerController player){player=null;if(Online){if(IsServer){if(!NetworkManager.ConnectedClients.TryGetValue(id,out var client)||client.PlayerObject==null)return false;player=client.PlayerObject.GetComponent<PlayerController>();}else{foreach(var obj in NetworkManager.SpawnManager.SpawnedObjectsList)if(obj!=null&&obj.IsPlayerObject&&obj.OwnerClientId==id){player=obj.GetComponent<PlayerController>();break;}}}else player=FindFirstObjectByType<PlayerController>();return Living(player);}
    bool Living(PlayerController player){if(player==null)return false;if(Online){var life=player.GetComponent<NetworkOxygenState>();return player.IsSpawned&&life!=null&&life.IsSpawned&&!life.IsDead.Value&&(!IsServer||(NetworkManager.ConnectedClients.TryGetValue(player.OwnerClientId,out var client)&&client.PlayerObject==player.NetworkObject));}var oxygen=player.GetComponent<OxygenSystem>();return player.isActiveAndEnabled&&(oxygen==null||!oxygen.IsDead());}
    static bool InBunker(PlayerController player){var life=player.GetComponent<NetworkOxygenState>();if(life!=null&&life.IsSpawned&&life.IsInBunker.Value)return true;var oxygen=player.GetComponent<OxygenSystem>();return oxygen!=null&&oxygen.IsInsideBunkerZone();}
    public static bool TryAttract(Component monster,out PlayerController player,out Yeller caller){
        player=null;caller=null;float nearest=float.PositiveInfinity;
        foreach(var bug in bugs){if(bug==null||!bug.isActiveAndEnabled||bug.gameObject.scene!=monster.gameObject.scene||bug.Online&&(!bug.IsSpawned||!bug.IsServer)||!bug.IsScreaming||!bug.TryPlayer(bug.Current.Player,out var victim)||InBunker(victim))continue;float distance=(victim.transform.position-monster.transform.position).sqrMagnitude;if(distance<nearest){nearest=distance;player=victim;caller=bug;}}
        return player!=null;
    }
    public static bool ClearSight(Vector3 from,Vector3 to,Transform monster,Transform player){var delta=to-from;foreach(var hit in Physics.RaycastAll(from,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore)){if(hit.transform.IsChildOf(monster)||hit.transform.IsChildOf(player)||hit.collider.GetComponentInParent<PlayerController>()!=null)continue;return false;}return true;}
    public void ResolveContact(Component monster,PlayerController player){
        if(!IsScreaming||!Living(player)||InBunker(player)||(Online&&(!IsSpawned||!IsServer)))return;
        if(Vector3.Distance(monster.transform.position,player.transform.position)>1.25f||!ClearSight(monster.transform.position+Vector3.up,player.transform.position+Vector3.up,monster.transform,player.transform))return;
        if(monster is PowerStationWatcher watcher){watcher.KillFromYeller(player);return;}
        if(Online){var life=player.GetComponent<NetworkOxygenState>();life.UseBlackDeathIntro.Value=false;life.IsDead.Value=true;DeathClientRpc(new ClientRpcParams{Send=new ClientRpcSendParams{TargetClientIds=new[]{player.OwnerClientId}}});}
        else player.GetComponent<OxygenSystem>()?.MarkDeadFromMonster();
    }
    [ClientRpc]void DeathClientRpc(ClientRpcParams rpc=default){LocalDeaths++;NetworkManager.LocalClient?.PlayerObject?.GetComponent<OxygenSystem>()?.MarkDeadFromMonster();}
    void Present(){var state=Current;bool active=state.Phase==1&&Now<state.EndsAt;if(visual!=null)visual.SetActive(state.Phase!=2);if(active&&!source.isPlaying){LocalScreamStarts++;source.time=(float)(System.Math.Max(0,Now-(state.EndsAt-ScreamSeconds))%scream.length);source.Play();}else if(!active&&source.isPlaying)source.Stop();}
    void OnDisable(){if(source!=null)source.Stop();}
    public override void OnDestroy(){bugs.Remove(this);if(generated!=null)Destroy(generated);base.OnDestroy();}
}

// Per-monster state; never adds NetworkBehaviours or changes prefab component ordering.
public sealed class YellerPursuit
{
    public bool Released {get;private set;}
    public PlayerController LastTarget {get;private set;}
    public bool WasAttracted {get;private set;}
    float gravity;readonly TerrainRockRoute route=new TerrainRockRoute();
    public bool Tick(Component monster,CharacterController motor,float speed,out PlayerController target){
        bool was=WasAttracted;bool active=Yeller.TryAttract(monster,out target,out var bug);Released=was&&!active;WasAttracted=active;
        if(!active)return false;LastTarget=target;
        Vector3 delta=Vector3.ProjectOnPlane(target.transform.position-monster.transform.position,Vector3.up);Vector3 direction=delta.normalized;
        if(motor!=null){if(!motor.enabled)motor.enabled=true;var surface=TerrainRockNavigation.ForScene(monster.gameObject.scene);if(surface!=null&&surface.Ready)direction=route.Direction(motor,target.transform.position,direction);gravity=motor.isGrounded?-2:Mathf.Max(-40,gravity-25*Time.deltaTime);motor.Move(direction*Mathf.Min(delta.magnitude,speed*Time.deltaTime)+Vector3.up*gravity*Time.deltaTime);}
        else {Vector3 position=monster.transform.position+direction*Mathf.Min(delta.magnitude,speed*Time.deltaTime);foreach(var terrain in Terrain.activeTerrains){var o=terrain.transform.position;var size=terrain.terrainData.size;if(terrain.gameObject.scene==monster.gameObject.scene&&position.x>=o.x&&position.x<=o.x+size.x&&position.z>=o.z&&position.z<=o.z+size.z){position.y=terrain.SampleHeight(position)+o.y+.05f;break;}}monster.transform.position=position;}
        if(direction.sqrMagnitude>.01f)monster.transform.rotation=Quaternion.LookRotation(direction);
        bug.ResolveContact(monster,target);return true;
    }
    public static bool Sees(Component monster,PlayerController player,float range)=>player!=null&&Vector3.Distance(monster.transform.position,player.transform.position)<=range&&Yeller.ClearSight(monster.transform.position+Vector3.up,player.transform.position+Vector3.up,monster.transform,player.transform);
}
