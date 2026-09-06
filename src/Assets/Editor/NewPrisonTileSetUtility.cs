using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DunGen;
using UnityEditor;
using UnityEngine;

public static class NewPrisonTileSetUtility
{
    private const string MainTileSetPath = "Assets/Prefabs/map_piece/NewPrison/New_Prison_Tiles.asset";
    private const string StartTileSetPath = "Assets/Prefabs/map_piece/NewPrison/New_Prison_StartTIle.asset";
    private const string TilesRotatedFolder = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated";

    [MenuItem("Tools/Dungeon/TileSets/Sync NewPrison Rotated TileSets")]
    public static void SyncProductionTileSetsToRotatedMenu()
    {
        Debug.Log(SyncProductionTileSetsToRotatedCli());
    }

    [MenuItem("Tools/Dungeon/TileSets/Report NewPrison Production TileSets")]
    public static void ReportProductionTileSetsMenu()
    {
        Debug.Log(ReportProductionTileSets());
    }

    public static string SyncProductionTileSetsToRotatedCli()
    {
        List<GameObject> rotatedPrefabs = LoadRotatedTilePrefabs();
        if (rotatedPrefabs.Count == 0)
            throw new InvalidOperationException($"No DunGen tile prefabs found under {TilesRotatedFolder}");

        List<GameObject> startPrefabs = rotatedPrefabs
            .Where(IsStartRoom)
            .OrderBy(prefab => prefab.name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<GameObject> mainPrefabs = rotatedPrefabs
            .Where(prefab => !IsStartRoom(prefab))
            .OrderBy(prefab => prefab.name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (startPrefabs.Count == 0)
            throw new InvalidOperationException("No StartRoom rotated prefabs found for New_Prison_StartTIle.");

        if (mainPrefabs.Count == 0)
            throw new InvalidOperationException("No non-StartRoom rotated prefabs found for New_Prison_Tiles.");

        UpdateTileSet(MainTileSetPath, mainPrefabs);
        UpdateTileSet(StartTileSetPath, startPrefabs);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return ReportProductionTileSets();
    }

    public static string ReportProductionTileSets()
    {
        List<GameObject> rotatedPrefabs = LoadRotatedTilePrefabs();
        int expectedStartCount = rotatedPrefabs.Count(IsStartRoom);
        int expectedMainCount = rotatedPrefabs.Count - expectedStartCount;

        var sb = new StringBuilder();
        sb.AppendLine("[NewPrisonTileSetUtility] Production TileSets");
        AppendTileSetReport(sb, "main", MainTileSetPath, expectedMainCount, expectStartRooms: false);
        AppendTileSetReport(sb, "start", StartTileSetPath, expectedStartCount, expectStartRooms: true);
        return sb.ToString().TrimEnd();
    }

    private static void UpdateTileSet(string path, List<GameObject> prefabs)
    {
        TileSet tileSet = AssetDatabase.LoadAssetAtPath<TileSet>(path);
        if (tileSet == null)
            throw new InvalidOperationException($"Missing TileSet: {path}");

        tileSet.TileWeights.Weights.Clear();
        tileSet.LockPrefabs.Clear();
        tileSet.AddTiles(prefabs, 1f, 1f);
        EditorUtility.SetDirty(tileSet);
    }

    private static List<GameObject> LoadRotatedTilePrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { TilesRotatedFolder });
        var prefabs = new List<GameObject>();

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrWhiteSpace(path) ||
                path.IndexOf("/BakedData/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponent<Tile>() == null)
                continue;

            prefabs.Add(prefab);
        }

        return prefabs
            .OrderBy(prefab => prefab.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AppendTileSetReport(
        StringBuilder sb,
        string label,
        string path,
        int expectedCount,
        bool expectStartRooms)
    {
        TileSet tileSet = AssetDatabase.LoadAssetAtPath<TileSet>(path);
        if (tileSet == null)
        {
            sb.AppendLine($"{label}.missing={path}");
            return;
        }

        int nullCount = 0;
        int wrongStartRoomPartitionCount = 0;
        int nonRotatedPathCount = 0;

        sb.AppendLine($"{label}.path={path}");
        sb.AppendLine($"{label}.count={tileSet.TileWeights.Weights.Count}");
        sb.AppendLine($"{label}.expectedCount={expectedCount}");

        for (int i = 0; i < tileSet.TileWeights.Weights.Count; i++)
        {
            GameObject prefab = tileSet.TileWeights.Weights[i].Value;
            string prefabPath = prefab != null ? AssetDatabase.GetAssetPath(prefab) : "null";
            bool isStartRoom = IsStartRoom(prefab);
            bool isRotatedPath = prefabPath.StartsWith(TilesRotatedFolder + "/", StringComparison.OrdinalIgnoreCase);

            if (prefab == null)
                nullCount++;
            if (isStartRoom != expectStartRooms)
                wrongStartRoomPartitionCount++;
            if (!isRotatedPath)
                nonRotatedPathCount++;

            sb.AppendLine($"  [{i:00}] {GetPrefabName(prefab)} | startRoom={isStartRoom} | {prefabPath}");
        }

        sb.AppendLine($"{label}.nullCount={nullCount}");
        sb.AppendLine($"{label}.wrongStartRoomPartitionCount={wrongStartRoomPartitionCount}");
        sb.AppendLine($"{label}.nonRotatedPathCount={nonRotatedPathCount}");
    }

    private static bool IsStartRoom(GameObject prefab)
    {
        return prefab != null && prefab.name.StartsWith("StartRoom", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPrefabName(GameObject prefab)
    {
        return prefab != null ? prefab.name : "null";
    }
}
