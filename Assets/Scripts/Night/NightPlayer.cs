using UnityEngine;

// メインプレイヤー(カメレオン)。見た目は Meshy AI で作った3Dアセット。
// 移動は上下左右のみで、通路の上だけを歩ける(モックアップの walkable / constrainActor と同じ判定)。
// 入力がある間だけ歩行アニメーションを再生する。
public class NightPlayer : MonoBehaviour
{
    public GameObject characterModelPrefab;              // Assets/Models/ChameleonMeshy/Chameleon.fbx
    public RuntimeAnimatorController animatorController; // Assets/Models/ChameleonMeshy/ChameleonAnimator.controller
    // 見た目の大きさ(1 = 元の大きさ)。画面の中で主人公がよく見えるよう 1.7倍にしている。
    // 背中の炎・頭上のゲージの高さもこれに合わせて上がる。当たり判定(CollisionRadius)は変えないので、狭い通路も今まで通り通れる。
    public float modelScale = 1.7f;
    public float modelYawOffset = 0f;

    // 頭上のHP・擬態ゲージを出す高さ(HUD が使う)
    public float HeadHeight => 1.45f * modelScale;

    public const float MoveSpeed = 4.8f, Damping = 8f, CollisionRadius = 0.55f;

    public Vector3 Velocity;
    Animator animator;
    Renderer[] renderers;
    Color[] baseColors;
    Color tint = Color.white;
    Color[] texAverage;      // 各素材のテクスチャの平均色(擬態の色を正確に合わせるため)
    Color camoColor;
    float camoBlend;
    Texture[] baseTextures; // 元のテクスチャ(擬態中は地面の模様に差し替え、解けたら戻す)
    bool texSwapped;
    Light selfGlow;

    static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

    // ---- トーチの灯火 ----
    // 暗い森の中でも自分の位置を見失わないよう、カメレオンの背中に小さな炎を灯し、足元を暖色で照らす。
    // 擬態中は炎を小さく青白くして、「隠れている」状態が一目で分かるようにする(位置は分かる明るさは残す)。
    static readonly Color TorchColor = NightMeshes.Hex(0xffb15c), CamoTorchColor = new Color(0.45f, 0.75f, 1f);
    const float TorchHeight = 1.25f; // modelScale = 1 のときの高さ。大きさに合わせて上げる
    Transform flame, halo;
    Material flameMat, haloMat;
    float torchScale = 1f;

    void BuildTorch()
    {
        float torchY = TorchHeight * modelScale; // 体が大きくなっても背中の上に来るように
        selfGlow = NightWorld.PointLight(transform, new Vector3(0, torchY + 0.2f, 0), TorchColor, 1.9f, 7f);
        flameMat = NightMeshes.Unlit(new Color(1f, 0.82f, 0.45f, 0.95f));
        haloMat = NightMeshes.Unlit(new Color(1f, 0.6f, 0.25f, 0.28f));
        flame = NightMeshes.Primitive(PrimitiveType.Sphere, transform, flameMat).transform;
        flame.name = "TorchFlame";
        flame.localPosition = new Vector3(0, torchY, 0);
        halo = NightMeshes.Primitive(PrimitiveType.Sphere, transform, haloMat).transform;
        halo.name = "TorchHalo";
        halo.localPosition = new Vector3(0, torchY, 0);
    }

    void UpdateTorch(float dt, bool camouflaged)
    {
        if (selfGlow == null) return;
        float t = Time.time;
        // 炎のゆらぎ(2つの周期を重ねて、規則的に見えないようにする)
        float flicker = 1f + 0.08f * Mathf.Sin(t * 13f) + 0.05f * Mathf.Sin(t * 29f + 1.3f);
        torchScale = Mathf.Lerp(torchScale, camouflaged ? 0.6f : 1f, Mathf.Min(1f, dt * 6f));
        // 背中のトーチは足元を少し照らす程度(先を見るには灯り役の光が要る)
        selfGlow.intensity = (camouflaged ? 0.7f : 1.2f) * flicker;
        selfGlow.range = camouflaged ? 3.8f : 4.5f;
        selfGlow.color = Color.Lerp(selfGlow.color, camouflaged ? CamoTorchColor : TorchColor, Mathf.Min(1f, dt * 6f));
        Color fc = camouflaged ? new Color(0.7f, 0.9f, 1f, 0.95f) : new Color(1f, 0.82f, 0.45f, 0.95f);
        flameMat.color = Color.Lerp(flameMat.color, fc, Mathf.Min(1f, dt * 6f));
        haloMat.color = Color.Lerp(haloMat.color, new Color(fc.r, fc.g * 0.75f, fc.b * 0.55f, 0.28f), Mathf.Min(1f, dt * 6f));
        flame.localScale = Vector3.one * 0.22f * torchScale * flicker;
        halo.localScale = Vector3.one * 0.62f * torchScale * (2f - flicker);
    }

