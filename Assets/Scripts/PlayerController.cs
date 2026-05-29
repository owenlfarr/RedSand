using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 10f;
    public float gravity = -9.81f;
    public float jumpHeight = 2f;

    [Header("Sprint Settings")]
    public float maxSprintDuration = 10f;
    public float sprintRechargeDelay = 2f;
    public float sprintRechargeRate = 5f;

    [Header("Camera Settings")]
    public Transform playerCamera;
    public float mouseSensitivity = 2f;
    public float verticalLookLimit = 80f;
    public float normalFOV = 60f;
    public float sprintFOV = 70f;
    public float fovTransitionSpeed = 5f;

    [Header("Head Bob Settings")]
    public bool enableHeadBob = true;
    public float walkBobSpeed = 14f;
    public float walkBobAmount = 0.2f;
    public float sprintBobSpeed = 18f;
    public float sprintBobAmount = 0.3f;
    public float tiltAmount = 2f;

    [Header("Landing Impact Settings")]
    public bool enableLandingImpact = true;
    public float landingImpactAmount = 0.15f;
    public float landingRecoverySpeed = 8f;

    private CharacterController controller;
    private NetworkPlayerMovementState movementState;
    private OxygenSystem oxygenSystem;
    private Vector3 velocity;
    private bool isGrounded;
    private bool wasGrounded;
    private float sprintEnergy;
    private float sprintRechargeTimer;
    private bool canSprint;
    private float verticalRotation = 0f;
    private Camera cam;
    private AudioListener cameraAudioListener;
    private float bobTimer = 0f;
    public Vector3 cameraStartPos;
    private float landingImpactOffset = 0f;
    [HideInInspector] public Vector3 externalCameraOffset = Vector3.zero;

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        movementState = GetComponent<NetworkPlayerMovementState>();
        oxygenSystem = GetComponent<OxygenSystem>();
        sprintEnergy = maxSprintDuration;
        canSprint = true;
        wasGrounded = true;

        if (playerCamera == null)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                playerCamera = mainCam.transform;
            }
        }

        if (playerCamera != null)
        {
            cam = playerCamera.GetComponent<Camera>();
            cameraAudioListener = playerCamera.GetComponent<AudioListener>();
            if (cam != null)
            {
                cam.fieldOfView = normalFOV;
            }
            cameraStartPos = playerCamera.localPosition;
            Debug.Log($"Camera start position captured: {cameraStartPos}");
        }

        ApplyLocalOwnershipPresentation();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[PlayerController] OnNetworkSpawn OwnerClientId={OwnerClientId} IsOwner={IsOwner} IsServer={IsServer} IsClient={IsClient}");
        ApplyLocalOwnershipPresentation();
    }

    private void Update()
    {
        if (!CanProcessLocalInput())
        {
            return;
        }

        HandleGroundCheck();
        HandleMovement();
        HandleJump();
        HandleSprint();
    }

    private void LateUpdate()
    {
        if (!CanProcessLocalInput())
        {
            return;
        }

        HandleCameraLook();
        HandleFOV();
        HandleLandingImpact();
        HandleHeadBob();
    }

    private bool CanProcessLocalInput()
    {
        if (oxygenSystem != null && oxygenSystem.IsDead())
        {
            return false;
        }

        return (!IsSpawned || IsOwner) && (movementState == null || movementState.MovementEnabled.Value);
    }

    private void ApplyLocalOwnershipPresentation()
    {
        if (playerCamera == null)
        {
            return;
        }

        bool isLocal = !IsSpawned || IsOwner;

        if (cam == null)
        {
            cam = playerCamera.GetComponent<Camera>();
        }

        if (cameraAudioListener == null)
        {
            cameraAudioListener = playerCamera.GetComponent<AudioListener>();
        }

        if (cam != null)
        {
            cam.enabled = isLocal;
        }

        if (cameraAudioListener != null)
        {
            cameraAudioListener.enabled = isLocal;
        }

        bool isMenuScene = SceneManager.GetActiveScene().name == "Menu";

        if (isLocal && !isMenuScene)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else if (isLocal && isMenuScene)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyLocalOwnershipPresentation();
    }

    void HandleGroundCheck()
    {
        wasGrounded = isGrounded;
        isGrounded = controller.isGrounded;

        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        if (!wasGrounded && isGrounded && enableLandingImpact)
        {
            landingImpactOffset = -landingImpactAmount;
        }

        if (movementState != null)
        {
            movementState.IsGrounded.Value = isGrounded;
        }
    }

    void HandleMovement()
    {
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        Vector3 move = transform.right * horizontal + transform.forward * vertical;

        float currentSpeed = (Input.GetKey(KeyCode.LeftShift) && canSprint && sprintEnergy > 0) ? sprintSpeed : walkSpeed;

        controller.Move(move * currentSpeed * Time.deltaTime);

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        if (movementState != null && (!IsSpawned || IsOwner))
        {
            movementState.MoveInput.Value = new Vector2(horizontal, vertical);
        }
    }

    void HandleJump()
    {
        if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
    }

    void HandleSprint()
    {
        bool isSprinting = Input.GetKey(KeyCode.LeftShift) && canSprint && sprintEnergy > 0;

        if (isSprinting)
        {
            float moveInput = Mathf.Abs(Input.GetAxis("Horizontal")) + Mathf.Abs(Input.GetAxis("Vertical"));
            
            if (moveInput > 0.1f)
            {
                sprintEnergy -= Time.deltaTime;

                if (sprintEnergy <= 0)
                {
                    sprintEnergy = 0;
                    canSprint = false;
                    sprintRechargeTimer = 0f;
                }
            }
        }
        else
        {
            if (!canSprint)
            {
                sprintRechargeTimer += Time.deltaTime;

                if (sprintRechargeTimer >= sprintRechargeDelay)
                {
                    sprintEnergy += sprintRechargeRate * Time.deltaTime;

                    if (sprintEnergy >= maxSprintDuration)
                    {
                        sprintEnergy = maxSprintDuration;
                        canSprint = true;
                    }
                }
            }
            else
            {
                sprintEnergy += sprintRechargeRate * Time.deltaTime;
                sprintEnergy = Mathf.Min(sprintEnergy, maxSprintDuration);
            }
        }

        if (movementState != null)
        {
            movementState.CanSprint.Value = canSprint;
            movementState.SprintEnergy.Value = sprintEnergy;
            movementState.IsSprinting.Value = isSprinting;
        }
    }

    void HandleCameraLook()
    {
        if (playerCamera == null) return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        transform.Rotate(Vector3.up * mouseX);

        verticalRotation -= mouseY;
        verticalRotation = Mathf.Clamp(verticalRotation, -verticalLookLimit, verticalLookLimit);

        if (!enableHeadBob)
        {
            playerCamera.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
        }

        if (movementState != null)
        {
            movementState.CameraPitch.Value = verticalRotation;
        }
    }

    void HandleFOV()
    {
        if (cam == null) return;

        bool isSprinting = Input.GetKey(KeyCode.LeftShift) && canSprint && sprintEnergy > 0;
        float moveInput = Mathf.Abs(Input.GetAxis("Horizontal")) + Mathf.Abs(Input.GetAxis("Vertical"));

        float targetFOV = (isSprinting && moveInput > 0.1f) ? sprintFOV : normalFOV;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, fovTransitionSpeed * Time.deltaTime);
    }

    void HandleLandingImpact()
    {
        if (playerCamera == null || !enableLandingImpact) return;

        if (Mathf.Abs(landingImpactOffset) > 0.001f)
        {
            landingImpactOffset = Mathf.Lerp(landingImpactOffset, 0f, landingRecoverySpeed * Time.deltaTime);
        }
        else
        {
            landingImpactOffset = 0f;
        }
    }

    void HandleHeadBob()
    {
        if (playerCamera == null) return;

        if (!enableHeadBob)
        {
            playerCamera.localPosition = cameraStartPos + new Vector3(0f, landingImpactOffset, 0f) + externalCameraOffset;
            return;
        }

        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        float moveInput = Mathf.Abs(horizontal) + Mathf.Abs(vertical);

        if (isGrounded && moveInput > 0.1f)
        {
            bool isSprinting = Input.GetKey(KeyCode.LeftShift) && canSprint && sprintEnergy > 0;
            float currentBobSpeed = isSprinting ? sprintBobSpeed : walkBobSpeed;
            float currentBobAmount = isSprinting ? sprintBobAmount : walkBobAmount;

            bobTimer += Time.deltaTime * currentBobSpeed;

            float bobOffsetY = Mathf.Sin(bobTimer) * currentBobAmount;
            float bobOffsetX = Mathf.Cos(bobTimer / 2f) * currentBobAmount * 0.5f;

            float tiltZ = -horizontal * tiltAmount;

            Vector3 newPosition = cameraStartPos + new Vector3(bobOffsetX, bobOffsetY + landingImpactOffset, 0f) + externalCameraOffset;
            playerCamera.localPosition = newPosition;

            Quaternion targetRotation = Quaternion.Euler(verticalRotation, 0f, tiltZ);
            playerCamera.localRotation = targetRotation;
        }
        else
        {
            bobTimer = 0f;

            Vector3 targetPos = cameraStartPos + new Vector3(0f, landingImpactOffset, 0f) + externalCameraOffset;
            
            if (externalCameraOffset.magnitude > 0.001f)
            {
                playerCamera.localPosition = targetPos;
            }
            else
            {
                playerCamera.localPosition = Vector3.Lerp(playerCamera.localPosition, targetPos, Time.deltaTime * 10f);
            }
            
            playerCamera.localRotation = Quaternion.Slerp(playerCamera.localRotation, Quaternion.Euler(verticalRotation, 0f, 0f), Time.deltaTime * 10f);
        }
    }

    public float GetSprintEnergyPercentage()
    {
        return sprintEnergy / maxSprintDuration;
    }

    public bool IsMovementEnabled()
    {
        if (movementState != null && IsSpawned && !IsOwner)
        {
            return movementState.MovementEnabled.Value;
        }

        return true;
    }

    public Vector2 GetMoveInput()
    {
        if (movementState != null && IsSpawned && !IsOwner)
        {
            return movementState.MoveInput.Value;
        }

        return new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
    }

    public bool GetNetworkGrounded()
    {
        if (movementState != null && IsSpawned && !IsOwner)
        {
            return movementState.IsGrounded.Value;
        }

        return controller != null && controller.isGrounded;
    }

    public float GetCameraPitch()
    {
        if (movementState != null && IsSpawned && !IsOwner)
        {
            return movementState.CameraPitch.Value;
        }

        return verticalRotation;
    }

    public bool IsSprinting()
    {
        if (movementState != null && IsSpawned && !IsOwner)
        {
            return movementState.IsSprinting.Value;
        }

        return Input.GetKey(KeyCode.LeftShift) && canSprint && sprintEnergy > 0;
    }
}
