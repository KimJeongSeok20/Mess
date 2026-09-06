using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public enum DungeonNavMeshAreaSnapFace
{
    NegativeX,
    PositiveX,
    NegativeY,
    PositiveY,
    NegativeZ,
    PositiveZ,
}

public readonly struct DungeonNavMeshAreaSnapResult
{
    public DungeonNavMeshAreaSnapResult(
        Vector3 center,
        Vector3 size,
        string colliderPath,
        float faceMovement)
    {
        Center = center;
        Size = size;
        ColliderPath = colliderPath;
        FaceMovement = faceMovement;
    }

    public Vector3 Center { get; }
    public Vector3 Size { get; }
    public string ColliderPath { get; }
    public float FaceMovement { get; }
}

public static class DungeonNavMeshAreaSnapUtility
{
    private const float SearchDistance = 100f;
    private const float MinimumBoxSize = 0.01f;
    private const float NotWalkableSurfaceOverlap = 0.02f;
    private static readonly float[] FaceSamples = { -0.8f, 0f, 0.8f };

    public static bool TryCalculateSnap(
        DungeonNavMeshArea area,
        DungeonNavMeshAreaSnapFace face,
        out DungeonNavMeshAreaSnapResult result,
        out string failure)
    {
        result = default;
        failure = string.Empty;
        if (area == null)
        {
            failure = "Dungeon NavMesh Area is missing.";
            return false;
        }

        if (area.Shape != DungeonNavMeshArea.ShapeType.Box)
        {
            failure = "Face snapping is available only for the Box shape.";
            return false;
        }

        Transform roomRoot = area.transform.root;
        Collider[] colliders = roomRoot.GetComponentsInChildren<Collider>(true);
        if (colliders.Length == 0)
        {
            failure = "No Collider was found in " + roomRoot.name + ".";
            return false;
        }

        ResolveFace(face, out int axis, out int sign);
        Vector3 center = area.Center;
        Vector3 size = area.Size;
        float oldFace = Component(center, axis) + sign * Component(size, axis) * 0.5f;
        float oppositeFace = Component(center, axis) - sign * Component(size, axis) * 0.5f;
        Vector3 localAxis = Axis(axis) * sign;
        Vector3 worldAxis = area.transform.TransformDirection(localAxis).normalized;

        bool found = false;
        float bestMovement = float.PositiveInfinity;
        float bestFace = oldFace;
        Collider bestCollider = null;
        bool fallbackFound = false;
        float bestFallbackDistance = float.PositiveInfinity;
        float bestFallbackMovement = float.PositiveInfinity;
        float bestFallbackFace = oldFace;
        Collider bestFallbackCollider = null;
        int firstOtherAxis = (axis + 1) % 3;
        int secondOtherAxis = (axis + 2) % 3;

        for (int firstIndex = 0; firstIndex < FaceSamples.Length; firstIndex++)
        {
            for (int secondIndex = 0; secondIndex < FaceSamples.Length; secondIndex++)
            {
                Vector3 localSample = center;
                SetComponent(ref localSample, axis, oldFace);
                SetComponent(
                    ref localSample,
                    firstOtherAxis,
                    Component(center, firstOtherAxis)
                    + FaceSamples[firstIndex] * Component(size, firstOtherAxis) * 0.5f);
                SetComponent(
                    ref localSample,
                    secondOtherAxis,
                    Component(center, secondOtherAxis)
                    + FaceSamples[secondIndex] * Component(size, secondOtherAxis) * 0.5f);

                Vector3 worldSample = area.transform.TransformPoint(localSample);
                for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
                {
                    Collider collider = colliders[colliderIndex];
                    if (!IsSnapTarget(area, collider))
                        continue;

                    EvaluateRayFromSide(
                        area,
                        collider,
                        worldSample - worldAxis * SearchDistance,
                        worldAxis,
                        axis,
                        sign,
                        oldFace,
                        oppositeFace,
                        ref found,
                        ref bestMovement,
                        ref bestFace,
                        ref bestCollider);
                    EvaluateRayFromSide(
                        area,
                        collider,
                        worldSample + worldAxis * SearchDistance,
                        -worldAxis,
                        axis,
                        sign,
                        oldFace,
                        oppositeFace,
                        ref found,
                        ref bestMovement,
                        ref bestFace,
                        ref bestCollider);
                    EvaluateClosestSurface(
                        area,
                        collider,
                        worldSample,
                        worldAxis,
                        axis,
                        sign,
                        oldFace,
                        oppositeFace,
                        ref fallbackFound,
                        ref bestFallbackDistance,
                        ref bestFallbackMovement,
                        ref bestFallbackFace,
                        ref bestFallbackCollider);
                }
            }
        }

        if (!found && fallbackFound)
        {
            found = true;
            bestMovement = bestFallbackMovement;
            bestFace = bestFallbackFace;
            bestCollider = bestFallbackCollider;
        }

        if (!found || bestCollider == null)
        {
            failure = $"No Collider surface was found within {SearchDistance:F0} m of the {face} face.";
            return false;
        }

        if (area.IsNotWalkable)
            bestFace += sign * NotWalkableSurfaceOverlap;
        bestMovement = Mathf.Abs(bestFace - oldFace);

        float snappedSize = sign * (bestFace - oppositeFace);
        Vector3 snappedCenter = center;
        Vector3 snappedSizeVector = size;
        SetComponent(ref snappedCenter, axis, (bestFace + oppositeFace) * 0.5f);
        SetComponent(ref snappedSizeVector, axis, snappedSize);

        result = new DungeonNavMeshAreaSnapResult(
            snappedCenter,
            snappedSizeVector,
            RelativePath(roomRoot, bestCollider.transform),
            bestMovement);
        return true;
    }

