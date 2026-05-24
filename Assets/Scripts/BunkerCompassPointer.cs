using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A directional arrow UI element that points the player toward the bunker,
/// mirroring the existing CompassPointer behavior but dedicated to the bunker.
/// Only visible when the player is outside the bunker.
/// </summary>
public class BunkerCompassPointer : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The bunker Transform to point toward (e.g. the BunkerZone or entrance cube)")]
    public Transform bunkerTarget;

    [Header("Player")]
    [Tooltip("Player transform. Auto-found if left empty.")]
    public Transform playerTransform;

    [Header("UI References")]
    [Tooltip("The RectTransform of the arrow/needle image that will be rotated")]
    public RectTransform compassNeedle;

    [Tooltip("CanvasGroup used for fading in and out")]
    public CanvasGroup compassCanvasGroup;

    [Tooltip("Optional label to show (e.g. 'BUNKER')")]
    public TextMeshProUGUI compassLabel;

    [Header("Visibility")]
    [Tooltip("Hide the bunker arrow when the player is inside the bunker")]
    public bool hideInsideBunker = true;

    [Tooltip("If assigned, uses OxygenSystem.IsInBunker() to detect bunker state")]
    public OxygenSystem oxygenSystem;

    [Header("Fade")]
    [Tooltip("Alpha fade speed")]
    public float fadeSpeed = 5f;

    [Header("Label Text")]
    public string labelText = "BUNKER";

    private bool shouldShow = true;

    void Start()
    {
        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerTransform = player.transform;
            }
            else
            {
                Debug.LogError("<color=red>[BunkerCompass]</color> No player found with tag 'Player'!");
            }
        }

        if (bunkerTarget == null)
        {
            // Try auto-finding the BunkerZone.
            GameObject zone = GameObject.FindGameObjectWithTag("Bunker");
            if (zone != null)
            {
                bunkerTarget = zone.transform;
                Debug.Log($"<color=cyan>[BunkerCompass]</color> Auto-found bunker target: {zone.name}");
            }
            else
            {
                Debug.LogError("<color=red>[BunkerCompass]</color> No bunker target assigned and no 'Bunker' tagged object found!");
            }
        }

        if (oxygenSystem == null && playerTransform != null)
        {
            oxygenSystem = playerTransform.GetComponent<OxygenSystem>();
        }

        if (compassLabel != null)
        {
            compassLabel.text = labelText;
        }

        if (compassCanvasGroup != null)
        {
            compassCanvasGroup.alpha = 0f;
        }
    }

    void Update()
    {
        UpdateVisibility();
        UpdateNeedleRotation();
        UpdateFade();
    }

    void UpdateVisibility()
    {
        if (!hideInsideBunker)
        {
            shouldShow = true;
            return;
        }

        if (oxygenSystem != null)
        {
            shouldShow = !oxygenSystem.IsInBunker();
        }
        else
        {
            shouldShow = true;
        }
    }

    void UpdateNeedleRotation()
    {
        if (compassNeedle == null || playerTransform == null || bunkerTarget == null) return;

        Vector3 direction = bunkerTarget.position - playerTransform.position;
        direction.y = 0;

        if (direction.sqrMagnitude < 0.01f) return;

        float angleToTarget = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        float playerAngle = playerTransform.eulerAngles.y;
        float relativeAngle = angleToTarget - playerAngle;

        compassNeedle.localRotation = Quaternion.Euler(0, 0, -relativeAngle);
    }

    void UpdateFade()
    {
        if (compassCanvasGroup == null) return;

        float target = shouldShow ? 1f : 0f;
        compassCanvasGroup.alpha = Mathf.Lerp(compassCanvasGroup.alpha, target, fadeSpeed * Time.deltaTime);
    }
}
