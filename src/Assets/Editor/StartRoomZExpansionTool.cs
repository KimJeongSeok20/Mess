using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class StartRoomZExpansionTool
{
    private const string DefaultPrefabPath = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/StartRoom_R000.prefab";
    private const string MenuExtendDefault = "Tools/Dungeon/StartRoom/Extend +Z By 1 Floor Cell";
    private const string MenuExtendSelected = "Assets/Dungeon/Extend Selected Tile +Z By 1 Floor Cell";

    [MenuItem(MenuExtendDefault)]
    private static void ExtendDefaultStartRoom()
    {
        ExtendPrefabAtPath(DefaultPrefabPath);
    }

    [MenuItem(MenuExtendSelected)]
    private static void ExtendSelectedPrefabs()
    {
        var prefabPaths = GetSelectedPrefabPaths();
        if (prefabPaths.Count == 0)
        {
            Debug.LogWarning("[StartRoomZExpansionTool] Select one or more prefab assets first.");
            return;
        }

        int successCount = 0;
        for (int i = 0; i < prefabPaths.Count; i++)
        {
            if (ExtendPrefabAtPath(prefabPaths[i]))
                successCount++;
        }

        EditorUtility.DisplayDialog(
            "StartRoom +Z Expansion",
            $"Processed {prefabPaths.Count} prefab(s). Succeeded: {successCount}.",
            "OK");
    }

    [MenuItem(MenuExtendSelected, true)]
    private static bool ValidateExtendSelectedPrefabs()
    {
        return GetSelectedPrefabPaths().Count > 0;
    }

    private static bool ExtendPrefabAtPath(string prefabPath)
    {
        if (string.IsNullOrWhiteSpace(prefabPath))
        {
            Debug.LogError("[StartRoomZExpansionTool] Prefab path is empty.");
            return false;
        }

        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefabAsset == null)
        {
            Debug.LogError($"[StartRoomZExpansionTool] Invalid prefab path: {prefabPath}");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Transform floorsRoot = FindDirectChild(root.transform, "Floors");
            Transform wallsRoot = FindDirectChild(root.transform, "Walls");
            Transform propsRoot = FindDirectChild(root.transform, "Props");

            if (floorsRoot == null)
            {
                Debug.LogError($"[StartRoomZExpansionTool] 'Floors' root was not found: {prefabPath}");
                return false;
            }

            var floorTiles = GatherFloorTiles(floorsRoot);
            if (floorTiles.Count == 0)
            {
                Debug.LogError($"[StartRoomZExpansionTool] No floor tile objects found under 'Floors': {prefabPath}");
                return false;
            }

            float maxFloorZ = floorTiles.Max(t => ToRootLocalPosition(root.transform, t).z);
            float cellDepth = ComputeCellDepth(root.transform, floorTiles);
            if (cellDepth <= 0.001f)
                cellDepth = ComputeFallbackCellDepthFromTileBounds(root);

            if (cellDepth <= 0.001f)
            {
                Debug.LogError($"[StartRoomZExpansionTool] Could not determine cell depth: {prefabPath}");
                return false;
            }

            float forwardBandMin = maxFloorZ - cellDepth + 0.05f;
            float boundaryTolerance = Mathf.Max(0.1f, cellDepth * 0.1f);
            Vector3 worldDelta = root.transform.TransformVector(Vector3.forward * cellDepth);
            Vector3 worldHalfDelta = worldDelta * 0.5f;

            int duplicatedFloors = DuplicateForwardBandFloors(root.transform, floorTiles, maxFloorZ, boundaryTolerance, worldDelta);
            int movedWalls = 0;
            int duplicatedWalls = 0;
            int duplicatedProps = 0;

            if (wallsRoot != null)
            {
                ProcessWalls(root.transform, wallsRoot, forwardBandMin, boundaryTolerance, worldDelta, out movedWalls, out duplicatedWalls);
            }

            if (propsRoot != null)
            {
                duplicatedProps = DuplicateForwardBandObjects(root.transform, propsRoot, forwardBandMin, worldDelta);
            }

            ExtendTileReflectionProbes(root.transform, cellDepth, worldHalfDelta);
            RecalculateTileBounds(root);

            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
            if (!success)
            {
                Debug.LogError($"[StartRoomZExpansionTool] Failed to save prefab: {prefabPath}");
                return false;
            }

            Debug.Log(
                $"[StartRoomZExpansionTool] Extended '{prefabPath}' by +Z one cell. " +
                $"CellDepth={cellDepth:0.###}, Floors+={duplicatedFloors}, WallsMoved={movedWalls}, Walls+={duplicatedWalls}, Props+={duplicatedProps}",
                prefabAsset);

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[StartRoomZExpansionTool] Failed: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static List<string> GetSelectedPrefabPaths()
    {
        var results = new List<string>();
        var guids = Selection.assetGUIDs;
        if (guids == null || guids.Length == 0)
            return results;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                results.Add(path);
        }

        return results;
    }

    private static Transform FindDirectChild(Transform root, string childName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }

    private static List<Transform> GatherFloorTiles(Transform floorsRoot)
    {
        var results = new List<Transform>();
        if (floorsRoot == null)
            return results;

        for (int i = 0; i < floorsRoot.childCount; i++)
        {
            Transform child = floorsRoot.GetChild(i);
            if (child == null)
                continue;

            if (!string.Equals(child.name, "Floor", StringComparison.OrdinalIgnoreCase))
                continue;

            if (child.GetComponent<MeshRenderer>() == null)
                continue;

            results.Add(child);
        }

        return results;
    }

    private static float ComputeCellDepth(Transform root, List<Transform> floorTiles)
    {
        var zValues = new List<float>(floorTiles.Count);
        for (int i = 0; i < floorTiles.Count; i++)
            zValues.Add(ToRootLocalPosition(root, floorTiles[i]).z);

        zValues.Sort();
        const float uniqueTolerance = 0.02f;

        var uniqueValues = new List<float>();
        for (int i = 0; i < zValues.Count; i++)
        {
            if (uniqueValues.Count == 0 || Mathf.Abs(zValues[i] - uniqueValues[uniqueValues.Count - 1]) > uniqueTolerance)
                uniqueValues.Add(zValues[i]);
        }

        float minPositiveDelta = float.MaxValue;
        for (int i = 1; i < uniqueValues.Count; i++)
        {
            float delta = uniqueValues[i] - uniqueValues[i - 1];
            if (delta > 0.02f && delta < minPositiveDelta)
                minPositiveDelta = delta;
        }

        return minPositiveDelta == float.MaxValue ? 0f : minPositiveDelta;
    }

    private static float ComputeFallbackCellDepthFromTileBounds(GameObject root)
    {
        var tileComponent = FindTileComponent(root);
        if (tileComponent == null)
            return 0f;

        var serialized = new SerializedObject(tileComponent);
        var placement = serialized.FindProperty("placement");
        var localBounds = placement?.FindPropertyRelative("localBounds");
        var extent = localBounds?.FindPropertyRelative("m_Extent");

        if (extent == null)
            return 0f;

        float depth = extent.vector3Value.z * 0.5f;
        return depth > 0f ? depth : 0f;
    }

    private static int DuplicateForwardBandFloors(
        Transform root,
        List<Transform> floorTiles,
        float maxFloorZ,
        float boundaryTolerance,
        Vector3 worldDelta)
    {
        int duplicateCount = 0;
        for (int i = 0; i < floorTiles.Count; i++)
        {
            Transform floor = floorTiles[i];
            float z = ToRootLocalPosition(root, floor).z;
            if (Mathf.Abs(z - maxFloorZ) > boundaryTolerance)
                continue;

            DuplicateAndTranslate(floor, worldDelta);
            duplicateCount++;
        }

        return duplicateCount;
    }

    private static void ProcessWalls(
        Transform root,
        Transform wallsRoot,
        float forwardBandMin,
        float boundaryTolerance,
        Vector3 worldDelta,
        out int movedCount,
        out int duplicatedCount)
    {
        movedCount = 0;
        duplicatedCount = 0;

        var candidates = GatherRenderableOrColliderNodes(wallsRoot)
            .Where(t => ToRootLocalPosition(root, t).z >= forwardBandMin)
            .ToList();

        if (candidates.Count == 0)
            return;

        float maxZ = candidates.Max(t => ToRootLocalPosition(root, t).z);

        for (int i = 0; i < candidates.Count; i++)
        {
            Transform target = candidates[i];
            float z = ToRootLocalPosition(root, target).z;
            bool isBoundary = Mathf.Abs(z - maxZ) <= boundaryTolerance;
            if (isBoundary)
            {
                target.position += worldDelta;
                movedCount++;
            }
            else
            {
                DuplicateAndTranslate(target, worldDelta);
                duplicatedCount++;
            }
        }
    }

    private static int DuplicateForwardBandObjects(Transform root, Transform sourceRoot, float forwardBandMin, Vector3 worldDelta)
    {
        int duplicatedCount = 0;
        var candidates = GatherRenderableOrColliderNodes(sourceRoot);

        for (int i = 0; i < candidates.Count; i++)
        {
            Transform target = candidates[i];
            float z = ToRootLocalPosition(root, target).z;
            if (z < forwardBandMin)
                continue;

            DuplicateAndTranslate(target, worldDelta);
            duplicatedCount++;
        }

        return duplicatedCount;
    }

    private static List<Transform> GatherRenderableOrColliderNodes(Transform sourceRoot)
    {
        var all = sourceRoot.GetComponentsInChildren<Transform>(true);
        var result = new List<Transform>(all.Length);

        for (int i = 0; i < all.Length; i++)
        {
            Transform current = all[i];
            if (current == sourceRoot)
                continue;

            if (!HasRenderableOrCollider(current.gameObject))
                continue;

            if (HasAncestorWithRenderableOrCollider(current, sourceRoot))
                continue;

            result.Add(current);
        }

        return result;
    }

    private static bool HasRenderableOrCollider(GameObject gameObject)
    {
        return gameObject.GetComponent<Renderer>() != null || gameObject.GetComponent<Collider>() != null;
    }

    private static bool HasAncestorWithRenderableOrCollider(Transform node, Transform stopExclusive)
    {
        Transform cursor = node.parent;
        while (cursor != null && cursor != stopExclusive)
        {
            if (HasRenderableOrCollider(cursor.gameObject))
                return true;

            cursor = cursor.parent;
        }

        return false;
    }

    private static void DuplicateAndTranslate(Transform source, Vector3 worldDelta)
    {
        GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, source.parent);
        clone.name = source.name;
        clone.transform.position = source.position + worldDelta;
        clone.transform.rotation = source.rotation;
        clone.transform.localScale = source.localScale;
    }

    private static Vector3 ToRootLocalPosition(Transform root, Transform node)
    {
        return root.InverseTransformPoint(node.position);
    }

    private static void ExtendTileReflectionProbes(Transform root, float cellDepth, Vector3 worldHalfDelta)
    {
        var probes = root.GetComponentsInChildren<ReflectionProbe>(true);
        if (probes == null || probes.Length == 0)
            return;

        for (int i = 0; i < probes.Length; i++)
        {
            ReflectionProbe probe = probes[i];
            if (probe == null)
                continue;

            if (probe.name.IndexOf("Tile Reflection Probe", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            probe.transform.position += worldHalfDelta;
            Vector3 size = probe.size;
            size.z += cellDepth;
            probe.size = size;
        }
    }

    private static Component FindTileComponent(GameObject root)
    {
        var tileType = FindTypeByName("DunGen.Tile") ?? FindTypeByName("Tile");
        return tileType == null ? null : root.GetComponent(tileType);
    }

    private static void RecalculateTileBounds(GameObject root)
    {
        Component tileComponent = FindTileComponent(root);
        if (tileComponent == null)
            return;

        var method = tileComponent.GetType().GetMethod("RecalculateBounds");
        method?.Invoke(tileComponent, null);
    }

    private static Type FindTypeByName(string fullTypeName)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type resolved = assemblies[i].GetType(fullTypeName, false);
            if (resolved != null)
                return resolved;
        }

        return null;
    }
}
