using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class StandaloneInteractionValidationBuild
{
    private const string ScenePath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
    private const string DefaultOutputPath = "D:/StillWorkingValidation/Build/StillWorkingValidation.exe";
    private const string OutputArgument = "-standaloneValidationBuildPath";

    [MenuItem("Tools/Codex/Build Standalone Interaction Validator")]
    public static void BuildFromMenu()
    {
        Build(DefaultOutputPath);
    }

    public static void BuildFromCommandLine()
    {
        Build(ReadArgument(Environment.GetCommandLineArgs(), OutputArgument) ?? DefaultOutputPath);
    }

    private static void Build(string rawOutputPath)
    {
        string outputPath = Path.GetFullPath(rawOutputPath);
        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException($"Invalid standalone validation build path: {rawOutputPath}");

        Directory.CreateDirectory(outputDirectory);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development | BuildOptions.AllowDebugging
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException(
                $"Standalone interaction validation build failed: {report.summary.result} ({report.summary.totalErrors} errors).");

        var manifest = new BuildManifest
        {
            createdUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            unityVersion = Application.unityVersion,
            scene = ScenePath,
            outputPath = outputPath,
            buildGuid = report.summary.guid.ToString(),
            totalBytes = report.summary.totalSize
        };
        File.WriteAllText(Path.Combine(outputDirectory, "validation-build-manifest.json"), JsonUtility.ToJson(manifest, true));
        Debug.Log($"[StandaloneValidationBuild] Succeeded output={outputPath} bytes={report.summary.totalSize}");
    }

    private static string ReadArgument(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    [Serializable]
    private sealed class BuildManifest
    {
        public string createdUtc;
        public string unityVersion;
        public string scene;
        public string outputPath;
        public string buildGuid;
        public ulong totalBytes;
    }
}
