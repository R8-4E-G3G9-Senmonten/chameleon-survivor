using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Linq;

// Meshy AIで作ったカメレオンのFBX(本体 + 歩行アニメーション)を、Unityでそのまま
// 使える状態(リグ設定・アバター共有・Animator Controller)まで自動で整えるセットアップ。
//
// 前提:
//   Assets/Models/ChameleonMeshy/Chameleon.fbx       … 本体(リグ付きメッシュ)
//   Assets/Models/ChameleonMeshy/Chameleon_Walk.fbx  … 歩行アニメーション(同じ骨格+スキン付き)
//
// 実行方法: メニュー ChameleonNight > Setup Chameleon Model
// (Chameleon_Walk.fbx のアニメーションを Chameleon.fbx の骨格に適用するには、
//  両者のボーン階層・名前が一致している必要がある。Meshyの「単一ファイル」書き出しの
//  代わりに2ファイルに分かれているため、ここでアバターを明示的に共有させている)
public static class SetupChameleonModel
{
    const string BaseFbxPath = "Assets/Models/ChameleonMeshy/Chameleon.fbx";
    const string WalkFbxPath = "Assets/Models/ChameleonMeshy/Chameleon_Walk.fbx";
    const string ControllerPath = "Assets/Models/ChameleonMeshy/ChameleonAnimator.controller";
    const string WalkClipAssetPath = "Assets/Models/ChameleonMeshy/WalkClip.anim";

    [MenuItem("ChameleonNight/Setup Chameleon Model")]
    public static void Setup()
    {
        ConfigureRig(BaseFbxPath, null);
        AssetDatabase.ImportAsset(BaseFbxPath, ImportAssetOptions.ForceUpdate);

        var baseAvatar = AssetDatabase.LoadAllAssetsAtPath(BaseFbxPath).OfType<Avatar>().FirstOrDefault();
        if (baseAvatar == null)
        {
            Debug.LogError("SetupChameleonModel: " + BaseFbxPath + " からAvatarを取得できませんでした。" +
                "リグ付きで書き出されているか確認してください。");
            return;
        }

        ConfigureRig(WalkFbxPath, baseAvatar);
        AssetDatabase.ImportAsset(WalkFbxPath, ImportAssetOptions.ForceUpdate);
        SetWalkClipLoop();

        SetupMaterial(); // ここで WalkFbxPath も再インポートされる(マテリアルのリマップ)。
                          // FBX埋め込みのクリップを直接編集しても次の再インポートで元に戻ってしまうため、
                          // 全ての再インポートが終わった後で、独立したアセットとして複製してから編集する。

        var walkClip = ExtractStandaloneWalkClip();
        if (walkClip == null) return;

        BuildAnimatorController(walkClip);

        AssetDatabase.SaveAssets();
        Debug.Log("SetupChameleonModel: 完了。ChameleonAnimator.controller を作成しました。");
    }

    // FBXに埋め込まれたクリップをそのまま参照すると、次にモデルを再インポートするたびに
    // 中身(カーブ)が元のFBXデータで上書きされてしまい、スケールカーブ除去などの編集が消えてしまう。
    // そのため、独立した .anim アセットとして複製して保存し、以後はこちらを使う。
    static AnimationClip ExtractStandaloneWalkClip()
    {
        var source = AssetDatabase.LoadAllAssetsAtPath(WalkFbxPath).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.Contains("__preview__"));
        if (source == null)
        {
            Debug.LogError("SetupChameleonModel: " + WalkFbxPath + " からAnimationClipを取得できませんでした。");
            return null;
        }

        var clip = Object.Instantiate(source);
        clip.name = "WalkClip";

        // Meshyの歩行アニメーションには、体全体を拡大縮小させるスケールのカーブが混ざっており、
        // 本体側(Chameleon.fbx)の骨格に適用すると歩いている間だけ体の大きさが変わって見えてしまう。
        // 見た目上スケールのアニメーションは不要なので取り除く。
        var bindings = AnimationUtility.GetCurveBindings(clip);
        int removed = 0;
        foreach (var binding in bindings)
        {
            if (binding.propertyName.StartsWith("m_LocalScale"))
            {
                AnimationUtility.SetEditorCurve(clip, binding, null);
                removed++;
            }
        }
        Debug.Log("SetupChameleonModel: WalkClip からスケールのカーブを " + removed + " 本取り除きました。");

