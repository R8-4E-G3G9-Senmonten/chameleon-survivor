using System.Linq;
using UnityEditor;
using UnityEngine;

// Meshy AIで作った敵キャラ3体(Assets/Models/Enemies/*)を、Unityでそのまま使える状態に整える。
//   Leafbound Wolf (四足)       … 群れ型     / Emberwood Colossus (二足) … 硬い型 / Mushroom Guardian … 遠距離型
// Generic リグ + 自分の骨格のアバター、アニメクリップはループ、マテリアルは Albedo+法線の Standard に差し替える。
public static class SetupEnemyModels
{
    public class Def
    {
        public string name;      // フォルダ名 = FBX名
        public string clipName;  // 再生する歩行クリップ名(部分一致)
    }

    public static readonly Def Wolf = new Def { name = "LeafboundWolf", clipName = "" };
    public static readonly Def Colossus = new Def { name = "EmberwoodColossus", clipName = "Walking" };
    public static readonly Def Mushroom = new Def { name = "MushroomGuardian", clipName = "Walking" };

    public static string FbxPath(Def d) => "Assets/Models/Enemies/" + d.name + "/" + d.name + ".fbx";

    [MenuItem("ChameleonNight/Setup Enemy Models")]
    public static void Run()
    {
        foreach (var d in new[] { Wolf, Colossus, Mushroom }) Setup(d);
    }

    public static GameObject LoadPrefab(Def d) => AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath(d));

    public static AnimationClip LoadClip(Def d)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(FbxPath(d)).OfType<AnimationClip>().Where(c => !c.name.Contains("__preview__")).ToArray();
        var match = clips.FirstOrDefault(c => d.clipName.Length > 0 && c.name.Contains(d.clipName));
        return match != null ? match : clips.FirstOrDefault();
    }

    static void Setup(Def d)
    {
        string dir = "Assets/Models/Enemies/" + d.name + "/";
        string fbx = FbxPath(d);
        string albedoPath = dir + d.name + "_texture_0.png";
        string normalPath = dir + d.name + "_normal.png";
        string matPath = dir + d.name + ".mat";

        AssetDatabase.ImportAsset(albedoPath, ImportAssetOptions.ForceUpdate);
        var texImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
        if (texImporter != null && texImporter.textureType != TextureImporterType.NormalMap)
        {
            texImporter.textureType = TextureImporterType.NormalMap;
            texImporter.SaveAndReimport();
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.shader = Shader.Find("Standard");
        mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath));
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
        mat.SetFloat("_Metallic", 0.05f);
        mat.SetFloat("_Glossiness", 0.3f);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        var importer = AssetImporter.GetAtPath(fbx) as ModelImporter;
        if (importer == null) { Debug.LogError("SetupEnemyModels: " + fbx + " が見つかりません。"); return; }
        importer.useFileScale = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;

        var clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        foreach (var c in clips) { c.loopTime = true; c.loopPose = true; }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();

        foreach (var embedded in AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Material>().ToArray())
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(embedded), mat);
        importer.SaveAndReimport();
        Debug.Log("SetupEnemyModels: " + d.name + " を設定しました。");
    }
}
