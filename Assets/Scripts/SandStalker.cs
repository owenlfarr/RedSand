using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

// Scene-placed encounters: only the server chooses targets, moves, and resolves hits.
[RequireComponent(typeof(NetworkObject), typeof(CharacterController))]
public sealed class SandStalker : NetworkBehaviour
{
    public enum Phase { Roaming, Warning, Chasing, Retreating, Dormant }
    public bool burrow;
    public GameObject visual;
    public ParticleSystem smoke;
    public AudioClip warningClip, burrowingClip;
    public float speed = 10.5f, sightRange = 32f;
    public const float BurrowRespawnSeconds = 30f;
    public NetworkVariable<Vector3> Position = new NetworkVariable<Vector3>();
    public NetworkVariable<int> State = new NetworkVariable<int>();
    public NetworkVariable<int> Cue = new NetworkVariable<int>();
    public NetworkVariable<ulong> TargetId = new NetworkVariable<ulong>(ulong.MaxValue);
    public NetworkVariable<double> DormantUntil = new NetworkVariable<double>();
    public Phase CurrentPhase => Online && IsSpawned ? (Phase)State.Value : phase;
    public PlayerController CurrentTarget => target;
    public int LocalWarnings { get; private set; }
    public int LocalDeaths { get; private set; }
    public AudioSource WarningSource => warningSource;
    public AudioSource BurrowingSource => diggingSource;

    CharacterController motor;
    AudioSource warningSource, diggingSource;
    AudioClip generatedWarning, generatedDigging;
    PlayerController target;
    Vector3 home, destination, strikePoint;
    float until, gravity, nextScan, nextWander, nextHandoff, nextRetreatPoint, strikeAt;
    int seenCue;
    double dormantUntil;
    Phase phase;
    bool wasOnline, strikePending, dodged, ready, offlineReady;
    readonly TerrainRockRoute route = new TerrainRockRoute();
    readonly List<PlayerController> players = new List<PlayerController>();
    bool Online => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    double Now => Online ? NetworkManager.Singleton.ServerTime.Time : Time.realtimeSinceStartupAsDouble;

    void Awake()
    {
        motor = GetComponent<CharacterController>();
        motor.enabled = false;
        home = destination = transform.position;
        warningSource = gameObject.AddComponent<AudioSource>();
        diggingSource = gameObject.AddComponent<AudioSource>();
        foreach (var source in new[] { warningSource, diggingSource }) {
            source.playOnAwake = false; source.spatialBlend = 1; source.dopplerLevel = 0;
            source.minDistance = 3; source.maxDistance = 48; source.rolloffMode = AudioRolloffMode.Linear;
        }
        warningSource.volume = .36f; diggingSource.volume = .38f; diggingSource.loop = true;
        if (warningClip == null) warningClip = generatedWarning = MakeCue(false);
        if (burrowingClip == null) burrowingClip = generatedDigging = MakeCue(true);
        diggingSource.clip = burrowingClip;
        ready = true;
    }

    AudioClip MakeCue(bool rumble)
    {
        const int rate = 22050;
        var samples = new float[rate]; var random = new System.Random(rumble ? 128 : 433);
        for (int i = 0; i < rate; i++) {
            float t = (float)i / rate, noise = (float)random.NextDouble() * 2 - 1;
            float envelope = rumble ? 1 : Mathf.Sin(Mathf.PI * t);
            samples[i] = envelope * (rumble ? .22f * noise + .3f * Mathf.Sin(2 * Mathf.PI * 65 * t)
                : (.22f * noise + .2f * Mathf.Sin(2 * Mathf.PI * (240 * t + .4f * Mathf.Sin(t * 20)))) * (.3f + .7f * Mathf.Pow(Mathf.Sin(t * 18), 2)));
        }
        var clip = AudioClip.Create(rumble ? "Sand burrowing" : "Distant whisper laugh", rate, 1, rate, false);
        clip.SetData(samples, 0); return clip;
    }

    public override void OnNetworkSpawn()
    {
        wasOnline = true; seenCue = Cue.Value; motor.enabled = IsServer;
        if (IsServer) { Snap(Ground(transform.position)); Position.Value = transform.position; Publish(); }
        else { transform.position = Position.Value; phase = (Phase)State.Value; }
        Present();
    }

    public override void OnNetworkDespawn() { motor.enabled = false; StopPresentation(); target = null; }

