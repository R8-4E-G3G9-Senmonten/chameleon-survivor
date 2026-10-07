using UnityEngine;

// マップ(コース)ごとの難易度。3つのマップで遊びごたえを変える。
//   A 森の迷路      … やさしい(初めての人向け)
//   B 遺跡攻略      … ふつう
//   C 中央湖の周回  … むずかしい
// ランキングはマップ別なので、難易度が違ってもスコアを比べるのは同じマップの中だけ。
public struct NightDifficulty
{
    public string label;      // 画面に出す名前
    public int stars;         // ★の数(色だけに頼らず難しさを伝える)
    public Color color;
    public float spawnInterval; // 敵の出現間隔の倍率(大きいほど出にくい)
    public float enemyHp;       // 敵の光HPの倍率(大きいほど倒しにくい)
    public float enemySpeed;    // 敵の速さの倍率
    public float damage;        // プレイヤーが受けるダメージの倍率
    public int maxEnemies;      // 同時に出る敵の上限

    public static NightDifficulty For(string mapId)
    {
        switch (mapId)
        {
            case "A":
                return new NightDifficulty { label = "やさしい", stars = 1, color = NightMeshes.Hex(0x6fe08a), spawnInterval = 1.35f, enemyHp = 0.85f, enemySpeed = 0.9f, damage = 0.75f, maxEnemies = 30 };
            case "C":
                return new NightDifficulty { label = "むずかしい", stars = 3, color = NightMeshes.Hex(0xff7a6a), spawnInterval = 0.8f, enemyHp = 1.2f, enemySpeed = 1.1f, damage = 1.25f, maxEnemies = 55 };
            default:
                return new NightDifficulty { label = "ふつう", stars = 2, color = NightMeshes.Hex(0xffd76a), spawnInterval = 1f, enemyHp = 1f, enemySpeed = 1f, damage = 1f, maxEnemies = 44 };
        }
    }

    public string Stars => new string('★', stars) + new string('☆', 3 - stars);
}
