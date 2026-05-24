using UnityEngine;

public class MoveCamera : MonoBehaviour {

    public Transform player;

    void LateUpdate() {
        if (player != null)
        {
            transform.position = player.position;
        }
    }
}
