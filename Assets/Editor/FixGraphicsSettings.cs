using UnityEngine;
using UnityEditor;

// 実行時に "new Material(Shader.Find("Standard"))" ->  EnableKeyword("_EMISSION") のように
// スクリプトから動的にキーワードを有効化しているマテリアル(敵の発光エフェクトなど)は、
// Unityエディタ上のPlayでは動くのに、スタンドアロンビルドだけで無効化された状態で
// 出力されてしまうことがある(ビルド時のシェーダーバリアント除外が、静的に参照されている
// マテリアルしか見てくれないため)。
// この対策として、Standardシェーダーを「Always Included Shaders」に登録し、
// 全バリアントをビルドに含めるようにする。
public static class FixGraphicsSettings
{
    [MenuItem("ChameleonNight/Fix Graphics Settings (Always Included Shaders)")]
    public static void Run()
    {
        var settingsObj = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0];
        var so = new SerializedObject(settingsObj);
        var prop = so.FindProperty("m_AlwaysIncludedShaders");

        var standard = Shader.Find("Standard");
        if (standard == null)
        {
            Debug.LogError("FixGraphicsSettings: Standard シェーダーが見つかりませんでした。");
            return;
        }

        for (int i = 0; i < prop.arraySize; i++)
        {
            var element = prop.GetArrayElementAtIndex(i);
            if (element.objectReferenceValue == standard)
            {
                Debug.Log("FixGraphicsSettings: Standard は既に Always Included Shaders に登録済みです。");
                return;
            }
        }

        prop.InsertArrayElementAtIndex(prop.arraySize);
        prop.GetArrayElementAtIndex(prop.arraySize - 1).objectReferenceValue = standard;
        so.ApplyModifiedProperties();

        AssetDatabase.SaveAssets();
        Debug.Log("FixGraphicsSettings: Standard シェーダーを Always Included Shaders に追加しました。");
    }
}
