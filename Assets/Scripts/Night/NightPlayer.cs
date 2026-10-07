using UnityEngine;

// メインプレイヤー(カメレオン)。見た目は Meshy AI で作った3Dアセット。
// 移動は上下左右のみで、通路の上だけを歩ける(モックアップの walkable / constrainActor と同じ判定)。
// 入力がある間だけ歩行アニメーションを再生する。
public class NightPlayer : MonoBehaviour
{
    public GameObject characterModelPrefab;              // Assets/Models/ChameleonMeshy/Chameleon.fbx
    public RuntimeAnimatorController animatorController; // Assets/Models/ChameleonMeshy/ChameleonAnimator.controller
    public float modelScale = 1f;
    public float modelYawOffset = 0f;

    public const float MoveSpeed = 4.8f, Damping = 8f, CollisionRadius = 0.55f;

    public Vector3 Velocity;
    Animator animator;
    Renderer[] renderers;
    Color[] baseColors;
    Color tint = Color.white;
    Light selfGlow;

    static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

    // ---- トーチの灯火 ----
    // 暗い森の中でも自分の位置を見失わないよう、カメレオンの背中に小さな炎を灯し、足元を暖色で照らす。
    // 擬態中は炎を小さく青白くして、「隠れている」状態が一目で分かるようにする(位置は分かる明るさは残す)。
    static readonly Color TorchColor = NightMeshes.Hex(0xffb15c), CamoTorchColor = new Color(0.45f, 0.75f, 1f);
    const float TorchHeight = 1.25f;
    Transform flame, halo;
    Material flameMat, haloMat;
    float torchScale = 1f;

    void BuildTorch()
    {
        selfGlow = NightWorld.PointLight(transform, new Vector3(0, TorchHeight + 0.2f, 0), TorchColor, 1.9f, 7f);
        flameMat = NightMeshes.Unlit(new Color(1f, 0.82f, 0.45f, 0.95f));
        haloMat = NightMeshes.Unlit(new Color(1f, 0.6f, 0.25f, 0.28f));
        flame = NightMeshes.Primitive(PrimitiveType.Sphere, transform, flameMat).transform;
        flame.name = "TorchFlame";
        flame.localPosition = new Vector3(0, TorchHeight, 0);
        halo = NightMeshes.Primitive(PrimitiveType.Sphere, transform, haloMat).transform;
        halo.name = "TorchHalo";
        halo.localPosition = new Vector3(0, TorchHeight, 0);
    }

    void UpdateTorch(float dt, bool camouflaged)
    {
        if (selfGlow == null) return;
        float t = Time.time;
        // 炎のゆらぎ(2つの周期を重ねて、規則的に見えないようにする)
        float flicker = 1f + 0.08f * Mathf.Sin(t * 13f) + 0.05f * Mathf.Sin(t * 29f + 1.3f);
        torchScale = Mathf.Lerp(torchScale, camouflaged ? 0.6f : 1f, Mathf.Min(1f, dt * 6f));
        selfGlow.intensity = (camouflaged ? 0.9f : 1.9f) * flicker;
        selfGlow.range = camouflaged ? 5f : 7f;
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
    }

    // 上下左右の入力。カメラは +Z 側から -Z を見ているので、画面上=-Z、画面右=-X。
    // 本番は圧力マット(PressureMatServer経由のESP32)、開発・動作確認中はWASDでも動かせるよう、
    // どちらの入力も同時にOR(合成)して受け付ける(どちらか一方しか効かない、という作りにはしない)。
    public static Vector3 ReadInput()
    {
        var mat = PressureMatServer.Instance;
        Vector3 input = Vector3.zero;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) || (mat != null && mat.Front)) input.z -= 1;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) || (mat != null && mat.Back)) input.z += 1;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) || (mat != null && mat.Left)) input.x += 1;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) || (mat != null && mat.Right)) input.x -= 1;
        return input;
    }

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
            if (map.Walkable(pos.x, oldZ)) pos.z = oldZ;
            else if (map.Walkable(oldX, pos.z)) pos.x = oldX;
            else { pos.x = oldX; pos.z = oldZ; Velocity = Vector3.zero; }
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
    public void UpdateLook(float dt, bool camouflaged, Color terrainColor, bool hurt)
    {
        // テクスチャに色を掛け合わせるので、地形色そのままだと真っ黒になる。明るさを持ち上げて色味だけ寄せる。
        Color camo = terrainColor * 2.6f; camo.a = 1f;
        Color target = camouflaged ? Color.Lerp(camo, new Color(0.45f, 0.75f, 0.95f), 0.25f) : Color.white;
        tint = Color.Lerp(tint, target, Mathf.Min(1f, dt * (camouflaged ? 4f : 6f)));
        UpdateTorch(dt, camouflaged);
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            var m = renderers[i].material;
            if (!m.HasProperty("_Color")) continue;
            Color c = baseColors[i] * tint;
            if (hurt) c = Color.Lerp(c, new Color(0.95f, 0.25f, 0.2f), 0.55f);
            c.a = baseColors[i].a;
            m.color = c;
        }
    }

    public void SetAnimating(bool moving)
    {
        if (animator != null) animator.SetBool(IsMovingHash, moving);
    }
}
