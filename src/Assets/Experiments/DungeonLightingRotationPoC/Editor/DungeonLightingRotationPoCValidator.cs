using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Read-only proof-of-concept for reusing the Maze R000 lighting bake on Maze R090.
///
/// This validator never saves a prefab, scene, texture, or bake-data asset. It reads the
/// existing independently baked R000/R090 assets and compares corresponding mesh UV2
/// samples, SH probes, and reflection cubemaps. R090 is treated as the reference result.
/// </summary>
public static class DungeonLightingRotationPoCValidator
{
    private const string Root = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated";
    private const string Root2 = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated2";
    private const string EvidenceFolder = "Assets/Experiments/DungeonLightingRotationPoC/Evidence";
    private static readonly string[] Tiles =
    {
        "AdminstrativeSegregation",
        "Cafeteria",
        "Conference_Room",
        "CorridorA",
        "CrossRoom",
        "HalfOrbitCorridor",
        "LockerRoom",
        "Maze",
        "OfficerRoom",
        "OneTileThreeWays",
        "StartRoom",
        "WaitingCorridor"
    };
    private static readonly string[] Tiles2 =
    {
        "CorridorB",
        "Library",
        "OldStorage",
        "Outdoor_Corridor",
        "PrisonRoom",
        "RandomRoom1",
        "ShowerRoom"
    };

    [Serializable]
    public sealed class Report
    {
        public string tile;
        public int sourceRotation;
        public int rotation;
        public string power;
        public bool completed;
        public string failure;
        public StructureReport structure = new StructureReport();
        public LightmapReport lightmaps = new LightmapReport();
        public RotationMetric sh = new RotationMetric();
        public RotationMetric reflection = new RotationMetric();
        public bool colorReuseSupported;
        public bool directionalRotationSupported;
        public bool shRotationSupported;
        public bool reflectionRotationSupported;
    }

    [Serializable]
    public sealed class BatchReport
    {
        public bool completed;
        public string failure;
        public int totalCases;
        public int completedCases;
        public int passedCases;
        public int failedCases;
        public float worstColorMeanRelativeError;
        public string worstColorCase;
        public float worstDirectionImprovementRatio;
        public string worstDirectionCase;
        public float worstShImprovementRatio;
        public string worstShCase;
        public float worstReflectionImprovementRatio;
        public string worstReflectionCase;
        public List<string> failedCaseNames = new List<string>();
        public List<Report> cases = new List<Report>();
    }

    [Serializable]
    private sealed class BatchSummary
    {
        public bool completed;
        public string failure;
        public int totalCases;
        public int completedCases;
        public int passedCases;
        public int failedCases;
        public float worstColorMeanRelativeError;
        public string worstColorCase;
        public float worstDirectionImprovementRatio;
        public string worstDirectionCase;
        public float worstShImprovementRatio;
        public string worstShCase;
        public float worstReflectionImprovementRatio;
        public string worstReflectionCase;
        public string fullReportPath;
        public List<string> failedCaseNames = new List<string>();
    }

    [Serializable]
    public sealed class StructureReport
    {
        public bool passed;
        public int rendererEntryCountR000;
        public int rendererEntryCountR090;
        public int rendererPathMismatchCount;
        public int rendererMeshMismatchCount;
        public int lightmapCountR000;
        public int lightmapCountR090;
        public int lightProbeCountR000;
        public int lightProbeCountR090;
        public int lightProbeLocalPositionMismatchCount;
        public float maxLightProbeLocalPositionError;
        public int reflectionProbeCountR000;
        public int reflectionProbeCountR090;
        public int reflectionPathMismatchCount;
        public bool reflectionTopologyMatches;
    }

    [Serializable]
    public sealed class LightmapReport
    {
        public int sampledMeshCount;
        public int skippedMeshCount;
        public int colorSampleCount;
        public float colorMeanAbsoluteError;
        public float colorMeanRelativeError;
        public float colorMeanCosineSimilarity;
        public int directionalSampleCount;
        public float directionUnrotatedMeanError;
        public float directionPlus90MeanError;
        public float directionMinus90MeanError;
        public float directionBestRotatedMeanError;
        public string directionBestRotation;
        public float directionAlphaMeanError;
    }

    [Serializable]
    public sealed class RotationMetric
    {
        public int entryCount;
        public int sampleCount;
        public int skippedCount;
        public float unrotatedMeanRelativeError;
        public float plus90MeanRelativeError;
        public float minus90MeanRelativeError;
        public float bestRotatedMeanRelativeError;
        public float unrotatedMeanAbsoluteError;
        public float plus90MeanAbsoluteError;
        public float minus90MeanAbsoluteError;
        public float bestRotatedMeanAbsoluteError;
        public float meanReferenceMagnitude;
        public string bestRotation;
        public float improvementRatio;
    }

    private sealed class MetricAccumulator
    {
        public int count;
        public double absoluteSum;
        public double relativeSum;
        public double referenceMagnitudeSum;

