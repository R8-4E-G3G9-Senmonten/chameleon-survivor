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
    // プレイヤーが擬態して見失っている間: 近くをうろうろする目的地と、「？」「！」の表示用
    public Vector3 wanderTarget;
    public float wanderTimer, wanderPause, confused, foundTimer;
    public bool dying;
    public float dieTimer;
    public List<Vector3> path;
    public float navTimer, shootTimer, fxTimer, stuckTimer, noDirectTimer;
    public float hitHeight = 0.4f; // 被ダメージの火花を出す高さ(体の中心あたり)

    public Vector3 Position { get => go.transform.position; set => go.transform.position = value; }

    public NightEnemy(Kind kind, Vector3 pos)
    {
        this.kind = kind;
        cfg = ConfigOf(kind);
        lightHp = lightHpMax = cfg.lightHp;

        go = new GameObject("Enemy_" + kind);
        go.transform.position = pos;
        float sc = cfg.scale;
        hitHeight = 0.4f * sc;

        var model = NightEnemyModels.Instance != null ? NightEnemyModels.Instance.For(kind) : null;
        if (model != null)
        {
            BuildModel(model);
            return;
        }

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
        SetupHitFx(0.75f * sc);
    }

    // 光を受けている間は即座に光り、ダメージが溜まるほど赤みを帯びて強く光る。外れた後は残りダメージ分だけ薄く残光。
    // _EmissionColor(発光)はスタンドアロンビルドで描画に反映されないことが確認できたため、
    // 代わりに確実に描画される _Color(通常の色)を直接、明るい黄色〜赤へブレンドする方式にしている。
    // ---- 光ダメージ中の演出: 足元の赤い輪(倒れる直前は太く速く点滅) ----
    GameObject ringThin, ringThick;
    Material ringMat;

    void SetupHitFx(float radius)
    {
        ringMat = NightMeshes.Unlit(new Color(1f, 0.18f, 0.1f, 0.9f));
        float thin = 0.05f + 0.02f * radius, thick = 0.18f + 0.07f * radius;
        ringThin = NightMeshes.MeshObject("HitRingThin", NightMeshes.Ring(radius - thin, radius, 40), ringMat, go.transform);
        ringThick = NightMeshes.MeshObject("HitRingThick", NightMeshes.Ring(radius - thick, radius, 40), ringMat, go.transform);
        ringThin.transform.localPosition = ringThick.transform.localPosition = new Vector3(0, 0.12f, 0);
        ringThin.SetActive(false);
        ringThick.SetActive(false);
    }

    void UpdateHitFx(bool lit)
    {
        if (ringThin == null) return;
        if (!lit)
        {
            if (ringThin.activeSelf) ringThin.SetActive(false);
            if (ringThick.activeSelf) ringThick.SetActive(false);
            return;
        }
        bool critical = lightHp / lightHpMax < 0.3f; // 倒れる直前
        float freq = critical ? 16f : 5f;
        float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * freq * Mathf.PI * 2f);
        Color c = ringMat.color;
        c.a = critical ? 0.55f + 0.45f * blink : 0.35f + 0.55f * blink;
        ringMat.color = c;
        ringThin.SetActive(!critical);
        ringThick.SetActive(critical);
    }

    public void ApplyGlow(bool lit)
    {
        UpdateHitFx(lit);
        float dmg = 1f - Mathf.Clamp01(lightHp / lightHpMax);
        // ダメージを受けている間は赤く点滅させる(溜まるほど濃い赤)
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 28f);
        Color litColor = Color.Lerp(new Color(1f, 0.5f, 0.3f), new Color(1f, 0.08f, 0.04f), Mathf.Clamp01(dmg + pulse * 0.3f));
        float blend = lit ? Mathf.Lerp(0.8f, 1f, dmg) : dmg * 0.35f;
        if (modelMats != null)
        {
            // テクスチャ付きモデルは「白 × テクスチャ」が元の見た目。光を浴びたら 1 を超える色を掛けて
            // テクスチャの暗い部分まで黄色く(オーバーブライトに)光らせる。
            Color c = Color.Lerp(Color.white, litColor * 2.4f, blend);
            c.a = 1f;
            foreach (var m in modelMats) if (m != null) m.color = c;
            return;
        }
        mat.color = Color.Lerp(cfg.color, litColor, blend);
    }

    Material[] modelMats;
    UnityEngine.Playables.PlayableGraph graph;

    // Meshy AI の3Dモデルを読み込み、指定の高さに合わせて地面に立たせ、歩行アニメを再生する。
    void BuildModel(NightEnemyModel model)
    {
        var inst = Object.Instantiate(model.prefab, go.transform);
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.Euler(0, model.yaw, 0);

        var renderers = inst.GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds();
        bool first = true;
        foreach (var r in renderers) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
        if (!first && b.size.y > 0.001f)
        {
            float k = model.targetHeight / b.size.y;
            inst.transform.localScale = Vector3.one * k;
            // 足元(最下点)が地面に来るように持ち上げ、中心はXZの原点に揃える
            Vector3 center = go.transform.InverseTransformPoint(b.center);
            inst.transform.localPosition = new Vector3(-center.x * k, -(b.min.y - go.transform.position.y) * k, -center.z * k);
        }

        hitHeight = model.targetHeight * 0.55f;
        SetupHitFx(model.targetHeight * 0.45f);
        var mats = new System.Collections.Generic.List<Material>();
        foreach (var r in renderers) mats.AddRange(r.materials);
        modelMats = mats.ToArray();
        foreach (var m in modelMats) m.SetFloat("_Glossiness", 0.15f);

        var animator = inst.GetComponentInChildren<Animator>();
        if (animator == null) animator = inst.AddComponent<Animator>();
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        if (model.walk != null)
        {
            UnityEngine.Playables.AnimationPlayableUtilities.PlayClip(animator, model.walk, out graph);
            var root = graph.GetRootPlayable(0); UnityEngine.Playables.PlayableExtensions.SetSpeed(root, model.animSpeed);
        }
    }

    public void Destroy()
    {
        if (graph.IsValid()) graph.Destroy();
        Object.Destroy(go);
    }
}

public class NightBullet
{
    public Transform t;
    public Vector3 dir;
    public float speed = 8f, life = 3f;
}
