using UnityEngine;
using Unity.Netcode;

/// <summary>Host-authoritative station predator. Controls one encounter at a time.</summary>
[RequireComponent(typeof(NetworkObject), typeof(CharacterController))]
public sealed class PowerStationWatcher : NetworkBehaviour
{
    public enum Phase { Roaming, Watching, Dormant }
    public static PowerStationWatcher Instance { get; private set; }
    [Header("Patrol bounds (world X/Z)")]
    public Transform patrolCenter;
    public Vector2 patrolHalfExtents = new Vector2(55f, 35f);
    public float roamSpeed = 3.5f;
    public float sightRange = 45f;
    [Range(20f, 180f)] public float fieldOfView = 180f;
    public float eyeHeight = 2.1f;
    [Header("Encounter")]
    public float reactionSeconds = 1.5f;
    public Vector2 stillSeconds = new Vector2(5f, 10f);
    public float respawnSeconds = 45f;
    public float staticFadeSeconds = 2f;
    public AudioClip spottedSound;
    [Range(0f, 1f)] public float spottedVolume = 0.12f;
    public GameObject visual;
    [Header("Victim death jumpscare")]
    public Texture2D deathJumpscareImage;
    public AudioClip deathJumpscareSound;
    [Min(0.1f)] public float deathJumpscareDuration = 2f;

    public NetworkVariable<int> State = new NetworkVariable<int>((int)Phase.Roaming);
    public NetworkVariable<ulong> Target = new NetworkVariable<ulong>(ulong.MaxValue);
    public NetworkVariable<uint> Encounter = new NetworkVariable<uint>();
    public NetworkVariable<double> StartedAt = new NetworkVariable<double>();
    public NetworkVariable<float> RequiredStillSeconds = new NetworkVariable<float>();
    public NetworkVariable<double> RespawnAt = new NetworkVariable<double>();
    public NetworkVariable<Vector3> Position = new NetworkVariable<Vector3>();
    public NetworkVariable<float> Yaw = new NetworkVariable<float>();
    public uint RejectedReports { get; private set; }
    public Vector3 PatrolDestination => destination;

    CharacterController motor;
    readonly TerrainRockRoute rockRoute = new TerrainRockRoute();
    Vector3 home, destination, anchor;
    float anchorYaw, anchorPitch, destinationUntil, verticalSpeed, stuckTime;
    double lastActivityReport;
    bool anchored;
    Renderer[] renderers;
    bool[] rendererEnabled;
    WatcherStaticEffect effect;
    uint localEncounter;
    Vector3 previousLocalPosition;
    float previousLocalYaw, previousLocalPitch;
    bool sampledLocal;
    bool wasVisible;
    bool offlineInitialized, previouslyNetworked;
    Phase offlinePhase = Phase.Roaming;
    OxygenSystem offlineVictim;
    double offlineStarted, offlineRespawn;
    float offlineHold;
    bool NetworkActive => Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening;
    bool pendingActivity;
    double pendingSampleAt;
    float nextReport;
    bool receivedDeathScare;
    uint lastDeathScareEncounter;
    readonly RaycastHit[] sightHits = new RaycastHit[64];
    public double Clock => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
    public double GraceEnds => StartedAt.Value + reactionSeconds;

