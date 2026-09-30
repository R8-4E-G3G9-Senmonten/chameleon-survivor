using UnityEngine;

// 敵(獣)。HTMLモックアップの ENEMY_TYPES(moth/brute)を再現し、ギミック付きの3種を追加。
// プレイヤーへ接触するとHPを削る一方、攻撃手段はプレイヤー自身になく、
// 協力プレイヤー役の懐中電灯(FlashlightController)を当て続けるとダウンして倒せる。
// ただしプレイヤーのすぐ近く(LightDeadZone以内)には光が効かない。
//
// 種類:
//  Moth     小さく速い群れタイプ            (HTML試作と同じ)
//  Brute    遅いが硬く痛い                  (HTML試作と同じ)
//  Dasher   ため動作のあと一直線に突進する  (突進の直前は黄色く光って予告)
//  Skulker  光を避けて逃げる                (光を追い詰めて当てる必要がある)
//  Splitter 倒すと小さな蛾に分裂する
public class SurvivorPredator : MonoBehaviour
{
    public enum EnemyType { Moth, Brute, Dasher, Skulker, Splitter }

    struct TypeConfig
    {
        public float speed, contactDps, lightHpMax, lightDrainRate, regenRate, scale;
        public Color color;
    }

    static readonly TypeConfig Moth = new TypeConfig
    {
        speed = 2.6f, contactDps = 10f, lightHpMax = 34f, lightDrainRate = 44f, regenRate = 16f,
        scale = 0.55f, color = new Color(0.541f, 0.294f, 0.541f)
    };
    static readonly TypeConfig Brute = new TypeConfig
    {
        speed = 1.5f, contactDps = 22f, lightHpMax = 110f, lightDrainRate = 38f, regenRate = 20f,
        scale = 1.0f, color = new Color(0.541f, 0.231f, 0.231f)
    };
    static readonly TypeConfig Dasher = new TypeConfig
    {
        speed = 2.0f, contactDps = 16f, lightHpMax = 45f, lightDrainRate = 44f, regenRate = 18f,
        scale = 0.65f, color = new Color(0.82f, 0.72f, 0.2f)
    };
    static readonly TypeConfig Skulker = new TypeConfig
    {
        speed = 2.9f, contactDps = 12f, lightHpMax = 36f, lightDrainRate = 50f, regenRate = 22f,
        scale = 0.5f, color = new Color(0.2f, 0.68f, 0.74f)
    };
    static readonly TypeConfig Splitter = new TypeConfig
    {
        speed = 1.9f, contactDps = 14f, lightHpMax = 80f, lightDrainRate = 40f, regenRate = 18f,
        scale = 0.85f, color = new Color(0.86f, 0.5f, 0.2f)
    };

    static TypeConfig ConfigFor(EnemyType t)
    {
        switch (t)
        {
            case EnemyType.Brute: return Brute;
            case EnemyType.Dasher: return Dasher;
            case EnemyType.Skulker: return Skulker;
            case EnemyType.Splitter: return Splitter;
            default: return Moth;
        }
    }

    public const float LightRadius = 2.4f;   // この半径内の敵が光の効果を受ける
    public const float LightDeadZone = 1.6f; // プレイヤーに近すぎる敵には光が効かない
    const float ContactDistance = 0.9f;

    // Dasher
    const float DashWindup = 0.6f, DashTime = 0.45f, DashSpeed = 9f;
    // Skulker: 光の中心からこの距離以内に入ると逃げ出す
    const float SkulkerFleeRadius = LightRadius * 1.9f;

    TypeConfig cfg;
    float lightHp;
    bool dying;
    float dieTimer;

    Material bodyMat;
    Transform playerT;

    Transform blobT;
    Vector3 blobBaseScale;
    float blobBaseY;
    float pulsePhase;
    float sidestepTimer;
    float sidestepSign = 1f;

    int dashPhase;            // 0=通常 1=ため 2=突進
    float dashCooldown = 2f;
    float dashTimer;
    Vector3 dashDir;

    public EnemyType Type { get; private set; }
    public bool IsDying => dying;

    public static Color MapColor(EnemyType t) => ConfigFor(t).color;

