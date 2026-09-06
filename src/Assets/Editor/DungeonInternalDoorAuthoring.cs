using System;
using System.Collections.Generic;
using System.Linq;
using DunGen;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using DungeonDoor = DunGen.Door;

/// <summary>
/// Authors continuous NavMesh portals for doors that are already part of a room prefab.
/// Runtime traversal never guesses from object names: each accepted passage door receives
/// an explicit MonsterDoorLinkBinding, and its existing NavLinkMesh marker receives a
/// small Walkable area that bridges the threshold.
/// </summary>
public static class DungeonInternalDoorAuthoring
{
    private const string SourcePath =
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/AdminstrativeSegregation.prefab";
    private const string CanonicalPath =
        "Assets/Prefabs/map_piece/NewPrison/test/V2_AdminstrativeSegregation/Canonical/AdminstrativeSegregation_R000.prefab";
    private const string OutputPath =
        "Assets/Prefabs/map_piece/NewPrison/test/V2_AdminstrativeSegregation/AdminstrativeSegregation.prefab";

    private const float MaximumMarkerHorizontalDistance = 0.75f;
    private const float PortalDepth = 1.5f;
    private const float PortalThickness = 0.08f;
    private const float MinimumPortalWidth = 1.2f;

    private static readonly string[] AdministrativePrefabPaths =
    {
        SourcePath,
        CanonicalPath,
        OutputPath,
    };

    private static readonly string[] MonsterPrefabPaths =
    {
        "Assets/Monster/smily/smily-horror-monster_cc_attribution/smily_fixed.prefab",
        "Assets/Clown/Prefab/Clown skin2 combined.prefab",
    };

    private static void AuthorMenu()
    {
        string result = AuthorAdministrativeInternalDoorsCli();
        Debug.Log("[DungeonInternalDoorAuthoring]\n" + result);
        EditorUtility.DisplayDialog("Administrative Internal Doors", result, "OK");
    }

    private static void ValidateMenu()
    {
        string result = ValidateAdministrativeInternalDoorsCli();
        Debug.Log("[DungeonInternalDoorAuthoring]\n" + result);
        EditorUtility.DisplayDialog("Administrative Internal Doors", result, "OK");
    }

    public static string AuthorAdministrativeInternalDoorsCli()
    {
        if (EditorApplication.isPlaying)
            return "ERROR: Exit Play Mode before authoring Administrative internal doors.";
        if (Lightmapping.isRunning)
            return "ERROR: Wait for light baking to finish before authoring Administrative internal doors.";

        var details = new List<string>();
        try
        {
            for (int i = 0; i < AdministrativePrefabPaths.Length; i++)
                AuthorPrefab(AdministrativePrefabPaths[i], details);

            for (int i = 0; i < MonsterPrefabPaths.Length; i++)
                ConfigureMonsterPrefab(MonsterPrefabPaths[i], details);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string validation = ValidateAdministrativeInternalDoorsCli();
            string status = validation.StartsWith("PASS", StringComparison.Ordinal) ? "PASS" : "FAIL";
            return status + ": Administrative internal door authoring\n" +
                   string.Join("\n", details) + "\n" + validation;
        }
        catch (Exception exception)
        {
            return "FAIL: " + exception;
        }
    }

    public static string ValidateAdministrativeInternalDoorsCli()
    {
        var failures = new List<string>();
        var details = new List<string>();

        for (int i = 0; i < AdministrativePrefabPaths.Length; i++)
            ValidatePrefab(AdministrativePrefabPaths[i], failures, details);
        for (int i = 0; i < MonsterPrefabPaths.Length; i++)
            ValidateMonsterPrefab(MonsterPrefabPaths[i], failures, details);

        string status = failures.Count == 0 ? "PASS" : "FAIL";
        string result = status + ": Administrative internal door portals\n" + string.Join("\n", details);
        if (failures.Count > 0)
            result += "\nFailures:\n- " + string.Join("\n- ", failures);
        return result;
    }

