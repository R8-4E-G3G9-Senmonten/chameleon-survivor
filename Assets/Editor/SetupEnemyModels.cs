using System.Linq;
using UnityEditor;
using UnityEngine;

// Meshy AIで作った「動物×お化け」の敵キャラ7体(Assets/Models/Enemies/*)を、Unityでそのまま使える状態に整える。
// ゲームで使うのは4体(群れ型=狼 / 遠距離型=狐 / 硬い型=熊 / すり抜け型=妖精)。猪・狼男・鎧は予備として残してある。
// 骨とアニメは Blender で自動で付けてから FBX にしてある。Walk は全員、Attack は狼・熊・妖精、Shoot は狐だけにある。
// Generic リグ + 自分の骨格のアバター、アニメクリップはループ(Shoot だけは1回きり)、マテリアルは色テクスチャの Standard に差し替える。
public static class SetupEnemyModels
{
    public class Def
    {
        public string name;      // フォルダ名 = FBX名
        public string clipName;  // 再生するクリップ名(部分一致)
    }

    static Def D(string name) => new Def { name = name, clipName = "Walk" };

    public static readonly Def Frostwolf = D("MoonlitFrostwolf");
    public static readonly Def Tuskboar = D("PurpleTuskboar");
    public static readonly Def Specter = D("MoonfangSpecter");
    public static readonly Def Bear = D("CrimsonSpiritBear");
    public static readonly Def Knight = D("SpectralKnight");
    public static readonly Def Emberfox = D("Emberfox");
    public static readonly Def Sprite = D("MintySprite");

    public static readonly Def[] All = { Frostwolf, Tuskboar, Specter, Bear, Knight, Emberfox, Sprite };

    public static string FbxPath(Def d) => "Assets/Models/Enemies/" + d.name + "/" + d.name + ".fbx";

    [MenuItem("ChameleonNight/Setup Enemy Models")]
    public static void Run()
    {
        foreach (var d in All) Setup(d);
    }

    public static GameObject LoadPrefab(Def d) => AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath(d));

    public static AnimationClip LoadClip(Def d)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(FbxPath(d)).OfType<AnimationClip>().Where(c => !c.name.Contains("__preview__")).ToArray();
        var match = clips.FirstOrDefault(c => d.clipName.Length > 0 && c.name.Contains(d.clipName));
        return match != null ? match : clips.FirstOrDefault();
    }

    // Walk 以外の動き("Attack" / "Shoot")。そのモデルに無ければ null
    public static AnimationClip LoadClip(Def d, string clipName) =>
        AssetDatabase.LoadAllAssetsAtPath(FbxPath(d)).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.Contains("__preview__") && c.name.Contains(clipName));

    static void Setup(Def d)
    {
        string dir = "Assets/Models/Enemies/" + d.name + "/";
        string fbx = FbxPath(d);
        string albedoPath = dir + d.name + "_texture_0.png";
        string matPath = dir + d.name + ".mat";

        AssetDatabase.ImportAsset(albedoPath, ImportAssetOptions.ForceUpdate);

        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.shader = Shader.Find("Standard");
        mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath));
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

        // FBX を書き出し直すとクリップ名が変わることがあるので、保存済みの設定ではなく毎回 FBX の中身から作る
        var clips = importer.defaultClipAnimations;
        foreach (var c in clips) { c.loopTime = c.loopPose = !c.name.Contains("Shoot"); }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();

        foreach (var embedded in AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Material>().ToArray())
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(embedded), mat);
        importer.SaveAndReimport();
        Debug.Log("SetupEnemyModels: " + d.name + " を設定しました。");
    }
}
