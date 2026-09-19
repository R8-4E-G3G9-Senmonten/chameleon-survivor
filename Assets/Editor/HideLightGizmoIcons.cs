using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Gameビューに Light のギズモアイコン(電球/太陽のマーク)が映り込むのを防ぐ。
// 1) Gizmos and Icons の Light のアイコン/ギズモを無効化
// 2) Gameビューの「Gizmos」表示自体をOFFにする
// どちらも Unity の内部APIをリフレクションで呼ぶため、失敗時は警告を出す。
[InitializeOnLoad]
public static class HideLightGizmoIcons
{
    const int LightClassId = 108;
    const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static HideLightGizmoIcons()
    {
        // ウィンドウ生成後に実行するため1フレーム遅らせる
        EditorApplication.delayCall += Apply;
        EditorApplication.playModeStateChanged += _ => Apply();
    }

    [MenuItem("ChameleonNight/Hide Light Icons In Game View")]
    static void Apply()
    {
        try { DisableLightAnnotations(); }
        catch (Exception e) { Debug.LogWarning("HideLightGizmoIcons: アイコン無効化に失敗: " + e.Message); }

        try { DisableGameViewGizmos(); }
        catch (Exception e) { Debug.LogWarning("HideLightGizmoIcons: Gameビューのギズモ無効化に失敗: " + e.Message); }
    }

    static void DisableLightAnnotations()
    {
        var util = typeof(Editor).Assembly.GetType("UnityEditor.AnnotationUtility");
        if (util == null) return;

        var setIcon = util.GetMethod("SetIconEnabled", AnyStatic);
        var setGizmo = util.GetMethod("SetGizmoEnabled", AnyStatic);
        var getAnnotations = util.GetMethod("GetAnnotations", AnyStatic);

        if (getAnnotations != null)
        {
            var list = getAnnotations.Invoke(null, null) as Array;
            if (list != null)
            {
                foreach (var a in list)
                {
                    var t = a.GetType();
                    int classId = (int)t.GetField("classID", AnyInstance).GetValue(a);
                    string script = (string)t.GetField("scriptClass", AnyInstance).GetValue(a);
                    if (classId != LightClassId) continue;
                    setIcon?.Invoke(null, new object[] { classId, script, 0 });
                    setGizmo?.Invoke(null, new object[] { classId, script, 0 });
                }
                return;
            }
        }

        setIcon?.Invoke(null, new object[] { LightClassId, "", 0 });
        setGizmo?.Invoke(null, new object[] { LightClassId, "", 0 });
    }

    static void DisableGameViewGizmos()
    {
        var gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        if (gameViewType == null) return;

        var prop = gameViewType.GetProperty("showGizmos", AnyInstance);
        foreach (var view in Resources.FindObjectsOfTypeAll(gameViewType))
        {
            if (prop != null) prop.SetValue(view, false);
            else gameViewType.GetField("m_Gizmos", AnyInstance)?.SetValue(view, false);
            ((EditorWindow)view).Repaint();
        }
    }
}
