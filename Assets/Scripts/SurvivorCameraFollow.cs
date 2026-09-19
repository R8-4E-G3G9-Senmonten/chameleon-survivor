using UnityEngine;

// プレイヤーを常に画面中心へロックオンするのではなく、一定の"遊び"(デッドゾーン)の
// 中でならプレイヤーが自由に動けるようにするカメラ追従。HTMLモックアップと同じ挙動。
public class SurvivorCameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0, 8, 8);
    public float deadzone = 0f; // 0 = 常にプレイヤーを画面中心に置く
    public float followLerp = 0f; // 0 = 遅れなしでぴったり追従

    Vector3 focus;
    bool initialized;

    void LateUpdate()
    {
        if (target == null) return;
        if (!initialized)
        {
            focus = target.position;
            initialized = true;
        }

        Vector2 diff = new Vector2(target.position.x - focus.x, target.position.z - focus.z);
        float dist = diff.magnitude;
        if (dist > deadzone)
        {
            float pull = dist - deadzone;
            diff = diff.normalized * pull;
            focus.x += diff.x;
            focus.z += diff.y;
        }

        Vector3 desired = focus + offset;
        transform.position = followLerp > 0f
            ? Vector3.Lerp(transform.position, desired, Mathf.Min(1f, Time.deltaTime * followLerp))
            : desired;
        transform.LookAt(new Vector3(focus.x, 0.6f, focus.z));
    }
}
