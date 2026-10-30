using System.Collections.Generic;
using UnityEngine;

// 光の球収集版(mockup: カメレオンナイト ― 光の球収集版)のマップ定義と、通路の歩行判定・経路探索。
// 座標はモックアップ(three.js)の値をそのまま書き、読み込み時に x の符号だけ反転する。
// Unityのカメラは +Z 側から -Z 方向を見るため画面右が -X になり、反転しておくと画面上の見た目がモックアップと一致する。
// Vector3 の y は「高さ」(モックアップの経路点の3番目の値)。
public class NightSegment
{
    public Vector3 a, b;
    public float width;
    public bool bridge;
    public int na, nb;
}

public struct NightRegion
{
    public float x, z, y, r;
    public NightRegion(float x, float z, float y, float r) { this.x = x; this.z = z; this.y = y; this.r = r; }
}

public struct NightSample
{
    public float distance, height, width, x, z, t;
    public NightSegment segment;
}

public class NightMap
{
    public const float HalfW = 40f, StartZ = 0f, GoalZ = -84f;
    public const float StageLength = StartZ - GoalZ;
    public static readonly Vector2 Exit = new Vector2(0f, 2.6f);
    public const float ExitHalfWidth = 1.9f;

    public static readonly string[] Ids = { "A", "B", "C" };
    // 練習モードのマップ。タイトルのマップ選択(Ids)には入れず、「練習モード」ボタンからだけ遊ぶ(ランキングもなし)
    public const string PracticeId = "P";

    public readonly string Id, Name, Description, Info;
    public readonly NightRegion[] Regions;
    public readonly Vector2[] Orbs;
    public readonly List<NightSegment> Segments;
    public readonly List<Vector3> NavNodes = new List<Vector3>();
    readonly List<List<KeyValuePair<int, float>>> navEdges = new List<List<KeyValuePair<int, float>>>();

    public static string NameOf(string id) => id == "B" ? "遺跡攻略" : id == "C" ? "中央湖の周回" : id == PracticeId ? "チュートリアルの森" : "森の迷路";

    NightMap(string id, string name, string desc, string info, float width, float[][] routes, int bridgeRoute,
        float[] orbs, float[] regions)
    {
        Id = id; Name = name; Description = desc; Info = info;

        Orbs = new Vector2[orbs.Length / 2];
        for (int i = 0; i < Orbs.Length; i++) Orbs[i] = new Vector2(-orbs[i * 2], orbs[i * 2 + 1]);

        Regions = new NightRegion[regions.Length / 4];
        for (int i = 0; i < Regions.Length; i++)
            Regions[i] = new NightRegion(-regions[i * 4], regions[i * 4 + 1], regions[i * 4 + 2], regions[i * 4 + 3]);

        var raw = new List<NightSegment>();
        for (int r = 0; r < routes.Length; r++)
        {
            float[] p = routes[r];
            for (int i = 3; i < p.Length; i += 3)
            {
                raw.Add(new NightSegment
                {
                    a = new Vector3(-p[i - 3], p[i - 1], p[i - 2]),
                    b = new Vector3(-p[i], p[i + 2], p[i + 1]),
                    width = width,
                    bridge = r == bridgeRoute,
                });
            }
        }
        Segments = SplitJunctions(raw);

        foreach (var s in Segments)
        {
            s.na = NavNode(s.a);
            s.nb = NavNode(s.b);
            float d = Flat(s.a, s.b);
            navEdges[s.na].Add(new KeyValuePair<int, float>(s.nb, d));
            navEdges[s.nb].Add(new KeyValuePair<int, float>(s.na, d));
        }
    }

    // ---- マップ定義(モックアップの MAPS / REGIONS をそのまま転記。経路点は x,z,高さ の3つ組) ----
    static float[] R(params float[] xz)
    {
        var o = new float[xz.Length / 2 * 3];
        for (int i = 0; i < xz.Length / 2; i++) { o[i * 3] = xz[i * 2]; o[i * 3 + 1] = xz[i * 2 + 1]; }
        return o;
    }
    static float[] R3(params float[] xzy) => xzy;

