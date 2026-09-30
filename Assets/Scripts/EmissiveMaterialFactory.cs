using UnityEngine;

// 発光(_EMISSION)を使うマテリアルを作るための共通ヘルパー。
// 単純に new Material(Shader.Find("Standard")) してから EnableKeyword("_EMISSION") すると、
// エディタのPlayでは見えるのにスタンドアロンビルドでは発光が消えることがある
// (ビルド時のシェーダーバリアント除外が、静的に参照されているマテリアルしか見てくれないため)。
// Resources/Materials/EmissiveStandardTemplate.mat (常にビルドへ含まれる) を複製することで、
// このバリアントを確実にビルドへ残す。
public static class EmissiveMaterialFactory
{
    const string TemplatePath = "Materials/EmissiveStandardTemplate";
    static Material template;

    public static Material Create(Color baseColor)
    {
        if (template == null) template = Resources.Load<Material>(TemplatePath);

        Material mat = template != null ? new Material(template) : new Material(Shader.Find("Standard"));
        if (template == null)
        {
            Debug.LogWarning("EmissiveMaterialFactory: " + TemplatePath + " が見つかりません。" +
                "エディタで ChameleonNight > Create Emissive Material Template を実行してください。" +
                "ビルド版では発光が表示されない可能性があります。");
            mat.EnableKeyword("_EMISSION");
        }

        mat.color = baseColor;
        mat.SetColor("_EmissionColor", Color.black);
        return mat;
    }
}
