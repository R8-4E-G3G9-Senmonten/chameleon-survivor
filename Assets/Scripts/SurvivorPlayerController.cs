using UnityEngine;

// メインプレイヤー: 攻撃手段を持たず、逃げ回るだけの役。
// HTMLモックアップの velocity+damping 移動と、進行方向へ向く回転をそのまま再現する。
// 見た目は Meshy AI で作った3Dアセット(Assets/Models/ChameleonMeshy)を使う。
// 入力がある(=移動している)間だけ Animator の IsMoving を true にして歩行アニメーションを再生し、
// 止まっている間はアニメーションを適用しない(最初のポーズで静止する)。
public class SurvivorPlayerController : MonoBehaviour
{
    public float moveSpeed = 5.6f;
    public float damping = 8f;
    public float worldHalfExtent = 19f;

    [Header("見た目(3Dモデル)")]
    public GameObject characterModelPrefab;   // Assets/Models/ChameleonMeshy/Chameleon.fbx
    public RuntimeAnimatorController animatorController; // Assets/Models/ChameleonMeshy/ChameleonAnimator.controller
    public float modelScale = 1f;
    public float modelYawOffset = 0f; // モデルの正面とUnity上のZ+が合っていない場合の補正(度)

    Vector3 velocity;
    Animator animator;
    Renderer[] modelRenderers;
    Color[] rendererBaseColors;
    float hurtBlend;

    static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

    void Awake()
    {
        BuildVisuals();
    }

    void BuildVisuals()
    {
        if (characterModelPrefab == null)
        {
            Debug.LogWarning("SurvivorPlayerController: characterModelPrefab が未設定です。" +
                "SurvivorSceneBuilder から Assets/Models/ChameleonMeshy/Chameleon.fbx を割り当ててください。");
            return;
        }

        var model = Instantiate(characterModelPrefab, transform);
        model.name = "ChameleonModel";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0, modelYawOffset, 0);
        model.transform.localScale = Vector3.one * modelScale;

        // Meshyの書き出しは、メッシュを持つ子オブジェクトが無効(inactive)な状態になっていることがあるため、
        // 階層をすべて強制的に有効化する。
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
            t.gameObject.SetActive(true);

        animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        if (animatorController != null) animator.runtimeAnimatorController = animatorController;

        modelRenderers = model.GetComponentsInChildren<Renderer>(true);
        foreach (var r in modelRenderers)
        {
            r.enabled = true;
            // バウンディングボックスの事前計算がおかしいモデルでも、実際のボーン位置から毎フレーム
            // 範囲を再計算させることで、カリングによって見えなくなるのを防ぐ。
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }
        rendererBaseColors = new Color[modelRenderers.Length];
        for (int i = 0; i < modelRenderers.Length; i++)
        {
            // マテリアルをインスタンス化(共有アセットを直接汚さないようにする)し、被ダメージ演出用に元の色を控えておく
            var mat = modelRenderers[i].material;
            rendererBaseColors[i] = mat.HasProperty("_Color") ? mat.color : Color.white;
        }
    }

    void Update()
    {
        if (SurvivorManager.Instance != null && SurvivorManager.Instance.State != SurvivorManager.GameState.Playing) return;

        // 本番は圧力マット(PressureMatServer経由のESP32)、開発・動作確認中はWASDでも動かせるよう、
        // どちらの入力も同時にOR(合成)して受け付ける。
        var mat = PressureMatServer.Instance;
        Vector3 input = Vector3.zero;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) || (mat != null && mat.Front)) input.z -= 1;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) || (mat != null && mat.Back)) input.z += 1;
        // Unityは左手座標系で、カメラは+Z側から-Z方向を見ているため、画面右は-X方向になる。
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) || (mat != null && mat.Left)) input.x += 1;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) || (mat != null && mat.Right)) input.x -= 1;

        float dt = Time.deltaTime;
        bool isMoving = input.sqrMagnitude > 0f;
        if (isMoving)
        {
            input.Normalize();
            velocity.x += (input.x * moveSpeed - velocity.x) * Mathf.Min(1f, dt * 10f);
            velocity.z += (input.z * moveSpeed - velocity.z) * Mathf.Min(1f, dt * 10f);
            float angle = Mathf.Atan2(input.x, input.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, angle, 0), Mathf.Min(1f, dt * 10f));
        }
        else
        {
            velocity *= Mathf.Max(0f, 1f - damping * dt);
        }

        Vector3 pos = transform.position + velocity * dt;
        pos = SurvivorObstacle.Resolve(pos, 0.45f);
        pos.x = Mathf.Clamp(pos.x, -worldHalfExtent, worldHalfExtent);
        pos.z = Mathf.Clamp(pos.z, -worldHalfExtent, worldHalfExtent);
        transform.position = pos;

        if (animator != null) animator.SetBool(IsMovingHash, isMoving);

        UpdateHurtTint(dt);
    }

    void UpdateHurtTint(float dt)
    {
        if (modelRenderers == null) return;

        bool hurt = SurvivorManager.Instance != null && SurvivorManager.Instance.IsHurt;
        hurtBlend = Mathf.MoveTowards(hurtBlend, hurt ? 1f : 0f, dt * (hurt ? 12f : 4f));

        for (int i = 0; i < modelRenderers.Length; i++)
        {
            var mat = modelRenderers[i].material;
            if (!mat.HasProperty("_Color")) continue;
            mat.color = Color.Lerp(rendererBaseColors[i], new Color(0.95f, 0.25f, 0.2f), hurtBlend * 0.6f);
        }
    }
}