    public void Initialize(EnemyType type)
    {
        Type = type;
        cfg = ConfigFor(type);
        lightHp = cfg.lightHpMax;
        dashCooldown = Random.Range(1.5f, 3f);
        BuildVisuals();
    }

    void BuildVisuals()
    {
        var blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        blob.name = "Blob";
        blob.transform.SetParent(transform, false);
        blob.transform.localScale = new Vector3(0.9f, 0.72f, 1.17f) * cfg.scale;
        blob.transform.localPosition = new Vector3(0, 0.4f * cfg.scale, 0);
        Destroy(blob.GetComponent<Collider>());
        blobT = blob.transform;
        blobBaseScale = blobT.localScale;
        blobBaseY = blobT.localPosition.y;
        pulsePhase = Random.value * Mathf.PI * 2f;

        bodyMat = EmissiveMaterialFactory.Create(cfg.color);
        blob.GetComponent<MeshRenderer>().material = bodyMat;

        var eyeMat = EmissiveMaterialFactory.Create(new Color(1f, 0.878f, 0.4f));
        eyeMat.SetColor("_EmissionColor", new Color(1f, 0.878f, 0.4f) * 1.4f);
        CreateEye(new Vector3(-0.16f * cfg.scale, 0.55f * cfg.scale, 0.55f * cfg.scale), eyeMat);
        CreateEye(new Vector3(0.16f * cfg.scale, 0.55f * cfg.scale, 0.55f * cfg.scale), eyeMat);
    }

    void CreateEye(Vector3 localPos, Material mat)
    {
        var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eye.name = "Eye";
        eye.transform.SetParent(transform, false);
        eye.transform.localScale = Vector3.one * 0.14f * cfg.scale;
        eye.transform.localPosition = localPos;
        Destroy(eye.GetComponent<Collider>());
        eye.GetComponent<MeshRenderer>().material = mat;
    }

    void Start()
    {
        playerT = SurvivorManager.Instance != null ? SurvivorManager.Instance.PlayerTransform : null;
    }

