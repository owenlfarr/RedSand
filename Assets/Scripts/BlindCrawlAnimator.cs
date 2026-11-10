using UnityEngine;
using Unity.Netcode;

// The server still owns the monster root. Only its visual rig is sampled here.
// A shared clock keeps looping poses aligned on clients and late joiners.
[RequireComponent(typeof(BlindLungeMonster))]
[DefaultExecutionOrder(100)]
public sealed class BlindCrawlAnimator:MonoBehaviour
{
    public Animator animator;
    public AnimationClip crawlClip;
    static readonly int PhaseParameter=Animator.StringToHash("crawlTime");
    static readonly int CrawlState=Animator.StringToHash("Base Layer.Crawl");
    bool initialized;
    public float LoopPhase {get;private set;}
    public static float PhaseAt(double time,float length)=>length>0?(float)((time%length)/length):0;
    void OnEnable(){initialized=false;}
    void Update(){
        if(animator==null||crawlClip==null||!animator.isActiveAndEnabled)return;
        double now=NetworkManager.Singleton!=null&&NetworkManager.Singleton.IsListening?NetworkManager.Singleton.ServerTime.Time:Time.timeAsDouble;
        LoopPhase=PhaseAt(now,crawlClip.length);animator.applyRootMotion=false;
        animator.SetFloat(PhaseParameter,LoopPhase);
        if(!initialized){animator.Play(CrawlState,0,LoopPhase);initialized=true;}
    }
}
