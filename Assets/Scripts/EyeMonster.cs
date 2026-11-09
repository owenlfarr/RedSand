using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Movement and kills belong to the server. Each living owner reports their actual game camera.
[RequireComponent(typeof(NetworkObject), typeof(CharacterController))]
public sealed class EyeMonster : NetworkBehaviour
{
    public enum Mode { Roaming, Chasing, Frozen }
    public Transform bunkerEntrance;
    public GameObject visual;
    public float roamRadius = 65f, chaseRange = 40f, roamSpeed = 3f, chaseSpeed = 15f, killDistance = 1.7f;
    public AudioClip footstep;
    [Header("Spatial footsteps")]
    public float footstepNearDistance = 2f;
    public float footstepFarDistance = 35f;
    [Range(0f, 1f)] public float footstepVolume = 0.35f;
    public NetworkVariable<uint> StepSequence = new NetworkVariable<uint>();
    public AnimationClip roamAnimation, chaseAnimation;
    public NetworkVariable<int> AnimationMode = new NetworkVariable<int>();
    public NetworkVariable<float> AnimationPhase = new NetworkVariable<float>();
    public NetworkVariable<int> State = new NetworkVariable<int>();
    public NetworkVariable<Vector3> Position = new NetworkVariable<Vector3>();
    public NetworkVariable<float> Yaw = new NetworkVariable<float>();
    public NetworkVariable<ulong> Target = new NetworkVariable<ulong>(ulong.MaxValue);
    struct Report { public bool visible; public float time; }
    readonly Dictionary<ulong, Report> observers = new Dictionary<ulong, Report>();
    CharacterController motor;
    Renderer[] meshes;
    Animator[] animators;
    AudioSource steps;
    AudioClip generatedStep;
    readonly List<Vector3> skinVertices = new List<Vector3>();
    Mesh bakedSkin;
    struct SurfacePoint { public Transform space; public Vector3 local; }
    readonly List<SurfacePoint> visibleSurface = new List<SurfacePoint>();
    uint heardStep;
    float distanceSinceStep;
    bool movedThisFrame;
    public AudioSource FootstepSource => steps;
    public uint LocalDeathPresentations { get; private set; }
    Vector3 home, destination;
    float nextReport, nextStep, nextDestination, gravity;
    bool previouslyNetworked, offlineReady;
    int localMode;
    bool ActiveSession => Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening;

