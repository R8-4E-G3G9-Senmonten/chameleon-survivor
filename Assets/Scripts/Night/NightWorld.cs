using System.Collections.Generic;
using UnityEngine;

// 擬態の色見本と、プレイヤーの当たり判定を兼ねる地形の飾り
public class NightTerrainProp
{
    public Vector3 position;
    public Color color;
    public float radius;
    public bool solid; // 木と岩だけが障害物。地面の色パッチは通り抜けられる

    // 懐中電灯が当たっている間、見た目を黄色く発光させるための状態(NightWorld.UpdateGlowが更新する)。
    // インスタンス化済みのマテリアルを直接書き換える(敵・ランタンと同じ方式。MaterialPropertyBlockは
    // 色が正しく出ないケースがあったため使わない)。
    public Material[] materials;
    public Color[] baseColors; // materials[i] の元の _Color(発光の代わりにこの色から黄色へブレンドする)
    public GameObject go; // デバッグ用(実体の位置・表示状態を確認するため)
    public float glow;
}

// カメラ手前でプレイヤーを隠す木
public class NightCanopy
{
    public float x, z;
    public Renderer[] renderers;
    public bool hidden;
}

// 選んだマップの地形一式を実行時に組み立てる(モックアップの地形生成コードと同じ手順・同じ乱数)。
public class NightWorld : MonoBehaviour
{
    public GameObject[] treePrefabs;
    public GameObject[] rockPrefabs;

    public NightMap Map { get; private set; }
    public static readonly Color GroundColor = NightMeshes.Hex(0x1a2b18);

    public readonly List<NightTerrainProp> Props = new List<NightTerrainProp>();
    public readonly List<NightCanopy> Canopies = new List<NightCanopy>();
    public readonly List<NightLamp> Lamps = new List<NightLamp>();
    public readonly List<NightGuidePlant> Plants = new List<NightGuidePlant>();
    public readonly List<NightOrb> Orbs = new List<NightOrb>();
    public NightGate Gate { get; private set; }
    public Transform Root { get; private set; }

    readonly List<Material> waterMats = new List<Material>();
    readonly Dictionary<float, Mesh> discCache = new Dictionary<float, Mesh>();
    NightRandom rng;

    public Material PathMat, StoneMat, WoodMat, MossMat, LampMetal;

    public void Build(NightMap map)
    {
        Clear();
        Map = map;
        rng = new NightRandom(map.Id);
        Root = new GameObject("World_" + map.Id).transform;

        PathMat = NightMeshes.Lit(NightMeshes.Hex(map.Id == "B" ? 0xb4beb7 : 0xb8a47a), 0.04f);
        PathMat.mainTexture = NightMeshes.PathTexture(map.Id == "B", rng);
        StoneMat = NightMeshes.Lit(NightMeshes.Hex(0x65716c), 0.05f);
        WoodMat = NightMeshes.Lit(NightMeshes.Hex(0x97663c), 0.08f);
        MossMat = NightMeshes.Lit(NightMeshes.Hex(0x35563a), 0f);
        LampMetal = NightMeshes.Lit(NightMeshes.Hex(0x263b3c), 0.55f, 0.6f);

        BuildGround();
        BuildWater();
        BuildPaths();
        BuildRegions();
        foreach (var r in map.Regions) BuildLamp(r);
        foreach (var r in map.Regions) Plants.Add(new NightGuidePlant(this, r));
        BuildOrbs();
        BuildExtras();
        ScatterForest();
        Gate = new NightGate(this);

        Physics.SyncTransforms();
    }

    void Clear()
    {
        if (Root != null) Destroy(Root.gameObject);
        Props.Clear(); Canopies.Clear(); Lamps.Clear(); Plants.Clear(); Orbs.Clear(); waterMats.Clear();
        Gate = null;
    }

    // ---- 組み立て用の小道具 ----
    public GameObject Box(float x, float y, float z, float w, float h, float d, Material mat, Transform parent = null)
    {
        var go = NightMeshes.Primitive(PrimitiveType.Cube, parent != null ? parent : Root, mat);
        go.transform.localPosition = new Vector3(x, y, z);
        go.transform.localScale = new Vector3(w, h, d);
        return go;
    }

