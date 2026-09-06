using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace DungeonPortalBakedBasisPoC
{
    /// <summary>
    /// Room-local baked lighting states and portal response bases. This asset is deliberately
    /// independent of a room prefab: renderer entries identify a canonical path and lightmap
    /// bucket, while a room instance supplies the actual Renderer references explicitly.
    ///
    /// Texture contract: native endpoint base and ON/OFF state atlases are linear
    /// shader-sampleable RGBAHalf, RGBA32, BC6H, or BC7. Repacked opposite-base direction
    /// atlases use linear RGBA32 so their directional alpha is preserved without BC7 block drift.
    /// Every sampled response texture in a semantic
    /// channel must match its P0 or P100 native endpoint texture's dimensions, mip chain,
    /// filter mode, wrap modes, anisotropy, and mip bias. Direct signed color-delta and
    /// directional-moment-delta atlases must be linear RGBAHalf. The compact signed encoding
    /// stores per-mip normalized positive/negative color magnitudes as a matching pair of
    /// linear BC6H textures, with a per-atlas linear RGBAHalf pair fallback when persisted
    /// BC6H reconstruction misses its quality gate. Directional-moment pairs likewise use
    /// linear BC7 with a per-atlas RGBAHalf pair fallback when BC7 misses the same persisted
    /// reconstruction gate. All compact channels use strictly-positive scales.
    /// Working lightmaps are always unique linear RGBAHalf Texture2Ds and preserve the captured
    /// endpoint's sampling contract.
    /// Color atlases contain full-HDR linear irradiance (alpha is ignored at composition time).
    /// A precomputed directional-moment delta stores the linearized
    /// CombinedDirectional response. For luminance Y, encoded q = direction.rgb - 0.5,
    /// and encoded rebalancing a = direction.a, the packed value is
    /// RGB = delta(Y * q / a) and A = delta(Y / (2 * a)).
    /// </summary>
    [CreateAssetMenu(
        fileName = "DungeonPortalBakedRoomBasisData",
        menuName = "Dungeon/Portal Baked Basis/Room Basis Data")]
    public sealed class DungeonPortalBakedRoomBasisData : ScriptableObject
    {
        public const string CurrentSchema = "DPBB-2";
        public const int SphericalHarmonicsCoefficientCount = 27;
        public const int NormalizedDeltaMaximumMipCount = 16;
        /// <summary>
        /// Positive fallback scale for a normalized signed-delta channel whose source values
        /// are zero for the whole mip. Compact scales may never be zero because the runtime
        /// contract deliberately distinguishes a valid zero channel from missing metadata.
        /// </summary>
        public const float NormalizedDeltaZeroChannelScale = 1e-8f;
        private const float RequiredPoseFractionTolerance = 0.0001f;
        private static readonly float[] RequiredReceiverDoorPoseFractions =
        {
            0.25f,
            0.5f,
            0.75f,
            1f
        };

        public enum ResponseAtlasEncoding
        {
            OnOffStatePair = 0,
            PrecomputedDelta = 1,
            NormalizedPositiveNegativeDelta = 2
        }

        public enum ProductionEndpoint
        {
            Power0 = 0,
            Power100 = 1
        }

        public readonly struct NativeEndpointAtlas
        {
            public NativeEndpointAtlas(
                ProductionEndpoint endpoint,
                int endpointBucketIndex,
                int nativeLocalLightmapIndex,
                string bucketId,
                Texture2D color,
                Texture2D direction,
                Texture2D shadowMask)
            {
                Endpoint = endpoint;
                EndpointBucketIndex = endpointBucketIndex;
                NativeLocalLightmapIndex = nativeLocalLightmapIndex;
                BucketId = bucketId ?? string.Empty;
                Color = color;
                Direction = direction;
                ShadowMask = shadowMask;
            }

            public ProductionEndpoint Endpoint { get; }
            public int EndpointBucketIndex { get; }
            public int NativeLocalLightmapIndex { get; }
            public string BucketId { get; }
            public Texture2D Color { get; }
            public Texture2D Direction { get; }
            public Texture2D ShadowMask { get; }
        }

        /// <summary>
        /// Two base states expressed in one captured production endpoint layout.  The layout
        /// endpoint is retained for the whole private-lightmap interval, so renderer indices/ST
        /// never change while <see cref="DungeonPortalBakedBasisRoomCompositor.BasePower01"/>
        /// moves between P0 and P100.
        /// </summary>
        public readonly struct BaseTransitionAtlas
        {
            public BaseTransitionAtlas(
                ProductionEndpoint layoutEndpoint,
                int endpointBucketIndex,
                int nativeLocalLightmapIndex,
                string bucketId,
                Texture2D power0Color,
                Texture2D power100Color,
                Texture2D power0Direction,
                Texture2D power100Direction,
                Texture2D unchangedShadowMask)
            {
                LayoutEndpoint = layoutEndpoint;
                EndpointBucketIndex = endpointBucketIndex;
                NativeLocalLightmapIndex = nativeLocalLightmapIndex;
                BucketId = bucketId ?? string.Empty;
                Power0Color = power0Color;
                Power100Color = power100Color;
                Power0Direction = power0Direction;
                Power100Direction = power100Direction;
                UnchangedShadowMask = unchangedShadowMask;
            }

            public ProductionEndpoint LayoutEndpoint { get; }
            public int EndpointBucketIndex { get; }
            public int NativeLocalLightmapIndex { get; }
            public string BucketId { get; }
            public Texture2D Power0Color { get; }
            public Texture2D Power100Color { get; }
            public Texture2D Power0Direction { get; }
            public Texture2D Power100Direction { get; }
            public Texture2D UnchangedShadowMask { get; }
        }

        [Serializable]
        public sealed class CanonicalAtlasBucket
        {
            [SerializeField] private string bucketId;
            [SerializeField] private int canonicalLocalLightmapIndex;
            [SerializeField] private Texture2D power0Color;
            [Tooltip("P100 base repacked into this exact P0 atlas layout for continuous power blending.")]
            [SerializeField] private Texture2D power100Color;
            [SerializeField] private Texture2D power0Direction;
            [Tooltip("P100 direction repacked into this exact P0 atlas layout.")]
            [SerializeField] private Texture2D power100Direction;
            [SerializeField] private Texture2D unchangedShadowMask;

            public string BucketId => bucketId;
            public int CanonicalLocalLightmapIndex => canonicalLocalLightmapIndex;
            public Texture2D Power0Color => power0Color;
            public Texture2D Power100Color => power100Color;
            public Texture2D Power0Direction => power0Direction;
            public Texture2D Power100Direction => power100Direction;
            public Texture2D UnchangedShadowMask => unchangedShadowMask;
            public int Width => power0Color != null ? power0Color.width : 0;
            public int Height => power0Color != null ? power0Color.height : 0;
            public int MipCount => power0Color != null ? power0Color.mipmapCount : 0;

            public void ConfigureAuthoring(
                string id,
                int localLightmapIndex,
                Texture2D p0Color,
                Texture2D p100Color,
                Texture2D p0Direction,
                Texture2D p100Direction,
                Texture2D shadowMask = null)
            {
                bucketId = id ?? string.Empty;
                canonicalLocalLightmapIndex = localLightmapIndex;
                power0Color = p0Color;
                power100Color = p100Color;
                power0Direction = p0Direction;
                power100Direction = p100Direction;
                unchangedShadowMask = shadowMask;
            }
        }

        [Serializable]
        public sealed class CanonicalRendererEntry
        {
            [Tooltip("roomRoot-relative hierarchy path plus #occurrence, for example " +
                     "Geometry/Wall#0. Occurrence is assigned by full Renderer traversal order.")]
            [SerializeField] private string canonicalRendererKey;
            [SerializeField] private int bucketIndex;
            [SerializeField] private int canonicalLocalLightmapIndex;
            [SerializeField] private Vector4 lightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f);
            [SerializeField] private int power100SourceLocalLightmapIndex;
            [SerializeField] private Vector4 power100SourceScaleOffset =
                new Vector4(1f, 1f, 0f, 0f);

            public string CanonicalRendererKey => canonicalRendererKey;
            public int BucketIndex => bucketIndex;
            public int CanonicalLocalLightmapIndex => canonicalLocalLightmapIndex;
            public Vector4 LightmapScaleOffset => lightmapScaleOffset;
            public int Power100SourceLocalLightmapIndex => power100SourceLocalLightmapIndex;
            public Vector4 Power100SourceScaleOffset => power100SourceScaleOffset;

            public void ConfigureAuthoring(
                string rendererKey,
                int atlasBucketIndex,
                int localLightmapIndex,
                Vector4 scaleOffset,
                int p100SourceLocalLightmapIndex,
                Vector4 p100SourceSt)
            {
                canonicalRendererKey = rendererKey ?? string.Empty;
                bucketIndex = atlasBucketIndex;
                canonicalLocalLightmapIndex = localLightmapIndex;
                lightmapScaleOffset = scaleOffset;
                power100SourceLocalLightmapIndex = p100SourceLocalLightmapIndex;
                power100SourceScaleOffset = p100SourceSt;
            }

            public int GetNativeLocalLightmapIndex(ProductionEndpoint endpoint)
            {
                return endpoint == ProductionEndpoint.Power100
                    ? power100SourceLocalLightmapIndex
                    : canonicalLocalLightmapIndex;
            }

            public Vector4 GetNativeScaleOffset(ProductionEndpoint endpoint)
            {
                return endpoint == ProductionEndpoint.Power100
                    ? power100SourceScaleOffset
                    : lightmapScaleOffset;
            }
        }

        /// <summary>
        /// Original, unrepacked P100 source atlas. DPBB-2 uses these exact native textures,
        /// mip chains, local lightmap identities, and shadow masks as the P100 composition base.
        /// </summary>
        [Serializable]
        public sealed class Power100SourceAtlas
        {
            [SerializeField] private int sourceLocalLightmapIndex;
            [SerializeField] private Texture2D color;
            [SerializeField] private Texture2D direction;
            [SerializeField] private Texture2D shadowMask;
            [Tooltip("P0 base repacked into this exact P100 atlas layout for continuous power blending.")]
            [SerializeField] private Texture2D power0ColorInPower100Layout;
            [SerializeField] private Texture2D power0DirectionInPower100Layout;

            public int SourceLocalLightmapIndex => sourceLocalLightmapIndex;
            public Texture2D Color => color;
            public Texture2D Direction => direction;
            public Texture2D ShadowMask => shadowMask;
            public Texture2D Power0ColorInPower100Layout => power0ColorInPower100Layout;
            public Texture2D Power0DirectionInPower100Layout => power0DirectionInPower100Layout;

            public void ConfigureAuthoring(
                int localLightmapIndex,
                Texture2D sourceColor,
                Texture2D sourceDirection,
                Texture2D sourceShadowMask = null)
            {
                sourceLocalLightmapIndex = localLightmapIndex;
                color = sourceColor;
                direction = sourceDirection;
                shadowMask = sourceShadowMask;
                power0ColorInPower100Layout = null;
                power0DirectionInPower100Layout = null;
            }

            public void ConfigureTransitionAuthoring(
                int localLightmapIndex,
                Texture2D exactPower100Color,
                Texture2D exactPower100Direction,
                Texture2D repackedPower0Color,
                Texture2D repackedPower0Direction,
                Texture2D unchangedSourceShadowMask = null)
            {
                sourceLocalLightmapIndex = localLightmapIndex;
                color = exactPower100Color;
                direction = exactPower100Direction;
                shadowMask = unchangedSourceShadowMask;
                power0ColorInPower100Layout = repackedPower0Color;
                power0DirectionInPower100Layout = repackedPower0Direction;
            }
        }

        [Serializable]
        public sealed class ResponseAtlas
        {
            [SerializeField] private int bucketIndex;
            [SerializeField] private ResponseAtlasEncoding encoding;

            [Header("On/Off state pair")]
            [SerializeField] private Texture2D offColor;
            [SerializeField] private Texture2D onColor;
            [SerializeField] private Texture2D offDirection;
            [SerializeField] private Texture2D onDirection;

            [Header("Precomputed signed delta")]
            [SerializeField] private Texture2D colorDelta;
            [SerializeField] private Texture2D directionalMomentDelta;

            [Header("Normalized positive/negative signed delta")]
            [Tooltip("Unsigned normalized positive color delta. Uses linear BC6H, or a matching per-atlas RGBAHalf fallback pair.")]
            [SerializeField] private Texture2D colorDeltaPositive;
            [Tooltip("Unsigned normalized negative color magnitude. Must match the positive texture as linear BC6H or RGBAHalf.")]
            [SerializeField] private Texture2D colorDeltaNegative;
            [Tooltip("One strictly-positive RGB scale per mip. W is reserved and must also " +
                     "be finite and positive.")]
            [SerializeField] private Vector4[] colorDeltaPositiveScales = Array.Empty<Vector4>();
            [SerializeField] private Vector4[] colorDeltaNegativeScales = Array.Empty<Vector4>();
            [Tooltip("Unsigned normalized positive directional-moment delta. Uses linear BC7, or a matching per-atlas RGBAHalf fallback pair.")]
            [SerializeField] private Texture2D directionalMomentDeltaPositive;
            [Tooltip("Unsigned normalized negative directional-moment magnitude. Must match the positive texture as linear BC7 or RGBAHalf.")]
            [SerializeField] private Texture2D directionalMomentDeltaNegative;
            [Tooltip("One strictly-positive RGBA scale per mip.")]
            [SerializeField] private Vector4[] directionalMomentDeltaPositiveScales =
                Array.Empty<Vector4>();
            [SerializeField] private Vector4[] directionalMomentDeltaNegativeScales =
                Array.Empty<Vector4>();

            public int BucketIndex => bucketIndex;
            public ResponseAtlasEncoding Encoding => encoding;
            public Texture2D OffColor => offColor;
            public Texture2D OnColor => onColor;
            public Texture2D OffDirection => offDirection;
            public Texture2D OnDirection => onDirection;
            public Texture2D ColorDelta => colorDelta;
            public Texture2D DirectionalMomentDelta => directionalMomentDelta;
            public Texture2D ColorDeltaPositive => colorDeltaPositive;
            public Texture2D ColorDeltaNegative => colorDeltaNegative;
            public Vector4[] ColorDeltaPositiveScales =>
                colorDeltaPositiveScales ?? Array.Empty<Vector4>();
            public Vector4[] ColorDeltaNegativeScales =>
                colorDeltaNegativeScales ?? Array.Empty<Vector4>();
            public Texture2D DirectionalMomentDeltaPositive =>
                directionalMomentDeltaPositive;
            public Texture2D DirectionalMomentDeltaNegative =>
                directionalMomentDeltaNegative;
            public Vector4[] DirectionalMomentDeltaPositiveScales =>
                directionalMomentDeltaPositiveScales ?? Array.Empty<Vector4>();
            public Vector4[] DirectionalMomentDeltaNegativeScales =>
                directionalMomentDeltaNegativeScales ?? Array.Empty<Vector4>();

            public void ConfigureOnOffAuthoring(
                int atlasBucketIndex,
                Texture2D offColorAtlas,
                Texture2D onColorAtlas,
                Texture2D offDirectionAtlas,
                Texture2D onDirectionAtlas)
            {
                bucketIndex = atlasBucketIndex;
                encoding = ResponseAtlasEncoding.OnOffStatePair;
                offColor = offColorAtlas;
                onColor = onColorAtlas;
                offDirection = offDirectionAtlas;
                onDirection = onDirectionAtlas;
                colorDelta = null;
                directionalMomentDelta = null;
                ClearNormalizedPositiveNegativeAuthoring();
            }

            public void ConfigurePrecomputedDeltaAuthoring(
                int atlasBucketIndex,
                Texture2D signedColorDelta,
                Texture2D signedDirectionalMomentDelta)
            {
                bucketIndex = atlasBucketIndex;
                encoding = ResponseAtlasEncoding.PrecomputedDelta;
                offColor = null;
                onColor = null;
                offDirection = null;
                onDirection = null;
                colorDelta = signedColorDelta;
                directionalMomentDelta = signedDirectionalMomentDelta;
                ClearNormalizedPositiveNegativeAuthoring();
            }

            /// <summary>
            /// Configures a compact signed delta as two unsigned normalized magnitudes per
            /// semantic channel. Each scale array is indexed by source mip and reconstructs
            /// signed values as positiveNormalized * positiveScale -
            /// negativeNormalized * negativeScale. Authoring must use
            /// <see cref="NormalizedDeltaZeroChannelScale"/> (or another finite positive
            /// value) for an all-zero channel instead of serializing a zero scale.
            /// </summary>
            public void ConfigureNormalizedPositiveNegativeDeltaAuthoring(
                int atlasBucketIndex,
                Texture2D positiveColorDelta,
                Texture2D negativeColorDelta,
                Vector4[] positiveColorScalesByMip,
                Vector4[] negativeColorScalesByMip,
                Texture2D positiveDirectionalMomentDelta,
                Texture2D negativeDirectionalMomentDelta,
                Vector4[] positiveDirectionalMomentScalesByMip,
                Vector4[] negativeDirectionalMomentScalesByMip)
            {
                bucketIndex = atlasBucketIndex;
                encoding = ResponseAtlasEncoding.NormalizedPositiveNegativeDelta;
                offColor = null;
                onColor = null;
                offDirection = null;
                onDirection = null;
                colorDelta = null;
                directionalMomentDelta = null;
                colorDeltaPositive = positiveColorDelta;
                colorDeltaNegative = negativeColorDelta;
                colorDeltaPositiveScales = CloneArray(positiveColorScalesByMip);
                colorDeltaNegativeScales = CloneArray(negativeColorScalesByMip);
                directionalMomentDeltaPositive = positiveDirectionalMomentDelta;
                directionalMomentDeltaNegative = negativeDirectionalMomentDelta;
                directionalMomentDeltaPositiveScales =
                    CloneArray(positiveDirectionalMomentScalesByMip);
                directionalMomentDeltaNegativeScales =
                    CloneArray(negativeDirectionalMomentScalesByMip);
            }

            private void ClearNormalizedPositiveNegativeAuthoring()
            {
                colorDeltaPositive = null;
                colorDeltaNegative = null;
                colorDeltaPositiveScales = Array.Empty<Vector4>();
                colorDeltaNegativeScales = Array.Empty<Vector4>();
                directionalMomentDeltaPositive = null;
                directionalMomentDeltaNegative = null;
                directionalMomentDeltaPositiveScales = Array.Empty<Vector4>();
                directionalMomentDeltaNegativeScales = Array.Empty<Vector4>();
            }
        }

        [Serializable]
        public sealed class OptionalDoorwayProbeResponse
        {
            [Tooltip("Optional doorway-probe SH in Unity channel-major 27-float order.")]
            [SerializeField] private float[] offCoefficients = Array.Empty<float>();
            [SerializeField] private float[] onCoefficients = Array.Empty<float>();

            public float[] OffCoefficients => offCoefficients ?? Array.Empty<float>();
            public float[] OnCoefficients => onCoefficients ?? Array.Empty<float>();
            public bool Authored => OffCoefficients.Length != 0 || OnCoefficients.Length != 0;

            public void ConfigureAuthoring(float[] offSh27, float[] onSh27)
            {
                offCoefficients = CloneArray(offSh27);
                onCoefficients = CloneArray(onSh27);
            }
        }

        [Serializable]
        public sealed class ReceiverResponseLobe
        {
            [SerializeField] private string basisId;
            [Tooltip("P0 endpoint-native response atlases, indexed by CanonicalAtlases bucket.")]
            [SerializeField] private ResponseAtlas[] atlasResponses = Array.Empty<ResponseAtlas>();
            [Tooltip("P100 endpoint-native response atlases, indexed by Power100SourceAtlases bucket.")]
            [SerializeField] private ResponseAtlas[] power100AtlasResponses =
                Array.Empty<ResponseAtlas>();
            [SerializeField] private OptionalDoorwayProbeResponse doorwayProbeResponse;

            public string BasisId => basisId;
            public ResponseAtlas[] AtlasResponses => atlasResponses ?? Array.Empty<ResponseAtlas>();
            public ResponseAtlas[] Power0AtlasResponses => AtlasResponses;
            public ResponseAtlas[] Power100AtlasResponses =>
                power100AtlasResponses ?? Array.Empty<ResponseAtlas>();
            public OptionalDoorwayProbeResponse DoorwayProbeResponse => doorwayProbeResponse;

            /// <summary>
            /// Legacy DPBB-1 authoring overload. It populates only the P0 set so DPBB-2
            /// validation fails honestly until an endpoint-native P100 set is supplied.
            /// </summary>
            public void ConfigureAuthoring(
                string id,
                ResponseAtlas[] responses,
                OptionalDoorwayProbeResponse probeResponse = null)
            {
                basisId = id ?? string.Empty;
                atlasResponses = CloneArray(responses);
                power100AtlasResponses = Array.Empty<ResponseAtlas>();
                doorwayProbeResponse = probeResponse;
            }

            public void ConfigureEndpointAuthoring(
                string id,
                ResponseAtlas[] power0Responses,
                ResponseAtlas[] power100Responses,
                OptionalDoorwayProbeResponse probeResponse = null)
            {
                basisId = id ?? string.Empty;
                atlasResponses = CloneArray(power0Responses);
                power100AtlasResponses = CloneArray(power100Responses);
                doorwayProbeResponse = probeResponse;
            }

            public bool TryGetAtlas(int bucketIndex, out ResponseAtlas result)
            {
                return TryGetAtlas(ProductionEndpoint.Power0, bucketIndex, out result);
            }

            public bool TryGetAtlas(
                ProductionEndpoint endpoint,
                int bucketIndex,
                out ResponseAtlas result)
            {
                ResponseAtlas[] values = endpoint == ProductionEndpoint.Power100
                    ? Power100AtlasResponses
                    : Power0AtlasResponses;
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] != null && values[i].BucketIndex == bucketIndex)
                    {
                        result = values[i];
                        return true;
                    }
                }

                result = null;
                return false;
            }
        }

        public readonly struct WeightedReceiverResponseLobe
        {
            public WeightedReceiverResponseLobe(ReceiverResponseLobe lobe, float poseWeight)
            {
                Lobe = lobe;
                PoseWeight = poseWeight;
            }

            public ReceiverResponseLobe Lobe { get; }
            public float PoseWeight { get; }
        }

        [Serializable]
        public sealed class ReceiverDoorPoseResponse
        {
            [SerializeField, Range(0f, 1f)] private float openFraction = 1f;
            [SerializeField] private ReceiverResponseLobe[] responseLobes =
                Array.Empty<ReceiverResponseLobe>();

            public float OpenFraction => openFraction;
            public ReceiverResponseLobe[] ResponseLobes =>
                responseLobes ?? Array.Empty<ReceiverResponseLobe>();

            public void ConfigureAuthoring(float physicalOpenFraction, ReceiverResponseLobe[] lobes)
            {
                openFraction = physicalOpenFraction;
                responseLobes = CloneArray(lobes);
            }
        }

        [Serializable]
        public sealed class ReceiverDoorBasis
        {
            [SerializeField] private string receiverDoorId;
            [Tooltip("Legacy full-open response. When poseResponses is empty this is treated " +
                     "as a synthetic D100 pose for backward compatibility.")]
            [SerializeField] private ReceiverResponseLobe[] responseLobes =
                Array.Empty<ReceiverResponseLobe>();
            [SerializeField] private ReceiverDoorPoseResponse[] poseResponses =
                Array.Empty<ReceiverDoorPoseResponse>();

            public string ReceiverDoorId => receiverDoorId;
            public ReceiverResponseLobe[] ResponseLobes =>
                responseLobes ?? Array.Empty<ReceiverResponseLobe>();
            public ReceiverDoorPoseResponse[] PoseResponses =>
                poseResponses ?? Array.Empty<ReceiverDoorPoseResponse>();
            public bool UsesPoseResponses => PoseResponses.Length != 0;
            public ReceiverResponseLobe[] ContractLobes => UsesPoseResponses
                ? PoseResponses[0] != null
                    ? PoseResponses[0].ResponseLobes
                    : Array.Empty<ReceiverResponseLobe>()
                : ResponseLobes;

            public void ConfigureAuthoring(string doorId, ReceiverResponseLobe[] lobes)
            {
                receiverDoorId = doorId ?? string.Empty;
                responseLobes = CloneArray(lobes);
                poseResponses = Array.Empty<ReceiverDoorPoseResponse>();
            }

            public void ConfigurePoseAuthoring(
                string doorId,
                ReceiverDoorPoseResponse[] physicalPoseResponses)
            {
                receiverDoorId = doorId ?? string.Empty;
                responseLobes = Array.Empty<ReceiverResponseLobe>();
                poseResponses = CloneArray(physicalPoseResponses);
                Array.Sort(
                    poseResponses,
                    (left, right) =>
                    {
                        if (ReferenceEquals(left, right))
                            return 0;
                        if (left == null)
                            return 1;
                        if (right == null)
                            return -1;
                        return left.OpenFraction.CompareTo(right.OpenFraction);
                    });
            }

            /// <summary>
            /// Appends the two (or one) pose samples needed at a physical door-open fraction.
            /// A legacy lobe array is a synthetic D100 pose. The returned pose weights already
            /// include zero-to-first-pose and bracket interpolation; callers must not multiply
            /// them by aperture again.
            /// </summary>
            public bool TryAppendWeightedResponseLobes(
                float physicalOpenFraction,
                List<WeightedReceiverResponseLobe> output,
                out string failure)
            {
                if (output == null)
                {
                    failure = "Weighted response output list is null.";
                    return false;
                }
                if (!IsFinite(physicalOpenFraction))
                {
                    failure = "Physical door-open fraction is non-finite.";
                    return false;
                }

                float open = Mathf.Clamp01(physicalOpenFraction);
                if (open <= 0f)
                {
                    failure = null;
                    return true;
                }

                ReceiverDoorPoseResponse[] poses = PoseResponses;
                if (poses.Length == 0)
                {
                    return TryAppendPoseLobes(
                        ResponseLobes,
                        open,
                        "legacy D100",
                        output,
                        out failure);
                }

                ReceiverDoorPoseResponse first = poses[0];
                if (first == null || !IsFinite(first.OpenFraction) || first.OpenFraction <= 0f)
                {
                    failure = "Receiver door pose responses are missing or not sorted above D0.";
                    return false;
                }

                if (open < first.OpenFraction)
                {
                    return TryAppendPoseLobes(
                        first.ResponseLobes,
                        open / first.OpenFraction,
                        "first pose",
                        output,
                        out failure);
                }

                for (int i = 0; i < poses.Length; i++)
                {
                    ReceiverDoorPoseResponse lower = poses[i];
                    if (lower == null || !IsFinite(lower.OpenFraction))
                    {
                        failure = $"Receiver door pose {i} is missing or non-finite.";
                        return false;
                    }

                    if (Mathf.Abs(open - lower.OpenFraction) <= 0.000001f ||
                        i == poses.Length - 1)
                    {
                        return TryAppendPoseLobes(
                            lower.ResponseLobes,
                            1f,
                            $"pose {i}",
                            output,
                            out failure);
                    }

                    ReceiverDoorPoseResponse upper = poses[i + 1];
                    if (upper == null || !IsFinite(upper.OpenFraction) ||
                        upper.OpenFraction <= lower.OpenFraction)
                    {
                        failure = $"Receiver door pose interval {i}/{i + 1} is invalid.";
                        return false;
                    }
                    if (open > upper.OpenFraction)
                        continue;

                    float t = Mathf.InverseLerp(lower.OpenFraction, upper.OpenFraction, open);
                    if (!TryAppendPoseLobes(
                            lower.ResponseLobes,
                            1f - t,
                            $"lower pose {i}",
                            output,
                            out failure))
                    {
                        return false;
                    }
                    return TryAppendPoseLobes(
                        upper.ResponseLobes,
                        t,
                        $"upper pose {i + 1}",
                        output,
                        out failure);
                }

                failure = "Physical door-open fraction could not be bracketed.";
                return false;
            }

            private static bool TryAppendPoseLobes(
                ReceiverResponseLobe[] lobes,
                float weight,
                string label,
                List<WeightedReceiverResponseLobe> output,
                out string failure)
            {
                if (!IsFinite(weight) || weight < 0f)
                {
                    failure = $"Receiver {label} has an invalid interpolation weight.";
                    return false;
                }
                if (weight <= 0f)
                {
                    failure = null;
                    return true;
                }

                lobes = lobes ?? Array.Empty<ReceiverResponseLobe>();
                if (lobes.Length == 0)
                {
                    failure = $"Receiver {label} has no response lobes.";
                    return false;
                }
                for (int i = 0; i < lobes.Length; i++)
                {
                    if (lobes[i] == null)
                    {
                        failure = $"Receiver {label} lobe {i} is missing.";
                        return false;
                    }
                    output.Add(new WeightedReceiverResponseLobe(lobes[i], weight));
                }

                failure = null;
                return true;
            }
        }

        [Serializable]
        public sealed class SourceBasisCoefficient
        {
            [SerializeField] private string basisId;
            [SerializeField, ColorUsage(false, true)] private Color power0Rgb = Color.black;
            [SerializeField, ColorUsage(false, true)] private Color power100Rgb = Color.white;

            public string BasisId => basisId;
            public Color Power0Rgb => power0Rgb;
            public Color Power100Rgb => power100Rgb;

            public void ConfigureAuthoring(string id, Color p0Rgb, Color p100Rgb)
            {
                basisId = id ?? string.Empty;
                power0Rgb = p0Rgb;
                power100Rgb = p100Rgb;
            }

            public Color Evaluate(float sourcePower01)
            {
                return Color.LerpUnclamped(power0Rgb, power100Rgb, Mathf.Clamp01(sourcePower01));
            }
        }

        [Serializable]
        public sealed class SourceDoorBasis
        {
            [SerializeField] private string sourceDoorId;
            [SerializeField] private SourceBasisCoefficient[] coefficients =
                Array.Empty<SourceBasisCoefficient>();

            public string SourceDoorId => sourceDoorId;
            public SourceBasisCoefficient[] Coefficients =>
                coefficients ?? Array.Empty<SourceBasisCoefficient>();

            public void ConfigureAuthoring(string doorId, SourceBasisCoefficient[] basisCoefficients)
            {
                sourceDoorId = doorId ?? string.Empty;
                coefficients = CloneArray(basisCoefficients);
            }

            public bool TryGetCoefficient(string basisId, out SourceBasisCoefficient result)
            {
                SourceBasisCoefficient[] values = Coefficients;
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] != null &&
                        string.Equals(values[i].BasisId, basisId, StringComparison.Ordinal))
                    {
                        result = values[i];
                        return true;
                    }
                }

                result = null;
                return false;
            }
        }

        [Serializable]
        public sealed class OptionalAmbientProbeStates
        {
            [SerializeField] private bool authored;
            [Tooltip("27 floats in Unity SphericalHarmonicsL2 channel-major coefficient order.")]
            [SerializeField] private float[] power0Coefficients = Array.Empty<float>();
            [SerializeField] private float[] power100Coefficients = Array.Empty<float>();

            public bool Authored => authored;
            public float[] Power0Coefficients => power0Coefficients ?? Array.Empty<float>();
            public float[] Power100Coefficients => power100Coefficients ?? Array.Empty<float>();

            public void ConfigureAuthoring(bool hasAuthoredValues, float[] p0, float[] p100)
            {
                authored = hasAuthoredValues;
                power0Coefficients = CloneArray(p0);
                power100Coefficients = CloneArray(p100);
            }
        }

        [Serializable]
        public sealed class OptionalReflectionStates
        {
            [SerializeField] private string reflectionId;
            [SerializeField] private Cubemap power0Cubemap;
            [SerializeField] private Cubemap power100Cubemap;
            [SerializeField] private Bounds localInfluenceBounds = new Bounds(Vector3.zero, Vector3.one);

            public string ReflectionId => reflectionId;
            public Cubemap Power0Cubemap => power0Cubemap;
            public Cubemap Power100Cubemap => power100Cubemap;
            public Bounds LocalInfluenceBounds => localInfluenceBounds;

            public void ConfigureAuthoring(
                string id,
                Cubemap p0,
                Cubemap p100,
                Bounds influenceBounds)
            {
                reflectionId = id ?? string.Empty;
                power0Cubemap = p0;
                power100Cubemap = p100;
                localInfluenceBounds = influenceBounds;
            }
        }

        [SerializeField] private string schema = CurrentSchema;
        [SerializeField] private string roomId;
        [SerializeField] private CanonicalAtlasBucket[] canonicalAtlases =
            Array.Empty<CanonicalAtlasBucket>();
        [SerializeField] private CanonicalRendererEntry[] canonicalRenderers =
            Array.Empty<CanonicalRendererEntry>();
        [SerializeField] private Power100SourceAtlas[] power100SourceAtlases =
            Array.Empty<Power100SourceAtlas>();
        [SerializeField] private ReceiverDoorBasis[] receiverDoors =
            Array.Empty<ReceiverDoorBasis>();
        [SerializeField] private SourceDoorBasis[] sourceDoors = Array.Empty<SourceDoorBasis>();
        [SerializeField] private OptionalAmbientProbeStates ambientProbeStates;
        [SerializeField] private OptionalReflectionStates[] reflectionStates =
            Array.Empty<OptionalReflectionStates>();

        public string Schema => schema;
        public string RoomId => roomId;
        public CanonicalAtlasBucket[] CanonicalAtlases =>
            canonicalAtlases ?? Array.Empty<CanonicalAtlasBucket>();
        public CanonicalRendererEntry[] CanonicalRenderers =>
            canonicalRenderers ?? Array.Empty<CanonicalRendererEntry>();
        public Power100SourceAtlas[] Power100SourceAtlases =>
            power100SourceAtlases ?? Array.Empty<Power100SourceAtlas>();
        public ReceiverDoorBasis[] ReceiverDoors => receiverDoors ?? Array.Empty<ReceiverDoorBasis>();
        public SourceDoorBasis[] SourceDoors => sourceDoors ?? Array.Empty<SourceDoorBasis>();
        public OptionalAmbientProbeStates AmbientProbeStates => ambientProbeStates;
        public OptionalReflectionStates[] ReflectionStates =>
            reflectionStates ?? Array.Empty<OptionalReflectionStates>();

        public void ConfigureAuthoring(
            string canonicalRoomId,
            CanonicalAtlasBucket[] atlases,
            CanonicalRendererEntry[] rendererEntries,
            Power100SourceAtlas[] originalPower100Atlases,
            ReceiverDoorBasis[] receiverDoorBases,
            SourceDoorBasis[] sourceDoorBases,
            OptionalAmbientProbeStates ambientStates = null,
            OptionalReflectionStates[] reflections = null)
        {
            schema = CurrentSchema;
            roomId = canonicalRoomId ?? string.Empty;
            canonicalAtlases = CloneArray(atlases);
            canonicalRenderers = CloneArray(rendererEntries);
            power100SourceAtlases = CloneArray(originalPower100Atlases);
            receiverDoors = CloneArray(receiverDoorBases);
            sourceDoors = CloneArray(sourceDoorBases);
            ambientProbeStates = ambientStates;
            reflectionStates = CloneArray(reflections);
        }

        public bool TryGetReceiverDoor(string doorId, out ReceiverDoorBasis result)
        {
            ReceiverDoorBasis[] values = ReceiverDoors;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != null &&
                    string.Equals(values[i].ReceiverDoorId, doorId, StringComparison.Ordinal))
                {
                    result = values[i];
                    return true;
                }
            }

            result = null;
            return false;
        }

        public bool TryGetSourceDoor(string doorId, out SourceDoorBasis result)
        {
            SourceDoorBasis[] values = SourceDoors;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != null &&
                    string.Equals(values[i].SourceDoorId, doorId, StringComparison.Ordinal))
                {
                    result = values[i];
                    return true;
                }
            }

            result = null;
            return false;
        }

        public bool TryGetPower100SourceAtlas(
            int sourceLocalLightmapIndex,
            out Power100SourceAtlas result)
        {
            Power100SourceAtlas[] values = Power100SourceAtlases;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != null &&
                    values[i].SourceLocalLightmapIndex == sourceLocalLightmapIndex)
                {
                    result = values[i];
                    return true;
                }
            }

            result = null;
            return false;
        }

        public int GetNativeEndpointAtlasCount(ProductionEndpoint endpoint)
        {
            return endpoint == ProductionEndpoint.Power100
                ? Power100SourceAtlases.Length
                : CanonicalAtlases.Length;
        }

        public bool TryGetNativeEndpointAtlas(
            ProductionEndpoint endpoint,
            int endpointBucketIndex,
            out NativeEndpointAtlas result)
        {
            if (endpoint == ProductionEndpoint.Power100)
            {
                Power100SourceAtlas[] sources = Power100SourceAtlases;
                if (endpointBucketIndex < 0 || endpointBucketIndex >= sources.Length ||
                    sources[endpointBucketIndex] == null)
                {
                    result = default;
                    return false;
                }

                Power100SourceAtlas source = sources[endpointBucketIndex];
                result = new NativeEndpointAtlas(
                    endpoint,
                    endpointBucketIndex,
                    source.SourceLocalLightmapIndex,
                    $"P100_LM{source.SourceLocalLightmapIndex}",
                    source.Color,
                    source.Direction,
                    source.ShadowMask);
                return true;
            }

            CanonicalAtlasBucket[] buckets = CanonicalAtlases;
            if (endpointBucketIndex < 0 || endpointBucketIndex >= buckets.Length ||
                buckets[endpointBucketIndex] == null)
            {
                result = default;
                return false;
            }

            CanonicalAtlasBucket bucket = buckets[endpointBucketIndex];
            result = new NativeEndpointAtlas(
                ProductionEndpoint.Power0,
                endpointBucketIndex,
                bucket.CanonicalLocalLightmapIndex,
                bucket.BucketId,
                bucket.Power0Color,
                bucket.Power0Direction,
                bucket.UnchangedShadowMask);
            return true;
        }

        public bool TryGetNativeEndpointBucketIndex(
            ProductionEndpoint endpoint,
            int nativeLocalLightmapIndex,
            out int endpointBucketIndex)
        {
            int count = GetNativeEndpointAtlasCount(endpoint);
            for (int i = 0; i < count; i++)
            {
                if (TryGetNativeEndpointAtlas(endpoint, i, out NativeEndpointAtlas atlas) &&
                    atlas.NativeLocalLightmapIndex == nativeLocalLightmapIndex)
                {
                    endpointBucketIndex = i;
                    return true;
                }
            }

            endpointBucketIndex = -1;
            return false;
        }

        public bool TryGetBaseTransitionAtlas(
            ProductionEndpoint layoutEndpoint,
            int endpointBucketIndex,
            out BaseTransitionAtlas result,
            out string failure)
        {
            if (layoutEndpoint == ProductionEndpoint.Power100)
            {
                Power100SourceAtlas[] sources = Power100SourceAtlases;
                if (endpointBucketIndex < 0 || endpointBucketIndex >= sources.Length ||
                    sources[endpointBucketIndex] == null)
                {
                    result = default;
                    failure = $"P100 transition-layout bucket {endpointBucketIndex} is missing.";
                    return false;
                }

                Power100SourceAtlas source = sources[endpointBucketIndex];
                if (source.Power0ColorInPower100Layout == null ||
                    source.Power0DirectionInPower100Layout == null ||
                    source.Color == null || source.Direction == null)
                {
                    result = default;
                    failure = $"P100 transition-layout bucket {endpointBucketIndex} lacks its " +
                              "repacked P0 or exact P100 base pair.";
                    return false;
                }

                result = new BaseTransitionAtlas(
                    layoutEndpoint,
                    endpointBucketIndex,
                    source.SourceLocalLightmapIndex,
                    $"P100_LM{source.SourceLocalLightmapIndex}",
                    source.Power0ColorInPower100Layout,
                    source.Color,
                    source.Power0DirectionInPower100Layout,
                    source.Direction,
                    source.ShadowMask);
                failure = null;
                return true;
            }

            CanonicalAtlasBucket[] buckets = CanonicalAtlases;
            if (endpointBucketIndex < 0 || endpointBucketIndex >= buckets.Length ||
                buckets[endpointBucketIndex] == null)
            {
                result = default;
                failure = $"P0 transition-layout bucket {endpointBucketIndex} is missing.";
                return false;
            }

            CanonicalAtlasBucket bucket = buckets[endpointBucketIndex];
            if (bucket.Power0Color == null || bucket.Power0Direction == null ||
                bucket.Power100Color == null || bucket.Power100Direction == null)
            {
                result = default;
                failure = $"P0 transition-layout bucket '{bucket.BucketId}' lacks its exact P0 " +
                          "or repacked P100 base pair.";
                return false;
            }

            result = new BaseTransitionAtlas(
                ProductionEndpoint.Power0,
                endpointBucketIndex,
                bucket.CanonicalLocalLightmapIndex,
                bucket.BucketId,
                bucket.Power0Color,
                bucket.Power100Color,
                bucket.Power0Direction,
                bucket.Power100Direction,
                bucket.UnchangedShadowMask);
            failure = null;
            return true;
        }

        /// <summary>
        /// Validates the two-layout continuous base-power contract.  This is deliberately a
        /// separate runtime-readiness gate from the general data definition so older isolated
        /// payloads remain inspectable but cannot activate the compositor accidentally.
        /// </summary>
        public bool TryValidateBaseTransitionLayouts(out string failure)
        {
            if (!TryValidateDefinition(out failure))
                return false;

            CanonicalAtlasBucket[] p0Buckets = CanonicalAtlases;
            Power100SourceAtlas[] p100Buckets = Power100SourceAtlases;
            if (p0Buckets.Length != p100Buckets.Length)
            {
                failure = "Continuous base transition currently requires equal P0/P100 atlas " +
                          $"cardinality; P0={p0Buckets.Length}, P100={p100Buckets.Length}. " +
                          "Unequal layouts require an explicit endpoint-bucket mapping.";
                return false;
            }

            for (int i = 0; i < p0Buckets.Length; i++)
            {
                if (!TryGetBaseTransitionAtlas(
                        ProductionEndpoint.Power0,
                        i,
                        out BaseTransitionAtlas p0Layout,
                        out failure) ||
                    !TryValidateTransitionTexture(
                        p0Layout.Power100Color,
                        p0Layout.Power0Color,
                        $"P0-layout bucket {i} repacked P100 color",
                        false,
                        out failure) ||
                    !TryValidateTransitionTexture(
                        p0Layout.Power100Direction,
                        p0Layout.Power0Direction,
                        $"P0-layout bucket {i} repacked P100 direction",
                        true,
                        out failure) ||
                    !TryGetBaseTransitionAtlas(
                        ProductionEndpoint.Power100,
                        i,
                        out BaseTransitionAtlas p100Layout,
                        out failure) ||
                    !TryValidateTransitionTexture(
                        p100Layout.Power0Color,
                        p100Layout.Power100Color,
                        $"P100-layout bucket {i} repacked P0 color",
                        false,
                        out failure) ||
                    !TryValidateTransitionTexture(
                        p100Layout.Power0Direction,
                        p100Layout.Power100Direction,
                        $"P100-layout bucket {i} repacked P0 direction",
                        true,
                        out failure))
                {
                    return false;
                }
            }

            CanonicalRendererEntry[] entries = CanonicalRenderers;
            for (int i = 0; i < entries.Length; i++)
            {
                CanonicalRendererEntry entry = entries[i];
                if (entry == null || entry.BucketIndex < 0 || entry.BucketIndex >= p0Buckets.Length ||
                    !TryGetPower100SourceAtlas(
                        entry.Power100SourceLocalLightmapIndex,
                        out Power100SourceAtlas p100Source))
                {
                    failure = $"Base-transition renderer mapping {i} is incomplete.";
                    return false;
                }

                Texture2D p0Shadow = p0Buckets[entry.BucketIndex].UnchangedShadowMask;
                Texture2D p100Shadow = p100Source.ShadowMask;
                if (p0Shadow != p100Shadow && (p0Shadow != null || p100Shadow != null))
                {
                    failure = $"Renderer '{entry.CanonicalRendererKey}' has a non-null " +
                              "P0/P100 shadow-mask mismatch. A LightmapData slot cannot blend " +
                              "or repack that shadow mask while retaining the captured layout.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        public bool TryValidateDefinition(out string failure)
        {
            if (!string.Equals(schema, CurrentSchema, StringComparison.Ordinal))
            {
                failure = $"Unsupported room-basis schema '{schema}'. Expected '{CurrentSchema}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(roomId))
            {
                failure = "Room id is empty.";
                return false;
            }

            CanonicalAtlasBucket[] buckets = CanonicalAtlases;
            if (buckets.Length == 0)
            {
                failure = "The room has no canonical lightmap atlas buckets.";
                return false;
            }

            var bucketIds = new HashSet<string>(StringComparer.Ordinal);
            var localIndices = new HashSet<int>();
            for (int i = 0; i < buckets.Length; i++)
            {
                CanonicalAtlasBucket bucket = buckets[i];
                if (bucket == null || string.IsNullOrWhiteSpace(bucket.BucketId) ||
                    !bucketIds.Add(bucket.BucketId))
                {
                    failure = $"Canonical atlas bucket {i} is missing or has a duplicate/empty id.";
                    return false;
                }

                if (bucket.CanonicalLocalLightmapIndex < 0 ||
                    !localIndices.Add(bucket.CanonicalLocalLightmapIndex))
                {
                    failure = $"Canonical atlas bucket '{bucket.BucketId}' has an invalid or " +
                              "duplicate local lightmap index.";
                    return false;
                }

                if (!TryValidateSampleAtlasTexture(
                        bucket.Power0Color,
                        bucket,
                        bucket.Power0Color,
                        "P0 color",
                        false,
                        out failure) ||
                    !TryValidateSampleAtlasTexture(
                        bucket.Power0Direction,
                        bucket,
                        bucket.Power0Direction,
                        "P0 direction",
                        true,
                        out failure))
                {
                    return false;
                }

                bool hasP0LayoutPower100 = bucket.Power100Color != null ||
                                           bucket.Power100Direction != null;
                if (hasP0LayoutPower100 &&
                    (bucket.Power100Color == null || bucket.Power100Direction == null))
                {
                    failure = $"Canonical bucket '{bucket.BucketId}' has an incomplete P100 " +
                              "base pair in its P0 layout.";
                    return false;
                }
                if (hasP0LayoutPower100 &&
                    (!TryValidateSampleAtlasTexture(
                         bucket.Power100Color,
                         bucket,
                         bucket.Power0Color,
                         "P0-layout repacked P100 color",
                         false,
                         out failure) ||
                     !TryValidateSampleAtlasTexture(
                         bucket.Power100Direction,
                         bucket,
                         bucket.Power0Direction,
                         "P0-layout repacked P100 direction",
                         true,
                         out failure)))
                {
                    return false;
                }
            }

            CanonicalRendererEntry[] rendererEntries = CanonicalRenderers;
            if (rendererEntries.Length == 0)
            {
                failure = "The room has no canonical renderer entries.";
                return false;
            }

            if (!TryValidatePower100SourceAtlases(out failure))
                return false;

            var rendererPaths = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < rendererEntries.Length; i++)
            {
                CanonicalRendererEntry entry = rendererEntries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.CanonicalRendererKey) ||
                    !rendererPaths.Add(entry.CanonicalRendererKey))
                {
                    failure = $"Canonical renderer entry {i} is missing or has a duplicate/empty path.";
                    return false;
                }

                if (entry.BucketIndex < 0 || entry.BucketIndex >= buckets.Length ||
                    entry.CanonicalLocalLightmapIndex !=
                    buckets[entry.BucketIndex].CanonicalLocalLightmapIndex ||
                    !IsFinite(entry.LightmapScaleOffset) ||
                    entry.Power100SourceLocalLightmapIndex < 0 ||
                    !IsFinite(entry.Power100SourceScaleOffset) ||
                    !TryGetPower100SourceAtlas(
                        entry.Power100SourceLocalLightmapIndex,
                        out _))
                {
                    failure = $"Canonical renderer '{entry.CanonicalRendererKey}' has an invalid " +
                              "bucket, local lightmap index, or scale/offset.";
                    return false;
                }
            }

            if (!TryValidateReceiverDoors(buckets, Power100SourceAtlases, out failure) ||
                !TryValidateSourceDoors(out failure) ||
                !TryValidateOptionalEnvironment(out failure))
            {
                return false;
            }

            failure = null;
            return true;
        }

        private bool TryValidatePower100SourceAtlases(out string failure)
        {
            Power100SourceAtlas[] atlases = Power100SourceAtlases;
            if (atlases.Length == 0)
            {
                failure = "The room has no original P100 source atlas references.";
                return false;
            }

            var sourceIndices = new HashSet<int>();
            for (int i = 0; i < atlases.Length; i++)
            {
                Power100SourceAtlas atlas = atlases[i];
                if (atlas == null || atlas.SourceLocalLightmapIndex < 0 ||
                    !sourceIndices.Add(atlas.SourceLocalLightmapIndex) || atlas.Color == null ||
                    atlas.Direction == null || atlas.Color.width != atlas.Direction.width ||
                    atlas.Color.height != atlas.Direction.height ||
                    atlas.Color.mipmapCount != atlas.Direction.mipmapCount)
                {
                    failure = $"Original P100 source atlas {i} is missing, duplicated, or has a " +
                              "mismatched color/direction layout.";
                    return false;
                }

                if (!TryValidateStandaloneSampleTexture(
                        atlas.Color,
                        atlas.Color.width,
                        atlas.Color.height,
                        atlas.Color.mipmapCount,
                        $"P100 source atlas {atlas.SourceLocalLightmapIndex} color",
                        false,
                        out failure) ||
                    !TryValidateStandaloneSampleTexture(
                        atlas.Direction,
                        atlas.Color.width,
                        atlas.Color.height,
                        atlas.Color.mipmapCount,
                        $"P100 source atlas {atlas.SourceLocalLightmapIndex} direction",
                        true,
                        out failure))
                {
                    return false;
                }

                bool hasRepackedPower0 = atlas.Power0ColorInPower100Layout != null ||
                                         atlas.Power0DirectionInPower100Layout != null;
                if (hasRepackedPower0 &&
                    (atlas.Power0ColorInPower100Layout == null ||
                     atlas.Power0DirectionInPower100Layout == null))
                {
                    failure = $"P100 source atlas {atlas.SourceLocalLightmapIndex} has an " +
                              "incomplete repacked P0 transition pair.";
                    return false;
                }
                if (hasRepackedPower0 &&
                    (!TryValidateTransitionTexture(
                         atlas.Power0ColorInPower100Layout,
                         atlas.Color,
                         $"P100 source atlas {atlas.SourceLocalLightmapIndex} repacked P0 color",
                         false,
                         out failure) ||
                     !TryValidateTransitionTexture(
                         atlas.Power0DirectionInPower100Layout,
                         atlas.Direction,
                         $"P100 source atlas {atlas.SourceLocalLightmapIndex} repacked P0 direction",
                         true,
                         out failure)))
                {
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private bool TryValidateReceiverDoors(
            CanonicalAtlasBucket[] buckets,
            Power100SourceAtlas[] power100Sources,
            out string failure)
        {
            var doorIds = new HashSet<string>(StringComparer.Ordinal);
            ReceiverDoorBasis[] doors = ReceiverDoors;
            for (int doorIndex = 0; doorIndex < doors.Length; doorIndex++)
            {
                ReceiverDoorBasis door = doors[doorIndex];
                if (door == null || string.IsNullOrWhiteSpace(door.ReceiverDoorId) ||
                    !doorIds.Add(door.ReceiverDoorId))
                {
                    failure = $"Receiver door {doorIndex} is missing or has a duplicate/empty id.";
                    return false;
                }

                ReceiverResponseLobe[] legacyLobes = door.ResponseLobes;
                ReceiverDoorPoseResponse[] poses = door.PoseResponses;
                if (poses.Length == 0)
                {
                    if (legacyLobes.Length == 0)
                    {
                        failure = $"Receiver door '{door.ReceiverDoorId}' has no legacy D100 " +
                                  "response lobes or pose responses.";
                        return false;
                    }
                    if (!TryValidateReceiverLobes(
                            door.ReceiverDoorId,
                            "legacy D100",
                            legacyLobes,
                            buckets,
                            power100Sources,
                            out _,
                            out failure))
                    {
                        return false;
                    }
                    continue;
                }

                if (legacyLobes.Length != 0)
                {
                    failure = $"Receiver door '{door.ReceiverDoorId}' mixes legacy D100 lobes " +
                              "with pose responses.";
                    return false;
                }
                if (poses.Length != RequiredReceiverDoorPoseFractions.Length)
                {
                    failure = $"Receiver door '{door.ReceiverDoorId}' has {poses.Length} poses; " +
                              "expected sorted D25/D50/D75/D100 responses.";
                    return false;
                }

                HashSet<string> contractBasisIds = null;
                float previousFraction = 0f;
                for (int poseIndex = 0; poseIndex < poses.Length; poseIndex++)
                {
                    ReceiverDoorPoseResponse pose = poses[poseIndex];
                    float expectedFraction = RequiredReceiverDoorPoseFractions[poseIndex];
                    if (pose == null || !IsFinite(pose.OpenFraction) ||
                        pose.OpenFraction <= previousFraction ||
                        Mathf.Abs(pose.OpenFraction - expectedFraction) >
                        RequiredPoseFractionTolerance)
                    {
                        failure = $"Receiver door '{door.ReceiverDoorId}' pose {poseIndex} must " +
                                  $"be sorted D{Mathf.RoundToInt(expectedFraction * 100f):000}; " +
                                  $"actual={(pose != null ? pose.OpenFraction.ToString("R") : "<null>")}.";
                        return false;
                    }

                    if (!TryValidateReceiverLobes(
                            door.ReceiverDoorId,
                            $"D{Mathf.RoundToInt(expectedFraction * 100f):000}",
                            pose.ResponseLobes,
                            buckets,
                            power100Sources,
                            out HashSet<string> poseBasisIds,
                            out failure))
                    {
                        return false;
                    }

                    if (contractBasisIds == null)
                        contractBasisIds = poseBasisIds;
                    else if (!contractBasisIds.SetEquals(poseBasisIds))
                    {
                        failure = $"Receiver door '{door.ReceiverDoorId}' pose {poseIndex} does " +
                                  "not preserve the same basis-id set as D025.";
                        return false;
                    }
                    previousFraction = pose.OpenFraction;
                }
            }

            failure = null;
            return true;
        }

        private static bool TryValidateReceiverLobes(
            string doorId,
            string poseLabel,
            ReceiverResponseLobe[] lobes,
            CanonicalAtlasBucket[] buckets,
            Power100SourceAtlas[] power100Sources,
            out HashSet<string> basisIds,
            out string failure)
        {
            basisIds = new HashSet<string>(StringComparer.Ordinal);
            lobes = lobes ?? Array.Empty<ReceiverResponseLobe>();
            if (lobes.Length == 0)
            {
                failure = $"Receiver door '{doorId}' {poseLabel} has no response lobes.";
                return false;
            }

            for (int lobeIndex = 0; lobeIndex < lobes.Length; lobeIndex++)
            {
                ReceiverResponseLobe lobe = lobes[lobeIndex];
                if (lobe == null || string.IsNullOrWhiteSpace(lobe.BasisId) ||
                    !basisIds.Add(lobe.BasisId))
                {
                    failure = $"Receiver door '{doorId}' {poseLabel} lobe {lobeIndex} is " +
                              "missing or has a duplicate/empty basis id.";
                    return false;
                }

                if (!TryValidateDoorwayProbeResponse(
                        doorId,
                        poseLabel,
                        lobe,
                        out failure))
                {
                    return false;
                }

                if (!TryValidateEndpointResponseSet(
                        doorId,
                        poseLabel,
                        lobe,
                        ProductionEndpoint.Power0,
                        lobe.Power0AtlasResponses,
                        buckets,
                        power100Sources,
                        out failure) ||
                    !TryValidateEndpointResponseSet(
                        doorId,
                        poseLabel,
                        lobe,
                        ProductionEndpoint.Power100,
                        lobe.Power100AtlasResponses,
                        buckets,
                        power100Sources,
                        out failure))
                {
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private static bool TryValidateEndpointResponseSet(
            string doorId,
            string poseLabel,
            ReceiverResponseLobe lobe,
            ProductionEndpoint endpoint,
            ResponseAtlas[] responses,
            CanonicalAtlasBucket[] power0Buckets,
            Power100SourceAtlas[] power100Sources,
            out string failure)
        {
            responses = responses ?? Array.Empty<ResponseAtlas>();
            int expectedCount = endpoint == ProductionEndpoint.Power100
                ? power100Sources.Length
                : power0Buckets.Length;
            string endpointLabel = endpoint == ProductionEndpoint.Power100 ? "P100" : "P0";
            if (responses.Length != expectedCount)
            {
                failure = $"Receiver door '{doorId}' {poseLabel} basis '{lobe.BasisId}' " +
                          $"has {responses.Length} {endpointLabel} response atlases; expected " +
                          $"{expectedCount} endpoint-native buckets.";
                return false;
            }

            var responseBuckets = new HashSet<int>();
            for (int responseIndex = 0; responseIndex < responses.Length; responseIndex++)
            {
                ResponseAtlas response = responses[responseIndex];
                if (response == null || response.BucketIndex < 0 ||
                    response.BucketIndex >= expectedCount ||
                    !responseBuckets.Add(response.BucketIndex))
                {
                    failure = $"Receiver door '{doorId}' {poseLabel} basis '{lobe.BasisId}' " +
                              $"has an invalid or duplicate {endpointLabel} response bucket at " +
                              $"{responseIndex}.";
                    return false;
                }

                CanonicalAtlasBucket validationBucket;
                if (endpoint == ProductionEndpoint.Power100)
                {
                    Power100SourceAtlas source = power100Sources[response.BucketIndex];
                    if (source == null)
                    {
                        failure = $"{endpointLabel} native response bucket " +
                                  $"{response.BucketIndex} has no source atlas.";
                        return false;
                    }
                    validationBucket = new CanonicalAtlasBucket();
                    validationBucket.ConfigureAuthoring(
                        $"P100 native LM{source.SourceLocalLightmapIndex}",
                        source.SourceLocalLightmapIndex,
                        source.Color,
                        source.Color,
                        source.Direction,
                        source.Direction,
                        source.ShadowMask);
                }
                else
                {
                    validationBucket = power0Buckets[response.BucketIndex];
                }

                if (!TryValidateResponse(response, validationBucket, out failure))
                {
                    failure = $"Receiver door '{doorId}' {poseLabel} basis '{lobe.BasisId}' " +
                              $"{endpointLabel} response bucket {response.BucketIndex}: {failure}";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private static bool TryValidateDoorwayProbeResponse(
            string doorId,
            string poseLabel,
            ReceiverResponseLobe lobe,
            out string failure)
        {
            OptionalDoorwayProbeResponse response = lobe.DoorwayProbeResponse;
            if (response == null)
            {
                failure = null;
                return true;
            }

            float[] off = response.OffCoefficients;
            float[] on = response.OnCoefficients;
            bool bothEmpty = off.Length == 0 && on.Length == 0;
            bool bothComplete = off.Length == SphericalHarmonicsCoefficientCount &&
                                on.Length == SphericalHarmonicsCoefficientCount;
            if (!bothEmpty && !bothComplete)
            {
                failure = $"Receiver door '{doorId}' {poseLabel} basis '{lobe.BasisId}' doorway-probe " +
                          "response must have either 0/0 or exactly 27/27 OFF/ON coefficients.";
                return false;
            }

            for (int i = 0; i < off.Length; i++)
            {
                if (!IsFinite(off[i]) || !IsFinite(on[i]))
                {
                    failure = $"Receiver door '{doorId}' {poseLabel} basis '{lobe.BasisId}' doorway-probe " +
                              $"response contains a non-finite coefficient at {i}.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private bool TryValidateSourceDoors(out string failure)
        {
            var doorIds = new HashSet<string>(StringComparer.Ordinal);
            SourceDoorBasis[] doors = SourceDoors;
            for (int doorIndex = 0; doorIndex < doors.Length; doorIndex++)
            {
                SourceDoorBasis door = doors[doorIndex];
                if (door == null || string.IsNullOrWhiteSpace(door.SourceDoorId) ||
                    !doorIds.Add(door.SourceDoorId))
                {
                    failure = $"Source door {doorIndex} is missing or has a duplicate/empty id.";
                    return false;
                }

                SourceBasisCoefficient[] coefficients = door.Coefficients;
                if (coefficients.Length == 0)
                {
                    failure = $"Source door '{door.SourceDoorId}' has no basis coefficients.";
                    return false;
                }

                var basisIds = new HashSet<string>(StringComparer.Ordinal);
                for (int coefficientIndex = 0; coefficientIndex < coefficients.Length; coefficientIndex++)
                {
                    SourceBasisCoefficient coefficient = coefficients[coefficientIndex];
                    if (coefficient == null || string.IsNullOrWhiteSpace(coefficient.BasisId) ||
                        !basisIds.Add(coefficient.BasisId) ||
                        !IsFiniteNonNegative(coefficient.Power0Rgb) ||
                        !IsFiniteNonNegative(coefficient.Power100Rgb))
                    {
                        failure = $"Source door '{door.SourceDoorId}' coefficient {coefficientIndex} " +
                                  "has an invalid basis id or non-finite/negative RGB value.";
                        return false;
                    }
                }
            }

            failure = null;
            return true;
        }

        private bool TryValidateOptionalEnvironment(out string failure)
        {
            if (ambientProbeStates != null && ambientProbeStates.Authored &&
                (ambientProbeStates.Power0Coefficients.Length != SphericalHarmonicsCoefficientCount ||
                 ambientProbeStates.Power100Coefficients.Length != SphericalHarmonicsCoefficientCount))
            {
                failure = "Authored ambient-probe placeholders must contain exactly 27 floats " +
                          "for both P0 and P100.";
                return false;
            }

            var reflectionIds = new HashSet<string>(StringComparer.Ordinal);
            OptionalReflectionStates[] reflections = ReflectionStates;
            for (int i = 0; i < reflections.Length; i++)
            {
                OptionalReflectionStates state = reflections[i];
                if (state == null || string.IsNullOrWhiteSpace(state.ReflectionId) ||
                    !reflectionIds.Add(state.ReflectionId) || state.Power0Cubemap == null ||
                    state.Power100Cubemap == null)
                {
                    failure = $"Reflection placeholder {i} is incomplete or has a duplicate id.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private static bool TryValidateResponse(
            ResponseAtlas response,
            CanonicalAtlasBucket bucket,
            out string failure)
        {
            if (response.Encoding == ResponseAtlasEncoding.OnOffStatePair)
            {
                if (!TryValidateSampleAtlasTexture(
                        response.OffColor,
                        bucket,
                        bucket.Power0Color,
                        "response OFF color",
                        false,
                        out failure) ||
                    !TryValidateSampleAtlasTexture(
                        response.OnColor,
                        bucket,
                        bucket.Power0Color,
                        "response ON color",
                        false,
                        out failure) ||
                    !TryValidateSampleAtlasTexture(
                        response.OffDirection,
                        bucket,
                        bucket.Power0Direction,
                        "response OFF direction",
                        true,
                        out failure) ||
                    !TryValidateSampleAtlasTexture(
                        response.OnDirection,
                        bucket,
                        bucket.Power0Direction,
                        "response ON direction",
                        true,
                        out failure))
                {
                    return false;
                }
            }
            else if (response.Encoding == ResponseAtlasEncoding.PrecomputedDelta)
            {
                if (!TryValidateSignedDeltaTexture(
                        response.ColorDelta,
                        bucket,
                        bucket.Power0Color,
                        "response color delta",
                        out failure) ||
                    !TryValidateSignedDeltaTexture(
                        response.DirectionalMomentDelta,
                        bucket,
                        bucket.Power0Direction,
                        "response directional-moment delta",
                        out failure))
                {
                    return false;
                }
            }
            else if (response.Encoding ==
                     ResponseAtlasEncoding.NormalizedPositiveNegativeDelta)
            {
                TextureFormat colorStorageFormat = response.ColorDeltaPositive != null
                    ? response.ColorDeltaPositive.format
                    : TextureFormat.RGBA32;
                if ((colorStorageFormat != TextureFormat.BC6H &&
                     colorStorageFormat != TextureFormat.RGBAHalf) ||
                    response.ColorDeltaNegative == null ||
                    response.ColorDeltaNegative.format != colorStorageFormat)
                {
                    failure = $"Bucket '{bucket.BucketId}' normalized response color deltas " +
                              "must use BC6H or RGBAHalf in the same format for the positive/negative pair.";
                    return false;
                }
                TextureFormat momentStorageFormat = response.DirectionalMomentDeltaPositive != null
                    ? response.DirectionalMomentDeltaPositive.format
                    : TextureFormat.RGBA32;
                if ((momentStorageFormat != TextureFormat.BC7 &&
                     momentStorageFormat != TextureFormat.RGBAHalf) ||
                    response.DirectionalMomentDeltaNegative == null ||
                    response.DirectionalMomentDeltaNegative.format != momentStorageFormat)
                {
                    failure = $"Bucket '{bucket.BucketId}' normalized response directional-moment deltas " +
                              "must use BC7 or RGBAHalf in the same format for the positive/negative pair.";
                    return false;
                }
                if (!TryValidateNormalizedMagnitudeTexture(
                        response.ColorDeltaPositive,
                        bucket,
                        bucket.Power0Color,
                        "normalized positive response color delta",
                        colorStorageFormat,
                        out failure) ||
                    !TryValidateNormalizedMagnitudeTexture(
                        response.ColorDeltaNegative,
                        bucket,
                        bucket.Power0Color,
                        "normalized negative response color delta",
                        colorStorageFormat,
                        out failure) ||
                    !TryValidateNormalizedMagnitudeTexture(
                        response.DirectionalMomentDeltaPositive,
                        bucket,
                        bucket.Power0Direction,
                        "normalized positive response directional-moment delta",
                        momentStorageFormat,
                        out failure) ||
                    !TryValidateNormalizedMagnitudeTexture(
                        response.DirectionalMomentDeltaNegative,
                        bucket,
                        bucket.Power0Direction,
                        "normalized negative response directional-moment delta",
                        momentStorageFormat,
                        out failure))
                {
                    return false;
                }

                int mipCount = response.ColorDeltaPositive.mipmapCount;
                if (!TryValidateNormalizedDeltaScales(
                        response.ColorDeltaPositiveScales,
                        mipCount,
                        "normalized positive response color scales",
                        out failure) ||
                    !TryValidateNormalizedDeltaScales(
                        response.ColorDeltaNegativeScales,
                        mipCount,
                        "normalized negative response color scales",
                        out failure) ||
                    !TryValidateNormalizedDeltaScales(
                        response.DirectionalMomentDeltaPositiveScales,
                        mipCount,
                        "normalized positive response directional-moment scales",
                        out failure) ||
                    !TryValidateNormalizedDeltaScales(
                        response.DirectionalMomentDeltaNegativeScales,
                        mipCount,
                        "normalized negative response directional-moment scales",
                        out failure))
                {
                    return false;
                }
            }
            else
            {
                failure = $"Response bucket {response.BucketIndex} uses unknown encoding " +
                          $"'{response.Encoding}'.";
                return false;
            }

            failure = null;
            return true;
        }

        private static bool TryValidateSampleAtlasTexture(
            Texture2D texture,
            CanonicalAtlasBucket bucket,
            Texture2D samplingPrototype,
            string label,
            bool requiresAlpha,
            out string failure)
        {
            int expectedWidth = bucket.Power0Color != null
                ? bucket.Power0Color.width
                : texture != null ? texture.width : 0;
            int expectedHeight = bucket.Power0Color != null
                ? bucket.Power0Color.height
                : texture != null ? texture.height : 0;
            int expectedMipCount = bucket.Power0Color != null
                ? bucket.Power0Color.mipmapCount
                : texture != null ? texture.mipmapCount : 0;
            if (!TryValidateStandaloneSampleTexture(
                texture,
                expectedWidth,
                expectedHeight,
                expectedMipCount,
                $"Bucket '{bucket.BucketId}' {label}",
                requiresAlpha,
                out failure))
            {
                return false;
            }

            if (samplingPrototype == null)
            {
                failure = $"Bucket '{bucket.BucketId}' {label} has no native endpoint " +
                          "sampling prototype.";
                return false;
            }

            if (!HasMatchingSamplingContract(texture, samplingPrototype))
            {
                failure = $"Bucket '{bucket.BucketId}' {label} does not match its native " +
                          $"endpoint sampling state (filter={samplingPrototype.filterMode}, " +
                          $"wrap={samplingPrototype.wrapModeU}/{samplingPrototype.wrapModeV}/" +
                          $"{samplingPrototype.wrapModeW}, aniso={samplingPrototype.anisoLevel}, " +
                          $"mipBias={samplingPrototype.mipMapBias}).";
                return false;
            }

            failure = null;
            return true;
        }

        private static bool TryValidateStandaloneSampleTexture(
            Texture2D texture,
            int expectedWidth,
            int expectedHeight,
            int expectedMipCount,
            string label,
            bool requiresAlpha,
            out string failure)
        {
            if (texture == null)
            {
                failure = $"{label} is missing.";
                return false;
            }

            if (texture.width != expectedWidth || texture.height != expectedHeight)
            {
                failure = $"{label} is {texture.width}x{texture.height}; " +
                          $"expected {expectedWidth}x{expectedHeight}.";
                return false;
            }

            if (expectedMipCount <= 0 || texture.mipmapCount != expectedMipCount)
            {
                failure = $"{label} has {texture.mipmapCount} mip levels; " +
                          $"expected {expectedMipCount}.";
                return false;
            }

            if (GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat))
            {
                failure = $"{label} is sRGB; lightmap basis " +
                          "textures must be imported as linear data.";
                return false;
            }

            if (!SystemInfo.SupportsTextureFormat(texture.format) ||
                (texture.format != TextureFormat.RGBAHalf &&
                 texture.format != TextureFormat.RGBA32 &&
                 texture.format != TextureFormat.BC6H &&
                 texture.format != TextureFormat.BC7))
            {
                failure = $"{label} uses unsupported sampling " +
                          $"format {texture.format}; expected linear RGBAHalf, RGBA32, BC6H, or BC7.";
                return false;
            }

            if (requiresAlpha && texture.format == TextureFormat.BC6H)
            {
                failure = $"{label} uses BC6H, which cannot retain " +
                          "the directional rebalancing alpha channel. Use linear BC7 or RGBAHalf.";
                return false;
            }

            failure = null;
            return true;
        }

        private static bool TryValidateTransitionTexture(
            Texture2D texture,
            Texture2D nativeLayoutPrototype,
            string label,
            bool requiresAlpha,
            out string failure)
        {
            if (nativeLayoutPrototype == null)
            {
                failure = label + " has no native-layout sampling prototype.";
                return false;
            }
            if (!TryValidateStandaloneSampleTexture(
                    texture,
                    nativeLayoutPrototype.width,
                    nativeLayoutPrototype.height,
                    nativeLayoutPrototype.mipmapCount,
                    label,
                    requiresAlpha,
                    out failure))
            {
                return false;
            }
            if (!HasMatchingSamplingContract(texture, nativeLayoutPrototype))
            {
                failure = label + " does not preserve its captured endpoint sampling state " +
                          $"(filter={nativeLayoutPrototype.filterMode}, " +
                          $"wrap={nativeLayoutPrototype.wrapModeU}/" +
                          $"{nativeLayoutPrototype.wrapModeV}/{nativeLayoutPrototype.wrapModeW}, " +
                          $"aniso={nativeLayoutPrototype.anisoLevel}, " +
                          $"mipBias={nativeLayoutPrototype.mipMapBias}).";
                return false;
            }

            failure = null;
            return true;
        }

        private static bool TryValidateSignedDeltaTexture(
            Texture2D texture,
            CanonicalAtlasBucket bucket,
            Texture2D samplingPrototype,
            string label,
            out string failure)
        {
            if (!TryValidateSampleAtlasTexture(
                    texture,
                    bucket,
                    samplingPrototype,
                    label,
                    true,
                    out failure))
                return false;
            if (texture.format != TextureFormat.RGBAHalf)
            {
                failure = $"Bucket '{bucket.BucketId}' {label} contains signed values and " +
                          $"therefore must be RGBAHalf, not {texture.format}.";
                return false;
            }

            failure = null;
            return true;
        }

        private static bool TryValidateNormalizedMagnitudeTexture(
            Texture2D texture,
            CanonicalAtlasBucket bucket,
            Texture2D samplingPrototype,
            string label,
            TextureFormat requiredFormat,
            out string failure)
        {
            bool requiresAlpha = requiredFormat == TextureFormat.BC7;
            if (!TryValidateSampleAtlasTexture(
                    texture,
                    bucket,
                    samplingPrototype,
                    label,
                    requiresAlpha,
                    out failure))
            {
                return false;
            }

            if (texture.format != requiredFormat)
            {
                failure = $"Bucket '{bucket.BucketId}' {label} uses {texture.format}; " +
                          $"compact normalized deltas require exact linear {requiredFormat}.";
                return false;
            }

            failure = null;
            return true;
        }

        private static bool TryValidateNormalizedDeltaScales(
            Vector4[] scales,
            int expectedMipCount,
            string label,
            out string failure)
        {
            scales = scales ?? Array.Empty<Vector4>();
            if (expectedMipCount <= 0 ||
                expectedMipCount > NormalizedDeltaMaximumMipCount)
            {
                failure = $"{label} targets {expectedMipCount} mip levels; compact " +
                          $"normalized deltas support 1..{NormalizedDeltaMaximumMipCount}.";
                return false;
            }

            if (scales.Length != expectedMipCount)
            {
                failure = $"{label} has {scales.Length} entries; expected exactly one for " +
                          $"each of {expectedMipCount} mip levels.";
                return false;
            }

            for (int mip = 0; mip < scales.Length; mip++)
            {
                Vector4 scale = scales[mip];
                if (!IsFinite(scale) || scale.x <= 0f || scale.y <= 0f ||
                    scale.z <= 0f || scale.w <= 0f)
                {
                    failure = $"{label} mip {mip} is {scale}; every component must be " +
                              "finite and strictly positive. Use " +
                              $"{NormalizedDeltaZeroChannelScale:R} for an all-zero channel.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private static bool HasMatchingSamplingContract(Texture2D value, Texture2D prototype)
        {
            return value.mipmapCount == prototype.mipmapCount &&
                   value.filterMode == prototype.filterMode &&
                   value.wrapModeU == prototype.wrapModeU &&
                   value.wrapModeV == prototype.wrapModeV &&
                   value.wrapModeW == prototype.wrapModeW &&
                   value.anisoLevel == prototype.anisoLevel &&
                   Mathf.Approximately(value.mipMapBias, prototype.mipMapBias);
        }

        private static bool IsFinite(Vector4 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) &&
                   IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFiniteNonNegative(Color value)
        {
            return IsFinite(value.r) && IsFinite(value.g) && IsFinite(value.b) &&
                   value.r >= 0f && value.g >= 0f && value.b >= 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static T[] CloneArray<T>(T[] values)
        {
            if (values == null || values.Length == 0)
                return Array.Empty<T>();
            var clone = new T[values.Length];
            Array.Copy(values, clone, values.Length);
            return clone;
        }
    }
}
