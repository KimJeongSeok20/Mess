using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DunGen;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;

public sealed class DungeonRuntimeNavMeshPipeline : MonoBehaviour
{
    private const int SmilyAgentTypeId = -1923039037;
    private const int ClownAgentTypeId = -902729914;
    private const string RuntimeBakeRootName = "__RuntimeNavMeshBake";

    public enum BakeGeometryMode
    {
        MeshAndDoorwayBridges,
        ProxyFloorsAndDoorwayBridges
    }

    [Header("Refs")]
    [SerializeField] private RuntimeDungeon runtimeDungeon;
    [SerializeField] private Component unityNavMeshAdapter;
    [SerializeField] private DungeonMonsterSpawner monsterSpawner;

    [Header("DunGen Runtime Bake")]
    [SerializeField] private bool enableRuntimeBake = true;
    [SerializeField] private bool configureDunGenAdapter = true;
    [SerializeField] private bool disableDunGenDoorwayLinks = true;
    [SerializeField] private int preBakePostProcessPriority = 100;
    [SerializeField] private BakeGeometryMode bakeGeometryMode = BakeGeometryMode.ProxyFloorsAndDoorwayBridges;
    [SerializeField] private int[] fallbackAgentTypeIds = { SmilyAgentTypeId, ClownAgentTypeId };
    [SerializeField] private string floorLayerName = "Floor";
    [SerializeField] private string navBakeLayerName = "NavBake";
    [SerializeField] private string[] meshBakeLayerNames = { "Floor", "NavBake" };
    [SerializeField] private float bridgeThickness = 0.12f;
    [SerializeField] private float bridgeOverlap = 0.35f;
    [SerializeField] private float minimumBridgeWidth = 0.75f;
    [SerializeField] private float minimumInternalLinkBridgeWidth = 1.2f;
    [SerializeField] private float internalLinkMaxVerticalDelta = 0.25f;
    [SerializeField] private float proxyFloorThickness = 0.08f;
    [SerializeField] private bool createRuntimeBlockerVolumes = true;
    [SerializeField] private bool cleanupTemporaryBakeRootAfterValidation = true;

    [Header("Validation")]
    [SerializeField] private bool validateBeforeMonsterSpawn = true;
    [SerializeField] private bool blockMonsterSpawnWhenValidationFails = true;
    [SerializeField] private bool validateUnwantedNavMesh = false;
    [SerializeField] private GameObject doorObstacleValidationPrefab;
    [SerializeField] private bool runDoorPrefabProbeWhenNoDoorInstances = true;
    [SerializeField] private float navSampleRadius = 6f;
    [SerializeField] private float doorPathProbeDistance = 2.5f;
    [SerializeField] private float doorCarveSettleSeconds = 0.75f;
    [SerializeField] private float closedDoorDetourMultiplier = 1.75f;
    [SerializeField] private float doorValidationCorridorWidth = 1.4f;
    [SerializeField] private float doorValidationCorridorLength = 7f;
    [SerializeField] private float doorValidationCorridorOffset = 30f;

    [Header("Report")]
    [SerializeField] private bool writeReportFile = true;
    [SerializeField] private string reportPath = "Assets/testnavmesh/StartMapRuntimeBakeProductionReport.txt";
    [SerializeField] private bool logReport = true;

    private Coroutine _finalizeRoutine;
    private readonly List<int> _runtimeAgentTypeIds = new();
    private readonly HashSet<Tile> _explicitAreaTiles = new();
    private bool _preBakePostProcessRegistered;
    private int _preparedRunId;
    private RuntimeBakeReport _preparedReport;
    private Dungeon _preparedDungeon;
    private GameObject _validationDoorProbe;
    private int _currentSeed;
    private int _currentFlowIndex;
    private bool _useNavBakeOnlyForCurrentRun;

    public bool IsReady { get; private set; }
    public bool LastValidationPassed { get; private set; }
    public string LastReport { get; private set; }
    public int CurrentRunId { get; private set; }
    public int ReadyRunId { get; private set; }
    /// <summary>Run id of the last bake that finished, whether or not validation accepted it.</summary>
    public int LastCompletedRunId { get; private set; }
    /// <summary>True when no bake is in flight for the current run (finished, accepted or rejected).</summary>
    public bool IsRunFinished => CurrentRunId > 0 && LastCompletedRunId == CurrentRunId;

    public event Action<DunGen.DungeonGenerator, int> RuntimeNavMeshReady;

    private void OnEnable()
    {
        RegisterPreBakePostProcess();
        ConfigureDunGenAdapter();
    }

    private void OnDisable()
    {
        UnregisterPreBakePostProcess();
    }

    public void BeginGeneration(int seed, int flowIndex)
    {
        CurrentRunId++;
        _currentSeed = seed;
        _currentFlowIndex = flowIndex;
        _preparedRunId = 0;
        _preparedReport = null;
        _preparedDungeon = null;
        _explicitAreaTiles.Clear();
        _useNavBakeOnlyForCurrentRun = false;
        DestroyValidationDoorProbe();
        ReadyRunId = 0;
        IsReady = false;
        LastValidationPassed = false;
        LastReport = string.Empty;
        ConfigureDunGenAdapter();

        if (_finalizeRoutine != null)
        {
            StopCoroutine(_finalizeRoutine);
            _finalizeRoutine = null;
        }
    }

    public void CancelCurrentGeneration(string reason)
    {
        CurrentRunId++;
        if (_preparedDungeon != null)
            DestroyRuntimeBakeRoot(_preparedDungeon.transform);

        _preparedRunId = 0;
        _preparedReport = null;
        _preparedDungeon = null;
        DestroyValidationDoorProbe();
        ReadyRunId = 0;
        IsReady = false;
        LastValidationPassed = false;

        if (_finalizeRoutine != null)
        {
            StopCoroutine(_finalizeRoutine);
            _finalizeRoutine = null;
        }

        if (!string.IsNullOrWhiteSpace(reason))
            LastReport = "[RuntimeNavMeshPipeline] Cancelled: " + reason;
    }

    public void RunAfterGeneration(DunGen.DungeonGenerator generator, int seed, int flowIndex)
    {
        if (CurrentRunId == 0)
            BeginGeneration(seed, flowIndex);

        if (_finalizeRoutine != null)
            StopCoroutine(_finalizeRoutine);

        _finalizeRoutine = StartCoroutine(FinalizeAfterGeneration(generator, seed, flowIndex, CurrentRunId));
    }

    private void RegisterPreBakePostProcess()
    {
        if (_preBakePostProcessRegistered || runtimeDungeon == null)
            return;

        runtimeDungeon.Generator.RegisterPostProcessStep(OnDunGenPreNavMeshBake, preBakePostProcessPriority);
        _preBakePostProcessRegistered = true;
    }

    private void UnregisterPreBakePostProcess()
    {
        if (!_preBakePostProcessRegistered || runtimeDungeon == null)
            return;

        runtimeDungeon.Generator.UnregisterPostProcessStep(OnDunGenPreNavMeshBake);
        _preBakePostProcessRegistered = false;
    }

    private void OnDunGenPreNavMeshBake(DunGen.DungeonGenerator generator)
    {
        if (!enableRuntimeBake || generator == null || generator.CurrentDungeon == null)
            return;

        Dungeon dungeon = generator.CurrentDungeon;
        RuntimeBakeReport report = CreateReport(dungeon, _currentSeed, _currentFlowIndex);
        report.BakeProvider = "DunGen UnityNavMeshAdapter FullDungeonBake";

        PrepareRuntimeBake(dungeon, report);
        ConfigureDunGenAdapter(report);
        ConfigureRootSurfacesForDunGenAdapter(dungeon, report);

        _preparedRunId = CurrentRunId;
        _preparedDungeon = dungeon;
        _preparedReport = report;
    }

    private IEnumerator FinalizeAfterGeneration(DunGen.DungeonGenerator generator, int seed, int flowIndex, int runId)
    {
        if (!IsCurrentRun(runId))
            yield break;

        if (generator == null || generator.CurrentDungeon == null)
        {
            Complete(false, "[RuntimeNavMeshPipeline] Missing generated dungeon.", generator, runId);
            yield break;
        }

        Dungeon dungeon = generator.CurrentDungeon;
        RuntimeBakeReport report = _preparedRunId == runId && _preparedDungeon == dungeon && _preparedReport != null
            ? _preparedReport
            : CreateReport(dungeon, seed, flowIndex);

        if (!enableRuntimeBake)
        {
            report.Accepted = true;
            report.Notes = "Runtime bake disabled; using existing generated NavMesh.";
            LastReport = BuildReportText(report);
            BindMonsterSpawner(dungeon);
            Complete(true, LastReport, generator, runId);
            yield break;
        }

        if (_preparedRunId != runId || _preparedDungeon != dungeon)
            AppendNote(report, "pre-bake bridge preparation did not run before DunGen adapter bake");

        report.RootSurfaceCount = CountRootSurfaces(dungeon);
        ApplyDoorObstacleState(dungeon.transform, report);
        yield return null;
        if (!IsCurrentRun(runId))
            yield break;

        ValidateNavigation(dungeon, report);
        ValidateContinuousDoorPaths(dungeon.transform, report);

        DestroyValidationDoorProbe();
        CleanupRuntimeBakeRootAfterValidation(dungeon, report);

        report.Accepted = EvaluateAcceptance(report);
        bool shouldBindMonsterSpawner = ShouldBindMonsterSpawner(report);
        if (shouldBindMonsterSpawner && !report.Accepted)
            AppendNote(report, "monster spawn binding allowed despite runtime NavMesh validation failure");

        LastReport = BuildReportText(report);
        WriteReport(LastReport);

        if (logReport)
            Debug.Log(LastReport, this);

        if (shouldBindMonsterSpawner)
        {
            BindMonsterSpawner(dungeon);
            if (!report.Accepted)
                Debug.LogWarning("[RuntimeNavMeshPipeline] Runtime bake validation failed, but monster spawn binding is allowed by settings.", this);
        }
        else
            Debug.LogWarning("[RuntimeNavMeshPipeline] Runtime bake validation failed; monster spawn binding was skipped.", this);

        Complete(report.Accepted, LastReport, generator, runId);
    }

    private WaitForSeconds WaitForCarveSettle()
    {
        return new WaitForSeconds(Mathf.Max(0f, doorCarveSettleSeconds));
    }

    private bool IsCurrentRun(int runId)
    {
        return runId == CurrentRunId;
    }

    private void Complete(bool success, string report, DunGen.DungeonGenerator generator, int runId)
    {
        if (!IsCurrentRun(runId))
            return;

        LastValidationPassed = success;
        IsReady = success;
        ReadyRunId = success ? runId : 0;
        LastCompletedRunId = runId;
        _finalizeRoutine = null;

        if (IsReady)
            RuntimeNavMeshReady?.Invoke(generator, runId);

        if (!success)
            LastReport = string.IsNullOrEmpty(report) ? LastReport : report;
    }

    private RuntimeBakeReport CreateReport(Dungeon dungeon, int seed, int flowIndex)
    {
        RuntimeBakeReport report = new RuntimeBakeReport
        {
            Seed = seed,
            FlowIndex = flowIndex,
            RoomCount = dungeon != null && dungeon.AllTiles != null ? dungeon.AllTiles.Count : 0,
            ConnectionCount = dungeon != null && dungeon.Connections != null ? dungeon.Connections.Count : 0,
            BakeProvider = "DunGen UnityNavMeshAdapter",
        };

        CollectRuntimeAgentTypeIds();
        FillBakeSettings(report);
        return report;
    }

    private void ConfigureDunGenAdapter(RuntimeBakeReport report = null)
    {
        if (!configureDunGenAdapter || unityNavMeshAdapter == null)
            return;

        CollectRuntimeAgentTypeIds();

        Type adapterType = unityNavMeshAdapter.GetType();
        SetAdapterEnum(adapterType, "BakeMode", 3);
        SetAdapterField(adapterType, "LayerMask", (LayerMask)RuntimeBakeLayerMask());
        SetAdapterField(adapterType, "AddNavMeshLinksBetweenRooms", !disableDunGenDoorwayLinks);
        // FullRebakeTargets are assigned from the generated DungeonRoot before the DunGen adapter runs.
        SetAdapterField(adapterType, "AutoGenerateFullRebakeSurfaces", false);
        ConfigureAdapterAgentTypes(adapterType);

        if (report != null)
            FillBakeSettings(report);
    }

