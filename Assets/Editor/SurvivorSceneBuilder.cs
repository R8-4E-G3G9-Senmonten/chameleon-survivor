using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

// mockup/chameleon-survivor.html を Unity(3D)で再現するシーンビルダー。
public static class SurvivorSceneBuilder
{
    const float WorldHalfExtent = 19f;

    [MenuItem("ChameleonNight/Build Survivor Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // --- 夜の照明・フォグ ---
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.008f, 0.016f, 0.039f);
        RenderSettings.fogDensity = 0.042f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.2f, 0.28f, 0.4f) * 0.14f; // 灯りが無い状態はかなり暗くする

        var moonGO = new GameObject("Moonlight");
        var moon = moonGO.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = new Color(0.29f, 0.373f, 0.541f);
        moon.intensity = 0.16f;
        moon.shadows = LightShadows.Soft;
        moonGO.transform.rotation = Quaternion.Euler(45f, -60f, 0f);

        // --- カメラ ---
        var camGO = new GameObject("Main Camera");
        var cam = camGO.AddComponent<Camera>();
        camGO.tag = "MainCamera";
        camGO.AddComponent<AudioListener>();
        cam.orthographic = false;
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 200f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.008f, 0.016f, 0.039f);
        camGO.transform.position = new Vector3(0, 8, 8);

        // --- 地面 ---
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(9f, 1f, 9f); // Unity標準Planeは10x10 -> 90x90相当
        var groundMat = new Material(Shader.Find("Standard"));
        groundMat.color = new Color(0.086f, 0.141f, 0.078f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;

        // --- 森の装飾(Kenney Nature Kit。モデルが未インポートなら従来のプリミティブで代用) ---
        CreateBoundary();
        ScatterEnvironment();

        // --- プレイヤー(見た目はMeshy AIで作った3Dアセット。先に ChameleonNight > Setup Chameleon Model
        //     を実行してリグ・Animator Controllerを整えておくこと) ---
        var playerGO = new GameObject("Player");
        playerGO.transform.position = Vector3.zero;
        var playerCtrl = playerGO.AddComponent<SurvivorPlayerController>();
        playerCtrl.worldHalfExtent = WorldHalfExtent;
        playerCtrl.characterModelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/ChameleonMeshy/Chameleon.fbx");
        playerCtrl.animatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Models/ChameleonMeshy/ChameleonAnimator.controller");
        if (playerCtrl.characterModelPrefab == null)
        {
            Debug.LogWarning("SurvivorSceneBuilder: Chameleon.fbx が見つかりません。" +
                "先に ChameleonNight > Setup Chameleon Model を実行してください(未実行でもシーンは作られます)。");
        }

        // --- 懐中電灯(協力プレイヤー役。スマホ接続時はPointerServer経由、未接続時はマウス) ---
        var flashlightGO = new GameObject("Flashlight");
        flashlightGO.AddComponent<FlashlightController>();

        // --- スマホ連携用HTTPサーバー(タッチ/傾きをQR経由でLAN越しに受信) ---
        var ps = new GameObject("PointerServer");
        ps.AddComponent<PointerServer>();

        // --- マネージャー ---
        var managerGO = new GameObject("SurvivorManager");
        var manager = managerGO.AddComponent<SurvivorManager>();
        manager.PlayerTransform = playerGO.transform;
        manager.worldHalfExtent = WorldHalfExtent;

        // --- カメラ追従(デッドゾーン付き) ---
        var camFollow = camGO.AddComponent<SurvivorCameraFollow>();
        camFollow.target = playerGO.transform;

        // Save scene
        string sceneDir = "Assets/Scenes";
        if (!Directory.Exists(sceneDir)) Directory.CreateDirectory(sceneDir);
        string scenePath = sceneDir + "/ChameleonSurvivor.unity";
        EditorSceneManager.SaveScene(scene, scenePath);

        AssetDatabase.SaveAssets();
        Debug.Log("SurvivorSceneBuilder: survivor scene built at " + scenePath);
    }

    const string ModelDir = "Assets/Models/KenneyNature/FBX/";

    static readonly string[] TreeModels =
    {
        "tree_pineTallA", "tree_pineTallB", "tree_pineTallC", "tree_pineTallD",
        "tree_pineRoundA", "tree_pineRoundC", "tree_oak_dark", "tree_detailed_dark", "tree_default_dark", "tree_fat_darkh",
    };
    static readonly string[] RockModels =
    {
        "rock_largeA", "rock_largeB", "rock_largeC", "rock_largeD", "rock_tallA", "rock_tallB", "rock_tallC", "stone_largeA", "stone_largeB",
    };
    static readonly string[] BushModels = { "plant_bush", "plant_bushLarge", "plant_bushDetailed", "plant_bushSmall" };
    static readonly string[] MushroomModels = { "mushroom_redGroup", "mushroom_tanGroup", "mushroom_red", "mushroom_tan" };
    static readonly string[] PropModels = { "stump_old", "stump_round", "log", "log_large" };
    static readonly string[] GrassModels = { "grass", "grass_large", "grass_leafs" };

    static void ScatterEnvironment()
    {
        var root = new GameObject("Environment");
        var prevState = Random.state;
        Random.InitState(20260919); // 毎回同じ配置になるよう固定

        // プレイ範囲(±19)の外側にも森を広げて、端が途切れて見えないようにする
        // collisionFactor: モデルの外形(バウンディングボックス)に対する衝突円の大きさ。0なら通り抜け可能。
        // 木は樹冠の下を通り抜けて見えないよう、外形の7割程度を衝突範囲にする。
        ScatterKind(root, TreeModels, 80, 1.6f, 2.4f, 5f, 32f, 0.7f, treeFallback: true);
        ScatterKind(root, RockModels, 26, 1.2f, 2.2f, 4f, 28f, 1.0f, rockFallback: true);
        ScatterKind(root, BushModels, 46, 1.2f, 2.0f, 3f, 28f, 0f);
        ScatterKind(root, MushroomModels, 18, 1.4f, 2.2f, 3f, 24f, 0f);
        ScatterKind(root, PropModels, 14, 1.4f, 2.0f, 4f, 26f, 0.9f);
        ScatterKind(root, GrassModels, 140, 1.6f, 2.6f, 2f, 26f, 0f);

        Random.state = prevState;
    }

    static void ScatterKind(GameObject root, string[] models, int count, float minScale, float maxScale,
        float minRadius, float maxRadius, float collisionFactor, bool treeFallback = false, bool rockFallback = false)
    {
        for (int i = 0; i < count; i++)
        {
            // 中心付近(プレイヤー開始位置)を避けるため、半径 minRadius〜maxRadius の環状に散らす
            float angle = Random.value * Mathf.PI * 2f;
            float radius = Mathf.Lerp(minRadius, maxRadius, Mathf.Sqrt(Random.value));
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            float scale = Random.Range(minScale, maxScale);
            float yaw = Random.Range(0f, 360f);
            string model = models[Random.Range(0, models.Length)];

            var go = PlaceModel(model, new Vector3(x, 0, z), scale, yaw);
            if (go != null)
            {
                go.transform.SetParent(root.transform, true);
                if (collisionFactor > 0f) AddObstacle(go, MeasureRadius(go) * collisionFactor);
            }
            else if (treeFallback) AddObstacle(CreateTree(x, z, scale * 0.6f), 0.4f);
            else if (rockFallback) AddObstacle(CreateRock(x, z, scale * 0.6f), 0.7f);
        }
    }

    static void AddObstacle(GameObject go, float radius)
    {
        go.AddComponent<SurvivorObstacle>().radius = Mathf.Max(0.3f, radius);
    }

    // モデルのXZ方向の外形の半径(小さい方)。スケール済みの大きさで測る。
    static float MeasureRadius(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0.5f;
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return Mathf.Min(b.extents.x, b.extents.z);
    }

    static GameObject PlaceModel(string modelName, Vector3 pos, float scale, float yaw)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + modelName + ".fbx");
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0, yaw, 0);
        go.transform.localScale = Vector3.one * scale;
        return go;
    }

    static GameObject CreateTree(float x, float z, float scale)
    {
        var group = new GameObject("Tree");
        group.transform.position = new Vector3(x, 0, z);

        var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunk.name = "Trunk";
        trunk.transform.SetParent(group.transform, false);
        trunk.transform.localScale = new Vector3(0.3f * scale, 1.1f * scale, 0.3f * scale);
        trunk.transform.localPosition = new Vector3(0, 1.1f * scale, 0);
        var trunkMat = new Material(Shader.Find("Standard"));
        trunkMat.color = new Color(0.353f, 0.239f, 0.133f);
        trunk.GetComponent<MeshRenderer>().sharedMaterial = trunkMat;

        var leaves = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        leaves.name = "Leaves";
        leaves.transform.SetParent(group.transform, false);
        leaves.transform.localScale = Vector3.one * 2.8f * scale;
        leaves.transform.localPosition = new Vector3(0, 2.6f * scale, 0);
        var leavesMat = new Material(Shader.Find("Standard"));
        leavesMat.color = new Color(0.184f, 0.42f, 0.184f);
        leaves.GetComponent<MeshRenderer>().sharedMaterial = leavesMat;
        return group;
    }

    static GameObject CreateRock(float x, float z, float scale)
    {
        // Unity に正十二面体プリミティブは無いため、不揃いに回転させた立方体で代用する。
        var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rock.name = "Rock";
        rock.transform.position = new Vector3(x, 0.5f * scale, z);
        rock.transform.localScale = Vector3.one * 1.1f * scale;
        rock.transform.rotation = Random.rotation;
        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.333f, 0.353f, 0.373f);
        rock.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return rock;
    }

    // 移動可能範囲(±WorldHalfExtent)を示す。内側の床を少し明るくし、境界に発光ラインを引く。
    static void CreateBoundary()
    {
        var root = new GameObject("PlayArea");

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "PlayAreaFloor";
        floor.transform.SetParent(root.transform, false);
        floor.transform.position = new Vector3(0, 0.02f, 0);
        floor.transform.localScale = new Vector3(WorldHalfExtent * 2f / 10f, 1f, WorldHalfExtent * 2f / 10f);
        var floorMat = new Material(Shader.Find("Standard"));
        floorMat.color = new Color(0.2f, 0.34f, 0.18f);
        floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;
        Object.DestroyImmediate(floor.GetComponent<Collider>());

        // 暗い中でも見えるよう、ライティングの影響を受けない半透明の光る壁にする
        var lineMat = new Material(Shader.Find("Sprites/Default"));
        lineMat.color = new Color(0.13f, 0.85f, 0.4f, 0.35f);

        float len = WorldHalfExtent * 2f + 0.4f;
        const float h = 4f;
        CreateLine(root, "Wall_N", new Vector3(0, h / 2f, -WorldHalfExtent), new Vector3(len, h, 0.1f), lineMat);
        CreateLine(root, "Wall_S", new Vector3(0, h / 2f, WorldHalfExtent), new Vector3(len, h, 0.1f), lineMat);
        CreateLine(root, "Wall_W", new Vector3(-WorldHalfExtent, h / 2f, 0), new Vector3(0.1f, h, len), lineMat);
        CreateLine(root, "Wall_E", new Vector3(WorldHalfExtent, h / 2f, 0), new Vector3(0.1f, h, len), lineMat);
    }

    static void CreateLine(GameObject root, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        line.name = name;
        line.transform.SetParent(root.transform, false);
        line.transform.position = pos;
        line.transform.localScale = scale;
        line.GetComponent<MeshRenderer>().sharedMaterial = mat;
        Object.DestroyImmediate(line.GetComponent<Collider>());
    }
}
