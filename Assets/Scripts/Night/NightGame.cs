using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 光の球が示す道案内(導きの花を照らすと現れる、葉の形の光の道)
public class NightGuidance
{
    public NightGuidePlant plant;
    public NightOrb orb; // null なら入口の門への案内
    public GameObject group;
    public Material material;
    public readonly List<Transform> markers = new List<Transform>();
    public float time, age;
}

// カメレオンナイト ― 光の球収集版(モックアップ)のゲーム進行。
//  - メインプレイヤーは上下左右の移動だけ。攻撃は協力者の光(スマホ or マウス)が担う。
//  - 止まると擬態: 地面や物の色に変わり、敵はプレイヤーを見失って「？」を出しながらうろうろする。最大3秒(ゲージ)。
//  - 球を2つ集めて入口の門を開き、帰還して脱出する。
//  - 十数秒に1回、雷鳴の予兆のあとに森全体が一瞬明るくなる(閃光)。範囲内の敵は光を浴びる。
public class NightGame : MonoBehaviour
{
    public static NightGame Instance { get; private set; }

    public enum GameState { Title, NameEntry, Playing, Paused, Ended }
    public GameState State { get; private set; } = GameState.Title;

    public NightWorld World;
    public NightPlayer Player;
    public NightFlashlight Flashlight;
    public Camera Cam;
    public Light Moon;
    public NightAudio Audio { get; private set; }

    public string MapId { get; private set; } = "A";
    // 練習モード(チュートリアル): 4つのステップを順に進めて、ざっくりルールを覚える。
    //   1 移動しよう … 前方の光るスポットまで歩く(カメレオン役の動作確認)
    //   2 敵を倒そう … その場にとどまる4種類の敵を、ライトで10体倒す(倒すと少したって復活)
    //   3 ホタルを集めよう … 奥の石を照らしてホタルを呼び、触れて捕まえる(ホタルが門の鍵)
    //   4 門から脱出しよう … 開いた入口の門をくぐるとクリア
    // 雷・脅威レベル・ダメージ・ランキングはなし。
    public bool Practice => MapId == NightMap.PracticeId;
    // タイトル画面で難易度(マップ)選択を出しているか。出していないときは「チュートリアルから始める」の画面
    public bool ChoosingMap { get; private set; }
    // 本番で遊ぶマップ(タイトルの難易度選択・チュートリアルの結果画面で選ぶ)。チュートリアル中も覚えておく
    public string PickedMap { get; private set; } = "A";
    static bool mapSelectOnLoad;   // 読み直したら難易度選択を出す(本番のあと「ステージ選択へ」)
    static string startMapOnLoad;  // 読み直したら、このマップですぐ本番を始める(チュートリアルの結果画面から)

    public void OpenMapSelect(bool open) { if (State == GameState.Title) ChoosingMap = open; }

