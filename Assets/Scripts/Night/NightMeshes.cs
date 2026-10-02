using System.Collections.Generic;
using UnityEngine;

// 光の球収集版で使うメッシュ・マテリアルの生成ヘルパー。
// マテリアルは Resources のテンプレートを複製して作る(実行時に new Material(Shader.Find()) すると
// スタンドアロンビルドでシェーダーバリアントが除外され、発光や半透明が消えることがあるため)。
public static class NightMeshes
{
    const string UnlitTemplatePath = "Materials/UnlitTransparentTemplate";
    static Material unlitTemplate;

    public static Color Hex(int rgb, float a = 1f) =>
        new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

    // ライティングを受ける通常のマテリアル(発光色は黒=発光なしで作り、必要な物だけ後から光らせる)
    public static Material Lit(Color color, float glossiness = 0.05f, float metallic = 0f)
    {
        var m = EmissiveMaterialFactory.Create(color);
        m.SetFloat("_Glossiness", glossiness);
        m.SetFloat("_Metallic", metallic);
        return m;
    }

    public static Material Emissive(Color color, Color emission, float glossiness = 0.3f, float metallic = 0f)
    {
        var m = Lit(color, glossiness, metallic);
        m.SetColor("_EmissionColor", emission);
        return m;
    }

    // ライティングの影響を受けない半透明(リング・光の道・光の球の外殻など)。両面描画。
    public static Material Unlit(Color color)
    {
        if (unlitTemplate == null) unlitTemplate = Resources.Load<Material>(UnlitTemplatePath);
        Material m;
        if (unlitTemplate != null) m = new Material(unlitTemplate);
        else
        {
            Debug.LogWarning("NightMeshes: " + UnlitTemplatePath + " が見つかりません。ChameleonNight > Build Night Scene を実行してください。");
            m = new Material(Shader.Find("Sprites/Default"));
        }
        m.color = color;
        return m;
    }

    public static GameObject MeshObject(string name, Mesh mesh, Material mat, Transform parent, bool collider = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    // 当たり判定なしのプリミティブ
    public static GameObject Primitive(PrimitiveType type, Transform parent, Material mat, bool keepCollider = false)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(parent, false);
        if (!keepCollider) UnityEngine.Object.Destroy(go.GetComponent<Collider>());
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        return go;
    }

    // ---- メッシュ ----
    // 上向きの三角形になるよう、頂点の順番を自動で揃えて追加する
    static void UpTri(List<int> tris, List<Vector3> v, int a, int b, int c)
    {
        Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
        if (n.y >= 0) { tris.Add(a); tris.Add(b); tris.Add(c); }
        else { tris.Add(a); tris.Add(c); tris.Add(b); }
    }

