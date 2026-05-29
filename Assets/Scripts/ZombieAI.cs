using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;
using Unity.Netcode;

[RequireComponent(typeof(CharacterController), typeof(NetworkObject))]
public class ZombieAI : NetworkBehaviour
{
    [Header("AI Behavior Settings")]
    [Tooltip("Radius to detect player for chase mode")]
    public float detectionRadius = 10f;

    [Tooltip("Speed when roaming/moving to bunker")]
    public float roamSpeed = 2f;

    [Tooltip("Speed when chasing player")]
    public float chaseSpeed = 5f;

    [Tooltip("Speed when stalking around the bunker")]
    public float stalkSpeed = 1.5f;

    [Tooltip("How close to get before picking new destination")]
    public float destinationThreshold = 1.5f;

    [Header("Player Detection")]
    [Tooltip("If true, prioritizes player when in FOV, otherwise goes to bunker")]
    public bool useFOVPriority = true;

    [Tooltip("Time to keep chasing player after losing sight (seconds)")]
    public float chaseMemoryTime = 3f;

    [Header("Roaming Destination")]
    [Tooltip("Set a specific location (bunker) for the zombie to roam towards. If null, uses spawn point with radius.")]
    public Transform roamDestinationTarget;

    [Tooltip("Only used if roamDestinationTarget is null. Radius around spawn point to pick random destinations.")]
    public float roamRadius = 20f;

    [Header("Stalking Settings")]
    [Tooltip("Minimum distance from bunker when stalking (should be inside shock/defense range)")]
    public float stalkMinRadius = 8f;

    [Tooltip("Maximum distance from bunker when stalking (must stay within defense threat radius)")]
    public float stalkMaxRadius = 18f;

    [Tooltip("How long to wait at each stalk point before picking a new one")]
    public float stalkWaitTime = 2f;

    [Tooltip("Distance from bunker at which stalking mode begins (should be <= defenseSystem.threatRadius)")]
    public float bunkerReachedDistance = 20f;

    [Header("Wandering Settings")]
    [Tooltip("How much the zombie wanders when objective is NOT in sight (0 = straight, 1 = pure random)")]
    [Range(0f, 1f)]
    public float searchBehaviorAmount = 0.85f;

    [Tooltip("Starting search behavior - will be reduced over time by NightTimeManager")]
    [Range(0f, 1f)]
    public float baseSearchBehaviorAmount = 0.85f;

    [Tooltip("How much the zombie wanders when objective IS in sight")]
    [Range(0f, 1f)]
    public float focusedSearchAmount = 0.3f;

    [Tooltip("Starting focused search amount - will be reduced over time")]
    [Range(0f, 1f)]
    public float baseFocusedSearchAmount = 0.3f;

    [Tooltip("Radius of the wander circle - higher = more erratic searching")]
    [Range(1f, 20f)]
    public float wanderCircleRadius = 10f;

    [Tooltip("How fast the wander angle changes")]
    [Range(10f, 100f)]
    public float wanderJitter = 50f;

    [Tooltip("Rotation speed when wandering")]
    public float wanderRotationSpeed = 2f;

    [Header("Field of View Settings")]
    [Tooltip("Field of view angle in degrees")]
    [Range(30f, 180f)]
    public float fieldOfViewAngle = 90f;

    [Tooltip("Maximum distance to see the objective")]
    public float sightDistance = 30f;

    [Header("Ground Alignment Settings")]
    public float groundCheckDistance = 2f;
    public float tiltSpeed = 5f;
    public LayerMask groundLayer = -1;
    public float maxTiltAngle = 30f;

    [Header("References")]
    public Transform bodyTransform;

    [Header("Jumpscare")]
    [Tooltip("Image to show when zombie catches player")]
    public Texture2D zombieJumpscareImage;

    [Tooltip("Sound to play when zombie catches player")]
    public AudioClip zombieJumpscareSound;

    [Tooltip("Time to show jumpscare before restarting")]
    public float jumpscareDisplayTime = 3f;

