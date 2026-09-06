using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DungeonPortalTransportPoC
{
    /// <summary>
    /// Immutable-by-convention authoring evidence for one receiver doorway's canonical
    /// Baseline, DirectOnly and Full baked-light responses. Runtime transport must not
    /// infer a bounce proxy from this asset; fitting is an explicit later authoring step.
    /// </summary>
    [CreateAssetMenu(
        fileName = "DungeonPortalReceiverResponseCapture",
        menuName = "Dungeon/Lighting/Portal Transport Receiver Response Capture")]
    public sealed class DungeonPortalReceiverResponseCapture : ScriptableObject
    {
        public const int CurrentSchemaVersion = 7;
        public const string BaselineStateName = "Baseline";
        public const string DirectOnlyStateName = "DirectOnly";
        public const string FullStateName = "Full";

        private const float CanonicalSpotAngle = 131.81032f;
        private const int CanonicalCullingMask = 66177;
        private const int DungeonRenderingLayerMask = 2;
        private const float StartRoomInjectorRange = 21.602848f;
        private const float AdministrativeSegregationInjectorRange = 57.865467f;

        [SerializeField] private int schemaVersion = CurrentSchemaVersion;
        [SerializeField] private string receiverRoomId;
        [SerializeField] private string stableDoorwayId;
        [SerializeField] private CaptureProvenance provenance;
        [SerializeField] private CaptureState baseline;
        [SerializeField] private CaptureState directOnly;
        [SerializeField] private CaptureState full;

        public int SchemaVersion => schemaVersion;
        public string ReceiverRoomId => receiverRoomId;
        public string StableDoorwayId => stableDoorwayId;
        public CaptureProvenance Provenance => CloneProvenance(provenance);
        public CaptureState Baseline => CloneState(baseline);
        public CaptureState DirectOnly => CloneState(directOnly);
        public CaptureState Full => CloneState(full);

        /// <summary>
        /// Editor-only callers provide every state in one transaction. The capture asset
        /// intentionally stays separate from DungeonPortalEndpointProfile until a later,
        /// evidenced proxy-fit step has been approved.
        /// </summary>
        public void ConfigureAuthoring(
            string stableReceiverRoomId,
            string stableReceiverDoorwayId,
            CaptureProvenance captureProvenance,
            CaptureState baselineState,
            CaptureState directOnlyState,
            CaptureState fullState)
        {
            schemaVersion = CurrentSchemaVersion;
            receiverRoomId = stableReceiverRoomId != null ? stableReceiverRoomId.Trim() : string.Empty;
            stableDoorwayId = stableReceiverDoorwayId != null
                ? stableReceiverDoorwayId.Trim()
                : string.Empty;
            provenance = CloneProvenance(captureProvenance);
            baseline = CloneState(baselineState);
            directOnly = CloneState(directOnlyState);
            full = CloneState(fullState);
        }

        public bool TryValidate(out string error)
        {
            if (schemaVersion != CurrentSchemaVersion)
            {
                error = $"Unsupported receiver-response schema version {schemaVersion}.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(receiverRoomId) ||
                string.IsNullOrWhiteSpace(stableDoorwayId))
            {
                error = "Receiver room id or stable doorway id is empty.";
                return false;
            }

            int expectedLightmaps;
            int exactBaselineRendererCount;
            float expectedInjectorRange;
            if (string.Equals(receiverRoomId, "StartRoom_R000", StringComparison.Ordinal))
            {
                expectedLightmaps = 4;
                // The connected doorway keeps No_Door_Placement active and disables
                // exactly one production blocker renderer, so the response workspace
                // intentionally has one fewer lightmapped renderer than the standalone
                // room bake asset.
                exactBaselineRendererCount = 76;
                expectedInjectorRange = StartRoomInjectorRange;
            }
            else if (string.Equals(receiverRoomId, "AdminstrativeSegregation_R000", StringComparison.Ordinal))
            {
                // The connected/open-passage workspace has its own fresh atlas packing.
                // Baseline is authoritative; DirectOnly/Full are compared to its exact
                // count, dimensions, source indices, renderer indices and scale/offsets.
                expectedLightmaps = baseline.lightmaps != null ? baseline.lightmaps.Length : 0;
                // The standalone Admin BakeData serializes inactive renderers too, so
                // its 306 entries are not an authoritative connected-scene lightmap
                // count. Baseline records the actual count; later states must match it.
                exactBaselineRendererCount = 0;
                expectedInjectorRange = AdministrativeSegregationInjectorRange;
            }
            else
            {
                error = "Receiver response capture only accepts the canonical StartRoom_R000 or AdminstrativeSegregation_R000 receiver.";
                return false;
            }

            int expectedRenderers = baseline.renderers != null ? baseline.renderers.Length : 0;
            if (expectedLightmaps <= 0 || expectedRenderers <= 0 ||
                (exactBaselineRendererCount > 0 && expectedRenderers != exactBaselineRendererCount))
            {
                error = "Baseline has no authoritative connected-scene lightmap/renderer layout, or its exact renderer count drifted.";
                return false;
            }

            if (!TryValidateProvenance(provenance, out error) ||
                !TryValidateState(
                    baseline,
                    BaselineStateName,
                    false,
                    0f,
                    expectedLightmaps,
                    expectedRenderers,
                    expectedInjectorRange,
                    provenance.lightingSettingsCloneDependencyHash,
                    out error) ||
                !TryValidateState(
                    directOnly,
                    DirectOnlyStateName,
                    true,
                    0f,
                    expectedLightmaps,
                    expectedRenderers,
                    expectedInjectorRange,
                    provenance.lightingSettingsCloneDependencyHash,
                    out error) ||
                !TryValidateState(
                    full,
                    FullStateName,
                    true,
                    1f,
                    expectedLightmaps,
                    expectedRenderers,
                    expectedInjectorRange,
                    provenance.lightingSettingsCloneDependencyHash,
                    out error))
            {
                return false;
            }

            if (!TryValidateCompatibleLayouts(baseline, directOnly, DirectOnlyStateName, out error) ||
                !TryValidateCompatibleLayouts(baseline, full, FullStateName, out error))
            {
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Editor authoring tools use this before reusing a resumable non-Baseline
        /// checkpoint. It exposes the same exact renderer/lightmap/probe/camera layout
        /// comparison used by the final capture validator.
        /// </summary>
        public static bool TryValidateCompatibleStateLayouts(
            CaptureState baselineState,
            CaptureState comparisonState,
            out string error)
        {
            string comparisonName = string.IsNullOrWhiteSpace(comparisonState.stateName)
                ? "comparison"
                : comparisonState.stateName;
            return TryValidateCompatibleLayouts(
                baselineState,
                comparisonState,
                comparisonName,
                out error);
        }

        [Serializable]
        public struct CaptureProvenance
        {
            public string toolVersion;
            public string unityVersion;
            public string stableDoorwayPath;
            public string receiverPowerState;
            public string reflectionProbePolicy;
            /// <summary>
            /// Human-readable declaration of the receiver-side mirrored injector
            /// convention. This is an explicitly recorded inference, not a
            /// production endpoint/profile value.
            /// </summary>
            public string injectorPlacementInterpretation;
            public string workspaceScenePath;
            /// <summary>
            /// Exact dependency hash of the saved, canonical, pre-bake workspace. The
            /// workspace is restored to this state after every completed capture so a
            /// resume never treats stale baked scene data as canonical input.
            /// </summary>
            public string canonicalWorkspaceDependencyHash;
            public string canonicalWorkspaceSetupSignature;
            public string canonicalFullRendererParitySignature;
            public int canonicalFullRendererCount;
            public int canonicalFullRendererComponentCount;
            public bool canonicalMaterialPropertyBlocksVerifiedEmpty;
            public string lightingSettingsClonePath;
            public string lightingSettingsCloneDependencyHash;
            public string capturedUtcIso8601;
            public AssetFingerprint[] productionInputs;
        }

        [Serializable]
        public struct AssetFingerprint
        {
            public string assetPath;
            public string dependencyHash;
            public bool wasDirtyBeforeCapture;
            public bool wasDirtyAfterCapture;
        }

        [Serializable]
        public struct CaptureState
        {
            public bool captured;
            public string stateName;
            public string capturedUtcIso8601;
            public string stateFolderPath;
            public double elapsedSeconds;
            public string stateHash;
            public string probeLocalPositionSignature;
            public string rendererLayoutSignature;
            /// <summary>
            /// Separate whole-workspace renderer inventory. Unlike <see cref="renderers"/>,
            /// it includes every Renderer in the receiver room and real-door subtree,
            /// including non-lightmapped components, so scene overrides cannot hide in
            /// a compact lightmap-only mapping.
            /// </summary>
            public string fullRendererParitySignature;
            public int fullRendererCount;
            public int fullRendererComponentCount;
            public bool materialPropertyBlocksVerifiedEmpty;
            public FullRendererInventoryEntry[] fullRendererInventory;
            public string lightingSettingsCloneDependencyHashBeforeBake;
            public string lightingSettingsCloneDependencyHashAfterBake;
            public int doorLightProbeCount;
            public string doorLightProbeLocalPositionSignature;
            public LightmapsMode lightmapsMode;
            public InjectorProvenance injector;
            public CaptureLightmap[] lightmaps;
            public CaptureRenderer[] renderers;
            public ProbeSample[] probes;
            public FixedCameraCapture[] fixedCameraCaptures;
        }

        [Serializable]
        public struct InjectorProvenance
        {
            public bool enabled;
            public LightType type;
            public LightmapBakeType lightmapBakeType;
            [ColorUsage(true, true)] public Color color;
            public float intensity;
            public float bounceIntensity;
            public Vector3 localPosition;
            public Vector3 localEulerAngles;
            public string placementInterpretation;
            public float range;
            public float spotAngle;
            public float innerSpotAngle;
            public LightShadows shadows;
            public float shadowStrength;
            public int cullingMask;
            public int renderingLayerMask;
            public int shadowRenderingLayerMask;
        }

        [Serializable]
        public struct CaptureLightmap
        {
            public int sourceLightmapIndex;
            public Texture2D colorTexture;
            public Texture2D directionTexture;
            public Texture2D shadowMaskTexture;
            public bool hasShadowMask;
            public string colorAssetPath;
            public string directionAssetPath;
            public string shadowMaskAssetPath;
            public string colorDependencyHash;
            public string directionDependencyHash;
            public string shadowMaskDependencyHash;
            public int colorWidth;
            public int colorHeight;
            public string colorFormat;
            public int directionWidth;
            public int directionHeight;
            public string directionFormat;
            public int shadowMaskWidth;
            public int shadowMaskHeight;
            public string shadowMaskFormat;
        }

        [Serializable]
        public struct CaptureRenderer
        {
            public string relativePath;
            /// <summary>
            /// Stable ordinal among canonical renderer buckets at the same relative path.
            /// It disambiguates otherwise identical component ordinals after lightmapping.
            /// </summary>
            public int rendererBucketIndex;
            public int componentOrdinal;
            public string meshAssetGuid;
            public long meshLocalId;
            public string meshUv2Hash;
            public int lightmapIndex;
            public Vector4 lightmapScaleOffset;
        }

        /// <summary>
        /// Compact auditable index into the full renderer-parity hash. The per-entry
        /// parity metadata hash covers visual, material, GI, probe, static, and
        /// lightmapping fields; it deliberately has no runtime use.
        /// </summary>
        [Serializable]
        public struct FullRendererInventoryEntry
        {
            public string scope;
            public string relativePath;
            public string rendererType;
            public int componentOrdinal;
            public string canonicalBucket;
            public string parityMetadataHash;
        }

        /// <summary>
        /// A deterministic, HDR fixed-camera ground-truth frame. It is evidence for a
        /// later visual proxy fit only; it is never sampled by runtime transport.
        /// </summary>
        [Serializable]
        public struct FixedCameraCapture
        {
            public string cameraId;
            public string cameraPath;
            public Vector3 doorwayLocalPosition;
            public Vector3 doorwayLocalEulerAngles;
            public float fieldOfView;
            public float nearClipPlane;
            public float farClipPlane;
            public bool orthographic;
            public float orthographicSize;
            public float aspect;
            public CameraClearFlags clearFlags;
            [ColorUsage(true, true)] public Color backgroundColor;
            public int cullingMask;
            public RenderingPath renderingPath;
            public bool postProcessingEnabled;
            public AntialiasingMode antialiasing;
            public bool dithering;
            public int volumeLayerMask;
            public bool allowMsaa;
            public bool allowDynamicResolution;
            public bool useOcclusionCulling;
            public int urpRendererIndex;
            public string renderPipelineAssetPath;
            public string renderPipelineDependencyHash;
            public bool hdr;
            public bool linearPreTonemap;
            public Texture2D hdrColorTexture;
            public string hdrColorAssetPath;
            public string hdrColorDependencyHash;
            public int width;
            public int height;
            public string textureFormat;
        }

        [Serializable]
        public struct ProbeSample
        {
            public int probeIndex;
            public Vector3 localPosition;
            public Vector3 worldPosition;
            public Vector3 coefficient0;
            public Vector3 coefficient1;
            public Vector3 coefficient2;
            public Vector3 coefficient3;
            public Vector3 coefficient4;
            public Vector3 coefficient5;
            public Vector3 coefficient6;
            public Vector3 coefficient7;
            public Vector3 coefficient8;
            public Vector4 occlusion;

            public Vector3 GetCoefficient(int index)
            {
                switch (index)
                {
                    case 0: return coefficient0;
                    case 1: return coefficient1;
                    case 2: return coefficient2;
                    case 3: return coefficient3;
                    case 4: return coefficient4;
                    case 5: return coefficient5;
                    case 6: return coefficient6;
                    case 7: return coefficient7;
                    case 8: return coefficient8;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(index), index, "SH coefficient index must be 0..8.");
                }
            }

            public SphericalHarmonicsL2 ToSphericalHarmonics()
            {
                var result = new SphericalHarmonicsL2();
                for (int coefficient = 0; coefficient < 9; coefficient++)
                {
                    Vector3 value = GetCoefficient(coefficient);
                    result[0, coefficient] = value.x;
                    result[1, coefficient] = value.y;
                    result[2, coefficient] = value.z;
                }

                return result;
            }
        }

        private static bool TryValidateProvenance(CaptureProvenance value, out string error)
        {
            if (string.IsNullOrWhiteSpace(value.toolVersion) ||
                string.IsNullOrWhiteSpace(value.unityVersion) ||
                string.IsNullOrWhiteSpace(value.stableDoorwayPath) ||
                !string.Equals(value.receiverPowerState, "P0", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(value.reflectionProbePolicy) ||
                string.IsNullOrWhiteSpace(value.injectorPlacementInterpretation) ||
                string.IsNullOrWhiteSpace(value.workspaceScenePath) ||
                string.IsNullOrWhiteSpace(value.canonicalWorkspaceDependencyHash) ||
                string.IsNullOrWhiteSpace(value.canonicalWorkspaceSetupSignature) ||
                string.IsNullOrWhiteSpace(value.canonicalFullRendererParitySignature) ||
                value.canonicalFullRendererCount <= 0 ||
                value.canonicalFullRendererComponentCount != value.canonicalFullRendererCount ||
                !value.canonicalMaterialPropertyBlocksVerifiedEmpty ||
                string.IsNullOrWhiteSpace(value.lightingSettingsClonePath) ||
                string.IsNullOrWhiteSpace(value.lightingSettingsCloneDependencyHash) ||
                string.IsNullOrWhiteSpace(value.capturedUtcIso8601))
            {
                error = "Capture provenance is incomplete.";
                return false;
            }

            AssetFingerprint[] inputs = value.productionInputs ?? Array.Empty<AssetFingerprint>();
            if (inputs.Length == 0)
            {
                error = "Capture provenance has no production input fingerprints.";
                return false;
            }

            for (int i = 0; i < inputs.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(inputs[i].assetPath) ||
                    string.IsNullOrWhiteSpace(inputs[i].dependencyHash) ||
                    inputs[i].wasDirtyBeforeCapture || inputs[i].wasDirtyAfterCapture)
                {
                    error = $"Production input fingerprint {i} is incomplete or was dirty.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidateState(
            CaptureState state,
            string expectedName,
            bool expectedEnabled,
            float expectedBounceIntensity,
            int expectedLightmapCount,
            int expectedRendererCount,
            float expectedInjectorRange,
            string expectedLightingSettingsCloneDependencyHash,
            out string error)
        {
            if (!state.captured || !string.Equals(state.stateName, expectedName, StringComparison.Ordinal))
            {
                error = $"Expected captured state '{expectedName}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(state.capturedUtcIso8601) ||
                string.IsNullOrWhiteSpace(state.stateFolderPath) ||
                string.IsNullOrWhiteSpace(state.stateHash) ||
                string.IsNullOrWhiteSpace(state.probeLocalPositionSignature) ||
                string.IsNullOrWhiteSpace(state.rendererLayoutSignature) ||
                string.IsNullOrWhiteSpace(state.fullRendererParitySignature) ||
                state.fullRendererCount <= 0 ||
                state.fullRendererComponentCount != state.fullRendererCount ||
                !state.materialPropertyBlocksVerifiedEmpty ||
                string.IsNullOrWhiteSpace(state.lightingSettingsCloneDependencyHashBeforeBake) ||
                string.IsNullOrWhiteSpace(state.lightingSettingsCloneDependencyHashAfterBake) ||
                state.doorLightProbeCount != 8 ||
                string.IsNullOrWhiteSpace(state.doorLightProbeLocalPositionSignature) ||
                double.IsNaN(state.elapsedSeconds) || double.IsInfinity(state.elapsedSeconds) ||
                state.elapsedSeconds < 0d)
            {
                error = $"State '{expectedName}' provenance is incomplete.";
                return false;
            }

            if (!string.Equals(
                    state.lightingSettingsCloneDependencyHashBeforeBake,
                    expectedLightingSettingsCloneDependencyHash,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    state.lightingSettingsCloneDependencyHashAfterBake,
                    expectedLightingSettingsCloneDependencyHash,
                    StringComparison.Ordinal))
            {
                error = $"State '{expectedName}' LightingSettings clone hash drifted before or after bake.";
                return false;
            }

            if (state.lightmapsMode != LightmapsMode.CombinedDirectional)
            {
                error = $"State '{expectedName}' is not CombinedDirectional.";
                return false;
            }

            if (!TryValidateInjector(
                    state.injector,
                    expectedName,
                    expectedEnabled,
                    expectedBounceIntensity,
                    expectedInjectorRange,
                    out error))
            {
                return false;
            }

            CaptureLightmap[] lightmaps = state.lightmaps ?? Array.Empty<CaptureLightmap>();
            CaptureRenderer[] renderers = state.renderers ?? Array.Empty<CaptureRenderer>();
            FullRendererInventoryEntry[] fullInventory = state.fullRendererInventory ??
                Array.Empty<FullRendererInventoryEntry>();
            ProbeSample[] probes = state.probes ?? Array.Empty<ProbeSample>();
            FixedCameraCapture[] cameras = state.fixedCameraCaptures ?? Array.Empty<FixedCameraCapture>();
            if (lightmaps.Length != expectedLightmapCount || renderers.Length != expectedRendererCount ||
                fullInventory.Length != state.fullRendererCount || state.fullRendererCount <= expectedRendererCount ||
                probes.Length != 27 ||
                cameras.Length != 4)
            {
                error = $"State '{expectedName}' is missing lightmaps/renderers or does not have the canonical 27 probes and four fixed HDR cameras.";
                return false;
            }

            for (int i = 0; i < lightmaps.Length; i++)
            {
                CaptureLightmap lightmap = lightmaps[i];
                if (lightmap.sourceLightmapIndex < 0 || lightmap.colorTexture == null ||
                    lightmap.directionTexture == null || lightmap.colorWidth <= 0 ||
                    lightmap.colorHeight <= 0 || lightmap.directionWidth <= 0 ||
                    lightmap.directionHeight <= 0 ||
                    string.IsNullOrWhiteSpace(lightmap.colorAssetPath) ||
                    string.IsNullOrWhiteSpace(lightmap.directionAssetPath) ||
                    string.IsNullOrWhiteSpace(lightmap.colorDependencyHash) ||
                    string.IsNullOrWhiteSpace(lightmap.directionDependencyHash) ||
                    string.IsNullOrWhiteSpace(lightmap.colorFormat) ||
                    string.IsNullOrWhiteSpace(lightmap.directionFormat) ||
                    lightmap.hasShadowMask != (lightmap.shadowMaskTexture != null) ||
                    (lightmap.shadowMaskTexture != null &&
                     (lightmap.shadowMaskWidth <= 0 || lightmap.shadowMaskHeight <= 0 ||
                      string.IsNullOrWhiteSpace(lightmap.shadowMaskAssetPath) ||
                      string.IsNullOrWhiteSpace(lightmap.shadowMaskDependencyHash) ||
                      string.IsNullOrWhiteSpace(lightmap.shadowMaskFormat))))
                {
                    error = $"State '{expectedName}' lightmap {i} is incomplete.";
                    return false;
                }
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                CaptureRenderer renderer = renderers[i];
                if (string.IsNullOrWhiteSpace(renderer.relativePath) ||
                    renderer.rendererBucketIndex < 0 ||
                    renderer.componentOrdinal < 0 ||
                    string.IsNullOrWhiteSpace(renderer.meshAssetGuid) ||
                    renderer.meshLocalId == 0L ||
                    string.IsNullOrWhiteSpace(renderer.meshUv2Hash) ||
                    !IsFinite(renderer.lightmapScaleOffset))
                {
                    error = $"State '{expectedName}' renderer {i} is incomplete.";
                    return false;
                }
            }

            var canonicalBuckets = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < fullInventory.Length; i++)
            {
                FullRendererInventoryEntry entry = fullInventory[i];
                if (string.IsNullOrWhiteSpace(entry.scope) ||
                    string.IsNullOrWhiteSpace(entry.relativePath) ||
                    string.IsNullOrWhiteSpace(entry.rendererType) ||
                    entry.componentOrdinal < 0 ||
                    string.IsNullOrWhiteSpace(entry.canonicalBucket) ||
                    string.IsNullOrWhiteSpace(entry.parityMetadataHash) ||
                    !canonicalBuckets.Add(entry.canonicalBucket))
                {
                    error = $"State '{expectedName}' full renderer inventory {i} is incomplete or duplicated.";
                    return false;
                }
            }

            for (int i = 0; i < probes.Length; i++)
            {
                if (probes[i].probeIndex != i || !IsFinite(probes[i].localPosition) ||
                    !IsFinite(probes[i].worldPosition) || !IsFinite(probes[i].occlusion))
                {
                    error = $"State '{expectedName}' probe {i} has invalid position or occlusion.";
                    return false;
                }

                for (int coefficient = 0; coefficient < 9; coefficient++)
                {
                    if (!IsFinite(probes[i].GetCoefficient(coefficient)))
                    {
                        error = $"State '{expectedName}' probe {i} has an invalid SH coefficient.";
                        return false;
                    }
                }
            }

            for (int i = 0; i < cameras.Length; i++)
            {
                FixedCameraCapture camera = cameras[i];
                if (string.IsNullOrWhiteSpace(camera.cameraId) ||
                    string.IsNullOrWhiteSpace(camera.cameraPath) || !IsFinite(camera.doorwayLocalPosition) ||
                    !IsFinite(camera.doorwayLocalEulerAngles) || !Approximately(camera.fieldOfView, 90f) ||
                    camera.orthographic ||
                    !IsFinitePositive(camera.nearClipPlane) || !IsFinitePositive(camera.farClipPlane) ||
                    camera.farClipPlane <= camera.nearClipPlane ||
                    !IsFiniteNonNegative(camera.orthographicSize) || !Approximately(camera.aspect, 1f) ||
                    camera.clearFlags != CameraClearFlags.SolidColor ||
                    !Approximately(camera.backgroundColor, Color.black) || camera.cullingMask != CanonicalCullingMask ||
                    camera.renderingPath != RenderingPath.Forward ||
                    camera.postProcessingEnabled || camera.antialiasing != AntialiasingMode.None || camera.dithering ||
                    camera.volumeLayerMask != 0 || camera.allowMsaa || camera.allowDynamicResolution ||
                    camera.useOcclusionCulling || string.IsNullOrWhiteSpace(camera.renderPipelineAssetPath) ||
                    string.IsNullOrWhiteSpace(camera.renderPipelineDependencyHash) ||
                    !camera.hdr || !camera.linearPreTonemap || camera.hdrColorTexture == null ||
                    string.IsNullOrWhiteSpace(camera.hdrColorAssetPath) ||
                    string.IsNullOrWhiteSpace(camera.hdrColorDependencyHash) || camera.width <= 0 ||
                    camera.width != 512 || camera.height != 512 ||
                    !string.Equals(camera.textureFormat, TextureFormat.RGBAHalf.ToString(), StringComparison.Ordinal))
                {
                    error = $"State '{expectedName}' fixed camera {i} is incomplete.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidateInjector(
            InjectorProvenance injector,
            string stateName,
            bool expectedEnabled,
            float expectedBounceIntensity,
            float expectedRange,
            out string error)
        {
            if (injector.enabled != expectedEnabled || injector.type != LightType.Spot ||
                injector.lightmapBakeType != LightmapBakeType.Baked ||
                !Approximately(injector.bounceIntensity, expectedBounceIntensity) ||
                !Approximately(injector.intensity, 1f) ||
                !Approximately(injector.color, Color.white) ||
                !Approximately(injector.localPosition, new Vector3(0f, 1f, 0.5f)) ||
                !Approximately(injector.localEulerAngles, new Vector3(0f, 180f, 0f)) ||
                string.IsNullOrWhiteSpace(injector.placementInterpretation) ||
                !Approximately(injector.range, expectedRange) ||
                !Approximately(injector.spotAngle, CanonicalSpotAngle) ||
                !Approximately(injector.innerSpotAngle, CanonicalSpotAngle) ||
                injector.shadows != LightShadows.Soft || !Approximately(injector.shadowStrength, 1f) ||
                injector.cullingMask != CanonicalCullingMask ||
                injector.renderingLayerMask != DungeonRenderingLayerMask ||
                injector.shadowRenderingLayerMask != DungeonRenderingLayerMask)
            {
                error = $"State '{stateName}' does not use the canonical baked Spot injector.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidateCompatibleLayouts(
            CaptureState baselineState,
            CaptureState comparisonState,
            string comparisonName,
            out string error)
        {
            if (baselineState.lightmapsMode != comparisonState.lightmapsMode ||
                baselineState.injector.type != comparisonState.injector.type ||
                baselineState.injector.lightmapBakeType != comparisonState.injector.lightmapBakeType ||
                !Approximately(baselineState.injector.color, comparisonState.injector.color) ||
                !Approximately(baselineState.injector.intensity, comparisonState.injector.intensity) ||
                !Approximately(baselineState.injector.localPosition, comparisonState.injector.localPosition) ||
                !Approximately(baselineState.injector.localEulerAngles, comparisonState.injector.localEulerAngles) ||
                !string.Equals(
                    baselineState.injector.placementInterpretation,
                    comparisonState.injector.placementInterpretation,
                    StringComparison.Ordinal) ||
                !Approximately(baselineState.injector.range, comparisonState.injector.range) ||
                !Approximately(baselineState.injector.spotAngle, comparisonState.injector.spotAngle) ||
                !Approximately(baselineState.injector.innerSpotAngle, comparisonState.injector.innerSpotAngle) ||
                baselineState.injector.shadows != comparisonState.injector.shadows ||
                !Approximately(baselineState.injector.shadowStrength, comparisonState.injector.shadowStrength) ||
                baselineState.injector.cullingMask != comparisonState.injector.cullingMask ||
                baselineState.injector.renderingLayerMask != comparisonState.injector.renderingLayerMask ||
                baselineState.injector.shadowRenderingLayerMask != comparisonState.injector.shadowRenderingLayerMask)
            {
                error = $"{comparisonName} does not match Baseline lightmap or layer policy.";
                return false;
            }

            CaptureLightmap[] baselineLightmaps = baselineState.lightmaps ?? Array.Empty<CaptureLightmap>();
            CaptureLightmap[] comparisonLightmaps = comparisonState.lightmaps ?? Array.Empty<CaptureLightmap>();
            if (baselineLightmaps.Length != comparisonLightmaps.Length)
            {
                error = $"{comparisonName} lightmap count differs from Baseline.";
                return false;
            }

            for (int i = 0; i < baselineLightmaps.Length; i++)
            {
                CaptureLightmap left = baselineLightmaps[i];
                CaptureLightmap right = comparisonLightmaps[i];
                if (left.sourceLightmapIndex != right.sourceLightmapIndex ||
                    left.colorWidth != right.colorWidth || left.colorHeight != right.colorHeight ||
                    left.directionWidth != right.directionWidth ||
                    left.directionHeight != right.directionHeight ||
                    left.hasShadowMask != right.hasShadowMask ||
                    left.shadowMaskWidth != right.shadowMaskWidth ||
                    left.shadowMaskHeight != right.shadowMaskHeight ||
                    !string.Equals(left.colorFormat, right.colorFormat, StringComparison.Ordinal) ||
                    !string.Equals(left.directionFormat, right.directionFormat, StringComparison.Ordinal) ||
                    !string.Equals(left.shadowMaskFormat, right.shadowMaskFormat, StringComparison.Ordinal))
                {
                    error = $"{comparisonName} lightmap layout differs from Baseline at entry {i}.";
                    return false;
                }
            }

            CaptureRenderer[] baselineRenderers = baselineState.renderers ?? Array.Empty<CaptureRenderer>();
            CaptureRenderer[] comparisonRenderers = comparisonState.renderers ?? Array.Empty<CaptureRenderer>();
            if (baselineRenderers.Length != comparisonRenderers.Length)
            {
                error = $"{comparisonName} renderer count differs from Baseline.";
                return false;
            }

            for (int i = 0; i < baselineRenderers.Length; i++)
            {
                CaptureRenderer left = baselineRenderers[i];
                CaptureRenderer right = comparisonRenderers[i];
                if (!string.Equals(left.relativePath, right.relativePath, StringComparison.Ordinal) ||
                    left.rendererBucketIndex != right.rendererBucketIndex ||
                    left.componentOrdinal != right.componentOrdinal ||
                    !string.Equals(left.meshAssetGuid, right.meshAssetGuid, StringComparison.Ordinal) ||
                    left.meshLocalId != right.meshLocalId ||
                    !string.Equals(left.meshUv2Hash, right.meshUv2Hash, StringComparison.Ordinal) ||
                    left.lightmapIndex != right.lightmapIndex ||
                    !Approximately(left.lightmapScaleOffset, right.lightmapScaleOffset))
                {
                    error = $"{comparisonName} renderer layout differs from Baseline at entry {i}.";
                    return false;
                }
            }

            ProbeSample[] baselineProbes = baselineState.probes ?? Array.Empty<ProbeSample>();
            ProbeSample[] comparisonProbes = comparisonState.probes ?? Array.Empty<ProbeSample>();
            if (baselineProbes.Length != comparisonProbes.Length)
            {
                error = $"{comparisonName} probe count differs from Baseline.";
                return false;
            }

            for (int i = 0; i < baselineProbes.Length; i++)
            {
                if (baselineProbes[i].probeIndex != comparisonProbes[i].probeIndex ||
                    !Approximately(baselineProbes[i].localPosition, comparisonProbes[i].localPosition))
                {
                    error = $"{comparisonName} probe grid differs from Baseline at entry {i}.";
                    return false;
                }
            }

            if (!string.Equals(
                    baselineState.probeLocalPositionSignature,
                    comparisonState.probeLocalPositionSignature,
                    StringComparison.Ordinal))
            {
                error = $"{comparisonName} probe local-position signature differs from Baseline.";
                return false;
            }

            if (!string.Equals(
                    baselineState.rendererLayoutSignature,
                    comparisonState.rendererLayoutSignature,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    baselineState.fullRendererParitySignature,
                    comparisonState.fullRendererParitySignature,
                    StringComparison.Ordinal) ||
                baselineState.fullRendererCount != comparisonState.fullRendererCount ||
                baselineState.fullRendererComponentCount != comparisonState.fullRendererComponentCount ||
                baselineState.materialPropertyBlocksVerifiedEmpty != comparisonState.materialPropertyBlocksVerifiedEmpty ||
                baselineState.doorLightProbeCount != comparisonState.doorLightProbeCount ||
                !string.Equals(
                    baselineState.doorLightProbeLocalPositionSignature,
                    comparisonState.doorLightProbeLocalPositionSignature,
                    StringComparison.Ordinal))
            {
                error = $"{comparisonName} renderer or preserved-door-probe signature differs from Baseline.";
                return false;
            }

            FullRendererInventoryEntry[] baselineInventory = baselineState.fullRendererInventory ??
                Array.Empty<FullRendererInventoryEntry>();
            FullRendererInventoryEntry[] comparisonInventory = comparisonState.fullRendererInventory ??
                Array.Empty<FullRendererInventoryEntry>();
            if (baselineInventory.Length != comparisonInventory.Length)
            {
                error = $"{comparisonName} full renderer inventory count differs from Baseline.";
                return false;
            }

            for (int i = 0; i < baselineInventory.Length; i++)
            {
                FullRendererInventoryEntry left = baselineInventory[i];
                FullRendererInventoryEntry right = comparisonInventory[i];
                if (!string.Equals(left.scope, right.scope, StringComparison.Ordinal) ||
                    !string.Equals(left.relativePath, right.relativePath, StringComparison.Ordinal) ||
                    !string.Equals(left.rendererType, right.rendererType, StringComparison.Ordinal) ||
                    left.componentOrdinal != right.componentOrdinal ||
                    !string.Equals(left.canonicalBucket, right.canonicalBucket, StringComparison.Ordinal) ||
                    !string.Equals(left.parityMetadataHash, right.parityMetadataHash, StringComparison.Ordinal))
                {
                    error = $"{comparisonName} full renderer inventory differs from Baseline at entry {i}.";
                    return false;
                }
            }

            FixedCameraCapture[] baselineCameras =
                baselineState.fixedCameraCaptures ?? Array.Empty<FixedCameraCapture>();
            FixedCameraCapture[] comparisonCameras =
                comparisonState.fixedCameraCaptures ?? Array.Empty<FixedCameraCapture>();
            if (baselineCameras.Length != comparisonCameras.Length)
            {
                error = $"{comparisonName} fixed-camera count differs from Baseline.";
                return false;
            }

            for (int i = 0; i < baselineCameras.Length; i++)
            {
                FixedCameraCapture left = baselineCameras[i];
                FixedCameraCapture right = comparisonCameras[i];
                if (!string.Equals(left.cameraId, right.cameraId, StringComparison.Ordinal) ||
                    !string.Equals(left.cameraPath, right.cameraPath, StringComparison.Ordinal) ||
                    !Approximately(left.doorwayLocalPosition, right.doorwayLocalPosition) ||
                    !Approximately(left.doorwayLocalEulerAngles, right.doorwayLocalEulerAngles) ||
                    !Approximately(left.fieldOfView, right.fieldOfView) || left.hdr != right.hdr ||
                    !Approximately(left.nearClipPlane, right.nearClipPlane) ||
                    !Approximately(left.farClipPlane, right.farClipPlane) ||
                    left.orthographic != right.orthographic ||
                    !Approximately(left.orthographicSize, right.orthographicSize) ||
                    !Approximately(left.aspect, right.aspect) || left.clearFlags != right.clearFlags ||
                    !Approximately(left.backgroundColor, right.backgroundColor) ||
                    left.cullingMask != right.cullingMask || left.renderingPath != right.renderingPath ||
                    left.postProcessingEnabled != right.postProcessingEnabled ||
                    left.antialiasing != right.antialiasing ||
                    left.dithering != right.dithering ||
                    left.volumeLayerMask != right.volumeLayerMask ||
                    left.allowMsaa != right.allowMsaa ||
                    left.allowDynamicResolution != right.allowDynamicResolution ||
                    left.useOcclusionCulling != right.useOcclusionCulling ||
                    left.urpRendererIndex != right.urpRendererIndex ||
                    !string.Equals(left.renderPipelineAssetPath, right.renderPipelineAssetPath, StringComparison.Ordinal) ||
                    !string.Equals(left.renderPipelineDependencyHash, right.renderPipelineDependencyHash, StringComparison.Ordinal) ||
                    left.linearPreTonemap != right.linearPreTonemap ||
                    left.width != right.width || left.height != right.height ||
                    !string.Equals(left.textureFormat, right.textureFormat, StringComparison.Ordinal))
                {
                    error = $"{comparisonName} fixed-camera layout differs from Baseline at entry {i}.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private static CaptureProvenance CloneProvenance(CaptureProvenance value)
        {
            value.productionInputs = CloneArray(value.productionInputs);
            return value;
        }

        private static CaptureState CloneState(CaptureState value)
        {
            value.lightmaps = CloneArray(value.lightmaps);
            value.renderers = CloneArray(value.renderers);
            value.fullRendererInventory = CloneArray(value.fullRendererInventory);
            value.probes = CloneArray(value.probes);
            value.fixedCameraCaptures = CloneArray(value.fixedCameraCaptures);
            return value;
        }

        private static T[] CloneArray<T>(T[] source)
        {
            return source != null ? (T[])source.Clone() : Array.Empty<T>();
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Vector4 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinitePositive(float value)
        {
            return IsFinite(value) && value > 0f;
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return IsFinite(value) && value >= 0f;
        }

        private static bool Approximately(Color left, Color right)
        {
            return Approximately(left.r, right.r) && Approximately(left.g, right.g) &&
                   Approximately(left.b, right.b) && Approximately(left.a, right.a);
        }

        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return (left - right).sqrMagnitude <= 0.00000001f;
        }

        private static bool Approximately(Vector4 left, Vector4 right)
        {
            return (left - right).sqrMagnitude <= 0.00000001f;
        }

        private static bool Approximately(float left, float right)
        {
            return Mathf.Abs(left - right) <= 0.00001f;
        }
    }
}
