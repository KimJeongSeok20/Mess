using System;
using DungeonPortalBakedBasisPoC.Validation.Editor;
using NUnit.Framework;

namespace DungeonPortalBakedBasisPoC.Validation.Tests.Editor
{
    [TestFixture]
    public sealed class DungeonPortalBakedBasisEvidencePolicyTests
    {
        [Test]
        public void HumanSpotOn_PostEnabledPng_IsReviewCandidateButNeverFinalEvidence()
        {
            string contract = DungeonPortalBakedBasisSmokeCapture.DescribeRawArtifactEvidenceForTests(
                DungeonPortalBakedBasisEvidenceSpotPolicy.HUMAN_SPOT_ON,
                true,
                true);

            StringAssert.Contains("evidenceRole=productionParityReviewCandidate", contract);
            StringAssert.Contains("postProcessingEnabled=true", contract);
            StringAssert.Contains("productionParityReviewCandidate=true", contract);
            StringAssert.Contains("visualVerdict=UNREVIEWED", contract);
            StringAssert.Contains("finalEvidenceEligible=false", contract);
        }

        [Test]
        public void OracleSpotOff_PostEnabledPng_IsDiagnosticAndNeverFinalEvidence()
        {
            string contract = DungeonPortalBakedBasisSmokeCapture.DescribeRawArtifactEvidenceForTests(
                DungeonPortalBakedBasisEvidenceSpotPolicy.ORACLE_SPOT_OFF,
                true,
                true);

            StringAssert.Contains("evidenceRole=diagnostic", contract);
            StringAssert.Contains("postProcessingEnabled=true", contract);
            StringAssert.Contains("productionParityReviewCandidate=false", contract);
            StringAssert.Contains("visualVerdict=UNREVIEWED", contract);
            StringAssert.Contains("finalEvidenceEligible=false", contract);
        }

        [Test]
        public void PostDisabledExr_IsDiagnosticAndPostEnabledExrFailsClosed()
        {
            string contract = DungeonPortalBakedBasisSmokeCapture.DescribeRawArtifactEvidenceForTests(
                DungeonPortalBakedBasisEvidenceSpotPolicy.HUMAN_SPOT_ON,
                false,
                false);

            StringAssert.Contains("evidenceRole=diagnostic", contract);
            StringAssert.Contains("postProcessingEnabled=false", contract);
            StringAssert.Contains("productionParityReviewCandidate=false", contract);
            StringAssert.Contains("visualVerdict=UNREVIEWED", contract);
            StringAssert.Contains("finalEvidenceEligible=false", contract);
            Assert.Throws<InvalidOperationException>(() =>
                DungeonPortalBakedBasisSmokeCapture.DescribeRawArtifactEvidenceForTests(
                    DungeonPortalBakedBasisEvidenceSpotPolicy.ORACLE_SPOT_OFF,
                    false,
                    true));
        }

        [Test]
        public void FinalCapture_RequiresOriginalPlayerCameraPostAlreadyEnabled()
        {
            Assert.DoesNotThrow(() =>
                DungeonPortalBakedBasisSmokeCapture.RequireFinalPresentationPostEnabledForTests(true));
            Assert.Throws<InvalidOperationException>(() =>
                DungeonPortalBakedBasisSmokeCapture.RequireFinalPresentationPostEnabledForTests(false));
        }

        [Test]
        public void FinalCaptureContract_IsExplicitlyUnreviewedAndNotFinalEligible()
        {
            string contract = DungeonPortalBakedBasisSmokeCapture.DescribeFinalCaptureContractForTests();

            StringAssert.Contains("requiresOriginalPlayerCameraPostProcessingEnabled=true", contract);
            StringAssert.Contains(
                "productionParityReviewCandidate=HUMAN_SPOT_ON+PNG+postEnabled",
                contract);
            StringAssert.Contains(
                "diagnosticArtifacts=ORACLE_SPOT_OFF_PNG+all_postDisabled_EXR+all_state",
                contract);
            StringAssert.Contains("rawVisualVerdict=UNREVIEWED", contract);
            StringAssert.Contains("rawFinalEvidenceEligible=false", contract);
        }
    }
}
