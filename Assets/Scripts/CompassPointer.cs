using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class CompassPointer : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The object to point towards (e.g., Powerbox)")]
    public Transform targetObject;

    [Header("Player Reference")]
    [Tooltip("Player transform to calculate direction from")]
    public Transform playerTransform;

    [Header("UI References")]
    [Tooltip("The compass needle/arrow image that rotates")]
    public RectTransform compassNeedle;

    [Tooltip("Parent canvas group for fading in/out")]
    public CanvasGroup compassCanvasGroup;

    [Tooltip("Text label showing what the compass is pointing to")]
    public TextMeshProUGUI compassLabel;

    [Header("Visibility Settings")]
    [Tooltip("Show compass only when on surface (not in bunker)")]
    public bool onlyShowOnSurface = true;

    [Header("Fade Settings")]
    [Tooltip("Fade speed when showing/hiding")]
    public float fadeSpeed = 5f;

    private OxygenSystem oxygenSystem;
    private bool shouldShow = false;
    private Transform defaultTarget;
    private Transform overrideTarget;
    private string defaultLabel = "POWER";
    private string overrideLabel = "";
    private float repairPercentage = 0f;
    private bool overrideRequiresSurface = false;
    private readonly Collider[] bunkerCheckResults = new Collider[10];

    void Start()
    {
        RefreshPlayerReference();

        RefreshPowerTarget();

        defaultTarget = targetObject;

        if (compassLabel == null)
        {
            compassLabel = GetComponentInChildren<TextMeshProUGUI>();
        }

        if (compassLabel != null)
        {
            defaultLabel = compassLabel.text;
        }

        if (compassCanvasGroup != null)
        {
            compassCanvasGroup.alpha = 0f;
        }

        Debug.Log("<color=green>[Compass]</color> Compass system initialized");
    }

    public void SetTarget(Transform newTarget, string label = "", bool requireOutsideBunker = false)
    {
        bool targetChanged = overrideTarget != newTarget;
        bool labelChanged = overrideLabel != label;
        bool visibilityChanged = overrideRequiresSurface != requireOutsideBunker;
        overrideTarget = newTarget;
        overrideLabel = label;
        overrideRequiresSurface = requireOutsideBunker;

        if (targetChanged || labelChanged)
        {
            repairPercentage = 0f;
        }
        UpdateLabel();

        if (targetChanged || labelChanged || visibilityChanged)
        {
            Debug.Log($"<color=cyan>[Compass]</color> Target override set to: {(newTarget != null ? newTarget.name : "null")} with label: {label} (outside bunker only: {requireOutsideBunker})");
        }
    }

    public bool HasTargetOverride(Transform target, string label = "")
    {
        if (overrideTarget != target || overrideTarget == null)
        {
            return false;
        }

        return string.IsNullOrEmpty(label) || overrideLabel == label;
    }

    public void ClearTargetOverride()
    {
        overrideTarget = null;
        overrideLabel = "";
        overrideRequiresSurface = false;
        repairPercentage = 0f;
        UpdateLabel();
        Debug.Log("<color=cyan>[Compass]</color> Target override cleared, returning to default target");
    }

    public void UpdateRepairPercentage(float percentage)
    {
        repairPercentage = Mathf.Clamp01(percentage);
        UpdateLabel();
    }

    void Update()
    {
        RefreshPlayerReference();
        RefreshPowerTarget();
        UpdateVisibility();
        UpdateCompassRotation();
        UpdateLabel();
        UpdateFade();
    }

    void UpdateVisibility()
    {
        if (overrideTarget != null && overrideRequiresSurface)
        {
            shouldShow = IsLocalPlayerOutsideBunker();
            return;
        }
        if (onlyShowOnSurface && oxygenSystem != null)
        {
            shouldShow = !oxygenSystem.IsInBunker();
        }
        else
        {
            shouldShow = !onlyShowOnSurface;
        }
    }

    void UpdateCompassRotation()
    {
        if (compassNeedle == null || playerTransform == null)
            return;

        Transform activeTarget = GetActiveTarget();
        
        if (activeTarget == null)
            return;

        Vector3 directionToTarget = activeTarget.position - playerTransform.position;
        directionToTarget.y = 0;

        if (directionToTarget.magnitude > 0.01f)
        {
            float angleToTarget = Mathf.Atan2(directionToTarget.x, directionToTarget.z) * Mathf.Rad2Deg;
            float playerRotation = playerTransform.eulerAngles.y;
            float relativeAngle = angleToTarget - playerRotation;

            compassNeedle.localRotation = Quaternion.Euler(0, 0, -relativeAngle);
        }
    }

    void UpdateFade()
    {
        if (compassCanvasGroup == null)
            return;

        float targetAlpha = shouldShow ? 1f : 0f;
        compassCanvasGroup.alpha = Mathf.Lerp(compassCanvasGroup.alpha, targetAlpha, fadeSpeed * Time.deltaTime);
    }

    void UpdateLabel()
    {
        if (compassLabel == null)
            return;

        if (overrideTarget != null && !string.IsNullOrEmpty(overrideLabel))
        {
            if (repairPercentage > 0f)
            {
                compassLabel.text = $"{overrideLabel}\n{(repairPercentage * 100f):F0}%";
            }
            else
            {
                compassLabel.text = overrideLabel;
            }
        }
        else if (IsPowerOutageActive())
        {
            compassLabel.text = "POWER";
        }
        else
        {
            compassLabel.text = defaultLabel;
        }
    }

    private Transform GetActiveTarget()
    {
        if (overrideTarget != null)
        {
            return overrideTarget;
        }

        if (IsPowerOutageActive() && PowerSystem.Instance.powerBoxTransform != null)
        {
            return PowerSystem.Instance.powerBoxTransform;
        }

        return defaultTarget;
    }

    private bool IsPowerOutageActive()
    {
        return PowerSystem.Instance != null && PowerSystem.Instance.IsPowerOut;
    }

    private void RefreshPlayerReference()
    {
        Transform nextPlayer = null;

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening &&
            NetworkManager.Singleton.LocalClient != null &&
            NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            nextPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.transform;
        }
        else
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                nextPlayer = player.transform;
            }
        }

        if (nextPlayer == null || nextPlayer == playerTransform)
        {
            return;
        }

        playerTransform = nextPlayer;
        oxygenSystem = playerTransform.GetComponent<OxygenSystem>();

        if (oxygenSystem == null)
        {
            Debug.LogWarning("<color=yellow>[Compass]</color> OxygenSystem not found on local player. Hiding surface-only compass until a valid player is found.");
        }
        else
        {
            Debug.Log($"<color=cyan>[Compass]</color> Tracking player: {playerTransform.name}");
        }
    }

    private void RefreshPowerTarget()
    {
        Transform powerTarget = PowerSystem.Instance != null ? PowerSystem.Instance.powerBoxTransform : null;

        if (powerTarget == null)
        {
            GameObject powerbox = GameObject.Find("Powerbox");
            if (powerbox != null)
            {
                powerTarget = powerbox.transform;
            }
        }

        if (powerTarget == null)
        {
            return;
        }

        if (targetObject != powerTarget || defaultTarget != powerTarget)
        {
            targetObject = powerTarget;
            defaultTarget = powerTarget;
            Debug.Log($"<color=cyan>[Compass]</color> Power target set to: {powerTarget.name} at {powerTarget.position}");
        }
    }

    private bool IsLocalPlayerOutsideBunker()
    {
        if (oxygenSystem != null)
        {
            return !oxygenSystem.IsInBunker();
        }

        if (playerTransform == null)
        {
            return false;
        }

        int hitCount = Physics.OverlapSphereNonAlloc(playerTransform.position, 1f, bunkerCheckResults);
        for (int i = 0; i < hitCount; i++)
        {
            if (bunkerCheckResults[i] != null && bunkerCheckResults[i].CompareTag("Bunker"))
            {
                return false;
            }
        }

        return true;
    }
}
