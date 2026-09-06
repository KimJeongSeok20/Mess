using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public sealed class DungeonTileRotationBakeTool : EditorWindow
{
    private enum PowerBakeLevel
    {
        P100,
        P00
    }

    [Serializable]
    private sealed class EmissionMaterialVariantSet
    {
        public string label = "Material";
        public bool isExpanded;
        public Material sourceMaterial;
        public Material power100Material;
        public Material power00Material;
    }

    [Serializable]
    private sealed class EmissionMaterialVariantSetSaveData
    {
        public string label;
        public string sourceMaterialPath;
        public string power100MaterialPath;
        public string power00MaterialPath;
    }

    [Serializable]
    private sealed class EmissionMaterialVariantSetSaveCollection
    {
        public List<EmissionMaterialVariantSetSaveData> entries = new List<EmissionMaterialVariantSetSaveData>();
    }

    [Serializable]
    private sealed class ToolChecklistSaveData
    {
        public bool useSelectedPrefabs;
        public bool collectBakedFilesToFolder;
        public bool bakedFilesUnderOutputFolder;
        public bool setAllowRotationFalse;
        public bool removeNavMeshLinksFromVariants;
        public bool buildBakeScene;
        public bool runBakeAfterBuild;
        public bool prepareDoorwayHoleWallsForBake;
        public bool useFixedBakeScenePath;
        public bool createReflectionProbeIfMissing;
        public bool autoCreatePowerProbeVariants;
        public bool fitReflectionProbesBeforeBake;
        public bool autoSegmentReflectionProbes;
        public float segmentOnlyWhenLongestAxisOver;
        public float targetProbeSegmentLength;
        public int maxReflectionProbeSegments;
        public float reflectionProbeSegmentOverlap;
        public bool autoNormalizeNewPrisonRoomsBeforeBake;
        public bool bakeLightProbeData;
        public float lightProbeGridSpacing;
        public float lightProbeWallInset;
        public int maxLightProbeSamples;
        public bool bakePower100;
        public bool bakePower00;
        public bool overrideBakeAmbient;
    }

    private readonly struct RuntimeEmissionEntry
    {
        public readonly string relativePath;
        public readonly int rendererBucketIndex;
        public readonly int materialIndex;
        public readonly Material power100Material;
        public readonly Material power00Material;

        public RuntimeEmissionEntry(
            string relativePath,
            int rendererBucketIndex,
            int materialIndex,
            Material power100Material,
            Material power00Material)
        {
            this.relativePath = relativePath;
            this.rendererBucketIndex = rendererBucketIndex;
            this.materialIndex = materialIndex;
            this.power100Material = power100Material;
            this.power00Material = power00Material;
        }
    }

    private sealed class ReflectionProbeTextureSet
    {
        public Cubemap power100Texture;
        public Cubemap power00Texture;
    }

    private readonly struct RuntimeReflectionProbeVariantEntry
    {
        public readonly string relativePath;
        public readonly int probeBucketIndex;
        public readonly ReflectionProbe power100Probe;
        public readonly ReflectionProbe power00Probe;

        public RuntimeReflectionProbeVariantEntry(
            string relativePath,
            int probeBucketIndex,
            ReflectionProbe power100Probe,
            ReflectionProbe power00Probe)
        {
            this.relativePath = relativePath;
            this.probeBucketIndex = probeBucketIndex;
            this.power100Probe = power100Probe;
            this.power00Probe = power00Probe;
        }
    }

    private sealed class FitPowerProbeGroup
    {
        public string normalizedPath;
        public string representativeRelativePath;
        public ReflectionProbe power100Probe;
        public ReflectionProbe power00Probe;
    }

    private readonly struct ReflectionProbeFitSegment
    {
        public readonly Vector3 center;
        public readonly Vector3 size;

        public ReflectionProbeFitSegment(Vector3 center, Vector3 size)
        {
            this.center = center;
            this.size = size;
        }
    }

    private readonly struct PowerBakeOption
    {
        public readonly PowerBakeLevel level;
        public readonly string suffix;
        public readonly float intensityScale;

        public PowerBakeOption(PowerBakeLevel level, string suffix, float intensityScale)
        {
            this.level = level;
            this.suffix = suffix;
            this.intensityScale = intensityScale;
        }
    }

    private sealed class PowerBakeDataSet
    {
        public string p100;
        public string p00;

        public void Set(PowerBakeLevel level, string assetPath)
        {
            switch (level)
            {
                case PowerBakeLevel.P100:
                    p100 = assetPath;
                    break;
                default:
                    p00 = assetPath;
                    break;
            }
        }

        public string Get(PowerBakeLevel level)
        {
            switch (level)
            {
                case PowerBakeLevel.P100:
                    return p100;
                default:
                    return p00;
            }
        }

        public int CountAssigned()
        {
            int count = 0;
            if (!string.IsNullOrWhiteSpace(p100)) count++;
            if (!string.IsNullOrWhiteSpace(p00)) count++;
            return count;
        }
    }

    private struct PlacedPrefabInfo
    {
        public string prefabPath;
        public GameObject instance;

        public PlacedPrefabInfo(string prefabPath, GameObject instance)
        {
            this.prefabPath = prefabPath;
            this.instance = instance;
        }
    }

    private struct ReflectionProbeFitResult
    {
        public int updatedPrefabs;
        public int skippedPrefabs;
        public int createdProbes;
        public int removedProbes;
    }

    private const string WindowTitle = "Tile Rotation Bake";
    private const string AutoBakeRootName = "AutoBakeRoot";
    private const string NewPrisonTileModifiedFolder = "Assets/Prefabs/map_piece/NewPrison/Tile_modified";
    private const string NewPrisonTilesFolder = "Assets/Prefabs/map_piece/NewPrison/Tiles";
    private const string NewPrisonTilesRotatedFolder = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated";
    private const string AdminstrativeSegregationSourcePrefab =
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/AdminstrativeSegregation.prefab";
    private static readonly string[] RequestedNewPrisonSourcePrefabs =
    {
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/Cafeteria.prefab",
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/Conference_Room.prefab",
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/CorridorA.prefab",
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/CrossRoom.prefab"
    };

    private static readonly string[] RequestedNewPrisonPrefabNames =
    {
        "Cafeteria",
        "Conference_Room",
        "CorridorA",
        "CrossRoom"
    };

    private static readonly string[] DecalAndLampFixSourcePrefabs =
    {
        AdminstrativeSegregationSourcePrefab,
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/CorridorA.prefab",
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/CrossRoom.prefab"
    };

    private static readonly string[] DecalAndLampFixPrefabNames =
    {
        "AdminstrativeSegregation",
        "CorridorA",
        "CrossRoom"
    };

    private static readonly string[] NewPrisonDecalRoomSourcePrefabs =
    {
        AdminstrativeSegregationSourcePrefab,
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/Cafeteria.prefab",
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/CorridorA.prefab",
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/CrossRoom.prefab"
    };

    private static readonly string[] NewPrisonDecalRoomPrefabNames =
    {
        "AdminstrativeSegregation",
        "Cafeteria",
        "CorridorA",
        "CrossRoom"
    };

    private static readonly string[] CorridorADecalSourcePrefabs =
    {
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/CorridorA.prefab"
    };

    private static readonly string[] CorridorADecalPrefabNames =
    {
        "CorridorA"
    };

    private static readonly string[] ReceptionWindowSourcePrefabs =
    {
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/CorridorA.prefab",
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/StartRoom.prefab"
    };

    private static readonly string[] ReceptionWindowPrefabNames =
    {
        "CorridorA",
        "StartRoom"
    };

    private static readonly string[] CafeteriaDecalSourcePrefabs =
    {
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/Cafeteria.prefab"
    };

    private static readonly string[] CafeteriaDecalPrefabNames =
    {
        "Cafeteria"
    };

    private static readonly string[] CrossRoomDecalSourcePrefabs =
    {
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/CrossRoom.prefab"
    };

    private static readonly string[] CrossRoomDecalPrefabNames =
    {
        "CrossRoom"
    };

    private const string GeneratedReflectionProbeVariantNamePrefix = "__AutoProbeVariant_";
    private const string GeneratedLightProbeGroupName = "__TileSHLightProbeGroup";
    private static readonly string[] FitPowerProbeSuffixes = { "P100", "P0" };
    private static readonly (string label, string sourcePath, string power100Path, string power00Path)[] DefaultEmissionMaterialVariants =
    {
        (
            "KriptoFX Source Lamps 01",
            "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Source/Materials/Lamps_01.mat",
            "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_01_P100.mat",
            "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_01_P0_Black.mat"
        ),
        (
            "NewPrison Lamps 02",
            "Assets/Prefabs/map_piece/NewPrison/Power_Material/Lamps_02.mat",
            "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_02_P100.mat",
            "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_02_P0_Black.mat"
        ),
        (
            "KriptoFX Source Lamps 05",
            "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Source/Materials/Lamps_05.mat",
            "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_05_P100.mat",
            "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_05_P0_Black.mat"
        ),
        (
            "NewPrison Lamps 05",
            "Assets/Prefabs/map_piece/NewPrison/Power_Material/Lamps_05.mat",
            "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_05_P100.mat",
            "Assets/Prefabs/map_piece/NewPrison/EMISSION_MATERIAL/Lamps_05_P0_Black.mat"
        )
    };
    private const string EmissionVariantsEditorPrefsKey = "DungeonTileRotationBakeTool.EmissionMaterialVariants";
    private const string ToolChecklistEditorPrefsKey = "DungeonTileRotationBakeTool.ChecklistState";
    private const int ReflectionProbeApplyModeCustomFromBakeData = 0;
    private const int ReflectionProbeApplyModeProbeVariantSet = 2;

    [SerializeField] private bool useSelectedPrefabs = true;
    [SerializeField] private string sourceFolder = "Assets/Prefabs/map_piece/NewPrison/Tiles";
    [SerializeField] private string outputFolder = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated";
    [SerializeField] private string bakeScenePath = "Assets/SceneTemplateAssets/Scenes/TileBake_Auto.unity";
    [SerializeField] private bool collectBakedFilesToFolder = true;
    [SerializeField] private bool bakedFilesUnderOutputFolder = true;
    [SerializeField] private string bakedFilesFolder = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/BakedData";
    [SerializeField] private bool setAllowRotationFalse = true;
    [SerializeField] private bool removeNavMeshLinksFromVariants = true;
    [SerializeField] private bool buildBakeScene = true;
    [SerializeField] private bool runBakeAfterBuild = true;
    [SerializeField] private bool prepareDoorwayHoleWallsForBake = true;
    [SerializeField] private bool useFixedBakeScenePath = false;
    [SerializeField] private bool createReflectionProbeIfMissing = true;
    [SerializeField] private bool autoCreatePowerProbeVariants = true;
    [SerializeField] private string newReflectionProbeName = "Tile Reflection Probe";
    [SerializeField] private bool fitReflectionProbesBeforeBake = true;
    [SerializeField] private bool autoSegmentReflectionProbes = true;
    [SerializeField] private float segmentOnlyWhenLongestAxisOver = 28f;
    [SerializeField] private float targetProbeSegmentLength = 16f;
    [SerializeField] private int maxReflectionProbeSegments = 4;
    [SerializeField] private float reflectionProbeSegmentOverlap = 1f;

    [Header("NewPrison Authoring Normalize")]
    [SerializeField] private bool autoNormalizeNewPrisonRoomsBeforeBake = true;

    [Header("Dynamic Object SH Probes")]
    [SerializeField] private bool bakeLightProbeData = true;
    [SerializeField] private float lightProbeGridSpacing = 2.5f;
    [SerializeField] private float lightProbeWallInset = 0.75f;
    [SerializeField] private int maxLightProbeSamples = 128;

    [Header("Power Bake Levels")]
    [SerializeField] private bool bakePower100 = true;
    [SerializeField] private bool bakePower00 = true;

    [Header("Bake Quality Override")]
    [SerializeField] private bool overrideBakeQuality;
    [SerializeField, Min(1)] private int bakeDirectSampleCount = 256;
    [SerializeField, Min(1)] private int bakeIndirectSampleCount = 1024;
    [SerializeField, Min(1)] private int bakeEnvironmentSampleCount = 256;
    [SerializeField, Min(0)] private int bakeBounces = 2;
    [SerializeField, Min(2)] private int bakePadding = 6;
    [SerializeField] private bool bakeTextureCompression;

    [Header("Emission Material Variants")]
    [SerializeField] private List<EmissionMaterialVariantSet> emissionMaterialVariants = new List<EmissionMaterialVariantSet>();

    [Header("Bake Scene Lighting")]
    [SerializeField] private bool overrideBakeAmbient = true;
    [SerializeField] private Color bakeAmbientColor = new Color(0f, 0f, 0f, 1f);
    [SerializeField] private float bakeAmbientIntensity = 0.9f;

    private readonly int[] rotationsY = { 0, 90, 180, 270 };
    private int[] cliRotationOverride;
    private Vector2 mainScrollPosition;

    private bool emissionVariantsLoaded;
    private bool isLoadingEmissionVariants;
    private bool checklistLoaded;
    private bool isLoadingChecklist;

    private static void OpenWindow()
    {
        var window = GetWindow<DungeonTileRotationBakeTool>(WindowTitle);
        window.minSize = new Vector2(520f, 520f);
        window.Show();
    }

    public static string BakeAdminstrativeSegregationLightingCli()
    {
        return BakeNewPrisonTileModifiedLightingCli(
            "AdminstrativeSegregation",
            new[] { AdminstrativeSegregationSourcePrefab },
            ReportAdminstrativeSegregationLightingCli);
    }

    public static string BakeAdminstrativeSegregationR180R270LightingCli()
    {
        return BakeNewPrisonTileModifiedLightingCli(
            "AdminstrativeSegregation R180/R270",
            new[] { AdminstrativeSegregationSourcePrefab },
            ReportAdminstrativeSegregationLightingCli,
            new[] { 180, 270 });
    }

    public static string BakeRequestedNewPrisonRoomsLightingCli()
    {
        return BakeNewPrisonTileModifiedLightingCli(
            "Cafeteria + Conference_Room + CorridorA + CrossRoom",
            RequestedNewPrisonSourcePrefabs,
            ReportRequestedNewPrisonRoomsLightingCli);
    }

    public static string BakeDecalAndLampFixRoomsLightingCli()
    {
        return BakeNewPrisonTileModifiedLightingCli(
            "AdminstrativeSegregation + CorridorA + CrossRoom",
            DecalAndLampFixSourcePrefabs,
            ReportDecalAndLampFixRoomsLightingCli);
    }

    public static string BakeNewPrisonDecalRoomsLightingCli()
    {
        return BakeNewPrisonTileModifiedLightingCli(
            "NewPrison decal rooms",
            NewPrisonDecalRoomSourcePrefabs,
            ReportNewPrisonDecalRoomsLightingCli);
    }

    public static string BakeCorridorADecalLightingCli()
    {
        return BakeNewPrisonTileModifiedLightingCli(
            "CorridorA decal",
            CorridorADecalSourcePrefabs,
            ReportCorridorADecalLightingCli);
    }

    public static string BakeCafeteriaDecalLightingCli()
    {
        return BakeNewPrisonTileModifiedLightingCli(
            "Cafeteria decal",
            CafeteriaDecalSourcePrefabs,
            ReportCafeteriaDecalLightingCli);
    }

    public static string BakeCafeteriaR270DecalLightingCli()
    {
        return BakeNewPrisonTileModifiedLightingCli(
            "Cafeteria R270 decal",
            CafeteriaDecalSourcePrefabs,
            ReportCafeteriaDecalLightingCli,
            new[] { 270 });
    }

    public static string BakeCrossRoomDecalLightingCli()
    {
        return BakeNewPrisonTileModifiedLightingCli(
            "CrossRoom decal",
            CrossRoomDecalSourcePrefabs,
            ReportCrossRoomDecalLightingCli);
    }

    public static string BakeReceptionWindowLightingCli()
    {
        return BakeNewPrisonTileModifiedLightingCli(
            "Reception window bake-safe",
            ReceptionWindowSourcePrefabs,
            ReportReceptionWindowLightingCli);
    }

    public static string BakeMissingNewPrisonTileModifiedLightingCli()
    {
        var missingSourcePrefabs = FindTileModifiedPrefabsMissingRotationBake();
        if (missingSourcePrefabs.Count == 0)
        {
            return "[DungeonTileRotationBakeTool] Missing NewPrison Tile_modified lighting bake\nNo missing Tile_modified rotation bake outputs were found.";
        }

        string[] prefabNames = missingSourcePrefabs
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .ToArray();

        return BakeNewPrisonTileModifiedLightingCli(
            "Missing NewPrison Tile_modified rooms",
            missingSourcePrefabs,
            () => ReportNewPrisonLightingCli("Missing NewPrison Tile_modified rooms", prefabNames));
    }

    private static List<string> FindTileModifiedPrefabsMissingRotationBake()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { NewPrisonTileModifiedFolder });
        var missing = new List<string>();
        foreach (string guid in guids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(prefabPath) || !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                continue;

            string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
            if (!HasCompleteRotationBakeOutput(prefabName))
                missing.Add(prefabPath);
        }

        missing.Sort(StringComparer.OrdinalIgnoreCase);
        return missing;
    }

    private static bool HasCompleteRotationBakeOutput(string prefabName)
    {
        if (string.IsNullOrWhiteSpace(prefabName))
            return false;

        foreach (int rotation in new[] { 0, 90, 180, 270 })
        {
            string variantName = $"{prefabName}_R{rotation:D3}";
            string rotatedPrefabPath = $"{NewPrisonTilesRotatedFolder}/{variantName}.prefab";
            if (!AssetExistsAtPath<GameObject>(rotatedPrefabPath))
                return false;

            if (!AssetExistsAtPath<DungeonTileBakeData>(BuildBakeDataAssetPath(variantName, "P100")) ||
                !AssetExistsAtPath<DungeonTileBakeData>(BuildBakeDataAssetPath(variantName, "P0")))
            {
                return false;
            }
        }

        return true;
    }

    private static string BakeNewPrisonTileModifiedLightingCli(
        string label,
        IReadOnlyList<string> sourcePrefabs,
        Func<string> reportFunc,
        IReadOnlyList<int> rotationOverride = null)
    {
        var report = new StringBuilder();
        report.AppendLine($"[DungeonTileRotationBakeTool] {label} lighting bake");

        if (sourcePrefabs == null || sourcePrefabs.Count == 0)
            throw new InvalidOperationException("No source prefabs were provided.");

        for (int i = 0; i < sourcePrefabs.Count; i++)
        {
            if (!AssetExistsAtPath<GameObject>(sourcePrefabs[i]))
                throw new InvalidOperationException($"Missing source prefab: {sourcePrefabs[i]}");
        }

        report.AppendLine(NormalizeNewPrisonLightingForBake().TrimEnd());

        var window = CreateInstance<DungeonTileRotationBakeTool>();
        try
        {
            window.LoadEmissionMaterialVariants();
            window.ConfigureCliAdminstrativeBakeDefaults();
            window.cliRotationOverride = ToRotationArray(rotationOverride);

            string resolvedBakedFolder = window.GetResolvedBakedFilesFolder();
            string bakeDataAssetRootFolder = window.GetBakeDataAssetRootFolder();
            EnsureFolder(window.outputFolder);
            EnsureFolder(bakeDataAssetRootFolder);
            EnsureFolder(Path.GetDirectoryName(window.GetNormalizedBakeScenePath())?.Replace("\\", "/"));
            if (window.collectBakedFilesToFolder)
                EnsureFolder(resolvedBakedFolder);

            var generatedPrefabs = window.GenerateVariants(new List<string>(sourcePrefabs));
            var fitResult = window.fitReflectionProbesBeforeBake
                ? window.FitReflectionProbesForPrefabs(generatedPrefabs, $"Fitting {label} reflection probes")
                : default;

            var bakeDataAssets = window.BuildAndOptionallyBakeScene(generatedPrefabs, resolvedBakedFolder, bakeDataAssetRootFolder);
            window.AttachLightmapSwitchers(bakeDataAssets);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report.AppendLine(
                $"generatedPrefabs={generatedPrefabs.Count}, bakeDataAssets={CountBakeDataAssets(bakeDataAssets)}, fitUpdated={fitResult.updatedPrefabs}, fitCreated={fitResult.createdProbes}, fitRemoved={fitResult.removedProbes}");

            if (reportFunc != null)
                report.Append(reportFunc());

            return report.ToString();
        }
        finally
        {
            DestroyImmediate(window);
            EditorUtility.ClearProgressBar();
        }
    }

    public static string RepairNewPrisonDecalRoomSwitchersCli()
    {
        return RepairNewPrisonLightmapSwitchersCli(
            "NewPrison decal rooms",
            NewPrisonDecalRoomPrefabNames,
            ReportNewPrisonDecalRoomsLightingCli);
    }

    public static string RepairAdminstrativeSegregationSwitchersCli()
    {
        return RepairNewPrisonLightmapSwitchersCli(
            "AdminstrativeSegregation",
            new[] { "AdminstrativeSegregation" },
            ReportAdminstrativeSegregationLightingCli);
    }

    private static string RepairNewPrisonLightmapSwitchersCli(
        string label,
        IReadOnlyList<string> prefabBaseNames,
        Func<string> reportFunc)
    {
        var report = new StringBuilder();
        report.AppendLine($"[DungeonTileRotationBakeTool] {label} switcher repair");

        var window = CreateInstance<DungeonTileRotationBakeTool>();
        try
        {
            window.LoadEmissionMaterialVariants();
            window.ConfigureCliAdminstrativeBakeDefaults();

            var bakeDataAssets = new Dictionary<string, PowerBakeDataSet>(StringComparer.OrdinalIgnoreCase);
            int missingPrefabs = 0;
            int missingBakeData = 0;

            for (int nameIndex = 0; nameIndex < prefabBaseNames.Count; nameIndex++)
            {
                string baseName = prefabBaseNames[nameIndex];
                for (int rotationIndex = 0; rotationIndex < window.rotationsY.Length; rotationIndex++)
                {
                    int angle = window.rotationsY[rotationIndex];
                    string prefabName = $"{baseName}_R{angle:000}";
                    string prefabPath = $"{window.outputFolder}/{prefabName}.prefab";
                    if (!AssetExistsAtPath<GameObject>(prefabPath))
                    {
                        missingPrefabs++;
                        continue;
                    }

                    var dataSet = new PowerBakeDataSet();
                    string p100Path = BuildBakeDataAssetPath(prefabName, "P100");
                    string p0Path = BuildBakeDataAssetPath(prefabName, "P0");
                    if (AssetExistsAtPath<DungeonTileBakeData>(p100Path))
                        dataSet.Set(PowerBakeLevel.P100, p100Path);
                    else
                        missingBakeData++;

                    if (AssetExistsAtPath<DungeonTileBakeData>(p0Path))
                        dataSet.Set(PowerBakeLevel.P00, p0Path);
                    else
                        missingBakeData++;

                    if (dataSet.CountAssigned() > 0)
                        bakeDataAssets[prefabPath] = dataSet;
                }
            }

            window.AttachLightmapSwitchers(bakeDataAssets);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report.AppendLine($"repairTargets={bakeDataAssets.Count}, missingPrefabs={missingPrefabs}, missingBakeData={missingBakeData}");
            if (reportFunc != null)
                report.Append(reportFunc());

            return report.ToString();
        }
        finally
        {
            DestroyImmediate(window);
            EditorUtility.ClearProgressBar();
        }
    }

    public static string ReportAdminstrativeSegregationLightingCli()
    {
        return ReportNewPrisonLightingCli("AdminstrativeSegregation", new[] { "AdminstrativeSegregation" });
    }

    public static string ReportRequestedNewPrisonRoomsLightingCli()
    {
        return ReportNewPrisonLightingCli(
            "Cafeteria + Conference_Room + CorridorA + CrossRoom",
            RequestedNewPrisonPrefabNames);
    }

    public static string ReportDecalAndLampFixRoomsLightingCli()
    {
        return ReportNewPrisonLightingCli(
            "AdminstrativeSegregation + CorridorA + CrossRoom",
            DecalAndLampFixPrefabNames);
    }

    public static string ReportNewPrisonDecalRoomsLightingCli()
    {
        return ReportNewPrisonLightingCli(
            "NewPrison decal rooms",
            NewPrisonDecalRoomPrefabNames);
    }

    public static string ReportCorridorADecalLightingCli()
    {
        return ReportNewPrisonLightingCli(
            "CorridorA decal",
            CorridorADecalPrefabNames);
    }

    public static string ReportCafeteriaDecalLightingCli()
    {
        return ReportNewPrisonLightingCli(
            "Cafeteria decal",
            CafeteriaDecalPrefabNames);
    }

    public static string ReportCrossRoomDecalLightingCli()
    {
        return ReportNewPrisonLightingCli(
            "CrossRoom decal",
            CrossRoomDecalPrefabNames);
    }

    public static string ReportReceptionWindowLightingCli()
    {
        return ReportNewPrisonLightingCli(
            "Reception window bake-safe",
            ReceptionWindowPrefabNames);
    }

    private static string ReportNewPrisonLightingCli(string label, IReadOnlyList<string> prefabBaseNames)
    {
        var report = new StringBuilder();
        report.AppendLine($"[DungeonTileRotationBakeTool] {label} lighting report");

        if (prefabBaseNames == null || prefabBaseNames.Count == 0)
        {
            report.AppendLine("summary: no prefab names provided");
            return report.ToString();
        }

        int totalOffMaterials = 0;
        int totalNonBakedLights = 0;
        int missingPrefabs = 0;
        int missingSwitchers = 0;
        int missingPowerSets = 0;
        int missingBakeDataAssets = 0;
        int bakeDataAssetsWithoutSh = 0;
        int totalDecalRenderers = 0;
        int totalFloorDecalRenderers = 0;
        int totalWallDecalRenderers = 0;
        int totalFloorDecalMissingContributeGi = 0;
        int totalWallDecalContributeGi = 0;
        int totalDecalReceiveShadows = 0;
        int totalBakeDataFloorDecalEntries = 0;
        int totalBakeDataWallDecalEntries = 0;
        int totalReceptionWindowVisuals = 0;
        int totalReceptionWindowVisualContributeGi = 0;
        int totalReceptionWindowVisualReceiveShadows = 0;
        int totalReceptionWindowVisualCastShadows = 0;
        int totalBakeDataReceptionWindowVisualEntries = 0;

        for (int nameIndex = 0; nameIndex < prefabBaseNames.Count; nameIndex++)
        {
            string baseName = prefabBaseNames[nameIndex];
            for (int i = 0; i < 4; i++)
            {
                int angle = i * 90;
                string prefabName = $"{baseName}_R{angle:000}";
                string prefabPath = $"Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/{prefabName}.prefab";
                if (!AssetExistsAtPath<GameObject>(prefabPath))
                {
                    missingPrefabs++;
                    report.AppendLine($"{prefabName}: missing prefab ({prefabPath})");
                    continue;
                }

                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    var lights = root.GetComponentsInChildren<Light>(true);
                    int bakedLights = 0;
                    int nonBakedLights = 0;
                    for (int l = 0; l < lights.Length; l++)
                    {
                        if (lights[l] == null)
                            continue;

                        if (lights[l].lightmapBakeType == LightmapBakeType.Baked)
                            bakedLights++;
                        else
                            nonBakedLights++;
                    }

                    int offMaterials = CountOffVariantMaterials(root);
                    CountDecalRendererState(
                        root,
                        out int decalRenderers,
                        out int floorDecalRenderers,
                        out int wallDecalRenderers,
                        out int floorDecalMissingContributeGi,
                        out int wallDecalContributeGi,
                        out int decalReceiveShadows);
                    CountReceptionWindowVisualState(
                        root,
                        out int receptionWindowVisuals,
                        out int receptionWindowVisualContributeGi,
                        out int receptionWindowVisualReceiveShadows,
                        out int receptionWindowVisualCastShadows);
                    totalOffMaterials += offMaterials;
                    totalNonBakedLights += nonBakedLights;
                    totalDecalRenderers += decalRenderers;
                    totalFloorDecalRenderers += floorDecalRenderers;
                    totalWallDecalRenderers += wallDecalRenderers;
                    totalFloorDecalMissingContributeGi += floorDecalMissingContributeGi;
                    totalWallDecalContributeGi += wallDecalContributeGi;
                    totalDecalReceiveShadows += decalReceiveShadows;
                    totalReceptionWindowVisuals += receptionWindowVisuals;
                    totalReceptionWindowVisualContributeGi += receptionWindowVisualContributeGi;
                    totalReceptionWindowVisualReceiveShadows += receptionWindowVisualReceiveShadows;
                    totalReceptionWindowVisualCastShadows += receptionWindowVisualCastShadows;

                    var switcher = root.GetComponent<DungeonTileLightmapSwitcher>();
                    var powerSet = root.GetComponent<DungeonTilePowerBakeSet>();
                    if (switcher == null) missingSwitchers++;
                    if (powerSet == null) missingPowerSets++;

                    int emissionEntries = powerSet != null ? powerSet.EmissionMaterialEntries.Length : 0;
                    bool suppressP0Probes = GetSuppressP0ReflectionProbes(switcher);

                    string p100Path = BuildBakeDataAssetPath(prefabName, "P100");
                    string p0Path = BuildBakeDataAssetPath(prefabName, "P0");
                    var p100Data = AssetDatabase.LoadAssetAtPath<DungeonTileBakeData>(p100Path);
                    var p0Data = AssetDatabase.LoadAssetAtPath<DungeonTileBakeData>(p0Path);
                    if (p100Data == null) missingBakeDataAssets++;
                    else if (GetLightProbeEntryCount(p100Data) == 0) bakeDataAssetsWithoutSh++;
                    if (p0Data == null) missingBakeDataAssets++;
                    else if (GetLightProbeEntryCount(p0Data) == 0) bakeDataAssetsWithoutSh++;
                    totalBakeDataFloorDecalEntries += CountFloorDecalRendererEntries(p100Data);
                    totalBakeDataFloorDecalEntries += CountFloorDecalRendererEntries(p0Data);
                    totalBakeDataWallDecalEntries += CountWallDecalRendererEntries(p100Data);
                    totalBakeDataWallDecalEntries += CountWallDecalRendererEntries(p0Data);
                    totalBakeDataReceptionWindowVisualEntries += CountReceptionWindowVisualRendererEntries(p100Data);
                    totalBakeDataReceptionWindowVisualEntries += CountReceptionWindowVisualRendererEntries(p0Data);

                    report.AppendLine(
                        $"{prefabName}: lights={lights.Length}, bakedLights={bakedLights}, nonBakedLights={nonBakedLights}, offMaterials={offMaterials}, decalRenderers={decalRenderers}, floorDecals={floorDecalRenderers}, wallDecals={wallDecalRenderers}, floorDecalMissingContributeGi={floorDecalMissingContributeGi}, wallDecalContributeGi={wallDecalContributeGi}, decalReceiveShadows={decalReceiveShadows}, receptionWindowVisuals={receptionWindowVisuals}, receptionWindowVisualContributeGi={receptionWindowVisualContributeGi}, receptionWindowVisualReceiveShadows={receptionWindowVisualReceiveShadows}, receptionWindowVisualCastShadows={receptionWindowVisualCastShadows}, switcher={(switcher != null)}, powerSet={(powerSet != null)}, suppressP0Probes={suppressP0Probes}, emissionEntries={emissionEntries}");
                    report.AppendLine($"  P100: {FormatBakeDataSummary(p100Data, p100Path)}");
                    report.AppendLine($"  P0:   {FormatBakeDataSummary(p0Data, p0Path)}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        report.AppendLine(
            $"summary: prefabs={prefabBaseNames.Count * 4}, missingPrefabs={missingPrefabs}, missingSwitchers={missingSwitchers}, missingPowerSets={missingPowerSets}, offMaterials={totalOffMaterials}, nonBakedLights={totalNonBakedLights}, decalRenderers={totalDecalRenderers}, floorDecals={totalFloorDecalRenderers}, wallDecals={totalWallDecalRenderers}, floorDecalMissingContributeGi={totalFloorDecalMissingContributeGi}, wallDecalContributeGi={totalWallDecalContributeGi}, decalReceiveShadows={totalDecalReceiveShadows}, receptionWindowVisuals={totalReceptionWindowVisuals}, receptionWindowVisualContributeGi={totalReceptionWindowVisualContributeGi}, receptionWindowVisualReceiveShadows={totalReceptionWindowVisualReceiveShadows}, receptionWindowVisualCastShadows={totalReceptionWindowVisualCastShadows}, bakeDataFloorDecalEntries={totalBakeDataFloorDecalEntries}, bakeDataWallDecalEntries={totalBakeDataWallDecalEntries}, bakeDataReceptionWindowVisualEntries={totalBakeDataReceptionWindowVisualEntries}, missingBakeDataAssets={missingBakeDataAssets}, bakeDataAssetsWithoutSh={bakeDataAssetsWithoutSh}");
        return report.ToString();
    }

    private void ConfigureCliAdminstrativeBakeDefaults()
    {
        useSelectedPrefabs = false;
        sourceFolder = "Assets/Prefabs/map_piece/NewPrison/Tile_modified";
        outputFolder = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated";
        bakeScenePath = "Assets/SceneTemplateAssets/Scenes/TileBake_Auto.unity";
        collectBakedFilesToFolder = true;
        bakedFilesUnderOutputFolder = true;
        bakedFilesFolder = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/BakedData";
        setAllowRotationFalse = true;
        removeNavMeshLinksFromVariants = true;
        buildBakeScene = true;
        runBakeAfterBuild = true;
        prepareDoorwayHoleWallsForBake = true;
        useFixedBakeScenePath = false;
        createReflectionProbeIfMissing = true;
        autoCreatePowerProbeVariants = true;
        fitReflectionProbesBeforeBake = true;
        autoSegmentReflectionProbes = true;
        autoNormalizeNewPrisonRoomsBeforeBake = true;
        bakeLightProbeData = true;
        lightProbeGridSpacing = 2.5f;
        lightProbeWallInset = 0.75f;
        maxLightProbeSamples = 128;
        bakePower100 = true;
        bakePower00 = true;
        overrideBakeAmbient = true;
        bakeAmbientColor = new Color(0f, 0f, 0f, 1f);
        bakeAmbientIntensity = 0.9f;
    }

    private static string NormalizeNewPrisonLightingForBake()
    {
        var report = new StringBuilder();
        report.AppendLine(NewPrisonEmissionMaterialSetup.SetupNewPrisonEmissionMaterials().TrimEnd());
        report.AppendLine(NewPrisonDecalMaterialSetup.SetupAndApplyAllDecals().TrimEnd());
        report.AppendLine(NewPrisonLightingPrefabNormalizer.NormalizeTileModifiedAndTilesLighting().TrimEnd());
        return report.ToString().TrimEnd();
    }

    private static string ReportNewPrisonLightingForBake()
    {
        var report = new StringBuilder();
        report.AppendLine(NewPrisonLightingPrefabNormalizer.ReportTileModifiedAndTilesLighting().TrimEnd());
        report.AppendLine(NewPrisonDecalMaterialSetup.ReportAllDecals().TrimEnd());
        report.AppendLine(NewPrisonReceptionWindowBakeSafeSetup.ReportTileModifiedAndTilesReceptionWindows().TrimEnd());
        return report.ToString().TrimEnd();
    }

    private static void CountDecalRendererState(
        GameObject root,
        out int decalRenderers,
        out int floorDecalRenderers,
        out int wallDecalRenderers,
        out int floorDecalMissingContributeGi,
        out int wallDecalContributeGi,
        out int decalReceiveShadows)
    {
        decalRenderers = 0;
        floorDecalRenderers = 0;
        wallDecalRenderers = 0;
        floorDecalMissingContributeGi = 0;
        wallDecalContributeGi = 0;
        decalReceiveShadows = 0;

        if (root == null)
            return;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (!NewPrisonDecalUtility.IsDecalRenderer(renderer))
                continue;

            decalRenderers++;
            bool isFloorDecal = NewPrisonDecalUtility.IsFloorDecalRenderer(renderer);
            if (isFloorDecal)
                floorDecalRenderers++;
            else
                wallDecalRenderers++;

            var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
            bool hasContributeGi = (flags & StaticEditorFlags.ContributeGI) != 0;
            if (isFloorDecal && !hasContributeGi)
                floorDecalMissingContributeGi++;
            else if (!isFloorDecal && hasContributeGi)
                wallDecalContributeGi++;

            if (renderer.receiveShadows)
                decalReceiveShadows++;
        }
    }

    private static int CountOffVariantMaterials(GameObject root)
    {
        int count = 0;
        if (root == null)
            return count;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            var renderer = renderers[r];
            if (renderer == null)
                continue;

            var materials = renderer.sharedMaterials;
            for (int m = 0; m < materials.Length; m++)
            {
                var material = materials[m];
                if (material == null)
                    continue;

                string materialPath = AssetDatabase.GetAssetPath(material);
                string fileName = Path.GetFileNameWithoutExtension(materialPath);
                if (material.name.EndsWith("_Off", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith("_Off", StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void CountReceptionWindowVisualState(
        GameObject root,
        out int visualRenderers,
        out int visualContributeGi,
        out int visualReceiveShadows,
        out int visualCastShadows)
    {
        visualRenderers = 0;
        visualContributeGi = 0;
        visualReceiveShadows = 0;
        visualCastShadows = 0;

        if (root == null)
            return;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (!NewPrisonReceptionWindowBakeSafeSetup.IsReceptionWindowVisualRenderer(renderer))
                continue;

            visualRenderers++;
            var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
            if ((flags & StaticEditorFlags.ContributeGI) != 0)
                visualContributeGi++;
            if (renderer.receiveShadows)
                visualReceiveShadows++;
            if (renderer.shadowCastingMode != ShadowCastingMode.Off)
                visualCastShadows++;
        }
    }

    private static string BuildBakeDataAssetPath(string prefabName, string powerSuffix)
    {
        return $"Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/BakedData/{SanitizeFileName(prefabName)}/{powerSuffix}/{prefabName}_BakeData.asset";
    }

    private static bool GetSuppressP0ReflectionProbes(DungeonTileLightmapSwitcher switcher)
    {
        if (switcher == null)
            return false;

        var serialized = new SerializedObject(switcher);
        var prop = serialized.FindProperty("disableReflectionProbesOnPower0");
        return prop != null && prop.boolValue;
    }

    private static int GetLightProbeEntryCount(DungeonTileBakeData data)
    {
        return data != null && data.lightProbeEntries != null ? data.lightProbeEntries.Length : 0;
    }

    private static string FormatBakeDataSummary(DungeonTileBakeData data, string path)
    {
        if (data == null)
            return $"missing ({path})";

        int lightmapCount = data.lightmapColors != null ? data.lightmapColors.Length : 0;
        int rendererCount = data.rendererEntries != null ? data.rendererEntries.Length : 0;
        int floorDecalRendererEntryCount = CountFloorDecalRendererEntries(data);
        int wallDecalRendererEntryCount = CountWallDecalRendererEntries(data);
        int receptionWindowVisualRendererEntryCount = CountReceptionWindowVisualRendererEntries(data);
        int probeCount = data.reflectionProbeEntries != null ? data.reflectionProbeEntries.Length : 0;
        int shCount = data.lightProbeEntries != null ? data.lightProbeEntries.Length : 0;
        return
            $"{path} lightmaps={lightmapCount}, renderers={rendererCount}, floorDecalRendererEntries={floorDecalRendererEntryCount}, wallDecalRendererEntries={wallDecalRendererEntryCount}, receptionWindowVisualRendererEntries={receptionWindowVisualRendererEntryCount}, reflectionProbes={probeCount}, shProbes={shCount}, avgL0={CalculateAverageL0(data):0.0000}";
    }

    private static int CountFloorDecalRendererEntries(DungeonTileBakeData data)
    {
        if (data == null || data.rendererEntries == null)
            return 0;

        int count = 0;
        for (int i = 0; i < data.rendererEntries.Length; i++)
        {
            if (NewPrisonDecalUtility.IsFloorDecalRelativePath(data.rendererEntries[i].relativePath))
                count++;
        }

        return count;
    }

    private static int CountWallDecalRendererEntries(DungeonTileBakeData data)
    {
        if (data == null || data.rendererEntries == null)
            return 0;

        int count = 0;
        for (int i = 0; i < data.rendererEntries.Length; i++)
        {
            if (NewPrisonDecalUtility.IsWallDecalRelativePath(data.rendererEntries[i].relativePath))
                count++;
        }

        return count;
    }

    private static int CountReceptionWindowVisualRendererEntries(DungeonTileBakeData data)
    {
        if (data == null || data.rendererEntries == null)
            return 0;

        int count = 0;
        for (int i = 0; i < data.rendererEntries.Length; i++)
        {
            if (NewPrisonReceptionWindowBakeSafeSetup.IsReceptionWindowVisualRelativePath(data.rendererEntries[i].relativePath))
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
        {
            Vector3 l0 = data.lightProbeEntries[i].coefficient0;
            total += l0.x * 0.2126f + l0.y * 0.7152f + l0.z * 0.0722f;
        }

        return total / data.lightProbeEntries.Length;
    }

    private void OnEnable()
    {
        LoadChecklistState();
        LoadEmissionMaterialVariants();
    }

    private void OnDisable()
    {
        SaveChecklistState();
        SaveEmissionMaterialVariants();
    }

    private void OnGUI()
    {
        mainScrollPosition = EditorGUILayout.BeginScrollView(mainScrollPosition);

        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        useSelectedPrefabs = EditorGUILayout.Toggle("Use Selected Prefabs", useSelectedPrefabs);
        if (useSelectedPrefabs)
        {
            int selectedCount = GetSelectedPrefabPaths().Count;
            EditorGUILayout.LabelField("Selected Prefab Count", selectedCount.ToString());
            EditorGUILayout.HelpBox("Select prefab assets or folders in Project window, then run the tool.", MessageType.None);
        }
        else
        {
            sourceFolder = EditorGUILayout.TextField("Tile Prefab Folder", sourceFolder);
        }

        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Variants", EditorStyles.boldLabel);
        setAllowRotationFalse = EditorGUILayout.Toggle("Set Tile.AllowRotation = false", setAllowRotationFalse);
        removeNavMeshLinksFromVariants = EditorGUILayout.Toggle("Remove NavMeshLinks", removeNavMeshLinksFromVariants);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("NewPrison Normalize", EditorStyles.boldLabel);
        autoNormalizeNewPrisonRoomsBeforeBake = EditorGUILayout.Toggle("Auto Normalize Before Generate/Bake", autoNormalizeNewPrisonRoomsBeforeBake);
        EditorGUILayout.HelpBox(
            "For NewPrison Tile_modified/Tiles sources, normalize room authoring assets before generating rotated variants: shared lamps, emission variants, decal bake rules, and bake-safe reception windows.",
            MessageType.Info);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Normalize NewPrison Rooms Now"))
        {
            RunNormalizeNewPrisonRoomsNow();
        }

        if (GUILayout.Button("Report NewPrison Normalize State"))
        {
            RunReportNewPrisonNormalizeState();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Reflection Probe", EditorStyles.boldLabel);
        createReflectionProbeIfMissing = EditorGUILayout.Toggle("Create Probe If Missing", createReflectionProbeIfMissing);
        autoCreatePowerProbeVariants = EditorGUILayout.Toggle("Auto Create Power Probe Variants", autoCreatePowerProbeVariants);
        newReflectionProbeName = EditorGUILayout.TextField("New Probe Name", newReflectionProbeName);
        fitReflectionProbesBeforeBake = EditorGUILayout.Toggle("Fit Before Build/Bake", fitReflectionProbesBeforeBake);
        autoSegmentReflectionProbes = EditorGUILayout.Toggle("Auto Segment Probes", autoSegmentReflectionProbes);
        using (new EditorGUI.DisabledScope(!autoSegmentReflectionProbes))
        {
            segmentOnlyWhenLongestAxisOver = EditorGUILayout.FloatField("Segment Longest Axis Over", segmentOnlyWhenLongestAxisOver);
            targetProbeSegmentLength = EditorGUILayout.FloatField("Target Segment Length", targetProbeSegmentLength);
            maxReflectionProbeSegments = EditorGUILayout.IntField("Max Segments", maxReflectionProbeSegments);
            reflectionProbeSegmentOverlap = EditorGUILayout.FloatField("Segment Overlap", reflectionProbeSegmentOverlap);
        }

        segmentOnlyWhenLongestAxisOver = Mathf.Max(0f, segmentOnlyWhenLongestAxisOver);
        targetProbeSegmentLength = Mathf.Max(0.1f, targetProbeSegmentLength);
        maxReflectionProbeSegments = Mathf.Max(1, maxReflectionProbeSegments);
        reflectionProbeSegmentOverlap = Mathf.Max(0f, reflectionProbeSegmentOverlap);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Dynamic Object SH Probes", EditorStyles.boldLabel);
        bakeLightProbeData = EditorGUILayout.Toggle("Bake SH Probe Data", bakeLightProbeData);
        using (new EditorGUI.DisabledScope(!bakeLightProbeData))
        {
            lightProbeGridSpacing = EditorGUILayout.FloatField("Grid Spacing", lightProbeGridSpacing);
            lightProbeWallInset = EditorGUILayout.FloatField("Wall Inset", lightProbeWallInset);
            maxLightProbeSamples = EditorGUILayout.IntField("Max Samples Per Tile", maxLightProbeSamples);
        }

        lightProbeGridSpacing = Mathf.Max(0.5f, lightProbeGridSpacing);
        lightProbeWallInset = Mathf.Max(0f, lightProbeWallInset);
        maxLightProbeSamples = Mathf.Max(1, maxLightProbeSamples);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Power Levels", EditorStyles.boldLabel);
        bakePower100 = EditorGUILayout.ToggleLeft("Bake P100 (100%)", bakePower100);
        bakePower00 = EditorGUILayout.ToggleLeft("Bake P0 (0%)", bakePower00);

        if (!HasSelectedPowerLevel())
            EditorGUILayout.HelpBox("Select at least one power level for baking.", MessageType.Warning);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Emission Material Variants", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        if (emissionMaterialVariants == null)
            emissionMaterialVariants = new List<EmissionMaterialVariantSet>();

        for (int i = 0; i < emissionMaterialVariants.Count; i++)
        {
            var variantSet = emissionMaterialVariants[i] ?? new EmissionMaterialVariantSet();
            emissionMaterialVariants[i] = variantSet;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            string foldoutLabel = string.IsNullOrWhiteSpace(variantSet.label) ? $"Material Set {i + 1}" : variantSet.label;
            variantSet.isExpanded = EditorGUILayout.Foldout(variantSet.isExpanded, foldoutLabel, true);
            if (GUILayout.Button("Remove", GUILayout.Width(72f)))
            {
                emissionMaterialVariants.RemoveAt(i);
                SaveEmissionMaterialVariants();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                i--;
                continue;
            }
            EditorGUILayout.EndHorizontal();

            if (variantSet.isExpanded)
            {
                variantSet.label = EditorGUILayout.TextField("Label", variantSet.label);
                variantSet.sourceMaterial = (Material)EditorGUILayout.ObjectField("Source", variantSet.sourceMaterial, typeof(Material), false);
                variantSet.power100Material = (Material)EditorGUILayout.ObjectField("P100", variantSet.power100Material, typeof(Material), false);
                variantSet.power00Material = (Material)EditorGUILayout.ObjectField("P00", variantSet.power00Material, typeof(Material), false);
            }
            EditorGUILayout.EndVertical();
        }

        if (GUILayout.Button("Add Emission Material Set"))
        {
            emissionMaterialVariants.Add(new EmissionMaterialVariantSet
            {
                isExpanded = true
            });
            SaveEmissionMaterialVariants();
        }

        if (GUILayout.Button("Save Emission Variant Preset"))
            SaveEmissionMaterialVariants();

        if (EditorGUI.EndChangeCheck())
            SaveEmissionMaterialVariants();

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Bake", EditorStyles.boldLabel);
        buildBakeScene = EditorGUILayout.Toggle("Build Bake Scene", buildBakeScene);
        runBakeAfterBuild = EditorGUILayout.Toggle("Run Lightmapping.Bake", runBakeAfterBuild);
        prepareDoorwayHoleWallsForBake = EditorGUILayout.Toggle(
            "Bake Doorway Hole Walls",
            prepareDoorwayHoleWallsForBake);
        using (new EditorGUI.DisabledScope(!buildBakeScene))
        {
            bakeScenePath = EditorGUILayout.TextField("Bake Scene Path", bakeScenePath);
            useFixedBakeScenePath = EditorGUILayout.Toggle("Use Exact Scene Path Only", useFixedBakeScenePath);
            if (useFixedBakeScenePath)
            {
                EditorGUILayout.HelpBox(
                    "All bake passes reuse the exact Bake Scene Path. Keep 'Collect Baked Files' enabled so each pass stores lightmaps separately.",
                    MessageType.Info);
            }
            EditorGUILayout.LabelField("Bake Mode", "Each Prefab Separately (fixed)");
            collectBakedFilesToFolder = EditorGUILayout.Toggle("Collect Baked Files", collectBakedFilesToFolder);
            if (runBakeAfterBuild && !collectBakedFilesToFolder)
            {
                EditorGUILayout.HelpBox(
                    "Collect Baked Files is strongly required for stable bake data. If disabled, later bakes can overwrite lightmap files and older BakeData assets will show Missing references.",
                    MessageType.Warning);
            }
            if (collectBakedFilesToFolder)
            {
                bakedFilesUnderOutputFolder = EditorGUILayout.Toggle("Store Under Output Folder", bakedFilesUnderOutputFolder);
                if (bakedFilesUnderOutputFolder)
                {
                    EditorGUILayout.LabelField("Resolved Baked Folder", GetResolvedBakedFilesFolder());
                }
                else
                {
                    bakedFilesFolder = EditorGUILayout.TextField("Baked Files Folder", bakedFilesFolder);
                }
            }

            overrideBakeAmbient = EditorGUILayout.Toggle("Override Ambient", overrideBakeAmbient);
            if (overrideBakeAmbient)
            {
                bakeAmbientColor = EditorGUILayout.ColorField("Ambient Color", bakeAmbientColor);
                bakeAmbientIntensity = EditorGUILayout.Slider("Ambient Intensity", bakeAmbientIntensity, 0f, 3f);
            }
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            "Output variants: _R000, _R090, _R180, _R270 (P100 prefabs only). " +
            "Power bake data is generated per selected level (P100/P0).",
            MessageType.Info);

        EditorGUILayout.Space(8f);
        if (GUILayout.Button("Generate Variants"))
        {
            Run(generateOnly: true);
        }

        if (GUILayout.Button("Build Bake Scene Only"))
        {
            RunBuildBakeSceneOnly();
        }

        if (GUILayout.Button("Fit Reflection Probe To Tile Bounds"))
        {
            RunFitReflectionProbeToTileBounds();
        }

        if (GUILayout.Button("Generate Variants + Build/Bake Scene"))
        {
            Run(generateOnly: false);
        }

        EditorGUILayout.EndScrollView();

        if (GUI.changed)
            SaveChecklistState();
    }

    private void RunNormalizeNewPrisonRoomsNow()
    {
        try
        {
            string report = NormalizeNewPrisonLightingForBake();
            Debug.Log(report);
            EditorUtility.DisplayDialog(
                WindowTitle,
                "NewPrison room normalization finished. Full report was written to Console.",
                "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{WindowTitle}] NewPrison normalization failed: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog(WindowTitle, ex.Message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void RunReportNewPrisonNormalizeState()
    {
        try
        {
            string report = ReportNewPrisonLightingForBake();
            Debug.Log(report);
            EditorUtility.DisplayDialog(
                WindowTitle,
                "NewPrison normalize report was written to Console.",
                "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{WindowTitle}] NewPrison normalize report failed: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog(WindowTitle, ex.Message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private bool TryNormalizeNewPrisonSourcesBeforeGenerate(List<string> sourcePrefabPaths, out string report)
    {
        report = string.Empty;
        if (!autoNormalizeNewPrisonRoomsBeforeBake || !ContainsNewPrisonAuthoringSource(sourcePrefabPaths))
            return false;

        report = NormalizeNewPrisonLightingForBake();
        Debug.Log(report);
        return true;
    }

    private static bool ContainsNewPrisonAuthoringSource(IEnumerable<string> sourcePrefabPaths)
    {
        if (sourcePrefabPaths == null)
            return false;

        foreach (string sourcePrefabPath in sourcePrefabPaths)
        {
            if (IsNewPrisonAuthoringSourcePath(sourcePrefabPath))
                return true;
        }

        return false;
    }

    private static bool IsNewPrisonAuthoringSourcePath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return false;

        string normalized = assetPath.Replace('\\', '/');
        return normalized.StartsWith(NewPrisonTileModifiedFolder + "/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(NewPrisonTilesFolder + "/", StringComparison.OrdinalIgnoreCase);
    }

    private void RunBuildBakeSceneOnly()
    {
        if (!buildBakeScene)
        {
            EditorUtility.DisplayDialog(WindowTitle, "Enable 'Build Bake Scene' first.", "OK");
            return;
        }

        if (runBakeAfterBuild && !HasSelectedPowerLevel())
        {
            EditorUtility.DisplayDialog(WindowTitle, "Select at least one power level before baking.", "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        try
        {
            string resolvedBakedFolder = GetResolvedBakedFilesFolder();
            string bakeDataAssetRootFolder = GetBakeDataAssetRootFolder();
            EnsureFolder(Path.GetDirectoryName(GetNormalizedBakeScenePath())?.Replace("\\", "/"));
            EnsureFolder(bakeDataAssetRootFolder);
            if (collectBakedFilesToFolder)
                EnsureFolder(resolvedBakedFolder);

            List<string> prefabPaths;
            if (useSelectedPrefabs)
            {
                prefabPaths = GetSelectedPrefabPaths();
            }
            else
            {
                prefabPaths = GetSourcePrefabs(outputFolder, excludeOutputFolder: false);
            }

            if (prefabPaths.Count == 0)
            {
                string message = useSelectedPrefabs
                    ? "No selected prefabs to build. Select prefabs/folders in Project window."
                    : $"No prefabs found in output folder: {outputFolder}";
                EditorUtility.DisplayDialog(WindowTitle, message, "OK");
                return;
            }

            ReflectionProbeFitResult fitResult = default;
            if (fitReflectionProbesBeforeBake)
                fitResult = FitReflectionProbesForPrefabs(prefabPaths, "Fitting reflection probes before bake");

            var bakeDataAssets = BuildAndOptionallyBakeScene(prefabPaths, resolvedBakedFolder, bakeDataAssetRootFolder);
            AttachLightmapSwitchers(bakeDataAssets);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int plannedBakePasses = GetPlannedBakePassCount(prefabPaths.Count);
            string summary = runBakeAfterBuild
                ? collectBakedFilesToFolder
                    ? $"Built {plannedBakePasses} bake scene(s), baked lighting, and collected baked files."
                    : $"Built {plannedBakePasses} bake scene(s) and baked lighting."
                : $"Built {plannedBakePasses} bake scene(s) only (bake skipped).";

            int bakeDataAssetCount = CountBakeDataAssets(bakeDataAssets);
            if (bakeDataAssetCount > 0)
                summary += $" Generated {bakeDataAssetCount} bake data asset(s).";

            if (fitReflectionProbesBeforeBake)
                summary += $" Fit reflection probes on {fitResult.updatedPrefabs} prefab(s), created {fitResult.createdProbes}, removed {fitResult.removedProbes}.";

            EditorUtility.DisplayDialog(WindowTitle, summary, "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{WindowTitle}] Build-only failed: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog(WindowTitle, ex.Message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private ReflectionProbeFitResult FitReflectionProbesForPrefabs(List<string> prefabPaths, string progressLabel)
    {
        var result = new ReflectionProbeFitResult();
        if (prefabPaths == null || prefabPaths.Count == 0)
            return result;

        string label = string.IsNullOrWhiteSpace(progressLabel) ? "Fitting reflection probes" : progressLabel;
        for (int i = 0; i < prefabPaths.Count; i++)
        {
            string prefabPath = prefabPaths[i];
            float progress = prefabPaths.Count <= 0 ? 0f : (float)i / prefabPaths.Count;
            EditorUtility.DisplayProgressBar(WindowTitle, $"{label} ({i + 1}/{prefabPaths.Count})", progress);

            if (FitReflectionProbesToTileBounds(prefabPath, out int createdForPrefab, out int removedForPrefab))
            {
                result.updatedPrefabs++;
                result.createdProbes += createdForPrefab;
                result.removedProbes += removedForPrefab;
            }
            else
            {
                result.skippedPrefabs++;
            }
        }

        return result;
    }

    private void RunFitReflectionProbeToTileBounds()
    {
        try
        {
            List<string> prefabPaths;
            if (useSelectedPrefabs)
            {
                prefabPaths = GetSelectedPrefabPaths();
            }
            else
            {
                prefabPaths = GetSourcePrefabs(outputFolder, excludeOutputFolder: false);
            }

            if (prefabPaths.Count == 0)
            {
                string message = useSelectedPrefabs
                    ? "No selected prefabs. Select prefab assets/folders in Project window."
                    : $"No prefabs found in folder: {outputFolder}";
                EditorUtility.DisplayDialog(WindowTitle, message, "OK");
                return;
            }

            var fitResult = FitReflectionProbesForPrefabs(prefabPaths, "Fitting reflection probes");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                WindowTitle,
                $"Processed {prefabPaths.Count} prefab(s). Updated {fitResult.updatedPrefabs}, skipped {fitResult.skippedPrefabs}, created {fitResult.createdProbes}, removed {fitResult.removedProbes} managed reflection probe(s).",
                "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{WindowTitle}] Reflection probe fit failed: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog(WindowTitle, ex.Message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void Run(bool generateOnly)
    {
        if (!ValidateInputs(out var validationMessage))
        {
            EditorUtility.DisplayDialog(WindowTitle, validationMessage, "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        try
        {
            string resolvedBakedFolder = GetResolvedBakedFilesFolder();
            string bakeDataAssetRootFolder = GetBakeDataAssetRootFolder();
            EnsureFolder(outputFolder);
            EnsureFolder(bakeDataAssetRootFolder);
            if (!generateOnly && buildBakeScene)
            {
                EnsureFolder(Path.GetDirectoryName(GetNormalizedBakeScenePath())?.Replace("\\", "/"));
                if (collectBakedFilesToFolder)
                    EnsureFolder(resolvedBakedFolder);
            }

            var sourcePrefabs = useSelectedPrefabs ? GetSelectedPrefabPaths() : GetSourcePrefabs(sourceFolder, excludeOutputFolder: true);
            if (sourcePrefabs.Count == 0)
            {
                EditorUtility.DisplayDialog(WindowTitle, "No prefabs found in source folder.", "OK");
                return;
            }

            bool normalizedBeforeGenerate = TryNormalizeNewPrisonSourcesBeforeGenerate(sourcePrefabs, out _);
            var generatedPrefabs = GenerateVariants(sourcePrefabs);

            var bakeDataAssets = new Dictionary<string, PowerBakeDataSet>(StringComparer.OrdinalIgnoreCase);
            ReflectionProbeFitResult fitResult = default;
            if (!generateOnly && buildBakeScene)
            {
                if (fitReflectionProbesBeforeBake)
                    fitResult = FitReflectionProbesForPrefabs(generatedPrefabs, "Fitting reflection probes before bake");

                bakeDataAssets = BuildAndOptionallyBakeScene(generatedPrefabs, resolvedBakedFolder, bakeDataAssetRootFolder);
                AttachLightmapSwitchers(bakeDataAssets);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string summary = $"Generated {generatedPrefabs.Count} prefab variants.";
            if (normalizedBeforeGenerate)
                summary += " Normalized NewPrison authoring prefabs before generation.";
            if (!generateOnly && buildBakeScene)
            {
                int plannedBakePasses = GetPlannedBakePassCount(generatedPrefabs.Count);
                if (runBakeAfterBuild)
                {
                    summary += collectBakedFilesToFolder
                        ? $" Built {plannedBakePasses} bake scene(s), baked lighting, and collected baked files."
                        : $" Built {plannedBakePasses} bake scene(s) and baked lighting.";
                }
                else
                {
                    summary += $" Built {plannedBakePasses} bake scene(s) (no bake run).";
                }

            }

            int bakeDataAssetCount = CountBakeDataAssets(bakeDataAssets);
            if (bakeDataAssetCount > 0)
                summary += $" Generated {bakeDataAssetCount} bake data asset(s).";

            if (!generateOnly && buildBakeScene && fitReflectionProbesBeforeBake)
                summary += $" Fit reflection probes on {fitResult.updatedPrefabs} prefab(s), created {fitResult.createdProbes}, removed {fitResult.removedProbes}.";

            EditorUtility.DisplayDialog(WindowTitle, summary, "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{WindowTitle}] Failed: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog(WindowTitle, ex.Message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private bool ValidateInputs(out string message)
    {
        if (useSelectedPrefabs)
        {
            if (GetSelectedPrefabPaths().Count == 0)
            {
                message = "No prefab selected. Select prefab assets/folders in Project window.";
                return false;
            }
        }
        else if (string.IsNullOrWhiteSpace(sourceFolder) || !AssetDatabase.IsValidFolder(sourceFolder))
        {
            message = "Source folder is invalid.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            message = "Output folder is required.";
            return false;
        }

        if (buildBakeScene && string.IsNullOrWhiteSpace(bakeScenePath))
        {
            message = "Bake scene path is required.";
            return false;
        }

        if (buildBakeScene && !bakeScenePath.Trim().EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
        {
            message = "Bake scene path must end with .unity.";
            return false;
        }

        if (buildBakeScene && runBakeAfterBuild && !collectBakedFilesToFolder)
        {
            message = "Enable 'Collect Baked Files' when baking. Otherwise baked lightmap assets can be overwritten and BakeData references become Missing.";
            return false;
        }

        if (buildBakeScene && collectBakedFilesToFolder && string.IsNullOrWhiteSpace(bakedFilesFolder))
        {
            if (!bakedFilesUnderOutputFolder)
            {
                message = "Baked files folder is required.";
                return false;
            }
        }

        if (buildBakeScene && runBakeAfterBuild && !HasSelectedPowerLevel())
        {
            message = "Select at least one power level before baking.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private bool HasSelectedPowerLevel()
    {
        return bakePower100 || bakePower00;
    }

    private List<PowerBakeOption> GetSelectedPowerBakeOptions()
    {
        var options = new List<PowerBakeOption>(2);
        if (bakePower100)
            options.Add(new PowerBakeOption(PowerBakeLevel.P100, "P100", 1f));
        if (bakePower00)
            options.Add(new PowerBakeOption(PowerBakeLevel.P00, "P0", 0f));
        return options;
    }

    private static int CountBakeDataAssets(Dictionary<string, PowerBakeDataSet> bakeDataAssets)
    {
        if (bakeDataAssets == null || bakeDataAssets.Count == 0)
            return 0;

        int count = 0;
        foreach (var pair in bakeDataAssets)
        {
            if (pair.Value == null)
                continue;

            count += pair.Value.CountAssigned();
        }

        return count;
    }

    private int GetPlannedBakePassCount(int prefabCount)
    {
        int powerCount = Mathf.Max(1, GetSelectedPowerBakeOptions().Count);
        return Mathf.Max(0, prefabCount) * powerCount;
    }

    private string GetResolvedBakedFilesFolder()
    {
        if (!collectBakedFilesToFolder)
            return string.Empty;

        if (bakedFilesUnderOutputFolder)
            return $"{outputFolder.TrimEnd('/')}/BakedData";

        return bakedFilesFolder;
    }

    private string GetBakeDataAssetRootFolder()
    {
        if (bakedFilesUnderOutputFolder || string.IsNullOrWhiteSpace(bakedFilesFolder))
            return $"{outputFolder.TrimEnd('/')}/BakedData";

        return bakedFilesFolder;
    }

    private List<string> GetSourcePrefabs(string folder, bool excludeOutputFolder)
    {
        var result = new List<string>();
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(path))
                continue;

            if (excludeOutputFolder && path.StartsWith(outputFolder, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(path);
        }

        return result;
    }

    private List<string> GetSelectedPrefabPaths()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] selectedGuids = Selection.assetGUIDs;
        if (selectedGuids == null || selectedGuids.Length == 0)
            return new List<string>();

        for (int i = 0; i < selectedGuids.Length; i++)
        {
            string selectedPath = AssetDatabase.GUIDToAssetPath(selectedGuids[i]);
            if (string.IsNullOrEmpty(selectedPath))
                continue;

            if (AssetDatabase.IsValidFolder(selectedPath))
            {
                string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { selectedPath });
                for (int p = 0; p < prefabGuids.Length; p++)
                {
                    string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[p]);
                    if (!string.IsNullOrEmpty(prefabPath))
                        result.Add(prefabPath);
                }
            }
            else if (selectedPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(selectedPath);
            }
        }

        return new List<string>(result);
    }

    private List<string> GenerateVariants(List<string> sourcePrefabPaths)
    {
        IReadOnlyList<int> activeRotations = GetActiveRotations();
        var outputPaths = new List<string>(sourcePrefabPaths.Count * activeRotations.Count);
        int total = sourcePrefabPaths.Count * activeRotations.Count;
        int processed = 0;

        for (int i = 0; i < sourcePrefabPaths.Count; i++)
        {
            string sourcePath = sourcePrefabPaths[i];
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);

            for (int r = 0; r < activeRotations.Count; r++)
            {
                int angle = activeRotations[r];
                string litOutput = $"{outputFolder}/{baseName}_R{angle:000}.prefab";
                CreateVariantPrefab(sourcePath, litOutput, angle);
                outputPaths.Add(litOutput);
                processed++;
                EditorUtility.DisplayProgressBar(WindowTitle, "Generating lit variants...", (float)processed / total);
            }
        }

        return outputPaths;
    }

    private IReadOnlyList<int> GetActiveRotations()
    {
        return cliRotationOverride != null && cliRotationOverride.Length > 0
            ? cliRotationOverride
            : rotationsY;
    }

    private static int[] ToRotationArray(IReadOnlyList<int> rotations)
    {
        if (rotations == null || rotations.Count == 0)
            return null;

        var values = new List<int>(rotations.Count);
        for (int i = 0; i < rotations.Count; i++)
        {
            int normalized = ((rotations[i] % 360) + 360) % 360;
            if (normalized % 90 != 0 || values.Contains(normalized))
                continue;

            values.Add(normalized);
        }

        return values.Count > 0 ? values.ToArray() : null;
    }

    private bool FitReflectionProbesToTileBounds(string prefabPath, out int createdProbeCount, out int removedProbeCount)
    {
        createdProbeCount = 0;
        removedProbeCount = 0;
        if (string.IsNullOrWhiteSpace(prefabPath))
            return false;

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Type tileType = FindTypeByName("DunGen.Tile") ?? FindTypeByName("Tile");
            if (tileType == null)
            {
                Debug.LogWarning($"[{WindowTitle}] Skipping '{prefabPath}' because DunGen.Tile type was not found.");
                return false;
            }

            var tileComponent = root.GetComponentInChildren(tileType, true);
            if (tileComponent == null)
            {
                Debug.LogWarning($"[{WindowTitle}] Skipping '{prefabPath}' because no DunGen.Tile was found.");
                return false;
            }

            var recalculateMethod = tileType.GetMethod("RecalculateBounds");
            recalculateMethod?.Invoke(tileComponent, null);

            if (!TryGetTileLocalBounds(tileComponent, out Bounds tileLocalBounds))
            {
                Debug.LogWarning($"[{WindowTitle}] Skipping '{prefabPath}' because tile bounds could not be resolved.");
                return false;
            }

            if (tileLocalBounds.size.x <= 0f || tileLocalBounds.size.y <= 0f || tileLocalBounds.size.z <= 0f)
            {
                Debug.LogWarning($"[{WindowTitle}] Skipping '{prefabPath}' because tile bounds are invalid.");
                return false;
            }

            string probeBaseName = GetFitProbeBaseName();
            removedProbeCount += RemoveGeneratedReflectionProbeVariants(root.transform);
            removedProbeCount += RemoveManagedFitProbes(root, probeBaseName);

            var segments = BuildReflectionProbeFitSegments(tileLocalBounds);
            if (segments.Count == 0)
            {
                Debug.LogWarning($"[{WindowTitle}] Skipping '{prefabPath}' because no ReflectionProbe segment could be built.");
                return false;
            }

            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                var segment = segments[segmentIndex];
                for (int suffixIndex = 0; suffixIndex < FitPowerProbeSuffixes.Length; suffixIndex++)
                {
                    string suffix = FitPowerProbeSuffixes[suffixIndex];
                    string probeName = BuildFitProbeName(probeBaseName, segmentIndex, segments.Count, suffix);
                    var probe = CreateFitProbe(tileComponent.transform, probeName);
                    if (probe == null)
                        continue;

                    ApplyFitSegmentToProbe(probe, tileComponent.transform, segment);
                    createdProbeCount++;
                }
            }

            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
            if (!success)
                throw new InvalidOperationException($"Failed to save reflection probe updates to prefab: {prefabPath}");

            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private List<ReflectionProbeFitSegment> BuildReflectionProbeFitSegments(Bounds tileLocalBounds)
    {
        var segments = new List<ReflectionProbeFitSegment>();

        Vector3 fullSize = new Vector3(
            Mathf.Abs(tileLocalBounds.size.x),
            Mathf.Abs(tileLocalBounds.size.y),
            Mathf.Abs(tileLocalBounds.size.z));

        if (fullSize.x <= 0f || fullSize.y <= 0f || fullSize.z <= 0f)
            return segments;

        float xLength = fullSize.x;
        float zLength = fullSize.z;
        bool splitOnX = xLength >= zLength;
        float longestLength = splitOnX ? xLength : zLength;
        int segmentCount = 1;

        if (autoSegmentReflectionProbes && longestLength > Mathf.Max(0f, segmentOnlyWhenLongestAxisOver))
        {
            float targetLength = Mathf.Max(0.1f, targetProbeSegmentLength);
            int maxSegments = Mathf.Max(1, maxReflectionProbeSegments);
            segmentCount = Mathf.Clamp(Mathf.CeilToInt(longestLength / targetLength), 1, maxSegments);
        }

        if (segmentCount <= 1)
        {
            segments.Add(new ReflectionProbeFitSegment(tileLocalBounds.center, fullSize));
            return segments;
        }

        float axisMin = splitOnX ? tileLocalBounds.min.x : tileLocalBounds.min.z;
        float axisMax = splitOnX ? tileLocalBounds.max.x : tileLocalBounds.max.z;
        float segmentSpan = (axisMax - axisMin) / segmentCount;
        float overlap = Mathf.Max(0f, reflectionProbeSegmentOverlap);

        for (int i = 0; i < segmentCount; i++)
        {
            float rawMin = axisMin + segmentSpan * i;
            float rawMax = i == segmentCount - 1 ? axisMax : axisMin + segmentSpan * (i + 1);
            float expandedMin = Mathf.Max(axisMin, rawMin - overlap);
            float expandedMax = Mathf.Min(axisMax, rawMax + overlap);

            Vector3 center = tileLocalBounds.center;
            Vector3 size = fullSize;
            float segmentCenter = (expandedMin + expandedMax) * 0.5f;
            float segmentLength = Mathf.Max(0.1f, expandedMax - expandedMin);

            if (splitOnX)
            {
                center.x = segmentCenter;
                size.x = segmentLength;
            }
            else
            {
                center.z = segmentCenter;
                size.z = segmentLength;
            }

            segments.Add(new ReflectionProbeFitSegment(center, size));
        }

        return segments;
    }

    private int RemoveManagedFitProbes(GameObject root, string probeBaseName)
    {
        if (root == null)
            return 0;

        int removedCount = 0;
        var probes = root.GetComponentsInChildren<ReflectionProbe>(true);
        var objectsToRemove = new HashSet<GameObject>();
        for (int i = 0; i < probes.Length; i++)
        {
            var probe = probes[i];
            if (probe == null || IsGeneratedReflectionProbeVariant(probe))
                continue;

            if (IsManagedFitProbeName(probe.name, probeBaseName) && probe.gameObject != null)
                objectsToRemove.Add(probe.gameObject);
        }

        foreach (var probeObject in objectsToRemove)
        {
            if (probeObject == null)
                continue;

            UnityEngine.Object.DestroyImmediate(probeObject);
            removedCount++;
        }

        return removedCount;
    }

    private string GetFitProbeBaseName()
    {
        return string.IsNullOrWhiteSpace(newReflectionProbeName) ? "Reflection Probe" : newReflectionProbeName.Trim();
    }

    private static string BuildFitProbeName(string probeBaseName, int segmentIndex, int segmentCount, string powerSuffix)
    {
        string baseName = string.IsNullOrWhiteSpace(probeBaseName) ? "Reflection Probe" : probeBaseName;
        if (segmentCount <= 1)
            return $"{baseName}_{powerSuffix}";

        return $"{baseName}_S{segmentIndex:00}_{powerSuffix}";
    }

    private static bool IsManagedFitProbeName(string probeName, string probeBaseName)
    {
        if (string.IsNullOrWhiteSpace(probeName) || string.IsNullOrWhiteSpace(probeBaseName))
            return false;

        int suffixStart = probeName.LastIndexOf("_P", StringComparison.Ordinal);
        if (suffixStart < 0 || suffixStart + 2 >= probeName.Length)
            return false;

        for (int i = suffixStart + 2; i < probeName.Length; i++)
        {
            if (!char.IsDigit(probeName[i]))
                return false;
        }

        string stem = probeName.Substring(0, suffixStart);
        if (string.IsNullOrWhiteSpace(stem))
            return false;

        if (string.Equals(stem, probeBaseName, StringComparison.Ordinal))
            return true;

        string segmentPrefix = $"{probeBaseName}_S";
        if (!stem.StartsWith(segmentPrefix, StringComparison.Ordinal))
            return false;

        string segmentText = stem.Substring(segmentPrefix.Length);
        return int.TryParse(segmentText, out _);
    }

    private static void ApplyFitSegmentToProbe(ReflectionProbe probe, Transform tileTransform, ReflectionProbeFitSegment segment)
    {
        if (probe == null || tileTransform == null)
            return;

        Vector3 worldCenter = tileTransform.TransformPoint(segment.center);
        Vector3 probeSize = new Vector3(
            Mathf.Abs(segment.size.x),
            Mathf.Abs(segment.size.y),
            Mathf.Abs(segment.size.z));

        probe.transform.SetPositionAndRotation(worldCenter, tileTransform.rotation);
        probe.transform.localScale = Vector3.one;
        probe.center = Vector3.zero;
        probe.size = probeSize;
        probe.boxProjection = true;
        probe.mode = ReflectionProbeMode.Baked;
        probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
        probe.backgroundColor = Color.black;
        EditorUtility.SetDirty(probe);
    }

    private static ReflectionProbe CreateFitProbe(Transform parent, string probeName)
    {
        if (parent == null)
            return null;

        var probeObject = new GameObject(string.IsNullOrWhiteSpace(probeName) ? "Reflection Probe" : probeName);
        probeObject.transform.SetParent(parent, false);
        probeObject.transform.localPosition = Vector3.zero;
        probeObject.transform.localRotation = Quaternion.identity;
        probeObject.transform.localScale = Vector3.one;

        var probe = probeObject.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Baked;
        probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
        probe.backgroundColor = Color.black;
        return probe;
    }

    private void CreateVariantPrefab(string sourcePath, string outputPath, int yRotationDegrees)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(sourcePath);
        try
        {
            root.transform.localRotation = Quaternion.Euler(0f, yRotationDegrees, 0f);

            if (setAllowRotationFalse)
            {
                var tiles = root.GetComponentsInChildren<DunGen.Tile>(true);
                for (int i = 0; i < tiles.Length; i++)
                {
                    var so = new SerializedObject(tiles[i]);
                    var allowRotationProp = so.FindProperty("AllowRotation");
                    if (allowRotationProp != null)
                    {
                        allowRotationProp.boolValue = false;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }

            if (removeNavMeshLinksFromVariants)
                RemoveNavMeshLinks(root);

            PrefabUtility.SaveAsPrefabAsset(root, outputPath, out bool success);
            if (!success)
                throw new InvalidOperationException($"Failed to save prefab: {outputPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void RemoveNavMeshLinks(GameObject root)
    {
        var links = root.GetComponentsInChildren<NavMeshLink>(true);
        for (int i = links.Length - 1; i >= 0; i--)
            DestroyImmediate(links[i]);
    }

    private Dictionary<string, PowerBakeDataSet> BuildAndOptionallyBakeScene(List<string> prefabPaths, string resolvedBakedFolder, string bakeDataAssetRootFolder)
    {
        return BuildAndOptionallyBakeEachPrefab(prefabPaths, resolvedBakedFolder, bakeDataAssetRootFolder);
    }

    private Dictionary<string, PowerBakeDataSet> BuildAndOptionallyBakeEachPrefab(List<string> prefabPaths, string resolvedBakedFolder, string bakeDataAssetRootFolder)
    {
        var bakeDataAssets = new Dictionary<string, PowerBakeDataSet>(StringComparer.OrdinalIgnoreCase);
        var powerOptions = GetSelectedPowerBakeOptions();

        if (powerOptions.Count == 0)
            return bakeDataAssets;

        int totalPasses = Mathf.Max(1, prefabPaths.Count * powerOptions.Count);
        int passIndex = 0;

        for (int i = 0; i < prefabPaths.Count; i++)
        {
            string prefabPath = prefabPaths[i];
            string prefabName = Path.GetFileNameWithoutExtension(prefabPath);

            for (int powerIndex = 0; powerIndex < powerOptions.Count; powerIndex++)
            {
                var power = powerOptions[powerIndex];
                float progress = passIndex / (float)totalPasses;
                passIndex++;

                EditorUtility.DisplayProgressBar(WindowTitle, $"Building {prefabName} ({power.suffix})", progress);

                string scenePath = useFixedBakeScenePath
                    ? GetNormalizedBakeScenePath()
                    : BuildPerPrefabScenePath(prefabName, power.suffix);

                Scene scene = PrepareSceneForBake(scenePath);
                var root = CreateAutoBakeRoot(scene);
                var placed = PlacePrefabs(new List<string> { prefabPath }, scene, root);

                ApplyPowerBakeState(placed, power);
                ValidateDecalBakeFlags(placed, prefabName, power.suffix);
                CreateGeneratedLightProbeGroups(placed);

                if (overrideBakeAmbient)
                {
                    RenderSettings.ambientMode = AmbientMode.Flat;
                    RenderSettings.ambientLight = bakeAmbientColor;
                    RenderSettings.ambientIntensity = bakeAmbientIntensity;
                }

                ApplyBakeQualityOverride();

                if (!EditorSceneManager.SaveScene(scene, scenePath))
                    throw new InvalidOperationException($"Failed to save bake scene: {scenePath}");

                if (!runBakeAfterBuild)
                    continue;

                EditorUtility.DisplayProgressBar(WindowTitle, $"Baking {prefabName} ({power.suffix})", progress + (0.5f / totalPasses));
                bool baked = Lightmapping.Bake();
                if (!baked)
                    throw new InvalidOperationException($"Lightmapping.Bake failed for prefab: {prefabName} ({power.suffix})");

                EditorSceneManager.SaveScene(scene, scenePath);

                string dataFolder = GetSceneDataFolder(scene.path);
                if (collectBakedFilesToFolder)
                {
                    string collectedFolderName = BuildCollectedBakedFolderName(scenePath, power.suffix, prefabName);
                    dataFolder = CollectBakedFiles(scene.path, resolvedBakedFolder, collectedFolderName);
                }

                if (placed.Count == 0)
                    continue;

                string bakeDataAssetFolder = BuildBakeDataAssetFolder(bakeDataAssetRootFolder, prefabPath, power.suffix);
                string dataAssetPath = CreateBakeDataAssetForPrefab(prefabPath, placed[0].instance, bakeDataAssetFolder);
                if (string.IsNullOrEmpty(dataAssetPath))
                    continue;

                GetOrCreatePowerBakeDataSet(bakeDataAssets, prefabPath).Set(power.level, dataAssetPath);
            }
        }

        return bakeDataAssets;
    }

    private void ApplyBakeQualityOverride()
    {
        if (!overrideBakeQuality)
            return;

        LightmapEditorSettings.directSampleCount = Mathf.Max(1, bakeDirectSampleCount);
        LightmapEditorSettings.indirectSampleCount = Mathf.Max(1, bakeIndirectSampleCount);
        LightmapEditorSettings.environmentSampleCount = Mathf.Max(1, bakeEnvironmentSampleCount);
        LightmapEditorSettings.bounces = Mathf.Max(0, bakeBounces);
        LightmapEditorSettings.padding = Mathf.Max(2, bakePadding);
        LightmapEditorSettings.textureCompression = bakeTextureCompression;
    }

    private static PowerBakeDataSet GetOrCreatePowerBakeDataSet(Dictionary<string, PowerBakeDataSet> bakeDataAssets, string prefabPath)
    {
        if (!bakeDataAssets.TryGetValue(prefabPath, out var dataSet) || dataSet == null)
        {
            dataSet = new PowerBakeDataSet();
            bakeDataAssets[prefabPath] = dataSet;
        }

        return dataSet;
    }

    private string BuildPerPrefabScenePath(string prefabName, string powerSuffix)
    {
        string normalizedBasePath = GetNormalizedBakeScenePath();
        string dir = Path.GetDirectoryName(normalizedBasePath)?.Replace("\\", "/") ?? "Assets";
        string baseName = Path.GetFileNameWithoutExtension(normalizedBasePath);
        string safePrefabName = SanitizeFileName(prefabName);
        string safePowerSuffix = SanitizeFileName(powerSuffix);
        return $"{dir}/{baseName}_{safePrefabName}_{safePowerSuffix}.unity";
    }

    private string GetNormalizedBakeScenePath()
    {
        return NormalizeScenePath(bakeScenePath);
    }

    private static string NormalizeScenePath(string scenePath)
    {
        string normalized = (scenePath ?? string.Empty).Replace("\\", "/").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return "Assets/TileBake_Auto.unity";

        if (!normalized.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            normalized += ".unity";

        return normalized;
    }

    private string BuildCollectedBakedFolderName(string scenePath, string powerSuffix, string prefabName = null)
    {
        if (!useFixedBakeScenePath)
            return null;

        string normalizedPath = scenePath.Replace("\\", "/");
        string sceneName = Path.GetFileNameWithoutExtension(normalizedPath);
        string safePowerSuffix = SanitizeFileName(powerSuffix);
        if (string.IsNullOrWhiteSpace(prefabName))
            return $"{sceneName}_{safePowerSuffix}";

        return $"{sceneName}_{SanitizeFileName(prefabName)}_{safePowerSuffix}";
    }

    private Scene PrepareSceneForBake(string scenePath)
    {
        if (!useFixedBakeScenePath || !AssetExistsAtPath<SceneAsset>(scenePath))
            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var loadedScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        RemoveGeneratedSceneObjects(loadedScene);
        // Clear stale lighting reference so Unity bakes into fresh scene data folder,
        // preventing overwrite of previously collected bake results.
        Lightmapping.lightingDataAsset = null;
        LightmapSettings.lightmaps = Array.Empty<LightmapData>();
        return loadedScene;
    }

    private static bool AssetExistsAtPath<TAsset>(string assetPath) where TAsset : UnityEngine.Object
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return false;

        return AssetDatabase.LoadAssetAtPath<TAsset>(assetPath) != null;
    }

    private static void RemoveGeneratedSceneObjects(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        var roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            var root = roots[i];
            if (root == null)
                continue;

            if (!string.Equals(root.name, AutoBakeRootName, StringComparison.Ordinal))
                continue;

            DestroyImmediate(root);
        }
    }

    private static Transform CreateAutoBakeRoot(Scene scene)
    {
        var rootObject = new GameObject(AutoBakeRootName);
        SceneManager.MoveGameObjectToScene(rootObject, scene);
        return rootObject.transform;
    }

    private void ApplyPowerBakeState(List<PlacedPrefabInfo> placedPrefabs, PowerBakeOption power)
    {
        if (placedPrefabs == null || placedPrefabs.Count == 0)
            return;

        var emissionLookup = BuildEmissionMaterialLookup();

        for (int i = 0; i < placedPrefabs.Count; i++)
        {
            var instance = placedPrefabs[i].instance;
            if (instance == null)
                continue;

            ApplyPowerToLights(instance, power);
            ApplyPowerToEmissionMaterials(instance, power, emissionLookup);
        }
    }

    private int CreateGeneratedLightProbeGroups(List<PlacedPrefabInfo> placedPrefabs)
    {
        if (!bakeLightProbeData || placedPrefabs == null || placedPrefabs.Count == 0)
            return 0;

        int totalProbeCount = 0;
        for (int i = 0; i < placedPrefabs.Count; i++)
        {
            var instance = placedPrefabs[i].instance;
            if (instance == null)
                continue;

            totalProbeCount += CreateGeneratedLightProbeGroup(instance);
        }

        return totalProbeCount;
    }

    private int CreateGeneratedLightProbeGroup(GameObject prefabInstance)
    {
        if (prefabInstance == null)
            return 0;

        RemoveGeneratedLightProbeGroups(prefabInstance.transform);

        Transform probeRoot = prefabInstance.transform;
        Bounds localBounds;

        Component tileComponent = ResolveTileComponent(prefabInstance);
        if (tileComponent != null)
        {
            var recalculateMethod = tileComponent.GetType().GetMethod("RecalculateBounds");
            recalculateMethod?.Invoke(tileComponent, null);
            probeRoot = tileComponent.transform;

            if (!TryGetTileLocalBounds(tileComponent, out localBounds))
                localBounds = default;
        }
        else
        {
            localBounds = default;
        }

        if (localBounds.size.sqrMagnitude <= 0f && !TryCalculateRendererLocalBounds(probeRoot, out localBounds))
            return 0;

        Vector3[] samplePositions = BuildLightProbeSamplePositions(localBounds);
        if (samplePositions.Length == 0)
            return 0;

        var probeObject = new GameObject(GeneratedLightProbeGroupName);
        probeObject.transform.SetParent(probeRoot, false);
        probeObject.transform.localPosition = Vector3.zero;
        probeObject.transform.localRotation = Quaternion.identity;
        probeObject.transform.localScale = Vector3.one;

        var group = probeObject.AddComponent<LightProbeGroup>();
        group.probePositions = samplePositions;
        return samplePositions.Length;
    }

    private static Component ResolveTileComponent(GameObject root)
    {
        if (root == null)
            return null;

        Type tileType = FindTypeByName("DunGen.Tile") ?? FindTypeByName("Tile");
        return tileType != null ? root.GetComponentInChildren(tileType, true) : null;
    }

    private static int RemoveGeneratedLightProbeGroups(Transform root)
    {
        if (root == null)
            return 0;

        int removed = 0;
        var groups = root.GetComponentsInChildren<LightProbeGroup>(true);
        for (int i = groups.Length - 1; i >= 0; i--)
        {
            LightProbeGroup group = groups[i];
            if (group == null || group.name != GeneratedLightProbeGroupName)
                continue;

            DestroyImmediate(group.gameObject);
            removed++;
        }

        return removed;
    }

    private Vector3[] BuildLightProbeSamplePositions(Bounds localBounds)
    {
        int maxSamples = Mathf.Max(1, maxLightProbeSamples);
        var samples = new List<Vector3>(Mathf.Min(maxSamples, 64));

        float inset = Mathf.Max(0f, lightProbeWallInset);
        float minX = localBounds.min.x + inset;
        float maxX = localBounds.max.x - inset;
        float minZ = localBounds.min.z + inset;
        float maxZ = localBounds.max.z - inset;

        if (minX > maxX)
            minX = maxX = localBounds.center.x;

        if (minZ > maxZ)
            minZ = maxZ = localBounds.center.z;

        float minY = localBounds.min.y + 0.05f;
        float maxY = localBounds.max.y - 0.05f;
        if (minY > maxY)
            minY = maxY = localBounds.center.y;

        float heightA = Mathf.Clamp(localBounds.min.y + 1.0f, minY, maxY);
        float heightB = Mathf.Clamp(localBounds.min.y + 1.8f, minY, maxY);

        AddLightProbeSample(samples, new Vector3(localBounds.center.x, heightA, localBounds.center.z), maxSamples);
        if (Mathf.Abs(heightB - heightA) > 0.05f)
            AddLightProbeSample(samples, new Vector3(localBounds.center.x, heightB, localBounds.center.z), maxSamples);

        AddLightProbeGrid(samples, minX, maxX, minZ, maxZ, heightA, maxSamples);
        if (Mathf.Abs(heightB - heightA) > 0.05f)
            AddLightProbeGrid(samples, minX, maxX, minZ, maxZ, heightB, maxSamples);

        return samples.ToArray();
    }

    private void AddLightProbeGrid(List<Vector3> samples, float minX, float maxX, float minZ, float maxZ, float y, int maxSamples)
    {
        if (samples == null || samples.Count >= maxSamples)
            return;

        float spacing = Mathf.Max(0.5f, lightProbeGridSpacing);
        int xCount = Mathf.Max(1, Mathf.FloorToInt((maxX - minX) / spacing) + 1);
        int zCount = Mathf.Max(1, Mathf.FloorToInt((maxZ - minZ) / spacing) + 1);

        for (int z = 0; z < zCount; z++)
        {
            float tz = zCount == 1 ? 0.5f : z / (float)(zCount - 1);
            float sampleZ = Mathf.Lerp(minZ, maxZ, tz);

            for (int x = 0; x < xCount; x++)
            {
                float tx = xCount == 1 ? 0.5f : x / (float)(xCount - 1);
                float sampleX = Mathf.Lerp(minX, maxX, tx);
                AddLightProbeSample(samples, new Vector3(sampleX, y, sampleZ), maxSamples);

                if (samples.Count >= maxSamples)
                    return;
            }
        }
    }

    private static void AddLightProbeSample(List<Vector3> samples, Vector3 sample, int maxSamples)
    {
        if (samples == null || samples.Count >= maxSamples)
            return;

        for (int i = 0; i < samples.Count; i++)
        {
            if ((samples[i] - sample).sqrMagnitude < 0.01f)
                return;
        }

        samples.Add(sample);
    }

    private static bool TryCalculateRendererLocalBounds(Transform root, out Bounds localBounds)
    {
        localBounds = default;
        if (root == null)
            return false;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            Bounds worldBounds = renderer.bounds;
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;

            EncapsulateLocalPoint(root, min.x, min.y, min.z, ref localBounds, ref hasBounds);
            EncapsulateLocalPoint(root, min.x, min.y, max.z, ref localBounds, ref hasBounds);
            EncapsulateLocalPoint(root, min.x, max.y, min.z, ref localBounds, ref hasBounds);
            EncapsulateLocalPoint(root, min.x, max.y, max.z, ref localBounds, ref hasBounds);
            EncapsulateLocalPoint(root, max.x, min.y, min.z, ref localBounds, ref hasBounds);
            EncapsulateLocalPoint(root, max.x, min.y, max.z, ref localBounds, ref hasBounds);
            EncapsulateLocalPoint(root, max.x, max.y, min.z, ref localBounds, ref hasBounds);
            EncapsulateLocalPoint(root, max.x, max.y, max.z, ref localBounds, ref hasBounds);
        }

        return hasBounds && localBounds.size.sqrMagnitude > 0f;
    }

    private static void EncapsulateLocalPoint(Transform root, float x, float y, float z, ref Bounds localBounds, ref bool hasBounds)
    {
        Vector3 localPoint = root.InverseTransformPoint(new Vector3(x, y, z));
        if (!hasBounds)
        {
            localBounds = new Bounds(localPoint, Vector3.zero);
            hasBounds = true;
            return;
        }

        localBounds.Encapsulate(localPoint);
    }

    private Dictionary<Material, EmissionMaterialVariantSet> BuildEmissionMaterialLookup()
    {
        var lookup = new Dictionary<Material, EmissionMaterialVariantSet>();
        if (emissionMaterialVariants == null || emissionMaterialVariants.Count == 0)
            return lookup;

        for (int i = 0; i < emissionMaterialVariants.Count; i++)
        {
            var variantSet = emissionMaterialVariants[i];
            if (variantSet == null)
                continue;

            RegisterEmissionMaterialLookup(lookup, variantSet.sourceMaterial, variantSet);
            RegisterEmissionMaterialLookup(lookup, variantSet.power100Material, variantSet);
            RegisterEmissionMaterialLookup(lookup, variantSet.power00Material, variantSet);
        }

        return lookup;
    }

    private static void RegisterEmissionMaterialLookup(
        Dictionary<Material, EmissionMaterialVariantSet> lookup,
        Material material,
        EmissionMaterialVariantSet variantSet)
    {
        if (lookup == null || material == null || variantSet == null)
            return;

        if (!lookup.ContainsKey(material))
            lookup[material] = variantSet;
    }

    private static bool HasEnabledIgnoreLightControl(Component component)
    {
        if (component == null)
            return false;

        var marker = component.GetComponentInParent<IgnoreLightControl>(true);
        return marker != null && marker.enabled;
    }

    private static bool HasEnabledIgnoreEmissionControl(Component component)
    {
        if (component == null)
            return false;

        var marker = component.GetComponentInParent<IgnoreEmissionControl>(true);
        return marker != null && marker.enabled;
    }

    private static void ApplyPowerToLights(GameObject root, PowerBakeOption power)
    {
        var lights = root.GetComponentsInChildren<Light>(true);
        for (int i = 0; i < lights.Length; i++)
        {
            var lightComponent = lights[i];
            if (lightComponent == null)
                continue;

            if (HasEnabledIgnoreLightControl(lightComponent))
                continue;

            if (power.intensityScale <= 0.0001f)
            {
                lightComponent.enabled = false;
                continue;
            }

            if (!lightComponent.enabled)
                continue;

            lightComponent.intensity *= power.intensityScale;
        }
    }

    private static void ApplyPowerToEmissionMaterials(
        GameObject root,
        PowerBakeOption power,
        Dictionary<Material, EmissionMaterialVariantSet> emissionLookup)
    {
        if (emissionLookup == null || emissionLookup.Count == 0)
            return;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            if (HasEnabledIgnoreEmissionControl(renderer))
                continue;

            var materials = renderer.sharedMaterials;
            bool changed = false;

            for (int m = 0; m < materials.Length; m++)
            {
                var material = materials[m];
                if (material == null)
                    continue;

                if (!emissionLookup.TryGetValue(material, out var variantSet))
                    continue;

                var replacement = ResolvePowerMaterial(variantSet, power.level);
                if (replacement == null || replacement == material)
                    continue;

                materials[m] = replacement;
                changed = true;
            }

            if (changed)
            {
                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(renderer);
            }
        }
    }

    private static Material ResolvePowerMaterial(EmissionMaterialVariantSet variantSet, PowerBakeLevel level)
    {
        if (variantSet == null)
            return null;

        Material source = variantSet.sourceMaterial;
        switch (level)
        {
            case PowerBakeLevel.P100:
                return variantSet.power100Material != null ? variantSet.power100Material : source;
            default:
                if (variantSet.power00Material != null) return variantSet.power00Material;
                if (variantSet.power100Material != null) return variantSet.power100Material;
                return source;
        }
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "Unnamed";

        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
            value = value.Replace(invalid[i], '_');

        return value;
    }

    private string CollectBakedFiles(string scenePath, string targetRootFolder, string targetFolderNameOverride = null)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
            return string.Empty;

        string sourceDataFolder = ResolveCurrentBakeDataFolder(scenePath);
        if (!AssetDatabase.IsValidFolder(sourceDataFolder))
        {
            Debug.LogWarning($"[{WindowTitle}] Baked data folder not found: {sourceDataFolder}");
            return string.Empty;
        }

        EnsureFolder(targetRootFolder);
        string sceneName = Path.GetFileNameWithoutExtension(scenePath.Replace("\\", "/"));
        string targetFolderName = string.IsNullOrWhiteSpace(targetFolderNameOverride)
            ? sceneName
            : SanitizeFileName(targetFolderNameOverride);
        string targetDataFolder = $"{targetRootFolder.TrimEnd('/')}/{targetFolderName}";

        if (AssetDatabase.IsValidFolder(targetDataFolder))
        {
            if (!AssetDatabase.DeleteAsset(targetDataFolder))
                throw new InvalidOperationException($"Failed to clear previous baked folder: {targetDataFolder}");
        }

        string error = AssetDatabase.MoveAsset(sourceDataFolder, targetDataFolder);
        if (!string.IsNullOrEmpty(error))
            throw new InvalidOperationException($"Failed to move baked files: {error}");

        AssetDatabase.Refresh();

        return targetDataFolder;
    }

    private static string ResolveCurrentBakeDataFolder(string scenePath)
    {
        var lightingDataAsset = Lightmapping.lightingDataAsset;
        if (lightingDataAsset != null)
        {
            string lightingAssetPath = AssetDatabase.GetAssetPath(lightingDataAsset);
            if (!string.IsNullOrWhiteSpace(lightingAssetPath))
            {
                string folder = Path.GetDirectoryName(lightingAssetPath)?.Replace("\\", "/");
                if (!string.IsNullOrWhiteSpace(folder) && AssetDatabase.IsValidFolder(folder))
                    return folder;
            }
        }

        return GetSceneDataFolder(scenePath);
    }

    private static string GetSceneDataFolder(string scenePath)
    {
        string normalizedScenePath = scenePath.Replace("\\", "/");
        string sceneDir = Path.GetDirectoryName(normalizedScenePath)?.Replace("\\", "/") ?? "Assets";
        string sceneName = Path.GetFileNameWithoutExtension(normalizedScenePath);
        return $"{sceneDir}/{sceneName}";
    }

    private static string BuildBakeDataAssetFolder(string bakeDataAssetRootFolder, string prefabPath, string powerSuffix)
    {
        string normalizedRoot = string.IsNullOrWhiteSpace(bakeDataAssetRootFolder)
            ? "Assets/BakedData"
            : bakeDataAssetRootFolder.Replace("\\", "/").TrimEnd('/');

        string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
        string safePrefabName = SanitizeFileName(prefabName);
        string safePowerSuffix = string.IsNullOrWhiteSpace(powerSuffix) ? "P100" : SanitizeFileName(powerSuffix);
        return $"{normalizedRoot}/{safePrefabName}/{safePowerSuffix}";
    }

    private string CreateBakeDataAssetForPrefab(
        string prefabPath,
        GameObject prefabInstance,
        string targetDataFolder)
    {
        if (prefabInstance == null || string.IsNullOrWhiteSpace(targetDataFolder))
            return string.Empty;

        var bakeDataType = FindTypeByName("DungeonTileBakeData");
        if (bakeDataType == null)
            throw new InvalidOperationException("DungeonTileBakeData type not found. Ensure script is compiled.");

        EnsureFolder(targetDataFolder);

        string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
        string assetPath = $"{targetDataFolder}/{prefabName}_BakeData.asset";
        if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath) != null)
        {
            if (!AssetDatabase.DeleteAsset(assetPath))
                throw new InvalidOperationException($"Failed to overwrite bake data asset: {assetPath}");
        }

        var data = ScriptableObject.CreateInstance(bakeDataType);
        PopulateBakeDataFromScene(data, prefabInstance);

        AssetDatabase.CreateAsset(data, assetPath);
        EditorUtility.SetDirty(data);
        return assetPath;
    }

    private static void PopulateBakeDataFromScene(
        UnityEngine.Object dataObject,
        GameObject prefabInstance)
    {
        var serialized = new SerializedObject(dataObject);
        var lightmaps = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();

        var modeProp = serialized.FindProperty("lightmapsMode");
        if (modeProp != null)
        {
            var capturedMode = SanitizeLightmapsMode(LightmapSettings.lightmapsMode, lightmaps);
            modeProp.intValue = (int)capturedMode;
        }

        SetTextureArray(serialized.FindProperty("lightmapColors"), lightmaps, x => x.lightmapColor);
        SetTextureArray(serialized.FindProperty("lightmapDirections"), lightmaps, x => x.lightmapDir);

        var allRenderers = prefabInstance.GetComponentsInChildren<Renderer>(true);
        var renderers = new List<Renderer>(allRenderers.Length);
        for (int i = 0; i < allRenderers.Length; i++)
        {
            var renderer = allRenderers[i];
            if (renderer == null ||
                NewPrisonDecalUtility.IsWallDecalRenderer(renderer) ||
                NewPrisonReceptionWindowBakeSafeSetup.IsReceptionWindowVisualRenderer(renderer))
                continue;

            renderers.Add(renderer);
        }

        var entriesProp = serialized.FindProperty("rendererEntries");
        if (entriesProp != null)
        {
            entriesProp.arraySize = renderers.Count;
            Transform root = prefabInstance.transform;

            for (int i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i];
                var entry = entriesProp.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("relativePath").stringValue = renderer == null ? string.Empty : GetRelativePath(root, renderer.transform);
                entry.FindPropertyRelative("lightmapIndex").intValue = renderer == null ? -1 : renderer.lightmapIndex;
                entry.FindPropertyRelative("lightmapScaleOffset").vector4Value = renderer == null ? Vector4.zero : renderer.lightmapScaleOffset;
            }
        }

        var allReflectionProbes = prefabInstance.GetComponentsInChildren<ReflectionProbe>(true);
        var reflectionProbes = new List<ReflectionProbe>(allReflectionProbes.Length);
        for (int i = 0; i < allReflectionProbes.Length; i++)
        {
            var probe = allReflectionProbes[i];
            if (probe == null || IsGeneratedReflectionProbeVariant(probe))
                continue;

            reflectionProbes.Add(probe);
        }

        var reflectionEntriesProp = serialized.FindProperty("reflectionProbeEntries");
        if (reflectionEntriesProp != null)
        {
            reflectionEntriesProp.arraySize = reflectionProbes.Count;
            Transform root = prefabInstance.transform;

            for (int i = 0; i < reflectionProbes.Count; i++)
            {
                var probe = reflectionProbes[i];
                var entry = reflectionEntriesProp.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("relativePath").stringValue = probe == null ? string.Empty : GetRelativePath(root, probe.transform);
                entry.FindPropertyRelative("bakedTexture").objectReferenceValue = ResolveProbeBakedTexture(probe);
            }
        }

        PopulateLightProbeEntries(serialized, prefabInstance);

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void PopulateLightProbeEntries(SerializedObject serialized, GameObject prefabInstance)
    {
        var entriesProp = serialized.FindProperty("lightProbeEntries");
        if (entriesProp == null)
            return;

        if (!CollectGeneratedLightProbePositions(prefabInstance, out Vector3[] localPositions, out Vector3[] worldPositions))
        {
            entriesProp.arraySize = 0;
            return;
        }

        var probes = new SphericalHarmonicsL2[worldPositions.Length];
        var occlusions = new Vector4[worldPositions.Length];
        LightProbes.CalculateInterpolatedLightAndOcclusionProbes(worldPositions, probes, occlusions);

        entriesProp.arraySize = localPositions.Length;
        for (int i = 0; i < localPositions.Length; i++)
        {
            var entry = entriesProp.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("localPosition").vector3Value = localPositions[i];
            SetSHCoefficient(entry, "coefficient0", probes[i], 0);
            SetSHCoefficient(entry, "coefficient1", probes[i], 1);
            SetSHCoefficient(entry, "coefficient2", probes[i], 2);
            SetSHCoefficient(entry, "coefficient3", probes[i], 3);
            SetSHCoefficient(entry, "coefficient4", probes[i], 4);
            SetSHCoefficient(entry, "coefficient5", probes[i], 5);
            SetSHCoefficient(entry, "coefficient6", probes[i], 6);
            SetSHCoefficient(entry, "coefficient7", probes[i], 7);
            SetSHCoefficient(entry, "coefficient8", probes[i], 8);
            entry.FindPropertyRelative("occlusion").vector4Value = occlusions[i];
        }
    }

    private static bool CollectGeneratedLightProbePositions(
        GameObject prefabInstance,
        out Vector3[] localPositions,
        out Vector3[] worldPositions)
    {
        localPositions = Array.Empty<Vector3>();
        worldPositions = Array.Empty<Vector3>();

        if (prefabInstance == null)
            return false;

        var localList = new List<Vector3>();
        var worldList = new List<Vector3>();
        Transform root = prefabInstance.transform;
        var groups = prefabInstance.GetComponentsInChildren<LightProbeGroup>(true);

        for (int i = 0; i < groups.Length; i++)
        {
            LightProbeGroup group = groups[i];
            if (group == null || group.name != GeneratedLightProbeGroupName)
                continue;

            Vector3[] groupPositions = group.probePositions ?? Array.Empty<Vector3>();
            for (int p = 0; p < groupPositions.Length; p++)
            {
                Vector3 worldPosition = group.transform.TransformPoint(groupPositions[p]);
                worldList.Add(worldPosition);
                localList.Add(root.InverseTransformPoint(worldPosition));
            }
        }

        if (worldList.Count == 0)
            return false;

        localPositions = localList.ToArray();
        worldPositions = worldList.ToArray();
        return true;
    }

    private static void SetSHCoefficient(SerializedProperty entry, string propertyName, SphericalHarmonicsL2 probe, int coefficientIndex)
    {
        var property = entry.FindPropertyRelative(propertyName);
        if (property == null)
            return;

        property.vector3Value = new Vector3(probe[0, coefficientIndex], probe[1, coefficientIndex], probe[2, coefficientIndex]);
    }

    private static Cubemap ResolveProbeBakedTexture(ReflectionProbe probe)
    {
        if (probe == null)
            return null;

        if (probe.customBakedTexture is Cubemap customCubemap)
            return customCubemap;

        if (probe.bakedTexture is Cubemap bakedCubemap)
            return bakedCubemap;

        return null;
    }

    private static void SetTextureArray(SerializedProperty arrayProp, LightmapData[] lightmaps, Func<LightmapData, Texture2D> selector)
    {
        if (arrayProp == null)
            return;

        arrayProp.arraySize = lightmaps.Length;
        for (int i = 0; i < lightmaps.Length; i++)
        {
            var element = arrayProp.GetArrayElementAtIndex(i);
            element.objectReferenceValue = selector(lightmaps[i]);
        }
    }

    private static LightmapsMode SanitizeLightmapsMode(LightmapsMode sourceMode, LightmapData[] lightmaps)
    {
        if (sourceMode == LightmapsMode.NonDirectional || sourceMode == LightmapsMode.CombinedDirectional)
            return sourceMode;

        return HasDirectionalLightmap(lightmaps)
            ? LightmapsMode.CombinedDirectional
            : LightmapsMode.NonDirectional;
    }

    private static bool HasDirectionalLightmap(LightmapData[] lightmaps)
    {
        if (lightmaps == null || lightmaps.Length == 0)
            return false;

        for (int i = 0; i < lightmaps.Length; i++)
        {
            var lightmapData = lightmaps[i];
            if (lightmapData != null && lightmapData.lightmapDir != null)
                return true;
        }

        return false;
    }

    private static bool TryGetTileLocalBounds(Component tileComponent, out Bounds localBounds)
    {
        localBounds = default;
        if (tileComponent == null)
            return false;

        var serialized = new SerializedObject(tileComponent);
        var overrideProp = serialized.FindProperty("OverrideAutomaticTileBounds");
        bool useOverride = overrideProp != null && overrideProp.boolValue;

        var overrideBoundsProp = serialized.FindProperty("TileBoundsOverride");
        var placementProp = serialized.FindProperty("placement");
        var placementLocalBoundsProp = placementProp?.FindPropertyRelative("localBounds");

        if (useOverride && TryReadBoundsProperty(overrideBoundsProp, out localBounds))
            return localBounds.size.sqrMagnitude > 0f;

        if (TryReadBoundsProperty(placementLocalBoundsProp, out localBounds) && localBounds.size.sqrMagnitude > 0f)
            return true;

        if (TryReadBoundsProperty(overrideBoundsProp, out localBounds))
            return localBounds.size.sqrMagnitude > 0f;

        return false;
    }

    private static bool TryReadBoundsProperty(SerializedProperty boundsProp, out Bounds bounds)
    {
        bounds = default;
        if (boundsProp == null)
            return false;

        var centerProp = boundsProp.FindPropertyRelative("m_Center");
        var extentProp = boundsProp.FindPropertyRelative("m_Extent");
        if (centerProp == null || extentProp == null)
            return false;

        bounds = new Bounds(centerProp.vector3Value, extentProp.vector3Value * 2f);
        return true;
    }

    private static string GetRelativePath(Transform root, Transform target)
    {
        if (root == target)
            return string.Empty;

        var names = new Stack<string>();
        var current = target;
        while (current != null && current != root)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
    }

    private void AttachLightmapSwitchers(Dictionary<string, PowerBakeDataSet> bakeDataAssetsByPrefab)
    {
        if (bakeDataAssetsByPrefab == null || bakeDataAssetsByPrefab.Count == 0)
            return;

        var switcherType = FindTypeByName("DungeonTileLightmapSwitcher");
        var powerBakeSetType = FindTypeByName("DungeonTilePowerBakeSet");
        if (switcherType == null || powerBakeSetType == null)
        {
            Debug.LogWarning($"[{WindowTitle}] Required types not found (DungeonTileLightmapSwitcher / DungeonTilePowerBakeSet). Skipping switcher attachment.");
            return;
        }

        foreach (var pair in bakeDataAssetsByPrefab)
        {
            string prefabPath = pair.Key;
            var dataSet = pair.Value;
            if (dataSet == null || dataSet.CountAssigned() == 0)
                continue;

            var power100Data = AssetDatabase.LoadAssetAtPath<ScriptableObject>(dataSet.Get(PowerBakeLevel.P100));
            var power00Data = AssetDatabase.LoadAssetAtPath<ScriptableObject>(dataSet.Get(PowerBakeLevel.P00));

            ConfigurePowerBakeSet(prefabPath, powerBakeSetType, power100Data, power00Data);
            ConfigureLightmapSwitcher(prefabPath, switcherType, powerBakeSetType, 0);
            ConfigureReflectionProbeVariants(
                prefabPath,
                switcherType,
                power100Data,
                power00Data,
                autoCreatePowerProbeVariants);
        }
    }

    private void ConfigureLightmapSwitcher(string prefabPath, Type switcherType, Type powerBakeSetType, int startPowerLevel)
    {
        if (string.IsNullOrWhiteSpace(prefabPath) || switcherType == null || powerBakeSetType == null)
            return;

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var switcher = root.GetComponent(switcherType);
            if (switcher == null)
                switcher = root.AddComponent(switcherType);

            var powerBakeSet = root.GetComponent(powerBakeSetType);
            if (powerBakeSet == null)
                powerBakeSet = root.AddComponent(powerBakeSetType);

            var serialized = new SerializedObject(switcher);
            var powerSetProp = serialized.FindProperty("powerBakeSet");
            var startPowerProp = serialized.FindProperty("startPowerLevel");
            var disablePower0ReflectionProp = serialized.FindProperty("disableReflectionProbesOnPower0");

            if (powerSetProp != null) powerSetProp.objectReferenceValue = powerBakeSet;
            if (startPowerProp != null) startPowerProp.intValue = startPowerLevel;
            if (disablePower0ReflectionProp != null) disablePower0ReflectionProp.boolValue = true;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(root);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
            if (!success)
                throw new InvalidOperationException($"Failed to save switcher component to prefab: {prefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private void ConfigureReflectionProbeVariants(
        string prefabPath,
        Type switcherType,
        ScriptableObject power100Data,
        ScriptableObject power00Data,
        bool enableVariantSet)
    {
        if (string.IsNullOrWhiteSpace(prefabPath) || switcherType == null)
            return;

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var switcher = root.GetComponent(switcherType);
            if (switcher == null)
                switcher = root.AddComponent(switcherType);

            RemoveGeneratedReflectionProbeVariants(root.transform);

            var variantEntries = enableVariantSet
                ? BuildRuntimeReflectionProbeVariantEntries(root.transform, power100Data, power00Data)
                : new List<RuntimeReflectionProbeVariantEntry>();

            var serialized = new SerializedObject(switcher);
            var applyModeProp = serialized.FindProperty("reflectionProbeApplyMode");
            var variantsProp = serialized.FindProperty("reflectionProbeVariantEntries");

            if (variantsProp != null)
                PopulateReflectionProbeVariantEntries(variantsProp, variantEntries);

            if (applyModeProp != null)
            {
                applyModeProp.intValue = variantEntries.Count > 0
                    ? ReflectionProbeApplyModeProbeVariantSet
                    : ReflectionProbeApplyModeCustomFromBakeData;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(root);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
            if (!success)
                throw new InvalidOperationException($"Failed to save reflection probe variants to prefab: {prefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void PopulateReflectionProbeVariantEntries(
        SerializedProperty entriesProp,
        List<RuntimeReflectionProbeVariantEntry> runtimeEntries)
    {
        if (entriesProp == null)
            return;

        int count = runtimeEntries?.Count ?? 0;
        entriesProp.arraySize = count;

        for (int i = 0; i < count; i++)
        {
            var serializedEntry = entriesProp.GetArrayElementAtIndex(i);
            var runtimeEntry = runtimeEntries[i];

            var relativePathProp = serializedEntry.FindPropertyRelative("relativePath");
            var bucketIndexProp = serializedEntry.FindPropertyRelative("probeBucketIndex");
            var p100Prop = serializedEntry.FindPropertyRelative("power100Probe");
            var p00Prop = serializedEntry.FindPropertyRelative("power00Probe");

            if (relativePathProp != null) relativePathProp.stringValue = runtimeEntry.relativePath;
            if (bucketIndexProp != null) bucketIndexProp.intValue = runtimeEntry.probeBucketIndex;
            if (p100Prop != null) p100Prop.objectReferenceValue = runtimeEntry.power100Probe;
            if (p00Prop != null) p00Prop.objectReferenceValue = runtimeEntry.power00Probe;
        }
    }

    private List<RuntimeReflectionProbeVariantEntry> BuildRuntimeReflectionProbeVariantEntries(
        Transform root,
        ScriptableObject power100Data,
        ScriptableObject power00Data)
    {
        var entries = new List<RuntimeReflectionProbeVariantEntry>();
        if (root == null)
            return entries;

        var textureSets = BuildReflectionProbeTextureSets(power100Data, power00Data);
        if (textureSets.Count == 0)
            return entries;

        var sourceProbes = root.GetComponentsInChildren<ReflectionProbe>(true);
        var fitGroups = new Dictionary<string, FitPowerProbeGroup>(StringComparer.Ordinal);
        var generatorSourceProbes = new List<ReflectionProbe>(sourceProbes.Length);

        for (int i = 0; i < sourceProbes.Length; i++)
        {
            var sourceProbe = sourceProbes[i];
            if (sourceProbe == null || IsGeneratedReflectionProbeVariant(sourceProbe))
                continue;

            string relativePath = GetRelativePath(root, sourceProbe.transform);
            string normalizedPath = NormalizeReflectionProbeRelativePath(relativePath);
            if (TryGetPowerSuffixFromProbeName(sourceProbe.name, out string powerSuffix))
            {
                if (!fitGroups.TryGetValue(normalizedPath, out var group) || group == null)
                {
                    group = new FitPowerProbeGroup
                    {
                        normalizedPath = normalizedPath,
                        representativeRelativePath = relativePath
                    };
                    fitGroups[normalizedPath] = group;
                }

                switch (powerSuffix)
                {
                    case "P100":
                        if (group.power100Probe == null) group.power100Probe = sourceProbe;
                        break;
                    case "P0":
                        if (group.power00Probe == null) group.power00Probe = sourceProbe;
                        break;
                }

                continue;
            }

            generatorSourceProbes.Add(sourceProbe);
        }

        foreach (var pair in fitGroups)
        {
            var group = pair.Value;
            if (group == null)
                continue;

            string key = BuildReflectionProbeEntryKey(group.normalizedPath, 0);
            if (textureSets.TryGetValue(key, out var textures) && textures != null)
            {
                ApplyCustomTextureToProbe(group.power100Probe, textures.power100Texture);
                ApplyCustomTextureToProbe(group.power00Probe, textures.power00Texture);
            }

            if (group.power100Probe == null && group.power00Probe == null)
                continue;

            entries.Add(new RuntimeReflectionProbeVariantEntry(
                group.representativeRelativePath,
                -1,
                group.power100Probe,
                group.power00Probe));
        }

        var pathCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < generatorSourceProbes.Count; i++)
        {
            var sourceProbe = generatorSourceProbes[i];

            string relativePath = GetRelativePath(root, sourceProbe.transform);
            string normalizedPath = NormalizeReflectionProbeRelativePath(relativePath);
            int bucketIndex = 0;
            if (pathCounts.TryGetValue(normalizedPath, out int currentCount))
                bucketIndex = currentCount;
            pathCounts[normalizedPath] = bucketIndex + 1;

            string key = BuildReflectionProbeEntryKey(normalizedPath, bucketIndex);
            if (!textureSets.TryGetValue(key, out var textures) || textures == null)
                continue;

            var p100Probe = CreateReflectionProbeVariant(root, sourceProbe, bucketIndex, "P100", textures.power100Texture);
            var p00Probe = CreateReflectionProbeVariant(root, sourceProbe, bucketIndex, "P0", textures.power00Texture);

            if (p100Probe == null && p00Probe == null)
                continue;

            entries.Add(new RuntimeReflectionProbeVariantEntry(
                relativePath,
                bucketIndex,
                p100Probe,
                p00Probe));
        }

        return entries;
    }

    private static void ApplyCustomTextureToProbe(ReflectionProbe probe, Cubemap cubemap)
    {
        if (probe == null || cubemap == null)
            return;

        probe.customBakedTexture = cubemap;
        probe.mode = ReflectionProbeMode.Custom;
        probe.enabled = false;
        EditorUtility.SetDirty(probe);
    }

    private static ReflectionProbe CreateReflectionProbeVariant(
        Transform root,
        ReflectionProbe sourceProbe,
        int bucketIndex,
        string powerSuffix,
        Cubemap cubemap)
    {
        if (root == null || sourceProbe == null || cubemap == null)
            return null;

        string sourceName = sourceProbe.gameObject.name;
        var variantObject = new GameObject($"{GeneratedReflectionProbeVariantNamePrefix}{sourceName}_{bucketIndex}_{powerSuffix}");

        Transform sourceParent = sourceProbe.transform.parent;
        if (sourceParent != null)
            variantObject.transform.SetParent(sourceParent, false);
        else
            variantObject.transform.SetParent(root, false);

        variantObject.transform.localPosition = sourceProbe.transform.localPosition;
        variantObject.transform.localRotation = sourceProbe.transform.localRotation;
        variantObject.transform.localScale = sourceProbe.transform.localScale;

        var variantProbe = variantObject.AddComponent<ReflectionProbe>();
        EditorUtility.CopySerialized(sourceProbe, variantProbe);
        variantProbe.mode = ReflectionProbeMode.Custom;
        variantProbe.customBakedTexture = cubemap;
        variantProbe.enabled = false;
        EditorUtility.SetDirty(variantProbe);
        return variantProbe;
    }

    private static Dictionary<string, ReflectionProbeTextureSet> BuildReflectionProbeTextureSets(
        ScriptableObject power100Data,
        ScriptableObject power00Data)
    {
        var textureSets = new Dictionary<string, ReflectionProbeTextureSet>(StringComparer.Ordinal);
        MergeReflectionProbeTextures(textureSets, power100Data, PowerBakeLevel.P100);
        MergeReflectionProbeTextures(textureSets, power00Data, PowerBakeLevel.P00);
        return textureSets;
    }

    private static void MergeReflectionProbeTextures(
        Dictionary<string, ReflectionProbeTextureSet> textureSets,
        ScriptableObject bakeData,
        PowerBakeLevel level)
    {
        if (textureSets == null || bakeData == null)
            return;

        var serialized = new SerializedObject(bakeData);
        var entriesProp = serialized.FindProperty("reflectionProbeEntries");
        if (entriesProp == null || !entriesProp.isArray)
            return;

        var pathCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < entriesProp.arraySize; i++)
        {
            var entry = entriesProp.GetArrayElementAtIndex(i);
            if (entry == null)
                continue;

            string relativePath = entry.FindPropertyRelative("relativePath")?.stringValue ?? string.Empty;
            string normalizedPath = NormalizeReflectionProbeRelativePath(relativePath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
                continue;

            int bucketIndex = 0;
            if (pathCounts.TryGetValue(normalizedPath, out int currentCount))
                bucketIndex = currentCount;
            pathCounts[normalizedPath] = bucketIndex + 1;

            var bakedTexture = entry.FindPropertyRelative("bakedTexture")?.objectReferenceValue as Cubemap;
            string key = BuildReflectionProbeEntryKey(normalizedPath, bucketIndex);
            if (!textureSets.TryGetValue(key, out var textureSet) || textureSet == null)
            {
                textureSet = new ReflectionProbeTextureSet();
                textureSets[key] = textureSet;
            }

            switch (level)
            {
                case PowerBakeLevel.P100:
                    textureSet.power100Texture = bakedTexture;
                    break;
                default:
                    textureSet.power00Texture = bakedTexture;
                    break;
            }
        }
    }

    private static string BuildReflectionProbeEntryKey(string relativePath, int bucketIndex)
    {
        return $"{relativePath}#{bucketIndex}";
    }

    private static string NormalizeReflectionProbeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return relativePath;

        string[] segments = relativePath.Split('/');
        if (segments.Length == 0)
            return relativePath;

        int lastIndex = segments.Length - 1;
        string lastSegment = segments[lastIndex];
        if (TryGetPowerSuffixFromProbeName(lastSegment, out string suffix))
        {
            int suffixLength = suffix.Length + 1;
            if (lastSegment.Length > suffixLength)
                segments[lastIndex] = lastSegment.Substring(0, lastSegment.Length - suffixLength);
        }

        return string.Join("/", segments);
    }

    private static bool TryGetPowerSuffixFromProbeName(string probeName, out string suffix)
    {
        suffix = null;
        if (string.IsNullOrWhiteSpace(probeName))
            return false;

        for (int i = 0; i < FitPowerProbeSuffixes.Length; i++)
        {
            string candidate = FitPowerProbeSuffixes[i];
            if (probeName.EndsWith($"_{candidate}", StringComparison.Ordinal))
            {
                suffix = candidate;
                return true;
            }
        }

        return false;
    }

    private static int RemoveGeneratedReflectionProbeVariants(Transform root)
    {
        if (root == null)
            return 0;

        int removedCount = 0;
        var probes = root.GetComponentsInChildren<ReflectionProbe>(true);
        for (int i = 0; i < probes.Length; i++)
        {
            var probe = probes[i];
            if (!IsGeneratedReflectionProbeVariant(probe))
                continue;

            if (probe != null)
            {
                DestroyImmediate(probe.gameObject);
                removedCount++;
            }
        }

        return removedCount;
    }

    private static bool IsGeneratedReflectionProbeVariant(ReflectionProbe probe)
    {
        if (probe == null)
            return false;

        string name = probe.gameObject != null ? probe.gameObject.name : string.Empty;
        return !string.IsNullOrEmpty(name) &&
               name.StartsWith(GeneratedReflectionProbeVariantNamePrefix, StringComparison.Ordinal);
    }

    private void ConfigurePowerBakeSet(
        string prefabPath,
        Type powerBakeSetType,
        ScriptableObject power100Data,
        ScriptableObject power00Data)
    {
        if (string.IsNullOrWhiteSpace(prefabPath) || powerBakeSetType == null)
            return;

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var powerBakeSet = root.GetComponent(powerBakeSetType);
            if (powerBakeSet == null)
                powerBakeSet = root.AddComponent(powerBakeSetType);

            var serialized = new SerializedObject(powerBakeSet);
            var p100Prop = serialized.FindProperty("power100Bake");
            var p00Prop = serialized.FindProperty("power00Bake");
            var p100ScaleProp = serialized.FindProperty("power100LightIntensityScale");
            var p00ScaleProp = serialized.FindProperty("power00LightIntensityScale");
            var emissionEntriesProp = serialized.FindProperty("emissionMaterialEntries");

            if (p100Prop != null) p100Prop.objectReferenceValue = power100Data;
            if (p00Prop != null) p00Prop.objectReferenceValue = power00Data;

            if (p100ScaleProp != null) p100ScaleProp.floatValue = 1f;
            if (p00ScaleProp != null) p00ScaleProp.floatValue = 0f;

            if (emissionEntriesProp != null)
                PopulateEmissionEntries(emissionEntriesProp, root.transform);

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(root);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
            if (!success)
                throw new InvalidOperationException($"Failed to save power bake set to prefab: {prefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private void PopulateEmissionEntries(SerializedProperty entriesProp, Transform root)
    {
        if (entriesProp == null)
            return;

        var entries = BuildRuntimeEmissionEntries(root);
        entriesProp.arraySize = entries.Count;

        for (int i = 0; i < entries.Count; i++)
        {
            var serializedEntry = entriesProp.GetArrayElementAtIndex(i);
            var runtimeEntry = entries[i];

            var relativePathProp = serializedEntry.FindPropertyRelative("relativePath");
            var bucketIndexProp = serializedEntry.FindPropertyRelative("rendererBucketIndex");
            var materialIndexProp = serializedEntry.FindPropertyRelative("materialIndex");
            var p100Prop = serializedEntry.FindPropertyRelative("power100Material");
            var p00Prop = serializedEntry.FindPropertyRelative("power00Material");

            if (relativePathProp != null) relativePathProp.stringValue = runtimeEntry.relativePath;
            if (bucketIndexProp != null) bucketIndexProp.intValue = runtimeEntry.rendererBucketIndex;
            if (materialIndexProp != null) materialIndexProp.intValue = runtimeEntry.materialIndex;
            if (p100Prop != null) p100Prop.objectReferenceValue = runtimeEntry.power100Material;
            if (p00Prop != null) p00Prop.objectReferenceValue = runtimeEntry.power00Material;
        }
    }

    private List<RuntimeEmissionEntry> BuildRuntimeEmissionEntries(Transform root)
    {
        var entries = new List<RuntimeEmissionEntry>();
        if (root == null)
            return entries;

        var lookup = BuildEmissionMaterialLookup();
        if (lookup.Count == 0)
            return entries;

        var pathCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var renderers = root.GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            if (HasEnabledIgnoreEmissionControl(renderer))
                continue;

            string relativePath = GetRelativePath(root, renderer.transform);
            int bucketIndex = 0;
            if (pathCounts.TryGetValue(relativePath, out int currentCount))
                bucketIndex = currentCount;
            pathCounts[relativePath] = bucketIndex + 1;

            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
                continue;

            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                var currentMaterial = materials[materialIndex];
                if (currentMaterial == null)
                    continue;

                if (!lookup.TryGetValue(currentMaterial, out var variantSet))
                    continue;

                var power100Material = ResolvePowerMaterial(variantSet, PowerBakeLevel.P100);
                var power00Material = ResolvePowerMaterial(variantSet, PowerBakeLevel.P00);

                if (power100Material == power00Material)
                    continue;

                entries.Add(new RuntimeEmissionEntry(
                    relativePath,
                    bucketIndex,
                    materialIndex,
                    power100Material,
                    power00Material));
            }
        }

        return entries;
    }

    private static Type FindTypeByName(string typeName)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            var assembly = assemblies[i];
            var type = assembly.GetType(typeName);
            if (type != null)
                return type;

            type = assembly.GetType($"{typeof(DungeonTileRotationBakeTool).Namespace}.{typeName}");
            if (type != null)
                return type;
        }

        return null;
    }

    private List<PlacedPrefabInfo> PlacePrefabs(List<string> prefabPaths, Scene scene, Transform root)
    {
        var placed = new List<PlacedPrefabInfo>(prefabPaths.Count);

        for (int i = 0; i < prefabPaths.Count; i++)
        {
            string path = prefabPaths[i];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
                continue;

            instance.transform.SetParent(root, true);
            instance.transform.position = Vector3.zero;

            MarkContributeGIRecursive(instance);
            if (prepareDoorwayHoleWallsForBake)
                PrepareDoorwayHoleWallsForBake(instance);
            placed.Add(new PlacedPrefabInfo(path, instance));
        }

        return placed;
    }

    private static void PrepareDoorwayHoleWallsForBake(GameObject root)
    {
        if (root == null)
            return;

        var transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform current = transforms[i];
            if (current == null)
                continue;

            string name = current.name;
            bool holeWall = IsDoorwayHoleWallName(name);
            bool blocker = IsDoorwayBlockerName(name);
            if (!holeWall && !blocker)
                continue;

            current.gameObject.SetActive(true);
            var flags = GameObjectUtility.GetStaticEditorFlags(current.gameObject);
            flags |= StaticEditorFlags.ContributeGI;
            GameObjectUtility.SetStaticEditorFlags(current.gameObject, flags);

            var renderers = current.GetComponentsInChildren<MeshRenderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                MeshRenderer renderer = renderers[r];
                if (renderer == null)
                    continue;

                renderer.enabled = true;
                if (blocker)
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }
    }

    private static bool IsDoorwayHoleWallName(string name)
    {
        return name == "Door_Placement" ||
               name == "No_Door_Placement" ||
               name == "No_DoorPlacement";
    }

    private static bool IsDoorwayBlockerName(string name)
    {
        return name.StartsWith("Blocker_SM", StringComparison.Ordinal) ||
               name.StartsWith("Blocker_LG", StringComparison.Ordinal);
    }

    private static void MarkContributeGIRecursive(GameObject root)
    {
        var stack = new Stack<BakeStaticFlagWorkItem>();
        stack.Push(new BakeStaticFlagWorkItem(root.transform, false));

        while (stack.Count > 0)
        {
            BakeStaticFlagWorkItem item = stack.Pop();
            Transform current = item.transform;
            var gameObject = current.gameObject;
            bool insideBakeExcludedSubtree = item.insideBakeExcludedSubtree ||
                NewPrisonDecalUtility.IsWallDecalTransformRoot(current) ||
                NewPrisonReceptionWindowBakeSafeSetup.IsReceptionWindowVisualTransform(current);

            var flags = GameObjectUtility.GetStaticEditorFlags(gameObject);
            if (insideBakeExcludedSubtree)
                flags &= ~StaticEditorFlags.ContributeGI;
            else
                flags |= StaticEditorFlags.ContributeGI;
            GameObjectUtility.SetStaticEditorFlags(gameObject, flags);

            for (int i = 0; i < current.childCount; i++)
                stack.Push(new BakeStaticFlagWorkItem(current.GetChild(i), insideBakeExcludedSubtree));
        }
    }

    private static void ValidateDecalBakeFlags(
        List<PlacedPrefabInfo> placed,
        string prefabName,
        string powerSuffix)
    {
        if (placed == null)
            return;

        for (int p = 0; p < placed.Count; p++)
        {
            var info = placed[p];
            if (info.instance == null)
                continue;

            Transform root = info.instance.transform;
            var renderers = info.instance.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                bool isReceptionWindowVisual = NewPrisonReceptionWindowBakeSafeSetup.IsReceptionWindowVisualRenderer(renderer);
                if (!isReceptionWindowVisual && !NewPrisonDecalUtility.IsDecalRenderer(renderer))
                    continue;

                var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                bool hasContributeGi = (flags & StaticEditorFlags.ContributeGI) != 0;
                if (isReceptionWindowVisual)
                {
                    if (hasContributeGi ||
                        renderer.shadowCastingMode != ShadowCastingMode.Off ||
                        renderer.receiveShadows)
                    {
                        string path = GetRelativePath(root, renderer.transform);
                        throw new InvalidOperationException(
                            $"Reception window visual renderer is bake-active before bake: {prefabName} ({powerSuffix}) {path}");
                    }

                    continue;
                }

                bool isFloorDecal = NewPrisonDecalUtility.IsFloorDecalRenderer(renderer);
                if (!isFloorDecal && hasContributeGi)
                {
                    string path = GetRelativePath(root, renderer.transform);
                    throw new InvalidOperationException(
                        $"Wall decal renderer has ContributeGI before bake: {prefabName} ({powerSuffix}) {path}");
                }

                if (isFloorDecal && !hasContributeGi)
                {
                    string path = GetRelativePath(root, renderer.transform);
                    throw new InvalidOperationException(
                        $"Floor decal renderer is missing ContributeGI before bake: {prefabName} ({powerSuffix}) {path}");
                }
            }
        }
    }

    private readonly struct BakeStaticFlagWorkItem
    {
        public readonly Transform transform;
        public readonly bool insideBakeExcludedSubtree;

        public BakeStaticFlagWorkItem(Transform transform, bool insideBakeExcludedSubtree)
        {
            this.transform = transform;
            this.insideBakeExcludedSubtree = insideBakeExcludedSubtree;
        }
    }

    private static void EnsureFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        string normalized = folderPath.Replace("\\", "/");
        if (AssetDatabase.IsValidFolder(normalized))
            return;

        string[] parts = normalized.Split('/');
        if (parts.Length == 0)
            return;

        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private void LoadEmissionMaterialVariants()
    {
        if (emissionVariantsLoaded)
            return;

        emissionVariantsLoaded = true;
        isLoadingEmissionVariants = true;
        try
        {
            if (!EditorPrefs.HasKey(EmissionVariantsEditorPrefsKey))
                return;

            string json = EditorPrefs.GetString(EmissionVariantsEditorPrefsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var saveCollection = JsonUtility.FromJson<EmissionMaterialVariantSetSaveCollection>(json);
            if (saveCollection == null || saveCollection.entries == null)
                return;

            var loaded = new List<EmissionMaterialVariantSet>(saveCollection.entries.Count);
            for (int i = 0; i < saveCollection.entries.Count; i++)
            {
                var saved = saveCollection.entries[i];
                if (saved == null)
                    continue;

                loaded.Add(new EmissionMaterialVariantSet
                {
                    label = string.IsNullOrWhiteSpace(saved.label) ? "Material" : saved.label,
                    sourceMaterial = LoadMaterialAtPath(saved.sourceMaterialPath),
                    power100Material = LoadMaterialAtPath(saved.power100MaterialPath),
                    power00Material = LoadMaterialAtPath(saved.power00MaterialPath)
                });
            }

            emissionMaterialVariants = loaded;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[{WindowTitle}] Failed to load emission variant preset: {ex.Message}");
        }
        finally
        {
            isLoadingEmissionVariants = false;
            AppendMissingDefaultEmissionMaterialVariants();
        }
    }

    private void AppendMissingDefaultEmissionMaterialVariants()
    {
        if (emissionMaterialVariants == null)
            emissionMaterialVariants = new List<EmissionMaterialVariantSet>();

        for (int i = 0; i < DefaultEmissionMaterialVariants.Length; i++)
        {
            var defaultVariant = DefaultEmissionMaterialVariants[i];
            var source = LoadMaterialAtPath(defaultVariant.sourcePath);
            var power100 = LoadMaterialAtPath(defaultVariant.power100Path);
            var power00 = LoadMaterialAtPath(defaultVariant.power00Path);
            if (source == null && power100 == null && power00 == null)
                continue;

            var existing = FindEmissionVariantBySource(source);
            if (existing != null)
            {
                if (string.IsNullOrWhiteSpace(existing.label))
                    existing.label = defaultVariant.label;

                if (existing.power100Material == null && power100 != null)
                    existing.power100Material = power100;

                if (existing.power00Material == null && power00 != null)
                    existing.power00Material = power00;

                continue;
            }

            if (ContainsEmissionVariant(source, power100, power00))
                continue;

            emissionMaterialVariants.Add(new EmissionMaterialVariantSet
            {
                label = defaultVariant.label,
                sourceMaterial = source,
                power100Material = power100,
                power00Material = power00
            });
        }
    }

    private EmissionMaterialVariantSet FindEmissionVariantBySource(Material source)
    {
        if (source == null || emissionMaterialVariants == null)
            return null;

        for (int i = 0; i < emissionMaterialVariants.Count; i++)
        {
            var variant = emissionMaterialVariants[i];
            if (variant != null && variant.sourceMaterial == source)
                return variant;
        }

        return null;
    }

    private bool ContainsEmissionVariant(Material source, Material power100, Material power00)
    {
        if (emissionMaterialVariants == null)
            return false;

        for (int i = 0; i < emissionMaterialVariants.Count; i++)
        {
            var variant = emissionMaterialVariants[i];
            if (variant == null)
                continue;

            if (source != null)
            {
                if (variant.sourceMaterial == source)
                    return true;

                continue;
            }

            if (power100 != null && variant.power100Material == power100)
                return true;

            if (power00 != null && variant.power00Material == power00)
                return true;
        }

        return false;
    }

    private void SaveEmissionMaterialVariants()
    {
        if (isLoadingEmissionVariants)
            return;

        if (emissionMaterialVariants == null || emissionMaterialVariants.Count == 0)
        {
            EditorPrefs.DeleteKey(EmissionVariantsEditorPrefsKey);
            return;
        }

        var saveCollection = new EmissionMaterialVariantSetSaveCollection();
        for (int i = 0; i < emissionMaterialVariants.Count; i++)
        {
            var variant = emissionMaterialVariants[i];
            if (variant == null)
                continue;

            saveCollection.entries.Add(new EmissionMaterialVariantSetSaveData
            {
                label = variant.label,
                sourceMaterialPath = GetAssetPath(variant.sourceMaterial),
                power100MaterialPath = GetAssetPath(variant.power100Material),
                power00MaterialPath = GetAssetPath(variant.power00Material)
            });
        }

        string json = JsonUtility.ToJson(saveCollection);
        EditorPrefs.SetString(EmissionVariantsEditorPrefsKey, json);
    }

    private static string GetAssetPath(Material material)
    {
        if (material == null)
            return string.Empty;

        return AssetDatabase.GetAssetPath(material) ?? string.Empty;
    }

    private static Material LoadMaterialAtPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    private void LoadChecklistState()
    {
        if (checklistLoaded)
            return;

        checklistLoaded = true;
        isLoadingChecklist = true;
        try
        {
            if (!EditorPrefs.HasKey(ToolChecklistEditorPrefsKey))
                return;

            string json = EditorPrefs.GetString(ToolChecklistEditorPrefsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var saved = JsonUtility.FromJson<ToolChecklistSaveData>(json);
            if (saved == null)
                return;

            bool hasFixedScenePathField =
                json.IndexOf("\"useFixedBakeScenePath\"", StringComparison.Ordinal) >= 0;
            bool hasAutoProbeVariantField =
                json.IndexOf("\"autoCreatePowerProbeVariants\"", StringComparison.Ordinal) >= 0;
            bool hasRemoveNavMeshLinksField =
                json.IndexOf("\"removeNavMeshLinksFromVariants\"", StringComparison.Ordinal) >= 0;
            bool hasFitBeforeBakeField =
                json.IndexOf("\"fitReflectionProbesBeforeBake\"", StringComparison.Ordinal) >= 0;
            bool hasAutoSegmentProbeField =
                json.IndexOf("\"autoSegmentReflectionProbes\"", StringComparison.Ordinal) >= 0;
            bool hasSegmentThresholdField =
                json.IndexOf("\"segmentOnlyWhenLongestAxisOver\"", StringComparison.Ordinal) >= 0;
            bool hasTargetSegmentLengthField =
                json.IndexOf("\"targetProbeSegmentLength\"", StringComparison.Ordinal) >= 0;
            bool hasMaxSegmentField =
                json.IndexOf("\"maxReflectionProbeSegments\"", StringComparison.Ordinal) >= 0;
            bool hasSegmentOverlapField =
                json.IndexOf("\"reflectionProbeSegmentOverlap\"", StringComparison.Ordinal) >= 0;
            bool hasAutoNormalizeNewPrisonRoomsField =
                json.IndexOf("\"autoNormalizeNewPrisonRoomsBeforeBake\"", StringComparison.Ordinal) >= 0;
            bool hasLightProbeDataField =
                json.IndexOf("\"bakeLightProbeData\"", StringComparison.Ordinal) >= 0;
            bool hasLightProbeGridSpacingField =
                json.IndexOf("\"lightProbeGridSpacing\"", StringComparison.Ordinal) >= 0;
            bool hasLightProbeWallInsetField =
                json.IndexOf("\"lightProbeWallInset\"", StringComparison.Ordinal) >= 0;
            bool hasMaxLightProbeSamplesField =
                json.IndexOf("\"maxLightProbeSamples\"", StringComparison.Ordinal) >= 0;
            bool hasDoorwayHoleWallsField =
                json.IndexOf("\"prepareDoorwayHoleWallsForBake\"", StringComparison.Ordinal) >= 0;
            useSelectedPrefabs = saved.useSelectedPrefabs;
            collectBakedFilesToFolder = saved.collectBakedFilesToFolder;
            bakedFilesUnderOutputFolder = saved.bakedFilesUnderOutputFolder;
            setAllowRotationFalse = saved.setAllowRotationFalse;
            removeNavMeshLinksFromVariants = hasRemoveNavMeshLinksField ? saved.removeNavMeshLinksFromVariants : true;
            buildBakeScene = saved.buildBakeScene;
            runBakeAfterBuild = saved.runBakeAfterBuild;
            prepareDoorwayHoleWallsForBake = hasDoorwayHoleWallsField
                ? saved.prepareDoorwayHoleWallsForBake
                : true;
            useFixedBakeScenePath = hasFixedScenePathField ? saved.useFixedBakeScenePath : true;
            createReflectionProbeIfMissing = saved.createReflectionProbeIfMissing;
            autoCreatePowerProbeVariants = hasAutoProbeVariantField ? saved.autoCreatePowerProbeVariants : true;
            fitReflectionProbesBeforeBake = hasFitBeforeBakeField ? saved.fitReflectionProbesBeforeBake : true;
            autoSegmentReflectionProbes = hasAutoSegmentProbeField ? saved.autoSegmentReflectionProbes : true;
            segmentOnlyWhenLongestAxisOver = hasSegmentThresholdField ? Mathf.Max(0f, saved.segmentOnlyWhenLongestAxisOver) : 28f;
            targetProbeSegmentLength = hasTargetSegmentLengthField ? Mathf.Max(0.1f, saved.targetProbeSegmentLength) : 16f;
            maxReflectionProbeSegments = hasMaxSegmentField ? Mathf.Max(1, saved.maxReflectionProbeSegments) : 4;
            reflectionProbeSegmentOverlap = hasSegmentOverlapField ? Mathf.Max(0f, saved.reflectionProbeSegmentOverlap) : 1f;
            autoNormalizeNewPrisonRoomsBeforeBake = hasAutoNormalizeNewPrisonRoomsField ? saved.autoNormalizeNewPrisonRoomsBeforeBake : true;
            bakeLightProbeData = hasLightProbeDataField ? saved.bakeLightProbeData : true;
            lightProbeGridSpacing = hasLightProbeGridSpacingField ? Mathf.Max(0.5f, saved.lightProbeGridSpacing) : 2.5f;
            lightProbeWallInset = hasLightProbeWallInsetField ? Mathf.Max(0f, saved.lightProbeWallInset) : 0.75f;
            maxLightProbeSamples = hasMaxLightProbeSamplesField ? Mathf.Max(1, saved.maxLightProbeSamples) : 128;
            bakePower100 = saved.bakePower100;
            bakePower00 = saved.bakePower00;
            overrideBakeAmbient = saved.overrideBakeAmbient;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[{WindowTitle}] Failed to load checklist state: {ex.Message}");
        }
        finally
        {
            isLoadingChecklist = false;
        }
    }

    private void SaveChecklistState()
    {
        if (isLoadingChecklist)
            return;

        var saved = new ToolChecklistSaveData
        {
            useSelectedPrefabs = useSelectedPrefabs,
            collectBakedFilesToFolder = collectBakedFilesToFolder,
            bakedFilesUnderOutputFolder = bakedFilesUnderOutputFolder,
            setAllowRotationFalse = setAllowRotationFalse,
            removeNavMeshLinksFromVariants = removeNavMeshLinksFromVariants,
            buildBakeScene = buildBakeScene,
            runBakeAfterBuild = runBakeAfterBuild,
            prepareDoorwayHoleWallsForBake = prepareDoorwayHoleWallsForBake,
            useFixedBakeScenePath = useFixedBakeScenePath,
            createReflectionProbeIfMissing = createReflectionProbeIfMissing,
            autoCreatePowerProbeVariants = autoCreatePowerProbeVariants,
            fitReflectionProbesBeforeBake = fitReflectionProbesBeforeBake,
            autoSegmentReflectionProbes = autoSegmentReflectionProbes,
            segmentOnlyWhenLongestAxisOver = segmentOnlyWhenLongestAxisOver,
            targetProbeSegmentLength = targetProbeSegmentLength,
            maxReflectionProbeSegments = maxReflectionProbeSegments,
            reflectionProbeSegmentOverlap = reflectionProbeSegmentOverlap,
            autoNormalizeNewPrisonRoomsBeforeBake = autoNormalizeNewPrisonRoomsBeforeBake,
            bakeLightProbeData = bakeLightProbeData,
            lightProbeGridSpacing = lightProbeGridSpacing,
            lightProbeWallInset = lightProbeWallInset,
            maxLightProbeSamples = maxLightProbeSamples,
            bakePower100 = bakePower100,
            bakePower00 = bakePower00,
            overrideBakeAmbient = overrideBakeAmbient
        };

        string json = JsonUtility.ToJson(saved);
        EditorPrefs.SetString(ToolChecklistEditorPrefsKey, json);
    }
}