    // 円柱(Unityの円柱は直径1・高さ2なので換算する)
    public GameObject Cylinder(float x, float y, float z, float radius, float height, Material mat, Transform parent = null, bool collider = false)
    {
        var go = NightMeshes.Primitive(PrimitiveType.Cylinder, parent != null ? parent : Root, mat, collider);
        go.transform.localPosition = new Vector3(x, y, z);
        go.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
        return go;
    }

    public GameObject Sphere(float x, float y, float z, float radius, Material mat, Transform parent = null, bool collider = false)
    {
        var go = NightMeshes.Primitive(PrimitiveType.Sphere, parent != null ? parent : Root, mat, collider);
        go.transform.localPosition = new Vector3(x, y, z);
        go.transform.localScale = Vector3.one * radius * 2f;
        return go;
    }

    // 地面に寝かせた円盤。光の照準が当たる地形面(当たり判定付き)
    public GameObject Circle(float x, float y, float z, float r, Material mat, bool collider = true)
    {
        float key = Mathf.Round(r * 100f) / 100f;
        if (!discCache.TryGetValue(key, out var mesh)) { mesh = NightMeshes.Disc(key); discCache[key] = mesh; }
        var go = NightMeshes.MeshObject("Disc", mesh, mat, Root, collider);
        go.transform.localPosition = new Vector3(x, y, z);
        return go;
    }

    public GameObject RingObject(float inner, float outer, Material mat, Transform parent, Vector3 localPos, int segments = 48)
    {
        var go = NightMeshes.MeshObject("Ring", NightMeshes.Ring(inner, outer, segments), mat, parent);
        go.transform.localPosition = localPos;
        return go;
    }

    public static Light PointLight(Transform parent, Vector3 localPos, Color color, float intensity, float range)
    {
        var go = new GameObject("PointLight");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        return l;
    }

    float Rand(float a, float b) => rng.Range(a, b);

    // ---- 地面・水面 ----
    void BuildGround()
    {
        var ground = NightMeshes.Primitive(PrimitiveType.Plane, Root,
            NightMeshes.Lit(NightMeshes.Hex(Map.Id == "B" ? 0x101b20 : 0x142a23), 0f));
        ground.name = "Ground";
        ground.transform.localPosition = new Vector3(0, 0, (NightMap.StartZ + NightMap.GoalZ) / 2f);
        ground.transform.localScale = new Vector3((NightMap.HalfW * 2f + 60f) / 10f, 1f, (NightMap.StageLength + 120f) / 10f);
    }

    void BuildWater()
    {
        if (Map.Id == "A") return;
        var mat = NightMeshes.Emissive(NightMeshes.Hex(0x09576f), NightMeshes.Hex(0x032732) * 0.45f, 0.76f, 0.55f);
        waterMats.Add(mat);
        GameObject water;
        if (Map.Id == "C")
        {
            water = Circle(0, 0.07f, -39f, 26f, mat, false);
        }
        else
        {
            water = NightMeshes.Primitive(PrimitiveType.Plane, Root, mat);
            water.transform.localPosition = new Vector3(0, 0.07f, -39f);
            water.transform.localScale = new Vector3(1.4f, 1f, 7.7f);
        }
        water.name = "Water";

        if (Map.Id == "C")
        {
            RingObject(25.8f, 28.5f, NightMeshes.Lit(NightMeshes.Hex(0x435849), 0f), Root, new Vector3(0, 0.08f, -39f), 64);
            Cylinder(0, -0.18f, -38f, 7.2f, 0.7f, MossMat);
            for (int i = 0; i < 42; i++)
            {
                float a = rng.Next() * Mathf.PI * 2f, r = 24f + rng.Next() * 1.4f;
                var reed = Box(Mathf.Cos(a) * r, 0.5f, -39f + Mathf.Sin(a) * r, 0.12f, 0.8f, 0.12f, MossMat);
                reed.transform.localRotation = Quaternion.Euler(0, 0, Rand(-0.25f, 0.25f) * Mathf.Rad2Deg);
            }
        }
    }

    public void UpdateWater(float elapsed)
    {
        foreach (var m in waterMats)
            m.SetColor("_EmissionColor", NightMeshes.Hex(0x032732) * (0.4f + Mathf.Sin(elapsed * 0.8f) * 0.08f));
    }