    public NetworkVariable<Vector3> PositionNetwork = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<Vector3> RotationNetwork = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<int> StateNetwork = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsStalkingNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsChasingNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> CanSeePlayerNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<Vector3> CurrentDestinationNetwork = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsNeutralizedNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<float> SearchBehaviorAmountNetwork = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<float> FocusedSearchAmountNetwork = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private CharacterController controller;
    private Transform playerTransform;
    private Vector3 currentDestination;
    private Vector3 currentMoveDirection;
    private float wanderAngle;
    private bool isChasing;
    private bool isStalking;
    private float gravity = -9.81f;
    private Vector3 velocity;
    private Vector3 spawnPoint;
    private bool canSeeObjective;
    private bool canSeePlayer;
    private float lastPlayerSeenTime;
    private float stalkWaitTimer;
    private bool isWaitingAtStalkPoint;
    private float stalkOrbitAngle;
    private float stalkOrbitRadius;
    private bool hasKilledPlayer = false;
    private AudioSource audioSource;
    private AudioSource movementAudioSource;
    private Renderer[] cachedRenderers;
    private Canvas gameCanvas;
    private GameObject jumpscarePanel;
    private Image jumpscareImageUI;
    private AudioSource jumpscareAudioSource;

    private Vector3 neutralizedRespawnPosition;
    private Quaternion neutralizedRespawnRotation = Quaternion.identity;
    private float neutralizedRespawnDelay;
    private Coroutine neutralizedRoutine;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 0f;
        audioSource.playOnAwake = false;

