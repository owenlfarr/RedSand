using UnityEngine;
using Unity.Netcode;

// Each peer presents sounds from the same replicated movement/jump snapshot.
// Distance-based steps follow actual displacement, so walls and idle input stay silent.
[RequireComponent(typeof(PlayerController))]
[DefaultExecutionOrder(150)]
public sealed class PlayerSurfaceAudio : MonoBehaviour
{
    public const float WalkStepDistance=1.7f,RunStepDistance=2.4f;
    PlayerController player;NetworkPlayerMovementState movement;OxygenSystem oxygen;
    AudioSource bunkerSource,surfaceSource;
    static AudioClip[] bunkerSteps,surfaceSteps;static AudioClip bunkerJump,surfaceJump,bunkerLand,surfaceLand;
    Vector3 previous;float distance,nextStep;uint sequence;double landed;
    bool initialized,wasSuppressed=true;
    public int StepCount {get;private set;}
    public int JumpCount {get;private set;}
    public int LandingCount {get;private set;}
    public bool LastWasBunker {get;private set;}
    public AudioSource BunkerSource=>bunkerSource;
    public AudioSource SurfaceSource=>surfaceSource;
    void Awake(){
        player=GetComponent<PlayerController>();movement=GetComponent<NetworkPlayerMovementState>();oxygen=GetComponent<OxygenSystem>();
        LoadClips();bunkerSource=CreateSource("Bunker boot sounds",true);surfaceSource=CreateSource("Surface boot sounds",false);
    }
    static void LoadClips(){
        if(bunkerSteps!=null)return;
        bunkerSteps=new AudioClip[4];surfaceSteps=new AudioClip[4];
        for(int i=0;i<4;i++){bunkerSteps[i]=Resources.Load<AudioClip>("PlayerMovement/Bunker/Step"+(i+1));surfaceSteps[i]=Resources.Load<AudioClip>("PlayerMovement/Surface/Step"+(i+1));}
        bunkerJump=Resources.Load<AudioClip>("PlayerMovement/Bunker/Jump");surfaceJump=Resources.Load<AudioClip>("PlayerMovement/Surface/Jump");
        bunkerLand=Resources.Load<AudioClip>("PlayerMovement/Bunker/Land");surfaceLand=Resources.Load<AudioClip>("PlayerMovement/Surface/Land");
    }
    AudioSource CreateSource(string name,bool echo){
        var root=new GameObject(name);root.transform.SetParent(transform,false);root.transform.localPosition=Vector3.down*.8f;
        var source=root.AddComponent<AudioSource>();source.playOnAwake=false;source.loop=false;source.spatialBlend=1;source.dopplerLevel=0;
        source.rolloffMode=AudioRolloffMode.Linear;source.minDistance=1.5f;source.maxDistance=22;source.volume=.7f;
        if(echo){var filter=root.AddComponent<AudioEchoFilter>();filter.delay=110;filter.decayRatio=.3f;filter.wetMix=.23f;filter.dryMix=1;}
        return source;
    }
    void LateUpdate(){
        bool online=NetworkManager.Singleton!=null&&NetworkManager.Singleton.IsListening;
        var snapshot=online&&movement!=null&&movement.IsSpawned?movement.Animation.Value:player.LocalAnimation;
        bool suppressed=!NightTransition.IsNightScene(gameObject.scene.name)||NightTransition.BlocksGameplay||snapshot.Suppressed||
            oxygen==null||oxygen.IsDead()||(online&&(movement==null||!movement.IsSpawned))||
            (movement!=null&&movement.IsSpawned&&!movement.MovementEnabled.Value);
        double now=online?NetworkManager.Singleton.ServerTime.Time:Time.realtimeSinceStartupAsDouble;
        Vector3 delta=Vector3.ProjectOnPlane(transform.position-previous,Vector3.up);previous=transform.position;
        if(!initialized||suppressed||wasSuppressed){
            sequence=snapshot.JumpSequence;landed=snapshot.LandedAt;distance=0;nextStep=Time.unscaledTime+.15f;
            if(suppressed){bunkerSource.Stop();surfaceSource.Stop();}
            initialized=true;wasSuppressed=suppressed;return;
        }
        bool bunker=oxygen.IsInsideBunkerZone();
        if(snapshot.JumpSequence!=sequence){
            sequence=snapshot.JumpSequence;distance=0;
            if(snapshot.Takeoff&&now-snapshot.JumpedAt<.5){Play(bunker,bunker?bunkerJump:surfaceJump,.65f,sequence);JumpCount++;}
        }
        if(snapshot.LandedAt>landed){
            landed=snapshot.LandedAt;distance=0;nextStep=Time.unscaledTime+.2f;
            if(landed>0&&now-landed<.5){Play(bunker,bunker?bunkerLand:surfaceLand,1,sequence+1);LandingCount++;}
        }
        if(snapshot.Airborne||snapshot.Move.sqrMagnitude<.01f||delta.sqrMagnitude>16){distance=0;return;}
        distance+=delta.magnitude;
        float stride=snapshot.Sprint?RunStepDistance:WalkStepDistance;
        if(distance<stride||Time.unscaledTime<nextStep)return;
        distance%=stride;nextStep=Time.unscaledTime+.16f;
        var bank=bunker?bunkerSteps:surfaceSteps;uint variation=(uint)(StepCount+(movement!=null&&movement.IsSpawned?(int)movement.OwnerClientId:0));
        Play(bunker,bank[variation%4],snapshot.Sprint?1:.75f,variation);StepCount++;
    }
    void Play(bool bunker,AudioClip clip,float volume,uint variation){
        if(clip==null)return;
        LastWasBunker=bunker;var source=bunker?bunkerSource:surfaceSource;
        source.pitch=.96f+(variation%5)*.02f;source.PlayOneShot(clip,volume);
    }
    void OnDisable(){if(bunkerSource!=null)bunkerSource.Stop();if(surfaceSource!=null)surfaceSource.Stop();initialized=false;wasSuppressed=true;}
}
