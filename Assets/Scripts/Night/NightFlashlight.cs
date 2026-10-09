using System.Collections.Generic;
using UnityEngine;

// 協力プレイヤー役の灯り。QRで接続したスマホは何人でも同時に参加でき、それぞれが自分の指/傾きで
// 狙った場所を同時に照らせる(接続中の人数ぶん光が増える)。誰も繋いでいなければマウスを使う。
// 全員が常にフル半径の光を持つとバランスが崩れるため、参加人数が増えるほどひとり分の光は小さくなる。
// 照準は地形(通路・広場・ランタン・花)へのレイキャストで決め、当たらなければプレイヤーの高さの水平面で受ける。
public class NightFlashlight : MonoBehaviour
{
    const float BaseLightRadius = 3.0f, MinLightRadius = 1.1f;
    const float BaseDeadZone = 1.5f; // プレイヤーからこの距離より近い敵には光が効かない

    public float LightRadius { get; private set; } = BaseLightRadius;
    public float DeadZone { get; private set; } = BaseDeadZone;

    // 現在照らされている全地点(接続中のスマホの人数ぶん。誰も居なければマウスの1点)
    public List<Vector3> Targets { get; } = new List<Vector3>();
    // Targets と同じ並びで、その光を出しているスマホのid(マウス・自動テストの光は null)。倒した敵をスマホごとに数えるため
    public List<string> TargetOwners { get; } = new List<string>();
    public Vector3 Target => Targets.Count > 0 ? Targets[0] : Vector3.zero;
    public bool HasPhoneInput { get; private set; }
    public Vector2? DebugAim; // 自動テスト用: 指定するとマウス/スマホの代わりにこの位置(上端原点のビューポート座標)を照らす

    readonly List<Light> spots = new List<Light>();
    readonly List<Transform> aimRings = new List<Transform>();

    Light MakeSpot()
    {
        var go = new GameObject("FlashlightSpot");
        go.transform.SetParent(transform, false);
        var l = go.AddComponent<Light>();
        l.type = LightType.Spot;
        l.color = NightMeshes.Hex(0xfff2c0);
        l.range = 20f;
        l.shadows = LightShadows.Soft;
        return l;
    }

    Transform MakeRing()
    {
        var ring = NightMeshes.MeshObject("AimRing", NightMeshes.Ring(2.82f, 2.94f, 64),
            NightMeshes.Unlit(NightMeshes.Hex(0xffe9a1, 0.32f)), null);
        ring.SetActive(false);
        return ring.transform;
    }

    public void Aim(Camera cam, NightMap map, float playerY)
    {
        List<PointerServer.PointerSample> aims = ReadAimPoints();
        // 人数が増えるほど緩やかに(sqrtで)縮小し、下限でクリップする
        float scale = Mathf.Max(MinLightRadius / BaseLightRadius, 1f / Mathf.Sqrt(aims.Count));
        LightRadius = BaseLightRadius * scale;
        DeadZone = BaseDeadZone * Mathf.Clamp(scale, 0.6f, 1f);

        while (spots.Count < aims.Count) spots.Add(MakeSpot());
        while (aimRings.Count < aims.Count) aimRings.Add(MakeRing());
        for (int i = 0; i < spots.Count; i++) spots[i].enabled = i < aims.Count;
        for (int i = 0; i < aimRings.Count; i++) aimRings[i].gameObject.SetActive(i < aims.Count);

        Targets.Clear();
        TargetOwners.Clear();
        for (int i = 0; i < aims.Count; i++)
        {
            Vector2 aim = aims[i].pos;
            Color color = aims[i].color;
            Ray ray = cam.ViewportPointToRay(new Vector3(aim.x, 1f - aim.y, 0f));
            Vector3 target;
            if (Physics.Raycast(ray, out RaycastHit hit, 400f)) target = hit.point;
            else if (new Plane(Vector3.up, new Vector3(0, playerY, 0)).Raycast(ray, out float d)) target = ray.GetPoint(d);
            else target = new Vector3(0, playerY, 0);
            Targets.Add(target);
            TargetOwners.Add(aims[i].id);

            var spot = spots[i];
            spot.color = color;
            spot.intensity = (7.5f * Mathf.Min(1.6f, 0.8f + 1.25f * 0.2f)) * Mathf.Clamp(scale, 0.5f, 1f);
            spot.spotAngle = 2f * Mathf.Min(Mathf.PI / 3.2f, Mathf.PI / 6.3f * (LightRadius / 2.4f)) * Mathf.Rad2Deg;
            spot.innerSpotAngle = spot.spotAngle * 0.5f;
            spot.transform.position = target + Vector3.up * 9f;
            spot.transform.LookAt(target);

            var ring = aimRings[i];
            ring.GetComponent<MeshRenderer>().sharedMaterial.color = new Color(color.r, color.g, color.b, 0.4f);
            ring.localScale = Vector3.one * Mathf.Clamp(scale, 0.4f, 1f);
            ring.position = new Vector3(target.x, map.SurfaceAt(target.x, target.z) + 0.15f, target.z);
        }
    }

    public void HideRing() { foreach (var r in aimRings) if (r != null) r.gameObject.SetActive(false); }

    static readonly Color MouseColor = NightMeshes.Hex(0xfff2c0); // マウス操作時は従来通りの暖色

    // 接続中の各スマホ(何人でも)のビューポート座標+割り当て色を返す。誰も繋いでいなければマウス1点。
    List<PointerServer.PointerSample> ReadAimPoints()
    {
        var result = new List<PointerServer.PointerSample>();
        if (DebugAim.HasValue)
        {
            result.Add(new PointerServer.PointerSample { pos = DebugAim.Value, color = MouseColor });
            HasPhoneInput = false;
            return result;
        }
        if (PointerServer.Instance != null)
        {
            var samples = PointerServer.Instance.GetActiveSamples();
            if (samples.Count > 0)
            {
                HasPhoneInput = true;
                result.AddRange(samples);
                return result;
            }
        }
        HasPhoneInput = false;
        result.Add(new PointerServer.PointerSample
        {
            pos = new Vector2(Input.mousePosition.x / Screen.width, 1f - Input.mousePosition.y / Screen.height),
            color = MouseColor
        });
        return result;
    }
}
