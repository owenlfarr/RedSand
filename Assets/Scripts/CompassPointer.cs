using UnityEngine;
using UnityEngine.UI;
using TMPro;

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

    void Start()
    {
        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerTransform = player.transform;
                Debug.Log($"<color=cyan>[Compass]</color> Found player: {player.name}");
            }
            else
            {
                Debug.LogError("<color=red>[Compass]</color> No player found with tag 'Player'!");
            }
        }

        if (targetObject == null)
        {
            GameObject powerbox = GameObject.Find("Powerbox");
            if (powerbox != null)
            {
                targetObject = powerbox.transform;
                Debug.Log($"<color=cyan>[Compass]</color> Found target: Powerbox at {targetObject.position}");
            }
            else
            {
                Debug.LogError("<color=red>[Compass]</color> No target object found! Please assign the Powerbox.");
            }
        }

        defaultTarget = targetObject;

        if (compassLabel == null)
        {
            compassLabel = GetComponentInChildren<TextMeshProUGUI>();
        }

        if (compassLabel != null)
        {
            defaultLabel = compassLabel.text;
        }

        if (onlyShowOnSurface)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                oxygenSystem = player.GetComponent<OxygenSystem>();
                if (oxygenSystem == null)
                {
                    Debug.LogWarning("<color=yellow>[Compass]</color> OxygenSystem not found on player. Compass will always show.");
                }
            }
        }

        if (compassCanvasGroup != null)
        {
            compassCanvasGroup.alpha = 0f;
        }

        Debug.Log("<color=green>[Compass]</color> Compass system initialized");
    }

    public void SetTarget(Transform newTarget, string label = "")
    {
        overrideTarget = newTarget;
        overrideLabel = label;
        repairPercentage = 0f;
        UpdateLabel();
        Debug.Log($"<color=cyan>[Compass]</color> Target override set to: {(newTarget != null ? newTarget.name : "null")} with label: {label}");
    }

    public void ClearTargetOverride()
    {
        overrideTarget = null;
        overrideLabel = "";
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
        UpdateVisibility();
        UpdateCompassRotation();
        UpdateFade();
    }

    void UpdateVisibility()
    {
        if (onlyShowOnSurface && oxygenSystem != null)
        {
            shouldShow = !oxygenSystem.IsInBunker();
        }
        else
        {
            shouldShow = true;
        }
    }

    void UpdateCompassRotation()
    {
        if (compassNeedle == null || playerTransform == null)
            return;

        Transform activeTarget = overrideTarget != null ? overrideTarget : defaultTarget;
        
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
        else
        {
            compassLabel.text = defaultLabel;
        }
    }
}
