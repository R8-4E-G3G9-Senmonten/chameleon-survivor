using UnityEngine;

// 協力プレイヤー(スマホ役)の懐中電灯。HTMLモックアップではマウス位置を地面(y=0)に
// 投影してスポットライトを向けていたのを再現しつつ、既存プロトタイプと同じ
// PointerServer(スマホのタッチ/傾きをQR経由でLAN越しに受信)を優先入力として使う。
// スマホが繋がっていなければマウス操作にフォールバックするので、
// 展示会場でスマホ役が来る前でも単独で動作確認できる。
public class FlashlightController : MonoBehaviour
{
    public static FlashlightController Instance { get; private set; }

    public float height = 9f;
    public Color lightColor = new Color(1f, 0.95f, 0.75f);
    public float intensity = 3.4f;
    public float range = 16f;
    public float spotAngle = 58f;

    Camera cam;
    readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

    public Vector3 GroundTarget { get; private set; }
    public bool HasPhoneInput { get; private set; }

    void Awake()
    {
        Instance = this;

        var light = gameObject.AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = lightColor;
        light.intensity = intensity;
        light.range = range;
        light.spotAngle = spotAngle;
        light.shadows = LightShadows.Soft;
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector2 aimTopOrigin = ReadAimPoint(); // (0,0)=画面左上, (1,1)=右下 (PointerServerと同じ規約)
        Ray ray = cam.ViewportPointToRay(new Vector3(aimTopOrigin.x, 1f - aimTopOrigin.y, 0f));
        if (groundPlane.Raycast(ray, out float dist))
        {
            GroundTarget = ray.GetPoint(dist);
        }

        transform.position = new Vector3(GroundTarget.x, height, GroundTarget.z);
        transform.LookAt(GroundTarget);
    }

    // スマホ(PointerServer)からの入力があればそれを使い、無ければマウス位置にフォールバックする。
    Vector2 ReadAimPoint()
    {
        if (PointerServer.Instance != null)
        {
            var points = PointerServer.Instance.GetActivePointers();
            if (points.Count > 0)
            {
                HasPhoneInput = true;
                return points[0];
            }
        }

        HasPhoneInput = false;
        return new Vector2(Input.mousePosition.x / Screen.width, 1f - Input.mousePosition.y / Screen.height);
    }
}
