using System.Collections.Generic;
using UnityEngine;

// 光の球収集版のUI。モックアップ(HTML/CSS)の見た目を IMGUI で再現する。
// 画面の高さ760pxを基準に全体を拡大縮小し、どの解像度でもモックアップと同じ配置になるようにする。
public class NightHud : MonoBehaviour
{
    public NightGame game;

    float scale = 1f, W, H;
    bool helpOpen;
    float toastAlpha;
    Color vignetteColor = new Color(1f, 0.16f, 0.08f, 0f);

    Font serif;
    readonly Dictionary<string, GUIStyle> styles = new Dictionary<string, GUIStyle>();
    readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
    readonly Dictionary<string, Texture2D> previews = new Dictionary<string, Texture2D>();
    Texture2D whiteTex, markTex, qrTexture;
    string qrUrl;

    static Color C(int rgb, float a = 1f) => NightMeshes.Hex(rgb, a);

    void Awake()
    {
        if (game == null) game = GetComponent<NightGame>();
        serif = Font.CreateDynamicFontFromOSFont(new[] { "Georgia", "Times New Roman", "Yu Mincho" }, 46);
        whiteTex = Texture2D.whiteTexture;
        markTex = UITextureFactory.CreateChameleonMark(64, C(0xf5a623), C(0x22c55e), new Color(0.9f, 0.93f, 0.75f));
        foreach (var id in NightMap.Ids) previews[id] = DrawHabitatPreview(id);
    }

    void Update()
    {
        // トーストのフェード(CSS transition: opacity .3s)と、ビネットの色の変化(.15s)
        float dt = Time.unscaledDeltaTime;
        toastAlpha = Mathf.MoveTowards(toastAlpha, game.ToastTimer > 0f ? 1f : 0f, dt / 0.3f);

        // box-shadow: inset … の再現。被弾=赤く太め、擬態=水色、通常=減ったHPに応じて赤みが増す
        Color target;
        float width;
        if (game.HurtNow) { target = new Color(1f, 0.16f, 0.08f, 0.5f); width = 1.3f; }
        else if (game.Camouflaged) { target = new Color(0.31f, 0.78f, 1f, 0.28f); width = 1f; }
        else
        {
            float lost = 1f - game.Hp / NightGame.HpMax;
            target = new Color(1f, 0.16f, 0.08f, 0.05f + lost * 0.3f);
            width = 0.55f + lost * 1.4f;
        }
        float k = Mathf.Min(1f, dt / 0.15f);
        vignetteColor = Color.Lerp(vignetteColor, target, k);
        vignetteWidth = Mathf.Lerp(vignetteWidth, width, k);
    }

    float vignetteWidth = 0.55f;

    // 画面の縁だけが光る内側の影。9スライスで描くので縁の太さは解像度に関係なく一定。
    void DrawEdgeGlow()
    {
        const int Edge = 64;
        var tex = Tex("edgeGlow", () =>
        {
            const int S = Edge * 2 + 2;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Min(Mathf.Min(x + 0.5f, S - x - 0.5f), Mathf.Min(y + 0.5f, S - y - 0.5f));
                    float a = Mathf.Clamp01(1f - d / Edge);
                    px[y * S + x] = new Color(1, 1, 1, a * a);
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        });
        var old = GUI.matrix;
        float w = Mathf.Max(0.1f, vignetteWidth);
        GUI.matrix = old * Matrix4x4.Scale(new Vector3(w, w, 1f));
        GUI.color = vignetteColor;
        Panel(new Rect(0, 0, W / w, H / w), tex, Edge);
        GUI.color = Color.white;
        GUI.matrix = old;
    }

    void OnGUI()
    {
        scale = Mathf.Clamp(Screen.height / 760f, 0.7f, 3f);
        W = Screen.width / scale;
        H = Screen.height / scale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        if (game.State == NightGame.GameState.Title)
        {
            DrawStartOverlay();
            DrawConnectPanel(true);
            return;
        }

        // 閃光(z-index 3) → ビネット(4) → HUD(5) → トースト(6) → 結果(10)
        if (game.FlashLevel > 0f) Fill(new Rect(0, 0, W, H), C(0xeaf2ff, game.FlashLevel * 0.5f));
        DrawEdgeGlow();

        DrawHud();
        DrawGoalHud();
        DrawTopRight();
        DrawToast();
        DrawHint();
        DrawConnectPanel(false);

        if (game.State == NightGame.GameState.Paused) DrawPauseMenu();
        if (game.State == NightGame.GameState.Ended) DrawEndOverlay();
    }