        public void Add(Vector3 a, Vector3 b)
        {
            float absolute = Vector3.Distance(a, b);
            float denominator = a.magnitude + b.magnitude + 1e-4f;
            absoluteSum += absolute;
            relativeSum += absolute / denominator;
            referenceMagnitudeSum += b.magnitude;
            count++;
        }

        public float MeanAbsolute => count > 0 ? (float)(absoluteSum / count) : 0f;
        public float MeanRelative => count > 0 ? (float)(relativeSum / count) : 0f;
        public float MeanReferenceMagnitude => count > 0 ? (float)(referenceMagnitudeSum / count) : 0f;
    }

    private sealed class CubePixels
    {
        public int size;
        public readonly Color[][] faces = new Color[6][];
    }

    [MenuItem("Tools/Dungeon Lighting/Rotation PoC/Validate Maze P100")]
    public static void ValidateMazeP100Menu()
    {
        Debug.Log(RunMazeP100Cli());
    }

    [MenuItem("Tools/Dungeon Lighting/Rotation PoC/Validate Maze P0")]
    public static void ValidateMazeP0Menu()
    {
        Debug.Log(RunMazeP0Cli());
    }

    public static string RunMazeP100Cli()
    {
        return Run("Maze", 90, "P100");
    }

    public static string RunMazeP0Cli()
    {
        return Run("Maze", 90, "P0");
    }

    public static string RunCaseCli(string tile, int rotation, string power)
    {
        return Run(tile, rotation, power);
    }

    public static string RunCaseFromRotationCli(
        string tile,
        int sourceRotation,
        int targetRotation,
        string power)
    {
        Report report = RunReportFromRotation(tile, sourceRotation, targetRotation, power);
        string json = JsonUtility.ToJson(report, true);
        Debug.Log($"[DungeonLightingRotationPoC] {tile} R{sourceRotation:000}->R{targetRotation:000} {power}\n{json}");
        return json;
    }

    public static string RunAllTilesCli()
    {
        return RunBatch(Root, Tiles, "AllTilesRotationValidation.json");
    }

    public static string RunAllTilesRotated2Cli()
    {
        return RunBatch(Root2, Tiles2, "AllTilesRotated2RotationValidation.json");
    }

    public static string RunTilesRotated2CaseCli(string tile, int rotation, string power)
    {
        Report report = RunReportFromRotation(Root2, tile, 0, rotation, power);
        string json = JsonUtility.ToJson(report, true);
        Debug.Log($"[DungeonLightingRotationPoC] Tiles_Rotated2 {tile} R{rotation:000} {power}\n{json}");
        return json;
    }

