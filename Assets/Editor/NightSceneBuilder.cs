using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// カメレオンナイト ― 光の球収集版(HTMLモックアップ)の Unity シーンを組み立てる。
// 地形はマップ選択に合わせて実行時に生成するので、ここではゲームの部品とアセット参照だけを置く。
public static class NightSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/ChameleonNight.unity";
    const string KenneyDir = "Assets/Models/KenneyNature/FBX/";
    const string UnlitTemplatePath = "Assets/Resources/Materials/UnlitTransparentTemplate.mat";

    static readonly string[] TreeModels =
    {
        "tree_pineTallA", "tree_pineTallB", "tree_pineTallC", "tree_pineTallD",
        "tree_pineRoundA", "tree_pineRoundC", "tree_oak_dark", "tree_detailed_dark", "tree_default_dark", "tree_fat_darkh",
    };
    static readonly string[] RockModels = { "rock_largeA", "rock_largeB", "rock_largeC", "rock_largeD", "stone_largeA", "stone_largeB" };

    [MenuItem("ChameleonNight/Build Night Scene (光の球収集版)")]
    public static void Build()
    {
        FixGraphicsSettings.Run();
        SetupEnemyModels.Run();
        CreateEmissiveMaterialTemplate.Run();
        CreateUnlitTemplate();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.008f, 0.016f, 0.039f);
        RenderSettings.fogDensity = 0.026f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = NightMeshes.Hex(0x223344) * 0.035f;
        RenderSettings.skybox = null;

        var moonGO = new GameObject("Moonlight");
        var moon = moonGO.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = NightMeshes.Hex(0x4a5f8a);
        moon.intensity = 0.05f;
        moon.shadows = LightShadows.None;
        moonGO.transform.position = new Vector3(10, 20, -6);
        moonGO.transform.LookAt(Vector3.zero);

        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        camGO.AddComponent<AudioListener>();
        cam.fieldOfView = 55f;
        // カメラは常にフォーカス地点から約17.7ユニット離れた位置にあるため(CameraOffset参照)、
        // ニアクリップを0.1のままにすると深度バッファの精度がほぼ使われない範囲に浪費され、
        // 地面や通路の近い高さのメッシュ同士でちらつき(Zファイティング)が起きていた。
        // ニアを現実的な値まで上げるだけで、同じ深度バッファの精度が意味のある範囲に集中する。
        cam.nearClipPlane = 3f;
        cam.farClipPlane = 160f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = NightMeshes.Hex(0x02040a);
        camGO.transform.position = new Vector3(0, 13, 12);

        var worldGO = new GameObject("NightWorld");
        var world = worldGO.AddComponent<NightWorld>();
        world.treePrefabs = LoadModels(TreeModels);
        world.rockPrefabs = LoadModels(RockModels);

        var playerGO = new GameObject("Player");
        var player = playerGO.AddComponent<NightPlayer>();
        player.characterModelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/ChameleonMeshy/Chameleon.fbx");
        player.animatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Models/ChameleonMeshy/ChameleonAnimator.controller");
        if (player.characterModelPrefab == null)
            Debug.LogWarning("NightSceneBuilder: Chameleon.fbx が見つかりません。先に ChameleonNight > Setup Chameleon Model を実行してください。");

        var flashGO = new GameObject("Flashlight");
        var flashlight = flashGO.AddComponent<NightFlashlight>();

        new GameObject("PointerServer").AddComponent<PointerServer>();
        new GameObject("PressureMatServer").AddComponent<PressureMatServer>();

        var gameGO = new GameObject("NightGame");
        var game = gameGO.AddComponent<NightGame>();
        game.World = world;
        game.Player = player;
        game.Flashlight = flashlight;
        game.Cam = cam;
        game.Moon = moon;
        gameGO.AddComponent<NightHud>().game = game;
        gameGO.AddComponent<NightAutoShot>();

        // 敵の見た目: 群れ型=狼 / 硬い型=巨人 / 遠距離型=キノコの守り手(いずれもMeshy AI製)
        var models = gameGO.AddComponent<NightEnemyModels>();
        models.moth = MakeEnemyModel(SetupEnemyModels.Wolf, 1.0f);
        models.brute = MakeEnemyModel(SetupEnemyModels.Colossus, 2.5f);
        models.shooter = MakeEnemyModel(SetupEnemyModels.Mushroom, 1.5f);

        if (!Directory.Exists("Assets/Scenes")) Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("NightSceneBuilder: " + ScenePath + " を作成しました。");
    }

    static NightEnemyModel MakeEnemyModel(SetupEnemyModels.Def def, float height)
    {
        var prefab = SetupEnemyModels.LoadPrefab(def);
        if (prefab == null) Debug.LogWarning("NightSceneBuilder: 敵モデル " + def.name + " が見つかりません。簡易形状で代用します。");
        return new NightEnemyModel { prefab = prefab, walk = prefab != null ? SetupEnemyModels.LoadClip(def) : null, targetHeight = height };
    }

    static GameObject[] LoadModels(string[] names)
    {
        var list = new List<GameObject>();
        foreach (var n in names)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(KenneyDir + n + ".fbx");
            if (go != null) list.Add(go);
        }
        if (list.Count == 0) Debug.LogWarning("NightSceneBuilder: Kenney Nature Kit のモデルが見つからないため、木・岩は簡易形状で代用します。");
        return list.ToArray();
    }

    // 半透明・ライティングなし(リング、光の道、光の球の外殻など)用のテンプレート。
    // Resources に置いたマテリアルが参照するシェーダーはビルドへ必ず含まれるので、実行時に複製しても描画が消えない。
    static void CreateUnlitTemplate()
    {
        var shader = Shader.Find("Sprites/Default");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(UnlitTemplatePath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, UnlitTemplatePath);
        }
        else mat.shader = shader;
        mat.color = new Color(1, 1, 1, 0.5f);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
    }
}
