using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DunGen;
using DunGen.Graph;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// V2 pilot: packages one canonical R000 bake into one rotatable prefab and one
/// lighting-set asset. Color lightmaps are shared; direction, SH, and reflection
/// data are derived for R090/R180/R270. This pilot never calls Lightmapping.Bake.
/// </summary>
public static class BAKEROTATETOOLV2
{
    private const string TileModifiedRoot = "Assets/Prefabs/map_piece/NewPrison/Tile_modified";
    private const string TilesRoot = "Assets/Prefabs/map_piece/NewPrison/Tiles";
    private const string TilesRotatedRoot = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated";
    private const string TilesRotated2Root = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated2";
    private const string TestRoot = "Assets/Prefabs/map_piece/NewPrison/TEST";
    private const string TileName = "StartRoom";
    private const string AuthoringPrefabPath = "Assets/Prefabs/map_piece/NewPrison/Tile_modified/StartRoom.prefab";
    private const string CanonicalPrefabPath = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/StartRoom_R000.prefab";
    private const string CanonicalDataRoot = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/BakedData/StartRoom_R000";
    private const string OutputFolder = "Assets/Prefabs/map_piece/NewPrison/TEST/V2_StartRoom";
    private const string GeneratedFolder = OutputFolder + "/Generated";
    private const string OutputPrefabPath = OutputFolder + "/StartRoom.prefab";
    private const string OutputSetPath = OutputFolder + "/StartRoom_LightingSetV2.asset";
    private const string RuntimeTestFolder = OutputFolder + "/RuntimeTest";
    private const string RuntimeTestTileSetPath = RuntimeTestFolder + "/V2_StartRoom_AllRooms_TileSet.asset";
    private const string RuntimeTestArchetypePath = RuntimeTestFolder + "/V2_StartRoom_AllRooms_Archetype.asset";
    private const string RuntimeTestFlowPath = RuntimeTestFolder + "/V2_StartRoom_AllRooms_Flow.asset";
    private const int CoefficientCount = 9;

    private static readonly int[] Rotations = { 0, 90, 180, 270 };

    public sealed class RoomContext
    {
        public string tileName;
        public string sourcePrefabPath;
        public string productionCanonicalRoot;
        public string productionCanonicalPrefabPath;
        public string productionCanonicalDataRoot;
        public string outputFolder;
        public string generatedFolder;
        public string outputPrefabPath;
        public string outputSetPath;
        public string testCanonicalFolder;
        public string testCanonicalPrefabPath;
        public string testCanonicalDataRoot;
        public string canonicalPrefabPath;
        public string canonicalDataRoot;
        public bool usingTestCanonical;
    }

    [Serializable]
    public sealed class RoomAnalysis
    {
        public bool completed;
        public string status;
        public string sourcePrefabPath;
        public string tileName;
        public string sourcePipeline;
        public string canonicalSource;
        public string canonicalPrefabPath;
        public string canonicalP100Path;
        public string canonicalP0Path;
        public string outputFolder;
        public string outputPrefabPath;
        public string outputSetPath;
        public int tileComponentCount;
        public int doorwayCount;
        public int authoredFloorColliderCount;
        public int rendererCount;
        public int lightCount;
        public int reflectionProbeCount;
        public int sourceIgnoreLightCount;
        public int sourceIgnoreEmissionCount;
        public int canonicalIgnoreLightCount;
        public int canonicalIgnoreEmissionCount;
        public bool markerPathsMatch;
        public bool bakeBridgeReady;
        public bool singleBakedState;
        public bool canonicalP100Ready;
        public bool canonicalP0Ready;
        public bool canonicalAppearsOlderThanSource;
        public bool v2OutputExists;
        public bool v2OutputStale;
        public bool canGenerate;
        public string[] blockers = Array.Empty<string>();
        public string[] warnings = Array.Empty<string>();
    }

    private static NetworkDungeonController s_runtimeController;
    private static DungeonMapList s_originalMapList;
    private static DungeonMapList s_runtimeMapList;
    private static DungeonRuntimeNavMeshPipeline s_runtimeNavMeshPipeline;
    private static Component s_runtimeNavMeshAdapter;
    private static MyCustomPostProcessor s_runtimeMyCustomPostProcessor;
    private static bool s_originalRuntimeBake;
    private static bool s_originalConfigureAdapter;
    private static bool s_originalAdapterEnabled;
    private static bool s_originalDetailedMyCustomDiagnostics;

    [Serializable]
    private sealed class BuildReport
    {
        public bool completed;
        public string failure;
        public string tile;
        public string prefabPath;
        public string lightingSetPath;
        public int prefabCount;
        public int rotationVariantCount;
        public int bakeCalls;
        public int sharedColorReferenceCount;
        public int generatedDirectionTextureCount;
        public int generatedReflectionCubemapCount;
        public int derivedShProbeSetCount;
        public bool allowRotation;
        public bool selectorAssigned;
        public bool canonicalColorsShared;
        public bool allVariantsComplete;
        public bool rotationDataNumericallyValid;
        public float maxDirectionVectorError;
        public float maxShEvaluationError;
        public float maxReflectionRgbError;
        public float meanReflectionRgbError;
        [NonSerialized] public double reflectionRgbErrorSum;
        [NonSerialized] public int reflectionRgbErrorCount;
    }

    private sealed class CubePixels
    {
        public int size;
        public readonly Color[][] faces = new Color[6][];
    }

    [Serializable]
    private sealed class RuntimeTileReport
    {
        public int index;
        public string tileName;
        public string prefabPath;
        public int worldYaw;
        public int selectedRotation;
        public string power100Bake;
        public bool rotationMatches;
        public bool bakeNameMatches;
    }

    [Serializable]
    private sealed class RuntimeTestReport
    {
        public string status;
        public int seed;
        public int roomCount;
        public int nonZeroRotationCount;
        public bool allTilesUseV2Prefab;
        public bool allRotationMappingsMatch;
        public bool allBakeNamesMatch;
        public string rotationDistribution;
        public RuntimeTileReport[] tiles;
        public string navMeshSuppression;
    }

    [Serializable]
    private sealed class Power0ParityReport
    {
        public bool completed;
        public string failure;
        public string canonicalBake;
        public string v2Bake;
        public int canonicalLightmapCount;
        public int v2LightmapCount;
        public bool sameLightmapColorReferences;
        public bool sameDirectionReferences;
        public bool sameRendererEntryCount;
        public bool sameReflectionReferences;
        public bool sameShData;
        public int canonicalEmissionEntryCount;
        public int v2EmissionEntryCount;
        public bool sameEmissionEntries;
        public string canonicalIgnoreLightPaths;
        public string v2IgnoreLightPaths;
        public bool sameIgnoreLightMarkers;
        public string canonicalIgnoreEmissionPaths;
        public string v2IgnoreEmissionPaths;
        public bool sameIgnoreEmissionMarkers;
        public bool canonicalSuppressesP0ReflectionProbes;
        public bool v2SuppressesP0ReflectionProbes;
        public bool sameP0ReflectionSuppression;
        public string authoringIgnoreLightPaths;
        public string authoringIgnoreEmissionPaths;
        public bool canonicalPreservesAuthoringIgnoreLightMarkers;
        public bool canonicalPreservesAuthoringIgnoreEmissionMarkers;
        public string warning;
        public string interpretation;
    }

    public static void BuildStartRoomTestMenu()
    {
        Debug.Log(BuildStartRoomTestCli());
    }

    public static void ValidateStartRoomTestMenu()
    {
        Debug.Log(ValidateStartRoomTestCli());
    }

    public static void BuildAllRoomsRuntimeTestAssetsMenu()
    {
        Debug.Log(BuildAllRoomsRuntimeTestAssetsCli());
    }

    public static void PrepareAndGenerateAllRoomsRuntimeTestMenu()
    {
        Debug.Log(PrepareAndGenerateAllRoomsRuntimeTestCli());
    }

    public static void ReportAllRoomsRuntimeTestMenu()
    {
        Debug.Log(ReportAllRoomsRuntimeTestCli());
    }

    public static void RestoreRuntimeTestOverridesMenu()
    {
        Debug.Log(RestoreRuntimeTestOverridesCli());
    }

    public static RoomAnalysis AnalyzeRoom(string sourcePrefabPath)
    {
        var report = new RoomAnalysis
        {
            sourcePrefabPath = NormalizeAssetPath(sourcePrefabPath)
        };
        var blockers = new List<string>();
        var warnings = new List<string>();
        GameObject sourceRoot = null;
        GameObject canonicalRoot = null;

        try
        {
            RoomContext context = ResolveRoomContext(report.sourcePrefabPath, true);
            report.tileName = context.tileName;
            report.sourcePipeline = context.sourcePrefabPath.StartsWith(TileModifiedRoot + "/", StringComparison.OrdinalIgnoreCase)
                ? "Tile_modified -> Tiles_Rotated"
                : "Tiles -> Tiles_Rotated2";
            report.canonicalSource = context.usingTestCanonical ? "TEST R000" : "Existing R000";
            report.canonicalPrefabPath = context.canonicalPrefabPath;
            report.singleBakedState = UsesSingleBakedState(context.sourcePrefabPath);
            report.canonicalP100Path = BuildCanonicalDataPath(context, "P100");
            report.canonicalP0Path = report.singleBakedState
                ? report.canonicalP100Path
                : BuildCanonicalDataPath(context, "P0");
            report.outputFolder = context.outputFolder;
            report.outputPrefabPath = context.outputPrefabPath;
            report.outputSetPath = context.outputSetPath;
            report.bakeBridgeReady = ValidateBakeBridge(out string bakeBridgeFailure);
            if (!report.bakeBridgeReady)
                warnings.Add("R000 bake bridge is unavailable: " + bakeBridgeFailure);

            GameObject sourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(context.sourcePrefabPath);
            if (sourceAsset == null)
                blockers.Add("Source prefab was not found.");
            else
            {
                sourceRoot = PrefabUtility.LoadPrefabContents(context.sourcePrefabPath);
                report.tileComponentCount = sourceRoot.GetComponentsInChildren<DunGen.Tile>(true).Length;
                report.doorwayCount = sourceRoot.GetComponentsInChildren<Doorway>(true).Length;
                report.authoredFloorColliderCount = CountAuthoredFloorColliders(sourceRoot.transform);
                report.rendererCount = sourceRoot.GetComponentsInChildren<Renderer>(true).Length;
                report.lightCount = sourceRoot.GetComponentsInChildren<Light>(true).Length;
                report.reflectionProbeCount = sourceRoot.GetComponentsInChildren<ReflectionProbe>(true).Length;
                report.sourceIgnoreLightCount = GetEnabledMarkerPaths<IgnoreLightControl>(sourceRoot.transform).Length;
                report.sourceIgnoreEmissionCount = GetEnabledMarkerPaths<IgnoreEmissionControl>(sourceRoot.transform).Length;
                if (report.tileComponentCount == 0)
                    blockers.Add("Source prefab has no DunGen Tile component.");
                if (report.doorwayCount == 0)
                    warnings.Add("Source prefab has no DunGen Doorway component.");
                if (report.authoredFloorColliderCount == 0)
                    warnings.Add("Source prefab has no enabled Floor-layer collider. Runtime NavMesh will use its tile-bounds fallback proxy.");
            }

            GameObject canonicalAsset = AssetDatabase.LoadAssetAtPath<GameObject>(context.canonicalPrefabPath);
            if (canonicalAsset == null)
            {
                blockers.Add("R000 canonical prefab is missing. Run the TEST R000 bake first.");
            }
            else
            {
                canonicalRoot = PrefabUtility.LoadPrefabContents(context.canonicalPrefabPath);
                string[] sourceLightMarkers = sourceRoot != null
                    ? GetEnabledMarkerPaths<IgnoreLightControl>(sourceRoot.transform)
                    : Array.Empty<string>();
                string[] sourceEmissionMarkers = sourceRoot != null
                    ? GetEnabledMarkerPaths<IgnoreEmissionControl>(sourceRoot.transform)
                    : Array.Empty<string>();
                string[] canonicalLightMarkers = GetEnabledMarkerPaths<IgnoreLightControl>(canonicalRoot.transform);
                string[] canonicalEmissionMarkers = GetEnabledMarkerPaths<IgnoreEmissionControl>(canonicalRoot.transform);
                report.canonicalIgnoreLightCount = canonicalLightMarkers.Length;
                report.canonicalIgnoreEmissionCount = canonicalEmissionMarkers.Length;
                report.markerPathsMatch = sourceLightMarkers.SequenceEqual(canonicalLightMarkers) &&
                                          sourceEmissionMarkers.SequenceEqual(canonicalEmissionMarkers);
                if (!report.markerPathsMatch)
                {
                    blockers.Add(
                        "IgnoreLightControl/IgnoreEmissionControl paths differ between source and canonical R000. " +
                        "Re-bake R000 in TEST before deriving rotations.");
                }
            }

            report.canonicalP100Ready = AssetDatabase.LoadAssetAtPath<DungeonTileBakeData>(report.canonicalP100Path) != null;
            report.canonicalP0Ready = report.singleBakedState
                ? report.canonicalP100Ready
                : AssetDatabase.LoadAssetAtPath<DungeonTileBakeData>(report.canonicalP0Path) != null;
            if (!report.canonicalP100Ready)
                blockers.Add("Canonical P100 bake data is missing.");
            if (!report.canonicalP0Ready)
                blockers.Add("Canonical P0 bake data is missing.");

            report.canonicalAppearsOlderThanSource =
                IsAssetNewer(context.sourcePrefabPath, report.canonicalP100Path) ||
                (!report.singleBakedState && IsAssetNewer(context.sourcePrefabPath, report.canonicalP0Path));
            if (report.canonicalAppearsOlderThanSource)
                warnings.Add("Source prefab is newer than its canonical bake data. Re-baking R000 is recommended.");

            report.v2OutputExists = AssetDatabase.LoadAssetAtPath<GameObject>(context.outputPrefabPath) != null &&
                                    AssetDatabase.LoadAssetAtPath<DungeonTileRotationLightingSetV2>(context.outputSetPath) != null;
            if (report.v2OutputExists)
            {
                DungeonTileRotationLightingSetV2 set =
                    AssetDatabase.LoadAssetAtPath<DungeonTileRotationLightingSetV2>(context.outputSetPath);
                string currentHash = AssetDatabase.GetAssetDependencyHash(context.sourcePrefabPath).ToString();
                report.v2OutputStale = set == null ||
                                       string.IsNullOrWhiteSpace(set.SourceDependencyHash) ||
                                       !string.Equals(set.SourceDependencyHash, currentHash, StringComparison.Ordinal);
                if (report.v2OutputStale)
                    warnings.Add("Existing V2 output was generated from an older or untracked source state.");
            }

            report.canGenerate = blockers.Count == 0;
            report.status = blockers.Count > 0 ? "BLOCKED" : warnings.Count > 0 ? "WARNING" : "PASS";
            report.completed = true;
        }
        catch (Exception exception)
        {
            blockers.Add(exception.Message);
            report.status = "BLOCKED";
            report.canGenerate = false;
            report.completed = false;
        }
        finally
        {
            if (sourceRoot != null)
                PrefabUtility.UnloadPrefabContents(sourceRoot);
            if (canonicalRoot != null)
                PrefabUtility.UnloadPrefabContents(canonicalRoot);
        }

        report.blockers = blockers.ToArray();
        report.warnings = warnings.ToArray();
        return report;
    }

