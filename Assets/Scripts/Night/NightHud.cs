using System.Collections.Generic;
using UnityEngine;

// 光の球収集版のUI。モックアップ(HTML/CSS)の見た目を IMGUI で再現する。
// 画面の高さ760pxを基準に全体を拡大縮小し、どの解像度でもモックアップと同じ配置になるようにする。
public class NightHud : MonoBehaviour
{
    public NightGame game;

    float scale = 1f, W, H;
    // 画面の高さをこの値として拡大縮小する(小さいほど文字やパネルが大きくなる)。
    // タイトル・名前入力は左右にランキングと接続パネルが並び横幅が足りなくなるため、プレイ中より控えめにする
    const float BaseHeight = 580f, TitleBaseHeight = 700f;
    const float SidePanelSpace = 272f; // 左右のパネルの幅+余白(中央の内容がここに重ならないようにする)
    float toastAlpha;
    Color vignetteColor = new Color(1f, 0.16f, 0.08f, 0f);

    Font uiFont; // 画面の文字はすべて RocknRoll One(太めで読みやすい手書き風ゴシック。SIL OFL)
    readonly Dictionary<string, GUIStyle> styles = new Dictionary<string, GUIStyle>();
    readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
    readonly Dictionary<string, Texture2D> previews = new Dictionary<string, Texture2D>();
    Texture2D whiteTex, markTex, qrTexture;
    Texture2D logoTex; // タイトルロゴ(カメレオンの絵+「カメレオンサバイバー」。promo_video の TitleLogo で作った画像)
    string qrUrl;

    static Color C(int rgb, float a = 1f) => NightMeshes.Hex(rgb, a);

    void Awake()
    {
        if (game == null) game = GetComponent<NightGame>();
        uiFont = Resources.Load<Font>("Fonts/RocknRollOne-Regular");
        if (uiFont == null) uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo" }, 16);
        whiteTex = Texture2D.whiteTexture;
        markTex = UITextureFactory.CreateChameleonMark(64, C(0xf5a623), C(0x22c55e), new Color(0.9f, 0.93f, 0.75f));
        logoTex = Resources.Load<Texture2D>("UI/TitleLogo");
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
        bool menu = game.State == NightGame.GameState.Title || game.State == NightGame.GameState.NameEntry;
        scale = Mathf.Clamp(Screen.height / (menu ? TitleBaseHeight : BaseHeight), 0.7f, 3f);
        W = Screen.width / scale;
        H = Screen.height / scale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        GUI.skin.font = uiFont;

        if (game.State == NightGame.GameState.Title)
        {
            bool zoom = QrZoomOpen;
            GUI.enabled = !zoom; // 拡大表示中は下のボタンを押せないようにする
            DrawStartOverlay();
            DrawRankingPanel();
            DrawConnectPanel(true);
            GUI.enabled = true;
            DrawQrZoom();
            return;
        }
        QrZoomOpen = false;
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

        // 結果画面ではプレイ中の表示を出さない(結果の文字と重なるため)
        bool playing = game.State != NightGame.GameState.Ended;
        if (playing)
        {
            DrawEnemyMarks();
            DrawObjectGauges();
            DrawObjectiveArrow();
            DrawPlayerBars();
            DrawHud();
            DrawGoalHud();
            DrawTopRight();
            DrawToast();
            DrawThreatAlert();
            DrawAnnounce();
            DrawCountdown();
            DrawHint();
        }
        DrawConnectPanel(false);

        if (game.State != NightGame.GameState.Ended) endStart = -1f;
        if (game.State == NightGame.GameState.Paused) DrawPauseMenu();
        if (game.State == NightGame.GameState.Ended) DrawEndOverlay();
    }

