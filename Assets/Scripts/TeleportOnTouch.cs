using UnityEngine;

public class TeleportOnTouch : MonoBehaviour
{
    [Header("Assign your player prefab here")]
    public GameObject playerPrefab;

    [Header("Assign your teleport destination here")]
    public Transform teleportDestination;

    [Header("Floor Detection Settings")]
    public bool detectThroughFloors = true;
    public float detectionRadius = 2f;
    public float detectionHeight = 5f;
    public LayerMask playerLayer;

    [Header("Flashlight Control")]
    public Light flashlight;
    public bool enableFlashlightOnTeleport = true;

    [Header("Teleport Cooldown")]
    public float teleportCooldown = 2f;

    private const float CHECK_INTERVAL = 0.1f;
    private float lastCheckTime;
    private float lastTeleportTime = -999f;

    private void Update()
    {
        if (detectThroughFloors && Time.time - lastCheckTime > CHECK_INTERVAL)
        {
            lastCheckTime = Time.time;
            CheckForPlayerThroughFloors();
        }
    }

    private void CheckForPlayerThroughFloors()
    {
        if (playerPrefab == null || teleportDestination == null)
            return;

        Vector3 position = transform.position;
        
        RaycastHit[] hitsAbove = Physics.SphereCastAll(position, detectionRadius, Vector3.up, detectionHeight, playerLayer);
        RaycastHit[] hitsBelow = Physics.SphereCastAll(position, detectionRadius, Vector3.down, detectionHeight, playerLayer);

        foreach (RaycastHit hit in hitsAbove)
        {
            if (hit.collider.gameObject == playerPrefab)
            {
                TeleportPlayer();
                return;
            }
        }

        foreach (RaycastHit hit in hitsBelow)
        {
            if (hit.collider.gameObject == playerPrefab)
            {
                TeleportPlayer();
                return;
            }
        }

        float distanceToPlayer = Vector3.Distance(position, playerPrefab.transform.position);
        if (distanceToPlayer <= detectionRadius)
        {
            TeleportPlayer();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject == playerPrefab)
        {
            TeleportPlayer();
        }
    }

    private void TeleportPlayer()
    {
        if (Time.time - lastTeleportTime < teleportCooldown)
        {
            return;
        }

        if (teleportDestination != null && playerPrefab != null)
        {
            CharacterController characterController = playerPrefab.GetComponent<CharacterController>();
            
            if (characterController != null)
            {
                characterController.enabled = false;
            }
            
            playerPrefab.transform.position = teleportDestination.position;
            
            if (characterController != null)
            {
                characterController.enabled = true;
            }
            
            if (flashlight != null)
            {
                flashlight.gameObject.SetActive(true);
                flashlight.enabled = enableFlashlightOnTeleport;
                Debug.Log($"<color=yellow>[Teleport]</color> Flashlight {(enableFlashlightOnTeleport ? "enabled" : "disabled")} on teleport");
            }
            
            lastTeleportTime = Time.time;
            Debug.Log($"{playerPrefab.name} teleported to {teleportDestination.name}");
        }
        else
        {
            Debug.LogWarning("Teleport destination not set!");
        }
    }
}
