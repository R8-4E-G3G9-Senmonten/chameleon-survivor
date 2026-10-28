using UnityEngine;

// 敵の見た目に使う Meshy AI の3Dモデル(NightSceneBuilder が参照を設定する)。
// モデルが未設定の敵は、従来の丸い簡易形状で表示される。
[System.Serializable]
public class NightEnemyModel
{
    public GameObject prefab;
    public AnimationClip walk;
    public AnimationClip attack; // プレイヤーのすぐそばで繰り返す攻撃(ループ)。無ければ歩きのまま
    public AnimationClip shoot;  // 弾を撃つときの1回きりの動き(遠距離型)
    public float targetHeight = 1f; // 読み込んだモデルをこの高さ(ユニット)に自動で拡縮する
    public float yaw;               // モデルの正面が +Z とずれている場合の補正(度)
    public float animSpeed = 1f;
    public float hover;             // 地面から浮かせる高さ(ユニット)。お化けは少し浮かせる
    public float glowRange;         // 0より大きいと、体のまわりをうっすら照らす小さなライトを付ける(暗い森でも見えるように)
    public Color glowColor = Color.white;
}

public class NightEnemyModels : MonoBehaviour
{
    public static NightEnemyModels Instance { get; private set; }

    // 種類ごとに見た目を何体か用意しておき、敵を作るたびに順番に使う
    public NightEnemyModel[] moth;    // 群れ型
    public NightEnemyModel[] brute;   // 硬い型
    public NightEnemyModel[] shooter; // 遠距離型
    public NightEnemyModel[] sprite;  // すり抜け型

    readonly int[] next = new int[NightEnemy.KindCount];

    void Awake() { Instance = this; }

    public NightEnemyModel[] ListOf(NightEnemy.Kind kind)
    {
        switch (kind)
        {
            case NightEnemy.Kind.Brute: return brute;
            case NightEnemy.Kind.Shooter: return shooter;
            case NightEnemy.Kind.Sprite: return sprite;
            default: return moth;
        }
    }

    public NightEnemyModel For(NightEnemy.Kind kind)
    {
        var list = ListOf(kind);
        if (list == null || list.Length == 0) return null;
        int k = (int)kind;
        for (int tries = 0; tries < list.Length; tries++)
        {
            var m = list[next[k]++ % list.Length];
            if (m != null && m.prefab != null) return m;
        }
        return null;
    }
}
