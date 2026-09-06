using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DungeonPortalTransportPoC
{
    /// <summary>
    /// Immutable-by-convention, editor-authored evidence for a receiver-side K=1 bounce fit.
    /// It deliberately records rejection evidence as well as accepted evidence: a missing
    /// semantic mask or comparable candidate response is never treated as a zero-error fit.
    /// Runtime transport does not read this asset.
    /// </summary>
    [CreateAssetMenu(
        fileName = "DungeonPortalReceiverBounceFitEvidence",
        menuName = "Dungeon/Lighting/Portal Transport Receiver Bounce Fit Evidence")]
    public sealed class DungeonPortalReceiverBounceFitEvidence : ScriptableObject
    {
        public const int CurrentSchemaVersion = 2;
        public const string RejectedStatus = "K1_REJECTED";
        public const string AcceptedStatus = "K1_ACCEPTED";
        public const int ExpectedCandidateCount = 648;
        public const string MetricDefinitionVersion = "receiver-bounce-k1-v1";
        public const string PixelCoverageMetric = "pixel=max(abs(RGB_delta))";
        public const string RgbEnergyMetric = "energy=sum(abs(RGB_channel_delta))";
        public const string ProbeL0Metric = "probe=L0_DC_RGB";
        public const string ProbeNonDcMetric = "probe=SH_nonDC_L2_RMS_maxAbs_no_sign_gate";
        private const string CanonicalStableDoorwayId = "Doorways[5]/Door_SM_A[0]/DoorWayPoint[0]";

        [SerializeField] private int schemaVersion = CurrentSchemaVersion;
        [SerializeField] private string receiverRoomId;
        [SerializeField] private string stableDoorwayId;
        [SerializeField] private string status;
        [SerializeField] private string authoredUtcIso8601;
        [SerializeField] private string fitToolVersion;
        [SerializeField] private string unityVersion;
        [SerializeField] private string metricDefinitionVersion;
        [SerializeField] private DungeonPortalReceiverResponseCapture receiverCapture;
        [SerializeField] private string receiverCaptureAssetPath;
        [SerializeField] private string receiverCaptureDependencyHash;
        [SerializeField] private string baselineStateHash;
        [SerializeField] private string directOnlyStateHash;
        [SerializeField] private string fullStateHash;
        [SerializeField] private CaptureIntegrityEvidence captureIntegrity;
        [SerializeField] private DungeonPortalEndpointProfile targetProfile;
        [SerializeField] private string targetProfileAssetPath;
        [SerializeField] private string targetProfileDependencyHash;
        [SerializeField] private SignalGateEvidence signalGates;
        [SerializeField] private SemanticMaskEvidence semanticMasks;
        [SerializeField] private CandidateSearchEvidence candidateSearch;
        [SerializeField] private FitGateEvidence fitGates;
        [SerializeField] private K1BounceDescriptor acceptedDescriptor;
        [SerializeField] private string decisionReason;
        [SerializeField] private string evidenceHash;

        public int SchemaVersion => schemaVersion;
        public string ReceiverRoomId => receiverRoomId;
        public string StableDoorwayId => stableDoorwayId;
        public string Status => status;
        public string AuthoredUtcIso8601 => authoredUtcIso8601;
        public string FitToolVersion => fitToolVersion;
        public string UnityVersion => unityVersion;
        public string MetricVersion => metricDefinitionVersion;
        public DungeonPortalReceiverResponseCapture ReceiverCapture => receiverCapture;
        public string ReceiverCaptureAssetPath => receiverCaptureAssetPath;
        public string ReceiverCaptureDependencyHash => receiverCaptureDependencyHash;
        public string BaselineStateHash => baselineStateHash;
        public string DirectOnlyStateHash => directOnlyStateHash;
        public string FullStateHash => fullStateHash;
        public CaptureIntegrityEvidence CaptureIntegrity => captureIntegrity;
        public DungeonPortalEndpointProfile TargetProfile => targetProfile;
        public string TargetProfileAssetPath => targetProfileAssetPath;
        public string TargetProfileDependencyHash => targetProfileDependencyHash;
        public SignalGateEvidence SignalGates => CloneSignalGates(signalGates);
        public SemanticMaskEvidence SemanticMasks => semanticMasks;
        public CandidateSearchEvidence CandidateSearch => CloneCandidateSearch(candidateSearch);
        public FitGateEvidence FitGates => CloneFitGates(fitGates);
        public K1BounceDescriptor AcceptedDescriptor => acceptedDescriptor;
        public string DecisionReason => decisionReason;
        public string EvidenceHash => evidenceHash;
        public bool IsAccepted => string.Equals(status, AcceptedStatus, StringComparison.Ordinal);

        /// <summary>
        /// Called only by the editor fitter after all source captures and gate results are
        /// collected. The payload is copied so callers cannot retain a mutable array alias.
        /// </summary>
        public void ConfigureAuthoring(FitEvidencePayload payload)
        {
            schemaVersion = CurrentSchemaVersion;
            receiverRoomId = Clean(payload.receiverRoomId);
            stableDoorwayId = Clean(payload.stableDoorwayId);
            status = Clean(payload.status);
            authoredUtcIso8601 = Clean(payload.authoredUtcIso8601);
            fitToolVersion = Clean(payload.fitToolVersion);
            unityVersion = Clean(payload.unityVersion);
            metricDefinitionVersion = Clean(payload.metricDefinitionVersion);
            receiverCapture = payload.receiverCapture;
            receiverCaptureAssetPath = Clean(payload.receiverCaptureAssetPath);
            receiverCaptureDependencyHash = Clean(payload.receiverCaptureDependencyHash);
            baselineStateHash = Clean(payload.baselineStateHash);
            directOnlyStateHash = Clean(payload.directOnlyStateHash);
            fullStateHash = Clean(payload.fullStateHash);
            captureIntegrity = payload.captureIntegrity;
            targetProfile = payload.targetProfile;
            targetProfileAssetPath = Clean(payload.targetProfileAssetPath);
            targetProfileDependencyHash = Clean(payload.targetProfileDependencyHash);
            signalGates = CloneSignalGates(payload.signalGates);
            semanticMasks = payload.semanticMasks;
            candidateSearch = CloneCandidateSearch(payload.candidateSearch);
            fitGates = CloneFitGates(payload.fitGates);
            acceptedDescriptor = payload.acceptedDescriptor;
            decisionReason = Clean(payload.decisionReason);
            evidenceHash = ComputeEvidenceHash(BuildPayload());
        }

        /// <summary>
        /// Structural validation intentionally cannot turn rejected data into an accepted
        /// fit. An accepted record requires semantic masks and comparable candidate
        /// response evidence in addition to every numeric gate.
        /// </summary>
        public bool TryValidate(out string error)
        {
            error = string.Empty;
            if (schemaVersion != CurrentSchemaVersion)
            {
                error = "Unsupported receiver-bounce fit evidence schema " + schemaVersion + ".";
                return false;
            }

            if (!IsCanonicalReceiver(receiverRoomId) ||
                !string.Equals(stableDoorwayId, CanonicalStableDoorwayId, StringComparison.Ordinal) ||
                (status != RejectedStatus && status != AcceptedStatus) ||
                string.IsNullOrWhiteSpace(authoredUtcIso8601) || string.IsNullOrWhiteSpace(fitToolVersion) ||
                string.IsNullOrWhiteSpace(unityVersion) ||
                !string.Equals(metricDefinitionVersion, MetricDefinitionVersion, StringComparison.Ordinal) ||
                !DateTime.TryParse(
                    authoredUtcIso8601,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out _) ||
                string.IsNullOrWhiteSpace(decisionReason))
            {
                error = "Fit evidence identity, status, timestamp, or decision reason is incomplete.";
                return false;
            }

            // Rejected evidence is still an auditable claim about a concrete capture and
            // target profile. Runtime cannot resolve AssetDatabase paths/hashes, but it
            // can require every recorded identity/state binding and validate the object
            // payloads themselves. The editor rechecks those paths/hashes before saving
            // and again before any future Apply.
            if (receiverCapture == null || targetProfile == null ||
                string.IsNullOrWhiteSpace(receiverCaptureAssetPath) ||
                string.IsNullOrWhiteSpace(receiverCaptureDependencyHash) ||
                string.IsNullOrWhiteSpace(targetProfileAssetPath) ||
                string.IsNullOrWhiteSpace(targetProfileDependencyHash) ||
                string.IsNullOrWhiteSpace(baselineStateHash) ||
                string.IsNullOrWhiteSpace(directOnlyStateHash) ||
                string.IsNullOrWhiteSpace(fullStateHash) ||
                !captureIntegrity.startPassed || !captureIntegrity.adminPassed || !captureIntegrity.receiverPassed ||
                !receiverCapture.TryValidate(out error) ||
                !string.Equals(receiverCapture.ReceiverRoomId, receiverRoomId, StringComparison.Ordinal) ||
                !string.Equals(receiverCapture.StableDoorwayId, stableDoorwayId, StringComparison.Ordinal) ||
                !string.Equals(receiverCapture.Baseline.stateHash, baselineStateHash, StringComparison.Ordinal) ||
                !string.Equals(receiverCapture.DirectOnly.stateHash, directOnlyStateHash, StringComparison.Ordinal) ||
                !string.Equals(receiverCapture.Full.stateHash, fullStateHash, StringComparison.Ordinal) ||
                !targetProfile.TryValidate(out error) ||
                !string.Equals(targetProfile.RoomId, receiverRoomId, StringComparison.Ordinal) ||
                !string.Equals(targetProfile.DoorwayId, stableDoorwayId, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(error))
                    error = "Fit evidence does not bind to a valid canonical capture/profile identity and three state hashes.";
                return false;
            }

            if (candidateSearch.candidates == null ||
                candidateSearch.candidates.Length != ExpectedCandidateCount ||
                candidateSearch.totalCandidateCount != ExpectedCandidateCount ||
                candidateSearch.pointCandidateCount != 0 || candidateSearch.autoK2CandidateCount != 0 ||
                !candidateSearch.spotOnlyPolicy || !candidateSearch.autoK2Disabled)
            {
                error = "K=1 candidate inventory is incomplete or permits Point/automatic-K2 fallback.";
                return false;
            }

            if (!IsFinite(candidateSearch.shCenterLocal))
            {
                error = "K=1 candidate inventory has a non-finite SH centre.";
                return false;
            }

            for (int i = 0; i < candidateSearch.candidates.Length; i++)
            {
                if (!TryValidateCandidate(candidateSearch.candidates[i], i, candidateSearch.shCenterLocal, out error))
                    return false;
            }

            if (!TryValidateSignalGates(signalGates, out error) ||
                !TryValidateSignalSampleCounts(receiverCapture, signalGates, out error) ||
                !TryValidateFitGates(fitGates, out error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(evidenceHash) ||
                !string.Equals(evidenceHash, ComputeEvidenceHash(BuildPayload()), StringComparison.Ordinal))
            {
                error = "Fit evidence hash is missing or no longer matches its serialized payload.";
                return false;
            }

            if (!IsAccepted)
            {
                if (candidateSearch.selectedCandidateIndex != -1)
                {
                    error = "Rejected K=1 evidence must not nominate a candidate for application.";
                    return false;
                }

                error = string.Empty;
                return true;
            }

            // This schema records target signal qualification and a deterministic
            // candidate inventory, but it has no immutable mask/candidate-render artifact
            // references (path/hash/dimensions/camera binding).  A boolean cannot prove
            // semantic leakage, overlap, or a rerender result.  Keep application closed
            // until a later artifact-bound schema explicitly adds that evidence.
            error = "K1_ACCEPTED is disabled for this evidence-only schema: persisted semantic and candidate-response artifacts are not bound by path/hash.";
            return false;
        }

        [Serializable]
        public struct FitEvidencePayload
        {
            public string receiverRoomId;
            public string stableDoorwayId;
            public string status;
            public string authoredUtcIso8601;
            public string fitToolVersion;
            public string unityVersion;
            public string metricDefinitionVersion;
            public DungeonPortalReceiverResponseCapture receiverCapture;
            public string receiverCaptureAssetPath;
            public string receiverCaptureDependencyHash;
            public string baselineStateHash;
            public string directOnlyStateHash;
            public string fullStateHash;
            public CaptureIntegrityEvidence captureIntegrity;
            public DungeonPortalEndpointProfile targetProfile;
            public string targetProfileAssetPath;
            public string targetProfileDependencyHash;
            public SignalGateEvidence signalGates;
            public SemanticMaskEvidence semanticMasks;
            public CandidateSearchEvidence candidateSearch;
            public FitGateEvidence fitGates;
            public K1BounceDescriptor acceptedDescriptor;
            public string decisionReason;
        }

        [Serializable]
        public struct CaptureIntegrityEvidence
        {
            public bool startPassed;
            public bool adminPassed;
            public bool receiverPassed;
            public string startFailure;
            public string adminFailure;
            public string receiverFailure;
        }

        [Serializable]
        public struct SignalGateEvidence
        {
            public float imageEpsilon;
            public float shL0Epsilon;
            /// <summary>Must be <see cref="PixelCoverageMetric"/> for image domains.</summary>
            public string pixelCoverageMetric;
            /// <summary>Must be <see cref="RgbEnergyMetric"/> for image domains.</summary>
            public string rgbEnergyMetric;
            /// <summary>Must be <see cref="ProbeL0Metric"/>; signs are meaningful only for L0/DC.</summary>
            public string probeL0Metric;
            /// <summary>Documents that non-DC SH is signal-only, never a signed-energy gate.</summary>
            public string probeNonDcMetric;
            public SignalDomainEvidence lightmaps;
            public SignalDomainEvidence hdr;
            public ProbeSignalEvidence probes;
            public bool allRequiredPassed;
            public string failureReason;
        }

        [Serializable]
        public struct SignalDomainEvidence
        {
            public bool evaluated;
            public bool readable;
            public int sampleCount;
            public int directPositiveSampleCount;
            public int indirectPositiveSampleCount;
            public int significantNegativeSampleCount;
            public float directPositiveCoverage;
            public float indirectPositiveCoverage;
            public float significantNegativeFraction;
            public float negativeToPositiveEnergy;
            public float indirectToDirectEnergy;
            public float maxAbsoluteChannel;
            public bool finite;
            public bool passed;
            public string failureReason;
        }

        [Serializable]
        public struct ProbeSignalEvidence
        {
            public bool evaluated;
            public int probeCount;
            public int directPositiveProbeCount;
            public int indirectPositiveProbeCount;
            public int significantNegativeProbeCount;
            public float directPositiveCoverage;
            public float indirectPositiveCoverage;
            public float significantNegativeFraction;
            public float negativeToPositiveEnergy;
            public float indirectToDirectEnergy;
            /// <summary>Maximum absolute component of coefficient 0 (the L0/DC gate domain).</summary>
            public float maxAbsoluteCoefficient;
            /// <summary>Non-DC response diagnostics. These never participate in signed negative gates.</summary>
            public float indirectL2Magnitude;
            public float indirectNonDcRms;
            public float maxAbsoluteNonDcCoefficient;
            public bool finite;
            public bool passed;
            public int[] topIndirectL0ProbeIndices;
            public float[] topIndirectL0Magnitudes;
            public string failureReason;
        }

        [Serializable]
        public struct SemanticMaskEvidence
        {
            public bool objectIdMasksAvailable;
            public bool receiverRegionMaskAvailable;
            public bool forbiddenRegionMaskAvailable;
            public bool sourceSideMaskAvailable;
            public bool fixedCameraRegionMaskAvailable;
            public string policy;
            public string failureReason;
        }

        [Serializable]
        public struct CandidateSearchEvidence
        {
            public int totalCandidateCount;
            public int pointCandidateCount;
            public int autoK2CandidateCount;
            public bool spotOnlyPolicy;
            public bool autoK2Disabled;
            public bool overlapEvidenceAvailable;
            public bool sourceSideEvidenceAvailable;
            public Vector3 shCenterLocal;
            public int selectedCandidateIndex;
            public K1CandidateEvidence[] candidates;
        }

        [Serializable]
        public struct K1CandidateEvidence
        {
            public int candidateIndex;
            public LightType type;
            public CandidateAxisMode axisMode;
            public Vector3 localPosition;
            public Vector3 localAxis;
            public Vector3 localEulerAngles;
            /// <summary>Deterministic range before applying <see cref="rangeFactor"/>.</summary>
            public float baseRange;
            public float range;
            public float rangeFactor;
            public float outerSpotAngle;
            public float innerSpotAngle;
            public float angleToInwardDegrees;
            public float halfConeAngleDegrees;
            public bool receiverInteriorGatePassed;
            public bool inwardHalfSpaceGatePassed;
            public bool overlapGateEvaluated;
            public bool overlapGatePassed;
            public bool sourceSideGateEvaluated;
            public bool sourceSideGatePassed;
            public bool comparableResponseAvailable;
            public bool eligibleForAcceptance;
            [ColorUsage(true, true)] public Color fittedRgbGain;
            public string rejectionReason;
        }

        public enum CandidateAxisMode
        {
            Inward = 0,
            TowardShCenter = 1
        }

        private static readonly float[] CandidateXs = { -0.4f, 0f, 0.4f };
        private static readonly float[] CandidateYs = { 0.5f, 1f, 1.5f };
        private static readonly float[] CandidateDepths = { 0.25f, 0.75f, 1.5f };
        private static readonly float[] CandidateOuterAngles = { 80f, 120f, 160f };
        private static readonly float[] CandidateRangeFactors = { 1.05f, 1.5f };

        private struct RequiredFitGate
        {
            public readonly string name;
            public readonly bool hasMinimum;
            public readonly bool hasMaximum;
            public readonly float minimumInclusive;
            public readonly float maximumInclusive;

            public RequiredFitGate(
                string name,
                bool hasMinimum,
                float minimumInclusive,
                bool hasMaximum,
                float maximumInclusive)
            {
                this.name = name;
                this.hasMinimum = hasMinimum;
                this.hasMaximum = hasMaximum;
                this.minimumInclusive = minimumInclusive;
                this.maximumInclusive = maximumInclusive;
            }
        }

        private static readonly RequiredFitGate[] RequiredFitGates =
        {
            new RequiredFitGate("drift", false, 0f, true, 0.05f),
            new RequiredFitGate("sh_nrmse_overall", false, 0f, true, 0.35f),
            new RequiredFitGate("sh_nrmse_slab", false, 0f, true, 0.45f),
            new RequiredFitGate("hdr_nrmse_overall", false, 0f, true, 0.20f),
            new RequiredFitGate("hdr_nrmse_c0", false, 0f, true, 0.25f),
            new RequiredFitGate("hdr_nrmse_c1", false, 0f, true, 0.25f),
            new RequiredFitGate("hdr_nrmse_c2", false, 0f, true, 0.25f),
            new RequiredFitGate("hdr_nrmse_c3", false, 0f, true, 0.25f),
            new RequiredFitGate("semantic", false, 0f, true, 0.30f),
            new RequiredFitGate("energy_ratio", true, 0.85f, true, 1.15f),
            new RequiredFitGate("p99_ratio", false, 0f, true, 1.35f),
            new RequiredFitGate("dark_off_target", false, 0f, true, 0.02f),
            new RequiredFitGate("forbidden_energy", false, 0f, true, 0.005f),
            new RequiredFitGate("forbidden_p99", false, 0f, true, 0.02f),
            new RequiredFitGate("gain_r", true, 0f, true, 4f),
            new RequiredFitGate("gain_g", true, 0f, true, 4f),
            new RequiredFitGate("gain_b", true, 0f, true, 4f)
        };

        [Serializable]
        public struct FitGateEvidence
        {
            public bool semanticMasksAvailable;
            public bool candidateResponseEvidenceAvailable;
            public bool allRequiredPassed;
            public FitGate[] gates;
        }

        [Serializable]
        public struct FitGate
        {
            public string name;
            public bool evaluated;
            public bool hasMinimum;
            public bool hasMaximum;
            public float minimumInclusive;
            public float maximumInclusive;
            public float value;
            public bool passed;
            public string failureReason;
        }

        [Serializable]
        public struct K1BounceDescriptor
        {
            public LightType type;
            public Vector3 localPosition;
            public Vector3 localEulerAngles;
            [ColorUsage(true, true)] public Color rgbGain;
            public float range;
            public float outerSpotAngle;
            public float innerSpotAngle;
            public bool castShadows;
            public float shadowStrength;
            public int cullingMask;
            public int renderingLayerMask;
        }

        private FitEvidencePayload BuildPayload()
        {
            return new FitEvidencePayload
            {
                receiverRoomId = receiverRoomId,
                stableDoorwayId = stableDoorwayId,
                status = status,
                authoredUtcIso8601 = authoredUtcIso8601,
                fitToolVersion = fitToolVersion,
                unityVersion = unityVersion,
                metricDefinitionVersion = metricDefinitionVersion,
                receiverCapture = receiverCapture,
                receiverCaptureAssetPath = receiverCaptureAssetPath,
                receiverCaptureDependencyHash = receiverCaptureDependencyHash,
                baselineStateHash = baselineStateHash,
                directOnlyStateHash = directOnlyStateHash,
                fullStateHash = fullStateHash,
                captureIntegrity = captureIntegrity,
                targetProfile = targetProfile,
                targetProfileAssetPath = targetProfileAssetPath,
                targetProfileDependencyHash = targetProfileDependencyHash,
                signalGates = signalGates,
                semanticMasks = semanticMasks,
                candidateSearch = candidateSearch,
                fitGates = fitGates,
                acceptedDescriptor = acceptedDescriptor,
                decisionReason = decisionReason
            };
        }

        public static string ComputeEvidenceHash(FitEvidencePayload payload)
        {
            var builder = new StringBuilder(32768);
            AppendString(builder, payload.receiverRoomId);
            AppendString(builder, payload.stableDoorwayId);
            AppendString(builder, payload.status);
            AppendString(builder, payload.authoredUtcIso8601);
            AppendString(builder, payload.fitToolVersion);
            AppendString(builder, payload.unityVersion);
            AppendString(builder, payload.metricDefinitionVersion);
            AppendString(builder, payload.receiverCapture != null ? payload.receiverCapture.ReceiverRoomId : string.Empty);
            AppendString(builder, payload.receiverCapture != null ? payload.receiverCapture.StableDoorwayId : string.Empty);
            AppendString(builder, payload.receiverCaptureAssetPath);
            AppendString(builder, payload.receiverCaptureDependencyHash);
            AppendString(builder, payload.baselineStateHash);
            AppendString(builder, payload.directOnlyStateHash);
            AppendString(builder, payload.fullStateHash);
            AppendCaptureIntegrity(builder, payload.captureIntegrity);
            AppendString(builder, payload.targetProfile != null ? payload.targetProfile.RoomId : string.Empty);
            AppendString(builder, payload.targetProfile != null ? payload.targetProfile.DoorwayId : string.Empty);
            AppendString(builder, payload.targetProfileAssetPath);
            AppendString(builder, payload.targetProfileDependencyHash);
            AppendSignalGates(builder, payload.signalGates);
            AppendSemanticMasks(builder, payload.semanticMasks);
            AppendCandidateSearch(builder, payload.candidateSearch);
            AppendFitGates(builder, payload.fitGates);
            AppendDescriptor(builder, payload.acceptedDescriptor);
            AppendString(builder, payload.decisionReason);
            return Hash128.Compute(builder.ToString()).ToString();
        }

        private static bool TryGetCanonicalCandidate(
            int index,
            Vector3 shCenterLocal,
            out Vector3 localPosition,
            out CandidateAxisMode axisMode,
            out Vector3 localAxis,
            out float outerSpotAngle,
            out float innerSpotAngle,
            out float rangeFactor,
            out string error)
        {
            localPosition = default;
            axisMode = CandidateAxisMode.Inward;
            localAxis = default;
            outerSpotAngle = 0f;
            innerSpotAngle = 0f;
            rangeFactor = 0f;

            if (index < 0 || index >= ExpectedCandidateCount || !IsFinite(shCenterLocal))
            {
                error = "K=1 candidate index or SH centre is outside the canonical grid.";
                return false;
            }

            int remainder = index;
            int xIndex = remainder / 216;
            remainder %= 216;
            int yIndex = remainder / 72;
            remainder %= 72;
            int depthIndex = remainder / 24;
            remainder %= 24;
            int axisIndex = remainder / 12;
            remainder %= 12;
            int outerIndex = remainder / 4;
            remainder %= 4;
            int innerIndex = remainder / 2;
            int factorIndex = remainder % 2;

            localPosition = new Vector3(
                CandidateXs[xIndex],
                CandidateYs[yIndex],
                -CandidateDepths[depthIndex]);
            axisMode = axisIndex == 0 ? CandidateAxisMode.Inward : CandidateAxisMode.TowardShCenter;
            localAxis = axisMode == CandidateAxisMode.Inward
                ? Vector3.back
                : shCenterLocal - localPosition;
            if (localAxis.sqrMagnitude <= 0.000001f)
            {
                error = "Canonical K=1 SH-centre axis is undefined at candidate " + index + ".";
                return false;
            }

            localAxis.Normalize();
            outerSpotAngle = CandidateOuterAngles[outerIndex];
            innerSpotAngle = innerIndex == 0 ? 0f : outerSpotAngle * 0.5f;
            rangeFactor = CandidateRangeFactors[factorIndex];
            error = string.Empty;
            return true;
        }

        private static bool TryValidateCandidate(
            K1CandidateEvidence candidate,
            int expectedIndex,
            Vector3 shCenterLocal,
            out string error)
        {
            if (!TryGetCanonicalCandidate(
                    expectedIndex,
                    shCenterLocal,
                    out Vector3 expectedPosition,
                    out CandidateAxisMode expectedAxisMode,
                    out Vector3 expectedAxis,
                    out float expectedOuter,
                    out float expectedInner,
                    out float expectedRangeFactor,
                    out error))
            {
                return false;
            }

            Vector3 eulerAxis = Quaternion.Euler(candidate.localEulerAngles) * Vector3.forward;
            if (candidate.candidateIndex != expectedIndex || candidate.type != LightType.Spot ||
                candidate.axisMode != expectedAxisMode || !Approximately(candidate.localPosition, expectedPosition) ||
                !IsFinite(candidate.localPosition) || !IsFinite(candidate.localAxis) ||
                candidate.localAxis.sqrMagnitude < 0.9998f || candidate.localAxis.sqrMagnitude > 1.0002f ||
                Vector3.Dot(candidate.localAxis.normalized, expectedAxis) < 0.9999f ||
                !IsFinite(candidate.localEulerAngles) ||
                Vector3.Dot(eulerAxis.normalized, candidate.localAxis.normalized) < 0.9999f ||
                !IsFinitePositive(candidate.baseRange) || !IsFinitePositive(candidate.range) ||
                !IsFinitePositive(candidate.rangeFactor) ||
                !Approximately(candidate.range, candidate.baseRange * candidate.rangeFactor) ||
                !Approximately(candidate.rangeFactor, expectedRangeFactor) ||
                !Approximately(candidate.outerSpotAngle, expectedOuter) ||
                !Approximately(candidate.innerSpotAngle, expectedInner) ||
                !IsFiniteNonNegative(candidate.angleToInwardDegrees) ||
                !IsFiniteNonNegative(candidate.halfConeAngleDegrees) ||
                !IsFiniteNonNegative(candidate.fittedRgbGain) ||
                candidate.fittedRgbGain.r > 4f || candidate.fittedRgbGain.g > 4f ||
                candidate.fittedRgbGain.b > 4f)
            {
                error = "K=1 candidate " + expectedIndex + " has invalid Spot geometry or gain.";
                return false;
            }

            float derivedAngleToInward = Vector3.Angle(candidate.localAxis, Vector3.back);
            float derivedHalfCone = candidate.outerSpotAngle * 0.5f;
            bool derivedInwardPass = derivedAngleToInward + derivedHalfCone <= 85.0001f;
            if (!Approximately(candidate.angleToInwardDegrees, derivedAngleToInward) ||
                !Approximately(candidate.halfConeAngleDegrees, derivedHalfCone) ||
                candidate.inwardHalfSpaceGatePassed != derivedInwardPass ||
                !candidate.receiverInteriorGatePassed)
            {
                error = "K=1 candidate " + expectedIndex + " has non-derived receiver-inward hard-gate evidence.";
                return false;
            }

            if ((candidate.overlapGatePassed && !candidate.overlapGateEvaluated) ||
                (candidate.sourceSideGatePassed && !candidate.sourceSideGateEvaluated))
            {
                error = "K=1 candidate " + expectedIndex + " reports a hard gate as passed without evaluating it.";
                return false;
            }

            if (candidate.eligibleForAcceptance &&
                (!candidate.receiverInteriorGatePassed || !candidate.inwardHalfSpaceGatePassed ||
                 !candidate.overlapGateEvaluated || !candidate.overlapGatePassed ||
                 !candidate.sourceSideGateEvaluated || !candidate.sourceSideGatePassed ||
                 !candidate.comparableResponseAvailable))
            {
                error = "K=1 candidate " + expectedIndex + " is eligible without all hard-gate evidence.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidateSignalGates(SignalGateEvidence value, out string error)
        {
            error = string.Empty;
            if (!Approximately(value.imageEpsilon, 0.002f) || !Approximately(value.shL0Epsilon, 0.0005f) ||
                !string.Equals(value.pixelCoverageMetric, PixelCoverageMetric, StringComparison.Ordinal) ||
                !string.Equals(value.rgbEnergyMetric, RgbEnergyMetric, StringComparison.Ordinal) ||
                !string.Equals(value.probeL0Metric, ProbeL0Metric, StringComparison.Ordinal) ||
                !string.Equals(value.probeNonDcMetric, ProbeNonDcMetric, StringComparison.Ordinal) ||
                !TryValidateSignalDomain(value.lightmaps, "rawStoredLightmapRgbSignal", out error) ||
                !TryValidateSignalDomain(value.hdr, "fixedCameraLinearHdr", out error) ||
                !TryValidateProbeSignal(value.probes, out error))
            {
                if (string.IsNullOrWhiteSpace(error))
                    error = "Signal metric definitions are incomplete or non-canonical.";
                return false;
            }

            bool expected = value.lightmaps.passed && value.hdr.passed && value.probes.passed;
            if (value.allRequiredPassed != expected)
            {
                error = "Signal allRequiredPassed is not derived from its independently validated domains.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidateSignalSampleCounts(
            DungeonPortalReceiverResponseCapture capture,
            SignalGateEvidence value,
            out string error)
        {
            error = string.Empty;
            DungeonPortalReceiverResponseCapture.CaptureState baseline = capture.Baseline;
            DungeonPortalReceiverResponseCapture.CaptureLightmap[] lightmaps =
                baseline.lightmaps ?? Array.Empty<DungeonPortalReceiverResponseCapture.CaptureLightmap>();
            DungeonPortalReceiverResponseCapture.FixedCameraCapture[] cameras =
                baseline.fixedCameraCaptures ?? Array.Empty<DungeonPortalReceiverResponseCapture.FixedCameraCapture>();
            DungeonPortalReceiverResponseCapture.ProbeSample[] probes =
                baseline.probes ?? Array.Empty<DungeonPortalReceiverResponseCapture.ProbeSample>();

            long expectedLightmapSamples = 0;
            for (int i = 0; i < lightmaps.Length; i++)
            {
                if (lightmaps[i].colorWidth <= 0 || lightmaps[i].colorHeight <= 0)
                {
                    error = "Bound capture has an invalid lightmap signal dimension.";
                    return false;
                }

                expectedLightmapSamples += (long)lightmaps[i].colorWidth * lightmaps[i].colorHeight;
            }

            long expectedHdrSamples = 0;
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i].width <= 0 || cameras[i].height <= 0)
                {
                    error = "Bound capture has an invalid fixed-camera signal dimension.";
                    return false;
                }

                expectedHdrSamples += (long)cameras[i].width * cameras[i].height;
            }

            if (expectedLightmapSamples <= 0 || expectedLightmapSamples > int.MaxValue ||
                expectedHdrSamples <= 0 || expectedHdrSamples > int.MaxValue || probes.Length != 27 ||
                !value.lightmaps.evaluated || !value.lightmaps.readable || !value.lightmaps.finite ||
                value.lightmaps.sampleCount != (int)expectedLightmapSamples ||
                !value.hdr.evaluated || !value.hdr.readable || !value.hdr.finite ||
                value.hdr.sampleCount != (int)expectedHdrSamples ||
                !value.probes.evaluated || !value.probes.finite || value.probes.probeCount != probes.Length)
            {
                error = "Signal evidence was not fully evaluated against the exact bound lightmap, HDR-camera, and 27-probe sample counts.";
                return false;
            }

            return true;
        }

        private static bool TryValidateSignalDomain(
            SignalDomainEvidence value,
            string domainName,
            out string error)
        {
            if (!value.evaluated)
            {
                if (value.passed)
                {
                    error = domainName + " reports a pass without evaluation.";
                    return false;
                }

                error = string.Empty;
                return true;
            }

            if (!value.readable || value.sampleCount <= 0 ||
                value.directPositiveSampleCount < 0 || value.directPositiveSampleCount > value.sampleCount ||
                value.indirectPositiveSampleCount < 0 || value.indirectPositiveSampleCount > value.sampleCount ||
                value.significantNegativeSampleCount < 0 || value.significantNegativeSampleCount > value.sampleCount ||
                !IsFiniteNonNegative(value.directPositiveCoverage) ||
                !IsFiniteNonNegative(value.indirectPositiveCoverage) ||
                !IsFiniteNonNegative(value.significantNegativeFraction) ||
                !IsFiniteNonNegative(value.negativeToPositiveEnergy) ||
                !IsFiniteNonNegative(value.indirectToDirectEnergy) ||
                !IsFiniteNonNegative(value.maxAbsoluteChannel) ||
                value.directPositiveCoverage > 1f || value.indirectPositiveCoverage > 1f ||
                value.significantNegativeFraction > 1f ||
                !Approximately(value.directPositiveCoverage, (float)value.directPositiveSampleCount / value.sampleCount) ||
                !Approximately(value.indirectPositiveCoverage, (float)value.indirectPositiveSampleCount / value.sampleCount) ||
                !Approximately(value.significantNegativeFraction, (float)value.significantNegativeSampleCount / value.sampleCount))
            {
                error = domainName + " signal metrics are malformed.";
                return false;
            }

            bool expectedPass = value.finite && value.maxAbsoluteChannel < 64f &&
                                value.directPositiveCoverage >= 0.01f &&
                                value.indirectPositiveCoverage >= 0.0025f &&
                                value.significantNegativeFraction <= 0.01f &&
                                value.negativeToPositiveEnergy <= 0.05f &&
                                value.indirectToDirectEnergy <= 4f;
            if (value.passed != expectedPass)
            {
                error = domainName + " pass flag is not derived from the canonical signal thresholds.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidateProbeSignal(ProbeSignalEvidence value, out string error)
        {
            if (!value.evaluated)
            {
                if (value.passed)
                {
                    error = "Probe signal reports a pass without evaluation.";
                    return false;
                }

                error = string.Empty;
                return true;
            }

            int[] ranks = value.topIndirectL0ProbeIndices ?? Array.Empty<int>();
            float[] rankMagnitudes = value.topIndirectL0Magnitudes ?? Array.Empty<float>();
            if (value.probeCount <= 0 || value.directPositiveProbeCount < 0 ||
                value.directPositiveProbeCount > value.probeCount || value.indirectPositiveProbeCount < 0 ||
                value.indirectPositiveProbeCount > value.probeCount ||
                value.significantNegativeProbeCount < 0 || value.significantNegativeProbeCount > value.probeCount ||
                !IsFiniteNonNegative(value.directPositiveCoverage) ||
                !IsFiniteNonNegative(value.indirectPositiveCoverage) ||
                !IsFiniteNonNegative(value.significantNegativeFraction) ||
                !IsFiniteNonNegative(value.negativeToPositiveEnergy) ||
                !IsFiniteNonNegative(value.indirectToDirectEnergy) ||
                !IsFiniteNonNegative(value.maxAbsoluteCoefficient) ||
                !IsFiniteNonNegative(value.indirectL2Magnitude) ||
                !IsFiniteNonNegative(value.indirectNonDcRms) ||
                !IsFiniteNonNegative(value.maxAbsoluteNonDcCoefficient) ||
                value.directPositiveCoverage > 1f || value.indirectPositiveCoverage > 1f ||
                value.significantNegativeFraction > 1f ||
                !Approximately(value.directPositiveCoverage, (float)value.directPositiveProbeCount / value.probeCount) ||
                !Approximately(value.indirectPositiveCoverage, (float)value.indirectPositiveProbeCount / value.probeCount) ||
                !Approximately(value.significantNegativeFraction, (float)value.significantNegativeProbeCount / value.probeCount) ||
                ranks.Length != Math.Min(8, value.probeCount) || rankMagnitudes.Length != ranks.Length)
            {
                error = "Probe L0/DC or non-DC diagnostic metrics are malformed.";
                return false;
            }

            for (int i = 0; i < ranks.Length; i++)
            {
                if (ranks[i] < 0 || ranks[i] >= value.probeCount || !IsFiniteNonNegative(rankMagnitudes[i]) ||
                    (i > 0 && rankMagnitudes[i] > rankMagnitudes[i - 1] + 0.000001f))
                {
                    error = "Probe top-L0 rank evidence is invalid.";
                    return false;
                }

                for (int other = 0; other < i; other++)
                {
                    if (ranks[other] == ranks[i])
                    {
                        error = "Probe top-L0 rank evidence has duplicate probe indices.";
                        return false;
                    }
                }
            }

            bool expectedPass = value.finite && value.maxAbsoluteCoefficient < 64f &&
                                value.maxAbsoluteNonDcCoefficient < 64f &&
                                value.directPositiveCoverage >= 0.01f &&
                                value.indirectPositiveCoverage >= 0.0025f &&
                                value.indirectPositiveProbeCount >= 3 &&
                                value.significantNegativeFraction <= 0.01f &&
                                value.negativeToPositiveEnergy <= 0.05f &&
                                value.indirectToDirectEnergy <= 4f;
            if (value.passed != expectedPass)
            {
                error = "Probe pass flag is not derived from L0/DC-only sign thresholds.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidateFitGates(FitGateEvidence value, out string error)
        {
            RequiredFitGate[] required = RequiredFitGates;
            FitGate[] actual = value.gates ?? Array.Empty<FitGate>();
            if (actual.Length != required.Length)
            {
                error = "Fit-gate inventory is incomplete or contains unexpected entries.";
                return false;
            }

            bool expectedAllPassed = value.semanticMasksAvailable && value.candidateResponseEvidenceAvailable;
            for (int i = 0; i < required.Length; i++)
            {
                FitGate gate = actual[i];
                RequiredFitGate requirement = required[i];
                if (!string.Equals(gate.name, requirement.name, StringComparison.Ordinal) ||
                    gate.hasMinimum != requirement.hasMinimum || gate.hasMaximum != requirement.hasMaximum ||
                    !Approximately(gate.minimumInclusive, requirement.minimumInclusive) ||
                    !Approximately(gate.maximumInclusive, requirement.maximumInclusive) ||
                    !IsFinite(gate.value))
                {
                    error = "Fit-gate '" + requirement.name + "' is not the canonical threshold definition.";
                    return false;
                }

                bool numericPass = (!gate.hasMinimum || gate.value >= gate.minimumInclusive) &&
                                   (!gate.hasMaximum || gate.value <= gate.maximumInclusive);
                if (gate.evaluated)
                {
                    if (gate.passed != numericPass)
                    {
                        error = "Fit-gate '" + requirement.name + "' pass flag is not derived from its numeric value.";
                        return false;
                    }
                }
                else if (gate.passed)
                {
                    error = "Fit-gate '" + requirement.name + "' passes without a comparable candidate evaluation.";
                    return false;
                }

                expectedAllPassed &= gate.evaluated && gate.passed;
            }

            if (value.allRequiredPassed != expectedAllPassed)
            {
                error = "Fit-gate aggregate pass is not independently derived.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidateDescriptor(K1BounceDescriptor descriptor, out string error)
        {
            Vector3 inward = Vector3.back;
            Vector3 axis = Quaternion.Euler(descriptor.localEulerAngles) * Vector3.forward;
            float halfCone = descriptor.outerSpotAngle * 0.5f;
            if (descriptor.type != LightType.Spot || !IsFinite(descriptor.localPosition) ||
                descriptor.localPosition.z > -0.05f || !IsFinite(descriptor.localEulerAngles) ||
                !IsFiniteNonNegative(descriptor.rgbGain) || descriptor.rgbGain.r > 4f ||
                descriptor.rgbGain.g > 4f || descriptor.rgbGain.b > 4f ||
                Mathf.Max(descriptor.rgbGain.r, descriptor.rgbGain.g, descriptor.rgbGain.b) <= 0f ||
                !IsFinite(descriptor.range) || descriptor.range < 0.01f || !IsFinite(descriptor.outerSpotAngle) ||
                descriptor.outerSpotAngle < 1f || descriptor.outerSpotAngle > 179f ||
                !IsFinite(descriptor.innerSpotAngle) || descriptor.innerSpotAngle < 0f ||
                descriptor.innerSpotAngle > descriptor.outerSpotAngle || !descriptor.castShadows ||
                !IsFinite(descriptor.shadowStrength) || descriptor.shadowStrength <= 0f ||
                descriptor.shadowStrength > 1f || descriptor.cullingMask != 66177 ||
                descriptor.renderingLayerMask != 2 ||
                Vector3.Angle(axis, inward) + halfCone > 85.0001f)
            {
                error = "Accepted K=1 descriptor violates the receiver-inward Spot safety contract.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static CandidateSearchEvidence CloneCandidateSearch(CandidateSearchEvidence value)
        {
            value.candidates = CloneArray(value.candidates);
            return value;
        }

        private static SignalGateEvidence CloneSignalGates(SignalGateEvidence value)
        {
            value.probes.topIndirectL0ProbeIndices = CloneArray(value.probes.topIndirectL0ProbeIndices);
            value.probes.topIndirectL0Magnitudes = CloneArray(value.probes.topIndirectL0Magnitudes);
            return value;
        }

        private static FitGateEvidence CloneFitGates(FitGateEvidence value)
        {
            value.gates = CloneArray(value.gates);
            return value;
        }

        private static T[] CloneArray<T>(T[] value)
        {
            return value != null ? (T[])value.Clone() : Array.Empty<T>();
        }

        private static bool IsCanonicalReceiver(string value)
        {
            return string.Equals(value, "StartRoom_R000", StringComparison.Ordinal) ||
                   string.Equals(value, "AdminstrativeSegregation_R000", StringComparison.Ordinal);
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

        private static bool IsFinitePositive(float value)
        {
            return IsFinite(value) && value > 0f;
        }

        private static bool IsFiniteNonNegative(Color value)
        {
            return IsFiniteNonNegative(value.r) && IsFiniteNonNegative(value.g) &&
                   IsFiniteNonNegative(value.b) && IsFinite(value.a);
        }

        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return (left - right).sqrMagnitude <= 0.00000001f;
        }

        private static bool Approximately(float left, float right)
        {
            return IsFinite(left) && IsFinite(right) && Mathf.Abs(left - right) <= 0.0001f;
        }

        private static bool Approximately(Color left, Color right)
        {
            return Mathf.Abs(left.r - right.r) <= 0.00001f &&
                   Mathf.Abs(left.g - right.g) <= 0.00001f &&
                   Mathf.Abs(left.b - right.b) <= 0.00001f &&
                   Mathf.Abs(left.a - right.a) <= 0.00001f;
        }

        private static void AppendCaptureIntegrity(StringBuilder builder, CaptureIntegrityEvidence value)
        {
            AppendBool(builder, value.startPassed);
            AppendBool(builder, value.adminPassed);
            AppendBool(builder, value.receiverPassed);
            AppendString(builder, value.startFailure);
            AppendString(builder, value.adminFailure);
            AppendString(builder, value.receiverFailure);
        }

        private static void AppendSignalGates(StringBuilder builder, SignalGateEvidence value)
        {
            AppendFloat(builder, value.imageEpsilon);
            AppendFloat(builder, value.shL0Epsilon);
            AppendString(builder, value.pixelCoverageMetric);
            AppendString(builder, value.rgbEnergyMetric);
            AppendString(builder, value.probeL0Metric);
            AppendString(builder, value.probeNonDcMetric);
            AppendSignalDomain(builder, value.lightmaps);
            AppendSignalDomain(builder, value.hdr);
            AppendProbeSignal(builder, value.probes);
            AppendBool(builder, value.allRequiredPassed);
            AppendString(builder, value.failureReason);
        }

        private static void AppendSignalDomain(StringBuilder builder, SignalDomainEvidence value)
        {
            AppendBool(builder, value.evaluated);
            AppendBool(builder, value.readable);
            builder.Append(value.sampleCount).Append('|');
            builder.Append(value.directPositiveSampleCount).Append('|');
            builder.Append(value.indirectPositiveSampleCount).Append('|');
            builder.Append(value.significantNegativeSampleCount).Append('|');
            AppendFloat(builder, value.directPositiveCoverage);
            AppendFloat(builder, value.indirectPositiveCoverage);
            AppendFloat(builder, value.significantNegativeFraction);
            AppendFloat(builder, value.negativeToPositiveEnergy);
            AppendFloat(builder, value.indirectToDirectEnergy);
            AppendFloat(builder, value.maxAbsoluteChannel);
            AppendBool(builder, value.finite);
            AppendBool(builder, value.passed);
            AppendString(builder, value.failureReason);
        }

        private static void AppendProbeSignal(StringBuilder builder, ProbeSignalEvidence value)
        {
            AppendBool(builder, value.evaluated);
            builder.Append(value.probeCount).Append('|');
            builder.Append(value.directPositiveProbeCount).Append('|');
            builder.Append(value.indirectPositiveProbeCount).Append('|');
            builder.Append(value.significantNegativeProbeCount).Append('|');
            AppendFloat(builder, value.directPositiveCoverage);
            AppendFloat(builder, value.indirectPositiveCoverage);
            AppendFloat(builder, value.significantNegativeFraction);
            AppendFloat(builder, value.negativeToPositiveEnergy);
            AppendFloat(builder, value.indirectToDirectEnergy);
            AppendFloat(builder, value.maxAbsoluteCoefficient);
            AppendFloat(builder, value.indirectL2Magnitude);
            AppendFloat(builder, value.indirectNonDcRms);
            AppendFloat(builder, value.maxAbsoluteNonDcCoefficient);
            AppendBool(builder, value.finite);
            AppendBool(builder, value.passed);
            int[] ranks = value.topIndirectL0ProbeIndices ?? Array.Empty<int>();
            builder.Append(ranks.Length).Append('|');
            for (int i = 0; i < ranks.Length; i++)
                builder.Append(ranks[i]).Append('|');
            float[] rankMagnitudes = value.topIndirectL0Magnitudes ?? Array.Empty<float>();
            builder.Append(rankMagnitudes.Length).Append('|');
            for (int i = 0; i < rankMagnitudes.Length; i++)
                AppendFloat(builder, rankMagnitudes[i]);
            AppendString(builder, value.failureReason);
        }

        private static void AppendSemanticMasks(StringBuilder builder, SemanticMaskEvidence value)
        {
            AppendBool(builder, value.objectIdMasksAvailable);
            AppendBool(builder, value.receiverRegionMaskAvailable);
            AppendBool(builder, value.forbiddenRegionMaskAvailable);
            AppendBool(builder, value.sourceSideMaskAvailable);
            AppendBool(builder, value.fixedCameraRegionMaskAvailable);
            AppendString(builder, value.policy);
            AppendString(builder, value.failureReason);
        }

        private static void AppendCandidateSearch(StringBuilder builder, CandidateSearchEvidence value)
        {
            builder.Append(value.totalCandidateCount).Append('|');
            builder.Append(value.pointCandidateCount).Append('|');
            builder.Append(value.autoK2CandidateCount).Append('|');
            AppendBool(builder, value.spotOnlyPolicy);
            AppendBool(builder, value.autoK2Disabled);
            AppendBool(builder, value.overlapEvidenceAvailable);
            AppendBool(builder, value.sourceSideEvidenceAvailable);
            AppendVector3(builder, value.shCenterLocal);
            builder.Append(value.selectedCandidateIndex).Append('|');
            K1CandidateEvidence[] candidates = value.candidates ?? Array.Empty<K1CandidateEvidence>();
            builder.Append(candidates.Length).Append('|');
            for (int i = 0; i < candidates.Length; i++)
                AppendCandidate(builder, candidates[i]);
        }

        private static void AppendCandidate(StringBuilder builder, K1CandidateEvidence value)
        {
            builder.Append(value.candidateIndex).Append('|').Append((int)value.type).Append('|');
            builder.Append((int)value.axisMode).Append('|');
            AppendVector3(builder, value.localPosition);
            AppendVector3(builder, value.localAxis);
            AppendVector3(builder, value.localEulerAngles);
            AppendFloat(builder, value.baseRange);
            AppendFloat(builder, value.range);
            AppendFloat(builder, value.rangeFactor);
            AppendFloat(builder, value.outerSpotAngle);
            AppendFloat(builder, value.innerSpotAngle);
            AppendFloat(builder, value.angleToInwardDegrees);
            AppendFloat(builder, value.halfConeAngleDegrees);
            AppendBool(builder, value.receiverInteriorGatePassed);
            AppendBool(builder, value.inwardHalfSpaceGatePassed);
            AppendBool(builder, value.overlapGateEvaluated);
            AppendBool(builder, value.overlapGatePassed);
            AppendBool(builder, value.sourceSideGateEvaluated);
            AppendBool(builder, value.sourceSideGatePassed);
            AppendBool(builder, value.comparableResponseAvailable);
            AppendBool(builder, value.eligibleForAcceptance);
            AppendColor(builder, value.fittedRgbGain);
            AppendString(builder, value.rejectionReason);
        }

        private static void AppendFitGates(StringBuilder builder, FitGateEvidence value)
        {
            AppendBool(builder, value.semanticMasksAvailable);
            AppendBool(builder, value.candidateResponseEvidenceAvailable);
            AppendBool(builder, value.allRequiredPassed);
            FitGate[] gates = value.gates ?? Array.Empty<FitGate>();
            builder.Append(gates.Length).Append('|');
            for (int i = 0; i < gates.Length; i++)
            {
                FitGate gate = gates[i];
                AppendString(builder, gate.name);
                AppendBool(builder, gate.evaluated);
                AppendBool(builder, gate.hasMinimum);
                AppendBool(builder, gate.hasMaximum);
                AppendFloat(builder, gate.minimumInclusive);
                AppendFloat(builder, gate.maximumInclusive);
                AppendFloat(builder, gate.value);
                AppendBool(builder, gate.passed);
                AppendString(builder, gate.failureReason);
            }
        }

        private static void AppendDescriptor(StringBuilder builder, K1BounceDescriptor value)
        {
            builder.Append((int)value.type).Append('|');
            AppendVector3(builder, value.localPosition);
            AppendVector3(builder, value.localEulerAngles);
            AppendColor(builder, value.rgbGain);
            AppendFloat(builder, value.range);
            AppendFloat(builder, value.outerSpotAngle);
            AppendFloat(builder, value.innerSpotAngle);
            AppendBool(builder, value.castShadows);
            AppendFloat(builder, value.shadowStrength);
            builder.Append(value.cullingMask).Append('|').Append(value.renderingLayerMask).Append('|');
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