    static Mesh Finish(string name, List<Vector3> v, List<int> t, List<Vector2> uv = null)
    {
        var m = new Mesh { name = name };
        if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        if (uv != null) m.SetUVs(0, uv);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    public static Mesh Disc(float radius, int segments = 48)
    {
        var v = new List<Vector3> { Vector3.zero };
        var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) };
        var t = new List<int>();
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            v.Add(new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius));
            uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
        }
        for (int i = 1; i <= segments; i++) UpTri(t, v, 0, i, i + 1);
        return Finish("Disc", v, t, uv);
    }

    public static Mesh Ring(float inner, float outer, int segments = 48)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            v.Add(new Vector3(c * inner, 0, s * inner));
            v.Add(new Vector3(c * outer, 0, s * outer));
        }
        for (int i = 0; i < segments; i++)
        {
            int n = i * 2;
            UpTri(t, v, n, n + 2, n + 1);
            UpTri(t, v, n + 1, n + 2, n + 3);
        }
        return Finish("Ring", v, t);
    }

    // 通路1本ぶんの帯。高さは地形関数(階段の上り下り)に合わせて頂点ごとに決める。
    public static Mesh Strip(NightMap map, NightSegment s)
    {
        float dx = s.b.x - s.a.x, dz = s.b.z - s.a.z, len = Mathf.Sqrt(dx * dx + dz * dz);
        float nx = -dz / len, nz = dx / len;
        int steps = Mathf.CeilToInt(len / 0.7f);
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var t = new List<int>();
        for (int i = 0; i <= steps; i++)
        {
            float k = i / (float)steps, x = s.a.x + dx * k, z = s.a.z + dz * k;
            float y = map.SegmentSample(s, x, z).height + 0.2f;
            for (int side = -1; side <= 1; side += 2)
            {
                v.Add(new Vector3(x + nx * s.width / 2f * side, y, z + nz * s.width / 2f * side));
                uv.Add(new Vector2((side + 1) / 2f, k * len / 5f));
            }
        }
        for (int i = 0; i < steps; i++)
        {
            int n = i * 2;
            UpTri(t, v, n, n + 2, n + 1);
            UpTri(t, v, n + 1, n + 2, n + 3);
        }
        return Finish("PathStrip", v, t, uv);
    }

    // 導きの花が示す光の道の葉(先端が -Z を向く小さな菱形)
    public static Mesh GuideLeaf()
    {
        var v = new List<Vector3> { new Vector3(-0.15f, 0, 0), new Vector3(0, 0, -0.5f), new Vector3(0.15f, 0, 0), new Vector3(0, 0, 0.2f) };
        var t = new List<int>();
        UpTri(t, v, 0, 1, 2);
        UpTri(t, v, 0, 2, 3);
        return Finish("GuideLeaf", v, t);
    }

    // フラットシェーディングの正二十面体(モックアップの敵 IcosahedronGeometry(…, 0) と同じ角ばった見た目)
    static Mesh icosahedron;
    public static Mesh Icosahedron()
    {
        if (icosahedron != null) return icosahedron;
        float p = (1f + Mathf.Sqrt(5f)) / 2f;
        var c = new[]
        {
            new Vector3(-1, p, 0), new Vector3(1, p, 0), new Vector3(-1, -p, 0), new Vector3(1, -p, 0),
            new Vector3(0, -1, p), new Vector3(0, 1, p), new Vector3(0, -1, -p), new Vector3(0, 1, -p),
            new Vector3(p, 0, -1), new Vector3(p, 0, 1), new Vector3(-p, 0, -1), new Vector3(-p, 0, 1),
        };
        int[] f =
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
        };
        var v = new List<Vector3>();
        var t = new List<int>();
        for (int i = 0; i < f.Length; i += 3)
        {
            Vector3 a = c[f[i]].normalized * 0.5f, b = c[f[i + 1]].normalized * 0.5f, d = c[f[i + 2]].normalized * 0.5f;
            // 面の法線が外側を向くように並べる(左手座標系なので外向き = Cross が外側)
            Vector3 center = (a + b + d) / 3f;
            if (Vector3.Dot(Vector3.Cross(b - a, d - a), center) < 0) { var tmp = b; b = d; d = tmp; }
            int n = v.Count;
            v.Add(a); v.Add(b); v.Add(d);
            t.Add(n); t.Add(n + 1); t.Add(n + 2);
        }
        icosahedron = Finish("Icosahedron", v, t);
        return icosahedron;
    }

    // ---- テクスチャ ----
    // 土の道 / 石畳(モックアップの makeTexture と同じ描き方)
    public static Texture2D PathTexture(bool stone, NightRandom rng)
    {
        const int S = 256;
        var px = new Color32[S * S];
        Color32 baseC = stone ? new Color32(0x62, 0x6b, 0x6a, 255) : new Color32(0x75, 0x60, 0x3d, 255);
        for (int i = 0; i < px.Length; i++) px[i] = baseC;
        for (int i = 0; i < 850; i++)
        {
            int x = (int)(rng.Next() * S), y = (int)(rng.Next() * S);
            bool light = rng.Next() < 0.5f;
            int w = 2 + (int)(rng.Next() * 7), h = 2 + (int)(rng.Next() * 5);
            Color32 over = light ? new Color32(255, 255, 255, 0x16) : new Color32(0, 0, 0, 0x22);
            for (int yy = y; yy < Mathf.Min(S, y + h); yy++)
                for (int xx = x; xx < Mathf.Min(S, x + w); xx++)
                    px[yy * S + xx] = Blend(px[yy * S + xx], over);
        }
        if (stone)
        {
            Color32 line = new Color32(0x26, 0x35, 0x38, 0x88);
            for (int y = 0; y < S; y += 64)
            {
                for (int yy = Mathf.Max(0, y - 1); yy <= Mathf.Min(S - 1, y + 1); yy++)
                    for (int x = 0; x < S; x++) px[yy * S + x] = Blend(px[yy * S + x], line);
                for (int x = (y % 128 != 0 ? 32 : 0); x < S; x += 64)
                    for (int yy = y; yy < Mathf.Min(S, y + 64); yy++)
                        for (int xx = Mathf.Max(0, x - 1); xx <= Mathf.Min(S - 1, x + 1); xx++)
                            px[yy * S + xx] = Blend(px[yy * S + xx], line);
            }
        }
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, true);
        tex.SetPixels32(px);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.Apply();
        return tex;
    }

    public static Color32 Blend(Color32 dst, Color32 src)
    {
        float a = src.a / 255f;
        return new Color32(
            (byte)(dst.r + (src.r - dst.r) * a),
            (byte)(dst.g + (src.g - dst.g) * a),
            (byte)(dst.b + (src.b - dst.b) * a),
            dst.a);
    }
}
