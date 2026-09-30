using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Text;

// 歩行中にキャラクターが浮いて見える原因(腰ボーンの上下動カーブが、静止姿勢の高さより
// 常に高い位置にある、など)を数値で確認するための診断。
public static class DiagnoseWalkHeight
{
    const string BaseFbxPath = "Assets/Models/ChameleonMeshy/Chameleon.fbx";
    const string WalkClipAssetPath = "Assets/Models/ChameleonMeshy/WalkClip.anim";

    [MenuItem("ChameleonNight/Diagnose Walk Height")]
    public static void Run()
    {
        var sb = new StringBuilder();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BaseFbxPath);
        var smr = prefab != null ? prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        Transform rootBone = smr != null ? smr.rootBone : null;

        if (rootBone == null)
        {
            Debug.LogError("DiagnoseWalkHeight: rootBoneが取得できませんでした。");
            return;
        }
        sb.AppendLine("rootBone名=" + rootBone.name);
        sb.AppendLine("rootBone 静止時 localPosition=" + rootBone.localPosition);

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkClipAssetPath);
        if (clip == null)
        {
            Debug.LogError("DiagnoseWalkHeight: " + WalkClipAssetPath + " が見つかりません。");
            return;
        }

        var bindings = AnimationUtility.GetCurveBindings(clip);
        sb.AppendLine("WalkClip の全バインディング数=" + bindings.Length);

        // rootBoneに関するpositionカーブを探す(パスの末尾がrootBone名のもの)
        foreach (var b in bindings)
        {
            if (b.propertyName.StartsWith("m_LocalPosition") &&
                (b.path == rootBone.name || b.path.EndsWith("/" + rootBone.name)))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                float min = float.MaxValue, max = float.MinValue;
                foreach (var k in curve.keys) { min = Mathf.Min(min, k.value); max = Mathf.Max(max, k.value); }
                sb.AppendLine(string.Format("  binding path='{0}' prop='{1}': min={2:F4} max={3:F4} keys={4}",
                    b.path, b.propertyName, min, max, curve.keys.Length));
            }
        }

        // ルート直下(パス空文字列)のカーブも念のため確認(Armature自体が動いている可能性)
        foreach (var b in bindings)
        {
            if (b.path == "" && b.propertyName.StartsWith("m_LocalPosition"))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                float min = float.MaxValue, max = float.MinValue;
                foreach (var k in curve.keys) { min = Mathf.Min(min, k.value); max = Mathf.Max(max, k.value); }
                sb.AppendLine(string.Format("  [ルート直下] prop='{0}': min={1:F4} max={2:F4}", b.propertyName, min, max));
            }
        }

        Debug.Log(sb.ToString());
    }
}
