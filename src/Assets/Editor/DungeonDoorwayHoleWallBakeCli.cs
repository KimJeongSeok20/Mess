using System;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class DungeonDoorwayHoleWallBakeCli
{
    private const string CafeteriaSource =
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/Cafeteria.prefab";
    private const string AdminSource =
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/AdminstrativeSegregation.prefab";

    public static string BakeCafeteriaAndAdminDoorwayWallsCli()
    {
        var report = new StringBuilder();
        report.AppendLine(BakeOneRoom(CafeteriaSource, "Cafeteria"));
        report.AppendLine();
        report.AppendLine(BakeOneRoom(AdminSource, "AdminstrativeSegregation"));
        string text = report.ToString();
        Debug.Log("[DungeonDoorwayHoleWallBake]\n" + text);
        return text;
    }

    public static string BakeCafeteriaDoorwayWallsCli()
    {
        return BakeOneRoom(CafeteriaSource, "Cafeteria");
    }

    public static string BakeAdminDoorwayWallsCli()
    {
        return BakeOneRoom(AdminSource, "AdminstrativeSegregation");
    }

    private static string BakeOneRoom(string sourcePrefabPath, string label)
    {
        var report = new StringBuilder();
        report.AppendLine("==== " + label + " isolated doorway-wall bake ====");
        string bake = BAKEROTATETOOLV2.BakeCanonicalR000Cli(sourcePrefabPath, true);
        report.AppendLine(bake);
        if (!bake.StartsWith("PASS:", StringComparison.Ordinal))
            return report.ToString();

        string build = BAKEROTATETOOLV2.BuildOrUpdateRoomCli(sourcePrefabPath, true);
        report.AppendLine(build);
        report.AppendLine(ReportHoleWalls(label));
        return report.ToString();
    }

    public static string ReportHoleWalls(string tileName)
    {
        string setPath =
            "Assets/Prefabs/map_piece/NewPrison/test/V2_" + tileName + "/" + tileName + "_LightingSetV2.asset";
        var assets = AssetDatabase.LoadAllAssetsAtPath(setPath);
        var report = new StringBuilder();
        report.AppendLine("hole-wall report " + setPath);
        int ok = 0;
        int missing = 0;
        for (int i = 0; i < assets.Length; i++)
        {
            var data = assets[i] as DungeonTileBakeData;
            if (data == null || data.rendererEntries == null)
                continue;
            for (int e = 0; e < data.rendererEntries.Length; e++)
            {
                var entry = data.rendererEntries[e];
                if (string.IsNullOrEmpty(entry.relativePath) || !IsHoleWallPath(entry.relativePath))
                    continue;
                if (entry.lightmapIndex >= 0)
                    ok++;
                else
                    missing++;
                report.AppendLine(
                    "  " + data.name + " " + entry.relativePath + " lmi=" + entry.lightmapIndex);
            }
        }

        report.AppendLine("ok=" + ok + " missing=" + missing);
        return report.ToString();
    }

    private static bool IsHoleWallPath(string path)
    {
        return path.IndexOf("Door_Placement", StringComparison.Ordinal) >= 0 ||
               path.IndexOf("No_Door_Placement", StringComparison.Ordinal) >= 0 ||
               path.IndexOf("No_DoorPlacement", StringComparison.Ordinal) >= 0;
    }
}