    void Awake()
    {
        BuildTorch();

        if (characterModelPrefab == null)
        {
            Debug.LogWarning("NightPlayer: characterModelPrefab が未設定です。");
            return;
        }
        var model = Instantiate(characterModelPrefab, transform);
        model.name = "ChameleonModel";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0, modelYawOffset, 0);
        model.transform.localScale = Vector3.one * modelScale;
        // Meshyの書き出しはメッシュを持つ子が無効(inactive)になっていることがあるため、すべて有効化する
        foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);

        animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        if (animatorController != null) animator.runtimeAnimatorController = animatorController;

        renderers = model.GetComponentsInChildren<Renderer>(true);
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = true;
            if (renderers[i] is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            var m = renderers[i].material;
            baseColors[i] = m.HasProperty("_Color") ? m.color : Color.white;
        }
        texAverage = new Color[renderers.Length];
        baseTextures = new Texture[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            baseTextures[i] = renderers[i].material.mainTexture;
            texAverage[i] = AverageColor(baseTextures[i]);
        }
    }

    // テクスチャ全体の平均色(縮小コピーして読む。読めないテクスチャでも使える)
    static Color AverageColor(Texture tex)
    {
        if (tex == null) return Color.white;
        var rt = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(tex, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var read = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
        read.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        Color sum = Color.black;
        foreach (var c in read.GetPixels()) sum += c;
        Object.Destroy(read);
        sum /= 64f;
        sum.a = 1f;
        return sum;
    }

    // 上下左右の入力。カメラは +Z 側から -Z を見ているので、画面上=-Z、画面右=-X。
    // 本番は圧力マット(PressureMatServer経由のESP32)、開発・動作確認中はWASDでも動かせるよう、
    // どちらの入力も同時にOR(合成)して受け付ける(どちらか一方しか効かない、という作りにはしない)。
    // 紹介動画の撮影(NightDemoVideo)で主人公を自動で動かすときだけ使う。null なら通常の入力
    public static Vector3? DebugInput;

    public static Vector3 ReadInput()
    {
        if (DebugInput.HasValue) return DebugInput.Value;
        var mat = PressureMatServer.Instance;
        Vector3 input = Vector3.zero;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) || (mat != null && mat.Front)) input.z -= 1;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) || (mat != null && mat.Back)) input.z += 1;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) || (mat != null && mat.Left)) input.x += 1;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) || (mat != null && mat.Right)) input.x -= 1;
        return input;
    }

    static readonly float[] WallSlideAngles = { 15f, -15f, 30f, -30f, 45f, -45f, 60f, -60f, 75f, -75f };

    public void Move(NightGame g, float dt, out float oldX, out float oldZ)
    {
        var map = g.World.Map;
        Vector3 input = ReadInput();
        bool hasInput = input.sqrMagnitude > 0f;
        if (hasInput)
        {
            input.Normalize();
            float k = Mathf.Min(1f, dt * 10f);
            Velocity.x += (input.x * MoveSpeed - Velocity.x) * k;
            Velocity.z += (input.z * MoveSpeed - Velocity.z) * k;
            float angle = Mathf.Atan2(input.x, input.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, angle, 0), k);
        }
        else
        {
            Velocity *= Mathf.Max(0f, 1f - Damping * dt);
        }

        Vector3 pos = transform.position;
        oldX = pos.x; oldZ = pos.z;
        pos += Velocity * dt;

        if (!map.Walkable(pos.x, pos.z))
        {
            // 斜めの道の壁にぶつかったら、進みたい向きに近い順に向きを少しずつ曲げて、歩ける向きへ滑らせる
            // (前後左右だけで入力しても、斜めの通路の壁に沿って進める)
            float step = Mathf.Sqrt(Velocity.x * Velocity.x + Velocity.z * Velocity.z) * dt;
            float baseAngle = Mathf.Atan2(Velocity.x, Velocity.z);
            bool slid = false;
            foreach (float deg in WallSlideAngles)
            {
                float a = baseAngle + deg * Mathf.Deg2Rad;
                float len = step * Mathf.Cos(deg * Mathf.Deg2Rad); // 曲げるほど遅く(壁に沿った分だけ進む)
                float nx = oldX + Mathf.Sin(a) * len, nz = oldZ + Mathf.Cos(a) * len;
                if (!map.Walkable(nx, nz)) continue;
                pos.x = nx; pos.z = nz; slid = true;
                break;
            }
            if (!slid) { pos.x = oldX; pos.z = oldZ; Velocity = Vector3.zero; }
        }
        ResolveTerrainCollision(g.World, ref pos, oldX, oldZ);
        map.ConstrainActor(ref pos);

        // 閉じた門の後ろには進めない。開いた後も門の両脇は通行不可。
        var gate = g.World.Gate;
        if (pos.z > 1.5f && (!gate.opened || gate.motion < 0.85f || Mathf.Abs(pos.x - NightMap.Exit.x) >= NightMap.ExitHalfWidth))
        {
            pos.z = 1.5f;
            Velocity.z = Mathf.Min(0f, Velocity.z);
        }
        pos.x = Mathf.Clamp(pos.x, -NightMap.HalfW + 0.5f, NightMap.HalfW - 0.5f);
        pos.z = Mathf.Min(pos.z, NightMap.StartZ + 5f);
        transform.position = pos;

        if (animator != null) animator.SetBool(IsMovingHash, hasInput);
    }

    // 木・岩との円形の当たり判定。めり込みを押し戻し、斜めにぶつかった時は表面を滑る。
    void ResolveTerrainCollision(NightWorld world, ref Vector3 pos, float oldX, float oldZ)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            bool collided = false;
            foreach (var p in world.Props)
            {
                if (!p.solid) continue;
                float dx = pos.x - p.position.x, dz = pos.z - p.position.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                float minDist = CollisionRadius + p.radius * 0.38f;
                if (dist >= minDist) continue;
                collided = true;
                float nx, nz;
                if (dist > 0.001f) { nx = dx / dist; nz = dz / dist; }
                else
                {
                    nx = oldX - p.position.x; nz = oldZ - p.position.z;
                    float len = Mathf.Sqrt(nx * nx + nz * nz);
                    if (len > 0.001f) { nx /= len; nz /= len; } else { nx = 1; nz = 0; }
                }
                pos.x = p.position.x + nx * minDist;
                pos.z = p.position.z + nz * minDist;
                float dot = Velocity.x * nx + Velocity.z * nz;
                if (dot < 0) { Velocity.x -= dot * nx; Velocity.z -= dot * nz; }
            }
            if (!collided) break;
        }
    }

    // 擬態中は周りの地形の色へ溶け込み、青白い光をまとう。被弾中は赤く。
    // 擬態中は、足元の地面や近くの物(terrainColor / terrainTexture)と同じ見た目になる。
    // ① まず「目標の色 ÷ 自分のテクスチャの平均色」を掛けて、全体の色を約0.3秒で目標の色に寄せる。
    // ② 色が揃ったら、自分の模様(緑の皮膚)を地面の模様に差し替えて、周りに溶け込ませる。
    // 擬態が解けたら、すぐに元の模様に戻して、色も元に戻していく。
    public void UpdateLook(float dt, bool camouflaged, Color terrainColor, Texture terrainTexture, bool hurt)
    {
        camoColor = camoBlend <= 0.001f ? terrainColor : Color.Lerp(camoColor, terrainColor, Mathf.Min(1f, dt * 5f));
        camoBlend = Mathf.MoveTowards(camoBlend, camouflaged ? 1f : 0f, dt * (camouflaged ? 3f : 5f)); // 約0.3秒で色が変わる
        UpdateTorch(dt, camouflaged);
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            var m = renderers[i].material;
            if (!m.HasProperty("_Color")) continue;
            Color avg = texAverage != null ? texAverage[i] : Color.white;
            Color camo = new Color(
                Mathf.Min(4f, camoColor.r / Mathf.Max(0.05f, avg.r)),
                Mathf.Min(4f, camoColor.g / Mathf.Max(0.05f, avg.g)),
                Mathf.Min(4f, camoColor.b / Mathf.Max(0.05f, avg.b)), 1f);
            bool swap = camouflaged && camoBlend >= 0.999f;
            if (swap) m.mainTexture = terrainTexture;
            else if (texSwapped) m.mainTexture = baseTextures[i];
            Color c = swap ? camoColor : Color.Lerp(baseColors[i], camo, camoBlend);
            if (hurt) c = Color.Lerp(c, new Color(0.95f, 0.25f, 0.2f), 0.55f);
            c.a = baseColors[i].a;
            m.color = c;
        }
        texSwapped = camouflaged && camoBlend >= 0.999f;
    }

    public void SetAnimating(bool moving)
    {
        if (animator != null) animator.SetBool(IsMovingHash, moving);
    }
}
