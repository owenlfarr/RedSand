using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(OxygenSystem))]
public class PlayerDeathSpectator : MonoBehaviour
{
    [Header("Spectating")]
    public string spectatorCameraName = "Spectator Cam";
    public float blackScreenDuration = 5f;
    public float fadeDuration = 1f;

    private OxygenSystem oxygenSystem;
    private NetworkOxygenState oxygenState;
    private NetworkObject networkObject;
    private PlayerController playerController;
    private CharacterController characterController;
    private NetworkPlayerMovementState movementState;
    private PlayerBodyAnimator bodyAnimator;
    private Camera playerCamera;
    private AudioListener playerAudioListener;
    private Camera activeSpectatorCamera;
    private AudioListener activeSpectatorListener;
    private NetworkObject activeSpectateTarget;
    private Transform activeSpectateViewSource;
    private Canvas overlayCanvas;
    private Image blackOverlay;
    private readonly List<NetworkObject> spectateTargets = new List<NetworkObject>();
    private bool localDeathSequenceStarted;
    private bool ragdollApplied;
    private bool subscribedToDeathState;
    private int spectateIndex;
    private float previousAudioVolume = 1f;
    private bool previousAudioPause;

    private void Awake()
    {
        CacheReferences();
        DisableLocalSpectatorCamera();
    }

    private void Start()
    {
        CacheReferences();
        DisableLocalSpectatorCamera();
        TrySubscribeToDeathState();

        if (!IsNetworkSessionActive() && oxygenSystem != null && oxygenSystem.IsDead())
        {
            ApplyDeathVisuals();
            BeginLocalDeathSequence();
        }
    }

    private void OnDestroy()
    {
        if (subscribedToDeathState && oxygenState != null)
        {
            oxygenState.IsDead.OnValueChanged -= OnDeadStateChanged;
        }
    }

    private void TrySubscribeToDeathState()
    {
        if (subscribedToDeathState)
        {
            return;
        }

        CacheReferences();
        if (oxygenState == null)
        {
            return;
        }

        oxygenState.IsDead.OnValueChanged += OnDeadStateChanged;
        subscribedToDeathState = true;

        if (oxygenState.IsDead.Value)
        {
            HandleDeathState();
        }
    }

    private void Update()
    {
        if (!localDeathSequenceStarted)
        {
            TrySubscribeToDeathState();
            return;
        }

        if (Input.GetKeyDown(KeyCode.D))
        {
            SwitchSpectateTarget(1);
        }
        else if (Input.GetKeyDown(KeyCode.A))
        {
            SwitchSpectateTarget(-1);
        }

        if (activeSpectateTarget != null && IsTargetDead(activeSpectateTarget))
        {
            SwitchSpectateTarget(1);
        }

        ApplySpectatorPitch();
    }

    private void OnDeadStateChanged(bool previousValue, bool currentValue)
    {
        if (currentValue)
        {
            HandleDeathState();
        }
    }

    public void HandleDeathState()
    {
        ApplyDeathVisuals();

        if (IsLocalPlayerObject())
        {
            bool useBlackIntro = oxygenSystem == null || oxygenSystem.ShouldUseBlackDeathIntro();
            BeginLocalDeathSequence(useBlackIntro);
        }
    }

    public void BeginLocalDeathSequence(bool useBlackIntro = true)
    {
        if (localDeathSequenceStarted)
        {
            return;
        }

        localDeathSequenceStarted = true;
        StartCoroutine(LocalDeathSequence(useBlackIntro));
    }

    private IEnumerator LocalDeathSequence(bool useBlackIntro)
    {
        DisableLocalControlAndView();

        if (useBlackIntro)
        {
            ShowBlackOverlay(1f);
            previousAudioVolume = AudioListener.volume;
            previousAudioPause = AudioListener.pause;
            AudioListener.volume = 0f;
            AudioListener.pause = true;

            yield return new WaitForSecondsRealtime(blackScreenDuration);
        }

        SelectFirstSpectateTarget();

        if (useBlackIntro)
        {
            AudioListener.pause = previousAudioPause;
            AudioListener.volume = previousAudioVolume;
        }

        if (useBlackIntro && activeSpectatorCamera != null)
        {
            yield return StartCoroutine(FadeBlackOverlay(0f));
            HideBlackOverlay();
        }
    }

