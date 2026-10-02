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
//  - 止まると擬態: ゲージがある間は無敵。擬態に入った瞬間に周囲をノックバックする。
//  - 球を2つ集めて入口の門を開き、帰還して脱出する。
//  - 十数秒に1回、雷鳴の予兆のあとに森全体が一瞬明るくなる(閃光)。範囲内の敵は光を浴びる。
public class NightGame : MonoBehaviour
{
    public static NightGame Instance { get; private set; }

    public enum GameState { Title, Playing, Paused, Ended }
    public GameState State { get; private set; } = GameState.Title;

    public NightWorld World;
    public NightPlayer Player;
    public NightFlashlight Flashlight;
    public Camera Cam;
    public Light Moon;
    public NightAudio Audio { get; private set; }

    public string MapId { get; private set; } = "A";
    public NightMap Map => World.Map;

    // ---- 能力値(レベルアップは無し。難易度は経過時間で上がる敵の出現ペースだけ) ----
    public const float HpMax = 100f, CampMax = 100f;
    const float CampUnlock = 35f, CampDrain = 40f, CampRecharge = 22f, CampEnterMin = 30f;
    const float LightPower = 1.25f;
    const float KnockRadius = 5f, KnockSpeed = 11f;
    public const int OrbTotal = 2;

    public float Hp { get; private set; } = HpMax;
    public float Camp { get; private set; } = CampMax;
    public bool CampLocked { get; private set; }
    public bool Camouflaged { get; private set; }
    public bool HurtNow { get; private set; }
    public float Elapsed { get; private set; }
    public int KillCount { get; private set; }
    public int OrbCount { get; private set; }
    public bool Cleared { get; private set; }
    public bool GateOpened => World.Gate != null && World.Gate.opened;
    float idleTimer;

    // ---- 閃光 ----
    const float FlashInterval = 12f, FlashHold = 0.35f, FlashFall = 1f;
    const float FlashTotal = 0.2f + FlashHold + FlashFall;
    const float FlashDamage = 0.6f, FlashRange = 26f;
    public float FlashWait { get; private set; } = 9f;
    public float FlashT { get; private set; } = -1f;
    public float FlashLevel { get; private set; }
    float warnLevel;
    bool warned;

    static readonly Color FogDark = NightMeshes.Hex(0x02040a), FogBright = NightMeshes.Hex(0x7d90b8);
    const float FogDensityDark = 0.026f, FogDensityBright = 0.006f;
    // 懐中電灯が無いとほとんど何も見えないくらい暗くする(閃光の時だけ大きく明るくなる)
    const float AmbientBase = 0.035f, MoonBase = 0.05f;
    static readonly Color AmbientColor = NightMeshes.Hex(0x223344);

    // ---- 敵・弾・衝撃波 ----
    public readonly List<NightEnemy> Enemies = new List<NightEnemy>();
    readonly List<NightBullet> bullets = new List<NightBullet>();
    class Pulse { public Transform t; public Material m; public float time, duration, maxScale; }
    readonly List<Pulse> pulses = new List<Pulse>();
    float spawnTimer = 1f;
    Material bulletMat;

    public NightGuidance Guidance { get; private set; }

    public Vector3 LightTarget { get; private set; }
    public Vector3 PlayerPos => Player.transform.position;

    // ---- 表示用 ----
    public string ToastText { get; private set; } = "";
    public float ToastTimer { get; private set; }
    public const string HintDefault = "花を約2秒照らすと、未回収の球まで光の道が現れます。ランタンは敵を撃退。";
    public string Hint { get; private set; } = HintDefault;

    // ---- カメラ(進行方向の少し先を狙い、遅れて追従する) ----
    static readonly Vector3 CameraOffset = new Vector3(0, 13, 12);
    const float CameraLag = 2.2f, CameraLookAhead = 0.8f;
    Vector3 cameraFocus;