    // ---- 通路・橋・階段 ----
    bool BridgeSegment(NightSegment s)
    {
        float x = (s.a.x + s.b.x) / 2f, z = (s.a.z + s.b.z) / 2f;
        return s.bridge
            || Map.Id == "B" && Mathf.Abs(x) < 8f && z < -23f
            || Map.Id == "C" && Mathf.Sqrt(x * x + (z + 39f) * (z + 39f)) < 23.5f;
    }

    void BuildPaths()
    {
        int segIndex = 0;
        foreach (var s in Map.Segments)
        {
            // 曲がり角や分岐では隣の通路の帯と同じ高さで重なり、Zファイティング(ちかちか)が起きる。
            // 通路ごとにごくわずか(2mm刻み)に高さをずらして、重なりでも必ず片方が手前になるようにする。
            float bias = (segIndex++) * 0.002f;
            float dx = s.b.x - s.a.x, dz = s.b.z - s.a.z, len = Mathf.Sqrt(dx * dx + dz * dz);
            bool bridge = BridgeSegment(s);
            float rise = s.b.y - s.a.y;
            var mat = bridge ? WoodMat : PathMat;

            var strip = NightMeshes.MeshObject("Path", NightMeshes.Strip(Map, s), mat, Root, true);
            strip.transform.localPosition = new Vector3(0, bias, 0);
            Circle(s.a.x, s.a.y + 0.17f + bias, s.a.z, s.width / 2f, mat);
            Circle(s.b.x, s.b.y + 0.17f + bias, s.b.z, s.width / 2f, mat);

            if (bridge || Mathf.Abs(rise) > 0.001f)
            {
                float yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                for (float t = 0.4f; t < len; t += bridge ? 0.85f : 0.65f)
                {
                    float x = s.a.x + dx * t / len, z = s.a.z + dz * t / len;
                    float y = Map.SegmentSample(s, x, z).height + 0.2f;
                    var rib = Box(x, y + 0.04f, z, s.width, 0.08f, bridge ? 0.12f : 0.2f, bridge ? WoodMat : StoneMat);
                    rib.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                }
            }
            if (bridge || Map.Id == "B") AddGuard(s, bridge);
        }
    }

    // 欄干・側壁は広場と交差する通路の手前で切り、接続口を開ける。
    bool GuardClear(NightSegment s, float t, int side, float offset, float padding = 0.6f)
    {
        float dx = s.b.x - s.a.x, dz = s.b.z - s.a.z, len = Mathf.Sqrt(dx * dx + dz * dz);
        float x = s.a.x + dx * t / len - dz / len * offset * side;
        float z = s.a.z + dz * t / len + dx / len * offset * side;
        foreach (var r in Map.Regions)
            if (Mathf.Sqrt((x - r.x) * (x - r.x) + (z - r.z) * (z - r.z)) < r.r + padding) return false;
        foreach (var other in Map.Segments)
            if (other != s && Map.SegmentSample(other, x, z).distance < other.width / 2f + padding) return false;
        return true;
    }

    List<Vector2> GuardIntervals(NightSegment s, int side, float offset)
    {
        float length = NightMap.Flat(s.a, s.b);
        var spans = new List<Vector2>();
        float start = -1f;
        for (float t = 0; t < length; t += 0.3f)
        {
            float end = Mathf.Min(length, t + 0.3f);
            bool safe = GuardClear(s, t, side, offset) && GuardClear(s, (t + end) / 2f, side, offset) && GuardClear(s, end, side, offset);
            if (safe) { if (start < 0) start = t; }
            else if (start >= 0) { if (t - start > 0.75f) spans.Add(new Vector2(start, t)); start = -1f; }
        }
        if (start >= 0 && length - start > 0.75f) spans.Add(new Vector2(start, length));
        return spans;
    }

