using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DunGen;
using UnityEditor;
using UnityEngine;

public static class NewPrisonStartTileSetUtility
{
    private const string StartTileSetPath = "Assets/Prefabs/map_piece/NewPrison/New_Prison_StartTIle.asset";
    private const string TilesRotatedFolder = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated";

    public static string EnsureStartTileSetUsesOnlyStartRoom()
    {
        TileSet tileSet = AssetDatabase.LoadAssetAtPath<TileSet>(StartTileSetPath);
        if (tileSet == null)
            throw new InvalidOperationException($"Missing TileSet: {StartTileSetPath}");

        List<GameObject> startRoomPrefabs = LoadStartRoomPrefabs();
        if (startRoomPrefabs.Count == 0)
            throw new InvalidOperationException($"No StartRoom prefabs found under {TilesRotatedFolder}");

        tileSet.TileWeights.Weights.Clear();
        tileSet.LockPrefabs.Clear();
        tileSet.AddTiles(startRoomPrefabs, 1f, 1f);
        EditorUtility.SetDirty(tileSet);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return ReportStartTileSet();
    }

    public static string ReportStartTileSet()
    {
        TileSet tileSet = AssetDatabase.LoadAssetAtPath<TileSet>(StartTileSetPath);
        if (tileSet == null)
            return $"missing={StartTileSetPath}";

        var sb = new StringBuilder();
        sb.AppendLine("[NewPrisonStartTileSetUtility] Start TileSet");
        sb.AppendLine($"path={StartTileSetPath}");
        sb.AppendLine($"count={tileSet.TileWeights.Weights.Count}");

        int nonStartRoomCount = 0;
        for (int i = 0; i < tileSet.TileWeights.Weights.Count; i++)
        {
            GameObject prefab = tileSet.TileWeights.Weights[i].Value;
            string name = prefab != null ? prefab.name : "null";
            string path = prefab != null ? AssetDatabase.GetAssetPath(prefab) : "null";
            bool isStartRoom = prefab != null && prefab.name.StartsWith("StartRoom", StringComparison.OrdinalIgnoreCase);
            if (!isStartRoom)
                nonStartRoomCount++;

            sb.AppendLine($"  [{i}] {name} | startRoom={isStartRoom} | {path}");
        }

        sb.AppendLine($"nonStartRoomCount={nonStartRoomCount}");
        return sb.ToString();
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

            if (prefab.GetComponent<Tile>() == null)
                continue;

            prefabs.Add(prefab);
        }

        return prefabs
            .OrderBy(prefab => prefab.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
