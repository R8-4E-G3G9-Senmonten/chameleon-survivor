using UnityEngine;

// 蓄光ランタン。合計0.5秒照らすと点灯し、周囲の敵を撃退。点灯後も周囲に継続ダメージを与える。
// 照らした時間はたまっていく(途中で光を外しても減らない)。進み具合はランタンの上のゲージで分かる。
public class NightLamp
{
    public const float ChargeTime = 0.5f, BurstRadius = 9.5f, DamageRadius = 8.5f, Dps = 28f;
    static readonly Color Glow = NightMeshes.Hex(0xffd390);

    public readonly float x, z, height;
    public float charge, power;
    public bool lit;
    float fxTimer;

    readonly Material housingMat, progressMat;
    readonly Transform progress;
    readonly Light light;
    readonly GameObject rangeRing;

    public NightLamp(NightWorld w, float x, float z, float height)
    {
        this.x = x; this.z = z; this.height = height;
        w.Box(x, height + 0.1f, z, 0.68f, 0.2f, 0.68f, w.StoneMat);
        w.Box(x, height + 0.64f, z, 0.14f, 1.1f, 0.14f, w.LampMetal);
        housingMat = NightMeshes.Emissive(NightMeshes.Hex(0x577b76), Glow * 0.025f, 0.7f, 0.15f);
        w.Cylinder(x, height + 1.43f, z, 0.32f, 0.62f, housingMat, null, true);
        w.Box(x, height + 1.06f, z, 0.75f, 0.12f, 0.75f, w.LampMetal);
        w.Box(x, height + 1.8f, z, 0.8f, 0.14f, 0.8f, w.LampMetal);
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                w.Box(x + sx * 0.28f, height + 1.43f, z + sz * 0.28f, 0.07f, 0.62f, 0.07f, w.LampMetal);
        progressMat = NightMeshes.Unlit(NightMeshes.Hex(0x9dc9bc, 0.2f));
        progress = w.RingObject(0.58f, 0.68f, progressMat, w.Root, new Vector3(x, height + 0.025f, z), 40).transform;
        light = NightWorld.PointLight(w.Root, new Vector3(x, height + 1.6f, z), NightMeshes.Hex(0xffcf88), 0f, 16f);
        light.enabled = false;
        // 点灯後に、継続ダメージが届く範囲を細い輪で示す(どこまで安全か分かるように)
        rangeRing = w.RingObject(DamageRadius - 0.1f, DamageRadius, NightMeshes.Unlit(NightMeshes.Hex(0xffd390, 0.4f)), w.Root, new Vector3(x, height + 0.12f, z), 72);
        rangeRing.SetActive(false);
        // ランタンは最初から灯っている(周りの敵に継続ダメージを与える)。照らして点ける必要はない
        lit = true;
        charge = ChargeTime;
    }

    public bool InLight(NightMap map, Vector3 pos, float radius)
    {
        float d = Mathf.Sqrt((x - pos.x) * (x - pos.x) + (z - pos.z) * (z - pos.z));
        return d < radius && map.ClearSight(new Vector3(x, height, z), pos);
    }

    public void Tick(NightGame g, float dt)
    {
        if (!lit)
        {
            Vector3 lt = g.NearestLightTargetTo(new Vector3(x, height, z));
            bool aimed = Mathf.Sqrt((x - lt.x) * (x - lt.x) + (z - lt.z) * (z - lt.z)) < 1.25f && Mathf.Abs(lt.y - height) < 2.15f;
            if (aimed) charge = Mathf.Min(ChargeTime, charge + dt); // 外しても減らない(蓄積)
            if (aimed && NightGame.FxTick(ref fxTimer, dt, 0.1f)) SurvivorBurst.Spawn(new Vector3(x, height + 1.4f, z), NightMeshes.Hex(0xfff0a0), 3);
            float p = charge / ChargeTime;
            housingMat.SetColor("_EmissionColor", Glow * (0.025f + p * 0.9f));
            progressMat.color = NightMeshes.Hex(0x9dc9bc, 0.2f + p * 0.65f);
            progress.localScale = Vector3.one * (1f + p * 0.18f);
            if (charge >= ChargeTime)
            {
                lit = true;
                g.IgniteLantern(this);
                g.Audio.PlayDownChime();
            }
        }
        if (lit)
        {
            power = Mathf.Min(1f, power + dt * 2.5f);
            light.enabled = true;
            // ダメージ範囲の輪(光の縁)は出さない
            light.intensity = power * 2.25f;
            housingMat.SetColor("_EmissionColor", Glow * (0.9f + power * 1.2f));
            progressMat.color = NightMeshes.Hex(0xffd390, 0.45f);
            progress.localScale = Vector3.one * 1.18f;
        }
    }
}