    public static string AnalyzeRoomCli(string sourcePrefabPath)
    {
        string json = JsonUtility.ToJson(AnalyzeRoom(sourcePrefabPath), true);
        Debug.Log($"[BAKEROATETOOLV2] Analyze Room\n{json}");
        return json;
    }

    public static string BakeCanonicalR000Cli(string sourcePrefabPath, bool archiveExisting = true)
    {
        if (EditorApplication.isPlaying)
            return "ERROR: Exit Play Mode before baking.";
        if (Lightmapping.isRunning)
            return "ERROR: Unity Lightmapping is already running.";

        RoomContext context = ResolveRoomContext(sourcePrefabPath, false);
        Scene originalScene = SceneManager.GetActiveScene();
        string originalScenePath = originalScene.path;
        if (string.IsNullOrWhiteSpace(originalScenePath))
            return "ERROR: Save the current scene before starting the V2 R000 bake.";
        if (!EditorSceneManager.SaveOpenScenes())
            return "ERROR: Failed to save the current scene before baking.";

        string archivePath = string.Empty;
        UnityEngine.Object bakeTool = null;
        bool singleBakedState = UsesSingleBakedState(context.sourcePrefabPath);
        try
        {
            if (archiveExisting && AssetDatabase.IsValidFolder(context.testCanonicalFolder))
                archivePath = ArchiveFolderByMove(context, context.testCanonicalFolder, "Canonical");

            EnsureFolder(context.testCanonicalFolder);
            string bakeSceneFolder = context.testCanonicalFolder + "/BakeScenes";
            EnsureFolder(bakeSceneFolder);

            bakeTool = ScriptableObject.CreateInstance<DungeonTileRotationBakeTool>();
            InvokePrivate(bakeTool, "LoadEmissionMaterialVariants");
            InvokePrivate(bakeTool, "ConfigureCliAdminstrativeBakeDefaults");
            SetPrivateField(bakeTool, "useSelectedPrefabs", false);
            SetPrivateField(bakeTool, "sourceFolder", Path.GetDirectoryName(context.sourcePrefabPath)?.Replace('\\', '/'));
            SetPrivateField(bakeTool, "outputFolder", context.testCanonicalFolder);
            SetPrivateField(bakeTool, "bakeScenePath", bakeSceneFolder + "/TileBake_Auto.unity");
            SetPrivateField(bakeTool, "collectBakedFilesToFolder", true);
            SetPrivateField(bakeTool, "bakedFilesUnderOutputFolder", true);
            SetPrivateField(bakeTool, "setAllowRotationFalse", true);
            SetPrivateField(bakeTool, "removeNavMeshLinksFromVariants", true);
            SetPrivateField(bakeTool, "buildBakeScene", true);
            SetPrivateField(bakeTool, "runBakeAfterBuild", true);
            SetPrivateField(bakeTool, "prepareDoorwayHoleWallsForBake", true);
            SetPrivateField(bakeTool, "useFixedBakeScenePath", false);
            SetPrivateField(bakeTool, "autoNormalizeNewPrisonRoomsBeforeBake", false);
            SetPrivateField(bakeTool, "bakePower100", true);
            SetPrivateField(bakeTool, "bakePower00", !singleBakedState);
            SetPrivateField(bakeTool, "overrideBakeQuality", singleBakedState);
            SetPrivateField(bakeTool, "bakeDirectSampleCount", 256);
            SetPrivateField(bakeTool, "bakeIndirectSampleCount", 1024);
            SetPrivateField(bakeTool, "bakeEnvironmentSampleCount", 256);
            SetPrivateField(bakeTool, "bakeBounces", 2);
            SetPrivateField(bakeTool, "bakePadding", 6);
            SetPrivateField(bakeTool, "bakeTextureCompression", false);
            SetPrivateField(bakeTool, "cliRotationOverride", new[] { 0 });

            var sources = new List<string> { context.sourcePrefabPath };
            var generated = (List<string>)InvokePrivate(bakeTool, "GenerateVariants", sources);
            bool fitProbes = (bool)GetPrivateField(bakeTool, "fitReflectionProbesBeforeBake");
            if (fitProbes)
                InvokePrivate(bakeTool, "FitReflectionProbesForPrefabs", generated, $"Fitting {context.tileName} R000 reflection probes");

            string bakedFolder = (string)InvokePrivate(bakeTool, "GetResolvedBakedFilesFolder");
            string dataRoot = (string)InvokePrivate(bakeTool, "GetBakeDataAssetRootFolder");
            EnsureFolder(bakedFolder);
            EnsureFolder(dataRoot);
            object dataSets = InvokePrivate(bakeTool, "BuildAndOptionallyBakeScene", generated, bakedFolder, dataRoot);
            InvokePrivate(bakeTool, "AttachLightmapSwitchers", dataSets);

            if (singleBakedState)
            {
                string staleP0Folder = context.testCanonicalDataRoot + "/P0";
                if (AssetDatabase.IsValidFolder(staleP0Folder))
                    AssetDatabase.DeleteAsset(staleP0Folder);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RoomAnalysis analysis = AnalyzeRoom(context.sourcePrefabPath);
            string bakeMode = singleBakedState
                ? "P100 single baked state, 1 bake pass"
                : "P100 + P0, 2 bake passes";
            return
                $"PASS: Baked TEST canonical R000 for {context.tileName} ({bakeMode}).\n" +
                $"Canonical={context.testCanonicalPrefabPath}\n" +
                $"Archive={(string.IsNullOrEmpty(archivePath) ? "none" : archivePath)}\n" +
                $"Analyze={analysis.status}, markersMatch={analysis.markerPathsMatch}, canGenerate={analysis.canGenerate}";
        }
        catch (Exception exception)
        {
            return $"ERROR: V2 R000 bake failed for {context.tileName}\n{UnwrapException(exception)}";
        }
        finally
        {
            if (bakeTool != null)
                UnityEngine.Object.DestroyImmediate(bakeTool);
            EditorUtility.ClearProgressBar();
            if (!string.IsNullOrWhiteSpace(originalScenePath) &&
                AssetDatabase.LoadAssetAtPath<SceneAsset>(originalScenePath) != null)
            {
                EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
            }
        }
    }

    public static string BuildRoomOneClickCli(string sourcePrefabPath, bool archiveExisting = true)
    {
        string sourcePath = NormalizeAssetPath(sourcePrefabPath);
        string tileName = Path.GetFileNameWithoutExtension(sourcePath);
        var steps = new List<string>();
        try
        {
            RoomContext context = ResolveRoomContext(sourcePath, false);
            tileName = context.tileName;
            steps.Add($"[1/4] Source: {context.sourcePrefabPath}");

            string bakeResult = BakeCanonicalR000Cli(context.sourcePrefabPath, archiveExisting);
            steps.Add("[2/4] R000 canonical bake\n" + bakeResult);
            if (bakeResult.StartsWith("CANCELLED:", StringComparison.Ordinal))
                return LogOneClickCancelled(tileName, steps, "The user cancelled before baking.");
            if (!bakeResult.StartsWith("PASS:", StringComparison.Ordinal))
                return LogOneClickResult(tileName, false, steps, "R000 bake did not complete.");

            RoomAnalysis analysis = AnalyzeRoom(context.sourcePrefabPath);
            if (!analysis.canGenerate)
            {
                steps.Add("[3/4] Preflight after bake: " + analysis.status + "\n" +
                          string.Join("\n", analysis.blockers ?? Array.Empty<string>()));
                return LogOneClickResult(tileName, false, steps, "Post-bake preflight is blocked.");
            }

            string buildJson = BuildOrUpdateRoomCli(context.sourcePrefabPath, archiveExisting);
            BuildReport build = JsonUtility.FromJson<BuildReport>(buildJson);
            steps.Add("[3/4] Generate single V2 prefab\n" + buildJson);
            if (build == null || !build.completed)
                return LogOneClickResult(tileName, false, steps, "V2 generation failed.");

            string validateJson = ValidateRoomCli(context.sourcePrefabPath);
            BuildReport validation = JsonUtility.FromJson<BuildReport>(validateJson);
            steps.Add("[4/4] Validate V2 output\n" + validateJson);
            if (validation == null || !validation.completed)
                return LogOneClickResult(tileName, false, steps, "V2 validation failed.");

            return LogOneClickResult(
                tileName,
                true,
                steps,
                $"Output={build.prefabPath}\nLightingSet={build.lightingSetPath}");
        }
        catch (Exception exception)
        {
            return LogOneClickResult(tileName, false, steps, UnwrapException(exception));
        }
    }

    private static string LogOneClickResult(
        string tileName,
        bool completed,
        List<string> steps,
        string conclusion)
    {
        string result =
            $"{(completed ? "PASS" : "ERROR")}: V2 one-click build for {tileName}\n" +
            string.Join("\n\n", steps ?? new List<string>()) +
            "\n\n" + conclusion;
        if (completed)
            Debug.Log("[BAKEROATETOOLV2] " + result);
        else
            Debug.LogError("[BAKEROATETOOLV2] " + result);
        return result;
    }

    private static string LogOneClickCancelled(string tileName, List<string> steps, string conclusion)
    {
        string result =
            $"CANCELLED: V2 one-click build for {tileName}\n" +
            string.Join("\n\n", steps ?? new List<string>()) +
            "\n\n" + conclusion;
        Debug.Log("[BAKEROATETOOLV2] " + result);
        return result;
    }

    public static string BuildOrUpdateRoomCli(string sourcePrefabPath, bool archiveExisting = true)
    {
        var report = new BuildReport { bakeCalls = 0 };
        try
        {
            RoomContext context = ResolveRoomContext(sourcePrefabPath, true);
            RoomAnalysis analysis = AnalyzeRoom(context.sourcePrefabPath);
            report.tile = context.tileName;
            if (!analysis.canGenerate)
                throw new InvalidOperationException("Room preflight is BLOCKED:\n" + string.Join("\n", analysis.blockers));

            EnsureFolder(context.outputFolder);
            if (archiveExisting &&
                (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(context.outputPrefabPath) != null ||
                 AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(context.outputSetPath) != null))
            {
                BackupDerivedOutput(context);
            }

            if (AssetDatabase.IsValidFolder(context.generatedFolder))
                AssetDatabase.DeleteAsset(context.generatedFolder);
            EnsureFolder(context.generatedFolder);

            DungeonTileRotationLightingSetV2 lightingSet =
                AssetDatabase.LoadAssetAtPath<DungeonTileRotationLightingSetV2>(context.outputSetPath);
            if (lightingSet == null)
            {
                lightingSet = ScriptableObject.CreateInstance<DungeonTileRotationLightingSetV2>();
                lightingSet.name = context.tileName + "_LightingSetV2";
                AssetDatabase.CreateAsset(lightingSet, context.outputSetPath);
            }
            else
            {
                ClearLightingSetSubAssets(context.outputSetPath, lightingSet);
            }

            bool singleBakedState = UsesSingleBakedState(context.sourcePrefabPath);
            DungeonTileBakeData canonicalP100 = LoadCanonicalData(context, "P100");
            DungeonTileBakeData canonicalP0 = singleBakedState
                ? canonicalP100
                : LoadCanonicalData(context, "P0");
            var variants = new DungeonTileRotationLightingSetV2.RotationVariant[Rotations.Length];
            var matrices = new Dictionary<int, double[,]>();
            for (int i = 0; i < Rotations.Length; i++)
            {
                int rotation = Rotations[i];
                if (rotation != 0)
                    matrices[rotation] = BuildShRotationMatrix(rotation);
                DungeonTileBakeData derivedP100 = CreateDerivedDataForRoom(
                    context, lightingSet, canonicalP100, rotation, "P100", matrices, report);
                variants[i] = new DungeonTileRotationLightingSetV2.RotationVariant
                {
                    rotationY = rotation,
                    power100 = derivedP100,
                    power0 = singleBakedState
                        ? derivedP100
                        : CreateDerivedDataForRoom(context, lightingSet, canonicalP0, rotation, "P0", matrices, report)
                };
            }

            lightingSet.Configure(
                context.tileName,
                variants,
                context.sourcePrefabPath,
                context.canonicalPrefabPath,
                AssetDatabase.GetAssetDependencyHash(context.sourcePrefabPath).ToString(),
                DateTime.UtcNow.ToString("O"));
            EditorUtility.SetDirty(lightingSet);
            AssetDatabase.SaveAssets();

            GameObject root = PrefabUtility.LoadPrefabContents(context.canonicalPrefabPath);
            try
            {
                root.name = context.tileName;
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                foreach (DunGen.Tile tile in root.GetComponentsInChildren<DunGen.Tile>(true))
                {
                    var serializedTile = new SerializedObject(tile);
                    SerializedProperty allowRotation = serializedTile.FindProperty("AllowRotation");
                    if (allowRotation != null)
                    {
                        allowRotation.boolValue = true;
                        serializedTile.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                DungeonTilePowerBakeSet powerSet = root.GetComponent<DungeonTilePowerBakeSet>();
                DungeonTileLightmapSwitcher switcher = root.GetComponent<DungeonTileLightmapSwitcher>();
                if (powerSet == null || switcher == null)
                    throw new InvalidOperationException("Canonical R000 is missing DungeonTilePowerBakeSet or DungeonTileLightmapSwitcher.");
                powerSet.ConfigureRuntimeBakeData(variants[0].power100, variants[0].power0);
                switcher.ConfigurePowerBakeSet(powerSet);
                DungeonTileRotationSelectorV2 selector = root.GetComponent<DungeonTileRotationSelectorV2>();
                if (selector == null)
                    selector = root.AddComponent<DungeonTileRotationSelectorV2>();
                selector.Configure(lightingSet);
                NormalizeV2NavigationAuthoring(root);
                DungeonV2NavigationPrefabAuthoring.EnsureSimpleAreaMarkers(root);

                PrefabUtility.SaveAsPrefabAsset(root, context.outputPrefabPath, out bool success);
                if (!success)
                    throw new InvalidOperationException($"Could not save V2 prefab: {context.outputPrefabPath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string productionFlowResult = DungeonV2ProductionFlowAuthoring.ApplyCli();
            if (!productionFlowResult.StartsWith("PASS", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "V2 prefab was built, but the V2-only production dungeon flow could not be refreshed.\n" +
                    productionFlowResult);
            }
            PopulateValidation(context, report);
            report.completed = report.allVariantsComplete && report.allowRotation &&
                               report.selectorAssigned && report.canonicalColorsShared &&
                               report.rotationDataNumericallyValid;
            if (!report.completed)
                report.failure = "One or more V2 validation gates failed.";
        }
        catch (Exception exception)
        {
            report.completed = false;
            report.failure = UnwrapException(exception);
        }

        string json = JsonUtility.ToJson(report, true);
        Debug.Log($"[BAKEROATETOOLV2] Build/Update Room\n{json}");
        return json;
    }

    public static string ValidateRoomCli(string sourcePrefabPath)
    {
        var report = new BuildReport { bakeCalls = 0 };
        try
        {
            RoomContext context = ResolveRoomContext(sourcePrefabPath, true);
            report.tile = context.tileName;
            PopulateValidation(context, report);
            report.completed = report.allVariantsComplete && report.allowRotation &&
                               report.selectorAssigned && report.canonicalColorsShared &&
                               report.rotationDataNumericallyValid;
            if (!report.completed)
                report.failure = "One or more V2 validation gates failed.";
        }
        catch (Exception exception)
        {
            report.completed = false;
            report.failure = UnwrapException(exception);
        }

        string json = JsonUtility.ToJson(report, true);
        Debug.Log($"[BAKEROATETOOLV2] Validate Room\n{json}");
        return json;
    }

    public static string RewireExistingV2LightingSetsCli()
    {
        if (EditorApplication.isPlaying)
            return "ERROR: Exit Play Mode before rewiring V2 lighting sets.";

        string[] guids = AssetDatabase.FindAssets("t:DungeonTileRotationLightingSetV2", new[] { TestRoot });
        var report = new StringBuilder();
        int rewired = 0;
        int alreadyOk = 0;
        int skipped = 0;
        int failed = 0;

        for (int i = 0; i < guids.Length; i++)
        {
            string setPath = AssetDatabase.GUIDToAssetPath(guids[i]).Replace('\\', '/');
            if (setPath.IndexOf("/History/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                skipped++;
                continue;
            }

            DungeonTileRotationLightingSetV2 set =
                AssetDatabase.LoadAssetAtPath<DungeonTileRotationLightingSetV2>(setPath);
            if (set == null)
            {
                skipped++;
                continue;
            }

            string folder = Path.GetDirectoryName(setPath);
            if (string.IsNullOrWhiteSpace(folder))
            {
                skipped++;
                continue;
            }

            folder = folder.Replace('\\', '/');
            string tileName = set.SourceTileName;
            if (string.IsNullOrWhiteSpace(tileName))
            {
                tileName = Path.GetFileNameWithoutExtension(setPath);
                if (tileName.EndsWith("_LightingSetV2", StringComparison.OrdinalIgnoreCase))
                    tileName = tileName.Substring(0, tileName.Length - "_LightingSetV2".Length);
            }

            string prefabPath = folder + "/" + tileName + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                skipped++;
                report.AppendLine("SKIP missing prefab for " + setPath);
                continue;
            }

            try
            {
                if (RewirePrefabLightingSet(prefabPath, set))
                {
                    rewired++;
                    report.AppendLine("REWIRED " + tileName + " -> " + setPath);
                }
                else
                {
                    alreadyOk++;
                    report.AppendLine("OK " + tileName);
                }
            }
            catch (Exception exception)
            {
                failed++;
                report.AppendLine("FAIL " + tileName + ": " + UnwrapException(exception));
            }
        }

        AssetDatabase.SaveAssets();
        string status = failed == 0 ? "PASS" : "FAIL";
        string summary =
            status +
            " rewired=" + rewired +
            " alreadyOk=" + alreadyOk +
            " skipped=" + skipped +
            " failed=" + failed;
        Debug.Log("[BAKEROATETOOLV2] Rewire V2 lighting sets\n" + summary + "\n" + report);
        return summary + "\n" + report;
    }

    private static bool RewirePrefabLightingSet(string prefabPath, DungeonTileRotationLightingSetV2 set)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            DungeonTileRotationSelectorV2 selector = root.GetComponent<DungeonTileRotationSelectorV2>();
            DungeonTilePowerBakeSet powerSet = root.GetComponent<DungeonTilePowerBakeSet>();
            DungeonTileLightmapSwitcher switcher = root.GetComponent<DungeonTileLightmapSwitcher>();
            if (powerSet == null || switcher == null)
                throw new InvalidOperationException(prefabPath + " is missing PowerBakeSet or LightmapSwitcher.");

            DungeonTileRotationLightingSetV2.RotationVariant r000 = set.Resolve(0f);
            if (r000 == null || r000.power100 == null || r000.power0 == null)
                throw new InvalidOperationException(set.name + " is missing R000 bake variants.");

            bool alreadyWired = selector != null &&
                                selector.LightingSet == set &&
                                powerSet.Power100Bake == r000.power100 &&
                                powerSet.Power00Bake == r000.power0;
            if (alreadyWired)
                return false;

            if (selector == null)
                selector = root.AddComponent<DungeonTileRotationSelectorV2>();

            selector.Configure(set);
            powerSet.ConfigureRuntimeBakeData(r000.power100, r000.power0);
            switcher.ConfigurePowerBakeSet(powerSet);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
            if (!success)
                throw new InvalidOperationException("Could not save " + prefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    public static string BuildAllRoomsRuntimeTestAssetsCli()
    {
        if (EditorApplication.isPlaying)
            return "ERROR: Build runtime test assets outside Play Mode.";

        EnsureFolder(RuntimeTestFolder);
        GameObject prefab = LoadRequired<GameObject>(OutputPrefabPath);

        TileSet tileSet = LoadOrCreateAsset<TileSet>(RuntimeTestTileSetPath);
        tileSet.TileWeights.Weights.Clear();
        tileSet.LockPrefabs.Clear();
        tileSet.AddTiles(new[] { prefab }, 1f, 1f);
        EditorUtility.SetDirty(tileSet);

        DungeonArchetype archetype = LoadOrCreateAsset<DungeonArchetype>(RuntimeTestArchetypePath);
        archetype.TileSets.Clear();
        archetype.TileSets.Add(tileSet);
        archetype.BranchStartTileSets.Clear();
        archetype.BranchStartTileSets.Add(tileSet);
        archetype.BranchCapTileSets.Clear();
        archetype.BranchCapTileSets.Add(tileSet);
        archetype.BranchingDepth = new IntRange(1, 2);
        archetype.BranchCount = new IntRange(1, 2);
        archetype.BranchStartType = BranchCapType.AsWellAs;
        archetype.BranchCapType = BranchCapType.AsWellAs;
        archetype.Unique = false;
        EditorUtility.SetDirty(archetype);

        DungeonFlow flow = LoadOrCreateAsset<DungeonFlow>(RuntimeTestFlowPath);
        flow.Length = new IntRange(6, 6);
        flow.BranchMode = BranchMode.Local;
        flow.BranchCount = new IntRange(1, 2);
        flow.BranchFromStartNode = true;
        flow.FillAllUnusedStartDoorways = true;
        flow.MinimumStartNodeBranches = 1;
        flow.MatchStartBranchDepthToMainPath = false;
        flow.DoorwayConnectionChance = 1f;
        flow.RestrictConnectionToSameSection = false;
        flow.GlobalProps.Clear();
        flow.TileInjectionRules.Clear();
        flow.TileConnectionTags.Clear();
        flow.BranchPruneTags.Clear();

        new DungeonFlowBuilder(flow)
            .AddNode(tileSet, "V2 Start")
            .AddLine(archetype, 1f)
            .AddNode(tileSet, "V2 Goal")
            .Complete();

        EditorUtility.SetDirty(flow);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return
            "PASS: Built isolated V2 all-rooms runtime TEST assets.\n" +
            $"Flow={RuntimeTestFlowPath}\n" +
            $"TileSet={RuntimeTestTileSetPath}\n" +
            $"Prefab={OutputPrefabPath}\n" +
            "Production DungeonMapList and StartMap were not modified.";
    }

    public static string PrepareAndGenerateAllRoomsRuntimeTestCli(int randomState = 2510425)
    {
        if (!EditorApplication.isPlaying)
            return "ERROR: Enter Play Mode before preparing the runtime test.";

        RestoreRuntimeTestOverridesCli();

        DungeonFlow flow = LoadRequired<DungeonFlow>(RuntimeTestFlowPath);
        s_runtimeController = UnityEngine.Object.FindAnyObjectByType<NetworkDungeonController>();
        if (s_runtimeController == null)
            return "ERROR: NetworkDungeonController not found.";

        FieldInfo mapListField = RequireField(typeof(NetworkDungeonController), "mapList");
        s_originalMapList = mapListField.GetValue(s_runtimeController) as DungeonMapList;
        if (s_originalMapList == null || s_originalMapList.Count == 0)
            return "ERROR: Production DungeonMapList is missing or empty.";

        s_runtimeMapList = UnityEngine.Object.Instantiate(s_originalMapList);
        s_runtimeMapList.name = "V2_StartRoom_RuntimeTest_MapList";
        var originalEntry = s_originalMapList.Entries[0];
        var entries = new List<DungeonMapList.MapEntry>
        {
            new DungeonMapList.MapEntry
            {
                flow = flow,
                selector = originalEntry != null ? originalEntry.selector : null,
                budget = originalEntry != null ? originalEntry.budget : 0
            }
        };
        RequireField(typeof(DungeonMapList), "entries").SetValue(s_runtimeMapList, entries);
        mapListField.SetValue(s_runtimeController, s_runtimeMapList);

        s_runtimeNavMeshPipeline = s_runtimeController.GetComponent<DungeonRuntimeNavMeshPipeline>();
        if (s_runtimeNavMeshPipeline != null)
        {
            s_originalRuntimeBake = GetPrivateBool(s_runtimeNavMeshPipeline, "enableRuntimeBake");
            s_originalConfigureAdapter = GetPrivateBool(s_runtimeNavMeshPipeline, "configureDunGenAdapter");
            SetPrivateField(s_runtimeNavMeshPipeline, "enableRuntimeBake", false);
            SetPrivateField(s_runtimeNavMeshPipeline, "configureDunGenAdapter", false);
            s_runtimeNavMeshAdapter = RequireField(typeof(DungeonRuntimeNavMeshPipeline), "unityNavMeshAdapter")
                .GetValue(s_runtimeNavMeshPipeline) as Component;
            if (s_runtimeNavMeshAdapter is Behaviour adapterBehaviour)
            {
                s_originalAdapterEnabled = adapterBehaviour.enabled;
                adapterBehaviour.enabled = false;
            }
        }

        s_runtimeMyCustomPostProcessor = s_runtimeController.GetComponent<MyCustomPostProcessor>();
        if (s_runtimeMyCustomPostProcessor != null)
        {
            s_originalDetailedMyCustomDiagnostics = GetPrivateBool(
                s_runtimeMyCustomPostProcessor,
                "enablePerTileTimingDiagnostics");
            SetPrivateField(s_runtimeMyCustomPostProcessor, "enablePerTileTimingDiagnostics", true);
        }

        UnityEngine.Random.InitState(randomState);
        string generationResult = DebugRemoteControl.GenerateDungeon();
        return
            generationResult + "\n" +
            $"Runtime-only flow={RuntimeTestFlowPath}\n" +
            "NavMesh test suppression: runtime bake OFF; UnityNavMeshAdapter disabled/unregistered; " +
            "post-complete validation skipped by enableRuntimeBake=false.";
    }

    public static string ReportAllRoomsRuntimeTestCli()
    {
        RuntimeDungeon runtimeDungeon = UnityEngine.Object.FindAnyObjectByType<RuntimeDungeon>();
        if (runtimeDungeon == null)
            return "ERROR: RuntimeDungeon not found.";

        DunGen.DungeonGenerator generator = runtimeDungeon.Generator;
        var report = new RuntimeTestReport
        {
            status = generator.Status.ToString(),
            seed = generator.ChosenSeed,
            navMeshSuppression = "runtime bake OFF; UnityNavMeshAdapter disabled; runtime validation skipped"
        };

        Dungeon dungeon = generator.CurrentDungeon;
        if (dungeon == null || dungeon.AllTiles == null)
        {
            report.tiles = Array.Empty<RuntimeTileReport>();
            return JsonUtility.ToJson(report, true);
        }

        GameObject expectedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPrefabPath);
        var tiles = new List<RuntimeTileReport>();
        var distribution = new SortedDictionary<int, int>();
        bool allV2 = true;
        bool allRotations = true;
        bool allBakeNames = true;

        for (int i = 0; i < dungeon.AllTiles.Count; i++)
        {
            DunGen.Tile tile = dungeon.AllTiles[i];
            DungeonTileRotationSelectorV2 selector = tile != null
                ? tile.GetComponentInChildren<DungeonTileRotationSelectorV2>(true)
                : null;
            DungeonTilePowerBakeSet powerSet = tile != null
                ? tile.GetComponentInChildren<DungeonTilePowerBakeSet>(true)
                : null;
            int yaw = tile != null
                ? DungeonTileRotationLightingSetV2.QuantizeRotation(tile.transform.eulerAngles.y)
                : -1;
            int selected = selector != null ? selector.SelectedRotation : -1;
            string bakeName = powerSet != null && powerSet.Power100Bake != null
                ? powerSet.Power100Bake.name
                : string.Empty;
            string expectedBakeName = selected >= 0 ? $"StartRoom_R{selected:000}_P100_V2" : string.Empty;
            bool usesV2 = tile != null && tile.Prefab == expectedPrefab && selector != null;
            bool rotationMatches = selected == yaw;
            bool bakeNameMatches = bakeName == expectedBakeName;

            allV2 &= usesV2;
            allRotations &= rotationMatches;
            allBakeNames &= bakeNameMatches;
            if (!distribution.ContainsKey(selected))
                distribution[selected] = 0;
            distribution[selected]++;

            tiles.Add(new RuntimeTileReport
            {
                index = i,
                tileName = tile != null ? tile.name : "null",
                prefabPath = tile != null && tile.Prefab != null ? AssetDatabase.GetAssetPath(tile.Prefab) : string.Empty,
                worldYaw = yaw,
                selectedRotation = selected,
                power100Bake = bakeName,
                rotationMatches = rotationMatches,
                bakeNameMatches = bakeNameMatches
            });
        }

        report.roomCount = tiles.Count;
        report.nonZeroRotationCount = tiles.Count(x => x.selectedRotation > 0);
        report.allTilesUseV2Prefab = allV2;
        report.allRotationMappingsMatch = allRotations;
        report.allBakeNamesMatch = allBakeNames;
        report.rotationDistribution = string.Join(", ", distribution.Select(x => $"R{x.Key:000}={x.Value}"));
        report.tiles = tiles.ToArray();
        return JsonUtility.ToJson(report, true);
    }

    public static string RestoreRuntimeTestOverridesCli()
    {
        if (s_runtimeController != null && s_originalMapList != null)
            RequireField(typeof(NetworkDungeonController), "mapList").SetValue(s_runtimeController, s_originalMapList);

        if (s_runtimeNavMeshPipeline != null)
        {
            SetPrivateField(s_runtimeNavMeshPipeline, "enableRuntimeBake", s_originalRuntimeBake);
            SetPrivateField(s_runtimeNavMeshPipeline, "configureDunGenAdapter", s_originalConfigureAdapter);
        }

        if (s_runtimeNavMeshAdapter is Behaviour adapterBehaviour)
            adapterBehaviour.enabled = s_originalAdapterEnabled;

        if (s_runtimeMyCustomPostProcessor != null)
        {
            SetPrivateField(
                s_runtimeMyCustomPostProcessor,
                "enablePerTileTimingDiagnostics",
                s_originalDetailedMyCustomDiagnostics);
        }

        if (s_runtimeMapList != null)
            UnityEngine.Object.Destroy(s_runtimeMapList);

        s_runtimeController = null;
        s_originalMapList = null;
        s_runtimeMapList = null;
        s_runtimeNavMeshPipeline = null;
        s_runtimeNavMeshAdapter = null;
        s_runtimeMyCustomPostProcessor = null;
        return "PASS: Runtime-only V2 test overrides restored.";
    }

    public static string BuildStartRoomTestCli()
    {
        var report = new BuildReport { tile = TileName, bakeCalls = 0 };
        try
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(OutputSetPath) != null ||
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(OutputPrefabPath) != null)
            {
                throw new InvalidOperationException(
                    $"V2 pilot output already exists. Existing assets were preserved: {OutputFolder}");
            }

            GameObject canonicalPrefab = LoadRequired<GameObject>(CanonicalPrefabPath);
            DungeonTileBakeData canonicalP100 = LoadCanonicalData("P100");
            DungeonTileBakeData canonicalP0 = LoadCanonicalData("P0");

            EnsureFolder(OutputFolder);
            EnsureFolder(GeneratedFolder);

            var lightingSet = ScriptableObject.CreateInstance<DungeonTileRotationLightingSetV2>();
            lightingSet.name = "StartRoom_LightingSetV2";
            AssetDatabase.CreateAsset(lightingSet, OutputSetPath);

            var variants = new DungeonTileRotationLightingSetV2.RotationVariant[Rotations.Length];
            var matrices = new Dictionary<int, double[,]>();
            for (int i = 0; i < Rotations.Length; i++)
            {
                int rotation = Rotations[i];
                if (rotation != 0)
                    matrices[rotation] = BuildShRotationMatrix(rotation);

                DungeonTileBakeData p100 = CreateDerivedData(
                    lightingSet, canonicalP100, rotation, "P100", matrices, report);
                DungeonTileBakeData p0 = CreateDerivedData(
                    lightingSet, canonicalP0, rotation, "P0", matrices, report);
                variants[i] = new DungeonTileRotationLightingSetV2.RotationVariant
                {
                    rotationY = rotation,
                    power100 = p100,
                    power0 = p0
                };
            }

            lightingSet.Configure(TileName, variants);
            EditorUtility.SetDirty(lightingSet);
            AssetDatabase.SaveAssets();

            if (!AssetDatabase.CopyAsset(CanonicalPrefabPath, OutputPrefabPath))
                throw new InvalidOperationException($"Could not copy canonical prefab to {OutputPrefabPath}");

            GameObject root = PrefabUtility.LoadPrefabContents(OutputPrefabPath);
            try
            {
                root.name = TileName;
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;

                foreach (DunGen.Tile tile in root.GetComponentsInChildren<DunGen.Tile>(true))
                {
                    var serializedTile = new SerializedObject(tile);
                    SerializedProperty allowRotation = serializedTile.FindProperty("AllowRotation");
                    if (allowRotation != null)
                    {
                        allowRotation.boolValue = true;
                        serializedTile.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                DungeonTilePowerBakeSet powerSet = root.GetComponent<DungeonTilePowerBakeSet>();
                DungeonTileLightmapSwitcher switcher = root.GetComponent<DungeonTileLightmapSwitcher>();
                if (powerSet == null || switcher == null)
                    throw new InvalidOperationException("Canonical StartRoom is missing its power set or lightmap switcher.");

                powerSet.ConfigureRuntimeBakeData(variants[0].power100, variants[0].power0);
                switcher.ConfigurePowerBakeSet(powerSet);
                DungeonTileRotationSelectorV2 selector = root.GetComponent<DungeonTileRotationSelectorV2>();
                if (selector == null)
                    selector = root.AddComponent<DungeonTileRotationSelectorV2>();
                selector.Configure(lightingSet);

                PrefabUtility.SaveAsPrefabAsset(root, OutputPrefabPath, out bool success);
                if (!success)
                    throw new InvalidOperationException($"Could not save V2 prefab: {OutputPrefabPath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            PopulateValidation(report);
            report.completed = report.allVariantsComplete && report.allowRotation &&
                               report.selectorAssigned && report.canonicalColorsShared &&
                               report.rotationDataNumericallyValid;
        }
        catch (Exception exception)
        {
            report.completed = false;
            report.failure = exception.ToString();
        }

        string json = JsonUtility.ToJson(report, true);
        Debug.Log($"[BAKEROATETOOLV2] Build StartRoom TEST\n{json}");
        return json;
    }

    public static string ValidateStartRoomTestCli()
    {
        var report = new BuildReport { tile = TileName, bakeCalls = 0 };
        try
        {
            PopulateValidation(report);
            report.completed = report.allVariantsComplete && report.allowRotation &&
                               report.selectorAssigned && report.canonicalColorsShared &&
                               report.rotationDataNumericallyValid;
            if (!report.completed)
                report.failure = "One or more V2 validation gates failed.";
        }
        catch (Exception exception)
        {
            report.completed = false;
            report.failure = exception.ToString();
        }

        string json = JsonUtility.ToJson(report, true);
        Debug.Log($"[BAKEROATETOOLV2] Validate StartRoom TEST\n{json}");
        return json;
    }

    public static string ValidateStartRoomPower0ParityCli()
    {
        var report = new Power0ParityReport();
        GameObject authoringRoot = null;
        GameObject canonicalRoot = null;
        GameObject v2Root = null;
        try
        {
            DungeonTileBakeData canonicalP0 = LoadCanonicalData("P0");
            DungeonTileRotationLightingSetV2 set = LoadRequired<DungeonTileRotationLightingSetV2>(OutputSetPath);
            DungeonTileRotationLightingSetV2.RotationVariant r000 =
                set.Variants.FirstOrDefault(variant => variant != null && variant.rotationY == 0);
            DungeonTileBakeData v2P0 = r000?.power0;
            if (v2P0 == null)
                throw new InvalidOperationException("V2 R000 P0 bake data is missing.");

            report.canonicalBake = canonicalP0.name;
            report.v2Bake = v2P0.name;
            report.canonicalLightmapCount = canonicalP0.lightmapColors?.Length ?? 0;
            report.v2LightmapCount = v2P0.lightmapColors?.Length ?? 0;
            report.sameLightmapColorReferences = SameObjectReferences(canonicalP0.lightmapColors, v2P0.lightmapColors);
            report.sameDirectionReferences = SameObjectReferences(canonicalP0.lightmapDirections, v2P0.lightmapDirections);
            report.sameRendererEntryCount = (canonicalP0.rendererEntries?.Length ?? 0) == (v2P0.rendererEntries?.Length ?? 0);
            report.sameReflectionReferences = SameReflectionEntries(canonicalP0.reflectionProbeEntries, v2P0.reflectionProbeEntries);
            report.sameShData = SameLightProbeEntries(canonicalP0.lightProbeEntries, v2P0.lightProbeEntries);

            authoringRoot = PrefabUtility.LoadPrefabContents(AuthoringPrefabPath);
            canonicalRoot = PrefabUtility.LoadPrefabContents(CanonicalPrefabPath);
            v2Root = PrefabUtility.LoadPrefabContents(OutputPrefabPath);

            DungeonTilePowerBakeSet canonicalPowerSet = canonicalRoot.GetComponent<DungeonTilePowerBakeSet>();
            DungeonTilePowerBakeSet v2PowerSet = v2Root.GetComponent<DungeonTilePowerBakeSet>();
            if (canonicalPowerSet == null || v2PowerSet == null)
                throw new InvalidOperationException("Canonical or V2 prefab is missing DungeonTilePowerBakeSet.");

            report.canonicalEmissionEntryCount = canonicalPowerSet.EmissionMaterialEntries.Length;
            report.v2EmissionEntryCount = v2PowerSet.EmissionMaterialEntries.Length;
            report.sameEmissionEntries = SameEmissionEntries(
                canonicalPowerSet.EmissionMaterialEntries,
                v2PowerSet.EmissionMaterialEntries);

            string[] canonicalIgnoreLights = GetEnabledMarkerPaths<IgnoreLightControl>(canonicalRoot.transform);
            string[] v2IgnoreLights = GetEnabledMarkerPaths<IgnoreLightControl>(v2Root.transform);
            report.canonicalIgnoreLightPaths = string.Join(" | ", canonicalIgnoreLights);
            report.v2IgnoreLightPaths = string.Join(" | ", v2IgnoreLights);
            report.sameIgnoreLightMarkers = canonicalIgnoreLights.SequenceEqual(v2IgnoreLights);

            string[] canonicalIgnoreEmission = GetEnabledMarkerPaths<IgnoreEmissionControl>(canonicalRoot.transform);
            string[] v2IgnoreEmission = GetEnabledMarkerPaths<IgnoreEmissionControl>(v2Root.transform);
            report.canonicalIgnoreEmissionPaths = string.Join(" | ", canonicalIgnoreEmission);
            report.v2IgnoreEmissionPaths = string.Join(" | ", v2IgnoreEmission);
            report.sameIgnoreEmissionMarkers = canonicalIgnoreEmission.SequenceEqual(v2IgnoreEmission);

            string[] authoringIgnoreLights = GetEnabledMarkerPaths<IgnoreLightControl>(authoringRoot.transform);
            string[] authoringIgnoreEmission = GetEnabledMarkerPaths<IgnoreEmissionControl>(authoringRoot.transform);
            report.authoringIgnoreLightPaths = string.Join(" | ", authoringIgnoreLights);
            report.authoringIgnoreEmissionPaths = string.Join(" | ", authoringIgnoreEmission);
            report.canonicalPreservesAuthoringIgnoreLightMarkers = authoringIgnoreLights.SequenceEqual(canonicalIgnoreLights);
            report.canonicalPreservesAuthoringIgnoreEmissionMarkers = authoringIgnoreEmission.SequenceEqual(canonicalIgnoreEmission);

            report.canonicalSuppressesP0ReflectionProbes = GetSerializedBool(
                canonicalRoot.GetComponent<DungeonTileLightmapSwitcher>(),
                "disableReflectionProbesOnPower0");
            report.v2SuppressesP0ReflectionProbes = GetSerializedBool(
                v2Root.GetComponent<DungeonTileLightmapSwitcher>(),
                "disableReflectionProbesOnPower0");
            report.sameP0ReflectionSuppression =
                report.canonicalSuppressesP0ReflectionProbes == report.v2SuppressesP0ReflectionProbes;

            report.completed = report.sameLightmapColorReferences &&
                               report.sameDirectionReferences &&
                               report.sameRendererEntryCount &&
                               report.sameReflectionReferences &&
                               report.sameShData &&
                               report.sameEmissionEntries &&
                               report.sameIgnoreLightMarkers &&
                               report.sameIgnoreEmissionMarkers &&
                               report.sameP0ReflectionSuppression;
            report.interpretation =
                "PASS means V2 R000 uses the exact original P0 bake references and preserves the original " +
                "IgnoreLightControl/IgnoreEmissionControl exceptions and runtime emission mapping. " +
                "Those marked exceptions are intentionally allowed to remain visible in P0.";
            if (!report.canonicalPreservesAuthoringIgnoreLightMarkers ||
                !report.canonicalPreservesAuthoringIgnoreEmissionMarkers)
            {
                report.warning =
                    "The current canonical R000 output does not preserve every enabled control marker from " +
                    "Tile_modified/StartRoom. V2 matches that output, but a new canonical P0 bake must preserve " +
                    "the authoring markers before this can be called authoring-equivalent.";
            }
            if (!report.completed)
                report.failure = "V2 P0 differs from the original BakeRotationTool output or prefab control markers.";
        }
        catch (Exception exception)
        {
            report.completed = false;
            report.failure = exception.ToString();
        }
        finally
        {
            if (authoringRoot != null)
                PrefabUtility.UnloadPrefabContents(authoringRoot);
            if (canonicalRoot != null)
                PrefabUtility.UnloadPrefabContents(canonicalRoot);
            if (v2Root != null)
                PrefabUtility.UnloadPrefabContents(v2Root);
        }

        string json = JsonUtility.ToJson(report, true);
        Debug.Log($"[BAKEROATETOOLV2] Validate StartRoom P0 parity\n{json}");
        return json;
    }

    public static RoomContext ResolveRoomContext(string sourcePrefabPath, bool preferTestCanonical)
    {
        string sourcePath = NormalizeAssetPath(sourcePrefabPath);
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath) == null)
        {
            throw new InvalidOperationException("Select a prefab asset from NewPrison/Tile_modified or NewPrison/Tiles.");
        }

        string productionRoot;
        if (sourcePath.StartsWith(TileModifiedRoot + "/", StringComparison.OrdinalIgnoreCase))
            productionRoot = TilesRotatedRoot;
        else if (sourcePath.StartsWith(TilesRoot + "/", StringComparison.OrdinalIgnoreCase))
            productionRoot = TilesRotated2Root;
        else
            throw new InvalidOperationException("V2 supports source prefabs under NewPrison/Tile_modified or NewPrison/Tiles only.");

        string tileName = Path.GetFileNameWithoutExtension(sourcePath);
        string outputFolder = $"{TestRoot}/V2_{tileName}";
        string testCanonicalFolder = outputFolder + "/Canonical";
        var context = new RoomContext
        {
            tileName = tileName,
            sourcePrefabPath = sourcePath,
            productionCanonicalRoot = productionRoot,
            productionCanonicalPrefabPath = $"{productionRoot}/{tileName}_R000.prefab",
            productionCanonicalDataRoot = $"{productionRoot}/BakedData/{tileName}_R000",
            outputFolder = outputFolder,
            generatedFolder = outputFolder + "/Generated",
            outputPrefabPath = $"{outputFolder}/{tileName}.prefab",
            outputSetPath = $"{outputFolder}/{tileName}_LightingSetV2.asset",
            testCanonicalFolder = testCanonicalFolder,
            testCanonicalPrefabPath = $"{testCanonicalFolder}/{tileName}_R000.prefab",
            testCanonicalDataRoot = $"{testCanonicalFolder}/BakedData/{tileName}_R000"
        };

        bool singleBakedState = UsesSingleBakedState(sourcePath);
        bool testReady = AssetDatabase.LoadAssetAtPath<GameObject>(context.testCanonicalPrefabPath) != null &&
                         AssetDatabase.LoadAssetAtPath<DungeonTileBakeData>(
                             $"{context.testCanonicalDataRoot}/P100/{tileName}_R000_BakeData.asset") != null &&
                         (singleBakedState ||
                          AssetDatabase.LoadAssetAtPath<DungeonTileBakeData>(
                              $"{context.testCanonicalDataRoot}/P0/{tileName}_R000_BakeData.asset") != null);
        context.usingTestCanonical = preferTestCanonical && testReady;
        context.canonicalPrefabPath = context.usingTestCanonical
            ? context.testCanonicalPrefabPath
            : context.productionCanonicalPrefabPath;
        context.canonicalDataRoot = context.usingTestCanonical
            ? context.testCanonicalDataRoot
            : context.productionCanonicalDataRoot;
        return context;
    }

    private static string BuildCanonicalDataPath(RoomContext context, string power)
    {
        return $"{context.canonicalDataRoot}/{power}/{context.tileName}_R000_BakeData.asset";
    }

    private static DungeonTileBakeData LoadCanonicalData(RoomContext context, string power)
    {
        return LoadRequired<DungeonTileBakeData>(BuildCanonicalDataPath(context, power));
    }

    private static bool UsesSingleBakedState(string prefabPath)
    {
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(prefabPath);
            DungeonTileLightingPolicy policy = root.GetComponent<DungeonTileLightingPolicy>();
            return policy != null &&
                   policy.Mode == DungeonTileLightmapSwitcher.LightingMode.SingleBakedState;
        }
        finally
        {
            if (root != null)
                PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static DungeonTileBakeData CreateDerivedDataForRoom(
        RoomContext context,
        DungeonTileRotationLightingSetV2 container,
        DungeonTileBakeData canonical,
        int rotation,
        string power,
        Dictionary<int, double[,]> matrices,
        BuildReport report)
    {
        var derived = ScriptableObject.CreateInstance<DungeonTileBakeData>();
        derived.name = $"{context.tileName}_R{rotation:000}_{power}_V2";
        derived.hideFlags = HideFlags.HideInHierarchy;
        derived.lightmapsMode = canonical.lightmapsMode;
        derived.lightmapColors = CloneArray(canonical.lightmapColors);
        report.sharedColorReferenceCount += derived.lightmapColors.Length;

        Texture2D[] canonicalDirections = canonical.lightmapDirections ?? Array.Empty<Texture2D>();
        derived.lightmapDirections = new Texture2D[canonicalDirections.Length];
        for (int i = 0; i < canonicalDirections.Length; i++)
        {
            Texture2D source = canonicalDirections[i];
            if (source == null || rotation == 0)
            {
                derived.lightmapDirections[i] = source;
                continue;
            }
            derived.lightmapDirections[i] = CreateRotatedDirectionTextureForRoom(context, source, rotation, power, i);
            report.generatedDirectionTextureCount++;
        }

        derived.rendererEntries = CloneArray(canonical.rendererEntries);
        derived.lightProbeEntries = RotateLightProbeEntries(
            canonical.lightProbeEntries ?? Array.Empty<DungeonTileBakeData.LightProbeBakeEntry>(),
            rotation,
            matrices);
        if (rotation != 0 && derived.lightProbeEntries.Length > 0)
            report.derivedShProbeSetCount++;

        DungeonTileBakeData.ReflectionProbeBakeEntry[] sourceReflection =
            canonical.reflectionProbeEntries ?? Array.Empty<DungeonTileBakeData.ReflectionProbeBakeEntry>();
        derived.reflectionProbeEntries = new DungeonTileBakeData.ReflectionProbeBakeEntry[sourceReflection.Length];
        for (int i = 0; i < sourceReflection.Length; i++)
        {
            Cubemap texture = sourceReflection[i].bakedTexture;
            if (texture != null && rotation != 0)
            {
                texture = CreateRotatedCubemapForRoom(context, container, texture, rotation, power, i);
                report.generatedReflectionCubemapCount++;
            }
            derived.reflectionProbeEntries[i] = new DungeonTileBakeData.ReflectionProbeBakeEntry
            {
                relativePath = sourceReflection[i].relativePath,
                bakedTexture = texture
            };
        }

        AssetDatabase.AddObjectToAsset(derived, container);
        EditorUtility.SetDirty(derived);
        return derived;
    }

    private static Texture2D CreateRotatedDirectionTextureForRoom(
        RoomContext context,
        Texture2D source,
        int rotation,
        string power,
        int index)
    {
        Texture2D readable = ReadbackTexture(source);
        string assetPath = $"{context.generatedFolder}/{context.tileName}_R{rotation:000}_{power}_Dir{index:00}.png";
        try
        {
            Color[] pixels = readable.GetPixels();
            Quaternion quaternion = Quaternion.Euler(0f, rotation, 0f);
            for (int i = 0; i < pixels.Length; i++)
            {
                Color encoded = pixels[i];
                Vector3 direction = new Vector3(encoded.r - 0.5f, encoded.g - 0.5f, encoded.b - 0.5f);
                direction = quaternion * direction;
                pixels[i] = new Color(direction.x + 0.5f, direction.y + 0.5f, direction.z + 0.5f, encoded.a);
            }

            var encodedTexture = new Texture2D(readable.width, readable.height, TextureFormat.RGBA32, false, true);
            try
            {
                encodedTexture.SetPixels(pixels);
                encodedTexture.Apply(false, false);
                File.WriteAllBytes(Path.GetFullPath(assetPath), encodedTexture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(encodedTexture);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(readable);
        }

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = source.mipmapCount > 1;
            importer.wrapMode = source.wrapMode;
            importer.filterMode = source.filterMode;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return LoadRequired<Texture2D>(assetPath);
    }

    private static Cubemap CreateRotatedCubemapForRoom(
        RoomContext context,
        DungeonTileRotationLightingSetV2 container,
        Cubemap source,
        int rotation,
        string power,
        int index)
    {
        CubePixels pixels = ReadbackCubemap(source);
        Quaternion inverse = Quaternion.Euler(0f, -rotation, 0f);
        var result = new Cubemap(source.width, TextureFormat.RGBAHalf, true)
        {
            name = $"{context.tileName}_R{rotation:000}_{power}_Reflection{index:00}",
            wrapMode = source.wrapMode,
            filterMode = source.filterMode,
            hideFlags = HideFlags.HideInHierarchy
        };

        for (int faceIndex = 0; faceIndex < 6; faceIndex++)
        {
            CubemapFace face = (CubemapFace)faceIndex;
            var output = new Color[pixels.size * pixels.size];
            for (int y = 0; y < pixels.size; y++)
            {
                float v = ((y + 0.5f) / pixels.size) * 2f - 1f;
                for (int x = 0; x < pixels.size; x++)
                {
                    float u = ((x + 0.5f) / pixels.size) * 2f - 1f;
                    Vector3 destinationDirection = CubemapTexelDirection(face, u, v);
                    output[y * pixels.size + x] = SampleCubemap(pixels, inverse * destinationDirection);
                }
            }
            result.SetPixels(output, face, 0);
        }
        result.Apply(true, false);
        AssetDatabase.AddObjectToAsset(result, container);
        EditorUtility.SetDirty(result);
        return result;
    }

    private static void PopulateValidation(RoomContext context, BuildReport report)
    {
        report.prefabPath = context.outputPrefabPath;
        report.lightingSetPath = context.outputSetPath;
        report.prefabCount = CountDirectPrefabs(context.outputFolder);
        DungeonTileRotationLightingSetV2 set = LoadRequired<DungeonTileRotationLightingSetV2>(context.outputSetPath);
        bool singleBakedState = UsesSingleBakedState(context.sourcePrefabPath);
        DungeonTileBakeData canonicalP100 = LoadCanonicalData(context, "P100");
        DungeonTileBakeData canonicalP0 = singleBakedState
            ? canonicalP100
            : LoadCanonicalData(context, "P0");
        report.rotationVariantCount = set.Variants.Length;
        report.allVariantsComplete = set.Variants.Length == 4;
        report.canonicalColorsShared = true;
        report.rotationDataNumericallyValid = true;

        foreach (DungeonTileRotationLightingSetV2.RotationVariant variant in set.Variants)
        {
            if (variant == null || variant.power100 == null || variant.power0 == null)
            {
                report.allVariantsComplete = false;
                continue;
            }
            if (!SameReferences(variant.power100.lightmapColors, canonicalP100.lightmapColors) ||
                !SameReferences(variant.power0.lightmapColors, canonicalP0.lightmapColors))
                report.canonicalColorsShared = false;
            if ((variant.power100.rendererEntries?.Length ?? 0) != (canonicalP100.rendererEntries?.Length ?? 0) ||
                (variant.power0.rendererEntries?.Length ?? 0) != (canonicalP0.rendererEntries?.Length ?? 0) ||
                (variant.power100.lightProbeEntries?.Length ?? 0) != (canonicalP100.lightProbeEntries?.Length ?? 0) ||
                (variant.power0.lightProbeEntries?.Length ?? 0) != (canonicalP0.lightProbeEntries?.Length ?? 0))
            {
                report.allVariantsComplete = false;
            }

            int rotation = DungeonTileRotationLightingSetV2.QuantizeRotation(variant.rotationY);
            if (rotation != 0)
            {
                ValidateDerivedData(canonicalP100, variant.power100, rotation, report);
                if (!singleBakedState)
                    ValidateDerivedData(canonicalP0, variant.power0, rotation, report);
            }
        }

        GameObject prefab = LoadRequired<GameObject>(context.outputPrefabPath);
        DungeonTileRotationSelectorV2 selector = prefab.GetComponent<DungeonTileRotationSelectorV2>();
        report.selectorAssigned = selector != null && selector.LightingSet == set;
        DunGen.Tile tile = prefab.GetComponentInChildren<DunGen.Tile>(true);
        if (tile != null)
        {
            var serializedTile = new SerializedObject(tile);
            SerializedProperty allowRotation = serializedTile.FindProperty("AllowRotation");
            report.allowRotation = allowRotation != null && allowRotation.boolValue;
        }

        report.generatedDirectionTextureCount = CountGeneratedDirectionTextures(context.generatedFolder);
        report.generatedReflectionCubemapCount = CountSubAssets<Cubemap>(context.outputSetPath);
        report.derivedShProbeSetCount = set.Variants.Length > 0
            ? (set.Variants.Length - 1) * (singleBakedState ? 1 : 2)
            : 0;
        report.sharedColorReferenceCount = set.Variants.Length *
                                           ((canonicalP100.lightmapColors?.Length ?? 0) +
                                            (singleBakedState ? 0 : canonicalP0.lightmapColors?.Length ?? 0));
        report.rotationDataNumericallyValid = report.maxDirectionVectorError <= 0.03f &&
                                              report.maxShEvaluationError <= 0.00001f &&
                                              report.meanReflectionRgbError <= 0.03f;
    }

    private static void NormalizeV2NavigationAuthoring(GameObject root)
    {
        if (root == null)
            return;

        foreach (Unity.AI.Navigation.NavMeshSurface surface in root.GetComponentsInChildren<Unity.AI.Navigation.NavMeshSurface>(true))
        {
            if (surface == null)
                continue;

            surface.RemoveData();
            surface.navMeshData = null;
            surface.enabled = false;
        }

        foreach (Unity.AI.Navigation.NavMeshLink link in root.GetComponentsInChildren<Unity.AI.Navigation.NavMeshLink>(true))
        {
            if (link != null && link.GetComponentInParent<Doorway>() != null)
                link.enabled = false;
        }

        foreach (NavMeshObstacle obstacle in root.GetComponentsInChildren<NavMeshObstacle>(true))
        {
            if (obstacle == null || obstacle.GetComponentInParent<Door>() == null)
                continue;

            obstacle.carving = false;
            obstacle.enabled = false;
        }
    }

    private static int CountAuthoredFloorColliders(Transform root)
    {
        if (root == null)
            return 0;

        int floorLayer = LayerMask.NameToLayer("Floor");
        if (floorLayer < 0)
            return 0;

        int count = 0;
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (collider != null && collider.enabled && !collider.isTrigger &&
                collider.gameObject.activeInHierarchy && collider.gameObject.layer == floorLayer)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountGeneratedDirectionTextures(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder))
            return 0;
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                count++;
        }
        return count;
    }

    private static void ClearLightingSetSubAssets(string assetPath, DungeonTileRotationLightingSetV2 mainAsset)
    {
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (asset != null && asset != mainAsset)
                UnityEngine.Object.DestroyImmediate(asset, true);
        }
        EditorUtility.SetDirty(mainAsset);
        AssetDatabase.SaveAssets();
    }

    private static void BackupDerivedOutput(RoomContext context)
    {
        string backupFolder = $"{context.outputFolder}/History/{DateTime.Now:yyyyMMdd_HHmmss_fff}";
        EnsureFolder(backupFolder);
        CopyAssetForBackup(context.outputPrefabPath, $"{backupFolder}/{context.tileName}.prefab");
        CopyAssetForBackup(context.outputSetPath, $"{backupFolder}/{context.tileName}_LightingSetV2.asset");
        if (AssetDatabase.IsValidFolder(context.generatedFolder))
        {
            string error = AssetDatabase.CopyAsset(context.generatedFolder, backupFolder + "/Generated")
                ? string.Empty
                : "AssetDatabase.CopyAsset returned false.";
            if (!string.IsNullOrEmpty(error))
                throw new InvalidOperationException("Could not back up V2 Generated folder: " + error);
        }
    }

    private static void CopyAssetForBackup(string source, string destination)
    {
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(source) == null)
            return;
        if (!AssetDatabase.CopyAsset(source, destination))
            throw new InvalidOperationException($"Could not back up asset: {source} -> {destination}");
    }

    private static string ArchiveFolderByMove(RoomContext context, string sourceFolder, string label)
    {
        string archiveParent = $"{context.outputFolder}/History/{DateTime.Now:yyyyMMdd_HHmmss_fff}";
        EnsureFolder(archiveParent);
        string destination = archiveParent + "/" + label;
        string error = AssetDatabase.MoveAsset(sourceFolder, destination);
        if (!string.IsNullOrEmpty(error))
            throw new InvalidOperationException($"Could not archive {sourceFolder}: {error}");
        return destination;
    }

    private static object InvokePrivate(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = target.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(candidate => candidate.Name == methodName &&
                                         candidate.GetParameters().Length == arguments.Length);
        if (method == null)
            throw new MissingMethodException(target.GetType().FullName, methodName);
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static bool ValidateBakeBridge(out string failure)
    {
        Type type = typeof(DungeonTileRotationBakeTool);
        string[] fields =
        {
            "useSelectedPrefabs", "sourceFolder", "outputFolder", "bakeScenePath",
            "collectBakedFilesToFolder", "bakedFilesUnderOutputFolder", "setAllowRotationFalse",
            "removeNavMeshLinksFromVariants", "buildBakeScene", "runBakeAfterBuild",
            "prepareDoorwayHoleWallsForBake",
            "useFixedBakeScenePath", "autoNormalizeNewPrisonRoomsBeforeBake",
            "bakePower100", "bakePower00", "overrideBakeQuality", "bakeDirectSampleCount",
            "bakeIndirectSampleCount", "bakeEnvironmentSampleCount", "bakeBounces", "bakePadding",
            "bakeTextureCompression", "cliRotationOverride", "fitReflectionProbesBeforeBake"
        };
        foreach (string field in fields)
        {
            if (type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic) == null)
            {
                failure = "missing field " + field;
                return false;
            }
        }

        (string name, int parameters)[] methods =
        {
            ("LoadEmissionMaterialVariants", 0),
            ("ConfigureCliAdminstrativeBakeDefaults", 0),
            ("GenerateVariants", 1),
            ("FitReflectionProbesForPrefabs", 2),
            ("GetResolvedBakedFilesFolder", 0),
            ("GetBakeDataAssetRootFolder", 0),
            ("BuildAndOptionallyBakeScene", 3),
            ("AttachLightmapSwitchers", 1)
        };
        MethodInfo[] available = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
        foreach ((string name, int parameters) method in methods)
        {
            if (!available.Any(candidate => candidate.Name == method.name &&
                                            candidate.GetParameters().Length == method.parameters))
            {
                failure = $"missing method {method.name}/{method.parameters}";
                return false;
            }
        }

        failure = string.Empty;
        return true;
    }

    private static object GetPrivateField(object target, string fieldName)
    {
        return RequireField(target.GetType(), fieldName).GetValue(target);
    }

    private static string UnwrapException(Exception exception)
    {
        Exception current = exception;
        while (current is TargetInvocationException invocation && invocation.InnerException != null)
            current = invocation.InnerException;
        return current.ToString();
    }

    private static bool IsAssetNewer(string sourceAssetPath, string targetAssetPath)
    {
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(sourceAssetPath) == null ||
            AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetAssetPath) == null)
            return false;
        string sourceFullPath = Path.GetFullPath(sourceAssetPath);
        string targetFullPath = Path.GetFullPath(targetAssetPath);
        return File.Exists(sourceFullPath) && File.Exists(targetFullPath) &&
               File.GetLastWriteTimeUtc(sourceFullPath) > File.GetLastWriteTimeUtc(targetFullPath);
    }

    private static string NormalizeAssetPath(string path)
    {
        return (path ?? string.Empty).Replace('\\', '/').Trim();
    }

    private static DungeonTileBakeData CreateDerivedData(
        DungeonTileRotationLightingSetV2 container,
        DungeonTileBakeData canonical,
        int rotation,
        string power,
        Dictionary<int, double[,]> matrices,
        BuildReport report)
    {
        var derived = ScriptableObject.CreateInstance<DungeonTileBakeData>();
        derived.name = $"{TileName}_R{rotation:000}_{power}_V2";
        derived.hideFlags = HideFlags.HideInHierarchy;
        derived.lightmapsMode = canonical.lightmapsMode;
        derived.lightmapColors = CloneArray(canonical.lightmapColors);
        report.sharedColorReferenceCount += derived.lightmapColors.Length;

        Texture2D[] canonicalDirections = canonical.lightmapDirections ?? Array.Empty<Texture2D>();
        derived.lightmapDirections = new Texture2D[canonicalDirections.Length];
        for (int i = 0; i < canonicalDirections.Length; i++)
        {
            Texture2D source = canonicalDirections[i];
            if (source == null || rotation == 0)
            {
                derived.lightmapDirections[i] = source;
                continue;
            }
            derived.lightmapDirections[i] = CreateRotatedDirectionTexture(source, rotation, power, i);
            report.generatedDirectionTextureCount++;
        }

        derived.rendererEntries = CloneArray(canonical.rendererEntries);
        derived.lightProbeEntries = RotateLightProbeEntries(
            canonical.lightProbeEntries ?? Array.Empty<DungeonTileBakeData.LightProbeBakeEntry>(),
            rotation,
            matrices);
        if (rotation != 0 && derived.lightProbeEntries.Length > 0)
            report.derivedShProbeSetCount++;

        DungeonTileBakeData.ReflectionProbeBakeEntry[] sourceReflection =
            canonical.reflectionProbeEntries ?? Array.Empty<DungeonTileBakeData.ReflectionProbeBakeEntry>();
        derived.reflectionProbeEntries = new DungeonTileBakeData.ReflectionProbeBakeEntry[sourceReflection.Length];
        for (int i = 0; i < sourceReflection.Length; i++)
        {
            Cubemap texture = sourceReflection[i].bakedTexture;
            if (texture != null && rotation != 0)
            {
                texture = CreateRotatedCubemap(container, texture, rotation, power, i);
                report.generatedReflectionCubemapCount++;
            }
            derived.reflectionProbeEntries[i] = new DungeonTileBakeData.ReflectionProbeBakeEntry
            {
                relativePath = sourceReflection[i].relativePath,
                bakedTexture = texture
            };
        }

        AssetDatabase.AddObjectToAsset(derived, container);
        EditorUtility.SetDirty(derived);
        return derived;
    }

    private static Texture2D CreateRotatedDirectionTexture(Texture2D source, int rotation, string power, int index)
    {
        Texture2D readable = ReadbackTexture(source);
        string assetPath = $"{GeneratedFolder}/{TileName}_R{rotation:000}_{power}_Dir{index:00}.png";
        try
        {
            Color[] pixels = readable.GetPixels();
            Quaternion quaternion = Quaternion.Euler(0f, rotation, 0f);
            for (int i = 0; i < pixels.Length; i++)
            {
                Color encoded = pixels[i];
                Vector3 direction = new Vector3(encoded.r - 0.5f, encoded.g - 0.5f, encoded.b - 0.5f);
                direction = quaternion * direction;
                pixels[i] = new Color(direction.x + 0.5f, direction.y + 0.5f, direction.z + 0.5f, encoded.a);
            }

            var encodedTexture = new Texture2D(readable.width, readable.height, TextureFormat.RGBA32, false, true);
            try
            {
                encodedTexture.SetPixels(pixels);
                encodedTexture.Apply(false, false);
                File.WriteAllBytes(Path.GetFullPath(assetPath), encodedTexture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(encodedTexture);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(readable);
        }

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = source.mipmapCount > 1;
            importer.wrapMode = source.wrapMode;
            importer.filterMode = source.filterMode;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return LoadRequired<Texture2D>(assetPath);
    }

    private static Cubemap CreateRotatedCubemap(
        DungeonTileRotationLightingSetV2 container,
        Cubemap source,
        int rotation,
        string power,
        int index)
    {
        CubePixels pixels = ReadbackCubemap(source);
        Quaternion inverse = Quaternion.Euler(0f, -rotation, 0f);
        var result = new Cubemap(source.width, TextureFormat.RGBAHalf, true)
        {
            name = $"{TileName}_R{rotation:000}_{power}_Reflection{index:00}",
            wrapMode = source.wrapMode,
            filterMode = source.filterMode,
            hideFlags = HideFlags.HideInHierarchy
        };

        for (int faceIndex = 0; faceIndex < 6; faceIndex++)
        {
            CubemapFace face = (CubemapFace)faceIndex;
            var output = new Color[pixels.size * pixels.size];
            for (int y = 0; y < pixels.size; y++)
            {
                float v = ((y + 0.5f) / pixels.size) * 2f - 1f;
                for (int x = 0; x < pixels.size; x++)
                {
                    float u = ((x + 0.5f) / pixels.size) * 2f - 1f;
                    Vector3 destinationDirection = CubemapTexelDirection(face, u, v);
                    output[y * pixels.size + x] = SampleCubemap(pixels, inverse * destinationDirection);
                }
            }
            result.SetPixels(output, face, 0);
        }
        result.Apply(true, false);
        AssetDatabase.AddObjectToAsset(result, container);
        EditorUtility.SetDirty(result);
        return result;
    }

    private static DungeonTileBakeData.LightProbeBakeEntry[] RotateLightProbeEntries(
        DungeonTileBakeData.LightProbeBakeEntry[] source,
        int rotation,
        Dictionary<int, double[,]> matrices)
    {
        var result = new DungeonTileBakeData.LightProbeBakeEntry[source.Length];
        if (rotation == 0)
        {
            Array.Copy(source, result, source.Length);
            return result;
        }

        double[,] matrix = matrices[rotation];
        for (int i = 0; i < source.Length; i++)
        {
            SphericalHarmonicsL2 rotated = RotateSh(source[i].ToSphericalHarmonics(), matrix);
            result[i] = DungeonTileBakeData.LightProbeBakeEntry.FromProbe(
                source[i].localPosition,
                rotated,
                source[i].occlusion);
        }
        return result;
    }

    private static void PopulateValidation(BuildReport report)
    {
        report.prefabPath = OutputPrefabPath;
        report.lightingSetPath = OutputSetPath;
        report.prefabCount = CountDirectPrefabs(OutputFolder);

        DungeonTileRotationLightingSetV2 set = LoadRequired<DungeonTileRotationLightingSetV2>(OutputSetPath);
        DungeonTileBakeData canonicalP100 = LoadCanonicalData("P100");
        DungeonTileBakeData canonicalP0 = LoadCanonicalData("P0");
        report.rotationVariantCount = set.Variants.Length;
        report.allVariantsComplete = set.Variants.Length == 4;
        report.canonicalColorsShared = true;
        report.rotationDataNumericallyValid = true;

        foreach (DungeonTileRotationLightingSetV2.RotationVariant variant in set.Variants)
        {
            if (variant == null || variant.power100 == null || variant.power0 == null)
            {
                report.allVariantsComplete = false;
                continue;
            }
            if (!SameReferences(variant.power100.lightmapColors, canonicalP100.lightmapColors) ||
                !SameReferences(variant.power0.lightmapColors, canonicalP0.lightmapColors))
                report.canonicalColorsShared = false;
            if (variant.power100.rendererEntries.Length != canonicalP100.rendererEntries.Length ||
                variant.power0.rendererEntries.Length != canonicalP0.rendererEntries.Length)
                report.allVariantsComplete = false;
            if (variant.power100.lightProbeEntries.Length != canonicalP100.lightProbeEntries.Length ||
                variant.power0.lightProbeEntries.Length != canonicalP0.lightProbeEntries.Length)
                report.allVariantsComplete = false;

            int rotation = DungeonTileRotationLightingSetV2.QuantizeRotation(variant.rotationY);
            if (rotation != 0)
            {
                ValidateDerivedData(canonicalP100, variant.power100, rotation, report);
                ValidateDerivedData(canonicalP0, variant.power0, rotation, report);
            }
        }

        GameObject prefab = LoadRequired<GameObject>(OutputPrefabPath);
        DungeonTileRotationSelectorV2 selector = prefab.GetComponent<DungeonTileRotationSelectorV2>();
        report.selectorAssigned = selector != null && selector.LightingSet == set;
        DunGen.Tile tile = prefab.GetComponentInChildren<DunGen.Tile>(true);
        if (tile != null)
        {
            var serializedTile = new SerializedObject(tile);
            SerializedProperty allowRotation = serializedTile.FindProperty("AllowRotation");
            report.allowRotation = allowRotation != null && allowRotation.boolValue;
        }

        report.generatedDirectionTextureCount = CountGeneratedDirectionTextures();
        report.generatedReflectionCubemapCount = CountSubAssets<Cubemap>(OutputSetPath);
        report.derivedShProbeSetCount = set.Variants.Length > 0 ? (set.Variants.Length - 1) * 2 : 0;
        report.sharedColorReferenceCount = set.Variants.Length *
                                           (canonicalP100.lightmapColors.Length + canonicalP0.lightmapColors.Length);
        report.rotationDataNumericallyValid = report.maxDirectionVectorError <= 0.03f &&
                                              report.maxShEvaluationError <= 0.00001f &&
                                              report.meanReflectionRgbError <= 0.03f;
    }

    private static void ValidateDerivedData(
        DungeonTileBakeData canonical,
        DungeonTileBakeData derived,
        int rotation,
        BuildReport report)
    {
        Quaternion quaternion = Quaternion.Euler(0f, rotation, 0f);
        Texture2D[] sourceDirections = canonical.lightmapDirections ?? Array.Empty<Texture2D>();
        Texture2D[] targetDirections = derived.lightmapDirections ?? Array.Empty<Texture2D>();
        int directionCount = Mathf.Min(sourceDirections.Length, targetDirections.Length);
        for (int i = 0; i < directionCount; i++)
        {
            if (sourceDirections[i] == null || targetDirections[i] == null)
                continue;
            Texture2D source = ReadbackTexture(sourceDirections[i]);
            Texture2D target = ReadbackTexture(targetDirections[i]);
            try
            {
                Color[] a = source.GetPixels();
                Color[] b = target.GetPixels();
                int stride = Mathf.Max(1, a.Length / 4096);
                for (int pixel = 0; pixel < Mathf.Min(a.Length, b.Length); pixel += stride)
                {
                    Vector3 expected = quaternion * new Vector3(a[pixel].r - 0.5f, a[pixel].g - 0.5f, a[pixel].b - 0.5f);
                    Vector3 actual = new Vector3(b[pixel].r - 0.5f, b[pixel].g - 0.5f, b[pixel].b - 0.5f);
                    report.maxDirectionVectorError = Mathf.Max(report.maxDirectionVectorError, Vector3.Distance(expected, actual));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        Vector3[] directions = BuildFibonacciDirections(64);
        Vector3[] inverseDirections = RotateDirections(directions, -rotation);
        int probeCount = Mathf.Min(
            canonical.lightProbeEntries?.Length ?? 0,
            derived.lightProbeEntries?.Length ?? 0);
        var expectedColors = new Color[directions.Length];
        var actualColors = new Color[directions.Length];
        for (int probe = 0; probe < probeCount; probe++)
        {
            canonical.lightProbeEntries[probe].ToSphericalHarmonics().Evaluate(inverseDirections, expectedColors);
            derived.lightProbeEntries[probe].ToSphericalHarmonics().Evaluate(directions, actualColors);
            for (int sample = 0; sample < directions.Length; sample++)
            {
                Vector3 expected = new Vector3(expectedColors[sample].r, expectedColors[sample].g, expectedColors[sample].b);
                Vector3 actual = new Vector3(actualColors[sample].r, actualColors[sample].g, actualColors[sample].b);
                report.maxShEvaluationError = Mathf.Max(report.maxShEvaluationError, Vector3.Distance(expected, actual));
            }
        }

        DungeonTileBakeData.ReflectionProbeBakeEntry[] sourceReflection =
            canonical.reflectionProbeEntries ?? Array.Empty<DungeonTileBakeData.ReflectionProbeBakeEntry>();
        DungeonTileBakeData.ReflectionProbeBakeEntry[] targetReflection =
            derived.reflectionProbeEntries ?? Array.Empty<DungeonTileBakeData.ReflectionProbeBakeEntry>();
        int reflectionCount = Mathf.Min(sourceReflection.Length, targetReflection.Length);
        Quaternion inverse = Quaternion.Euler(0f, -rotation, 0f);
        Vector3[] cubeDirections = BuildFibonacciDirections(256);
        for (int probe = 0; probe < reflectionCount; probe++)
        {
            if (sourceReflection[probe].bakedTexture == null || targetReflection[probe].bakedTexture == null)
                continue;
            CubePixels source = ReadbackCubemap(sourceReflection[probe].bakedTexture);
            CubePixels target = ReadbackCubemap(targetReflection[probe].bakedTexture);
            foreach (Vector3 direction in cubeDirections)
            {
                Color expected = SampleCubemap(source, inverse * direction);
                Color actual = SampleCubemap(target, direction);
                Vector3 expectedRgb = new Vector3(expected.r, expected.g, expected.b);
                Vector3 actualRgb = new Vector3(actual.r, actual.g, actual.b);
                float error = Vector3.Distance(expectedRgb, actualRgb);
                report.maxReflectionRgbError = Mathf.Max(report.maxReflectionRgbError, error);
                report.reflectionRgbErrorSum += error;
                report.reflectionRgbErrorCount++;
            }
        }
        report.meanReflectionRgbError = report.reflectionRgbErrorCount > 0
            ? (float)(report.reflectionRgbErrorSum / report.reflectionRgbErrorCount)
            : 0f;
    }

    private static int CountDirectPrefabs(string folder)
    {
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.Equals(Path.GetDirectoryName(path)?.Replace('\\', '/'), folder, StringComparison.OrdinalIgnoreCase))
                count++;
        }
        return count;
    }

    private static int CountGeneratedDirectionTextures()
    {
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { GeneratedFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                count++;
        }
        return count;
    }

    private static int CountSubAssets<T>(string assetPath) where T : UnityEngine.Object
    {
        int count = 0;
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (asset is T)
                count++;
        }
        return count;
    }

    private static bool SameReferences<T>(T[] a, T[] b) where T : UnityEngine.Object
    {
        if (a == null || b == null || a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    private static T[] CloneArray<T>(T[] source)
    {
        if (source == null || source.Length == 0)
            return Array.Empty<T>();
        return (T[])source.Clone();
    }

    private static DungeonTileBakeData LoadCanonicalData(string power)
    {
        return LoadRequired<DungeonTileBakeData>(
            $"{CanonicalDataRoot}/{power}/StartRoom_R000_BakeData.asset");
    }

    private static T LoadRequired<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
            throw new InvalidOperationException($"Required asset was not found: {path}");
        return asset;
    }

    private static T LoadOrCreateAsset<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null)
            return asset;

        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static FieldInfo RequireField(Type type, string fieldName)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException($"Missing private field '{fieldName}' on {type.FullName}.");
        return field;
    }

    private static bool GetPrivateBool(object target, string fieldName)
    {
        return (bool)RequireField(target.GetType(), fieldName).GetValue(target);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        RequireField(target.GetType(), fieldName).SetValue(target, value);
    }

    private static void EnsureFolder(string assetFolder)
    {
        string normalized = assetFolder.Replace('\\', '/').TrimEnd('/');
        if (AssetDatabase.IsValidFolder(normalized))
            return;
        string parent = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
        string name = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"Invalid asset folder: {assetFolder}");
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static Texture2D ReadbackTexture(Texture source)
    {
        RenderTexture temporary = RenderTexture.GetTemporary(
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
                    decoded, 0, 0, source.width, 0, source.height, face, 1, TextureFormat.RGBAFloat);
                request.WaitForCompletion();
                if (request.hasError)
                    throw new InvalidOperationException($"GPU readback failed for cubemap '{source.name}', face {face}.");
                NativeArray<Color> native = request.GetData<Color>();
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
            if (direction.x >= 0f) { face = (int)CubemapFace.PositiveX; u = -direction.z / ax; v = -direction.y / ax; }
            else { face = (int)CubemapFace.NegativeX; u = direction.z / ax; v = -direction.y / ax; }
        }
        else if (ay >= ax && ay >= az)
        {
            if (direction.y >= 0f) { face = (int)CubemapFace.PositiveY; u = direction.x / ay; v = direction.z / ay; }
            else { face = (int)CubemapFace.NegativeY; u = direction.x / ay; v = -direction.z / ay; }
        }
        else
        {
            if (direction.z >= 0f) { face = (int)CubemapFace.PositiveZ; u = direction.x / az; v = -direction.y / az; }
            else { face = (int)CubemapFace.NegativeZ; u = -direction.x / az; v = -direction.y / az; }
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

    private static Vector3 CubemapTexelDirection(CubemapFace face, float u, float v)
    {
        Vector3 direction;
        switch (face)
        {
            case CubemapFace.PositiveX: direction = new Vector3(1f, -v, -u); break;
            case CubemapFace.NegativeX: direction = new Vector3(-1f, -v, u); break;
            case CubemapFace.PositiveY: direction = new Vector3(u, 1f, v); break;
            case CubemapFace.NegativeY: direction = new Vector3(u, -1f, -v); break;
            case CubemapFace.PositiveZ: direction = new Vector3(u, -v, 1f); break;
            default: direction = new Vector3(-u, -v, -1f); break;
        }
        return direction.normalized;
    }

    private static bool SameObjectReferences<T>(T[] left, T[] right) where T : UnityEngine.Object
    {
        left = left ?? Array.Empty<T>();
        right = right ?? Array.Empty<T>();
        if (left.Length != right.Length)
            return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                return false;
        }
        return true;
    }

    private static bool SameReflectionEntries(
        DungeonTileBakeData.ReflectionProbeBakeEntry[] left,
        DungeonTileBakeData.ReflectionProbeBakeEntry[] right)
    {
        left = left ?? Array.Empty<DungeonTileBakeData.ReflectionProbeBakeEntry>();
        right = right ?? Array.Empty<DungeonTileBakeData.ReflectionProbeBakeEntry>();
        if (left.Length != right.Length)
            return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (!string.Equals(left[i].relativePath, right[i].relativePath, StringComparison.Ordinal) ||
                left[i].bakedTexture != right[i].bakedTexture)
                return false;
        }
        return true;
    }

    private static bool SameLightProbeEntries(
        DungeonTileBakeData.LightProbeBakeEntry[] left,
        DungeonTileBakeData.LightProbeBakeEntry[] right)
    {
        left = left ?? Array.Empty<DungeonTileBakeData.LightProbeBakeEntry>();
        right = right ?? Array.Empty<DungeonTileBakeData.LightProbeBakeEntry>();
        if (left.Length != right.Length)
            return false;
        const float tolerance = 0.000001f;
        for (int i = 0; i < left.Length; i++)
        {
            if ((left[i].localPosition - right[i].localPosition).sqrMagnitude > tolerance * tolerance ||
                (left[i].occlusion - right[i].occlusion).sqrMagnitude > tolerance * tolerance)
                return false;
            for (int coefficient = 0; coefficient < CoefficientCount; coefficient++)
            {
                if ((left[i].GetCoefficient(coefficient) - right[i].GetCoefficient(coefficient)).sqrMagnitude >
                    tolerance * tolerance)
                    return false;
            }
        }
        return true;
    }

    private static bool SameEmissionEntries(
        DungeonTilePowerBakeSet.EmissionMaterialEntry[] left,
        DungeonTilePowerBakeSet.EmissionMaterialEntry[] right)
    {
        left = left ?? Array.Empty<DungeonTilePowerBakeSet.EmissionMaterialEntry>();
        right = right ?? Array.Empty<DungeonTilePowerBakeSet.EmissionMaterialEntry>();
        if (left.Length != right.Length)
            return false;
        for (int i = 0; i < left.Length; i++)
        {
            DungeonTilePowerBakeSet.EmissionMaterialEntry a = left[i];
            DungeonTilePowerBakeSet.EmissionMaterialEntry b = right[i];
            if (!string.Equals(a.relativePath, b.relativePath, StringComparison.Ordinal) ||
                a.rendererBucketIndex != b.rendererBucketIndex ||
                a.materialIndex != b.materialIndex ||
                a.power100Material != b.power100Material ||
                a.power00Material != b.power00Material)
                return false;
        }
        return true;
    }

    private static string[] GetEnabledMarkerPaths<T>(Transform root) where T : Behaviour
    {
        if (root == null)
            return Array.Empty<string>();
        return root.GetComponentsInChildren<T>(true)
            .Where(marker => marker != null && marker.enabled)
            .Select(marker => GetRelativeTransformPath(root, marker.transform))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetRelativeTransformPath(Transform root, Transform target)
    {
        if (root == null || target == null)
            return string.Empty;
        if (target == root)
            return "<root>";

        var names = new Stack<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            names.Push(current.name);
            current = current.parent;
        }
        return current == root ? string.Join("/", names) : target.name;
    }

    private static bool GetSerializedBool(Component component, string propertyName)
    {
        if (component == null)
            return false;
        var serialized = new SerializedObject(component);
        SerializedProperty property = serialized.FindProperty(propertyName);
        return property != null && property.boolValue;
    }

    private static double[,] BuildShRotationMatrix(float rotationDegrees)
    {
        Vector3[] fitDirections = BuildFibonacciDirections(128);
        Vector3[] inverseDirections = RotateDirections(fitDirections, -rotationDegrees);
        var basisAtDirections = new double[fitDirections.Length, CoefficientCount];
        for (int basisIndex = 0; basisIndex < CoefficientCount; basisIndex++)
        {
            SphericalHarmonicsL2 basis = BuildShBasis(basisIndex);
            var colors = new Color[fitDirections.Length];
            basis.Evaluate(fitDirections, colors);
            for (int sample = 0; sample < fitDirections.Length; sample++)
                basisAtDirections[sample, basisIndex] = colors[sample].r;
        }

        var normal = new double[CoefficientCount, CoefficientCount];
        for (int row = 0; row < CoefficientCount; row++)
        {
            for (int column = 0; column < CoefficientCount; column++)
            {
                double sum = 0d;
                for (int sample = 0; sample < fitDirections.Length; sample++)
                    sum += basisAtDirections[sample, row] * basisAtDirections[sample, column];
                normal[row, column] = sum;
            }
            normal[row, row] += 1e-10d;
        }

        var matrix = new double[CoefficientCount, CoefficientCount];
        for (int sourceBasisIndex = 0; sourceBasisIndex < CoefficientCount; sourceBasisIndex++)
        {
            SphericalHarmonicsL2 sourceBasis = BuildShBasis(sourceBasisIndex);
            var desired = new Color[inverseDirections.Length];
            sourceBasis.Evaluate(inverseDirections, desired);
            var rhs = new double[CoefficientCount];
            for (int row = 0; row < CoefficientCount; row++)
            {
                double sum = 0d;
                for (int sample = 0; sample < fitDirections.Length; sample++)
                    sum += basisAtDirections[sample, row] * desired[sample].r;
                rhs[row] = sum;
            }
            double[] solution = SolveLinearSystem(normal, rhs);
            for (int output = 0; output < CoefficientCount; output++)
                matrix[output, sourceBasisIndex] = solution[output];
        }
        return matrix;
    }

    private static SphericalHarmonicsL2 BuildShBasis(int coefficient)
    {
        var basis = new SphericalHarmonicsL2();
        basis[0, coefficient] = 1f;
        return basis;
    }

    private static SphericalHarmonicsL2 RotateSh(SphericalHarmonicsL2 source, double[,] matrix)
    {
        var result = new SphericalHarmonicsL2();
        for (int channel = 0; channel < 3; channel++)
        for (int output = 0; output < CoefficientCount; output++)
        {
            double sum = 0d;
            for (int input = 0; input < CoefficientCount; input++)
                sum += matrix[output, input] * source[channel, input];
            result[channel, output] = (float)sum;
        }
        return result;
    }

    private static Vector3[] BuildFibonacciDirections(int count)
    {
        var directions = new Vector3[count];
        float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int i = 0; i < count; i++)
        {
            float y = 1f - 2f * (i + 0.5f) / count;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float angle = goldenAngle * i;
            directions[i] = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        }
        return directions;
    }

    private static Vector3[] RotateDirections(Vector3[] source, float degrees)
    {
        Quaternion rotation = Quaternion.Euler(0f, degrees, 0f);
        var result = new Vector3[source.Length];
        for (int i = 0; i < source.Length; i++)
            result[i] = rotation * source[i];
        return result;
    }

    private static double[] SolveLinearSystem(double[,] matrix, double[] vector)
    {
        int size = vector.Length;
        var augmented = new double[size, size + 1];
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
                augmented[row, column] = matrix[row, column];
            augmented[row, size] = vector[row];
        }

        for (int pivot = 0; pivot < size; pivot++)
        {
            int bestRow = pivot;
            double best = Math.Abs(augmented[pivot, pivot]);
            for (int row = pivot + 1; row < size; row++)
            {
                double candidate = Math.Abs(augmented[row, pivot]);
                if (candidate > best) { best = candidate; bestRow = row; }
            }
            if (best < 1e-12d)
                throw new InvalidOperationException("SH rotation matrix fit is singular.");
            if (bestRow != pivot)
            {
                for (int column = pivot; column <= size; column++)
                {
                    double temporary = augmented[pivot, column];
                    augmented[pivot, column] = augmented[bestRow, column];
                    augmented[bestRow, column] = temporary;
                }
            }
            double divisor = augmented[pivot, pivot];
            for (int column = pivot; column <= size; column++)
                augmented[pivot, column] /= divisor;
            for (int row = 0; row < size; row++)
            {
                if (row == pivot) continue;
                double factor = augmented[row, pivot];
                for (int column = pivot; column <= size; column++)
                    augmented[row, column] -= factor * augmented[pivot, column];
            }
        }

        var solution = new double[size];
        for (int row = 0; row < size; row++)
            solution[row] = augmented[row, size];
        return solution;
    }
}

