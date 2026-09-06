using System;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class DungeonNavMeshFootprintBlockerAuthoring
{
    [MenuItem("GameObject/Dungeon NavMesh/Add Footprint Blocker", false, 22)]
    private static void AddToSelection()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null || selected.GetComponent<DungeonNavMeshFootprintBlocker>() != null)
            return;

        Undo.AddComponent<DungeonNavMeshFootprintBlocker>(selected);
    }

    public static string ApplyStartRoomBenchesCli()
    {
        string[] prefabPaths =
        {
            "Assets/Prefabs/map_piece/NewPrison/Tile_modified/StartRoom.prefab",
            "Assets/Prefabs/map_piece/NewPrison/test/V2_StartRoom/StartRoom.prefab",
            "Assets/Prefabs/map_piece/NewPrison/test/V2_StartRoom/Canonical/StartRoom_R000.prefab",
        };

        var report = new StringBuilder();
        bool passed = true;
        int added = 0;
        int existing = 0;
        for (int pathIndex = 0; pathIndex < prefabPaths.Length; pathIndex++)
        {
            string prefabPath = prefabPaths[pathIndex];
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                int matchedBench01 = 0;
                int matchedBench04 = 0;
                Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
                for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
                {
                    Transform candidate = transforms[transformIndex];
                    if (!IsBenchRoot(candidate))
                        continue;

                    if (string.Equals(candidate.name, "Bench_01", StringComparison.OrdinalIgnoreCase))
                        matchedBench01++;
                    else
                        matchedBench04++;
                    DungeonNavMeshFootprintBlocker blocker =
                        candidate.GetComponent<DungeonNavMeshFootprintBlocker>();
                    if (blocker == null)
                    {
                        blocker = candidate.gameObject.AddComponent<DungeonNavMeshFootprintBlocker>();
                        EditorUtility.SetDirty(blocker);
                        added++;
                    }
                    else
                    {
                        existing++;
                    }

                    report.AppendLine($"MARK {prefabPath}/{RelativePath(root.transform, candidate)}");
                }

                if (matchedBench01 != 4 || matchedBench04 != 1)
                {
                    passed = false;
                    report.AppendLine(
                        $"FAIL {prefabPath}: expected Bench_01=4 and Bench_04=1, " +
                        $"found {matchedBench01}/{matchedBench04}");
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved);
                if (!saved)
                {
                    passed = false;
                    report.AppendLine("FAIL " + prefabPath + ": save failed");
                }
            }
            catch (Exception exception)
            {
                passed = false;
                report.AppendLine("FAIL " + prefabPath + ": " + exception);
            }
            finally
            {
                if (root != null)
                    PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        report.Insert(0, $"{(passed ? "PASS" : "FAIL")}: added={added}, existing={existing}\n");
        return report.ToString();
    }

    private static bool IsBenchRoot(Transform candidate)
    {
        if (candidate == null)
            return false;

        return string.Equals(candidate.name, "Bench_01", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.name, "Bench_04", StringComparison.OrdinalIgnoreCase);
    }

    private static string RelativePath(Transform root, Transform target)
    {
        if (root == target)
            return root.name;

        string path = target.name;
        Transform current = target.parent;
        while (current != null && current != root)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return root.name + "/" + path;
    }
}