    // チュートリアルの結果画面から、選んだマップでそのまま本番を始める(スマホはつながったまま)
    public void StartFromTutorial()
    {
        if (!Practice || State != GameState.Ended) return;
        startMapOnLoad = PickedMap;
        Restart();
    }
    public enum TutorialStep { Move, Defeat, Fireflies, Escape }
    public TutorialStep Step { get; private set; } = TutorialStep.Move;
    public const int TutorialSteps = 4, TutorialKillGoal = 10;
    public int TutorialKills { get; private set; }
    // 蛍の石を照らしてよいか(チュートリアルでは「ホタルを集めよう」になるまで照らしても反応しない)
    public bool OrbsActive => !Practice || Step >= TutorialStep.Fireflies;
    // 門を開けるのに必要なホタルの数(チュートリアルは石が1つなので1匹)
    public int OrbGoal => Practice ? 1 : OrbTotal;
    // 主人公の近く(NearOrbRange 以内)にある、まだ捕まえていないホタルの石。上の目標パネルでチャージの進み具合を出す
    const float NearOrbRange = 9f;
    public NightOrb NearbyOrb
    {
        get
        {
            if (!OrbsActive || State != GameState.Playing) return null;
            NightOrb best = null; float bd = NearOrbRange;
            foreach (var o in World.Orbs)
            {
                if (o.collected) continue;
                float d = NightMap.Flat(o.Position, PlayerPos);
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }
    }
    public NightMap Map => World.Map;
    public NightDifficulty Difficulty => NightDifficulty.For(MapId);

    // ---- 擬態の連携ルール ----
    // 擬態中に敵を倒すと擬態ゲージが少し戻る。(擬態中に光が2倍効くルールはなくした)
    public const float AmbushKillRefill = 25f;

    // ---- 能力値(レベルアップは無し。難易度は経過時間で上がる敵の出現ペースだけ) ----
    public const float HpMax = 100f, CampMax = 100f;
    // 擬態ゲージは移動中に空→満タンまで約10秒で戻る(速すぎると擬態に頼りきりで簡単になるため)
    public const float CampSeconds = 3f; // 擬態できるのは最大3秒(ゲージ満タンから空になるまで)
    const float CampUnlock = 35f, CampDrain = CampMax / CampSeconds, CampRecharge = 10f, CampEnterMin = 30f;
    // 止まりっぱなしのまま擬態が切れたら、ゲージが8割を超えるまでは擬態し直さない(止まっているだけで無敵が続かないように)
    const float CampReenterIdle = 80f;
    bool camoEndedWhileIdle;
    const float LightPower = 0.8f; // 灯り役の光が敵に与えるダメージの倍率(1.25 では強すぎて簡単だったため下げた)
    public const int OrbTotal = 2;

    public float Hp { get; private set; } = HpMax;
    public float Camp { get; private set; } = CampMax;
    public bool CampLocked { get; private set; }
    public bool Camouflaged { get; private set; }
    public float AmbushRefillFlash; // 擬態ゲージが回復した瞬間に、ゲージを光らせる演出用(1→0)
    public bool HurtNow { get; private set; }
    public float Elapsed { get; private set; }
    public int KillCount { get; private set; }

    // ---- 回復オーブと脅威レベル ----
    // 敵を倒すと回復オーブを落とす(近づくと吸い寄せられ、触れるとHPが回復)。
    // その代わり「回復した量」と「蛍の回収」で脅威レベルが上がり、敵の出る数・速さが増えていく。
    public const int ThreatMax = 9;
    const float HealPerThreat = 90f;          // これだけ回復するごとに脅威レベル+1(45 では増えすぎたので倍にした)
    const float HealOrbLife = 14f, HealMagnet = 3.2f, HealPickup = 0.8f;
    public int Threat { get; private set; } = 1;
    public float HealedTotal { get; private set; }
    float healedSinceThreat;
    public float ThreatProgress => Threat >= ThreatMax ? 1f : healedSinceThreat / HealPerThreat; // 次のレベルまで(回復の分)
    public float HealFlash; // HPが回復した瞬間に、HPバーを光らせる演出用(1→0)
    public int LastHeal { get; private set; }
    public string AlertText { get; private set; } = "";
    public string AlertSub { get; private set; } = "";
    public float AlertTimer { get; private set; }
    public const float AlertDuration = 2.8f;
    class HealOrb { public Transform t, halo; public float amount, age, baseY; public Vector3 vel; }
    readonly List<HealOrb> healOrbs = new List<HealOrb>();
    Material healCoreMat, healHaloMat;
    float SpawnRateBoost => 1f + 0.16f * (Threat - 1);       // 出現ペースの倍率
    int MaxEnemiesNow => Mathf.RoundToInt((Difficulty.maxEnemies + 5 * (Threat - 1)) * MaxEnemiesScale);

    // ---- 敵の多さ(たくさん出して、まとめて倒せる爽快感を出す) ----
    const float SpawnPace = 1.5f;       // 出現ペースの倍率(1.5 = 元の1.5倍の頻度で出る。2 だと多すぎたので減らした)
    const float MaxEnemiesScale = 1.1f; // 同時に出る敵の上限の倍率(1.5 から減らした)
    const int MothSwarm = 3;            // 群れ型(小さい敵)は、この数でかたまって出てくる
    public int OrbCount { get; private set; }
    public bool Cleared { get; private set; }
    public bool GateOpened => World.Gate != null && World.Gate.opened;
    float idleTimer;
    const float CamoDelay = 1f;      // 止まってから擬態(無敵)になるまでの秒数
    const float HealOrbChance = 0.5f; // 敵を倒したときに回復オーブを落とす確率

    // ---- 閃光 ----
    const float FlashInterval = 12f, FlashHold = 0.35f, FlashFall = 1f;
    const float FlashTotal = 0.2f + FlashHold + FlashFall;
    // FlashDamage: 雷のダメージの倍率。1回の雷(約1.5秒)で、敵の drain × FlashDamage × 約1 のダメージ。
    // 0.6 では小さい敵が雷だけでほぼ全滅して強すぎたので下げた(0.35 なら小さい敵も少し残る)
    const float FlashDamage = 0.35f, FlashRange = 26f;
    const float AttackAnimRange = 1.8f; // 敵がこの距離より近いと攻撃の動きにする(体が大きいので触れる距離 0.9 より手前から)
    public float FlashWait { get; private set; } = 9f;
    public float FlashT { get; private set; } = -1f;
    public float FlashLevel { get; private set; }
    float warnLevel;
    bool warned;

    static readonly Color FogDark = NightMeshes.Hex(0x02040a), FogBright = NightMeshes.Hex(0x7d90b8);
    const float FogDensityDark = 0.034f, FogDensityBright = 0.006f;
    // 懐中電灯が無いとほとんど何も見えないくらい暗くする(閃光の時だけ大きく明るくなる)
    const float AmbientBase = 0.004f, MoonBase = 0.005f; // 灯り(スマホの光・ランタン)がない所はほとんど見えないくらい暗く(最初の約8分の1)
    static readonly Color AmbientColor = NightMeshes.Hex(0x223344);

    // ---- 敵・弾・衝撃波 ----
    public readonly List<NightEnemy> Enemies = new List<NightEnemy>();
    readonly List<NightBullet> bullets = new List<NightBullet>();
    class Pulse { public Transform t; public Material m; public float time, duration, maxScale; }
    readonly List<Pulse> pulses = new List<Pulse>();
    float spawnTimer = 1f;
    Material bulletMat;

    public NightGuidance Guidance { get; private set; }

    public Vector3 LightTarget { get; private set; } // 代表点(Targets[0])。ランタン・導きの花・球の判定に使う
    public List<Vector3> LightTargets => Flashlight.Targets; // 接続中の全スマホ(何人でも)の照準点
    public Vector3 PlayerPos => Player.transform.position;

    // 光が当たっている間、一定間隔でだけ true を返す(当たり演出の火花を出す間隔の管理用)
    public static bool FxTick(ref float timer, float dt, float interval)
    {
        timer -= dt;
        if (timer > 0f) return false;
        timer = interval;
        return true;
    }

    // pos に当たっている灯り(スマホ・マウス)の本数を数える。当てているスマホには「当てている」を知らせる(スマホの音・振動用)。
    // owner: 一番近い光のスマホのid(マウスなら null)、mix: 当たっている光の色を混ぜた色
    int CountLightsOn(Vector3 pos, out string owner, out Color mix)
    {
        var targets = LightTargets;
        var owners = Flashlight.TargetOwners;
        var colors = Flashlight.TargetColors;
        float r2 = Flashlight.LightRadius * Flashlight.LightRadius, best = float.MaxValue;
        int count = 0;
        owner = null;
        mix = Color.black;
        for (int i = 0; i < targets.Count; i++)
        {
            Vector3 t = targets[i];
            float dx = pos.x - t.x, dz = pos.z - t.z, d2 = dx * dx + dz * dz;
            if (d2 >= r2 || Mathf.Abs(pos.y - t.y) >= 1.8f) continue;
            count++;
            string id = i < owners.Count ? owners[i] : null;
            if (i < colors.Count) mix += colors[i];
            if (id != null && PointerServer.Instance != null) PointerServer.Instance.MarkHitting(id);
            if (d2 < best) { best = d2; owner = id; }
        }
        mix = count > 0 ? mix / count : Color.white;
        return count;
    }

    // pos から見て最も近い照準点を返す(どのスマホが照らしても反応させたい箇所向け)
    public Vector3 NearestLightTargetTo(Vector3 pos) => NearestLightTargetTo(pos, out _);

    // owner: その光を出しているスマホのid(マウスの光なら null)
    public Vector3 NearestLightTargetTo(Vector3 pos, out string owner)
    {
        var targets = LightTargets;
        owner = null;
        if (targets.Count == 0) return LightTarget;
        int best = 0;
        float bestD = (targets[0] - pos).sqrMagnitude;
        for (int i = 1; i < targets.Count; i++)
        {
            float d = (targets[i] - pos).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        var owners = Flashlight.TargetOwners;
        if (best < owners.Count) owner = owners[best];
        return targets[best];
    }

    // ---- 表示用 ----
    public string ToastText { get; private set; } = "";
    public float ToastTimer { get; private set; }
    public const string HintDefault = "花を照らすと、まだホタルのいる石まで光の道が現れます。ランタンのまわりは敵が弱る。倒した敵の緑のオーブでHP回復。";
    public string Hint { get; private set; } = "";

    // ---- カメラ(進行方向の少し先を狙い、遅れて追従する) ----
    static readonly Vector3 CameraOffset = new Vector3(0, 13, 12);
    const float CameraLag = 2.2f, CameraLookAhead = 0.8f;
    Vector3 cameraFocus;

    // ---- 設定(ESCメニュー) ----
    static readonly KeyCode[] MapKeys = { KeyCode.Z, KeyCode.X, KeyCode.C }; // タイトルでマップ A / B / C を選ぶキー
    public static readonly string[] MapKeyNames = { "Z", "X", "C" };
    const string MapKey = "Night_Map", BrightnessKey = "Night_Brightness", VolumeKey = "Night_Volume";
    public const float BrightnessMin = 0.2f, BrightnessMax = 5f;
    public float Brightness { get; private set; } = 1f;
    public float Volume { get; private set; } = 1f;

    void Awake()
    {
        Instance = this;
        Audio = gameObject.AddComponent<NightAudio>();
        Brightness = Mathf.Clamp(PlayerPrefs.GetFloat(BrightnessKey, 1f), BrightnessMin, BrightnessMax);
        Volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
        AudioListener.volume = Volume;
        string saved = PlayerPrefs.GetString(MapKey, "A");
        MapId = System.Array.IndexOf(NightMap.Ids, saved) >= 0 ? saved : "A";
        PickedMap = MapId;

        bulletMat = EmissiveMaterialFactory.Create(NightMeshes.Hex(0xff3300));
        bulletMat.SetColor("_EmissionColor", NightMeshes.Hex(0xff3300) * 1.3f);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        Time.timeScale = 1f;
    }

    void Start()
    {
        BuildMap();
        ApplyLighting(0f);
        // 敵を先に作って取っておく(タイトル画面の間に済ませ、プレイ中のカクつきを防ぐ)
        NightEnemy.ClearPool();
        NightEnemy.Prewarm(NightEnemy.Kind.Moth, 16);
        NightEnemy.Prewarm(NightEnemy.Kind.Shooter, 5);
        NightEnemy.Prewarm(NightEnemy.Kind.Brute, 3);
        NightEnemy.Prewarm(NightEnemy.Kind.Sprite, 3);
        // exe は、起動したらまずマップ(コース)選択から始まる。-skipTitle を付けたときだけ、選択済みのマップで名前入力へ進む。
        var args = System.Environment.GetCommandLineArgs();
        bool backToTitle = returnToTitle;
        returnToTitle = false;
        if (System.Array.IndexOf(args, "-skipTitle") >= 0 && !backToTitle) StartGame();
        // チュートリアルの結果画面で選んだマップで、すぐ本番を始める
        if (startMapOnLoad != null && State == GameState.Title) { startMapOnLoad = null; StartGame(); }
        ChoosingMap = mapSelectOnLoad;
        mapSelectOnLoad = false;
        // 結果画面の「もう一度練習」、または -practice 付きの起動なら、そのまま練習モードを始める
        if ((practiceOnLoad || System.Array.IndexOf(args, "-practice") >= 0) && State == GameState.Title) StartPractice();
        practiceOnLoad = false;
        if (State == GameState.Title) Audio.PlayLobby(); // タイトル(ロビー)画面のBGM
        if (PointerServer.Instance != null) PointerServer.Instance.ResetKills(); // 次のゲームに続けて参加するスマホの撃破数を 0 に
    }

    void BuildMap()
    {
        World.Build(NightMap.Create(MapId));
        // モックアップは原点スタートだが、Unityでは入口の門(z=2.6)の扉がカメラとの間に入りプレイヤーが隠れるため、
        // 入口広場の中で少しだけ奥(北)から始める。
        var p = new Vector3(0, World.Map.SurfaceAt(0, -2.5f), -2.5f);
        Player.transform.position = p;
        Player.transform.rotation = Quaternion.Euler(0, 180f, 0); // 出口(北 = 画面上)を向いて始める
        Player.Velocity = Vector3.zero;
        cameraFocus = p;
        PlaceCamera();
    }

    // タイトル画面でマップを選び直す
    public void SelectMap(string id)
    {
        if (System.Array.IndexOf(NightMap.Ids, id) < 0) return;
        bool tutorialEnd = Practice && State == GameState.Ended;
        if (State != GameState.Title && !tutorialEnd) return;
        PickedMap = id;
        PlayerPrefs.SetString(MapKey, id);
        PlayerPrefs.Save();
        if (tutorialEnd || id == MapId) return; // チュートリアルの結果画面では選ぶだけ(始めるときに読み直す)
        MapId = id;
        BuildMap();
    }

    // コース(マップ)を決めたあと、ゲームを始める前に名前を入力する画面へ進む。
    public string PlayerName { get; private set; } = NightRanking.DefaultName;
    const float CountdownSeconds = 3f;
    public float CountdownTimer { get; private set; } // 0 より大きい間はカウントダウン中
    int countdownBeeped; // 最後に音を鳴らしたカウントダウンの数字
    public float NameEntryTime { get; private set; }
    public void StartGame()
    {
        if (State != GameState.Title) return;
        // 名前はゲームの最後(結果画面)で聞くので、ここではすぐに始める
        BeginPlay("");
    }

    // 練習モードを始める(タイトル画面から)。選んでいるマップの設定は変えない(保存しない)
    public void StartPractice()
    {
        if (State != GameState.Title) return;
        MapId = NightMap.PracticeId;
        BuildMap();
        ApplyLighting(PracticeLight); // カウントダウンの間から明るくしておく
        BeginPlay("");
        Step = TutorialStep.Move;
        TutorialKills = 0;
        BuildTutorialSpot();
        practiceSlots.Clear();
        for (int i = 0; i < PracticeEnemies.Length; i++)
        {
            // 敵は「敵を倒そう」のステップで出す。台座と明かりは先に置いておく
            var slot = new PracticeSlot { kind = PracticeEnemies[i], home = PracticeHomes[i] };
            practiceSlots.Add(slot);
            // 展示のように、敵の足元に台座を置き、上から明かりで照らす(暗い森でも4体が見えるように)
            Vector3 h = slot.home;
            float y = Map.SurfaceAt(h.x, h.z);
            World.Circle(h.x, y + 0.03f, h.z, 1.6f, World.StoneMat, false);
            NightWorld.PointLight(World.Root, new Vector3(h.x, y + 3.2f, h.z), NightMeshes.Hex(0xffe6b0), 2.2f, 6f);
        }
    }

    // 結果画面の「もう一度練習」: 読み込み直してから、すぐ練習モードを始める
    static bool practiceOnLoad;
    public void RestartPractice()
    {
        practiceOnLoad = true;
        Restart();
    }

    // 名前入力の画面から、タイトル(コース選択)へ戻る
    public void CancelNameEntry() { if (State == GameState.NameEntry) State = GameState.Title; }

    // 名前を確定してゲーム開始。名前が空なら「ゲスト」。灯り役(スマホ)は名前の入力なしで参加できる。
    public void BeginPlay(string name)
    {
        if (State != GameState.Title && State != GameState.NameEntry) return;
        name = (name ?? "").Trim();
        if (name.Length > NightRanking.MaxNameLength) name = name.Substring(0, NightRanking.MaxNameLength);
        PlayerName = name.Length == 0 ? NightRanking.DefaultName : name;
        State = GameState.Playing;
        // 人が遊ぶときは 3・2・1 のカウントダウンから(自動テスト・紹介動画は名前を渡して始めるので、すぐ始める)
        CountdownTimer = PlayerName == NightRanking.DefaultName ? CountdownSeconds : 0f;
        if (PointerServer.Instance != null) PointerServer.Instance.ResetKills(); // スマホの撃破数はゲームごとに数え直す
        Audio.StartAmbient();
        if (!Practice) ShowToast(
            MapId + ". " + Map.Name + " — 2つの石を照らしてホタルを集め、入口の門から脱出しよう");
    }

    // ステージ選択(タイトル)画面へ戻る。次の起動では exe でもタイトルを飛ばさない。
    static bool returnToTitle;
    public void BackToTitle()
    {
        Time.timeScale = 1f;
        returnToTitle = true;
        mapSelectOnLoad = !Practice; // 本番のあとは難易度選択へ、チュートリアルのあとは最初の画面へ
        NightEnemy.ClearPool();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void Restart()
    {
        Time.timeScale = 1f;
        if (!Practice && startMapOnLoad == null) startMapOnLoad = MapId; // 本番の「やり直す」は、同じマップですぐ始める
        NightEnemy.ClearPool();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void Resume() { if (State == GameState.Paused) { State = GameState.Playing; Time.timeScale = 1f; } }

    public void SetBrightness(float v)
    {
        Brightness = Mathf.Clamp(v, BrightnessMin, BrightnessMax);
        PlayerPrefs.SetFloat(BrightnessKey, Brightness);
        ApplyLighting(Mathf.Max(FlashLevel, warnLevel));
    }

    public void SetVolume(float v)
    {
        Volume = Mathf.Clamp01(v);
        AudioListener.volume = Volume;
        PlayerPrefs.SetFloat(VolumeKey, Volume);
    }

    // 大事な出来事(蛍を捕まえた・門が開いた)の大きなお知らせ。「敵が増えた」より目立たせる
    public const float AnnounceDuration = 2.6f;
    public string AnnounceText { get; private set; } = "";
    public string AnnounceSub { get; private set; } = "";
    public float AnnounceTimer { get; private set; }
    void Announce(string text, string sub)
    {
        AnnounceText = text;
        AnnounceSub = sub;
        AnnounceTimer = AnnounceDuration;
    }

    // いまやること(画面上の目標パネルに常に出す)
    public string Objective
    {
        get
        {
            if (Practice) return TutorialTodo;
            if (GateOpened) return "入口の門へ戻って脱出しよう";
            foreach (var o in World.Orbs) if (o.materialized && !o.collected) return "光った石に近づいて、ホタルを捕まえよう";
            return "石を照らしてホタルを呼ぼう";
        }
    }

    // 次に向かう場所(蛍1 → 蛍2 → 入口の門 の順。捕まえていない蛍のうち番号の小さい方)への方向。主人公のまわりの矢印に使う。
    // 通路は気にせず、目的地へのまっすぐの向きを指す
    public bool TryObjectiveDirection(out Vector3 aim, out Color color)
    {
        aim = Vector3.zero;
        color = NightMeshes.Hex(0xffe066); // 「いまやること」と同じ黄色。行き先が変わっても色は同じ
        if (State != GameState.Playing || CountdownTimer > 0f) return false;
        if (Practice && Step == TutorialStep.Move)
        {
            aim = tutorialSpot;
            return NightMap.Flat(aim, PlayerPos) >= 1.2f;
        }
        if (Practice && Step == TutorialStep.Defeat)
        {
            float best = float.MaxValue;
            foreach (var en in Enemies)
            {
                if (en.dying) continue;
                float d = NightMap.Flat(en.Position, PlayerPos);
                if (d < best) { best = d; aim = en.Position; }
            }
            return best < float.MaxValue;
        }
        if (!GateOpened)
        {
            // 行く順番は 蛍1(index 0) → 蛍2(index 1) → ゴール。まだ捕まえていない蛍のうち番号が一番小さい石を指す
            NightOrb next = null;
            foreach (var o in World.Orbs)
            {
                if (o.collected) continue;
                if (next == null || o.index < next.index) next = o;
            }
            if (next == null) return false;
            aim = next.Position;
        }
        else aim = new Vector3(NightMap.Exit.x, 0, NightMap.Exit.y);
        return NightMap.Flat(aim, PlayerPos) >= 1.2f; // もう着いていれば出さない
    }

    void SelectMapByKey()
    {
        for (int i = 0; i < NightMap.Ids.Length; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i) || Input.GetKeyDown(MapKeys[i])) SelectMap(NightMap.Ids[i]);
    }

    public void ShowToast(string text, float duration = 1.6f)
    {
        ToastText = text;
        ToastTimer = duration;
    }

    void Update()
    {
        switch (State)
        {
            // キーボードだけで全部操作できるように、多くはキーボードの下の段(Z X C V B N M)に割り当てる
            case GameState.Title:
                if (Input.GetKeyDown(KeyCode.N)) { NightHud.ToggleQrZoom(); return; } // QRの拡大 / 閉じる
                if (NightHud.QrZoomOpen) return; // QRの拡大表示中は、閉じる操作だけ受け付ける(NightHud 側)
                if (Input.GetKeyDown(KeyCode.P)) { StartPractice(); return; }
                bool go = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.B);
                if (!ChoosingMap)
                {
                    // 最初の画面: Z/X/C = そのマップのランキングを見る(もう一度押す・ESCで閉じる)
                    for (int i = 0; i < NightMap.Ids.Length; i++)
                        if (Input.GetKeyDown(MapKeys[i])) { NightHud.ToggleRanking(NightMap.Ids[i]); return; }
                    if (NightHud.RankingPopup != null)
                    {
                        if (go || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.V)) NightHud.ToggleRanking(null);
                        return;
                    }
                    // B(Enter) = チュートリアルから始める、V = スキップして難易度選択へ
                    if (go) StartPractice();
                    else if (Input.GetKeyDown(KeyCode.V)) ChoosingMap = true;
                    return;
                }
                // 難易度選択: Z/X/C = マップ、B(Enter) = プレイ、V(ESC) = 戻る
                if (go) { StartGame(); return; }
                if (Input.GetKeyDown(KeyCode.V) || Input.GetKeyDown(KeyCode.Escape)) { ChoosingMap = false; return; }
                SelectMapByKey();
                return;
            case GameState.NameEntry:
                if (Input.GetKeyDown(KeyCode.Escape)) CancelNameEntry();
                return;
            case GameState.Paused:
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Z)) Resume();
                if (Input.GetKeyDown(KeyCode.X)) Restart();
                if (Input.GetKeyDown(KeyCode.T) || Input.GetKeyDown(KeyCode.C)) BackToTitle();
                if (Input.GetKeyDown(KeyCode.V)) SetBrightness(Brightness / 1.15f);
                if (Input.GetKeyDown(KeyCode.B)) SetBrightness(Brightness * 1.15f);
                if (Input.GetKeyDown(KeyCode.N)) SetVolume(Mathf.Round((Volume - 0.1f) * 10f) / 10f);
                if (Input.GetKeyDown(KeyCode.M)) SetVolume(Mathf.Round((Volume + 0.1f) * 10f) / 10f);
                return;
            case GameState.Ended:
                // 名前を入力している間は文字を打てるように、ショートカットは登録が終わってから(入力中は Esc で「登録しない」)
                if (!RankingDone)
                {
                    if (Input.GetKeyDown(KeyCode.Escape)) SubmitRanking("");
                    return;
                }
                if (Practice)
                {
                    // チュートリアルの結果: Z/X/C = マップ、B(Enter) = そのまま本番、R = もう一度、T = タイトルへ
                    SelectMapByKey();
                    if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) StartFromTutorial();
                    if (Input.GetKeyDown(KeyCode.R)) RestartPractice();
                    if (Input.GetKeyDown(KeyCode.T)) BackToTitle();
                    return;
                }
                if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Z)) Restart();
                if (Input.GetKeyDown(KeyCode.T) || Input.GetKeyDown(KeyCode.X)) BackToTitle();
                return;
        }

        // 一時停止(M / ESC)。3・2・1 のカウントダウン中でも止められる
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.M))
        {
            State = GameState.Paused;
            Time.timeScale = 0f;
            return;
        }
        // 始まる前のカウントダウン中は、ゲームを進めない(カメラだけ主人公に合わせる)
        if (CountdownTimer > 0f)
        {
            // 3・2・1 の数字が変わるたびに「ピッ」、0 になったら「ピーン」
            int shown = Mathf.CeilToInt(CountdownTimer);
            if (shown != countdownBeeped) { countdownBeeped = shown; Audio.PlayCountBeep(); }
            CountdownTimer -= Time.deltaTime;
            if (CountdownTimer <= 0f) { Audio.PlayCountGo(); if (Practice) AnnounceStep(); }
            PlaceCamera();
            return;
        }

        Tick(Mathf.Min(Time.deltaTime, 0.05f));
    }

    void Tick(float dt)
    {
        Elapsed += dt;
        World.UpdateWater(Elapsed);
        if (ToastTimer > 0f) ToastTimer -= dt;

        // --- 移動 ---
        Player.Move(this, dt, out float oldX, out float oldZ);
        bool isMoving = Player.Velocity.magnitude > 0.35f;
        if (isMoving) idleTimer = 0f; else idleTimer += dt;

        // --- 擬態(止まる=ギミック): ゲージがある間(最大3秒)は無敵で、敵はプレイヤーを見失う ---
        bool wasCamouflaged = Camouflaged;
        if (isMoving)
        {
            Camouflaged = false;
            camoEndedWhileIdle = false;
            Camp = Mathf.Min(CampMax, Camp + CampRecharge * dt);
            if (CampLocked && Camp >= CampUnlock) CampLocked = false;
        }
        else if (idleTimer > CamoDelay && !CampLocked && !DebugNoCamo)
        {
            if (!wasCamouflaged && Camp >= (camoEndedWhileIdle ? CampReenterIdle : CampEnterMin)) { Camouflaged = true; OnCamouflageStart(); }
            else if (wasCamouflaged) Camouflaged = true;

            if (Camouflaged)
            {
                Camp -= CampDrain * dt;
                if (Camp <= 0f) { Camp = 0f; Camouflaged = false; CampLocked = true; }
            }
            else Camp = Mathf.Min(CampMax, Camp + CampRecharge * 0.5f * dt);
        }
        else
        {
            Camouflaged = false;
            Camp = Mathf.Min(CampMax, Camp + CampRecharge * 0.5f * dt);
            if (CampLocked && Camp >= CampUnlock) CampLocked = false;
        }
        if (wasCamouflaged && !Camouflaged) { OnCamouflageEnd(); if (!isMoving) camoEndedWhileIdle = true; }
        Color camoColor = World.CamouflageColorAt(PlayerPos, out var camoTexture);
        Player.UpdateLook(dt, Camouflaged, camoColor, camoTexture, HurtNow);

        // --- 灯り ---
        Flashlight.Aim(Cam, Map, PlayerPos.y);
        LightTarget = Flashlight.Target;
        World.UpdateGlow(LightTargets, dt);
        foreach (var lamp in World.Lamps) lamp.Tick(this, dt);
        if (Guidance != null && Guidance.orb != null && Guidance.orb.collected) ClearGuidance();
        foreach (var plant in World.Plants) plant.Tick(this, dt);
        UpdateGuidance(dt);

        World.UpdateCanopies(PlayerPos);
        if (Practice) ApplyLighting(PracticeLight); // 練習では雷は落ちない。展示の敵が見えるよう、本番より明るくする
        else UpdateFlash(dt);

        // --- 出現(練習では増えない。倒した敵がもとの場所に復活する) ---
        if (Practice) UpdateTutorial(dt);
        else spawnTimer -= dt;
        if (!Practice && spawnTimer <= 0f)
        {
            var kind = PickType();
            SpawnEnemy(kind, kind == NightEnemy.Kind.Moth ? MothSwarm : 1);
            spawnTimer = Mathf.Max(0.7f, 2.4f - Elapsed * 0.012f) * (GateOpened ? 0.85f : 1f) * Difficulty.spawnInterval / SpawnRateBoost / SpawnPace;
        }

        // --- 敵 ---
        float contactDps = UpdateEnemies(dt);
        PushPlayerOutOfEnemies();

        // --- 被ダメージ(擬態中は無敵) ---
        HurtNow = contactDps > 0f && !Camouflaged;
        if (HurtNow)
        {
            Hp = Mathf.Max(0f, Hp - contactDps * Difficulty.damage * dt);
            if (Random.value < dt * 3f) Audio.PlayHurt();
        }
        if (Hp <= 0f) { End(false); return; }

        UpdateBullets(dt);
        if (Hp <= 0f) { End(false); return; }
        UpdateHealOrbs(dt);
        if (HealFlash > 0f) HealFlash = Mathf.Max(0f, HealFlash - dt * 1.5f);
        if (AlertTimer > 0f) AlertTimer -= dt;
        if (AnnounceTimer > 0f) AnnounceTimer -= dt;

        // --- 蛍の石・門 ---
        foreach (var orb in World.Orbs) orb.Tick(this, dt);
        var gate = World.Gate;
        gate.Animate(dt);
        Vector3 pp = PlayerPos;
        if (gate.opened && gate.motion >= 0.85f && oldZ < NightMap.Exit.y && pp.z >= NightMap.Exit.y
            && Mathf.Abs(pp.x - NightMap.Exit.x) < NightMap.ExitHalfWidth && Mathf.Abs(pp.y - gate.Y) < 0.8f)
        {
            End(true);
            return;
        }

        if (AmbushRefillFlash > 0f) AmbushRefillFlash = Mathf.Max(0f, AmbushRefillFlash - dt * 2f);
        // 下のヒントはチュートリアルの間だけ出す。本番はできるだけ文字を使わず、画面の演出で伝える
        Hint = !Practice ? "" : CampLocked ? "擬態ゲージ切れ…動いてゲージを回復しよう" : TutorialHint;

        UpdatePulses(dt);
        UpdateCamera(dt);
    }

    void LateUpdate()
    {
        if (State == GameState.Title || State == GameState.NameEntry) PlaceCamera();
        // スマホ画面に、カメレオンが今ゲーム画面のどこにいるか・ゲームの状態・終わった時の結果を出す
        if (PointerServer.Instance != null && Cam != null && Player != null)
        {
            Vector3 v = Cam.WorldToViewportPoint(PlayerPos);
            string st = State == GameState.Playing || State == GameState.Paused ? "play"
                      : State == GameState.Ended ? "end" : "wait";
            PointerServer.Instance.SetGameView(new Vector2(v.x, 1f - v.y), st, Cleared, KillCount, ScoreSeconds,
                MapId + ". " + Map.Name, Practice);
        }
    }

    // ---- 敵 ----
    NightEnemy.Kind PickType()
    {
        float r = Random.value;
        if (r < 0.25f) return NightEnemy.Kind.Brute;   // 25% 硬い型
        if (r < 0.50f) return NightEnemy.Kind.Shooter; // 25% 遠距離型
        if (r < 0.62f) return NightEnemy.Kind.Sprite;  // 12% すり抜け型
        return NightEnemy.Kind.Moth;                   // 38% 群れ型
    }

    // count > 1 のときは、同じ場所のまわりにかためて出す(光をひと振りすると、まとめて倒せる)
    void SpawnEnemy(NightEnemy.Kind kind, int count = 1)
    {
        if (Enemies.Count >= MaxEnemiesNow) return;
        var segs = Map.Segments;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            var s = segs[Random.Range(0, segs.Count)];
            float t = Random.value;
            float x = s.a.x + (s.b.x - s.a.x) * t, z = s.a.z + (s.b.z - s.a.z) * t;
            float d = Mathf.Sqrt((x - PlayerPos.x) * (x - PlayerPos.x) + (z - PlayerPos.z) * (z - PlayerPos.z));
            if (d >= 12f && d <= 24f)
            {
                for (int k = 0; k < count && Enemies.Count < MaxEnemiesNow; k++)
                {
                    float ex = x, ez = z;
                    if (k > 0)
                    {
                        float ox = x + Random.Range(-0.9f, 0.9f), oz = z + Random.Range(-0.9f, 0.9f);
                        if (Map.Walkable(ox, oz, 0.3f)) { ex = ox; ez = oz; }
                    }
                    var en = NightEnemy.Spawn(kind, new Vector3(ex, Map.SurfaceAt(ex, ez), ez));
                    en.lightHp = en.lightHpMax = en.cfg.lightHp * Difficulty.enemyHp;
                    Enemies.Add(en);
                }
                return;
            }
        }
    }

    float LanternDamageAt(Vector3 pos)
    {
        float damage = 0f;
        foreach (var lamp in World.Lamps)
        {
            if (!lamp.lit || !lamp.InLight(Map, pos, NightLamp.DamageRadius)) continue;
            float distance = Mathf.Sqrt((lamp.x - pos.x) * (lamp.x - pos.x) + (lamp.z - pos.z) * (lamp.z - pos.z));
            float power = lamp.power * (1f - 0.65f * Mathf.Clamp01((distance - 2f) / (NightLamp.DamageRadius - 2f)));
            damage = Mathf.Max(damage, NightLamp.Dps * power);
        }
        return damage;
    }

    // 敵の体の中心(cfg.body の半径)には入れないように、主人公を押し戻す。
    // 敵の群れに突っ込んでも、すり抜けてごり押しで進むことはできない(押し戻された先が通れない所なら動かさない)。
    const float PlayerBodyRadius = 0.3f;
    void PushPlayerOutOfEnemies()
    {
        Vector3 p = PlayerPos;
        Vector3 push = Vector3.zero;
        foreach (var en in Enemies)
        {
            if (en.dying || Mathf.Abs(en.Position.y - p.y) > 1.2f) continue;
            float min = en.cfg.body + PlayerBodyRadius;
            float dx = p.x - en.Position.x, dz = p.z - en.Position.z, d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d >= min) continue;
            if (d < 0.001f) { dx = 0f; dz = 1f; d = 1f; }
            push.x += dx / d * (min - d);
            push.z += dz / d * (min - d);
        }
        if (push.sqrMagnitude < 1e-6f) return;
        Vector3 q = p + push;
        if (!Map.Walkable(q.x, q.z, 0.3f)) return;
        q.y = p.y;
        Player.transform.position = q;
    }

    float UpdateEnemies(float dt)
    {
        float contactDps = 0f;
        Vector3 pp = PlayerPos;
        for (int i = Enemies.Count - 1; i >= 0; i--)
        {
            var en = Enemies[i];
            if (en.dying)
            {
                en.dieTimer -= dt;
                float s = Mathf.Max(0f, en.dieTimer / 0.4f);
                en.go.transform.localScale = Vector3.one * s;
                en.Position += Vector3.up * dt * 0.6f;
                if (en.dieTimer <= 0f) { en.Destroy(); Enemies.RemoveAt(i); }
                continue;
            }

            // ノックバック(減衰しつつ押し出される)
            Vector3 pos = en.Position;
            if (en.knock.sqrMagnitude > 0.01f)
            {
                pos += en.knock * dt;
                en.knock *= Mathf.Max(0f, 1f - 5.5f * dt);
            }
            if (en.cfg.phasing) FollowGround(ref pos, dt); // すり抜け型は通路の外にいてもよい
            else Map.ConstrainActor(ref pos, 0.35f);
            pos.x = Mathf.Clamp(pos.x, -NightMap.HalfW - 2f, NightMap.HalfW + 2f);
            pos.z = Mathf.Clamp(pos.z, NightMap.GoalZ - 2f, NightMap.StartZ + 10f);
            en.Position = pos;

            Vector3 toPlayer = pp - pos; toPlayer.y = 0;
            float dist = toPlayer.magnitude;
            en.confused = Mathf.MoveTowards(en.confused, Camouflaged ? 1f : 0f, dt * 4f);
            if (en.foundTimer > 0f) en.foundTimer -= dt;
            if (Practice)
            {
                // 練習の敵は展示のように、その場にとどまって主人公の方を向くだけ(追わない・撃たない)
                if (dist > 0.01f)
                {
                    Quaternion face = Quaternion.Euler(0, Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg, 0);
                    en.go.transform.rotation = Quaternion.Slerp(en.go.transform.rotation, face, Mathf.Min(1f, dt * 3f));
                }
            }
            else if (Camouflaged)
            {
                // 擬態中はプレイヤーを見失い、その場の近くをうろうろする(弾も撃たない)
                WanderEnemy(en, dt);
            }
            else if (en.cfg.ranged && dist < 9f && SeesPlayer(en, pos, pp))
            {
                // 遠距離型は一定距離で立ち止まって弾を撃つ
                en.go.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg, 0);
                // 撃つ少し前から、狐火を振りかぶって投げる動きを始める
                if (en.shootTimer > NightEnemy.ShootLead && en.shootTimer - dt <= NightEnemy.ShootLead) en.PlayShoot();
                en.shootTimer -= dt;
                // 初めて見つけたときなど、振りかぶらずに撃とうとしたら、まず振りかぶってから撃つ
                if (en.shootTimer <= 0f && !en.ShootReady) { en.PlayShoot(); en.shootTimer = NightEnemy.ShootLead; }
                if (en.shootTimer <= 0f) { SpawnBullet(pos, pp); en.shootTimer = 2.5f; }
            }
            else if (en.knock.magnitude < 2.5f && dist > 0.7f)
            {
                if (en.cfg.phasing) PhaseTowardPlayer(en, dt);
                else AdvanceEnemy(en, dt);
            }
            if (!Practice && dist < 0.9f && Mathf.Abs(en.Position.y - pp.y) < 0.9f) contactDps = Mathf.Max(contactDps, en.cfg.contactDps);
            // 触れる少し手前から攻撃の動きにする(擬態中は見失っているので攻撃しない)
            en.Animate(dt, !Practice && !Camouflaged && dist < AttackAnimRange);

            // 光: 当て続けるとダウンゲージが減る。プレイヤーのすぐ近く(死角)には効かない。
            // 接続中のスマホは何人でも同時に照らせる。何本の光が当たっているかを数え、重ねた本数ぶんダメージが増える
            // (2本なら2倍)。撃破は一番近い光のスマホに数える。
            Vector3 ep = en.Position;
            int lightsOn = CountLightsOn(ep, out string lightOwner, out Color lightMix);
            bool spotHit = lightsOn > 0;
            bool flashHit = FlashLevel > 0.05f && dist < FlashRange;
            float lantern = LanternDamageAt(ep);
            bool lit = spotHit || flashHit || lantern > 0f;
            if (spotHit)
            {
                en.lightHp -= en.cfg.drain * LightPower * lightsOn * dt;
                if (Random.value < dt * 8f) Audio.PlayHitBuzz();
                if (lightOwner != null) { en.litBy = lightOwner; en.litByTime = Time.time; }
                // 光が重なっている間は、混ざった光の色の輪が足元から広がり、白く強い火花が散る(重ねると効いていると分かるように)
                if (lightsOn >= 2 && FxTick(ref en.crossFxTimer, dt, 0.15f))
                {
                    // 色を混ぜると暗くくすむので、白に寄せて明るくする。重ねた本数が多いほど輪を大きく
                    Color bright = Color.Lerp(lightMix, Color.white, 0.45f) * 1.25f; bright.a = 1f;
                    float size = (en.kind == NightEnemy.Kind.Brute ? 4.5f : 3.2f) * (1f + 0.25f * (lightsOn - 2));
                    AddPulse(new Vector3(ep.x, ep.y + 0.12f, ep.z), 0.3f, 0.55f, bright, 0.35f, size);
                    SurvivorBurst.Spawn(en.Position + Vector3.up * en.hitHeight, bright, 8);
                }
            }
            if (flashHit) en.lightHp -= en.cfg.drain * FlashDamage * FlashLevel * dt;
            if (lantern > 0f) en.lightHp -= lantern * dt;
            // 光を外しても敵の体力は戻らない(照らした分はそのまま残る)
            en.ApplyGlow(lit, spotHit && lightsOn >= 2);
            if (lit && FxTick(ref en.fxTimer, dt, 0.12f)) SurvivorBurst.Spawn(en.Position + Vector3.up * en.hitHeight, new Color(1f, 0.15f, 0.08f), 4);
            if (en.lightHp <= 0f) KillEnemy(en);
        }
        return contactDps;
    }

    // プレイヤーを追う。通路が見通せるなら直進し、見通せない(カーブ・分岐の向こう)ときは通路グラフに沿って進む。
    // 進みたい方向が壁(通路の外)で塞がれる角では、通れる方向へ少しずつ曲げて壁沿いに滑るので、角でスタックしない。
    // それでも詰まったら、しばらく通路グラフの経路だけに従って回り込む。
    static readonly float[] SlideAngles = { 0f, 20f, -20f, 40f, -40f, 65f, -65f, 90f, -90f, 125f, -125f };

    // 敵から主人公が見通せるか。通路の判定をたくさん呼ぶ重い処理なので、結果を0.15秒ずつ使い回す
    bool SeesPlayer(NightEnemy en, Vector3 pos, Vector3 pp)
    {
        if (Time.time >= en.sightTimer)
        {
            en.sightCached = Map.ClearSight(pos, pp);
            en.sightTimer = Time.time + 0.15f + Random.value * 0.05f; // 全員が同じフレームに調べないよう少しずらす
        }
        return en.sightCached;
    }

    void AdvanceEnemy(NightEnemy en, float dt)
    {
        Vector3 pos = en.Position;
        Vector3 pp = PlayerPos;
        en.noDirectTimer -= dt;

        Vector3 target;
        if (en.noDirectTimer <= 0f && SeesPlayer(en, pos, pp))
        {
            target = pp;
            en.path = null;
        }
        else
        {
            en.navTimer -= dt;
            if (en.navTimer <= 0f || en.path == null || en.path.Count == 0)
            {
                en.path = Map.NavigationPath(pos, pp);
                en.navTimer = 0.4f;
            }
            while (en.path.Count > 1 && NightMap.Flat(pos, en.path[0]) < 0.7f) en.path.RemoveAt(0);
            target = en.path.Count > 0 ? en.path[0] : pp;
        }

        float dx = target.x - pos.x, dz = target.z - pos.z, d = Mathf.Sqrt(dx * dx + dz * dz);
        if (d < 0.05f) { dx = pp.x - pos.x; dz = pp.z - pos.z; d = Mathf.Sqrt(dx * dx + dz * dz); }
        float step = Mathf.Min(Mathf.Max(d, 0.0001f), en.cfg.speed * Difficulty.enemySpeed * dt);
        Vector3 before = pos;
        if (d > 0.01f)
        {
            float baseAngle = Mathf.Atan2(dx, dz);
            // 進みたい方向から順に、歩ける方向を探す(正面→少し曲げる→大きく曲げる)
            for (int i = 0; i < SlideAngles.Length; i++)
            {
                float a = baseAngle + SlideAngles[i] * Mathf.Deg2Rad;
                float nx = pos.x + Mathf.Sin(a) * step, nz = pos.z + Mathf.Cos(a) * step;
                if (!Map.Walkable(nx, nz, 0.3f)) continue;
                pos.x = nx; pos.z = nz;
                en.go.transform.rotation = Quaternion.Euler(0, a * Mathf.Rad2Deg, 0);
                break;
            }
        }
        Map.ConstrainActor(ref pos, 0.35f);
        en.Position = pos;

        // それでも進めない状態が続いたら、0.8秒だけ直進をやめて通路の経路(中心線)に従う
        float moved = NightMap.Flat(before, pos);
        en.stuckTimer = moved < step * 0.3f ? en.stuckTimer + dt : 0f;
        if (en.stuckTimer > 0.3f) { en.navTimer = 0f; en.path = null; en.noDirectTimer = 0.8f; en.stuckTimer = 0f; }
    }

    // すり抜け型: 通路も壁も無視して、プレイヤーへまっすぐ進む
    void PhaseTowardPlayer(NightEnemy en, float dt)
    {
        Vector3 pos = en.Position;
        Vector3 to = PlayerPos - pos; to.y = 0f;
        float d = to.magnitude;
        if (d < 0.01f) return;
        pos += to / d * Mathf.Min(d, en.cfg.speed * Difficulty.enemySpeed * dt);
        FollowGround(ref pos, dt);
        en.Position = pos;
        en.go.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0);
    }

    // 通路の外でも、いちばん近い地面の高さへなめらかに合わせる(段差の上下で急にワープしないように)
    void FollowGround(ref Vector3 pos, float dt)
    {
        pos.y = Mathf.MoveTowards(pos.y, Map.SurfaceAt(pos.x, pos.z), 6f * dt);
    }

    void KillEnemy(NightEnemy en)
    {
        if (en.dying) return;
        en.dying = true;
        en.dieTimer = 0.4f;
        // 直前(1.5秒以内)までスマホの光が当たっていたら、そのスマホの撃破として知らせる(閃光・ランタンでとどめを刺しても)。
        // 画面の撃破数(チーム全体)は、スマホの人の撃破数を足したものと同じにする。
        // スマホの光が当たっていない敵をランタンや雷だけで倒したときは、誰の撃破にも数えない
        if (en.litBy != null && Time.time - en.litByTime < 1.5f && PointerServer.Instance != null
            && PointerServer.Instance.AddKill(en.litBy))
            KillCount++;
        Audio.PlayDownChime();
        if (Camouflaged && !CampLocked)
        {
            Camp = Mathf.Min(CampMax, Camp + AmbushKillRefill);
            AmbushRefillFlash = 1f;
        }
        // 倒した手ごたえ: 光の粒を多めに飛ばし、足元から衝撃波の輪を広げる
        SurvivorBurst.Spawn(en.Position + Vector3.up * 0.4f * en.cfg.scale, new Color(1f, 0.85f, 0.45f),
            en.kind == NightEnemy.Kind.Brute ? 22 : 12);
        Vector3 ep = en.Position;
        AddPulse(new Vector3(ep.x, ep.y + 0.1f, ep.z), 0.32f, 0.5f, new Color(1f, 0.9f, 0.6f, 0.85f), 0.35f,
            en.kind == NightEnemy.Kind.Brute ? 5f : 3.2f);
        if (Practice && Step == TutorialStep.Defeat) TutorialKills++;
        if (!Practice && Random.value < HealOrbChance) DropHealOrb(en); // 練習では回復オーブは落とさない
    }

    // ---- 練習モードの敵(4種類を1体ずつ、決まった場所に置く。倒すと少したって同じ場所に復活) ----
    class PracticeSlot { public NightEnemy.Kind kind; public Vector3 home; public NightEnemy en; public float wait; }
    readonly List<PracticeSlot> practiceSlots = new List<PracticeSlot>();
    const float PracticeLight = 0.15f;  // 練習の森の明るさ(雷の明るさを1としたとき。0 だと真っ暗で敵が見えない)
    const float PracticeRespawn = 4f;   // 倒してから復活するまでの秒数
    static readonly NightEnemy.Kind[] PracticeEnemies = { NightEnemy.Kind.Moth, NightEnemy.Kind.Shooter, NightEnemy.Kind.Brute, NightEnemy.Kind.Sprite };
    // 広場の左右に2体ずつ(入口のランタンの光が届かない所)。蛍の石は奥のまん中
    static readonly Vector3[] PracticeHomes = { new Vector3(-8f, 0, -11f), new Vector3(8f, 0, -11f), new Vector3(-8f, 0, -19f), new Vector3(8f, 0, -19f) };

    void SpawnPracticeEnemy(PracticeSlot slot, bool effect)
    {
        Vector3 p = slot.home;
        p.y = Map.SurfaceAt(p.x, p.z);
        var en = NightEnemy.Spawn(slot.kind, p);
        en.lightHp = en.lightHpMax = en.cfg.lightHp * Difficulty.enemyHp;
        en.go.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(PlayerPos.x - p.x, PlayerPos.z - p.z) * Mathf.Rad2Deg, 0);
        Enemies.Add(en);
        slot.en = en;
        if (!effect) return;
        // 復活した合図: 足元から輪が広がり、光の粒が出る
        AddPulse(new Vector3(p.x, p.y + 0.1f, p.z), 0.32f, 0.5f, new Color(0.75f, 0.6f, 1f, 0.85f), 0.45f, 3.5f);
        SurvivorBurst.Spawn(p + Vector3.up * 0.6f, new Color(0.8f, 0.65f, 1f), 10);
    }

    // ---- チュートリアルの進行 ----
    static readonly Vector3 TutorialSpotPos = new Vector3(0f, 0f, -12f); // 「移動しよう」で歩いていくスポット(広場のまん中)
    Vector3 tutorialSpot;
    Transform spotRoot;
    Material spotBeamMat;

    public string TutorialTitle => Step == TutorialStep.Move ? "移動しよう" : Step == TutorialStep.Defeat ? "敵を倒そう"
                                 : Step == TutorialStep.Fireflies ? "ホタルを集めよう" : "門から脱出しよう";
    public string TutorialTodo => Step == TutorialStep.Move ? "前方の光るスポットまで移動しよう"
        : Step == TutorialStep.Defeat ? "ライトを当て続けて、敵を" + TutorialKillGoal + "体倒そう(" + Mathf.Min(TutorialKills, TutorialKillGoal) + " / " + TutorialKillGoal + ")"
        : Step == TutorialStep.Fireflies ? (World.Orbs.Count > 0 && World.Orbs[0].materialized ? "光った石に近づいて、ホタルを捕まえよう" : "奥の石に光を当てて、鍵となるホタルを集めよう")
        : "開いた入口の門をくぐって脱出しよう";
    string TutorialHint => Step == TutorialStep.Move ? "カメレオン役はマットを踏んで移動します(キーボードは WASD / 矢印キー)。"
        : Step == TutorialStep.Defeat ? "灯り役はスマホのライトを敵に当て続けよう。敵はその場から動かず、倒すと少したって復活します。"
        : Step == TutorialStep.Fireflies ? "石に光を当て続けるとゲージがたまり、ホタルが集まる。何人かで照らすと早くたまる。"
        : "ホタルが鍵になって、入口の門が開いた！ 後ろの門をくぐろう。";
    public int TutorialStepNumber => (int)Step + 1;
    // 今のステップの進み具合(0〜1)。上の目標パネルのバーに使う
    public float TutorialProgress
    {
        get
        {
            switch (Step)
            {
                case TutorialStep.Move: return Mathf.Clamp01(1f - (NightMap.Flat(PlayerPos, tutorialSpot) - 1.6f) / 9f);
                case TutorialStep.Defeat: return Mathf.Clamp01(TutorialKills / (float)TutorialKillGoal);
                case TutorialStep.Fireflies: return World.Orbs.Count == 0 ? 0f : World.Orbs[0].collected ? 1f : World.Orbs[0].reveal / World.Orbs[0].RevealTime;
                default: return 0f;
            }
        }
    }

    void AnnounceStep()
    {
        string sub = Step == TutorialStep.Move ? "前方の光るスポットまで歩こう"
                   : Step == TutorialStep.Defeat ? "ライトを当てると敵が倒れる！ " + TutorialKillGoal + "体倒そう"
                   : Step == TutorialStep.Fireflies ? "奥の石を照らして、鍵となるホタルを集めよう"
                   : "入口の門が開いた！ くぐって脱出しよう";
        Announce("STEP " + TutorialStepNumber + "  " + TutorialTitle, sub);
    }

    void SetStep(TutorialStep step)
    {
        Step = step;
        Audio.PlayPulse();
        AnnounceStep();
    }

    // 「移動しよう」の目印: 地面の光る輪と、上へのびる光の柱
    void BuildTutorialSpot()
    {
        tutorialSpot = TutorialSpotPos;
        tutorialSpot.y = Map.SurfaceAt(tutorialSpot.x, tutorialSpot.z);
        spotRoot = new GameObject("TutorialSpot").transform;
        spotRoot.SetParent(World.Root, false);
        spotRoot.position = tutorialSpot;
        Color gold = NightMeshes.Hex(0xffe066);
        World.RingObject(1.25f, 1.55f, NightMeshes.Unlit(new Color(gold.r, gold.g, gold.b, 0.95f)), spotRoot, new Vector3(0, 0.08f, 0));
        NightMeshes.MeshObject("SpotDisc", NightMeshes.Disc(1.25f), NightMeshes.Unlit(new Color(gold.r, gold.g, gold.b, 0.22f)), spotRoot)
            .transform.localPosition = new Vector3(0, 0.06f, 0);
        spotBeamMat = NightMeshes.Unlit(new Color(gold.r, gold.g, gold.b, 0.18f));
        World.Cylinder(0, 2f, 0, 1.1f, 4f, spotBeamMat, spotRoot);
        NightWorld.PointLight(spotRoot, new Vector3(0, 1.5f, 0), gold, 2.5f, 6f);
    }

    void UpdateTutorial(float dt)
    {
        switch (Step)
        {
            case TutorialStep.Move:
                if (spotRoot != null)
                {
                    float k = 1f + 0.08f * Mathf.Sin(Time.time * 4f);
                    spotRoot.localScale = new Vector3(k, 1f, k);
                    Color c = spotBeamMat.color; c.a = 0.14f + 0.08f * Mathf.Sin(Time.time * 4f); spotBeamMat.color = c;
                }
                if (NightMap.Flat(PlayerPos, tutorialSpot) < 1.6f)
                {
                    AddPulse(tutorialSpot + Vector3.up * 0.1f, 0.4f, 0.6f, NightMeshes.Hex(0xffe066, 0.9f), 0.6f, 5f);
                    if (spotRoot != null) Destroy(spotRoot.gameObject);
                    spotRoot = null;
                    // 4種類の敵が台座の上に現れる
                    foreach (var slot in practiceSlots) SpawnPracticeEnemy(slot, true);
                    SetStep(TutorialStep.Defeat);
                }
                break;
            case TutorialStep.Defeat:
                UpdatePracticeEnemies(dt);
                if (TutorialKills >= TutorialKillGoal)
                {
                    // 残っている敵は消えて、ホタル集めに集中できるようにする(撃破には数えない)
                    foreach (var en in Enemies)
                    {
                        if (en.dying) continue;
                        en.dying = true;
                        en.dieTimer = 0.4f;
                        SurvivorBurst.Spawn(en.Position + Vector3.up * 0.6f, new Color(0.8f, 0.65f, 1f), 10);
                    }
                    SetStep(TutorialStep.Fireflies);
                }
                break;
        }
    }

    void UpdatePracticeEnemies(float dt)
    {
        foreach (var slot in practiceSlots)
        {
            if (slot.en != null && slot.en.dying) { slot.en = null; slot.wait = PracticeRespawn; }
            if (slot.en != null) continue;
            slot.wait -= dt;
            if (slot.wait <= 0f) SpawnPracticeEnemy(slot, true);
        }
    }

    // ---- 回復オーブ ----
    void DropHealOrb(NightEnemy en)
    {
        if (en.cfg.noHealDrop) return; // すり抜け型は落とさない
        float amount = en.kind == NightEnemy.Kind.Brute ? 14f : en.kind == NightEnemy.Kind.Shooter ? 6f : 4f;
        DropHealOrb(en.Position, amount);
    }

    void DropHealOrb(Vector3 p, float amount)
    {
        if (healCoreMat == null)
        {
            healCoreMat = NightMeshes.Unlit(new Color(0.55f, 1f, 0.6f, 1f));
            healHaloMat = NightMeshes.Unlit(new Color(0.4f, 1f, 0.5f, 0.22f));
        }
        var core = NightMeshes.Primitive(PrimitiveType.Sphere, null, healCoreMat).transform;
        core.name = "HealOrb";
        float size = 0.22f + amount * 0.012f; // たくさん回復するオーブほど大きい
        core.localScale = Vector3.one * size;
        var halo = NightMeshes.Primitive(PrimitiveType.Sphere, core, healHaloMat).transform;
        halo.localScale = Vector3.one * 2.2f;
        float baseY = Map.SurfaceAt(p.x, p.z) + 0.45f;
        core.position = new Vector3(p.x, baseY + 0.6f, p.z);
        // 倒した場所から少し跳ねて落ちる
        Vector2 dir = Random.insideUnitCircle.normalized * Random.Range(0.6f, 1.6f);
        healOrbs.Add(new HealOrb { t = core, halo = halo, amount = amount, baseY = baseY, vel = new Vector3(dir.x, 3.2f, dir.y) });
    }

    void UpdateHealOrbs(float dt)
    {
        Vector3 pp = PlayerPos + Vector3.up * 0.4f;
        for (int i = healOrbs.Count - 1; i >= 0; i--)
        {
            var o = healOrbs[i];
            o.age += dt;
            Vector3 pos = o.t.position;
            float d = Vector3.Distance(pos, pp);
            if (d < HealMagnet)
            {
                // 近づくと吸い寄せられる(近いほど速く)
                float speed = Mathf.Lerp(9f, 3f, d / HealMagnet);
                pos = Vector3.MoveTowards(pos, pp, speed * dt);
                o.vel = Vector3.zero;
            }
            else if (o.vel.sqrMagnitude > 0f)
            {
                o.vel.y -= 12f * dt;
                pos += o.vel * dt;
                if (pos.y <= o.baseY) { pos.y = o.baseY; o.vel = Vector3.zero; }
            }
            else pos.y = o.baseY + Mathf.Sin((o.age + i) * 3f) * 0.08f; // 落ちた後はふわふわ浮く
            o.t.position = pos;
            o.halo.localScale = Vector3.one * (2.1f + 0.35f * Mathf.Sin(o.age * 6f));

            // 消える3秒前から点滅
            float left = HealOrbLife - o.age;
            bool visible = left > 3f || Mathf.Repeat(o.age * (left > 1.2f ? 4f : 9f), 1f) < 0.6f;
            if (o.t.gameObject.activeSelf != visible) o.t.gameObject.SetActive(visible);

            if (Vector3.Distance(pos, pp) < HealPickup)
            {
                Heal(o.amount);
                SurvivorBurst.Spawn(pos, new Color(0.55f, 1f, 0.6f), 6);
                Object.Destroy(o.t.gameObject);
                healOrbs.RemoveAt(i);
            }
            else if (left <= 0f)
            {
                Object.Destroy(o.t.gameObject);
                healOrbs.RemoveAt(i);
            }
        }
    }

    void Heal(float amount)
    {
        float before = Hp;
        Hp = Mathf.Min(HpMax, Hp + amount);
        float healed = Hp - before;
        LastHeal = Mathf.RoundToInt(amount);
        HealFlash = 1f;
        Audio.PlayPulse();
        if (healed <= 0f) return; // HPが満タンのときは脅威レベルに数えない
        HealedTotal += healed;
        healedSinceThreat += healed;
        if (healedSinceThreat >= HealPerThreat && Threat < ThreatMax)
        {
            healedSinceThreat -= HealPerThreat;
            RaiseThreat("回復した分だけ、敵も集まってきた…", 1);
        }
    }

    // 動作確認(NightAutoShot)用: HPを減らし、プレイヤーの周りに回復オーブを落とす / 脅威レベルを上げる
    public void DebugHealTest()
    {
        Hp = 40f;
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f;
            DropHealOrb(PlayerPos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 4.5f, i == 0 ? 14f : 6f);
        }
    }
    // 動作確認(NightAutoShot)用: プレイヤーの近く(4〜7m)に敵を出す
    public void DebugSpawnNear(int count) => DebugSpawnNear(count, 50f); // 撮影中に倒れないように硬くする

    public void DebugSpawnNear(int count, float hpScale)
    {
        int made = 0;
        for (int attempt = 0; attempt < 200 && made < count; attempt++)
        {
            Vector2 r = Random.insideUnitCircle.normalized * Random.Range(4f, 7f);
            float x = PlayerPos.x + r.x, z = PlayerPos.z + r.y;
            if (!Map.Walkable(x, z, 0.5f)) continue;
            var en = NightEnemy.Spawn(PickType(), new Vector3(x, Map.SurfaceAt(x, z), z));
            en.lightHp = en.lightHpMax = en.cfg.lightHp * Difficulty.enemyHp * hpScale;
            Enemies.Add(en);
            made++;
        }
    }

    // 紹介動画の撮影(NightDemoVideo)用
    public void DebugSetHp(float hp) => Hp = Mathf.Clamp(hp, 0f, HpMax);
    public bool DebugNoCamo; // 動作確認用: 止まっていても擬態しない(敵が追ってくる様子を見るため)
    public void DebugFlashIn(float seconds) { if (FlashT < 0f) FlashWait = seconds; }
    public int DebugHealOrbCount => healOrbs.Count;
    public Vector3 DebugNearestHealOrb()
    {
        Vector3 best = PlayerPos; float bd = float.MaxValue;
        foreach (var o in healOrbs) { float d = NightMap.Flat(o.t.position, PlayerPos); if (d < bd) { bd = d; best = o.t.position; } }
        return best;
    }

    public void DebugRaiseThreat() => RaiseThreat("ホタルを捕まえた…森がざわめき出した", 4);

    // ---- 脅威レベル(敵が増える) ----
    void RaiseThreat(string reason, int wave)
    {
        if (Threat >= ThreatMax) return;
        Threat++;
        AlertText = "敵が増えた！";
        AlertSub = reason + "  脅威レベル " + Threat;
        AlertTimer = AlertDuration;
        Audio.PlayRumble();
        for (int i = 0; i < wave; i++) SpawnEnemy(PickType()); // その場で群れが押し寄せる
    }

    void SpawnBullet(Vector3 from, Vector3 target)
    {
        var go = NightMeshes.Primitive(PrimitiveType.Sphere, null, bulletMat);
        go.name = "EnemyBullet";
        go.transform.localScale = Vector3.one * 0.4f;
        go.transform.position = new Vector3(from.x, from.y + 0.5f, from.z);
        bullets.Add(new NightBullet { t = go.transform, dir = (target - from).normalized });
    }

    void UpdateBullets(float dt)
    {
        Vector3 pp = PlayerPos;
        for (int i = bullets.Count - 1; i >= 0; i--)
        {
            var b = bullets[i];
            b.life -= dt;
            b.t.position += b.dir * b.speed * dt;
            Vector3 p = b.t.position;
            float d = Mathf.Sqrt((p.x - pp.x) * (p.x - pp.x) + (p.z - pp.z) * (p.z - pp.z));
            if (d < 0.7f && Mathf.Abs(p.y - pp.y - 0.5f) < 0.8f)
            {
                if (!Camouflaged) { Hp = Mathf.Max(0f, Hp - 12f * Difficulty.damage); Audio.PlayHurt(); }
                Destroy(b.t.gameObject);
                bullets.RemoveAt(i);
                continue;
            }
            if (b.life <= 0f || !Map.Walkable(p.x, p.z, 0.05f))
            {
                Destroy(b.t.gameObject);
                bullets.RemoveAt(i);
            }
        }
    }

    // ---- 擬態の始まり・終わり ----
    void OnCamouflageStart()
    {
        Audio.PlayPulse();
        // 周りの色に溶け込む合図の、小さく静かな波紋(敵を押し返す力は無い)
        AddPulse(PlayerPos + Vector3.up * 0.15f, 0.4f, 0.6f, NightMeshes.Hex(0x8fe6ff, 0.5f), 0.5f, 3.5f);
        foreach (var en in Enemies)
        {
            en.wanderTimer = 0f; // すぐに新しい行き先を選ばせる
            en.path = null;
        }
    }

    // 擬態が解けたら、近くの敵はプレイヤーに気づいて「！」を出し、また追いかけ始める
    void OnCamouflageEnd()
    {
        foreach (var en in Enemies)
        {
            if (en.dying) continue;
            if (NightMap.Flat(en.Position, PlayerPos) < 16f) en.foundTimer = 0.8f;
            en.path = null;
            en.navTimer = 0f;
        }
    }

    // 見失った敵: 近くの通路の上で、行き先を選んで歩く → 立ち止まって辺りを見回す、を繰り返す
    void WanderEnemy(NightEnemy en, float dt)
    {
        Vector3 pos = en.Position;
        en.wanderTimer -= dt;
        if (en.wanderTimer <= 0f)
        {
            en.wanderTarget = pos;
            for (int k = 0; k < 6; k++)
            {
                Vector2 r = Random.insideUnitCircle.normalized * Random.Range(1.2f, 3.2f);
                Vector3 t = pos + new Vector3(r.x, 0, r.y);
                if (Map.Walkable(t.x, t.z, 0.4f)) { en.wanderTarget = t; break; }
            }
            en.wanderPause = Random.value < 0.35f ? Random.Range(0.4f, 0.9f) : 0f; // ときどき立ち止まる
            en.wanderTimer = en.wanderPause + Random.Range(0.8f, 1.6f);
        }
        if (en.wanderPause > 0f)
        {
            en.wanderPause -= dt;
            // 立ち止まって左右を見回す
            en.go.transform.rotation *= Quaternion.Euler(0, Mathf.Sin(Time.time * 3f + en.go.GetInstanceID()) * 90f * dt, 0);
            return;
        }
        Vector3 to = en.wanderTarget - pos; to.y = 0;
        if (to.magnitude < 0.2f) return;
        float speed = en.cfg.speed * Difficulty.enemySpeed * 0.35f;
        Vector3 next = pos + to.normalized * Mathf.Min(to.magnitude, speed * dt);
        if (en.cfg.phasing) FollowGround(ref next, dt);
        else Map.ConstrainActor(ref next, 0.35f);
        en.Position = next;
        Quaternion face = Quaternion.Euler(0, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0);
        en.go.transform.rotation = Quaternion.Slerp(en.go.transform.rotation, face, Mathf.Min(1f, dt * 6f));
    }

    void AddPulse(Vector3 pos, float inner, float outer, Color color, float duration, float maxScale)
    {
        var mat = NightMeshes.Unlit(color);
        var go = NightMeshes.MeshObject("Pulse", NightMeshes.Ring(inner, outer, 40), mat, null);
        go.transform.position = pos;
        pulses.Add(new Pulse { t = go.transform, m = mat, duration = duration, maxScale = maxScale });
    }

    void UpdatePulses(float dt)
    {
        for (int i = pulses.Count - 1; i >= 0; i--)
        {
            var p = pulses[i];
            p.time += dt;
            float k = p.time / p.duration;
            p.t.localScale = Vector3.one * (1f + k * (p.maxScale - 1f));
            Color c = p.m.color; c.a = Mathf.Max(0f, 0.9f * (1f - k)); p.m.color = c;
            if (k >= 1f) { Destroy(p.t.gameObject); pulses.RemoveAt(i); }
        }
    }

    // ---- ランタン点灯: 半径7m・同じ高さ・光の届く範囲の敵を倒す ----
    public void IgniteLantern(NightLamp lamp)
    {
        AddPulse(new Vector3(lamp.x, lamp.height + 0.06f, lamp.z), 0.85f, 1f, NightMeshes.Hex(0xffd390, 0.9f), 0.8f, NightLamp.BurstRadius);
        foreach (var en in Enemies)
        {
            if (en.dying || !lamp.InLight(Map, en.Position, NightLamp.BurstRadius)) continue;
            en.lightHp = 0f;
            KillEnemy(en);
        }
        Audio.PlayPulse();
    }

    // ---- 導きの花 ----
    public void ActivateGuidePlant(NightGuidePlant plant)
    {
        Vector3 from = new Vector3(plant.x, 0, plant.z);
        NightOrb targetOrb = null;
        List<Vector3> bestPath = null;
        float bestDist = float.PositiveInfinity;
        foreach (var orb in World.Orbs)
        {
            if (orb.collected) continue;
            var path = Map.NavigationPath(from, orb.Position);
            float d = NightMap.PathLength(from, path);
            if (d < bestDist) { bestDist = d; bestPath = path; targetOrb = orb; }
        }
        Color color;
        if (targetOrb == null)
        {
            bestPath = Map.NavigationPath(from, new Vector3(NightMap.Exit.x, 0, NightMap.Exit.y));
            color = NightMeshes.Hex(0xafffd1);
        }
        else color = NightMeshes.Hex(0xffcf65); // ホタルの石はどれも黄色

        ClearGuidance();
        var g = new NightGuidance
        {
            plant = plant,
            orb = targetOrb,
            group = new GameObject("GuidePath"),
            material = NightMeshes.Unlit(new Color(color.r, color.g, color.b, 0.8f)),
            time = float.PositiveInfinity, // 一度示したら、その蛍を捕まえる(門なら脱出する)まで消さない
        };
        var leaf = NightMeshes.GuideLeaf();
        var path2 = new List<Vector3> { from };
        path2.AddRange(bestPath);
        float travelled = 0f, nextMark = 0.5f;
        for (int i = 1; i < path2.Count; i++)
        {
            Vector3 a = path2[i - 1], b = path2[i];
            float dx = b.x - a.x, dz = b.z - a.z, length = Mathf.Sqrt(dx * dx + dz * dz);
            if (length < 0.01f) continue;
            while (nextMark <= travelled + length)
            {
                float t = (nextMark - travelled) / length, x = a.x + dx * t, z = a.z + dz * t;
                if (Map.Walkable(x, z, 0.1f))
                {
                    var m = NightMeshes.MeshObject("GuideLeaf", leaf, g.material, g.group.transform);
                    m.transform.position = new Vector3(x, Map.SurfaceAt(x, z) + 0.22f, z);
                    m.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(-dx, -dz) * Mathf.Rad2Deg, 0);
                    g.markers.Add(m.transform);
                }
                nextMark += 1.2f;
            }
            travelled += length;
        }
        Guidance = g;
        Audio.PlayDownChime();
        ShowToast("導きの花が光の道を示した");
    }

    void ClearGuidance()
    {
        if (Guidance == null) return;
        Destroy(Guidance.group);
        Destroy(Guidance.material);
        Guidance = null;
    }

    void UpdateGuidance(float dt)
    {
        if (Guidance == null) return;
        var g = Guidance;
        g.time -= dt;
        g.age += dt;
        Color c = g.material.color; c.a = 0.8f * Mathf.Min(1f, g.time / 1.25f); g.material.color = c;
        for (int i = 0; i < g.markers.Count; i++)
            g.markers[i].localScale = Vector3.one * (0.85f + 0.25f * Mathf.Max(0f, Mathf.Sin(g.age * 4f - i * 0.65f)));
        if (g.time <= 0f) ClearGuidance();
    }

    // ---- 蛍の石・門 ----
    public void OnOrbCollected(NightOrb orb)
    {
        OrbCount++;
        World.Gate.UpdateSockets(World.Orbs);
        if (OrbCount < OrbGoal) Announce("ホタルをゲット！", OrbCount + " / " + OrbGoal + "  あと" + (OrbGoal - OrbCount) + "匹");
        Audio.PlayOrbGet();
        if (!Practice) RaiseThreat("ホタルを捕まえた…森がざわめき出した", 3 + Threat);
        if (OrbCount >= OrbGoal && !World.Gate.opened)
        {
            World.Gate.Open();
            ClearGuidance();
            if (Practice) SetStep(TutorialStep.Escape);
            else Announce("門が開いた！", "ホタルが揃った！入口へ戻ろう");
            Audio.PlayPulse();
        }
    }

    // ---- 閃光(予兆 → 稲妻のように一撃 → 一瞬暗く → 二撃目 → 保持 → 余韻) ----
    static float FlashShape(float t)
    {
        if (t < 0.05f) return t / 0.05f;
        if (t < 0.15f) return 1f - (t - 0.05f) / 0.1f * 0.7f;
        if (t < 0.2f) return 0.3f + (t - 0.15f) / 0.05f * 0.7f;
        if (t < 0.2f + FlashHold) return 1f;
        return Mathf.Max(0f, 1f - (t - 0.2f - FlashHold) / FlashFall);
    }

    void UpdateFlash(float dt)
    {
        warnLevel = 0f;
        if (FlashT < 0f)
        {
            FlashWait -= dt;
            if (FlashWait <= NightAudio.FlashWarn && !warned) { warned = true; Audio.PlayRumble(); }
            if (FlashWait <= NightAudio.FlashWarn) warnLevel = Mathf.Max(0f, 0.1f + 0.1f * Mathf.Sin(Elapsed * 45f)); // ちらつき
            if (FlashWait <= 0f) { FlashT = 0f; warned = false; Audio.PlayThunder(); }
        }
        if (FlashT >= 0f)
        {
            FlashT += dt;
            FlashLevel = FlashShape(FlashT);
            if (FlashT >= FlashTotal) { FlashT = -1f; FlashLevel = 0f; FlashWait = FlashInterval; }
        }
        ApplyLighting(Mathf.Max(FlashLevel, warnLevel));
    }

    void ApplyLighting(float b)
    {
        RenderSettings.ambientLight = AmbientColor * (AmbientBase + b * 2.4f) * Brightness;
        if (Moon != null) Moon.intensity = (MoonBase + b * 1.2f) * Brightness;
        RenderSettings.fogDensity = FogDensityDark + (FogDensityBright - FogDensityDark) * b;
        RenderSettings.fogColor = Color.Lerp(FogDark, FogBright, b);
        if (Cam != null) Cam.backgroundColor = RenderSettings.fogColor;
    }

    // ---- カメラ ----
    void UpdateCamera(float dt)
    {
        Vector3 p = PlayerPos, v = Player.Velocity;
        float aimX = p.x + v.x * CameraLookAhead, aimZ = p.z + v.z * CameraLookAhead;
        float k = 1f - Mathf.Exp(-CameraLag * dt);
        cameraFocus.x += (aimX - cameraFocus.x) * k;
        cameraFocus.z += (aimZ - cameraFocus.z) * k;
        cameraFocus.y += (p.y - cameraFocus.y) * k;
        PlaceCamera();
    }

    void PlaceCamera()
    {
        if (Cam == null) return;
        Cam.transform.position = cameraFocus + CameraOffset;
        Cam.transform.LookAt(cameraFocus + Vector3.up * 0.6f);
    }

    // ---- スコア・ランキング(結果画面用) ----
    public int Score { get; private set; }
    public int ScoreSeconds { get; private set; }
    public int Rank { get; private set; }            // このマップでの順位(10位以内なら1〜10、圏外は0)
    public NightRanking.Entry MyEntry { get; private set; }
    // ランキングへの登録は結果画面で名前を入れてから。名前を入れなければ登録しない
    public bool RankingDone { get; private set; }   // 登録した、または登録しないと決めた
    public bool Registered { get; private set; }    // ランキングに登録した

    public void SubmitRanking(string name)
    {
        if (State != GameState.Ended || RankingDone) return;
        name = (name ?? "").Trim();
        if (name.Length > NightRanking.MaxNameLength) name = name.Substring(0, NightRanking.MaxNameLength);
        RankingDone = true;
        if (name.Length == 0) return; // 名無しはランキングに追加しない
        PlayerName = name;
        Registered = true;
        Rank = NightRanking.Add(MapId, PlayerName, Score, ScoreSeconds, KillCount, Cleared, out var entry);
        MyEntry = entry;
    }

    void End(bool win)
    {
        if (State == GameState.Ended) return;
        State = GameState.Ended;
        Cleared = win;
        ScoreSeconds = Mathf.FloorToInt(Elapsed);
        Score = NightRanking.ScoreOf(win, ScoreSeconds, KillCount);
        Rank = 0;
        MyEntry = null;
        RankingDone = Registered = false;
        // 自動テスト・紹介動画の撮影は、始めるときに名前を渡しているので、そのまま登録する(テスト用のファイルに入る)
        if (Practice) RankingDone = true; // 練習はランキングに登録しない
        else if (PlayerName != NightRanking.DefaultName) SubmitRanking(PlayerName);
        Player.SetAnimating(false);
        Audio.StopBgm();
        if (win) Audio.PlayClear(); else Audio.PlayGameOver();
    }
}