// 導きの植物。少し照らす(ChargeTime 秒)と、最も近い未回収の球(全部集めた後は入口の門)までの光の道を約7秒示す。
// 照らした時間はたまっていく(途中で光を外しても減らない)。道を示したらゲージは空に戻り、また貯めれば使える。
public class NightGuidePlant
{
    public const float ChargeTime = 0.6f; // 照らす時間は短め(2秒では長かった)
    const float AimRadius = 2.2f;         // 光がこの距離まで近ければ照らした扱い(ゆるめ)

    public readonly float x, z, height;
    public float charge;
    public bool latched;
    float fxTimer;

    readonly Material coreMat, petalMat, ringMat;
    readonly Transform ring;
    readonly Light light;

    public NightGuidePlant(NightWorld w, NightRegion r)
    {
        var map = w.Map;
        float bestScore = -1f, px = r.x, pz = r.z;
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.PI / 12f;
            float lx = r.x + Mathf.Cos(a) * (r.r - 1f), lz = r.z + Mathf.Sin(a) * (r.r - 1f);
            bool nearLamp = false;
            foreach (var l in w.Lamps) if (Mathf.Sqrt((l.x - lx) * (l.x - lx) + (l.z - lz) * (l.z - lz)) < 1.9f) nearLamp = true;
            if (nearLamp) continue;
            if (Mathf.Abs(r.z) < 0.01f && lz > 0.5f) continue; // 入口広場では門側に置かない
            float score = map.RouteSurface(lx, lz).distance;
            if (score > bestScore) { bestScore = score; px = lx; pz = lz; }
        }
        x = px; z = pz; height = map.SurfaceAt(px, pz);

        var root = new GameObject("GuidePlant").transform;
        root.SetParent(w.Root, false);
        root.localPosition = new Vector3(x, height, z);