        FixHipsHeightOffset(clip);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkClipAssetPath);
        if (existing != null) AssetDatabase.DeleteAsset(WalkClipAssetPath);
        AssetDatabase.CreateAsset(clip, WalkClipAssetPath);
        AssetDatabase.SaveAssets();

        return clip;
    }

    // Meshyの書き出しは、ファイル内蔵の単位スケールが正しく設定されておらず、
    // 読み込むと実寸の約1/1000(数ミリ相当)になってしまう。そのため useFileScale を切り、
    // globalScale で直接倍率を指定する。この値は DiagnoseChameleonModel の実測結果から逆算して調整すること。
    // globalScale=1 で実測したところ、体長にあたる範囲が約0.78ユニットしかなかった(実寸の1/1000に
    // なってしまう単位変換ミスがある)。既存の敵・プレイヤーの当たり判定(半径0.45前後)に合う
    // 体長1.8〜2.0ユニットに近づくよう、2.5倍を基準値にしている。
    public const float ModelScaleFactor = 2.5f;

    // 歩行アニメーションの再生速度の倍率。1.0で等倍、1.5なら1.5倍速。
    // ここを変えて ChameleonNight > Setup Chameleon Model を再実行すれば反映される。
    public const float WalkAnimationSpeed = 1.4f;

    static void ConfigureRig(string path, Avatar copyFrom)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError("SetupChameleonModel: " + path + " が見つかりません。先にFBXを配置してください。");
            return;
        }

        importer.useFileScale = false;
        importer.globalScale = ModelScaleFactor;
        importer.animationType = ModelImporterAnimationType.Generic;
        if (copyFrom != null)
        {
            // 歩行アニメーション側は自分の骨格を使わず、本体(Chameleon.fbx)のアバターを共有する。
            // こうすることでアニメーションクリップを本体のモデルにそのまま適用できる。
            importer.sourceAvatar = copyFrom;
        }
        else
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        }

        importer.importAnimation = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;

        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
    }

    // 歩行アニメーションのクリップ設定を「ループする」ようにする。デフォルトはループしない設定に
    // なっていて、しばらく歩き続けると最後のフレームで止まってしまう(=アニメーションが終わったように見える)。
    static void SetWalkClipLoop()
    {
        var importer = AssetImporter.GetAtPath(WalkFbxPath) as ModelImporter;
        if (importer == null) return;

        var clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;

        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i].name.Contains("__preview__")) continue;
            clips[i].loopTime = true;
            clips[i].loopPose = true;
        }
        importer.clipAnimations = clips;

        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
    }

    // Meshyの歩行アニメーションは、腰(Hips)の高さが本体側の骨格の静止姿勢よりも一定量高い位置で
    // 焼き込まれており(実測で約0.2ユニット)、Walkに切り替わった瞬間に体全体が浮き上がって見える。
    // ほぼ一定値(上下動としての変化はごくわずか)なので、カーブ全体を「静止姿勢の高さ」に
    // 合わせて平行移動させることでズレを解消する。
    static void FixHipsHeightOffset(AnimationClip clip)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BaseFbxPath);
        var smr = prefab != null ? prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        var rootBone = smr != null ? smr.rootBone : null;
        if (rootBone == null)
        {
            Debug.LogWarning("SetupChameleonModel: rootBoneが取得できず、高さのズレ補正をスキップしました。");
            return;
        }
        float restY = rootBone.localPosition.y;

        var bindings = AnimationUtility.GetCurveBindings(clip);
        foreach (var binding in bindings)
        {
            if (binding.propertyName != "m_LocalPosition.y") continue;
            if (binding.path != rootBone.name && !binding.path.EndsWith("/" + rootBone.name)) continue;

            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            var keys = curve.keys;
            float avg = 0f;
            foreach (var k in keys) avg += k.value;
            avg /= Mathf.Max(1, keys.Length);

            float delta = restY - avg;
            for (int i = 0; i < keys.Length; i++) keys[i].value += delta;
            curve.keys = keys;
            AnimationUtility.SetEditorCurve(clip, binding, curve);

            Debug.Log(string.Format("SetupChameleonModel: '{0}' の高さを静止姿勢(Y={1:F3})に合わせて {2:F3} だけ補正しました(補正前平均Y={3:F3})。",
                binding.path, restY, delta, avg));
        }
    }

    static void BuildAnimatorController(AnimationClip walkClip)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);

        var rootStateMachine = controller.layers[0].stateMachine;

        var idleState = rootStateMachine.AddState("Idle");
        idleState.motion = null; // クリップが無い間は最初のポーズで静止

        var walkState = rootStateMachine.AddState("Walk");
        walkState.motion = walkClip;
        walkState.speed = WalkAnimationSpeed; // 歩行アニメーションの再生速度の倍率。1.0が等倍

        rootStateMachine.defaultState = idleState;

        var toWalk = idleState.AddTransition(walkState);
        toWalk.hasExitTime = false;
        toWalk.duration = 0.15f;
        toWalk.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");

        var toIdle = walkState.AddTransition(idleState);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.15f;
        toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");
    }

    // Meshy書き出しのメタリック/ラフネスは別テクスチャに分かれており、Standardシェーダーが
    // 期待する「メタリック(R)+スムースネス(A)」1枚packingとは形式が違うため、ここでは
    // Albedo+法線だけをStandardシェーダーに接続し、メタリック/スムースネスは値で近似する。
    //
    // FBXに埋め込まれたマテリアルを直接書き換えても、次にモデルを再インポートした瞬間に
    // FBX内のマテリアル定義で上書きされて消えてしまう。そのため、ここでは独立した
    // Materialアセットを新しく作り、ModelImporter.AddRemap で「FBX内のマテリアル名」→
    // 「このMaterialアセット」を紐付ける(公式にサポートされている恒久的な差し替え方法)。
    static void SetupMaterial()
    {
        string albedoPath = "Assets/Models/ChameleonMeshy/Meshy_AI_Spiky_Chameleon_quadruped_texture_0.png";
        string normalPath = "Assets/Models/ChameleonMeshy/Meshy_AI_Spiky_Chameleon_quadruped_texture_0_normal.png";
        string matPath = "Assets/Models/ChameleonMeshy/ChameleonBody.mat";

        // コピー直後のテクスチャがまだ正しくインポートされていないことがあるため、明示的に再インポートしてから使う
        AssetDatabase.ImportAsset(albedoPath, ImportAssetOptions.ForceUpdate);
        SetTextureImportType(normalPath, TextureImporterType.NormalMap);

        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);

        if (albedo == null) Debug.LogWarning("SetupChameleonModel: albedoテクスチャの読み込みに失敗しました: " + albedoPath);
        if (normal == null) Debug.LogWarning("SetupChameleonModel: 法線マップの読み込みに失敗しました: " + normalPath);

        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, matPath);
        }
        else
        {
            mat.shader = Shader.Find("Standard");
        }

        if (albedo != null) mat.SetTexture("_MainTex", albedo);
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.EnableKeyword("_NORMALMAP");
        }
        mat.SetFloat("_Metallic", 0.05f);
        mat.SetFloat("_Glossiness", 0.35f);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        RemapMaterials(BaseFbxPath, mat);
        RemapMaterials(WalkFbxPath, mat);
    }

    static void RemapMaterials(string fbxPath, Material replacement)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null) return;

        var embeddedMaterials = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Material>().ToArray();
        foreach (var embedded in embeddedMaterials)
        {
            var id = new AssetImporter.SourceAssetIdentifier(embedded);
            importer.AddRemap(id, replacement);
            Debug.Log("SetupChameleonModel: " + fbxPath + " のマテリアル '" + embedded.name + "' を " + replacement.name + " にリマップしました。");
        }
        importer.SaveAndReimport();
    }

    static void SetTextureImportType(string path, TextureImporterType type)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;
        importer.textureType = type;
        importer.SaveAndReimport();
    }
}
