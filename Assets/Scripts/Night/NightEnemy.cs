using System.Collections.Generic;
using UnityEngine;

// 敵: 群れ型(moth)・硬い型(brute)・遠距離型(shooter)。通路グラフに沿ってプレイヤーを追う。
// 光(懐中電灯・閃光・点灯したランタン)を浴び続けると lightHp が減って倒れる。
public class NightEnemy
{
    public enum Kind { Moth, Brute, Shooter }

    public struct Config
    {
        public float speed, contactDps, lightHp, drain, regen, scale;
        public Color color;
        public bool ranged;
    }

    public static Config ConfigOf(Kind k)
    {
        switch (k)
        {
            case Kind.Brute: return new Config { speed = 2.05f, contactDps = 24, lightHp = 115, drain = 38, regen = 20, color = NightMeshes.Hex(0x8a3b3b), scale = 1f };
            case Kind.Shooter: return new Config { speed = 1.8f, contactDps = 8, lightHp = 60, drain = 35, regen = 15, color = NightMeshes.Hex(0xff8c00), scale = 0.8f, ranged = true };
            default: return new Config { speed = 3.5f, contactDps = 11, lightHp = 34, drain = 44, regen = 16, color = NightMeshes.Hex(0x8a4b8a), scale = 0.55f };
        }
    }

    public readonly Kind kind;
    public readonly Config cfg;
    public readonly GameObject go;
    readonly Material mat;

    public float lightHp, lightHpMax;
    public Vector3 knock;
    public bool dying;
    public float dieTimer;
    public List<Vector3> path;
    public float navTimer, shootTimer;

    public Vector3 Position { get => go.transform.position; set => go.transform.position = value; }

    public NightEnemy(Kind kind, Vector3 pos)
    {
        this.kind = kind;
        cfg = ConfigOf(kind);
        lightHp = lightHpMax = cfg.lightHp;

        go = new GameObject("Enemy_" + kind);
        go.transform.position = pos;
        float sc = cfg.scale;

        mat = EmissiveMaterialFactory.Create(cfg.color);
        mat.SetFloat("_Glossiness", 0.1f);
        var blob = NightMeshes.MeshObject("Blob", NightMeshes.Icosahedron(), mat, go.transform);
        blob.transform.localScale = new Vector3(0.9f, 0.72f, 1.17f) * sc;
        blob.transform.localPosition = new Vector3(0, 0.4f * sc, 0);

        var eyeMat = EmissiveMaterialFactory.Create(NightMeshes.Hex(0xffe066));
        eyeMat.SetColor("_EmissionColor", NightMeshes.Hex(0xffe066) * 1.4f);
        for (int s = -1; s <= 1; s += 2)
        {
            var eye = NightMeshes.Primitive(PrimitiveType.Sphere, go.transform, eyeMat);
            eye.transform.localScale = Vector3.one * 0.14f * sc;
            eye.transform.localPosition = new Vector3(0.16f * s * sc, 0.55f * sc, 0.55f * sc);
        }
    }

    // 光を受けている間は即座に光り、ダメージが溜まるほど赤みを帯びて強く光る。外れた後は残りダメージ分だけ薄く残光。
    // _EmissionColor(発光)はスタンドアロンビルドで描画に反映されないことが確認できたため、
    // 代わりに確実に描画される _Color(通常の色)を直接、明るい黄色〜赤へブレンドする方式にしている。
    public void ApplyGlow(bool lit)
    {
        float dmg = 1f - Mathf.Clamp01(lightHp / lightHpMax);
        Color litColor = Color.Lerp(new Color(1f, 0.95f, 0.55f), new Color(1f, 0.35f, 0.1f), dmg);
        float blend = lit ? Mathf.Lerp(0.75f, 1f, dmg) : dmg * 0.35f;
        mat.color = Color.Lerp(cfg.color, litColor, blend);
    }

    public void Destroy() => Object.Destroy(go);
}

public class NightBullet
{
    public Transform t;
    public Vector3 dir;
    public float speed = 8f, life = 3f;
}