    private static string RunBatch(string root, IReadOnlyList<string> tiles, string outputFileName)
    {
        var batch = new BatchReport { totalCases = tiles.Count * 3 * 2 };
        try
        {
            int caseIndex = 0;
            foreach (string tile in tiles)
            {
                foreach (int rotation in new[] { 90, 180, 270 })
                {
                    foreach (string power in new[] { "P100", "P0" })
                    {
                        caseIndex++;
                        EditorUtility.DisplayProgressBar(
                            "Dungeon Lighting Rotation PoC",
                            $"{caseIndex}/{batch.totalCases} {tile} R{rotation:000} {power}",
                            caseIndex / (float)batch.totalCases);
                        Debug.Log($"[DungeonLightingRotationPoC] Batch {caseIndex}/{batch.totalCases}: {tile} R{rotation:000} {power}");
                        Report report = RunReportFromRotation(root, tile, 0, rotation, power);
                        batch.cases.Add(report);
                        if (report.completed)
                            batch.completedCases++;

                        bool passed = IsCasePassed(report);
                        if (passed)
                            batch.passedCases++;
                        else
                        {
                            batch.failedCases++;
                            batch.failedCaseNames.Add(CaseName(report));
                        }

                        UpdateWorstCases(batch, report);
                    }
                }
            }
            batch.completed = batch.completedCases == batch.totalCases;
        }
        catch (Exception exception)
        {
            batch.completed = false;
            batch.failure = exception.ToString();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        string fullPath = Path.GetFullPath(EvidenceFolder + "/" + outputFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Path.GetFullPath(EvidenceFolder));
        File.WriteAllText(fullPath, JsonUtility.ToJson(batch, true));

        var summary = new BatchSummary
        {
            completed = batch.completed,
            failure = batch.failure,
            totalCases = batch.totalCases,
            completedCases = batch.completedCases,
            passedCases = batch.passedCases,
            failedCases = batch.failedCases,
            worstColorMeanRelativeError = batch.worstColorMeanRelativeError,
            worstColorCase = batch.worstColorCase,
            worstDirectionImprovementRatio = batch.worstDirectionImprovementRatio,
            worstDirectionCase = batch.worstDirectionCase,
            worstShImprovementRatio = batch.worstShImprovementRatio,
            worstShCase = batch.worstShCase,
            worstReflectionImprovementRatio = batch.worstReflectionImprovementRatio,
            worstReflectionCase = batch.worstReflectionCase,
            fullReportPath = EvidenceFolder + "/" + outputFileName,
            failedCaseNames = batch.failedCaseNames
        };
        string summaryJson = JsonUtility.ToJson(summary, true);
        Debug.Log($"[DungeonLightingRotationPoC] Batch complete\n{summaryJson}");
        return summaryJson;
    }

    private static string Run(string tile, int rotation, string power)
    {
        Report report = RunReport(tile, rotation, power);
        string json = JsonUtility.ToJson(report, true);
        Debug.Log($"[DungeonLightingRotationPoC] {tile} R{rotation:000} {power}\n{json}");
        return json;
    }

    private static Report RunReport(string tile, int rotation, string power)
    {
        return RunReportFromRotation(tile, 0, rotation, power);
    }

    private static Report RunReportFromRotation(
        string tile,
        int sourceRotation,
        int targetRotation,
        string power)
    {
        return RunReportFromRotation(Root, tile, sourceRotation, targetRotation, power);
    }

    private static Report RunReportFromRotation(
        string root,
        string tile,
        int sourceRotation,
        int targetRotation,
        string power)
    {
        int normalizedSource = ((sourceRotation % 360) + 360) % 360;
        int normalizedTarget = ((targetRotation % 360) + 360) % 360;
        int deltaRotation = (normalizedTarget - normalizedSource + 360) % 360;
        var report = new Report
        {
            tile = tile,
            sourceRotation = normalizedSource,
            rotation = normalizedTarget,
            power = power
        };
        try
        {
            string sourceName = $"{tile}_R{normalizedSource:000}";
            string targetName = $"{tile}_R{normalizedTarget:000}";
            GameObject prefabR000 = LoadRequired<GameObject>($"{root}/{sourceName}.prefab");
            GameObject prefabTarget = LoadRequired<GameObject>($"{root}/{targetName}.prefab");
            DungeonTileBakeData dataR000 = LoadRequired<DungeonTileBakeData>(BuildDataPath(root, sourceName, power));
            DungeonTileBakeData dataTarget = LoadRequired<DungeonTileBakeData>(BuildDataPath(root, targetName, power));

            CompareStructure(prefabR000, prefabTarget, dataR000, dataTarget, report.structure);
            CompareLightmaps(prefabR000, prefabTarget, dataR000, dataTarget, deltaRotation, report.lightmaps);
            CompareSphericalHarmonics(dataR000, dataTarget, deltaRotation, report.sh);
            if (report.structure.reflectionTopologyMatches)
                CompareReflectionCubemaps(dataR000, dataTarget, deltaRotation, report.reflection);
            else
            {
                report.reflection.entryCount = Mathf.Max(
                    dataR000.reflectionProbeEntries?.Length ?? 0,
                    dataTarget.reflectionProbeEntries?.Length ?? 0);
                report.reflection.skippedCount = report.reflection.entryCount;
            }

            report.colorReuseSupported = report.structure.passed &&
                                         report.lightmaps.colorSampleCount >= 1 &&
                                         (report.lightmaps.colorMeanRelativeError <= 0.25f ||
                                          report.lightmaps.colorMeanAbsoluteError <= 0.005f);
            report.directionalRotationSupported = report.lightmaps.directionalSampleCount < 100 ||
                                                  report.lightmaps.directionBestRotatedMeanError <
                                                  report.lightmaps.directionUnrotatedMeanError * 0.7f ||
                                                  report.lightmaps.directionBestRotatedMeanError <= 0.05f;
            report.shRotationSupported = report.sh.entryCount == 0 ||
                                         report.sh.bestRotatedMeanRelativeError <= 0.05f ||
                                         (report.sh.sampleCount >= 100 && report.sh.improvementRatio < 0.7f);
            report.reflectionRotationSupported = report.structure.reflectionTopologyMatches &&
                                                 (report.reflection.entryCount == 0 ||
                                                 report.reflection.bestRotatedMeanRelativeError <= 0.05f ||
                                                 report.reflection.bestRotatedMeanAbsoluteError <= 0.02f ||
                                                 (report.reflection.sampleCount >= 100 && report.reflection.improvementRatio < 0.7f));
            report.completed = true;
        }
        catch (Exception exception)
        {
            report.completed = false;
            report.failure = exception.ToString();
        }
        return report;
    }

    private static string BuildDataPath(string variantName, string power)
    {
        return BuildDataPath(Root, variantName, power);
    }

    private static string BuildDataPath(string root, string variantName, string power)
    {
        return $"{root}/BakedData/{variantName}/{power}/{variantName}_BakeData.asset";
    }

    private static bool IsCasePassed(Report report)
    {
        return report.completed &&
               report.structure.passed &&
               report.colorReuseSupported &&
               report.directionalRotationSupported &&
               report.shRotationSupported &&
               report.reflectionRotationSupported;
    }

    private static string CaseName(Report report)
    {
        return $"{report.tile}_R{report.rotation:000}_{report.power}";
    }

    private static void UpdateWorstCases(BatchReport batch, Report report)
    {
        string caseName = CaseName(report);
        if (report.lightmaps.colorMeanRelativeError >= batch.worstColorMeanRelativeError)
        {
            batch.worstColorMeanRelativeError = report.lightmaps.colorMeanRelativeError;
            batch.worstColorCase = caseName;
        }
        float directionRatio = report.lightmaps.directionUnrotatedMeanError > 1e-6f
            ? report.lightmaps.directionBestRotatedMeanError / report.lightmaps.directionUnrotatedMeanError
            : 0f;
        if (directionRatio >= batch.worstDirectionImprovementRatio)
        {
            batch.worstDirectionImprovementRatio = directionRatio;
            batch.worstDirectionCase = caseName;
        }
        if (report.sh.improvementRatio >= batch.worstShImprovementRatio)
        {
            batch.worstShImprovementRatio = report.sh.improvementRatio;
            batch.worstShCase = caseName;
        }
        if (report.reflection.improvementRatio >= batch.worstReflectionImprovementRatio)
        {
            batch.worstReflectionImprovementRatio = report.reflection.improvementRatio;
            batch.worstReflectionCase = caseName;
        }
    }

    private static T LoadRequired<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
            throw new InvalidOperationException($"Required asset was not found: {path}");
        return asset;
    }