    void Update()
    {
        if (!ready) return;
        if (Online) {
            if (!IsSpawned) return;
            if (!IsServer) {
                transform.position = Vector3.Lerp(transform.position, Position.Value, 1 - Mathf.Exp(-25 * Time.deltaTime));
                phase = (Phase)State.Value; Present(); return;
            }
        } else {
            if (wasOnline) return;
            if (!offlineReady) { Snap(Ground(transform.position)); motor.enabled = true; offlineReady = true; }
        }
        if (!NightTransition.BlocksGameplay && Time.timeScale > 0 && Networking.NetworkLevelProgression.Instance?.IsTransferring != true) Tick();
        if (Online) Publish();
        Present();
    }

    void Publish()
    {
        Position.Value = transform.position; State.Value = (int)phase;
        TargetId.Value = target != null && target.IsSpawned ? target.OwnerClientId : ulong.MaxValue;
    }

    bool Living(PlayerController player)
    {
        if (player == null) return false;
        if (Online) {
            var life = player.GetComponent<NetworkOxygenState>();
            return player.IsSpawned && life != null && life.IsSpawned && !life.IsDead.Value && !life.IsInBunker.Value &&
                NetworkManager.ConnectedClients.TryGetValue(player.OwnerClientId, out var client) && client.PlayerObject == player.NetworkObject;
        }
        var oxygen = player.GetComponent<OxygenSystem>();
        return player.isActiveAndEnabled && (oxygen == null || !oxygen.IsDead() && !oxygen.IsInsideBunkerZone());
    }

    void GatherPlayers()
    {
        players.Clear();
        if (Online) {
            foreach (var client in NetworkManager.ConnectedClientsList) {
                var player = client.PlayerObject != null ? client.PlayerObject.GetComponent<PlayerController>() : null;
                if (Living(player)) players.Add(player);
            }
        } else foreach (var player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            if (Living(player)) players.Add(player);
    }

    bool ClearLine(Vector3 from, Vector3 to, PlayerController player)
    {
        Vector3 delta = to - from;
        foreach (var hit in Physics.RaycastAll(from, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore)) {
            if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(player.transform)) continue;
            // Other players do not become a solid wall for perception or beam checks.
            if (hit.collider.GetComponentInParent<PlayerController>() != null) continue;
            return false;
        }
        return true;
    }

    bool Visible(PlayerController player, float range)
    {
        return Living(player) && Vector3.Distance(transform.position, player.transform.position) <= range &&
            ClearLine(transform.position + Vector3.up, player.transform.position + Vector3.up, player);
    }

    PlayerController FindTarget(PlayerController exclude = null)
    {
        PlayerController best = null; float nearest = sightRange;
        foreach (var player in players) {
            if (!Living(player)) continue;
            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (player != exclude && distance < nearest && Visible(player, sightRange)) { best = player; nearest = distance; }
        }
        return best;
    }

    Vector3 Eye(PlayerController player) => player.playerCamera != null ? player.playerCamera.position : player.transform.position + Vector3.up * 1.6f;
    Vector3 ViewDirection(PlayerController player) => Quaternion.Euler(player.GetCameraPitch(), player.transform.eulerAngles.y, 0) * Vector3.forward;
    bool InPlayerView(PlayerController player) => Vector3.Angle(ViewDirection(player), transform.position + Vector3.up - Eye(player)) < 65 && Visible(player, 38);

    bool Lit()
    {
        foreach (var player in players) {
            if (!Living(player)) continue;
            var oxygen = player.GetComponent<OxygenSystem>();
            bool on = Online && oxygen != null && oxygen.IsSpawned && oxygen.IsFlashlightEnabledNetwork.Value;
            float range = 22, halfAngle = 22;
            foreach (var light in player.GetComponentsInChildren<Light>(true)) {
                if (light.type != LightType.Spot) continue;
                if (!Online) on = light.enabled && light.gameObject.activeInHierarchy;
                range = Mathf.Min(22, light.range); halfAngle = light.spotAngle * .5f;
                break;
            }
            if (on && Visible(player, range) && Vector3.Angle(ViewDirection(player), transform.position + Vector3.up - Eye(player)) <= halfAngle &&
                ClearLine(Eye(player), transform.position + Vector3.up, player)) return true;
        }
        return false;
    }