    // ================= 開始画面 =================
    void DrawStartOverlay()
    {
        GUI.DrawTexture(new Rect(0, 0, W, H), Tex("startBg", () => RadialBackground()), ScaleMode.StretchToFill);

        float maxW = Mathf.Min(810f, W - 48f);
        float cardW = Mathf.Floor((maxW - 32f) / 3f);
        bool compact = H < 760f;
        float preview = Mathf.Min(cardW - 24f, compact ? 180f : 235f);
        float cardH = 14f + preview + 12f + 22f + 7f + 14f + 16f;

        string paragraph = "球や敵にマウスの光を当て続けます。止まると擬態して一時的に無敵になり、敵を押し返します。擬態ゲージは移動中に回復。約12秒ごとの閃光でも敵にダメージを与えられます。ランタンを約0.5秒照らすと点灯し、半径7mの同じ高さ・光の届く範囲の敵を倒します。点灯後も半径6mの敵に継続ダメージを与えます。途中で光を外すと蓄光はリセットされます。球は約2秒照らして実体化させ、触れて回収します。2つ揃うと入口の門が開きます。導きの花を約2秒連続で照らすと、花から最も近い未回収の球までの道が約7秒間光ります。光の道は橋や階段に沿って現れます。球が2つ揃った後は、花が入口の門まで案内します。門を通過するとクリアです。帰り道は敵の出現が少し速くなります。";
        var helpStyle = S("help", 11, C(0x8fada4), TextAnchor.UpperLeft, wrap: true);
        float helpW = Mathf.Min(650f, W - 48f);
        float helpH = helpOpen ? helpStyle.CalcHeight(new GUIContent(paragraph), helpW) + 8f : 0f;

        float total = 14 + 8 + 56 + 8 + 18 + (compact ? 12 : 22) + 22 + 12 + 20 + (compact ? 14 : 22) + cardH + 18 + 42 + 18 + 46 + 18 + 18 + helpH;
        float y = Mathf.Max(16f, (H - total) / 2f);
        float cx = W / 2f;

        Label(new Rect(0, y, W, 14), "光と影の森 / EXPLORATION", S("eyebrow", 10, C(0xb3d2c3), TextAnchor.MiddleCenter));
        y += 14 + 8;
        var title = S("title", 46, C(0xe6f5e9), TextAnchor.MiddleCenter);
        title.font = serif;
        Label(new Rect(0, y, W, 56), "C H A M E L E O N   N I G H T", title);
        y += 56 + 8;
        Label(new Rect(0, y, W, 18), "灯りを導きに、夜の向こうへ。", S("menuSub", 13, C(0xc8ded2), TextAnchor.MiddleCenter));
        y += 18 + (compact ? 12 : 22);
        Label(new Rect(0, y, W, 22), "2つの光の球を集めて門を開き、入口へ戻って脱出しよう。", S("mission", 13, C(0xe1eadd), TextAnchor.MiddleCenter));
        y += 22 + 12;

        DrawControlStrip(y);
        y += 20 + (compact ? 14 : 22);

        // マップ選択カード
        float rowW = cardW * 3 + 32f;
        float x0 = cx - rowW / 2f;
        for (int i = 0; i < NightMap.Ids.Length; i++)
        {
            string id = NightMap.Ids[i];
            Rect r = new Rect(x0 + i * (cardW + 16f), y, cardW, cardH);
            bool hover = r.Contains(Event.current.mousePosition);
            bool selected = id == game.MapId;
            if (hover) r.y -= 4f;
            DrawMapCard(r, id, selected, hover, preview);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) game.SelectMap(id);
        }
        y += cardH + 18;

        Label(new Rect(cx - 300, y, 600, 42), game.Map != null ? game.Map.Description : "", S("mapDesc", 13, C(0xc7dacf), TextAnchor.MiddleCenter, wrap: true));
        y += 42 + 18;

        Rect btn = new Rect(cx - 140, y, 280, 46);
        bool btnHover = btn.Contains(Event.current.mousePosition);
        GUI.DrawTexture(btn, Tex("startBtn" + btnHover, () => Gradient(280, 46, C(0x2e7974), C(0x246257), 8, C(0x8ddccb, btnHover ? 0.9f : 0.53f), 1, 120f)));
        Label(btn, "このマップでプレイ →", S("startBtnText", 16, Color.white, TextAnchor.MiddleCenter));
        if (GUI.Button(btn, GUIContent.none, GUIStyle.none)) game.StartGame();
        y += 46 + 18;

        Rect summary = new Rect(cx - 140, y, 280, 18);
        Label(summary, (helpOpen ? "▼ " : "▶ ") + "遊び方・擬態と閃光について", S("helpSummary", 11, C(0x8fada4), TextAnchor.MiddleCenter));
        if (GUI.Button(summary, GUIContent.none, GUIStyle.none)) helpOpen = !helpOpen;
        y += 18 + 8;
        if (helpOpen) Label(new Rect(cx - helpW / 2f, y, helpW, helpH), paragraph, helpStyle);

        Label(new Rect(16, H - 24, W - 32, 18), "Enter でプレイ開始　1 / 2 / 3 でマップ選択", S("titleKeys", 10, C(0x8fada4, 0.7f), TextAnchor.MiddleLeft));

