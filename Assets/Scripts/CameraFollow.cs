using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float smoothTime = 0.15f;
    Vector3 velocity;

    void LateUpdate()
    {
        if (target == null) return;
        Vector3 dest = new Vector3(target.position.x, target.position.y, transform.position.z);
        transform.position = Vector3.SmoothDamp(transform.position, dest, ref velocity, smoothTime);
    }
}
