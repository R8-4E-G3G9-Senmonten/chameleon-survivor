using UnityEngine;

// ゲームモード: カメレオンナイト・サバイバー(HTMLモックアップ chameleon-survivor.html の再現)
// メインプレイヤー(WASD)は逃げ回るだけで攻撃手段を持たない。
// 協力プレイヤー役の懐中電灯(FlashlightController)を敵に当て続けるとダウンゲージ(lightHp)
// が減って倒せるが、逸らすと回復してしまう駆け引きがある。懐中電灯はスマホ(PointerServer
// 経由のタッチ/傾き)が繋がっていればそちらを、未接続ならマウスを使う。
// 時間経過で出現間隔が短くなり、硬いタイプ(brute)の出現率も上がっていく。
//
// UI配色は「夜の森+擬態」をテーマにしたセマンティックトークンとして定義し、
// タイトル/HUD/ゲームオーバーで使い回す(アドホックな色の乱立を避けるため)。
public class SurvivorManager : MonoBehaviour
{
    public static SurvivorManager Instance { get; private set; }

    public enum GameState { Title, Playing, Paused, GameOver }
    public GameState State { get; private set; } = GameState.Title;

    public Transform PlayerTransform;
    public float worldHalfExtent = 19f;

    public float hpMax = 100f;

    // 0なら時間制限なし(生き残った時間を競う)。展示用に制限時間を付けたい場合だけ秒数を入れる。
    public float timeLimit = 0f;
    public bool TimedOut { get; private set; }

    // 敵に触れた時のダメージ倍率。回転を上げるため、HTML試作より大きくしている。
    public float contactDamageMultiplier = 2.2f;
    // 画面内の敵の上限(増えすぎて画面が騒がしくなり、酔いやすくなるのを防ぐ)
    public int maxEnemies = 45;
    float hp;

    public float ElapsedTime { get; private set; }
    public int KillCount { get; private set; }

    float spawnTimer = 1.2f;
    float pendingContactDps;
    float hurtFlashTimer;

    public bool IsHurt => hurtFlashTimer > 0f;
    public SurvivorAudio Audio { get; private set; }

    // --- ベスト記録(生存時間と撃破数)。端末ごとに保存される ---
    const string BestTimeKey = "Survivor_BestTime";
    const string BestKillsKey = "Survivor_BestKills";
    const string VolumeKey = "Survivor_Volume";
    float bestTime;
    int bestKills;
    bool newRecord;
    Texture2D vignetteTex;
    float volume = 1f;

    // --- 配色トークン(felt green + gold on dark) ---
    static readonly Color ColorBackground = new Color32(0x0F, 0x17, 0x2A, 0xFF);
    static readonly Color ColorCard = new Color32(0x19, 0x21, 0x34, 0xE8);
    static readonly Color ColorCardSoft = new Color32(0x19, 0x21, 0x34, 0xB8);
    static readonly Color ColorPrimary = new Color32(0x22, 0xC5, 0x5E, 0xFF);   // 擬態の緑
    static readonly Color ColorAccent = new Color32(0xF5, 0xA6, 0x23, 0xFF);    // 灯りの金
    static readonly Color ColorDestructive = new Color32(0xE0, 0x4A, 0x3B, 0xFF);
    static readonly Color ColorForeground = Color.white;
    static readonly Color ColorMuted = new Color32(0x94, 0xA3, 0xB8, 0xFF);

    const float Space = 8f; // 8pxグリッド

    GUIStyle titleStyle, subtitleStyle, hudStyle, bigStyle, smallStyle, tinyStyle, buttonStyle;
    Texture2D minimapTex;
    Color32[] minimapPixels;
    const int MinimapSize = 150;

    Texture2D qrTexture;
    string qrTextureUrl;

    Texture2D panelTex;      // 汎用角丸パネル(白地。GUI.colorで着色して使い回す)
    Texture2D markTexLarge;  // タイトル画面用のロゴマーク

