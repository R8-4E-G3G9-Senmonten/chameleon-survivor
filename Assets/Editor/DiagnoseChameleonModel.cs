using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Text;

// Chameleon.fbx が正しく読み込めているか(メッシュ・レンダラー・マテリアル・アバターの状態)を
// Unityエディタを開かずに調べるための診断スクリプト。
public static class DiagnoseChameleonModel
{
    const string BaseFbxPath = "Assets/Models/ChameleonMeshy/Chameleon.fbx";
    const string WalkFbxPath = "Assets/Models/ChameleonMeshy/Chameleon_Walk.fbx";

    [MenuItem("ChameleonNight/Diagnose Chameleon Model")]
    public static void Run()
    {
        Diagnose(BaseFbxPath);
        Diagnose(WalkFbxPath);
    }

    static void Diagnose(string BaseFbxPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== DiagnoseChameleonModel: " + BaseFbxPath + " ===");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BaseFbxPath);
        if (prefab == null)
        {
            sb.AppendLine("NG: " + BaseFbxPath + " を GameObject として読み込めませんでした(パス違い、またはインポート失敗)。");
            Debug.LogError(sb.ToString());
            return;
        }
        sb.AppendLine("OK: prefab root = " + prefab.name);
        sb.AppendLine("root localScale=" + prefab.transform.localScale + " localPosition=" + prefab.transform.localPosition);

        var renderers = prefab.GetComponentsInChildren<Renderer>(true);
        sb.AppendLine("Renderer数: " + renderers.Length);
        foreach (var r in renderers)
        {
            string kind = r is SkinnedMeshRenderer ? "SkinnedMeshRenderer" : r.GetType().Name;
            Mesh mesh = null;
            if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            else if (r is MeshRenderer mr) mesh = r.GetComponent<MeshFilter>()?.sharedMesh;

            int vcount = mesh != null ? mesh.vertexCount : -1;
            Bounds b = mesh != null ? mesh.bounds : default;
            int matCount = r.sharedMaterials?.Length ?? 0;

            sb.AppendLine(string.Format(
                "  - {0} ({1}): active={2}, mesh頂点数={3}, mesh.bounds center={4} size={5}, lossyScale={6}",
                r.name, kind, r.gameObject.activeInHierarchy, vcount, b.center, b.size, r.transform.lossyScale));

            if (mesh != null && mesh.vertexCount > 0)
            {
                var verts = mesh.vertices;
                Vector3 min = verts[0], max = verts[0];
                for (int i = 1; i < verts.Length; i++)
                {
                    min = Vector3.Min(min, verts[i]);
                    max = Vector3.Max(max, verts[i]);
                }
                Vector3 diff = max - min;
                sb.AppendLine(string.Format("      実測 頂点座標範囲(高精度): min=({0:E4},{1:E4},{2:E4}) max=({3:E4},{4:E4},{5:E4}) 差=({6:E4},{7:E4},{8:E4})",
                    min.x, min.y, min.z, max.x, max.y, max.z, diff.x, diff.y, diff.z));

                sb.AppendLine("      blendShapeCount=" + mesh.blendShapeCount);
                if (mesh.blendShapeCount > 0)
                {
                    var dv = new Vector3[mesh.vertexCount];
                    var dn = new Vector3[mesh.vertexCount];
                    var dt = new Vector3[mesh.vertexCount];
                    mesh.GetBlendShapeFrameVertices(0, 0, dv, dn, dt);
                    float maxDelta = 0f;
                    foreach (var d in dv) maxDelta = Mathf.Max(maxDelta, d.magnitude);
                    sb.AppendLine(string.Format("      blendShape[0] '{0}' の最大デルタ量={1:E4}", mesh.GetBlendShapeName(0), maxDelta));
                }
            }

            if (r is SkinnedMeshRenderer smr2)
            {
                sb.AppendLine("      rootBone=" + (smr2.rootBone != null ? smr2.rootBone.name : "null") +
                    " bones数=" + (smr2.bones != null ? smr2.bones.Length : 0));
                if (smr2.bones != null)
                {
                    foreach (var bone in smr2.bones.Take(5))
                    {
                        if (bone == null) { sb.AppendLine("        bone=null"); continue; }
                        sb.AppendLine(string.Format("        bone '{0}': localPos={1} localScale={2} lossyScale={3}",
                            bone.name, bone.localPosition, bone.localScale, bone.lossyScale));
                    }
                }
            }
        }

        var animators = prefab.GetComponentsInChildren<Animator>(true);
        sb.AppendLine("Animator数: " + animators.Length);
        foreach (var a in animators)
        {
            sb.AppendLine(string.Format("  - avatar={0}, isValid={1}, isHuman={2}",
                a.avatar != null ? a.avatar.name : "null",
                a.avatar != null && a.avatar.isValid,
                a.avatar != null && a.avatar.isHuman));
        }

        var importer = AssetImporter.GetAtPath(BaseFbxPath) as ModelImporter;
        if (importer != null)
        {
            sb.AppendLine(string.Format("ModelImporter: globalScale={0}, useFileScale={1}, animationType={2}, importBlendShapes={3}",
                importer.globalScale, importer.useFileScale, importer.animationType, importer.importBlendShapes));
        }

        Debug.Log(sb.ToString());
    }
}
