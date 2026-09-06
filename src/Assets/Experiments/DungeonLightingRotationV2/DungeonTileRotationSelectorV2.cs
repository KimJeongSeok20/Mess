using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DungeonTilePowerBakeSet))]
[RequireComponent(typeof(DungeonTileLightmapSwitcher))]
public sealed class DungeonTileRotationSelectorV2 : MonoBehaviour
{
    [SerializeField] private DungeonTileRotationLightingSetV2 lightingSet;
    [SerializeField] private bool logSelection;

    private DungeonTilePowerBakeSet _powerBakeSet;
    private DungeonTileLightmapSwitcher _switcher;
    private int _selectedRotation = -1;

    public DungeonTileRotationLightingSetV2 LightingSet => lightingSet;
    public int SelectedRotation => _selectedRotation;

    public void Configure(DungeonTileRotationLightingSetV2 set)
    {
        lightingSet = set;
    }

    private void Awake()
    {
        ApplyForCurrentRotation(false);
    }

    private void Start()
    {
        int current = DungeonTileRotationLightingSetV2.QuantizeRotation(transform.eulerAngles.y);
        if (current != _selectedRotation)
            ApplyForCurrentRotation(true);
    }

    [ContextMenu("Apply V2 Lighting For Current Rotation")]
    public void ReapplyForCurrentRotation()
    {
        ApplyForCurrentRotation(true);
    }

    public bool ApplyForCurrentRotation(bool reapplySwitcher)
    {
        if (lightingSet == null)
        {
            Debug.LogWarning(
                "[DungeonTileRotationSelectorV2] Missing LightingSet on '" + name +
                "'. Baked lightmaps will not apply until the V2 lighting set is rewired.",
                this);
            return false;
        }

        _powerBakeSet ??= GetComponent<DungeonTilePowerBakeSet>();
        _switcher ??= GetComponent<DungeonTileLightmapSwitcher>();
        if (_powerBakeSet == null || _switcher == null)
            return false;

        DungeonTileRotationLightingSetV2.RotationVariant variant = lightingSet.Resolve(transform.eulerAngles.y);
        if (variant == null)
            return false;

        _selectedRotation = DungeonTileRotationLightingSetV2.QuantizeRotation(variant.rotationY);
        DungeonTileBakeData power0 = _switcher.SupportsPowerToggle
            ? variant.power0
            : variant.power100;
        _powerBakeSet.ConfigureRuntimeBakeData(variant.power100, power0);
        _switcher.ConfigurePowerBakeSet(_powerBakeSet);
        ApplyReflectionTextures(variant, _switcher.SupportsPowerToggle);

        if (reapplySwitcher)
            _switcher.ReapplyCurrentBakeData();

        if (logSelection)
            Debug.Log($"[DungeonTileRotationSelectorV2] tile={name} rotation=R{_selectedRotation:000}", this);
        return true;
    }

    private void ApplyReflectionTextures(
        DungeonTileRotationLightingSetV2.RotationVariant variant,
        bool includePower0)
    {
        var probesByPath = new Dictionary<string, List<ReflectionProbe>>(StringComparer.Ordinal);
        foreach (ReflectionProbe probe in GetComponentsInChildren<ReflectionProbe>(true))
        {
            if (probe == null)
                continue;
            string path = GetRelativePath(transform, probe.transform);
            if (!probesByPath.TryGetValue(path, out List<ReflectionProbe> bucket))
            {
                bucket = new List<ReflectionProbe>();
                probesByPath.Add(path, bucket);
            }
            bucket.Add(probe);
        }

        ApplyReflectionData(variant.power100, probesByPath, usePower0NamedProbes: false);
        if (includePower0)
            ApplyReflectionData(variant.power0, probesByPath, usePower0NamedProbes: true);
    }

    private static void ApplyReflectionData(
        DungeonTileBakeData data,
        Dictionary<string, List<ReflectionProbe>> probesByPath,
        bool usePower0NamedProbes)
    {
        if (data == null || data.reflectionProbeEntries == null)
            return;

        var useCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (DungeonTileBakeData.ReflectionProbeBakeEntry entry in data.reflectionProbeEntries)
        {
            bool isPower0Path = entry.relativePath.EndsWith("_P0", StringComparison.OrdinalIgnoreCase);
            if (isPower0Path != usePower0NamedProbes)
                continue;
            if (!probesByPath.TryGetValue(entry.relativePath, out List<ReflectionProbe> bucket) || bucket.Count == 0)
                continue;
            useCounts.TryGetValue(entry.relativePath, out int index);
            if (index >= bucket.Count)
                continue;
            ReflectionProbe probe = bucket[index];
            useCounts[entry.relativePath] = index + 1;
            probe.customBakedTexture = entry.bakedTexture;
            probe.mode = ReflectionProbeMode.Custom;
        }
    }

    private static string GetRelativePath(Transform root, Transform target)
    {
        if (root == target)
            return string.Empty;
        var names = new Stack<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            names.Push(current.name);
            current = current.parent;
        }
        return string.Join("/", names);
    }
}
