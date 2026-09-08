using System;
using System.Collections.Generic;
using DungeonRoomLocalLightShare;
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
    [SerializeField] private bool receivePortalDirectSH;
    [Tooltip("Seconds between hierarchy scans for renderers that joined or left after Awake (held items, weapons, attachments). 0 = explicit RefreshRenderers() only.")]
    [SerializeField, Min(0f)] private float rendererRefreshInterval = 0.5f;
    [Tooltip("Renderers that appear after Awake adopt the rendering layers of the authored renderers, so a held item is lit by the same lights and the same SH as the body.")]
    [SerializeField] private bool lateRenderersInheritRenderingLayers;
    [SerializeField] private bool logSampleDiagnostics;

    private RendererState[] _renderers = Array.Empty<RendererState>();
    private readonly List<Renderer> _rendererBuffer = new List<Renderer>();
    private float _nextRendererSyncTime;
    private MaterialPropertyBlock _propertyBlock;
    private readonly SphericalHarmonicsL2[] _singleProbe = new SphericalHarmonicsL2[1];
    private readonly Vector4[] _singleOcclusion = new Vector4[1];

    private Vector3 _lastSamplePosition;
    private float _nextSampleTime;
    private bool _hasSamplePosition;
    private bool _hasAppliedCustomProbe;
    private bool _lastSampleWasSpatialBlend;

    public Vector3 CurrentSamplePosition => GetSamplePosition();
    public DungeonTileProbeRegistry.SampleInfo LastSampleInfo { get; private set; }
    public bool HasValidSample { get; private set; }
    public bool ReceivePortalDirectSH => receivePortalDirectSH;

    /// <summary>Own opt-in, or the registry switch that opts in every dynamic receiver.</summary>
    public bool EffectiveReceivePortalDirectSH
    {
        get
        {
            if (receivePortalDirectSH)
                return true;
            DungeonTileProbeRegistry activeRegistry = registry != null ? registry : DungeonTileProbeRegistry.Active;
            return activeRegistry != null && activeRegistry.DynamicReceiversReceivePortalDirectSH;
        }
    }

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
        // Held items, weapons and attachments are instantiated long after Awake cached the body.
        if (rendererRefreshInterval > 0f && Time.unscaledTime >= _nextRendererSyncTime)
        {
            _nextRendererSyncTime = Time.unscaledTime + rendererRefreshInterval;
            if (SyncRenderers())
            {
                ApplyProbeAt(GetSamplePosition());
                return;
            }
        }

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

        SyncRenderers();
        ApplyProbeAt(GetSamplePosition());
    }

    /// <summary>
    /// Re-scan the hierarchy now (call right after equipping a held item or weapon) and re-apply
    /// the current SH so the new renderers never show a frame without it.
    /// </summary>
    public void RefreshRenderers()
    {
        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        SyncRenderers();
        _nextRendererSyncTime = Time.unscaledTime + rendererRefreshInterval;
        ApplyProbeAt(GetSamplePosition());
    }

    public int RendererCount => _renderers != null ? _renderers.Length : 0;

    public void SetSamplePoint(Transform point)
    {
        samplePoint = point;
        ForceRefresh();
    }

    public void SetReceivePortalDirectSH(bool enabled)
    {
        if (receivePortalDirectSH == enabled)
            return;
        receivePortalDirectSH = enabled;
        ForceRefresh();
    }

    private void CacheRenderers()
    {
        SyncRenderers();
    }

    /// <summary>
    /// Rebuilds the renderer cache when renderers joined or left this hierarchy; returns true when it
    /// changed. Renderers that leave while still alive get their original probe usage back. Late
    /// arrivals may inherit the authored renderers' rendering layers.
    /// </summary>
    private bool SyncRenderers()
    {
        GetComponentsInChildren(includeInactiveRenderers, _rendererBuffer);
        RendererState[] previous = _renderers ?? Array.Empty<RendererState>();

        bool changed = previous.Length != _rendererBuffer.Count;
        for (int i = 0; !changed && i < previous.Length; i++)
        {
            if (!ReferenceEquals(previous[i].renderer, _rendererBuffer[i]))
                changed = true;
        }
        if (!changed)
        {
            _rendererBuffer.Clear();
            return false;
        }

        // Rendering layers follow the body (the authored renderers), never another late arrival.
        bool hasInheritedMask = false;
        uint inheritedMask = 0;
        for (int i = 0; i < previous.Length; i++)
        {
            if (previous[i].late || previous[i].renderer == null)
                continue;
            inheritedMask = previous[i].renderer.renderingLayerMask;
            hasInheritedMask = true;
            break;
        }

        var next = new RendererState[_rendererBuffer.Count];
        for (int i = 0; i < _rendererBuffer.Count; i++)
        {
            Renderer renderer = _rendererBuffer[i];
            int materialCount = renderer != null && renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0;
            int previousIndex = IndexOf(previous, renderer);
            if (previousIndex >= 0)
            {
                next[i] = previous[previousIndex];
                next[i].materialCount = materialCount;
                continue;
            }

            bool late = previous.Length > 0;
            if (late && lateRenderersInheritRenderingLayers && hasInheritedMask && renderer != null)
                renderer.renderingLayerMask = inheritedMask;
            next[i] = new RendererState
            {
                renderer = renderer,
                originalLightProbeUsage = renderer != null ? renderer.lightProbeUsage : LightProbeUsage.Off,
                materialCount = materialCount,
                late = late
            };
        }

        // A renderer re-parented out of this hierarchy must not keep our custom probe usage.
        if (_hasAppliedCustomProbe)
        {
            for (int i = 0; i < previous.Length; i++)
            {
                Renderer renderer = previous[i].renderer;
                if (renderer != null && IndexOf(next, renderer) < 0)
                    renderer.lightProbeUsage = previous[i].originalLightProbeUsage;
            }
        }

        _renderers = next;
        _rendererBuffer.Clear();
        return true;
    }

    private static int IndexOf(RendererState[] states, Renderer renderer)
    {
        for (int i = 0; i < states.Length; i++)
        {
            if (ReferenceEquals(states[i].renderer, renderer))
                return i;
        }
        return -1;
    }

    private void ApplyProbeAt(Vector3 position)
    {
        _lastSamplePosition = position;
        _hasSamplePosition = true;

        DungeonTileProbeRegistry activeRegistry = registry != null ? registry : DungeonTileProbeRegistry.Active;
        SphericalHarmonicsL2 probe = default;
        Vector4 occlusion = Vector4.one;
        DungeonTileProbeRegistry.SampleInfo info = default;
        HasValidSample = activeRegistry != null && activeRegistry.TrySample(position, out probe, out occlusion,
            out info, transform);
        LastSampleInfo = info;
        if (!HasValidSample)
        {
            _nextSampleTime = Time.unscaledTime + updateInterval;
            _lastSampleWasSpatialBlend = false;

            if (info.hasTileData && info.noVisibleProbes)
            {
                // A blocked room sample must not revive global probes or retain SH
                // from a previously lit room. Missing dungeon data keeps the old policy.
                _singleOcclusion[0] = Vector4.one;
                ApplyCustomProbeToRenderers(default, default, false);
                _hasAppliedCustomProbe = true;
            }
            else if (restoreOriginalUsageWhenNoSample)
                RestoreOriginalProbeUsage();

            return;
        }

        // The registry switch opts in the player, monsters and items together; the serialized
        // flag opts in one object on its own (used by the standalone player tests).
        bool wantsPortal = receivePortalDirectSH || activeRegistry.DynamicReceiversReceivePortalDirectSH;
        bool applyPortal = false;
        if (wantsPortal)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i].renderer;
                if (renderer != null &&
                    (renderer.renderingLayerMask & (uint)RoomLocalLightShareContract.CookieEnvironmentRenderingLayerMask) == 0)
                { applyPortal = true; break; }
            }
        }
        SphericalHarmonicsL2 withPortal = probe;
        if (applyPortal)
            activeRegistry.AddPortalLighting(position, ref withPortal, ref info, transform);
        LastSampleInfo = info;

        // Door motion changes incoming light even while the receiver stands still, so a room
        // with an incoming connection resamples quickly. Rooms without one keep the normal
        // cadence even when every monster and item has opted in.
        _lastSampleWasSpatialBlend = info.spatialBlendActive || (applyPortal && info.portalConnectionCount > 0);
        _nextSampleTime = Time.unscaledTime + (_lastSampleWasSpatialBlend
            ? Mathf.Min(updateInterval, spatialBlendUpdateInterval)
            : updateInterval);

        _singleOcclusion[0] = occlusion;

        ApplyCustomProbeToRenderers(probe, withPortal, applyPortal);
        _hasAppliedCustomProbe = true;

        if (logSampleDiagnostics)
        {
            string blend = info.spatialBlendActive
                ? $" blend={info.blendZoneName} {info.tileAWeight:0.00}/{info.tileBWeight:0.00}"
                : string.Empty;
            Debug.Log(
                $"[DungeonDynamicProbeReceiver] {name} tile={info.tileName}{blend} probes={info.blendedProbeCount}/{info.probeCount} l0={info.l0Luminance:0.000} portal={info.portalContributionCount} portalL0={info.portalL0Luminance:0.000}",
                this);
        }
    }

    private void ApplyCustomProbeToRenderers(SphericalHarmonicsL2 baseProbe,
        SphericalHarmonicsL2 withPortal, bool usePortalDirectSH)
    {
        if (_renderers == null)
            return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer renderer = _renderers[i].renderer;
            if (renderer == null)
                continue;

            bool receivesCookie =
                (renderer.renderingLayerMask & (uint)RoomLocalLightShareContract.CookieEnvironmentRenderingLayerMask) != 0;
            _singleProbe[0] = usePortalDirectSH && !receivesCookie ? withPortal : baseProbe;
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
        public bool late;
    }
}