        // 展示会場でESP32やスマホの接続先を手動設定する際にすぐ分かるよう、PCのIPを大きめに表示しておく
        string ip = PointerServer.Instance != null ? PointerServer.Instance.LocalIp : "取得中...";
        Label(new Rect(16, H - 44, W - 32, 18), "このPCのIP: " + ip, S("titleIp", 13, C(0xdfc985, 0.95f), TextAnchor.MiddleLeft, bold: true));
    }

    void DrawControlStrip(float y)
    {
        var text = S("strip", 11, C(0xa6c3b6), TextAnchor.MiddleLeft);
        var kbd = S("kbd", 11, C(0xdfeee6), TextAnchor.MiddleCenter);
        // [項目] = (キー..., 説明)
        var items = new List<KeyValuePair<string[], string>>
        {
            new KeyValuePair<string[], string>(new[] { "WASD", "↑↓←→" }, "移動"),
            new KeyValuePair<string[], string>(new string[0], "マウスで照らす"),
            new KeyValuePair<string[], string>(new string[0], "止まると擬態"),
            new KeyValuePair<string[], string>(new string[0], "ランタンは照らすとすぐ点灯・周囲の敵を撃退"),
            new KeyValuePair<string[], string>(new string[0], "花を照らすと道案内"),
        };
        const float gap = 14f;
        float total = 0f;
        var widths = new List<float>();
        foreach (var it in items)
        {
            float w = 0f;
            for (int k = 0; k < it.Key.Length; k++) w += kbd.CalcSize(new GUIContent(it.Key[k])).x + 10f + (k > 0 ? text.CalcSize(new GUIContent(" / ")).x : 0f);
            if (it.Key.Length > 0) w += 4f;
            w += text.CalcSize(new GUIContent(it.Value)).x;
            widths.Add(w);
            total += w;
        }
        total += gap * (items.Count - 1);
        float x = Mathf.Max(8f, (W - total) / 2f);
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            float cx = x;
            for (int k = 0; k < it.Key.Length; k++)
            {
                if (k > 0)
                {
                    float sw = text.CalcSize(new GUIContent(" / ")).x;
                    Label(new Rect(cx, y, sw, 20), " / ", text);
                    cx += sw;
                }
                float kw = kbd.CalcSize(new GUIContent(it.Key[k])).x + 10f;
                Rect kr = new Rect(cx, y + 1, kw, 18);
                Panel(kr, Tex("kbdBox", () => RoundedBox(32, 4, C(0x152c2c), C(0x608778, 0.4f), 1)), 4);
                Label(kr, it.Key[k], kbd);
                cx += kw;
            }
            if (it.Key.Length > 0) cx += 4f;
            Label(new Rect(cx, y, widths[i], 20), it.Value, text);
            x += widths[i] + gap;
        }
    }

    void DrawMapCard(Rect r, string id, bool selected, bool hover, float preview)
    {
        Color border = selected ? C(0xdfc985) : hover ? C(0xbde2ca) : C(0x739e90, 0.33f);
        string key = "card" + (int)r.width + "x" + (int)r.height + (selected ? "s" : hover ? "h" : "n");
        if (selected) GUI.DrawTexture(new Rect(r.x - 6, r.y - 2, r.width + 12, r.height + 14), Tex("cardShadow" + (int)r.width, () => ShadowTex()), ScaleMode.StretchToFill);
        GUI.DrawTexture(r, Tex(key, () => Gradient((int)r.width, (int)r.height, C(0x15322e), C(0x071a1f), 10, border, 1, 150f)));

        Rect pv = new Rect(r.x + (r.width - preview) / 2f, r.y + 14, preview, preview);
        GUI.DrawTexture(pv, previews[id], ScaleMode.ScaleToFit);
        Label(new Rect(r.x + 12, pv.yMax + 12, r.width - 24, 22), id + ". " + NightMap.NameOf(id), S("cardTitle", 16, C(0xdfffe0), TextAnchor.MiddleLeft, bold: true));
        string info = id == "A" ? "分岐 / 導きの花 / 祠" : id == "B" ? "階段 / 高台 / 石造遺跡" : "周回 / 湖畔 / 小島";
        Label(new Rect(r.x + 12, pv.yMax + 41, r.width - 24, 14), info, S("cardInfo", 10, C(0x98b6a8), TextAnchor.MiddleLeft));

        if (selected)
        {
            var badgeStyle = S("selBadge", 10, C(0xeedda8), TextAnchor.MiddleCenter);
            float bw = badgeStyle.CalcSize(new GUIContent("選択中")).x + 14f;
            Rect b = new Rect(r.xMax - 12 - bw, r.y + 12, bw, 20);
            Panel(b, Tex("selBadgeBg", () => RoundedBox(24, 4, C(0x11251e), C(0x11251e), 0)), 4);
            Label(b, "選択中", badgeStyle);
        }
    }

    // ================= HUD =================
    void DrawHud()
    {
        Rect r = new Rect(12, 12, 248, 120);
        Panel(r, PanelTex(), 9);
        float x = r.x + 14, y = r.y + 10;
        GUI.DrawTexture(new Rect(x, y + 1, 16, 16), markTex);
        Shadowed(new Rect(x + 21, y, r.width - 49, 18), game.MapId + ". " + game.Map.Name, S("hudH1", 14, C(0xdfffe0, 0.9f), TextAnchor.MiddleLeft, bold: true));
        y += 21;
        var row = S("hudRow", 10, C(0xdfffe0, 0.82f), TextAnchor.UpperLeft, wrap: true);
        string rowText = "移動: WASD/矢印　灯り: " + (game.Flashlight.HasPhoneInput ? "スマホ" : "マウス") + "　止まる=擬態　光の球を2つ集めよう";
        float rowH = row.CalcHeight(new GUIContent(rowText), 220);
        Shadowed(new Rect(x, y, 220, rowH), rowText, row);
        y += rowH + 2;

        // 圧力マット(ESP32)との接続表示。展示中に接続が切れていないか一目で分かるように。
        var mat = PressureMatServer.Instance;
        bool matConnected = mat != null && mat.Connected;
        var matStyle = S(matConnected ? "matOn" : "matOff", 10, matConnected ? C(0x9dffb0) : C(0xff8f7a), TextAnchor.MiddleLeft);
        string matText = matConnected
            ? "接続中(" + mat.Source + ")"
            : "未接続" + (mat != null && mat.SerialStatus.Length > 0 ? "  USB: " + mat.SerialStatus : "");
        Shadowed(new Rect(x, y, 220, 14), (matConnected ? "● " : "○ ") + "コントローラー: " + matText, matStyle);
        y += 14 + 4;

        float hpRatio = game.Hp / NightGame.HpMax;
        bool low = hpRatio < 0.3f;
        Bar(new Rect(x, y, 190, 11), hpRatio, low ? "hpLow" : "hp", low ? C(0xff5a4a) : C(0x6fe08a), low ? C(0xff8f7a) : C(0x9dffb0), "HP");
        y += 11 + 5;
        bool locked = game.CampLocked;
        Bar(new Rect(x, y, 190, 11), game.Camp / NightGame.CampMax, locked ? "campLocked" : "camp",
            locked ? C(0x666666) : C(0x3fa0c8), locked ? C(0x999999) : C(0x8fe6ff), "擬態ゲージ");
        r.height = y + 11 + 10 - r.y;
    }

    void DrawGoalHud()
    {
        string text = game.GateOpened ? "門が開いた！入口へ戻ろう" : "光の球 " + game.OrbCount + " / " + NightGame.OrbTotal;
        var style = S("goal", 14, C(0xdfffe0), TextAnchor.MiddleCenter, bold: true);
        float w = Mathf.Max(250f, style.CalcSize(new GUIContent(text)).x + 30f);
        Rect r = new Rect(W / 2f - w / 2f, 12, w, 46);
        Panel(r, PanelTex(), 9);
        Shadowed(new Rect(r.x, r.y + 8, r.width, 18), text, style);
        Bar(new Rect(W / 2f - 110f, r.y + 30, 220, 8), game.OrbCount / (float)NightGame.OrbTotal, "prog", C(0xffdd55), C(0x9dffb0), null, 0.18f);
    }

    void DrawTopRight()
    {
        string badge; Color badgeColor;
        if (game.Camouflaged) { badge = "擬態中(無敵)"; badgeColor = new Color(60 / 255f, 160 / 255f, 200 / 255f, 0.9f); }
        else if (game.CampLocked) { badge = "ゲージ切れ"; badgeColor = new Color(120 / 255f, 120 / 255f, 120 / 255f, 0.85f); }
        else { badge = "移動中"; badgeColor = new Color(190 / 255f, 110 / 255f, 50 / 255f, 0.9f); }

        bool flashing = game.FlashT >= 0f;
        int wait = Mathf.Max(0, Mathf.CeilToInt(game.FlashWait));
        string flash = flashing ? "閃光!" : "閃光まで " + wait + "s";
        bool soon = flashing || game.FlashWait <= 3f;

        var badgeStyle = S("badge", 12, Color.white, TextAnchor.MiddleCenter, bold: true);
        var killStyle = S("kill", 14, C(0xffb0b0), TextAnchor.MiddleRight);
        var timerStyle = S("timer", 16, C(0xbbccdd), TextAnchor.MiddleRight);
        var flashStyle = S(soon ? "flashSoon" : "flash", 13, soon ? Color.white : C(0xffe9a0), TextAnchor.MiddleRight, bold: soon);

        string kill = "撃破: " + game.KillCount;
        string timer = FormatTime(game.Elapsed);
        float bw = badgeStyle.CalcSize(new GUIContent(badge)).x + 20f;
        float contentW = Mathf.Max(bw, killStyle.CalcSize(new GUIContent(kill)).x, timerStyle.CalcSize(new GUIContent(timer)).x,
            S("flashSoon", 13, Color.white, TextAnchor.MiddleRight, bold: true).CalcSize(new GUIContent("閃光まで 12s")).x);
        Rect r = new Rect(W - 12 - (contentW + 24), 12, contentW + 24, 8 + 22 + 2 + 20 + 2 + 22 + 2 + 18 + 8);
        Panel(r, PanelTex(), 9);
        float x = r.x + 12, y = r.y + 8, w = contentW;

        GUI.color = badgeColor;
        Panel(new Rect(x + w - bw, y, bw, 22), Tex("pill", () => RoundedBox(32, 11, Color.white, Color.white, 0)), 11);
        GUI.color = Color.white;
        Label(new Rect(x + w - bw, y, bw, 22), badge, badgeStyle);
        y += 22 + 2;
        Shadowed(new Rect(x, y, w, 20), kill, killStyle);
        y += 20 + 2;
        Shadowed(new Rect(x, y, w, 22), timer, timerStyle);
        y += 22 + 2;
        Shadowed(new Rect(x, y, w, 18), flash, flashStyle);
    }

    void DrawToast()
    {
        if (toastAlpha <= 0.001f) return;
        var style = S("toast", 20, C(0xffe9a0), TextAnchor.MiddleCenter, bold: true);
        Rect r = new Rect(0, H * 0.22f, W, 30);
        Color old = GUI.color;
        GUI.color = new Color(1, 1, 1, toastAlpha);
        Shadowed(r, game.ToastText, style, 2f);
        GUI.color = old;
    }

    void DrawHint()
    {
        Color old = GUI.color;
        GUI.color = new Color(1, 1, 1, 0.75f);
        Shadowed(new Rect(16, H - 18 - 16, W - 32 - 190, 16), game.Hint, S("hint", 11, C(0xcdeccd), TextAnchor.MiddleLeft));
        GUI.color = old;
    }

    void DrawEndOverlay()
    {
        Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.82f));
        bool win = game.Cleared;
        float cy = H / 2f;
        Label(new Rect(0, cy - 110, W, 50), win ? "門を抜けて脱出した！" : "力尽きた…",
            S(win ? "endWin" : "endLose", 38, win ? C(0x9dffb0) : C(0xff8f7a), TextAnchor.MiddleCenter, bold: true));
        string stats = (win ? "クリアタイム: " : "生存時間: ") + FormatTime(game.Elapsed) + "\n撃破数: " + game.KillCount +
                       "\n光の球: " + game.OrbCount + " / " + NightGame.OrbTotal;
        var statStyle = S("endStats", 14, new Color(1, 1, 1, 0.85f), TextAnchor.UpperCenter, wrap: true);
        statStyle.fixedHeight = 0;
        Label(new Rect(0, cy - 50, W, 80), stats, statStyle);

        Rect btn = new Rect(W / 2f - 110, cy + 40, 220, 42);
        bool hover = btn.Contains(Event.current.mousePosition);
        Panel(btn, Tex("endBtn" + hover, () => RoundedBox(32, 8, hover ? C(0x46a058) : C(0x3a8a4a), hover ? C(0x46a058) : C(0x3a8a4a), 0)), 8);
        Label(btn, "もう一度やり直す (R)", S("endBtnText", 15, Color.white, TextAnchor.MiddleCenter));
        if (GUI.Button(btn, GUIContent.none, GUIStyle.none)) game.Restart();
    }

    // ESCで開く一時停止メニュー(開発者向けに明るさ・音量を調整できる)
    void DrawPauseMenu()
    {
        Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.35f));
        Rect card = new Rect(W / 2f - 200, H / 2f - 175, 400, 350);
        Panel(card, Tex("pausePanel", () => RoundedBox(48, 12, C(0x06131b, 0.94f), C(0x739e90, 0.45f), 1)), 12);
        Label(new Rect(card.x, card.y + 18, card.width, 40), "一時停止", S("pauseTitle", 28, C(0xe6f5e9), TextAnchor.MiddleCenter, bold: true));
        Label(new Rect(card.x + 24, card.y + 72, card.width - 48, 18), "開発者向け設定", S("pauseSection", 12, C(0xdfc985), TextAnchor.MiddleLeft, bold: true));

        var label = S("pauseLabel", 14, C(0xdfffe0), TextAnchor.MiddleLeft);
        Label(new Rect(card.x + 24, card.y + 96, card.width - 48, 22), "画面の明るさ  ×" + game.Brightness.ToString("0.00"), label);
        float b = GUI.HorizontalSlider(new Rect(card.x + 24, card.y + 124, card.width - 48, 20), game.Brightness, NightGame.BrightnessMin, NightGame.BrightnessMax);
        if (!Mathf.Approximately(b, game.Brightness)) game.SetBrightness(b);
        if (GUI.Button(new Rect(card.x + 24, card.y + 148, 130, 26), "明るさをリセット")) game.SetBrightness(1f);

        Label(new Rect(card.x + 24, card.y + 190, card.width - 48, 22), "音量  " + Mathf.RoundToInt(game.Volume * 100f) + "%", label);
        float v = GUI.HorizontalSlider(new Rect(card.x + 24, card.y + 218, card.width - 48, 20), game.Volume, 0f, 1f);
        if (!Mathf.Approximately(v, game.Volume)) game.SetVolume(v);

        if (MenuButton(new Rect(card.x + 24, card.y + 262, 168, 42), "再開")) game.Resume();
        if (MenuButton(new Rect(card.xMax - 24 - 168, card.y + 262, 168, 42), "最初からやり直す")) game.Restart();
        Label(new Rect(card.x, card.y + 316, card.width, 18), "ESCでも再開できます", S("pauseHint", 11, C(0x8fada4), TextAnchor.MiddleCenter));
    }

    bool MenuButton(Rect r, string text)
    {
        bool hover = r.Contains(Event.current.mousePosition);
        GUI.DrawTexture(r, Tex("menuBtn" + hover, () => Gradient(168, 42, C(0x2e7974), C(0x246257), 8, C(0x8ddccb, hover ? 0.9f : 0.53f), 1, 120f)));
        Label(r, text, S("menuBtnText", 15, Color.white, TextAnchor.MiddleCenter));
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    // ================= スマホ接続(QRコード) =================
    void DrawConnectPanel(bool title)
    {
        string httpsUrl = PointerServer.Instance != null ? PointerServer.Instance.HttpsServerUrl : "起動中...";
        string httpUrl = PointerServer.Instance != null ? PointerServer.Instance.ServerUrl : "起動中...";
        bool phone = game.Flashlight != null && game.Flashlight.HasPhoneInput;
        int phoneCount = PointerServer.Instance != null ? PointerServer.Instance.GetActivePointers().Count : 0;

        float qr = title ? 120f : 92f;
        float w = qr + 20f;
        float h = 34f + qr + 30f;
        Rect r = title ? new Rect(W - w - 16, 16, w, h) : new Rect(W - w - 12, H - h - 12, w, h);
        Panel(r, PanelTex(), 9);

        var head = S("qrHead", 10, C(0xdfffe0, 0.85f), TextAnchor.UpperCenter, wrap: true);
        Label(new Rect(r.x + 6, r.y + 6, r.width - 12, 28), "灯り役はスマホで\nQRを読み取り", head);

        Rect qrRect = new Rect(r.x + 10, r.y + 34, qr, qr);
        if (httpsUrl.StartsWith("https://"))
        {
            try { EnsureQrTexture(httpsUrl); }
            catch (System.Exception ex) { Debug.LogException(ex); qrTexture = null; }
            if (qrTexture != null) GUI.DrawTexture(qrRect, qrTexture);
        }
        else
        {
            Label(qrRect, "準備中…\n" + httpUrl, S("qrWait", 9, C(0x98b6a8), TextAnchor.MiddleCenter, wrap: true));
        }

        var status = S(phone ? "qrOn" : "qrOff", 10, phone ? C(0x9dffb0) : C(0x98b6a8), TextAnchor.MiddleCenter);
        string statusText = phone ? "● スマホ " + phoneCount + "人操作中" : "○ マウス操作中";
        Label(new Rect(r.x + 4, qrRect.yMax + 3, r.width - 8, 14), statusText, status);
        var url = S("qrUrl", 8, C(0x98b6a8, 0.8f), TextAnchor.MiddleCenter);
        Label(new Rect(r.x + 2, qrRect.yMax + 16, r.width - 4, 12), httpsUrl, url);
    }

    void EnsureQrTexture(string url)
    {
        if (qrTexture != null && qrUrl == url) return;
        qrUrl = url;
        bool[,] modules = QRCodeGenerator.Encode(url, out int size);
        const int moduleScale = 3, border = 4;
        int texSize = (size + border * 2) * moduleScale;
        var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var px = new Color32[texSize * texSize];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
        for (int row = 0; row < size; row++)
            for (int col = 0; col < size; col++)
            {
                if (!modules[row, col]) continue;
                int x0 = (col + border) * moduleScale, y0 = (size - 1 - row + border) * moduleScale;
                for (int dy = 0; dy < moduleScale; dy++)
                    for (int dx = 0; dx < moduleScale; dx++)
                        px[(y0 + dy) * texSize + x0 + dx] = new Color32(0, 0, 0, 255);
            }
        tex.SetPixels32(px);
        tex.Apply();
        if (qrTexture != null) Destroy(qrTexture);
        qrTexture = tex;
    }

    // ================= 描画ヘルパー =================
    static string FormatTime(float s) => ((int)(s / 60)).ToString("00") + ":" + ((int)(s % 60)).ToString("00");

    GUIStyle S(string key, int size, Color color, TextAnchor align, bool bold = false, bool wrap = false)
    {
        if (!styles.TryGetValue(key, out var s))
        {
            s = new GUIStyle { fontSize = size, alignment = align, wordWrap = wrap, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, clipping = TextClipping.Overflow };
            styles[key] = s;
        }
        s.normal.textColor = color;
        return s;
    }

    static void Label(Rect r, string text, GUIStyle s) => GUI.Label(r, text, s);

    // text-shadow: 0 1px 3px #000 の代わりに、少しずらした黒い文字を下に敷く
    void Shadowed(Rect r, string text, GUIStyle s, float offset = 1f)
    {
        Color c = s.normal.textColor;
        Color old = GUI.color;
        s.normal.textColor = new Color(0, 0, 0, 0.75f * c.a);
        GUI.Label(new Rect(r.x, r.y + offset, r.width, r.height), text, s);
        GUI.Label(new Rect(r.x + offset * 0.6f, r.y + offset, r.width, r.height), text, s);
        s.normal.textColor = c;
        GUI.color = old;
        GUI.Label(r, text, s);
    }

    void Fill(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, whiteTex);
        GUI.color = old;
    }

    // 9スライスで角丸を保ったまま任意サイズに引き伸ばす
    void Panel(Rect r, Texture2D tex, int radius)
    {
        string key = "panelStyle" + tex.GetInstanceID();
        if (!styles.TryGetValue(key, out var st))
        {
            st = new GUIStyle { border = new RectOffset(radius + 1, radius + 1, radius + 1, radius + 1) };
            st.normal.background = tex;
            styles[key] = st;
        }
        if (Event.current.type == EventType.Repaint) st.Draw(r, false, false, false, false);
    }

    Texture2D PanelTex() => Tex("hudPanel", () => RoundedBox(48, 9, C(0x06131b, 0.74f), C(0x739e90, 0.33f), 1));

    // 角丸のゲージ。グラデーションの帯を丸ごと描き、残量の幅でクリップする。
    void Bar(Rect r, float ratio, string key, Color a, Color b, string label, float trackAlpha = 0.15f)
    {
        int radius = Mathf.RoundToInt(r.height / 2f);
        Panel(r, Tex("track" + (int)r.height + trackAlpha, () => RoundedBox((int)r.height * 2 + 4, radius, new Color(1, 1, 1, trackAlpha), new Color(1, 1, 1, trackAlpha), 0)), radius);
        float fw = r.width * Mathf.Clamp01(ratio);
        if (fw > 0.5f)
        {
            GUI.BeginGroup(new Rect(r.x, r.y, fw, r.height));
            GUI.DrawTexture(new Rect(0, 0, r.width, r.height), Tex("bar" + key + (int)r.width + "x" + (int)r.height,
                () => Gradient((int)r.width * 2, (int)r.height * 2, a, b, radius * 2, a, 0, 90f)));
            GUI.EndGroup();
        }
        if (label != null) Label(new Rect(r.x + 6, r.y - 1, r.width, r.height + 1), label, S("barLabel", 9, new Color(1, 1, 1, 0.9f), TextAnchor.MiddleLeft));
    }

    Texture2D Tex(string key, System.Func<Texture2D> make)
    {
        if (!texCache.TryGetValue(key, out var t) || t == null) { t = make(); texCache[key] = t; }
        return t;
    }

    static float Coverage(float px, float py, int w, int h, float r)
    {
        float dx = Mathf.Max(Mathf.Max(r - px, 0f), Mathf.Max(px - (w - r), 0f));
        float dy = Mathf.Max(Mathf.Max(r - py, 0f), Mathf.Max(py - (h - r), 0f));
        if (dx <= 0f || dy <= 0f) return 1f;
        return Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
    }

    // 角丸 + 枠線の単色パネル
    static Texture2D RoundedBox(int size, float radius, Color fill, Color border, float borderW)
    {
        return Gradient(size, size, fill, fill, radius, border, borderW, 0f);
    }

    // 角丸 + 枠線 + 線形グラデーション(angleDeg は CSS の linear-gradient と同じ向き: 90=左→右, 180=上→下)
    static Texture2D Gradient(int w, int h, Color a, Color b, float radius, Color border, float borderW, float angleDeg)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[w * h];
        float ang = angleDeg * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Sin(ang), -Mathf.Cos(ang)); // CSS: 0deg=下→上
        float half = Mathf.Abs(w * dir.x) / 2f + Mathf.Abs(h * dir.y) / 2f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                // テクスチャは下→上、CSSは上→下なので y を反転して考える
                float cssY = h - fy;
                float t = angleDeg == 0f ? 0f : Mathf.Clamp01(((fx - w / 2f) * dir.x + (cssY - h / 2f) * dir.y) / (2f * half) + 0.5f);
                Color c = Color.Lerp(a, b, t);
                float outer = Coverage(fx, fy, w, h, radius);
                if (borderW > 0f)
                {
                    float inner = Coverage(fx - borderW, fy - borderW, w - (int)(borderW * 2), h - (int)(borderW * 2), Mathf.Max(0f, radius - borderW));
                    bool insideX = fx > borderW && fx < w - borderW, insideY = fy > borderW && fy < h - borderW;
                    if (!insideX || !insideY) inner = 0f;
                    // 半透明の枠は塗りの上に重ねる
                    Color over = Color.Lerp(c, new Color(border.r, border.g, border.b, 1f), border.a);
                    over.a = Mathf.Max(c.a, border.a);
                    c = Color.Lerp(over, c, inner);
                }
                c.a *= outer;
                px[y * w + x] = c;
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // 開始画面の背景: radial-gradient(ellipse at 50% 0, #204b49, #07161d 60%, #03090d)
    static Texture2D RadialBackground()
    {
        const int S = 256;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        Color c0 = NightMeshes.Hex(0x204b49), c1 = NightMeshes.Hex(0x07161d), c2 = NightMeshes.Hex(0x03090d);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S - 0.5f, v = 1f - (y + 0.5f) / S; // v: 上端0
                float d = Mathf.Sqrt((u / 0.707f) * (u / 0.707f) + (v / 1.414f) * (v / 1.414f));
                px[y * S + x] = d < 0.6f ? Color.Lerp(c0, c1, d / 0.6f) : Color.Lerp(c1, c2, (d - 0.6f) / 0.4f);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static Texture2D ShadowTex()
    {
        const int w = 64, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - w / 2f) / (w / 2f), dy = Mathf.Abs(y + 0.5f - h / 2f) / (h / 2f);
                float a = Mathf.Clamp01(1f - Mathf.Max(dx, dy)) * 0.6f;
                px[y * w + x] = new Color(0.87f, 0.79f, 0.52f, a * 0.35f);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // マップ選択カードの絵(モックアップ drawHabitatPreview と同じ描画。経路や球の場所は描かない)
    static Texture2D DrawHabitatPreview(string id)
    {
        const int S = 240;
        var px = new Color32[S * S];
        void Set(int x, int y, Color32 c) { if (x >= 0 && x < S && y >= 0 && y < S) px[(S - 1 - y) * S + x] = c; }
        void Rect(int x, int y, int w, int h, Color32 c) { for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++) Set(xx, yy, c); }
        void Circle(float cx, float cy, float r, Color32 c)
        {
            for (int yy = (int)(cy - r - 1); yy <= cy + r + 1; yy++)
                for (int xx = (int)(cx - r - 1); xx <= cx + r + 1; xx++)
                    if ((xx + 0.5f - cx) * (xx + 0.5f - cx) + (yy + 0.5f - cy) * (yy + 0.5f - cy) <= r * r) Set(xx, yy, c);
        }
        void Tri(float x, float top, Color32 c)
        {
            for (int yy = (int)top; yy < 210; yy++)
            {
                float k = (yy + 0.5f - top) / (210f - top);
                for (int xx = (int)(x - 25 * k); xx <= x + 25 * k; xx++) Set(xx, yy, c);
            }
        }
        Color32 Hx(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        Rect(0, 0, S, S, Hx(0x0a1b24));
        Circle(180, 46, 13, Hx(0xbed7ce));
        int[] layerColors = { 0x183739, 0x12332d, 0x0b2622 };
        for (int layer = 0; layer < 3; layer++)
            for (int i = 0; i < 8; i++)
                Tri(i * 36 - 12 + layer * 8, 85 + layer * 20 + (i % 3) * 14, Hx(layerColors[layer]));
        if (id == "B")
        {
            Rect(67, 115, 26, 85, Hx(0x526962)); Rect(147, 115, 26, 85, Hx(0x526962)); Rect(64, 102, 112, 20, Hx(0x526962));
            Rect(76, 201, 89, 5, Hx(0x748179)); Rect(86, 210, 69, 5, Hx(0x748179));
        }
        if (id == "C")
        {
            for (int yy = 182 - 29; yy <= 182 + 29; yy++)
                for (int xx = 120 - 78; xx <= 120 + 78; xx++)
                {
                    float ex = (xx + 0.5f - 120) / 78f, ey = (yy + 0.5f - 182) / 29f;
                    if (ex * ex + ey * ey <= 1f) Set(xx, yy, Hx(0x125064));
                }
            for (int i = 0; i < 5; i++) Rect(76 + i * 8, 170 + i * 8, 89 - i * 16, 1, Hx(0x59949a));
        }
        Circle(35, 191, 3, Hx(0x5fb698));
        Circle(203, 186, 3, Hx(0x5fb698));

        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}
