using UnityEditor;
using UnityEditor.Build.Reporting;

public static class BuildScript
{
    public static void BuildWindows()
    {
        var report = BuildPipeline.BuildPlayer(
            new[] { "Assets/Scenes/ChameleonPrototype.unity" },
            "Build/ChameleonNight.exe",
            BuildTarget.StandaloneWindows64,
            BuildOptions.None);

        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            EditorApplication.Exit(1);
        }
    }
}
