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

    // 毎回エディタを開いて Build Survivor Scene → シーンを開く → Play する手間を省くための単体exe。
    // scripts/play_survivor.bat から呼ばれる想定(エディタを起動せずコマンドラインだけでビルドできる)。
    public static void BuildSurvivor()
    {
        SurvivorSceneBuilder.Build();

        var report = BuildPipeline.BuildPlayer(
            new[] { "Assets/Scenes/ChameleonSurvivor.unity" },
            "Build/ChameleonSurvivor.exe",
            BuildTarget.StandaloneWindows64,
            BuildOptions.None);

        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            EditorApplication.Exit(1);
        }
    }
}
