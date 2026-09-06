using System;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class DungeonDynamicProbeReceiver : MonoBehaviour
{
    [SerializeField] private DungeonTileProbeRegistry registry;
    [SerializeField] private Transform samplePoint;
    [SerializeField] private bool includeInactiveRenderers = true;
    [SerializeField, Min(0.02f)] private float updateInterval = 0.2f;
    [SerializeField, Min(0f)] private float movementThreshold = 0.25f;
    [SerializeField, Min(0.02f)] private float spatialBlendUpdateInterval = 0.06f;
    [SerializeField, Min(0f)] private float spatialBlendMovementThreshold = 0.08f;
    [SerializeField] private bool applyOnEnable = true;
    [SerializeField] private bool restoreOriginalUsageWhenNoSample = true;
    [SerializeField] private bool logSampleDiagnostics;

    private RendererState[] _renderers = Array.Empty<RendererState>();
    private MaterialPropertyBlock _propertyBlock;
    private readonly SphericalHarmonicsL2[] _singleProbe = new SphericalHarmonicsL2[1];
    private readonly Vector4[] _singleOcclusion = new Vector4[1];

    private Vector3 _lastSamplePosition;
    private float _nextSampleTime;
    private bool _hasSamplePosition;
    private bool _hasAppliedCustomProbe;
    private bool _lastSampleWasSpatialBlend;

    public Vector3 CurrentSamplePosition => GetSamplePosition();

    private void Awake()
    {
        _propertyBlock = new MaterialPropertyBlock();
        CacheRenderers();
    }

    private void OnEnable()
    {
        if (applyOnEnable)
            ForceRefresh();
    }

    private void OnDisable()
    {
        RestoreOriginalProbeUsage();
    }

    private void Update()
    {
        Vector3 position = GetSamplePosition();
        float activeMovementThreshold = _lastSampleWasSpatialBlend
            ? Mathf.Min(movementThreshold, spatialBlendMovementThreshold)
            : movementThreshold;
        float movementThresholdSqr = activeMovementThreshold * activeMovementThreshold;
        bool movedEnough = !_hasSamplePosition || (position - _lastSamplePosition).sqrMagnitude >= movementThresholdSqr;
        bool intervalElapsed = Time.unscaledTime >= _nextSampleTime;

        if (!movedEnough && !intervalElapsed)
            return;

        ApplyProbeAt(position);
    }

    [ContextMenu("Refresh Dungeon Probe Receiver")]
    public void ForceRefresh()
    {
        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        if (_renderers == null || _renderers.Length == 0)
            CacheRenderers();

        ApplyProbeAt(GetSamplePosition());
    }

    public void SetSamplePoint(Transform point)
    {
        samplePoint = point;
        ForceRefresh();
    }

    private void CacheRenderers()
    {
        var found = GetComponentsInChildren<Renderer>(includeInactiveRenderers);
        _renderers = new RendererState[found.Length];

        for (int i = 0; i < found.Length; i++)
        {
            Renderer renderer = found[i];
            _renderers[i] = new RendererState
            {
                renderer = renderer,
                originalLightProbeUsage = renderer != null ? renderer.lightProbeUsage : LightProbeUsage.Off,
                materialCount = renderer != null && renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0
            };
        }
    }

    private void ApplyProbeAt(Vector3 position)
    {
        _lastSamplePosition = position;
        _hasSamplePosition = true;

        DungeonTileProbeRegistry activeRegistry = registry != null ? registry : DungeonTileProbeRegistry.Active;
        if (activeRegistry == null ||
            !activeRegistry.TrySample(position, out SphericalHarmonicsL2 probe, out Vector4 occlusion, out var info))
        {
            _nextSampleTime = Time.unscaledTime + updateInterval;
            _lastSampleWasSpatialBlend = false;

            if (restoreOriginalUsageWhenNoSample)
                RestoreOriginalProbeUsage();

            return;
        }

        _lastSampleWasSpatialBlend = info.spatialBlendActive;
        _nextSampleTime = Time.unscaledTime + (_lastSampleWasSpatialBlend
            ? Mathf.Min(updateInterval, spatialBlendUpdateInterval)
            : updateInterval);

        _singleProbe[0] = probe;
        _singleOcclusion[0] = occlusion;

        ApplyCustomProbeToRenderers();
        _hasAppliedCustomProbe = true;

        if (logSampleDiagnostics)
        {
            string blend = info.spatialBlendActive
                ? $" blend={info.blendZoneName} {info.tileAWeight:0.00}/{info.tileBWeight:0.00}"
                : string.Empty;
            Debug.Log(
                $"[DungeonDynamicProbeReceiver] {name} tile={info.tileName}{blend} probes={info.blendedProbeCount}/{info.probeCount} l0={info.l0Luminance:0.000}",
                this);
        }
    }

    private void ApplyCustomProbeToRenderers()
    {
        if (_renderers == null)
            return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer renderer = _renderers[i].renderer;
            if (renderer == null)
                continue;

            if (renderer.lightProbeUsage != LightProbeUsage.CustomProvided)
                renderer.lightProbeUsage = LightProbeUsage.CustomProvided;

            int materialCount = _renderers[i].materialCount;
            if (materialCount <= 0)
            {
                renderer.GetPropertyBlock(_propertyBlock);
                CopyProbeDataToBlock();
                renderer.SetPropertyBlock(_propertyBlock);
                continue;
            }

            for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
            {
                renderer.GetPropertyBlock(_propertyBlock, materialIndex);
                CopyProbeDataToBlock();
                renderer.SetPropertyBlock(_propertyBlock, materialIndex);
            }
        }
    }

    private void CopyProbeDataToBlock()
    {
        _propertyBlock.CopySHCoefficientArraysFrom(_singleProbe);
        _propertyBlock.CopyProbeOcclusionArrayFrom(_singleOcclusion);
    }

    private void RestoreOriginalProbeUsage()
    {
        if (!_hasAppliedCustomProbe || _renderers == null)
            return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer renderer = _renderers[i].renderer;
            if (renderer != null)
                renderer.lightProbeUsage = _renderers[i].originalLightProbeUsage;
        }

        _hasAppliedCustomProbe = false;
    }

    private Vector3 GetSamplePosition()
    {
        if (samplePoint != null)
            return samplePoint.position;

        return transform.position;
    }

    private struct RendererState
    {
        public Renderer renderer;
        public LightProbeUsage originalLightProbeUsage;
        public int materialCount;
    }
}
