using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class DungeonDoorwayBoundaryValidator
{
    private const string ValidateDefaultMenuPath = "Tools/Dungeon/Doorway/Validate NewPrison Tile Doorways";
    private const string ValidateSelectedMenuPath = "Assets/Dungeon/Validate Doorway Boundary";
    private const string DefaultTileFolder = "Assets/Prefabs/map_piece/NewPrison/Tiles";
    private const float BoundaryTolerance = 0.01f;
    private const float EdgeThreshold = 0.1f;

    [MenuItem(ValidateDefaultMenuPath)]
    private static void ValidateNewPrisonTiles()
    {
        ValidateFolder(DefaultTileFolder);
    }

    [MenuItem(ValidateSelectedMenuPath)]
    private static void ValidateSelectedPrefabs()
    {
        var prefabPaths = GetSelectedPrefabPaths();
        if (prefabPaths.Count == 0)
        {
            Debug.LogWarning("[DoorwayBoundaryValidator] Select one or more prefab assets first.");
            return;
        }

        ValidatePrefabPaths(prefabPaths, "selection");
    }

    [MenuItem(ValidateSelectedMenuPath, true)]
    private static bool ValidateSelectedPrefabsMenu()
    {
        var guids = Selection.assetGUIDs;
        if (guids == null || guids.Length == 0)
            return false;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void ValidateFolder(string folderPath)
    {
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogError($"[DoorwayBoundaryValidator] Invalid folder: {folderPath}");
            return;
        }

        var prefabPaths = new List<string>();
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
        for (int i = 0; i < guids.Length; i++)
            prefabPaths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));

        if (prefabPaths.Count == 0)
        {
            Debug.LogWarning($"[DoorwayBoundaryValidator] No prefabs found in: {folderPath}");
            return;
        }

        ValidatePrefabPaths(prefabPaths, folderPath);
    }

    private static List<string> GetSelectedPrefabPaths()
    {
        var results = new List<string>();
        var guids = Selection.assetGUIDs;
        if (guids == null)
            return results;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                continue;

            results.Add(path);
        }

        return results;
    }

    private static void ValidatePrefabPaths(List<string> prefabPaths, string sourceLabel)
    {
        Type tileType = FindTypeByName("DunGen.Tile") ?? FindTypeByName("Tile");
        Type doorwayType = FindTypeByName("DunGen.Doorway") ?? FindTypeByName("Doorway");

        if (tileType == null || doorwayType == null)
        {
            Debug.LogError("[DoorwayBoundaryValidator] Could not resolve DunGen Tile/Doorway types.");
            return;
        }

        MethodInfo recalculateBoundsMethod = tileType.GetMethod("RecalculateBounds", BindingFlags.Instance | BindingFlags.Public);

        int tileCount = 0;
        int doorwayCount = 0;
        int issueCount = 0;
        int noTileCount = 0;
        int noDoorwayCount = 0;

        for (int i = 0; i < prefabPaths.Count; i++)
        {
            string prefabPath = prefabPaths[i];
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
                continue;

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var tileComponent = root.GetComponent(tileType);
                if (tileComponent == null)
                {
                    noTileCount++;
                    continue;
                }

                tileCount++;

                if (recalculateBoundsMethod != null)
                    recalculateBoundsMethod.Invoke(tileComponent, null);

                var doorwayComponents = root.GetComponentsInChildren(doorwayType, true);
                if (doorwayComponents == null || doorwayComponents.Length == 0)
                {
                    noDoorwayCount++;
                    Debug.LogWarning($"[DoorwayBoundaryValidator] Tile has no doorways: {prefabPath}", prefabAsset);
                    continue;
                }

                for (int doorwayIndex = 0; doorwayIndex < doorwayComponents.Length; doorwayIndex++)
                {
                    var doorwayComponent = doorwayComponents[doorwayIndex];
                    doorwayCount++;

                    if (TryBuildIssueMessage(tileComponent, doorwayComponent, out string issueMessage))
                    {
                        issueCount++;
                        string hierarchyPath = GetHierarchyPath(tileComponent.transform, doorwayComponent.transform);
                        Debug.LogError($"[DoorwayBoundaryValidator] {prefabPath} :: {hierarchyPath} -> {issueMessage}", prefabAsset);
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        string summary =
            $"[DoorwayBoundaryValidator] Validation complete ({sourceLabel}). " +
            $"Tiles={tileCount}, Doorways={doorwayCount}, Issues={issueCount}, " +
            $"NoTile={noTileCount}, NoDoorway={noDoorwayCount}, Tolerance={BoundaryTolerance:0.###}.";

        if (issueCount > 0)
            Debug.LogError(summary);
        else
            Debug.Log(summary);
    }

    private static bool TryBuildIssueMessage(Component tileComponent, Component doorwayComponent, out string message)
    {
        bool hasBounds = TryGetTileLocalBounds(tileComponent, out var localTileBounds);
        bool isAxisAligned = IsAxisAligned(doorwayComponent.transform.forward);

        float edgeDistance = float.PositiveInfinity;
        bool isEdgePositioned = false;
        bool isWithinTolerance = false;

        if (hasBounds)
        {
            Vector3 projected = ProjectPositionToTileBounds(tileComponent.transform, doorwayComponent.transform, localTileBounds);
            edgeDistance = Vector3.Distance(doorwayComponent.transform.position, projected);
            isEdgePositioned = edgeDistance <= EdgeThreshold;
            isWithinTolerance = edgeDistance <= BoundaryTolerance;
        }

        if (isAxisAligned && isEdgePositioned && isWithinTolerance)
        {
            message = string.Empty;
            return false;
        }

        var parts = new List<string>(4);
        if (!hasBounds)
            parts.Add("tile local bounds are invalid");
        if (!isAxisAligned)
            parts.Add("forward is not axis-aligned");
        if (!isEdgePositioned)
            parts.Add("doorway is not on tile bounds edge");
        if (!isWithinTolerance)
            parts.Add($"distance to projected edge is {edgeDistance:0.###} (>{BoundaryTolerance:0.###})");

        message = string.Join(", ", parts);
        return true;
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

    private static Vector3 ProjectPositionToTileBounds(Transform tileTransform, Transform doorwayTransform, Bounds localTileBounds)
    {
        Bounds worldBounds = TransformBounds(tileTransform, localTileBounds);

        Vector3 correctedForward = GetCardinalDirection(doorwayTransform.forward, out float magnitude);
        Vector3 offsetFromBoundsCenter = doorwayTransform.position - worldBounds.center;

        float currentForwardDistance = Vector3.Dot(correctedForward, offsetFromBoundsCenter);
        float extentForwardDistance = Vector3.Dot(magnitude < 0f ? -correctedForward : correctedForward, worldBounds.extents);
        float forwardCorrectionDistance = extentForwardDistance - currentForwardDistance;

        Vector3 targetPosition = doorwayTransform.position + correctedForward * forwardCorrectionDistance;
        return ClampVector(targetPosition, worldBounds.min, worldBounds.max);
    }

    private static Bounds TransformBounds(Transform transform, Bounds localBounds)
    {
        Vector3 transformedCenter = transform.TransformPoint(localBounds.center);
        Vector3 transformedSize = transform.rotation * localBounds.size;

        transformedSize.x = Mathf.Abs(transformedSize.x);
        transformedSize.y = Mathf.Abs(transformedSize.y);
        transformedSize.z = Mathf.Abs(transformedSize.z);

        return new Bounds(transformedCenter, transformedSize);
    }

    private static bool IsAxisAligned(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0f)
            return false;

        Vector3 normalized = direction.normalized;
        float dotX = Mathf.Abs(Vector3.Dot(normalized, Vector3.right));
        float dotY = Mathf.Abs(Vector3.Dot(normalized, Vector3.up));
        float dotZ = Mathf.Abs(Vector3.Dot(normalized, Vector3.forward));

        const float epsilon = 0.01f;

        if (dotX > 1f - epsilon && dotY < epsilon && dotZ < epsilon)
            return true;
        if (dotY > 1f - epsilon && dotX < epsilon && dotZ < epsilon)
            return true;
        if (dotZ > 1f - epsilon && dotX < epsilon && dotY < epsilon)
            return true;

        return false;
    }

    private static Vector3 GetCardinalDirection(Vector3 direction, out float magnitude)
    {
        float absX = Mathf.Abs(direction.x);
        float absY = Mathf.Abs(direction.y);
        float absZ = Mathf.Abs(direction.z);

        if (absX >= absY && absX >= absZ)
        {
            float sign = direction.x >= 0f ? 1f : -1f;
            magnitude = sign;
            return new Vector3(sign, 0f, 0f);
        }

        if (absY >= absX && absY >= absZ)
        {
            float sign = direction.y >= 0f ? 1f : -1f;
            magnitude = sign;
            return new Vector3(0f, sign, 0f);
        }

        float zSign = direction.z >= 0f ? 1f : -1f;
        magnitude = zSign;
        return new Vector3(0f, 0f, zSign);
    }

    private static Vector3 ClampVector(Vector3 input, Vector3 min, Vector3 max)
    {
        return new Vector3(
            Mathf.Clamp(input.x, min.x, max.x),
            Mathf.Clamp(input.y, min.y, max.y),
            Mathf.Clamp(input.z, min.z, max.z));
    }

    private static Type FindTypeByName(string fullTypeName)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            var type = assemblies[i].GetType(fullTypeName, false);
            if (type != null)
                return type;
        }

        return null;
    }

    private static string GetHierarchyPath(Transform root, Transform target)
    {
        if (root == null || target == null)
            return "<unknown>";
        if (root == target)
            return root.name;

        var names = new Stack<string>();
        var current = target;

        while (current != null)
        {
            names.Push(current.name);
            if (current == root)
                break;

            current = current.parent;
        }

        return string.Join("/", names);
    }
}
