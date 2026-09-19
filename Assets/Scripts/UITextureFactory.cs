using UnityEngine;

// OnGUI(IMGUI)向けのテクスチャをランタイム生成するヘルパー。
// SpriteFactory/QRCodeGenerator と同じ方針(インポート資産を使わず自前描画)。
public static class UITextureFactory
{
    // 角丸の単色パネル。IMGUIには border-radius が無いため、アンチエイリアス付きの
    // 角丸マスクをピクセル単位で焼き込んだテクスチャを都度生成して代用する。
    public static Texture2D CreateRoundedRect(Color color, int size, int radius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float edge = RoundedRectCoverage(x, y, size, size, radius);
                Color c = color;
                c.a = color.a * edge;
                pixels[y * size + x] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // 中央が透明で、四隅にいくほど不透明になる放射状のマスク。テクスチャ自体は白で、GUI.colorで着色して使う(黒=暗いビネット、赤=被ダメージ)。
    public static Texture2D CreateVignette(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        float c = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy); // 中心0、辺の中央1、角およそ1.41
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.35f, d));
                pixels[y * size + x] = new Color(1, 1, 1, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static float RoundedRectCoverage(int x, int y, int w, int h, float r)
    {
        float px = x + 0.5f, py = y + 0.5f;
        float dx = Mathf.Max(Mathf.Max(r - px, 0f), Mathf.Max(px - (w - r), 0f));
        float dy = Mathf.Max(Mathf.Max(r - py, 0f), Mathf.Max(py - (h - r), 0f));
        if (dx <= 0f || dy <= 0f) return 1f; // 角丸の外側の直線部分
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Clamp01(r - d + 0.5f);
    }

    // カメレオンナイトのロゴマーク: 暗色の円バッジの中に、擬態(色が変わる)と
    // 夜(三日月)をモチーフにした葉っぱ型のシルエットを重ねたエンブレム。
    public static Texture2D CreateChameleonMark(int size, Color ringColor, Color leafColor, Color moonColor)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[size * size];
        float r = size / 2f;
        float ringInner = r * 0.86f;
        float ringWidth = r * 0.08f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r;
                float dy = y + 0.5f - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                Color c = new Color(0, 0, 0, 0);

                // 外周リング
                if (d <= r && d >= ringInner - ringWidth - 1.5f)
                {
                    float a = Mathf.Clamp01(Mathf.Min(r - d, d - (ringInner - ringWidth)) + 0.5f);
                    if (a > 0f)
                    {
                        c = ringColor;
                        c.a = a;
                    }
                }

                // 内側の背景(バッジの地)
                if (d < ringInner - ringWidth)
                {
                    float a = Mathf.Clamp01(ringInner - ringWidth - d + 0.5f);
                    Color bg = new Color(0.04f, 0.07f, 0.05f, 1f);
                    c = Color.Lerp(c, bg, a);
                    if (c.a < a) c.a = a;
                }

                pixels[y * size + x] = c;
            }
        }

        // 三日月(夜)
        float moonR = r * 0.34f;
        Vector2 moonCenter = new Vector2(r * 0.62f, r * 1.32f);
        Vector2 moonCutCenter = new Vector2(r * 0.78f, r * 1.22f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d1 = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), moonCenter);
                float d2 = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), moonCutCenter);
                float aMain = Mathf.Clamp01(moonR - d1 + 0.5f);
                float aCut = Mathf.Clamp01(moonR * 0.92f - d2 + 0.5f);
                float a = Mathf.Clamp01(aMain - aCut);
                if (a > 0f)
                {
                    int idx = y * size + x;
                    Color existing = pixels[idx];
                    Color blended = Color.Lerp(existing, moonColor, a);
                    blended.a = Mathf.Max(existing.a, a);
                    pixels[idx] = blended;
                }
            }
        }

        // 葉(擬態)のしずく型シルエット。回転させた楕円+先端の三角形の簡易近似として、
        // 中心をずらした2つの円のブレンドで葉らしいシルエットを作る。
        Vector2 leafA = new Vector2(r * 0.92f, r * 0.62f);
        Vector2 leafB = new Vector2(r * 1.32f, r * 1.02f);
        float leafRA = r * 0.5f;
        float leafRB = r * 0.3f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - leafA, leafB - leafA) / (leafB - leafA).sqrMagnitude);
                Vector2 closest = Vector2.Lerp(leafA, leafB, t);
                float rad = Mathf.Lerp(leafRA, leafRB, t);
                float d = Vector2.Distance(p, closest);
                float a = Mathf.Clamp01(rad - d + 0.5f);
                if (a > 0f)
                {
                    int idx = y * size + x;
                    Color existing = pixels[idx];
                    Color blended = Color.Lerp(existing, leafColor, a);
                    blended.a = Mathf.Max(existing.a, a);
                    pixels[idx] = blended;
                }
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
