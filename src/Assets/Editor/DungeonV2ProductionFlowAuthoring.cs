using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DunGen;
using DunGen.Graph;
using UnityEditor;
using UnityEngine;

public static class DungeonV2ProductionFlowAuthoring
{
    private const string NewPrisonRoot = "Assets/Prefabs/map_piece/NewPrison";
    private const string V2OutputRoot = NewPrisonRoot + "/test";
    private const string SourceFlowPath = NewPrisonRoot + "/New_Prison_Flow.asset";
    private const string SourceArchetypePath = NewPrisonRoot + "/New_Prison_Arch.asset";
    private const string V2StartTileSetPath = NewPrisonRoot + "/New_Prison_V2_StartTiles.asset";
    private const string V2RoomTileSetPath = NewPrisonRoot + "/New_Prison_V2_Rooms.asset";
    private const string V2ArchetypePath = NewPrisonRoot + "/New_Prison_V2_Arch.asset";
    private const string V2FlowPath = NewPrisonRoot + "/New_Prison_V2_Flow.asset";
    private const string MapListPath = "Assets/Scripts/Dungeon 1/DungeonMapList.asset";

    public static void ApplyMenu()
    {
        Debug.Log("[DungeonV2ProductionFlowAuthoring]\n" + ApplyCli());
    }

    public static void ApplyBatchCli()
    {
        string result = ApplyCli();
        Debug.Log("[DungeonV2ProductionFlowAuthoring]\n" + result);
        if (!result.StartsWith("PASS", StringComparison.Ordinal))
            EditorApplication.Exit(1);
    }

    public static void ValidateMenu()
    {
        Debug.Log("[DungeonV2ProductionFlowAuthoring]\n" + ValidateCli());
    }