        w.Cylinder(0, 0.32f, 0, 0.07f, 0.65f, NightMeshes.Lit(NightMeshes.Hex(0x3e7961)), root);
        petalMat = NightMeshes.Emissive(NightMeshes.Hex(0x71b9b2), NightMeshes.Hex(0x4ca39c) * 0.18f, 0.4f);
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f;
            var petal = w.Sphere(Mathf.Sin(a) * 0.25f, 0.62f, Mathf.Cos(a) * 0.25f, 0.22f, petalMat, root);
            petal.transform.localScale = new Vector3(0.44f * 0.7f, 0.44f * 0.25f, 0.44f * 1.8f);
            petal.transform.localRotation = Quaternion.Euler(0, a * Mathf.Rad2Deg, 0);
        }
        coreMat = NightMeshes.Emissive(NightMeshes.Hex(0xacf5cf), NightMeshes.Hex(0x8ceeb7) * 0.25f, 0.7f);
        w.Sphere(0, 0.76f, 0, 0.19f, coreMat, root, true);
        ringMat = NightMeshes.Unlit(NightMeshes.Hex(0x98efbd, 0.14f));
        ring = w.RingObject(0.55f, 0.62f, ringMat, root, new Vector3(0, 0.035f, 0), 40).transform;
        light = NightWorld.PointLight(root, new Vector3(0, 0.85f, 0), NightMeshes.Hex(0x8debc3), 0.08f, 3f);
    }

    public void Tick(NightGame g, float dt)
    {
        Vector3 lt = g.NearestLightTargetTo(new Vector3(x, height, z));
        bool aimed = Mathf.Sqrt((x - lt.x) * (x - lt.x) + (z - lt.z) * (z - lt.z)) < AimRadius && Mathf.Abs(lt.y - height) < 2.5f;
        if (aimed && NightGame.FxTick(ref fxTimer, dt, 0.14f)) SurvivorBurst.Spawn(new Vector3(x, height + 0.8f, z), NightMeshes.Hex(0xc9ffe2), 3);
        if (!aimed) latched = false;
        else if (!latched)
        {
            charge = Mathf.Min(ChargeTime, charge + dt);
            if (charge >= ChargeTime) { latched = true; g.ActivateGuidePlant(this); }
        }
        if (latched && charge >= ChargeTime) charge = 0f; // 道を示したら空に戻す(照らし続けている間は道が延長される)
        float p = charge / ChargeTime;
        bool active = g.Guidance != null && g.Guidance.plant == this;
        coreMat.SetColor("_EmissionColor", NightMeshes.Hex(0x8ceeb7) * (0.25f + p * 1.5f + (active ? 0.5f : 0f)));
        petalMat.SetColor("_EmissionColor", NightMeshes.Hex(0x4ca39c) * (0.18f + p * 0.6f));
        ringMat.color = NightMeshes.Hex(0x98efbd, 0.14f + p * 0.65f);
        ring.localScale = Vector3.one * (1f + p * 0.15f);
        light.intensity = 0.08f + p * 0.65f;
        if (active && aimed) g.Guidance.time = Mathf.Max(g.Guidance.time, 1.5f);
    }
}

// 蛍の石。光を当てた時間がたまるほど石が灰色から石の色(青/金)に変わり、まわりから蛍が1匹ずつ飛んでくる。
// 合計2秒照らすと蛍の群れがそろい(materialized)、主人公が触れると蛍が主人公に飛び込んで捕まえられる(collected)。
// 照らした時間はたまっていく(光を外しても減らない)。
public class NightOrb
{
    public const float RevealTime = 2f, LightRadius = 3.2f;
    const int FireflyCount = 14;
    const float StoneHeight = 1.15f, SwarmY = 1.9f, PickupRadius = 1.6f;
    static readonly Color StoneGray = NightMeshes.Hex(0x4a5352), FireflyGreen = NightMeshes.Hex(0xe4ff7a);

    public readonly int index;
    public readonly Color color;
    public readonly float baseY;
    public bool collected, materialized;
    public float reveal;
    float phase, fxTimer, flyAway;

    public readonly Transform root;
    readonly Transform swarm;
    readonly Material stoneMat, fireflyMat, glowMat, ringMat;
    readonly Transform ring;
    readonly Light light;

    // 蛍1匹ずつの動き
    readonly Transform[] flies = new Transform[FireflyCount];
    readonly Vector3[] fromPos = new Vector3[FireflyCount];
    readonly float[] arrive = new float[FireflyCount], orbitR = new float[FireflyCount], orbitH = new float[FireflyCount],
                     orbitSpeed = new float[FireflyCount], orbitPhase = new float[FireflyCount], blinkPhase = new float[FireflyCount];

    public Vector3 Position => root.position;