    // ---- 設定(ESCメニュー) ----
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
        if (State != GameState.Title || id == MapId) return;
        MapId = id;
        PlayerPrefs.SetString(MapKey, id);
        PlayerPrefs.Save();
        BuildMap();
    }

    public void StartGame()
    {
        if (State != GameState.Title) return;
        State = GameState.Playing;
        Audio.StartAmbient();
        ShowToast(MapId + ". " + Map.Name + " — 球を2つ集め、入口の門から脱出しよう");
    }

    public void Restart()
    {
        Time.timeScale = 1f;
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

    public void ShowToast(string text, float duration = 1.6f)
    {
        ToastText = text;
        ToastTimer = duration;
    }

    void Update()
    {
        switch (State)
        {
            case GameState.Title:
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) StartGame();
                for (int i = 0; i < NightMap.Ids.Length; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) SelectMap(NightMap.Ids[i]);
                return;
            case GameState.Paused:
                if (Input.GetKeyDown(KeyCode.Escape)) Resume();
                return;
            case GameState.Ended:
                if (Input.GetKeyDown(KeyCode.R)) Restart();
                return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            State = GameState.Paused;
            Time.timeScale = 0f;
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

        // --- 擬態(止まる=ギミック): ゲージがある間は無敵。入った瞬間にノックバック ---
        bool wasCamouflaged = Camouflaged;
        if (isMoving)
        {
            Camouflaged = false;
            Camp = Mathf.Min(CampMax, Camp + CampRecharge * dt);
            if (CampLocked && Camp >= CampUnlock) CampLocked = false;
        }
        else if (idleTimer > 0.3f && !CampLocked)
        {
            if (!wasCamouflaged && Camp >= CampEnterMin) { Camouflaged = true; KnockbackPulse(); }
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
        Player.UpdateLook(dt, Camouflaged, World.NearestTerrainColor(PlayerPos), HurtNow);

        // --- 灯り ---
        Flashlight.Aim(Cam, Map, PlayerPos.y);
        LightTarget = Flashlight.Target;
        World.UpdateGlow(LightTarget, dt);
        foreach (var lamp in World.Lamps) lamp.Tick(this, dt);
        if (Guidance != null && Guidance.orb != null && Guidance.orb.collected) ClearGuidance();
        foreach (var plant in World.Plants) plant.Tick(this, dt);
        UpdateGuidance(dt);

        World.UpdateCanopies(PlayerPos);
        UpdateFlash(dt);

        // --- 出現 ---
        spawnTimer -= dt;
        if (spawnTimer <= 0f)
        {
            SpawnEnemy(PickType());
            spawnTimer = Mathf.Max(0.7f, 2.4f - Elapsed * 0.012f) * (GateOpened ? 0.85f : 1f);
        }

        // --- 敵 ---
        float contactDps = UpdateEnemies(dt);

        // --- 被ダメージ(擬態中は無敵) ---
        HurtNow = contactDps > 0f && !Camouflaged;
        if (HurtNow)
        {
            Hp = Mathf.Max(0f, Hp - contactDps * dt);
            if (Random.value < dt * 3f) Audio.PlayHurt();
        }
        if (Hp <= 0f) { End(false); return; }

        UpdateBullets(dt);
        if (Hp <= 0f) { End(false); return; }

        // --- 光の球・門 ---
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

        Hint = CampLocked ? "擬態ゲージ切れ…動いてゲージを回復しよう"
            : GateOpened ? "入口の門が開いた！導きの花を照らすと、門への光の道が現れます。" : HintDefault;

        UpdatePulses(dt);
        UpdateCamera(dt);
    }

    void LateUpdate()
    {
        if (State == GameState.Title) PlaceCamera();
    }

    // ---- 敵 ----
    NightEnemy.Kind PickType()
    {
        float r = Random.value;
        if (r < 0.25f) return NightEnemy.Kind.Brute;   // 25% 硬い型
        if (r < 0.55f) return NightEnemy.Kind.Shooter; // 30% 遠距離型
        return NightEnemy.Kind.Moth;                   // 45% 群れ型
    }

    void SpawnEnemy(NightEnemy.Kind kind)
    {
        if (Enemies.Count >= 44) return;
        var segs = Map.Segments;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            var s = segs[Random.Range(0, segs.Count)];
            float t = Random.value;
            float x = s.a.x + (s.b.x - s.a.x) * t, z = s.a.z + (s.b.z - s.a.z) * t;
            float d = Mathf.Sqrt((x - PlayerPos.x) * (x - PlayerPos.x) + (z - PlayerPos.z) * (z - PlayerPos.z));
            if (d >= 12f && d <= 24f)
            {
                Enemies.Add(new NightEnemy(kind, new Vector3(x, Map.SurfaceAt(x, z), z)));
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
            Map.ConstrainActor(ref pos, 0.35f);
            pos.x = Mathf.Clamp(pos.x, -NightMap.HalfW - 2f, NightMap.HalfW + 2f);
            pos.z = Mathf.Clamp(pos.z, NightMap.GoalZ - 2f, NightMap.StartZ + 10f);
            en.Position = pos;

            Vector3 toPlayer = pp - pos; toPlayer.y = 0;
            float dist = toPlayer.magnitude;
            if (en.cfg.ranged && dist < 9f && Map.ClearSight(pos, pp))
            {
                // 遠距離型は一定距離で立ち止まって弾を撃つ
                en.go.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg, 0);
                en.shootTimer -= dt;
                if (en.shootTimer <= 0f) { SpawnBullet(pos, pp); en.shootTimer = 2.5f; }
            }
            else if (en.knock.magnitude < 2.5f && dist > 0.7f)
            {
                AdvanceEnemy(en, dt);
            }
            if (dist < 0.9f && Mathf.Abs(en.Position.y - pp.y) < 0.9f) contactDps = Mathf.Max(contactDps, en.cfg.contactDps);

            // 光: 当て続けるとダウンゲージが減る。プレイヤーのすぐ近く(死角)には効かない
            Vector3 ep = en.Position;
            float distToLight = Mathf.Sqrt((ep.x - LightTarget.x) * (ep.x - LightTarget.x) + (ep.z - LightTarget.z) * (ep.z - LightTarget.z));
            bool spotHit = distToLight < NightFlashlight.LightRadius && dist > NightFlashlight.DeadZone && Mathf.Abs(ep.y - LightTarget.y) < 1.8f;
            bool flashHit = FlashLevel > 0.05f && dist < FlashRange;
            float lantern = LanternDamageAt(ep);
            bool lit = spotHit || flashHit || lantern > 0f;
            if (spotHit)
            {
                en.lightHp -= en.cfg.drain * LightPower * dt;
                if (Random.value < dt * 8f) Audio.PlayHitBuzz();
            }
            if (flashHit) en.lightHp -= en.cfg.drain * FlashDamage * FlashLevel * dt;
            if (lantern > 0f) en.lightHp -= lantern * dt;
            if (!lit) en.lightHp = Mathf.Min(en.lightHpMax, en.lightHp + en.cfg.regen * dt);
            en.ApplyGlow(lit);
            if (en.lightHp <= 0f) KillEnemy(en);
        }
        return contactDps;
    }

    void AdvanceEnemy(NightEnemy en, float dt)
    {
        en.navTimer -= dt;
        Vector3 pos = en.Position;
        if (en.navTimer <= 0f || en.path == null || en.path.Count == 0)
        {
            en.path = Map.NavigationPath(pos, PlayerPos);
            en.navTimer = 0.65f;
        }
        while (en.path.Count > 1 && NightMap.Flat(pos, en.path[0]) < 0.7f) en.path.RemoveAt(0);
        if (en.path.Count == 0) return;
        Vector3 target = en.path[0];
        float dx = target.x - pos.x, dz = target.z - pos.z, d = Mathf.Sqrt(dx * dx + dz * dz);
        float step = Mathf.Min(d, en.cfg.speed * dt);
        if (d > 0.01f)
        {
            pos.x += dx / d * step;
            pos.z += dz / d * step;
            en.go.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(dx, dz) * Mathf.Rad2Deg, 0);
        }
        Map.ConstrainActor(ref pos, 0.35f);
        en.Position = pos;
    }

    void KillEnemy(NightEnemy en)
    {
        if (en.dying) return;
        en.dying = true;
        en.dieTimer = 0.4f;
        KillCount++;
        Audio.PlayDownChime();
        SurvivorBurst.Spawn(en.Position + Vector3.up * 0.4f * en.cfg.scale, new Color(1f, 0.85f, 0.45f),
            en.kind == NightEnemy.Kind.Brute ? 14 : 8);
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
                if (!Camouflaged) { Hp = Mathf.Max(0f, Hp - 12f); Audio.PlayHurt(); }
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

    // ---- 擬態に入った瞬間のノックバック ----
    void KnockbackPulse()
    {
        Audio.PlayPulse();
        AddPulse(PlayerPos + Vector3.up * 0.06f, 0.4f, 0.6f, NightMeshes.Hex(0x8fe6ff, 0.9f), 0.45f, 1f + KnockRadius * 1.6f);
        foreach (var en in Enemies)
        {
            if (en.dying) continue;
            Vector3 d = en.Position - PlayerPos; d.y = 0;
            float dist = d.magnitude;
            if (dist >= KnockRadius) continue;
            d = dist < 0.01f ? Vector3.right : d / dist;
            float w = 1f - dist / KnockRadius * 0.5f; // 近いほど強く飛ぶ
            en.knock = d * KnockSpeed * w;
        }
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
        else color = NightMeshes.Hex(targetOrb.index == 0 ? 0x78c8ff : 0xffcf65);

        ClearGuidance();
        var g = new NightGuidance
        {
            plant = plant,
            orb = targetOrb,
            group = new GameObject("GuidePath"),
            material = NightMeshes.Unlit(new Color(color.r, color.g, color.b, 0.8f)),
            time = 7f,
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
                    m.transform.position = new Vector3(x, Map.SurfaceAt(x, z) + 0.06f, z);
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

    // ---- 光の球・門 ----
    public void OnOrbCollected(NightOrb orb)
    {
        OrbCount++;
        World.Gate.UpdateSockets(World.Orbs);
        ShowToast("光の球を獲得！ " + OrbCount + " / " + OrbTotal);
        Audio.PlayDownChime();
        if (OrbCount >= OrbTotal && !World.Gate.opened)
        {
            World.Gate.Open();
            ClearGuidance();
            ShowToast("2つの球が揃った！入口の門が開いた！", 3.5f);
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

    void End(bool win)
    {
        if (State == GameState.Ended) return;
        State = GameState.Ended;
        Cleared = win;
        Player.SetAnimating(false);
        if (win) Audio.PlayClear(); else Audio.PlayGameOver();
    }
}