    private static void CompareStructure(
        GameObject prefabR000,
        GameObject prefabR090,
        DungeonTileBakeData dataR000,
        DungeonTileBakeData dataR090,
        StructureReport report)
    {
        DungeonTileBakeData.RendererBakeEntry[] entriesR000 = dataR000.rendererEntries ?? Array.Empty<DungeonTileBakeData.RendererBakeEntry>();
        DungeonTileBakeData.RendererBakeEntry[] entriesR090 = dataR090.rendererEntries ?? Array.Empty<DungeonTileBakeData.RendererBakeEntry>();
        Renderer[] renderersR000 = prefabR000.GetComponentsInChildren<Renderer>(true);
        Renderer[] renderersR090 = prefabR090.GetComponentsInChildren<Renderer>(true);

        report.rendererEntryCountR000 = entriesR000.Length;
        report.rendererEntryCountR090 = entriesR090.Length;
        report.lightmapCountR000 = dataR000.lightmapColors?.Length ?? 0;
        report.lightmapCountR090 = dataR090.lightmapColors?.Length ?? 0;

        int rendererCount = Mathf.Min(
            Mathf.Min(entriesR000.Length, entriesR090.Length),
            Mathf.Min(renderersR000.Length, renderersR090.Length));
        for (int i = 0; i < rendererCount; i++)
        {
            if (!string.Equals(entriesR000[i].relativePath, entriesR090[i].relativePath, StringComparison.Ordinal))
                report.rendererPathMismatchCount++;

            Mesh meshR000 = GetRendererMesh(renderersR000[i]);
            Mesh meshR090 = GetRendererMesh(renderersR090[i]);
            if (meshR000 != meshR090)
                report.rendererMeshMismatchCount++;
        }

        DungeonTileBakeData.LightProbeBakeEntry[] lightProbesR000 = dataR000.lightProbeEntries ?? Array.Empty<DungeonTileBakeData.LightProbeBakeEntry>();
        DungeonTileBakeData.LightProbeBakeEntry[] lightProbesR090 = dataR090.lightProbeEntries ?? Array.Empty<DungeonTileBakeData.LightProbeBakeEntry>();
        report.lightProbeCountR000 = lightProbesR000.Length;
        report.lightProbeCountR090 = lightProbesR090.Length;
        int lightProbeCount = Mathf.Min(lightProbesR000.Length, lightProbesR090.Length);
        for (int i = 0; i < lightProbeCount; i++)
        {
            float error = Vector3.Distance(lightProbesR000[i].localPosition, lightProbesR090[i].localPosition);
            report.maxLightProbeLocalPositionError = Mathf.Max(report.maxLightProbeLocalPositionError, error);
            if (error > 1e-4f)
                report.lightProbeLocalPositionMismatchCount++;
        }

        DungeonTileBakeData.ReflectionProbeBakeEntry[] reflectionR000 = dataR000.reflectionProbeEntries ?? Array.Empty<DungeonTileBakeData.ReflectionProbeBakeEntry>();
        DungeonTileBakeData.ReflectionProbeBakeEntry[] reflectionR090 = dataR090.reflectionProbeEntries ?? Array.Empty<DungeonTileBakeData.ReflectionProbeBakeEntry>();
        report.reflectionProbeCountR000 = reflectionR000.Length;
        report.reflectionProbeCountR090 = reflectionR090.Length;
        int reflectionCount = Mathf.Min(reflectionR000.Length, reflectionR090.Length);
        for (int i = 0; i < reflectionCount; i++)
        {
            if (!string.Equals(reflectionR000[i].relativePath, reflectionR090[i].relativePath, StringComparison.Ordinal))
                report.reflectionPathMismatchCount++;
        }

        report.reflectionTopologyMatches = reflectionR000.Length == reflectionR090.Length &&
                                           report.reflectionPathMismatchCount == 0;

        report.passed = entriesR000.Length == entriesR090.Length &&
                        renderersR000.Length == renderersR090.Length &&
                        report.rendererPathMismatchCount == 0 &&
                        report.rendererMeshMismatchCount == 0 &&
                        report.lightmapCountR000 == report.lightmapCountR090 &&
                        lightProbesR000.Length == lightProbesR090.Length &&
                        report.lightProbeLocalPositionMismatchCount == 0;
    }

