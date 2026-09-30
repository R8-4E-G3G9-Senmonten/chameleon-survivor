using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Linq;

public static class DiagnoseAnimatorSetup
{
    const string ControllerPath = "Assets/Models/ChameleonMeshy/ChameleonAnimator.controller";

    [MenuItem("ChameleonNight/Diagnose Animator Setup")]
    public static void Run()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError("DiagnoseAnimatorSetup: controller が見つかりません: " + ControllerPath);
            return;
        }

        foreach (var state in controller.layers[0].stateMachine.states)
        {
            var motion = state.state.motion;
            if (motion is AnimationClip clip)
            {
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                int scaleCurves = AnimationUtility.GetCurveBindings(clip).Count(b => b.propertyName.StartsWith("m_LocalScale"));
                Debug.Log(string.Format(
                    "State '{0}': motion='{1}' length={2:F2}s loopTime={3} スケールカーブ残り={4}",
                    state.state.name, clip.name, clip.length, settings.loopTime, scaleCurves));
            }
            else
            {
                Debug.Log(string.Format("State '{0}': motion={1}", state.state.name, motion == null ? "null(静止ポーズ)" : motion.name));
            }
        }
    }
}