    // ================= 開始画面 =================
    void DrawStartOverlay()
    {
        GUI.DrawTexture(new Rect(0, 0, W, H), Tex("startBg", () => RadialBackground()), ScaleMode.StretchToFill);

        bool sidePanels = W >= 1000f; // DrawRankingPanel と同じ条件
        // マップ(難易度)選択のカード。タイトルが目立つよう、カードは控えめな大きさにする
        float maxW = Mathf.Min(660f, W - (sidePanels ? 2f * SidePanelSpace : 48f));
        float cardW = Mathf.Floor((maxW - 32f) / 3f);
        bool compact = H < 760f;
        float preview = Mathf.Min(cardW - 24f, compact ? 140f : 170f);
        float cardH = 14f + preview + 12f + 22f + 14f;

        // タイトルロゴ。左右のパネルに重ならない幅いっぱいまで大きく出す(ロゴ画像がなければ文字のタイトル)。
        // 操作説明やマップの説明は出さず、ロゴ・マップ選択・開始ボタンだけのすっきりした画面にする
        float titleRoom = W - (sidePanels ? 2f * SidePanelSpace : 48f);
        float logoH = 56f, logoW = 0f;
        if (logoTex != null)
        {
            logoH = compact ? 170f : 230f;
            logoW = logoH * logoTex.width / logoTex.height;
            if (logoW > titleRoom) { logoH *= titleRoom / logoW; logoW = titleRoom; }
        }

        float gap = compact ? 22f : 36f;
        // ロゴの下に一文だけのサブタイトル(遠くからでも読める大きさ)
        const string Subtitle = "スマホのライトで敵を照らして、カメレオンと夜の森を生き抜け！";
        var subStyle = S("subtitle", compact ? 20 : 26, C(0xe6f5e9), TextAnchor.MiddleCenter, bold: true);
        float subH = subStyle.fontSize * 1.6f;

        float total = logoH + 6 + subH + gap + cardH + gap + 46;
        float y = Mathf.Max(16f, (H - total) / 2f);
        float cx = W / 2f;

        if (logoTex != null)
        {
            GUI.DrawTexture(new Rect(cx - logoW / 2f, y, logoW, logoH), logoTex, ScaleMode.ScaleToFit, true);
        }
        else
        {
            const string TitleText = "カメレオンサバイバー";
            var title = S("title", 48, C(0xe6f5e9), TextAnchor.MiddleCenter);
            title.fontSize = 48;
            float titleW = title.CalcSize(new GUIContent(TitleText)).x;
            if (titleW > titleRoom) title.fontSize = Mathf.Max(24, Mathf.FloorToInt(48 * titleRoom / titleW));
            Label(new Rect(0, y, W, 56), TitleText, title);
        }
        y += logoH + 6;
        Shadowed(new Rect(0, y, W, subH), Subtitle, subStyle, 2f);
        y += subH + gap;

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
        y += cardH + gap;

        Rect btn = new Rect(cx - 140, y, 280, 46);
        bool btnHover = btn.Contains(Event.current.mousePosition);
        GUI.DrawTexture(btn, Tex("startBtn" + btnHover, () => Gradient(280, 46, C(0x2e7974), C(0x246257), 8, C(0x8ddccb, btnHover ? 0.9f : 0.53f), 1, 120f)));
        Label(btn, "このマップでプレイ →", S("startBtnText", 16, Color.white, TextAnchor.MiddleCenter));
        if (GUI.Button(btn, GUIContent.none, GUIStyle.none)) game.StartGame();

        // 始める前にマットが反応するか確かめられるよう、タイトルでも左下に大きめに接続状態と踏んでいるパッドを出す
        DrawMatStatus(20, H - 20 - MatStatusHeight(TitleMatScale, 14), TitleMatScale, 14);
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
        // HPバー・擬態ゲージは主人公の頭上に出す(DrawPlayerBars)。ここはマップ名・操作・接続状態だけ
        var row = S("hudRow", 10, C(0xdfffe0, 0.82f), TextAnchor.UpperLeft, wrap: true);
        string rowText = "移動: WASD/矢印　灯り: " + (game.Flashlight.HasPhoneInput ? "スマホ" : "スマホ未接続") + "　止まる=擬態　石を照らして蛍を集めよう";
        float rowH = row.CalcHeight(new GUIContent(rowText), 220);
        Rect r = new Rect(12, 12, 248, 10 + 21 + rowH + 10);
        Panel(r, PanelTex(), 9);
        float x = r.x + 14, y = r.y + 10;
        GUI.DrawTexture(new Rect(x, y + 1, 16, 16), markTex);
        Shadowed(new Rect(x + 21, y, r.width - 49, 18), game.MapId + ". " + game.Map.Name, S("hudH1", 14, C(0xdfffe0, 0.9f), TextAnchor.MiddleLeft, bold: true));
        y += 21;
        Shadowed(new Rect(x, y, 220, rowH), rowText, row);

        // マットの反応はプレイ中は小さめにして左下(下のヒントの上)に出す
        DrawMatStatus(16, H - 18 - 16 - 10 - MatStatusHeight(PlayMatScale, 10), PlayMatScale, 10);
    }

    // 圧力マット(ESP32)との接続表示と、どのパッドが踏まれているか。タイトル画面とプレイ中の左下に出す。
    // 展示中に接続が切れていないか一目で分かるように。
    // k はマットの図の大きさの倍率、fontSize は文字の大きさ(図だけ大きくして、文字は控えめにできる)
    const float MatCell = 10f; // マットの図の1マス(8px + すき間2px)
    const float PlayMatScale = 1.3f, TitleMatScale = 2.5f;
    static float MatStatusHeight(float k, int fontSize) => fontSize * 1.4f + 4 + MatCell * 5 * k; // 接続表示の行 + マットの図(5マス四方)

    void DrawMatStatus(float x, float y, float k, int fontSize)
    {
        var mat = PressureMatServer.Instance;
        bool matConnected = mat != null && mat.Connected;
        var matStyle = S((matConnected ? "matOn" : "matOff") + fontSize, fontSize, matConnected ? C(0x9dffb0) : C(0xff8f7a), TextAnchor.MiddleLeft);
        string matText = matConnected
            ? (mat.Source.Length > 0 ? "接続中(" + mat.Source + ")" : "接続中")
            : "未接続"; // 詳しい理由(USBの状態など)は出さず、画面の情報を少なくする
        Shadowed(new Rect(x, y, 300, fontSize * 1.4f), (matConnected ? "● " : "○ ") + "コントローラー: " + matText, matStyle);
        y += fontSize * 1.4f + 4;

        // マットのどこが踏まれているか。実物のマットと同じ並び(上=前・下=後ろの横長、左右の縦長)で、
        // 踏んでいるパッドが緑に光る。マットの調子を確かめる用
        //    ■■■
        //  ■      ■
        //  ■      ■
        //  ■      ■
        //    ■■■
        float c = MatCell * k, g = 2f * k;
        Shadowed(new Rect(x, y + c * 2, fontSize * 3.4f, c), "マット", S("matLabel" + fontSize, fontSize, C(0xdfffe0, 0.82f), TextAnchor.MiddleLeft));
        float ox = x + fontSize * 3.6f;
        bool[] on = mat != null && mat.HasMatInput
            ? new[] { mat.Front, mat.Back, mat.Left, mat.Right }
            : new bool[4];
        Rect[] pads =
        {
            new Rect(ox + c, y, c * 3 - g, c - g),         // 前(上)
            new Rect(ox + c, y + c * 4, c * 3 - g, c - g), // 後ろ(下)
            new Rect(ox, y + c, c - g, c * 3 - g),         // 左
            new Rect(ox + c * 4, y + c, c - g, c * 3 - g), // 右
        };
        for (int i = 0; i < 4; i++)
            Fill(pads[i], on[i] ? C(0x9dffb0) : new Color(1f, 1f, 1f, matConnected ? 0.18f : 0.08f));
    }

    void DrawGoalHud()
    {
        string text = game.GateOpened ? "門が開いた！入口へ戻ろう" : "蛍 " + game.OrbCount + " / " + NightGame.OrbTotal;
        var style = S("goal", 14, C(0xdfffe0), TextAnchor.MiddleCenter, bold: true);
        // いまやることを、パネルの下の段に常に出す
        string todo = "いまやること: " + game.Objective;
        var todoStyle = S("goalTodo", 14, C(0xffe9a0), TextAnchor.MiddleCenter, bold: true);
        float w = Mathf.Max(250f, style.CalcSize(new GUIContent(text)).x + 30f, todoStyle.CalcSize(new GUIContent(todo)).x + 34f);
        Rect r = new Rect(W / 2f - w / 2f, 12, w, 68);
        Panel(r, PanelTex(), 9);
        Shadowed(new Rect(r.x, r.y + 8, r.width, 18), text, style);
        Bar(new Rect(W / 2f - 110f, r.y + 30, 220, 8), game.OrbCount / (float)NightGame.OrbTotal, "prog", C(0xffdd55), C(0x9dffb0), null, 0.18f);
        Shadowed(new Rect(r.x, r.y + 44, r.width, 18), todo, todoStyle);
    }