    public static string SnapAdministrativePrefabsCli()
    {
        string[] prefabPaths =
        {
            "Assets/Prefabs/map_piece/NewPrison/Tile_modified/AdminstrativeSegregation.prefab",
            "Assets/Prefabs/map_piece/NewPrison/test/V2_AdminstrativeSegregation/AdminstrativeSegregation.prefab",
            "Assets/Prefabs/map_piece/NewPrison/test/V2_AdminstrativeSegregation/Canonical/AdminstrativeSegregation_R000.prefab",
        };

        var report = new StringBuilder();
        bool allPassed = true;
        int snappedAreaCount = 0;
        for (int pathIndex = 0; pathIndex < prefabPaths.Length; pathIndex++)
        {
            string prefabPath = prefabPaths[pathIndex];
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                DungeonNavMeshArea[] areas = root.GetComponentsInChildren<DungeonNavMeshArea>(true);
                int prefabSnapCount = 0;
                for (int areaIndex = 0; areaIndex < areas.Length; areaIndex++)
                {
                    DungeonNavMeshArea area = areas[areaIndex];
                    if (area == null || !area.IsNotWalkable || area.Shape != DungeonNavMeshArea.ShapeType.Box)
                        continue;

                    Vector3 oldCenter = area.Center;
                    Vector3 oldSize = area.Size;
                    if (!TryCalculateSnap(area, DungeonNavMeshAreaSnapFace.NegativeY, out DungeonNavMeshAreaSnapResult result, out string failure))
                    {
                        allPassed = false;
                        report.AppendLine($"FAIL {prefabPath}/{area.name}: {failure}");
                        continue;
                    }

                    area.SetBoxBounds(result.Center, result.Size);
                    EditorUtility.SetDirty(area);
                    prefabSnapCount++;
                    snappedAreaCount++;
                    report.AppendLine(
                        $"SNAP {prefabPath}/{area.name}: "
                        + $"center {Format(oldCenter)} -> {Format(result.Center)}, "
                        + $"size {Format(oldSize)} -> {Format(result.Size)}, "
                        + $"target={result.ColliderPath}, moved={result.FaceMovement:F4}m");
                }

                if (prefabSnapCount == 0)
                {
                    allPassed = false;
                    report.AppendLine("FAIL " + prefabPath + ": no Not Walkable Box was snapped");
                    continue;
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved);
                if (!saved)
                {
                    allPassed = false;
                    report.AppendLine("FAIL " + prefabPath + ": PrefabUtility.SaveAsPrefabAsset returned false");
                }
            }
            catch (Exception exception)
            {
                allPassed = false;
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
        report.Insert(0, $"{(allPassed ? "PASS" : "FAIL")}: snappedAreas={snappedAreaCount}\n");
        return report.ToString();
    }

    private static void EvaluateRayFromSide(
        DungeonNavMeshArea area,
        Collider collider,
        Vector3 origin,
        Vector3 direction,
        int axis,
        int sign,
        float oldFace,
        float oppositeFace,
        ref bool found,
        ref float bestMovement,
        ref float bestFace,
        ref Collider bestCollider)
    {
        if (!collider.Raycast(new Ray(origin, direction), out RaycastHit hit, SearchDistance * 2f))
            return;

        float candidateFace = Component(area.transform.InverseTransformPoint(hit.point), axis);
        float candidateSize = sign * (candidateFace - oppositeFace);
        if (candidateSize < MinimumBoxSize)
            return;

        float movement = Mathf.Abs(candidateFace - oldFace);
        if (movement > SearchDistance || movement >= bestMovement)
            return;

        found = true;
        bestMovement = movement;
        bestFace = candidateFace;
        bestCollider = collider;
    }

    private static void EvaluateClosestSurface(
        DungeonNavMeshArea area,
        Collider collider,
        Vector3 worldSample,
        Vector3 worldAxis,
        int axis,
        int sign,
        float oldFace,
        float oppositeFace,
        ref bool found,
        ref float bestDistance,
        ref float bestMovement,
        ref float bestFace,
        ref Collider bestCollider)
    {
        Vector3 closestPoint = collider.ClosestPoint(worldSample);
        Vector3 worldDelta = closestPoint - worldSample;
        float distance = worldDelta.magnitude;
        if (distance > SearchDistance || distance >= bestDistance)
            return;

        float axialDistance = Mathf.Abs(Vector3.Dot(worldDelta, worldAxis));
        float perpendicularDistance = Mathf.Sqrt(Mathf.Max(0f, distance * distance - axialDistance * axialDistance));
        if (axialDistance < 0.001f || axialDistance < perpendicularDistance * 0.25f)
            return;

        float candidateFace = Component(area.transform.InverseTransformPoint(closestPoint), axis);
        float candidateSize = sign * (candidateFace - oppositeFace);
        if (candidateSize < MinimumBoxSize)
            return;

        found = true;
        bestDistance = distance;
        bestMovement = Mathf.Abs(candidateFace - oldFace);
        bestFace = candidateFace;
        bestCollider = collider;
    }

    private static bool IsSnapTarget(DungeonNavMeshArea area, Collider collider)
    {
        return collider != null
            && collider.enabled
            && !collider.isTrigger
            && collider.gameObject.activeInHierarchy
            && collider.transform != area.transform
            && !collider.transform.IsChildOf(area.transform);
    }

    private static void ResolveFace(DungeonNavMeshAreaSnapFace face, out int axis, out int sign)
    {
        axis = face == DungeonNavMeshAreaSnapFace.NegativeX || face == DungeonNavMeshAreaSnapFace.PositiveX
            ? 0
            : face == DungeonNavMeshAreaSnapFace.NegativeY || face == DungeonNavMeshAreaSnapFace.PositiveY
                ? 1
                : 2;
        sign = face == DungeonNavMeshAreaSnapFace.PositiveX
            || face == DungeonNavMeshAreaSnapFace.PositiveY
            || face == DungeonNavMeshAreaSnapFace.PositiveZ
                ? 1
                : -1;
    }

    private static Vector3 Axis(int axis)
    {
        return axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
    }

    private static float Component(Vector3 value, int axis)
    {
        return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
    }

    private static void SetComponent(ref Vector3 value, int axis, float component)
    {
        if (axis == 0)
            value.x = component;
        else if (axis == 1)
            value.y = component;
        else
            value.z = component;
    }

    private static string RelativePath(Transform root, Transform target)
    {
        var names = new Stack<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            names.Push(current.name);
            current = current.parent;
        }

        if (current == root)
            names.Push(root.name);
        return string.Join("/", names);
    }

    private static string Format(Vector3 value)
    {
        return $"({value.x:F4},{value.y:F4},{value.z:F4})";
    }
}