    void AddGuard(NightSegment s, bool bridge)
    {
        float dx = s.b.x - s.a.x, dz = s.b.z - s.a.z, length = Mathf.Sqrt(dx * dx + dz * dz);
        float offset = s.width / 2f + (bridge ? 0.12f : 0.4f);
        System.Func<float, int, Vector3> point = (t, side) =>
        {
            float x = s.a.x + dx * t / length - dz / length * offset * side;
            float z = s.a.z + dz * t / length + dx / length * offset * side;
            float y = Map.SegmentSample(s, s.a.x + dx * t / length, s.a.z + dz * t / length).height + 0.2f;
            return new Vector3(x, y, z);
        };
        float lift = bridge ? 1.06f : 0.32f;
        for (int side = -1; side <= 1; side += 2)
        {
            foreach (var span in GuardIntervals(s, side, offset))
            {
                float start = span.x, end = span.y;
                int divisions = Mathf.CeilToInt((end - start) / 2.1f);
                if (bridge)
                {
                    for (int n = 0; n <= divisions; n++)
                    {
                        var p = point(start + (end - start) * n / divisions, side);
                        Box(p.x, p.y + 0.62f, p.z, 0.18f, 1.24f, 0.18f, WoodMat);
                    }
                }
                for (int n = 0; n < divisions; n++)
                {
                    Vector3 a = point(start + (end - start) * n / divisions, side);
                    Vector3 b = point(start + (end - start) * (n + 1) / divisions, side);
                    float flat = NightMap.Flat(a, b), rise = b.y - a.y;
                    var rail = Box((a.x + b.x) / 2f, (a.y + b.y) / 2f + lift, (a.z + b.z) / 2f,
                        bridge ? 0.13f : 0.62f, bridge ? 0.14f : 0.64f, Mathf.Sqrt(flat * flat + rise * rise), bridge ? WoodMat : StoneMat);
                    rail.transform.LookAt(new Vector3(b.x, b.y + lift, b.z));
                }
            }
        }
    }

    // ---- 広場 ----
    void BuildRegions()
    {
        foreach (var r in Map.Regions)
        {
            Circle(r.x, r.y + 0.24f, r.z, r.r, PathMat);
            if (Map.Id == "B")
            {
                float h = r.y + 0.2f;
                Cylinder(r.x, h / 2f - 0.03f, r.z, r.r + 0.3f, h, StoneMat);
                RingObject(r.r - 0.6f, r.r - 0.3f, MossMat, Root, new Vector3(r.x, r.y + 0.28f, r.z));
            }
        }
    }

    // ---- 蓄光ランタン(広場ごとに1基。通路から一番離れた縁に置く) ----
    void BuildLamp(NightRegion r)
    {
        float bestX = r.x, bestZ = r.z, bestClear = -1f;
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.PI / 12f;
            float x = r.x + Mathf.Cos(a) * (r.r - 0.55f), z = r.z + Mathf.Sin(a) * (r.r - 0.55f);
            float c = Map.RouteSurface(x, z).distance;
            if (c > bestClear) { bestClear = c; bestX = x; bestZ = z; }
        }
        Lamps.Add(new NightLamp(this, bestX, bestZ, r.y + 0.2f));
        Props.Add(new NightTerrainProp { position = new Vector3(bestX, r.y + 0.3f, bestZ), color = NightMeshes.Hex(0x263b3c), radius = 1.05f, solid = true });

