using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Drives the astronaut body animator based on player input and sprint state.
/// Reads input axes directly — same source as PlayerController — to avoid
/// the one-frame lag and scale distortion of CharacterController.velocity.
/// Attach to the Capsule root. Assign bodyAnimator to the AstronautBody Animator.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerBodyAnimator : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The Animator on the AstronautBody child GameObject.")]
    public Animator bodyAnimator;

    [Header("Smoothing")]
    [Tooltip("How fast velocityZ blends toward the target. Lower = snappier.")]
    public float animationDampTime = 0.1f;

    // Cached parameter hashes — avoids string lookups every frame.
    private static readonly int VelocityZHash   = Animator.StringToHash("velocityZ");
    private static readonly int IsSprintingHash = Animator.StringToHash("isSprinting");

    private PlayerController playerController;
    private NetworkBehaviour networkBehaviour;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        networkBehaviour = GetComponent<NetworkBehaviour>();
    }

    private void Update()
    {
        if (bodyAnimator == null) return;

        bool isRemoteSpawnedPlayer = networkBehaviour != null && networkBehaviour.IsSpawned && !networkBehaviour.IsOwner;

        // Read local input for the owning player, synced movement for observers.
        // Positive = forward (W), negative = backward (S).
        float rawVertical = isRemoteSpawnedPlayer ? playerController.GetMoveInput().y : Input.GetAxis("Vertical");

        // Scale by actual speed so the threshold is always met when moving.
        // walkSpeed is the minimum speed, giving a velocityZ of ±walkSpeed when walking.
        float targetVelocityZ = rawVertical * playerController.walkSpeed;

        bool isSprinting = playerController.IsSprinting();

        // When sprinting forward, boost to sprintSpeed so the Sprint state triggers.
        if (isSprinting && rawVertical > 0f)
            targetVelocityZ = rawVertical * playerController.sprintSpeed;

        bodyAnimator.SetFloat(VelocityZHash,  targetVelocityZ, animationDampTime, Time.deltaTime);
        bodyAnimator.SetBool(IsSprintingHash, isSprinting);
    }
}
