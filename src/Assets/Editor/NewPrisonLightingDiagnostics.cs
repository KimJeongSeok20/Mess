using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class NewPrisonLightingDiagnostics
{
    private const string TilesRotatedFolder = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated";
    private const string BakedDataFolder = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/BakedData";
    private const string LightingDiagnosticsFolder = "Screenshots/LightingDiagnostics";
    private static readonly int[] TileRotations = { 0, 90, 180, 270 };
    private static readonly string[] PowerSuffixes = { "P100", "P0" };

    public static string VerifyStartRoomPower0ReflectionProbeSuppression()
    {
        var report = new StringBuilder();
        report.AppendLine("[NewPrisonLightingDiagnostics] StartRoom P0 reflection probe suppression");

        List<GameObject> prefabs = LoadStartRoomPrefabs();
        report.AppendLine($"prefabs={prefabs.Count}");

        int failingPrefabs = 0;
        for (int i = 0; i < prefabs.Count; i++)
        {
            GameObject prefab = prefabs[i];
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = $"{prefab.name}_ProbeSuppressionTest";

            try
            {
                var switcher = instance.GetComponent<DungeonTileLightmapSwitcher>();
                if (switcher == null)
                {
                    failingPrefabs++;
                    report.AppendLine($"  {prefab.name}: missing DungeonTileLightmapSwitcher");
                    continue;
                }

                InvokeAwake(switcher);

                switcher.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P100);
                CountReflectionProbes(instance, out int p100Total, out int p100Enabled);

                switcher.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P0);
                CountReflectionProbes(instance, out int p0Total, out int p0Enabled);

                if (p0Enabled != 0)
                    failingPrefabs++;

                report.AppendLine(
                    $"  {prefab.name}: P100 probes={p100Enabled}/{p100Total}, P0 probes={p0Enabled}/{p0Total}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        report.AppendLine($"failingPrefabs={failingPrefabs}");
        return report.ToString();
    }

    public static string ReportCrossRoomFloorSpecklesCli()
    {
        return ReportFloorLightmapSpeckles("CrossRoom", exportCropsForFirstPower100: true);
    }

    public static string ReportFloorLightmapSpeckles(string baseTileName, bool exportCropsForFirstPower100 = false)
    {
        var report = new StringBuilder();
        report.AppendLine($"[NewPrisonLightingDiagnostics] floor lightmap speckle report: {baseTileName}");

        int missingBakeData = 0;
        int analyzedEntries = 0;
        int suspiciousEntries = 0;
        bool exportedCrops = false;

        for (int rotationIndex = 0; rotationIndex < TileRotations.Length; rotationIndex++)
        {
            int rotation = TileRotations[rotationIndex];
            string prefabName = $"{baseTileName}_R{rotation:000}";

            for (int powerIndex = 0; powerIndex < PowerSuffixes.Length; powerIndex++)
            {
                string power = PowerSuffixes[powerIndex];
                string dataPath = $"{BakedDataFolder}/{prefabName}/{power}/{prefabName}_BakeData.asset";
                var data = AssetDatabase.LoadAssetAtPath<DungeonTileBakeData>(dataPath);
                if (data == null)
                {
                    missingBakeData++;
                    report.AppendLine($"{prefabName}/{power}: missing bake data ({dataPath})");
                    continue;
                }

                bool exportThisData = exportCropsForFirstPower100 && !exportedCrops && power == "P100";
                var result = AnalyzeFloorLightmapRegions(data, prefabName, power, exportThisData);
                analyzedEntries += result.AnalyzedEntries;
                suspiciousEntries += result.SuspiciousEntries;
                if (result.ExportedCrops > 0)
                    exportedCrops = true;

                report.Append(result.Report);
            }
        }

        report.AppendLine(
            $"summary: missingBakeData={missingBakeData}, analyzedEntries={analyzedEntries}, suspiciousEntries={suspiciousEntries}");
        return report.ToString();
    }

    private static List<GameObject> LoadStartRoomPrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab StartRoom", new[] { TilesRotatedFolder });
        var prefabs = new List<GameObject>();

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrWhiteSpace(path) || path.IndexOf("/BakedData/", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || !prefab.name.StartsWith("StartRoom", StringComparison.OrdinalIgnoreCase))
                continue;

            prefabs.Add(prefab);
        }

        return prefabs
            .OrderBy(prefab => prefab.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void InvokeAwake(DungeonTileLightmapSwitcher switcher)
    {
        MethodInfo awake = typeof(DungeonTileLightmapSwitcher).GetMethod(
            "Awake",
            BindingFlags.Instance | BindingFlags.NonPublic);

        awake?.Invoke(switcher, null);
    }

    private static void CountReflectionProbes(GameObject root, out int total, out int enabled)
    {
        total = 0;
        enabled = 0;

        var probes = root.GetComponentsInChildren<ReflectionProbe>(true);
        for (int i = 0; i < probes.Length; i++)
        {
            ReflectionProbe probe = probes[i];
            if (probe == null)
                continue;

            total++;
            if (probe.enabled)
                enabled++;
        }
    }

    private static FloorLightmapAnalysisResult AnalyzeFloorLightmapRegions(
        DungeonTileBakeData data,
        string prefabName,
        string powerSuffix,
        bool exportCrops)
    {
        var result = new FloorLightmapAnalysisResult();
        var report = new StringBuilder();
        report.AppendLine($"{prefabName}/{powerSuffix}: lightmaps={GetLength(data.lightmapColors)}, entries={GetLength(data.rendererEntries)}");

        var readableLightmaps = new Dictionary<Texture2D, Texture2D>();
        try
        {
            var entries = data.rendererEntries ?? Array.Empty<DungeonTileBakeData.RendererBakeEntry>();
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (!IsFloorSpeckleCandidate(entry.relativePath))
                    continue;

                result.AnalyzedEntries++;
                var stats = AnalyzeLightmapRegion(data, entry, readableLightmaps);
                if (!stats.HasTexture)
                {
                    report.AppendLine($"  {i}: {entry.relativePath} lm={entry.lightmapIndex} missing lightmap texture");
                    continue;
                }

                if (stats.IsSuspicious)
                    result.SuspiciousEntries++;

                report.AppendLine(
                    $"  {i}: {stats.Label} {entry.relativePath} lm={entry.lightmapIndex} rect={stats.Rect} pixels={stats.PixelCount} mean={stats.Mean:0.0000} p95={stats.P95:0.0000} p99={stats.P99:0.0000} max={stats.Max:0.0000} hotAbs={stats.AbsoluteHotPixels} hotOutlier={stats.OutlierHotPixels} isolated={stats.IsolatedHotPixels} suspicious={stats.IsSuspicious}");

                if (exportCrops && stats.HasTexture && stats.Label == "FLOOR")
                {
                    ExportLightmapCrop(stats, prefabName, powerSuffix, i, entry.relativePath);
                    result.ExportedCrops++;
                }
            }
        }
        finally
        {
            foreach (Texture2D texture in readableLightmaps.Values)
            {
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        result.Report = report.ToString();
        return result;
    }

    private static LightmapRegionStats AnalyzeLightmapRegion(
        DungeonTileBakeData data,
        DungeonTileBakeData.RendererBakeEntry entry,
        Dictionary<Texture2D, Texture2D> readableLightmaps)
    {
        var stats = new LightmapRegionStats
        {
            Label = GetFloorCandidateLabel(entry.relativePath)
        };

        var lightmaps = data.lightmapColors ?? Array.Empty<Texture2D>();
        if (entry.lightmapIndex < 0 || entry.lightmapIndex >= lightmaps.Length)
            return stats;

        Texture2D source = lightmaps[entry.lightmapIndex];
        if (source == null)
            return stats;

        if (!readableLightmaps.TryGetValue(source, out Texture2D readable))
        {
            readable = CreateReadableLightmapCopy(source);
            readableLightmaps[source] = readable;
        }

        RectInt rect = BuildLightmapPixelRect(entry.lightmapScaleOffset, readable.width, readable.height);
        if (rect.width <= 0 || rect.height <= 0)
            return stats;

        int pixelCount = rect.width * rect.height;
        var luminance = new float[pixelCount];
        bool[] hotMask = new bool[pixelCount];
        double sum = 0d;
        double sum2 = 0d;
        float max = 0f;

        int index = 0;
        for (int y = rect.yMin; y < rect.yMax; y++)
        {
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                float value = GetLuminance(readable.GetPixel(x, y));
                luminance[index++] = value;
                sum += value;
                sum2 += value * value;
                if (value > max)
                    max = value;
            }
        }

        Array.Sort(luminance);
        float mean = pixelCount > 0 ? (float)(sum / pixelCount) : 0f;
        float variance = pixelCount > 0 ? Mathf.Max(0f, (float)(sum2 / pixelCount - mean * mean)) : 0f;
        float std = Mathf.Sqrt(variance);
        float p50 = GetPercentile(luminance, 0.50f);
        float p95 = GetPercentile(luminance, 0.95f);
        float p99 = GetPercentile(luminance, 0.99f);
        float absoluteThreshold = 0.25f;
        float outlierThreshold = Mathf.Max(absoluteThreshold, mean + std * 4f);

        int absoluteHotPixels = 0;
        int outlierHotPixels = 0;
        index = 0;
        for (int y = rect.yMin; y < rect.yMax; y++)
        {
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                float value = GetLuminance(readable.GetPixel(x, y));
                if (value > absoluteThreshold)
                    absoluteHotPixels++;

                bool isHot = value > outlierThreshold;
                hotMask[index++] = isHot;
                if (isHot)
                    outlierHotPixels++;
            }
        }

        int isolatedHotPixels = CountIsolatedHotPixels(hotMask, rect.width, rect.height);
        bool suspicious = max > Mathf.Max(0.35f, p99 * 1.75f) ||
            isolatedHotPixels > Mathf.Max(8, pixelCount / 10000);

        stats.HasTexture = true;
        stats.Source = readable;
        stats.Rect = rect;
        stats.PixelCount = pixelCount;
        stats.Mean = mean;
        stats.P50 = p50;
        stats.P95 = p95;
        stats.P99 = p99;
        stats.Max = max;
        stats.OutlierThreshold = outlierThreshold;
        stats.AbsoluteHotPixels = absoluteHotPixels;
        stats.OutlierHotPixels = outlierHotPixels;
        stats.IsolatedHotPixels = isolatedHotPixels;
        stats.IsSuspicious = suspicious;
        return stats;
    }

    private static Texture2D CreateReadableLightmapCopy(Texture2D source)
    {
        var renderTexture = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.ARGBHalf,
            RenderTextureReadWrite.Linear);

        RenderTexture previous = RenderTexture.active;
        Graphics.Blit(source, renderTexture);
        RenderTexture.active = renderTexture;

        var copy = new Texture2D(source.width, source.height, TextureFormat.RGBAHalf, false, true);
        copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        copy.Apply(false, false);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(renderTexture);
        return copy;
    }

    private static void ExportLightmapCrop(
        LightmapRegionStats stats,
        string prefabName,
        string powerSuffix,
        int entryIndex,
        string relativePath)
    {
        if (stats.Source == null || stats.Rect.width <= 0 || stats.Rect.height <= 0)
            return;

        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outputFolder = Path.Combine(root, LightingDiagnosticsFolder);
        Directory.CreateDirectory(outputFolder);

        var crop = new Texture2D(stats.Rect.width, stats.Rect.height, TextureFormat.RGBA32, false, false);
        float multiplier = 1f / Mathf.Max(0.001f, stats.P99);
        for (int y = 0; y < stats.Rect.height; y++)
        {
            for (int x = 0; x < stats.Rect.width; x++)
            {
                Color source = stats.Source.GetPixel(stats.Rect.xMin + x, stats.Rect.yMin + y);
                float luminance = GetLuminance(source);
                bool hot = luminance > stats.OutlierThreshold;
                Color output = hot
                    ? new Color(1f, 0f, 0f, 1f)
                    : new Color(source.r * multiplier, source.g * multiplier, source.b * multiplier, 1f);
                crop.SetPixel(x, y, output);
            }
        }

        crop.Apply(false, false);
        string fileName = $"{prefabName}_{powerSuffix}_{entryIndex:000}_{SanitizeFileName(relativePath)}_lightmapHot.png";
        File.WriteAllBytes(Path.Combine(outputFolder, fileName), crop.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(crop);
    }

    private static RectInt BuildLightmapPixelRect(Vector4 scaleOffset, int width, int height)
    {
        int xMin = Mathf.Clamp(Mathf.FloorToInt(scaleOffset.z * width), 0, width);
        int yMin = Mathf.Clamp(Mathf.FloorToInt(scaleOffset.w * height), 0, height);
        int xMax = Mathf.Clamp(Mathf.CeilToInt((scaleOffset.z + scaleOffset.x) * width), 0, width);
        int yMax = Mathf.Clamp(Mathf.CeilToInt((scaleOffset.w + scaleOffset.y) * height), 0, height);
        return new RectInt(xMin, yMin, Mathf.Max(0, xMax - xMin), Mathf.Max(0, yMax - yMin));
    }

    private static bool IsFloorSpeckleCandidate(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return false;

        return relativePath.Equals("Floors/Floor", StringComparison.OrdinalIgnoreCase) ||
            relativePath.IndexOf("Decal_stripe", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string GetFloorCandidateLabel(string relativePath)
    {
        return !string.IsNullOrEmpty(relativePath) &&
            relativePath.IndexOf("Decal_stripe", StringComparison.OrdinalIgnoreCase) >= 0
                ? "STRIPE"
                : "FLOOR";
    }

    private static float GetLuminance(Color color)
    {
        return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
    }

    private static float GetPercentile(float[] sortedValues, float percentile)
    {
        if (sortedValues == null || sortedValues.Length == 0)
            return 0f;

        int index = Mathf.Clamp(Mathf.RoundToInt((sortedValues.Length - 1) * percentile), 0, sortedValues.Length - 1);
        return sortedValues[index];
    }

    private static int CountIsolatedHotPixels(bool[] hotMask, int width, int height)
    {
        if (hotMask == null || hotMask.Length == 0 || width <= 0 || height <= 0)
            return 0;

        int isolated = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (!hotMask[index])
                    continue;

                int neighbors = 0;
                for (int oy = -1; oy <= 1; oy++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oy == 0)
                            continue;

                        int nx = x + ox;
                        int ny = y + oy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                            continue;

                        if (hotMask[ny * width + nx])
                            neighbors++;
                    }
                }

                if (neighbors <= 1)
                    isolated++;
            }
        }

        return isolated;
    }

    private static int GetLength<T>(T[] values)
    {
        return values != null ? values.Length : 0;
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "unnamed";

        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            builder.Append(Path.GetInvalidFileNameChars().Contains(c) || c == '/' || c == '\\' ? '_' : c);
        }

        return builder.ToString();
    }

    private struct FloorLightmapAnalysisResult
    {
        public string Report;
        public int AnalyzedEntries;
        public int SuspiciousEntries;
        public int ExportedCrops;
    }

    private struct LightmapRegionStats
    {
        public bool HasTexture;
        public string Label;
        public Texture2D Source;
        public RectInt Rect;
        public int PixelCount;
        public float Mean;
        public float P50;
        public float P95;
        public float P99;
        public float Max;
        public float OutlierThreshold;
        public int AbsoluteHotPixels;
        public int OutlierHotPixels;
        public int IsolatedHotPixels;
        public bool IsSuspicious;
    }
}
