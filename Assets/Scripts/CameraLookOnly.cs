using UnityEngine;

/// <summary>
/// Allows the player to look around with the mouse but blocks all movement.
/// Used by BunkerEndingSequence to give a cinematic look-only experience.
/// </summary>
public class CameraLookOnly : MonoBehaviour
{
    [Tooltip("The player camera transform to rotate")]
    public Transform playerCamera;

    [Tooltip("Mouse sensitivity - matched from PlayerController")]
    public float mouseSensitivity = 2f;

    [Tooltip("Maximum vertical look angle in degrees")]
    public float verticalLookLimit = 80f;

    private float verticalRotation = 0f;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Capture current vertical rotation from the camera to avoid a snap.
        if (playerCamera != null)
        {
            verticalRotation = playerCamera.localEulerAngles.x;
            if (verticalRotation > 180f) verticalRotation -= 360f;
        }
    }

    void LateUpdate()
    {
        if (playerCamera == null) return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // Horizontal: rotate the player body.
        transform.Rotate(Vector3.up * mouseX);

        // Vertical: tilt the camera.
        verticalRotation -= mouseY;
        verticalRotation = Mathf.Clamp(verticalRotation, -verticalLookLimit, verticalLookLimit);
        playerCamera.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
    }
}
