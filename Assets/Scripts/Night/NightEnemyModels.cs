using UnityEngine;

// 敵の見た目に使う Meshy AI の3Dモデル(NightSceneBuilder が参照を設定する)。
// モデルが未設定の敵は、従来の丸い簡易形状で表示される。
[System.Serializable]
public class NightEnemyModel
{
    public GameObject prefab;
    public AnimationClip walk;
    public float targetHeight = 1f; // 読み込んだモデルをこの高さ(ユニット)に自動で拡縮する
    public float yaw;               // モデルの正面が +Z とずれている場合の補正(度)
    public float animSpeed = 1f;
}

public class NightEnemyModels : MonoBehaviour
{
    public static NightEnemyModels Instance { get; private set; }

    public NightEnemyModel moth;    // 群れ型
    public NightEnemyModel brute;   // 硬い型
    public NightEnemyModel shooter; // 遠距離型

    void Awake() { Instance = this; }

    public NightEnemyModel For(NightEnemy.Kind kind)
    {
        NightEnemyModel m = kind == NightEnemy.Kind.Brute ? brute : kind == NightEnemy.Kind.Shooter ? shooter : moth;
        return m != null && m.prefab != null ? m : null;
    }
}