    void Awake()
    {
        motor = GetComponent<CharacterController>();
        motor.minMoveDistance = 0f;
        if (visual == null && transform.childCount > 0) visual = transform.GetChild(0).gameObject;
        renderers = visual != null ? visual.GetComponentsInChildren<Renderer>(true) : GetComponentsInChildren<Renderer>(true);
        rendererEnabled = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) rendererEnabled[i] = renderers[i].enabled;
        // Multiplayer waits for spawn; direct scene Play initializes a local simulation in Update.
        motor.enabled = false;
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        previouslyNetworked = true;
        motor.enabled = IsServer;
        offlineInitialized = false;
        offlineVictim = null;
        sampledLocal = false;
        receivedDeathScare = false;
        if (effect != null) effect.SetExposure(0f, staticFadeSeconds);
        home = patrolCenter != null ? patrolCenter.position : transform.position;
        home.y = transform.position.y;
        if (IsServer)
        {
            motor.enabled = true;
            Position.Value = transform.position;
            Yaw.Value = transform.eulerAngles.y;
            PickDestination();
        }
    }

    void Update()
    {
        if (!NetworkActive)
        {
            // Never restart local AI after a multiplayer disconnect/scene teardown.
            if (!previouslyNetworked && !WatcherNetworkTest.IsRunning) TickOffline();
            return;
        }
        if (!IsSpawned) { motor.enabled = false; return; }
        if (IsServer) TickServer();
        else
        {
            bool appearing = State.Value != (int)Phase.Dormant && !wasVisible;
            transform.position = appearing ? Position.Value : Vector3.Lerp(transform.position, Position.Value, 15f * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, Yaw.Value, 0f), 15f * Time.deltaTime);
        }
        SetVisible(State.Value != (int)Phase.Dormant);
        UpdateLocalEncounter();
    }

    void TickServer()
    {
        if (State.Value == (int)Phase.Dormant)
        {
            if (Clock >= RespawnAt.Value)
            {
                motor.enabled = true;
                // Reappear on valid ground within the station patrol bounds.
                PickDestination();
                motor.enabled = false;
                transform.position = destination;
                motor.enabled = true;
                State.Value = (int)Phase.Roaming;
                Position.Value = transform.position;
            }
            return;
        }
        if (State.Value == (int)Phase.Watching) TickEncounter();
        else
        {
            NetworkObject spotted = FindVisiblePlayer();
            if (spotted != null) BeginEncounter(spotted);
            else Roam();
        }
        Position.Value = transform.position;
        Yaw.Value = transform.eulerAngles.y;
    }

    public bool IsEligible(NetworkObject player)
    {
        if (player == null || !player.IsSpawned) return false;
        var oxygen = player.GetComponent<NetworkOxygenState>();
        if (oxygen == null || oxygen.IsDead.Value || oxygen.IsInBunker.Value) return false;
        var zones = player.GetComponent<OxygenSystem>();
        return zones == null || !zones.IsInsideBunkerZone();
    }

    NetworkObject FindVisiblePlayer()
    {
        NetworkObject closest = null;
        float best = sightRange * sightRange;
        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            var player = client.PlayerObject;
            if (!IsEligible(player)) continue;
            float distance = (player.transform.position - transform.position).sqrMagnitude;
            if (distance >= best || !CanSee(player)) continue;
            best = distance;
            closest = player;
        }
        return closest;
    }

    public bool CanSee(NetworkObject player)
    {
        if (!IsEligible(player)) return false;
        return CanSeeTransform(player.transform);
    }

    bool CanSeeTransform(Transform subject)
    {
        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        var controller = subject.GetComponent<PlayerController>();
        Vector3 target = controller != null && controller.playerCamera != null
            ? controller.playerCamera.position : subject.position + Vector3.up * 1.5f;
        Vector3 direction = target - origin;
        if (direction.sqrMagnitude > sightRange * sightRange) return false;
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (Vector3.Angle(transform.forward, flat) > fieldOfView * 0.5f) return false;
        int count = Physics.RaycastNonAlloc(origin, direction.normalized, sightHits, direction.magnitude, ~0, QueryTriggerInteraction.Ignore);
        // All hits are considered: collider order is undefined. Never see through a wall.
        for (int i = 0; i < count; i++)
        {
            Transform hit = sightHits[i].transform;
            if (hit.IsChildOf(transform) || hit.IsChildOf(subject)) continue;
            return false;
        }
        return count < sightHits.Length;
    }

    void BeginEncounter(NetworkObject player)
    {
        Target.Value = player.OwnerClientId;
        Encounter.Value++;
        StartedAt.Value = Clock;
        RequiredStillSeconds.Value = Random.Range(Mathf.Max(0.1f, stillSeconds.x), Mathf.Max(stillSeconds.x, stillSeconds.y));
        anchored = false;
        lastActivityReport = Clock;
        State.Value = (int)Phase.Watching;
        Debug.Log($"[Watcher] Noticed client {Target.Value}; encounter {Encounter.Value}; stillness {RequiredStillSeconds.Value:F2}s after {reactionSeconds:F2}s grace.");
    }

    void TickEncounter()
    {
        if (!NetworkManager.ConnectedClients.TryGetValue(Target.Value, out var client) || !IsEligible(client.PlayerObject))
        {
            Disappear();
            return;
        }
        var player = client.PlayerObject;
        Vector3 look = player.transform.position - transform.position;
        look.y = 0f;
        if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(look);
        if (Clock < GraceEnds) return;
        var movement = player.GetComponent<NetworkPlayerMovementState>();
        if (!anchored)
        {
            anchor = player.transform.position;
            anchorYaw = player.transform.eulerAngles.y;
            anchorPitch = movement != null ? movement.CameraPitch.Value : 0f;
            anchored = true;
        }
        Vector3 displacement = player.transform.position - anchor;
        bool moved = new Vector2(displacement.x, displacement.z).sqrMagnitude > 0.04f * 0.04f || Mathf.Abs(displacement.y) > 0.12f;
        bool looked = Mathf.Abs(Mathf.DeltaAngle(anchorYaw, player.transform.eulerAngles.y)) > 0.35f ||
            (movement != null && Mathf.Abs(anchorPitch - movement.CameraPitch.Value) > 0.35f);
        if (moved || looked || Clock - lastActivityReport > 2.5f)
        {
            FailEncounter(player);
            return;
        }
        if (Clock >= GraceEnds + RequiredStillSeconds.Value) Disappear();
    }

    void FailEncounter(NetworkObject player)
    {
        if (!IsServer || !IsEligible(player)) return;
        // Death remains authoritative; only the victim receives the timed presentation.
        var oxygen = player.GetComponent<NetworkOxygenState>();
        if (oxygen != null)
        {
            oxygen.UseBlackDeathIntro.Value = false;
            oxygen.IsDead.Value = true;
        }
        PlayDeathJumpscareClientRpc(Encounter.Value, new ClientRpcParams {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { player.OwnerClientId } }
        });
        Debug.Log($"[Watcher] Client {player.OwnerClientId} broke stillness in encounter {Encounter.Value}.");
        Disappear();
    }

    [ClientRpc]
    void PlayDeathJumpscareClientRpc(uint encounter, ClientRpcParams clientRpcParams = default)
    {
        if (receivedDeathScare && lastDeathScareEncounter == encounter) return;
        receivedDeathScare = true;
        lastDeathScareEncounter = encounter;
        ShowDeathJumpscare();
    }

    void ShowDeathJumpscare()
    {
        LocalJumpscarePresentation.Show(deathJumpscareImage, deathJumpscareSound, deathJumpscareDuration);
    }

    void Disappear()
    {
        State.Value = (int)Phase.Dormant;
        Target.Value = ulong.MaxValue;
        RespawnAt.Value = Clock + respawnSeconds;
        motor.enabled = false;
    }

    [ServerRpc(RequireOwnership = false)]
    public void ReportActivityServerRpc(uint encounter, double sampledAt, bool active, ServerRpcParams rpc = default)
    {
        if (!IsServer || State.Value != (int)Phase.Watching || encounter != Encounter.Value ||
            rpc.Receive.SenderClientId != Target.Value || sampledAt < StartedAt.Value || sampledAt > Clock + 0.5 || sampledAt < Clock - 2.5)
        {
            RejectedReports++;
            return;
        }
        lastActivityReport = Clock;
        if (active && sampledAt >= GraceEnds && NetworkManager.ConnectedClients.TryGetValue(Target.Value, out var victim) && IsEligible(victim.PlayerObject))
            FailEncounter(victim.PlayerObject);
    }

    void UpdateLocalEncounter()
    {
        bool watchingMe = IsClient && State.Value == (int)Phase.Watching && Target.Value == NetworkManager.LocalClientId;
        if (!watchingMe)
        {
            if (effect != null) effect.SetExposure(0f, staticFadeSeconds);
            sampledLocal = false;
            pendingActivity = false;
            return;
        }
        if (effect == null)
        {
            effect = new GameObject("Watcher static (local victim only)").AddComponent<WatcherStaticEffect>();
        }
        if (localEncounter != Encounter.Value)
        {
            localEncounter = Encounter.Value;
            effect.PlaySpotCue(spottedSound, spottedVolume);
            sampledLocal = false;
            pendingActivity = false;
        }
        float progress = Mathf.Clamp01((float)((Clock - StartedAt.Value) / (reactionSeconds + RequiredStillSeconds.Value)));
        effect.SetExposure(Mathf.Lerp(0.008f, 0.6f, progress * progress), staticFadeSeconds);
        // Test players use explicit synthetic input through the same validated RPC.
        if (WatcherNetworkTest.IsRunning) return;
        var player = NetworkManager.LocalClient != null ? NetworkManager.LocalClient.PlayerObject : null;
        if (player == null) return;
        bool active = SampleActivity(player.transform);
        if (active && Clock >= GraceEnds) { pendingActivity = true; pendingSampleAt = Clock; }
        if (Time.unscaledTime >= nextReport)
        {
            nextReport = Time.unscaledTime + 0.1f;
            ReportActivityServerRpc(Encounter.Value, pendingActivity ? pendingSampleAt : Clock, pendingActivity);
            pendingActivity = false;
        }
    }

    void SetVisible(bool visible)
    {
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].enabled = visible && rendererEnabled[i];
        wasVisible = visible;
    }

    bool SampleActivity(Transform subject)
    {
        var controller = subject.GetComponent<PlayerController>();
        float pitch = controller != null ? controller.GetCameraPitch() : 0f;
        bool active = Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.01f ||
            Mathf.Abs(Input.GetAxis("Mouse X")) > 0.001f || Mathf.Abs(Input.GetAxis("Mouse Y")) > 0.001f ||
            Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
            Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.F);
        for (int i = 0; i <= 9; i++) active |= Input.GetKey((KeyCode)((int)KeyCode.Alpha0 + i));
        // Menu clicks and mouse travel do not move the character. Keep the
        // actual pose checks below active, including any external movement.
        if (PlayerSettingsUI.IsGameMenuOpen) active = false;
        if (sampledLocal)
            active |= (subject.position - previousLocalPosition).sqrMagnitude > 0.003f * 0.003f ||
                Mathf.Abs(Mathf.DeltaAngle(previousLocalYaw, subject.eulerAngles.y)) > 0.05f || Mathf.Abs(previousLocalPitch - pitch) > 0.05f;
        previousLocalPosition = subject.position;
        previousLocalYaw = subject.eulerAngles.y;
        previousLocalPitch = pitch;
        sampledLocal = true;
        return active;
    }

    void TickOffline()
    {
        if (!offlineInitialized)
        {
            offlineInitialized = true;
            Instance = this;
            home = patrolCenter != null ? patrolCenter.position : transform.position;
            home.y = transform.position.y;
            offlinePhase = Phase.Roaming;
            motor.enabled = true;
            PickDestination();
            Debug.Log("[Watcher] Direct scene Play: local patrol and encounters enabled.");
        }
        if (offlinePhase == Phase.Dormant)
        {
            if (Time.timeAsDouble >= offlineRespawn)
            {
                PickDestination();
                motor.enabled = false;
                transform.position = destination;
                motor.enabled = true;
                offlinePhase = Phase.Roaming;
            }
        }
        else if (offlinePhase == Phase.Roaming)
        {
            OxygenSystem closest = null;
            float best = sightRange * sightRange;
            foreach (var candidate in FindObjectsByType<OxygenSystem>(FindObjectsSortMode.None))
            {
                if (!EligibleOffline(candidate)) continue;
                float distance = (candidate.transform.position - transform.position).sqrMagnitude;
                if (distance >= best || !CanSeeTransform(candidate.transform)) continue;
                best = distance;
                closest = candidate;
            }
            if (closest == null) Roam();
            else
            {
                offlineVictim = closest;
                offlineStarted = Time.timeAsDouble;
                offlineHold = Random.Range(Mathf.Max(0.1f, stillSeconds.x), Mathf.Max(stillSeconds.x, stillSeconds.y));
                offlinePhase = Phase.Watching;
                anchored = false;
                sampledLocal = false;
                if (effect == null) effect = new GameObject("Watcher static (local victim only)").AddComponent<WatcherStaticEffect>();
                effect.PlaySpotCue(spottedSound, spottedVolume);
            }
        }
        else if (!EligibleOffline(offlineVictim)) DisappearOffline();
        else
        {
            Transform subject = offlineVictim.transform;
            Vector3 look = Vector3.ProjectOnPlane(subject.position - transform.position, Vector3.up);
            if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(look);
            bool active = SampleActivity(subject);
            double grace = offlineStarted + reactionSeconds;
            if (Time.timeAsDouble >= grace)
            {
                var controller = subject.GetComponent<PlayerController>();
                float pitch = controller != null ? controller.GetCameraPitch() : 0f;
                if (!anchored)
                {
                    anchor = subject.position;
                    anchorYaw = subject.eulerAngles.y;
                    anchorPitch = pitch;
                    anchored = true;
                }
                Vector3 displacement = subject.position - anchor;
                bool moved = new Vector2(displacement.x, displacement.z).sqrMagnitude > 0.04f * 0.04f || Mathf.Abs(displacement.y) > 0.12f;
                bool looked = Mathf.Abs(Mathf.DeltaAngle(anchorYaw, subject.eulerAngles.y)) > 0.35f || Mathf.Abs(anchorPitch - pitch) > 0.35f;
                if (active || moved || looked)
                {
                    ShowDeathJumpscare();
                    offlineVictim.MarkDeadFromMonster();
                    DisappearOffline();
                }
                else if (Time.timeAsDouble >= grace + offlineHold) DisappearOffline();
            }
        }
        SetVisible(offlinePhase != Phase.Dormant);
        if (effect != null)
        {
            float progress = Mathf.Clamp01((float)((Time.timeAsDouble - offlineStarted) / (reactionSeconds + offlineHold)));
            effect.SetExposure(offlinePhase == Phase.Watching ? Mathf.Lerp(0.008f, 0.6f, progress * progress) : 0f, staticFadeSeconds);
        }
    }

    bool EligibleOffline(OxygenSystem candidate)
    {
        if (candidate == null || !candidate.isActiveAndEnabled || candidate.IsDead() || candidate.IsInsideBunkerZone()) return false;
        var controller = candidate.GetComponent<PlayerController>();
        return controller != null && controller.isActiveAndEnabled;
    }

    void DisappearOffline()
    {
        offlinePhase = Phase.Dormant;
        offlineVictim = null;
        offlineRespawn = Time.timeAsDouble + respawnSeconds;
        motor.enabled = false;
        sampledLocal = false;
    }

    void Roam()
    {
        Vector3 delta = destination - transform.position;
        delta.y = 0f;
        if (delta.magnitude < 1.2f || Time.time >= destinationUntil) PickDestination();
        Vector3 direction = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up).normalized;
        direction = rockRoute.Direction(motor, destination, direction);
        Vector3 previous = transform.position;
        verticalSpeed = motor.isGrounded ? -2f : Mathf.Max(-20f, verticalSpeed - 20f * Time.deltaTime);
        motor.Move((direction * roamSpeed + Vector3.up * verticalSpeed) * Time.deltaTime);
        Vector3 current = transform.position;
        current.x = Mathf.Clamp(current.x, home.x - patrolHalfExtents.x, home.x + patrolHalfExtents.x);
        current.z = Mathf.Clamp(current.z, home.z - patrolHalfExtents.y, home.z + patrolHalfExtents.y);
        if (current != transform.position) { motor.enabled = false; transform.position = current; motor.enabled = true; }
        if (direction.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 90f * Time.deltaTime);
        float expectedStep = roamSpeed * Time.deltaTime;
        stuckTime = Vector3.ProjectOnPlane(transform.position - previous, Vector3.up).sqrMagnitude < expectedStep * expectedStep * 0.04f ? stuckTime + Time.deltaTime : 0f;
        if (stuckTime > 1f) { PickDestination(); stuckTime = 0f; }
    }

    void PickDestination()
    {
        destination = transform.position;
        for (int attempt = 0; attempt < 32; attempt++)
        {
            Vector3 point = new Vector3(home.x + Random.Range(-patrolHalfExtents.x, patrolHalfExtents.x), home.y + 25f,
                home.z + Random.Range(-patrolHalfExtents.y, patrolHalfExtents.y));
            if (!Physics.Raycast(point, Vector3.down, out var hit, 70f, ~0, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.7f) continue;
            if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<NetworkOxygenState>() != null) continue;
            // Stay on outdoor ground: reject station roofs and bunker floors.
            if (Mathf.Abs(hit.point.y - home.y) > 4f) continue;
            Vector3 feet = hit.point + Vector3.up * 0.08f;
            if (Physics.CheckCapsule(feet + Vector3.up * 0.4f, feet + Vector3.up * Mathf.Max(0.5f, motor.height - 0.4f), 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;
            destination = feet;
            break;
        }
        destinationUntil = Time.time + Random.Range(7f, 16f);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 center = patrolCenter != null ? patrolCenter.position : transform.position;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(center, new Vector3(patrolHalfExtents.x * 2f, 0.4f, patrolHalfExtents.y * 2f));
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * eyeHeight, sightRange);
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (Instance == this) Instance = null;
        if (effect != null) Destroy(effect.gameObject);
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
        if (effect != null) Destroy(effect.gameObject);
        if (motor != null) motor.enabled = false;
    }
}