    private void ConfigureAdapterAgentTypes(Type adapterType)
    {
        if (adapterType == null)
            return;

        System.Reflection.FieldInfo field = adapterType.GetField("NavMeshAgentTypes");
        if (field == null)
            return;

        if (field.GetValue(unityNavMeshAdapter) is not IList list)
            return;

        Type elementType = field.FieldType.IsGenericType ? field.FieldType.GetGenericArguments()[0] : null;
        if (elementType == null)
            return;

        list.Clear();
        for (int i = 0; i < _runtimeAgentTypeIds.Count; i++)
        {
            object info = Activator.CreateInstance(elementType);
            SetObjectField(info, "AgentTypeID", _runtimeAgentTypeIds[i]);
            SetObjectField(info, "AreaTypeID", 0);
            SetObjectField(info, "DisableLinkWhenDoorIsClosed", false);
            list.Add(info);
        }
    }

    private void SetAdapterEnum(Type adapterType, string fieldName, int value)
    {
        System.Reflection.FieldInfo field = adapterType?.GetField(fieldName);
        if (field == null || !field.FieldType.IsEnum)
            return;

        field.SetValue(unityNavMeshAdapter, Enum.ToObject(field.FieldType, value));
    }

    private void SetAdapterField(Type adapterType, string fieldName, object value)
    {
        System.Reflection.FieldInfo field = adapterType?.GetField(fieldName);
        if (field == null)
            return;

        field.SetValue(unityNavMeshAdapter, value);
    }

    private static void SetObjectField(object target, string fieldName, object value)
    {
        if (target == null)
            return;

        System.Reflection.FieldInfo field = target.GetType().GetField(fieldName);
        if (field == null)
            return;

        field.SetValue(target, value);
    }

    private void FillBakeSettings(RuntimeBakeReport report)
    {
        if (report == null)
            return;

        NavMeshCollectGeometry geometry = bakeGeometryMode == BakeGeometryMode.ProxyFloorsAndDoorwayBridges
            ? NavMeshCollectGeometry.PhysicsColliders
            : NavMeshCollectGeometry.RenderMeshes;

        int mask = RuntimeBakeLayerMask();

        report.BakeGeometry = geometry.ToString();
        report.LayerMask = mask;
        report.AgentTypeSummary = BuildAgentTypeSummary(_runtimeAgentTypeIds);
        report.BlockMonsterSpawnWhenValidationFails = blockMonsterSpawnWhenValidationFails;
    }

    private void PrepareRuntimeBake(Dungeon dungeon, RuntimeBakeReport report)
    {
        Transform root = dungeon.transform;
        DestroyExistingRuntimeBakeRoot(root);
        DisableChildSurfacesAndCountLinks(dungeon, report);
        ApplyDoorObstacleState(root, report);

        Transform bakeRoot = CreateRuntimeBakeRoot(root);
        AuthoredAreaBakeStats authoredAreas = CreateAuthoredAreaGeometry(dungeon, bakeRoot);
        report.ExplicitAreaTileCount = authoredAreas.ExplicitTileCount;
        report.WalkableAreaCount = authoredAreas.WalkableAreaCount;
        report.WalkableAreaProxyCount = authoredAreas.WalkableProxyCount;
        report.NotWalkableAreaCount = authoredAreas.NotWalkableAreaCount;
        report.IgnoredLegacyFloorObjectCount = authoredAreas.IgnoredLegacyFloorObjectCount;
        _useNavBakeOnlyForCurrentRun = dungeon.AllTiles != null && dungeon.AllTiles.Count > 0 &&
                                       authoredAreas.ExplicitTileCount == dungeon.AllTiles.Count;
        report.DoorwayBridgeCount = CreateDoorwayBridges(dungeon, bakeRoot);
        report.InternalLinkBridgeCount = CreateInternalLinkBridges(dungeon, bakeRoot);
        report.AuthoredBridgeProxyCount = CreateAuthoredBridgeRenderProxies(dungeon, bakeRoot);
        report.BlockerVolumeCount = authoredAreas.NotWalkableVolumeCount +
                                    CreateFootprintBlockerVolumes(dungeon, bakeRoot) +
                                    CreateRuntimeBlockerGeometry(dungeon, bakeRoot);

        if (bakeGeometryMode == BakeGeometryMode.ProxyFloorsAndDoorwayBridges)
            report.ProxyFloorCount = CreateTileFloorProxies(dungeon, bakeRoot, report);
    }

    private AuthoredAreaBakeStats CreateAuthoredAreaGeometry(Dungeon dungeon, Transform parent)
    {
        AuthoredAreaBakeStats stats = new AuthoredAreaBakeStats();
        _explicitAreaTiles.Clear();
        if (dungeon == null || parent == null || dungeon.AllTiles == null)
            return stats;

        for (int tileIndex = 0; tileIndex < dungeon.AllTiles.Count; tileIndex++)
        {
            Tile tile = dungeon.AllTiles[tileIndex];
            if (tile == null)
                continue;

            DungeonNavMeshArea[] areas = tile.GetComponentsInChildren<DungeonNavMeshArea>(true);
            bool explicitWalkableLayout = HasActiveWalkableArea(areas);
            if (explicitWalkableLayout)
            {
                _explicitAreaTiles.Add(tile);
                stats.ExplicitTileCount++;
                stats.IgnoredLegacyFloorObjectCount += IgnoreLegacyFloorSources(tile);
            }

            for (int areaIndex = 0; areaIndex < areas.Length; areaIndex++)
            {
                DungeonNavMeshArea area = areas[areaIndex];
                if (!IsActiveArea(area))
                    continue;

                if (area.IsWalkable)
                {
                    stats.WalkableAreaCount++;
                    stats.WalkableProxyCount += CreateWalkableAreaProxies(area, parent);
                }
                else
                {
                    stats.NotWalkableAreaCount++;
                    stats.NotWalkableVolumeCount += CreateNotWalkableAreaVolumes(area, parent);
                }
            }
        }

        return stats;
    }

    private int CreateWalkableAreaProxies(DungeonNavMeshArea area, Transform parent)
    {
        if (area == null || parent == null)
            return 0;

        if (area.Shape == DungeonNavMeshArea.ShapeType.Box)
        {
            CreateBoxBakeCollider(
                parent,
                "RuntimeNavMesh_Walkable_" + area.name,
                area.transform.TransformPoint(area.Center),
                area.transform.rotation,
                Vector3.Scale(area.Size, Abs(area.transform.lossyScale)));
            return 1;
        }

        var colliders = new List<Collider>();
        area.GetEnabledAttachedColliders(colliders);
        int count = 0;
        for (int i = 0; i < colliders.Count; i++)
        {
            if (CreateColliderBakeProxy(parent, colliders[i], area.name, i))
                count++;
        }

        return count;
    }

    private int CreateNotWalkableAreaVolumes(DungeonNavMeshArea area, Transform parent)
    {
        if (area == null || parent == null)
            return 0;

        if (area.Shape == DungeonNavMeshArea.ShapeType.Box)
        {
            CreateNotWalkableVolume(
                parent,
                "RuntimeNavMesh_NotWalkable_" + area.name,
                area.transform.TransformPoint(area.Center),
                area.transform.rotation,
                Vector3.Scale(area.Size, Abs(area.transform.lossyScale)));
            return 1;
        }

        var colliders = new List<Collider>();
        area.GetEnabledAttachedColliders(colliders);
        int count = 0;
        for (int i = 0; i < colliders.Count; i++)
        {
            Collider collider = colliders[i];
            Bounds bounds = collider.bounds;
            if (bounds.size.x <= 0.01f || bounds.size.y <= 0.01f || bounds.size.z <= 0.01f)
                continue;

            CreateNotWalkableVolume(
                parent,
                "RuntimeNavMesh_NotWalkable_" + area.name + "_" + i,
                bounds.center,
                Quaternion.identity,
                bounds.size);
            count++;
        }

        return count;
    }

    private void CreateBoxBakeCollider(
        Transform parent,
        string objectName,
        Vector3 worldCenter,
        Quaternion worldRotation,
        Vector3 worldSize)
    {
        GameObject proxy = new GameObject(objectName);
        proxy.transform.SetParent(parent, true);
        proxy.transform.SetPositionAndRotation(worldCenter, worldRotation);
        proxy.transform.localScale = Vector3.one;
        SetLayerRecursively(proxy, LayerIndex(navBakeLayerName));

        BoxCollider box = proxy.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = PositiveSize(worldSize);
    }

    private bool CreateColliderBakeProxy(Transform parent, Collider source, string areaName, int index)
    {
        return CreateColliderBakeProxy(
            parent,
            source,
            $"RuntimeNavMesh_Walkable_{areaName}_{index}");
    }

    private bool CreateColliderBakeProxy(Transform parent, Collider source, string objectName)
    {
        if (parent == null || source == null)
            return false;

        GameObject proxy = new GameObject(objectName);
        proxy.transform.SetParent(parent, true);
        SetLayerRecursively(proxy, LayerIndex(navBakeLayerName));

        if (source is BoxCollider sourceBox)
        {
            proxy.transform.SetPositionAndRotation(
                sourceBox.transform.TransformPoint(sourceBox.center),
                sourceBox.transform.rotation);
            BoxCollider box = proxy.AddComponent<BoxCollider>();
            box.size = PositiveSize(Vector3.Scale(sourceBox.size, Abs(sourceBox.transform.lossyScale)));
            return true;
        }

        if (source is MeshCollider sourceMesh && sourceMesh.sharedMesh != null)
        {
            proxy.transform.SetPositionAndRotation(sourceMesh.transform.position, sourceMesh.transform.rotation);
            proxy.transform.localScale = sourceMesh.transform.lossyScale;
            MeshCollider mesh = proxy.AddComponent<MeshCollider>();
            mesh.sharedMesh = sourceMesh.sharedMesh;
            mesh.convex = sourceMesh.convex;
            return true;
        }

        if (source is SphereCollider sourceSphere)
        {
            proxy.transform.SetPositionAndRotation(
                sourceSphere.transform.TransformPoint(sourceSphere.center),
                sourceSphere.transform.rotation);
            SphereCollider sphere = proxy.AddComponent<SphereCollider>();
            sphere.radius = sourceSphere.radius * MaxAbsComponent(sourceSphere.transform.lossyScale);
            return true;
        }

        if (source is CapsuleCollider sourceCapsule)
        {
            proxy.transform.SetPositionAndRotation(
                sourceCapsule.transform.TransformPoint(sourceCapsule.center),
                sourceCapsule.transform.rotation);
            CapsuleCollider capsule = proxy.AddComponent<CapsuleCollider>();
            capsule.direction = sourceCapsule.direction;
            capsule.radius = sourceCapsule.radius * MaxAbsComponent(sourceCapsule.transform.lossyScale);
            capsule.height = sourceCapsule.height * MaxAbsComponent(sourceCapsule.transform.lossyScale);
            return true;
        }

        if (source is TerrainCollider sourceTerrain && sourceTerrain.terrainData != null)
        {
            proxy.transform.SetPositionAndRotation(sourceTerrain.transform.position, sourceTerrain.transform.rotation);
            proxy.transform.localScale = sourceTerrain.transform.lossyScale;
            TerrainCollider terrain = proxy.AddComponent<TerrainCollider>();
            terrain.terrainData = sourceTerrain.terrainData;
            return true;
        }

        Bounds bounds = source.bounds;
        if (bounds.size.x <= 0.01f || bounds.size.z <= 0.01f)
        {
            Destroy(proxy);
            return false;
        }

        proxy.transform.SetPositionAndRotation(bounds.center, Quaternion.identity);
        BoxCollider fallback = proxy.AddComponent<BoxCollider>();
        fallback.size = PositiveSize(bounds.size);
        return true;
    }

    private void CreateNotWalkableVolume(
        Transform parent,
        string objectName,
        Vector3 worldCenter,
        Quaternion worldRotation,
        Vector3 worldSize)
    {
        GameObject volumeObject = new GameObject(objectName);
        volumeObject.transform.SetParent(parent, true);
        volumeObject.transform.SetPositionAndRotation(worldCenter, worldRotation);
        volumeObject.transform.localScale = Vector3.one;
        SetLayerRecursively(volumeObject, LayerIndex(navBakeLayerName));

        NavMeshModifierVolume volume = volumeObject.AddComponent<NavMeshModifierVolume>();
        volume.center = Vector3.zero;
        volume.size = PositiveSize(worldSize);
        volume.area = 1;
    }

    private int IgnoreLegacyFloorSources(Tile tile)
    {
        if (tile == null)
            return 0;

        int floorLayer = LayerIndex(floorLayerName);
        Collider[] colliders = tile.GetComponentsInChildren<Collider>(true);
        var configuredObjects = new HashSet<GameObject>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || collider.gameObject.layer != floorLayer ||
                !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy ||
                !configuredObjects.Add(collider.gameObject))
            {
                continue;
            }