    private void CacheReferences()
    {
        oxygenSystem = GetComponent<OxygenSystem>();
        oxygenState = GetComponent<NetworkOxygenState>();
        networkObject = GetComponent<NetworkObject>();
        playerController = GetComponent<PlayerController>();
        characterController = GetComponent<CharacterController>();
        movementState = GetComponent<NetworkPlayerMovementState>();
        bodyAnimator = GetComponent<PlayerBodyAnimator>();

        if (playerController != null && playerController.playerCamera != null)
        {
            playerCamera = playerController.playerCamera.GetComponent<Camera>();
            playerAudioListener = playerController.playerCamera.GetComponent<AudioListener>();
        }
    }

    private bool IsLocalPlayerObject()
    {
        return !IsNetworkSessionActive() || (networkObject != null && networkObject.IsOwner);
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    private void ApplyDeathVisuals()
    {
        if (ragdollApplied)
        {
            return;
        }

        ragdollApplied = true;

        if (bodyAnimator != null)
        {
            bodyAnimator.enabled = false;
        }

        Animator[] animators = GetComponentsInChildren<Animator>(true);
        foreach (Animator animator in animators)
        {
            if (animator != null)
            {
                animator.enabled = false;
            }
        }

        if (characterController != null)
        {
            characterController.enabled = false;
        }

        BuildRuntimeRagdollIfNeeded();

        Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody body in bodies)
        {
            if (body == null)
            {
                continue;
            }

            body.isKinematic = false;
            body.useGravity = true;
            body.detectCollisions = true;
            body.AddForce((Random.insideUnitSphere + Vector3.down) * 0.6f, ForceMode.Impulse);
            body.AddTorque(Random.insideUnitSphere * 0.4f, ForceMode.Impulse);
        }

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        foreach (Collider collider in colliders)
        {
            if (collider == null || collider is CharacterController)
            {
                continue;
            }

            if (collider.transform == transform)
            {
                collider.enabled = false;
            }
            else
            {
                collider.enabled = true;
            }
        }
    }

