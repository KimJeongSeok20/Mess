using System;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DungeonPortalTransportPoC
{
    /// <summary>
    /// Immutable-by-convention Stage A evidence for a DirectOnly receiver reconstruction.
    /// This is deliberately not fit evidence: it records a render baseline and missing
    /// pose gates, never a candidate light, endpoint profile, or transport connection.
    /// </summary>
    [CreateAssetMenu(
        fileName = "DungeonPortalReceiverBounceRenderArtifact",
        menuName = "Dungeon/Lighting/Portal Transport Receiver Bounce Render Artifact")]
    public sealed class DungeonPortalReceiverBounceRenderArtifact : ScriptableObject
    {
        public const int CurrentSchemaVersion = 3;
        public const string RenderBaselineReadyStatus = "RENDER_BASELINE_READY";
        public const string IncompleteStatus = "INCOMPLETE";
        public const string DirectOnlyStateName = "DirectOnly";
        public const int FixedCameraCount = 4;
        public const string DriftMetricDefinitionVersion =
            "StageA.v2.supportedOpaque.channelMeanAndRms_pixelMaxAbsP99AndMax";
        public const string TargetSignalMetricDefinitionVersion =
            "StageA.v1.supportedOpaque.FullMinusDirect.channelMeanAndRms";
        public const float MaximumDriftToTargetSignalRatio = 0.10f;
        public const string MaskInterpretationVersion =
            "StageA.v2.selfOwnedSinglePassMrtDepth;notUrpColorIdentity;unsupportedFrontGeometrySentinel;worldGeometricVertexNormal";
        // Stage A reconstruction noise must remain materially below the roughly
        // 0.004 fixed-view bounce signal; a loose whole-frame image tolerance would
        // otherwise make a later fit comparison meaningless.  These fixed limits are
        // intentionally stricter than the available schema-7 target-ratio evidence.
        public const float MaximumMeanAbsoluteRgbDrift = 0.0005f;
        public const float MaximumRootMeanSquareRgbDrift = 0.002f;
        public const float MaximumPercentile99PixelMaxAbsoluteRgbDrift = 0.01f;
        public const float MaximumPixelMaxAbsoluteRgbDrift = 0.1f;

        private const string CanonicalStableDoorwayId = "Doorways[5]/Door_SM_A[0]/DoorWayPoint[0]";
        private static readonly string[] RequiredMissingPoseGateIds =
        {
            "closed_0",
            "intermediate_25",
            "intermediate_50",
            "intermediate_75"
        };

        [SerializeField] private int schemaVersion = CurrentSchemaVersion;
        [SerializeField] private string receiverRoomId;
        [SerializeField] private string stableDoorwayId;
        [SerializeField] private string status;
        [SerializeField] private string authoredUtcIso8601;
        [SerializeField] private string toolVersion;
        [SerializeField] private string unityVersion;
        [SerializeField] private DungeonPortalReceiverResponseCapture capture;
        [SerializeField] private string captureAssetPath;
        [SerializeField] private string captureDependencyHash;
        [SerializeField] private string directOnlyStateHash;
        [SerializeField] private string workspaceScenePath;
        [SerializeField] private string workspaceDependencyHash;
        [SerializeField] private string canonicalWorkspaceSetupSignature;
        [SerializeField] private string fullRendererParitySignature;
        [SerializeField] private string pipelineAssetPath;
        [SerializeField] private string pipelineDependencyHash;
        [SerializeField] private Shader objectIdMrtShader;
        [SerializeField] private string objectIdMrtShaderPath;
        [SerializeField] private string objectIdMrtShaderDependencyHash;
        [SerializeField] private DoorPoseEvidence doorPose;
        [SerializeField] private string stableObjectIdManifestHash;
        [SerializeField] private bool transparentOrAlphaTestRejected;
        [SerializeField] private string transparentOrAlphaTestReason;
        [SerializeField] private bool opaqueDepthAttestationVerified;
        [SerializeField] private int opaqueDepthAttestedRendererCount;
        [SerializeField] private string opaqueDepthAttestationHash;
        [SerializeField] private string maskInterpretation;
        [SerializeField] private MaskRendererAttestation[] maskRendererAttestations =
            Array.Empty<MaskRendererAttestation>();
        [SerializeField] private int unsupportedMaskSubmeshCount;
        [SerializeField] private string unsupportedMaskReason;
        [SerializeField] private bool dynamicDoorProbeReconstructionAvailable;
        [SerializeField] private string dynamicDoorProbeReconstructionReason;
        [SerializeField] private bool reconstructedOffRenderAvailable;
        [SerializeField] private bool masksAvailable;
        [SerializeField] private bool driftWithinPolicy;
        [SerializeField] private string driftMetricDefinitionVersion;
        [SerializeField] private string targetSignalMetricDefinitionVersion;
        [SerializeField] private CameraArtifact[] cameras = Array.Empty<CameraArtifact>();
        [SerializeField] private MissingPoseGate[] missingPoseGates = Array.Empty<MissingPoseGate>();
        [SerializeField] private string decisionReason;
        [SerializeField] private string artifactHash;

        public int SchemaVersion => schemaVersion;
        public string ReceiverRoomId => receiverRoomId;
        public string StableDoorwayId => stableDoorwayId;
        public string Status => status;
        public string AuthoredUtcIso8601 => authoredUtcIso8601;
        public string ToolVersion => toolVersion;
        public string UnityVersion => unityVersion;
        public DungeonPortalReceiverResponseCapture Capture => capture;
        public string CaptureAssetPath => captureAssetPath;
        public string CaptureDependencyHash => captureDependencyHash;
        public string DirectOnlyStateHash => directOnlyStateHash;
        public string WorkspaceScenePath => workspaceScenePath;
        public string WorkspaceDependencyHash => workspaceDependencyHash;
        public string CanonicalWorkspaceSetupSignature => canonicalWorkspaceSetupSignature;
        public string FullRendererParitySignature => fullRendererParitySignature;
        public string PipelineAssetPath => pipelineAssetPath;
        public string PipelineDependencyHash => pipelineDependencyHash;
        public Shader ObjectIdMrtShader => objectIdMrtShader;
        public string ObjectIdMrtShaderPath => objectIdMrtShaderPath;
        public string ObjectIdMrtShaderDependencyHash => objectIdMrtShaderDependencyHash;
        public DoorPoseEvidence DoorPose => doorPose;
        public string StableObjectIdManifestHash => stableObjectIdManifestHash;
        public bool TransparentOrAlphaTestRejected => transparentOrAlphaTestRejected;
        public string TransparentOrAlphaTestReason => transparentOrAlphaTestReason;
        public bool OpaqueDepthAttestationVerified => opaqueDepthAttestationVerified;
        public int OpaqueDepthAttestedRendererCount => opaqueDepthAttestedRendererCount;
        public string OpaqueDepthAttestationHash => opaqueDepthAttestationHash;
        public string MaskInterpretation => maskInterpretation;
        public MaskRendererAttestation[] MaskRendererAttestations => CloneArray(maskRendererAttestations);
        public int UnsupportedMaskSubmeshCount => unsupportedMaskSubmeshCount;
        public string UnsupportedMaskReason => unsupportedMaskReason;
        public bool DynamicDoorProbeReconstructionAvailable => dynamicDoorProbeReconstructionAvailable;
        public string DynamicDoorProbeReconstructionReason => dynamicDoorProbeReconstructionReason;
        public bool ReconstructedOffRenderAvailable => reconstructedOffRenderAvailable;
        public bool MasksAvailable => masksAvailable;
        public bool DriftWithinPolicy => driftWithinPolicy;
        public string DriftMetricDefinition => driftMetricDefinitionVersion;
        public string TargetSignalMetricDefinition => targetSignalMetricDefinitionVersion;
        public CameraArtifact[] Cameras => CloneArray(cameras);
        public MissingPoseGate[] MissingPoseGates => CloneArray(missingPoseGates);
        public string DecisionReason => decisionReason;
        public string ArtifactHash => artifactHash;
        public bool IsRenderBaselineReady => string.Equals(status, RenderBaselineReadyStatus, StringComparison.Ordinal);

        public void ConfigureAuthoring(RenderArtifactPayload payload)
        {
            schemaVersion = CurrentSchemaVersion;
            receiverRoomId = Clean(payload.receiverRoomId);
            stableDoorwayId = Clean(payload.stableDoorwayId);
            status = Clean(payload.status);
            authoredUtcIso8601 = Clean(payload.authoredUtcIso8601);
            toolVersion = Clean(payload.toolVersion);
            unityVersion = Clean(payload.unityVersion);
            capture = payload.capture;
            captureAssetPath = Clean(payload.captureAssetPath);
            captureDependencyHash = Clean(payload.captureDependencyHash);
            directOnlyStateHash = Clean(payload.directOnlyStateHash);
            workspaceScenePath = Clean(payload.workspaceScenePath);
            workspaceDependencyHash = Clean(payload.workspaceDependencyHash);
            canonicalWorkspaceSetupSignature = Clean(payload.canonicalWorkspaceSetupSignature);
            fullRendererParitySignature = Clean(payload.fullRendererParitySignature);
            pipelineAssetPath = Clean(payload.pipelineAssetPath);
            pipelineDependencyHash = Clean(payload.pipelineDependencyHash);
            objectIdMrtShader = payload.objectIdMrtShader;
            objectIdMrtShaderPath = Clean(payload.objectIdMrtShaderPath);
            objectIdMrtShaderDependencyHash = Clean(payload.objectIdMrtShaderDependencyHash);
            doorPose = payload.doorPose;
            stableObjectIdManifestHash = Clean(payload.stableObjectIdManifestHash);
            transparentOrAlphaTestRejected = payload.transparentOrAlphaTestRejected;
            transparentOrAlphaTestReason = Clean(payload.transparentOrAlphaTestReason);
            opaqueDepthAttestationVerified = payload.opaqueDepthAttestationVerified;
            opaqueDepthAttestedRendererCount = payload.opaqueDepthAttestedRendererCount;
            opaqueDepthAttestationHash = Clean(payload.opaqueDepthAttestationHash);
            maskInterpretation = Clean(payload.maskInterpretation);
            maskRendererAttestations = CloneArray(payload.maskRendererAttestations);
            unsupportedMaskSubmeshCount = payload.unsupportedMaskSubmeshCount;
            unsupportedMaskReason = Clean(payload.unsupportedMaskReason);
            dynamicDoorProbeReconstructionAvailable = payload.dynamicDoorProbeReconstructionAvailable;
            dynamicDoorProbeReconstructionReason = Clean(payload.dynamicDoorProbeReconstructionReason);
            reconstructedOffRenderAvailable = payload.reconstructedOffRenderAvailable;
            masksAvailable = payload.masksAvailable;
            driftWithinPolicy = payload.driftWithinPolicy;
            driftMetricDefinitionVersion = Clean(payload.driftMetricDefinitionVersion);
            targetSignalMetricDefinitionVersion = Clean(payload.targetSignalMetricDefinitionVersion);
            cameras = CloneArray(payload.cameras);
            missingPoseGates = CloneArray(payload.missingPoseGates);
            decisionReason = Clean(payload.decisionReason);
            artifactHash = ComputeArtifactHash(BuildPayload());
        }

        public bool TryValidate(out string error)
        {
            error = string.Empty;
            if (schemaVersion != CurrentSchemaVersion ||
                !IsCanonicalReceiver(receiverRoomId) ||
                !string.Equals(stableDoorwayId, CanonicalStableDoorwayId, StringComparison.Ordinal) ||
                (status != RenderBaselineReadyStatus && status != IncompleteStatus) ||
                string.IsNullOrWhiteSpace(authoredUtcIso8601) ||
                !DateTime.TryParse(authoredUtcIso8601, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) ||
                string.IsNullOrWhiteSpace(toolVersion) || string.IsNullOrWhiteSpace(unityVersion) ||
                string.IsNullOrWhiteSpace(decisionReason))
            {
                error = "Stage A artifact identity/status/timestamp is incomplete.";
                return false;
            }

            if (capture == null || string.IsNullOrWhiteSpace(captureAssetPath) ||
                string.IsNullOrWhiteSpace(captureDependencyHash) || string.IsNullOrWhiteSpace(directOnlyStateHash) ||
                !capture.TryValidate(out error) ||
                !string.Equals(capture.ReceiverRoomId, receiverRoomId, StringComparison.Ordinal) ||
                !string.Equals(capture.StableDoorwayId, stableDoorwayId, StringComparison.Ordinal) ||
                !string.Equals(capture.DirectOnly.stateHash, directOnlyStateHash, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(error))
                    error = "Stage A artifact does not bind to a valid canonical DirectOnly capture.";
                return false;
            }

            DungeonPortalReceiverResponseCapture.CaptureProvenance provenance = capture.Provenance;
            DungeonPortalReceiverResponseCapture.CaptureState directOnly = capture.DirectOnly;
            if (!IsAssetPath(workspaceScenePath) || string.IsNullOrWhiteSpace(workspaceDependencyHash) ||
                string.IsNullOrWhiteSpace(canonicalWorkspaceSetupSignature) ||
                string.IsNullOrWhiteSpace(fullRendererParitySignature) ||
                !string.Equals(workspaceScenePath, provenance.workspaceScenePath, StringComparison.Ordinal) ||
                !string.Equals(workspaceDependencyHash, provenance.canonicalWorkspaceDependencyHash, StringComparison.Ordinal) ||
                !string.Equals(canonicalWorkspaceSetupSignature, provenance.canonicalWorkspaceSetupSignature, StringComparison.Ordinal) ||
                !string.Equals(fullRendererParitySignature, directOnly.fullRendererParitySignature, StringComparison.Ordinal) ||
                !string.Equals(fullRendererParitySignature, provenance.canonicalFullRendererParitySignature, StringComparison.Ordinal))
            {
                error = "Stage A artifact workspace or full-renderer signature does not bind to the capture provenance.";
                return false;
            }

            if (!IsAssetPath(pipelineAssetPath) || string.IsNullOrWhiteSpace(pipelineDependencyHash) ||
                objectIdMrtShader == null || !IsAssetPath(objectIdMrtShaderPath) ||
                string.IsNullOrWhiteSpace(objectIdMrtShaderDependencyHash) ||
                string.IsNullOrWhiteSpace(stableObjectIdManifestHash) ||
                !string.Equals(maskInterpretation, MaskInterpretationVersion, StringComparison.Ordinal) ||
                !string.Equals(driftMetricDefinitionVersion, DriftMetricDefinitionVersion, StringComparison.Ordinal) ||
                !string.Equals(targetSignalMetricDefinitionVersion, TargetSignalMetricDefinitionVersion, StringComparison.Ordinal) ||
                (!dynamicDoorProbeReconstructionAvailable && string.IsNullOrWhiteSpace(dynamicDoorProbeReconstructionReason)) ||
                !TryValidateDoorPose(doorPose, out error))
            {
                if (string.IsNullOrWhiteSpace(error))
                    error = "Stage A artifact pipeline/shader/door-pose evidence is incomplete.";
                return false;
            }

            if (!TryEvaluateRequiredDoorPoseGates(
                    missingPoseGates,
                    out bool allRequiredDoorPosesPassed,
                    out error))
            {
                return false;
            }

            CameraArtifact[] recordedCameras = cameras ?? Array.Empty<CameraArtifact>();
            if (recordedCameras.Length != 0 && recordedCameras.Length != FixedCameraCount)
            {
                error = "Stage A artifact must record either zero or all four fixed camera outputs.";
                return false;
            }

            if (recordedCameras.Length == FixedCameraCount)
            {
                DungeonPortalReceiverResponseCapture.FixedCameraCapture[] sourceCameras = directOnly.fixedCameraCaptures;
                bool derivedDriftWithinPolicy = true;
                for (int i = 0; i < recordedCameras.Length; i++)
                {
                    if (!TryValidateCameraArtifact(
                            recordedCameras[i], sourceCameras[i], masksAvailable, out error))
                        return false;
                    derivedDriftWithinPolicy &= recordedCameras[i].drift.withinPolicy;
                }
                if (!reconstructedOffRenderAvailable || driftWithinPolicy != derivedDriftWithinPolicy)
                {
                    error = "Stage A reconstructed HDR/drift aggregate does not match the per-camera evidence.";
                    return false;
                }
            }
            else if (reconstructedOffRenderAvailable || masksAvailable || driftWithinPolicy)
            {
                error = "Stage A aggregate render/mask/drift flags require all four fixed-camera records.";
                return false;
            }

            MaskRendererAttestation[] attestations = maskRendererAttestations ??
                Array.Empty<MaskRendererAttestation>();
            if (!TryValidateMaskRendererAttestations(
                    attestations,
                    directOnly.fullRendererInventory,
                    stableObjectIdManifestHash,
                    opaqueDepthAttestationHash,
                    out int derivedUnsupportedSubmeshCount,
                    out error))
            {
                return false;
            }
            bool allCameraUnsupportedSentinelFree = true;
            for (int i = 0; i < recordedCameras.Length; i++)
                allCameraUnsupportedSentinelFree &= recordedCameras[i].unsupportedSentinelPixelCount == 0;
            if (unsupportedMaskSubmeshCount != derivedUnsupportedSubmeshCount ||
                (derivedUnsupportedSubmeshCount > 0 && string.IsNullOrWhiteSpace(unsupportedMaskReason)) ||
                (derivedUnsupportedSubmeshCount == 0 && !string.IsNullOrEmpty(unsupportedMaskReason)) ||
                opaqueDepthAttestedRendererCount != attestations.Length ||
                opaqueDepthAttestationVerified != (masksAvailable && derivedUnsupportedSubmeshCount == 0) ||
                (derivedUnsupportedSubmeshCount == 0 && !allCameraUnsupportedSentinelFree))
            {
                error = "Stage A unsupported-mask count/reason does not match the renderer attestation manifest.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(artifactHash) ||
                !string.Equals(artifactHash, ComputeArtifactHash(BuildPayload()), StringComparison.Ordinal))
            {
                error = "Stage A artifact hash is missing or does not match serialized evidence.";
                return false;
            }

            if (IsRenderBaselineReady)
            {
                bool allCameraDriftWithinPolicy = recordedCameras.Length == FixedCameraCount;
                bool allCameraOrientationVerified = recordedCameras.Length == FixedCameraCount;
                bool allCameraTargetRatioPassed = recordedCameras.Length == FixedCameraCount;
                for (int i = 0; i < recordedCameras.Length; i++)
                {
                    allCameraDriftWithinPolicy &= recordedCameras[i].drift.withinPolicy;
                    allCameraOrientationVerified &= recordedCameras[i].pixelOrientation.verified;
                    allCameraTargetRatioPassed &= recordedCameras[i].targetSignal.driftBelowTargetRatioPolicy;
                }
                if (transparentOrAlphaTestRejected || !reconstructedOffRenderAvailable || !masksAvailable ||
                    !driftWithinPolicy || !opaqueDepthAttestationVerified ||
                    !allCameraDriftWithinPolicy || !allCameraOrientationVerified ||
                    !allCameraTargetRatioPassed ||
                    !dynamicDoorProbeReconstructionAvailable ||
                    opaqueDepthAttestedRendererCount != attestations.Length ||
                     string.IsNullOrWhiteSpace(opaqueDepthAttestationHash) ||
                     derivedUnsupportedSubmeshCount != 0 ||
                     !allCameraUnsupportedSentinelFree ||
                     !allRequiredDoorPosesPassed ||
                     recordedCameras.Length != FixedCameraCount)
                {
                    error = "RENDER_BASELINE_READY lacks required door poses, opaque masks, all HDR outputs, or bounded reconstruction drift.";
                    return false;
                }
            }
            else if (transparentOrAlphaTestRejected && string.IsNullOrWhiteSpace(transparentOrAlphaTestReason))
            {
                error = "INCOMPLETE transparent/alpha-test rejection lacks its fail-closed reason.";
                return false;
            }

            return true;
        }

        [Serializable]
        public struct RenderArtifactPayload
        {
            public string receiverRoomId;
            public string stableDoorwayId;
            public string status;
            public string authoredUtcIso8601;
            public string toolVersion;
            public string unityVersion;
            public DungeonPortalReceiverResponseCapture capture;
            public string captureAssetPath;
            public string captureDependencyHash;
            public string directOnlyStateHash;
            public string workspaceScenePath;
            public string workspaceDependencyHash;
            public string canonicalWorkspaceSetupSignature;
            public string fullRendererParitySignature;
            public string pipelineAssetPath;
            public string pipelineDependencyHash;
            public Shader objectIdMrtShader;
            public string objectIdMrtShaderPath;
            public string objectIdMrtShaderDependencyHash;
            public DoorPoseEvidence doorPose;
            public string stableObjectIdManifestHash;
            public bool transparentOrAlphaTestRejected;
            public string transparentOrAlphaTestReason;
            public bool opaqueDepthAttestationVerified;
            public int opaqueDepthAttestedRendererCount;
            public string opaqueDepthAttestationHash;
            public string maskInterpretation;
            public MaskRendererAttestation[] maskRendererAttestations;
            public int unsupportedMaskSubmeshCount;
            public string unsupportedMaskReason;
            public bool dynamicDoorProbeReconstructionAvailable;
            public string dynamicDoorProbeReconstructionReason;
            public bool reconstructedOffRenderAvailable;
            public bool masksAvailable;
            public bool driftWithinPolicy;
            public string driftMetricDefinitionVersion;
            public string targetSignalMetricDefinitionVersion;
            public CameraArtifact[] cameras;
            public MissingPoseGate[] missingPoseGates;
            public string decisionReason;
        }

        [Serializable]
        public struct DoorPoseEvidence
        {
            public bool fullOpenPoseVerified;
            public bool doorOpenFlagVerified;
            public Vector3 leafLocalEulerAngles;
            public string fullOpenPoseSignature;
            public int preservedDoorLightProbeCount;
            public string preservedDoorLightProbeSignature;
        }

        [Serializable]
        public struct MissingPoseGate
        {
            public string gateId;
            public bool available;
            public bool passed;
            public string evidenceSignature;
            public string reason;
        }

        [Serializable]
        public struct CameraBinding
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
            public string pipelineAssetPath;
            public string pipelineDependencyHash;
            public bool hdr;
            public bool linearPreTonemap;
            public int width;
            public int height;
            public string textureFormat;
        }

        [Serializable]
        public struct ArtifactTexture
        {
            public Texture2D texture;
            public string assetPath;
            public string sha256;
            public int width;
            public int height;
            public string textureFormat;
            public string encoding;
        }

        [Serializable]
        public struct DriftMetrics
        {
            public int sampleCount;
            public float meanAbsoluteRgb;
            public float rootMeanSquareRgb;
            public float percentile99AbsoluteRgb;
            public float maxAbsoluteRgb;
            public bool finite;
            public bool withinPolicy;
        }

        [Serializable]
        public struct CameraArtifact
        {
            public CameraBinding sourceCamera;
            public ArtifactTexture reconstructedOffHdr;
            public ArtifactTexture stableObjectIdMask;
            public ArtifactTexture doorwayLocalZWorldNormalMask;
            public DriftMetrics drift;
            public bool driftUsesSupportedOpaqueMask;
            public TargetSignalEvidence targetSignal;
            public int supportedOpaquePixelCount;
            public int unsupportedSentinelPixelCount;
            public int backgroundPixelCount;
            public PixelOrientationEvidence pixelOrientation;
        }

        [Serializable]
        public struct TargetSignalEvidence
        {
            public bool measured;
            public bool finite;
            public int sampleCount;
            public float meanAbsoluteRgb;
            public float rootMeanSquareRgb;
            public float meanDriftToTargetRatio;
            public float rmsDriftToTargetRatio;
            public bool driftBelowTargetRatioPolicy;
            public string unavailableReason;
        }

        [Serializable]
        public struct PixelOrientationEvidence
        {
            public bool verified;
            public bool renderIntoTextureGpuProjection;
            public bool readPixelsBottomLeftOrigin;
            public int projectedSampleCount;
            public int matchedSampleCount;
            public string calibrationSignature;
            public string reason;
        }

        /// <summary>
        /// One stable object-id entry for every capture full-inventory renderer.  It is
        /// deliberately per-renderer (rather than only a visible-pixel count) so a
        /// manual override cannot silently turn an unsupported front surface into
        /// background. Unsupported submeshes write the Stage A sentinel in the
        /// self-owned MRT/depth pass and therefore make this artifact INCOMPLETE.
        /// </summary>
        [Serializable]
        public struct MaskRendererAttestation
        {
            public string canonicalBucket;
            public int stableObjectId;
            public int submeshCount;
            public int supportedOpaqueSubmeshCount;
            public int unsupportedSubmeshCount;
            public string attestationHash;
            public string unsupportedReason;
        }

        private RenderArtifactPayload BuildPayload()
        {
            return new RenderArtifactPayload
            {
                receiverRoomId = receiverRoomId,
                stableDoorwayId = stableDoorwayId,
                status = status,
                authoredUtcIso8601 = authoredUtcIso8601,
                toolVersion = toolVersion,
                unityVersion = unityVersion,
                capture = capture,
                captureAssetPath = captureAssetPath,
                captureDependencyHash = captureDependencyHash,
                directOnlyStateHash = directOnlyStateHash,
                workspaceScenePath = workspaceScenePath,
                workspaceDependencyHash = workspaceDependencyHash,
                canonicalWorkspaceSetupSignature = canonicalWorkspaceSetupSignature,
                fullRendererParitySignature = fullRendererParitySignature,
                pipelineAssetPath = pipelineAssetPath,
                pipelineDependencyHash = pipelineDependencyHash,
                objectIdMrtShader = objectIdMrtShader,
                objectIdMrtShaderPath = objectIdMrtShaderPath,
                objectIdMrtShaderDependencyHash = objectIdMrtShaderDependencyHash,
                doorPose = doorPose,
                stableObjectIdManifestHash = stableObjectIdManifestHash,
                transparentOrAlphaTestRejected = transparentOrAlphaTestRejected,
                transparentOrAlphaTestReason = transparentOrAlphaTestReason,
                opaqueDepthAttestationVerified = opaqueDepthAttestationVerified,
                opaqueDepthAttestedRendererCount = opaqueDepthAttestedRendererCount,
                opaqueDepthAttestationHash = opaqueDepthAttestationHash,
                maskInterpretation = maskInterpretation,
                maskRendererAttestations = maskRendererAttestations,
                unsupportedMaskSubmeshCount = unsupportedMaskSubmeshCount,
                unsupportedMaskReason = unsupportedMaskReason,
                dynamicDoorProbeReconstructionAvailable = dynamicDoorProbeReconstructionAvailable,
                dynamicDoorProbeReconstructionReason = dynamicDoorProbeReconstructionReason,
                reconstructedOffRenderAvailable = reconstructedOffRenderAvailable,
                masksAvailable = masksAvailable,
                driftWithinPolicy = driftWithinPolicy,
                driftMetricDefinitionVersion = driftMetricDefinitionVersion,
                targetSignalMetricDefinitionVersion = targetSignalMetricDefinitionVersion,
                cameras = cameras,
                missingPoseGates = missingPoseGates,
                decisionReason = decisionReason
            };
        }

        public static string ComputeArtifactHash(RenderArtifactPayload payload)
        {
            var builder = new StringBuilder(16384);
            AppendString(builder, payload.receiverRoomId);
            AppendString(builder, payload.stableDoorwayId);
            AppendString(builder, payload.status);
            AppendString(builder, payload.authoredUtcIso8601);
            AppendString(builder, payload.toolVersion);
            AppendString(builder, payload.unityVersion);
            AppendString(builder, payload.capture != null ? payload.capture.ReceiverRoomId : string.Empty);
            AppendString(builder, payload.capture != null ? payload.capture.StableDoorwayId : string.Empty);
            AppendString(builder, payload.captureAssetPath);
            AppendString(builder, payload.captureDependencyHash);
            AppendString(builder, payload.directOnlyStateHash);
            AppendString(builder, payload.workspaceScenePath);
            AppendString(builder, payload.workspaceDependencyHash);
            AppendString(builder, payload.canonicalWorkspaceSetupSignature);
            AppendString(builder, payload.fullRendererParitySignature);
            AppendString(builder, payload.pipelineAssetPath);
            AppendString(builder, payload.pipelineDependencyHash);
            AppendString(builder, payload.objectIdMrtShaderPath);
            AppendString(builder, payload.objectIdMrtShaderDependencyHash);
            AppendDoorPose(builder, payload.doorPose);
            AppendString(builder, payload.stableObjectIdManifestHash);
            AppendBool(builder, payload.transparentOrAlphaTestRejected);
            AppendString(builder, payload.transparentOrAlphaTestReason);
            AppendBool(builder, payload.opaqueDepthAttestationVerified);
            builder.Append(payload.opaqueDepthAttestedRendererCount).Append('|');
            AppendString(builder, payload.opaqueDepthAttestationHash);
            AppendString(builder, payload.maskInterpretation);
            MaskRendererAttestation[] attestations = payload.maskRendererAttestations ??
                Array.Empty<MaskRendererAttestation>();
            builder.Append(attestations.Length).Append('|');
            for (int i = 0; i < attestations.Length; i++)
                AppendMaskRendererAttestation(builder, attestations[i]);
            builder.Append(payload.unsupportedMaskSubmeshCount).Append('|');
            AppendString(builder, payload.unsupportedMaskReason);
            AppendBool(builder, payload.dynamicDoorProbeReconstructionAvailable);
            AppendString(builder, payload.dynamicDoorProbeReconstructionReason);
            AppendBool(builder, payload.reconstructedOffRenderAvailable);
            AppendBool(builder, payload.masksAvailable);
            AppendBool(builder, payload.driftWithinPolicy);
            AppendString(builder, payload.driftMetricDefinitionVersion);
            AppendString(builder, payload.targetSignalMetricDefinitionVersion);
            CameraArtifact[] cameraArtifacts = payload.cameras ?? Array.Empty<CameraArtifact>();
            builder.Append(cameraArtifacts.Length).Append('|');
            for (int i = 0; i < cameraArtifacts.Length; i++)
                AppendCameraArtifact(builder, cameraArtifacts[i]);
            MissingPoseGate[] gates = payload.missingPoseGates ?? Array.Empty<MissingPoseGate>();
            builder.Append(gates.Length).Append('|');
            for (int i = 0; i < gates.Length; i++)
                AppendMissingPoseGate(builder, gates[i]);
            AppendString(builder, payload.decisionReason);
            return Hash128.Compute(builder.ToString()).ToString();
        }

        private static bool TryValidateDoorPose(DoorPoseEvidence value, out string error)
        {
            if (!value.fullOpenPoseVerified || !value.doorOpenFlagVerified ||
                !IsFinite(value.leafLocalEulerAngles) ||
                Mathf.Abs(Mathf.DeltaAngle(value.leafLocalEulerAngles.y, 90f)) > 0.01f ||
                string.IsNullOrWhiteSpace(value.fullOpenPoseSignature) ||
                value.preservedDoorLightProbeCount != 8 ||
                string.IsNullOrWhiteSpace(value.preservedDoorLightProbeSignature))
            {
                error = "Stage A requires the schema-7 fully-open real-door and eight-probe contract.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Validates the exact 0/25/50/75 door-pose gate manifest and separately reports
        /// whether every pose is both captured and validated. A structurally valid
        /// fail-closed manifest may therefore return true while
        /// <paramref name="allRequiredPosesPassed"/> remains false.
        /// </summary>
        public static bool TryEvaluateRequiredDoorPoseGates(
            MissingPoseGate[] value,
            out bool allRequiredPosesPassed,
            out string error)
        {
            allRequiredPosesPassed = false;
            MissingPoseGate[] gates = value ?? Array.Empty<MissingPoseGate>();
            if (gates.Length != RequiredMissingPoseGateIds.Length)
            {
                error = "Stage A must explicitly record all required closed/intermediate door-pose gates.";
                return false;
            }

            bool allPassed = true;
            for (int i = 0; i < RequiredMissingPoseGateIds.Length; i++)
            {
                MissingPoseGate gate = gates[i];
                if (!string.Equals(gate.gateId, RequiredMissingPoseGateIds[i], StringComparison.Ordinal))
                {
                    error = "Stage A required door-pose gate '" + RequiredMissingPoseGateIds[i] +
                            "' is missing or out of order.";
                    return false;
                }

                if (gate.available)
                {
                    if (string.IsNullOrWhiteSpace(gate.evidenceSignature))
                    {
                        error = "Stage A required door-pose gate '" + gate.gateId +
                                "' is marked available without captured evidence.";
                        return false;
                    }
                }
                else if (gate.passed || !string.IsNullOrEmpty(gate.evidenceSignature))
                {
                    error = "Stage A required door-pose gate '" + gate.gateId +
                            "' cannot pass or carry evidence while unavailable.";
                    return false;
                }

                if (gate.passed)
                {
                    if (!string.IsNullOrEmpty(gate.reason))
                    {
                        error = "Stage A required door-pose gate '" + gate.gateId +
                                "' cannot be both passed and blocked.";
                        return false;
                    }
                }
                else if (string.IsNullOrWhiteSpace(gate.reason))
                {
                    error = "Stage A required door-pose gate '" + gate.gateId +
                            "' is not fail-closed with a reason.";
                    return false;
                }

                allPassed &= gate.available && gate.passed;
            }

            allRequiredPosesPassed = allPassed;
            error = string.Empty;
            return true;
        }

        private static bool TryValidateCameraArtifact(
            CameraArtifact value,
            DungeonPortalReceiverResponseCapture.FixedCameraCapture source,
            bool requireMasks,
            out string error)
        {
            error = string.Empty;
            CameraBinding binding = value.sourceCamera;
            if (!string.Equals(binding.cameraId, source.cameraId, StringComparison.Ordinal) ||
                !string.Equals(binding.cameraPath, source.cameraPath, StringComparison.Ordinal) ||
                !Approximately(binding.doorwayLocalPosition, source.doorwayLocalPosition) ||
                !Approximately(binding.doorwayLocalEulerAngles, source.doorwayLocalEulerAngles) ||
                !Approximately(binding.fieldOfView, source.fieldOfView) ||
                !Approximately(binding.nearClipPlane, source.nearClipPlane) ||
                !Approximately(binding.farClipPlane, source.farClipPlane) ||
                binding.orthographic != source.orthographic ||
                !Approximately(binding.orthographicSize, source.orthographicSize) ||
                !Approximately(binding.aspect, source.aspect) ||
                binding.clearFlags != source.clearFlags || !Approximately(binding.backgroundColor, source.backgroundColor) ||
                binding.cullingMask != source.cullingMask || binding.renderingPath != source.renderingPath ||
                binding.postProcessingEnabled != source.postProcessingEnabled ||
                binding.antialiasing != source.antialiasing || binding.dithering != source.dithering ||
                binding.volumeLayerMask != source.volumeLayerMask || binding.allowMsaa != source.allowMsaa ||
                binding.allowDynamicResolution != source.allowDynamicResolution ||
                binding.useOcclusionCulling != source.useOcclusionCulling ||
                binding.urpRendererIndex != source.urpRendererIndex ||
                !string.Equals(binding.pipelineAssetPath, source.renderPipelineAssetPath, StringComparison.Ordinal) ||
                !string.Equals(binding.pipelineDependencyHash, source.renderPipelineDependencyHash, StringComparison.Ordinal) ||
                binding.hdr != source.hdr || binding.linearPreTonemap != source.linearPreTonemap ||
                binding.width != source.width || binding.height != source.height ||
                !string.Equals(binding.textureFormat, source.textureFormat, StringComparison.Ordinal) ||
                !TryValidateTexture(value.reconstructedOffHdr, source.width, source.height, "EXR", out error))
            {
                if (string.IsNullOrWhiteSpace(error))
                    error = "Stage A camera artifact does not bind to its DirectOnly camera contract.";
                return false;
            }

            int fullSampleCount = source.width * source.height;
            if (!TryValidatePixelCounts(value, fullSampleCount, requireMasks, out error) ||
                !TryValidatePixelOrientation(value.pixelOrientation, requireMasks, out error))
            {
                return false;
            }

            int driftSampleCount = requireMasks ? value.supportedOpaquePixelCount : fullSampleCount;
            if (value.driftUsesSupportedOpaqueMask != requireMasks ||
                !TryValidateDrift(value.drift, driftSampleCount, out error) ||
                !TryValidateTargetSignal(value.targetSignal, value.drift, driftSampleCount, requireMasks, out error))
            {
                return false;
            }

            return true;
        }

        private static bool TryValidatePixelCounts(CameraArtifact value, int sampleCount, bool requireMasks, out string error)
        {
            bool hasObjectId = value.stableObjectIdMask.texture != null;
            bool hasGeometry = value.doorwayLocalZWorldNormalMask.texture != null;
            if (hasObjectId != hasGeometry || (requireMasks && !hasObjectId) ||
                (!requireMasks && hasObjectId))
            {
                error = "Stage A mask outputs must be present for every camera exactly when the mask aggregate is available.";
                return false;
            }

            if (hasObjectId &&
                (!TryValidateTexture(value.stableObjectIdMask, value.sourceCamera.width, value.sourceCamera.height, "PNG", out error) ||
                 !TryValidateTexture(value.doorwayLocalZWorldNormalMask, value.sourceCamera.width, value.sourceCamera.height, "EXR", out error)))
            {
                return false;
            }

            if (value.supportedOpaquePixelCount < 0 || value.unsupportedSentinelPixelCount < 0 ||
                value.backgroundPixelCount < 0 ||
                value.supportedOpaquePixelCount + value.unsupportedSentinelPixelCount + value.backgroundPixelCount != sampleCount ||
                (!hasObjectId &&
                 (value.supportedOpaquePixelCount != 0 || value.unsupportedSentinelPixelCount != 0 ||
                  value.backgroundPixelCount != 0)))
            {
                error = "Stage A mask pixel classification counts are malformed.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidatePixelOrientation(
            PixelOrientationEvidence value,
            bool masksRequired,
            out string error)
        {
            if (!masksRequired)
            {
                if (value.verified || value.projectedSampleCount != 0 || value.matchedSampleCount != 0 ||
                    !string.IsNullOrEmpty(value.calibrationSignature) || string.IsNullOrWhiteSpace(value.reason))
                {
                    error = "Stage A unavailable mask output must explicitly record unavailable pixel-orientation proof.";
                    return false;
                }
                error = string.Empty;
                return true;
            }

            if (value.renderIntoTextureGpuProjection != true || !value.readPixelsBottomLeftOrigin ||
                (value.verified &&
                 (value.projectedSampleCount <= 0 || value.matchedSampleCount != value.projectedSampleCount ||
                  string.IsNullOrWhiteSpace(value.calibrationSignature))) ||
                (!value.verified && string.IsNullOrWhiteSpace(value.reason)))
            {
                error = "Stage A mask/HDR pixel-orientation evidence is incomplete.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidateTexture(ArtifactTexture value, int width, int height, string encoding, out string error)
        {
            if (value.texture == null || !IsAssetPath(value.assetPath) || string.IsNullOrWhiteSpace(value.sha256) ||
                value.width != width || value.height != height || value.texture.width != width ||
                value.texture.height != height || string.IsNullOrWhiteSpace(value.textureFormat) ||
                !string.Equals(value.encoding, encoding, StringComparison.Ordinal))
            {
                error = "Stage A generated texture evidence is incomplete or has a camera dimension mismatch.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static bool TryValidateDrift(DriftMetrics value, int expectedSampleCount, out string error)
        {
            if (expectedSampleCount == 0)
            {
                if (value.sampleCount != 0 || value.finite || value.withinPolicy ||
                    value.meanAbsoluteRgb != 0f || value.rootMeanSquareRgb != 0f ||
                    value.percentile99AbsoluteRgb != 0f || value.maxAbsoluteRgb != 0f)
                {
                    error = "Stage A empty supported-opaque mask subset must have explicitly unavailable drift metrics.";
                    return false;
                }
                error = string.Empty;
                return true;
            }
            bool derivedWithinPolicy = value.finite &&
                                       IsFiniteNonNegative(value.meanAbsoluteRgb) &&
                                       IsFiniteNonNegative(value.rootMeanSquareRgb) &&
                                       IsFiniteNonNegative(value.percentile99AbsoluteRgb) &&
                                       IsFiniteNonNegative(value.maxAbsoluteRgb) &&
                                       value.meanAbsoluteRgb <= MaximumMeanAbsoluteRgbDrift &&
                                       value.rootMeanSquareRgb <= MaximumRootMeanSquareRgbDrift &&
                                       value.percentile99AbsoluteRgb <= MaximumPercentile99PixelMaxAbsoluteRgbDrift &&
                                       value.maxAbsoluteRgb <= MaximumPixelMaxAbsoluteRgbDrift;
            if (value.sampleCount != expectedSampleCount || !value.finite ||
                !IsFiniteNonNegative(value.meanAbsoluteRgb) || !IsFiniteNonNegative(value.rootMeanSquareRgb) ||
                !IsFiniteNonNegative(value.percentile99AbsoluteRgb) || !IsFiniteNonNegative(value.maxAbsoluteRgb) ||
                value.maxAbsoluteRgb < value.percentile99AbsoluteRgb ||
                value.rootMeanSquareRgb + 0.000001f < value.meanAbsoluteRgb ||
                value.withinPolicy != derivedWithinPolicy)
            {
                error = "Stage A reconstructed-off HDR drift evidence is malformed.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static bool TryValidateTargetSignal(
            TargetSignalEvidence value,
            DriftMetrics drift,
            int expectedSampleCount,
            bool requireMaskSubset,
            out string error)
        {
            if (!requireMaskSubset || expectedSampleCount == 0)
            {
                if (value.measured || value.finite || value.sampleCount != 0 ||
                    value.meanAbsoluteRgb != 0f || value.rootMeanSquareRgb != 0f ||
                    value.meanDriftToTargetRatio != 0f || value.rmsDriftToTargetRatio != 0f ||
                    value.driftBelowTargetRatioPolicy || string.IsNullOrWhiteSpace(value.unavailableReason))
                {
                    error = "Stage A target-signal evidence without an exact supported-opaque mask must be explicitly unavailable.";
                    return false;
                }
                error = string.Empty;
                return true;
            }

            bool finite = value.measured && value.finite && value.sampleCount == expectedSampleCount &&
                          expectedSampleCount > 0 && IsFiniteNonNegative(value.meanAbsoluteRgb) &&
                          IsFiniteNonNegative(value.rootMeanSquareRgb) &&
                          value.meanAbsoluteRgb > 0f && value.rootMeanSquareRgb > 0f &&
                          value.rootMeanSquareRgb + 0.000001f >= value.meanAbsoluteRgb &&
                          IsFiniteNonNegative(value.meanDriftToTargetRatio) &&
                          IsFiniteNonNegative(value.rmsDriftToTargetRatio) &&
                          string.IsNullOrEmpty(value.unavailableReason);
            bool derivedRatioPass = finite &&
                                    Mathf.Abs(value.meanDriftToTargetRatio -
                                              drift.meanAbsoluteRgb / value.meanAbsoluteRgb) <= 0.0001f &&
                                    Mathf.Abs(value.rmsDriftToTargetRatio -
                                              drift.rootMeanSquareRgb / value.rootMeanSquareRgb) <= 0.0001f &&
                                    value.meanDriftToTargetRatio <= MaximumDriftToTargetSignalRatio &&
                                    value.rmsDriftToTargetRatio <= MaximumDriftToTargetSignalRatio;
            if (!finite || value.driftBelowTargetRatioPolicy != derivedRatioPass)
            {
                error = "Stage A supported-opaque Full-minus-Direct signal or relative reconstruction-noise gate is malformed.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidateMaskRendererAttestations(
            MaskRendererAttestation[] value,
            DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry[] inventory,
            string expectedStableObjectIdManifestHash,
            string expectedOpaqueDepthAttestationHash,
            out int unsupportedSubmeshCount,
            out string error)
        {
            value = value ?? Array.Empty<MaskRendererAttestation>();
            inventory = inventory ?? Array.Empty<DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry>();
            unsupportedSubmeshCount = 0;
            if (value.Length != inventory.Length || value.Length == 0)
            {
                error = "Stage A mask attestation must bind exactly one record to every full-inventory renderer.";
                return false;
            }

            var manifestBuilder = new StringBuilder(value.Length * 96);
            var depthBuilder = new StringBuilder(value.Length * 160);
            for (int i = 0; i < value.Length; i++)
            {
                MaskRendererAttestation entry = value[i];
                DungeonPortalReceiverResponseCapture.FullRendererInventoryEntry source = inventory[i];
                if (!string.Equals(entry.canonicalBucket, source.canonicalBucket, StringComparison.Ordinal) ||
                    entry.stableObjectId != i + 1 || entry.submeshCount <= 0 ||
                    entry.supportedOpaqueSubmeshCount < 0 || entry.unsupportedSubmeshCount < 0 ||
                    entry.supportedOpaqueSubmeshCount + entry.unsupportedSubmeshCount != entry.submeshCount ||
                    string.IsNullOrWhiteSpace(entry.attestationHash) ||
                    (entry.unsupportedSubmeshCount > 0 && string.IsNullOrWhiteSpace(entry.unsupportedReason)) ||
                    (entry.unsupportedSubmeshCount == 0 && !string.IsNullOrEmpty(entry.unsupportedReason)))
                {
                    error = "Stage A mask renderer attestation is malformed or does not bind to the canonical inventory.";
                    return false;
                }

                AppendString(manifestBuilder, entry.canonicalBucket);
                manifestBuilder.Append(entry.stableObjectId).Append('|');
                AppendString(depthBuilder, entry.canonicalBucket);
                depthBuilder.Append(entry.stableObjectId).Append('|');
                depthBuilder.Append(entry.submeshCount).Append('|');
                depthBuilder.Append(entry.supportedOpaqueSubmeshCount).Append('|');
                depthBuilder.Append(entry.unsupportedSubmeshCount).Append('|');
                AppendString(depthBuilder, entry.attestationHash);
                AppendString(depthBuilder, entry.unsupportedReason);
                unsupportedSubmeshCount += entry.unsupportedSubmeshCount;
            }

            if (!string.Equals(
                    Hash128.Compute(manifestBuilder.ToString()).ToString(),
                    expectedStableObjectIdManifestHash,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    Hash128.Compute(depthBuilder.ToString()).ToString(),
                    expectedOpaqueDepthAttestationHash,
                    StringComparison.Ordinal))
            {
                error = "Stage A stable object-id or opaque-depth attestation manifest hash does not match its entries.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool IsCanonicalReceiver(string value)
        {
            return string.Equals(value, "StartRoom_R000", StringComparison.Ordinal) ||
                   string.Equals(value, "AdminstrativeSegregation_R000", StringComparison.Ordinal);
        }

        private static bool IsAssetPath(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.StartsWith("Assets/", StringComparison.Ordinal) &&
                   value.IndexOf("..", StringComparison.Ordinal) < 0;
        }

        private static string Clean(string value)
        {
            return value != null ? value.Trim() : string.Empty;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return IsFinite(value) && value >= 0f;
        }

        private static bool Approximately(float left, float right)
        {
            return IsFinite(left) && IsFinite(right) && Mathf.Abs(left - right) <= 0.0001f;
        }

        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return IsFinite(left) && IsFinite(right) && (left - right).sqrMagnitude <= 0.00000001f;
        }

        private static bool Approximately(Color left, Color right)
        {
            return IsFinite(left.r) && IsFinite(left.g) && IsFinite(left.b) && IsFinite(left.a) &&
                   IsFinite(right.r) && IsFinite(right.g) && IsFinite(right.b) && IsFinite(right.a) &&
                   Mathf.Abs(left.r - right.r) <= 0.0001f && Mathf.Abs(left.g - right.g) <= 0.0001f &&
                   Mathf.Abs(left.b - right.b) <= 0.0001f && Mathf.Abs(left.a - right.a) <= 0.0001f;
        }

        private static T[] CloneArray<T>(T[] value)
        {
            return value != null ? (T[])value.Clone() : Array.Empty<T>();
        }

        private static void AppendDoorPose(StringBuilder builder, DoorPoseEvidence value)
        {
            AppendBool(builder, value.fullOpenPoseVerified);
            AppendBool(builder, value.doorOpenFlagVerified);
            AppendVector3(builder, value.leafLocalEulerAngles);
            AppendString(builder, value.fullOpenPoseSignature);
            builder.Append(value.preservedDoorLightProbeCount).Append('|');
            AppendString(builder, value.preservedDoorLightProbeSignature);
        }

        private static void AppendMissingPoseGate(StringBuilder builder, MissingPoseGate value)
        {
            AppendString(builder, value.gateId);
            AppendBool(builder, value.available);
            AppendBool(builder, value.passed);
            AppendString(builder, value.evidenceSignature);
            AppendString(builder, value.reason);
        }

        private static void AppendCameraArtifact(StringBuilder builder, CameraArtifact value)
        {
            CameraBinding binding = value.sourceCamera;
            AppendString(builder, binding.cameraId);
            AppendString(builder, binding.cameraPath);
            AppendVector3(builder, binding.doorwayLocalPosition);
            AppendVector3(builder, binding.doorwayLocalEulerAngles);
            AppendFloat(builder, binding.fieldOfView);
            AppendFloat(builder, binding.nearClipPlane);
            AppendFloat(builder, binding.farClipPlane);
            AppendBool(builder, binding.orthographic);
            AppendFloat(builder, binding.orthographicSize);
            AppendFloat(builder, binding.aspect);
            builder.Append((int)binding.clearFlags).Append('|');
            AppendColor(builder, binding.backgroundColor);
            builder.Append(binding.cullingMask).Append('|').Append((int)binding.renderingPath).Append('|');
            AppendBool(builder, binding.postProcessingEnabled);
            builder.Append((int)binding.antialiasing).Append('|');
            AppendBool(builder, binding.dithering);
            builder.Append(binding.volumeLayerMask).Append('|');
            AppendBool(builder, binding.allowMsaa);
            AppendBool(builder, binding.allowDynamicResolution);
            AppendBool(builder, binding.useOcclusionCulling);
            builder.Append(binding.urpRendererIndex).Append('|');
            AppendString(builder, binding.pipelineAssetPath);
            AppendString(builder, binding.pipelineDependencyHash);
            AppendBool(builder, binding.hdr);
            AppendBool(builder, binding.linearPreTonemap);
            builder.Append(binding.width).Append('|').Append(binding.height).Append('|');
            AppendString(builder, binding.textureFormat);
            AppendTexture(builder, value.reconstructedOffHdr);
            AppendTexture(builder, value.stableObjectIdMask);
            AppendTexture(builder, value.doorwayLocalZWorldNormalMask);
            DriftMetrics drift = value.drift;
            builder.Append(drift.sampleCount).Append('|');
            AppendFloat(builder, drift.meanAbsoluteRgb);
            AppendFloat(builder, drift.rootMeanSquareRgb);
            AppendFloat(builder, drift.percentile99AbsoluteRgb);
            AppendFloat(builder, drift.maxAbsoluteRgb);
            AppendBool(builder, drift.finite);
            AppendBool(builder, drift.withinPolicy);
            AppendBool(builder, value.driftUsesSupportedOpaqueMask);
            TargetSignalEvidence signal = value.targetSignal;
            AppendBool(builder, signal.measured);
            AppendBool(builder, signal.finite);
            builder.Append(signal.sampleCount).Append('|');
            AppendFloat(builder, signal.meanAbsoluteRgb);
            AppendFloat(builder, signal.rootMeanSquareRgb);
            AppendFloat(builder, signal.meanDriftToTargetRatio);
            AppendFloat(builder, signal.rmsDriftToTargetRatio);
            AppendBool(builder, signal.driftBelowTargetRatioPolicy);
            AppendString(builder, signal.unavailableReason);
            builder.Append(value.supportedOpaquePixelCount).Append('|');
            builder.Append(value.unsupportedSentinelPixelCount).Append('|');
            builder.Append(value.backgroundPixelCount).Append('|');
            PixelOrientationEvidence orientation = value.pixelOrientation;
            AppendBool(builder, orientation.verified);
            AppendBool(builder, orientation.renderIntoTextureGpuProjection);
            AppendBool(builder, orientation.readPixelsBottomLeftOrigin);
            builder.Append(orientation.projectedSampleCount).Append('|');
            builder.Append(orientation.matchedSampleCount).Append('|');
            AppendString(builder, orientation.calibrationSignature);
            AppendString(builder, orientation.reason);
        }

        private static void AppendMaskRendererAttestation(StringBuilder builder, MaskRendererAttestation value)
        {
            AppendString(builder, value.canonicalBucket);
            builder.Append(value.stableObjectId).Append('|');
            builder.Append(value.submeshCount).Append('|');
            builder.Append(value.supportedOpaqueSubmeshCount).Append('|');
            builder.Append(value.unsupportedSubmeshCount).Append('|');
            AppendString(builder, value.attestationHash);
            AppendString(builder, value.unsupportedReason);
        }

        private static void AppendTexture(StringBuilder builder, ArtifactTexture value)
        {
            AppendString(builder, value.assetPath);
            AppendString(builder, value.sha256);
            builder.Append(value.width).Append('|').Append(value.height).Append('|');
            AppendString(builder, value.textureFormat);
            AppendString(builder, value.encoding);
        }

        private static void AppendString(StringBuilder builder, string value)
        {
            value = value ?? string.Empty;
            builder.Append(value.Length).Append(':').Append(value).Append('|');
        }

        private static void AppendBool(StringBuilder builder, bool value)
        {
            builder.Append(value ? '1' : '0').Append('|');
        }

        private static void AppendFloat(StringBuilder builder, float value)
        {
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        }

        private static void AppendVector3(StringBuilder builder, Vector3 value)
        {
            AppendFloat(builder, value.x);
            AppendFloat(builder, value.y);
            AppendFloat(builder, value.z);
        }

        private static void AppendColor(StringBuilder builder, Color value)
        {
            AppendFloat(builder, value.r);
            AppendFloat(builder, value.g);
            AppendFloat(builder, value.b);
            AppendFloat(builder, value.a);
        }
    }
}
