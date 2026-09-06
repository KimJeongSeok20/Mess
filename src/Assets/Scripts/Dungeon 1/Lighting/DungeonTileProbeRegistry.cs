using System;
using System.Collections.Generic;
using DunGen;
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

    [Header("Doorway Spatial Blend")]
    [SerializeField] private bool spatialBlendEnabled = true;
    [SerializeField, Min(0.05f)] private float doorwayBlendHalfDepth = 1.35f;
    [SerializeField, Min(0f)] private float doorwayLateralPadding = 0.75f;

    [Header("Generated Doors")]
    [SerializeField] private bool attachGeneratedDoorReceivers = true;
    [SerializeField] private bool logGeneratedDoorReceiverSummary;

    private readonly List<TileProbeSet> _sets = new List<TileProbeSet>();
    private readonly List<DoorwayBlendZone> _doorwayBlendZones = new List<DoorwayBlendZone>();
    private readonly Dictionary<DungeonTileLightmapSwitcher, TileProbeSet> _setsBySwitcher =
        new Dictionary<DungeonTileLightmapSwitcher, TileProbeSet>();
    private readonly Dictionary<DungeonTileLightmapSwitcher, Action<DungeonTileLightmapSwitcher.PowerLevel>> _powerHandlers =
        new Dictionary<DungeonTileLightmapSwitcher, Action<DungeonTileLightmapSwitcher.PowerLevel>>();

    private readonly float[] _bestDistances = new float[BlendProbeCount];
    private readonly int[] _bestIndices = new int[BlendProbeCount];

    private bool _postProcessRegistered;

    public static DungeonTileProbeRegistry Active { get; private set; }

    public event Action SpatialBlendSettingsChanged;

    public int TileSetCount => _sets.Count;
    public int DoorwayBlendZoneCount => _doorwayBlendZones.Count;
    public bool SpatialBlendEnabled => spatialBlendEnabled;
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

    public bool TrySample(Vector3 worldPosition, out SphericalHarmonicsL2 probe, out Vector4 occlusion, out SampleInfo info)
    {
        probe = default;
        occlusion = Vector4.one;
        info = default;

        if (TrySampleDoorwayBlend(worldPosition, out probe, out occlusion, out info))
            return true;

        TileProbeSet set = FindBestSet(worldPosition);
        return TrySampleFromSet(set, worldPosition, out probe, out occlusion, out info);
    }

    public bool TrySampleForTile(Tile tile, Vector3 worldPosition, out SphericalHarmonicsL2 probe, out Vector4 occlusion, out SampleInfo info)
    {
        probe = default;
        occlusion = Vector4.one;
        info = default;

        TileProbeSet set = FindSet(tile);
        return TrySampleFromSet(set, worldPosition, out probe, out occlusion, out info);
    }

    public void SetSpatialBlendEnabled(bool enabled)
    {
        if (spatialBlendEnabled == enabled)
            return;

        spatialBlendEnabled = enabled;
        SpatialBlendSettingsChanged?.Invoke();
        ForceRefreshReceivers();
    }

    public bool ToggleSpatialBlendEnabled()
    {
        SetSpatialBlendEnabled(!spatialBlendEnabled);
        return spatialBlendEnabled;
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
        _postProcessRegistered = true;
    }

    private void UnregisterPostProcess()
    {
        if (!_postProcessRegistered || runtimeDungeon == null || runtimeDungeon.Generator == null)
            return;

        runtimeDungeon.Generator.UnregisterPostProcessStep(OnDungeonPostProcess);
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
            _setsBySwitcher.Add(switcher, set);
            _sets.Add(set);
            SubscribeSwitcher(switcher);
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
    }

    private void BuildDoorwayBlendZones(Dungeon dungeon)
    {
        _doorwayBlendZones.Clear();

        if (dungeon == null || dungeon.AllTiles == null)
            return;

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
                if (door == null || door.TileA == null || door.TileB == null || !seenDoors.Add(door))
                    continue;

                if (TryBuildDoorwayBlendZone(door, out var zone))
                    _doorwayBlendZones.Add(zone);
            }
        }
    }

    private bool TryBuildDoorwayBlendZone(DunGen.Door door, out DoorwayBlendZone zone)
    {
        zone = default;

        Vector3 axis = door.TileB.Bounds.center - door.TileA.Bounds.center;
        axis.y = 0f;
        if (axis.sqrMagnitude <= 0.0001f)
        {
            axis = door.transform.forward;
            axis.y = 0f;
        }

        if (axis.sqrMagnitude <= 0.0001f)
            return false;

        axis.Normalize();

        bool hasBounds = TryGetObjectBounds(door.gameObject, out Bounds bounds);
        Vector3 center = hasBounds ? bounds.center : door.transform.position;
        float horizontalExtent = hasBounds ? Mathf.Max(bounds.extents.x, bounds.extents.z) : 0.5f;
        float lateralRadius = Mathf.Max(0.5f, horizontalExtent + doorwayLateralPadding);

        zone = new DoorwayBlendZone
        {
            Door = door,
            TileA = door.TileA,
            TileB = door.TileB,
            Center = center,
            Axis = axis,
            HalfDepth = Mathf.Max(0.05f, doorwayBlendHalfDepth),
            LateralRadius = lateralRadius,
            LateralRadiusSqr = lateralRadius * lateralRadius,
            DisplayName = $"{door.TileA.name}<->{door.TileB.name}"
        };

        return true;
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

    private bool TrySampleFromSet(TileProbeSet set, Vector3 worldPosition, out SphericalHarmonicsL2 probe, out Vector4 occlusion, out SampleInfo info)
    {
        probe = default;
        occlusion = Vector4.one;
        info = default;

        if (set == null || set.Samples == null || set.Samples.Length == 0)
            return false;

        ResetNearestBuffers();

        for (int i = 0; i < set.Samples.Length; i++)
        {
            float sqrDistance = (set.Samples[i].WorldPosition - worldPosition).sqrMagnitude;
            InsertNearestProbe(i, sqrDistance);
        }

        float totalWeight = 0f;
        Vector4 blendedOcclusion = Vector4.zero;

        for (int i = 0; i < BlendProbeCount; i++)
        {
            int sampleIndex = _bestIndices[i];
            if (sampleIndex < 0)
                continue;

            RuntimeProbeSample sample = set.Samples[sampleIndex];
            float distance = Mathf.Sqrt(Mathf.Max(0f, _bestDistances[i]));
            float weight = 1f / Mathf.Max(0.05f, distance + 0.05f);

            AddWeightedProbe(ref probe, sample.Probe, weight);
            blendedOcclusion += sample.Occlusion * weight;
            totalWeight += weight;
        }

        if (totalWeight <= 0f)
            return false;

        float invWeight = 1f / totalWeight;
        ScaleProbe(ref probe, invWeight);
        occlusion = blendedOcclusion * invWeight;

        info = new SampleInfo
        {
            tileName = set.DisplayName,
            probeCount = set.ProbeCount,
            blendedProbeCount = CountBlendedProbes(),
            l0Luminance = CalculateLuminance(GetCoefficient(probe, 0))
        };

        return true;
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
        for (int i = 0; i < BlendProbeCount; i++)
        {
            _bestDistances[i] = float.PositiveInfinity;
            _bestIndices[i] = -1;
        }
    }

    private void InsertNearestProbe(int probeIndex, float sqrDistance)
    {
        for (int i = 0; i < BlendProbeCount; i++)
        {
            if (sqrDistance >= _bestDistances[i])
                continue;

            for (int j = BlendProbeCount - 1; j > i; j--)
            {
                _bestDistances[j] = _bestDistances[j - 1];
                _bestIndices[j] = _bestIndices[j - 1];
            }

            _bestDistances[i] = sqrDistance;
            _bestIndices[i] = probeIndex;
            return;
        }
    }

    private int CountBlendedProbes()
    {
        int count = 0;
        for (int i = 0; i < BlendProbeCount; i++)
        {
            if (_bestIndices[i] >= 0)
                count++;
        }

        return count;
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

        public int ProbeCount => Samples != null ? Samples.Length : 0;
    }

    private struct RuntimeProbeSample
    {
        public Vector3 WorldPosition;
        public SphericalHarmonicsL2 Probe;
        public Vector4 Occlusion;
    }

    private struct DoorwayBlendZone
    {
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