    private void BuildRuntimeRagdollIfNeeded()
    {
        Animator animator = GetComponentInChildren<Animator>(true);
        if (animator != null && animator.isHuman)
        {
            AddBoneBody(animator.GetBoneTransform(HumanBodyBones.Hips), 0.18f, 0.28f);
            AddBoneBody(animator.GetBoneTransform(HumanBodyBones.Spine), 0.16f, 0.28f);
            AddBoneBody(animator.GetBoneTransform(HumanBodyBones.Chest), 0.16f, 0.28f);
            AddBoneBody(animator.GetBoneTransform(HumanBodyBones.Head), 0.12f, 0.18f);
            AddBoneBody(animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), 0.07f, 0.25f);
            AddBoneBody(animator.GetBoneTransform(HumanBodyBones.LeftLowerArm), 0.06f, 0.25f);
            AddBoneBody(animator.GetBoneTransform(HumanBodyBones.RightUpperArm), 0.07f, 0.25f);
            AddBoneBody(animator.GetBoneTransform(HumanBodyBones.RightLowerArm), 0.06f, 0.25f);
            AddBoneBody(animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), 0.09f, 0.32f);
            AddBoneBody(animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), 0.08f, 0.32f);
            AddBoneBody(animator.GetBoneTransform(HumanBodyBones.RightUpperLeg), 0.09f, 0.32f);
            AddBoneBody(animator.GetBoneTransform(HumanBodyBones.RightLowerLeg), 0.08f, 0.32f);
            return;
        }

        Rigidbody[] existingBodies = GetComponentsInChildren<Rigidbody>(true);
        if (existingBodies.Length > 0)
        {
            return;
        }

        Rigidbody rootBody = gameObject.AddComponent<Rigidbody>();
        rootBody.mass = 2f;
        rootBody.isKinematic = true;
    }

    private void AddBoneBody(Transform bone, float radius, float height)
    {
        if (bone == null)
        {
            return;
        }

        if (bone.GetComponent<Rigidbody>() == null)
        {
            Rigidbody body = bone.gameObject.AddComponent<Rigidbody>();
            body.mass = 0.5f;
            body.isKinematic = true;
        }

        if (bone.GetComponent<Collider>() == null)
        {
            CapsuleCollider capsule = bone.gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = radius;
            capsule.height = Mathf.Max(height, radius * 2f);
            capsule.direction = 1;
        }

        if (bone.GetComponent<CharacterJoint>() == null)
        {
            Rigidbody parentBody = FindParentRigidbody(bone.parent);
            if (parentBody != null)
            {
                CharacterJoint joint = bone.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = parentBody;
                joint.enableProjection = true;
            }
        }
    }

    private static Rigidbody FindParentRigidbody(Transform current)
    {
        while (current != null)
        {
            Rigidbody body = current.GetComponent<Rigidbody>();
            if (body != null)
            {
                return body;
            }

            current = current.parent;
        }

        return null;
    }

    private void DisableLocalControlAndView()
    {
        if (movementState != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            movementState.MovementEnabled.Value = false;
        }

        if (playerController != null)
        {
            playerController.enabled = false;
        }

        if (characterController != null)
        {
            characterController.enabled = false;
        }

        if (playerCamera != null)
        {
            playerCamera.enabled = false;
        }

        if (playerAudioListener != null)
        {
            playerAudioListener.enabled = false;
        }

        DisableLocalSpectatorCamera();
    }

    private void DisableLocalSpectatorCamera()
    {
        Transform spectatorTransform = FindSpectatorCameraTransform(transform);
        if (spectatorTransform == null)
        {
            return;
        }

        Camera spectatorCamera = spectatorTransform.GetComponent<Camera>();
        if (spectatorCamera != null)
        {
            spectatorCamera.enabled = false;
        }

        AudioListener spectatorListener = spectatorTransform.GetComponent<AudioListener>();
        if (spectatorListener != null)
        {
            spectatorListener.enabled = false;
        }
    }

    private void SelectFirstSpectateTarget()
    {
        RefreshSpectateTargets();
        spectateIndex = Mathf.Clamp(spectateIndex, 0, Mathf.Max(0, spectateTargets.Count - 1));
        ApplySpectateTarget();
    }

    private void SwitchSpectateTarget(int direction)
    {
        RefreshSpectateTargets();
        if (spectateTargets.Count == 0)
        {
            ClearSpectateCamera();
            ShowBlackOverlay(1f);
            return;
        }

        spectateIndex = (spectateIndex + direction) % spectateTargets.Count;
        if (spectateIndex < 0)
        {
            spectateIndex = spectateTargets.Count - 1;
        }

        ApplySpectateTarget();
    }

    private void RefreshSpectateTargets()
    {
        spectateTargets.Clear();

        if (!IsNetworkSessionActive() || NetworkManager.Singleton == null)
        {
            return;
        }

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null || client.PlayerObject == networkObject)
            {
                continue;
            }

            OxygenSystem targetOxygen = client.PlayerObject.GetComponent<OxygenSystem>();
            if (targetOxygen != null && targetOxygen.IsDead())
            {
                continue;
            }

            spectateTargets.Add(client.PlayerObject);
        }
    }

    private void ApplySpectateTarget()
    {
        ClearSpectateCamera();

        if (spectateTargets.Count == 0)
        {
            ShowBlackOverlay(1f);
            return;
        }

        NetworkObject target = spectateTargets[Mathf.Clamp(spectateIndex, 0, spectateTargets.Count - 1)];
        activeSpectateTarget = target;
        activeSpectateViewSource = null;
        Transform spectatorTransform = FindSpectatorCameraTransform(target.transform);
        PlayerController targetController = target.GetComponent<PlayerController>();
        if (targetController != null && targetController.playerCamera != null)
        {
            activeSpectateViewSource = targetController.playerCamera;
        }

        if (spectatorTransform == null)
        {
            if (targetController != null)
            {
                spectatorTransform = targetController.playerCamera;
            }
        }

        if (spectatorTransform == null)
        {
            return;
        }

        activeSpectatorCamera = spectatorTransform.GetComponent<Camera>();
        if (activeSpectatorCamera == null)
        {
            activeSpectatorCamera = spectatorTransform.gameObject.AddComponent<Camera>();
        }

        activeSpectatorListener = spectatorTransform.GetComponent<AudioListener>();
        if (activeSpectatorListener == null)
        {
            activeSpectatorListener = spectatorTransform.gameObject.AddComponent<AudioListener>();
        }

        activeSpectatorCamera.enabled = true;
        activeSpectatorListener.enabled = true;
        ApplySpectatorPose();
    }

    private void ApplySpectatorPitch()
    {
        ApplySpectatorPose();
    }

    private void ApplySpectatorPose()
    {
        if (activeSpectatorCamera == null)
        {
            return;
        }

        if (activeSpectateViewSource != null)
        {
            activeSpectatorCamera.transform.SetPositionAndRotation(activeSpectateViewSource.position, activeSpectateViewSource.rotation);
            Camera sourceCamera = activeSpectateViewSource.GetComponent<Camera>();
            if (sourceCamera != null)
            {
                activeSpectatorCamera.fieldOfView = sourceCamera.fieldOfView;
            }
        }
    }

    private void ClearSpectateCamera()
    {
        if (activeSpectatorCamera != null)
        {
            activeSpectatorCamera.enabled = false;
        }

        if (activeSpectatorListener != null)
        {
            activeSpectatorListener.enabled = false;
        }

        activeSpectatorCamera = null;
        activeSpectatorListener = null;
        activeSpectateTarget = null;
        activeSpectateViewSource = null;
    }

    private static bool IsTargetDead(NetworkObject target)
    {
        if (target == null)
        {
            return true;
        }

        OxygenSystem oxygen = target.GetComponent<OxygenSystem>();
        return oxygen != null && oxygen.IsDead();
    }

    private void ShowBlackOverlay(float alpha)
    {
        EnsureOverlay();
        if (overlayCanvas != null)
        {
            overlayCanvas.gameObject.SetActive(true);
        }

        if (blackOverlay != null)
        {
            Color color = blackOverlay.color;
            color.a = alpha;
            blackOverlay.color = color;
        }
    }

    private IEnumerator FadeBlackOverlay(float targetAlpha)
    {
        EnsureOverlay();
        if (blackOverlay == null)
        {
            yield break;
        }

        float startAlpha = blackOverlay.color.a;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = fadeDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / fadeDuration);
            Color color = blackOverlay.color;
            color.a = Mathf.Lerp(startAlpha, targetAlpha, t);
            blackOverlay.color = color;
            yield return null;
        }
    }

    private void HideBlackOverlay()
    {
        if (overlayCanvas != null)
        {
            overlayCanvas.gameObject.SetActive(false);
        }
    }

    private void EnsureOverlay()
    {
        if (overlayCanvas != null && blackOverlay != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("DeathSpectatorOverlay");
        overlayCanvas = canvasObject.AddComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = 20000;
        canvasObject.AddComponent<CanvasScaler>();

        GameObject imageObject = new GameObject("BlackScreen");
        imageObject.transform.SetParent(canvasObject.transform, false);
        blackOverlay = imageObject.AddComponent<Image>();
        blackOverlay.color = Color.black;

        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Transform FindChildRecursive(Transform parent, string childName)
    {
        if (parent == null)
        {
            return null;
        }

        foreach (Transform child in parent)
        {
            if (child.name == childName)
            {
                return child;
            }

            Transform found = FindChildRecursive(child, childName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private Transform FindSpectatorCameraTransform(Transform root)
    {
        Transform exact = FindChildRecursive(root, spectatorCameraName);
        if (exact != null)
        {
            return exact;
        }

        Transform currentPrefabSpelling = FindChildRecursive(root, "Specator cam");
        if (currentPrefabSpelling != null)
        {
            return currentPrefabSpelling;
        }

        return FindChildByNameParts(root, "spec", "cam");
    }

    private static Transform FindChildByNameParts(Transform parent, string firstPart, string secondPart)
    {
        if (parent == null)
        {
            return null;
        }

        foreach (Transform child in parent)
        {
            string lowerName = child.name.ToLowerInvariant();
            if (lowerName.Contains(firstPart) && lowerName.Contains(secondPart))
            {
                return child;
            }

            Transform found = FindChildByNameParts(child, firstPart, secondPart);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
