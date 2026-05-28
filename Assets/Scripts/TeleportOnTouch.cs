using UnityEngine;
using Unity.Netcode;

public class TeleportOnTouch : MonoBehaviour
{
    [Header("Assign your player prefab here")]
    [Tooltip("Optional legacy reference. Multiplayer uses the collider that entered the trigger.")]
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
        if (teleportDestination == null)
            return;

        Vector3 position = transform.position;
        
        RaycastHit[] hitsAbove = Physics.SphereCastAll(position, detectionRadius, Vector3.up, detectionHeight, playerLayer);
        RaycastHit[] hitsBelow = Physics.SphereCastAll(position, detectionRadius, Vector3.down, detectionHeight, playerLayer);

        foreach (RaycastHit hit in hitsAbove)
        {
            if (TryGetTeleportTarget(hit.collider, out var player))
            {
                TeleportPlayer(player);
                return;
            }
        }

        foreach (RaycastHit hit in hitsBelow)
        {
            if (TryGetTeleportTarget(hit.collider, out var player))
            {
                TeleportPlayer(player);
                return;
            }
        }

        // Legacy fallback path for existing singleplayer setups.
        if (playerPrefab != null)
        {
            float distanceToPlayer = Vector3.Distance(position, playerPrefab.transform.position);
            if (distanceToPlayer <= detectionRadius)
            {
                TeleportPlayer(playerPrefab);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (TryGetTeleportTarget(other, out var player))
        {
            TeleportPlayer(player);
        }
        else if (playerPrefab != null && other.gameObject == playerPrefab)
        {
            TeleportPlayer(playerPrefab);
        }
    }

    private bool TryGetTeleportTarget(Collider other, out GameObject target)
    {
        target = null;
        if (other == null)
        {
            return false;
        }

        var player = other.GetComponentInParent<PlayerController>();
        if (player == null)
        {
            return false;
        }

        // In multiplayer, each client should only move its own local player.
        if (player.TryGetComponent<NetworkObject>(out var netObj) && netObj != null && netObj.IsSpawned)
        {
            if (!netObj.IsOwner)
            {
                return false;
            }
        }

        target = player.gameObject;
        return true;
    }

    private void TeleportPlayer(GameObject targetPlayer)
    {
        if (Time.time - lastTeleportTime < teleportCooldown)
        {
            return;
        }

        if (teleportDestination != null && targetPlayer != null)
        {
            CharacterController characterController = targetPlayer.GetComponent<CharacterController>();
            
            if (characterController != null)
            {
                characterController.enabled = false;
            }
            
            targetPlayer.transform.position = teleportDestination.position;
            
            if (characterController != null)
            {
                characterController.enabled = true;
            }
            
            SetPlayerFlashlight(targetPlayer, enableFlashlightOnTeleport);
            
            lastTeleportTime = Time.time;
            Debug.Log($"{targetPlayer.name} teleported to {teleportDestination.name}");
        }
        else
        {
            Debug.LogWarning("Teleport destination not set!");
        }
    }

    private void SetPlayerFlashlight(GameObject targetPlayer, bool enabledState)
    {
        Light targetFlashlight = null;

        if (targetPlayer != null)
        {
            Light[] playerLights = targetPlayer.GetComponentsInChildren<Light>(true);
            foreach (Light playerLight in playerLights)
            {
                if (playerLight != null && playerLight.type == LightType.Spot)
                {
                    targetFlashlight = playerLight;
                    break;
                }
            }

            if (targetFlashlight == null && playerLights.Length > 0)
            {
                targetFlashlight = playerLights[0];
            }
        }

        if (targetFlashlight == null)
        {
            targetFlashlight = flashlight;
        }

        if (targetFlashlight == null)
        {
            return;
        }

        targetFlashlight.gameObject.SetActive(true);
        targetFlashlight.enabled = enabledState;
        Debug.Log($"<color=yellow>[Teleport]</color> {targetFlashlight.name} {(enabledState ? "enabled" : "disabled")} on teleport");
    }
}
