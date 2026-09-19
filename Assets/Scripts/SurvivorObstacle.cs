using System.Collections.Generic;
using UnityEngine;

// 木・岩・切り株など、プレイヤーと敵が通り抜けられない障害物(XZ平面上の円として扱う)。
// 物理エンジンを使わず、移動後に円の外へ押し出すだけの軽量な衝突判定にしている。
public class SurvivorObstacle : MonoBehaviour
{
    public float radius = 0.5f;

    static readonly List<SurvivorObstacle> all = new List<SurvivorObstacle>();

    // Sceneビュー(Gizmos ON)で当たり判定の円を確認できる
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.1f, radius);
    }

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    // pos を、全障害物の円(+ mover の半径)の外側へ押し出した位置を返す。
    public static Vector3 Resolve(Vector3 pos, float moverRadius)
    {
        // 押し出しで別の障害物に食い込むことがあるので数回繰り返す
        for (int iter = 0; iter < 3; iter++)
        {
            bool moved = false;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                Vector3 c = o.transform.position;
                float dx = pos.x - c.x;
                float dz = pos.z - c.z;
                float minDist = o.radius + moverRadius;
                float sq = dx * dx + dz * dz;
                if (sq >= minDist * minDist) continue;

                float dist = Mathf.Sqrt(sq);
                if (dist < 0.0001f) { dx = 1f; dz = 0f; dist = 1f; }
                float push = minDist - dist;
                pos.x += dx / dist * push;
                pos.z += dz / dist * push;
                moved = true;
            }
            if (!moved) break;
        }
        return pos;
    }
}
