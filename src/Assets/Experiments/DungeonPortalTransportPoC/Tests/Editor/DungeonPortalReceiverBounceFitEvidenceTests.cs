using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DungeonPortalTransportPoC.Tests
{
    public sealed class DungeonPortalReceiverBounceFitEvidenceTests
    {
        private const string StartEvidencePath =
            "Assets/Experiments/DungeonPortalTransportPoC/FitEvidence/StartRoom_R000_K1ReceiverBounceFitEvidence.asset";
        private const string AdminEvidencePath =
            "Assets/Experiments/DungeonPortalTransportPoC/FitEvidence/AdminstrativeSegregation_R000_K1ReceiverBounceFitEvidence.asset";

        [TestCase(StartEvidencePath)]
        [TestCase(AdminEvidencePath)]
        public void PersistedRejectedEvidence_IsBoundAndValid(string assetPath)
        {
            DungeonPortalReceiverBounceFitEvidence evidence = LoadEvidence(assetPath);

            Assert.That(evidence.TryValidate(out string error), Is.True, error);
            Assert.That(evidence.Status, Is.EqualTo(DungeonPortalReceiverBounceFitEvidence.RejectedStatus));
            Assert.That(evidence.IsAccepted, Is.False);
            Assert.That(evidence.SignalGates.allRequiredPassed, Is.True);
            Assert.That(evidence.CandidateSearch.totalCandidateCount,
                Is.EqualTo(DungeonPortalReceiverBounceFitEvidence.ExpectedCandidateCount));
            Assert.That(evidence.CandidateSearch.selectedCandidateIndex, Is.EqualTo(-1));
            Assert.That(evidence.TargetProfile.IncomingBounceLights, Is.Empty);
        }

        [TestCase(StartEvidencePath)]
        [TestCase(AdminEvidencePath)]
        public void AcceptedClone_IsStructurallyRejected(string assetPath)
        {
            DungeonPortalReceiverBounceFitEvidence source = LoadEvidence(assetPath);
            DungeonPortalReceiverBounceFitEvidence clone =
                ScriptableObject.CreateInstance<DungeonPortalReceiverBounceFitEvidence>();
            try
            {
                DungeonPortalReceiverBounceFitEvidence.FitEvidencePayload payload = CopyPayload(source);
                payload.status = DungeonPortalReceiverBounceFitEvidence.AcceptedStatus;
                clone.ConfigureAuthoring(payload);

                Assert.That(clone.TryValidate(out string error), Is.False);
                StringAssert.Contains("K1_ACCEPTED is disabled", error);
            }
            finally
            {
                Object.DestroyImmediate(clone);
            }
        }

        [TestCase(StartEvidencePath)]
        [TestCase(AdminEvidencePath)]
        public void TamperedStateHash_IsRejected(string assetPath)
        {
            DungeonPortalReceiverBounceFitEvidence source = LoadEvidence(assetPath);
            DungeonPortalReceiverBounceFitEvidence clone =
                ScriptableObject.CreateInstance<DungeonPortalReceiverBounceFitEvidence>();
            try
            {
                DungeonPortalReceiverBounceFitEvidence.FitEvidencePayload payload = CopyPayload(source);
                payload.baselineStateHash = "tampered";
                clone.ConfigureAuthoring(payload);

                Assert.That(clone.TryValidate(out string error), Is.False);
                StringAssert.Contains("capture/profile identity", error);
            }
            finally
            {
                Object.DestroyImmediate(clone);
            }
        }

        [TestCase(StartEvidencePath)]
        [TestCase(AdminEvidencePath)]
        public void CandidateGetter_ReturnsDefensiveCopy(string assetPath)
        {
            DungeonPortalReceiverBounceFitEvidence evidence = LoadEvidence(assetPath);
            DungeonPortalReceiverBounceFitEvidence.CandidateSearchEvidence first = evidence.CandidateSearch;
            float originalRange = first.candidates[0].range;
            first.candidates[0].range = originalRange + 1000f;

            DungeonPortalReceiverBounceFitEvidence.CandidateSearchEvidence second = evidence.CandidateSearch;
            Assert.That(second.candidates[0].range, Is.EqualTo(originalRange));
        }

        private static DungeonPortalReceiverBounceFitEvidence LoadEvidence(string assetPath)
        {
            DungeonPortalReceiverBounceFitEvidence evidence =
                AssetDatabase.LoadAssetAtPath<DungeonPortalReceiverBounceFitEvidence>(assetPath);
            Assert.That(evidence, Is.Not.Null, "Missing persisted fit evidence at " + assetPath);
            return evidence;
        }

        private static DungeonPortalReceiverBounceFitEvidence.FitEvidencePayload CopyPayload(
            DungeonPortalReceiverBounceFitEvidence source)
        {
            return new DungeonPortalReceiverBounceFitEvidence.FitEvidencePayload
            {
                receiverRoomId = source.ReceiverRoomId,
                stableDoorwayId = source.StableDoorwayId,
                status = source.Status,
                authoredUtcIso8601 = source.AuthoredUtcIso8601,
                fitToolVersion = source.FitToolVersion,
                unityVersion = source.UnityVersion,
                metricDefinitionVersion = source.MetricVersion,
                receiverCapture = source.ReceiverCapture,
                receiverCaptureAssetPath = source.ReceiverCaptureAssetPath,
                receiverCaptureDependencyHash = source.ReceiverCaptureDependencyHash,
                baselineStateHash = source.BaselineStateHash,
                directOnlyStateHash = source.DirectOnlyStateHash,
                fullStateHash = source.FullStateHash,
                captureIntegrity = source.CaptureIntegrity,
                targetProfile = source.TargetProfile,
                targetProfileAssetPath = source.TargetProfileAssetPath,
                targetProfileDependencyHash = source.TargetProfileDependencyHash,
                signalGates = source.SignalGates,
                semanticMasks = source.SemanticMasks,
                candidateSearch = source.CandidateSearch,
                fitGates = source.FitGates,
                acceptedDescriptor = source.AcceptedDescriptor,
                decisionReason = source.DecisionReason
            };
        }
    }
}
