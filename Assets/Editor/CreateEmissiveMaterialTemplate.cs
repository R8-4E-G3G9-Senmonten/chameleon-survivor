using UnityEngine;
using UnityEditor;

// "Always Included Shaders" にStandardシェーダーを登録しても、_EMISSION のような
// shader_feature系のキーワードは「ビルドに含まれるどこかのマテリアルアセットが実際にそのキーワードを
// 有効にしているか」で個別に判定されるため、実行時に new Material() して EnableKeyword() するだけの
// マテリアルは、スタンドアロンビルドでは除外されてしまうことがある(敵の発光やパーティクルの発光が
// エディタのPlayでは見えるのにビルドだけ見えなくなる不具合の原因)。
//
// 対策として、_EMISSION を有効にした状態のマテリアルを Resources フォルダに実体として置いておく。
// Resources フォルダの中身はビルド時に無条件で含まれるため、このキーワードのシェーダーバリアントが
// 確実にビルドへ残る。実行時は EmissiveMaterialFactory がこのテンプレートを複製して使う。
public static class CreateEmissiveMaterialTemplate
{
    const string Dir = "Assets/Resources/Materials";
    const string Path = Dir + "/EmissiveStandardTemplate.mat";

    [MenuItem("ChameleonNight/Create Emissive Material Template")]
    public static void Run()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Dir))
            AssetDatabase.CreateFolder("Assets/Resources", "Materials");

        var mat = AssetDatabase.LoadAssetAtPath<Material>(Path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, Path);
        }
        else
        {
            mat.shader = Shader.Find("Standard");
        }

        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        mat.SetColor("_EmissionColor", Color.white * 0.5f); // 0だとキーワードが最適化で消える場合があるため非ゼロにしておく
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        Debug.Log("CreateEmissiveMaterialTemplate: " + Path + " を作成/更新しました。");
    }
}
