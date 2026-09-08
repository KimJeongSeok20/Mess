using System;
using System.Collections.Generic;
using DunGen;
using DungeonRoomLocalLightShare;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class DungeonTileProbeRegistry : MonoBehaviour
{
    private const int BlendProbeCount = 4;

    [SerializeField] private RuntimeDungeon runtimeDungeon;
    [SerializeField] private bool registerPostProcessStep = true;
    [SerializeField] private int postProcessPriority = 90;
    [SerializeField] private bool logSummary;

    [Header("Probe Visibility")]
    [SerializeField] private bool visibilityFilteringEnabled = true;
    [SerializeField, Range(4, 128)] private int visibilityCandidateCount = 24;

    [Header("Portal Direct SH")]
    [Tooltip("Every enabled DungeonDynamicProbeReceiver (player, monsters, items and receivers added at runtime) " +
             "adds the incoming door connection as SH for renderers that do not receive the projected cookie. " +
             "A receiver's own Receive Portal Direct SH flag still opts that single object in while this is off.")]
    [SerializeField] private bool dynamicReceiversReceivePortalDirectSH;

    [Header("Doorway Spatial Blend")]
    [SerializeField] private bool spatialBlendEnabled = true;
    [SerializeField, Min(0.05f)] private float doorwayBlendHalfDepth = 1.35f;
    [SerializeField, Min(0f)] private float doorwayLateralPadding = 0.75f;

    [Header("Generated Doors")]
    [SerializeField] private bool attachGeneratedDoorReceivers = true;
    [SerializeField] private bool logGeneratedDoorReceiverSummary;

    private readonly List<TileProbeSet> _sets = new List<TileProbeSet>();
    private readonly List<DoorwayBlendZone> _doorwayBlendZones = new List<DoorwayBlendZone>();
    private readonly HashSet<RoomLocalConnection> _sampledPortalConnections = new HashSet<RoomLocalConnection>();
    private readonly Dictionary<DungeonTileLightmapSwitcher, TileProbeSet> _setsBySwitcher =
        new Dictionary<DungeonTileLightmapSwitcher, TileProbeSet>();
    private readonly Dictionary<DungeonTileLightmapSwitcher, Action<DungeonTileLightmapSwitcher.PowerLevel>> _powerHandlers =
        new Dictionary<DungeonTileLightmapSwitcher, Action<DungeonTileLightmapSwitcher.PowerLevel>>();

    private readonly float[] _bestDistances = new float[128];
    private readonly int[] _bestIndices = new int[128];
    private int _nearestBufferCount;

    private bool _postProcessRegistered;

    public static DungeonTileProbeRegistry Active { get; private set; }

    public event Action SpatialBlendSettingsChanged;

    public int TileSetCount => _sets.Count;
    public int DoorwayBlendZoneCount => _doorwayBlendZones.Count;
    public bool SpatialBlendEnabled => spatialBlendEnabled;
    public bool VisibilityFilteringEnabled => visibilityFilteringEnabled;
    public bool DynamicReceiversReceivePortalDirectSH => dynamicReceiversReceivePortalDirectSH;
    public float DoorwayBlendHalfDepth => doorwayBlendHalfDepth;
    public float DoorwayLateralPadding => doorwayLateralPadding;

    private void Awake()
    {
        if (Active == null)
            Active = this;

        TryRegisterPostProcess();
    }

    private void OnEnable()
    {
        if (Active == null)
            Active = this;

        TryRegisterPostProcess();
    }

    private void OnDisable()
    {
        UnregisterPostProcess();
        ClearRegistry();

        if (Active == this)
            Active = null;
    }

    public void Configure(RuntimeDungeon dungeon)
    {
        if (runtimeDungeon != dungeon)
        {
            UnregisterPostProcess();
            runtimeDungeon = dungeon;
        }

        TryRegisterPostProcess();
    }

    public void RebuildFromGenerator(DunGen.DungeonGenerator generator)
    {
        ClearRegistry();

        Dungeon dungeon = generator != null ? generator.CurrentDungeon : null;
        if (dungeon == null || dungeon.AllTiles == null)
            return;

        int switcherCount = 0;
        int probeCount = 0;

        for (int i = 0; i < dungeon.AllTiles.Count; i++)
        {
            Tile tile = dungeon.AllTiles[i];
            if (tile == null)
                continue;

            var switchers = tile.GetComponentsInChildren<DungeonTileLightmapSwitcher>(true);
            for (int s = 0; s < switchers.Length; s++)
            {
                if (RebuildSwitcherSet(tile, switchers[s]))
                {
                    switcherCount++;
                    probeCount += _setsBySwitcher[switchers[s]].ProbeCount;
                }
            }
        }

        BuildDoorwayBlendZones(dungeon);

        if (logSummary)
        {
            Debug.Log(
                $"[DungeonTileProbeRegistry] rebuilt tileSets={_sets.Count} switchers={switcherCount} " +
                $"probes={probeCount} doorwayBlendZones={_doorwayBlendZones.Count}",
                this);
        }
    }

    public bool TrySample(Vector3 worldPosition, out SphericalHarmonicsL2 probe, out Vector4 occlusion,
        out SampleInfo info, Transform ignoredGeometryRoot = null, bool includePortalBlend = true)
    {
        probe = default;
        occlusion = Vector4.one;
        info = default;

        if (visibilityFilteringEnabled)
        {
            if (TryFindVisibilityZone(worldPosition, out var zone, out bool ownsA, out float neighbourWeight))
                return TrySampleVisibleDoorway(zone, ownsA, includePortalBlend ? neighbourWeight : 0f,
                    worldPosition, out probe, out occlusion, out info, ignoredGeometryRoot);
        }
        else if (includePortalBlend && TrySampleDoorwayBlend(worldPosition, out probe, out occlusion, out info))
            return true;

        TileProbeSet set = FindBestSet(worldPosition);
        return TrySampleFromSet(set, worldPosition, out probe, out occlusion, out info, ignoredGeometryRoot: ignoredGeometryRoot);
    }

    public bool TrySampleForTile(Tile tile, Vector3 worldPosition, out SphericalHarmonicsL2 probe,
        out Vector4 occlusion, out SampleInfo info, Transform ignoredGeometryRoot = null)
    {
        probe = default;
        occlusion = Vector4.one;
        info = default;

        TileProbeSet set = FindSet(tile);
        return TrySampleFromSet(set, worldPosition, out probe, out occlusion, out info, ignoredGeometryRoot: ignoredGeometryRoot);
    }

    public int AddPortalLighting(Vector3 worldPosition, ref SphericalHarmonicsL2 probe,
        ref SampleInfo info, Transform ignoredGeometryRoot = null)
    {
        info.portalContributionCount = 0;
        info.portalConnectionCount = 0;
        info.portalL0Luminance = 0f;
        _sampledPortalConnections.Clear();
        if (!info.hasTileData || info.noVisibleProbes)
            return 0;

        // B already blends the two rooms at the threshold. Fade the added light to zero
        // at equal weights so changing the receiving side does not introduce a jump.
        // Failed neighbour visibility leaves spatialBlendActive false and keeps full weight.
        float handoffWeight = info.spatialBlendActive
            ? Mathf.SmoothStep(0f, 1f, Mathf.Abs(info.tileAWeight - info.tileBWeight))
            : 1f;
        if (handoffWeight <= 0.00001f)
            return 0;

        // The portal plane identifies which room's incoming connections apply here.
        TileProbeSet set = visibilityFilteringEnabled &&
            TryFindVisibilityZone(worldPosition, out var zone, out bool ownsA, out _)
            ? FindSet(ownsA ? zone.TileA : zone.TileB)
            : FindBestSet(worldPosition);
        if (set == null || set.Tile == null)
            return 0;

        SphericalHarmonicsL2 incoming = default;
        for (int i = 0; i < set.DoorZones.Count; i++)
        {
            RoomLocalConnection connection = set.DoorZones[i].Connection;
            if (connection == null || !_sampledPortalConnections.Add(connection))
                continue;
            // Counted even when the door is closed: a receiver in this room must keep polling
            // for the door to open, while rooms without a connection keep the slow cadence.
            info.portalConnectionCount++;
            if (!connection.TrySampleDirectSH(set.Tile, worldPosition, ignoredGeometryRoot, out var contribution))
                continue;
            AddWeightedProbe(ref incoming, contribution, 1f);
            info.portalContributionCount++;
        }

        if (info.portalContributionCount > 0)
        {
            ScaleProbe(ref incoming, handoffWeight);
            AddWeightedProbe(ref probe, incoming, 1f);
            info.portalL0Luminance = CalculateLuminance(GetCoefficient(incoming, 0));
            info.l0Luminance = CalculateLuminance(GetCoefficient(probe, 0));
        }
        return info.portalContributionCount;
    }

    public bool IsPortalSegmentVisible(Tile receiverTile, Vector3 from, Vector3 to, Transform ignoredGeometryRoot)
    {
        TileProbeSet set = FindSet(receiverTile);
        if (set == null || !DungeonProbeVisibility.IsVisible(from, to, set.StructuralBlockers, out _, ignoredGeometryRoot))
            return false;
        // Other connected doors must still occlude this receiver-side segment.
        for (int i = 0; i < _doorwayBlendZones.Count; i++)
        {
            DoorwayBlendZone zone = _doorwayBlendZones[i];
            if (zone.VisibilityPrimary &&
                !DungeonProbeVisibility.IsVisible(from, to, zone.DoorBlockers, out _, ignoredGeometryRoot))
                return false;
        }
        return true;
    }

    public void SetSpatialBlendEnabled(bool enabled)
    {
        if (spatialBlendEnabled == enabled)
            return;

        spatialBlendEnabled = enabled;
        SpatialBlendSettingsChanged?.Invoke();
        ForceRefreshReceivers();
    }

    public void SetVisibilityFilteringEnabled(bool enabled)
    {
        if (visibilityFilteringEnabled == enabled)
            return;
        visibilityFilteringEnabled = enabled;
        SpatialBlendSettingsChanged?.Invoke();
        ForceRefreshReceivers();
    }

    public bool ToggleSpatialBlendEnabled()
    {
        SetSpatialBlendEnabled(!spatialBlendEnabled);
        return spatialBlendEnabled;
    }

    /// <summary>
    /// Opts every dynamic receiver (player, monsters, items, runtime-added receivers) into the
    /// incoming door connection SH at once, so the three classes never drift apart.
    /// </summary>
    public void SetDynamicReceiversReceivePortalDirectSH(bool enabled)
    {
        if (dynamicReceiversReceivePortalDirectSH == enabled)
            return;
        dynamicReceiversReceivePortalDirectSH = enabled;
        SpatialBlendSettingsChanged?.Invoke();
        ForceRefreshReceivers();
    }

    public bool ToggleDynamicReceiversReceivePortalDirectSH()
    {
        SetDynamicReceiversReceivePortalDirectSH(!dynamicReceiversReceivePortalDirectSH);
        return dynamicReceiversReceivePortalDirectSH;
    }

    public void ForceRefreshReceivers()
    {
        var dynamicReceivers = FindObjectsByType<DungeonDynamicProbeReceiver>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < dynamicReceivers.Length; i++)
        {
            if (dynamicReceivers[i] != null && dynamicReceivers[i].enabled)
                dynamicReceivers[i].ForceRefresh();
        }

        var doorReceivers = FindObjectsByType<DungeonDoorDualSideProbeReceiver>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < doorReceivers.Length; i++)
        {
            if (doorReceivers[i] != null && doorReceivers[i].enabled)
                doorReceivers[i].ForceRefresh();
        }
    }

    public string BuildSampleReport(Vector3 worldPosition)
    {
        if (!TrySample(worldPosition, out _, out _, out var info))
            return $"No dungeon SH sample at {worldPosition}";

        if (info.spatialBlendActive)
        {
            return
                $"SpatialBlend=ON active zone={info.blendZoneName} " +
                $"{info.tileAName}:{info.tileAWeight:0.00} {info.tileBName}:{info.tileBWeight:0.00} " +
                $"l0={info.l0Luminance:0.000} pos={worldPosition}";
        }

        return
            $"SpatialBlend={(spatialBlendEnabled ? "ON" : "OFF")} inactive " +
            $"tile={info.tileName} probes={info.blendedProbeCount}/{info.probeCount} " +
            $"l0={info.l0Luminance:0.000} pos={worldPosition}";
    }

    private void TryRegisterPostProcess()
    {
        if (_postProcessRegistered || !registerPostProcessStep || runtimeDungeon == null || runtimeDungeon.Generator == null)
            return;

        runtimeDungeon.Generator.RegisterPostProcessStep(OnDungeonPostProcess, postProcessPriority, PostProcessPhase.AfterBuiltIn);
        runtimeDungeon.Generator.Cleared += ClearRegistry;
        _postProcessRegistered = true;
    }

    private void UnregisterPostProcess()
    {
        if (!_postProcessRegistered || runtimeDungeon == null || runtimeDungeon.Generator == null)
            return;

        runtimeDungeon.Generator.UnregisterPostProcessStep(OnDungeonPostProcess);
        runtimeDungeon.Generator.Cleared -= ClearRegistry;
        _postProcessRegistered = false;
    }

    private void OnDungeonPostProcess(DunGen.DungeonGenerator generator)
    {
        RebuildFromGenerator(generator);
        AttachGeneratedDoorReceivers(generator);
    }

    private void AttachGeneratedDoorReceivers(DunGen.DungeonGenerator generator)
    {
        if (!attachGeneratedDoorReceivers || generator == null || generator.Root == null)
            return;

        var doors = generator.Root.GetComponentsInChildren<DunGen.Door>(true);
        int addedCount = 0;
        int refreshedCount = 0;
        int skippedWithoutRendererCount = 0;

        for (int i = 0; i < doors.Length; i++)
        {
            DunGen.Door door = doors[i];
            if (door == null)
                continue;

            GameObject doorObject = door.gameObject;
            if ((door.TileA == null || door.TileB == null) && HasConnectedDoorAncestor(door.transform))
            {
                var genericReceiver = doorObject.GetComponent<DungeonDynamicProbeReceiver>();
                if (genericReceiver != null)
                    genericReceiver.enabled = false;

                var dualReceiver = doorObject.GetComponent<DungeonDoorDualSideProbeReceiver>();
                if (dualReceiver != null)
                    dualReceiver.enabled = false;

                continue;
            }

            if (!TryGetRendererBoundsCenter(doorObject, out Vector3 sampleWorldPosition))
            {
                skippedWithoutRendererCount++;
                continue;
            }

            if (door.TileA != null && door.TileB != null)
            {
                var genericReceiver = doorObject.GetComponent<DungeonDynamicProbeReceiver>();
                if (genericReceiver != null)
                    genericReceiver.enabled = false;

                var dualReceiver = doorObject.GetComponent<DungeonDoorDualSideProbeReceiver>();
                if (dualReceiver == null)
                {
                    dualReceiver = doorObject.AddComponent<DungeonDoorDualSideProbeReceiver>();
                    addedCount++;
                }

                dualReceiver.Configure(door, this);
                refreshedCount++;
                continue;
            }

            var receiver = doorObject.GetComponent<DungeonDynamicProbeReceiver>();
            if (receiver == null)
            {
                receiver = doorObject.AddComponent<DungeonDynamicProbeReceiver>();
                addedCount++;
            }

            Transform samplePoint = EnsureRuntimeSamplePoint(doorObject.transform, sampleWorldPosition);
            receiver.SetSamplePoint(samplePoint);
            refreshedCount++;
        }

        if (logGeneratedDoorReceiverSummary)
        {
            Debug.Log(
                $"[DungeonTileProbeRegistry] generatedDoorReceivers doors={doors.Length} added={addedCount} " +
                $"refreshed={refreshedCount} skippedWithoutRenderer={skippedWithoutRendererCount}",
                this);
        }
    }

    private bool RebuildSwitcherSet(Tile tile, DungeonTileLightmapSwitcher switcher)
    {
        if (switcher == null)
            return false;

        DungeonTileBakeData data = switcher.CurrentBakeData;
        var entries = data != null ? data.lightProbeEntries : null;
        if (entries == null || entries.Length == 0)
        {
            RemoveSwitcherSet(switcher);
            return false;
        }

        if (!_setsBySwitcher.TryGetValue(switcher, out TileProbeSet set))
        {
            set = new TileProbeSet();
            set.StructuralBlockers = CollectStructuralBlockers(tile != null ? tile.transform : switcher.transform);
            _setsBySwitcher.Add(switcher, set);
            _sets.Add(set);
            SubscribeSwitcher(switcher);
            for (int i = 0; i < _doorwayBlendZones.Count; i++)
            {
                var zone = _doorwayBlendZones[i];
                if (zone.VisibilityPrimary && (zone.TileA == tile || zone.TileB == tile))
                    set.DoorZones.Add(zone);
            }
        }

        set.Tile = tile;
        set.Switcher = switcher;
        set.DisplayName = BuildDisplayName(tile, switcher);
        set.Samples = BuildSamples(switcher.transform, entries);
        set.Bounds = ResolveBounds(tile, set.Samples);
        return set.Samples.Length > 0;
    }

    private void RemoveSwitcherSet(DungeonTileLightmapSwitcher switcher)
    {
        if (switcher == null || !_setsBySwitcher.TryGetValue(switcher, out TileProbeSet set))
            return;

        _setsBySwitcher.Remove(switcher);
        _sets.Remove(set);
        UnsubscribeSwitcher(switcher);
    }

    private void SubscribeSwitcher(DungeonTileLightmapSwitcher switcher)
    {
        if (switcher == null || _powerHandlers.ContainsKey(switcher))
            return;

        Action<DungeonTileLightmapSwitcher.PowerLevel> handler = _ =>
        {
            if (_setsBySwitcher.TryGetValue(switcher, out TileProbeSet set))
                RebuildSwitcherSet(set.Tile, switcher);
        };

        _powerHandlers.Add(switcher, handler);
        switcher.PowerLevelApplied += handler;
    }

    private void UnsubscribeSwitcher(DungeonTileLightmapSwitcher switcher)
    {
        if (switcher == null || !_powerHandlers.TryGetValue(switcher, out var handler))
            return;

        switcher.PowerLevelApplied -= handler;
        _powerHandlers.Remove(switcher);
    }

    private void ClearRegistry()
    {
        foreach (var pair in _powerHandlers)
        {
            if (pair.Key != null)
                pair.Key.PowerLevelApplied -= pair.Value;
        }

        _powerHandlers.Clear();
        _setsBySwitcher.Clear();
        _sets.Clear();
        _doorwayBlendZones.Clear();
        _sampledPortalConnections.Clear();
    }

    private void BuildDoorwayBlendZones(Dungeon dungeon)
    {
        _doorwayBlendZones.Clear();
        for (int i = 0; i < _sets.Count; i++)
            _sets[i].DoorZones.Clear();
        if (dungeon == null || dungeon.AllTiles == null)
            return;
        // Preserve the original door-based zones for the visibility-OFF comparison.
        var seenDoors = new HashSet<DunGen.Door>();
        for (int i = 0; i < dungeon.AllTiles.Count; i++)
        {
            Tile tile = dungeon.AllTiles[i];
            if (tile == null)
                continue;
            var doors = tile.GetComponentsInChildren<DunGen.Door>(true);
            for (int d = 0; d < doors.Length; d++)
            {
                DunGen.Door door = doors[d];
                if (door == null || !seenDoors.Add(door))
                    continue;
                if (TryBuildDoorwayBlendZone(door.DoorwayA, door.DoorwayB, out var zone, door))
                    AddDoorwayZone(zone);
            }
        }
        if (dungeon.Connections == null)
            return;
        for (int i = 0; i < dungeon.Connections.Count; i++)
        {
            var pair = dungeon.Connections[i];
            bool exists = false;
            for (int z = 0; z < _doorwayBlendZones.Count; z++)
            {
                var existing = _doorwayBlendZones[z];
                if ((existing.FirstDoorway == pair.A && existing.SecondDoorway == pair.B) ||
                    (existing.FirstDoorway == pair.B && existing.SecondDoorway == pair.A))
                { exists = true; break; }
            }
            if (exists)
                continue;
            if (TryBuildDoorwayBlendZone(pair.A, pair.B, out var zone))
                AddDoorwayZone(zone);
        }
    }

    private bool TryBuildDoorwayBlendZone(Doorway first, Doorway second, out DoorwayBlendZone zone, DunGen.Door legacyDoor = null)
    {
        zone = null;
        if (first == null || second == null || first.Tile == null || second.Tile == null)
            return false;
        DunGen.Door door = legacyDoor != null ? legacyDoor : first.DoorComponent != null ? first.DoorComponent : second.DoorComponent;
        Vector3 axis = second.Tile.Bounds.center - first.Tile.Bounds.center;
        axis.y = 0f;
        if (axis.sqrMagnitude <= 0.0001f)
        {
            axis = door != null ? door.transform.forward : first.transform.forward;
            axis.y = 0f;
        }

        if (axis.sqrMagnitude <= 0.0001f)
            return false;

        axis.Normalize();

        Bounds bounds = default;
        bool hasBounds = door != null && TryGetObjectBounds(door.gameObject, out bounds);
        Vector3 center = hasBounds ? bounds.center : first.transform.position;
        float horizontalExtent = hasBounds ? Mathf.Max(bounds.extents.x, bounds.extents.z) : 0.5f;
        float lateralRadius = Mathf.Max(0.5f, horizontalExtent + doorwayLateralPadding);

        zone = new DoorwayBlendZone
        {
            Door = door,
            LegacyEligible = legacyDoor != null,
            FirstDoorway = first,
            SecondDoorway = second,
            TileA = first.Tile,
            TileB = second.Tile,
            Center = center,
            Axis = axis,
            HalfDepth = Mathf.Max(0.05f, doorwayBlendHalfDepth),
            LateralRadius = lateralRadius,
            LateralRadiusSqr = lateralRadius * lateralRadius,
            DisplayName = $"{first.Tile.name}<->{second.Tile.name}"
        };
        zone.TileAName = first.Tile.name;
        zone.TileBName = second.Tile.name;
        ConfigurePortalFrame(zone);
        return true;
    }

    private void AddDoorwayZone(DoorwayBlendZone zone)
    {
        for (int i = 0; i < _doorwayBlendZones.Count; i++)
        {
            var existing = _doorwayBlendZones[i];
            if ((existing.FirstDoorway == zone.FirstDoorway && existing.SecondDoorway == zone.SecondDoorway) ||
                (existing.FirstDoorway == zone.SecondDoorway && existing.SecondDoorway == zone.FirstDoorway))
            { zone.VisibilityPrimary = false; break; }
        }
        _doorwayBlendZones.Add(zone);
        if (zone.VisibilityPrimary)
        {
            FindSet(zone.TileA)?.DoorZones.Add(zone);
            FindSet(zone.TileB)?.DoorZones.Add(zone);
        }
    }

    public void RegisterDoorwayVisibility(Doorway first, Doorway second, RoomLocalDoorAngleSource source,
        RoomLocalConnection connection = null)
    {
        if (first == null || second == null || (source == null && connection == null) ||
            (connection == null && !source.IsConfigured))
            return;
        DoorwayBlendZone zone = null;
        for (int i = 0; i < _doorwayBlendZones.Count; i++)
        {
            var candidate = _doorwayBlendZones[i];
            if ((candidate.FirstDoorway == first && candidate.SecondDoorway == second) ||
                (candidate.FirstDoorway == second && candidate.SecondDoorway == first))
            {
                zone = candidate;
                break;
            }
        }
        if (zone == null)
        {
            if (!TryBuildDoorwayBlendZone(first, second, out zone))
                return;
            AddDoorwayZone(zone);
        }
        bool hasConnectionState = connection != null;
        Transform leaf = source != null ? source.DoorLeaf : null;
        if (zone.AngleSource == source && zone.BoundLeaf == leaf &&
            zone.Connection == connection && zone.HasConnectionState == hasConnectionState)
            return;
        zone.AngleSource = source;
        zone.Connection = connection;
        zone.HasConnectionState = hasConnectionState;
        zone.BoundLeaf = leaf;
        zone.DoorBlockers = leaf != null
            ? leaf.GetComponentsInChildren<Collider>(true)
            : Array.Empty<Collider>();
        ConfigurePortalFrame(zone);
        if (leaf != null)
            FitClosedLeafAperture(zone, source);
    }

    public void UnregisterDoorwayVisibility(RoomLocalDoorAngleSource source, RoomLocalConnection connection = null)
    {
        if (source == null && connection == null)
            return;
        for (int i = 0; i < _doorwayBlendZones.Count; i++)
        {
            var zone = _doorwayBlendZones[i];
            if (connection != null ? zone.Connection != connection : zone.AngleSource != source)
                continue;
            zone.AngleSource = null;
            zone.Connection = null;
            zone.HasConnectionState = false;
            zone.BoundLeaf = null;
            zone.DoorBlockers = Array.Empty<Collider>();
        }
    }

    private static void ConfigurePortalFrame(DoorwayBlendZone zone)
    {
        Transform frame = zone.FirstDoorway.transform;
        zone.PortalOrigin = frame.position;
        zone.PortalAxis = frame.forward.normalized;
        if (Vector3.Dot(zone.PortalAxis, zone.TileB.Bounds.center - zone.TileA.Bounds.center) < 0f)
            zone.PortalAxis = -zone.PortalAxis;
        zone.PortalRight = frame.right.normalized;
        zone.PortalUp = frame.up.normalized;
        Vector2 firstSize = zone.FirstDoorway.Socket.Size;
        Vector2 secondSize = zone.SecondDoorway.Socket.Size;
        zone.ApertureValid = IsValidSocketSize(firstSize) && IsValidSocketSize(secondSize);
        // Invalid metadata does not imply a default open aperture. A measured closed leaf
        // may establish a valid aperture below, including leaves wider than nominal sockets.
        Vector2 size = zone.ApertureValid ? Vector2.Min(firstSize, secondSize) : Vector2.zero;
        zone.MinX = -frame.TransformVector(Vector3.right * size.x).magnitude * 0.5f;
        zone.MaxX = -zone.MinX;
        zone.Bottom = 0f;
        zone.Top = frame.TransformVector(Vector3.up * size.y).magnitude;
        zone.ApertureValid &= zone.MaxX - zone.MinX > 0.05f && zone.Top > 0.05f;
    }

    private static bool IsValidSocketSize(Vector2 size)
    {
        return size.x > 0.05f && size.y > 0.05f && !float.IsInfinity(size.x) && !float.IsInfinity(size.y);
    }

    private static void FitClosedLeafAperture(DoorwayBlendZone zone, RoomLocalDoorAngleSource source)
    {
        Transform leaf = source.DoorLeaf;
        Matrix4x4 closedWorld = (leaf.parent != null ? leaf.parent.localToWorldMatrix : Matrix4x4.identity) *
            Matrix4x4.TRS(leaf.localPosition, source.ClosedLocalRotation, leaf.localScale);
        Renderer[] renderers = leaf.GetComponentsInChildren<Renderer>(true);
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;
            Bounds local = renderer.localBounds;
            Matrix4x4 toClosedWorld = closedWorld * leaf.worldToLocalMatrix * renderer.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = local.center + Vector3.Scale(local.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                Vector3 offset = toClosedWorld.MultiplyPoint3x4(point) - zone.PortalOrigin;
                float x = Vector3.Dot(offset, zone.PortalRight);
                float y = Vector3.Dot(offset, zone.PortalUp);
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y);
            }
        }
        // Production socket labels can be 1x2 while the real closed leaf is wider/taller.
        if (maxX - minX > 0.1f && top - bottom > 0.1f &&
            !float.IsInfinity(minX) && !float.IsInfinity(maxX) && !float.IsInfinity(bottom) && !float.IsInfinity(top))
        {
            zone.MinX = minX; zone.MaxX = maxX;
            zone.Bottom = bottom; zone.Top = top;
            zone.ApertureValid = true;
        }
    }

    private bool TrySampleDoorwayBlend(Vector3 worldPosition, out SphericalHarmonicsL2 probe, out Vector4 occlusion, out SampleInfo info)
    {
        probe = default;
        occlusion = Vector4.one;
        info = default;

        if (!spatialBlendEnabled ||
            _doorwayBlendZones.Count == 0 ||
            !TryFindDoorwayBlendZone(worldPosition, out var zone, out float tileAWeight, out float tileBWeight))
        {
            return false;
        }

        TileProbeSet setA = FindSet(zone.TileA);
        TileProbeSet setB = FindSet(zone.TileB);
        bool hasA = TrySampleFromSet(setA, worldPosition, out var probeA, out var occlusionA, out var infoA);
        bool hasB = TrySampleFromSet(setB, worldPosition, out var probeB, out var occlusionB, out var infoB);

        if (hasA && hasB)
        {
            AddWeightedProbe(ref probe, probeA, tileAWeight);
            AddWeightedProbe(ref probe, probeB, tileBWeight);
            occlusion = occlusionA * tileAWeight + occlusionB * tileBWeight;
            info = new SampleInfo
            {
                hasTileData = true,
                candidateProbeCount = infoA.candidateProbeCount + infoB.candidateProbeCount,
                rejectedProbeCount = infoA.rejectedProbeCount + infoB.rejectedProbeCount,
                visibilityRayCount = infoA.visibilityRayCount + infoB.visibilityRayCount,
                visibilityBlockerCount = infoA.visibilityBlockerCount + infoB.visibilityBlockerCount,
                doorBlockerCount = infoA.doorBlockerCount + infoB.doorBlockerCount,
                tileName = $"{infoA.tileName}+{infoB.tileName}",
                probeCount = infoA.probeCount + infoB.probeCount,
                blendedProbeCount = infoA.blendedProbeCount + infoB.blendedProbeCount,
                l0Luminance = CalculateLuminance(GetCoefficient(probe, 0)),
                spatialBlendActive = true,
                blendZoneName = zone.DisplayName,
                tileAName = infoA.tileName,
                tileBName = infoB.tileName,
                tileAWeight = tileAWeight,
                tileBWeight = tileBWeight
            };
            return true;
        }

        if (hasA)
        {
            probe = probeA;
            occlusion = occlusionA;
            info = infoA;
            return true;
        }

        if (hasB)
        {
            probe = probeB;
            occlusion = occlusionB;
            info = infoB;
            return true;
        }

        return false;
    }

    private bool TryFindDoorwayBlendZone(
        Vector3 worldPosition,
        out DoorwayBlendZone zone,
        out float tileAWeight,
        out float tileBWeight)
    {
        zone = default;
        tileAWeight = 1f;
        tileBWeight = 0f;

        float bestScore = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < _doorwayBlendZones.Count; i++)
        {
            DoorwayBlendZone candidate = _doorwayBlendZones[i];
            if (!candidate.LegacyEligible)
                continue;
            if (!TryEvaluateDoorwayBlendZone(candidate, worldPosition, out float weightA, out float weightB, out float score))
                continue;

            if (score >= bestScore)
                continue;

            zone = candidate;
            tileAWeight = weightA;
            tileBWeight = weightB;
            bestScore = score;
            found = true;
        }

        return found;
    }

    private static bool TryEvaluateDoorwayBlendZone(
        DoorwayBlendZone zone,
        Vector3 worldPosition,
        out float tileAWeight,
        out float tileBWeight,
        out float score)
    {
        tileAWeight = 1f;
        tileBWeight = 0f;
        score = float.PositiveInfinity;

        Vector3 offset = worldPosition - zone.Center;
        offset.y = 0f;

        float signedDistance = Vector3.Dot(offset, zone.Axis);
        if (Mathf.Abs(signedDistance) > zone.HalfDepth)
            return false;

        Vector3 lateral = offset - zone.Axis * signedDistance;
        float lateralSqr = lateral.sqrMagnitude;
        if (lateralSqr > zone.LateralRadiusSqr)
            return false;

        float t = Mathf.InverseLerp(-zone.HalfDepth, zone.HalfDepth, signedDistance);
        tileBWeight = Mathf.SmoothStep(0f, 1f, t);
        tileAWeight = 1f - tileBWeight;
        score = lateralSqr + Mathf.Abs(signedDistance) * 0.05f;
        return true;
    }

    private bool TryFindVisibilityZone(Vector3 position, out DoorwayBlendZone zone, out bool ownsA, out float neighbourWeight)
    {
        zone = null;
        ownsA = true;
        neighbourWeight = 0f;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < _doorwayBlendZones.Count; i++)
        {
            var candidate = _doorwayBlendZones[i];
            if (!candidate.VisibilityPrimary || candidate.FirstDoorway == null || candidate.SecondDoorway == null)
                continue;
            Vector3 offset = position - candidate.PortalOrigin;
            float z = Vector3.Dot(offset, candidate.PortalAxis);
            float x = Vector3.Dot(offset, candidate.PortalRight);
            float y = Vector3.Dot(offset, candidate.PortalUp);
            // Ownership also applies beside a closed portal; it must not depend on blending.
            if (Mathf.Abs(z) > Mathf.Max(0.6f, doorwayBlendHalfDepth) ||
                x < candidate.MinX - 0.25f || x > candidate.MaxX + 0.25f ||
                y < candidate.Bottom - 0.25f || y > candidate.Top + 0.25f)
                continue;
            float centerX = (candidate.MinX + candidate.MaxX) * 0.5f;
            float score = (x - centerX) * (x - centerX) + Mathf.Abs(z) * 0.05f;
            if (candidate.AngleSource == null)
                score += 0.001f;
            if (score >= bestScore)
                continue;
            bestScore = score;
            zone = candidate;
            ownsA = z <= 0f;
            neighbourWeight = 0f;
            if (!spatialBlendEnabled || !candidate.ApertureValid)
                continue;
            float aperture;
            if (candidate.HasConnectionState)
            {
                // A removed/disabled shared connection must not fall back to the angle-only
                // path and keep transferring SH after its cookie transport has stopped.
                if (candidate.Connection == null)
                    continue;
                var transfer = candidate.Connection.EvaluateTransferState();
                if (!transfer.Enabled)
                    continue;
                aperture = Mathf.Clamp01(transfer.ApertureFraction);
            }
            else
            {
                // Preserve the standalone three-argument registration contract.
                if (candidate.AngleSource == null || !candidate.AngleSource.IsConfigured)
                    continue;
                candidate.AngleSource.EvaluateNow();
                aperture = Mathf.Clamp01(candidate.AngleSource.ApertureFraction);
            }
            if (aperture <= 0.00001f)
                continue;
            // Narrow the blend depth while the leaf closes. Scaling the weight directly
            // would introduce a step when ownership changes at z=0 during partial opening.
            float edgeDistance = Mathf.Min(Mathf.Min(x - candidate.MinX, candidate.MaxX - x),
                Mathf.Min(y - candidate.Bottom, candidate.Top - y));
            float edgeFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edgeDistance / 0.2f));
            float depth = Mathf.Min(0.6f, Mathf.Max(0.05f, doorwayBlendHalfDepth)) * aperture * edgeFade;
            if (depth <= 0.00001f || Mathf.Abs(z) >= depth)
                continue;
            float weightB = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-depth, depth, z));
            neighbourWeight = ownsA ? weightB : 1f - weightB;
        }
        return zone != null;
    }

    private bool TrySampleVisibleDoorway(DoorwayBlendZone zone, bool ownsA, float neighbourWeight,
        Vector3 position, out SphericalHarmonicsL2 probe, out Vector4 occlusion, out SampleInfo info, Transform ignoredGeometryRoot)
    {
        TileProbeSet ownSet = FindSet(ownsA ? zone.TileA : zone.TileB);
        bool ownVisible = TrySampleFromSet(ownSet, position, out probe, out occlusion, out info, ignoredGeometryRoot: ignoredGeometryRoot);
        info.blendZoneName = zone.DisplayName;
        info.tileAName = zone.TileAName;
        info.tileBName = zone.TileBName;
        info.tileAWeight = ownsA ? 1f : 0f;
        info.tileBWeight = ownsA ? 0f : 1f;
        if (!ownVisible || neighbourWeight <= 0f)
            return ownVisible;

        TileProbeSet neighbourSet = FindSet(ownsA ? zone.TileB : zone.TileA);
        bool neighbourVisible = TrySampleFromSet(neighbourSet, position, out var otherProbe,
            out var otherOcclusion, out var otherInfo, ownSet, ignoredGeometryRoot);
        info.candidateProbeCount += otherInfo.candidateProbeCount;
        info.rejectedProbeCount += otherInfo.rejectedProbeCount;
        info.visibilityRayCount += otherInfo.visibilityRayCount;
        if (!neighbourVisible)
        {
            info.usedOwnRoomFallback = true;
            return true;
        }
        ScaleProbe(ref probe, 1f - neighbourWeight);
        AddWeightedProbe(ref probe, otherProbe, neighbourWeight);
        occlusion = occlusion * (1f - neighbourWeight) + otherOcclusion * neighbourWeight;
        info.tileName = zone.DisplayName;
        info.probeCount += otherInfo.probeCount;
        info.blendedProbeCount += otherInfo.blendedProbeCount;
        info.spatialBlendActive = true;
        info.tileAWeight = ownsA ? 1f - neighbourWeight : neighbourWeight;
        info.tileBWeight = 1f - info.tileAWeight;
        info.l0Luminance = CalculateLuminance(GetCoefficient(probe, 0));
        return true;
    }

    private TileProbeSet FindBestSet(Vector3 worldPosition)
    {
        TileProbeSet best = null;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < _sets.Count; i++)
        {
            TileProbeSet set = _sets[i];
            if (set == null || set.Samples == null || set.Samples.Length == 0)
                continue;

            float distance = set.Bounds.Contains(worldPosition)
                ? 0f
                : set.Bounds.SqrDistance(worldPosition);

            if (distance >= bestDistance)
                continue;

            best = set;
            bestDistance = distance;
        }

        return best;
    }

    private static bool HasConnectedDoorAncestor(Transform transform)
    {
        if (transform == null)
            return false;

        Transform current = transform.parent;
        while (current != null)
        {
            var ancestorDoor = current.GetComponent<DunGen.Door>();
            if (ancestorDoor != null && ancestorDoor.TileA != null && ancestorDoor.TileB != null)
                return true;

            current = current.parent;
        }

        return false;
    }

    private TileProbeSet FindSet(Tile tile)
    {
        if (tile == null)
            return null;

        for (int i = 0; i < _sets.Count; i++)
        {
            TileProbeSet set = _sets[i];
            if (set != null && set.Tile == tile)
                return set;
        }

        return null;
    }

    private bool TrySampleFromSet(TileProbeSet set, Vector3 worldPosition, out SphericalHarmonicsL2 probe,
        out Vector4 occlusion, out SampleInfo info, TileProbeSet receiverSet = null, Transform ignoredGeometryRoot = null)
    {
        probe = default;
        occlusion = Vector4.one;
        info = default;

        if (set == null || set.Samples == null || set.Samples.Length == 0)
            return false;
        info.tileName = set.DisplayName;
        info.probeCount = set.ProbeCount;
        info.hasTileData = true;
        info.visibilityBlockerCount = set.StructuralBlockers.Length;
        for (int i = 0; i < set.DoorZones.Count; i++)
            info.doorBlockerCount += set.DoorZones[i].DoorBlockers.Length;
        ResetNearestBuffers();

        for (int i = 0; i < set.Samples.Length; i++)
        {
            float sqrDistance = (set.Samples[i].WorldPosition - worldPosition).sqrMagnitude;
            if (float.IsNaN(sqrDistance) || float.IsInfinity(sqrDistance))
                continue;
            InsertNearestProbe(i, sqrDistance);
        }

        float totalWeight = 0f;
        Vector4 blendedOcclusion = Vector4.zero;

        for (int i = 0; i < _nearestBufferCount && info.blendedProbeCount < BlendProbeCount; i++)
        {
            int sampleIndex = _bestIndices[i];
            if (sampleIndex < 0)
                continue;

            RuntimeProbeSample sample = set.Samples[sampleIndex];
            info.candidateProbeCount++;
            if (visibilityFilteringEnabled)
            {
                bool visible = IsProbeVisible(set, receiverSet, worldPosition, sample.WorldPosition, out int rayCount, ignoredGeometryRoot);
                info.visibilityRayCount += rayCount;
                if (!visible)
                {
                    info.rejectedProbeCount++;
                    continue;
                }
            }
            float distance = Mathf.Sqrt(Mathf.Max(0f, _bestDistances[i]));
            float weight = 1f / Mathf.Max(0.05f, distance + 0.05f);

            AddWeightedProbe(ref probe, sample.Probe, weight);
            blendedOcclusion += sample.Occlusion * weight;
            totalWeight += weight;
            info.blendedProbeCount++;
        }

        if (totalWeight <= 0f)
        {
            info.noVisibleProbes = visibilityFilteringEnabled;
            return false;
        }

        float invWeight = 1f / totalWeight;
        ScaleProbe(ref probe, invWeight);
        occlusion = blendedOcclusion * invWeight;

        info.l0Luminance = CalculateLuminance(GetCoefficient(probe, 0));

        return true;
    }

    private static bool IsProbeVisible(TileProbeSet set, TileProbeSet receiverSet, Vector3 from, Vector3 to,
        out int rays, Transform ignoredGeometryRoot)
    {
        bool visible = DungeonProbeVisibility.IsVisible(from, to, set.StructuralBlockers, out rays, ignoredGeometryRoot);
        if (!visible)
            return false;
        for (int i = 0; i < set.DoorZones.Count; i++)
        {
            visible = DungeonProbeVisibility.IsVisible(from, to, set.DoorZones[i].DoorBlockers, out int count, ignoredGeometryRoot);
            rays += count;
            if (!visible)
                return false;
        }
        if (receiverSet == null || receiverSet == set)
            return true;
        visible = DungeonProbeVisibility.IsVisible(from, to, receiverSet.StructuralBlockers, out int receiverRays, ignoredGeometryRoot);
        rays += receiverRays;
        if (!visible)
            return false;
        for (int i = 0; i < receiverSet.DoorZones.Count; i++)
        {
            var zone = receiverSet.DoorZones[i];
            if (set.DoorZones.Contains(zone))
                continue;
            visible = DungeonProbeVisibility.IsVisible(from, to, zone.DoorBlockers, out int count, ignoredGeometryRoot);
            rays += count;
            if (!visible)
                return false;
        }
        return true;
    }

    private static Collider[] CollectStructuralBlockers(Transform root)
    {
        var blockers = new List<Collider>();
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            global::Door movingDoor = collider.GetComponentInParent<global::Door>(true);
            bool isRoomDoor = movingDoor != null && movingDoor.transform.IsChildOf(root);
            bool structural = false, excluded = false;
            for (Transform current = collider.transform; current != null; current = current.parent)
            {
                if (current.GetComponent<Item>() != null || current.GetComponent<CharacterController>() != null ||
                    current.GetComponent<SkinnedMeshRenderer>() != null)
                { excluded = true; break; }
                bool hasReceiver = current.GetComponent<DungeonDynamicProbeReceiver>() != null ||
                    current.GetComponent<DungeonDoorDualSideProbeReceiver>() != null;
                // A door can itself receive SH and still block samples taken by other objects.
                // Its own receiver passes ignoredGeometryRoot, instead of removing the leaf
                // from everyone's visibility cache. Actor/loot receiver subtrees stay excluded.
                if (hasReceiver && (!isRoomDoor || !current.IsChildOf(movingDoor.transform)))
                { excluded = true; break; }
                // This is the tested Prison prefab authoring contract, not a general geometry
                // classifier. Floors and loose furniture are deliberately outside this set.
                string name = current.name;
                // Locker leaves reuse gameplay Door, but do not partition a room. Their
                // small enclosed storage has no baked probe coverage; keep it in the
                // furniture category instead of rejecting every probe for spawned loot.
                if (name.StartsWith("Locker_", StringComparison.OrdinalIgnoreCase))
                { excluded = true; break; }
                if (name == "Walls" || name == "Pillars" || name == "Doorways" ||
                    name.StartsWith("Wall", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Pillar", StringComparison.OrdinalIgnoreCase))
                    structural = true;
                if (current == root)
                    break;
            }
            // Internal doors have no dungeon.Connections entry; retain their moving collider
            // references here. Queries always use current bounds/poses, not baked bounds.
            if ((structural || isRoomDoor) && !excluded)
                blockers.Add(collider);
        }
        return blockers.ToArray();
    }

    private static RuntimeProbeSample[] BuildSamples(Transform root, DungeonTileBakeData.LightProbeBakeEntry[] entries)
    {
        if (root == null || entries == null || entries.Length == 0)
            return Array.Empty<RuntimeProbeSample>();

        var samples = new RuntimeProbeSample[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            DungeonTileBakeData.LightProbeBakeEntry entry = entries[i];
            samples[i] = new RuntimeProbeSample
            {
                WorldPosition = root.TransformPoint(entry.localPosition),
                Probe = entry.ToSphericalHarmonics(),
                Occlusion = entry.occlusion
            };
        }

        return samples;
    }

    private static Bounds ResolveBounds(Tile tile, RuntimeProbeSample[] samples)
    {
        if (tile != null)
            return tile.Bounds;

        if (samples == null || samples.Length == 0)
            return new Bounds(Vector3.zero, Vector3.one);

        var bounds = new Bounds(samples[0].WorldPosition, Vector3.one);
        for (int i = 1; i < samples.Length; i++)
            bounds.Encapsulate(samples[i].WorldPosition);

        if (bounds.size.sqrMagnitude <= 0.0001f)
            bounds.size = Vector3.one;

        return bounds;
    }

    private static Transform EnsureRuntimeSamplePoint(Transform root, Vector3 worldPosition)
    {
        Transform samplePoint = root.Find("DungeonProbeSamplePoint");
        if (samplePoint == null)
        {
            var samplePointObject = new GameObject("DungeonProbeSamplePoint");
            samplePoint = samplePointObject.transform;
            samplePoint.SetParent(root, false);
        }

        samplePoint.position = worldPosition;
        samplePoint.localRotation = Quaternion.identity;
        samplePoint.localScale = Vector3.one;
        return samplePoint;
    }

    private static bool TryGetRendererBoundsCenter(GameObject root, out Vector3 center)
    {
        center = default;

        if (!TryGetObjectBounds(root, out Bounds bounds))
            return false;

        center = bounds.center;
        return true;
    }

    private static bool TryGetObjectBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
            return false;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
        }

        if (!hasBounds)
            return false;

        return true;
    }

    private void ResetNearestBuffers()
    {
        _nearestBufferCount = visibilityFilteringEnabled ? Mathf.Clamp(visibilityCandidateCount, BlendProbeCount, 128) : BlendProbeCount;
        for (int i = 0; i < _nearestBufferCount; i++)
        {
            _bestDistances[i] = float.PositiveInfinity;
            _bestIndices[i] = -1;
        }
    }

    private void InsertNearestProbe(int probeIndex, float sqrDistance)
    {
        for (int i = 0; i < _nearestBufferCount; i++)
        {
            if (sqrDistance >= _bestDistances[i])
                continue;

            for (int j = _nearestBufferCount - 1; j > i; j--)
            {
                _bestDistances[j] = _bestDistances[j - 1];
                _bestIndices[j] = _bestIndices[j - 1];
            }

            _bestDistances[i] = sqrDistance;
            _bestIndices[i] = probeIndex;
            return;
        }
    }

    private static void AddWeightedProbe(ref SphericalHarmonicsL2 destination, SphericalHarmonicsL2 source, float weight)
    {
        for (int rgb = 0; rgb < 3; rgb++)
        {
            for (int coefficient = 0; coefficient < 9; coefficient++)
                destination[rgb, coefficient] += source[rgb, coefficient] * weight;
        }
    }

    private static void ScaleProbe(ref SphericalHarmonicsL2 probe, float scale)
    {
        for (int rgb = 0; rgb < 3; rgb++)
        {
            for (int coefficient = 0; coefficient < 9; coefficient++)
                probe[rgb, coefficient] *= scale;
        }
    }

    private static Vector3 GetCoefficient(SphericalHarmonicsL2 probe, int coefficient)
    {
        return new Vector3(probe[0, coefficient], probe[1, coefficient], probe[2, coefficient]);
    }

    private static float CalculateLuminance(Vector3 rgb)
    {
        return rgb.x * 0.2126f + rgb.y * 0.7152f + rgb.z * 0.0722f;
    }

    private static string BuildDisplayName(Tile tile, DungeonTileLightmapSwitcher switcher)
    {
        if (tile != null)
            return tile.name;

        return switcher != null ? switcher.name : "UnknownTile";
    }

    public struct SampleInfo
    {
        public int portalContributionCount;
        public int portalConnectionCount;
        public float portalL0Luminance;
        public bool hasTileData;
        public bool noVisibleProbes;
        public bool usedOwnRoomFallback;
        public int candidateProbeCount;
        public int rejectedProbeCount;
        public int visibilityRayCount;
        public int visibilityBlockerCount;
        public int doorBlockerCount;
        public string tileName;
        public int probeCount;
        public int blendedProbeCount;
        public float l0Luminance;
        public bool spatialBlendActive;
        public string blendZoneName;
        public string tileAName;
        public string tileBName;
        public float tileAWeight;
        public float tileBWeight;
    }

    private sealed class TileProbeSet
    {
        public Tile Tile;
        public DungeonTileLightmapSwitcher Switcher;
        public string DisplayName;
        public Bounds Bounds;
        public RuntimeProbeSample[] Samples = Array.Empty<RuntimeProbeSample>();
        public Collider[] StructuralBlockers = Array.Empty<Collider>();
        public readonly List<DoorwayBlendZone> DoorZones = new List<DoorwayBlendZone>();

        public int ProbeCount => Samples != null ? Samples.Length : 0;
    }

    private struct RuntimeProbeSample
    {
        public Vector3 WorldPosition;
        public SphericalHarmonicsL2 Probe;
        public Vector4 Occlusion;
    }

    private sealed class DoorwayBlendZone
    {
        public Doorway FirstDoorway;
        public Doorway SecondDoorway;
        public bool LegacyEligible;
        public bool VisibilityPrimary = true;
        public bool ApertureValid;
        public RoomLocalDoorAngleSource AngleSource;
        public RoomLocalConnection Connection;
        public bool HasConnectionState;
        public Transform BoundLeaf;
        public Collider[] DoorBlockers = Array.Empty<Collider>();
        public Vector3 PortalOrigin, PortalAxis, PortalRight, PortalUp;
        public float MinX, MaxX, Bottom, Top;
        public string TileAName, TileBName;
        public DunGen.Door Door;
        public Tile TileA;
        public Tile TileB;
        public Vector3 Center;
        public Vector3 Axis;
        public float HalfDepth;
        public float LateralRadius;
        public float LateralRadiusSqr;
        public string DisplayName;
    }
}