            NavMeshModifier modifier = collider.GetComponent<NavMeshModifier>();
            if (modifier == null)
                modifier = collider.gameObject.AddComponent<NavMeshModifier>();

            modifier.enabled = true;
            modifier.ignoreFromBuild = true;
            modifier.overrideArea = false;
            modifier.applyToChildren = false;
            SetModifierToAffectAllAgents(modifier);
        }

        return configuredObjects.Count;
    }

    private static void SetModifierToAffectAllAgents(NavMeshModifier modifier)
    {
        if (modifier == null)
            return;

        System.Reflection.FieldInfo field = typeof(NavMeshModifier).GetField(
            "m_AffectedAgents",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field?.GetValue(modifier) is not IList agents)
            return;

        agents.Clear();
        agents.Add(-1);
    }

    private static bool HasActiveWalkableArea(DungeonNavMeshArea[] areas)
    {
        if (areas == null)
            return false;

        for (int i = 0; i < areas.Length; i++)
        {
            if (IsActiveArea(areas[i]) && areas[i].IsWalkable)
                return true;
        }

        return false;
    }

    private static bool IsActiveArea(DungeonNavMeshArea area)
    {
        return area != null && area.enabled && area.IncludeInRuntimeBake && area.gameObject.activeInHierarchy;
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private static Vector3 PositiveSize(Vector3 value)
    {
        Vector3 absolute = Abs(value);
        return new Vector3(
            Mathf.Max(0.01f, absolute.x),
            Mathf.Max(0.01f, absolute.y),
            Mathf.Max(0.01f, absolute.z));
    }

    private static float MaxAbsComponent(Vector3 value)
    {
        value = Abs(value);
        return Mathf.Max(value.x, Mathf.Max(value.y, value.z));
    }

    private void CollectRuntimeAgentTypeIds()
    {
        _runtimeAgentTypeIds.Clear();

        if (monsterSpawner != null)
        {
            List<int> configuredAgentTypeIds = new List<int>();
            monsterSpawner.CollectConfiguredAgentTypeIds(configuredAgentTypeIds);
            for (int i = 0; i < configuredAgentTypeIds.Count; i++)
            {
                if (IsSupportedRuntimeAgentType(configuredAgentTypeIds[i]))
                    AddUniqueAgentType(_runtimeAgentTypeIds, configuredAgentTypeIds[i]);
            }
        }

        if (fallbackAgentTypeIds != null)
        {
            for (int i = 0; i < fallbackAgentTypeIds.Length; i++)
            {
                if (IsSupportedRuntimeAgentType(fallbackAgentTypeIds[i]))
                    AddUniqueAgentType(_runtimeAgentTypeIds, fallbackAgentTypeIds[i]);
            }
        }

        if (_runtimeAgentTypeIds.Count == 0)
        {
            AddUniqueAgentType(_runtimeAgentTypeIds, SmilyAgentTypeId);
            AddUniqueAgentType(_runtimeAgentTypeIds, ClownAgentTypeId);
        }
    }

    private static bool IsSupportedRuntimeAgentType(int agentTypeId)
    {
        return agentTypeId == SmilyAgentTypeId || agentTypeId == ClownAgentTypeId;
    }

    private static void AddUniqueAgentType(List<int> target, int agentTypeId)
    {
        if (target == null)
            return;

        for (int i = 0; i < target.Count; i++)
        {
            if (target[i] == agentTypeId)
                return;
        }

        target.Add(agentTypeId);
    }

    private void DisableChildSurfacesAndCountLinks(Dungeon dungeon, RuntimeBakeReport report)
    {
        NavMeshSurface[] surfaces = dungeon.GetComponentsInChildren<NavMeshSurface>(true);
        for (int i = 0; i < surfaces.Length; i++)
        {
            if (surfaces[i] == null)
                continue;

            if (surfaces[i].gameObject == dungeon.gameObject)
                continue;

            surfaces[i].RemoveData();
            surfaces[i].enabled = false;
            report.DisabledChildSurfaceCount++;
        }

        NavMeshLink[] links = dungeon.GetComponentsInChildren<NavMeshLink>(true);
        report.AuthoredNavMeshLinkCount = links.Length;
    }

    private void ConfigureRootSurfacesForDunGenAdapter(Dungeon dungeon, RuntimeBakeReport report)
    {
        if (dungeon == null)
            return;

        int mask = RuntimeBakeLayerMask();

        NavMeshCollectGeometry geometry = bakeGeometryMode == BakeGeometryMode.ProxyFloorsAndDoorwayBridges
            ? NavMeshCollectGeometry.PhysicsColliders
            : NavMeshCollectGeometry.RenderMeshes;

        List<NavMeshSurface> surfaces = new List<NavMeshSurface>(dungeon.GetComponents<NavMeshSurface>());
        for (int i = surfaces.Count - 1; i >= 0; i--)
        {
            NavMeshSurface surface = surfaces[i];
            if (surface == null)
            {
                surfaces.RemoveAt(i);
                continue;
            }

            if (ContainsAgentType(_runtimeAgentTypeIds, surface.agentTypeID))
                continue;

            surface.RemoveData();
            surface.enabled = false;
            surfaces.RemoveAt(i);
            if (Application.isPlaying)
                Destroy(surface);
            else
                DestroyImmediate(surface);
        }

        for (int i = 0; i < _runtimeAgentTypeIds.Count; i++)
        {
            int agentTypeId = _runtimeAgentTypeIds[i];
            if (FindSurfaceForAgentType(surfaces, agentTypeId) != null)
                continue;

            NavMeshSurface surface = dungeon.gameObject.AddComponent<NavMeshSurface>();
            surface.agentTypeID = agentTypeId;
            surfaces.Add(surface);
        }

        for (int i = 0; i < surfaces.Count; i++)
        {
            NavMeshSurface surface = surfaces[i];
            if (surface == null)
                continue;

            surface.enabled = true;
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = geometry;
            surface.layerMask = mask;
            surface.defaultArea = 0;
            surface.buildHeightMesh = false;
        }

        ConfigureAdapterFullRebakeTargets(surfaces);
        report.RootSurfaceCount = surfaces.Count;
    }

    private void ConfigureAdapterFullRebakeTargets(List<NavMeshSurface> surfaces)
    {
        if (!configureDunGenAdapter || unityNavMeshAdapter == null)
            return;

        Type adapterType = unityNavMeshAdapter.GetType();
        SetAdapterField(adapterType, "AutoGenerateFullRebakeSurfaces", false);

        System.Reflection.FieldInfo targetsField = adapterType.GetField("FullRebakeTargets");
        if (targetsField == null)
            return;

        if (targetsField.GetValue(unityNavMeshAdapter) is not IList targets)
            return;

        targets.Clear();
        for (int i = 0; i < surfaces.Count; i++)
        {
            NavMeshSurface surface = surfaces[i];
            if (surface != null && ContainsAgentType(_runtimeAgentTypeIds, surface.agentTypeID))
                targets.Add(surface);
        }
    }

    private static bool ContainsAgentType(List<int> agentTypeIds, int agentTypeId)
    {
        if (agentTypeIds == null)
            return false;

        for (int i = 0; i < agentTypeIds.Count; i++)
        {
            if (agentTypeIds[i] == agentTypeId)
                return true;
        }

        return false;
    }

    private static NavMeshSurface FindSurfaceForAgentType(List<NavMeshSurface> surfaces, int agentTypeId)
    {
        if (surfaces == null)
            return null;

        for (int i = 0; i < surfaces.Count; i++)
        {
            NavMeshSurface surface = surfaces[i];
            if (surface != null && surface.agentTypeID == agentTypeId)
                return surface;
        }

        return null;
    }

    private static int CountRootSurfaces(Dungeon dungeon)
    {
        if (dungeon == null)
            return 0;

        return dungeon.GetComponents<NavMeshSurface>().Length;
    }

    private Transform CreateRuntimeBakeRoot(Transform dungeonRoot)
    {
        GameObject root = new GameObject(RuntimeBakeRootName);
        root.transform.SetParent(dungeonRoot, false);
        return root.transform;
    }

    private void DestroyValidationDoorProbe()
    {
        if (_validationDoorProbe == null)
            return;

        Destroy(_validationDoorProbe);
        _validationDoorProbe = null;
    }

    private void DestroyExistingRuntimeBakeRoot(Transform dungeonRoot)
    {
        Transform existing = dungeonRoot.Find(RuntimeBakeRootName);
        if (existing != null)
        {
            existing.name = RuntimeBakeRootName + "_Old";
            existing.gameObject.SetActive(false);
            Destroy(existing.gameObject);
        }
    }

    private void CleanupRuntimeBakeRootAfterValidation(Dungeon dungeon, RuntimeBakeReport report)
    {
        if (!cleanupTemporaryBakeRootAfterValidation || dungeon == null)
            return;

        bool cleaned = DestroyRuntimeBakeRoot(dungeon.transform);
        if (cleaned)
            AppendNote(report, "temporary runtime bake geometry cleaned after validation");
    }

    private bool DestroyRuntimeBakeRoot(Transform dungeonRoot)
    {
        if (dungeonRoot == null)
            return false;

        Transform existing = dungeonRoot.Find(RuntimeBakeRootName);
        if (existing == null)
            return false;

        existing.name = RuntimeBakeRootName + "_Cleaned";
        existing.gameObject.SetActive(false);
        Destroy(existing.gameObject);
        return true;
    }

    private int CreateDoorwayBridges(Dungeon dungeon, Transform parent)
    {
        if (dungeon.Connections == null)
            return 0;

        int count = 0;
        foreach (DoorwayConnection connection in dungeon.Connections)
        {
            if (connection?.A == null || connection.B == null)
                continue;

            if (CreateDoorwayBridge(parent, connection.A, connection.B))
                count++;
        }

        return count;
    }

    private bool CreateDoorwayBridge(Transform parent, Doorway a, Doorway b)
    {
        Vector3 aPosition = a.transform.position;
        Vector3 bPosition = b.transform.position;
        Vector3 horizontal = bPosition - aPosition;
        horizontal.y = 0f;

        if (horizontal.sqrMagnitude < 0.0001f)
        {
            horizontal = a.transform.forward;
            horizontal.y = 0f;
        }

        if (horizontal.sqrMagnitude < 0.0001f)
            return false;

        Vector3 direction = horizontal.normalized;
        float length = Mathf.Max(horizontal.magnitude + bridgeOverlap * 2f, 0.5f);
        float width = Mathf.Max(MinSocketWidth(a, b), minimumBridgeWidth);
        float floorY = EstimateDoorwayFloorY(aPosition, bPosition);
        Vector3 center = (aPosition + bPosition) * 0.5f;
        center.y = floorY - bridgeThickness * 0.5f;

        GameObject bridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bridge.name = "RuntimeNavMesh_DoorwayBridge";
        bridge.transform.SetParent(parent, true);
        bridge.transform.position = center;
        bridge.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        bridge.transform.localScale = new Vector3(width, bridgeThickness, length);
        SetLayerRecursively(bridge, LayerIndex(GeneratedBakeGeometryLayerName()));
        HideGeneratedBakeRenderers(bridge.transform);
        return true;
    }

    private int CreateInternalLinkBridges(Dungeon dungeon, Transform parent)
    {
        NavMeshLink[] links = dungeon.GetComponentsInChildren<NavMeshLink>(true);
        var created = new HashSet<string>();
        int count = 0;

        for (int i = 0; i < links.Length; i++)
        {
            NavMeshLink link = links[i];
            if (link == null || !link.gameObject.activeInHierarchy)
                continue;

            if (link.GetComponentInParent<Doorway>() != null)
                continue;

            Vector3 start = link.transform.TransformPoint(link.startPoint);
            Vector3 end = link.transform.TransformPoint(link.endPoint);
            if (Mathf.Abs(start.y - end.y) > internalLinkMaxVerticalDelta)
                continue;

            string key = InternalLinkBridgeKey(start, end, link.width);
            if (!created.Add(key))
                continue;

            if (CreateInternalLinkBridge(parent, start, end, link.width))
                count++;
        }

        return count;
    }

    private bool CreateInternalLinkBridge(Transform parent, Vector3 start, Vector3 end, float linkWidth)
    {
        Vector3 horizontal = end - start;
        horizontal.y = 0f;

        if (horizontal.sqrMagnitude < 0.0001f)
            return false;

        Vector3 direction = horizontal.normalized;
        float length = Mathf.Max(horizontal.magnitude + bridgeOverlap * 2f, 0.5f);
        float width = Mathf.Max(linkWidth, minimumInternalLinkBridgeWidth);
        float floorY = Mathf.Min(start.y, end.y);
        Vector3 center = (start + end) * 0.5f;
        center.y = floorY - bridgeThickness * 0.5f;

        GameObject bridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bridge.name = "RuntimeNavMesh_InternalLinkBridge";
        bridge.transform.SetParent(parent, true);
        bridge.transform.position = center;
        bridge.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        bridge.transform.localScale = new Vector3(width, bridgeThickness, length);
        SetLayerRecursively(bridge, LayerIndex(GeneratedBakeGeometryLayerName()));
        HideGeneratedBakeRenderers(bridge.transform);
        return true;
    }

    private int CreateAuthoredBridgeRenderProxies(Dungeon dungeon, Transform parent)
    {
        BoxCollider[] colliders = dungeon.GetComponentsInChildren<BoxCollider>(true);
        int count = 0;

        for (int i = 0; i < colliders.Length; i++)
        {
            BoxCollider collider = colliders[i];
            if (collider == null || !collider.gameObject.activeInHierarchy)
                continue;

            if (!collider.name.StartsWith("NavBakeBridge_InternalDoor", StringComparison.Ordinal))
                continue;

            if (CreateAuthoredBridgeRenderProxy(parent, collider))
                count++;
        }

        return count;
    }

    private bool CreateAuthoredBridgeRenderProxy(Transform parent, BoxCollider source)
    {
        if (source == null)
            return false;

        Vector3 size = Vector3.Scale(source.size, source.transform.lossyScale);
        if (size.x <= 0.01f || size.y <= 0.01f || size.z <= 0.01f)
            return false;

        GameObject proxy = GameObject.CreatePrimitive(PrimitiveType.Cube);
        proxy.name = "RuntimeNavMesh_AuthoredBridgeProxy_" + source.name;
        Destroy(proxy.GetComponent<Collider>());
        proxy.transform.SetParent(parent, true);
        proxy.transform.position = source.transform.TransformPoint(source.center);
        proxy.transform.rotation = source.transform.rotation;
        proxy.transform.localScale = size;
        SetLayerRecursively(proxy, LayerIndex(GeneratedBakeGeometryLayerName()));
        HideGeneratedBakeRenderers(proxy.transform);
        return true;
    }

    private int CreateRuntimeBlockerGeometry(Dungeon dungeon, Transform parent)
    {
        if (!createRuntimeBlockerVolumes || dungeon == null || parent == null)
            return 0;

        Collider[] colliders = dungeon.GetComponentsInChildren<Collider>(true);
        int count = 0;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (!ShouldCreateBlockerVolume(collider, parent))
                continue;

            if (CreateColliderBakeProxy(
                    parent,
                    collider,
                    $"RuntimeNavMesh_BlockerGeometry_{i}_{collider.name}"))
            {
                Transform proxy = parent.GetChild(parent.childCount - 1);
                NavMeshModifier modifier = proxy.gameObject.AddComponent<NavMeshModifier>();
                modifier.overrideArea = true;
                modifier.area = 1;
                modifier.applyToChildren = false;
                modifier.ignoreFromBuild = false;
                SetModifierToAffectAllAgents(modifier);
                count++;
            }
        }

        return count;
    }

    private int CreateFootprintBlockerVolumes(Dungeon dungeon, Transform parent)
    {
        if (dungeon == null || parent == null)
            return 0;

        DungeonNavMeshFootprintBlocker[] blockers =
            dungeon.GetComponentsInChildren<DungeonNavMeshFootprintBlocker>(true);
        int count = 0;
        for (int i = 0; i < blockers.Length; i++)
        {
            DungeonNavMeshFootprintBlocker blocker = blockers[i];
            if (blocker == null || !blocker.enabled || !blocker.gameObject.activeInHierarchy ||
                !blocker.TryGetColliderBounds(out Bounds bounds))
            {
                continue;
            }

            Tile tile = blocker.GetComponentInParent<Tile>();
            float floorY = tile != null ? EstimateTileFloorY(tile) : bounds.min.y;
            float minY = Mathf.Min(bounds.min.y, floorY - Mathf.Max(0f, blocker.FloorOverlap));
            float maxY = Mathf.Max(bounds.max.y, floorY + 0.1f);
            float horizontalPadding = Mathf.Max(0f, blocker.HorizontalPadding);
            Vector3 size = new Vector3(
                bounds.size.x + horizontalPadding * 2f,
                Mathf.Max(0.1f, maxY - minY),
                bounds.size.z + horizontalPadding * 2f);
            Vector3 center = new Vector3(bounds.center.x, (minY + maxY) * 0.5f, bounds.center.z);

            CreateNotWalkableVolume(
                parent,
                "RuntimeNavMesh_FootprintBlocker_" + blocker.name,
                center,
                Quaternion.identity,
                size);
            count++;
        }

        return count;
    }

    private bool ShouldCreateBlockerVolume(Collider collider, Transform runtimeBakeRoot)
    {
        if (collider == null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy)
            return false;

        if (runtimeBakeRoot != null && collider.transform.IsChildOf(runtimeBakeRoot))
            return false;

        if (collider.GetComponentInParent<Doorway>() != null)
            return false;

        if (collider.GetComponentInParent<DungeonNavMeshArea>() != null)
            return false;

        if (collider.GetComponentInParent<DungeonNavMeshFootprintBlocker>() != null)
            return false;

        if (collider.GetComponentInParent<MonsterDoorLinkBinding>() != null ||
            collider.GetComponentInParent<Door>() != null)
            return false;

        if (collider.GetComponentInParent<DoorInteractable>() != null)
            return false;

        string layerName = LayerMask.LayerToName(collider.gameObject.layer);
        if (string.Equals(layerName, floorLayerName, StringComparison.Ordinal) ||
            string.Equals(layerName, navBakeLayerName, StringComparison.Ordinal) ||
            string.Equals(layerName, "Door", StringComparison.Ordinal) ||
            string.Equals(layerName, "Interactable", StringComparison.Ordinal))
            return false;

        return true;
    }

    private int CreateTileFloorProxies(Dungeon dungeon, Transform parent, RuntimeBakeReport report)
    {
        if (dungeon.AllTiles == null)
            return 0;

        int count = 0;
        foreach (Tile tile in dungeon.AllTiles)
        {
            if (tile == null)
                continue;

            if (HasActiveWalkableArea(tile.GetComponentsInChildren<DungeonNavMeshArea>(true)))
                continue;

            int authoredFloorColliderCount = CountBakeableFloorColliders(tile);
            if (authoredFloorColliderCount > 0)
            {
                if (report != null)
                    report.AuthoredFloorColliderCount += authoredFloorColliderCount;
                continue;
            }

            Bounds bounds = tile.Bounds;
            if (bounds.size.x <= 0.1f || bounds.size.z <= 0.1f)
                continue;

            float floorY = EstimateTileFloorY(tile);
            Vector3 center = new Vector3(bounds.center.x, floorY - proxyFloorThickness * 0.5f, bounds.center.z);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "RuntimeNavMesh_FloorProxy_" + GetTileName(tile);
            floor.transform.SetParent(parent, true);
            floor.transform.position = center;
            floor.transform.rotation = Quaternion.identity;
            floor.transform.localScale = new Vector3(
                Mathf.Max(bounds.size.x, minimumBridgeWidth),
                proxyFloorThickness,
                Mathf.Max(bounds.size.z, minimumBridgeWidth));
            SetLayerRecursively(floor, LayerIndex(GeneratedBakeGeometryLayerName()));
            HideGeneratedBakeRenderers(floor.transform);
            count++;
        }

        return count;
    }

    private int CountBakeableFloorColliders(Tile tile)
    {
        if (tile == null)
            return 0;

        int floorLayer = LayerIndex(floorLayerName);
        Collider[] colliders = tile.GetComponentsInChildren<Collider>(true);
        int count = 0;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger ||
                !collider.gameObject.activeInHierarchy || collider.gameObject.layer != floorLayer)
            {
                continue;
            }

            Bounds bounds = collider.bounds;
            if (bounds.size.x > 0.05f && bounds.size.z > 0.05f)
                count++;
        }

        return count;
    }

    private void ApplyDoorObstacleState(Transform root, RuntimeBakeReport report)
    {
        if (root == null || report == null)
            return;

        report.DoorCount = 0;
        report.DoorBindingCount = 0;
        report.UnboundDoorCount = 0;
        report.DoorObstacleCount = 0;
        report.EnabledDoorObstacleCount = 0;
        report.CarvingDoorObstacleCount = 0;

        MonsterDoorLinkBinding[] bindings = root.GetComponentsInChildren<MonsterDoorLinkBinding>(true);
        for (int i = 0; i < bindings.Length; i++)
        {
            MonsterDoorLinkBinding binding = bindings[i];
            if (binding == null || !binding.gameObject.activeInHierarchy)
                continue;

            binding.RefreshPassageCollidersFromReferences();
            report.DoorCount++;
            if (binding.CanOpen)
                report.DoorBindingCount++;
            else
                report.UnboundDoorCount++;

            NavMeshObstacle[] obstacles = binding.GetComponentsInChildren<NavMeshObstacle>(true);
            for (int obstacleIndex = 0; obstacleIndex < obstacles.Length; obstacleIndex++)
            {
                NavMeshObstacle obstacle = obstacles[obstacleIndex];
                if (obstacle == null)
                    continue;

                report.DoorObstacleCount++;
                obstacle.carving = false;
                obstacle.enabled = false;
                if (obstacle.enabled)
                    report.EnabledDoorObstacleCount++;
                if (obstacle.carving)
                    report.CarvingDoorObstacleCount++;
            }
        }

        report.DoorProbeSource =
            $"continuous portals bindings={report.DoorBindingCount}, unbound={report.UnboundDoorCount}";
    }

    private GameObject CreateDoorObstacleValidationProbeIfNeeded(Dungeon dungeon, RuntimeBakeReport report)
    {
        if (!runDoorPrefabProbeWhenNoDoorInstances || doorObstacleValidationPrefab == null)
            return null;

        if (CountActiveDoors(dungeon.transform) > 0)
            return null;

        Transform parent = dungeon.transform.Find(RuntimeBakeRootName);
        if (parent == null)
            return null;

        Vector3 direction = Vector3.forward;
        Vector3 center = DoorValidationCorridorCenter(dungeon);
        CreateDoorValidationCorridor(parent, center, direction);

        GameObject probe = Instantiate(doorObstacleValidationPrefab, center, Quaternion.LookRotation(direction, Vector3.up));
        probe.name = "RuntimeNavMesh_DoorObstacleValidationProbe";
        probe.transform.SetParent(parent, true);
        DisableNavMeshLinks(probe);

        DoorInteractable door = probe.GetComponentInChildren<DoorInteractable>(true);
        if (door == null)
        {
            Destroy(probe);
            report.DoorProbeSource = "validation prefab has no DoorInteractable";
            return null;
        }

        door.SetNavigationObstacleBlockedForValidation(true);
        HideGeneratedBakeRenderers(probe.transform);
        report.DoorProbeSource = $"validation corridor prefab={doorObstacleValidationPrefab.name}";
        return probe;
    }

    private Vector3 DoorValidationCorridorCenter(Dungeon dungeon)
    {
        Bounds bounds = DungeonBounds(dungeon);
        float floorY = bounds.min.y;
        if (dungeon.AllTiles != null && dungeon.AllTiles.Count > 0 && dungeon.AllTiles[0] != null)
            floorY = EstimateTileFloorY(dungeon.AllTiles[0]);

        return new Vector3(bounds.max.x + doorValidationCorridorOffset, floorY, bounds.center.z);
    }

    private static Bounds DungeonBounds(Dungeon dungeon)
    {
        if (dungeon == null || dungeon.AllTiles == null || dungeon.AllTiles.Count == 0 || dungeon.AllTiles[0] == null)
            return new Bounds(Vector3.zero, Vector3.one);

        Bounds bounds = dungeon.AllTiles[0].Bounds;
        for (int i = 1; i < dungeon.AllTiles.Count; i++)
        {
            Tile tile = dungeon.AllTiles[i];
            if (tile != null)
                bounds.Encapsulate(tile.Bounds);
        }

        return bounds;
    }

    private void CreateDoorValidationCorridor(Transform parent, Vector3 center, Vector3 direction)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "RuntimeNavMesh_DoorObstacleValidationCorridor";
        floor.transform.SetParent(parent, true);
        floor.transform.position = center + Vector3.down * (proxyFloorThickness * 0.5f);
        floor.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        floor.transform.localScale = new Vector3(
            Mathf.Max(0.2f, doorValidationCorridorWidth),
            proxyFloorThickness,
            Mathf.Max(doorPathProbeDistance * 2f + 0.5f, doorValidationCorridorLength));
        SetLayerRecursively(floor, LayerIndex(GeneratedBakeGeometryLayerName()));
        HideGeneratedBakeRenderers(floor.transform);
    }

    private static void DisableNavMeshLinks(GameObject root)
    {
        NavMeshLink[] links = root.GetComponentsInChildren<NavMeshLink>(true);
        for (int i = 0; i < links.Length; i++)
        {
            if (links[i] != null)
                links[i].enabled = false;
        }
    }

    private void ValidateNavigation(Dungeon dungeon, RuntimeBakeReport report)
    {
        report.ActiveNavMeshLinkCount = CountActiveNavMeshLinks(dungeon);

        for (int i = 0; i < _runtimeAgentTypeIds.Count; i++)
            ValidateAgent(dungeon, _runtimeAgentTypeIds[i], AgentLabel(_runtimeAgentTypeIds[i]), report);
    }

    private void ValidateAgent(Dungeon dungeon, int agentTypeId, string label, RuntimeBakeReport report)
    {
        AgentReport agentReport = new AgentReport { Label = label, AgentTypeId = agentTypeId };
        agentReport.MainPathComplete = TryCalculateMainPath(dungeon, agentTypeId, out string mainPathSummary);
        agentReport.MainPathSummary = mainPathSummary;
        agentReport.SegmentsComplete = ValidateMainPathSegments(dungeon, agentTypeId, out string segmentSummary);
        agentReport.SegmentSummary = segmentSummary;
        agentReport.AllRoomsReachable = ValidateAllRoomsReachable(dungeon, agentTypeId, out string allRoomsSummary);
        agentReport.AllRoomsSummary = allRoomsSummary;
        agentReport.AllConnectionsComplete = ValidateAllConnections(dungeon, agentTypeId, out string allConnectionsSummary);
        agentReport.AllConnectionsSummary = allConnectionsSummary;
        agentReport.SpawnProbeOnNavMesh = TryCreateSpawnProbe(dungeon, agentTypeId, out string spawnSummary);
        agentReport.SpawnSummary = spawnSummary;
        bool authoredAreasClear = ValidateAuthoredNotWalkableAreas(
            dungeon,
            agentTypeId,
            out string authoredAreaSummary);
        if (validateUnwantedNavMesh)
        {
            bool exhaustiveClear = ValidateUnwantedNavMesh(dungeon, agentTypeId, out string unwantedSummary);
            agentReport.UnwantedNavMeshClear = authoredAreasClear && exhaustiveClear;
            agentReport.UnwantedNavMeshSummary = authoredAreaSummary + "; exhaustive: " + unwantedSummary;
        }
        else
        {
            agentReport.UnwantedNavMeshClear = authoredAreasClear;
            agentReport.UnwantedNavMeshSummary = authoredAreaSummary + "; exhaustive wall scan disabled";
        }
        report.AgentReports.Add(agentReport);
    }

    private void ValidateContinuousDoorPaths(Transform root, RuntimeBakeReport report)
    {
        if (root == null || report == null)
            return;

        MonsterDoorLinkBinding[] bindings = root.GetComponentsInChildren<MonsterDoorLinkBinding>(true);
        bool complete = true;
        int validated = 0;
        StringBuilder summary = new StringBuilder();

        for (int i = 0; i < bindings.Length; i++)
        {
            MonsterDoorLinkBinding binding = bindings[i];
            if (binding == null || !binding.gameObject.activeInHierarchy || !binding.CanOpen)
                continue;

            validated++;
            for (int agentIndex = 0; agentIndex < _runtimeAgentTypeIds.Count; agentIndex++)
            {
                int agentTypeId = _runtimeAgentTypeIds[agentIndex];
                DoorPathReport result = EvaluateContinuousDoorPath(binding, agentTypeId, AgentLabel(agentTypeId));
                complete &= result.Calculated && result.Status == NavMeshPathStatus.PathComplete;
                if (summary.Length < 1600)
                {
                    if (summary.Length > 0)
                        summary.Append("; ");
                    summary.Append(binding.name).Append('/').Append(result);
                }
            }
        }

        report.DoorPortalPathComplete = validated == 0 ? report.DoorCount == 0 : complete;
        report.DoorPortalPathSummary = validated == 0
            ? "no continuous door portals"
            : $"validated={validated}; {summary}";
    }

    private DoorPathReport EvaluateContinuousDoorPath(
        MonsterDoorLinkBinding binding,
        int agentTypeId,
        string label)
    {
        Vector3[] directions =
        {
            Horizontal(binding.transform.forward),
            Horizontal(binding.transform.right)
        };
        DoorPathReport best = DoorPathReport.Failed(label, "no valid continuous door samples");
        float sampleRadius = Mathf.Min(navSampleRadius, 1.25f);

        for (int i = 0; i < directions.Length; i++)
        {
            Vector3 direction = directions[i];
            if (direction.sqrMagnitude < 0.0001f)
                continue;

            Vector3 startCandidate = binding.transform.position - direction * doorPathProbeDistance;
            Vector3 endCandidate = binding.transform.position + direction * doorPathProbeDistance;
            if (!SampleNavMesh(startCandidate, agentTypeId, out NavMeshHit startHit, sampleRadius) ||
                !SampleNavMesh(endCandidate, agentTypeId, out NavMeshHit endHit, sampleRadius))
            {
                continue;
            }

            NavMeshPath path = new NavMeshPath();
            bool calculated = NavMesh.CalculatePath(startHit.position, endHit.position, Filter(agentTypeId), path);
            DoorPathReport current = new DoorPathReport
            {
                Label = label,
                Calculated = calculated,
                Status = path.status,
                PathDistance = CornerDistance(path),
                DirectDistance = Vector3.Distance(startHit.position, endHit.position),
                Axis = i == 0 ? "forward" : "right",
            };

            if (calculated && current.Status == NavMeshPathStatus.PathComplete)
                return current;

            best = current;
        }

        return best;
    }

    private void ValidateDoorObstacleClosed(Transform root, RuntimeBakeReport report)
    {
        DoorInteractable door = FindDoorProbe(root);
        if (door == null)
        {
            report.ClosedDoorPathSummary = "no door obstacle probe";
            return;
        }

        bool accepted = true;
        StringBuilder summary = new StringBuilder();
        summary.Append("door=").Append(door.name);
        for (int i = 0; i < _runtimeAgentTypeIds.Count; i++)
        {
            DoorPathReport result = EvaluateDoorPath(door, _runtimeAgentTypeIds[i], AgentLabel(_runtimeAgentTypeIds[i]), true);
            accepted &= IsClosedDoorAcceptable(result);
            summary.Append("; ").Append(result);
        }

        report.ClosedDoorBlocksOrDetours = accepted;
        report.ClosedDoorPathSummary = summary.ToString();
    }

    private void ValidateDoorObstacleOpen(DoorInteractable door, RuntimeBakeReport report)
    {
        bool complete = true;
        StringBuilder summary = new StringBuilder();
        summary.Append("door=").Append(door.name);
        for (int i = 0; i < _runtimeAgentTypeIds.Count; i++)
        {
            DoorPathReport result = EvaluateDoorPath(door, _runtimeAgentTypeIds[i], AgentLabel(_runtimeAgentTypeIds[i]), false);
            complete &= result.Status == NavMeshPathStatus.PathComplete;
            summary.Append("; ").Append(result);
        }

        report.OpenDoorPathComplete = complete;
        report.OpenDoorPathSummary = summary.ToString();
    }

    private DoorInteractable FindDoorProbe(Transform root)
    {
        DoorInteractable[] doors = root.GetComponentsInChildren<DoorInteractable>(true);
        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] != null && doors[i].gameObject.activeInHierarchy && doors[i].HasNavigationObstacle)
                return doors[i];
        }

        return null;
    }

    private int CountActiveDoors(Transform root)
    {
        int count = 0;
        DoorInteractable[] doors = root.GetComponentsInChildren<DoorInteractable>(true);
        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] != null && doors[i].gameObject.activeInHierarchy)
                count++;
        }

        return count;
    }

    private bool EvaluateAcceptance(RuntimeBakeReport report)
    {
        if (!validateBeforeMonsterSpawn)
            return true;

        if (report.ActiveNavMeshLinkCount != 0)
            return false;

        if (report.DoorCount > 0)
        {
            if (report.DoorBindingCount < report.DoorCount || report.UnboundDoorCount > 0)
                return false;

            if (report.EnabledDoorObstacleCount > 0 || report.CarvingDoorObstacleCount > 0)
                return false;

            if (!report.DoorPortalPathComplete)
                return false;
        }

        if (report.ConnectionCount > 0 && report.DoorBindingCount < report.ConnectionCount)
            return false;

        for (int i = 0; i < report.AgentReports.Count; i++)
        {
            AgentReport agent = report.AgentReports[i];
            if (!agent.MainPathComplete ||
                !agent.SegmentsComplete ||
                !agent.AllRoomsReachable ||
                !agent.AllConnectionsComplete ||
                !agent.SpawnProbeOnNavMesh ||
                !agent.UnwantedNavMeshClear)
                return false;
        }

        return true;
    }

    private bool ShouldBindMonsterSpawner(RuntimeBakeReport report)
    {
        if (report == null)
            return false;

        if (report.Accepted)
            return true;

        return !blockMonsterSpawnWhenValidationFails;
    }

    private void BindMonsterSpawner(Dungeon dungeon)
    {
        if (monsterSpawner == null)
            return;

        monsterSpawner.ServerBindDungeonRoot(dungeon.transform);
    }

    private bool TryCalculateMainPath(Dungeon dungeon, int agentTypeId, out string summary)
    {
        summary = "main path unavailable";
        if (dungeon.MainPathTiles == null || dungeon.MainPathTiles.Count == 0)
            return false;

        Tile first = dungeon.MainPathTiles[0];
        Tile second = dungeon.MainPathTiles.Count > 1 ? dungeon.MainPathTiles[1] : null;
        Tile last = dungeon.MainPathTiles[dungeon.MainPathTiles.Count - 1];
        Tile beforeLast = dungeon.MainPathTiles.Count > 1 ? dungeon.MainPathTiles[dungeon.MainPathTiles.Count - 2] : null;

        bool startFound = TryFindMainPathEndSample(first, second, agentTypeId, out NavMeshHit start, out string startSample);
        bool endFound = TryFindMainPathEndSample(last, beforeLast, agentTypeId, out NavMeshHit end, out string endSample);
        if (!startFound || !endFound)
        {
            summary = $"sample failed start={startSample}; end={endSample}";
            return false;
        }

        NavMeshPath path = new NavMeshPath();
        bool calculated = NavMesh.CalculatePath(start.position, end.position, Filter(agentTypeId), path);
        float distance = CornerDistance(path);
        summary = $"calculated={calculated}, status={path.status}, corners={path.corners.Length}, distance={distance:F2}, start={startSample}, end={endSample}";
        return calculated && path.status == NavMeshPathStatus.PathComplete;
    }

    private bool ValidateMainPathSegments(Dungeon dungeon, int agentTypeId, out string summary)
    {
        summary = "segments unavailable";
        if (dungeon.MainPathTiles == null || dungeon.MainPathTiles.Count < 2)
            return false;

        StringBuilder builder = new StringBuilder();
        bool allComplete = true;

        for (int i = 0; i < dungeon.MainPathTiles.Count - 1; i++)
        {
            Tile from = dungeon.MainPathTiles[i];
            Tile to = dungeon.MainPathTiles[i + 1];
            Doorway fromDoorway = FindDoorwayToTile(from, to);
            Doorway toDoorway = FindDoorwayToTile(to, from);

            if (i > 0)
                builder.Append(" | ");

            builder.Append(GetTileName(from));
            builder.Append(" -> ");
            builder.Append(GetTileName(to));
            builder.Append(": ");

            if (fromDoorway == null || toDoorway == null)
            {
                builder.Append("missing doorway");
                allComplete = false;
                continue;
            }

            bool fromFound = TryFindNavMeshPointNearDoorway(from, fromDoorway, agentTypeId, out NavMeshHit fromHit, out _);
            bool toFound = TryFindNavMeshPointNearDoorway(to, toDoorway, agentTypeId, out NavMeshHit toHit, out _);
            if (!fromFound || !toFound)
            {
                builder.Append("sample failed");
                allComplete = false;
                continue;
            }

            NavMeshPath path = new NavMeshPath();
            bool calculated = NavMesh.CalculatePath(fromHit.position, toHit.position, Filter(agentTypeId), path);
            builder.Append(calculated ? path.status.ToString() : "CalculatePathFalse");
            builder.Append($", distance={CornerDistance(path):F2}");

            if (!calculated || path.status != NavMeshPathStatus.PathComplete)
                allComplete = false;
        }

        summary = builder.ToString();
        return allComplete;
    }

    private bool ValidateAllRoomsReachable(Dungeon dungeon, int agentTypeId, out string summary)
    {
        summary = "rooms unavailable";
        if (dungeon.AllTiles == null || dungeon.AllTiles.Count == 0)
            return false;

        Tile startTile = dungeon.MainPathTiles != null && dungeon.MainPathTiles.Count > 0
            ? dungeon.MainPathTiles[0]
            : dungeon.AllTiles[0];

        var reachable = new HashSet<Tile>();
        var pending = new Queue<Tile>();
        reachable.Add(startTile);
        pending.Enqueue(startTile);

        while (pending.Count > 0)
        {
            Tile current = pending.Dequeue();
            if (dungeon.Connections == null)
                continue;

            for (int i = 0; i < dungeon.Connections.Count; i++)
            {
                DoorwayConnection connection = dungeon.Connections[i];
                Tile a = connection?.A != null ? connection.A.Tile : null;
                Tile b = connection?.B != null ? connection.B.Tile : null;

                Tile next = null;
                if (a == current)
                    next = b;
                else if (b == current)
                    next = a;

                if (next == null || reachable.Contains(next))
                    continue;

                reachable.Add(next);
                pending.Enqueue(next);
            }
        }

        int sampledCount = 0;
        var graphFailed = new List<string>();
        var sampleFailed = new List<string>();

        for (int i = 0; i < dungeon.AllTiles.Count; i++)
        {
            Tile tile = dungeon.AllTiles[i];

            if (!reachable.Contains(tile))
                graphFailed.Add(GetTileName(tile));

            if (TryFindTileSample(tile, agentTypeId, out _, out string sampleSummary))
                sampledCount++;
            else
                sampleFailed.Add(GetTileName(tile) + " " + sampleSummary);
        }

        summary = $"graphReachable={reachable.Count}/{dungeon.AllTiles.Count}, navSampled={sampledCount}/{dungeon.AllTiles.Count}, start={GetTileName(startTile)}";
        if (graphFailed.Count > 0)
            summary += ", graphFailed=" + string.Join(", ", graphFailed.GetRange(0, Mathf.Min(10, graphFailed.Count)));
        if (sampleFailed.Count > 0)
            summary += ", sampleFailed=" + string.Join(", ", sampleFailed.GetRange(0, Mathf.Min(10, sampleFailed.Count)));

        return graphFailed.Count == 0 && sampleFailed.Count == 0;
    }

    private bool ValidateAllConnections(Dungeon dungeon, int agentTypeId, out string summary)
    {
        summary = "connections unavailable";
        if (dungeon.Connections == null)
            return false;

        int completeCount = 0;
        var failed = new List<string>();

        for (int i = 0; i < dungeon.Connections.Count; i++)
        {
            DoorwayConnection connection = dungeon.Connections[i];
            if (connection?.A == null || connection.B == null)
            {
                failed.Add("connection " + i + " missing doorway");
                continue;
            }

            Tile aTile = connection.A.Tile;
            Tile bTile = connection.B.Tile;
            bool aFound = TryFindNavMeshPointNearDoorway(aTile, connection.A, agentTypeId, out NavMeshHit aHit, out _);
            bool bFound = TryFindNavMeshPointNearDoorway(bTile, connection.B, agentTypeId, out NavMeshHit bHit, out _);

            if (!aFound || !bFound)
            {
                failed.Add($"{GetTileName(aTile)}->{GetTileName(bTile)} sample failed");
                continue;
            }

            NavMeshPath path = new NavMeshPath();
            bool calculated = NavMesh.CalculatePath(aHit.position, bHit.position, Filter(agentTypeId), path);
            if (calculated && path.status == NavMeshPathStatus.PathComplete)
            {
                completeCount++;
                continue;
            }

            failed.Add($"{GetTileName(aTile)}->{GetTileName(bTile)} {path.status}");
        }

        summary = $"complete={completeCount}/{dungeon.Connections.Count}";
        if (failed.Count > 0)
            summary += ", failed=" + string.Join(", ", failed.GetRange(0, Mathf.Min(10, failed.Count)));

        return failed.Count == 0;
    }

    private bool TryCreateSpawnProbe(Dungeon dungeon, int agentTypeId, out string summary)
    {
        summary = "spawn probe unavailable";
        if (dungeon.AllTiles == null)
            return false;

        for (int i = 0; i < dungeon.AllTiles.Count; i++)
        {
            if (!TryFindTileSample(dungeon.AllTiles[i], agentTypeId, out NavMeshHit hit, out string sample))
                continue;

            GameObject probe = new GameObject("RuntimeNavMesh_SpawnProbe_" + agentTypeId);
            probe.transform.position = hit.position;
            // Add the agent while the object is inactive: an active NavMeshAgent is placed on the
            // navmesh of its agent type the moment it is added, and the default type (Humanoid) may
            // have no navmesh here, which logs "Failed to create agent because it is not close enough".
            probe.SetActive(false);
            NavMeshAgent agent = probe.AddComponent<NavMeshAgent>();
            agent.agentTypeID = agentTypeId;
            probe.SetActive(true);
            bool warped = agent.Warp(hit.position);
            bool onNavMesh = agent.isOnNavMesh;
            Destroy(probe);

            summary = $"sample={sample}, warped={warped}, agentOnNavMesh={onNavMesh}";
            return warped && onNavMesh;
        }

        summary = "no tile sample found";
        return false;
    }

    private bool ValidateUnwantedNavMesh(Dungeon dungeon, int agentTypeId, out string summary)
    {
        summary = "unwanted navmesh unavailable";
        if (dungeon == null || dungeon.AllTiles == null || dungeon.AllTiles.Count == 0)
            return false;

        Tile startTile = dungeon.MainPathTiles != null && dungeon.MainPathTiles.Count > 0
            ? dungeon.MainPathTiles[0]
            : dungeon.AllTiles[0];

        if (!TryFindTileSample(startTile, agentTypeId, out NavMeshHit accessibleStart, out string startSample))
        {
            summary = "accessible start sample failed: " + startSample;
            return false;
        }

        int wallColliderCount = 0;
        int wallNavHits = 0;
        int wallCrossings = 0;
        int ceilingNavHits = 0;
        int floorSampleCount = 0;
        int disconnectedFloorSamples = 0;
        int generatedTileInstances = 0;
        HashSet<string> generatedTileNames = new HashSet<string>();
        List<string> examples = new List<string>();

        for (int i = 0; i < dungeon.AllTiles.Count; i++)
        {
            Tile tile = dungeon.AllTiles[i];
            if (tile == null)
                continue;

            generatedTileInstances++;
            generatedTileNames.Add(NormalizeTileName(GetTileName(tile)));
            ValidateTileBlockers(dungeon, tile, agentTypeId, ref wallColliderCount, ref wallNavHits, ref wallCrossings, ref ceilingNavHits, examples);
            ValidateReachableFloorSamples(tile, agentTypeId, accessibleStart.position, ref floorSampleCount, ref disconnectedFloorSamples, examples);
        }

        string generatedTiles = generatedTileNames.Count > 0
            ? string.Join("/", generatedTileNames.OrderBy(name => name).Take(12))
            : "none";
        if (generatedTileNames.Count > 12)
            generatedTiles += "/+" + (generatedTileNames.Count - 12);

        summary =
            $"start={startSample}, generatedTileInstances={generatedTileInstances}/{dungeon.AllTiles.Count}, generatedTilePrefabs={generatedTileNames.Count} [{generatedTiles}], wallColliders={wallColliderCount}, wallNavHits={wallNavHits}, wallCrossings={wallCrossings}, ceilingNavHits={ceilingNavHits}, floorSamples={floorSampleCount}, disconnectedFloorSamples={disconnectedFloorSamples}";

        if (examples.Count > 0)
            summary += ", examples=" + string.Join(" | ", examples.GetRange(0, Mathf.Min(8, examples.Count)));

        return wallNavHits == 0 && wallCrossings == 0 && ceilingNavHits == 0;
    }

    private bool ValidateAuthoredNotWalkableAreas(Dungeon dungeon, int agentTypeId, out string summary)
    {
        summary = "authored Not Walkable validation unavailable";
        if (dungeon == null || dungeon.AllTiles == null)
            return false;

        int authoredAreaCount = 0;
        int checkedVolumeCount = 0;
        int sampleCount = 0;
        int violatingVolumeCount = 0;
        var examples = new List<string>();

        for (int tileIndex = 0; tileIndex < dungeon.AllTiles.Count; tileIndex++)
        {
            Tile tile = dungeon.AllTiles[tileIndex];
            if (tile == null)
                continue;

            DungeonNavMeshArea[] areas = tile.GetComponentsInChildren<DungeonNavMeshArea>(true);
            for (int areaIndex = 0; areaIndex < areas.Length; areaIndex++)
            {
                DungeonNavMeshArea area = areas[areaIndex];
                if (!IsActiveArea(area) || !area.IsNotWalkable)
                    continue;

                authoredAreaCount++;
                if (area.Shape == DungeonNavMeshArea.ShapeType.Box)
                {
                    Vector3 size = PositiveSize(Vector3.Scale(area.Size, Abs(area.transform.lossyScale)));
                    checkedVolumeCount++;
                    if (!ValidateNoNavMeshInsideVolume(
                            area.transform.TransformPoint(area.Center),
                            area.transform.rotation,
                            size,
                            agentTypeId,
                            ref sampleCount,
                            out NavMeshHit hit))
                    {
                        violatingVolumeCount++;
                        AddExample(examples, $"{GetTileName(tile)}/{area.name} nav={FormatVector(hit.position)}");
                    }

                    continue;
                }

                var colliders = new List<Collider>();
                area.GetEnabledAttachedColliders(colliders);
                for (int colliderIndex = 0; colliderIndex < colliders.Count; colliderIndex++)
                {
                    Collider collider = colliders[colliderIndex];
                    Bounds bounds = collider.bounds;
                    Vector3 size = PositiveSize(bounds.size);
                    checkedVolumeCount++;
                    if (!ValidateNoNavMeshInsideVolume(
                            bounds.center,
                            Quaternion.identity,
                            size,
                            agentTypeId,
                            ref sampleCount,
                            out NavMeshHit hit))
                    {
                        violatingVolumeCount++;
                        AddExample(examples, $"{GetTileName(tile)}/{area.name}/{collider.name} nav={FormatVector(hit.position)}");
                    }
                }
            }
        }

        summary =
            $"authoredNotWalkable areas={authoredAreaCount}, volumes={checkedVolumeCount}, samples={sampleCount}, violations={violatingVolumeCount}";
        if (examples.Count > 0)
            summary += ", examples=" + string.Join(" | ", examples.GetRange(0, Mathf.Min(8, examples.Count)));

        return violatingVolumeCount == 0;
    }

    private bool ValidateNoNavMeshInsideVolume(
        Vector3 worldCenter,
        Quaternion worldRotation,
        Vector3 worldSize,
        int agentTypeId,
        ref int sampleCount,
        out NavMeshHit violatingHit)
    {
        Vector3 halfSize = PositiveSize(worldSize) * 0.5f;
        float[] horizontalSamples = { -0.6f, 0f, 0.6f };
        float sampleRadius = Mathf.Max(navSampleRadius, halfSize.y + 0.5f);

        for (int xIndex = 0; xIndex < horizontalSamples.Length; xIndex++)
        {
            for (int zIndex = 0; zIndex < horizontalSamples.Length; zIndex++)
            {
                Vector3 localOffset = new Vector3(
                    horizontalSamples[xIndex] * halfSize.x,
                    0f,
                    horizontalSamples[zIndex] * halfSize.z);
                Vector3 candidate = worldCenter + worldRotation * localOffset;
                sampleCount++;
                if (!SampleNavMesh(candidate, agentTypeId, out NavMeshHit hit, sampleRadius))
                    continue;

                Vector3 localHit = Quaternion.Inverse(worldRotation) * (hit.position - worldCenter);
                const float tolerance = 0.02f;
                if (Mathf.Abs(localHit.x) <= halfSize.x + tolerance &&
                    Mathf.Abs(localHit.y) <= halfSize.y + tolerance &&
                    Mathf.Abs(localHit.z) <= halfSize.z + tolerance)
                {
                    violatingHit = hit;
                    return false;
                }
            }
        }

        violatingHit = default;
        return true;
    }

    private static string NormalizeTileName(string tileName)
    {
        if (string.IsNullOrWhiteSpace(tileName))
            return "unnamed";

        return tileName.Replace("(Clone)", string.Empty).Trim();
    }

    private void ValidateTileBlockers(
        Dungeon dungeon,
        Tile tile,
        int agentTypeId,
        ref int wallColliderCount,
        ref int wallNavHits,
        ref int wallCrossings,
        ref int ceilingNavHits,
        List<string> examples)
    {
        Collider[] colliders = tile.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy)
                continue;

            bool wallLike = IsWallLike(collider);
            bool ceilingLike = IsCeilingLike(collider);
            DungeonNavMeshArea authoredArea = collider.GetComponent<DungeonNavMeshArea>();
            if (ceilingLike &&
                authoredArea != null &&
                authoredArea.IncludeInRuntimeBake &&
                authoredArea.IsWalkable)
            {
                // Some multi-level room floor meshes retain source names such as
                // "Ceiling_Floor_Uncapped". Explicit prefab authoring wins over
                // the fallback name heuristic; real Ceils remain validated.
                ceilingLike = false;
            }
            if (!wallLike && !ceilingLike)
                continue;

            Bounds bounds = collider.bounds;
            if (wallLike)
            {
                wallColliderCount++;
                if (collider.name.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                Vector3 interiorSample = new Vector3(bounds.center.x, bounds.min.y + 0.08f, bounds.center.z);
                if (SampleNavMesh(interiorSample, agentTypeId, out NavMeshHit hit, 0.2f))
                {
                    wallNavHits++;
                    AddExample(examples, $"wallNav {GetTileName(tile)}/{collider.name} nav={FormatVector(hit.position)} dist={hit.distance:F2}");

                    if (HasShortPathThroughCollider(collider, interiorSample, agentTypeId))
                    {
                        wallCrossings++;
                        AddExample(examples, $"wallCross {GetTileName(tile)}/{collider.name}");
                    }
                }
            }

            if (ceilingLike)
            {
                Vector3 ceilingSample = new Vector3(bounds.center.x, bounds.max.y + 0.05f, bounds.center.z);
                if (SampleNavMesh(ceilingSample, agentTypeId, out NavMeshHit hit, 0.25f))
                {
                    ceilingNavHits++;
                    AddExample(examples, $"ceilingNav {GetTileName(tile)}/{collider.name} nav={FormatVector(hit.position)} dist={hit.distance:F2}");
                }
            }
        }
    }

    private void ValidateReachableFloorSamples(
        Tile tile,
        int agentTypeId,
        Vector3 accessibleStart,
        ref int floorSampleCount,
        ref int disconnectedFloorSamples,
        List<string> examples)
    {
        Bounds bounds = tile.Bounds;
        float floorY = EstimateTileFloorY(tile) + 0.08f;
        float[] fractions = { 0.25f, 0.5f, 0.75f };

        for (int x = 0; x < fractions.Length; x++)
        {
            for (int z = 0; z < fractions.Length; z++)
            {
                Vector3 candidate = new Vector3(
                    Mathf.Lerp(bounds.min.x, bounds.max.x, fractions[x]),
                    floorY,
                    Mathf.Lerp(bounds.min.z, bounds.max.z, fractions[z]));

                if (!SampleNavMesh(candidate, agentTypeId, out NavMeshHit sample, 0.4f))
                    continue;

                floorSampleCount++;
                NavMeshPath path = new NavMeshPath();
                bool calculated = NavMesh.CalculatePath(accessibleStart, sample.position, Filter(agentTypeId), path);
                if (calculated && path.status == NavMeshPathStatus.PathComplete)
                    continue;

                disconnectedFloorSamples++;
                AddExample(examples, $"disconnected {GetTileName(tile)} nav={FormatVector(sample.position)} status={(calculated ? path.status.ToString() : "CalculatePathFalse")}");
            }
        }
    }

    private bool HasShortPathThroughCollider(Collider collider, Vector3 center, int agentTypeId)
    {
        Bounds bounds = collider.bounds;
        Vector3 normal = bounds.extents.x < bounds.extents.z ? Vector3.right : Vector3.forward;
        float halfThickness = Mathf.Min(bounds.extents.x, bounds.extents.z);
        Vector3 a = center - normal * (halfThickness + 0.65f);
        Vector3 b = center + normal * (halfThickness + 0.65f);

        if (!Physics.Linecast(a + Vector3.up * 0.2f, b + Vector3.up * 0.2f, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            return false;

        if (hit.collider != collider)
            return false;

        if (!SampleNavMesh(a, agentTypeId, out NavMeshHit aHit, 1f) ||
            !SampleNavMesh(b, agentTypeId, out NavMeshHit bHit, 1f))
            return false;

        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.CalculatePath(aHit.position, bHit.position, Filter(agentTypeId), path) ||
            path.status != NavMeshPathStatus.PathComplete)
            return false;

        float direct = Vector3.Distance(aHit.position, bHit.position);
        return direct > 0.01f && CornerDistance(path) <= direct * 1.4f;
    }

    private bool IsWallLike(Collider collider)
    {
        if (collider == null)
            return false;

        string layerName = LayerMask.LayerToName(collider.gameObject.layer);
        if (string.Equals(layerName, "Walls", StringComparison.Ordinal))
            return true;

        return collider.name.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsCeilingLike(Collider collider)
    {
        if (collider == null)
            return false;

        return collider.name.IndexOf("Ceil", StringComparison.OrdinalIgnoreCase) >= 0 ||
               collider.name.IndexOf("Upper", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void AddExample(List<string> examples, string example)
    {
        if (examples == null || examples.Count >= 12 || string.IsNullOrWhiteSpace(example))
            return;

        examples.Add(example);
    }

    private DoorPathReport EvaluateDoorPath(DoorInteractable door, int agentTypeId, string label, bool preferBlockedOrDetoured)
    {
        Vector3[] directions =
        {
            Horizontal(door.transform.right),
            Horizontal(door.transform.forward)
        };

        DoorPathReport best = DoorPathReport.Failed(label, "no valid door samples");
        for (int i = 0; i < directions.Length; i++)
        {
            Vector3 direction = directions[i];
            if (direction.sqrMagnitude < 0.0001f)
                continue;

            Vector3 startCandidate = door.transform.position - direction * doorPathProbeDistance;
            Vector3 endCandidate = door.transform.position + direction * doorPathProbeDistance;

            if (!SampleNavMesh(startCandidate, agentTypeId, out NavMeshHit startHit, navSampleRadius) ||
                !SampleNavMesh(endCandidate, agentTypeId, out NavMeshHit endHit, navSampleRadius))
                continue;

            NavMeshPath path = new NavMeshPath();
            bool calculated = NavMesh.CalculatePath(startHit.position, endHit.position, Filter(agentTypeId), path);
            float distance = CornerDistance(path);
            float direct = Vector3.Distance(startHit.position, endHit.position);
            DoorPathReport current = new DoorPathReport
            {
                Label = label,
                Calculated = calculated,
                Status = path.status,
                PathDistance = distance,
                DirectDistance = direct,
                Axis = i == 0 ? "right" : "forward",
            };

            if (preferBlockedOrDetoured && IsClosedDoorAcceptable(current))
                return current;

            if (!preferBlockedOrDetoured && current.Status == NavMeshPathStatus.PathComplete)
                return current;

            best = current;
        }

        return best;
    }

    private bool IsClosedDoorAcceptable(DoorPathReport report)
    {
        if (!report.Calculated || report.Status != NavMeshPathStatus.PathComplete)
            return true;

        if (report.DirectDistance <= 0.01f)
            return false;

        return report.PathDistance >= report.DirectDistance * closedDoorDetourMultiplier;
    }

    private bool TryFindMainPathEndSample(Tile tile, Tile connectedTile, int agentTypeId, out NavMeshHit hit, out string sample)
    {
        if (tile != null && connectedTile != null)
        {
            Doorway doorway = FindDoorwayToTile(tile, connectedTile);
            if (doorway != null && TryFindNavMeshPointNearDoorway(tile, doorway, agentTypeId, out hit, out sample))
            {
                sample = $"{GetTileName(tile)}/mainDoorway {sample}";
                return true;
            }
        }

        return TryFindTileSample(tile, agentTypeId, out hit, out sample);
    }

    private bool TryFindTileSample(Tile tile, int agentTypeId, out NavMeshHit hit, out string sample)
    {
        if (tile == null)
        {
            hit = default;
            sample = "tile null";
            return false;
        }

        Bounds bounds = tile.Bounds;
        Vector3 center = bounds.center;
        center.y = EstimateTileFloorY(tile);

        if (tile.UsedDoorways != null)
        {
            foreach (Doorway doorway in tile.UsedDoorways)
            {
                if (doorway == null)
                    continue;

                foreach (Vector3 candidate in GetInteriorDoorwayCandidates(tile, doorway))
                {
                    if (SampleNavMesh(candidate, agentTypeId, out hit, navSampleRadius))
                    {
                        sample = $"{GetTileName(tile)}/doorway {doorway.name} nav={FormatVector(hit.position)}";
                        return true;
                    }
                }
            }
        }

        Vector3[] candidates =
        {
            center,
            center + new Vector3(bounds.extents.x * 0.4f, 0f, 0f),
            center - new Vector3(bounds.extents.x * 0.4f, 0f, 0f),
            center + new Vector3(0f, 0f, bounds.extents.z * 0.4f),
            center - new Vector3(0f, 0f, bounds.extents.z * 0.4f),
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            if (SampleNavMesh(candidates[i], agentTypeId, out hit, navSampleRadius))
            {
                sample = $"{GetTileName(tile)}[{i}] nav={FormatVector(hit.position)}";
                return true;
            }
        }

        hit = default;
        sample = $"{GetTileName(tile)} no navmesh sample";
        return false;
    }

    private bool TryFindNavMeshPointNearDoorway(Tile tile, Doorway doorway, int agentTypeId, out NavMeshHit hit, out string sample)
    {
        foreach (Vector3 candidate in GetInteriorDoorwayCandidates(tile, doorway))
        {
            if (SampleNavMesh(candidate, agentTypeId, out hit, navSampleRadius))
            {
                sample = $"{doorway.name} nav={FormatVector(hit.position)}";
                return true;
            }
        }

        hit = default;
        sample = $"{doorway.name} no navmesh sample";
        return false;
    }

    private IEnumerable<Vector3> GetInteriorDoorwayCandidates(Tile tile, Doorway doorway)
    {
        Vector3 position = doorway.transform.position;
        Vector3 forward = Horizontal(doorway.transform.forward);
        Vector3[] directions = { forward, -forward };
        float[] offsets = { 0f, 0.4f, 0.8f, 1.2f, 1.8f, 2.5f, 3.5f };

        foreach (Vector3 direction in directions)
        {
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 candidate = position + direction * offsets[i];
                if (offsets[i] <= 0f || IsPointInsideTileXZ(tile, candidate, 0.25f))
                    yield return candidate;
            }
        }
    }

    private static Doorway FindDoorwayToTile(Tile from, Tile to)
    {
        if (from == null || to == null || from.UsedDoorways == null)
            return null;

        foreach (Doorway doorway in from.UsedDoorways)
        {
            if (doorway != null && doorway.ConnectedDoorway != null && doorway.ConnectedDoorway.Tile == to)
                return doorway;
        }

        return null;
    }

    private bool SampleNavMesh(Vector3 position, int agentTypeId, out NavMeshHit hit, float maxDistance)
    {
        return NavMesh.SamplePosition(position, out hit, maxDistance, Filter(agentTypeId));
    }

    private static NavMeshQueryFilter Filter(int agentTypeId)
    {
        return new NavMeshQueryFilter
        {
            agentTypeID = agentTypeId,
            areaMask = NavMesh.AllAreas,
        };
    }

    private static string AgentLabel(int agentTypeId)
    {
        if (agentTypeId == SmilyAgentTypeId)
            return "Smily";

        if (agentTypeId == ClownAgentTypeId)
            return "Clown";

        return "Agent" + agentTypeId;
    }

    private static string BuildAgentTypeSummary(List<int> agentTypeIds)
    {
        if (agentTypeIds == null || agentTypeIds.Count == 0)
            return "none";

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < agentTypeIds.Count; i++)
        {
            if (i > 0)
                builder.Append(", ");

            builder.Append(AgentLabel(agentTypeIds[i])).Append("=").Append(agentTypeIds[i]);
        }

        return builder.ToString();
    }

    private int CountActiveNavMeshLinks(Dungeon dungeon)
    {
        int count = 0;
        NavMeshLink[] links = dungeon.GetComponentsInChildren<NavMeshLink>(true);
        for (int i = 0; i < links.Length; i++)
        {
            if (links[i] != null && links[i].enabled && links[i].gameObject.activeInHierarchy)
                count++;
        }

        return count;
    }

    private static bool IsPointInsideTileXZ(Tile tile, Vector3 point, float padding)
    {
        Bounds bounds = tile.Bounds;
        return point.x >= bounds.min.x - padding &&
               point.x <= bounds.max.x + padding &&
               point.z >= bounds.min.z - padding &&
               point.z <= bounds.max.z + padding;
    }

    private float EstimateTileFloorY(Tile tile)
    {
        Bounds bounds = tile.Bounds;
        float best = bounds.min.y;
        Renderer[] renderers = tile.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !IsFloorLikeLayer(renderer.gameObject.layer))
                continue;

            best = Mathf.Max(best, renderer.bounds.min.y);
        }

        return best;
    }

    private float EstimateDoorwayFloorY(Vector3 aPosition, Vector3 bPosition)
    {
        Vector3 center = (aPosition + bPosition) * 0.5f;
        if (Physics.Raycast(center + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, LayerMaskFor(floorLayerName)))
            return hit.point.y;

        return Mathf.Min(aPosition.y, bPosition.y);
    }

    private bool IsFloorLikeLayer(int layer)
    {
        return layer == LayerIndex(floorLayerName) || layer == LayerIndex(navBakeLayerName);
    }

    private string GeneratedBakeGeometryLayerName()
    {
        return bakeGeometryMode == BakeGeometryMode.ProxyFloorsAndDoorwayBridges
            ? navBakeLayerName
            : floorLayerName;
    }

    private int RuntimeBakeLayerMask()
    {
        return _useNavBakeOnlyForCurrentRun
            ? LayerMaskFor(navBakeLayerName)
            : LayerMaskFor(meshBakeLayerNames);
    }

    private float MinSocketWidth(Doorway a, Doorway b)
    {
        return Mathf.Max(a.Socket.Size.x, b.Socket.Size.x, minimumBridgeWidth);
    }

    private int LayerIndex(string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        return layer >= 0 ? layer : 0;
    }

    private int LayerMaskFor(string layerName)
    {
        int layer = LayerIndex(layerName);
        return 1 << layer;
    }

    private int LayerMaskFor(string[] layerNames)
    {
        int mask = 0;
        if (layerNames == null || layerNames.Length == 0)
            return LayerMaskFor(floorLayerName);

        for (int i = 0; i < layerNames.Length; i++)
        {
            string layerName = layerNames[i];
            if (string.IsNullOrWhiteSpace(layerName))
                continue;

            mask |= LayerMaskFor(layerName);
        }

        return mask != 0 ? mask : LayerMaskFor(floorLayerName);
    }

    private static Vector3 Horizontal(Vector3 value)
    {
        value.y = 0f;
        if (value.sqrMagnitude > 0.0001f)
            value.Normalize();
        return value;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (Transform child in root.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private static void HideGeneratedBakeRenderers(Transform root)
    {
        if (root == null)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.forceRenderingOff = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    private static float CornerDistance(NavMeshPath path)
    {
        if (path == null || path.corners == null || path.corners.Length < 2)
            return 0f;

        float distance = 0f;
        for (int i = 1; i < path.corners.Length; i++)
            distance += Vector3.Distance(path.corners[i - 1], path.corners[i]);

        return distance;
    }

    private static string GetTileName(Tile tile)
    {
        return tile != null ? tile.gameObject.name : "NULL";
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }

    private static string InternalLinkBridgeKey(Vector3 start, Vector3 end, float width)
    {
        string a = PointKey(start);
        string b = PointKey(end);
        if (string.CompareOrdinal(a, b) > 0)
            (a, b) = (b, a);

        return $"{a}|{b}|{Mathf.RoundToInt(width * 100f)}";
    }

    private static string PointKey(Vector3 value)
    {
        return $"{Mathf.RoundToInt(value.x * 100f)},{Mathf.RoundToInt(value.y * 100f)},{Mathf.RoundToInt(value.z * 100f)}";
    }

    private static void AppendNote(RuntimeBakeReport report, string note)
    {
        if (report == null || string.IsNullOrWhiteSpace(note))
            return;

        if (string.IsNullOrWhiteSpace(report.Notes))
            report.Notes = note;
        else
            report.Notes += "; " + note;
    }

    private void WriteReport(string report)
    {
        if (!writeReportFile || string.IsNullOrWhiteSpace(reportPath))
            return;

        if (!TryResolveSafeReportPath(reportPath, out string safeReportPath, out string rejectionReason))
        {
            Debug.LogError($"REPORT_PATH_REJECTED|path={reportPath}|reason={rejectionReason}", this);
            return;
        }

        string directory = Path.GetDirectoryName(safeReportPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(safeReportPath, report, Encoding.UTF8);
    }

    internal static bool TryResolveSafeReportPath(
        string configuredPath,
        out string safeReportPath,
        out string rejectionReason)
    {
        safeReportPath = string.Empty;
        rejectionReason = string.Empty;

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            rejectionReason = "EMPTY";
            return false;
        }

        try
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string evidenceRoot = Path.GetFullPath(Path.Combine(projectRoot, ".omo", "evidence"));
            string candidate = Path.IsPathRooted(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(Path.Combine(projectRoot, configuredPath));
            StringComparison pathComparison = Application.platform == RuntimePlatform.WindowsEditor
                || Application.platform == RuntimePlatform.WindowsPlayer
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
            string evidencePrefix = evidenceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(evidencePrefix, pathComparison))
            {
                rejectionReason = "OUTSIDE_EVIDENCE";
                return false;
            }

            string protectedReport = Path.GetFullPath(
                Path.Combine(projectRoot, "Assets", "testnavmesh", "StartMapRuntimeBakeProductionReport.txt"));
            if (string.Equals(candidate, protectedReport, pathComparison))
            {
                rejectionReason = "PROTECTED_REPORT";
                return false;
            }

            if (HasReparsePointInExistingPath(projectRoot, candidate))
            {
                rejectionReason = "REPARSE_POINT";
                return false;
            }

            safeReportPath = candidate;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            || exception is IOException
            || exception is NotSupportedException
            || exception is UnauthorizedAccessException)
        {
            rejectionReason = exception.GetType().Name;
            return false;
        }
    }

    private static bool HasReparsePointInExistingPath(string projectRoot, string candidatePath)
    {
        string current = Path.GetFullPath(projectRoot);
        string relative = Path.GetRelativePath(current, candidatePath);
        string[] segments = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < segments.Length; i++)
        {
            current = Path.Combine(current, segments[i]);
            if (!Directory.Exists(current) && !File.Exists(current))
                continue;

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return true;
        }

        return false;
    }

    private static string BuildReportText(RuntimeBakeReport report)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("StartMap DunGen Runtime NavMesh Validation Report");
        builder.AppendLine($"generated seed={report.Seed}, flowIndex={report.FlowIndex}, rooms={report.RoomCount}, connections={report.ConnectionCount}");
        builder.AppendLine($"bake provider={report.BakeProvider}, rootSurfaces={report.RootSurfaceCount}, disabledChildSurfaces={report.DisabledChildSurfaceCount}");
        builder.AppendLine($"geometry={report.BakeGeometry}, layerMask={report.LayerMask}, agents={report.AgentTypeSummary}, failClosed={report.BlockMonsterSpawnWhenValidationFails}");
        builder.AppendLine($"links active={report.ActiveNavMeshLinkCount}, authored={report.AuthoredNavMeshLinkCount}, doorwayBridges={report.DoorwayBridgeCount}, internalLinkBridges={report.InternalLinkBridgeCount}, authoredBridgeProxies={report.AuthoredBridgeProxyCount}, authoredFloorColliders={report.AuthoredFloorColliderCount}, fallbackProxyFloors={report.ProxyFloorCount}, blockerGeometry={report.BlockerVolumeCount}");
        builder.AppendLine($"simpleAreas explicitTiles={report.ExplicitAreaTileCount}, walkable={report.WalkableAreaCount}, walkableProxies={report.WalkableAreaProxyCount}, notWalkable={report.NotWalkableAreaCount}, ignoredLegacyFloorObjects={report.IgnoredLegacyFloorObjectCount}");
        builder.AppendLine($"doors total={report.DoorCount}, bindings={report.DoorBindingCount}, unbound={report.UnboundDoorCount}, obstacle={report.DoorObstacleCount}, enabled={report.EnabledDoorObstacleCount}, carving={report.CarvingDoorObstacleCount}");
        builder.AppendLine($"door probe source={report.DoorProbeSource}");
        builder.AppendLine($"continuous door paths complete={report.DoorPortalPathComplete}: {report.DoorPortalPathSummary}");

        for (int i = 0; i < report.AgentReports.Count; i++)
        {
            AgentReport agent = report.AgentReports[i];
            builder.AppendLine($"{agent.Label} agentType={agent.AgentTypeId}");
            builder.AppendLine($"  mainPath={agent.MainPathComplete}: {agent.MainPathSummary}");
            builder.AppendLine($"  segments={agent.SegmentsComplete}: {agent.SegmentSummary}");
            builder.AppendLine($"  allRooms={agent.AllRoomsReachable}: {agent.AllRoomsSummary}");
            builder.AppendLine($"  allConnections={agent.AllConnectionsComplete}: {agent.AllConnectionsSummary}");
            builder.AppendLine($"  spawnProbe={agent.SpawnProbeOnNavMesh}: {agent.SpawnSummary}");
            builder.AppendLine($"  unwantedNavMeshClear={agent.UnwantedNavMeshClear}: {agent.UnwantedNavMeshSummary}");
        }

        if (!string.IsNullOrWhiteSpace(report.Notes))
            builder.AppendLine("notes=" + report.Notes);

        builder.AppendLine("accepted=" + report.Accepted);
        return builder.ToString();
    }

    private sealed class RuntimeBakeReport
    {
        public int Seed;
        public int FlowIndex;
        public int RoomCount;
        public int ConnectionCount;
        public string BakeProvider;
        public string BakeGeometry;
        public int LayerMask;
        public string AgentTypeSummary;
        public bool BlockMonsterSpawnWhenValidationFails;
        public int RootSurfaceCount;
        public int DisabledChildSurfaceCount;
        public int AuthoredNavMeshLinkCount;
        public int ActiveNavMeshLinkCount;
        public int DoorwayBridgeCount;
        public int InternalLinkBridgeCount;
        public int AuthoredBridgeProxyCount;
        public int AuthoredFloorColliderCount;
        public int ProxyFloorCount;
        public int BlockerVolumeCount;
        public int ExplicitAreaTileCount;
        public int WalkableAreaCount;
        public int WalkableAreaProxyCount;
        public int NotWalkableAreaCount;
        public int IgnoredLegacyFloorObjectCount;
        public int DoorCount;
        public int DoorBindingCount;
        public int UnboundDoorCount;
        public int DoorObstacleCount;
        public int EnabledDoorObstacleCount;
        public int CarvingDoorObstacleCount;
        public string DoorProbeSource = "generated door instance";
        public bool ClosedDoorBlocksOrDetours;
        public bool OpenDoorPathComplete;
        public string ClosedDoorPathSummary;
        public string OpenDoorPathSummary;
        public bool DoorPortalPathComplete;
        public string DoorPortalPathSummary;
        public string Notes;
        public bool Accepted;
        public readonly List<AgentReport> AgentReports = new List<AgentReport>();
    }

    private struct AuthoredAreaBakeStats
    {
        public int ExplicitTileCount;
        public int WalkableAreaCount;
        public int WalkableProxyCount;
        public int NotWalkableAreaCount;
        public int NotWalkableVolumeCount;
        public int IgnoredLegacyFloorObjectCount;
    }

    private sealed class AgentReport
    {
        public string Label;
        public int AgentTypeId;
        public bool MainPathComplete;
        public string MainPathSummary;
        public bool SegmentsComplete;
        public string SegmentSummary;
        public bool AllRoomsReachable;
        public string AllRoomsSummary;
        public bool AllConnectionsComplete;
        public string AllConnectionsSummary;
        public bool SpawnProbeOnNavMesh;
        public string SpawnSummary;
        public bool UnwantedNavMeshClear;
        public string UnwantedNavMeshSummary;
    }

    private struct DoorPathReport
    {
        public string Label;
        public bool Calculated;
        public NavMeshPathStatus Status;
        public float PathDistance;
        public float DirectDistance;
        public string Axis;
        public string Failure;

        public static DoorPathReport Failed(string label, string failure)
        {
            return new DoorPathReport
            {
                Label = label,
                Calculated = false,
                Status = NavMeshPathStatus.PathInvalid,
                Failure = failure,
            };
        }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(Failure))
                return $"{Label}=failed({Failure})";

            return $"{Label}=calculated:{Calculated}, status:{Status}, axis:{Axis}, direct:{DirectDistance:F2}, path:{PathDistance:F2}";
        }
    }
}