    internal static IReadOnlyList<PortalPair> FindPortalPairs(GameObject root, List<string> failures)
    {
        var result = new List<PortalPair>();
        if (root == null)
        {
            failures?.Add("room root is null");
            return result;
        }

        Door[] passageDoors = root.GetComponentsInChildren<Door>(true)
            .Where(IsAuthoredPassageDoorCandidate)
            .ToArray();

        foreach (IGrouping<Transform, Door> group in passageDoors.GroupBy(door => door.transform.parent))
        {
            Transform parent = group.Key;
            List<Door> doors = group.ToList();
            List<Transform> markers = DirectChildrenNamed(parent, "NavLinkMesh");
            if (markers.Count != doors.Count)
            {
                failures?.Add(
                    RelativePath(root.transform, parent) +
                    $" has passageDoors={doors.Count}, NavLinkMesh markers={markers.Count}");
                continue;
            }

            var unassigned = new List<Transform>(markers);
            for (int doorIndex = 0; doorIndex < doors.Count; doorIndex++)
            {
                Door door = doors[doorIndex];
                Collider[] colliders = PassageColliders(door);
                if (colliders.Length == 0)
                {
                    failures?.Add(RelativePath(root.transform, door.transform) + " has no physical passage collider");
                    continue;
                }

                Vector3 passageCenter = AverageColliderCenter(colliders);
                Transform marker = unassigned
                    .OrderBy(candidate => HorizontalSqrDistance(candidate.position, passageCenter))
                    .FirstOrDefault();
                if (marker == null)
                {
                    failures?.Add(RelativePath(root.transform, door.transform) + " has no unassigned NavLinkMesh marker");
                    continue;
                }

                float horizontalDistance = Mathf.Sqrt(HorizontalSqrDistance(marker.position, passageCenter));
                if (horizontalDistance > MaximumMarkerHorizontalDistance)
                {
                    failures?.Add(
                        RelativePath(root.transform, door.transform) +
                        $" nearest NavLinkMesh is {horizontalDistance:F2} m away");
                    continue;
                }

                unassigned.Remove(marker);
                result.Add(new PortalPair(door, marker, colliders, horizontalDistance));
            }
        }

        return result;
    }

    private static void AuthorPrefab(string path, List<string> details)
    {
        RequirePrefab(path);
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var mappingFailures = new List<string>();
            IReadOnlyList<PortalPair> pairs = FindPortalPairs(root, mappingFailures);
            if (mappingFailures.Count > 0)
                throw new InvalidOperationException(path + ": " + string.Join("; ", mappingFailures));
            ValidateAdministrativeBreakdown(path, root, pairs, null, throwOnFailure: true);

            for (int i = 0; i < pairs.Count; i++)
                AuthorPortal(pairs[i]);

            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved)
                throw new InvalidOperationException("Could not save " + path);