    // --- 開発者向け設定: 画面の明るさ(シーン構築時の環境光・月明かりに対する倍率) ---
    const string BrightnessKey = "Survivor_Brightness";
    const float BrightnessMin = 0.2f, BrightnessMax = 5f;
    float brightness = 1f;
    Light moonLight;
    Color baseAmbient;
    float baseMoonIntensity;

    void Start()
    {
        baseAmbient = RenderSettings.ambientLight;
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type == LightType.Directional) { moonLight = l; baseMoonIntensity = l.intensity; break; }
        }
        brightness = Mathf.Clamp(PlayerPrefs.GetFloat(BrightnessKey, 1f), BrightnessMin, BrightnessMax);
        ApplyBrightness();
    }

    void ApplyBrightness()
    {
        RenderSettings.ambientLight = baseAmbient * brightness;
        if (moonLight != null) moonLight.intensity = baseMoonIntensity * brightness;
    }

    void Awake()
    {
        Instance = this;
        hp = hpMax;
        Audio = gameObject.AddComponent<SurvivorAudio>();
        volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
        AudioListener.volume = volume;
        bestTime = PlayerPrefs.GetFloat(BestTimeKey, 0f);
        bestKills = PlayerPrefs.GetInt(BestKillsKey, 0);
        vignetteTex = UITextureFactory.CreateVignette(256);
        minimapTex = new Texture2D(MinimapSize, MinimapSize, TextureFormat.RGBA32, false);
        minimapTex.filterMode = FilterMode.Point;
        minimapPixels = new Color32[MinimapSize * MinimapSize];

        panelTex = UITextureFactory.CreateRoundedRect(Color.white, 64, 14);
        markTexLarge = UITextureFactory.CreateChameleonMark(160, ColorAccent, ColorPrimary, new Color(0.9f, 0.93f, 0.75f));
    }

    public void ReportContact(float dps)
    {
        pendingContactDps = Mathf.Max(pendingContactDps, dps);
    }

    public void OnEnemyKilled()
    {
        KillCount++;
    }

    float SpawnInterval() => Mathf.Max(0.3f, 1.5f - ElapsedTime * 0.06f); // 約20秒で下限
    // 各タイプの出現しやすさ。時間が経つほど強い敵・ギミック持ちが混ざる。
    float BruteChance() => Mathf.Min(0.3f, 0.04f + ElapsedTime * 0.012f);
    float DasherChance() => ElapsedTime < 8f ? 0f : Mathf.Min(0.2f, (ElapsedTime - 8f) * 0.012f);
    float SplitterChance() => ElapsedTime < 14f ? 0f : Mathf.Min(0.16f, (ElapsedTime - 14f) * 0.01f);
    float SkulkerChance() => ElapsedTime < 20f ? 0f : Mathf.Min(0.16f, (ElapsedTime - 20f) * 0.01f);

    SurvivorPredator.EnemyType PickType()
    {
        float r = Random.value;
        if ((r -= BruteChance()) < 0f) return SurvivorPredator.EnemyType.Brute;
        if ((r -= DasherChance()) < 0f) return SurvivorPredator.EnemyType.Dasher;
        if ((r -= SplitterChance()) < 0f) return SurvivorPredator.EnemyType.Splitter;
        if ((r -= SkulkerChance()) < 0f) return SurvivorPredator.EnemyType.Skulker;
        return SurvivorPredator.EnemyType.Moth;
    }

    void StartGame()
    {
        if (State != GameState.Title) return;
        State = GameState.Playing;
    }

    void Update()
    {
        if (State == GameState.Title)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0))
                StartGame();
            return;
        }

        if (State == GameState.Paused)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) State = GameState.Playing;
            return;
        }

        if (State == GameState.Playing && Input.GetKeyDown(KeyCode.Escape))
        {
            State = GameState.Paused;
            return;
        }

        if (State == GameState.GameOver)
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            }
            return;
        }

        float dt = Time.deltaTime;
        ElapsedTime += dt;
        if (timeLimit > 0f && ElapsedTime >= timeLimit)
        {
            ElapsedTime = timeLimit;
            EndGame(true);
            return;
        }

        spawnTimer -= dt;
        if (spawnTimer <= 0f)
        {
            if (FindObjectsByType<SurvivorPredator>(FindObjectsSortMode.None).Length < maxEnemies) SpawnEnemy(PickType());
            spawnTimer = SpawnInterval();
        }
    }

    void LateUpdate()
    {
        if (State != GameState.Playing) return;

        float dt = Time.deltaTime;
        if (pendingContactDps > 0f)
        {
            hp = Mathf.Max(0f, hp - pendingContactDps * contactDamageMultiplier * dt);
            hurtFlashTimer = 0.15f;
        }
        pendingContactDps = 0f;
        if (hurtFlashTimer > 0f) hurtFlashTimer -= dt;

        if (hp <= 0f) EndGame(false);
    }

    void EndGame(bool timedOut)
    {
        TimedOut = timedOut;
        State = GameState.GameOver;
        SaveRecord();
    }

    // 記録は生き残った時間で比べる(制限時間ありの設定にした場合は撃破数で比べる)
    void SaveRecord()
    {
        newRecord = timeLimit > 0f ? KillCount > bestKills : ElapsedTime > bestTime;
        if (KillCount > bestKills) { bestKills = KillCount; PlayerPrefs.SetInt(BestKillsKey, bestKills); }
        if (ElapsedTime > bestTime) { bestTime = ElapsedTime; PlayerPrefs.SetFloat(BestTimeKey, bestTime); }
        PlayerPrefs.Save();
    }

    // 分裂などで、指定した位置に敵を出す
    public void SpawnEnemyAt(SurvivorPredator.EnemyType type, Vector3 pos)
    {
        pos.x = Mathf.Clamp(pos.x, -worldHalfExtent, worldHalfExtent);
        pos.z = Mathf.Clamp(pos.z, -worldHalfExtent, worldHalfExtent);
        pos.y = 0f;
        var go = new GameObject("Enemy_" + type);
        go.transform.position = pos;
        go.AddComponent<SurvivorPredator>().Initialize(type);
    }

    void SpawnEnemy(SurvivorPredator.EnemyType type)
    {
        if (PlayerTransform == null) return;

        float angle = Random.value * Mathf.PI * 2f;
        float dist = 15f + Random.value * 3f;
        Vector3 pos = new Vector3(
            Mathf.Clamp(PlayerTransform.position.x + Mathf.Sin(angle) * dist, -worldHalfExtent, worldHalfExtent),
            0f,
            Mathf.Clamp(PlayerTransform.position.z + Mathf.Cos(angle) * dist, -worldHalfExtent, worldHalfExtent));

        var go = new GameObject("Enemy_" + type);
        go.transform.position = pos;
        go.AddComponent<SurvivorPredator>().Initialize(type);
    }

    // --- スタイル定義 ---
    void EnsureStyles()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = ColorForeground;
        }
        if (subtitleStyle == null)
        {
            subtitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            subtitleStyle.normal.textColor = ColorMuted;
        }
        if (hudStyle == null)
        {
            hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            hudStyle.normal.textColor = ColorForeground;
        }
        if (bigStyle == null)
        {
            bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            bigStyle.normal.textColor = ColorDestructive;
        }
        if (smallStyle == null)
        {
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
            smallStyle.normal.textColor = ColorForeground;
        }
        if (tinyStyle == null)
        {
            tinyStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            tinyStyle.normal.textColor = ColorMuted;
        }
        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold, fixedHeight = 46 };
        }
    }

    static string FormatTime(float t)
    {
        int m = (int)(t / 60);
        int s = (int)(t % 60);
        return string.Format("{0:00}:{1:00}", m, s);
    }

    // 画面の四隅を暗くして夜の雰囲気を出す。敵に触れている間は赤いビネットが重なる。
    void DrawVignette()
    {
        var full = new Rect(0, 0, Screen.width, Screen.height);
        GUI.color = new Color(0f, 0f, 0f, 0.8f); // テクスチャは白なので、黒く着色して四隅を暗くする
        GUI.DrawTexture(full, vignetteTex, ScaleMode.StretchToFill);
        if (IsHurt)
        {
            GUI.color = new Color(1f, 0.15f, 0.1f, 0.85f);
            GUI.DrawTexture(full, vignetteTex, ScaleMode.StretchToFill);
        }
        GUI.color = Color.white;
    }

    void DrawPanel(Rect r, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(r, panelTex);
        GUI.color = Color.white;
    }

    void OnGUI()
    {
        EnsureStyles();

        if (State == GameState.Title)
        {
            DrawTitleScreen();
            return;
        }

        DrawVignette();

        // --- ステータスパネル(左上) ---
        float panelW = 300f;
        Rect statusRect = new Rect(Space * 2, Space * 2, panelW, 128);
        DrawPanel(statusRect, ColorCard);

        float px = statusRect.x + Space * 1.5f;
        float py = statusRect.y + Space * 1.5f;
        float innerW = panelW - Space * 3f;

        // HPバー
        DrawPanel(new Rect(px, py, innerW, 16), new Color(0, 0, 0, 0.35f));
        Color hpColor = hp < hpMax * 0.3f ? ColorDestructive : ColorPrimary;
        GUI.color = hpColor;
        GUI.DrawTexture(new Rect(px + 2, py + 2, (innerW - 4) * Mathf.Clamp01(hp / hpMax), 12), panelTex);
        GUI.color = Color.white;
        py += 16 + Space;

        GUI.Label(new Rect(px, py, innerW, 26), (timeLimit > 0f ? "残り " + FormatTime(Mathf.Max(0f, timeLimit - ElapsedTime)) : "生存 " + FormatTime(ElapsedTime)) + "　撃破 " + KillCount, hudStyle);
        py += 22;
        GUI.Label(new Rect(px, py, innerW, 22), "出現間隔 " + SpawnInterval().ToString("0.00") + "s", tinyStyle);
        py += 18;

        bool phoneActive = FlashlightController.Instance != null && FlashlightController.Instance.HasPhoneInput;
        GUIStyle inputStyle = new GUIStyle(tinyStyle);
        inputStyle.normal.textColor = phoneActive ? ColorPrimary : ColorMuted;
        GUI.Label(new Rect(px, py, innerW, 22), phoneActive ? "● スマホ操作中" : "○ マウス操作中(スマホ未接続)", inputStyle);

        // 操作説明(ステータスパネルの下に軽く)
        GUI.color = ColorMuted;
        GUI.Label(new Rect(Space * 2, statusRect.yMax + Space, panelW, 20),
            "移動 WASD/矢印　灯り スマホ(接続時)/マウス", tinyStyle);
        GUI.color = Color.white;

        DrawConnectPanel();
        DrawMinimap();

        if (State == GameState.Paused)
        {
            DrawPauseMenu();
        }

        if (State == GameState.GameOver)
        {
            DrawPanel(new Rect(0, 0, Screen.width, Screen.height), new Color(ColorBackground.r, ColorBackground.g, ColorBackground.b, 0.86f));

            Rect card = new Rect(Screen.width / 2f - 190, Screen.height / 2f - 140, 380, 270);
            DrawPanel(card, ColorCard);

            GUIStyle resultStyle = new GUIStyle(bigStyle);
            resultStyle.normal.textColor = TimedOut ? ColorPrimary : ColorDestructive;
            GUI.Label(new Rect(card.x, card.y + 18, card.width, 50), TimedOut ? "タイムアップ!" : "力尽きた…", resultStyle);
            GUI.Label(new Rect(card.x, card.y + 78, card.width, 30), (TimedOut ? "生き延びた! " : "生存時間 ") + FormatTime(ElapsedTime), smallStyle);
            GUI.Label(new Rect(card.x, card.y + 108, card.width, 30), "撃破数 " + KillCount, smallStyle);

            GUIStyle record = new GUIStyle(tinyStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
            record.normal.textColor = newRecord ? ColorAccent : ColorMuted;
            GUI.Label(new Rect(card.x, card.y + 142, card.width, 22),
                (newRecord ? "★ 新記録!  " : "") + "ベスト 生存 " + FormatTime(bestTime) + " / 撃破 " + bestKills, record);

            GUIStyle hint = new GUIStyle(tinyStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
            GUI.Label(new Rect(card.x, card.y + 182, card.width, 24), "R キーでやり直す", hint);

            if (GUI.Button(new Rect(card.x + card.width / 2f - 90, card.y + 210, 180, 46), "もう一度挑戦", buttonStyle))
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            }
        }
    }

    // --- ポーズ画面(ESC)。開発者向け設定として画面の明るさを調整できる ---
    void DrawPauseMenu()
    {
        // 明るさの変化を確認しやすいよう、背景の暗幕は薄めにする
        DrawPanel(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.35f));

        Rect card = new Rect(Screen.width / 2f - 200, Screen.height / 2f - 190, 400, 380);
        DrawPanel(card, ColorCard);

        GUI.Label(new Rect(card.x, card.y + 16, card.width, 44), "一時停止", bigStyleNeutral());

        GUIStyle section = new GUIStyle(tinyStyle) { fontSize = 12, fontStyle = FontStyle.Bold };
        section.normal.textColor = ColorAccent;
        GUI.Label(new Rect(card.x + 24, card.y + 76, card.width - 48, 20), "開発者向け設定", section);

        GUI.Label(new Rect(card.x + 24, card.y + 100, card.width - 48, 22),
            "画面の明るさ  ×" + brightness.ToString("0.00"), hudStyle);
        float newValue = GUI.HorizontalSlider(new Rect(card.x + 24, card.y + 130, card.width - 48, 20),
            brightness, BrightnessMin, BrightnessMax);
        if (!Mathf.Approximately(newValue, brightness))
        {
            brightness = newValue;
            ApplyBrightness();
            PlayerPrefs.SetFloat(BrightnessKey, brightness);
        }

        if (GUI.Button(new Rect(card.x + 24, card.y + 162, 120, 30), "明るさをリセット"))
        {
            brightness = 1f;
            ApplyBrightness();
            PlayerPrefs.SetFloat(BrightnessKey, brightness);
        }

        GUI.Label(new Rect(card.x + 24, card.y + 210, card.width - 48, 22), "音量  " + Mathf.RoundToInt(volume * 100f) + "%", hudStyle);
        float newVolume = GUI.HorizontalSlider(new Rect(card.x + 24, card.y + 240, card.width - 48, 20), volume, 0f, 1f);
        if (!Mathf.Approximately(newVolume, volume))
        {
            volume = newVolume;
            AudioListener.volume = volume;
            PlayerPrefs.SetFloat(VolumeKey, volume);
        }

        if (GUI.Button(new Rect(card.x + 24, card.y + 296, 170, 46), "再開", buttonStyle))
        {
            State = GameState.Playing;
        }
        if (GUI.Button(new Rect(card.x + card.width - 24 - 170, card.y + 296, 170, 46), "最初からやり直す", buttonStyle))
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        GUIStyle hint = new GUIStyle(tinyStyle) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(card.x, card.y + 350, card.width, 20), "ESCでも再開できます", hint);
    }

    GUIStyle bigStyleNeutral()
    {
        var s = new GUIStyle(bigStyle);
        s.normal.textColor = ColorForeground;
        return s;
    }

    // --- タイトル(起動)画面 ---
    void DrawTitleScreen()
    {
        DrawPanel(new Rect(0, 0, Screen.width, Screen.height), ColorBackground);

        float cx = Screen.width / 2f;
        float cardW = Mathf.Min(560, Screen.width - 40);
        Rect card = new Rect(cx - cardW / 2f, Screen.height / 2f - 260, cardW, 520);
        DrawPanel(card, ColorCardSoft);

        float y = card.y + 28;
        GUI.DrawTexture(new Rect(cx - 48, y, 96, 96), markTexLarge);
        y += 96 + Space;

        GUI.Label(new Rect(card.x, y, card.width, 50), "カメレオンナイト", titleStyle);
        y += 50;
        GUIStyle subTitle = new GUIStyle(subtitleStyle) { fontSize = 16, fontStyle = FontStyle.Bold };
        subTitle.normal.textColor = ColorAccent;
        GUI.Label(new Rect(card.x, y, card.width, 26), "サバイバー", subTitle);
        y += 26 + Space * 2;

        GUI.Label(new Rect(card.x + 30, y, card.width - 60, 90),
            "夜の森を逃げ回るカメレオンと、懐中電灯で敵を焼き払う協力者の2役サバイバル。\n" +
            "灯りを当て続けると敵は倒せるが、逸らすと回復してしまう。", subtitleStyle);
        y += 90;

        Rect controlsRect = new Rect(card.x + Space * 2, y, card.width - Space * 4, 92);
        DrawPanel(controlsRect, new Color(0, 0, 0, 0.28f));
        GUIStyle controlLabel = new GUIStyle(tinyStyle) { fontSize = 13 };
        controlLabel.normal.textColor = ColorForeground;
        GUI.Label(new Rect(controlsRect.x + Space, controlsRect.y + Space, controlsRect.width - Space * 2, 24),
            "🏃 メイン役: WASD / 矢印キーで移動(逃げる専用)", controlLabel);
        GUI.Label(new Rect(controlsRect.x + Space, controlsRect.y + Space + 28, controlsRect.width - Space * 2, 24),
            "🔦 灯り役: マウス操作、またはスマホでQRを読み取って接続", controlLabel);
        GUI.Label(new Rect(controlsRect.x + Space, controlsRect.y + Space + 56, controlsRect.width - Space * 2, 24),
            "敵に3秒以上照らされると危険。プレイヤーの至近距離には光が効かない", controlLabel);
        y += 92 + Space * 2;

        if (GUI.Button(new Rect(cx - 110, y, 220, 46), "はじめる", buttonStyle))
        {
            StartGame();
        }
        y += 46 + Space;

        GUIStyle pressAny = new GUIStyle(tinyStyle) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(card.x, y, card.width, 20), "Enter / Space / クリックでも開始できます", pressAny);

        // タイトル画面でもスマホ接続の準備ができるようQRを右上に出しておく
        DrawConnectPanel();
    }

    void DrawMinimap()
    {
        int cx = MinimapSize / 2, cy = MinimapSize / 2;
        float R = MinimapSize / 2f - 1f;
        Color32 bg = new Color32(20, 40, 25, 150);
        Color32 clear = new Color32(0, 0, 0, 0);

        for (int y = 0; y < MinimapSize; y++)
        {
            for (int x = 0; x < MinimapSize; x++)
            {
                float dx = x - cx, dy = y - cy;
                minimapPixels[y * MinimapSize + x] = (dx * dx + dy * dy <= R * R) ? bg : clear;
            }
        }

        if (PlayerTransform != null)
        {
            var enemies = FindObjectsByType<SurvivorPredator>(FindObjectsSortMode.None);
            foreach (var en in enemies)
            {
                if (en.IsDying) continue;
                float relX = en.transform.position.x - PlayerTransform.position.x;
                float relZ = en.transform.position.z - PlayerTransform.position.z;
                int px = cx - Mathf.RoundToInt(relX / worldHalfExtent * 70f); // 画面右は-X方向
                int py = cy - Mathf.RoundToInt(relZ / worldHalfExtent * 70f);
                bool isBrute = en.Type == SurvivorPredator.EnemyType.Brute;
                // 種類ごとに色分け(蛾=紫、大型=赤、突進=黄、光を避ける=水色、分裂=橙)
                Color32 c = en.Type == SurvivorPredator.EnemyType.Moth ? new Color32(201, 138, 255, 255)
                          : en.Type == SurvivorPredator.EnemyType.Brute ? new Color32(255, 106, 90, 255)
                          : (Color32)Color.Lerp(SurvivorPredator.MapColor(en.Type), Color.white, 0.35f);
                PlotDot(px, py, isBrute ? 4 : 3, c);
            }
        }

        PlotDot(cx, cy, 3, new Color32(234, 254, 234, 255));

        minimapTex.SetPixels32(minimapPixels);
        minimapTex.Apply();

        Rect r = new Rect(Screen.width - MinimapSize - 14, Screen.height - MinimapSize - 14, MinimapSize, MinimapSize);
        GUI.color = Color.white;
        GUI.DrawTexture(r, minimapTex);
    }

    void PlotDot(int px0, int py0, int radius, Color32 c)
    {
        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                if (x * x + y * y > radius * radius) continue;
                int px = px0 + x, py = py0 + y;
                if (px < 0 || px >= MinimapSize || py < 0 || py >= MinimapSize) continue;
                minimapPixels[py * MinimapSize + px] = c;
            }
        }
    }

    // --- スマホ接続用のURL/QRコード表示(GameManagerと同じPointerServer/QRCodeGeneratorを利用) ---
    void DrawConnectPanel()
    {
        string httpsUrl = PointerServer.Instance != null ? PointerServer.Instance.HttpsServerUrl : "起動中...";
        string httpUrl = PointerServer.Instance != null ? PointerServer.Instance.ServerUrl : "起動中...";

        const int qrSize = 130;
        Rect panelRect = new Rect(Screen.width - qrSize - Space * 3, Space * 2, qrSize + Space * 2, qrSize + 56);
        DrawPanel(panelRect, ColorCard);

        GUI.Label(new Rect(panelRect.x + Space, panelRect.y + 4, panelRect.width - Space * 2, 40),
            "灯り役はスマホでQRを読み取り\n" + httpsUrl, tinyStyle);

        bool httpsReady = httpsUrl.StartsWith("https://");
        if (httpsReady)
        {
            try { EnsureQrTexture(httpsUrl); }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                qrTexture = null;
            }
            if (qrTexture != null)
            {
                Rect qrRect = new Rect(panelRect.x + Space, panelRect.y + 46, qrSize, qrSize);
                GUI.color = Color.white;
                GUI.DrawTexture(qrRect, qrTexture);
            }
        }
        else
        {
            GUI.Label(new Rect(panelRect.x + Space, panelRect.y + 50, panelRect.width - Space * 2, 40),
                "タッチのみで確実に繋ぎたい場合:\n" + httpUrl, tinyStyle);
        }
    }

    void EnsureQrTexture(string url)
    {
        if (qrTexture != null && qrTextureUrl == url) return;
        qrTextureUrl = url;

        bool[,] modules = QRCodeGenerator.Encode(url, out int size);
        const int moduleScale = 3;
        const int border = 4;
        int texSize = (size + border * 2) * moduleScale;

        var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var pixels = new Color32[texSize * texSize];
        Color32 white = new Color32(255, 255, 255, 255);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = white;

        Color32 black = new Color32(0, 0, 0, 255);
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (!modules[r, c]) continue;
                int px0 = (c + border) * moduleScale;
                int py0 = (size - 1 - r + border) * moduleScale; // テクスチャは下→上, QRの行0は上端
                for (int dy = 0; dy < moduleScale; dy++)
                {
                    int rowStart = (py0 + dy) * texSize + px0;
                    for (int dx = 0; dx < moduleScale; dx++)
                        pixels[rowStart + dx] = black;
                }
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        if (qrTexture != null) Destroy(qrTexture);
        qrTexture = tex;
    }
}
