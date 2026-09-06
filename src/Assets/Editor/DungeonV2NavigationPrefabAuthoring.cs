using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DunGen;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using DungeonDoor = DunGen.Door;

public static class DungeonV2NavigationPrefabAuthoring
{
    private const string DoorRoot = "Assets/Prefabs/map_piece/NewPrison/SomePicees";
    private const string V2OutputRoot = "Assets/Prefabs/map_piece/NewPrison/TEST";
    private const string V2ModifiedSourceRoot = "Assets/Prefabs/map_piece/NewPrison/Tile_modified";
    private const string V2LegacySourceRoot = "Assets/Prefabs/map_piece/NewPrison/Tiles";

    private static readonly string[] DoorPrefabPaths =
    {
        DoorRoot + "/Door_LG_A_DoorPlacement.prefab",
        DoorRoot + "/Door_LG_B_DoorPlacement.prefab",
        DoorRoot + "/Door_SM_A_Door_Placement.prefab",
        DoorRoot + "/Door_SM_B_DoorPlacement.prefab",
    };

    private static readonly string[] MonsterPrefabPaths =
    {
        "Assets/Monster/smily/smily-horror-monster_cc_attribution/smily_fixed.prefab",
        "Assets/Clown/Prefab/Clown skin2 combined.prefab",
    };

    public static void ApplyMenu()
    {
        Debug.Log(ApplyCli());
    }

    public static void ApplyBatchCli()
    {
        string result = ApplyCli();
        Debug.Log("[DungeonV2NavigationPrefabAuthoring]\n" + result);
        if (!result.StartsWith("PASS", StringComparison.Ordinal))
            EditorApplication.Exit(1);
    }

    public static void ValidateMenu()
    {
        Debug.Log(ValidateCli());
    }

