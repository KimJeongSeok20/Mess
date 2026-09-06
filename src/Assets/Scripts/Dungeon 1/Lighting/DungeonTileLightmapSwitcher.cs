using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class DungeonTileLightmapSwitcher : MonoBehaviour
{
    public enum LightingMode
    {
        PowerToggle,
        SingleBakedState,
    }

    public enum PowerLevel
    {
        P100,
        P0
    }

    public enum ReflectionProbeApplyMode
    {
        CustomFromBakeData,
        ForceBakedMode,
        ProbeVariantSet
    }

    [Serializable]
    public struct ReflectionProbeVariantEntry
    {
        public string relativePath;
        public int probeBucketIndex;
        public ReflectionProbe power100Probe;
        public ReflectionProbe power00Probe;
    }

    [SerializeField] private DungeonTilePowerBakeSet powerBakeSet;
    [SerializeField] private bool applyOnAwake = true;
    [SerializeField, Tooltip("PowerToggle uses P100/P0. SingleBakedState always keeps the canonical P100 bake.")]
    private LightingMode lightingMode = LightingMode.PowerToggle;
    [SerializeField] private PowerLevel startPowerLevel = PowerLevel.P100;
    [SerializeField] private ReflectionProbeApplyMode reflectionProbeApplyMode = ReflectionProbeApplyMode.CustomFromBakeData;
    [SerializeField] private bool disableReflectionProbesOnPower0 = true;
    [SerializeField] private ReflectionProbeVariantEntry[] reflectionProbeVariantEntries = Array.Empty<ReflectionProbeVariantEntry>();

    private readonly Dictionary<string, List<Renderer>> _renderersByPath = new Dictionary<string, List<Renderer>>();
    private readonly Dictionary<string, int> _rendererUseCountByPath = new Dictionary<string, int>();
    private readonly Dictionary<string, List<ReflectionProbe>> _reflectionProbesByPath = new Dictionary<string, List<ReflectionProbe>>();
    private readonly Dictionary<string, int> _reflectionProbeUseCountByPath = new Dictionary<string, int>();

    private PowerLevel _currentPowerLevel = PowerLevel.P100;

    // One temporary probe only. It draws the exact atlas rectangle used by a
    // renderer, so it can distinguish a missing/black runtime lightmap from a normal
    // material or render-path failure without changing the renderer's materials.
    // It remains disabled in the source project after the dedicated probe build.
    private const bool RuntimeLightmapProbeEnabled = false;
    private static DungeonTileLightmapSwitcher s_runtimeProbeOwner;
    private static Texture2D s_runtimeProbeTexture;
    private static Vector4 s_runtimeProbeScaleOffset;
    private static string s_runtimeProbeLabel;

    public PowerLevel CurrentPowerLevel => _currentPowerLevel;
    public DungeonTileBakeData CurrentBakeData => ResolveBakeData(_currentPowerLevel);
    public LightingMode Mode => ResolveLightingMode();
    public bool SupportsPowerToggle => ResolveLightingMode() == LightingMode.PowerToggle;
    public event Action<PowerLevel> PowerLevelApplied;

    public bool HasAssignedBakeData => ResolveAnyBakeData() != null;

    private void Awake()
    {
        CacheRenderers();
        CacheReflectionProbes();

        if (powerBakeSet == null)
            powerBakeSet = GetComponent<DungeonTilePowerBakeSet>();

        DisableTileLights();

        if (!applyOnAwake)
            return;

        SetPowerLevel(startPowerLevel);
    }

    public void ConfigurePowerBakeSet(DungeonTilePowerBakeSet set)
    {
        powerBakeSet = set;
    }

    public void ConfigureLightingMode(LightingMode mode)
    {
        lightingMode = mode;
        startPowerLevel = NormalizePowerLevel(startPowerLevel);
        _currentPowerLevel = NormalizePowerLevel(_currentPowerLevel);
    }

    public void ConfigureRuntimeState(PowerLevel initialPowerLevel)
    {
        startPowerLevel = NormalizePowerLevel(initialPowerLevel);
        DisableTileLights();
    }

    public void RefreshControlledLights()
    {
        DisableTileLights();
    }

    [ContextMenu("Apply Power P100")]
    public void ApplyPower100Context()
    {
        SetPowerLevel(PowerLevel.P100);
    }

    [ContextMenu("Apply Power P0")]
    public void ApplyPower0Context()
    {
        SetPowerLevel(PowerLevel.P0);
    }

    public void SetPowerPercent(float normalizedPower)
    {
        float clamped = Mathf.Clamp01(normalizedPower);
        if (clamped <= 0.0001f)
        {
            SetPowerLevel(PowerLevel.P0);
            return;
        }

        SetPowerLevel(PowerLevel.P100);
    }

    public DungeonTileBakeData GetBakeData(PowerLevel level)
    {
        return ResolveBakeData(level);
    }

    public void SetPowerLevel(PowerLevel level)
    {
        PowerLevel appliedLevel = NormalizePowerLevel(level);
        _currentPowerLevel = appliedLevel;

        ApplyEmissionMaterials(appliedLevel);
        ApplyBakeData(ResolveBakeData(appliedLevel));
        ApplyReflectionProbeVariants(appliedLevel);
        ClampEnabledReflectionProbesToTile();
        PowerLevelApplied?.Invoke(appliedLevel);
    }

    public void ReapplyCurrentBakeData()
    {
        ApplyEmissionMaterials(_currentPowerLevel);
        ApplyBakeData(ResolveBakeData(_currentPowerLevel));
        ApplyReflectionProbeVariants(_currentPowerLevel);
        ClampEnabledReflectionProbesToTile();
    }



    private void DisableTileLights()
    {
        var lights = GetComponentsInChildren<Light>(true);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null)
                lights[i].enabled = false;
        }
    }


    private static bool HasEnabledIgnoreEmissionControl(Component component)
    {
        if (component == null)
            return false;

        var marker = component.GetComponentInParent<IgnoreEmissionControl>(true);
        return marker != null && marker.enabled;
    }


    private void ApplyEmissionMaterials(PowerLevel level)
    {
        var set = ResolvePowerBakeSet();
        if (set == null)
            return;

        var entries = set.EmissionMaterialEntries;
        if (entries == null || entries.Length == 0)
            return;

        var rendererMaterials = new Dictionary<Renderer, Material[]>();
        var changedRenderers = new HashSet<Renderer>();

        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (string.IsNullOrWhiteSpace(entry.relativePath))
                continue;

            if (!_renderersByPath.TryGetValue(entry.relativePath, out var bucket) || bucket == null || bucket.Count == 0)
                continue;

            int rendererIndex = Mathf.Clamp(entry.rendererBucketIndex, 0, bucket.Count - 1);
            var renderer = bucket[rendererIndex];
            if (renderer == null)
                continue;

            if (HasEnabledIgnoreEmissionControl(renderer))
                continue;

            if (!rendererMaterials.TryGetValue(renderer, out var materials))
            {
                var sourceMaterials = renderer.sharedMaterials;
                if (sourceMaterials == null || sourceMaterials.Length == 0)
                    continue;

                materials = (Material[])sourceMaterials.Clone();
                rendererMaterials[renderer] = materials;
            }

            if (entry.materialIndex < 0 || entry.materialIndex >= materials.Length)
                continue;

            var replacement = ResolveEmissionMaterial(entry, level);
            if (replacement == null)
                continue;

            if (materials[entry.materialIndex] == replacement)
                continue;

            materials[entry.materialIndex] = replacement;
            changedRenderers.Add(renderer);
        }

        foreach (var renderer in changedRenderers)
        {
            if (renderer == null)
                continue;

            if (!rendererMaterials.TryGetValue(renderer, out var materials) || materials == null)
                continue;

            renderer.sharedMaterials = materials;
        }
    }

    private static Material ResolveEmissionMaterial(DungeonTilePowerBakeSet.EmissionMaterialEntry entry, PowerLevel level)
    {
        switch (level)
        {
            case PowerLevel.P100:
                return FirstNonNullMaterial(entry.power100Material, entry.power00Material);
            default:
                return FirstNonNullMaterial(entry.power00Material, entry.power100Material);
        }
    }

    private static Material FirstNonNullMaterial(params Material[] candidates)
    {
        if (candidates == null)
            return null;

        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i] != null)
                return candidates[i];
        }

        return null;
    }

    private DungeonTileBakeData ResolveBakeData(PowerLevel level)
    {
        var set = ResolvePowerBakeSet();
        if (set == null)
            return null;

        level = NormalizePowerLevel(level);

        switch (level)
        {
            case PowerLevel.P100:
                return FirstNonNull(set.Power100Bake, set.Power00Bake);
            default:
                return FirstNonNull(set.Power00Bake, set.Power100Bake);
        }
    }

    private DungeonTileBakeData ResolveAnyBakeData()
    {
        var set = ResolvePowerBakeSet();
        if (set == null)
            return null;

        return FirstNonNull(set.Power100Bake, set.Power00Bake);
    }

    private DungeonTilePowerBakeSet ResolvePowerBakeSet()
    {
        if (powerBakeSet == null)
            powerBakeSet = GetComponent<DungeonTilePowerBakeSet>();

        return powerBakeSet;
    }

    private static DungeonTileBakeData FirstNonNull(params DungeonTileBakeData[] candidates)
    {
        if (candidates == null)
            return null;

        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i] != null)
                return candidates[i];
        }

        return null;
    }

    private void CacheRenderers()
    {
        _renderersByPath.Clear();
        var root = transform;
        var renderers = GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            string path = GetRelativePath(root, renderer.transform);
            if (!_renderersByPath.TryGetValue(path, out var bucket))
            {
                bucket = new List<Renderer>(1);
                _renderersByPath[path] = bucket;
            }

            bucket.Add(renderer);
        }
    }

    private void CacheReflectionProbes()
    {
        _reflectionProbesByPath.Clear();
        var root = transform;
        var probes = GetComponentsInChildren<ReflectionProbe>(true);

        for (int i = 0; i < probes.Length; i++)
        {
            var probe = probes[i];
            if (probe == null)
                continue;

            string path = GetRelativePath(root, probe.transform);
            if (!_reflectionProbesByPath.TryGetValue(path, out var bucket))
            {
                bucket = new List<ReflectionProbe>(1);
                _reflectionProbesByPath[path] = bucket;
            }

            bucket.Add(probe);
        }
    }

    private void ApplyBakeData(DungeonTileBakeData data)
    {
        if (data == null)
        {
            LogBakeDiagnostics(null, 0, 0, 0, 0, 0, 0);
            return;
        }

        int lightmapCountBefore = LightmapSettings.lightmaps != null ? LightmapSettings.lightmaps.Length : 0;
        int[] remap = RegisterLightmaps(data);
        var entries = data.rendererEntries ?? Array.Empty<DungeonTileBakeData.RendererBakeEntry>();
        int appliedRendererCount = 0;
        int missingRendererPathCount = 0;
        int exhaustedRendererBucketCount = 0;
        int invalidLightmapIndexCount = 0;
        int nullRendererCount = 0;

        _rendererUseCountByPath.Clear();

        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];

            if (!_renderersByPath.TryGetValue(entry.relativePath, out var bucket) || bucket == null || bucket.Count == 0)
            {
                missingRendererPathCount++;
                continue;
            }

            _rendererUseCountByPath.TryGetValue(entry.relativePath, out int bucketIndex);
            if (bucketIndex < 0 || bucketIndex >= bucket.Count)
            {
                exhaustedRendererBucketCount++;
                continue;
            }

            var renderer = bucket[bucketIndex];
            _rendererUseCountByPath[entry.relativePath] = bucketIndex + 1;

            if (renderer == null)
            {
                nullRendererCount++;
                continue;
            }

            if (entry.lightmapIndex < 0)
            {
                renderer.lightmapIndex = -1;
                renderer.lightmapScaleOffset = entry.lightmapScaleOffset;
                appliedRendererCount++;
                continue;
            }

            if (entry.lightmapIndex >= remap.Length)
            {
                invalidLightmapIndexCount++;
                continue;
            }

            renderer.lightmapIndex = remap[entry.lightmapIndex];
            renderer.lightmapScaleOffset = entry.lightmapScaleOffset;
            appliedRendererCount++;
        }

        LogBakeDiagnostics(
            data,
            lightmapCountBefore,
            appliedRendererCount,
            missingRendererPathCount,
            exhaustedRendererBucketCount,
            invalidLightmapIndexCount,
            nullRendererCount);

        TryStartRuntimeLightmapProbe();

        if (!ShouldSuppressReflectionProbes(_currentPowerLevel) &&
            reflectionProbeApplyMode != ReflectionProbeApplyMode.ProbeVariantSet)
        {
            ApplyReflectionProbeData(data.reflectionProbeEntries ?? Array.Empty<DungeonTileBakeData.ReflectionProbeBakeEntry>());
        }
    }

    private void LogBakeDiagnostics(
        DungeonTileBakeData data,
        int lightmapCountBefore,
        int appliedRendererCount,
        int missingRendererPathCount,
        int exhaustedRendererBucketCount,
        int invalidLightmapIndexCount,
        int nullRendererCount)
    {
        if (!Application.isEditor && !Debug.isDebugBuild)
            return;

        int lightmapCountAfter = LightmapSettings.lightmaps != null ? LightmapSettings.lightmaps.Length : 0;
        int colorCount = data != null && data.lightmapColors != null ? data.lightmapColors.Length : 0;
        int directionCount = data != null && data.lightmapDirections != null ? data.lightmapDirections.Length : 0;
        int entryCount = data != null && data.rendererEntries != null ? data.rendererEntries.Length : 0;
        string bakeName = data != null ? data.name : "<missing>";

        string message =
            $"[DungeonLightmapDiag] tile={name} power={_currentPowerLevel} bake={bakeName} " +
            $"maps={colorCount}/{directionCount} globalMaps={lightmapCountBefore}->{lightmapCountAfter} " +
            $"rendererEntries={entryCount} applied={appliedRendererCount} " +
            $"missingPath={missingRendererPathCount} exhaustedBucket={exhaustedRendererBucketCount} " +
            $"invalidMapIndex={invalidLightmapIndexCount} nullRenderer={nullRendererCount}";

        Debug.unityLogger.Log(LogType.Log, (object)message, this);

        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.persistentDataPath, "DungeonLightmapDiagnostics.log"),
                message + Environment.NewLine);
        }
        catch (Exception)
        {
            // Diagnostics must not affect dungeon generation if the log cannot be written.
        }
    }

    private void TryStartRuntimeLightmapProbe()
    {
        // This is intentionally disabled for normal editor/release operation.
        if (Application.isEditor || !RuntimeLightmapProbeEnabled || s_runtimeProbeOwner != null)
            return;

        Renderer fallback = null;
        Renderer selected = null;

        foreach (var pair in _renderersByPath)
        {
            var bucket = pair.Value;
            if (bucket == null)
                continue;

            for (int i = 0; i < bucket.Count; i++)
            {
                var renderer = bucket[i];
                if (renderer == null || renderer.lightmapIndex < 0)
                    continue;

                var lightmaps = LightmapSettings.lightmaps;
                if (lightmaps == null || renderer.lightmapIndex >= lightmaps.Length ||
                    lightmaps[renderer.lightmapIndex].lightmapColor == null)
                    continue;

                fallback ??= renderer;
                if (pair.Key.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    selected = renderer;
                    break;
                }
            }

            if (selected != null)
                break;
        }

        selected ??= fallback;
        if (selected == null)
            return;

        var maps = LightmapSettings.lightmaps;
        var map = maps[selected.lightmapIndex];
        var color = map.lightmapColor;
        if (color == null)
            return;

        s_runtimeProbeOwner = this;
        s_runtimeProbeTexture = color;
        s_runtimeProbeScaleOffset = selected.lightmapScaleOffset;
        var material = selected.sharedMaterial;
        string shaderName = material != null && material.shader != null ? material.shader.name : "<missing>";
        s_runtimeProbeLabel =
            $"LM probe: {name}/{selected.name}  index={selected.lightmapIndex}  " +
            $"atlas={color.name} {color.width}x{color.height} {color.format}\n" +
            $"UV scale/offset={s_runtimeProbeScaleOffset}  shader={shaderName}\n" +
            "Bright crop + dark room = material/render path. Dark crop = lightmap texture/decode.";

        WriteRuntimeProbeLog("[DungeonLightmapProbe] " + s_runtimeProbeLabel);
        StartCoroutine(CaptureRuntimeLightmapProbePixels());
    }

    private IEnumerator CaptureRuntimeLightmapProbePixels()
    {
        yield return new WaitForEndOfFrame();

        if (s_runtimeProbeOwner != this || s_runtimeProbeTexture == null)
            yield break;

        RenderTexture target = null;
        Texture2D readback = null;
        var previous = RenderTexture.active;

        try
        {
            target = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            Graphics.Blit(
                s_runtimeProbeTexture,
                target,
                new Vector2(s_runtimeProbeScaleOffset.x, s_runtimeProbeScaleOffset.y),
                new Vector2(s_runtimeProbeScaleOffset.z, s_runtimeProbeScaleOffset.w));

            RenderTexture.active = target;
            readback = new Texture2D(32, 32, TextureFormat.RGBAHalf, false, true);
            readback.ReadPixels(new Rect(0f, 0f, 32f, 32f), 0, 0, false);
            readback.Apply(false, false);

            var pixels = readback.GetPixels();
            float minLuminance = float.MaxValue;
            float maxLuminance = 0f;
            float totalLuminance = 0f;

            for (int i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                float luminance = Mathf.Max(0f, pixel.r * 0.2126f + pixel.g * 0.7152f + pixel.b * 0.0722f);
                minLuminance = Mathf.Min(minLuminance, luminance);
                maxLuminance = Mathf.Max(maxLuminance, luminance);
                totalLuminance += luminance;
            }

            float meanLuminance = pixels.Length > 0 ? totalLuminance / pixels.Length : 0f;
            string result =
                $"[DungeonLightmapProbe] GPU crop samples={pixels.Length} " +
                $"luminance min={minLuminance:F4} mean={meanLuminance:F4} max={maxLuminance:F4}";
            s_runtimeProbeLabel += "\n" + result;
            WriteRuntimeProbeLog(result);
        }
        catch (Exception exception)
        {
            WriteRuntimeProbeLog($"[DungeonLightmapProbe] GPU crop failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            RenderTexture.active = previous;
            if (target != null)
                RenderTexture.ReleaseTemporary(target);
            if (readback != null)
                Destroy(readback);
        }
    }

    private void OnGUI()
    {
        if (s_runtimeProbeOwner != this || s_runtimeProbeTexture == null)
            return;

        const float size = 220f;
        var box = new Rect(Screen.width - size - 20f, 20f, size, size + 86f);
        var preview = new Rect(box.x + 8f, box.y + 8f, size - 16f, size - 16f);
        GUI.Box(box, GUIContent.none);
        GUI.DrawTextureWithTexCoords(
            preview,
            s_runtimeProbeTexture,
            new Rect(
                s_runtimeProbeScaleOffset.z,
                s_runtimeProbeScaleOffset.w,
                s_runtimeProbeScaleOffset.x,
                s_runtimeProbeScaleOffset.y));
        GUI.Label(new Rect(box.x + 8f, preview.yMax + 4f, size - 16f, 78f), s_runtimeProbeLabel);
    }

    private static void WriteRuntimeProbeLog(string message)
    {
        Debug.Log(message);

        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.persistentDataPath, "DungeonLightmapDiagnostics.log"),
                message + Environment.NewLine);
        }
        catch (Exception)
        {
            // Diagnostics must not affect dungeon generation if the log cannot be written.
        }
    }

    private void ApplyReflectionProbeData(DungeonTileBakeData.ReflectionProbeBakeEntry[] entries)
    {
        if (entries == null || entries.Length == 0)
            return;

        _reflectionProbeUseCountByPath.Clear();

        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (!_reflectionProbesByPath.TryGetValue(entry.relativePath, out var bucket) || bucket == null || bucket.Count == 0)
                continue;

            _reflectionProbeUseCountByPath.TryGetValue(entry.relativePath, out int bucketIndex);
            if (bucketIndex < 0 || bucketIndex >= bucket.Count)
                continue;

            var probe = bucket[bucketIndex];
            _reflectionProbeUseCountByPath[entry.relativePath] = bucketIndex + 1;

            if (probe == null)
                continue;

            if (!probe.enabled)
                probe.enabled = true;

            ApplyReflectionProbeBake(probe, entry.bakedTexture, reflectionProbeApplyMode);
        }
    }

    private void ApplyReflectionProbeVariants(PowerLevel level)
    {
        if (ShouldSuppressReflectionProbes(level))
        {
            DisableAllReflectionProbes();
            return;
        }

        var entries = reflectionProbeVariantEntries;
        if (entries == null || entries.Length == 0)
            return;

        bool useVariantSet = reflectionProbeApplyMode == ReflectionProbeApplyMode.ProbeVariantSet;
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            ReflectionProbe target = useVariantSet ? ResolveVariantProbe(entry, level) : null;

            SetVariantProbeEnabled(entry.power100Probe, entry.power100Probe == target);
            SetVariantProbeEnabled(entry.power00Probe, entry.power00Probe == target);

            if (useVariantSet)
                DisableBaseProbe(entry);
        }
    }

    private bool ShouldSuppressReflectionProbes(PowerLevel level)
    {
        return NormalizePowerLevel(level) == PowerLevel.P0 && disableReflectionProbesOnPower0;
    }

    private PowerLevel NormalizePowerLevel(PowerLevel requested)
    {
        return SupportsPowerToggle ? requested : PowerLevel.P100;
    }

    private LightingMode ResolveLightingMode()
    {
        DungeonTileLightingPolicy policy = GetComponent<DungeonTileLightingPolicy>();
        return policy != null ? policy.Mode : lightingMode;
    }

    // Unity ReflectionProbe influence is an axis-aligned world box from
    // transform.position + center ± size/2. Tile yaw is ignored, so an R090/R270
    // cafeteria keeps its R000 (28,8,21) box and overlaps the next room.
    private const float ReflectionProbeTileInset = 0.2f;

    private void ClampEnabledReflectionProbesToTile()
    {
        if (!TryGetTileWorldBounds(out Bounds tileBounds))
            return;

        Bounds insetBounds = tileBounds;
        insetBounds.Expand(-ReflectionProbeTileInset);
        if (insetBounds.size.x < 0.5f || insetBounds.size.y < 0.5f || insetBounds.size.z < 0.5f)
            insetBounds = tileBounds;

        var enabled = new List<ReflectionProbe>(4);
        foreach (var pair in _reflectionProbesByPath)
        {
            var bucket = pair.Value;
            if (bucket == null)
                continue;

            for (int i = 0; i < bucket.Count; i++)
            {
                var probe = bucket[i];
                if (probe != null && probe.enabled)
                    enabled.Add(probe);
            }
        }

        for (int i = 0; i < enabled.Count; i++)
        {
            ReflectionProbe probe = enabled[i];
            Bounds target = enabled.Count == 1
                ? insetBounds
                : IntersectBounds(probe.bounds, insetBounds);
            if (target.size.x < 0.25f || target.size.y < 0.25f || target.size.z < 0.25f)
                continue;

            probe.center = target.center - probe.transform.position;
            probe.size = target.size;
        }
    }

    private bool TryGetTileWorldBounds(out Bounds bounds)
    {
        var tile = GetComponent<DunGen.Tile>();
        if (tile == null)
            tile = GetComponentInParent<DunGen.Tile>();
        if (tile != null && tile.Bounds.size.sqrMagnitude > 0.01f)
        {
            bounds = tile.Bounds;
            return true;
        }

        bool has = false;
        bounds = default;
        foreach (var pair in _renderersByPath)
        {
            var bucket = pair.Value;
            if (bucket == null)
                continue;

            for (int i = 0; i < bucket.Count; i++)
            {
                var renderer = bucket[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                if (!has)
                {
                    bounds = renderer.bounds;
                    has = true;
                }
                else
                    bounds.Encapsulate(renderer.bounds);
            }
        }

        return has;
    }

    private static Bounds IntersectBounds(Bounds a, Bounds b)
    {
        Vector3 min = Vector3.Max(a.min, b.min);
        Vector3 max = Vector3.Min(a.max, b.max);
        if (min.x > max.x || min.y > max.y || min.z > max.z)
            return new Bounds(a.center, Vector3.zero);

        var result = new Bounds();
        result.SetMinMax(min, max);
        return result;
    }

    private void DisableAllReflectionProbes()
    {
        foreach (var pair in _reflectionProbesByPath)
        {
            var bucket = pair.Value;
            if (bucket == null)
                continue;

            for (int i = 0; i < bucket.Count; i++)
            {
                var probe = bucket[i];
                if (probe != null && probe.enabled)
                    probe.enabled = false;
            }
        }
    }

    private void DisableBaseProbe(ReflectionProbeVariantEntry entry)
    {
        if (entry.probeBucketIndex < 0)
            return;

        if (string.IsNullOrWhiteSpace(entry.relativePath))
            return;

        if (!_reflectionProbesByPath.TryGetValue(entry.relativePath, out var bucket) || bucket == null || bucket.Count == 0)
            return;

        int index = Mathf.Clamp(entry.probeBucketIndex, 0, bucket.Count - 1);
        var probe = bucket[index];
        if (probe != null && probe.enabled)
            probe.enabled = false;
    }

    private static ReflectionProbe ResolveVariantProbe(ReflectionProbeVariantEntry entry, PowerLevel level)
    {
        switch (level)
        {
            case PowerLevel.P100:
                return FirstNonNullProbe(entry.power100Probe, entry.power00Probe);
            default:
                return FirstNonNullProbe(entry.power00Probe, entry.power100Probe);
        }
    }

    private static ReflectionProbe FirstNonNullProbe(params ReflectionProbe[] candidates)
    {
        if (candidates == null)
            return null;

        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i] != null)
                return candidates[i];
        }

        return null;
    }

    private static void SetVariantProbeEnabled(ReflectionProbe probe, bool enabled)
    {
        if (probe == null)
            return;

        if (enabled && probe.customBakedTexture != null && probe.mode != ReflectionProbeMode.Custom)
            probe.mode = ReflectionProbeMode.Custom;

        if (probe.enabled != enabled)
            probe.enabled = enabled;
    }

    private static void ApplyReflectionProbeBake(ReflectionProbe probe, Cubemap bakedTexture, ReflectionProbeApplyMode applyMode)
    {
        if (applyMode == ReflectionProbeApplyMode.ForceBakedMode)
        {
            if (probe.customBakedTexture != null)
                probe.customBakedTexture = null;

            if (probe.mode != ReflectionProbeMode.Baked)
                probe.mode = ReflectionProbeMode.Baked;

            return;
        }

        if (bakedTexture != null)
        {
            probe.customBakedTexture = bakedTexture;
            probe.mode = ReflectionProbeMode.Custom;
            return;
        }

        if (probe.mode == ReflectionProbeMode.Custom || probe.customBakedTexture != null)
        {
            probe.customBakedTexture = null;
            probe.mode = ReflectionProbeMode.Baked;
        }
    }

    private static int[] RegisterLightmaps(DungeonTileBakeData data)
    {
        var colors = data.lightmapColors ?? Array.Empty<Texture2D>();
        var directions = data.lightmapDirections ?? Array.Empty<Texture2D>();

        int localCount = colors.Length;
        var remap = new int[localCount];

        var existing = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
        var merged = new List<LightmapData>(existing);

        for (int i = 0; i < localCount; i++)
        {
            var color = colors[i];
            var dir = i < directions.Length ? directions[i] : null;

            int found = FindMatchingLightmapIndex(merged, color, dir);
            if (found < 0)
            {
                merged.Add(new LightmapData
                {
                    lightmapColor = color,
                    lightmapDir = dir
                });
                found = merged.Count - 1;
            }

            remap[i] = found;
        }

        var targetMode = ResolveLightmapsMode(data, directions);
        if (LightmapSettings.lightmapsMode != targetMode)
        {
            try
            {
                LightmapSettings.lightmapsMode = targetMode;
            }
            catch (ArgumentException)
            {
                Debug.LogWarning($"[DungeonTileLightmapSwitcher] Could not apply lightmaps mode '{targetMode}' from bake '{data.name}'. Keeping current mode '{LightmapSettings.lightmapsMode}'.", data);
            }
        }

        LightmapSettings.lightmaps = merged.ToArray();
        return remap;
    }

    private static LightmapsMode ResolveLightmapsMode(DungeonTileBakeData data, Texture2D[] directions)
    {
        if (IsSupportedLightmapsMode(data.lightmapsMode))
            return data.lightmapsMode;

        if (HasDirectionalLightmap(directions))
            return LightmapsMode.CombinedDirectional;

        return LightmapsMode.NonDirectional;
    }

    private static bool IsSupportedLightmapsMode(LightmapsMode mode)
    {
        return mode == LightmapsMode.NonDirectional || mode == LightmapsMode.CombinedDirectional;
    }

    private static bool HasDirectionalLightmap(Texture2D[] directions)
    {
        if (directions == null || directions.Length == 0)
            return false;

        for (int i = 0; i < directions.Length; i++)
        {
            if (directions[i] != null)
                return true;
        }

        return false;
    }

    private static int FindMatchingLightmapIndex(List<LightmapData> existing, Texture2D color, Texture2D dir)
    {
        for (int i = 0; i < existing.Count; i++)
        {
            var current = existing[i];
            if (current.lightmapColor == color && current.lightmapDir == dir)
                return i;
        }

        return -1;
    }

    private static string GetRelativePath(Transform root, Transform target)
    {
        if (root == target)
            return string.Empty;

        var names = new Stack<string>();
        var current = target;

        while (current != null && current != root)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
    }
}