            details.Add($"room={path}, internalPortals={pairs.Count}, lockersExcluded=10");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void AuthorPortal(PortalPair pair)
    {
        MonsterDoorLinkBinding binding = pair.Door.GetComponent<MonsterDoorLinkBinding>();
        if (binding == null)
            binding = pair.Door.gameObject.AddComponent<MonsterDoorLinkBinding>();

        DungeonDoor dungeonDoor = pair.Door.GetComponent<DungeonDoor>();
        binding.ConfigureContinuousDoor(pair.Door, dungeonDoor, pair.Colliders);
        EditorUtility.SetDirty(binding);

        NavMeshModifier modifier = pair.Door.GetComponent<NavMeshModifier>();
        if (modifier != null)
        {
            modifier.overrideArea = false;
            EditorUtility.SetDirty(modifier);
        }

        NavMeshObstacle[] obstacles = pair.Door.GetComponentsInChildren<NavMeshObstacle>(true);
        for (int i = 0; i < obstacles.Length; i++)
        {
            obstacles[i].carving = false;
            obstacles[i].enabled = false;
            EditorUtility.SetDirty(obstacles[i]);
        }

        DungeonNavMeshArea portalArea = pair.Marker.GetComponent<DungeonNavMeshArea>();
        if (portalArea == null)
            portalArea = pair.Marker.gameObject.AddComponent<DungeonNavMeshArea>();

        float portalWidth = Mathf.Max(MinimumPortalWidth, CalculatePortalWidth(pair));
        portalArea.ConfigureBox(
            DungeonNavMeshArea.AreaType.Walkable,
            new Vector3(0f, -PortalThickness * 0.5f, 0f),
            new Vector3(portalWidth, PortalThickness, PortalDepth));
        EditorUtility.SetDirty(portalArea);

        var doorObject = new SerializedObject(pair.Door);
        SerializedProperty navLinkMeshObject = doorObject.FindProperty("navLinkMeshObject");
        if (navLinkMeshObject != null)
        {
            navLinkMeshObject.objectReferenceValue = pair.Marker.gameObject;
            doorObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void ConfigureMonsterPrefab(string path, List<string> details)
    {
        RequirePrefab(path);
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            NavMeshAgent agent = root.GetComponentInChildren<NavMeshAgent>(true);
            if (agent == null)
                throw new InvalidOperationException(path + " has no NavMeshAgent");

            DoorAutoOpener opener = agent.GetComponent<DoorAutoOpener>();
            if (opener == null)
                opener = agent.gameObject.AddComponent<DoorAutoOpener>();

            LayerMask mask = DungeonV2NavigationPrefabAuthoring.BuildContinuousDoorDetectionMask();
            float lookAhead = Mathf.Max(2.5f, agent.radius * 4f);
            float probeRadius = Mathf.Max(0.55f, agent.radius * 1.25f);
            opener.ConfigureContinuousDoorDetection(true, lookAhead, probeRadius, 0.1f, mask);
            EditorUtility.SetDirty(opener);

            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved)
                throw new InvalidOperationException("Could not save " + path);

            details.Add($"monster={path}, doorMask={MaskSummary(mask)}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ValidatePrefab(string path, List<string> failures, List<string> details)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            failures.Add("missing Administrative prefab: " + path);
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var mappingFailures = new List<string>();
            IReadOnlyList<PortalPair> pairs = FindPortalPairs(root, mappingFailures);
            for (int i = 0; i < mappingFailures.Count; i++)
                failures.Add(path + ": " + mappingFailures[i]);

            ValidateAdministrativeBreakdown(path, root, pairs, failures, throwOnFailure: false);

            int validBindings = 0;
            int validAreas = 0;
            for (int i = 0; i < pairs.Count; i++)
            {
                PortalPair pair = pairs[i];
                MonsterDoorLinkBinding binding = pair.Door.GetComponent<MonsterDoorLinkBinding>();
                bool bindingValid = binding != null && binding.CanOpen && binding.RegularDoor == pair.Door &&
                                    pair.Colliders.All(collider => binding.PassageColliders.Contains(collider));
                if (bindingValid)
                    validBindings++;
                else
                    failures.Add(path + ": invalid binding on " + RelativePath(root.transform, pair.Door.transform));

                DungeonNavMeshArea area = pair.Marker.GetComponent<DungeonNavMeshArea>();
                bool areaValid = area != null && area.enabled && area.IncludeInRuntimeBake && area.IsWalkable &&
                                 area.Shape == DungeonNavMeshArea.ShapeType.Box &&
                                 area.Size.x >= MinimumPortalWidth && area.Size.z >= PortalDepth;
                if (areaValid)
                    validAreas++;
                else
                    failures.Add(path + ": invalid portal Walkable area on " + RelativePath(root.transform, pair.Marker));

                if (pair.Door.GetComponentsInChildren<NavMeshObstacle>(true).Any(obstacle => obstacle.enabled || obstacle.carving))
                    failures.Add(path + ": enabled/carving door obstacle on " + RelativePath(root.transform, pair.Door.transform));
            }

            Door[] excludedDoors = root.GetComponentsInChildren<Door>(true)
                .Where(door => !pairs.Any(pair => pair.Door == door))
                .ToArray();
            int excludedBindings = excludedDoors.Count(door => door.GetComponent<MonsterDoorLinkBinding>() != null);
            if (excludedBindings > 0)
                failures.Add(path + $": non-passage doors have bindings={excludedBindings}");

            details.Add(
                $"room={path}, doors={root.GetComponentsInChildren<Door>(true).Length}, " +
                $"portals={pairs.Count}, bindings={validBindings}, walkableThresholds={validAreas}, " +
                $"excludedDoors={excludedDoors.Length}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ValidateMonsterPrefab(string path, List<string> failures, List<string> details)
    {
        GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        NavMeshAgent agent = root != null ? root.GetComponentInChildren<NavMeshAgent>(true) : null;
        DoorAutoOpener opener = agent != null ? agent.GetComponent<DoorAutoOpener>() : null;
        if (opener == null)
        {
            failures.Add("missing DoorAutoOpener on monster: " + path);
            return;
        }

        var serialized = new SerializedObject(opener);
        SerializedProperty enabled = serialized.FindProperty("useContinuousDoorDetection");
        SerializedProperty maskProperty = serialized.FindProperty("doorLayerMask");
        int mask = maskProperty != null ? maskProperty.intValue : 0;
        int doorLayer = LayerMask.NameToLayer("Door");
        int interactableLayer = LayerMask.NameToLayer("Interactable");
        bool hasDoor = doorLayer < 0 || (mask & (1 << doorLayer)) != 0;
        bool hasInteractable = interactableLayer < 0 || (mask & (1 << interactableLayer)) != 0;
        if (enabled == null || !enabled.boolValue || !hasDoor || !hasInteractable)
            failures.Add(path + ": continuous door detection must include Door and Interactable layers");

        details.Add($"monster={path}, continuous={enabled != null && enabled.boolValue}, doorMask={MaskSummary(mask)}");
    }

    private static void ValidateAdministrativeBreakdown(
        string path,
        GameObject root,
        IReadOnlyList<PortalPair> pairs,
        List<string> failures,
        bool throwOnFailure)
    {
        int cellDoors = pairs.Count(pair => pair.Door.name.StartsWith("Cell_02_Door", StringComparison.Ordinal));
        int guardDoors = pairs.Count(pair => pair.Door.name == "Door_01" && pair.Door.transform.parent.name == "Guardbooth_Door");
        int fenceDoors = pairs.Count(pair => pair.Door.name == "Fence_Gate" && pair.Door.transform.parent.name == "Fence_Interior_Gate");
        int totalDoors = root.GetComponentsInChildren<Door>(true).Length;
        bool valid = totalDoors == 28 && pairs.Count == 18 && cellDoors == 16 && guardDoors == 1 && fenceDoors == 1;
        if (valid)
            return;

        string message =
            $"{path}: expected doors=28, portals=18 (cell=16, guard=1, fence=1), " +
            $"found doors={totalDoors}, portals={pairs.Count} (cell={cellDoors}, guard={guardDoors}, fence={fenceDoors})";
        if (throwOnFailure)
            throw new InvalidOperationException(message);
        failures?.Add(message);
    }

    private static bool IsAuthoredPassageDoorCandidate(Door door)
    {
        if (door == null || door.GetComponent<NavMeshModifier>() == null)
            return false;

        Transform parent = door.transform.parent;
        if (parent == null || !DirectChildrenNamed(parent, "NavLinkMesh").Any())
            return false;

        for (Transform current = door.transform; current != null; current = current.parent)
        {
            if (current.name == "No_Interactables")
                return false;
            if (current.name == "Interactables")
                return true;
        }

        return false;
    }

    private static Collider[] PassageColliders(Door door)
    {
        return door.GetComponents<Collider>()
            .Where(collider => collider != null && collider.enabled && !collider.isTrigger)
            .ToArray();
    }

    private static List<Transform> DirectChildrenNamed(Transform parent, string name)
    {
        var result = new List<Transform>();
        if (parent == null)
            return result;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, name, StringComparison.Ordinal))
                result.Add(child);
        }
        return result;
    }

    private static Vector3 AverageColliderCenter(Collider[] colliders)
    {
        Vector3 result = Vector3.zero;
        for (int i = 0; i < colliders.Length; i++)
            result += colliders[i].bounds.center;
        return result / Mathf.Max(1, colliders.Length);
    }

    private static float CalculatePortalWidth(PortalPair pair)
    {
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        for (int colliderIndex = 0; colliderIndex < pair.Colliders.Length; colliderIndex++)
        {
            Bounds bounds = pair.Colliders[colliderIndex].bounds;
            Vector3 extents = bounds.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = bounds.center + Vector3.Scale(extents, new Vector3(x, y, z));
                float localX = pair.Marker.InverseTransformPoint(corner).x;
                min = Mathf.Min(min, localX);
                max = Mathf.Max(max, localX);
            }
        }

        return float.IsInfinity(min) || float.IsInfinity(max) ? MinimumPortalWidth : max - min;
    }

    private static float HorizontalSqrDistance(Vector3 left, Vector3 right)
    {
        float x = left.x - right.x;
        float z = left.z - right.z;
        return x * x + z * z;
    }

    private static string MaskSummary(LayerMask mask)
    {
        var names = new List<string>();
        for (int layer = 0; layer < 32; layer++)
        {
            if ((mask.value & (1 << layer)) == 0)
                continue;
            string name = LayerMask.LayerToName(layer);
            names.Add(string.IsNullOrEmpty(name) ? layer.ToString() : name);
        }
        return string.Join("|", names);
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
        return root.name + "/" + string.Join("/", names);
    }

    private static void RequirePrefab(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            throw new InvalidOperationException("Prefab not found: " + path);
    }

    internal readonly struct PortalPair
    {
        public PortalPair(Door door, Transform marker, Collider[] colliders, float markerDistance)
        {
            Door = door;
            Marker = marker;
            Colliders = colliders;
            MarkerDistance = markerDistance;
        }

        public Door Door { get; }
        public Transform Marker { get; }
        public Collider[] Colliders { get; }
        public float MarkerDistance { get; }
    }
}