    public NightOrb(NightWorld w, float x, float z, float baseY, Color color, int index)
    {
        this.index = index; this.color = color; this.baseY = baseY;
        phase = Random.value * Mathf.PI * 2f;
        root = new GameObject("FireflyStone").transform;
        root.SetParent(w.Root, false);
        root.localPosition = new Vector3(x, baseY, z);

        // 石: 森の岩モデルを使い、色を変えられる専用の素材に差し替える(モデルが無いときは潰した球)
        stoneMat = NightMeshes.Emissive(StoneGray, Color.black, 0.15f);
        var prefab = w.rockPrefabs != null && w.rockPrefabs.Length > 0 ? w.rockPrefabs[w.rockPrefabs.Length - 1] : null;
        if (prefab != null)
        {
            var go = Object.Instantiate(prefab, root);
            go.name = "Stone";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0, Random.value * 360f, 0);
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                Bounds bb = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) bb.Encapsulate(rs[i].bounds);
                if (bb.size.y > 0.01f) go.transform.localScale = Vector3.one * (StoneHeight / bb.size.y);
            }
            foreach (var r in rs)
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = stoneMat;
                r.sharedMaterials = mats;
            }
        }
        else
        {
            var st = w.Sphere(0, StoneHeight * 0.45f, 0, 0.7f, stoneMat, root).transform;
            st.localScale = new Vector3(1.5f, StoneHeight, 1.3f);
        }

        ringMat = NightMeshes.Unlit(new Color(color.r, color.g, color.b, 0.25f));
        ring = w.RingObject(0.95f, 1.08f, ringMat, root, new Vector3(0, 0.08f, 0), 40).transform;
        light = NightWorld.PointLight(root, new Vector3(0, SwarmY, 0), Color.Lerp(FireflyGreen, color, 0.3f), 0f, 5f);

        // 蛍(光る小さな粒 + ぼんやりした光の輪)。最初は遠くにいて見えない
        swarm = new GameObject("Fireflies").transform;
        swarm.SetParent(root, false);
        fireflyMat = NightMeshes.Unlit(new Color(1f, 1f, 0.75f, 1f));
        glowMat = NightMeshes.Unlit(new Color(FireflyGreen.r, FireflyGreen.g, FireflyGreen.b, 0.35f));
        for (int i = 0; i < FireflyCount; i++)
        {
            var fly = new GameObject("Firefly").transform;
            fly.SetParent(swarm, false);
            w.Sphere(0, 0, 0, 0.06f, fireflyMat, fly);
            w.Sphere(0, 0, 0, 0.2f, glowMat, fly);
            float ang = Random.value * Mathf.PI * 2f, dist = Random.Range(6f, 9f);
            fromPos[i] = new Vector3(Mathf.Cos(ang) * dist, Random.Range(2.5f, 4.5f), Mathf.Sin(ang) * dist);
            orbitR[i] = Random.Range(0.45f, 1.25f);
            orbitH[i] = SwarmY + Random.Range(-0.5f, 0.6f);
            orbitSpeed[i] = Random.Range(0.7f, 1.4f) * (Random.value < 0.5f ? -1f : 1f);
            orbitPhase[i] = Random.value * Mathf.PI * 2f;
            blinkPhase[i] = Random.value * Mathf.PI * 2f;
            fly.localPosition = fromPos[i];
            fly.gameObject.SetActive(false);
            flies[i] = fly;
        }
        ApplyStone(0f);
    }

    public void Tick(NightGame g, float dt)
    {
        phase += dt;
        Vector3 p0 = root.position;

        if (collected)
        {
            // 捕まえた蛍が主人公に吸い込まれていく
            if (flyAway >= 1f) return;
            flyAway = Mathf.Min(1f, flyAway + dt * 1.6f);
            Vector3 target = g.PlayerPos + Vector3.up * 1.2f - p0;
            for (int i = 0; i < FireflyCount; i++)
            {
                float t = Mathf.Clamp01(flyAway * 1.3f - i * 0.02f);
                flies[i].localPosition = Vector3.Lerp(flies[i].localPosition, target, t);
                flies[i].localScale = Vector3.one * (1f - t * 0.8f);
            }
            light.intensity = 1.2f * (1f - flyAway);
            if (flyAway >= 1f) swarm.gameObject.SetActive(false);
            return;
        }

        Vector3 lt = g.NearestLightTargetTo(p0);
        float distToLight = Mathf.Sqrt((p0.x - lt.x) * (p0.x - lt.x) + (p0.z - lt.z) * (p0.z - lt.z));
        bool beingLit = distToLight < LightRadius && Mathf.Abs(lt.y - baseY) < 1.8f;

        if (!materialized)
        {
            if (beingLit) reveal = Mathf.Min(RevealTime, reveal + dt); // 外しても減らない(蓄積)
            if (beingLit && NightGame.FxTick(ref fxTimer, dt, 0.16f)) SurvivorBurst.Spawn(p0 + Vector3.up * 0.9f, Color.Lerp(color, Color.white, 0.4f), 2);
            if (beingLit && Random.value < dt * 3f) g.Audio.PlayHitBuzz();
            if (reveal >= RevealTime)
            {
                materialized = true;
                g.ShowToast("蛍が集まった！触れて捕まえよう");
                g.Audio.PlayDownChime();
            }
        }
        float p = reveal / RevealTime;
        ApplyStone(p);

        // 照らした分だけ蛍が飛んでくる(14匹を順番に呼ぶ)
        for (int i = 0; i < FireflyCount; i++)
        {
            bool called = p >= (i + 0.5f) / FireflyCount || materialized;
            if (called && !flies[i].gameObject.activeSelf) flies[i].gameObject.SetActive(true);
            if (!called) continue;
            arrive[i] = Mathf.Min(1f, arrive[i] + dt * 0.9f);
            float a = orbitPhase[i] + phase * orbitSpeed[i];
            var orbit = new Vector3(Mathf.Cos(a) * orbitR[i], orbitH[i] + Mathf.Sin(phase * 2.3f + i) * 0.15f, Mathf.Sin(a) * orbitR[i]);
            float e = 1f - (1f - arrive[i]) * (1f - arrive[i]);
            Vector3 pos = Vector3.Lerp(fromPos[i], orbit, e);
            pos.y += Mathf.Sin(e * Mathf.PI) * 0.8f; // ふわっと弧を描いて飛んでくる
            flies[i].localPosition = pos;
            // 蛍らしく、ゆっくり明滅する
            float blink = 0.55f + 0.45f * Mathf.Sin(phase * 3.1f + blinkPhase[i]);
            flies[i].localScale = Vector3.one * (0.6f + 0.6f * blink);
        }

        if (materialized)
        {
            light.intensity = 1.2f + Mathf.Sin(phase * 2.5f) * 0.25f;
            ring.localScale = Vector3.one * (1f + Mathf.Sin(phase * 3f) * 0.06f);
            Vector3 pp = g.PlayerPos;
            float d = Mathf.Sqrt((pp.x - p0.x) * (pp.x - p0.x) + (pp.z - p0.z) * (pp.z - p0.z));
            if (d < PickupRadius && Mathf.Abs(pp.y - baseY) < 1.1f)
            {
                collected = true;
                ApplyStone(1f, 0.35f);
                g.OnOrbCollected(this);
            }
        }
        else light.intensity = p * 0.9f;
    }

    // 石の色: 照らすほど灰色 → 石の色。光り方は glow 倍
    void ApplyStone(float p, float glow = 1f)
    {
        stoneMat.color = Color.Lerp(StoneGray, Color.Lerp(color, Color.white, 0.15f), p);
        stoneMat.SetColor("_EmissionColor", color * (p * p * 0.9f * glow));
        ringMat.color = new Color(color.r, color.g, color.b, 0.2f + p * 0.6f);
    }
}

