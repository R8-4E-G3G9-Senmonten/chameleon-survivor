using System.Collections.Generic;
using UnityEngine;

// 敵: 群れ型(moth)・硬い型(brute)・遠距離型(shooter)・すり抜け型(sprite)。通路グラフに沿ってプレイヤーを追う。
// すり抜け型だけは通路を無視して、木や崖を突き抜けてまっすぐ追ってくる(近距離型・体力は一番低い・回復オーブを落とさない)。
// 光(懐中電灯・閃光・点灯したランタン)を浴び続けると lightHp が減って倒れる。
public class NightEnemy
{
    public enum Kind { Moth, Brute, Shooter, Sprite }
    public const int KindCount = 4;

    public struct Config
    {
        public float speed, contactDps, lightHp, drain, regen, scale;
        public Color color;
        public bool ranged;
        public bool phasing;    // 通路を無視して壁や木をすり抜ける
        public bool noHealDrop; // 倒しても回復オーブを落とさない
    }

    // 全種類の敵の光HPにかける倍率。照らすとテンポよく倒れて気持ちいいように、元の値の6割にしている。
    // 倒れるまでの照射時間(マップB・灯り1つ・待ち伏せなし)の目安:
    //   すり抜け型 約0.4秒 / 群れ型 約1.0秒→0.6秒 / 遠距離型 約2.1秒→1.3秒 / 硬い型 約3.8秒→2.3秒
    // 難しすぎる・簡単すぎる時はこの1つの数字で調整する(マップ別の倍率は NightDifficulty.enemyHp)。
    const float HpScale = 0.6f;

    public static Config ConfigOf(Kind k)
    {
        switch (k)
        {
            case Kind.Brute: return new Config { speed = 2.05f, contactDps = 24, lightHp = 115 * HpScale, drain = 38, regen = 20, color = NightMeshes.Hex(0x8a3b3b), scale = 1f };
            case Kind.Shooter: return new Config { speed = 1.8f, contactDps = 8, lightHp = 60 * HpScale, drain = 35, regen = 15, color = NightMeshes.Hex(0xff8c00), scale = 0.8f, ranged = true };
            // 近道してくる分、足は群れ型より遅め
            case Kind.Sprite: return new Config { speed = 2.6f, contactDps = 10, lightHp = 20 * HpScale, drain = 44, regen = 16, color = NightMeshes.Hex(0x7fe0b0), scale = 0.6f, phasing = true, noHealDrop = true };
            default: return new Config { speed = 3.5f, contactDps = 11, lightHp = 34 * HpScale, drain = 44, regen = 16, color = NightMeshes.Hex(0x8a4b8a), scale = 0.55f };
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
    public float navTimer, shootTimer, fxTimer, crossFxTimer, stuckTimer, noDirectTimer;
    public float hitHeight = 0.4f; // 被ダメージの火花を出す高さ(体の中心あたり)
    public string litBy; // 最後にこの敵を照らしたスマホのid(倒した時に、そのスマホの撃破数に数える)
    public float litByTime;

    public Vector3 Position { get => go.transform.position; set => go.transform.position = value; }

    // ---- 使い回し(オブジェクトプール) ----
    // 敵を出すたびにモデルを複製し、倒すたびに捨てると、そのたびに処理落ち(カクつき)が起きやすい。
    // 倒した敵は隠して種類ごとに取っておき、次に同じ種類を出すときに状態を戻して使い回す。
    static readonly Dictionary<Kind, Stack<NightEnemy>> pool = new Dictionary<Kind, Stack<NightEnemy>>();

    static Stack<NightEnemy> PoolOf(Kind kind)
    {
        if (!pool.TryGetValue(kind, out var st)) pool[kind] = st = new Stack<NightEnemy>();
        return st;
    }

    // 敵を出す(取っておいた敵があれば使い回す)
    public static NightEnemy Spawn(Kind kind, Vector3 pos)
    {
        var st = PoolOf(kind);
        while (st.Count > 0)
        {
            var en = st.Pop();
            if (en.go == null) continue; // シーンを読み直したときに消えたもの
            en.ResetFor(pos);
            return en;
        }
        return new NightEnemy(kind, pos);
    }

    // 先にいくつか作って取っておく(プレイ中に初めて作るときのカクつきを防ぐ)
    public static void Prewarm(Kind kind, int count)
    {
        var st = PoolOf(kind);
        for (int need = count - st.Count; need > 0; need--) new NightEnemy(kind, new Vector3(0, -100f, 0)).Destroy();
    }

    // シーンを読み直す前に呼ぶ(読み直すと敵のオブジェクトは消えるので、取っておいた分も捨てる)
    public static void ClearPool()
    {
        foreach (var st in pool.Values)
            foreach (var en in st)
                if (en.graph.IsValid()) en.graph.Destroy();
        pool.Clear();
    }

    void ResetFor(Vector3 pos)
    {
        lightHp = lightHpMax = cfg.lightHp;
        knock = Vector3.zero;
        wanderTarget = Vector3.zero;
        wanderTimer = wanderPause = confused = foundTimer = 0f;
        dying = false;
        dieTimer = 0f;
        path = null;
        navTimer = shootTimer = fxTimer = crossFxTimer = stuckTimer = noDirectTimer = 0f;
        litBy = null;
        litByTime = 0f;
        go.transform.SetPositionAndRotation(pos, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        go.SetActive(true);
        ApplyGlow(false);
    }

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

    // hot: 2本以上の光が重なっている(ダメージが倍になっている)。白く焼けるように光らせて、重ねた効果を見て分かるようにする
    public void ApplyGlow(bool lit, bool hot = false)
    {
        UpdateHitFx(lit);
        float dmg = 1f - Mathf.Clamp01(lightHp / lightHpMax);
        // ダメージを受けている間は赤く点滅させる(溜まるほど濃い赤)
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 28f);
        Color litColor = Color.Lerp(new Color(1f, 0.5f, 0.3f), new Color(1f, 0.08f, 0.04f), Mathf.Clamp01(dmg + pulse * 0.3f));
        if (lit && hot) litColor = Color.Lerp(litColor, new Color(1f, 0.97f, 0.85f), 0.55f + 0.3f * pulse);
        float blend = lit ? (hot ? 1f : Mathf.Lerp(0.8f, 1f, dmg)) : dmg * 0.35f;
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
            inst.transform.localPosition = new Vector3(-center.x * k, -(b.min.y - go.transform.position.y) * k + model.hover, -center.z * k);
        }

        hitHeight = model.targetHeight * 0.55f + model.hover;
        if (model.glowRange > 0f)
        {
            var glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(go.transform, false);
            // 体の中に置くと表面を照らせないので、頭の上の手前から照らす(通路の外は地面がなく、照らせるのは体だけのため)
            glow.transform.localPosition = new Vector3(0f, model.targetHeight + model.hover + 0.8f, 0.6f);
            glow.type = LightType.Point;
            glow.color = model.glowColor;
            glow.range = model.glowRange;
            glow.intensity = 3f;
            glow.renderMode = LightRenderMode.ForcePixel;
            glow.shadows = LightShadows.None;
        }
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

    // 倒れた敵を片付ける。捨てずに隠して取っておき、Spawn で使い回す。
    public void Destroy()
    {
        go.SetActive(false);
        PoolOf(kind).Push(this);
    }
}

public class NightBullet
{
    public Transform t;
    public Vector3 dir;
    public float speed = 8f, life = 3f;
}
