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
            DrawRankingPanel();
            DrawConnectPanel(true);
            return;
        }
        if (game.State == NightGame.GameState.NameEntry)
        {
            DrawNameEntry();
            DrawRankingPanel();
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
        DrawThreatAlert();
        DrawHint();
        DrawConnectPanel(false);

        if (game.State != NightGame.GameState.Ended) endStart = -1f;
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

        string paragraph = "球や敵にマウスの光を当て続けます。止まると擬態して一時的に無敵になり、敵を押し返します。擬態中は灯り役の光が2倍効き(待ち伏せ)、擬態中に敵を倒すと擬態ゲージが回復します。擬態ゲージは移動中に回復。約12秒ごとの閃光でも敵にダメージを与えられます。ランタンを約0.5秒照らすと点灯し、半径9.5mの同じ高さ・光の届く範囲の敵を倒します。点灯後も半径8.5mの敵に継続ダメージを与えます。途中で光を外すと蓄光はリセットされます。球は約2秒照らして実体化させ、触れて回収します。2つ揃うと入口の門が開きます。導きの花を約2秒連続で照らすと、花から最も近い未回収の球までの道が約7秒間光ります。光の道は橋や階段に沿って現れます。球が2つ揃った後は、花が入口の門まで案内します。門を通過するとクリアです。帰り道は敵の出現が少し速くなります。マップごとに難易度が違います(A やさしい / B ふつう / C むずかしい)。";
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

        var diff = NightDifficulty.For(id);
        var dStyle = S("cardDiff" + id, 11, diff.color, TextAnchor.MiddleCenter, bold: true);
        string dText = diff.Stars + " " + diff.label;
        float dw = dStyle.CalcSize(new GUIContent(dText)).x + 16f;
        Rect db = new Rect(r.x + 12, r.y + 12, dw, 22);
        Panel(db, Tex("diffBadgeBg", () => RoundedBox(24, 6, C(0x06131b, 0.88f), C(0x06131b, 0.88f), 0)), 6);
        Label(db, dText, dStyle);

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
            ? (mat.Source.Length > 0 ? "接続中(" + mat.Source + ")" : "接続中")
            : "未接続" + (mat != null && mat.SerialStatus.Length > 0 ? "  USB: " + mat.SerialStatus : "");
        Shadowed(new Rect(x, y, 220, 14), (matConnected ? "● " : "○ ") + "コントローラー: " + matText, matStyle);
        y += 14 + 4;

        float hpRatio = game.Hp / NightGame.HpMax;
        bool low = hpRatio < 0.3f;
        Bar(new Rect(x, y, 190, 11), hpRatio, low ? "hpLow" : "hp", low ? C(0xff5a4a) : C(0x6fe08a), low ? C(0xff8f7a) : C(0x9dffb0), "HP");
        if (game.HealFlash > 0f)
        {
            Fill(new Rect(x - 2, y - 2, 194, 15), new Color(0.6f, 1f, 0.65f, 0.55f * game.HealFlash));
            Label(new Rect(x + 196, y - 4, 60, 18), "+" + game.LastHeal, S("hpHeal", 12, new Color(0.65f, 1f, 0.7f, game.HealFlash), TextAnchor.MiddleLeft, bold: true));
        }
        y += 11 + 5;
        bool locked = game.CampLocked;
        Bar(new Rect(x, y, 190, 11), game.Camp / NightGame.CampMax, locked ? "campLocked" : "camp",
            locked ? C(0x666666) : C(0x3fa0c8), locked ? C(0x999999) : C(0x8fe6ff), "擬態ゲージ");
        if (game.AmbushRefillFlash > 0f)
        {
            Fill(new Rect(x - 2, y - 2, 194, 15), new Color(0.75f, 0.95f, 1f, 0.55f * game.AmbushRefillFlash));
            Label(new Rect(x + 196, y - 4, 60, 18), "+回復", S("campRefill", 11, new Color(0.75f, 0.95f, 1f, game.AmbushRefillFlash), TextAnchor.MiddleLeft, bold: true));
        }
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
        if (game.Camouflaged) { badge = "擬態中(無敵・光2倍)"; badgeColor = new Color(60 / 255f, 160 / 255f, 200 / 255f, 0.9f); }
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
        string threat = "脅威 Lv." + game.Threat + (game.Threat >= NightGame.ThreatMax ? " (最大)" : "");
        var threatStyle = S("threat" + Mathf.Min(game.Threat, 5), 13, Color.Lerp(C(0xffd9a0), C(0xff5a4a), (game.Threat - 1) / 4f), TextAnchor.MiddleRight, bold: true);
        string timer = FormatTime(game.Elapsed);
        float bw = badgeStyle.CalcSize(new GUIContent(badge)).x + 20f;
        float contentW = Mathf.Max(bw, killStyle.CalcSize(new GUIContent(kill)).x, threatStyle.CalcSize(new GUIContent(threat)).x, timerStyle.CalcSize(new GUIContent(timer)).x,
            S("flashSoon", 13, Color.white, TextAnchor.MiddleRight, bold: true).CalcSize(new GUIContent("閃光まで 12s")).x);
        Rect r = new Rect(W - 12 - (contentW + 24), 12, contentW + 24, 8 + 22 + 2 + 20 + 2 + 18 + 6 + 2 + 22 + 2 + 18 + 8);
        Panel(r, PanelTex(), 9);
        float x = r.x + 12, y = r.y + 8, w = contentW;

        GUI.color = badgeColor;
        Panel(new Rect(x + w - bw, y, bw, 22), Tex("pill", () => RoundedBox(32, 11, Color.white, Color.white, 0)), 11);
        GUI.color = Color.white;
        Label(new Rect(x + w - bw, y, bw, 22), badge, badgeStyle);
        y += 22 + 2;
        Shadowed(new Rect(x, y, w, 20), kill, killStyle);
        y += 20 + 2;
        // 脅威レベル。下の細いバーは「あとどれだけ回復すると次のレベルか」
        if (game.AlertTimer > 0f) // レベルが上がった直後は白く点滅
            threatStyle.normal.textColor = Color.Lerp(threatStyle.normal.textColor, Color.white, Mathf.Abs(Mathf.Sin(Time.unscaledTime * 10f)));
        Shadowed(new Rect(x, y, w, 18), threat, threatStyle);
        y += 18 + 1;
        Fill(new Rect(x + w - 90, y, 90, 4), new Color(1f, 1f, 1f, 0.12f));
        Fill(new Rect(x + w - 90, y, 90 * Mathf.Clamp01(game.ThreatProgress), 4), new Color(1f, 0.45f, 0.3f, 0.85f));
        y += 5 + 2;
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

    // 「敵が増えた！」の警告。ドンと拡大して出て、赤く脈打ち、最後にふわっと消える
    void DrawThreatAlert()
    {
        if (game.AlertTimer <= 0f) return;
        float t = NightGame.AlertDuration - game.AlertTimer;
        float appear = EaseOutBack(t / 0.35f);
        float alpha = Mathf.Clamp01(t / 0.12f) * Mathf.Clamp01(game.AlertTimer / 0.45f);
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 14f);
        float w = 420f, h = 78f;
        Rect r = new Rect(W / 2f - w / 2f, H * 0.3f, w, h);

        Matrix4x4 old = GUI.matrix;
        Color oldC = GUI.color;
        Vector2 c = r.center;
        float sc = Mathf.Lerp(0.6f, 1f, appear);
        GUI.matrix = old * Matrix4x4.TRS(new Vector3(c.x, c.y, 0), Quaternion.identity, new Vector3(sc, sc, 1f)) * Matrix4x4.TRS(new Vector3(-c.x, -c.y, 0), Quaternion.identity, Vector3.one);
        GUI.color = new Color(0.55f + 0.25f * pulse, 0.06f, 0.04f, 0.78f * alpha);
        Panel(r, Tex("pill", () => RoundedBox(32, 11, Color.white, Color.white, 0)), 11);
        Color line = new Color(1f, 0.55f, 0.4f, (0.35f + 0.5f * pulse) * alpha);
        Fill(new Rect(r.x + 18, r.y + 4, r.width - 36, 2), line);
        Fill(new Rect(r.x + 18, r.yMax - 6, r.width - 36, 2), line);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        Shadowed(new Rect(r.x, r.y + 10, r.width, 34), game.AlertText, S("alertMain", 28, C(0xfff0e0), TextAnchor.MiddleCenter, bold: true), 2f);
        Shadowed(new Rect(r.x, r.y + 46, r.width, 20), game.AlertSub, S("alertSub", 13, C(0xffd0c0), TextAnchor.MiddleCenter), 1f);
        GUI.color = oldC;
        GUI.matrix = old;
    }

    void DrawHint()
    {
        Color old = GUI.color;
        GUI.color = new Color(1, 1, 1, 0.75f);
        Shadowed(new Rect(16, H - 18 - 16, W - 32 - 190, 16), game.Hint, S("hint", 11, C(0xcdeccd), TextAnchor.MiddleLeft));
        GUI.color = old;
    }

    // ---- 結果画面のアニメーション ----
    // 暗転 → タイトルがドンと出る → 内訳が1行ずつ流れ込む → スコアがカウントアップ → 順位がポンと弾む(紙吹雪) → ボタン。
    // 画面をクリックすると、アニメーションを飛ばして最後まで表示する。
    float endStart = -1f;
    bool endSkip, endPopSfx;

    static float EaseOutCubic(float x) { x = Mathf.Clamp01(x); return 1f - Mathf.Pow(1f - x, 3f); }
    static float EaseOutBack(float x) { x = Mathf.Clamp01(x); const float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f); }
    static float Seg(float t, float start, float dur) => Mathf.Clamp01((t - start) / dur);

    // 拡大縮小・透明度・横ずれを付けて文字を描く(拡大の中心は枠の中心)
    void LabelFx(Rect r, string text, GUIStyle st, float scale, float alpha, float dx = 0f)
    {
        if (alpha <= 0.001f) return;
        Matrix4x4 old = GUI.matrix;
        Color oldC = GUI.color;
        Vector2 pivot = r.center;
        GUI.matrix = old * Matrix4x4.TRS(new Vector3(pivot.x + dx, pivot.y, 0), Quaternion.identity, new Vector3(scale, scale, 1f)) * Matrix4x4.TRS(new Vector3(-pivot.x, -pivot.y, 0), Quaternion.identity, Vector3.one);
        GUI.color = new Color(1, 1, 1, alpha);
        GUI.Label(r, text, st);
        GUI.color = oldC;
        GUI.matrix = old;
    }

    void DrawConfetti(float tt)
    {
        Color[] pal = { C(0xff6b6b), C(0x6bdfff), C(0xffe066), C(0xb388ff), C(0x6bff95), C(0xff9f6b), C(0xff6bd6) };
        float fade = 1f - Seg(tt, 6f, 2f);
        if (fade <= 0f) return;
        for (int i = 0; i < 48; i++)
        {
            float h1 = Mathf.Repeat(Mathf.Sin(i * 12.9898f) * 43758.5453f, 1f);
            float h2 = Mathf.Repeat(Mathf.Sin(i * 78.233f) * 12345.678f, 1f);
            float speed = 90f + h2 * 150f, size = 6f + h1 * 6f;
            float y = Mathf.Repeat(tt * speed + h1 * H, H + 40f) - 20f;
            float x = h2 * W + Mathf.Sin(tt * 3f + i) * 22f;
            Color c = pal[i % pal.Length]; c.a = fade;
            Fill(new Rect(x, y, size, size * 0.6f), c);
        }
    }

    void DrawEndOverlay()
    {
        if (endStart < 0f) { endStart = Time.unscaledTime; endSkip = false; endPopSfx = false; }
        if (Event.current.type == EventType.MouseDown) endSkip = true;
        float t = endSkip ? 99f : Time.unscaledTime - endStart;

        Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.82f * Seg(t, 0f, 0.4f)));
        bool win = game.Cleared;
        float cx = W / 2f, y = Mathf.Max(24f, H / 2f - 250f);

        float tp = Seg(t, 0.15f, 0.5f);
        LabelFx(new Rect(0, y, W, 50), win ? "門を抜けて脱出した！" : "力尽きた…",
            S(win ? "endWin" : "endLose", 38, win ? C(0x9dffb0) : C(0xff8f7a), TextAnchor.MiddleCenter, bold: true), Mathf.Lerp(1.6f, 1f, EaseOutBack(tp)), tp);
        y += 56;
        LabelFx(new Rect(0, y, W, 20), game.PlayerName + "  /  " + game.MapId + ". " + game.Map.Name + "  /  " + (win ? "クリアタイム " : "生存 ") + FormatTime(game.Elapsed) + "  /  光の球 " + game.OrbCount + " / " + NightGame.OrbTotal,
            S("endMap", 13, new Color(1, 1, 1, 0.75f), TextAnchor.MiddleCenter), 1f, Seg(t, 0.6f, 0.3f));
        y += 30;

        // スコアの内訳(クリアタイムと撃破数を合わせたスコア)
        var row = S("endRow", 15, new Color(1, 1, 1, 0.9f), TextAnchor.MiddleLeft);
        var val = S("endVal", 15, new Color(1, 1, 1, 0.9f), TextAnchor.MiddleRight);
        float rx = cx - 190f, rw = 380f;
        int s0 = game.ScoreSeconds, kills = game.KillCount;
        var rows = new List<KeyValuePair<string, int>>();
        if (win)
        {
            rows.Add(new KeyValuePair<string, int>("クリアボーナス", NightRanking.ClearBonus));
            rows.Add(new KeyValuePair<string, int>("撃破 " + kills + "体 × " + NightRanking.KillPoints, kills * NightRanking.KillPoints));
            rows.Add(new KeyValuePair<string, int>("時間ボーナス (あと" + Mathf.Max(0, NightRanking.TimeBase - s0) + "秒 × " + NightRanking.TimePoints + ")", Mathf.Max(0, NightRanking.TimeBase - s0) * NightRanking.TimePoints));
        }
        else
        {
            rows.Add(new KeyValuePair<string, int>("撃破 " + kills + "体 × " + NightRanking.KillPoints, kills * NightRanking.KillPoints));
            rows.Add(new KeyValuePair<string, int>("生存 " + s0 + "秒 × " + NightRanking.SurvivePoints, s0 * NightRanking.SurvivePoints));
        }
        for (int i = 0; i < rows.Count; i++)
        {
            float rp = Seg(t, 0.9f + i * 0.3f, 0.3f);
            float dx = (1f - EaseOutCubic(rp)) * 40f;
            LabelFx(new Rect(rx, y, rw, 24), rows[i].Key, row, 1f, rp, dx);
            LabelFx(new Rect(rx, y, rw, 24), rows[i].Value.ToString("N0"), val, 1f, rp, dx);
            y += 26;
        }
        float rowsEnd = 0.9f + rows.Count * 0.3f + 0.2f;
        Fill(new Rect(rx, y + 2, rw, 1), new Color(1, 1, 1, 0.3f * Seg(t, rowsEnd, 0.2f)));
        y += 8;

        // 合計スコア: 0 からカウントアップし、数えきれた瞬間にポンと弾む
        float sp = Seg(t, rowsEnd + 0.1f, 1.3f);
        int shown = Mathf.RoundToInt(game.Score * EaseOutCubic(sp));
        float pop = Seg(t, rowsEnd + 1.4f, 0.35f);
        float scoreScale = sp < 1f ? 1f : 1f + 0.3f * (1f - EaseOutCubic(pop));
        float sa = Seg(t, rowsEnd, 0.2f);
        LabelFx(new Rect(rx, y, rw, 40), "スコア", S("endScoreL", 18, C(0xffd76a), TextAnchor.MiddleLeft, bold: true), 1f, sa);
        LabelFx(new Rect(rx, y, rw, 40), shown.ToString("N0"), S("endScoreV", 32, C(0xffd76a), TextAnchor.MiddleRight, bold: true), scoreScale, sa);
        y += 50;

        // 順位: 数えきれたあとに大きく弾んで出る。10位以内なら紙吹雪、3位以内は金色に光る
        float tRank = rowsEnd + 1.5f;
        float rk = Seg(t, tRank, 0.55f);
        var top = NightRanking.Top(game.MapId);
        string rankText = game.Rank > 0 ? game.MapId + "マップ  " + game.Rank + "位  TOP" + NightRanking.MaxEntries + " 入り！"
            : game.MapId + "マップ  ランク外" + (top.Count >= NightRanking.MaxEntries ? "(" + NightRanking.MaxEntries + "位は " + top[top.Count - 1].score.ToString("N0") + "点)" : "");
        if (game.Rank > 0 && game.Rank <= 3 && rk > 0f)
        {
            float glow = 0.35f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f);
            Color oldG = GUI.color;
            GUI.color = new Color(1f, 0.84f, 0.3f, glow * rk);
            GUI.DrawTexture(new Rect(cx - 260, y - 30, 520, 100), Tex("glow", () => GlowTex()));
            GUI.color = oldG;
        }
        LabelFx(new Rect(0, y, W, 34), rankText, S(game.Rank > 0 ? "endRankOn" : "endRankOff", 22, game.Rank > 0 ? C(0x9dffb0) : C(0xb5c8bd), TextAnchor.MiddleCenter, bold: true),
            Mathf.Lerp(2.2f, 1f, EaseOutBack(rk)), rk);
        if (t >= tRank && !endPopSfx && game.Rank > 0) { endPopSfx = true; game.Audio.PlayDownChime(); }
        if (game.Rank > 0 && t > tRank) DrawConfetti(t - tRank);
        y += 54;

        // ボタン(最後にふわっと出る)
        float ba = Seg(t, tRank + 0.5f, 0.4f);
        Color prev = GUI.color;
        GUI.color = new Color(1, 1, 1, ba);
        Rect btn = new Rect(cx - 110, y, 220, 42);
        bool hover = btn.Contains(Event.current.mousePosition);
        Panel(btn, Tex("endBtn" + hover, () => RoundedBox(32, 8, hover ? C(0x46a058) : C(0x3a8a4a), hover ? C(0x46a058) : C(0x3a8a4a), 0)), 8);
        Label(btn, "もう一度やり直す (R)", S("endBtnText", 15, Color.white, TextAnchor.MiddleCenter));
        Rect btn2 = new Rect(cx - 110, y + 52, 220, 42);
        bool hover2 = btn2.Contains(Event.current.mousePosition);
        Panel(btn2, Tex("endBtn2" + hover2, () => RoundedBox(32, 8, hover2 ? C(0x3a6f8a) : C(0x2f5d75), hover2 ? C(0x3a6f8a) : C(0x2f5d75), 0)), 8);
        Label(btn2, "ステージ選択へ (T)", S("endBtnText", 15, Color.white, TextAnchor.MiddleCenter));
        GUI.color = prev;
        if (GUI.Button(btn, GUIContent.none, GUIStyle.none)) game.Restart();
        if (GUI.Button(btn2, GUIContent.none, GUIStyle.none)) game.BackToTitle();
    }

    static Texture2D GlowTex()
    {
        const int w = 128, h = 32;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        for (int yy = 0; yy < h; yy++)
            for (int xx = 0; xx < w; xx++)
            {
                float dx = (xx + 0.5f - w / 2f) / (w / 2f), dy = (yy + 0.5f - h / 2f) / (h / 2f);
                px[yy * w + xx] = new Color(1, 1, 1, Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)) * 0.9f);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    string nameText = "";
    float nameEntryStamp = -1f;

    // コースを決めたあと、ゲームを始める前の「名前入力」画面。名前はそのコースのランキングに載る。
    void DrawNameEntry()
    {
        GUI.DrawTexture(new Rect(0, 0, W, H), Tex("startBg", () => RadialBackground()), ScaleMode.StretchToFill);
        if (!Mathf.Approximately(nameEntryStamp, game.NameEntryTime)) { nameEntryStamp = game.NameEntryTime; nameText = ""; }

        float cx = W / 2f, cy = H / 2f;
        Rect card = new Rect(cx - 270, cy - 190, 540, 380);
        Panel(card, Tex("pausePanel", () => RoundedBox(48, 12, C(0x06131b, 0.94f), C(0x739e90, 0.45f), 1)), 12);
        float y = card.y + 24;
        Label(new Rect(card.x, y, card.width, 28), game.MapId + ". " + game.Map.Name + "   " + game.Difficulty.Stars + " " + game.Difficulty.label, S("neMap", 20, C(0xdfc985), TextAnchor.MiddleCenter, bold: true));
        y += 40;
        Label(new Rect(card.x, y, card.width, 34), "あなたの名前を入力してください", S("neTitle", 24, C(0xe6f5e9), TextAnchor.MiddleCenter, bold: true));
        y += 40;
        Label(new Rect(card.x + 20, y, card.width - 40, 36), "このコースのランキングに載る名前です(" + NightRanking.MaxNameLength + "文字まで)。\n入力しなくてもOK。その場合は「" + NightRanking.DefaultName + "」で始まります。",
            S("neHint", 12, C(0xa9c4b8), TextAnchor.UpperCenter, wrap: true));
        y += 50;

        var fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
        fieldStyle.normal.textColor = Color.white; fieldStyle.focused.textColor = Color.white;
        GUI.SetNextControlName("playerName");
        nameText = GUI.TextField(new Rect(cx - 140, y, 280, 48), nameText, NightRanking.MaxNameLength, fieldStyle);
        if (Time.unscaledTime - game.NameEntryTime < 0.4f) GUI.FocusControl("playerName"); // 開いた直後は入力欄を選択済みにする
        bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
        if (enter && Time.unscaledTime - game.NameEntryTime > 0.25f) { Event.current.Use(); game.BeginPlay(nameText); return; }
        y += 64;

        Rect start = new Rect(cx - 140, y, 280, 48);
        bool hs = start.Contains(Event.current.mousePosition);
        GUI.DrawTexture(start, Tex("startBtn" + hs, () => Gradient(280, 46, C(0x2e7974), C(0x246257), 8, C(0x8ddccb, hs ? 0.9f : 0.53f), 1, 120f)));
        Label(start, "スタート (Enter)", S("startBtnText", 16, Color.white, TextAnchor.MiddleCenter));
        if (GUI.Button(start, GUIContent.none, GUIStyle.none)) { game.BeginPlay(nameText); return; }
        y += 56;

        Rect guest = new Rect(cx - 140, y, 280, 36);
        bool hg = guest.Contains(Event.current.mousePosition);
        Label(guest, "名前なしで始める(" + NightRanking.DefaultName + ")", S(hg ? "neGuestH" : "neGuest", 14, hg ? Color.white : C(0xa9c4b8), TextAnchor.MiddleCenter));
        if (GUI.Button(guest, GUIContent.none, GUIStyle.none)) { game.BeginPlay(""); return; }
        y += 38;

        Rect back = new Rect(cx - 140, y, 280, 30);
        bool hb = back.Contains(Event.current.mousePosition);
        Label(back, "← コース選択へ戻る (Esc)", S(hb ? "neBackH" : "neBack", 13, hb ? Color.white : C(0x8fada4), TextAnchor.MiddleCenter));
        if (GUI.Button(back, GUIContent.none, GUIStyle.none)) game.CancelNameEntry();

        Label(new Rect(16, H - 24, W - 32, 18), "灯り役のスマホは、名前の入力なしでQRを読み取るだけで参加できます", S("neKeys", 10, C(0x8fada4, 0.8f), TextAnchor.MiddleLeft));
    }

    void ScoreRow(ref float y, float x, float w, GUIStyle row, GUIStyle val, string label, int value)
    {
        Label(new Rect(x, y, w, 24), label, row);
        Label(new Rect(x, y, w, 24), value.ToString("N0"), val);
        y += 26;
    }

    // タイトル画面の左に、選択中のマップのランキング(TOP10)を見やすく出す。
    // 1〜3位はメダル色の丸、行は交互に色を変え、文字は大きめ。スコアは右揃えで桁が見比べやすい。
    void DrawRankingPanel()
    {
        if (W < 1000f) return; // 狭い画面では中央のカードと重なるので出さない
        var top = NightRanking.Top(game.MapId);
        const float w = 252f, rowH = 28f;
        bool anyFail = top.Exists(e => !e.cleared);
        float h = 74f + Mathf.Max(top.Count, 3) * rowH + 12f + (anyFail ? 18f : 0f);
        Rect r = new Rect(10, 16, w, h);
        Panel(r, PanelTex(), 9);
        Label(new Rect(r.x + 14, r.y + 10, w - 28, 24), game.MapId + ". " + game.Map.Name, S("rkHead", 17, C(0xdfffe0), TextAnchor.MiddleLeft, bold: true));
        var rd = game.Difficulty;
        Label(new Rect(r.x + 14, r.y + 34, w - 28, 18), rd.Stars + " " + rd.label, S("rkDiff" + game.MapId, 12, rd.color, TextAnchor.MiddleRight, bold: true));
        Label(new Rect(r.x + 14, r.y + 34, w - 28, 18), "TOP " + NightRanking.MaxEntries + " ランキング", S("rkSub", 13, C(0xdfc985), TextAnchor.MiddleLeft, bold: true));
        Label(new Rect(r.x + 14, r.y + 52, w - 28, 16), "スコア = クリアタイム + 撃破数", S("rkSub2", 11, C(0x9fbdb0), TextAnchor.MiddleLeft));
        float y = r.y + 74f;
        if (top.Count == 0)
        {
            Label(new Rect(r.x + 14, y, w - 28, 60), "まだ記録がありません。\n最初の1位を目指そう！", S("rkNone", 14, C(0xc4dacd), TextAnchor.UpperLeft));
            return;
        }
        var dotTex = Tex("playerDot", () => RoundedBox(32, 16, Color.white, Color.white, 0));
        for (int i = 0; i < top.Count; i++)
        {
            var e = top[i];
            if (i % 2 == 0) Fill(new Rect(r.x + 6, y, w - 12, rowH - 2), new Color(1, 1, 1, 0.06f));
            Color medal = i == 0 ? C(0xffd76a) : i == 1 ? C(0xd5dde0) : i == 2 ? C(0xe0a070) : Color.clear;
            if (i < 3)
            {
                Color old = GUI.color;
                GUI.color = medal;
                GUI.DrawTexture(new Rect(r.x + 12, y + 3, 22, 22), dotTex);
                GUI.color = old;
                Label(new Rect(r.x + 12, y + 3, 22, 22), (i + 1).ToString(), S("rkMedal", 13, C(0x1a1400), TextAnchor.MiddleCenter, bold: true));
            }
            else Label(new Rect(r.x + 12, y + 2, 22, 24), (i + 1).ToString(), S("rkNum", 14, C(0xa9c4b8), TextAnchor.MiddleCenter));
            Color tc = i < 3 ? medal : C(0xe6f5e9);
            string nm = e.name + (e.cleared ? "" : " *");
            Label(new Rect(r.x + 42, y + 1, 120, 26), nm, S("rkName" + (i < 3 ? i : 3), 14, tc, TextAnchor.MiddleLeft, bold: i < 3));
            Label(new Rect(r.x + 150, y + 1, w - 164, 26), e.score.ToString("N0"), S("rkScore" + (i < 3 ? i : 3), 15, tc, TextAnchor.MiddleRight, bold: true));
            y += rowH;
        }
        if (anyFail) Label(new Rect(r.x + 14, y + 2, w - 28, 16), "* 途中終了", S("rkNote", 11, C(0x8fada4), TextAnchor.MiddleLeft));
    }

    // ESCで開く一時停止メニュー(開発者向けに明るさ・音量を調整できる)
    void DrawPauseMenu()
    {
        Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.35f));
        Rect card = new Rect(W / 2f - 200, H / 2f - 205, 400, 410);
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
        if (MenuButton(new Rect(card.x + 24, card.y + 316, card.width - 48, 42), "ステージ選択へ戻る (T)")) game.BackToTitle();
        Label(new Rect(card.x, card.y + 372, card.width, 18), "ESCでも再開できます", S("pauseHint", 11, C(0x8fada4), TextAnchor.MiddleCenter));
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
        float h = 34f + qr + 30f + 28f;
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
        DrawPlayers(new Rect(r.x + 4, qrRect.yMax + 30, r.width - 8, 20));
    }

    // 接続中のスマホ(灯り役)の一覧。参加順に、その人のライトの色の丸と番号を並べる。
    void DrawPlayers(Rect r)
    {
        var colors = PointerServer.Instance != null ? PointerServer.Instance.GetConnectedColors() : new List<Color>();
        var label = S("plLabel", 10, C(0xdfffe0, 0.9f), TextAnchor.MiddleCenter);
        if (colors.Count == 0)
        {
            Label(r, "接続中: 0/" + PointerServer.MaxPlayers + "人", S("plNone", 10, C(0x98b6a8), TextAnchor.MiddleCenter));
            return;
        }
        const float d = 12f, gap = 2f;
        float total = colors.Count * d + (colors.Count - 1) * gap;
        float x = r.x + (r.width - total) / 2f;
        var dotTex = Tex("playerDot", () => RoundedBox(32, 16, Color.white, Color.white, 0));
        for (int i = 0; i < colors.Count; i++)
        {
            Color old = GUI.color;
            GUI.color = colors[i];
            GUI.DrawTexture(new Rect(x + i * (d + gap), r.y, d, d), dotTex);
            GUI.color = old;
        }
        Label(new Rect(r.x, r.y + d + 1, r.width, 12), "接続中 " + colors.Count + "/" + PointerServer.MaxPlayers + "人", label);
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