        movementAudioSource = gameObject.AddComponent<AudioSource>();
        movementAudioSource.spatialBlend = 0f;
        movementAudioSource.playOnAwake = false;
        movementAudioSource.loop = true;
    }

    public static ZombieAI Instance { get; private set; }

    void Start()
    {
        controller = GetComponent<CharacterController>();
        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        spawnPoint = transform.position;

        RefreshLocalPlayerReference();

        if (bodyTransform == null)
        {
            bodyTransform = transform;
        }

        baseSearchBehaviorAmount = searchBehaviorAmount;
        baseFocusedSearchAmount = focusedSearchAmount;

        SetNewRoamDestination();
        currentMoveDirection = transform.forward;
        wanderAngle = Random.Range(0f, 360f);

        if (IsNetworkSessionActive() && IsSpawned && !IsServer && controller != null)
        {
            controller.enabled = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            PushNetworkStateFromServer();
        }
        else
        {
            ApplyNetworkStateToClient();
        }
    }

    public void AdjustDifficulty(float difficultyReduction)
    {
        float oldSearch = searchBehaviorAmount;
        float oldFocused = focusedSearchAmount;

        searchBehaviorAmount = Mathf.Clamp01(baseSearchBehaviorAmount - difficultyReduction);
        focusedSearchAmount = Mathf.Clamp01(baseFocusedSearchAmount - difficultyReduction);

        if (IsNetworkSessionActive() && IsServer)
        {
            SearchBehaviorAmountNetwork.Value = searchBehaviorAmount;
            FocusedSearchAmountNetwork.Value = focusedSearchAmount;
        }

        Debug.Log($"<color=cyan>[{gameObject.name}]</color> Difficulty adjusted! Search: {oldSearch:F2} -> {searchBehaviorAmount:F2} | Focused: {oldFocused:F2} -> {focusedSearchAmount:F2}");
    }

    public void ResetToWander()
    {
        isStalking = false;
        isChasing = false;
        isWaitingAtStalkPoint = false;
        canSeePlayer = false;
        canSeeObjective = false;
        lastPlayerSeenTime = -999f;
        hasKilledPlayer = false;

        spawnPoint = transform.position;
        wanderAngle = Random.Range(0f, 360f);
        currentMoveDirection = transform.forward;
        SetNewRoamDestination();

        Debug.Log($"<color=cyan>[{gameObject.name}]</color> State reset - entering WANDER from spawn point");
    }

    public void ServerNeutralizeForDefense(Transform respawnPoint, float respawnDelay)
    {
        if (!IsServer)
        {
            return;
        }

        if (respawnPoint == null)
        {
            return;
        }

        neutralizedRespawnPosition = respawnPoint.position;
        neutralizedRespawnRotation = respawnPoint.rotation;
        neutralizedRespawnDelay = respawnDelay;

        if (neutralizedRoutine != null)
        {
            StopCoroutine(neutralizedRoutine);
        }

        neutralizedRoutine = StartCoroutine(NeutralizedRoutine());
    }

    public bool IsNeutralized()
    {
        return IsNetworkSessionActive() ? IsNeutralizedNetwork.Value : false;
    }

    void Update()
    {
        bool networkActive = IsNetworkSessionActive();
        bool isSpawnedReplica = networkActive && IsSpawned;

        if (networkActive && isSpawnedReplica)
        {
            if (IsServer)
            {
                UpdateServerAI();
                PushNetworkStateFromServer();
            }
            else
            {
                ApplyNetworkStateToClient();
            }

            return;
        }

        // Fallback path for offline play or scene objects that are not network-spawned yet.
        UpdateOfflineAI();
    }

    void UpdateServerAI()
    {
        if (IsNeutralizedNetwork.Value)
        {
            return;
        }

        RefreshLocalPlayerReference();

        if (useFOVPriority)
        {
            CheckPlayerVisibility();
        }
        else
        {
            CheckPlayerDistance();
        }

        CheckObjectiveVisibility();

        if (isChasing)
        {
            ChasePlayer();
        }
        else if (isStalking)
        {
            StalkBehavior();
        }
        else
        {
            RoamBehavior();
        }

        ApplyGravity();
        AlignToGround();
    }

    void UpdateOfflineAI()
    {
        if (controller != null && !controller.enabled)
        {
            controller.enabled = true;
        }

        RefreshLocalPlayerReference();

        if (useFOVPriority)
        {
            CheckPlayerVisibility();
        }
        else
        {
            CheckPlayerDistance();
        }

        CheckObjectiveVisibility();

        if (isChasing)
        {
            ChasePlayer();
        }
        else if (isStalking)
        {
            StalkBehavior();
        }
        else
        {
            RoamBehavior();
        }

        ApplyGravity();
        AlignToGround();
    }

    void CheckPlayerDistance()
    {
        if (playerTransform == null) return;
        if (IsPlayerDead(playerTransform))
        {
            ClearKilledOrDeadPlayerTarget();
            return;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
        if (distanceToPlayer <= detectionRadius)
        {
            if (!isChasing)
            {
                isChasing = true;
                isStalking = false;
                Debug.Log($"<color=yellow>[{gameObject.name}]</color> Player in radius - CHASING");
            }
        }
        else if (isChasing)
        {
            isChasing = false;
            CheckForStalking();
            Debug.Log($"<color=cyan>[{gameObject.name}]</color> Player left radius - returning to bunker");
        }
    }

    void CheckPlayerVisibility()
    {
        if (playerTransform == null) return;
        if (IsPlayerDead(playerTransform))
        {
            ClearKilledOrDeadPlayerTarget();
            return;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
        Vector3 directionToPlayer = (playerTransform.position - transform.position).normalized;

        if (distanceToPlayer > sightDistance)
        {
            canSeePlayer = false;
            HandlePlayerLoss();
            return;
        }

        float angleToPlayer = Vector3.Angle(transform.forward, directionToPlayer);
        if (angleToPlayer <= fieldOfViewAngle * 0.5f)
        {
            RaycastHit hit;
            if (Physics.Raycast(transform.position + Vector3.up * 0.5f, directionToPlayer, out hit, distanceToPlayer))
            {
                PlayerController hitPlayer = hit.collider.GetComponentInParent<PlayerController>();
                if (hitPlayer != null)
                {
                    canSeePlayer = true;
                    lastPlayerSeenTime = Time.time;

                    if (!isChasing)
                    {
                        isChasing = true;
                        isStalking = false;
                        Debug.Log($"<color=red>[{gameObject.name}]</color> PLAYER IN SIGHT - CHASING! Distance: {distanceToPlayer:F1}m, Angle: {angleToPlayer:F1}°");
                    }

                    return;
                }
            }
        }

        canSeePlayer = false;
        HandlePlayerLoss();
    }

    void HandlePlayerLoss()
    {
        if (!isChasing) return;

        float timeSinceLastSeen = Time.time - lastPlayerSeenTime;
        if (timeSinceLastSeen > chaseMemoryTime)
        {
            isChasing = false;
            CheckForStalking();
            Debug.Log($"<color=green>[{gameObject.name}]</color> Lost player - returning to bunker or stalking");
        }
    }

    void CheckForStalking()
    {
        if (roamDestinationTarget == null)
        {
            SetNewRoamDestination();
            return;
        }

        float distToBunker = Vector3.Distance(transform.position, roamDestinationTarget.position);
        if (distToBunker <= bunkerReachedDistance)
        {
            isStalking = true;
            isWaitingAtStalkPoint = false;
            InitStalkOrbit();
            Debug.Log($"<color=magenta>[{gameObject.name}]</color> Near bunker - entering STALK mode");
        }
        else
        {
            isStalking = false;
            SetNewRoamDestination();
        }
    }

    void CheckObjectiveVisibility()
    {
        Vector3 directionToObjective = (currentDestination - transform.position).normalized;
        float distanceToObjective = Vector3.Distance(transform.position, currentDestination);

        if (distanceToObjective > sightDistance)
        {
            canSeeObjective = false;
            return;
        }

        float angleToObjective = Vector3.Angle(transform.forward, directionToObjective);
        canSeeObjective = angleToObjective <= fieldOfViewAngle * 0.5f;
    }

    void ChasePlayer()
    {
        if (playerTransform == null) return;
        if (IsPlayerDead(playerTransform))
        {
            ClearKilledOrDeadPlayerTarget();
            return;
        }

        Vector3 playerDirection = (playerTransform.position - transform.position).normalized;
        playerDirection.y = 0;

        wanderAngle += Random.Range(-1f, 1f) * wanderJitter * Time.deltaTime;

        Vector3 circleCenter = transform.position + currentMoveDirection.normalized * 2f;
        Vector3 wanderForce = new Vector3(Mathf.Sin(wanderAngle), 0, Mathf.Cos(wanderAngle)) * wanderCircleRadius;
        Vector3 wanderTarget = circleCenter + wanderForce;
        Vector3 wanderDirection = (wanderTarget - transform.position).normalized;

        Vector3 targetDirection = Vector3.Lerp(playerDirection, wanderDirection, searchBehaviorAmount * 0.5f);
        currentMoveDirection = Vector3.Slerp(currentMoveDirection, targetDirection, Time.deltaTime * wanderRotationSpeed);

        if (controller != null && controller.enabled)
        {
            controller.Move(currentMoveDirection * chaseSpeed * Time.deltaTime);
        }

        if (currentMoveDirection != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(currentMoveDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * wanderRotationSpeed);
        }
    }

    void InitStalkOrbit()
    {
        if (roamDestinationTarget == null) return;

        Vector3 toEnemy = transform.position - roamDestinationTarget.position;
        toEnemy.y = 0f;

        stalkOrbitAngle = Mathf.Atan2(toEnemy.z, toEnemy.x) * Mathf.Rad2Deg;
        stalkOrbitRadius = Mathf.Clamp(toEnemy.magnitude, stalkMinRadius, stalkMaxRadius);
    }

    void StalkBehavior()
    {
        if (roamDestinationTarget == null) return;

        float angularSpeed = (stalkSpeed / stalkOrbitRadius) * Mathf.Rad2Deg;
        stalkOrbitAngle += angularSpeed * Time.deltaTime;
        if (stalkOrbitAngle >= 360f) stalkOrbitAngle -= 360f;

        float rad = stalkOrbitAngle * Mathf.Deg2Rad;
        Vector3 orbitPoint = roamDestinationTarget.position + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * stalkOrbitRadius;
        orbitPoint.y = transform.position.y;

        Vector3 dir = orbitPoint - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, 180f * Time.deltaTime);
        }

        currentMoveDirection = transform.forward;
        if (controller != null && controller.enabled)
        {
            controller.Move(transform.forward * stalkSpeed * Time.deltaTime);
        }
    }

    void RoamBehavior()
    {
        if (roamDestinationTarget != null)
        {
            float distToBunker = Vector3.Distance(transform.position, roamDestinationTarget.position);
            if (distToBunker <= bunkerReachedDistance)
            {
                isStalking = true;
                isWaitingAtStalkPoint = false;
                InitStalkOrbit();
                Debug.Log($"<color=magenta>[{gameObject.name}]</color> Reached bunker area - entering STALK mode");
                return;
            }
        }

        float distanceToDestination = Vector3.Distance(
            new Vector3(transform.position.x, 0, transform.position.z),
            new Vector3(currentDestination.x, 0, currentDestination.z));

        if (distanceToDestination < destinationThreshold)
        {
            SetNewRoamDestination();
        }

        Vector3 directionToDestination = (currentDestination - transform.position).normalized;
        directionToDestination.y = 0;

        wanderAngle += Random.Range(-1f, 1f) * wanderJitter * Time.deltaTime;
        Vector3 circleCenter = transform.position + currentMoveDirection.normalized * 2f;
        Vector3 wanderForce = new Vector3(Mathf.Sin(wanderAngle), 0, Mathf.Cos(wanderAngle)) * wanderCircleRadius;
        Vector3 wanderTarget = circleCenter + wanderForce;
        Vector3 wanderDirection = (wanderTarget - transform.position).normalized;

        float currentSearchAmount = canSeeObjective ? focusedSearchAmount : searchBehaviorAmount;
        Vector3 targetDirection = Vector3.Lerp(directionToDestination, wanderDirection, currentSearchAmount);
        currentMoveDirection = Vector3.Slerp(currentMoveDirection, targetDirection, Time.deltaTime * wanderRotationSpeed);

        if (controller != null && controller.enabled)
        {
            controller.Move(currentMoveDirection * roamSpeed * Time.deltaTime);
        }

        if (currentMoveDirection != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(currentMoveDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * wanderRotationSpeed);
        }
    }

    void SetNewRoamDestination()
    {
        if (roamDestinationTarget != null)
        {
            currentDestination = roamDestinationTarget.position;
        }
        else
        {
            Vector2 randomCircle = Random.insideUnitCircle * roamRadius;
            currentDestination = spawnPoint + new Vector3(randomCircle.x, 0, randomCircle.y);
        }

        if (IsNetworkSessionActive() && IsServer)
        {
            CurrentDestinationNetwork.Value = currentDestination;
        }
    }

    void ApplyGravity()
    {
        if (controller == null || !controller.enabled)
        {
            return;
        }

        if (controller.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    void AlignToGround()
    {
        if (bodyTransform == null)
        {
            return;
        }

        RaycastHit hit;
        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;

        if (Physics.Raycast(rayOrigin, Vector3.down, out hit, groundCheckDistance, groundLayer))
        {
            Vector3 groundNormal = hit.normal;
            Vector3 forwardProjected = Vector3.ProjectOnPlane(transform.forward, groundNormal).normalized;

            if (forwardProjected != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(forwardProjected, groundNormal);
                float currentTiltAngle = Vector3.Angle(Vector3.up, groundNormal);

                if (currentTiltAngle <= maxTiltAngle)
                {
                    bodyTransform.rotation = Quaternion.Slerp(bodyTransform.rotation, targetRotation, Time.deltaTime * tiltSpeed);
                }
            }
        }
    }

    void PushNetworkStateFromServer()
    {
        PositionNetwork.Value = transform.position;
        RotationNetwork.Value = transform.eulerAngles;
        StateNetwork.Value = isChasing ? 2 : (isStalking ? 1 : 0);
        IsStalkingNetwork.Value = isStalking;
        IsChasingNetwork.Value = isChasing;
        CanSeePlayerNetwork.Value = canSeePlayer;
        CurrentDestinationNetwork.Value = currentDestination;
        SearchBehaviorAmountNetwork.Value = searchBehaviorAmount;
        FocusedSearchAmountNetwork.Value = focusedSearchAmount;
    }

    void ApplyNetworkStateToClient()
    {
        if (controller != null && controller.enabled)
        {
            controller.enabled = false;
        }

        if (IsNeutralizedNetwork.Value)
        {
            ApplyNeutralizedVisuals(true);
            return;
        }

        ApplyNeutralizedVisuals(false);

        transform.position = PositionNetwork.Value;
        transform.eulerAngles = RotationNetwork.Value;
        currentDestination = CurrentDestinationNetwork.Value;
        isStalking = IsStalkingNetwork.Value;
        isChasing = IsChasingNetwork.Value;
        canSeePlayer = CanSeePlayerNetwork.Value;
        searchBehaviorAmount = SearchBehaviorAmountNetwork.Value;
        focusedSearchAmount = FocusedSearchAmountNetwork.Value;
    }

    void ApplyNeutralizedVisuals(bool neutralized)
    {
        if (cachedRenderers == null)
        {
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
        }

        foreach (Renderer renderer in cachedRenderers)
        {
            if (renderer != null)
            {
                renderer.enabled = !neutralized;
            }
        }
    }

    IEnumerator NeutralizedRoutine()
    {
        IsNeutralizedNetwork.Value = true;
        ApplyNeutralizedVisuals(true);

        if (controller != null)
        {
            controller.enabled = false;
        }

        yield return new WaitForSeconds(neutralizedRespawnDelay);

        transform.position = neutralizedRespawnPosition;
        transform.rotation = neutralizedRespawnRotation;
        spawnPoint = transform.position;

        ResetToWander();

        IsNeutralizedNetwork.Value = false;
        ApplyNeutralizedVisuals(false);

        if (controller != null)
        {
            controller.enabled = true;
        }

        neutralizedRoutine = null;
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hasKilledPlayer)
        {
            return;
        }

        if (IsNetworkSessionActive() && !IsServer)
        {
            return;
        }

        PlayerController hitPlayerController = hit.gameObject.GetComponentInParent<PlayerController>();
        if (hitPlayerController != null)
        {
            if (IsPlayerDead(hitPlayerController.transform))
            {
                ClearKilledOrDeadPlayerTarget();
                return;
            }

            hasKilledPlayer = true;
            Transform killedPlayer = hitPlayerController.transform;
            MarkPlayerKilled(killedPlayer);

            if (IsNetworkSessionActive() && IsServer)
            {
                NetworkObject playerObject = hit.gameObject.GetComponentInParent<NetworkObject>();
                if (playerObject != null)
                {
                    TriggerZombieJumpscareForClient(playerObject.OwnerClientId);
                }
            }
            else
            {
                TriggerZombieJumpscareOffline();
            }

            ResumeRoamingAfterKill(killedPlayer);
        }
    }

    void TriggerZombieJumpscareForClient(ulong clientId)
    {
        if (!IsServer)
        {
            return;
        }

        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        };

        TriggerZombieJumpscareClientRpc(clientRpcParams);
    }

    [ClientRpc]
    void TriggerZombieJumpscareClientRpc(ClientRpcParams clientRpcParams = default)
    {
        StartCoroutine(ShowZombieJumpscareNetwork());
    }

    void TriggerZombieJumpscareOffline()
    {
        Debug.Log("<color=red>[Zombie]</color> Player caught! Triggering jumpscare");

        PlayerController playerController = playerTransform != null ? playerTransform.GetComponent<PlayerController>() : null;
        OxygenSystem oxygen = playerController != null ? playerController.GetComponent<OxygenSystem>() : null;
        if (oxygen != null)
        {
            if (JumpscareSystem.Instance != null && zombieJumpscareImage != null)
            {
                JumpscareSystem.Instance.TriggerJumpscareWithTexture(zombieJumpscareImage, "", zombieJumpscareSound);
            }
            else
            {
                StartCoroutine(ShowZombieJumpscareOffline());
            }

            oxygen.MarkDeadFromMonster();
        }
    }

    IEnumerator ShowZombieJumpscareOffline()
    {
        yield return ShowZombieJumpscareCommon(true);
    }

    IEnumerator ShowZombieJumpscareNetwork()
    {
        yield return ShowZombieJumpscareCommon(false);
    }

    IEnumerator ShowZombieJumpscareCommon(bool restartScene)
    {
        CreateJumpscareUI();

        if (jumpscarePanel != null)
        {
            jumpscarePanel.SetActive(true);
        }

        if (zombieJumpscareSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(zombieJumpscareSound);
        }

        if (jumpscareImageUI != null && zombieJumpscareImage != null)
        {
            Rect rect = new Rect(0, 0, zombieJumpscareImage.width, zombieJumpscareImage.height);
            Sprite newSprite = Sprite.Create(zombieJumpscareImage, rect, new Vector2(0.5f, 0.5f));
            jumpscareImageUI.sprite = newSprite;
            jumpscareImageUI.color = Color.white;
        }

        yield return new WaitForSecondsRealtime(jumpscareDisplayTime);

        if (restartScene)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    void CreateJumpscareUI()
    {
        if (jumpscarePanel != null) return;

        if (gameCanvas == null)
        {
            gameCanvas = GameObject.Find("GameUICanvas")?.GetComponent<Canvas>();
            if (gameCanvas == null)
            {
                gameCanvas = FindObjectOfType<Canvas>();
            }
        }

        if (gameCanvas == null)
        {
            Debug.LogError("<color=red>[Zombie]</color> No Canvas found for jumpscare!");
            return;
        }

        jumpscarePanel = new GameObject("ZombieJumpscarePanel");
        jumpscarePanel.transform.SetParent(gameCanvas.transform, false);
        RectTransform panelRect = jumpscarePanel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        GameObject imageObj = new GameObject("JumpscareImage");
        imageObj.transform.SetParent(jumpscarePanel.transform, false);
        RectTransform imageRect = imageObj.AddComponent<RectTransform>();
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = Vector2.zero;
        imageRect.offsetMax = Vector2.zero;
        jumpscareImageUI = imageObj.AddComponent<Image>();
        jumpscareImageUI.color = Color.black;

        jumpscarePanel.SetActive(false);
    }

    void RefreshLocalPlayerReference()
    {
        if (IsNetworkSessionActive() && NetworkManager.Singleton != null)
        {
            if (IsServer)
            {
                Transform closest = null;
                float closestSqrDist = float.MaxValue;

                foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
                {
                    if (client.PlayerObject == null)
                    {
                        continue;
                    }

                    Transform candidate = client.PlayerObject.transform;
                    if (IsPlayerDead(candidate))
                    {
                        continue;
                    }

                    float sqrDist = (candidate.position - transform.position).sqrMagnitude;
                    if (sqrDist < closestSqrDist)
                    {
                        closestSqrDist = sqrDist;
                        closest = candidate;
                    }
                }

                if (closest != null)
                {
                    playerTransform = closest;
                    return;
                }
            }
            else
            {
                if (NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
                {
                    playerTransform = NetworkManager.Singleton.LocalClient.PlayerObject.transform;
                    return;
                }
            }
        }

        if (playerTransform != null && playerTransform.gameObject.activeInHierarchy)
        {
            return;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
        }
    }

    public bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    private bool IsPlayerDead(Transform candidate)
    {
        if (candidate == null)
        {
            return true;
        }

        OxygenSystem oxygen = candidate.GetComponent<OxygenSystem>();
        return oxygen != null && oxygen.IsDead();
    }

    private void MarkPlayerKilled(Transform killedPlayer)
    {
        if (killedPlayer == null)
        {
            return;
        }

        OxygenSystem oxygen = killedPlayer.GetComponent<OxygenSystem>();
        if (oxygen != null)
        {
            oxygen.MarkDeadFromMonster();
        }
    }

    private void ResumeRoamingAfterKill(Transform killedPlayer)
    {
        if (playerTransform == killedPlayer)
        {
            playerTransform = null;
        }

        hasKilledPlayer = false;
        canSeePlayer = false;
        isChasing = false;
        lastPlayerSeenTime = -999f;
        CheckForStalking();
    }

    private void ClearKilledOrDeadPlayerTarget()
    {
        playerTransform = null;
        hasKilledPlayer = false;
        canSeePlayer = false;
        isChasing = false;
        lastPlayerSeenTime = -999f;
        CheckForStalking();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        if (roamDestinationTarget != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(roamDestinationTarget.position, 1f);
            Gizmos.DrawLine(transform.position, roamDestinationTarget.position);

            Gizmos.color = new Color(0.8f, 0f, 1f, 0.4f);
            Gizmos.DrawWireSphere(roamDestinationTarget.position, stalkMinRadius);
            Gizmos.color = new Color(1f, 0f, 0.5f, 0.4f);
            Gizmos.DrawWireSphere(roamDestinationTarget.position, stalkMaxRadius);
            Gizmos.color = new Color(0f, 0.5f, 1f, 0.3f);
            Gizmos.DrawWireSphere(roamDestinationTarget.position, bunkerReachedDistance);
        }
        else
        {
            Gizmos.color = Color.blue;
            Vector3 center = Application.isPlaying ? spawnPoint : transform.position;
            Gizmos.DrawWireSphere(center, roamRadius);
        }

        if (Application.isPlaying)
        {
            Gizmos.color = isStalking ? Color.magenta : (canSeeObjective ? Color.green : Color.red);
            Gizmos.DrawLine(transform.position, currentDestination);
            Gizmos.DrawWireSphere(currentDestination, 0.5f);

            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position, currentMoveDirection * 3f);

            Gizmos.color = canSeePlayer ? Color.red : (canSeeObjective ? new Color(0, 1, 0, 0.2f) : new Color(1, 1, 0, 0.2f));
            Vector3 forward = transform.forward;
            Vector3 leftBoundary = Quaternion.Euler(0, -fieldOfViewAngle * 0.5f, 0) * forward * sightDistance;
            Vector3 rightBoundary = Quaternion.Euler(0, fieldOfViewAngle * 0.5f, 0) * forward * sightDistance;

            Gizmos.DrawLine(transform.position, transform.position + leftBoundary);
            Gizmos.DrawLine(transform.position, transform.position + rightBoundary);
            Gizmos.DrawLine(transform.position + leftBoundary, transform.position + rightBoundary);

            if (playerTransform != null && canSeePlayer)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position, playerTransform.position);
            }
        }
    }
}
