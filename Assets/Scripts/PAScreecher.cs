using UnityEngine;
using Unity.Netcode;
using Networking;

// Uses the night clock's replicated deadline: host, clients and late joiners hear one event.
public sealed class PAScreecher : MonoBehaviour
{
    public float checkInterval = 90f;
    [Range(0f,1f)] public float eventChance = 0.35f;
    public float recoverySeconds = 180f;
    public AudioClip screechClip;
    [Range(0f,1f)] public float volume = 0.32f;
    public bool IsActive { get; private set; }
    NightTimeManager clock;
    AudioSource source;
    AudioClip generated;
    VoiceChatManager voice;
    double nextCheck, offlineEnd, eventStart;
    float savedListenerVolume;
    bool networked;
    int eventsThisNight, nightlyEventLimit;
    public static bool SupportsNight(string name) => name != null && name.StartsWith("Night ") && int.TryParse(name.Substring(6),out int n) && n >= 3;
    bool Online => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    double Now => Online ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;
    void Awake()
    {
        clock=GetComponent<NightTimeManager>();
        source=gameObject.AddComponent<AudioSource>();
        source.playOnAwake=false;source.loop=true;source.spatialBlend=0;
        source.ignoreListenerVolume=true;source.priority=20;
        if(screechClip==null){
            const int rate=22050;var data=new float[rate*2];var random=new System.Random(339);
            for(int i=0;i<data.Length;i++){
                float t=(float)i/rate;
                // Band-limited, distorted PA feedback with crackling interference.
                float tone=Mathf.Sin(2*Mathf.PI*970*t+2*Mathf.Sin(2*Mathf.PI*13*t));
                tone+=0.38f*Mathf.Sin(2*Mathf.PI*1940*t)+0.15f*(float)(random.NextDouble()*2-1);
                data[i]=Mathf.Clamp(tone,-1,1)*(0.7f+0.25f*Mathf.Sin(2*Mathf.PI*7*t));
            }
            generated=AudioClip.Create("PA feedback takeover",data.Length,1,rate,false);generated.SetData(data,0);screechClip=generated;
        }
        source.clip=screechClip;
        // Some nights stay completely quiet; others allow at most two or three scares.
        nightlyEventLimit=Random.value<0.25f?0:Random.Range(2,4);
        nextCheck=Now+120;
    }
    public bool Trigger(float duration)
    {
        if(!SupportsNight(gameObject.scene.name) || (Online && (!clock.IsSpawned || !clock.IsServer)))return false;
        if(eventsThisNight>=nightlyEventLimit || (Online?clock.PAScreechEndsAt.Value:offlineEnd)>Now)return false;
        double end=Now+Mathf.Clamp(duration,5,15);
        if(Online)clock.PAScreechEndsAt.Value=end;else offlineEnd=end;
        eventsThisNight++;
        if(BlindLungeMonster.Instance!=null){var pa=GameObject.Find("Radio");if(pa!=null)BlindLungeMonster.Instance.HearNoise(pa.transform.position,40);}
        nextCheck=end+Mathf.Max(180,recoverySeconds);
        return true;
    }
    void Update()
    {
        if(Online){networked=true;if(!clock.IsSpawned){Restore();return;}}
        else if(networked){Restore();return;}
        double now=Now,end=Online?clock.PAScreechEndsAt.Value:offlineEnd;
        bool authority=!Online||clock.IsServer;
        if(authority && eventsThisNight<nightlyEventLimit && now>=nextCheck && end<=now && Time.timeScale>0){
            nextCheck=now+Mathf.Max(30,checkInterval);
            if((PowerSystem.Instance==null||!PowerSystem.Instance.IsPowerOut)&&Random.value<eventChance){Trigger(Random.Range(5f,15f));end=Online?clock.PAScreechEndsAt.Value:offlineEnd;}
        }
        if(end>now){
            if(!IsActive){IsActive=true;eventStart=now;savedListenerVolume=AudioListener.volume;source.Play();}
            AudioListener.volume=savedListenerVolume*0.04f;
            source.volume=volume*savedListenerVolume*Mathf.Min(1,(float)(now-eventStart)/0.15f,(float)(end-now)/0.2f);
            if(voice==null)voice=FindFirstObjectByType<VoiceChatManager>();
            if(voice!=null)voice.SetPAScreecherMuted(true);
        }else Restore();
    }
    void Restore(){
        if(!IsActive)return;
        IsActive=false;source.Stop();AudioListener.volume=savedListenerVolume;
        if(voice!=null)voice.SetPAScreecherMuted(false);
    }
    void OnDisable()=>Restore();
    void OnDestroy(){Restore();if(generated!=null)Destroy(generated);}
}
