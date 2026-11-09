using UnityEngine;
using Unity.Netcode;

// Scene-authored pickup. Availability belongs to the monster's replicated mask.
[RequireComponent(typeof(SphereCollider))]
public sealed class SmallRockPickup : MonoBehaviour
{
    [Range(0, 63)] public int pickupId;
    Renderer visual;
    Collider target;
    public bool Available => BlindLungeMonster.Instance != null &&
        BlindLungeMonster.Instance.gameObject.scene == gameObject.scene &&
        BlindLungeMonster.Instance.IsPickupAvailable(pickupId);
    void Awake(){visual=GetComponent<Renderer>();target=GetComponent<Collider>();target.isTrigger=true;}
    void Update(){bool show=Available;if(visual!=null)visual.enabled=show;if(target!=null)target.enabled=show;}

    public static Vector3 Feet(PlayerController player)
    {
        var body=player.GetComponent<CharacterController>();
        return body!=null?player.transform.TransformPoint(body.center-Vector3.up*body.height*.5f):player.transform.position;
    }
    public bool CanReach(PlayerController player)
    {
        if(player==null||!player.enabled||!Available||Vector3.Distance(Feet(player),transform.position)>3f)return false;
        var life=player.GetComponent<OxygenSystem>();if(life!=null&&life.IsDead())return false;
        var state=player.GetComponent<NetworkPlayerMovementState>();
        if(NetworkManager.Singleton!=null&&NetworkManager.Singleton.IsListening&&state!=null&&!state.MovementEnabled.Value)return false;
        Vector3 eye=player.playerCamera!=null?player.playerCamera.position:player.transform.position+Vector3.up;
        Vector3 ray=transform.position-eye;
        foreach(var hit in Physics.RaycastAll(eye,ray.normalized,ray.magnitude,~0,QueryTriggerInteraction.Ignore))
            if(!hit.transform.IsChildOf(player.transform)&&!hit.transform.IsChildOf(transform))return false;
        return true;
    }
    public static SmallRockPickup LookTarget(PlayerController player)
    {
        if(player==null||player.playerCamera==null)return null;
        var view=player.playerCamera;
        float range=Mathf.Clamp(Vector3.Distance(view.position,Feet(player))+3,3,15);
        RaycastHit? nearest=null;
        foreach(var hit in Physics.RaycastAll(view.position,view.forward,range,~0,QueryTriggerInteraction.Collide))
        {
            if(hit.transform.IsChildOf(player.transform))continue;
            var pickup=hit.collider.GetComponent<SmallRockPickup>();
            if(hit.collider.isTrigger&&pickup==null)continue;
            if(!nearest.HasValue||hit.distance<nearest.Value.distance)nearest=hit;
        }
        var result=nearest.HasValue?nearest.Value.collider.GetComponent<SmallRockPickup>():null;
        return result!=null&&result.CanReach(player)?result:null;
    }
}
