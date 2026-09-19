using UnityEngine;

// メインプレイヤー: 攻撃手段を持たず、逃げ回るだけの役。
// HTMLモックアップの velocity+damping 移動と、進行方向へ向く回転をそのまま再現する。
// 見た目(球の胴体+頭+目+尻尾)も自前で組み立てる(SpriteFactory と同じくインポート資産なし)。
public class SurvivorPlayerController : MonoBehaviour
{
    public float moveSpeed = 5.6f;
    public float damping = 8f;
    public float worldHalfExtent = 19f;

    Vector3 velocity;

    // アニメーション用(歩行の上下の揺れ・尻尾のスイング・呼吸・被ダメージで赤くなる)
    Transform bodyT, headT, tailT;
    Material bodyMat;
    Color bodyBaseColor;
    Quaternion tailBaseRot;
    float animTime;
    float hurtBlend;

    void Awake()
    {
        BuildVisuals();
    }

    void BuildVisuals()
    {
        bodyMat = new Material(Shader.Find("Standard"));
        bodyBaseColor = new Color(0.435f, 0.682f, 0.435f);
        bodyMat.color = bodyBaseColor;

        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = "Body";
        body.transform.SetParent(transform, false);
        body.transform.localScale = new Vector3(1f, 0.85f, 1.6f);
        body.transform.localPosition = new Vector3(0, 0.55f, 0);
        Destroy(body.GetComponent<Collider>());
        body.GetComponent<MeshRenderer>().material = bodyMat;
        bodyT = body.transform;

        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(transform, false);
        head.transform.localScale = Vector3.one * 0.6f;
        head.transform.localPosition = new Vector3(0, 0.7f, 0.75f);
        Destroy(head.GetComponent<Collider>());
        head.GetComponent<MeshRenderer>().material = bodyMat;
        headT = head.transform;

        var eyeMat = new Material(Shader.Find("Standard"));
        eyeMat.color = new Color(1f, 0.949f, 0.627f);
        eyeMat.EnableKeyword("_EMISSION");
        eyeMat.SetColor("_EmissionColor", new Color(0.333f, 0.267f, 0f) * 0.4f);
        CreateEye(new Vector3(-0.18f, 0.85f, 0.95f), eyeMat);
        CreateEye(new Vector3(0.18f, 0.85f, 0.95f), eyeMat);

        var tailGO = new GameObject("Tail");
        tailGO.transform.SetParent(transform, false);
        tailGO.transform.localPosition = new Vector3(0, 0.5f, -0.95f);
        tailGO.transform.localRotation = Quaternion.Euler(180f - Mathf.Rad2Deg * (Mathf.PI / 2.6f), 0, 0);
        var mf = tailGO.AddComponent<MeshFilter>();
        mf.mesh = PrimitiveMeshFactory.CreateCone(0.28f, 0.9f, 10);
        var mr = tailGO.AddComponent<MeshRenderer>();
        mr.material = bodyMat;
        tailT = tailGO.transform;
        tailBaseRot = tailT.localRotation;

        var glow = gameObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(0.165f, 0.290f, 0.227f);
        glow.intensity = 0.6f;
        glow.range = 2.4f;
    }

    void CreateEye(Vector3 localPos, Material mat)
    {
        var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eye.name = "Eye";
        eye.transform.SetParent(transform, false);
        eye.transform.localScale = Vector3.one * 0.16f;
        eye.transform.localPosition = localPos;
        Destroy(eye.GetComponent<Collider>());
        eye.GetComponent<MeshRenderer>().material = mat;
    }

    void Update()
    {
        if (SurvivorManager.Instance != null && SurvivorManager.Instance.State != SurvivorManager.GameState.Playing) return;

        Vector3 input = Vector3.zero;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) input.z -= 1;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) input.z += 1;
        // Unityは左手座標系で、カメラは+Z側から-Z方向を見ているため、画面右は-X方向になる。
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) input.x += 1;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) input.x -= 1;

        float dt = Time.deltaTime;
        if (input.sqrMagnitude > 0f)
        {
            input.Normalize();
            velocity.x += (input.x * moveSpeed - velocity.x) * Mathf.Min(1f, dt * 10f);
            velocity.z += (input.z * moveSpeed - velocity.z) * Mathf.Min(1f, dt * 10f);
            float angle = Mathf.Atan2(input.x, input.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, angle, 0), Mathf.Min(1f, dt * 10f));
        }
        else
        {
            velocity *= Mathf.Max(0f, 1f - damping * dt);
        }

        Vector3 pos = transform.position + velocity * dt;
        pos = SurvivorObstacle.Resolve(pos, 0.45f);
        pos.x = Mathf.Clamp(pos.x, -worldHalfExtent, worldHalfExtent);
        pos.z = Mathf.Clamp(pos.z, -worldHalfExtent, worldHalfExtent);
        transform.position = pos;

        Animate(dt);
    }

    void Animate(float dt)
    {
        animTime += dt;
        float moveAmount = Mathf.Clamp01(velocity.magnitude / moveSpeed);

        // 歩くと胴体と頭が小刻みに上下し、止まっている時はゆっくり呼吸する
        float bob = Mathf.Sin(animTime * 13f) * 0.05f * moveAmount;
        float breath = Mathf.Sin(animTime * 2f) * 0.02f * (1f - moveAmount);
        if (bodyT != null)
        {
            bodyT.localPosition = new Vector3(0, 0.55f + bob, 0);
            bodyT.localScale = new Vector3(1f, 0.85f + breath, 1.6f);
        }
        if (headT != null) headT.localPosition = new Vector3(0, 0.7f + bob * 1.4f, 0.75f);

        // 尻尾は待機中も少し揺れ、移動中は大きく振る
        if (tailT != null)
        {
            float sway = Mathf.Sin(animTime * (3f + 9f * moveAmount)) * (8f + 24f * moveAmount);
            tailT.localRotation = Quaternion.Euler(0, sway, 0) * tailBaseRot;
        }

        // 敵に触れている間は体が赤くなり、離れると元の緑へなめらかに戻る
        bool hurt = SurvivorManager.Instance != null && SurvivorManager.Instance.IsHurt;
        hurtBlend = Mathf.MoveTowards(hurtBlend, hurt ? 1f : 0f, dt * (hurt ? 12f : 4f));
        if (bodyMat != null) bodyMat.color = Color.Lerp(bodyBaseColor, new Color(0.95f, 0.25f, 0.2f), hurtBlend * 0.75f);
    }
}
