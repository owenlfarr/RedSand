using UnityEngine;
using Unity.Netcode;

// Each observer evaluates the same atomic snapshot against the synchronized clock.
[RequireComponent(typeof(PlayerController))]
[DefaultExecutionOrder(100)]
public class PlayerBodyAnimator : MonoBehaviour
{
    public Animator bodyAnimator;
    public float animationDampTime=.1f;
    static readonly int X=Animator.StringToHash("velocityX"),Z=Animator.StringToHash("velocityZ"),Gait=Animator.StringToHash("gait"),JumpTime=Animator.StringToHash("jumpTime");
    static readonly int Locomotion=Animator.StringToHash("Base Layer.Locomotion"),Jump=Animator.StringToHash("Base Layer.Jump");
    PlayerController player;NetworkPlayerMovementState movement;
    float jumpLength=2.3666667f;bool jumping,initialized;RuntimeAnimatorController configured;
    public bool IsPlayingJump=>jumping;
    public float JumpPhase {get;private set;}
    void Awake(){player=GetComponent<PlayerController>();movement=GetComponent<NetworkPlayerMovementState>();}
    void OnEnable(){initialized=false;jumping=false;}
    void LateUpdate(){
        if(bodyAnimator==null||!bodyAnimator.enabled||!bodyAnimator.gameObject.activeInHierarchy)return;
        if(configured!=bodyAnimator.runtimeAnimatorController){configured=bodyAnimator.runtimeAnimatorController;initialized=false;jumping=false;if(configured!=null)foreach(var clip in configured.animationClips)if(clip.name=="AstronautJump"){jumpLength=clip.length;break;}}
        if(!bodyAnimator.HasState(0,Locomotion)||!bodyAnimator.HasState(0,Jump))return;
        var state=movement!=null&&movement.IsSpawned?movement.Animation.Value:player.LocalAnimation;
        if((movement!=null&&movement.IsSpawned&&!movement.MovementEnabled.Value)||NightTransition.BlocksGameplay||GetComponent<OxygenSystem>()?.IsDead()==true)state.Suppressed=true;
        Vector2 move=state.Suppressed?Vector2.zero:Vector2.ClampMagnitude(state.Move,1);
        bodyAnimator.applyRootMotion=false;
        bodyAnimator.SetFloat(X,move.x,animationDampTime,Time.deltaTime);bodyAnimator.SetFloat(Z,move.y,animationDampTime,Time.deltaTime);
        bodyAnimator.SetFloat(Gait,state.Sprint&&move.sqrMagnitude>.001f?1:0,animationDampTime,Time.deltaTime);
        bool online=NetworkManager.Singleton!=null&&NetworkManager.Singleton.IsListening;
        double now=online?NetworkManager.Singleton.ServerTime.Time:Time.realtimeSinceStartupAsDouble;
        bool jump=AstronautJumpTiming.Sample(state,now,jumpLength,out float phase);JumpPhase=phase;
        bodyAnimator.SetFloat(JumpTime,Mathf.Clamp01(phase));
        if(!initialized){bodyAnimator.Play(jump?Jump:Locomotion,0,jump?phase:0);initialized=true;}
        else if(jump!=jumping)bodyAnimator.CrossFadeInFixedTime(jump?Jump:Locomotion,jump?.06f:.1f,0);
        jumping=jump;
    }
}