    public static NightMap Create(string id)
    {
        switch (id)
        {
            case "B":
                return new NightMap("B", "遺跡攻略", "3層の回廊と分岐する階段。谷を渡る橋と迂回路を探そう。", "階段 / 高台 / 石造遺跡", 4.6f,
                    new[]
                    {
                        R3(0,0,0, -20,-12,0, -20,-26,3, -6,-36,3, 12,-36,3, 12,-53,6, -12,-53,6, -22,-64,6),
                        R3(0,0,0, 0,-16,0, 20,-16,0, 24,-28,3, 24,-38,3, 12,-36,3),
                        R3(-20,-26,3, -30,-40,3, -22,-64,6),
                        R3(-20,-12,0, -33,-12,0, -33,-26,3, -20,-26,3),
                        R3(0,-16,0, 0,-26,0, 14,-26,0, 20,-16,0),
                        R3(14,-26,0, 24,-28,3),
                        R3(-30,-40,3, -34,-54,3, -34,-68,6, -22,-64,6),
                        R3(12,-53,6, 26,-53,6, 32,-65,6, 12,-72,6, -12,-53,6),
                        R3(24,-38,3, 26,-53,6),
                        R3(12,-36,3, 0,-43,3, -6,-36,3),
                        R3(-33,-12,0, -35,-4,0),
                        R3(0,-26,0, -7,-25,0),
                        R3(32,-65,6, 35,-76,6),
                        R3(12,-72,6, 0,-76,6),
                    }, -1,
                    new float[] { 24, -38, -22, -64 }, // 蛍1 = 右上の石、蛍2 = 左奥の石(矢印はこの順に向く)
                    new float[] { 0,0,0,5.5f, -20,-26,3,4, 0,-26,0,3, -34,-54,3,3, 12,-36,3,5, 12,-53,6,3.5f, 26,-53,6,3, 12,-72,6,3, -22,-64,6,5, 24,-38,3,4.5f });
            case PracticeId:
                // 練習の森: 入口の広場から奥へ続く、四角い広場(幅の広い通路を格子に重ねて、ほぼ四角に歩ける所を作る)。
                // 奥のまん中に蛍の石が1つ。敵は NightGame の練習用の場所に4体置く
                return new NightMap(PracticeId, "チュートリアルの森", "四角い森の広場。移動・ライト・ホタル集めを練習しよう。", "練習", 8f,
                    new[]
                    {
                        R(0,0, 0,-26),
                        R(-11,-6, 11,-6), R(-11,-12, 11,-12), R(-11,-18, 11,-18), R(-11,-24, 11,-24),
                        R(-11,-6, -11,-24), R(11,-6, 11,-24),
                    }, -1,
                    new float[] { 0, -27 },
                    new float[] { 0,0,0,5 });
            case "C":
                return new NightMap("C", "中央湖の周回", "二重の湖畔道と小島の桟橋。岸辺を巡るか、橋を渡るか。", "周回 / 湖畔 / 小島", 5.2f,
                    new[]
                    {
                        R(0,0, 0,-12, -22,-20, -30,-38, -23,-61, 0,-70, 23,-61, 30,-38, 22,-20, 0,-12),
                        R(0,-12, 0,-38),
                        R(-30,-38, -15,-38, 0,-38),
                        R(0,0, -14,-5, -27,-10, -22,-20),
                        R(0,0, 14,-5, 30,-10, 22,-20),
                        R(-22,-20, -36,-27, -36,-48, -23,-61),
                        R(22,-20, 36,-27, 36,-48, 23,-61),
                        R(-23,-61, -30,-74, -12,-77, 0,-70),
                        R(23,-61, 30,-74, 12,-77, 0,-70),
                        R(0,-38, 13,-38, 13,-54, 23,-61),
                        R(-36,-27, -36,-17),
                        R(36,-48, 36,-60),
                        R(0,-38, -6,-46),
                    }, 9,
                    new float[] { -23, -61, 23, -61 },
                    new float[] { 0,0,0,5, 0,-12,0,4, -30,-38,0,3.5f, 0,-38,0,6.5f, -36,-27,0,3, 36,-48,0,3, 0,-70,0,3.5f, 12,-77,0,3, -23,-61,0,4.5f, 23,-61,0,4.5f });
            default:
                return new NightMap("A", "森の迷路", "幾重にも分岐する森の通路。花の導きを覚え、祠を探そう。", "分岐 / 導きの花 / 祠", 4.6f,
                    new[]
                    {
                        R(0,0, 0,-14, -15,-14, -30,-14, -30,-30, -15,-30, -15,-46, -30,-46, -30,-62, -22,-65),
                        R(0,-14, 15,-14, 30,-14, 30,-30, 15,-30, 15,-46, 24,-40),
                        R(0,-14, 0,-30, 0,-46, 15,-46),
                        R(-15,-30, 0,-30, 15,-30),
                        R(-30,-46, -36,-46, -36,-68, -22,-65),
                        R(-15,-46, -15,-62, 0,-62, 15,-62, 15,-46),
                        R(0,-62, 0,-76, 20,-76, 30,-62, 24,-40),
                        R(0,-30, 7,-36),
                        R(-30,-30, -36,-24),
                        R(-15,-62, -22,-76, -32,-76),
                        R(15,-14, 20,-6),
                    }, -1,
                    new float[] { -22, -65, 24, -40 },
                    new float[] { 0,0,0,5, 0,-14,0,3.5f, -15,-30,0,3.2f, 15,-30,0,3.2f, 0,-46,0,3.3f, -15,-62,0,3.2f, 30,-62,0,3.2f, -22,-65,0,4.3f, 24,-40,0,4.3f });
        }
    }

