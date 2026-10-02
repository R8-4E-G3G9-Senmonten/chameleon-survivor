using UnityEngine;

// 協力プレイヤー役の灯り。スマホ(QRで接続した PointerServer 経由のタッチ/傾き)が繋がっていればそちらを、
// 未接続ならマウスを使う。照準は地形(通路・広場・ランタン・花)へのレイキャストで決め、
// 当たらなければプレイヤーの高さの水平面で受ける(階段や高台の上でも狙った場所を照らせる)。
public class NightFlashlight : MonoBehaviour
{
    public const float LightRadius = 3.0f;
    public const float DeadZone = 1.5f; // プレイヤーからこの距離より近い敵には光が効かない

    public Vector3 Target { get; private set; }
    public bool HasPhoneInput { get; private set; }
    public Vector2? DebugAim; // 自動テスト用: 指定するとマウス/スマホの代わりにこの位置(上端原点のビューポート座標)を照らす

    Light spot;
    Transform aimRing;

    void Awake()
    {
        spot = gameObject.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.color = NightMeshes.Hex(0xfff2c0);
        // 周囲をほぼ真っ暗にした分、懐中電灯そのものは強め・広めにして「光が当たった所だけ見える」感じにする
        spot.intensity = 7.5f * Mathf.Min(1.6f, 0.8f + 1.25f * 0.2f);
        spot.range = 20f;
        spot.spotAngle = 2f * Mathf.Min(Mathf.PI / 3.2f, Mathf.PI / 6.3f * (LightRadius / 2.4f)) * Mathf.Rad2Deg;
        spot.innerSpotAngle = spot.spotAngle * 0.5f;
        spot.shadows = LightShadows.Soft;
    }

    void Start()
    {
        var ring = NightMeshes.MeshObject("AimRing", NightMeshes.Ring(2.82f, 2.94f, 64),
            NightMeshes.Unlit(NightMeshes.Hex(0xffe9a1, 0.32f)), null);
        aimRing = ring.transform;
        aimRing.gameObject.SetActive(false);
    }

    public void Aim(Camera cam, NightMap map, float playerY)
    {
        Vector2 aim = ReadAimPoint();
        Ray ray = cam.ViewportPointToRay(new Vector3(aim.x, 1f - aim.y, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, 400f)) Target = hit.point;
        else if (new Plane(Vector3.up, new Vector3(0, playerY, 0)).Raycast(ray, out float d)) Target = ray.GetPoint(d);

        transform.position = Target + Vector3.up * 9f;
        transform.LookAt(Target);
        if (aimRing != null)
        {
            aimRing.gameObject.SetActive(true);
            aimRing.position = new Vector3(Target.x, map.SurfaceAt(Target.x, Target.z) + 0.035f, Target.z);
        }
    }

    public void HideRing() { if (aimRing != null) aimRing.gameObject.SetActive(false); }

    // 0..1 のビューポート座標(上端原点)
    Vector2 ReadAimPoint()
    {
        if (DebugAim.HasValue) return DebugAim.Value;
        if (PointerServer.Instance != null)
        {
            var points = PointerServer.Instance.GetActivePointers();
            if (points.Count > 0) { HasPhoneInput = true; return points[0]; }
        }
        HasPhoneInput = false;
        return new Vector2(Input.mousePosition.x / Screen.width, 1f - Input.mousePosition.y / Screen.height);
    }
}