    void Awake()
    {
        motor = GetComponent<CharacterController>(); motor.enabled = false;
        meshes = (visual != null ? visual : gameObject).GetComponentsInChildren<Renderer>(true);
        animators = GetComponentsInChildren<Animator>(true);
        foreach (var a in animators) a.applyRootMotion = false;
        steps = gameObject.AddComponent<AudioSource>(); steps.playOnAwake = false;
        steps.loop = false; steps.spatialBlend = 1f;
        steps.minDistance = footstepNearDistance; steps.maxDistance = footstepFarDistance;
        steps.spread = 0f; steps.dopplerLevel = 0f; steps.priority = 50;
        steps.rolloffMode = AudioRolloffMode.Custom; steps.volume = footstepVolume;
        steps.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.05f, 0.85f), new Keyframe(0.15f, 0.5f),
            new Keyframe(0.4f, 0.18f), new Keyframe(0.7f, 0.045f), new Keyframe(1f, 0f)));
        bakedSkin = new Mesh { name = "Eye ground alignment skin" };
        if (footstep == null)
        {
            const int rate = 22050; var data = new float[rate / 5]; var random = new System.Random(817);
            for (int i=0;i<data.Length;i++) { float t=(float)i/rate; data[i]=(Mathf.Sin(t*450f)*0.7f+(float)(random.NextDouble()*2-1)*0.3f)*Mathf.Exp(-t*28f); }
            generatedStep = AudioClip.Create("Eye heavy footstep",data.Length,1,rate,false); generatedStep.SetData(data,0); footstep=generatedStep;
        }
    }
    public override void OnNetworkSpawn()
    {
        previouslyNetworked = true; observers.Clear(); motor.enabled = IsServer;
        InitializeHome();
        if(IsServer) { PlaceInsidePatrol(); SnapToFloor(1000f); }
        heardStep = StepSequence.Value;
        if(IsServer) { Position.Value=transform.position; Yaw.Value=transform.eulerAngles.y; State.Value=0; }
    }
    public override void OnNetworkDespawn() { motor.enabled=false; observers.Clear(); steps.Stop(); }
    void InitializeHome() { home=bunkerEntrance!=null?bunkerEntrance.position:transform.position; PickDestination(); }
    void PlaceInsidePatrol()
    {
        Vector3 offset=transform.position-home; offset.y=0;
        if(offset.magnitude<=roamRadius)return;
        Vector3 point=home+offset.normalized*(roamRadius*0.7f);
        foreach(var terrain in Terrain.activeTerrains)
        {
            Vector3 origin=terrain.transform.position; Vector3 size=terrain.terrainData.size;
            if(point.x>=origin.x && point.x<=origin.x+size.x && point.z>=origin.z && point.z<=origin.z+size.z)
            { point.y=terrain.SampleHeight(point)+origin.y+0.1f;break; }
        }
        motor.enabled=false; transform.position=point; motor.enabled=true; PickDestination();
    }
    void PickDestination() { Vector2 p=Random.insideUnitCircle*roamRadius; destination=home+new Vector3(p.x,0,p.y); destination.y=transform.position.y; nextDestination=Time.time+10f; }
    bool Alive(NetworkObject player) => player!=null && player.IsSpawned && player.TryGetComponent<NetworkOxygenState>(out var life) && !life.IsDead.Value;
    void Update()
    {
        if(ActiveSession)
        {
            if(!IsSpawned)return;
            if(IsClient && Time.unscaledTime>=nextReport)
            {
                nextReport=Time.unscaledTime+0.05f;
                var player=NetworkManager.LocalClient?.PlayerObject;
                ReportVisibilityServerRpc(Alive(player) && CameraSees(Camera.main, player.transform));
            }
            if(IsServer) TickServer();
            else { transform.SetPositionAndRotation(Position.Value,Quaternion.Euler(0,Yaw.Value,0)); }
            localMode=State.Value;
        }
        else
        {
            if(previouslyNetworked)return;
            if(!offlineReady) { InitializeHome(); PlaceInsidePatrol(); motor.enabled=true; SnapToFloor(1000f); offlineReady=true; }
            var camera=Camera.main; var player=camera!=null?camera.GetComponentInParent<PlayerController>():null;
            var life=player!=null?player.GetComponent<OxygenSystem>():null;
            bool alive=player!=null&&(life==null||!life.IsDead());
            bool watched=alive&&CameraSees(camera,player.transform);
            TickMovement(alive?player.transform:null,watched);
            if(alive&&CanKill(player.transform)) life?.MarkDeadFromMonster();
        }
        Present();
    }
    [ServerRpc(RequireOwnership=false)]
    void ReportVisibilityServerRpc(bool visible, ServerRpcParams rpc=default)
    {
        ulong id=rpc.Receive.SenderClientId;
        if(!NetworkManager.ConnectedClients.TryGetValue(id,out var client)||!Alive(client.PlayerObject)) { observers.Remove(id); return; }
        observers[id]=new Report{visible=visible,time=Time.unscaledTime};
    }
    void TickServer()
    {
        bool watched=false; NetworkObject nearest=null; float best=chaseRange*chaseRange;
        foreach(var client in NetworkManager.ConnectedClientsList)
        {
            var p=client.PlayerObject; if(!Alive(p))continue;
            // Missing/stalled reports freeze conservatively rather than allowing blind network kills.
            if(!observers.TryGetValue(client.ClientId,out var report)||Time.unscaledTime-report.time>0.5f||report.visible) watched=true;
            float d=(p.transform.position-transform.position).sqrMagnitude;
            if(d<best) { best=d; nearest=p; }
        }
        TickMovement(nearest!=null?nearest.transform:null,watched);
        Target.Value=nearest!=null?nearest.OwnerClientId:ulong.MaxValue;
        if(nearest!=null && CanKill(nearest.transform))
        {
            var life=nearest.GetComponent<NetworkOxygenState>();
            life.UseBlackDeathIntro.Value=false; life.IsDead.Value=true;
            // Use the same victim-local monster death/spectator flow as the other predators.
            FinishKillClientRpc(new ClientRpcParams { Send = new ClientRpcSendParams {
                TargetClientIds = new[] { nearest.OwnerClientId } } });
        }
        if(localMode!=(int)Mode.Frozen)
        {
            if(AnimationMode.Value!=localMode){AnimationMode.Value=localMode;AnimationPhase.Value=0;}
            var clip=localMode==(int)Mode.Chasing?chaseAnimation:roamAnimation;
            AnimationPhase.Value=(AnimationPhase.Value+Time.deltaTime*(localMode==(int)Mode.Chasing?1.4f:1f)/Mathf.Max(0.1f,clip!=null?clip.length:1f))%1f;
        }
        State.Value=localMode; Position.Value=transform.position; Yaw.Value=transform.eulerAngles.y;
    }
    void TickMovement(Transform target,bool watched)
    {
        movedThisFrame = false;
        if(watched) {localMode=(int)Mode.Frozen;SnapToFloor(2f);return;}
        bool chase=target!=null && Vector3.Distance(transform.position,target.position)<=chaseRange;
        localMode=(int)(chase?Mode.Chasing:Mode.Roaming);
        if(!chase && (Time.time>nextDestination || Vector3.ProjectOnPlane(destination-transform.position,Vector3.up).sqrMagnitude<4f))PickDestination();
        Vector3 direction=(chase?target.position:destination)-transform.position; direction.y=0;
        if(direction.sqrMagnitude>0.01f)transform.rotation=Quaternion.LookRotation(direction);
        gravity=motor.isGrounded?-2f:Mathf.Max(gravity-30f*Time.deltaTime,-50f);
        Vector3 previous = transform.position;
        motor.Move((direction.normalized*(chase?chaseSpeed:roamSpeed)+Vector3.up*gravity)*Time.deltaTime);
        SnapToFloor(1.5f);
        float travelled = Vector3.ProjectOnPlane(transform.position-previous, Vector3.up).magnitude;
        movedThisFrame = travelled > 0.0001f;
        distanceSinceStep += travelled;
        float stride = chase ? 3f : 1.8f;
        if(distanceSinceStep >= stride)
        {
            distanceSinceStep %= stride;
            if(ActiveSession && IsServer) StepSequence.Value++;
        }
    }
    public bool CameraSees(Camera camera,Transform viewer)
    {
        if(camera==null||!camera.isActiveAndEnabled)return false;
        // Renderer bounds contain empty space around long limbs. Testing box corners made a
        // nearby player freeze the monster even with its whole body behind their camera.
        if(visibleSurface.Count==0)return true; // Wait for the first animated geometry sample.
        foreach(var sample in visibleSurface)
        {
            Vector3 point=sample.space.TransformPoint(sample.local);
            Vector3 screen=camera.WorldToViewportPoint(point);
            if(screen.z<camera.nearClipPlane||screen.z>camera.farClipPlane||screen.x<0||screen.x>1||screen.y<0||screen.y>1)continue;
            Vector3 ray=point-camera.transform.position;bool blocked=false;
            foreach(var hit in Physics.RaycastAll(camera.transform.position,ray.normalized,ray.magnitude,~0,QueryTriggerInteraction.Ignore))
            {
                if(hit.transform.IsChildOf(transform)||(viewer!=null&&hit.transform.IsChildOf(viewer)))continue;
                blocked=true;break;
            }
            if(!blocked)return true;
        }
        return false;
    }
    void Present()
    {
        bool frozen=localMode==(int)Mode.Frozen;
        foreach(var a in animators) { if(!a.enabled)continue; if(ActiveSession&&IsSpawned) { a.speed=0; a.Play(AnimationMode.Value==(int)Mode.Chasing?"Chase":"Roam",0,AnimationPhase.Value);a.Update(0); }
            else { a.SetBool("Chasing",localMode==(int)Mode.Chasing); a.speed=frozen?0f:(localMode==(int)Mode.Chasing?1.4f:1f); } }
        if(frozen){steps.Stop();heardStep=StepSequence.Value;nextStep=0;return;}
        if(ActiveSession && IsSpawned)
        {
            if(heardStep!=StepSequence.Value) { heardStep=StepSequence.Value; PlayStep(); }
        }
        else if(movedThisFrame && Time.time>=nextStep)
        {
            PlayStep(); nextStep=Time.time+(localMode==(int)Mode.Chasing?0.2f:0.6f);
        }
    }
    void PlayStep()
    {
        var camera = Camera.main;
        if(camera == null || footstep == null)return;
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        Vector3 ray = camera.transform.position - origin;
        if(ray.magnitude >= footstepFarDistance)return;
        float gain = localMode == (int)Mode.Chasing ? 1f : 0.55f;
        var listenerRoot = camera.GetComponentInParent<PlayerController>();
        var networkListener = camera.GetComponentInParent<NetworkObject>();
        foreach(var hit in Physics.RaycastAll(origin, ray.normalized, ray.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if(hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(camera.transform) ||
                (listenerRoot != null && hit.transform.IsChildOf(listenerRoot.transform)) ||
                (networkListener != null && hit.transform.IsChildOf(networkListener.transform)))continue;
            gain *= 0.08f; // Bunker walls muffle exterior footsteps.
            break;
        }
        steps.Stop(); // A long assigned clip must never stack with the next stride.
        steps.PlayOneShot(footstep, gain);
    }

    [ClientRpc]
    void FinishKillClientRpc(ClientRpcParams rpc=default)
    {
        var victim=NetworkManager.LocalClient?.PlayerObject;
        if(victim==null)return;
        victim.GetComponent<OxygenSystem>()?.MarkDeadFromMonster();
        LocalDeathPresentations++;
    }

    // Compare the player's body to the motor, not two root pivots with different heights.
    public bool CanKill(Transform player)
    {
        if(player==null || Vector3.Distance(motor.bounds.ClosestPoint(player.position),player.position)>killDistance)return false;
        var body = player.GetComponent<CharacterController>();
        Vector3 to = body != null ? body.bounds.center : player.position + Vector3.up;
        Vector3 from = motor.bounds.ClosestPoint(to);
        // Start inside the monster at the victim's height so floors do not block contact.
        from = Vector3.MoveTowards(from, motor.bounds.center, 0.1f);
        Vector3 delta=to-from;
        foreach(var hit in Physics.RaycastAll(from,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
        {
            if(hit.transform.IsChildOf(transform)||hit.transform.IsChildOf(player))continue;
            return false;
        }
        return true;
    }

    public bool SnapToFloor(float maxDrop)
    {
        Vector3 feet=transform.position+motor.center-Vector3.up*(motor.height*0.5f);
        float rise=motor.stepOffset+0.2f;
        RaycastHit? ground=null;
        foreach(var hit in Physics.RaycastAll(feet+Vector3.up*rise,Vector3.down,maxDrop+rise,~0,QueryTriggerInteraction.Ignore))
        {
            if(hit.transform.IsChildOf(transform)||hit.collider.GetComponentInParent<PlayerController>()!=null ||
                hit.collider.GetComponentInParent<NetworkOxygenState>()!=null || hit.normal.y<0.5f)continue;
            if(!ground.HasValue||hit.distance<ground.Value.distance)ground=hit;
        }
        if(!ground.HasValue)return false;
        bool enabled=motor.enabled;motor.enabled=false;
        transform.position+=Vector3.up*(ground.Value.point.y-feet.y+0.015f);
        motor.enabled=enabled;gravity=-2f;return true;
    }

    void AlignVisualFacing()
    {
        // The retargeted FBX poses use a different up axis as well as a different
        // forward axis. Correct the complete animated body basis, not just yaw.
        foreach(var animator in animators)
        {
            if(!animator.enabled || !animator.isHuman || animator.avatar == null)continue;
            var left = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if(left == null || right == null || hips == null || head == null)continue;
            Vector3 up = head.position - hips.position;
            Vector3 facing = Vector3.Cross(right.position - left.position, up);
            if(up.sqrMagnitude < 0.0001f || facing.sqrMagnitude < 0.0001f)return;
            Quaternion bodyBasis = Quaternion.LookRotation(facing, up);
            visual.transform.rotation = transform.rotation * Quaternion.Inverse(bodyBasis) * visual.transform.rotation;
            return;
        }
    }

    void LateUpdate()
    {
        if(visual==null||bakedSkin==null||ActiveSession&&!IsSpawned)return;
        AlignVisualFacing();
        // Mixamo clips move the skeleton relative to the model pivot. Align the actual
        // animated soles, rather than the oversized skinned-renderer bounds, on every peer.
        float lowest=float.PositiveInfinity;
        visibleSurface.Clear();
        foreach(var renderer in meshes)
        {
            if(!renderer.enabled||!renderer.gameObject.activeInHierarchy)continue;
            if(renderer is SkinnedMeshRenderer skin)
            {
                // Compensate renderer scale before applying TransformPoint (the placed model is scaled 5x).
                skin.BakeMesh(bakedSkin, true); bakedSkin.GetVertices(skinVertices);
                int sampleStride=Mathf.Max(1,skinVertices.Count/256);
                for(int i=0;i<skinVertices.Count;i++)
                {
                    Vector3 vertex=skinVertices[i];
                    lowest=Mathf.Min(lowest,skin.transform.TransformPoint(vertex).y);
                    if(i%sampleStride==0)visibleSurface.Add(new SurfacePoint{space=skin.transform,local=vertex});
                }
            }
            else { lowest=Mathf.Min(lowest,renderer.bounds.min.y);visibleSurface.Add(new SurfacePoint{space=renderer.transform,local=renderer.transform.InverseTransformPoint(renderer.bounds.center)}); }
        }
        if(float.IsInfinity(lowest))return;
        float feetY=transform.position.y+motor.center.y-motor.height*0.5f;
        visual.transform.position+=Vector3.up*(feetY-lowest);
    }
    public override void OnDestroy(){if(generatedStep!=null)Destroy(generatedStep);if(bakedSkin!=null)Destroy(bakedSkin);base.OnDestroy();}
    void OnDrawGizmosSelected(){Gizmos.color=Color.yellow; Gizmos.DrawWireSphere(bunkerEntrance!=null?bunkerEntrance.position:transform.position,roamRadius); Gizmos.color=Color.red;Gizmos.DrawWireSphere(transform.position,chaseRange);}
}