    private static void CompareLightmaps(
        GameObject prefabR000,
        GameObject prefabR090,
        DungeonTileBakeData dataR000,
        DungeonTileBakeData dataR090,
        float rotationDegrees,
        LightmapReport report)
    {
        Renderer[] renderersR000 = prefabR000.GetComponentsInChildren<Renderer>(true);
        Renderer[] renderersR090 = prefabR090.GetComponentsInChildren<Renderer>(true);
        DungeonTileBakeData.RendererBakeEntry[] entriesR000 = dataR000.rendererEntries ?? Array.Empty<DungeonTileBakeData.RendererBakeEntry>();
        DungeonTileBakeData.RendererBakeEntry[] entriesR090 = dataR090.rendererEntries ?? Array.Empty<DungeonTileBakeData.RendererBakeEntry>();

        int rendererCount = Mathf.Min(
            Mathf.Min(entriesR000.Length, entriesR090.Length),
            Mathf.Min(renderersR000.Length, renderersR090.Length));
        var groups = new Dictionary<long, List<int>>();
        for (int i = 0; i < rendererCount; i++)
        {
            int indexR000 = entriesR000[i].lightmapIndex;
            int indexR090 = entriesR090[i].lightmapIndex;
            if (indexR000 < 0 || indexR090 < 0 ||
                indexR000 >= (dataR000.lightmapColors?.Length ?? 0) ||
                indexR090 >= (dataR090.lightmapColors?.Length ?? 0))
            {
                report.skippedMeshCount++;
                continue;
            }

            long key = ((long)indexR000 << 32) | (uint)indexR090;
            if (!groups.TryGetValue(key, out List<int> indices))
            {
                indices = new List<int>();
                groups.Add(key, indices);
            }
            indices.Add(i);
        }

        var colorMetric = new MetricAccumulator();
        double colorCosineSum = 0d;
        int colorCosineCount = 0;
        var directionUnrotated = new MetricAccumulator();
        var directionPlus = new MetricAccumulator();
        var directionMinus = new MetricAccumulator();
        double directionAlphaErrorSum = 0d;

        Quaternion plusRotation = Quaternion.Euler(0f, rotationDegrees, 0f);
        Quaternion minusRotation = Quaternion.Euler(0f, -rotationDegrees, 0f);

        foreach (KeyValuePair<long, List<int>> pair in groups)
        {
            int indexR000 = (int)(pair.Key >> 32);
            int indexR090 = (int)(pair.Key & 0xffffffffL);
            Texture2D colorR000 = ReadbackTexture(dataR000.lightmapColors[indexR000]);
            Texture2D colorR090 = ReadbackTexture(dataR090.lightmapColors[indexR090]);
            Texture2D directionR000 = ReadbackOptionalTexture(dataR000.lightmapDirections, indexR000);
            Texture2D directionR090 = ReadbackOptionalTexture(dataR090.lightmapDirections, indexR090);

            try
            {
                foreach (int rendererIndex in pair.Value)
                {
                    Mesh meshR000 = GetRendererMesh(renderersR000[rendererIndex]);
                    Mesh meshR090 = GetRendererMesh(renderersR090[rendererIndex]);
                    if (meshR000 == null || meshR090 == null || meshR000 != meshR090)
                    {
                        report.skippedMeshCount++;
                        continue;
                    }

                    Vector2[] uv2 = meshR000.uv2;
                    int[] triangles = meshR000.triangles;
                    if (uv2 == null || uv2.Length == 0 || triangles == null || triangles.Length < 3)
                    {
                        report.skippedMeshCount++;
                        continue;
                    }

                    report.sampledMeshCount++;
                    int triangleCount = triangles.Length / 3;
                    int samplesForMesh = Mathf.Min(24, triangleCount);
                    for (int sample = 0; sample < samplesForMesh; sample++)
                    {
                        int triangleIndex = Mathf.Min(triangleCount - 1, sample * triangleCount / samplesForMesh);
                        int offset = triangleIndex * 3;
                        int a = triangles[offset];
                        int b = triangles[offset + 1];
                        int c = triangles[offset + 2];
                        if ((uint)a >= uv2.Length || (uint)b >= uv2.Length || (uint)c >= uv2.Length)
                            continue;

                        Vector2 localUv = (uv2[a] + uv2[b] + uv2[c]) / 3f;
                        Vector2 atlasUvR000 = ApplyScaleOffset(localUv, entriesR000[rendererIndex].lightmapScaleOffset);
                        Vector2 atlasUvR090 = ApplyScaleOffset(localUv, entriesR090[rendererIndex].lightmapScaleOffset);
                        if (!IsUnitUv(atlasUvR000) || !IsUnitUv(atlasUvR090))
                            continue;

                        Color sampleColorR000 = colorR000.GetPixelBilinear(atlasUvR000.x, atlasUvR000.y);
                        Color sampleColorR090 = colorR090.GetPixelBilinear(atlasUvR090.x, atlasUvR090.y);
                        Vector3 rgbR000 = ToRgb(sampleColorR000);
                        Vector3 rgbR090 = ToRgb(sampleColorR090);
                        if (!IsFinite(rgbR000) || !IsFinite(rgbR090))
                            continue;

                        colorMetric.Add(rgbR000, rgbR090);
                        if (rgbR000.sqrMagnitude > 1e-8f && rgbR090.sqrMagnitude > 1e-8f)
                        {
                            colorCosineSum += Vector3.Dot(rgbR000.normalized, rgbR090.normalized);
                            colorCosineCount++;
                        }

                        if (directionR000 == null || directionR090 == null ||
                            Mathf.Max(MaxComponent(rgbR000), MaxComponent(rgbR090)) < 0.0025f)
                            continue;

                        Color rawDirectionR000 = directionR000.GetPixelBilinear(atlasUvR000.x, atlasUvR000.y);
                        Color rawDirectionR090 = directionR090.GetPixelBilinear(atlasUvR090.x, atlasUvR090.y);
                        Vector3 vectorR000 = new Vector3(rawDirectionR000.r - 0.5f, rawDirectionR000.g - 0.5f, rawDirectionR000.b - 0.5f);
                        Vector3 vectorR090 = new Vector3(rawDirectionR090.r - 0.5f, rawDirectionR090.g - 0.5f, rawDirectionR090.b - 0.5f);
                        if (!IsFinite(vectorR000) || !IsFinite(vectorR090))
                            continue;

                        directionUnrotated.Add(vectorR000, vectorR090);
                        directionPlus.Add(plusRotation * vectorR000, vectorR090);
                        directionMinus.Add(minusRotation * vectorR000, vectorR090);
                        directionAlphaErrorSum += Mathf.Abs(rawDirectionR000.a - rawDirectionR090.a);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(colorR000);
                UnityEngine.Object.DestroyImmediate(colorR090);
                if (directionR000 != null) UnityEngine.Object.DestroyImmediate(directionR000);
                if (directionR090 != null) UnityEngine.Object.DestroyImmediate(directionR090);
            }
        }

        report.colorSampleCount = colorMetric.count;
        report.colorMeanAbsoluteError = colorMetric.MeanAbsolute;
        report.colorMeanRelativeError = colorMetric.MeanRelative;
        report.colorMeanCosineSimilarity = colorCosineCount > 0 ? (float)(colorCosineSum / colorCosineCount) : 0f;
        report.directionalSampleCount = directionUnrotated.count;
        report.directionUnrotatedMeanError = directionUnrotated.MeanAbsolute;
        report.directionPlus90MeanError = directionPlus.MeanAbsolute;
        report.directionMinus90MeanError = directionMinus.MeanAbsolute;
        bool plusIsBest = directionPlus.MeanAbsolute <= directionMinus.MeanAbsolute;
        report.directionBestRotatedMeanError = plusIsBest ? directionPlus.MeanAbsolute : directionMinus.MeanAbsolute;
        report.directionBestRotation = plusIsBest ? $"+{rotationDegrees:0}" : $"-{rotationDegrees:0}";
        report.directionAlphaMeanError = directionUnrotated.count > 0
            ? (float)(directionAlphaErrorSum / directionUnrotated.count)
            : 0f;
    }

    private static void CompareSphericalHarmonics(
        DungeonTileBakeData dataR000,
        DungeonTileBakeData dataR090,
        float rotationDegrees,
        RotationMetric report)
    {
        DungeonTileBakeData.LightProbeBakeEntry[] entriesR000 = dataR000.lightProbeEntries ?? Array.Empty<DungeonTileBakeData.LightProbeBakeEntry>();
        DungeonTileBakeData.LightProbeBakeEntry[] entriesR090 = dataR090.lightProbeEntries ?? Array.Empty<DungeonTileBakeData.LightProbeBakeEntry>();
        report.entryCount = Mathf.Min(entriesR000.Length, entriesR090.Length);

        Vector3[] directions = BuildFibonacciDirections(48);
        Vector3[] inversePlusDirections = RotateDirections(directions, -rotationDegrees);
        Vector3[] inverseMinusDirections = RotateDirections(directions, rotationDegrees);
        var unrotated = new MetricAccumulator();
        var plus = new MetricAccumulator();
        var minus = new MetricAccumulator();
        var colorsR090 = new Color[directions.Length];
        var colorsUnrotated = new Color[directions.Length];
        var colorsPlus = new Color[directions.Length];
        var colorsMinus = new Color[directions.Length];

        for (int i = 0; i < report.entryCount; i++)
        {
            SphericalHarmonicsL2 shR000 = entriesR000[i].ToSphericalHarmonics();
            SphericalHarmonicsL2 shR090 = entriesR090[i].ToSphericalHarmonics();
            shR090.Evaluate(directions, colorsR090);
            shR000.Evaluate(directions, colorsUnrotated);
            shR000.Evaluate(inversePlusDirections, colorsPlus);
            shR000.Evaluate(inverseMinusDirections, colorsMinus);

            for (int d = 0; d < directions.Length; d++)
            {
                Vector3 reference = ToRgb(colorsR090[d]);
                if (!IsFinite(reference))
                {
                    report.skippedCount++;
                    continue;
                }

                unrotated.Add(ToRgb(colorsUnrotated[d]), reference);
                plus.Add(ToRgb(colorsPlus[d]), reference);
                minus.Add(ToRgb(colorsMinus[d]), reference);
            }
        }

        PopulateRotationMetric(report, unrotated, plus, minus, rotationDegrees);
    }

    private static void CompareReflectionCubemaps(
        DungeonTileBakeData dataR000,
        DungeonTileBakeData dataR090,
        float rotationDegrees,
        RotationMetric report)
    {
        DungeonTileBakeData.ReflectionProbeBakeEntry[] entriesR000 = dataR000.reflectionProbeEntries ?? Array.Empty<DungeonTileBakeData.ReflectionProbeBakeEntry>();
        DungeonTileBakeData.ReflectionProbeBakeEntry[] entriesR090 = dataR090.reflectionProbeEntries ?? Array.Empty<DungeonTileBakeData.ReflectionProbeBakeEntry>();
        report.entryCount = Mathf.Min(entriesR000.Length, entriesR090.Length);

        Vector3[] directions = BuildFibonacciDirections(384);
        Quaternion inversePlus = Quaternion.Euler(0f, -rotationDegrees, 0f);
        Quaternion inverseMinus = Quaternion.Euler(0f, rotationDegrees, 0f);
        var unrotated = new MetricAccumulator();
        var plus = new MetricAccumulator();
        var minus = new MetricAccumulator();

        for (int i = 0; i < report.entryCount; i++)
        {
            Cubemap cubemapR000 = entriesR000[i].bakedTexture;
            Cubemap cubemapR090 = entriesR090[i].bakedTexture;
            if (cubemapR000 == null || cubemapR090 == null)
            {
                report.skippedCount++;
                continue;
            }

            CubePixels pixelsR000 = ReadbackCubemap(cubemapR000);
            CubePixels pixelsR090 = ReadbackCubemap(cubemapR090);
            for (int d = 0; d < directions.Length; d++)
            {
                Vector3 direction = directions[d];
                Vector3 reference = ToRgb(SampleCubemap(pixelsR090, direction));
                Vector3 original = ToRgb(SampleCubemap(pixelsR000, direction));
                Vector3 rotatedPlus = ToRgb(SampleCubemap(pixelsR000, inversePlus * direction));
                Vector3 rotatedMinus = ToRgb(SampleCubemap(pixelsR000, inverseMinus * direction));
                if (!IsFinite(reference) || !IsFinite(original) || !IsFinite(rotatedPlus) || !IsFinite(rotatedMinus))
                {
                    report.skippedCount++;
                    continue;
                }

                unrotated.Add(original, reference);
                plus.Add(rotatedPlus, reference);
                minus.Add(rotatedMinus, reference);
            }
        }

        PopulateRotationMetric(report, unrotated, plus, minus, rotationDegrees);
    }

    private static void PopulateRotationMetric(
        RotationMetric report,
        MetricAccumulator unrotated,
        MetricAccumulator plus,
        MetricAccumulator minus,
        float rotationDegrees)
    {
        report.sampleCount = unrotated.count;
        report.unrotatedMeanRelativeError = unrotated.MeanRelative;
        report.plus90MeanRelativeError = plus.MeanRelative;
        report.minus90MeanRelativeError = minus.MeanRelative;
        report.unrotatedMeanAbsoluteError = unrotated.MeanAbsolute;
        report.plus90MeanAbsoluteError = plus.MeanAbsolute;
        report.minus90MeanAbsoluteError = minus.MeanAbsolute;
        report.meanReferenceMagnitude = unrotated.MeanReferenceMagnitude;
        bool plusIsBest = plus.MeanRelative <= minus.MeanRelative;
        report.bestRotatedMeanRelativeError = plusIsBest ? plus.MeanRelative : minus.MeanRelative;
        report.bestRotatedMeanAbsoluteError = plusIsBest ? plus.MeanAbsolute : minus.MeanAbsolute;
        report.bestRotation = plusIsBest ? $"+{rotationDegrees:0}" : $"-{rotationDegrees:0}";
        report.improvementRatio = unrotated.MeanRelative > 1e-6f
            ? report.bestRotatedMeanRelativeError / unrotated.MeanRelative
            : 1f;
    }

    private static Texture2D ReadbackOptionalTexture(Texture2D[] textures, int index)
    {
        if (textures == null || index < 0 || index >= textures.Length || textures[index] == null)
            return null;
        return ReadbackTexture(textures[index]);
    }

    private static Texture2D ReadbackTexture(Texture source)
    {
        var temporary = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear);
        RenderTexture previous = RenderTexture.active;
        bool previousSrgbWrite = GL.sRGBWrite;
        try
        {
            GL.sRGBWrite = false;
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            var result = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
            result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            result.Apply(false, false);
            return result;
        }
        finally
        {
            GL.sRGBWrite = previousSrgbWrite;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
        }
    }

    private static CubePixels ReadbackCubemap(Cubemap source)
    {
        var decoded = new Cubemap(source.width, TextureFormat.RGBAFloat, false, true);
        try
        {
            if (!Graphics.ConvertTexture(source, decoded))
                throw new InvalidOperationException($"Could not decode cubemap '{source.name}'.");

            var result = new CubePixels { size = source.width };
            for (int face = 0; face < 6; face++)
            {
                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(
                    decoded,
                    0,
                    0,
                    source.width,
                    0,
                    source.height,
                    face,
                    1,
                    TextureFormat.RGBAFloat);
                request.WaitForCompletion();
                if (request.hasError)
                    throw new InvalidOperationException($"GPU readback failed for cubemap '{source.name}', face {face}.");

                var native = request.GetData<Color>();
                var colors = new Color[native.Length];
                for (int i = 0; i < native.Length; i++)
                    colors[i] = native[i];
                result.faces[face] = colors;
            }
            return result;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(decoded);
        }
    }

    private static Color SampleCubemap(CubePixels cube, Vector3 direction)
    {
        direction.Normalize();
        float ax = Mathf.Abs(direction.x);
        float ay = Mathf.Abs(direction.y);
        float az = Mathf.Abs(direction.z);
        int face;
        float u;
        float v;

        if (ax >= ay && ax >= az)
        {
            if (direction.x >= 0f)
            {
                face = (int)CubemapFace.PositiveX;
                u = -direction.z / ax;
                v = -direction.y / ax;
            }
            else
            {
                face = (int)CubemapFace.NegativeX;
                u = direction.z / ax;
                v = -direction.y / ax;
            }
        }
        else if (ay >= ax && ay >= az)
        {
            if (direction.y >= 0f)
            {
                face = (int)CubemapFace.PositiveY;
                u = direction.x / ay;
                v = direction.z / ay;
            }
            else
            {
                face = (int)CubemapFace.NegativeY;
                u = direction.x / ay;
                v = -direction.z / ay;
            }
        }
        else
        {
            if (direction.z >= 0f)
            {
                face = (int)CubemapFace.PositiveZ;
                u = direction.x / az;
                v = -direction.y / az;
            }
            else
            {
                face = (int)CubemapFace.NegativeZ;
                u = -direction.x / az;
                v = -direction.y / az;
            }
        }

        float x = (u * 0.5f + 0.5f) * (cube.size - 1);
        float y = (v * 0.5f + 0.5f) * (cube.size - 1);
        int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, cube.size - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, cube.size - 1);
        int x1 = Mathf.Min(x0 + 1, cube.size - 1);
        int y1 = Mathf.Min(y0 + 1, cube.size - 1);
        float tx = x - x0;
        float ty = y - y0;
        Color[] pixels = cube.faces[face];
        Color c00 = pixels[y0 * cube.size + x0];
        Color c10 = pixels[y0 * cube.size + x1];
        Color c01 = pixels[y1 * cube.size + x0];
        Color c11 = pixels[y1 * cube.size + x1];
        return Color.Lerp(Color.Lerp(c00, c10, tx), Color.Lerp(c01, c11, tx), ty);
    }