public sealed class DungeonTileRotationBakeToolV2Window : EditorWindow
{
    private const string WindowTitle = "Tile Rotation Bake Tool V2";
    private const string SourceEditorPrefsKey = "DungeonTileRotationBakeToolV2.SourcePrefab";

    [SerializeField] private GameObject sourcePrefab;
    [SerializeField] private bool archiveBeforeUpdate = true;
    [SerializeField] private bool showAdvancedSteps;
    private BAKEROTATETOOLV2.RoomAnalysis analysis;
    private Vector2 scroll;
    private string lastResult = string.Empty;

    public static void OpenWindow()
    {
        var window = GetWindow<DungeonTileRotationBakeToolV2Window>(WindowTitle);
        window.minSize = new Vector2(620f, 620f);
        window.Show();
    }

    private void OnEnable()
    {
        string savedPath = EditorPrefs.GetString(SourceEditorPrefsKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(savedPath))
            sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(savedPath);
        if (sourcePrefab == null)
            TryUseSelectedPrefab(false);
        RefreshAnalysis(false);
    }

    private void OnSelectionChange()
    {
        if (TryUseSelectedPrefab(false))
        {
            RefreshAnalysis(false);
            Repaint();
        }
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("Dungeon Tile Rotation Bake Tool V2", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "한 방의 R000 P100/P0만 정식 bake하고 R090/R180/R270의 Direction, SH, Reflection을 파생합니다. " +
            "운영 TileSet/Flow는 변경하지 않으며 결과는 NewPrison/TEST/V2_<Room>에 생성됩니다.",
            MessageType.Info);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("1. Room", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        sourcePrefab = (GameObject)EditorGUILayout.ObjectField(
            "Source Prefab", sourcePrefab, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck())
        {
            SaveSelection();
            RefreshAnalysis(false);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Use Project Selection"))
        {
            if (!TryUseSelectedPrefab(true))
                lastResult = "Select a prefab under NewPrison/Tile_modified or NewPrison/Tiles.";
            RefreshAnalysis(false);
        }
        if (GUILayout.Button("Analyze Room"))
            RefreshAnalysis(true);
        EditorGUILayout.EndHorizontal();

        DrawAnalysis();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("2. Fast Settings Sync (No Bake)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Copies the selected Tile_modified room hierarchy and non-baked settings into its existing TEST Canonical/V2 prefabs. " +
            "BakeData, LightingSet, baked probes, and lightmap assignments are preserved and verified by hash.",
            MessageType.Info);

        bool canFastSync = sourcePrefab != null && analysis != null && analysis.v2OutputExists &&
                           IsTileModifiedSource() && !EditorApplication.isPlaying && !Lightmapping.isRunning;
        using (new EditorGUI.DisabledScope(!canFastSync))
        {
            if (GUILayout.Button("FAST SYNC: Source -> Canonical/V2 (No Light Bake)", GUILayout.Height(36f)))
            {
                lastResult = DungeonV2NonBakeSettingsSync.SyncRoomCli(GetSourcePath());
                RefreshAnalysis(false);
            }
        }

        if (sourcePrefab != null && !IsTileModifiedSource())
        {
            EditorGUILayout.HelpBox(
                "Fast Sync is available for sources under NewPrison/Tile_modified. Legacy Tiles sources still use the normal V2 build steps.",
                MessageType.None);
        }
        if (Lightmapping.isRunning)
        {
            EditorGUILayout.HelpBox(
                "Lightmapping is running. Fast Sync is locked until the bake finishes.",
                MessageType.Warning);
        }
        if (!string.IsNullOrEmpty(lastResult) &&
            lastResult.IndexOf("REBAKE_REQUIRED", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            EditorGUILayout.HelpBox(
                "The sync completed without changing BakeData, but renderer paths changed. Re-bake this room before using the updated lighting output.",
                MessageType.Warning);
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("3. One-Click Build", EditorStyles.boldLabel);
        archiveBeforeUpdate = EditorGUILayout.ToggleLeft("Archive existing TEST output before update", archiveBeforeUpdate);
        EditorGUILayout.HelpBox(
            "선택한 원본 방 하나를 기준으로 R000 P100/P0 실제 bake 2회, 회전 데이터 파생, " +
            "단일 V2 prefab 생성, 최종 검증까지 순서대로 처리합니다.",
            MessageType.Info);
        using (new EditorGUI.DisabledScope(
                   sourcePrefab == null || analysis == null || !analysis.bakeBridgeReady ||
                   EditorApplication.isPlaying || Lightmapping.isRunning))
        {
            if (GUILayout.Button("ONE CLICK: Bake R000 + Build V2 + Validate", GUILayout.Height(42f)))
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    WindowTitle,
                    "선택한 방의 R000 P100/P0를 실제로 2회 bake한 뒤 V2 생성과 검증까지 실행합니다. 계속할까요?",
                    "Build V2",
                    "Cancel");
                if (confirmed)
                {
                    lastResult = BAKEROTATETOOLV2.BuildRoomOneClickCli(GetSourcePath(), archiveBeforeUpdate);
                    RefreshAnalysis(false);
                }
            }
        }

        EditorGUILayout.Space(8f);
        showAdvancedSteps = EditorGUILayout.Foldout(showAdvancedSteps, "Advanced: Run Individual Steps", true);
        if (showAdvancedSteps)
        {
        EditorGUILayout.LabelField("4. Canonical R000", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Bake R000은 기존 BakeRotationTool의 검증된 bake 로직을 사용하지만 회전은 R000 하나로 제한합니다. " +
            "P100/P0 두 번만 bake하며 NewPrison 자동 정규화는 끄므로 Ignore marker를 보존합니다. " +
            "실행 중 Unity가 잠시 응답하지 않는 것처럼 보일 수 있습니다.",
            MessageType.None);
        EditorGUILayout.HelpBox(
            "Reflection Probe: R000 P100/P0 cubemap을 실제 bake하고, R090/R180/R270은 회전 파생합니다. " +
            "Probe가 없는 방은 R000 bake 단계에서 자동 생성 및 bounds fit을 적용합니다.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(
                   sourcePrefab == null || analysis == null || !analysis.bakeBridgeReady ||
                   EditorApplication.isPlaying || Lightmapping.isRunning))
        {
            if (GUILayout.Button("Bake / Refresh TEST R000 (P100 + P0, 2 passes)", GUILayout.Height(30f)))
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    WindowTitle,
                    "This runs two real lightmapping passes (P100 and P0). The current scene will be saved and restored. Continue?",
                    "Bake R000",
                    "Cancel");
                if (confirmed)
                {
                    lastResult = BAKEROTATETOOLV2.BakeCanonicalR000Cli(GetSourcePath(), archiveBeforeUpdate);
                    RefreshAnalysis(false);
                }
            }
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("5. Derive Rotations", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(analysis == null || !analysis.canGenerate || EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Generate / Update Single V2 Prefab", GUILayout.Height(30f)))
            {
                bool proceed = !analysis.canonicalAppearsOlderThanSource || EditorUtility.DisplayDialog(
                    WindowTitle,
                    "The source prefab appears newer than the selected R000 bake. Generate anyway?",
                    "Generate Anyway",
                    "Cancel");
                if (proceed)
                {
                    lastResult = BAKEROTATETOOLV2.BuildOrUpdateRoomCli(GetSourcePath(), archiveBeforeUpdate);
                    RefreshAnalysis(false);
                }
            }
        }

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(analysis == null || !analysis.v2OutputExists))
        {
            if (GUILayout.Button("Validate V2 Output"))
                lastResult = BAKEROTATETOOLV2.ValidateRoomCli(GetSourcePath());
            if (GUILayout.Button("Ping Output Prefab"))
            {
                UnityEngine.Object output = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(analysis.outputPrefabPath);
                EditorGUIUtility.PingObject(output);
                Selection.activeObject = output;
            }
        }
        EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
        EditorGUILayout.TextArea(
            string.IsNullOrWhiteSpace(lastResult) ? "No action has been run in this window yet." : lastResult,
            GUILayout.MinHeight(110f));
        EditorGUILayout.EndScrollView();
    }

    private void DrawAnalysis()
    {
        if (analysis == null)
        {
            EditorGUILayout.HelpBox(
                "Select a source prefab under Tile_modified or Tiles.",
                MessageType.Warning);
            return;
        }

        MessageType type = analysis.status == "PASS"
            ? MessageType.Info
            : analysis.status == "WARNING" ? MessageType.Warning : MessageType.Error;
        EditorGUILayout.HelpBox(
            $"{analysis.status}: {analysis.tileName} | {analysis.sourcePipeline} | Canonical={analysis.canonicalSource}",
            type);

        EditorGUILayout.LabelField("Output", analysis.outputFolder);
        EditorGUILayout.LabelField(
            "DunGen / NavMesh",
            $"Tile={analysis.tileComponentCount}, Doorway={analysis.doorwayCount}, FloorColliders={analysis.authoredFloorColliderCount}");
        EditorGUILayout.LabelField("Lighting", $"Lights={analysis.lightCount}, Probes={analysis.reflectionProbeCount}, Renderers={analysis.rendererCount}");
        EditorGUILayout.LabelField(
            "Control Markers",
            $"Source L/E={analysis.sourceIgnoreLightCount}/{analysis.sourceIgnoreEmissionCount}, " +
            $"R000 L/E={analysis.canonicalIgnoreLightCount}/{analysis.canonicalIgnoreEmissionCount}, " +
            $"Match={analysis.markerPathsMatch}");
        EditorGUILayout.LabelField(
            "Bake Data",
            $"P100={StatusWord(analysis.canonicalP100Ready)}, P0={StatusWord(analysis.canonicalP0Ready)}");
        EditorGUILayout.LabelField("R000 Bake Pipeline", StatusWord(analysis.bakeBridgeReady));
        EditorGUILayout.LabelField(
            "V2 Output",
            analysis.v2OutputExists
                ? analysis.v2OutputStale ? "Exists (STALE)" : "Exists (CURRENT)"
                : "Not generated");

        foreach (string blocker in analysis.blockers ?? Array.Empty<string>())
            EditorGUILayout.HelpBox(blocker, MessageType.Error);
        foreach (string warning in analysis.warnings ?? Array.Empty<string>())
            EditorGUILayout.HelpBox(warning, MessageType.Warning);
    }

    private bool TryUseSelectedPrefab(bool force)
    {
        GameObject selected = Selection.activeObject as GameObject;
        string path = selected != null ? AssetDatabase.GetAssetPath(selected) : string.Empty;
        bool supported = path.StartsWith(
                             "Assets/Prefabs/map_piece/NewPrison/Tile_modified/",
                             StringComparison.OrdinalIgnoreCase) ||
                         path.StartsWith(
                             "Assets/Prefabs/map_piece/NewPrison/Tiles/",
                             StringComparison.OrdinalIgnoreCase);
        if (!supported)
            return false;
        if (!force && selected == sourcePrefab)
            return false;
        sourcePrefab = selected;
        SaveSelection();
        return true;
    }

    private void RefreshAnalysis(bool writeLog)
    {
        string path = GetSourcePath();
        if (string.IsNullOrWhiteSpace(path))
        {
            analysis = null;
            return;
        }

        analysis = BAKEROTATETOOLV2.AnalyzeRoom(path);
        if (writeLog)
        {
            lastResult = JsonUtility.ToJson(analysis, true);
            Debug.Log($"[BAKEROATETOOLV2] Analyze Room\n{lastResult}");
        }
        Repaint();
    }

    private void SaveSelection()
    {
        string path = GetSourcePath();
        if (string.IsNullOrWhiteSpace(path))
            EditorPrefs.DeleteKey(SourceEditorPrefsKey);
        else
            EditorPrefs.SetString(SourceEditorPrefsKey, path);
    }

    private string GetSourcePath()
    {
        return sourcePrefab != null ? AssetDatabase.GetAssetPath(sourcePrefab) : string.Empty;
    }

    private bool IsTileModifiedSource()
    {
        string path = GetSourcePath();
        return path.StartsWith(
            "Assets/Prefabs/map_piece/NewPrison/Tile_modified/",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string StatusWord(bool ready)
    {
        return ready ? "READY" : "MISSING";
    }
}
