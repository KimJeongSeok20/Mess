using System;

namespace DungeonPortalBakedBasisPoC.Editor
{
    /// <summary>
    /// JSON-only provenance for generated baked-basis payloads.  It deliberately stores
    /// asset-relative paths and dependency hashes rather than references so it remains
    /// inspectable even when a staging folder is transactionally promoted.
    /// </summary>
    [Serializable]
    internal sealed class DungeonPortalBakedBasisGeneratedManifest
    {
        // GEN-5 records explicit mip-chain evidence, UV2-triangle ownership, and the
        // explicit one-texel representation used only for genuinely absent TEXCOORD1.
        // used to avoid treating unused portions of overlapping Unity ST rectangles as
        // physically shared texels.
        public const string CurrentSchema = "DPBB-GEN-5";

        public string schema = CurrentSchema;
        public string toolVersion;
        public string generatedUtcIso8601;
        public string payloadRootName;
        public string chartCopyShaderPath;
        public string chartCopyShaderDependencyHash;
        public bool productionInputsReadOnly = true;
        public bool strictPersistedCaptureIntegrityClaimed = false;
        public string k1SpatialApproximationCaveat;
        public CookieComparisonRecord sourceCookieComparison;
        public InputFingerprint[] inputs = Array.Empty<InputFingerprint>();
        public CaptureIntegrityRecord[] captureIntegrity = Array.Empty<CaptureIntegrityRecord>();
        public RoomRecord[] rooms = Array.Empty<RoomRecord>();
        public ArtifactFingerprint[] artifacts = Array.Empty<ArtifactFingerprint>();
    }

    [Serializable]
    internal sealed class InputFingerprint
    {
        public string assetPath;
        public string dependencyHash;
        public string purpose;
    }

    [Serializable]
    internal sealed class CaptureIntegrityRecord
    {
        public string receiverRoomId;
        public string captureAssetPath;
        public bool structuralValidationPassed;
        public string structuralValidationError;
        public string baselineStateHash;
        public string fullStateHash;
        public bool baselineStateHashRecomputed;
        public bool fullStateHashRecomputed;
        public bool allNonWaivedProductionHashesMatch;
        public bool rotationBakeToolHashOnlyWaiverApplied;
        public string waiverAssetPath;
        public string savedDependencyHash;
        public string currentDependencyHash;
        public string strictValidationStatus;
    }

    [Serializable]
    internal sealed class RoomRecord
    {
        public string roomId;
        public string basisAssetRelativePath;
        public string productionPrefabPath;
        public string p0BakeDataPath;
        public string p100BakeDataPath;
        public string receiverCapturePath;
        public string endpointProfilePath;
        public string doorwayId;
        public string basisId;
        public int canonicalRendererCount;
        public int captureRendererCount;
        public int unmatchedProductionRendererCount;
        public bool receiverInjectorCookieIsNull;
        public string chartOwnershipPolicy;
        public string canonicalRendererMappingSignature;
        public DoorwayProbeRecord receiverDoorwayProbe;
        public AmbientProbeRecord ambientProbe;
        public AtlasRecord[] atlases = Array.Empty<AtlasRecord>();
        public RendererMappingRecord[] rendererMappings = Array.Empty<RendererMappingRecord>();
    }

    [Serializable]
    internal sealed class AtlasRecord
    {
        public int canonicalBucketIndex;
        public int canonicalLocalLightmapIndex;
        public string p0ColorSourceAssetPath;
        public string p0DirectionSourceAssetPath;
        public string p100ColorCanonicalRelativePath;
        public string p100DirectionCanonicalRelativePath;
        public string responseBaselineColorRelativePath;
        public string responseBaselineDirectionRelativePath;
        public string responseFullColorRelativePath;
        public string responseFullDirectionRelativePath;
        public int width;
        public int height;
        public int p0ColorMipmapCount;
        public int p0DirectionMipmapCount;
        public int p100ColorCanonicalMipmapCount;
        public int p100DirectionCanonicalMipmapCount;
        public int responseBaselineColorMipmapCount;
        public int responseBaselineDirectionMipmapCount;
        public int responseFullColorMipmapCount;
        public int responseFullDirectionMipmapCount;

        // The rectangle represented by a Unity lightmap ST is not chart ownership.
        // These records are from the actual UV2-triangle owner render (one record per
        // source set; color and direction deliberately share the same mesh coverage).
        public int p100OperationCount;
        public int p100OwnedCoreTexelCount;
        public int p100ImplicitZeroCoreTexelCount;
        public int p100OwnerCollisionCount;
        public int responseBaselineOperationCount;
        public int responseBaselineOwnedCoreTexelCount;
        public int responseBaselineImplicitZeroCoreTexelCount;
        public int responseBaselineOwnerCollisionCount;
        public int responseFullOperationCount;
        public int responseFullOwnedCoreTexelCount;
        public int responseFullImplicitZeroCoreTexelCount;
        public int responseFullOwnerCollisionCount;

        // P0 source copied through the exact same P0 UV2/ST path and compared at
        // owned texels.  This is a fail-closed orientation/identity proof, not a
        // visual-quality claim.
        public int canonicalOrientationProofSampledTexelCount;
        public float canonicalOrientationProofMaxAbsoluteChannelError;
    }

    [Serializable]
    internal sealed class RendererMappingRecord
    {
        public string canonicalKey;
        public string productionRelativePath;
        public int canonicalOccurrence;
        public string productionMeshAssetGuid;
        public long productionMeshLocalId;
        public string productionMeshUv2Hash;
        public string lightmapUvMode;
        public int canonicalLocalLightmapIndex;
        public float[] canonicalScaleOffset = Array.Empty<float>();
        public int originalP100LocalLightmapIndex;
        public float[] originalP100ScaleOffset = Array.Empty<float>();
        public bool hasReceiverCapture;
        public string captureStableRelativePath;
        public int captureLocalLightmapIndex;
        public float[] captureScaleOffset = Array.Empty<float>();
    }

    [Serializable]
    internal sealed class ArtifactFingerprint
    {
        public string relativePath;
        public string dependencyHash;
        public string kind;
        public int width;
        public int height;
        public int mipmapCount;
        public string textureFormat;
        public bool linear;
    }

    [Serializable]
    internal sealed class CookieComparisonRecord
    {
        public string leftRoomId;
        public string rightRoomId;
        public bool bothCookiesLinearRgbaHalf;
        public int width;
        public int height;
        public float normalizedCosine;
        public float bestScale;
        public float relativeRmse;
        public float leftMeanLuminance;
        public float rightMeanLuminance;
        public string interpretation;
    }

    [Serializable]
    internal sealed class DoorwayProbeRecord
    {
        public int baselineNearDoorSampleCount;
        public int fullNearDoorSampleCount;
        public float localPlaneZ;
        public string baselineSh27Signature;
        public string fullSh27Signature;
    }

    [Serializable]
    internal sealed class AmbientProbeRecord
    {
        public float[] targetLocalPosition = Array.Empty<float>();
        public int[] selectedP0Indices = Array.Empty<int>();
        public int[] selectedP100Indices = Array.Empty<int>();
        public float[] normalizedWeights = Array.Empty<float>();
        public string p0Sh27Signature;
        public string p100Sh27Signature;
    }
}