    public static string ApplyCli()
    {
        try
        {
            ResolveV2RoomPrefabs(out GameObject startRoom, out List<GameObject> regularRooms);
            ValidateTilePrefab(startRoom, expectedDoorwayCount: 3);
            for (int i = 0; i < regularRooms.Count; i++)
                ValidateTilePrefab(regularRooms[i]);

            TileSet startTileSet = LoadOrCreateAsset<TileSet>(V2StartTileSetPath);
            ConfigureTileSet(startTileSet, new[] { startRoom });

            TileSet roomTileSet = LoadOrCreateAsset<TileSet>(V2RoomTileSetPath);
            ConfigureTileSet(roomTileSet, regularRooms);

            DungeonArchetype sourceArchetype = LoadRequired<DungeonArchetype>(SourceArchetypePath);
            DungeonArchetype v2Archetype = LoadOrCreateAsset<DungeonArchetype>(V2ArchetypePath);
            EditorUtility.CopySerialized(sourceArchetype, v2Archetype);
            v2Archetype.name = Path.GetFileNameWithoutExtension(V2ArchetypePath);
            v2Archetype.TileSets = new List<TileSet> { roomTileSet };
            v2Archetype.BranchStartTileSets = new List<TileSet>();
            v2Archetype.BranchCapTileSets = new List<TileSet> { roomTileSet };
            v2Archetype.BranchCapType = BranchCapType.InsteadOf;
            EditorUtility.SetDirty(v2Archetype);

            DungeonFlow sourceFlow = LoadRequired<DungeonFlow>(SourceFlowPath);
            DungeonFlow v2Flow = LoadOrCreateAsset<DungeonFlow>(V2FlowPath);
            EditorUtility.CopySerialized(sourceFlow, v2Flow);
            v2Flow.name = Path.GetFileNameWithoutExtension(V2FlowPath);
            v2Flow.TileInjectionRules.Clear();
            // StartMap applies LengthMultiplier=2, so 3-4 produces a 6-8 room main path at runtime.
            v2Flow.Length = new IntRange(3, 4);
            v2Flow.BranchFromStartNode = true;
            v2Flow.FillAllUnusedStartDoorways = true;
            v2Flow.MinimumStartNodeBranches = 1;
            v2Flow.MatchStartBranchDepthToMainPath = false;
            new DungeonFlowBuilder(v2Flow)
                .AddNode(startTileSet, "Start")
                .AddLine(v2Archetype, 1f)
                .AddNode(roomTileSet, "Goal")
                .Complete();
            EditorUtility.SetDirty(v2Flow);

            DungeonMapList mapList = LoadRequired<DungeonMapList>(MapListPath);
            SerializedObject serializedMapList = new SerializedObject(mapList);
            SerializedProperty entries = serializedMapList.FindProperty("entries");
            if (entries == null)
                throw new InvalidOperationException("DungeonMapList.entries was not found.");

            entries.arraySize = 1;
            SerializedProperty entry = entries.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("flow").objectReferenceValue = v2Flow;
            serializedMapList.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mapList);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return ValidateCli();
        }
        catch (Exception exception)
        {
            return "FAIL: " + exception;
        }
    }

    public static string ValidateCli()
    {
        var failures = new List<string>();
        var details = new List<string>();

        DungeonFlow flow = AssetDatabase.LoadAssetAtPath<DungeonFlow>(V2FlowPath);
        DungeonMapList mapList = AssetDatabase.LoadAssetAtPath<DungeonMapList>(MapListPath);
        if (flow == null)
            failures.Add("missing V2 production flow: " + V2FlowPath);
        if (mapList == null)
            failures.Add("missing DungeonMapList: " + MapListPath);

        if (flow != null)
        {
            try
            {
                ResolveV2RoomPrefabs(out GameObject startRoom, out _);
                ValidateTilePrefab(startRoom, expectedDoorwayCount: 3);
                details.Add($"startRoom={AssetDatabase.GetAssetPath(startRoom)}, doorways=3");
            }
            catch (Exception exception)
            {
                failures.Add("invalid V2 StartRoom: " + exception.Message);
            }

            HashSet<string> referencedPrefabPaths = CollectReferencedPrefabPaths(flow, failures);
            string[] currentV2Paths = FindCurrentV2RoomPrefabPaths();
            HashSet<string> expectedPaths = new HashSet<string>(currentV2Paths, StringComparer.OrdinalIgnoreCase);

            foreach (string path in referencedPrefabPaths)
            {
                if (!expectedPaths.Contains(path))
                    failures.Add("non-V2 room is reachable from the production flow: " + path);
            }

            foreach (string path in expectedPaths)
            {
                if (!referencedPrefabPaths.Contains(path))
                    failures.Add("current V2 room is not referenced by the production flow: " + path);
            }

            if (flow.TileInjectionRules != null && flow.TileInjectionRules.Count > 0)
                failures.Add("V2 production flow must not inject an unverified tile set.");

            if (!flow.BranchFromStartNode)
                failures.Add("V2 production flow must branch from unused StartRoom doorways.");
            if (!flow.FillAllUnusedStartDoorways)
                failures.Add("V2 production flow must fill every unused StartRoom doorway.");
            if (flow.MinimumStartNodeBranches != 1)
                failures.Add("V2 production flow must require one successful StartRoom branch.");
            if (flow.MatchStartBranchDepthToMainPath)
                failures.Add("V2 production flow must use the archetype branch depth instead of duplicating the scaled main-path length.");
            if (flow.Length.Min != 3 || flow.Length.Max != 4)
                failures.Add("V2 production flow length must be 3-4 because StartMap applies LengthMultiplier=2.");

            details.Add(
                $"flow={V2FlowPath}, length={flow.Length.Min}-{flow.Length.Max}, " +
                $"branchFromStart={flow.BranchFromStartNode}, fillAllStartDoors={flow.FillAllUnusedStartDoorways}, " +
                $"minimumStartBranches={flow.MinimumStartNodeBranches}, matchStartDepth={flow.MatchStartBranchDepthToMainPath}, " +
                $"nodes={flow.Nodes.Count}, lines={flow.Lines.Count}, reachableV2Rooms={referencedPrefabPaths.Count}");
            foreach (string path in referencedPrefabPaths.OrderBy(path => path, StringComparer.Ordinal))
                details.Add("room=" + path);
        }

        if (mapList != null)
        {
            bool normalGenerationUsesOnlyV2Flow = mapList.Count == 1 &&
                                                  mapList.Entries[0] != null &&
                                                  mapList.Entries[0].flow == flow;
            details.Add($"mapList={MapListPath}, entries={mapList.Count}, v2Only={normalGenerationUsesOnlyV2Flow}");
            if (!normalGenerationUsesOnlyV2Flow)
                failures.Add("normal dungeon generation is not restricted to the V2 production flow.");
        }

        string status = failures.Count == 0 ? "PASS" : "FAIL";
        string result = status + ": normal dungeon generation uses V2 room prefabs only\n" +
                        string.Join("\n", details);
        if (failures.Count > 0)
            result += "\nFailures:\n- " + string.Join("\n- ", failures);
        return result;
    }

    private static void ResolveV2RoomPrefabs(out GameObject startRoom, out List<GameObject> regularRooms)
    {
        string[] paths = FindCurrentV2RoomPrefabPaths();
        string[] startPaths = paths
            .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), "StartRoom", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (startPaths.Length != 1)
            throw new InvalidOperationException($"Expected exactly one V2 StartRoom, found {startPaths.Length}.");

        startRoom = LoadRequired<GameObject>(startPaths[0]);
        regularRooms = paths
            .Where(path => !string.Equals(path, startPaths[0], StringComparison.OrdinalIgnoreCase))
            .Select(LoadRequired<GameObject>)
            .ToList();
        if (regularRooms.Count == 0)
            throw new InvalidOperationException("At least one non-start V2 room is required.");
    }

    private static string[] FindCurrentV2RoomPrefabPaths()
    {
        if (!AssetDatabase.IsValidFolder(V2OutputRoot))
            return Array.Empty<string>();

        return AssetDatabase.FindAssets("t:Prefab", new[] { V2OutputRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(IsCurrentV2RoomPrefabPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsCurrentV2RoomPrefabPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string normalized = path.Replace('\\', '/');
        string directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(directory))
            return false;

        string folderName = Path.GetFileName(directory);
        return folderName.StartsWith("V2_", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   Path.GetFileNameWithoutExtension(normalized),
                   folderName.Substring(3),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void ConfigureTileSet(TileSet tileSet, IEnumerable<GameObject> prefabs)
    {
        if (tileSet.TileWeights == null)
            tileSet.TileWeights = new GameObjectChanceTable();

        tileSet.TileWeights.Weights.Clear();
        tileSet.LockPrefabs.Clear();
        foreach (GameObject prefab in prefabs)
            tileSet.AddTile(prefab, 1f, 1f);

        EditorUtility.SetDirty(tileSet);
    }

    private static void ValidateTilePrefab(GameObject prefab, int? expectedDoorwayCount = null)
    {
        if (prefab == null)
            throw new InvalidOperationException("V2 tile prefab is null.");

        Tile[] tiles = prefab.GetComponentsInChildren<Tile>(true);
        Doorway[] doorways = prefab.GetComponentsInChildren<Doorway>(true);
        if (tiles.Length != 1)
            throw new InvalidOperationException($"{AssetDatabase.GetAssetPath(prefab)} must contain exactly one DunGen.Tile.");
        if (doorways.Length == 0)
            throw new InvalidOperationException($"{AssetDatabase.GetAssetPath(prefab)} has no DunGen.Doorway.");
        if (expectedDoorwayCount.HasValue && doorways.Length != expectedDoorwayCount.Value)
            throw new InvalidOperationException(
                $"{AssetDatabase.GetAssetPath(prefab)} must contain exactly {expectedDoorwayCount.Value} DunGen.Doorways, found {doorways.Length}.");
        if (prefab.GetComponentInChildren<DungeonTileRotationSelectorV2>(true) == null)
            throw new InvalidOperationException($"{AssetDatabase.GetAssetPath(prefab)} is not a V2 rotation prefab.");
    }

    private static HashSet<string> CollectReferencedPrefabPaths(DungeonFlow flow, List<string> failures)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tileSets = new HashSet<TileSet>();
        var archetypes = new HashSet<DungeonArchetype>();

        foreach (GraphNode node in flow.Nodes ?? new List<GraphNode>())
        {
            if (node == null)
                continue;
            foreach (TileSet tileSet in node.TileSets ?? new List<TileSet>())
                AddTileSet(tileSet, tileSets, failures);
        }

        foreach (GraphLine line in flow.Lines ?? new List<GraphLine>())
        {
            if (line == null)
                continue;
            foreach (DungeonArchetype archetype in line.DungeonArchetypes ?? new List<DungeonArchetype>())
            {
                if (archetype == null || !archetypes.Add(archetype))
                    continue;
                foreach (TileSet tileSet in archetype.TileSets ?? new List<TileSet>())
                    AddTileSet(tileSet, tileSets, failures);
                foreach (TileSet tileSet in archetype.BranchStartTileSets ?? new List<TileSet>())
                    AddTileSet(tileSet, tileSets, failures);
                foreach (TileSet tileSet in archetype.BranchCapTileSets ?? new List<TileSet>())
                    AddTileSet(tileSet, tileSets, failures);
            }
        }

        foreach (TileInjectionRule injection in flow.TileInjectionRules ?? new List<TileInjectionRule>())
        {
            if (injection != null)
                AddTileSet(injection.TileSet, tileSets, failures);
        }

        foreach (TileSet tileSet in tileSets)
        {
            if (tileSet.TileWeights == null)
                continue;
            foreach (GameObjectChance chance in tileSet.TileWeights.Weights)
            {
                if (chance == null || chance.Value == null)
                {
                    failures.Add("null tile entry in " + AssetDatabase.GetAssetPath(tileSet));
                    continue;
                }

                paths.Add(AssetDatabase.GetAssetPath(chance.Value).Replace('\\', '/'));
            }
        }

        return paths;
    }

    private static void AddTileSet(TileSet tileSet, HashSet<TileSet> tileSets, List<string> failures)
    {
        if (tileSet == null)
        {
            failures.Add("V2 production flow contains a null TileSet reference.");
            return;
        }

        tileSets.Add(tileSet);
    }

    private static T LoadOrCreateAsset<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null)
            return asset;

        asset = ScriptableObject.CreateInstance<T>();
        asset.name = Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static T LoadRequired<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
            throw new InvalidOperationException("Required asset was not found: " + path);
        return asset;
    }
}