    void Warn()
    {
        phase = Phase.Warning; until = Time.time + (burrow ? 1.2f : .75f); strikePending = dodged = false;
        if (Online) Cue.Value++; else PlayWarning();
    }

    void ChooseRetreat()
    {
        Vector3 away = Vector3.ProjectOnPlane(transform.position - target.transform.position, Vector3.up).normalized;
        if (away.sqrMagnitude < .1f) away = -ViewDirection(target);
        // A side route carries it around the victim, rather than back down the same line.
        destination = Ground(target.transform.position + Quaternion.Euler(0, Random.value < .5f ? 105 : -105, 0) * away * 25);
        nextRetreatPoint = Time.time + 5;
    }

    void Rest(float seconds)
    {
        target = null; phase = Phase.Dormant; until = Time.time + seconds; strikePending = dodged = false; motor.enabled = false;
        dormantUntil = Now + seconds;
        if (Online) DormantUntil.Value = dormantUntil;
    }

    void Tick()
    {
        if (phase == Phase.Dormant) {
            if (Now < dormantUntil) return;
            Snap(Ground(home)); phase = Phase.Roaming; nextScan = 0; until = 0;
            if (Online) DormantUntil.Value = 0;
        }
        if (phase != Phase.Roaming && !Living(target)) { Rest(burrow ? BurrowRespawnSeconds : 5); return; }
        if (!motor.enabled) motor.enabled = true;
        if (Time.time >= nextScan) { nextScan = Time.time + .1f; GatherPlayers(); }
        if (phase == Phase.Roaming && Time.time >= until) {
            target = FindTarget(); if (target != null) { nextHandoff = Time.time + 4; Warn(); }
        }
        if (!burrow && (phase == Phase.Chasing || phase == Phase.Warning) && Lit()) {
            phase = Phase.Retreating; until = Time.time + 2; ChooseRetreat();
        }
        if (phase == Phase.Warning && Time.time >= until) phase = Phase.Chasing;
        if (!burrow && phase == Phase.Chasing && Time.time >= nextHandoff) {
            PlayerController other = null; float nearest = 12;
            foreach (var candidate in players) {
                if (candidate == target || !Living(candidate) || Vector3.Distance(candidate.transform.position, target.transform.position) > 6) continue;
                float distance = Vector3.Distance(candidate.transform.position, transform.position);
                if (distance <= nearest && Visible(candidate, 12)) { nearest = distance; other = candidate; }
            }
            if (other != null) {
                target = other; nextHandoff = Time.time + 8; Warn();
            }
        }
        if (phase == Phase.Retreating && Time.time >= until && !InPlayerView(target)) Warn();

        Vector3 step = Vector3.zero;
        if (phase == Phase.Chasing) {
            Vector3 aim = target.transform.position;
            if (burrow && strikePending) {
                motor.enabled = false;
                float distance = Vector3.ProjectOnPlane(aim - strikePoint, Vector3.up).magnitude;
                dodged |= distance <= 1.6f && JumpSafe(target);
                if (Time.time < strikeAt) return;
                strikePending = false;
                if (dodged) { Rest(BurrowRespawnSeconds); return; }
                if (distance <= 1.6f) { Kill(target); Rest(BurrowRespawnSeconds); return; }
                // Running on the ground does not consume an attack or open a free cooldown.
            }
            Vector3 delta = Vector3.ProjectOnPlane(aim - transform.position, Vector3.up);
            float travel = speed * Time.deltaTime;
            step = delta.normalized * Mathf.Min(delta.magnitude, travel);
            if (delta.magnitude <= (burrow ? Mathf.Max(.25f, travel) : 1.1f)) {
                if (burrow) {
                    strikePoint = Ground(aim); Snap(strikePoint); motor.enabled = false;
                    strikePending = true; dodged = JumpSafe(target);
                    // Brief host-side grace allows an owning client's jump update to arrive.
                    strikeAt = Time.time + .18f; return;
                }
                if (Visible(target, 2)) { Kill(target); Rest(5); return; }
            }
        } else if (phase == Phase.Retreating) {
            if (Time.time >= nextRetreatPoint || Vector3.Distance(transform.position, destination) < 2) ChooseRetreat();
            step = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up).normalized * speed * 1.35f * Time.deltaTime;
        } else if (phase == Phase.Roaming) {
            if (Time.time >= nextWander || Vector3.Distance(transform.position, destination) < 2) {
                var offset = Random.insideUnitCircle * 25; destination = Ground(home + new Vector3(offset.x, 0, offset.y)); nextWander = Time.time + 5;
            }
            step = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up).normalized * 2 * Time.deltaTime;
        }
        if (burrow && phase == Phase.Chasing) { motor.enabled = false; Snap(Ground(transform.position + step)); }
        else {
            var surface = TerrainRockNavigation.ForScene(gameObject.scene);
            if (surface != null && surface.Ready && step.sqrMagnitude > 0) {
                Vector3 goal = phase == Phase.Chasing ? target.transform.position : destination;
                step = route.Direction(motor, goal, step.normalized) * step.magnitude;
            }
            gravity = motor.isGrounded ? -2 : Mathf.Max(-40, gravity - 25 * Time.deltaTime);
            motor.Move(step + Vector3.up * gravity * Time.deltaTime);
        }
    }

    public static bool JumpSafe(PlayerController player)
    {
        var body = player.GetComponent<CharacterController>();
        Vector3 feet = body != null ? player.transform.TransformPoint(body.center - Vector3.up * body.height * .5f) : player.transform.position;
        bool grounded = player.GetNetworkGrounded();
        if (player.IsSpawned && !player.IsOwner) {
            var movement = player.GetComponent<NetworkPlayerMovementState>();
            if (movement != null && movement.IsSpawned) grounded = movement.IsGrounded.Value;
        }
        return !grounded && feet.y - Ground(feet).y > .35f;
    }

    static Vector3 Ground(Vector3 point)
    {
        foreach (var terrain in Terrain.activeTerrains) {
            var origin = terrain.transform.position; var size = terrain.terrainData.size;
            if (point.x >= origin.x && point.x <= origin.x + size.x && point.z >= origin.z && point.z <= origin.z + size.z) {
                point.y = terrain.SampleHeight(point) + origin.y + .05f; return point;
            }
        }
        float closest = float.PositiveInfinity; Vector3 grounded = point;
        foreach (var hit in Physics.RaycastAll(point + Vector3.up * 100, Vector3.down, 250, ~0, QueryTriggerInteraction.Ignore)) {
            if (hit.collider.GetComponentInParent<PlayerController>() != null || hit.collider.GetComponentInParent<SandStalker>() != null) continue;
            if (hit.distance < closest) { closest = hit.distance; grounded.y = hit.point.y + .05f; }
        }
        return grounded;
    }

    void Snap(Vector3 point) { bool active = motor.enabled; motor.enabled = false; transform.position = point; motor.enabled = active; }
    void Kill(PlayerController player)
    {
        if (!Living(player)) return;
        if (Online) {
            var life = player.GetComponent<NetworkOxygenState>(); life.UseBlackDeathIntro.Value = false; life.IsDead.Value = true;
            KillClientRpc(new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { player.OwnerClientId } } });
        } else { LocalDeaths++; player.GetComponent<OxygenSystem>()?.MarkDeadFromMonster(); }
    }
    [ClientRpc] void KillClientRpc(ClientRpcParams rpc = default) { LocalDeaths++; NetworkManager.LocalClient?.PlayerObject?.GetComponent<OxygenSystem>()?.MarkDeadFromMonster(); }
    void PlayWarning() { LocalWarnings++; warningSource.PlayOneShot(warningClip); }
    void Present()
    {
        if (visual != null) visual.SetActive(phase != Phase.Dormant && (!burrow || phase != Phase.Chasing));
        if (Online && seenCue != Cue.Value) { seenCue = Cue.Value; PlayWarning(); }
        bool underground = burrow && phase == Phase.Chasing;
        if (smoke != null) { if (underground && !smoke.isPlaying) smoke.Play(); else if (!underground && smoke.isPlaying) smoke.Stop(); }
        if (underground && !diggingSource.isPlaying) diggingSource.Play();
        else if (!underground && diggingSource.isPlaying) diggingSource.Stop();
    }
    void StopPresentation() { if (warningSource != null) warningSource.Stop(); if (diggingSource != null) diggingSource.Stop(); if (smoke != null) smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
    void OnDisable() => StopPresentation();
    public override void OnDestroy() { if (generatedWarning != null) Destroy(generatedWarning); if (generatedDigging != null) Destroy(generatedDigging); base.OnDestroy(); }
}
