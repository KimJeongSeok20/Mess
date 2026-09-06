using NUnit.Framework;

namespace DungeonPortalTransportPoC.Tests
{
    public sealed class DungeonPortalReceiverBounceRenderArtifactDoorPoseGateTests
    {
        private static readonly string[] RequiredGateIds =
        {
            "closed_0",
            "intermediate_25",
            "intermediate_50",
            "intermediate_75"
        };

        [Test]
        public void UnavailablePoseManifest_IsValidIncompleteEvidenceButNotReady()
        {
            DungeonPortalReceiverBounceRenderArtifact.MissingPoseGate[] gates =
                BuildUnavailableGates();

            bool valid = DungeonPortalReceiverBounceRenderArtifact.TryEvaluateRequiredDoorPoseGates(
                gates,
                out bool allRequiredPosesPassed,
                out string error);

            Assert.That(valid, Is.True, error);
            Assert.That(allRequiredPosesPassed, Is.False);
        }

        [Test]
        public void UnavailablePoseMarkedPassed_IsRejected()
        {
            DungeonPortalReceiverBounceRenderArtifact.MissingPoseGate[] gates =
                BuildUnavailableGates();
            gates[1].passed = true;

            bool valid = DungeonPortalReceiverBounceRenderArtifact.TryEvaluateRequiredDoorPoseGates(
                gates,
                out bool allRequiredPosesPassed,
                out string error);

            Assert.That(valid, Is.False);
            Assert.That(allRequiredPosesPassed, Is.False);
            StringAssert.Contains("cannot pass", error);
        }

        [Test]
        public void PassedPoseWithoutCapturedEvidence_IsRejected()
        {
            DungeonPortalReceiverBounceRenderArtifact.MissingPoseGate[] gates =
                BuildPassedGates();
            gates[2].evidenceSignature = string.Empty;

            bool valid = DungeonPortalReceiverBounceRenderArtifact.TryEvaluateRequiredDoorPoseGates(
                gates,
                out bool allRequiredPosesPassed,
                out string error);

            Assert.That(valid, Is.False);
            Assert.That(allRequiredPosesPassed, Is.False);
            StringAssert.Contains("without captured evidence", error);
        }

        [Test]
        public void AllCapturedAndValidatedRequiredPoses_AreReady()
        {
            DungeonPortalReceiverBounceRenderArtifact.MissingPoseGate[] gates =
                BuildPassedGates();

            bool valid = DungeonPortalReceiverBounceRenderArtifact.TryEvaluateRequiredDoorPoseGates(
                gates,
                out bool allRequiredPosesPassed,
                out string error);

            Assert.That(valid, Is.True, error);
            Assert.That(allRequiredPosesPassed, Is.True);
        }

        private static DungeonPortalReceiverBounceRenderArtifact.MissingPoseGate[] BuildUnavailableGates()
        {
            var gates = new DungeonPortalReceiverBounceRenderArtifact.MissingPoseGate[RequiredGateIds.Length];
            for (int i = 0; i < gates.Length; i++)
            {
                gates[i] = new DungeonPortalReceiverBounceRenderArtifact.MissingPoseGate
                {
                    gateId = RequiredGateIds[i],
                    available = false,
                    passed = false,
                    evidenceSignature = string.Empty,
                    reason = "Pose has not been captured."
                };
            }

            return gates;
        }

        private static DungeonPortalReceiverBounceRenderArtifact.MissingPoseGate[] BuildPassedGates()
        {
            var gates = new DungeonPortalReceiverBounceRenderArtifact.MissingPoseGate[RequiredGateIds.Length];
            for (int i = 0; i < gates.Length; i++)
            {
                gates[i] = new DungeonPortalReceiverBounceRenderArtifact.MissingPoseGate
                {
                    gateId = RequiredGateIds[i],
                    available = true,
                    passed = true,
                    evidenceSignature = "pose-evidence-" + RequiredGateIds[i],
                    reason = string.Empty
                };
            }

            return gates;
        }
    }
}