    void Update()
    {
        if (SurvivorManager.Instance == null || SurvivorManager.Instance.State != SurvivorManager.GameState.Playing) return;
        if (playerT == null) return;

        float dt = Time.deltaTime;

        if (dying)
        {
            dieTimer -= dt;
            float s = Mathf.Max(0f, dieTimer / 0.4f) * cfg.scale;
            transform.localScale = new Vector3(s, s, s);
            transform.position += Vector3.up * dt * 0.6f;
            if (dieTimer <= 0f) Destroy(gameObject);
            return;
        }

        Vector3 lightTarget = FlashlightController.Instance != null ? FlashlightController.Instance.GroundTarget : playerT.position;
        Vector3 toPlayer = playerT.position - transform.position;
        toPlayer.y = 0;
        float dist = toPlayer.magnitude;

        bool windup = false;
        if (dist > 0.7f)
        {
            Vector3 dir = toPlayer.normalized;
            float speed = cfg.speed;

            if (Type == EnemyType.Dasher)
            {
                UpdateDash(dt, dist, ref dir, ref speed, out windup);
            }
            else if (Type == EnemyType.Skulker)
            {
                Vector3 away = transform.position - lightTarget;
                away.y = 0;
                if (away.magnitude < SkulkerFleeRadius)
                {
                    dir = (away.normalized * 1.2f + dir * 0.3f).normalized;
                    speed *= 1.1f;
                }
            }

            // 木や岩に進路をふさがれて進めていない間は、少しの間ななめに迂回する(経路探索はしない簡易版)
            if (sidestepTimer > 0f)
            {
                sidestepTimer -= dt;
                dir = Quaternion.Euler(0, sidestepSign * 75f, 0) * dir;
            }

            Vector3 before = transform.position;
            Vector3 after = SurvivorObstacle.Resolve(before + dir * speed * dt, 0.35f * cfg.scale + 0.1f);
            transform.position = after;
            if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

            bool blocked = speed > 0.1f && (after - before).magnitude < speed * dt * 0.35f;
            if (blocked && sidestepTimer <= 0f)
            {
                sidestepTimer = 0.6f;
                sidestepSign = Random.value < 0.5f ? -1f : 1f;
            }
        }

        // 生き物らしい脈動(体がふくらんだり縮んだりする)と、はずむ上下動
        if (blobT != null)
        {
            float t = Time.time * 6f + pulsePhase;
            float pulse = 1f + Mathf.Sin(t) * 0.07f;
            blobT.localScale = new Vector3(blobBaseScale.x * pulse, blobBaseScale.y / pulse, blobBaseScale.z * pulse);
            blobT.localPosition = new Vector3(0, blobBaseY + Mathf.Abs(Mathf.Sin(t * 0.5f)) * 0.06f * cfg.scale, 0);
        }

        if (dist < ContactDistance)
        {
            SurvivorManager.Instance.ReportContact(cfg.contactDps);
        }

        float distToLight = Vector2.Distance(
            new Vector2(transform.position.x, transform.position.z),
            new Vector2(lightTarget.x, lightTarget.z));
        bool lit = distToLight < LightRadius && dist > LightDeadZone;

        if (lit)
        {
            lightHp -= cfg.lightDrainRate * dt;
        }
        else
        {
            lightHp = Mathf.Min(cfg.lightHpMax, lightHp + cfg.regenRate * dt);
        }

        // ダメージ量(dmgRatio)だけで光の強さを決めていると、光が当たった直後はまだダメージが
        // 蓄積していないためほとんど光らず、「今まさに光が当たっている」ことが分かりにくかった。
        // そこで「光を受けている間は即座に基本の輝きが出る」ようにし、ダメージが溜まるほど
        // 赤みを帯びて強く光るようにする。光が外れた後も、残っているダメージ分だけ薄く残光させる。
        float dmgRatio = 1f - Mathf.Clamp01(lightHp / cfg.lightHpMax);
        Color litColor = Color.Lerp(new Color(1f, 0.95f, 0.55f), new Color(1.2f, 0.35f, 0.1f), dmgRatio);
        float intensity = lit ? Mathf.Lerp(0.9f, 2.4f, dmgRatio) : dmgRatio * 0.5f;
        Color emission = litColor * intensity;
        if (windup) emission = new Color(1f, 0.9f, 0.25f) * (1.2f + Mathf.Sin(Time.time * 30f) * 0.5f); // 突進の予告
        bodyMat.SetColor("_EmissionColor", emission);

        if (lightHp <= 0f) Die();
    }

    // ため(その場で光る)→ 一直線に突進 → クールダウン、を繰り返す
    void UpdateDash(float dt, float dist, ref Vector3 dir, ref float speed, out bool windup)
    {
        windup = false;
        if (dashPhase == 0)
        {
            dashCooldown -= dt;
            if (dashCooldown <= 0f && dist < 9f)
            {
                dashPhase = 1;
                dashTimer = DashWindup;
            }
        }
        else if (dashPhase == 1)
        {
            dashTimer -= dt;
            speed = 0f;
            windup = true;
            dashDir = dir; // ためている間もプレイヤーの方を向き続け、最後の向きで突進する
            if (dashTimer <= 0f)
            {
                dashPhase = 2;
                dashTimer = DashTime;
            }
        }
        else
        {
            dashTimer -= dt;
            dir = dashDir;
            speed = DashSpeed;
            if (dashTimer <= 0f)
            {
                dashPhase = 0;
                dashCooldown = Random.Range(2.5f, 4f);
            }
        }
    }

    void Die()
    {
        dying = true;
        dieTimer = 0.4f;
        SurvivorManager.Instance.OnEnemyKilled();
        SurvivorManager.Instance.Audio?.PlayDown();
        SurvivorBurst.Spawn(transform.position + Vector3.up * 0.4f * cfg.scale, new Color(1f, 0.85f, 0.45f), Type == EnemyType.Brute ? 16 : 9);

        if (Type == EnemyType.Splitter)
        {
            // 倒すと小さな蛾が2体飛び出す
            SurvivorManager.Instance.SpawnEnemyAt(EnemyType.Moth, transform.position + new Vector3(0.8f, 0, 0));
            SurvivorManager.Instance.SpawnEnemyAt(EnemyType.Moth, transform.position + new Vector3(-0.8f, 0, 0));
        }
    }
}