    public static string ApplyCli()
    {
        var messages = new List<string>();
        try
        {
            string[] v2RoomPrefabPaths = FindCurrentV2RoomPrefabPaths();
            string[] v2SourcePrefabPaths = FindCurrentV2SourcePrefabPaths(v2RoomPrefabPaths);
            for (int i = 0; i < v2SourcePrefabPaths.Length; i++)
                AuthorV2SourceAreaPrefab(v2SourcePrefabPaths[i], messages);
            for (int i = 0; i < v2RoomPrefabPaths.Length; i++)
                AuthorV2RoomPrefab(v2RoomPrefabPaths[i], messages);
            for (int i = 0; i < DoorPrefabPaths.Length; i++)
                AuthorDoorPrefab(DoorPrefabPaths[i], messages);
            for (int i = 0; i < MonsterPrefabPaths.Length; i++)
                AuthorMonsterPrefab(MonsterPrefabPaths[i], messages);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string validation = ValidateCli();
            string status = validation.StartsWith("PASS", StringComparison.Ordinal) ? "PASS" : "FAIL";
            return status + "\n" + string.Join("\n", messages) + "\n" + validation;
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

        string[] v2RoomPrefabPaths = FindCurrentV2RoomPrefabPaths();
        string[] v2SourcePrefabPaths = FindCurrentV2SourcePrefabPaths(v2RoomPrefabPaths);
        for (int i = 0; i < v2SourcePrefabPaths.Length; i++)
            ValidateV2SourceAreaPrefab(v2SourcePrefabPaths[i], failures, details);
        for (int i = 0; i < v2RoomPrefabPaths.Length; i++)
            ValidateV2RoomPrefab(v2RoomPrefabPaths[i], failures, details);
        for (int i = 0; i < DoorPrefabPaths.Length; i++)
            ValidateDoorPrefab(DoorPrefabPaths[i], failures, details);
        for (int i = 0; i < MonsterPrefabPaths.Length; i++)
            ValidateMonsterPrefab(MonsterPrefabPaths[i], failures, details);

        string status = failures.Count == 0 ? "PASS" : "FAIL";
        string result = $"{status}: V2 navigation prefab baseline\n" + string.Join("\n", details);
        if (failures.Count > 0)
            result += "\nFailures:\n- " + string.Join("\n- ", failures);
        return result;
    }

    /// <summary>
    /// Restores the V2 room-only navigation invariants after an authored source prefab
    /// has been synchronized into a canonical or runtime V2 prefab.
    /// </summary>
    public static void NormalizeRoomNavigationRoot(GameObject root)
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));

        NavMeshSurface[] surfaces = root.GetComponentsInChildren<NavMeshSurface>(true);
        for (int i = 0; i < surfaces.Length; i++)
        {
            NavMeshSurface surface = surfaces[i];
            if (surface == null)
                continue;

            surface.RemoveData();
            surface.navMeshData = null;
            surface.enabled = false;
        }

        NavMeshLink[] links = root.GetComponentsInChildren<NavMeshLink>(true);
        for (int i = 0; i < links.Length; i++)
        {
            NavMeshLink link = links[i];
            if (link != null && link.GetComponentInParent<DunGen.Doorway>() != null)
                link.enabled = false;
        }

        NavMeshObstacle[] obstacles = root.GetComponentsInChildren<NavMeshObstacle>(true);
        for (int i = 0; i < obstacles.Length; i++)
        {
            NavMeshObstacle obstacle = obstacles[i];
            if (obstacle == null || obstacle.GetComponentInParent<Door>() == null)
                continue;

            obstacle.carving = false;
            obstacle.enabled = false;
        }

        EnsureSimpleAreaMarkers(root);
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

    private static string[] FindCurrentV2SourcePrefabPaths(string[] v2RoomPrefabPaths)
    {
        if (v2RoomPrefabPaths == null || v2RoomPrefabPaths.Length == 0)
            return Array.Empty<string>();

        var results = new List<string>();
        for (int i = 0; i < v2RoomPrefabPaths.Length; i++)
        {
            string fileName = Path.GetFileName(v2RoomPrefabPaths[i]);
            string modifiedCandidate = V2ModifiedSourceRoot + "/" + fileName;
            string legacyCandidate = V2LegacySourceRoot + "/" + fileName;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(modifiedCandidate) != null)
                results.Add(modifiedCandidate);
            else if (AssetDatabase.LoadAssetAtPath<GameObject>(legacyCandidate) != null)
                results.Add(legacyCandidate);
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AuthorV2SourceAreaPrefab(string path, List<string> messages)
    {
        RequirePrefab(path);
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int simpleAreaCount = EnsureSimpleAreaMarkers(root);
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved)
                throw new InvalidOperationException("Could not save V2 source area prefab: " + path);

            messages.Add($"source={path}, simpleAreas={simpleAreaCount}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ValidateV2SourceAreaPrefab(
        string path,
        List<string> failures,
        List<string> details)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
        {
            failures.Add("missing V2 source prefab: " + path);
            return;
        }

        DungeonNavMeshArea[] areas = asset.GetComponentsInChildren<DungeonNavMeshArea>(true);
        int walkableAreas = areas.Count(area => area != null && area.IncludeInRuntimeBake && area.IsWalkable);
        int notWalkableAreas = areas.Count(area => area != null && area.IncludeInRuntimeBake && area.IsNotWalkable);
        details.Add($"source={path}, walkableAreas={walkableAreas}, notWalkableAreas={notWalkableAreas}");
        if (walkableAreas == 0)
            failures.Add("V2 source prefab requires at least one simple Walkable area: " + path);
    }

    private static void AuthorV2RoomPrefab(string path, List<string> messages)
    {
        RequirePrefab(path);
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            NavMeshSurface[] surfaces = root.GetComponentsInChildren<NavMeshSurface>(true);
            for (int i = 0; i < surfaces.Length; i++)
            {
                NavMeshSurface surface = surfaces[i];
                if (surface == null)
                    continue;

                surface.RemoveData();
                surface.navMeshData = null;
                surface.enabled = false;
            }

            NavMeshLink[] links = root.GetComponentsInChildren<NavMeshLink>(true);
            int doorwayLinkCount = 0;
            for (int i = 0; i < links.Length; i++)
            {
                NavMeshLink link = links[i];
                if (link == null || link.GetComponentInParent<DunGen.Doorway>() == null)
                    continue;

                link.enabled = false;
                doorwayLinkCount++;
            }

            NavMeshObstacle[] obstacles = root.GetComponentsInChildren<NavMeshObstacle>(true);
            int doorObstacleCount = 0;
            for (int i = 0; i < obstacles.Length; i++)
            {
                NavMeshObstacle obstacle = obstacles[i];
                if (obstacle == null || obstacle.GetComponentInParent<Door>() == null)
                    continue;

                obstacle.carving = false;
                obstacle.enabled = false;
                doorObstacleCount++;
            }

            int simpleAreaCount = EnsureSimpleAreaMarkers(root);

            PrefabUtility.SaveAsPrefabAsset(root, path, out bool savedRoom);
            if (!savedRoom)
                throw new InvalidOperationException("Could not save V2 room prefab: " + path);

            messages.Add(
                $"room={path}, surfaces={surfaces.Length}, doorwayLinks={doorwayLinkCount}, " +
                $"doorObstacles={doorObstacleCount}, floorColliders={CountFloorColliders(root)}, " +
                $"simpleAreas={simpleAreaCount}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void AuthorDoorPrefab(string path, List<string> messages)
    {
        RequirePrefab(path);
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Door door = root.GetComponentInChildren<Door>(true);
            DungeonDoor dungeonDoor = root.GetComponentInChildren<DungeonDoor>(true);
            if (door == null || dungeonDoor == null)
                throw new InvalidOperationException($"{path} must contain both Door and DunGen.Door.");

            MonsterDoorLinkBinding binding = door.GetComponent<MonsterDoorLinkBinding>();
            if (binding == null)
                binding = door.gameObject.AddComponent<MonsterDoorLinkBinding>();

            Collider[] passageColliders = door.GetComponents<Collider>()
                .Where(collider => collider != null && !collider.isTrigger)
                .ToArray();
            if (passageColliders.Length == 0)
                throw new InvalidOperationException($"{path} has no physical door collider.");

            binding.ConfigureContinuousDoor(door, dungeonDoor, passageColliders);

            NavMeshObstacle[] obstacles = root.GetComponentsInChildren<NavMeshObstacle>(true);
            for (int i = 0; i < obstacles.Length; i++)
            {
                obstacles[i].carving = false;
                obstacles[i].enabled = false;
            }

            NavMeshLink[] doorwayLinks = root.GetComponentsInChildren<NavMeshLink>(true);
            for (int i = 0; i < doorwayLinks.Length; i++)
                doorwayLinks[i].enabled = false;

            PrefabUtility.SaveAsPrefabAsset(root, path, out bool savedDoor);
            if (!savedDoor)
                throw new InvalidOperationException("Could not save door prefab: " + path);

            messages.Add($"door={path}, binding={binding.name}, colliders={passageColliders.Length}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void AuthorMonsterPrefab(string path, List<string> messages)
    {
        RequirePrefab(path);
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            NavMeshAgent agent = root.GetComponentInChildren<NavMeshAgent>(true);
            if (agent == null)
                throw new InvalidOperationException($"{path} has no NavMeshAgent.");

            DoorAutoOpener opener = agent.GetComponent<DoorAutoOpener>();
            if (opener == null)
                opener = agent.gameObject.AddComponent<DoorAutoOpener>();

            LayerMask doorMask = BuildContinuousDoorDetectionMask();
            float lookAhead = Mathf.Max(2.5f, agent.radius * 4f);
            float probeRadius = Mathf.Max(0.55f, agent.radius * 1.25f);
            opener.ConfigureContinuousDoorDetection(true, lookAhead, probeRadius, 0.1f, doorMask);

            PrefabUtility.SaveAsPrefabAsset(root, path, out bool savedMonster);
            if (!savedMonster)
                throw new InvalidOperationException("Could not save monster prefab: " + path);

            messages.Add($"monster={path}, agentType={agent.agentTypeID}, lookAhead={lookAhead:0.00}, radius={probeRadius:0.00}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ValidateDoorPrefab(string path, List<string> failures, List<string> details)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
        {
            failures.Add("missing door prefab: " + path);
            return;
        }

        Door door = asset.GetComponentInChildren<Door>(true);
        MonsterDoorLinkBinding binding = asset.GetComponentInChildren<MonsterDoorLinkBinding>(true);
        DungeonDoor dungeonDoor = asset.GetComponentInChildren<DungeonDoor>(true);
        NavMeshObstacle[] obstacles = asset.GetComponentsInChildren<NavMeshObstacle>(true);
        NavMeshLink[] links = asset.GetComponentsInChildren<NavMeshLink>(true);

        bool directRefs = door != null && binding != null && binding.RegularDoor == door &&
                          dungeonDoor != null && binding.DungeonDoor == dungeonDoor && binding.HasPassageCollider;
        bool obstaclesDisabled = obstacles.All(obstacle => obstacle != null && !obstacle.enabled && !obstacle.carving);
        bool linksDisabled = links.All(link => link != null && !link.enabled);
        details.Add($"door={path}, directRefs={directRefs}, obstaclesDisabled={obstaclesDisabled}, linksDisabled={linksDisabled}");

        if (!directRefs)
            failures.Add("door binding references are incomplete: " + path);
        if (!obstaclesDisabled)
            failures.Add("door obstacle must remain disabled and non-carving: " + path);
        if (!linksDisabled)
            failures.Add("door NavMeshLink must remain disabled: " + path);
    }

    private static void ValidateV2RoomPrefab(string path, List<string> failures, List<string> details)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
        {
            failures.Add("missing V2 room prefab: " + path);
            return;
        }

        NavMeshSurface[] surfaces = asset.GetComponentsInChildren<NavMeshSurface>(true);
        NavMeshLink[] doorwayLinks = asset.GetComponentsInChildren<NavMeshLink>(true)
            .Where(link => link != null && link.GetComponentInParent<DunGen.Doorway>() != null)
            .ToArray();
        NavMeshObstacle[] doorObstacles = asset.GetComponentsInChildren<NavMeshObstacle>(true)
            .Where(obstacle => obstacle != null && obstacle.GetComponentInParent<Door>() != null)
            .ToArray();

        bool surfacesNormalized = surfaces.All(surface =>
            surface != null && !surface.enabled && surface.navMeshData == null);
        bool doorwayLinksDisabled = doorwayLinks.All(link => !link.enabled);
        bool doorObstaclesDisabled = doorObstacles.All(obstacle => !obstacle.enabled && !obstacle.carving);
        int floorColliders = CountFloorColliders(asset);
        DungeonNavMeshArea[] simpleAreas = asset.GetComponentsInChildren<DungeonNavMeshArea>(true);
        int walkableAreas = simpleAreas.Count(area => area != null && area.IncludeInRuntimeBake && area.IsWalkable);
        int notWalkableAreas = simpleAreas.Count(area => area != null && area.IncludeInRuntimeBake && area.IsNotWalkable);
        int invalidAttachedAreas = simpleAreas.Count(area =>
            area != null && area.IncludeInRuntimeBake &&
            area.Shape == DungeonNavMeshArea.ShapeType.AttachedColliders &&
            !area.GetComponents<Collider>().Any(collider => collider != null && collider.enabled && !collider.isTrigger));
        details.Add(
            $"room={path}, surfacesNormalized={surfacesNormalized}, doorwayLinksDisabled={doorwayLinksDisabled}, " +
            $"doorObstaclesDisabled={doorObstaclesDisabled}, floorColliders={floorColliders}, " +
            $"walkableAreas={walkableAreas}, notWalkableAreas={notWalkableAreas}, invalidAreas={invalidAttachedAreas}");

        if (!surfacesNormalized)
            failures.Add("V2 room child NavMeshSurface data must be removed and disabled: " + path);
        if (!doorwayLinksDisabled)
            failures.Add("V2 room doorway NavMeshLink must remain disabled: " + path);
        if (!doorObstaclesDisabled)
            failures.Add("V2 room door obstacle must remain disabled and non-carving: " + path);
        if (walkableAreas == 0)
            failures.Add("V2 room requires at least one simple Walkable area: " + path);
        if (invalidAttachedAreas > 0)
            failures.Add("V2 room has an AttachedColliders area without a usable Collider: " + path);
    }

    public static int EnsureSimpleAreaMarkers(GameObject root)
    {
        if (root == null)
            return 0;

        int floorLayer = LayerMask.NameToLayer("Floor");
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        var floorObjects = colliders
            .Where(collider => collider != null && collider.enabled && !collider.isTrigger &&
                               collider.gameObject.layer == floorLayer)
            .Select(collider => collider.gameObject)
            .Distinct()
            .ToArray();

        for (int i = 0; i < floorObjects.Length; i++)
        {
            GameObject floorObject = floorObjects[i];
            DungeonNavMeshArea area = floorObject.GetComponent<DungeonNavMeshArea>();
            if (area == null)
            {
                area = floorObject.AddComponent<DungeonNavMeshArea>();
                area.ConfigureFromAttachedColliders(DungeonNavMeshArea.AreaType.Walkable);
            }
        }

        NavMeshModifierVolume[] volumes = root.GetComponentsInChildren<NavMeshModifierVolume>(true);
        for (int i = 0; i < volumes.Length; i++)
        {
            NavMeshModifierVolume volume = volumes[i];
            if (volume == null || volume.area != 1)
                continue;

            DungeonNavMeshArea area = volume.GetComponent<DungeonNavMeshArea>();
            if (area == null)
            {
                area = volume.gameObject.AddComponent<DungeonNavMeshArea>();
                area.ConfigureBox(DungeonNavMeshArea.AreaType.NotWalkable, volume.center, volume.size);
            }

            // The simple component becomes the single source of truth for V2 runtime baking.
            volume.enabled = false;
        }

        return root.GetComponentsInChildren<DungeonNavMeshArea>(true).Length;
    }

    private static void ValidateMonsterPrefab(string path, List<string> failures, List<string> details)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
        {
            failures.Add("missing monster prefab: " + path);
            return;
        }

        NavMeshAgent agent = asset.GetComponentInChildren<NavMeshAgent>(true);
        DoorAutoOpener opener = agent != null ? agent.GetComponent<DoorAutoOpener>() : null;
        details.Add($"monster={path}, agent={agent != null}, doorOpener={opener != null}");
        if (agent == null || opener == null)
            failures.Add("monster requires NavMeshAgent and DoorAutoOpener on the same object: " + path);
    }

    private static void RequirePrefab(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            throw new InvalidOperationException("Prefab not found: " + path);
    }

    internal static LayerMask BuildContinuousDoorDetectionMask()
    {
        int mask = 0;
        AddLayerToMask(ref mask, "Door");
        AddLayerToMask(ref mask, "Interactable");
        return mask != 0 ? mask : ~0;
    }

    private static void AddLayerToMask(ref int mask, string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer >= 0)
            mask |= 1 << layer;
    }

    private static int CountFloorColliders(GameObject root)
    {
        if (root == null)
            return 0;

        int floorLayer = LayerMask.NameToLayer("Floor");
        if (floorLayer < 0)
            return 0;

        return root.GetComponentsInChildren<Collider>(true).Count(collider =>
            collider != null && collider.enabled && !collider.isTrigger &&
            IsActiveInPrefabHierarchy(collider.transform) && collider.gameObject.layer == floorLayer);
    }

    private static bool IsActiveInPrefabHierarchy(Transform transform)
    {
        for (Transform current = transform; current != null; current = current.parent)
        {
            if (!current.gameObject.activeSelf)
                return false;
        }

        return true;
    }
}
