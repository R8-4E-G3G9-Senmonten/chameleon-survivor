using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.IO;

public static class SceneBuilder
{
    [MenuItem("ChameleonNight/Build Prototype Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // --- Camera ---
        var camGO = new GameObject("Main Camera");
        var cam = camGO.AddComponent<Camera>();
        camGO.tag = "MainCamera";
        camGO.AddComponent<AudioListener>();
        cam.orthographic = true;
        cam.orthographicSize = 6.5f;
        cam.backgroundColor = new Color(0.03f, 0.05f, 0.03f);
        cam.transform.position = new Vector3(0, 0, -10);

        // --- Managers ---
        var gm = new GameObject("GameManager").AddComponent<GameManager>();
        var ps = new GameObject("PointerServer").AddComponent<PointerServer>();
        var light = new GameObject("LightController").AddComponent<LightController>();

        // --- Ground background ---
        var ground = new GameObject("Ground");
        var groundSr = ground.AddComponent<SpriteRenderer>();
        groundSr.sprite = SpriteFactory.CreateSquareSprite(Color.white, 8);
        groundSr.color = new Color(0.10f, 0.16f, 0.09f);
        groundSr.sortingOrder = -10;
        ground.transform.localScale = new Vector3(24, 16, 1);

        // --- Border walls (solid obstacles) ---
        CreateWall("Wall_Top", new Vector3(0, 6.5f, 0), new Vector3(22, 1, 1));
        CreateWall("Wall_Bottom", new Vector3(0, -6.5f, 0), new Vector3(22, 1, 1));
        CreateWall("Wall_Left", new Vector3(-10.5f, 0, 0), new Vector3(1, 14, 1));
        CreateWall("Wall_Right", new Vector3(10.5f, 0, 0), new Vector3(1, 14, 1));
        // A couple of inner obstacles for basic maze feel.
        CreateWall("Wall_Inner1", new Vector3(-1f, 1.5f, 0), new Vector3(1, 5, 1));
        CreateWall("Wall_Inner2", new Vector3(4f, -1f, 0), new Vector3(1, 6, 1));

        // --- Camouflage zones ---
        CreateZone("Zone_Leaf1", new Vector3(-6, 3.5f, 0), new Vector3(3, 2.5f, 1), new Color(0.20f, 0.55f, 0.18f));
        CreateZone("Zone_Leaf2", new Vector3(2, 4.5f, 0), new Vector3(3.5f, 2f, 1), new Color(0.22f, 0.6f, 0.2f));
        CreateZone("Zone_Trunk1", new Vector3(-3, -3.5f, 0), new Vector3(3, 2.5f, 1), new Color(0.42f, 0.28f, 0.14f));
        CreateZone("Zone_Ground1", new Vector3(6, -4f, 0), new Vector3(3.5f, 2.5f, 1), new Color(0.28f, 0.30f, 0.12f));
        CreateZone("Zone_Leaf3", new Vector3(7.5f, 3f, 0), new Vector3(3, 3, 1), new Color(0.20f, 0.55f, 0.18f));

        // --- Predators with patrol points ---
        CreatePredator("Predator_A", new Vector3(-2, 2, 0), new Vector3(2, 2, 0));
        CreatePredator("Predator_B", new Vector3(2, -4, 0), new Vector3(8, -4, 0));

        // --- Player ---
        var playerGO = new GameObject("Player");
        playerGO.transform.position = new Vector3(-8.5f, 0, 0);
        playerGO.AddComponent<Rigidbody2D>();
        playerGO.AddComponent<PlayerController>();

        // Camera follow
        var camFollow = camGO.AddComponent<CameraFollow>();
        camFollow.target = playerGO.transform;

        // --- Goal ---
        var goalGO = new GameObject("Goal");
        goalGO.transform.position = new Vector3(9, 4.8f, 0);
        goalGO.transform.localScale = new Vector3(1.5f, 1.5f, 1);
        goalGO.AddComponent<GoalZone>();

        // Save scene
        string sceneDir = "Assets/Scenes";
        if (!Directory.Exists(sceneDir)) Directory.CreateDirectory(sceneDir);
        string scenePath = sceneDir + "/ChameleonPrototype.unity";
        EditorSceneManager.SaveScene(scene, scenePath);

        var buildScenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
        EditorBuildSettings.scenes = buildScenes;

        AssetDatabase.SaveAssets();
        Debug.Log("SceneBuilder: prototype scene built at " + scenePath);
    }

    static void CreateWall(string name, Vector3 pos, Vector3 scale)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        go.transform.localScale = scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SpriteFactory.CreateSquareSprite(Color.white, 8);
        sr.color = new Color(0.18f, 0.12f, 0.08f);
        sr.sortingOrder = 1;
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = false;
    }

    static void CreateZone(string name, Vector3 pos, Vector3 scale, Color color)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        go.transform.localScale = scale;
        var zone = go.AddComponent<CamouflageZone>();
        zone.zoneColor = color;
    }

    static void CreatePredator(string name, Vector3 a, Vector3 b)
    {
        var parent = new GameObject(name);

        var wpA = new GameObject(name + "_PointA");
        wpA.transform.SetParent(parent.transform);
        wpA.transform.position = a;

        var wpB = new GameObject(name + "_PointB");
        wpB.transform.SetParent(parent.transform);
        wpB.transform.position = b;

        parent.transform.position = a;
        var predator = parent.AddComponent<Predator>();
        predator.pointA = wpA.transform;
        predator.pointB = wpB.transform;
    }
}
