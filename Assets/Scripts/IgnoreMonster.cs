using UnityEngine;

/// <summary>
/// Static end-scene prop. No movement, no kill logic, no networking.
/// Exists solely so RadarSystem and other callers compile without errors.
/// </summary>
public class IgnoreMonster : MonoBehaviour
{
    public static IgnoreMonster Instance { get; private set; }

    [Header("Radar Deception")]
    [Tooltip("Fake distance to show on radar (constant)")]
    public float fakeRadarDistance = 75f;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>Always false — this is a static prop, never an active threat.</summary>
    public bool IsCurrentlyPresent() => false;

    /// <summary>Radar deception distance (unused while IsCurrentlyPresent is false).</summary>
    public float GetFakeDistance() => fakeRadarDistance;
}