    // ---- 幾何 ----
    public static float Flat(Vector3 a, Vector3 b) => Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));
    static float Hypot(float x, float z) => Mathf.Sqrt(x * x + z * z);

    // 同じ高さの交差点で通路を分割し、案内と敵の経路を地形に一致させる。
    static List<NightSegment> SplitJunctions(List<NightSegment> segs)
    {
        var cuts = new List<List<float>>();
        foreach (var _ in segs) cuts.Add(new List<float> { 0f, 1f });
        for (int i = 0; i < segs.Count; i++)
        {
            for (int j = i + 1; j < segs.Count; j++)
            {
                var a = segs[i]; var b = segs[j];
                float ax = a.b.x - a.a.x, az = a.b.z - a.a.z, bx = b.b.x - b.a.x, bz = b.b.z - b.a.z;
                float cross = ax * bz - az * bx;
                if (Mathf.Abs(cross) < 0.0001f) continue;
                float qx = b.a.x - a.a.x, qz = b.a.z - a.a.z;
                float t = (qx * bz - qz * bx) / cross, u = (qx * az - qz * ax) / cross;
                if (t < -0.0001f || t > 1.0001f || u < -0.0001f || u > 1.0001f) continue;
                float ya = a.a.y + t * (a.b.y - a.a.y), yb = b.a.y + u * (b.b.y - b.a.y);
                if (Mathf.Abs(ya - yb) > 0.01f) continue;
                if (t > 0.0001f && t < 0.9999f) cuts[i].Add(t);
                if (u > 0.0001f && u < 0.9999f) cuts[j].Add(u);
            }
        }

        var result = new List<NightSegment>();
        for (int i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            var ts = new SortedSet<float>();
            foreach (float t in cuts[i]) ts.Add(Mathf.Round(t * 1e6f) / 1e6f);
            var list = new List<float>(ts);
            for (int k = 1; k < list.Count; k++)
            {
                result.Add(new NightSegment
                {
                    a = Vector3.Lerp(s.a, s.b, list[k - 1]),
                    b = Vector3.Lerp(s.a, s.b, list[k]),
                    width = s.width,
                    bridge = s.bridge,
                });
            }
        }
        return result;
    }

    float RadiusAt(Vector3 p)
    {
        foreach (var r in Regions) if (Hypot(r.x - p.x, r.z - p.z) < 0.01f) return r.r;
        return 2.5f;
    }

    // 通路の中心線上の最寄り点と、その点の高さ(階段は広場の縁から縁へ向けて上がる)
    public NightSample SegmentSample(NightSegment s, float x, float z)
    {
        float dx = s.b.x - s.a.x, dz = s.b.z - s.a.z, len = Hypot(dx, dz);
        float t = Mathf.Clamp01(((x - s.a.x) * dx + (z - s.a.z) * dz) / (len * len));
        float ra = RadiusAt(s.a), rb = RadiusAt(s.b);
        float rise = Mathf.Clamp01((t * len - ra) / Mathf.Max(1f, len - ra - rb));
        return new NightSample
        {
            distance = Hypot(x - s.a.x - t * dx, z - s.a.z - t * dz),
            height = s.a.y + rise * (s.b.y - s.a.y),
            width = s.width,
            x = s.a.x + t * dx,
            z = s.a.z + t * dz,
            t = t,
            segment = s,
        };
    }

    public NightSample RouteSurface(float x, float z)
    {
        var nearest = new NightSample { distance = float.PositiveInfinity };
        foreach (var s in Segments)
        {
            var p = SegmentSample(s, x, z);
            if (p.distance < nearest.distance) nearest = p;
        }
        return nearest;
    }

    public float SurfaceAt(float x, float z)
    {
        foreach (var r in Regions) if (Hypot(x - r.x, z - r.z) < r.r) return r.y + 0.2f;
        return RouteSurface(x, z).height + 0.2f;
    }

    public bool Walkable(float x, float z, float margin = 0.55f)
    {
        foreach (var r in Regions) if (Hypot(x - r.x, z - r.z) < r.r - margin) return true;
        foreach (var s in Segments) if (SegmentSample(s, x, z).distance < s.width / 2f - margin) return true;
        return false;
    }

    // 通路の外に出ていたら、最寄りの通路の内側へ戻す。高さも地形に合わせる。
    public void ConstrainActor(ref Vector3 pos, float margin = 0.55f)
    {
        if (!Walkable(pos.x, pos.z, margin))
        {
            var p = RouteSurface(pos.x, pos.z);
            float d = Mathf.Max(0.001f, p.distance), allowed = p.width / 2f - margin - 0.05f;
            pos.x = p.x + (pos.x - p.x) / d * allowed;
            pos.z = p.z + (pos.z - p.z) / d * allowed;
        }
        pos.y = SurfaceAt(pos.x, pos.z);
    }

    public bool ClearSight(Vector3 a, Vector3 b)
    {
        float d = Flat(a, b);
        for (float t = 0; t <= d; t += 0.6f)
        {
            float k = t / Mathf.Max(0.01f, d);
            if (!Walkable(a.x + (b.x - a.x) * k, a.z + (b.z - a.z) * k, 0.05f)) return false;
        }
        return Mathf.Abs(a.y - b.y) < 1.5f;
    }

    // ---- 経路探索(通路グラフ上のダイクストラ法)。敵も湖・谷を横切らずに追跡する ----
    int NavNode(Vector3 p)
    {
        for (int i = 0; i < NavNodes.Count; i++) if (Flat(NavNodes[i], p) < 0.01f) return i;
        NavNodes.Add(p);
        navEdges.Add(new List<KeyValuePair<int, float>>());
        return NavNodes.Count - 1;
    }

    public List<Vector3> NavigationPath(Vector3 from, Vector3 to)
    {
        var f = RouteSurface(from.x, from.z);
        var g = RouteSurface(to.x, to.z);
        var path = new List<Vector3>();
        if (f.segment == g.segment) { path.Add(to); return path; }

        int n = NavNodes.Count;
        var costs = new float[n];
        var previous = new int[n];
        var used = new bool[n];
        for (int i = 0; i < n; i++) { costs[i] = float.PositiveInfinity; previous[i] = -1; }
        costs[f.segment.na] = Flat(from, NavNodes[f.segment.na]);
        costs[f.segment.nb] = Flat(from, NavNodes[f.segment.nb]);

        for (int k = 0; k < n; k++)
        {
            int u = -1;
            for (int i = 0; i < n; i++) if (!used[i] && (u < 0 || costs[i] < costs[u])) u = i;
            if (u < 0 || float.IsInfinity(costs[u])) break;
            used[u] = true;
            foreach (var e in navEdges[u])
            {
                if (costs[u] + e.Value < costs[e.Key]) { costs[e.Key] = costs[u] + e.Value; previous[e.Key] = u; }
            }
        }

        int ea = g.segment.na, eb = g.segment.nb;
        int end = costs[ea] + Flat(to, NavNodes[ea]) < costs[eb] + Flat(to, NavNodes[eb]) ? ea : eb;
        for (int node = end; node >= 0; node = previous[node]) path.Insert(0, NavNodes[node]);
        path.Insert(0, new Vector3(f.x, 0, f.z));
        path.Add(new Vector3(g.x, 0, g.z));
        path.Add(to);
        return path;
    }

    public static float PathLength(Vector3 from, List<Vector3> path)
    {
        float length = 0; Vector3 previous = from;
        foreach (var p in path) { length += Flat(p, previous); previous = p; }
        return length;
    }
}

// モックアップの mapRandom と同じ線形合同法。同じマップには同じ景色を生成する。
public class NightRandom
{
    uint seed;
    public NightRandom(string id) { seed = (uint)(id[0] * 7919); }
    public float Next() { seed = seed * 1664525u + 1013904223u; return seed / 4294967296f; }
    public float Range(float a, float b) => a + Next() * (b - a);
}
