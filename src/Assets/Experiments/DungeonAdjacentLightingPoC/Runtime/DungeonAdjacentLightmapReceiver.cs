using DunGen;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonAdjacentLightingPoC
{
    [DisallowMultipleComponent]
    public sealed class DungeonAdjacentLightmapReceiver : MonoBehaviour
    {
        private static readonly int AdjacentLightmapId = Shader.PropertyToID("_AdjacentLightmap");
        private static readonly int AdjacentLightmapDirectionId = Shader.PropertyToID("_AdjacentLightmapDir");
        private static readonly int AdjacentBaselineLightmapId =
            Shader.PropertyToID("_AdjacentBaselineLightmap");
        private static readonly int AdjacentBaselineLightmapDirectionId =
            Shader.PropertyToID("_AdjacentBaselineLightmapDir");
        private static readonly int AdjacentLightmapEnabledId = Shader.PropertyToID("_AdjacentLightmapEnabled");
        private static readonly int AdjacentLightmapDirectionalId = Shader.PropertyToID("_AdjacentLightmapDirectional");
        private static readonly int AdjacentBaselineDirectionalId =
            Shader.PropertyToID("_AdjacentBaselineDirectional");
        private static readonly int AdjacentDoorwayPositionId =
            Shader.PropertyToID("_AdjacentDoorwayPositionWS");
        private static readonly int AdjacentDoorwayForwardSignId =
            Shader.PropertyToID("_AdjacentDoorwayForwardSignWS");
        private static readonly int AdjacentDoorwayRightHalfWidthId =
            Shader.PropertyToID("_AdjacentDoorwayRightHalfWidthWS");
        private static readonly int AdjacentDoorwayUpHalfHeightId =
            Shader.PropertyToID("_AdjacentDoorwayUpHalfHeightWS");
        private static readonly int AdjacentPortalFadeParametersId =
            Shader.PropertyToID("_AdjacentPortalFadeParameters");
        private static readonly int AdjacentContributionScaleId =
            Shader.PropertyToID("_AdjacentContributionScale");
        private static readonly int AdjacentUsePortalFadeId =
            Shader.PropertyToID("_AdjacentUsePortalFade");
        private static readonly int AdjacentReflectionCubeId =
            Shader.PropertyToID("_AdjacentReflectionCube");
        private static readonly int AdjacentReflectionHdrId =
            Shader.PropertyToID("_AdjacentReflectionHDR");
        private static readonly int AdjacentReflectionEnabledId =
            Shader.PropertyToID("_AdjacentReflectionEnabled");

        [Header("Explicit connection wiring")]
        [SerializeField] private DunGen.Door door;
        [SerializeField] private Transform receiverDoorway;
        [SerializeField] private MeshFilter receiverMeshFilter;
        [SerializeField] private MeshRenderer receiverRenderer;
        [SerializeField] private DungeonAdjacentLightmapExtension sourceExtension;
        [SerializeField] private DungeonTileLightmapSwitcher sourceLighting;
        [SerializeField] private DungeonTileLightmapSwitcher receiverLighting;

        [Header("Four-state pair bake")]
        [SerializeField] private bool usePairBakeData;
        [SerializeField] private DungeonAdjacentPairLightmapData pairBakeData;
        [SerializeField] private DungeonAdjacentPairLightmapData.RoomRole pairReceiverRoom;
        [SerializeField] private string pairRendererPath;
        [SerializeField] private int pairRendererBucketIndex;

        [Header("Projection")]
        [SerializeField, Min(0.05f)] private float blendDepth = 2.75f;
        [SerializeField, Min(0.05f)] private float maxProjectionDistance = 3.5f;
        [SerializeField, Min(0f)] private float lateralFadeDistance = 1.25f;
        [SerializeField] private float receiverInteriorSign = -1f;
        [SerializeField] private bool logDiagnostics;

        private MaterialPropertyBlock _propertyBlock;
        private Mesh _additionalVertexStream;
        private bool _subscribed;
        private bool _visualizationEnabled = true;
        private Texture _sourceReflectionTexture;
        private Vector4 _sourceReflectionHdr;

        public int LastProjectedVertexCount { get; private set; }
        public string LastDiagnostic { get; private set; }

        public void Configure(
            DunGen.Door connectedDoor,
            Transform localDoorway,
            MeshFilter localMeshFilter,
            MeshRenderer localRenderer,
            DungeonAdjacentLightmapExtension neighborExtension,
            DungeonTileLightmapSwitcher neighborLighting,
            float doorwayBlendDepth = 2.75f,
            float projectionDistance = 3.5f,
            float interiorSign = -1f,
            float doorwayLateralFade = 1.25f)
        {
            Unsubscribe();
            door = connectedDoor;
            receiverDoorway = localDoorway;
            receiverMeshFilter = localMeshFilter;
            receiverRenderer = localRenderer;
            sourceExtension = neighborExtension;
            sourceLighting = neighborLighting;
            receiverLighting = null;
            usePairBakeData = false;
            pairBakeData = null;
            pairRendererPath = null;
            pairRendererBucketIndex = 0;
            blendDepth = Mathf.Max(0.05f, doorwayBlendDepth);
            maxProjectionDistance = Mathf.Max(0.05f, projectionDistance);
            lateralFadeDistance = Mathf.Max(0f, doorwayLateralFade);
            receiverInteriorSign = interiorSign;
            Subscribe();
        }

        public void ConfigurePairBake(
            DunGen.Door connectedDoor,
            Transform localDoorway,
            MeshFilter localMeshFilter,
            MeshRenderer localRenderer,
            DungeonAdjacentLightmapExtension neighborExtension,
            DungeonTileLightmapSwitcher localLighting,
            DungeonTileLightmapSwitcher neighborLighting,
            DungeonAdjacentPairLightmapData data,
            DungeonAdjacentPairLightmapData.RoomRole receiverRoom,
            string rendererPath,
            int rendererBucketIndex)
        {
            Unsubscribe();
            door = connectedDoor;
            receiverDoorway = localDoorway;
            receiverMeshFilter = localMeshFilter;
            receiverRenderer = localRenderer;
            sourceExtension = neighborExtension;
            receiverLighting = localLighting;
            sourceLighting = neighborLighting;
            usePairBakeData = true;
            pairBakeData = data;
            pairReceiverRoom = receiverRoom;
            pairRendererPath = rendererPath;
            pairRendererBucketIndex = Mathf.Max(0, rendererBucketIndex);
            Subscribe();
        }

        private void OnEnable()
        {
            Subscribe();
            RebuildAndApply();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ClearOwnedVertexStream();
            if (usePairBakeData)
                ApplyPairEnabledState(false, default, default);
            else
                ApplyEnabledState(false, default);
        }

        [ContextMenu("Rebuild Adjacent Lightmap Mapping")]
        public void RebuildAndApply()
        {
            LastProjectedVertexCount = 0;
            RefreshSourceReflection();
            if (usePairBakeData)
            {
                RebuildPairBakeAndApply();
                return;
            }

            if (!TryResolveInputs(out Mesh receiverMesh, out Mesh extensionMesh, out string failure))
            {
                SetDiagnostic(failure, true);
                ApplyEnabledState(false, default);
                return;
            }

            DungeonTileLightmapSwitcher.PowerLevel powerLevel = sourceLighting.CurrentPowerLevel;
            DungeonAdjacentLightmapExtension.BakedState state = sourceExtension.GetState(powerLevel);
            if (!state.IsValid)
            {
                SetDiagnostic($"Source extension has no captured {powerLevel} lightmap.", true);
                ApplyEnabledState(false, state);
                return;
            }

            DungeonAdjacentLightmapExtension.BakedState baselineState = sourceExtension.GetState(
                DungeonTileLightmapSwitcher.PowerLevel.P0);
            if (!baselineState.IsValid)
            {
                SetDiagnostic("Source extension has no captured P0 baseline lightmap.", true);
                ApplyEnabledState(false, state);
                return;
            }

            Vector4[] uv4 = new Vector4[receiverMesh.vertexCount];
            Vector4[] uv5 = new Vector4[receiverMesh.vertexCount];
            Vector4[] projectionBasis = new Vector4[receiverMesh.vertexCount];
            try
            {
                LastProjectedVertexCount = DungeonAdjacentLightmapProjection.BuildVertexData(
                    receiverMesh,
                    receiverMeshFilter.transform.localToWorldMatrix,
                    receiverDoorway.position,
                    receiverDoorway.forward,
                    receiverInteriorSign,
                    extensionMesh,
                    sourceExtension.ExtensionTransform.localToWorldMatrix,
                    state.lightmapScaleOffset,
                    blendDepth,
                    maxProjectionDistance,
                    uv4,
                    lateralFadeDistance,
                    projectionBasis,
                    sourceExtension.PortalHalfWidth,
                    sourceExtension.PortalHalfHeight * 2f);
                DungeonAdjacentLightmapProjection.BuildVertexData(
                    receiverMesh,
                    receiverMeshFilter.transform.localToWorldMatrix,
                    receiverDoorway.position,
                    receiverDoorway.forward,
                    receiverInteriorSign,
                    extensionMesh,
                    sourceExtension.ExtensionTransform.localToWorldMatrix,
                    baselineState.lightmapScaleOffset,
                    blendDepth,
                    maxProjectionDistance,
                    uv5,
                    lateralFadeDistance);
            }
            catch (System.Exception exception)
            {
                SetDiagnostic($"Projection failed: {exception.Message}", true);
                ApplyEnabledState(false, state);
                return;
            }

            if (LastProjectedVertexCount == 0)
            {
                SetDiagnostic("No receiver vertices reached the doorway blend band.", true);
                ApplyEnabledState(false, state);
                return;
            }

            if (!TryAssignAdditionalVertexStream(uv4, uv5, projectionBasis, out failure))
            {
                SetDiagnostic(failure, true);
                ApplyEnabledState(false, state);
                return;
            }

            bool isOpen = door == null || door.IsOpen;
            ApplyEnabledState(isOpen, state);
            SetDiagnostic(
                $"Mapped {LastProjectedVertexCount}/{receiverMesh.vertexCount} vertices from " +
                $"{sourceExtension.DoorwayId} ({powerLevel}, doorOpen={isOpen}).",
                false);
        }

        private void RebuildPairBakeAndApply()
        {
            if (!TryResolvePairInputs(out Mesh receiverMesh, out string failure))
            {
                SetDiagnostic(failure, true);
                ApplyPairEnabledState(false, default, default);
                return;
            }

            if (!TryResolvePairStates(
                    out DungeonAdjacentPairLightmapData.LightmapState current,
                    out DungeonAdjacentPairLightmapData.LightmapState baseline,
                    out failure))
            {
                SetDiagnostic(failure, true);
                ApplyPairEnabledState(false, current, baseline);
                return;
            }

            if (!TryBuildPairAtlasStreams(
                    receiverMesh,
                    current.lightmapScaleOffset,
                    baseline.lightmapScaleOffset,
                out Vector4[] currentUv,
                out Vector4[] baselineUv,
                out failure) ||
                !TryAssignAdditionalVertexStream(currentUv, baselineUv, null, out failure))
            {
                SetDiagnostic(failure, true);
                ApplyPairEnabledState(false, current, baseline);
                return;
            }

            LastProjectedVertexCount = receiverMesh.vertexCount;
            bool isOpen = door == null || door.IsOpen;
            ApplyPairEnabledState(isOpen, current, baseline);
            SetDiagnostic(
                $"Pair-bake mapped {receiverMesh.vertexCount}/{receiverMesh.vertexCount} vertices " +
                $"for {pairReceiverRoom}:{pairRendererPath}[{pairRendererBucketIndex}] " +
                $"(receiver={receiverLighting.CurrentPowerLevel}, source={sourceLighting.CurrentPowerLevel}, " +
                $"doorOpen={isOpen}).",
                false);
        }

        private bool TryResolvePairInputs(out Mesh receiverMesh, out string failure)
        {
            receiverMesh = null;
            failure = null;
            if (receiverDoorway == null)
                failure = "Receiver doorway is not assigned.";
            else if (receiverMeshFilter == null || receiverRenderer == null)
                failure = "Receiver MeshFilter/MeshRenderer is not assigned.";
            else if (receiverLighting == null || sourceLighting == null)
                failure = "Pair receiver/source lighting is not assigned.";
            else if (pairBakeData == null)
                failure = "Four-state pair bake data is not assigned.";
            else if (string.IsNullOrEmpty(pairRendererPath))
                failure = "Pair renderer path is missing.";
            else if ((receiverMesh = receiverMeshFilter.sharedMesh) == null)
                failure = "Receiver mesh is missing.";
            else if (receiverRenderer.additionalVertexStreams != null &&
                     receiverRenderer.additionalVertexStreams != _additionalVertexStream)
                failure = "Receiver already owns another additionalVertexStreams mesh.";
            return failure == null;
        }

        private bool TryResolvePairStates(
            out DungeonAdjacentPairLightmapData.LightmapState current,
            out DungeonAdjacentPairLightmapData.LightmapState baseline,
            out string failure)
        {
            current = default;
            baseline = default;
            failure = null;
            if (pairBakeData == null || receiverLighting == null || sourceLighting == null)
            {
                failure = "Pair bake data or lighting switcher is missing.";
                return false;
            }

            if (!pairBakeData.TryResolveTransfer(
                    pairReceiverRoom,
                    pairRendererPath,
                    pairRendererBucketIndex,
                    receiverLighting.CurrentPowerLevel,
                    sourceLighting.CurrentPowerLevel,
                    out current,
                    out baseline))
            {
                failure =
                    $"No complete pair-bake entry for {pairReceiverRoom}:" +
                    $"{pairRendererPath}[{pairRendererBucketIndex}].";
                return false;
            }
            return true;
        }

        private static bool TryBuildPairAtlasStreams(
            Mesh receiverMesh,
            Vector4 currentScaleOffset,
            Vector4 baselineScaleOffset,
            out Vector4[] currentUv,
            out Vector4[] baselineUv,
            out string failure)
        {
            currentUv = null;
            baselineUv = null;
            failure = null;
            Vector2[] lightmapUv = receiverMesh != null ? receiverMesh.uv2 : null;
            if (receiverMesh == null || lightmapUv == null || lightmapUv.Length != receiverMesh.vertexCount)
            {
                failure = "Receiver mesh has no complete UV2 lightmap channel.";
                return false;
            }

            currentUv = new Vector4[lightmapUv.Length];
            baselineUv = new Vector4[lightmapUv.Length];
            for (int i = 0; i < lightmapUv.Length; i++)
            {
                Vector2 uv = lightmapUv[i];
                currentUv[i] = new Vector4(
                    uv.x * currentScaleOffset.x + currentScaleOffset.z,
                    uv.y * currentScaleOffset.y + currentScaleOffset.w,
                    1f,
                    1f);
                baselineUv[i] = new Vector4(
                    uv.x * baselineScaleOffset.x + baselineScaleOffset.z,
                    uv.y * baselineScaleOffset.y + baselineScaleOffset.w,
                    1f,
                    1f);
            }
            return true;
        }

        public void SetVisualizationEnabled(bool enabled)
        {
            _visualizationEnabled = enabled;
            if (usePairBakeData)
            {
                TryResolvePairStates(
                    out DungeonAdjacentPairLightmapData.LightmapState current,
                    out DungeonAdjacentPairLightmapData.LightmapState baseline,
                    out _);
                bool pairDoorOpen = door == null || door.IsOpen;
                ApplyPairEnabledState(pairDoorOpen, current, baseline);
                return;
            }
            DungeonAdjacentLightmapExtension.BakedState state =
                sourceExtension != null && sourceLighting != null
                    ? sourceExtension.GetState(sourceLighting.CurrentPowerLevel)
                    : default;
            bool isOpen = door == null || door.IsOpen;
            ApplyEnabledState(isOpen, state);
        }

        private bool TryResolveInputs(out Mesh receiverMesh, out Mesh extensionMesh, out string failure)
        {
            receiverMesh = null;
            extensionMesh = null;
            failure = null;

            if (receiverDoorway == null)
                failure = "Receiver doorway is not assigned.";
            else if (receiverMeshFilter == null || receiverRenderer == null)
                failure = "Receiver MeshFilter/MeshRenderer is not assigned.";
            else if (sourceExtension == null || sourceLighting == null)
                failure = "Source extension/source lighting is not assigned.";
            else if ((receiverMesh = receiverMeshFilter.sharedMesh) == null)
                failure = "Receiver mesh is missing.";
            else if ((extensionMesh = sourceExtension.ExtensionMesh) == null)
                failure = "Extension mesh is missing.";
            else if (receiverRenderer.additionalVertexStreams != null &&
                     receiverRenderer.additionalVertexStreams != _additionalVertexStream)
                failure = "Receiver already owns another additionalVertexStreams mesh.";

            return failure == null;
        }

        private bool TryAssignAdditionalVertexStream(
            Vector4[] uv4,
            Vector4[] baselineUv5,
            Vector4[] projectionBasisUv6,
            out string failure)
        {
            failure = null;
            if (baselineUv5 == null || baselineUv5.Length != uv4.Length)
            {
                failure = "P0 UV5 stream must match the P100/current UV4 stream.";
                return false;
            }
            if (projectionBasisUv6 != null && projectionBasisUv6.Length != uv4.Length)
            {
                failure = "Projection basis UV6 stream must match the UV4/UV5 streams.";
                return false;
            }

            if (_additionalVertexStream == null)
            {
                _additionalVertexStream = new Mesh
                {
                    name = $"{name}_AdjacentLightmapUV4_Runtime",
                    hideFlags = HideFlags.DontSave
                };
            }
            else
            {
                _additionalVertexStream.Clear();
            }

            try
            {
                var streamData = new AdjacentVertexStreamData[uv4.Length];
                for (int vertex = 0; vertex < streamData.Length; vertex++)
                {
                    streamData[vertex] = new AdjacentVertexStreamData
                    {
                        current = uv4[vertex],
                        baseline = baselineUv5[vertex],
                        projectionBasis = projectionBasisUv6 != null
                            ? projectionBasisUv6[vertex]
                            : Vector4.zero
                    };
                }

                _additionalVertexStream.SetVertexBufferParams(
                    uv4.Length,
                    new VertexAttributeDescriptor(
                        VertexAttribute.TexCoord3,
                        VertexAttributeFormat.Float32,
                        4),
                    new VertexAttributeDescriptor(
                        VertexAttribute.TexCoord4,
                        VertexAttributeFormat.Float32,
                        4),
                    new VertexAttributeDescriptor(
                        VertexAttribute.TexCoord5,
                        VertexAttributeFormat.Float32,
                        4));
                _additionalVertexStream.SetVertexBufferData(
                    streamData,
                    0,
                    0,
                    uv4.Length,
                    0,
                    MeshUpdateFlags.DontRecalculateBounds |
                    MeshUpdateFlags.DontValidateIndices |
                    MeshUpdateFlags.DontNotifyMeshUsers);
                receiverRenderer.additionalVertexStreams = _additionalVertexStream;
                return true;
            }
            catch (System.Exception exception)
            {
                failure = $"Unable to assign UV4/UV5/UV6 vertex stream: {exception.Message}";
                ClearOwnedVertexStream();
                return false;
            }
        }

        private void ApplyEnabledState(
            bool enabled,
            DungeonAdjacentLightmapExtension.BakedState state)
        {
            if (receiverRenderer == null)
                return;

            if (_propertyBlock == null)
                _propertyBlock = new MaterialPropertyBlock();

            DungeonAdjacentLightmapExtension.BakedState baselineState = sourceExtension != null
                ? sourceExtension.GetState(DungeonTileLightmapSwitcher.PowerLevel.P0)
                : default;
            receiverRenderer.GetPropertyBlock(_propertyBlock);
            ApplyPortalGeometryProperties(_propertyBlock);
            _propertyBlock.SetFloat(AdjacentContributionScaleId, 0.5f);
            _propertyBlock.SetFloat(AdjacentUsePortalFadeId, 1f);
            bool sourceHasIncrementalLight =
                sourceLighting != null &&
                sourceLighting.CurrentPowerLevel != DungeonTileLightmapSwitcher.PowerLevel.P0;
            _propertyBlock.SetFloat(
                AdjacentLightmapEnabledId,
                enabled &&
                _visualizationEnabled &&
                sourceHasIncrementalLight &&
                state.IsValid &&
                baselineState.IsValid
                    ? 1f
                    : 0f);
            ApplyAdjacentReflection(
                _propertyBlock,
                enabled && _visualizationEnabled && sourceHasIncrementalLight);
            _propertyBlock.SetFloat(AdjacentLightmapDirectionalId, state.lightmapDirection != null ? 1f : 0f);
            _propertyBlock.SetFloat(
                AdjacentBaselineDirectionalId,
                baselineState.lightmapDirection != null ? 1f : 0f);

            if (state.lightmapColor != null)
                _propertyBlock.SetTexture(AdjacentLightmapId, state.lightmapColor);
            if (state.lightmapDirection != null)
                _propertyBlock.SetTexture(AdjacentLightmapDirectionId, state.lightmapDirection);
            if (baselineState.lightmapColor != null)
                _propertyBlock.SetTexture(AdjacentBaselineLightmapId, baselineState.lightmapColor);
            if (baselineState.lightmapDirection != null)
            {
                _propertyBlock.SetTexture(
                    AdjacentBaselineLightmapDirectionId,
                    baselineState.lightmapDirection);
            }

            receiverRenderer.SetPropertyBlock(_propertyBlock);
        }

        private void ApplyPairEnabledState(
            bool enabled,
            DungeonAdjacentPairLightmapData.LightmapState current,
            DungeonAdjacentPairLightmapData.LightmapState baseline)
        {
            if (receiverRenderer == null)
                return;
            if (_propertyBlock == null)
                _propertyBlock = new MaterialPropertyBlock();

            receiverRenderer.GetPropertyBlock(_propertyBlock);
            ApplyPortalGeometryProperties(_propertyBlock);
            _propertyBlock.SetFloat(AdjacentContributionScaleId, 1f);
            _propertyBlock.SetFloat(AdjacentUsePortalFadeId, 0f);
            bool sourceHasIncrementalLight =
                sourceLighting != null &&
                sourceLighting.CurrentPowerLevel == DungeonTileLightmapSwitcher.PowerLevel.P100;
            _propertyBlock.SetFloat(
                AdjacentLightmapEnabledId,
                enabled && _visualizationEnabled && sourceHasIncrementalLight &&
                current.IsValid && baseline.IsValid
                    ? 1f
                    : 0f);
            ApplyAdjacentReflection(
                _propertyBlock,
                enabled && _visualizationEnabled && sourceHasIncrementalLight);
            _propertyBlock.SetFloat(
                AdjacentLightmapDirectionalId,
                current.lightmapDirection != null ? 1f : 0f);
            _propertyBlock.SetFloat(
                AdjacentBaselineDirectionalId,
                baseline.lightmapDirection != null ? 1f : 0f);

            if (current.lightmapColor != null)
                _propertyBlock.SetTexture(AdjacentLightmapId, current.lightmapColor);
            if (current.lightmapDirection != null)
                _propertyBlock.SetTexture(AdjacentLightmapDirectionId, current.lightmapDirection);
            if (baseline.lightmapColor != null)
                _propertyBlock.SetTexture(AdjacentBaselineLightmapId, baseline.lightmapColor);
            if (baseline.lightmapDirection != null)
            {
                _propertyBlock.SetTexture(
                    AdjacentBaselineLightmapDirectionId,
                    baseline.lightmapDirection);
            }
            receiverRenderer.SetPropertyBlock(_propertyBlock);
        }

        private void ApplyAdjacentReflection(
            MaterialPropertyBlock propertyBlock,
            bool requested)
        {
            propertyBlock.SetFloat(AdjacentReflectionEnabledId, 0f);
            if (!requested || _sourceReflectionTexture == null)
                return;

            propertyBlock.SetTexture(AdjacentReflectionCubeId, _sourceReflectionTexture);
            propertyBlock.SetVector(AdjacentReflectionHdrId, _sourceReflectionHdr);
            propertyBlock.SetFloat(AdjacentReflectionEnabledId, 1f);
        }

        private void RefreshSourceReflection()
        {
            _sourceReflectionTexture = null;
            _sourceReflectionHdr = default;
            if (sourceLighting == null || sourceExtension == null)
                return;

            ReflectionProbe[] probes = sourceLighting.GetComponentsInChildren<ReflectionProbe>(true);
            ReflectionProbe closest = null;
            float closestDistanceSquared = float.PositiveInfinity;
            Vector3 doorwayPosition = sourceExtension.Doorway != null
                ? sourceExtension.Doorway.transform.position
                : sourceExtension.transform.position;
            for (int i = 0; i < probes.Length; i++)
            {
                ReflectionProbe probe = probes[i];
                Texture texture = probe != null ? probe.texture : null;
                if (probe == null || !probe.enabled || texture == null ||
                    texture.dimension != TextureDimension.Cube)
                {
                    continue;
                }

                float distanceSquared = (probe.transform.position - doorwayPosition).sqrMagnitude;
                if (distanceSquared >= closestDistanceSquared)
                    continue;

                closest = probe;
                closestDistanceSquared = distanceSquared;
            }

            if (closest == null || closest.texture == null)
                return;

            _sourceReflectionTexture = closest.texture;
            _sourceReflectionHdr = closest.textureHDRDecodeValues;
        }

        private void ApplyPortalGeometryProperties(MaterialPropertyBlock propertyBlock)
        {
            if (receiverDoorway == null || sourceExtension == null || sourceExtension.ExtensionMesh == null)
                return;

            Vector3 doorwayForward = receiverDoorway.forward.sqrMagnitude > 0.000001f
                ? receiverDoorway.forward.normalized
                : Vector3.forward;
            Vector3 doorwayRight = Vector3.Cross(Vector3.up, doorwayForward).normalized;
            if (doorwayRight.sqrMagnitude <= 0.000001f)
                doorwayRight = Vector3.right;
            Vector3 doorwayUp = receiverDoorway.up.sqrMagnitude > 0.000001f
                ? receiverDoorway.up.normalized
                : Vector3.up;

            float doorwayRightScale = receiverDoorway.TransformVector(Vector3.right).magnitude;
            float doorwayUpScale = receiverDoorway.TransformVector(Vector3.up).magnitude;
            float doorwayHalfWidth = sourceExtension.PortalHalfWidth * doorwayRightScale;
            float doorwayHalfHeight = sourceExtension.PortalHalfHeight * doorwayUpScale;
            float interiorSign = receiverInteriorSign >= 0f ? 1f : -1f;

            propertyBlock.SetVector(
                AdjacentDoorwayPositionId,
                new Vector4(
                    receiverDoorway.position.x,
                    receiverDoorway.position.y,
                    receiverDoorway.position.z,
                    1f));
            propertyBlock.SetVector(
                AdjacentDoorwayForwardSignId,
                new Vector4(
                    doorwayForward.x,
                    doorwayForward.y,
                    doorwayForward.z,
                    interiorSign));
            propertyBlock.SetVector(
                AdjacentDoorwayRightHalfWidthId,
                new Vector4(
                    doorwayRight.x,
                    doorwayRight.y,
                    doorwayRight.z,
                    doorwayHalfWidth));
            propertyBlock.SetVector(
                AdjacentDoorwayUpHalfHeightId,
                new Vector4(
                    doorwayUp.x,
                    doorwayUp.y,
                    doorwayUp.z,
                    doorwayHalfHeight));
            propertyBlock.SetVector(
                AdjacentPortalFadeParametersId,
                new Vector4(
                    blendDepth,
                    maxProjectionDistance,
                    lateralFadeDistance,
                    0f));
        }

        private void Subscribe()
        {
            if (_subscribed)
                return;

            if (sourceLighting != null)
                sourceLighting.PowerLevelApplied += OnSourcePowerLevelApplied;
            if (usePairBakeData && receiverLighting != null && receiverLighting != sourceLighting)
                receiverLighting.PowerLevelApplied += OnReceiverPowerLevelApplied;
            if (door != null)
                door.OnDoorStateChanged += OnDoorStateChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;

            if (sourceLighting != null)
                sourceLighting.PowerLevelApplied -= OnSourcePowerLevelApplied;
            if (usePairBakeData && receiverLighting != null && receiverLighting != sourceLighting)
                receiverLighting.PowerLevelApplied -= OnReceiverPowerLevelApplied;
            if (door != null)
                door.OnDoorStateChanged -= OnDoorStateChanged;
            _subscribed = false;
        }

        private void OnSourcePowerLevelApplied(DungeonTileLightmapSwitcher.PowerLevel powerLevel)
        {
            RebuildAndApply();
        }

        private void OnReceiverPowerLevelApplied(DungeonTileLightmapSwitcher.PowerLevel powerLevel)
        {
            RebuildAndApply();
        }

        private void OnDoorStateChanged(DunGen.Door changedDoor, bool isOpen)
        {
            if (usePairBakeData)
            {
                TryResolvePairStates(
                    out DungeonAdjacentPairLightmapData.LightmapState current,
                    out DungeonAdjacentPairLightmapData.LightmapState baseline,
                    out _);
                ApplyPairEnabledState(isOpen, current, baseline);
                return;
            }
            DungeonAdjacentLightmapExtension.BakedState state = sourceExtension != null && sourceLighting != null
                ? sourceExtension.GetState(sourceLighting.CurrentPowerLevel)
                : default;
            ApplyEnabledState(isOpen, state);
        }

        private void ClearOwnedVertexStream()
        {
            if (receiverRenderer != null && receiverRenderer.additionalVertexStreams == _additionalVertexStream)
                receiverRenderer.additionalVertexStreams = null;

            if (_additionalVertexStream == null)
                return;

            if (Application.isPlaying)
                Destroy(_additionalVertexStream);
            else
                DestroyImmediate(_additionalVertexStream);
            _additionalVertexStream = null;
        }

        private void SetDiagnostic(string message, bool warning)
        {
            LastDiagnostic = message;
            if (!logDiagnostics)
                return;

            if (warning)
                Debug.LogWarning($"[DungeonAdjacentLightmapPoC] {message}", this);
            else
                Debug.Log($"[DungeonAdjacentLightmapPoC] {message}", this);
        }

        private struct AdjacentVertexStreamData
        {
            public Vector4 current;
            public Vector4 baseline;
            public Vector4 projectionBasis;
        }
    }
}
