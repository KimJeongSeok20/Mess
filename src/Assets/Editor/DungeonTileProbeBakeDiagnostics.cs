using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class DungeonTileProbeBakeDiagnostics
{
    private const string MenuPath = "Tools/Dungeon/Lighting/Report Selected Tile SH Bake Data";

    [MenuItem(MenuPath)]
    private static void ReportSelected()
    {
        var assets = CollectSelectedBakeData();
        if (assets.Count == 0)
        {
            Debug.LogWarning("[DungeonTileProbeBakeDiagnostics] Select one or more DungeonTileBakeData assets or folders.");
            return;
        }

        for (int i = 0; i < assets.Count; i++)
            Report(assets[i]);
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateReportSelected()
    {
        return Selection.objects != null && Selection.objects.Length > 0;
    }

    private static List<DungeonTileBakeData> CollectSelectedBakeData()
    {
        var result = new List<DungeonTileBakeData>();
        Object[] selected = Selection.objects;
        if (selected == null)
            return result;

        for (int i = 0; i < selected.Length; i++)
        {
            Object obj = selected[i];
            if (obj == null)
                continue;

            if (obj is DungeonTileBakeData data)
            {
                AddUnique(result, data);
                continue;
            }

            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrWhiteSpace(path) || !AssetDatabase.IsValidFolder(path))
                continue;

            string[] guids = AssetDatabase.FindAssets("t:DungeonTileBakeData", new[] { path });
            for (int g = 0; g < guids.Length; g++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[g]);
                var found = AssetDatabase.LoadAssetAtPath<DungeonTileBakeData>(assetPath);
                AddUnique(result, found);
            }
        }

        return result;
    }

    private static void AddUnique(List<DungeonTileBakeData> list, DungeonTileBakeData data)
    {
        if (list == null || data == null || list.Contains(data))
            return;

        list.Add(data);
    }

    private static void Report(DungeonTileBakeData data)
    {
        var entries = data.lightProbeEntries ?? System.Array.Empty<DungeonTileBakeData.LightProbeBakeEntry>();
        float averageLuminance = 0f;

        for (int i = 0; i < entries.Length; i++)
        {
            Vector3 l0 = entries[i].coefficient0;
            averageLuminance += l0.x * 0.2126f + l0.y * 0.7152f + l0.z * 0.0722f;
        }

        if (entries.Length > 0)
            averageLuminance /= entries.Length;

        Debug.Log(
            $"[DungeonTileProbeBakeDiagnostics] {AssetDatabase.GetAssetPath(data)} probes={entries.Length} avgL0={averageLuminance:0.0000}",
            data);
    }
}
