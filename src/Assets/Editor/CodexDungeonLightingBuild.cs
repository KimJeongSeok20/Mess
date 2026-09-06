using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class CodexDungeonLightingBuild
{
    // 빌드 출력은 프로젝트 루트 옆 Builds/ 아래로 내보낸다.
    // 로컬 절대경로를 박아두면 다른 머신에서 그대로 실패한다.
    private static string ResolveOutputPath()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, "Builds", "DungeonLightmapProbeRelease", "StartOn.exe");
    }

    public static void BuildLightmapProbeRelease()
    {
        string outputPath = ResolveOutputPath();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        var report = BuildPipeline.BuildPlayer(
            new[] { "Assets/SceneTemplateAssets/Scenes/StartMap.unity" },
            outputPath,
            BuildTarget.StandaloneWindows64,
            BuildOptions.None);

        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException(
                $"Dungeon lightmap probe release build failed: {report.summary.result} ({report.summary.totalErrors} errors).");
    }
}