// 入口の封印門。2つの球で開き、正面から敷居を越えると脱出(クリア)。
public class NightGate
{
    public readonly Transform root;
    readonly Transform[] hinges = new Transform[2];
    readonly int[] sides = { -1, 1 };
    readonly Material[] socketMats = new Material[2];
    readonly Color[] socketColors = { NightMeshes.Hex(0x78c8ff), NightMeshes.Hex(0xffcf65) };
    readonly GameObject beam;
    readonly Material goalLineMat;
    readonly Light goalLight;

    public bool opened;
    public float motion;
    public float Y => root.position.y;

    public NightGate(NightWorld w)
    {
        root = new GameObject("ExitGate").transform;
        root.SetParent(w.Root, false);
        root.localPosition = new Vector3(NightMap.Exit.x, w.Map.SurfaceAt(NightMap.Exit.x, NightMap.Exit.y), NightMap.Exit.y);

        var stone = NightMeshes.Lit(NightMeshes.Hex(0x596c65), 0.1f);
        var metal = NightMeshes.Lit(NightMeshes.Hex(0x273b37), 0.45f, 0.65f);
        foreach (int s in sides)
        {
            w.Box(s * 2.5f, 1.9f, 0, 0.8f, 3.8f, 0.9f, stone, root);
            w.Box(s * 2.5f, 3.85f, 0, 1.1f, 0.25f, 1.1f, w.MossMat, root);
        }
        w.Box(0, 4.1f, 0, 5.8f, 0.5f, 1f, stone, root);

        for (int i = 0; i < 2; i++)
        {
            int s = sides[i];
            var hinge = new GameObject("GateHinge").transform;
            hinge.SetParent(root, false);
            hinge.localPosition = new Vector3(s * 2f, 0, 0);
            w.Box(-s, 1.8f, 0, 2f, 3.35f, 0.22f, metal, hinge);
            foreach (float y in new[] { 0.6f, 1.9f, 3.1f }) w.Box(-s, y, 0, 2f, 0.1f, 0.28f, w.StoneMat, hinge);
            hinges[i] = hinge;
        }

        // 台座の球。画面上の左=青(1つ目の球)、右=金(2つ目の球)。Unityでは画面左が +X。
        for (int i = 0; i < 2; i++)
        {
            float sx = i == 0 ? 3.4f : -3.4f;
            w.Box(sx, 0.5f, -1.5f, 0.9f, 1f, 0.9f, stone, root);
            socketMats[i] = NightMeshes.Emissive(NightMeshes.Hex(0x263c3b), socketColors[i] * 0.07f, 0.7f);
            w.Sphere(sx, 1.2f, -1.5f, 0.32f, socketMats[i], root);
        }

        beam = w.Cylinder(0, 22.5f, 0, 1.8f, 45f, NightMeshes.Unlit(NightMeshes.Hex(0xc9ffe2, 0.1f)), root);
        beam.SetActive(false);

        goalLineMat = NightMeshes.Unlit(NightMeshes.Hex(0xbdffda, 0.1f));
        var line = NightMeshes.Primitive(PrimitiveType.Quad, root, goalLineMat);
        line.transform.localPosition = new Vector3(0, 0.035f, 0);
        line.transform.localRotation = Quaternion.Euler(90, 0, 0);
        line.transform.localScale = new Vector3(3.8f, 0.45f, 1f);

        goalLight = NightWorld.PointLight(root, new Vector3(0, 3f, -0.5f), NightMeshes.Hex(0xc9ffe2), 0.35f, 12f);
    }

    public void UpdateSockets(System.Collections.Generic.List<NightOrb> orbs)
    {
        for (int i = 0; i < orbs.Count && i < 2; i++)
        {
            bool c = orbs[i].collected;
            socketMats[i].color = c ? socketColors[i] : NightMeshes.Hex(0x263c3b);
            socketMats[i].SetColor("_EmissionColor", socketColors[i] * (c ? 2f : 0.07f));
        }
    }

    public void Open()
    {
        opened = true;
        beam.SetActive(true);
    }

    public void Animate(float dt)
    {
        if (!opened) return;
        motion = Mathf.Min(1f, motion + dt * 0.85f);
        float ease = motion * motion * (3f - 2f * motion);
        // 扉は外側(カメラ側 = +Z)へ開く
        for (int i = 0; i < 2; i++) hinges[i].localRotation = Quaternion.Euler(0, sides[i] * 90f * ease, 0);
        goalLight.intensity = 0.35f + ease * 1.9f;
        goalLineMat.color = NightMeshes.Hex(0xbdffda, 0.1f + ease * 0.65f);
    }
}
