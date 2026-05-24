using UnityEngine;

public class EnemyJumpscareTrigger : MonoBehaviour
{
    [Header("Jumpscare Settings")]
    [Tooltip("Jumpscare image to show when player touches this enemy")]
    public Sprite jumpscareSprite;

    [Tooltip("Optional message to display")]
    public string jumpscareMessage = "YOU DIED";

    [Tooltip("Custom jumpscare sound (optional, uses default if null)")]
    public AudioClip customJumpscareSound;

    [Header("Detection Settings")]
    [Tooltip("Use trigger collider instead of collision")]
    public bool useTrigger = false;

    [Tooltip("Only trigger jumpscare once")]
    public bool triggerOnce = true;

    private bool hasTriggered = false;

    void OnCollisionEnter(Collision collision)
    {
        if (useTrigger) return;

        if (collision.gameObject.CompareTag("Player"))
        {
            TriggerJumpscare();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!useTrigger) return;

        if (other.CompareTag("Player"))
        {
            TriggerJumpscare();
        }
    }

    void TriggerJumpscare()
    {
        if (triggerOnce && hasTriggered) return;

        hasTriggered = true;

        if (JumpscareSystem.Instance != null)
        {
            Debug.Log($"<color=red>[Enemy Jumpscare]</color> {gameObject.name} triggered jumpscare!");
            JumpscareSystem.Instance.TriggerJumpscare(jumpscareSprite, jumpscareMessage, customJumpscareSound);
        }
        else
        {
            Debug.LogError("<color=red>[Enemy Jumpscare]</color> JumpscareSystem not found in scene!");
        }
    }
}