        if (Map.Id == "B")
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float x = r.x + side * (r.r - 0.7f);
                if (Map.RouteSurface(x, r.z - 2f).distance > 3.2f)
                {
                    Box(x, r.y + 1.4f, r.z - 2f, 0.75f, 2.8f, 0.75f, StoneMat);
                    Box(x, r.y + 2.8f, r.z - 2f, 1.1f, 0.3f, 1.1f, MossMat);
                }
            }
        }
    }

    // ---- 光の球の祠 ----
    void BuildOrbs()
    {
        for (int i = 0; i < Map.Orbs.Length; i++)
        {
            Vector2 p = Map.Orbs[i];
            float y = Map.SurfaceAt(p.x, p.y);
            Cylinder(p.x, y + 0.05f, p.y, 1.35f, 0.22f, StoneMat);
            var ringMat = NightMeshes.Unlit(NightMeshes.Hex(i > 0 ? 0xffc65d : 0x69c8ff, 0.7f));
            RingObject(1.9f, 2.12f, ringMat, Root, new Vector3(p.x, y + 0.03f, p.y));
            Orbs.Add(new NightOrb(this, p.x, p.y, y + 0.18f, NightMeshes.Hex(i > 0 ? 0xffdc72 : 0x78c8ff), i));
        }
    }

    void BuildExtras()
    {
        if (Map.Id == "C") { AddTree(2f, -39f, 1.2f, false); AddRock(-3f, -40f, 1.3f); }
        if (Map.Id == "A")
        {
            Box(-7f, 0.5f, -36f, 0.9f, 1f, 1f, WoodMat);
            Box(32f, 0.5f, -76f, 0.9f, 1f, 1f, WoodMat);
        }
    }

    // ---- 森(擬態の色見本を兼ねる飾り)と、外周の密な木の壁 ----
    bool NearRegion(float x, float z, float pad)
    {
        foreach (var r in Map.Regions) if (Mathf.Sqrt((x - r.x) * (x - r.x) + (z - r.z) * (z - r.z)) < r.r + pad) return true;
        return false;
    }

    void ScatterForest()
    {
        float zLo = NightMap.GoalZ - 6f, zHi = NightMap.StartZ + 8f, hw = NightMap.HalfW;
        for (int i = 0; i < 480; i++)
        {
            float x = Rand(-hw, hw), z = Rand(zLo, zHi);
            if (Map.RouteSurface(x, z).distance < 5f || NearRegion(x, z, 2.5f)) continue;
            if (Map.Id == "C" && Mathf.Sqrt(x * x + (z + 39f) * 0.95f * (z + 39f) * 0.95f) < 25f) continue;
            if (Map.Id == "B" && Mathf.Abs(x) < 9f) continue;
            AddTree(x, z, Rand(0.9f, 1.4f), true);
        }
        for (int i = 0; i < 90; i++)
        {
            float x = Rand(-hw, hw), z = Rand(zLo, zHi);
            if (Map.RouteSurface(x, z).distance > 5f && !NearRegion(x, z, 2f) && !(Map.Id == "C" && Mathf.Sqrt(x * x + (z + 39f) * (z + 39f)) < 25f))
                AddRock(x, z, Rand(0.8f, 1.4f));
        }
        var patchMat = NightMeshes.Lit(NightMeshes.Hex(0x253d2a), 0f);
        for (int i = 0; i < 65; i++)
        {
            float x = Rand(-hw, hw), z = Rand(zLo, zHi);
            float lake = Mathf.Sqrt(x * x + (z + 39f) * (z + 39f));
            if (Map.RouteSurface(x, z).distance > 6f && (Map.Id == "A" || Map.Id == "C" && lake > 29f || Map.Id == "B" && Mathf.Abs(x) > 10f))
            {
                float radius = Rand(1f, 2f);
                var patchGo = Circle(x, 0.02f + rng.Next() * 0.01f, z, radius, patchMat, false);
                var patchMats = CollectInstancedMaterials(patchGo);
                Props.Add(new NightTerrainProp { position = new Vector3(x, 0.02f, z), color = NightMeshes.Hex(0x253d2a), radius = radius * 0.9f, solid = false, materials = patchMats, baseColors = BaseColorsOf(patchMats) });
            }
        }

        for (float z = zLo; z <= zHi; z += 2.4f)
        {
            AddTree(-hw - 3f, z + Rand(-0.6f, 0.6f), Rand(1.5f, 2f), false);
            AddTree(hw + 3f, z + Rand(-0.6f, 0.6f), Rand(1.5f, 2f), false);
        }
        for (float x = -hw; x <= hw; x += 2.4f) AddTree(x, NightMap.StartZ + 9f, Rand(1.5f, 2f), false);
    }

    // Kenneyモデルが持つ元のマテリアルには _EMISSION キーワードが有効になっていないため、
    // MaterialPropertyBlockで_EmissionColorを与えても光らない。同じ元マテリアルを使う全インスタンスで
    // 1つだけ複製(キーワード有効化済み)を使い回すことで、見た目(テクスチャ)は保ったまま発光対応にする。
    readonly Dictionary<Material, Material> emissiveVariantCache = new Dictionary<Material, Material>();

    void EnsureEmissiveCapable(Renderer[] renderers)
    {
        foreach (var rend in renderers)
        {
            var mats = rend.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var src = mats[i];
                if (src == null || src.IsKeywordEnabled("_EMISSION")) continue;
                if (!emissiveVariantCache.TryGetValue(src, out var variant))
                {
                    // src を直接複製して _EMISSION を有効化すると、元の組み合わせ次第でそのシェーダーバリアントが
                    // スタンドアロンビルドから除外され、値は正しくても実際には光らないことがある(今回実際に発生した)。
                    // Kenneyモデルはテクスチャを持たない色だけのマテリアルなので、敵やランタンと同じ
                    // 「Resourcesの発光テンプレートを複製する」方式(ビルドで確実に残る)に完全に置き換える。
                    variant = EmissiveMaterialFactory.Create(src.color);
                    emissiveVariantCache[src] = variant;
                }
                mats[i] = variant;
                changed = true;
            }
            if (changed) rend.sharedMaterials = mats;
        }
    }

    // 発光対応済みのマテリアルを前提に、各レンダラーをインスタンス化(renderer.materials)して集める。
    // 以後はこの配列へ直接 SetColor すれば、他の同じ見た目のオブジェクトを巻き込まずに1体だけ光らせられる。
    static Material[] CollectInstancedMaterials(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        var list = new List<Material>();
        foreach (var r in renderers) list.AddRange(r.materials);
        return list.ToArray();
    }

    static Color[] BaseColorsOf(Material[] mats)
    {
        var c = new Color[mats.Length];
        for (int i = 0; i < mats.Length; i++) c[i] = mats[i].color;
        return c;
    }

    GameObject PlaceModel(GameObject[] prefabs, float x, float z, float targetHeight)
    {
        if (prefabs == null || prefabs.Length == 0) return null;
        var prefab = prefabs[Mathf.Min(prefabs.Length - 1, (int)(rng.Next() * prefabs.Length))];
        if (prefab == null) return null;
        var go = Instantiate(prefab, Root);
        go.transform.localPosition = new Vector3(x, 0, z);
        go.transform.localRotation = Quaternion.Euler(0, rng.Next() * 360f, 0);
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            if (b.size.y > 0.01f) go.transform.localScale = Vector3.one * (targetHeight / b.size.y);
        }
        EnsureEmissiveCapable(renderers);
        return go;
    }

    void AddTree(float x, float z, float scale, bool register)
    {
        var go = PlaceModel(treePrefabs, x, z, 4f * scale);
        if (go == null)
        {
            go = new GameObject("Tree");
            go.transform.SetParent(Root, false);
            go.transform.localPosition = new Vector3(x, 0, z);
            go.transform.localScale = Vector3.one * scale;
            Cylinder(0, 1.1f, 0, 0.3f, 2.2f, NightMeshes.Lit(NightMeshes.Hex(0x5a3d22)), go.transform);
            Sphere(0, 2.6f, 0, 1.4f, NightMeshes.Lit(NightMeshes.Hex(0x2f6b2f)), go.transform);
        }
        if (!register) return;
        var treeMats = CollectInstancedMaterials(go);
        Props.Add(new NightTerrainProp { position = new Vector3(x, 0, z), color = NightMeshes.Hex(0x2f6b2f), radius = 2.2f * scale, solid = true, materials = treeMats, baseColors = BaseColorsOf(treeMats) });
        Canopies.Add(new NightCanopy { x = x, z = z, renderers = go.GetComponentsInChildren<Renderer>() });
    }

    void AddRock(float x, float z, float scale)
    {
        var go = PlaceModel(rockPrefabs, x, z, 1.2f * scale);
        if (go == null)
        {
            go = Box(x, 0.5f * scale, z, 1.1f * scale, 1.1f * scale, 1.1f * scale, NightMeshes.Lit(NightMeshes.Hex(0x555a5f)));
            go.transform.localRotation = Quaternion.Euler(rng.Next() * 360f, rng.Next() * 360f, rng.Next() * 360f);
        }
        var rockMats = CollectInstancedMaterials(go);
        Props.Add(new NightTerrainProp { position = new Vector3(x, 0, z), color = NightMeshes.Hex(0x555a5f), radius = 1.6f * scale, solid = true, materials = rockMats, baseColors = BaseColorsOf(rockMats), go = go });
    }

    // カメラ手前(南側)の木はプレイヤーを隠すので消す
    public void UpdateCanopies(Vector3 player)
    {
        foreach (var c in Canopies)
        {
            float dz = c.z - player.z;
            bool hide = dz > -0.5f && dz < 10f && Mathf.Abs(c.x - player.x) < 3.2f;
            if (hide == c.hidden) continue;
            c.hidden = hide;
            foreach (var r in c.renderers) if (r != null) r.enabled = !hide;
        }
    }

    // 懐中電灯の光が当たっている木・岩・地面のパッチを黄色く発光させる(敵・ランタン・光の球と同じ「光が当たると光る」演出)。
    // 敵と同じく、インスタンス化済みのマテリアルへ直接 SetColor する(MaterialPropertyBlockは色化けが起きたため不使用)。
    // 懐中電灯の直接光(通常のライティング)で元の素材色が強く残ってしまう場合でも黄色がはっきり勝つよう、
    // 加算する発光はかなり強めにしておく(Kenneyモデルの中には素材色が想定外に鮮やかなものがあるため)。
    // _EmissionColor(発光)はスタンドアロンビルドで描画に反映されないことが確認できたため、
    // 代わりに確実に描画される _Color(通常の色)を直接、明るい黄色へブレンドする方式にしている。
    static readonly Color GlowColor = new Color(1f, 0.92f, 0.45f);
    const float GlowRadius = 3f, GlowHeightTolerance = 1.8f;

    // lightTargets: 接続中のスマホ何人ぶんの照準点も渡せる(誰か1人でも当てていれば光る)
    public void UpdateGlow(System.Collections.Generic.List<Vector3> lightTargets, float dt)
    {
        float r2 = GlowRadius * GlowRadius;
        foreach (var p in Props)
        {
            if (p.materials == null || p.materials.Length == 0) continue;
            bool lit = false;
            for (int i = 0; i < lightTargets.Count && !lit; i++)
            {
                Vector3 lightTarget = lightTargets[i];
                float dx = p.position.x - lightTarget.x, dz = p.position.z - lightTarget.z;
                lit = dx * dx + dz * dz < r2 && Mathf.Abs(p.position.y - lightTarget.y) < GlowHeightTolerance;
            }
            float prev = p.glow;
            p.glow = Mathf.MoveTowards(p.glow, lit ? 1f : 0f, dt * (lit ? 10f : 4f));
            if (p.glow < 0.002f && prev < 0.002f) continue; // 既に消えていて今回も光っていないなら何もしない

            for (int i = 0; i < p.materials.Length; i++)
            {
                if (p.materials[i] == null) continue;
                Color baseColor = p.baseColors != null && p.baseColors.Length > i ? p.baseColors[i] : p.materials[i].color;
                p.materials[i].color = Color.Lerp(baseColor, GlowColor, p.glow);
            }
        }
    }

    public Color NearestTerrainColor(Vector3 pos)
    {
        NightTerrainProp best = null;
        float bestDist = float.PositiveInfinity;
        foreach (var p in Props)
        {
            float d = Vector3.Distance(pos, p.position);
            if (d < p.radius + 1.2f && d < bestDist) { bestDist = d; best = p; }
        }
        return best != null ? best.color : GroundColor;
    }

    // 擬態で寄せる見た目。木・岩・草むらのすぐそばならその色、それ以外は足元(通路の土・石畳・橋の板)の色と模様。
    public Color CamouflageColorAt(Vector3 pos, out Texture texture)
    {
        texture = null;
        NightTerrainProp best = null;
        float bestDist = float.PositiveInfinity;
        foreach (var p in Props)
        {
            float d = Vector3.Distance(new Vector3(pos.x, 0, pos.z), new Vector3(p.position.x, 0, p.position.z));
            if (d < p.radius + 0.6f && d < bestDist) { bestDist = d; best = p; }
        }
        if (best != null) return best.color;
        foreach (var r in Map.Regions)
            if (Mathf.Sqrt((pos.x - r.x) * (pos.x - r.x) + (pos.z - r.z) * (pos.z - r.z)) < r.r) { texture = PathMat.mainTexture; return PathMat.color; }
        var s = Map.RouteSurface(pos.x, pos.z);
        if (s.segment != null && BridgeSegment(s.segment)) return WoodMat.color;
        texture = PathMat.mainTexture;
        return PathMat.color;
    }
}