    void DrawTopRight()
    {
        string badge; Color badgeColor;
        if (game.Camouflaged) { badge = "擬態中(無敵・光2倍)"; badgeColor = new Color(60 / 255f, 160 / 255f, 200 / 255f, 0.9f); }
        else if (game.CampLocked) { badge = "ゲージ切れ"; badgeColor = new Color(120 / 255f, 120 / 255f, 120 / 255f, 0.85f); }
        else { badge = "移動中"; badgeColor = new Color(190 / 255f, 110 / 255f, 50 / 255f, 0.9f); }


        var badgeStyle = S("badge", 12, Color.white, TextAnchor.MiddleCenter, bold: true);
        var killStyle = S("kill", 14, C(0xffb0b0), TextAnchor.MiddleRight);
        var timerStyle = S("timer", 16, C(0xbbccdd), TextAnchor.MiddleRight);

        string kill = "撃破: " + game.KillCount;
        string threat = "脅威 Lv." + game.Threat + (game.Threat >= NightGame.ThreatMax ? " (最大)" : "");
        var threatStyle = S("threat" + Mathf.Min(game.Threat, 5), 13, Color.Lerp(C(0xffd9a0), C(0xff5a4a), (game.Threat - 1) / 4f), TextAnchor.MiddleRight, bold: true);
        string timer = FormatTime(game.Elapsed);
        float bw = badgeStyle.CalcSize(new GUIContent(badge)).x + 20f;
        float contentW = Mathf.Max(bw, killStyle.CalcSize(new GUIContent(kill)).x, threatStyle.CalcSize(new GUIContent(threat)).x, timerStyle.CalcSize(new GUIContent(timer)).x);
        // 雷(閃光)までの秒数は出さない(ゴロゴロという予兆の音と画面のちらつきで分かる)
        Rect r = new Rect(W - 12 - (contentW + 24), 12, contentW + 24, 8 + 22 + 2 + 20 + 2 + 18 + 6 + 2 + 22 + 8);
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
    }

    void DrawToast()
    {
        if (toastAlpha <= 0.001f || game.AnnounceTimer > 0f || game.CountdownTimer > 0f) return; // 大きなお知らせの間は、小さいお知らせを重ねない
        var style = S("toast", 20, C(0xffe9a0), TextAnchor.MiddleCenter, bold: true);
        float y = H * 0.22f;
        if (game.AlertTimer > 0f) y = Mathf.Max(y, AlertTop + AlertHeight + 10f); // 警告の帯と重ならないように
        Rect r = new Rect(0, y, W, 30);
        Color old = GUI.color;
        GUI.color = new Color(1, 1, 1, toastAlpha);
        Shadowed(r, game.ToastText, style, 2f);
        GUI.color = old;
    }

    // ワールド座標を、この HUD の座標(拡大縮小後)に変換する。カメラの後ろなら false
    bool ToHud(Vector3 world, out Vector2 p)
    {
        Vector3 s = game.Cam.WorldToScreenPoint(world);
        p = new Vector2(s.x / scale, (Screen.height - s.y) / scale);
        return s.z > 0f;
    }

    // 擬態中、敵の頭の上に「？」(見失っている)。擬態が解けた直後は「！」(見つけた)。
    void DrawEnemyMarks()
    {
        var q = S("markQ", 26, C(0xffe27a), TextAnchor.MiddleCenter, bold: true);
        var ex = S("markEx", 28, C(0xff6a4a), TextAnchor.MiddleCenter, bold: true);
        Color old = GUI.color;
        foreach (var en in game.Enemies)
        {
            if (en.dying) continue;
            bool found = en.foundTimer > 0f;
            if (en.confused <= 0.01f && !found) continue;
            if (!ToHud(en.Position + Vector3.up * (en.hitHeight * 1.9f + 0.35f), out var p)) continue;
            if (p.x < -40 || p.x > W + 40 || p.y < -40 || p.y > H + 40) continue;
            float bob = Mathf.Sin(Time.time * 4f + en.go.GetInstanceID() * 0.37f) * 3f;
            if (found)
            {
                float t = 1f - en.foundTimer / 0.8f;
                float pop = 1f + 0.5f * Mathf.Max(0f, 1f - t * 4f); // 出た瞬間にぽんと大きく
                GUI.color = new Color(1, 1, 1, Mathf.Clamp01(en.foundTimer / 0.25f));
                LabelFx(new Rect(p.x - 20, p.y - 20, 40, 40), "！", ex, pop, 1f);
            }
            else
            {
                GUI.color = new Color(1, 1, 1, en.confused);
                Shadowed(new Rect(p.x - 20, p.y - 20 + bob, 40, 40), "？", q, 2f);
            }
        }
        GUI.color = old;
    }

