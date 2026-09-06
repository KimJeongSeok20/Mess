using System;
using DunGen;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class DungeonDoorDualSideProbeReceiver : MonoBehaviour
{
    [SerializeField] private DunGen.Door door;
    [SerializeField] private DungeonTileProbeRegistry registry;
    [SerializeField] private bool includeInactiveRenderers = true;
    [SerializeField, Min(0.01f)] private float sideSampleOffset = 0.35f;
    [SerializeField, Min(0.02f)] private float updateInterval = 0.2f;
    [SerializeField] private bool blendSideProbesByAnimatedPose = true;
    [SerializeField, Min(0.01f)] private float animatedSideRefreshInterval = 0.05f;
    [SerializeField] private bool smoothProbeChanges = true;
    [SerializeField, Min(0.01f)] private float probeSmoothTime = 0.18f;
    [SerializeField] private bool applyOnEnable = true;
    [SerializeField] private bool logSampleDiagnostics;

    private RendererBinding[] _bindings = Array.Empty<RendererBinding>();
    private MaterialPropertyBlock _propertyBlock;
    private readonly SphericalHarmonicsL2[] _singleProbe = new SphericalHarmonicsL2[1];
    private readonly Vector4[] _singleOcclusion = new Vector4[1];

    private DungeonTileLightmapSwitcher _subscribedTileA;
    private DungeonTileLightmapSwitcher _subscribedTileB;
    private Action<DungeonTileLightmapSwitcher.PowerLevel> _powerHandler;
    private float _nextRefreshTime;
    private bool _hasAppliedCustomProbe;

    private void Awake()
    {
        _propertyBlock = new MaterialPropertyBlock();
        ResolveDoor();
        CacheRendererBindings();
    }

    private void OnEnable()
    {
        ResolveDoor();
        CacheRendererBindings();
        SubscribePowerEvents();

        if (applyOnEnable)
            ForceRefresh();
    }

    private void OnDisable()
    {
        UnsubscribePowerEvents();
        RestoreOriginalProbeUsage();
    }

    private void Update()
    {
        if (Time.unscaledTime >= _nextRefreshTime)
        {
            ForceRefresh();
            return;
        }

        if (smoothProbeChanges)
            ApplySmoothedProbeTargets();
    }

    public void Configure(DunGen.Door sourceDoor, DungeonTileProbeRegistry sourceRegistry)
    {
        door = sourceDoor != null ? sourceDoor : door;
        registry = sourceRegistry != null ? sourceRegistry : registry;

        ResolveDoor();
        CacheRendererBindings();
        SubscribePowerEvents();
        ForceRefresh();
    }

    [ContextMenu("Refresh Dungeon Door Dual Side Probe Receiver")]
    public void ForceRefresh()
    {
        _nextRefreshTime = Time.unscaledTime + ResolveRefreshInterval();

        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        if (_bindings == null || _bindings.Length == 0)
            CacheRendererBindings();

        ApplyProbes();
    }

    public string BuildDiagnostics()
    {
        ResolveDoor();

        int fixedSideBindings = 0;
        if (_bindings != null)
        {
            for (int i = 0; i < _bindings.Length; i++)
            {
                if (_bindings[i].group != DungeonDoorProbeRendererGroup.Group.Edge &&
                    _bindings[i].fixedTargetTile != null)
                {
                    fixedSideBindings++;
                }
            }
        }

        return
            $"{name} tileA={(door != null && door.TileA != null ? door.TileA.name : "null")} " +
            $"tileB={(door != null && door.TileB != null ? door.TileB.name : "null")} " +
            $"bindings={(_bindings != null ? _bindings.Length : 0)} " +
            $"fixedSideBindings={fixedSideBindings} " +
            $"animatedSideBlend={blendSideProbesByAnimatedPose}";
    }

    private float ResolveRefreshInterval()
    {
        float interval = Mathf.Max(0.02f, updateInterval);
        if (blendSideProbesByAnimatedPose)
            interval = Mathf.Min(interval, Mathf.Max(0.01f, animatedSideRefreshInterval));

        return interval;
    }

    private void ResolveDoor()
    {
        if (door == null)
            door = GetComponent<DunGen.Door>();

        if (door == null)
            door = GetComponentInParent<DunGen.Door>();
    }

    private void CacheRendererBindings()
    {
        var groups = GetComponentsInChildren<DungeonDoorProbeRendererGroup>(includeInactiveRenderers);
        if (groups != null && groups.Length > 0)
        {
            var bindings = new RendererBinding[groups.Length];
            int count = 0;
            for (int i = 0; i < groups.Length; i++)
            {
                var group = groups[i];
                if (group == null)
                    continue;

                var renderer = group.GetComponent<Renderer>();
                if (renderer == null)
                    renderer = group.GetComponentInChildren<Renderer>(includeInactiveRenderers);

                if (renderer == null)
                    continue;

                bindings[count++] = BuildBinding(renderer, group.ProbeGroup, group.transform);
            }

            Array.Resize(ref bindings, count);
            _bindings = bindings;
            return;
        }

        var renderers = GetComponentsInChildren<Renderer>(includeInactiveRenderers);
        var fallbackBindings = new RendererBinding[renderers.Length];
        int fallbackCount = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            fallbackBindings[fallbackCount++] = BuildBinding(renderer, DungeonDoorProbeRendererGroup.Group.Edge, renderer.transform);
        }

        Array.Resize(ref fallbackBindings, fallbackCount);
        _bindings = fallbackBindings;
    }

    private RendererBinding BuildBinding(
        Renderer renderer,
        DungeonDoorProbeRendererGroup.Group group,
        Transform orientationRoot)
    {
        Tile fixedTargetTile = null;
        if (renderer != null &&
            group != DungeonDoorProbeRendererGroup.Group.Edge &&
            door != null &&
            door.TileA != null &&
            door.TileB != null)
        {
            Vector3 initialNormal = ResolveWorldNormal(group, orientationRoot, renderer.transform);
            fixedTargetTile = ResolveFacingTile(renderer.bounds.center, initialNormal);
        }

        return new RendererBinding
        {
            renderer = renderer,
            group = group,
            orientationRoot = orientationRoot,
            fixedTargetTile = fixedTargetTile,
            originalLightProbeUsage = renderer != null ? renderer.lightProbeUsage : LightProbeUsage.Off,
            materialCount = renderer != null && renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0
        };
    }

    private void SubscribePowerEvents()
    {
        UnsubscribePowerEvents();

        if (door == null)
            return;

        _powerHandler = _ => ForceRefresh();
        _subscribedTileA = FindSwitcher(door.TileA);
        _subscribedTileB = FindSwitcher(door.TileB);

        if (_subscribedTileA != null)
            _subscribedTileA.PowerLevelApplied += _powerHandler;

        if (_subscribedTileB != null && _subscribedTileB != _subscribedTileA)
            _subscribedTileB.PowerLevelApplied += _powerHandler;
    }

    private void UnsubscribePowerEvents()
    {
        if (_powerHandler != null)
        {
            if (_subscribedTileA != null)
                _subscribedTileA.PowerLevelApplied -= _powerHandler;

            if (_subscribedTileB != null && _subscribedTileB != _subscribedTileA)
                _subscribedTileB.PowerLevelApplied -= _powerHandler;
        }

        _powerHandler = null;
        _subscribedTileA = null;
        _subscribedTileB = null;
    }

    private static DungeonTileLightmapSwitcher FindSwitcher(Tile tile)
    {
        return tile != null ? tile.GetComponentInChildren<DungeonTileLightmapSwitcher>(true) : null;
    }

    private void ApplyProbes()
    {
        DungeonTileProbeRegistry activeRegistry = registry != null ? registry : DungeonTileProbeRegistry.Active;
        if (activeRegistry == null || door == null || door.TileA == null || door.TileB == null || _bindings == null)
            return;

        for (int i = 0; i < _bindings.Length; i++)
        {
            Renderer renderer = _bindings[i].renderer;
            if (renderer == null)
                continue;

            if (!TryResolveProbe(activeRegistry, _bindings[i], out var probe, out var occlusion, out var info))
                continue;

            SetBindingProbeTarget(i, probe, occlusion);
            _hasAppliedCustomProbe = true;

            if (logSampleDiagnostics)
            {
                string blend = info.spatialBlendActive
                    ? $" blend={info.blendZoneName} {info.tileAWeight:0.00}/{info.tileBWeight:0.00}"
                    : string.Empty;
                Debug.Log($"[DungeonDoorDualSideProbeReceiver] {renderer.name} group={_bindings[i].group} tile={info.tileName}{blend} l0={info.l0Luminance:0.000}", renderer);
            }
        }

        if (smoothProbeChanges)
            ApplySmoothedProbeTargets();
    }

    private void SetBindingProbeTarget(int bindingIndex, SphericalHarmonicsL2 probe, Vector4 occlusion)
    {
        RendererBinding binding = _bindings[bindingIndex];
        binding.targetProbe = probe;
        binding.targetOcclusion = occlusion;
        binding.hasTargetProbe = true;

        if (!smoothProbeChanges || !binding.hasCurrentProbe)
        {
            binding.currentProbe = probe;
            binding.currentOcclusion = occlusion;
            binding.hasCurrentProbe = true;
            ApplyProbeToRenderer(binding.renderer, binding.currentProbe, binding.currentOcclusion, binding.materialCount);
        }

        _bindings[bindingIndex] = binding;
    }

    private void ApplySmoothedProbeTargets()
    {
        if (_bindings == null || _bindings.Length == 0)
            return;

        float smoothTime = Mathf.Max(0.01f, probeSmoothTime);
        float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / smoothTime);

        for (int i = 0; i < _bindings.Length; i++)
        {
            RendererBinding binding = _bindings[i];
            if (binding.renderer == null || !binding.hasTargetProbe)
                continue;

            if (!binding.hasCurrentProbe)
            {
                binding.currentProbe = binding.targetProbe;
                binding.currentOcclusion = binding.targetOcclusion;
                binding.hasCurrentProbe = true;
            }
            else
            {
                LerpProbe(ref binding.currentProbe, binding.targetProbe, blend);
                binding.currentOcclusion = Vector4.Lerp(binding.currentOcclusion, binding.targetOcclusion, blend);
            }

            ApplyProbeToRenderer(binding.renderer, binding.currentProbe, binding.currentOcclusion, binding.materialCount);
            _bindings[i] = binding;
        }
    }

    private bool TryResolveProbe(
        DungeonTileProbeRegistry activeRegistry,
        RendererBinding binding,
        out SphericalHarmonicsL2 probe,
        out Vector4 occlusion,
        out DungeonTileProbeRegistry.SampleInfo info)
    {
        probe = default;
        occlusion = Vector4.one;
        info = default;

        Bounds bounds = binding.renderer.bounds;
        Vector3 center = bounds.center;

        if (binding.group == DungeonDoorProbeRendererGroup.Group.Edge)
        {
            return TryResolveEdgeProbe(activeRegistry, center, out probe, out occlusion, out info);
        }

        Vector3 normal = ResolveWorldNormal(binding);
        Vector3 samplePosition = center + normal * sideSampleOffset;

        if (blendSideProbesByAnimatedPose &&
            TryResolveAnimatedSideProbe(activeRegistry, binding, center, normal, samplePosition, out probe, out occlusion, out info))
        {
            return true;
        }

        Tile targetTile = binding.fixedTargetTile != null
            ? binding.fixedTargetTile
            : ResolveFacingTile(center, normal);
        if (targetTile != null &&
            activeRegistry.TrySampleForTile(targetTile, samplePosition, out probe, out occlusion, out info))
        {
            return true;
        }

        return activeRegistry.TrySample(samplePosition, out probe, out occlusion, out info);
    }

    private bool TryResolveAnimatedSideProbe(
        DungeonTileProbeRegistry activeRegistry,
        RendererBinding binding,
        Vector3 center,
        Vector3 normal,
        Vector3 samplePosition,
        out SphericalHarmonicsL2 probe,
        out Vector4 occlusion,
        out DungeonTileProbeRegistry.SampleInfo info)
    {
        probe = default;
        occlusion = Vector4.one;
        info = default;

        bool hasA = activeRegistry.TrySampleForTile(door.TileA, samplePosition, out var probeA, out var occA, out var infoA);
        bool hasB = activeRegistry.TrySampleForTile(door.TileB, samplePosition, out var probeB, out var occB, out var infoB);

        if (hasA && hasB)
        {
            float weightA = CalculateAnimatedSideTileAWeight(center, normal, binding.fixedTargetTile);
            float weightB = 1f - weightA;

            BlendProbes(out probe, probeA, probeB, weightA, weightB);
            occlusion = occA * weightA + occB * weightB;
            info = new DungeonTileProbeRegistry.SampleInfo
            {
                tileName = $"{infoA.tileName}+{infoB.tileName}",
                probeCount = infoA.probeCount + infoB.probeCount,
                blendedProbeCount = infoA.blendedProbeCount + infoB.blendedProbeCount,
                l0Luminance = infoA.l0Luminance * weightA + infoB.l0Luminance * weightB,
                spatialBlendActive = true,
                blendZoneName = "DoorAnimatedSidePose",
                tileAName = infoA.tileName,
                tileBName = infoB.tileName,
                tileAWeight = weightA,
                tileBWeight = weightB
            };
            return true;
        }

        if (hasA)
        {
            probe = probeA;
            occlusion = occA;
            info = infoA;
            return true;
        }

        if (hasB)
        {
            probe = probeB;
            occlusion = occB;
            info = infoB;
            return true;
        }

        return false;
    }

    private float CalculateAnimatedSideTileAWeight(Vector3 center, Vector3 normal, Tile fallbackTile)
    {
        Vector3 dirA = DirectionToTile(center, door.TileA);
        Vector3 dirB = DirectionToTile(center, door.TileB);
        Vector3 axisToA = dirA - dirB;

        if (axisToA.sqrMagnitude <= 0.0001f)
        {
            if (fallbackTile == door.TileA)
                return 1f;

            if (fallbackTile == door.TileB)
                return 0f;

            return 0.5f;
        }

        float signedFacing = Vector3.Dot(normal, axisToA.normalized);
        float weightA = Mathf.Clamp01(0.5f + signedFacing * 0.5f);
        return Mathf.SmoothStep(0f, 1f, weightA);
    }

    private bool TryResolveEdgeProbe(
        DungeonTileProbeRegistry activeRegistry,
        Vector3 center,
        out SphericalHarmonicsL2 probe,
        out Vector4 occlusion,
        out DungeonTileProbeRegistry.SampleInfo info)
    {
        probe = default;
        occlusion = Vector4.one;
        info = default;

        Vector3 dirA = DirectionToTile(center, door.TileA);
        Vector3 dirB = DirectionToTile(center, door.TileB);

        bool hasA = activeRegistry.TrySampleForTile(door.TileA, center + dirA * sideSampleOffset, out var probeA, out var occA, out var infoA);
        bool hasB = activeRegistry.TrySampleForTile(door.TileB, center + dirB * sideSampleOffset, out var probeB, out var occB, out var infoB);

        if (hasA && hasB)
        {
            AddProbe(ref probe, probeA);
            AddProbe(ref probe, probeB);
            ScaleProbe(ref probe, 0.5f);
            occlusion = (occA + occB) * 0.5f;
            info = new DungeonTileProbeRegistry.SampleInfo
            {
                tileName = $"{infoA.tileName}+{infoB.tileName}",
                probeCount = infoA.probeCount + infoB.probeCount,
                blendedProbeCount = infoA.blendedProbeCount + infoB.blendedProbeCount,
                l0Luminance = (infoA.l0Luminance + infoB.l0Luminance) * 0.5f,
                blendZoneName = "DoorEdgeAverage",
                tileAName = infoA.tileName,
                tileBName = infoB.tileName,
                tileAWeight = 0.5f,
                tileBWeight = 0.5f
            };
            return true;
        }

        if (hasA)
        {
            probe = probeA;
            occlusion = occA;
            info = infoA;
            return true;
        }

        if (hasB)
        {
            probe = probeB;
            occlusion = occB;
            info = infoB;
            return true;
        }

        return activeRegistry.TrySample(center, out probe, out occlusion, out info);
    }

    private Vector3 ResolveWorldNormal(RendererBinding binding)
    {
        return ResolveWorldNormal(
            binding.group,
            binding.orientationRoot,
            binding.renderer != null ? binding.renderer.transform : transform);
    }

    private Vector3 ResolveWorldNormal(
        DungeonDoorProbeRendererGroup.Group group,
        Transform orientationRoot,
        Transform fallbackRoot)
    {
        Transform root = orientationRoot != null ? orientationRoot : fallbackRoot;
        if (root == null)
            root = transform;

        Vector3 normal = root.TransformDirection(Vector3.forward);

        if (group == DungeonDoorProbeRendererGroup.Group.NegativeZ)
            normal = -normal;

        if (normal.sqrMagnitude <= 0.0001f)
            normal = transform.forward;

        return normal.normalized;
    }

    private Tile ResolveFacingTile(Vector3 center, Vector3 normal)
    {
        Vector3 dirA = DirectionToTile(center, door.TileA);
        Vector3 dirB = DirectionToTile(center, door.TileB);

        float dotA = Vector3.Dot(normal, dirA);
        float dotB = Vector3.Dot(normal, dirB);
        return dotA >= dotB ? door.TileA : door.TileB;
    }

    private static Vector3 DirectionToTile(Vector3 center, Tile tile)
    {
        if (tile == null)
            return Vector3.zero;

        Vector3 direction = tile.Bounds.center - center;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    private void ApplyProbeToRenderer(Renderer renderer, SphericalHarmonicsL2 probe, Vector4 occlusion, int materialCount)
    {
        if (renderer.lightProbeUsage != LightProbeUsage.CustomProvided)
            renderer.lightProbeUsage = LightProbeUsage.CustomProvided;

        _singleProbe[0] = probe;
        _singleOcclusion[0] = occlusion;

        if (materialCount <= 0)
        {
            renderer.GetPropertyBlock(_propertyBlock);
            CopyProbeDataToBlock();
            renderer.SetPropertyBlock(_propertyBlock);
            return;
        }

        for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
        {
            renderer.GetPropertyBlock(_propertyBlock, materialIndex);
            CopyProbeDataToBlock();
            renderer.SetPropertyBlock(_propertyBlock, materialIndex);
        }
    }

    private void CopyProbeDataToBlock()
    {
        _propertyBlock.CopySHCoefficientArraysFrom(_singleProbe);
        _propertyBlock.CopyProbeOcclusionArrayFrom(_singleOcclusion);
    }

    private void RestoreOriginalProbeUsage()
    {
        if (!_hasAppliedCustomProbe || _bindings == null)
            return;

        for (int i = 0; i < _bindings.Length; i++)
        {
            Renderer renderer = _bindings[i].renderer;
            if (renderer != null)
                renderer.lightProbeUsage = _bindings[i].originalLightProbeUsage;
        }

        _hasAppliedCustomProbe = false;
    }

    private static void AddProbe(ref SphericalHarmonicsL2 destination, SphericalHarmonicsL2 source)
    {
        for (int rgb = 0; rgb < 3; rgb++)
        {
            for (int coefficient = 0; coefficient < 9; coefficient++)
                destination[rgb, coefficient] += source[rgb, coefficient];
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

    private static void BlendProbes(
        out SphericalHarmonicsL2 destination,
        SphericalHarmonicsL2 probeA,
        SphericalHarmonicsL2 probeB,
        float weightA,
        float weightB)
    {
        destination = default;

        for (int rgb = 0; rgb < 3; rgb++)
        {
            for (int coefficient = 0; coefficient < 9; coefficient++)
                destination[rgb, coefficient] = probeA[rgb, coefficient] * weightA + probeB[rgb, coefficient] * weightB;
        }
    }

    private static void LerpProbe(ref SphericalHarmonicsL2 current, SphericalHarmonicsL2 target, float blend)
    {
        blend = Mathf.Clamp01(blend);

        for (int rgb = 0; rgb < 3; rgb++)
        {
            for (int coefficient = 0; coefficient < 9; coefficient++)
                current[rgb, coefficient] = Mathf.Lerp(current[rgb, coefficient], target[rgb, coefficient], blend);
        }
    }

    private struct RendererBinding
    {
        public Renderer renderer;
        public DungeonDoorProbeRendererGroup.Group group;
        public Transform orientationRoot;
        public Tile fixedTargetTile;
        public LightProbeUsage originalLightProbeUsage;
        public int materialCount;
        public SphericalHarmonicsL2 currentProbe;
        public SphericalHarmonicsL2 targetProbe;
        public Vector4 currentOcclusion;
        public Vector4 targetOcclusion;
        public bool hasCurrentProbe;
        public bool hasTargetProbe;
    }
}
