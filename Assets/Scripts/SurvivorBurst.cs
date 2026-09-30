using UnityEngine;

// 撃破時などに飛び散る小さな粒のエフェクト。ParticleSystemを使わず、球を数個飛ばして縮めるだけの軽量版。
public class SurvivorBurst : MonoBehaviour
{
    const float Life = 0.55f;

    class Piece { public Transform t; public Vector3 vel; public float size; }

    Piece[] pieces;
    float age;

    public static void Spawn(Vector3 position, Color color, int count = 10)
    {
        var go = new GameObject("Burst");
        go.transform.position = position;
        var burst = go.AddComponent<SurvivorBurst>();
        burst.Init(color, count);
    }

    void Init(Color color, int count)
    {
        var mat = EmissiveMaterialFactory.Create(color);
        mat.SetColor("_EmissionColor", color * 1.5f);

        pieces = new Piece[count];
        for (int i = 0; i < count; i++)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = "Piece";
            s.transform.SetParent(transform, false);
            Destroy(s.GetComponent<Collider>());
            s.GetComponent<MeshRenderer>().sharedMaterial = mat;

            float size = Random.Range(0.08f, 0.18f);
            s.transform.localScale = Vector3.one * size;
            Vector3 dir = new Vector3(Random.Range(-1f, 1f), Random.Range(0.4f, 1.2f), Random.Range(-1f, 1f)).normalized;
            pieces[i] = new Piece { t = s.transform, vel = dir * Random.Range(1.5f, 3.5f), size = size };
        }
    }

    void Update()
    {
        age += Time.deltaTime;
        float k = Mathf.Clamp01(age / Life);
        foreach (var p in pieces)
        {
            p.vel += Vector3.down * 6f * Time.deltaTime;
            p.t.position += p.vel * Time.deltaTime;
            p.t.localScale = Vector3.one * p.size * (1f - k);
        }
        if (age >= Life) Destroy(gameObject);
    }
}
