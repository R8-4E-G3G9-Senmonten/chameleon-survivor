using UnityEngine;

// Unity にはコーンのプリミティブが無いため、SpriteFactory と同じ方針
// (インポート資産を使わずランタイムで生成する)でメッシュを作る。
public static class PrimitiveMeshFactory
{
    public static Mesh CreateCone(float radius, float height, int segments = 10)
    {
        var mesh = new Mesh { name = "Cone" };
        var vertices = new Vector3[segments + 2];
        vertices[0] = new Vector3(0, height, 0); // apex
        vertices[1] = Vector3.zero;               // base center
        for (int i = 0; i < segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            vertices[2 + i] = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
        }

        var triangles = new int[segments * 6];
        int t = 0;
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            // 側面
            triangles[t++] = 0;
            triangles[t++] = 2 + next;
            triangles[t++] = 2 + i;
            // 底面
            triangles[t++] = 1;
            triangles[t++] = 2 + i;
            triangles[t++] = 2 + next;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