    // 主人公の頭上に HPバー(太い・緑) と 擬態ゲージ(細い・水色)。文字は出さず、色と長さだけで伝える。
    // 回復したときはHPバーが、擬態ゲージが戻ったときは擬態ゲージが一瞬光る。擬態の残りが1秒を切ると赤く点滅。
    // 主人公のまわりに、次に向かう場所(蛍の石・ゴールの門)の方向を指す矢印を出す。色は行き先の色(青・黄の石 / 緑の門)
    void DrawObjectiveArrow()
    {
        if (!game.TryObjectiveDirection(out Vector3 aim, out Color color)) return;
        Vector3 from = game.PlayerPos + Vector3.up * 0.1f; // 足元を中心に回す
        Vector3 dir = aim - game.PlayerPos; dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        if (!ToHud(from, out var p0) || !ToHud(from + dir.normalized * 2f, out var p1)) return;
        Vector2 d = (p1 - p0).normalized;
        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        float bob = 6f * Mathf.Sin(Time.time * 5f); // 少し前後に動かして目立たせる
        Vector2 c = p0 + d * (95f + bob);
        var tex = Tex("objArrow", () => ArrowTex(64));
        Matrix4x4 old = GUI.matrix;
        Color oldC = GUI.color;
        // GUI 全体が拡大縮小されているので、回転は今の行列に重ねて組み立てる(RotateAroundPivot だと位置がずれる)
        GUI.matrix = old * Matrix4x4.TRS(new Vector3(c.x, c.y, 0), Quaternion.Euler(0, 0, angle), Vector3.one) * Matrix4x4.TRS(new Vector3(-c.x, -c.y, 0), Quaternion.identity, Vector3.one);
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(c.x - 31, c.y - 29, 66, 62), tex); // 影(縁取りの代わり)
        GUI.color = new Color(color.r, color.g, color.b, 0.95f);
        GUI.DrawTexture(new Rect(c.x - 33, c.y - 30, 62, 56), tex);
        GUI.matrix = old;
        GUI.color = oldC;
    }

    // 右向きの矢印(三角)のテクスチャ
    static Texture2D ArrowTex(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size - 0.5f;
                // 先が右を向いた三角形。根元を少しへこませて矢じりの形にする
                float half = 0.5f * (1f - u);
                bool inside = Mathf.Abs(v) < half && u > 0.08f + 0.25f * (1f - Mathf.Abs(v) / 0.5f) * 0f && !(u < 0.32f && Mathf.Abs(v) < 0.5f * (0.32f - u) / 0.32f * 0.9f);
                float edge = Mathf.Clamp01((half - Mathf.Abs(v)) * size * 0.5f);
                px[y * size + x] = new Color(1, 1, 1, inside ? edge : 0f);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    void DrawPlayerBars()
    {
        if (!ToHud(game.PlayerPos + Vector3.up * game.Player.HeadHeight, out var p)) return;
        const float w = 104f, hpH = 12f, campH = 7f, gap = 4f;
        float x = p.x - w / 2f, y = p.y - 34f;

        Fill(new Rect(x - 3, y - 3, w + 6, hpH + gap + campH + 6), new Color(0f, 0f, 0f, 0.5f));

        // HP
        float hpRatio = game.Hp / NightGame.HpMax;
        bool low = hpRatio < 0.3f;
        Bar(new Rect(x, y, w, hpH), hpRatio, low ? "hpLow" : "hp", low ? C(0xff5a4a) : C(0x6fe08a), low ? C(0xff8f7a) : C(0x9dffb0), null, 0.18f);
        if (game.HealFlash > 0f) Fill(new Rect(x - 3, y - 3, w + 6, hpH + 6), new Color(0.6f, 1f, 0.65f, 0.55f * game.HealFlash));

        // 擬態ゲージ
        y += hpH + gap;
        float camp = game.Camp / NightGame.CampMax;
        Color campColor;
        if (game.CampLocked) campColor = C(0x8a8a8a);
        else if (game.Camouflaged && camp * NightGame.CampSeconds < 1f) campColor = Mathf.Repeat(Time.time * 6f, 1f) < 0.5f ? C(0xff6a4a) : C(0xffb0a0);
        else campColor = C(0x8fe6ff);
        Fill(new Rect(x, y, w, campH), new Color(1f, 1f, 1f, 0.15f));
        Fill(new Rect(x, y, w * camp, campH), campColor);
        if (game.AmbushRefillFlash > 0f) Fill(new Rect(x - 3, y - 3, w + 6, campH + 6), new Color(0.75f, 0.95f, 1f, 0.55f * game.AmbushRefillFlash));
    }

    // 光で動くもの(ランタン・導きの花・蛍の石)の上に、照らした時間のたまり具合のゲージを出す
    void DrawObjectGauges()
    {
        var world = game.World;
        foreach (var lamp in world.Lamps)
            if (!lamp.lit) ObjectGauge(new Vector3(lamp.x, lamp.height + 2.2f, lamp.z), lamp.charge / NightLamp.ChargeTime, C(0xffd390), "ランタン");
        foreach (var plant in world.Plants)
        {
            bool active = game.Guidance != null && game.Guidance.plant == plant;
            if (!active) ObjectGauge(new Vector3(plant.x, plant.height + 1.35f, plant.z), plant.charge / NightGuidePlant.ChargeTime, C(0x98efbd), "導きの花");
        }
        foreach (var orb in world.Orbs)
            if (!orb.collected && !orb.materialized) ObjectGauge(orb.Position + Vector3.up * 2.6f, orb.reveal / NightOrb.RevealTime, orb.color, "蛍の石");
    }

    void ObjectGauge(Vector3 world, float ratio, Color color, string name)
    {
        if (ratio <= 0.001f) return; // まだ一度も照らしていないものには出さない
        if (!ToHud(world, out var p)) return;
        if (p.x < -60 || p.x > W + 60 || p.y < -30 || p.y > H + 30) return;
        const float w = 76f, h = 9f;
        Rect bar = new Rect(p.x - w / 2f, p.y, w, h);
        Fill(new Rect(bar.x - 2, bar.y - 2, w + 4, h + 4), new Color(0f, 0f, 0f, 0.55f));
        Fill(bar, new Color(1f, 1f, 1f, 0.15f));
        Fill(new Rect(bar.x, bar.y, w * Mathf.Clamp01(ratio), h), color);
    }

    const float AlertTop = 88f, AlertHeight = 62f;

    // 「敵が増えた！」の警告。ドンと拡大して出て、赤く脈打ち、最後にふわっと消える
    void DrawThreatAlert()
    {
        if (game.AlertTimer <= 0f) return;
        float t = NightGame.AlertDuration - game.AlertTimer;
        float appear = EaseOutBack(t / 0.35f);
        float alpha = Mathf.Clamp01(t / 0.12f) * Mathf.Clamp01(game.AlertTimer / 0.45f);
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 14f);
        float w = 330f, h = AlertHeight;
        // 画面の中央をふさがないよう、上の「蛍」パネルのすぐ下に出す
        Rect r = new Rect(W / 2f - w / 2f, AlertTop, w, h);

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
        Shadowed(new Rect(r.x, r.y + 8, r.width, 28), game.AlertText, S("alertMain", 20, C(0xfff0e0), TextAnchor.MiddleCenter, bold: true), 2f);
        Shadowed(new Rect(r.x, r.y + 36, r.width, 20), game.AlertSub, S("alertSub", 13, C(0xffd0c0), TextAnchor.MiddleCenter), 1f);
        GUI.color = oldC;
        GUI.matrix = old;
    }

    // 蛍を捕まえた・門が開いた の大きなお知らせ。画面の中央より少し上に、ポンと大きく出す
    void DrawAnnounce()
    {
        if (game.AnnounceTimer <= 0f) return;
        float t = NightGame.AnnounceDuration - game.AnnounceTimer;
        float appear = EaseOutBack(t / 0.4f);
        float alpha = Mathf.Clamp01(t / 0.1f) * Mathf.Clamp01(game.AnnounceTimer / 0.5f);
        Rect r = new Rect(0, H * 0.27f, W, 120);
        Matrix4x4 old = GUI.matrix;
        Color oldC = GUI.color;
        Vector2 c = r.center;
        float sc = Mathf.Lerp(0.4f, 1f, appear);
        GUI.matrix = old * Matrix4x4.TRS(new Vector3(c.x, c.y, 0), Quaternion.identity, new Vector3(sc, sc, 1f)) * Matrix4x4.TRS(new Vector3(-c.x, -c.y, 0), Quaternion.identity, Vector3.one);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        Shadowed(new Rect(r.x, r.y, r.width, 72), game.AnnounceText, S("announce", 58, C(0xffe066), TextAnchor.MiddleCenter, bold: true), 4f);
        Shadowed(new Rect(r.x, r.y + 76, r.width, 30), game.AnnounceSub, S("announceSub", 22, C(0xfff6d0), TextAnchor.MiddleCenter, bold: true), 2f);
        GUI.color = oldC;
        GUI.matrix = old;
    }

    // 始まる前の 3・2・1 のカウントダウン。数字が大きく出て縮みながら消える
    void DrawCountdown()
    {
        if (game.CountdownTimer <= 0f) return;
        int n = Mathf.CeilToInt(game.CountdownTimer);
        float f = game.CountdownTimer - (n - 1); // 1 → 0 に減る
        float sc = Mathf.Lerp(1f, 1.8f, Mathf.Clamp01((f - 0.75f) / 0.25f)); // 出た瞬間は大きく、すぐ普通の大きさに
        Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.35f));
        Rect r = new Rect(0, H / 2f - 90, W, 180);
        Matrix4x4 old = GUI.matrix;
        Color oldC = GUI.color;
        Vector2 c = r.center;
        GUI.matrix = old * Matrix4x4.TRS(new Vector3(c.x, c.y, 0), Quaternion.identity, new Vector3(sc, sc, 1f)) * Matrix4x4.TRS(new Vector3(-c.x, -c.y, 0), Quaternion.identity, Vector3.one);
        GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(f * 3f));
        Shadowed(r, n.ToString(), S("countdown", 150, C(0xffe066), TextAnchor.MiddleCenter, bold: true), 5f);
        GUI.color = oldC;
        GUI.matrix = old;
        Shadowed(new Rect(0, H / 2f + 100, W, 30), "灯り役はスマホの「ライトON」を押して準備しよう", S("countdownSub", 20, C(0xfff6d0), TextAnchor.MiddleCenter, bold: true), 2f);
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
        LabelFx(new Rect(0, y, W, 20), (game.Registered ? game.PlayerName + "  /  " : "") + game.MapId + ". " + game.Map.Name + "  /  " + (win ? "クリアタイム " : "生存 ") + FormatTime(game.Elapsed) + "  /  蛍 " + game.OrbCount + " / " + NightGame.OrbTotal,
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
        // 登録前は「このスコアなら何位か」を見せ、名前を入れてもらう。登録しなかった時はそう表示する
        bool done = game.RankingDone;
        int rankNow = done ? game.Rank : NightRanking.RankIfAdded(game.MapId, game.Score, game.ScoreSeconds);
        bool celebrate = done && game.Registered && game.Rank > 0;
        string rankText = done && !game.Registered ? "ランキングには登録しませんでした"
            : rankNow > 0 ? game.MapId + "マップ  " + (done ? rankNow + "位  TOP" + NightRanking.MaxEntries + " 入り！" : "このスコアなら " + rankNow + "位！")
            : game.MapId + "マップ  ランク外" + (top.Count >= NightRanking.MaxEntries ? "(" + NightRanking.MaxEntries + "位は " + top[top.Count - 1].score.ToString("N0") + "点)" : "");
        if (rankNow > 0 && rankNow <= 3 && rk > 0f && (!done || celebrate))
        {
            float glow = 0.35f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f);
            Color oldG = GUI.color;
            GUI.color = new Color(1f, 0.84f, 0.3f, glow * rk);
            GUI.DrawTexture(new Rect(cx - 260, y - 30, 520, 100), Tex("glow", () => GlowTex()));
            GUI.color = oldG;
        }
        bool rankOn = rankNow > 0 && (!done || game.Registered);
        LabelFx(new Rect(0, y, W, 34), rankText, S(rankOn ? "endRankOn" : "endRankOff", 22, rankOn ? C(0x9dffb0) : C(0xb5c8bd), TextAnchor.MiddleCenter, bold: true),
            Mathf.Lerp(2.2f, 1f, EaseOutBack(rk)), rk);
        if (celebrate && !endPopSfx) { endPopSfx = true; endCelebrateStart = Time.unscaledTime; game.Audio.PlayDownChime(); }
        if (celebrate) DrawConfetti(Time.unscaledTime - endCelebrateStart);
        y += 54;

        // ランキングに載せる名前の入力(入れた人だけ登録する)
        if (!done)
        {
            if (t > tRank + 0.5f) DrawEndNameEntry(cx, y, Seg(t, tRank + 0.5f, 0.4f));
            return;
        }

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

    float endCelebrateStart;
    string endName = "";
    float endNameShown = -1f;

    // 結果画面の名前入力。Enter または「登録する」で登録、何も入れずに進めば登録しない
    void DrawEndNameEntry(float cx, float y, float alpha)
    {
        if (endNameShown < 0f || endNameShown < endStart) { endNameShown = Time.unscaledTime; endName = ""; }
        Color prev = GUI.color;
        GUI.color = new Color(1, 1, 1, alpha);
        Label(new Rect(0, y, W, 24), "ランキングに名前を残す？(" + NightRanking.MaxNameLength + "文字まで・名前を入れた人だけ載ります)",
            S("endNameTitle", 15, C(0xe6f5e9), TextAnchor.MiddleCenter, bold: true));
        y += 30;

        // Enter で登録(名前入力の画面と同じく、日本語の変換を確定する Enter では登録しない)
        if (Input.compositionString.Length > 0) lastImeTime = Time.unscaledTime;
        Event ev = Event.current;
        bool enter = ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter || ev.character == '\n');
        bool imeBusy = Input.compositionString.Length > 0 || Time.unscaledTime - lastImeTime < 0.15f;
        if (enter && !imeBusy && Time.unscaledTime - endNameShown > 0.3f) { ev.Use(); game.SubmitRanking(endName); GUI.color = prev; return; }

        var fieldStyle = new GUIStyle(GUI.skin.textField) { font = uiFont, fontSize = 22, alignment = TextAnchor.MiddleCenter };
        fieldStyle.normal.textColor = Color.white; fieldStyle.focused.textColor = Color.white;
        GUI.SetNextControlName("endName");
        endName = GUI.TextField(new Rect(cx - 140, y, 280, 44), endName, NightRanking.MaxNameLength, fieldStyle);
        if (Time.unscaledTime - endNameShown < 0.4f) GUI.FocusControl("endName");
        y += 54;

        Rect ok = new Rect(cx - 110, y, 220, 42);
        bool ho = ok.Contains(Event.current.mousePosition);
        Panel(ok, Tex("endBtn" + ho, () => RoundedBox(32, 8, ho ? C(0x46a058) : C(0x3a8a4a), ho ? C(0x46a058) : C(0x3a8a4a), 0)), 8);
        Label(ok, endName.Trim().Length > 0 ? "登録する (Enter)" : "登録しないで進む (Enter)", S("endBtnText", 15, Color.white, TextAnchor.MiddleCenter));
        if (GUI.Button(ok, GUIContent.none, GUIStyle.none)) game.SubmitRanking(endName);
        Rect skip = new Rect(cx - 110, y + 50, 220, 28);
        bool hs = skip.Contains(Event.current.mousePosition);
        Label(skip, "登録しない", S(hs ? "neGuestH" : "neGuest", 14, hs ? Color.white : C(0xa9c4b8), TextAnchor.MiddleCenter));
        if (GUI.Button(skip, GUIContent.none, GUIStyle.none)) game.SubmitRanking("");
        GUI.color = prev;
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
    float lastImeTime = -1f; // 日本語入力で変換中だった最後の時刻(確定の Enter でゲームを始めないため)

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

        // Enter でゲーム開始。入力欄(TextField)より先に調べること:
        // Windows では Enter 1回で「キーコードだけ」と「改行文字だけ」の2つのイベントが来て、
        // 前者は入力欄が先に使ってしまう(後に調べると届かない)ため、どちらでも開始できるようにしている。
        // 日本語入力の変換を確定するための Enter では始めない(変換中か、確定した直後は無視する)。
        if (Input.compositionString.Length > 0) lastImeTime = Time.unscaledTime;
        Event ev = Event.current;
        bool enter = ev.type == EventType.KeyDown &&
                     (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter || ev.character == '\n');
        bool imeBusy = Input.compositionString.Length > 0 || Time.unscaledTime - lastImeTime < 0.15f;
        if (enter && !imeBusy && Time.unscaledTime - game.NameEntryTime > 0.25f) { ev.Use(); game.BeginPlay(nameText); return; }

        var fieldStyle = new GUIStyle(GUI.skin.textField) { font = uiFont, fontSize = 22, alignment = TextAnchor.MiddleCenter };
        fieldStyle.normal.textColor = Color.white; fieldStyle.focused.textColor = Color.white;
        GUI.SetNextControlName("playerName");
        nameText = GUI.TextField(new Rect(cx - 140, y, 280, 48), nameText, NightRanking.MaxNameLength, fieldStyle);
        if (Time.unscaledTime - game.NameEntryTime < 0.4f) GUI.FocusControl("playerName"); // 開いた直後は入力欄を選択済みにする
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
        const float listTop = 94f;
        float h = listTop + Mathf.Max(top.Count, 3) * rowH + 12f + (anyFail ? 18f : 0f);
        Rect r = new Rect(10, 16, w, h);
        Panel(r, PanelTex(), 9);
        Label(new Rect(r.x + 14, r.y + 10, w - 28, 24), game.MapId + ". " + game.Map.Name, S("rkHead", 17, C(0xdfffe0), TextAnchor.MiddleLeft, bold: true));
        var rd = game.Difficulty;
        Label(new Rect(r.x + 14, r.y + 34, w - 28, 18), rd.Stars + " " + rd.label, S("rkDiff" + game.MapId, 12, rd.color, TextAnchor.MiddleLeft, bold: true));
        Label(new Rect(r.x + 14, r.y + 54, w - 28, 18), "TOP " + NightRanking.MaxEntries + " ランキング", S("rkSub", 13, C(0xdfc985), TextAnchor.MiddleLeft, bold: true));
        Label(new Rect(r.x + 14, r.y + 72, w - 28, 16), "スコア = クリアタイム + 撃破数", S("rkSub2", 11, C(0x9fbdb0), TextAnchor.MiddleLeft));
        float y = r.y + listTop;
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
            var nameStyle = S("rkName" + (i < 3 ? i : 3), 14, tc, TextAnchor.MiddleLeft, bold: i < 3);
            nameStyle.clipping = TextClipping.Clip;
            Label(new Rect(r.x + 42, y + 1, 118, 26), nm, nameStyle);
            Label(new Rect(r.x + 164, y + 1, w - 178, 26), e.score.ToString("N0"), S("rkScore" + (i < 3 ? i : 3), 15, tc, TextAnchor.MiddleRight, bold: true));
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
    // タイトル画面: QRを大きく出し、クリックすると画面いっぱいに拡大(「戻る」で閉じる)。参加者を色の名前つきで一覧表示。
    // プレイ中: 右下に小さなQRと、参加人数・参加中の色だけを出す。
    // QRは準備ができるまで「QRコードを作成中」と出し、読み取れないQRや途中で変わるQRは見せない。
    public static bool QrZoomOpen { get; private set; }
    public static void DebugOpenQrZoom() => QrZoomOpen = true;   // 自動撮影(NightAutoShot)用
    public static void DebugCloseQrZoom() => QrZoomOpen = false;
    public static bool DebugQrWaiting; // 自動撮影用: QRの準備中の表示を出す
    readonly Dictionary<int, float> joinedAt = new Dictionary<int, float>(); // 色番号 → 参加した時刻(参加直後に光らせる)

    string QrTarget(out bool online)
    {
        var srv = PointerServer.Instance;
        online = srv != null && !string.IsNullOrEmpty(srv.PublicUrl);
        if (srv == null || !srv.QrReady || DebugQrWaiting) return null;
        // Cloudflare のURL(5G版)が準備できたらQRはそちら(5G・どのWi-Fiからでも参加可)、できなければ同じWi-Fi用にする
        string url = online ? srv.PublicUrl : srv.HttpsServerUrl;
        try { EnsureQrTexture(url); }
        catch (System.Exception ex) { Debug.LogException(ex); qrTexture = null; return null; }
        return qrTexture != null ? url : null;
    }

    List<PointerServer.ConnectedPlayer> ConnectedPlayers()
    {
        var players = PointerServer.Instance != null ? PointerServer.Instance.GetConnectedPlayers() : new List<PointerServer.ConnectedPlayer>();
        // 新しく参加した色を記録し、プレイ中なら「◯のライトが参加！」と知らせる
        var now = new HashSet<int>();
        foreach (var pl in players)
        {
            now.Add(pl.colorIndex);
            if (!joinedAt.ContainsKey(pl.colorIndex))
            {
                joinedAt[pl.colorIndex] = Time.unscaledTime;
                if (game.State == NightGame.GameState.Playing) game.ShowToast(pl.name + "のライトが参加！", 2f);
            }
        }
        var gone = new List<int>();
        foreach (var k in joinedAt.Keys) if (!now.Contains(k)) gone.Add(k);
        foreach (var k in gone) joinedAt.Remove(k);
        return players;
    }

    // 準備中の表示(回る点と「QRコードを作成中」)
    void DrawQrWaiting(Rect r, int fontSize)
    {
        Fill(r, new Color(1f, 1f, 1f, 0.06f));
        float c = Mathf.Min(r.width, r.height);
        var dot = Tex("playerDot", () => RoundedBox(32, 16, Color.white, Color.white, 0));
        Vector2 center = new Vector2(r.center.x, r.y + r.height * 0.42f);
        float rad = c * 0.14f, d = Mathf.Max(4f, c * 0.05f);
        for (int i = 0; i < 8; i++)
        {
            float ang = i / 8f * Mathf.PI * 2f + Time.unscaledTime * 4f;
            Color old = GUI.color;
            GUI.color = new Color(0.62f, 1f, 0.75f, 0.15f + 0.85f * (i / 7f));
            GUI.DrawTexture(new Rect(center.x + Mathf.Cos(ang) * rad - d / 2, center.y + Mathf.Sin(ang) * rad - d / 2, d, d), dot);
            GUI.color = old;
        }
        string text = "QRコードを\n作成中" + new string('.', 1 + (int)(Time.unscaledTime * 2f) % 3);
        // 5G用URLは、作られてからインターネットで開けるようになるまで1分ほどかかることがある(Cloudflare側の準備待ち)。
        // 止まっているように見えないよう、大きく表示できる所では目安も出す
        bool tunnelWait = PointerServer.Instance != null && PointerServer.Instance.TunnelRequested && fontSize >= 12;
        if (tunnelWait) text += "\n(1分ほどかかります)";
        Label(new Rect(r.x + 4, center.y + rad + d + 4, r.width - 8, fontSize * (tunnelWait ? 3.9f : 2.6f)), text,
            S("qrWait" + fontSize, fontSize, C(0xbfe3d2), TextAnchor.UpperCenter, wrap: true));
    }

    void DrawConnectPanel(bool title)
    {
        if (title) DrawTitleConnectPanel();
        else DrawPlayConnectPanel();
    }

    void DrawTitleConnectPanel()
    {
        var srv = PointerServer.Instance;
        string target = QrTarget(out bool online);
        var players = ConnectedPlayers();
        bool canZoom = game.State == NightGame.GameState.Title && target != null;

        const float qr = 196f, w = qr + 28f;
        int rows = Mathf.Max(1, (players.Count + 1) / 2);
        float h = 36f + qr + 22f + 34f + rows * 28f + 14f;
        Rect r = new Rect(W - w - 16, 16, w, h);
        Panel(r, PanelTex(), 9);
        Label(new Rect(r.x + 8, r.y + 8, r.width - 16, 24), "灯り役はスマホでQRを読み取り", S("qrHeadT", 13, C(0xdfffe0), TextAnchor.MiddleCenter, bold: true));

        Rect qrRect = new Rect(r.x + 14, r.y + 36, qr, qr);
        if (target != null)
        {
            bool hover = canZoom && qrRect.Contains(Event.current.mousePosition);
            if (hover) Fill(new Rect(qrRect.x - 3, qrRect.y - 3, qr + 6, qr + 6), C(0x9dffb0, 0.8f));
            GUI.DrawTexture(qrRect, qrTexture);
            if (canZoom && GUI.Button(qrRect, GUIContent.none, GUIStyle.none)) QrZoomOpen = true;
            string sub = canZoom ? "クリックで拡大" : "";
            // 5G用のURLが作れず(インターネットがない・cloudflared がない等)同じWi-Fi用のQRになった時は、そう伝える
            sub = (sub.Length > 0 ? sub + "・" : "") + (online ? "5G・どのWi-Fiからでも参加OK" : "同じWi-Fiにつないで参加");
            Label(new Rect(r.x + 4, qrRect.yMax + 3, r.width - 8, 16), sub, S("qrSubT", 10, C(0x98b6a8), TextAnchor.MiddleCenter));
        }
        else DrawQrWaiting(qrRect, 14);

        float y = qrRect.yMax + 22f;
        DrawPlayerCount(new Rect(r.x + 12, y, r.width - 24, 28), players.Count, 17);
        y += 34f;
        if (players.Count == 0)
        {
            Label(new Rect(r.x + 8, y, r.width - 16, 20), "まだ誰も参加していません", S("plNoneT", 11, C(0x98b6a8), TextAnchor.MiddleCenter));
            return;
        }
        float colW = (r.width - 24f) / 2f;
        for (int i = 0; i < players.Count; i++)
        {
            var pl = players[i];
            Rect cell = new Rect(r.x + 12 + (i % 2) * colW + 2, y + (i / 2) * 28f + 2, colW - 8, 24);
            DrawPlayerChip(cell, pl, 13);
        }
    }

    // 参加人数(大きな数字)と、7人分の枠のうち埋まっている数
    void DrawPlayerCount(Rect r, int count, int size)
    {
        Label(new Rect(r.x, r.y, r.width * 0.5f, r.height), "参加", S("plCntL" + size, size - 3, C(0xdfffe0, 0.85f), TextAnchor.MiddleLeft, bold: true));
        Label(new Rect(r.x, r.y, r.width, r.height), count + " / " + PointerServer.MaxPlayers + " 人",
            S("plCnt" + size, size, count > 0 ? C(0x9dffb0) : C(0x98b6a8), TextAnchor.MiddleRight, bold: true));
    }

    // 参加者の札: その人のライトの色で塗りつぶし、色の名前を濃い文字で書く(色が一目で分かり、色だけに頼らず名前でも分かる)。
    // ライトを当てている人は白い枠で光らせ、参加した直後の2秒は札が明滅する。
    void DrawPlayerChip(Rect cell, PointerServer.ConnectedPlayer pl, int fontSize)
    {
        var pill = Tex("playerPill", () => RoundedBox(48, 10, Color.white, Color.white, 0));
        float since = joinedAt.TryGetValue(pl.colorIndex, out var t0) ? Time.unscaledTime - t0 : 99f;
        float flash = since < 2f ? 0.5f + 0.5f * Mathf.Sin(since * 14f) : 1f;
        Color old = GUI.color;
        if (pl.lit)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(cell.x - 2, cell.y - 2, cell.width + 4, cell.height + 4), pill);
        }
        Color c = pl.color;
        GUI.color = new Color(c.r, c.g, c.b, Mathf.Lerp(0.8f, 1f, flash));
        GUI.DrawTexture(cell, pill);
        GUI.color = old;
        Label(cell, pl.name, S("plName" + fontSize, fontSize, C(0x0b1712), TextAnchor.MiddleCenter, bold: true));
    }

    void DrawPlayConnectPanel()
    {
        string target = QrTarget(out bool online);
        var players = ConnectedPlayers();
        const float qr = 62f;
        float listW = 132f;
        int rows = Mathf.Max(1, (players.Count + 1) / 2);
        float w = 10f + qr + 10f + listW + 10f;
        float h = Mathf.Max(qr + 20f, 30f + rows * 22f + 8f);
        Rect r = new Rect(W - w - 12, H - h - 12, w, h);
        Panel(r, PanelTex(), 9);

        Rect qrRect = new Rect(r.x + 10, r.y + (h - qr) / 2f, qr, qr);
        if (target != null) GUI.DrawTexture(qrRect, qrTexture);
        else DrawQrWaiting(qrRect, 8);

        float x = qrRect.xMax + 10f, y = r.y + 6f;
        DrawPlayerCount(new Rect(x, y, listW, 20), players.Count, 13);
        y += 24f;
        if (players.Count == 0)
        {
            Label(new Rect(x, y, listW, 16), "QRで参加できます", S("plNoneP", 9, C(0x98b6a8), TextAnchor.MiddleLeft));
            return;
        }
        float colW = listW / 2f;
        for (int i = 0; i < players.Count; i++)
            DrawPlayerChip(new Rect(x + (i % 2) * colW + 2, y + (i / 2) * 22f + 2, colW - 6, 18), players[i], 11);
    }

    // タイトル画面でQRをクリックしたときの拡大表示。「戻る」ボタン・ESC・まわりのクリックで閉じる。
    void DrawQrZoom()
    {
        if (!QrZoomOpen) return;
        if (game.State != NightGame.GameState.Title || qrTexture == null) { QrZoomOpen = false; return; }
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) { QrZoomOpen = false; Event.current.Use(); return; }

        Fill(new Rect(0, 0, W, H), new Color(0f, 0.02f, 0.04f, 0.82f));
        float qr = Mathf.Min(H - 190f, W - 80f);
        float w = qr + 48f, h = qr + 150f;
        Rect r = new Rect((W - w) / 2f, (H - h) / 2f, w, h);
        Panel(r, PanelTex(), 9);
        Label(new Rect(r.x, r.y + 10, w, 30), "灯り役はスマホのカメラでQRを読み取り", S("qrZoomHead", 18, C(0xdfffe0), TextAnchor.MiddleCenter, bold: true));
        Rect qrRect = new Rect(r.x + 24, r.y + 46, qr, qr);
        GUI.DrawTexture(qrRect, qrTexture);
        var players = ConnectedPlayers();
        DrawPlayerCount(new Rect(r.x + 24, qrRect.yMax + 8, qr, 26), players.Count, 16);
        if (MenuButton(new Rect(r.center.x - 84, r.yMax - 56, 168, 42), "戻る")) QrZoomOpen = false;
        // パネルの外をクリックしても閉じる(パネル内のクリックは下のタイトル画面に通さない)
        if (Event.current.type == EventType.MouseDown)
        {
            if (!r.Contains(Event.current.mousePosition)) QrZoomOpen = false;
            Event.current.Use();
        }
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
            s = new GUIStyle { font = uiFont, fontSize = size, alignment = align, wordWrap = wrap, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, clipping = TextClipping.Overflow };
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