    private static Vector3[] BuildFibonacciDirections(int count)
    {
        var directions = new Vector3[count];
        float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int i = 0; i < count; i++)
        {
            float y = 1f - (2f * (i + 0.5f) / count);
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float angle = goldenAngle * i;
            directions[i] = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        }
        return directions;
    }

    private static Vector3[] RotateDirections(Vector3[] directions, float degrees)
    {
        Quaternion rotation = Quaternion.Euler(0f, degrees, 0f);
        var rotated = new Vector3[directions.Length];
        for (int i = 0; i < directions.Length; i++)
            rotated[i] = rotation * directions[i];
        return rotated;
    }

    private static Mesh GetRendererMesh(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned)
            return skinned.sharedMesh;
        if (renderer is MeshRenderer)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }
        return null;
    }

    private static Vector2 ApplyScaleOffset(Vector2 uv, Vector4 scaleOffset)
    {
        return new Vector2(
            uv.x * scaleOffset.x + scaleOffset.z,
            uv.y * scaleOffset.y + scaleOffset.w);
    }

    private static bool IsUnitUv(Vector2 uv)
    {
        return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
    }

    private static Vector3 ToRgb(Color color)
    {
        return new Vector3(color.r, color.g, color.b);
    }

    private static float MaxComponent(Vector3 value)
    {
        return Mathf.Max(value.x, Mathf.Max(value.y, value.z));
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
