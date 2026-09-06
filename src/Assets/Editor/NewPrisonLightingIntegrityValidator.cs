using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class NewPrisonLightingIntegrityValidator
{
    private const string TileModifiedFolder = "Assets/Prefabs/map_piece/NewPrison/Tile_modified";
    private const string TilesRotatedFolder = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated";
    private const string BakedDataFolder = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/BakedData";
    private const string ReportFolder = "Reports/NewPrisonLightingAudit";
    private const string ScreenshotFolder = "Screenshots/LightingAudit";
    private const string StartTileSetPath = "Assets/Prefabs/map_piece/NewPrison/New_Prison_StartTIle.asset";
    private const float MaxExpectedPower0L0 = 0.01f;
    private const int ThumbnailWidth = 320;
    private const int ThumbnailHeight = 200;
    private const int FocusThumbnailWidth = 360;
    private const int FocusThumbnailHeight = 240;

    private static readonly int[] Rotations = { 0, 90, 180, 270 };
    private static readonly string[] PowerSuffixes = { "P100", "P0" };

    [MenuItem("Tools/Dungeon/Lighting/Run NewPrison Full Integrity Report")]
    public static void RunFullIntegrityReportMenu()
    {
        Debug.Log(RunFullIntegrityReportCli());
    }

    [MenuItem("Tools/Dungeon/Lighting/Capture NewPrison Visual Audit")]
    public static void CaptureVisualAuditMenu()
    {
        Debug.Log(CaptureVisualAuditCli());
    }

    public static string RunFullIntegrityReportCli()
    {
        IntegrityReport report = BuildIntegrityReport();
        WriteReportFiles(report);
        return BuildCliSummary(report);
    }

    public static string CaptureVisualAuditCli()
    {
        IntegrityReport report = BuildIntegrityReport();
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string absoluteOutputFolder = GetAbsoluteProjectPath(ScreenshotFolder + "/" + stamp);
        Directory.CreateDirectory(absoluteOutputFolder);

        var images = new List<VisualAuditImage>();
        var originalLightmaps = LightmapSettings.lightmaps;
        var originalLightmapsMode = LightmapSettings.lightmapsMode;

        try
        {
            List<string> tileNames = FindTileModifiedNames();
            for (int i = 0; i < tileNames.Count; i++)
            {
                string tileName = tileNames[i];
                EditorUtility.DisplayProgressBar(
                    "NewPrison Lighting Visual Audit",
                    $"Capturing {tileName}",
                    tileNames.Count > 0 ? (float)i / tileNames.Count : 0f);

                Texture2D contactSheet = BuildTileContactSheet(tileName, report);
                string imagePath = SaveTexture(contactSheet, absoluteOutputFolder, $"{tileName}_contact.png");
                UnityEngine.Object.DestroyImmediate(contactSheet);

                images.Add(new VisualAuditImage
                {
                    kind = "overview",
                    tile = tileName,
                    path = ToProjectRelative(imagePath)
                });

                Texture2D focusSheet = BuildTileFocusSheet(tileName, report);
                if (focusSheet != null)
                {
                    string focusPath = SaveTexture(focusSheet, absoluteOutputFolder, $"{tileName}_focus.png");
                    UnityEngine.Object.DestroyImmediate(focusSheet);
                    images.Add(new VisualAuditImage
                    {
                        kind = "focus",
                        tile = tileName,
                        path = ToProjectRelative(focusPath)
                    });
                }
            }
        }
        finally
        {
            LightmapSettings.lightmaps = originalLightmaps;
            LightmapSettings.lightmapsMode = originalLightmapsMode;
            EditorUtility.ClearProgressBar();
        }

        report.visualAuditTimestamp = stamp;
        report.visualAuditFolder = ScreenshotFolder + "/" + stamp;
        report.visualImages = images;
        WriteReportFiles(report);

        return BuildCliSummary(report) +
            Environment.NewLine +
            $"visualAuditFolder={report.visualAuditFolder}" +
            Environment.NewLine +
            $"visualImages={images.Count}";
    }

    private static IntegrityReport BuildIntegrityReport()
    {
        var report = new IntegrityReport
        {
            generatedAt = DateTime.Now.ToString("O", CultureInfo.InvariantCulture)
        };

        List<string> tileNames = FindTileModifiedNames();
        report.tileModifiedCount = tileNames.Count;

        ValidateStartTileSet(report);

        foreach (string tileName in tileNames)
            ValidateTile(tileName, report);

        AppendExistingDiagnostics(report);
        FinalizeReport(report);
        return report;
    }

    private static void ValidateTile(string tileName, IntegrityReport report)
    {
        var tileReport = new TileIntegrityReport { tile = tileName };
        report.tiles.Add(tileReport);

        for (int r = 0; r < Rotations.Length; r++)
        {
            int rotation = Rotations[r];
            string variantName = $"{tileName}_R{rotation:000}";
            string prefabPath = $"{TilesRotatedFolder}/{variantName}.prefab";
            var variantReport = new VariantIntegrityReport
            {
                variant = variantName,
                prefabPath = prefabPath
            };
            tileReport.variants.Add(variantReport);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                AddFailure(report, tileReport, variantReport, $"{variantName}: missing rotated prefab");
                continue;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                ValidateVariantPrefab(root, tileReport, variantReport, report);

                DungeonTileBakeData p100 = ValidateBakeData(variantName, "P100", root, tileReport, variantReport, report);
                DungeonTileBakeData p0 = ValidateBakeData(variantName, "P0", root, tileReport, variantReport, report);
                ValidatePowerRelationship(p100, p0, tileReport, variantReport, report);
                ValidateP0ReflectionSuppression(root, tileReport, variantReport, report);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    private static void ValidateVariantPrefab(
        GameObject root,
        TileIntegrityReport tileReport,
        VariantIntegrityReport variantReport,
        IntegrityReport report)
    {
        var lights = root.GetComponentsInChildren<Light>(true);
        int nonBakedLights = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light != null && light.lightmapBakeType != LightmapBakeType.Baked)
                nonBakedLights++;
        }

        variantReport.lightCount = lights.Length;
        variantReport.nonBakedLights = nonBakedLights;
        if (nonBakedLights > 0)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: nonBakedLights={nonBakedLights}");

        var switcher = root.GetComponent<DungeonTileLightmapSwitcher>();
        var powerSet = root.GetComponent<DungeonTilePowerBakeSet>();
        variantReport.hasSwitcher = switcher != null;
        variantReport.hasPowerSet = powerSet != null;
        variantReport.emissionEntries = powerSet != null ? powerSet.EmissionMaterialEntries.Length : 0;

        if (switcher == null)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: missing DungeonTileLightmapSwitcher");
        if (powerSet == null)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: missing DungeonTilePowerBakeSet");

        variantReport.offMaterialReferences = CountOffVariantMaterials(root);
        if (variantReport.offMaterialReferences > 0)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: off material references={variantReport.offMaterialReferences}");

        variantReport.suppressP0ReflectionProbes = GetSerializedBool(switcher, "disableReflectionProbesOnPower0");
        if (!variantReport.suppressP0ReflectionProbes)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: P0 reflection probe suppression is disabled");

        ValidateDecalRules(root, tileReport, variantReport, report);
        ValidateReceptionRules(root, tileReport, variantReport, report);
    }

    private static DungeonTileBakeData ValidateBakeData(
        string variantName,
        string powerSuffix,
        GameObject root,
        TileIntegrityReport tileReport,
        VariantIntegrityReport variantReport,
        IntegrityReport report)
    {
        string path = BuildBakeDataAssetPath(variantName, powerSuffix);
        DungeonTileBakeData data = AssetDatabase.LoadAssetAtPath<DungeonTileBakeData>(path);
        var powerReport = new PowerIntegrityReport
        {
            power = powerSuffix,
            bakeDataPath = path
        };
        variantReport.powers.Add(powerReport);

        if (data == null)
        {
            AddFailure(report, tileReport, variantReport, $"{variantName}/{powerSuffix}: missing bake data");
            return null;
        }

        powerReport.lightmaps = SafeLength(data.lightmapColors);
        powerReport.lightmapDirections = SafeLength(data.lightmapDirections);
        powerReport.rendererEntries = SafeLength(data.rendererEntries);
        powerReport.reflectionProbeEntries = SafeLength(data.reflectionProbeEntries);
        powerReport.shProbes = SafeLength(data.lightProbeEntries);
        powerReport.avgL0 = CalculateAverageL0(data);
        powerReport.nullLightmapTextures = CountNulls(data.lightmapColors);
        powerReport.nullReflectionProbeTextures = CountNullReflectionProbeTextures(data);
        powerReport.unresolvedRendererEntries = CountUnresolvedRendererEntries(root.transform, data);
        powerReport.wallDecalRendererEntries = CountWallDecalRendererEntries(data);
        powerReport.floorDecalRendererEntries = CountFloorDecalRendererEntries(data);
        powerReport.receptionWindowVisualRendererEntries = CountReceptionWindowVisualRendererEntries(data);
        powerReport.floorSpeckle = AnalyzeLightmapHotPixels(data);

        if (powerReport.lightmaps <= 0)
            AddFailure(report, tileReport, variantReport, $"{variantName}/{powerSuffix}: lightmaps=0");
        if (powerReport.rendererEntries <= 0)
            AddFailure(report, tileReport, variantReport, $"{variantName}/{powerSuffix}: rendererEntries=0");
        if (powerReport.shProbes <= 0)
            AddFailure(report, tileReport, variantReport, $"{variantName}/{powerSuffix}: SH probes=0");
        if (powerReport.nullLightmapTextures > 0)
            AddFailure(report, tileReport, variantReport, $"{variantName}/{powerSuffix}: null lightmap textures={powerReport.nullLightmapTextures}");
        if (powerReport.nullReflectionProbeTextures > 0)
            AddFailure(report, tileReport, variantReport, $"{variantName}/{powerSuffix}: null reflection probe textures={powerReport.nullReflectionProbeTextures}");
        if (powerReport.unresolvedRendererEntries > 0)
            AddFailure(report, tileReport, variantReport, $"{variantName}/{powerSuffix}: unresolved renderer entries={powerReport.unresolvedRendererEntries}");
        if (powerReport.wallDecalRendererEntries > 0)
            AddFailure(report, tileReport, variantReport, $"{variantName}/{powerSuffix}: wall decal bake entries={powerReport.wallDecalRendererEntries}");
        if (powerReport.receptionWindowVisualRendererEntries > 0)
            AddFailure(report, tileReport, variantReport, $"{variantName}/{powerSuffix}: reception visual bake entries={powerReport.receptionWindowVisualRendererEntries}");
        if (variantReport.floorDecalRenderers > 0 && powerReport.floorDecalRendererEntries <= 0)
            AddFailure(report, tileReport, variantReport, $"{variantName}/{powerSuffix}: floor decals exist but no floor decal bake entries");
        if (powerSuffix == "P0" && powerReport.avgL0 > MaxExpectedPower0L0)
            AddFailure(report, tileReport, variantReport, $"{variantName}/{powerSuffix}: avgL0={powerReport.avgL0:0.0000} > {MaxExpectedPower0L0:0.0000}");

        if (powerReport.floorSpeckle.suspiciousHotPixels > 0)
        {
            AddWarning(
                report,
                tileReport,
                variantReport,
                $"{variantName}/{powerSuffix}: possible lightmap speckles isolated={powerReport.floorSpeckle.suspiciousHotPixels} max={powerReport.floorSpeckle.max:0.000}");
        }

        return data;
    }

    private static void ValidatePowerRelationship(
        DungeonTileBakeData p100,
        DungeonTileBakeData p0,
        TileIntegrityReport tileReport,
        VariantIntegrityReport variantReport,
        IntegrityReport report)
    {
        if (p100 == null || p0 == null)
            return;

        float p100L0 = CalculateAverageL0(p100);
        float p0L0 = CalculateAverageL0(p0);
        if (p100L0 <= p0L0)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: P100 avgL0={p100L0:0.0000} <= P0 avgL0={p0L0:0.0000}");
    }

    private static void ValidateP0ReflectionSuppression(
        GameObject root,
        TileIntegrityReport tileReport,
        VariantIntegrityReport variantReport,
        IntegrityReport report)
    {
        DungeonTileLightmapSwitcher switcher = root.GetComponent<DungeonTileLightmapSwitcher>();
        if (switcher == null)
            return;

        InvokeSwitcherAwake(switcher);
        switcher.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P0);

        int total = 0;
        int enabled = 0;
        ReflectionProbe[] probes = root.GetComponentsInChildren<ReflectionProbe>(true);
        for (int i = 0; i < probes.Length; i++)
        {
            if (probes[i] == null)
                continue;

            total++;
            if (probes[i].enabled)
                enabled++;
        }

        variantReport.p0ReflectionProbesTotal = total;
        variantReport.p0ReflectionProbesEnabled = enabled;

        if (enabled > 0)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: P0 enabled reflection probes={enabled}/{total}");
    }

    private static void ValidateDecalRules(
        GameObject root,
        TileIntegrityReport tileReport,
        VariantIntegrityReport variantReport,
        IntegrityReport report)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!NewPrisonDecalUtility.IsDecalRenderer(renderer))
                continue;

            variantReport.decalRenderers++;
            bool isFloor = NewPrisonDecalUtility.IsFloorDecalRenderer(renderer);
            bool hasContributeGi = HasStaticFlag(renderer.gameObject, StaticEditorFlags.ContributeGI);
            bool hasProbeReceiver = renderer.GetComponentInParent<DungeonDynamicProbeReceiver>(true) != null;

            if (isFloor)
            {
                variantReport.floorDecalRenderers++;
                if (!hasContributeGi)
                    variantReport.floorDecalMissingContributeGi++;
                if (hasProbeReceiver)
                    variantReport.floorDecalProbeReceivers++;
            }
            else
            {
                variantReport.wallDecalRenderers++;
                if (hasContributeGi)
                    variantReport.wallDecalContributeGi++;
                if (!hasProbeReceiver)
                    variantReport.wallDecalMissingProbeReceivers++;
            }

            if (renderer.receiveShadows)
                variantReport.decalReceiveShadows++;
            if (renderer.shadowCastingMode != ShadowCastingMode.Off)
                variantReport.decalShadowCasters++;
        }

        if (variantReport.floorDecalMissingContributeGi > 0)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: floor decals missing ContributeGI={variantReport.floorDecalMissingContributeGi}");
        if (variantReport.floorDecalProbeReceivers > 0)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: floor decals with SH receivers={variantReport.floorDecalProbeReceivers}");
        if (variantReport.wallDecalContributeGi > 0)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: wall decals with ContributeGI={variantReport.wallDecalContributeGi}");
        if (variantReport.wallDecalMissingProbeReceivers > 0)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: wall decals missing SH receivers={variantReport.wallDecalMissingProbeReceivers}");
        if (variantReport.decalReceiveShadows > 0)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: decals receive shadows={variantReport.decalReceiveShadows}");
        if (variantReport.decalShadowCasters > 0)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: decals casting shadows={variantReport.decalShadowCasters}");
    }

    private static void ValidateReceptionRules(
        GameObject root,
        TileIntegrityReport tileReport,
        VariantIntegrityReport variantReport,
        IntegrityReport report)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (NewPrisonReceptionWindowBakeSafeSetup.IsReceptionFrameStaticRenderer(renderer))
            {
                variantReport.receptionFrameRenderers++;
                if (!HasStaticFlag(renderer.gameObject, StaticEditorFlags.ContributeGI))
                    variantReport.receptionFrameMissingContributeGi++;
                continue;
            }

            if (!NewPrisonReceptionWindowBakeSafeSetup.IsReceptionWindowVisualRenderer(renderer))
                continue;

            variantReport.receptionWindowVisualRenderers++;
            if (HasStaticFlag(renderer.gameObject, StaticEditorFlags.ContributeGI))
                variantReport.receptionWindowVisualContributeGi++;
            if (renderer.receiveShadows)
                variantReport.receptionWindowVisualReceiveShadows++;
            if (renderer.shadowCastingMode != ShadowCastingMode.Off)
                variantReport.receptionWindowVisualCastShadows++;
            if (renderer.GetComponentInParent<DungeonDynamicProbeReceiver>(true) == null)
                variantReport.receptionWindowVisualMissingReceiver++;
            if (renderer.transform.Find("SH_SamplePoint") == null)
                variantReport.receptionWindowVisualMissingSamplePoint++;
        }

        if (variantReport.receptionFrameMissingContributeGi > 0)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: reception frame missing ContributeGI={variantReport.receptionFrameMissingContributeGi}");
        if (variantReport.receptionWindowVisualContributeGi > 0)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: reception visual ContributeGI={variantReport.receptionWindowVisualContributeGi}");
        if (variantReport.receptionWindowVisualReceiveShadows > 0)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: reception visual receiveShadows={variantReport.receptionWindowVisualReceiveShadows}");
        if (variantReport.receptionWindowVisualCastShadows > 0)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: reception visual casting shadows={variantReport.receptionWindowVisualCastShadows}");
        if (variantReport.receptionWindowVisualMissingReceiver > 0)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: reception visual missing SH receiver={variantReport.receptionWindowVisualMissingReceiver}");
        if (variantReport.receptionWindowVisualMissingSamplePoint > 0)
            AddFailure(report, tileReport, variantReport, $"{variantReport.variant}: reception visual missing sample point={variantReport.receptionWindowVisualMissingSamplePoint}");
    }

    private static void ValidateStartTileSet(IntegrityReport report)
    {
        TileSet tileSet = AssetDatabase.LoadAssetAtPath<TileSet>(StartTileSetPath);
        if (tileSet == null)
        {
            AddFailure(report, null, null, $"missing Start TileSet: {StartTileSetPath}");
            return;
        }

        report.startTileSetPath = StartTileSetPath;
        report.startTileSetCount = tileSet.TileWeights.Weights.Count;

        for (int i = 0; i < tileSet.TileWeights.Weights.Count; i++)
        {
            GameObject prefab = tileSet.TileWeights.Weights[i].Value;
            string prefabName = prefab != null ? prefab.name : "null";
            report.startTileSetEntries.Add(prefabName);

            if (prefab == null || !prefabName.StartsWith("StartRoom_R", StringComparison.OrdinalIgnoreCase))
                AddFailure(report, null, null, $"Start TileSet has non-StartRoom entry: {prefabName}");
        }

        if (report.startTileSetCount != 4)
            AddFailure(report, null, null, $"Start TileSet expected 4 StartRoom rotations, got {report.startTileSetCount}");
    }

    private static void AppendExistingDiagnostics(IntegrityReport report)
    {
        AppendDiagnostic(report, "NewPrisonDecalMaterialSetup.ReportAllDecals", NewPrisonDecalMaterialSetup.ReportAllDecals());
        AppendDiagnostic(report, "NewPrisonReceptionWindowBakeSafeSetup.Report", NewPrisonReceptionWindowBakeSafeSetup.ReportTileModifiedAndTilesReceptionWindows());
        AppendDiagnostic(report, "DungeonDoorProbeMeshSplitTool.ReportAllForCli", DungeonDoorProbeMeshSplitTool.ReportAllForCli());
        AppendDiagnostic(report, "NewPrisonLightingDiagnostics.VerifyStartRoomPower0ReflectionProbeSuppression", NewPrisonLightingDiagnostics.VerifyStartRoomPower0ReflectionProbeSuppression());
        AppendDiagnostic(report, "NewPrisonLightingDiagnostics.ReportCrossRoomFloorSpecklesCli", NewPrisonLightingDiagnostics.ReportFloorLightmapSpeckles("CrossRoom", false));
        AppendDiagnostic(report, "NewPrisonStartTileSetUtility.ReportStartTileSet", NewPrisonStartTileSetUtility.ReportStartTileSet());
    }

    private static void AppendDiagnostic(IntegrityReport report, string name, string text)
    {
        report.diagnostics.Add(new DiagnosticText
        {
            name = name,
            text = text ?? string.Empty
        });
    }

    private static void FinalizeReport(IntegrityReport report)
    {
        report.variantCount = report.tiles.Sum(tile => tile.variants.Count);
        report.powerBakeDataCount = report.tiles.Sum(tile => tile.variants.Sum(variant => variant.powers.Count));
        report.totalNonBakedLights = report.tiles.Sum(tile => tile.variants.Sum(variant => variant.nonBakedLights));
        report.totalWallDecalBakeEntries = report.tiles.Sum(tile => tile.variants.Sum(variant => variant.powers.Sum(power => power.wallDecalRendererEntries)));
        report.totalReceptionVisualBakeEntries = report.tiles.Sum(tile => tile.variants.Sum(variant => variant.powers.Sum(power => power.receptionWindowVisualRendererEntries)));
        report.totalShProbes = report.tiles.Sum(tile => tile.variants.Sum(variant => variant.powers.Sum(power => power.shProbes)));
        report.hardFailures = report.failures.Count;
        report.warnings = report.warningMessages.Count;
    }

    private static string BuildCliSummary(IntegrityReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[NewPrisonLightingIntegrityValidator] Full integrity report");
        sb.AppendLine($"generatedAt={report.generatedAt}");
        sb.AppendLine($"tiles={report.tileModifiedCount}, variants={report.variantCount}, bakeData={report.powerBakeDataCount}");
        sb.AppendLine($"hardFailures={report.hardFailures}, warnings={report.warnings}");
        sb.AppendLine($"totalNonBakedLights={report.totalNonBakedLights}, wallDecalBakeEntries={report.totalWallDecalBakeEntries}, receptionVisualBakeEntries={report.totalReceptionVisualBakeEntries}, totalShProbes={report.totalShProbes}");
        sb.AppendLine($"reportMarkdown={ReportFolder}/latest.md");
        sb.AppendLine($"reportJson={ReportFolder}/latest.json");

        if (report.failures.Count > 0)
        {
            sb.AppendLine("Failures:");
            for (int i = 0; i < Mathf.Min(25, report.failures.Count); i++)
                sb.AppendLine($"  - {report.failures[i].message}");
            if (report.failures.Count > 25)
                sb.AppendLine($"  ... {report.failures.Count - 25} more");
        }

        if (report.warningMessages.Count > 0)
        {
            sb.AppendLine("Warnings:");
            for (int i = 0; i < Mathf.Min(15, report.warningMessages.Count); i++)
                sb.AppendLine($"  - {report.warningMessages[i].message}");
            if (report.warningMessages.Count > 15)
                sb.AppendLine($"  ... {report.warningMessages.Count - 15} more");
        }

        return sb.ToString().TrimEnd();
    }

    private static void WriteReportFiles(IntegrityReport report)
    {
        string absoluteFolder = GetAbsoluteProjectPath(ReportFolder);
        Directory.CreateDirectory(absoluteFolder);

        string json = JsonUtility.ToJson(report, true);
        File.WriteAllText(Path.Combine(absoluteFolder, "latest.json"), json, Encoding.UTF8);
        File.WriteAllText(Path.Combine(absoluteFolder, "latest.md"), BuildMarkdown(report), Encoding.UTF8);
    }

    private static string BuildMarkdown(IntegrityReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# NewPrison Lighting Audit");
        sb.AppendLine();
        sb.AppendLine($"- Generated: `{report.generatedAt}`");
        sb.AppendLine($"- Tiles: `{report.tileModifiedCount}`");
        sb.AppendLine($"- Variants: `{report.variantCount}`");
        sb.AppendLine($"- Bake data assets: `{report.powerBakeDataCount}`");
        sb.AppendLine($"- Hard failures: `{report.hardFailures}`");
        sb.AppendLine($"- Warnings: `{report.warnings}`");
        if (!string.IsNullOrEmpty(report.visualAuditFolder))
            sb.AppendLine($"- Visual audit: `{report.visualAuditFolder}`");
        sb.AppendLine();

        sb.AppendLine("## Summary Table");
        sb.AppendLine();
        sb.AppendLine("| Tile | Variants | Non-baked lights | SH probes | P100 avgL0 | P0 avgL0 | Failures | Warnings |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (TileIntegrityReport tile in report.tiles)
        {
            List<float> p100 = new List<float>();
            List<float> p0 = new List<float>();
            int sh = 0;
            int nonBaked = 0;
            foreach (VariantIntegrityReport variant in tile.variants)
            {
                nonBaked += variant.nonBakedLights;
                foreach (PowerIntegrityReport power in variant.powers)
                {
                    sh += power.shProbes;
                    if (power.power == "P100") p100.Add(power.avgL0);
                    else if (power.power == "P0") p0.Add(power.avgL0);
                }
            }

            sb.AppendLine(
                $"| {tile.tile} | {tile.variants.Count} | {nonBaked} | {sh} | {AverageOrZero(p100):0.0000} | {AverageOrZero(p0):0.0000} | {tile.failures} | {tile.warnings} |");
        }

        if (report.visualImages.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Visual Audit Images");
            sb.AppendLine();
            sb.AppendLine("| Tile | Kind | Path |");
            sb.AppendLine("| --- | --- | --- |");
            foreach (VisualAuditImage image in report.visualImages)
                sb.AppendLine($"| {image.tile} | {image.kind} | `{image.path}` |");
        }

        if (report.failures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Hard Failures");
            sb.AppendLine();
            foreach (IntegrityIssue issue in report.failures)
                sb.AppendLine($"- `{issue.tile}` `{issue.variant}` {issue.message}");
        }

        if (report.warningMessages.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Warnings");
            sb.AppendLine();
            foreach (IntegrityIssue issue in report.warningMessages)
                sb.AppendLine($"- `{issue.tile}` `{issue.variant}` {issue.message}");
        }

        sb.AppendLine();
        sb.AppendLine("## Existing Diagnostics");
        foreach (DiagnosticText diagnostic in report.diagnostics)
        {
            sb.AppendLine();
            sb.AppendLine($"### {diagnostic.name}");
            sb.AppendLine();
            sb.AppendLine("```text");
            sb.AppendLine(diagnostic.text.TrimEnd());
            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    private static Texture2D BuildTileContactSheet(string tileName, IntegrityReport report)
    {
        int columns = Rotations.Length;
        int rows = PowerSuffixes.Length;
        Texture2D sheet = CreateSolidTexture(columns * ThumbnailWidth, rows * ThumbnailHeight, Color.black);

        for (int row = 0; row < rows; row++)
        {
            string power = PowerSuffixes[row];
            for (int col = 0; col < columns; col++)
            {
                int rotation = Rotations[col];
                string variantName = $"{tileName}_R{rotation:000}";
                string prefabPath = $"{TilesRotatedFolder}/{variantName}.prefab";
                string label = BuildThumbnailLabel(report, variantName, power);
                Texture2D thumbnail = RenderPrefabThumbnail(prefabPath, power, label, ThumbnailWidth, ThumbnailHeight, null);
                BlitTexture(sheet, thumbnail, col * ThumbnailWidth, (rows - row - 1) * ThumbnailHeight);
                UnityEngine.Object.DestroyImmediate(thumbnail);
            }
        }

        sheet.Apply(false);
        return sheet;
    }

    private static Texture2D BuildTileFocusSheet(string tileName, IntegrityReport report)
    {
        string[] focusTiles =
        {
            "AdminstrativeSegregation",
            "CorridorA",
            "CrossRoom",
            "StartRoom"
        };

        if (!focusTiles.Contains(tileName, StringComparer.OrdinalIgnoreCase))
            return null;

        string variantName = $"{tileName}_R000";
        string prefabPath = $"{TilesRotatedFolder}/{variantName}.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            return null;

        List<string> focusPaths = CollectFocusRendererPaths(prefabPath);
        if (focusPaths.Count == 0)
            return null;

        int columns = 2;
        int rows = Mathf.CeilToInt(focusPaths.Count / (float)columns) * PowerSuffixes.Length;
        Texture2D sheet = CreateSolidTexture(columns * FocusThumbnailWidth, rows * FocusThumbnailHeight, Color.black);

        int cell = 0;
        for (int i = 0; i < focusPaths.Count; i++)
        {
            for (int powerIndex = 0; powerIndex < PowerSuffixes.Length; powerIndex++)
            {
                string power = PowerSuffixes[powerIndex];
                string label = $"{variantName} {power}\n{focusPaths[i]}";
                Texture2D thumbnail = RenderPrefabThumbnail(
                    prefabPath,
                    power,
                    label,
                    FocusThumbnailWidth,
                    FocusThumbnailHeight,
                    focusPaths[i]);
                int col = cell % columns;
                int row = cell / columns;
                BlitTexture(sheet, thumbnail, col * FocusThumbnailWidth, (rows - row - 1) * FocusThumbnailHeight);
                UnityEngine.Object.DestroyImmediate(thumbnail);
                cell++;
            }
        }

        sheet.Apply(false);
        return sheet;
    }

    private static Texture2D RenderPrefabThumbnail(
        string prefabPath,
        string powerSuffix,
        string label,
        int width,
        int height,
        string focusRendererPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            return CreateErrorTexture(width, height, Color.red);

        Scene previewScene = EditorSceneManager.NewPreviewScene();
        RenderTexture renderTexture = null;
        Texture2D output = null;
        try
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(instance, previewScene);
            DungeonTileLightmapSwitcher switcher = instance.GetComponent<DungeonTileLightmapSwitcher>();
            if (switcher != null)
            {
                InvokeSwitcherAwake(switcher);
                switcher.SetPowerLevel(powerSuffix == "P0"
                    ? DungeonTileLightmapSwitcher.PowerLevel.P0
                    : DungeonTileLightmapSwitcher.PowerLevel.P100);
            }

            Bounds bounds = ResolveRenderBounds(instance, focusRendererPath);
            GameObject cameraObject = new GameObject("AuditCamera");
            SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
            var camera = cameraObject.AddComponent<Camera>();
            ConfigureAuditCamera(camera, bounds, width, height, focusRendererPath != null);
            AddSceneLabel(camera.transform, label, bounds);

            renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            renderTexture.Create();
            camera.targetTexture = renderTexture;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            output = new Texture2D(width, height, TextureFormat.RGBA32, false);
            output.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            output.Apply(false);
            RenderTexture.active = previous;
            return output;
        }
        finally
        {
            if (renderTexture != null)
            {
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }

            EditorSceneManager.ClosePreviewScene(previewScene);
        }
    }

    private static void ConfigureAuditCamera(Camera camera, Bounds bounds, int width, int height, bool focused)
    {
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z, 1f);
        float heightOffset = Mathf.Max(bounds.size.y + radius * 1.5f, 4f);
        Vector3 viewDirection = focused
            ? new Vector3(0.15f, -0.35f, -0.92f).normalized
            : new Vector3(0.25f, -0.55f, -0.80f).normalized;
        camera.transform.position = bounds.center - viewDirection * heightOffset;
        camera.transform.rotation = Quaternion.LookRotation(viewDirection, Vector3.up);
        camera.orthographic = true;
        camera.orthographicSize = Mathf.Max(1.2f, focused ? radius * 1.4f : Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.15f);
        camera.aspect = width / (float)height;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = Mathf.Max(100f, heightOffset * 4f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.02f, 0.02f, 0.025f, 1f);
        camera.allowHDR = false;
        camera.allowMSAA = false;
    }

    private static void AddSceneLabel(Transform cameraTransform, string label, Bounds bounds)
    {
        GameObject labelObject = new GameObject("AuditLabel");
        labelObject.transform.SetParent(cameraTransform, false);
        labelObject.transform.localPosition = new Vector3(-1.3f, 0.72f, 2f);
        labelObject.transform.localRotation = Quaternion.identity;
        var text = labelObject.AddComponent<TextMesh>();
        text.text = label;
        text.anchor = TextAnchor.UpperLeft;
        text.alignment = TextAlignment.Left;
        text.fontSize = 42;
        text.characterSize = 0.025f * Mathf.Max(1f, bounds.size.magnitude / 10f);
        text.color = Color.white;
    }

    private static Bounds ResolveRenderBounds(GameObject instance, string focusRendererPath)
    {
        if (!string.IsNullOrWhiteSpace(focusRendererPath))
        {
            Transform focus = FindRelativeTransform(instance.transform, focusRendererPath);
            if (focus != null)
            {
                Renderer focusRenderer = focus.GetComponent<Renderer>();
                if (focusRenderer != null)
                    return focusRenderer.bounds;
            }
        }

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds bounds = new Bounds(instance.transform.position, Vector3.one);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return bounds;
    }

    private static List<string> CollectFocusRendererPaths(string prefabPath)
    {
        var paths = new List<string>();
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                bool isFocus =
                    NewPrisonDecalUtility.IsDecalRenderer(renderer) ||
                    NewPrisonReceptionWindowBakeSafeSetup.IsReceptionWindowVisualRenderer(renderer) ||
                    NewPrisonReceptionWindowBakeSafeSetup.IsReceptionFrameStaticRenderer(renderer);
                if (!isFocus)
                    continue;

                string path = BuildRelativePath(root.transform, renderer.transform);
                if (!paths.Contains(path))
                    paths.Add(path);
                if (paths.Count >= 8)
                    break;
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return paths;
    }

    private static string BuildThumbnailLabel(IntegrityReport report, string variantName, string power)
    {
        PowerIntegrityReport powerReport = FindPowerReport(report, variantName, power);
        if (powerReport == null)
            return $"{variantName}\n{power}\nmissing data";

        return
            $"{variantName} {power}\n" +
            $"L0 {powerReport.avgL0:0.000} SH {powerReport.shProbes}\n" +
            $"hot {powerReport.floorSpeckle.suspiciousHotPixels}";
    }

    private static PowerIntegrityReport FindPowerReport(IntegrityReport report, string variantName, string power)
    {
        foreach (TileIntegrityReport tile in report.tiles)
        {
            foreach (VariantIntegrityReport variant in tile.variants)
            {
                if (!string.Equals(variant.variant, variantName, StringComparison.OrdinalIgnoreCase))
                    continue;

                return variant.powers.FirstOrDefault(item => item.power == power);
            }
        }

        return null;
    }

    private static FloorSpeckleStats AnalyzeLightmapHotPixels(DungeonTileBakeData data)
    {
        var stats = new FloorSpeckleStats();
        if (data == null || data.lightmapColors == null || data.lightmapColors.Length == 0)
            return stats;

        Texture2D source = data.lightmapColors.FirstOrDefault(texture => texture != null);
        if (source == null)
            return stats;

        Texture2D readable = null;
        try
        {
            readable = CreateReadableCopy(source, 256, 256);
            Color[] pixels = readable.GetPixels();
            if (pixels == null || pixels.Length == 0)
                return stats;

            var luminance = new float[pixels.Length];
            float sum = 0f;
            float max = 0f;
            for (int i = 0; i < pixels.Length; i++)
            {
                float value = CalculateLuminance(pixels[i]);
                luminance[i] = value;
                sum += value;
                if (value > max)
                    max = value;
            }

            Array.Sort(luminance);
            stats.mean = sum / pixels.Length;
            stats.p95 = Percentile(luminance, 0.95f);
            stats.p99 = Percentile(luminance, 0.99f);
            stats.max = max;
            float threshold = Mathf.Max(stats.p99 * 1.75f, stats.mean + 0.45f);
            for (int i = 0; i < luminance.Length; i++)
            {
                if (luminance[i] > threshold && luminance[i] > 0.65f)
                    stats.suspiciousHotPixels++;
            }
        }
        finally
        {
            if (readable != null)
                UnityEngine.Object.DestroyImmediate(readable);
        }

        return stats;
    }

    private static Texture2D CreateReadableCopy(Texture2D source, int width, int height)
    {
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        Graphics.Blit(source, rt);
        RenderTexture.active = rt;
        var readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        readable.Apply(false);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return readable;
    }

    private static Texture2D CreateSolidTexture(int width, int height, Color color)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = Enumerable.Repeat(color, width * height).ToArray();
        texture.SetPixels(pixels);
        texture.Apply(false);
        return texture;
    }

    private static Texture2D CreateErrorTexture(int width, int height, Color color)
    {
        return CreateSolidTexture(width, height, color);
    }

    private static void BlitTexture(Texture2D destination, Texture2D source, int x, int y)
    {
        if (destination == null || source == null)
            return;

        Color[] pixels = source.GetPixels();
        destination.SetPixels(x, y, source.width, source.height, pixels);
    }

    private static string SaveTexture(Texture2D texture, string absoluteFolder, string fileName)
    {
        Directory.CreateDirectory(absoluteFolder);
        string path = Path.Combine(absoluteFolder, fileName);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        return path;
    }

    private static List<string> FindTileModifiedNames()
    {
        return AssetDatabase.FindAssets("t:Prefab", new[] { TileModifiedFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrEmpty(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildBakeDataAssetPath(string variantName, string powerSuffix)
    {
        return $"{BakedDataFolder}/{variantName}/{powerSuffix}/{variantName}_BakeData.asset";
    }

    private static int CountUnresolvedRendererEntries(Transform root, DungeonTileBakeData data)
    {
        if (root == null || data == null || data.rendererEntries == null)
            return 0;

        int count = 0;
        for (int i = 0; i < data.rendererEntries.Length; i++)
        {
            string path = data.rendererEntries[i].relativePath;
            if (string.IsNullOrWhiteSpace(path) || FindRelativeTransform(root, path) == null)
                count++;
        }

        return count;
    }

    private static Transform FindRelativeTransform(Transform root, string relativePath)
    {
        if (root == null || string.IsNullOrWhiteSpace(relativePath))
            return null;

        if (string.Equals(root.name, relativePath, StringComparison.OrdinalIgnoreCase))
            return root;

        Transform direct = root.Find(relativePath);
        if (direct != null)
            return direct;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (string.Equals(BuildRelativePath(root, transforms[i]), relativePath, StringComparison.OrdinalIgnoreCase))
                return transforms[i];
        }

        return null;
    }

    private static string BuildRelativePath(Transform root, Transform transform)
    {
        if (root == null || transform == null || transform == root)
            return string.Empty;

        var stack = new Stack<string>();
        Transform cursor = transform;
        while (cursor != null && cursor != root)
        {
            stack.Push(cursor.name);
            cursor = cursor.parent;
        }

        return string.Join("/", stack.ToArray());
    }

    private static int CountOffVariantMaterials(GameObject root)
    {
        int count = 0;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] materials = renderers[i].sharedMaterials;
            for (int m = 0; m < materials.Length; m++)
            {
                Material material = materials[m];
                if (material == null)
                    continue;

                string path = AssetDatabase.GetAssetPath(material);
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (material.name.EndsWith("_Off", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith("_Off", StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountFloorDecalRendererEntries(DungeonTileBakeData data)
    {
        return CountRendererEntries(data, NewPrisonDecalUtility.IsFloorDecalRelativePath);
    }

    private static int CountWallDecalRendererEntries(DungeonTileBakeData data)
    {
        return CountRendererEntries(data, NewPrisonDecalUtility.IsWallDecalRelativePath);
    }

    private static int CountReceptionWindowVisualRendererEntries(DungeonTileBakeData data)
    {
        return CountRendererEntries(data, NewPrisonReceptionWindowBakeSafeSetup.IsReceptionWindowVisualRelativePath);
    }

    private static int CountRendererEntries(DungeonTileBakeData data, Func<string, bool> predicate)
    {
        if (data == null || data.rendererEntries == null)
            return 0;

        int count = 0;
        for (int i = 0; i < data.rendererEntries.Length; i++)
        {
            if (predicate(data.rendererEntries[i].relativePath))
                count++;
        }

        return count;
    }

    private static int CountNulls(UnityEngine.Object[] objects)
    {
        if (objects == null)
            return 0;

        int count = 0;
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] == null)
                count++;
        }

        return count;
    }

    private static int CountNullReflectionProbeTextures(DungeonTileBakeData data)
    {
        if (data == null || data.reflectionProbeEntries == null)
            return 0;

        int count = 0;
        for (int i = 0; i < data.reflectionProbeEntries.Length; i++)
        {
            if (data.reflectionProbeEntries[i].bakedTexture == null)
                count++;
        }

        return count;
    }

    private static float CalculateAverageL0(DungeonTileBakeData data)
    {
        if (data == null || data.lightProbeEntries == null || data.lightProbeEntries.Length == 0)
            return 0f;

        float total = 0f;
        for (int i = 0; i < data.lightProbeEntries.Length; i++)
            total += CalculateLuminance(data.lightProbeEntries[i].coefficient0);

        return total / data.lightProbeEntries.Length;
    }

    private static float CalculateLuminance(Vector3 value)
    {
        return value.x * 0.2126f + value.y * 0.7152f + value.z * 0.0722f;
    }

    private static float CalculateLuminance(Color value)
    {
        return value.r * 0.2126f + value.g * 0.7152f + value.b * 0.0722f;
    }

    private static float Percentile(float[] sortedValues, float percentile)
    {
        if (sortedValues == null || sortedValues.Length == 0)
            return 0f;

        int index = Mathf.Clamp(Mathf.RoundToInt((sortedValues.Length - 1) * percentile), 0, sortedValues.Length - 1);
        return sortedValues[index];
    }

    private static bool HasStaticFlag(GameObject gameObject, StaticEditorFlags flag)
    {
        return gameObject != null && (GameObjectUtility.GetStaticEditorFlags(gameObject) & flag) != 0;
    }

    private static bool GetSerializedBool(UnityEngine.Object target, string propertyName)
    {
        if (target == null)
            return false;

        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        return property != null && property.boolValue;
    }

    private static void InvokeSwitcherAwake(DungeonTileLightmapSwitcher switcher)
    {
        MethodInfo method = typeof(DungeonTileLightmapSwitcher).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        method?.Invoke(switcher, null);
    }

    private static int SafeLength<T>(T[] values)
    {
        return values != null ? values.Length : 0;
    }

    private static float AverageOrZero(List<float> values)
    {
        return values != null && values.Count > 0 ? values.Average() : 0f;
    }

    private static string GetAbsoluteProjectPath(string projectRelativePath)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, projectRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string ToProjectRelative(string absolutePath)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string full = Path.GetFullPath(absolutePath);
        if (!full.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            return absolutePath;

        return full.Substring(projectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
    }

    private static void AddFailure(
        IntegrityReport report,
        TileIntegrityReport tile,
        VariantIntegrityReport variant,
        string message)
    {
        report.failures.Add(new IntegrityIssue
        {
            tile = tile != null ? tile.tile : string.Empty,
            variant = variant != null ? variant.variant : string.Empty,
            message = message
        });

        if (tile != null)
            tile.failures++;
        if (variant != null)
            variant.failures++;
    }

    private static void AddWarning(
        IntegrityReport report,
        TileIntegrityReport tile,
        VariantIntegrityReport variant,
        string message)
    {
        report.warningMessages.Add(new IntegrityIssue
        {
            tile = tile != null ? tile.tile : string.Empty,
            variant = variant != null ? variant.variant : string.Empty,
            message = message
        });

        if (tile != null)
            tile.warnings++;
        if (variant != null)
            variant.warnings++;
    }

    [Serializable]
    public sealed class IntegrityReport
    {
        public string generatedAt;
        public int tileModifiedCount;
        public int variantCount;
        public int powerBakeDataCount;
        public int hardFailures;
        public int warnings;
        public int totalNonBakedLights;
        public int totalWallDecalBakeEntries;
        public int totalReceptionVisualBakeEntries;
        public int totalShProbes;
        public string startTileSetPath;
        public int startTileSetCount;
        public List<string> startTileSetEntries = new List<string>();
        public string visualAuditTimestamp;
        public string visualAuditFolder;
        public List<TileIntegrityReport> tiles = new List<TileIntegrityReport>();
        public List<IntegrityIssue> failures = new List<IntegrityIssue>();
        public List<IntegrityIssue> warningMessages = new List<IntegrityIssue>();
        public List<DiagnosticText> diagnostics = new List<DiagnosticText>();
        public List<VisualAuditImage> visualImages = new List<VisualAuditImage>();
    }

    [Serializable]
    public sealed class TileIntegrityReport
    {
        public string tile;
        public int failures;
        public int warnings;
        public List<VariantIntegrityReport> variants = new List<VariantIntegrityReport>();
    }

    [Serializable]
    public sealed class VariantIntegrityReport
    {
        public string variant;
        public string prefabPath;
        public int failures;
        public int warnings;
        public bool hasSwitcher;
        public bool hasPowerSet;
        public bool suppressP0ReflectionProbes;
        public int lightCount;
        public int nonBakedLights;
        public int emissionEntries;
        public int offMaterialReferences;
        public int p0ReflectionProbesTotal;
        public int p0ReflectionProbesEnabled;
        public int decalRenderers;
        public int floorDecalRenderers;
        public int wallDecalRenderers;
        public int floorDecalMissingContributeGi;
        public int floorDecalProbeReceivers;
        public int wallDecalContributeGi;
        public int wallDecalMissingProbeReceivers;
        public int decalReceiveShadows;
        public int decalShadowCasters;
        public int receptionFrameRenderers;
        public int receptionFrameMissingContributeGi;
        public int receptionWindowVisualRenderers;
        public int receptionWindowVisualContributeGi;
        public int receptionWindowVisualReceiveShadows;
        public int receptionWindowVisualCastShadows;
        public int receptionWindowVisualMissingReceiver;
        public int receptionWindowVisualMissingSamplePoint;
        public List<PowerIntegrityReport> powers = new List<PowerIntegrityReport>();
    }

    [Serializable]
    public sealed class PowerIntegrityReport
    {
        public string power;
        public string bakeDataPath;
        public int lightmaps;
        public int lightmapDirections;
        public int rendererEntries;
        public int reflectionProbeEntries;
        public int shProbes;
        public float avgL0;
        public int nullLightmapTextures;
        public int nullReflectionProbeTextures;
        public int unresolvedRendererEntries;
        public int wallDecalRendererEntries;
        public int floorDecalRendererEntries;
        public int receptionWindowVisualRendererEntries;
        public FloorSpeckleStats floorSpeckle;
    }

    [Serializable]
    public struct FloorSpeckleStats
    {
        public float mean;
        public float p95;
        public float p99;
        public float max;
        public int suspiciousHotPixels;
    }

    [Serializable]
    public sealed class IntegrityIssue
    {
        public string tile;
        public string variant;
        public string message;
    }

    [Serializable]
    public sealed class DiagnosticText
    {
        public string name;
        public string text;
    }

    [Serializable]
    public sealed class VisualAuditImage
    {
        public string kind;
        public string tile;
        public string path;
    }
}

public static class NewPrisonLightingRuntimeSmoke
{
    public static string RunCli()
    {
        var report = new StringBuilder();
        report.AppendLine("[NewPrisonLightingRuntimeSmoke]");

        if (!Application.isPlaying)
        {
            report.AppendLine("hardFailures=1");
            report.AppendLine("Run this command in Play Mode.");
            return report.ToString().TrimEnd();
        }

        int hardFailures = 0;
        NetworkDungeonController controller =
            UnityEngine.Object.FindFirstObjectByType<NetworkDungeonController>(FindObjectsInactive.Include);
        RuntimeDungeon runtimeDungeon = ResolveRuntimeDungeon(controller);
        DungeonTileProbeRegistry registry = DungeonTileProbeRegistry.Active ??
            UnityEngine.Object.FindFirstObjectByType<DungeonTileProbeRegistry>(FindObjectsInactive.Include);

        if (runtimeDungeon == null)
        {
            hardFailures++;
            report.AppendLine("missing RuntimeDungeon");
        }

        if (registry == null)
        {
            hardFailures++;
            report.AppendLine("missing DungeonTileProbeRegistry");
        }

        if (runtimeDungeon != null && registry != null)
        {
            registry.Configure(runtimeDungeon);

            DunGen.DungeonGenerator generator = runtimeDungeon.Generator;
            DunGen.Dungeon dungeon = generator != null ? generator.CurrentDungeon : null;
            if (generator != null && (dungeon == null || dungeon.AllTiles == null || dungeon.AllTiles.Count == 0))
            {
                EnsureGeneratedDungeon(runtimeDungeon, controller, report);
                dungeon = generator.CurrentDungeon;
            }

            if (dungeon != null && dungeon.AllTiles != null && dungeon.AllTiles.Count > 0)
                registry.RebuildFromGenerator(generator);

            report.AppendLine($"registryTileSets={registry.TileSetCount}");
            report.AppendLine($"doorwayBlendZones={registry.DoorwayBlendZoneCount}");
            report.AppendLine($"spatialBlendEnabled={registry.SpatialBlendEnabled}");

            if (registry.TileSetCount <= 0)
                hardFailures++;

            hardFailures += AppendReceiverSummary(report, registry);
            hardFailures += AppendDoorBlendSamples(report, runtimeDungeon, registry);
        }

        report.Insert(0, $"hardFailures={hardFailures}\n");
        return report.ToString().TrimEnd();
    }

    private static RuntimeDungeon ResolveRuntimeDungeon(NetworkDungeonController controller)
    {
        RuntimeDungeon runtimeDungeon = null;
        if (controller != null)
        {
            FieldInfo field = typeof(NetworkDungeonController).GetField(
                "runtimeDungeon",
                BindingFlags.Instance | BindingFlags.NonPublic);
            runtimeDungeon = field != null ? field.GetValue(controller) as RuntimeDungeon : null;

            if (runtimeDungeon == null)
                runtimeDungeon = controller.GetComponent<RuntimeDungeon>();
        }

        return runtimeDungeon != null
            ? runtimeDungeon
            : UnityEngine.Object.FindFirstObjectByType<RuntimeDungeon>(FindObjectsInactive.Include);
    }

    private static void EnsureGeneratedDungeon(
        RuntimeDungeon runtimeDungeon,
        NetworkDungeonController controller,
        StringBuilder report)
    {
        DunGen.DungeonGenerator generator = runtimeDungeon.Generator;
        if (generator == null)
            return;

        if (generator.DungeonFlow == null)
            TryAssignFlowFromController(generator, controller, report);

        if (generator.DungeonFlow == null)
        {
            report.AppendLine("generationSkipped=true reason=missing DungeonFlow");
            return;
        }

        List<BehaviourEnabledState> disabledNavMeshAdapters = DisableNavMeshAdapters(runtimeDungeon);
        try
        {
            generator.Clear(stopCoroutines: true);
            generator.GenerateAsynchronously = false;
            generator.ShouldRandomizeSeed = false;
            generator.Seed = 314159;
            runtimeDungeon.Generate();
        }
        finally
        {
            RestoreBehaviours(disabledNavMeshAdapters);
        }

        int tileCount = generator.CurrentDungeon != null && generator.CurrentDungeon.AllTiles != null
            ? generator.CurrentDungeon.AllTiles.Count
            : 0;

        report.AppendLine(
            $"generationRequested=true seed=314159 status={generator.Status} tiles={tileCount} " +
            $"disabledNavMeshAdapters={disabledNavMeshAdapters.Count}");
    }

    private static List<BehaviourEnabledState> DisableNavMeshAdapters(RuntimeDungeon runtimeDungeon)
    {
        var states = new List<BehaviourEnabledState>();
        if (runtimeDungeon == null)
            return states;

        Behaviour[] behaviours = runtimeDungeon.GetComponents<Behaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            Behaviour behaviour = behaviours[i];
            if (behaviour == null || !behaviour.enabled)
                continue;

            Type type = behaviour.GetType();
            string fullName = type.FullName ?? type.Name;
            if (!fullName.Contains("DunGen.Adapters", StringComparison.Ordinal) ||
                !type.Name.Contains("NavMesh", StringComparison.Ordinal))
            {
                continue;
            }

            states.Add(new BehaviourEnabledState { behaviour = behaviour, wasEnabled = true });
            behaviour.enabled = false;
        }

        return states;
    }

    private static void RestoreBehaviours(List<BehaviourEnabledState> states)
    {
        for (int i = 0; i < states.Count; i++)
        {
            Behaviour behaviour = states[i].behaviour;
            if (behaviour != null)
                behaviour.enabled = states[i].wasEnabled;
        }
    }

    private static void TryAssignFlowFromController(
        DunGen.DungeonGenerator generator,
        NetworkDungeonController controller,
        StringBuilder report)
    {
        DungeonMapList mapList = controller != null ? controller.MapList : null;
        int flowIndex = controller != null && controller.CurrentFlowIndex >= 0
            ? controller.CurrentFlowIndex
            : 0;

        if (mapList == null || !mapList.TryGetFlow(flowIndex, out var flow))
        {
            report.AppendLine($"flowResolved=false flowIndex={flowIndex}");
            return;
        }

        generator.DungeonFlow = flow;
        report.AppendLine($"flowResolved=true flowIndex={flowIndex} flow={flow.name}");
    }

    private static int AppendReceiverSummary(StringBuilder report, DungeonTileProbeRegistry registry)
    {
        DungeonDynamicProbeReceiver[] dynamicReceivers =
            UnityEngine.Object.FindObjectsByType<DungeonDynamicProbeReceiver>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        DungeonDoorDualSideProbeReceiver[] doorReceivers =
            UnityEngine.Object.FindObjectsByType<DungeonDoorDualSideProbeReceiver>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        int customProvidedRenderers = 0;
        Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].lightProbeUsage == LightProbeUsage.CustomProvided)
                customProvidedRenderers++;
        }

        report.AppendLine($"dynamicReceivers={dynamicReceivers.Length}");
        report.AppendLine($"doorDualSideReceivers={doorReceivers.Length}");
        report.AppendLine($"customProvidedRenderers={customProvidedRenderers}");

        int hardFailures = 0;
        if (dynamicReceivers.Length <= 0)
            hardFailures++;
        if (doorReceivers.Length <= 0)
            hardFailures++;
        if (customProvidedRenderers <= 0)
            hardFailures++;

        if (dynamicReceivers.Length > 0)
        {
            DungeonDynamicProbeReceiver receiver = dynamicReceivers[0];
            receiver.ForceRefresh();
            report.AppendLine("firstDynamicSample=" + registry.BuildSampleReport(receiver.CurrentSamplePosition));
        }

        if (doorReceivers.Length > 0)
        {
            doorReceivers[0].ForceRefresh();
            report.AppendLine("firstDoor=" + doorReceivers[0].BuildDiagnostics());
        }

        return hardFailures;
    }

    private static int AppendDoorBlendSamples(
        StringBuilder report,
        RuntimeDungeon runtimeDungeon,
        DungeonTileProbeRegistry registry)
    {
        DunGen.DungeonGenerator generator = runtimeDungeon != null ? runtimeDungeon.Generator : null;
        if (generator == null || generator.Root == null)
        {
            report.AppendLine("doorBlendSamples=0 no generator root");
            return 1;
        }

        DunGen.Door[] doors = generator.Root.GetComponentsInChildren<DunGen.Door>(true);
        int samples = 0;
        for (int i = 0; i < doors.Length && samples < 3; i++)
        {
            DunGen.Door door = doors[i];
            if (door == null || door.TileA == null || door.TileB == null)
                continue;

            Vector3 center = door.transform.position;
            Vector3 axis = (door.TileB.transform.position - door.TileA.transform.position).normalized;
            if (axis.sqrMagnitude < 0.01f)
                axis = door.transform.forward;

            report.AppendLine($"doorSample[{samples}].center=" + registry.BuildSampleReport(center));
            report.AppendLine($"doorSample[{samples}].aSide=" + registry.BuildSampleReport(center - axis * 0.8f));
            report.AppendLine($"doorSample[{samples}].bSide=" + registry.BuildSampleReport(center + axis * 0.8f));
            samples++;
        }

        report.AppendLine($"doorBlendSamples={samples}");
        return samples > 0 ? 0 : 1;
    }

    private sealed class BehaviourEnabledState
    {
        public Behaviour behaviour;
        public bool wasEnabled;
    }
}
