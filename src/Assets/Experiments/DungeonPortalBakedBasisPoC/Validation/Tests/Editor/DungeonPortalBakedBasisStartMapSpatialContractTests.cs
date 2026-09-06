using NUnit.Framework;
using UnityEngine;

namespace DungeonPortalBakedBasisPoC.Validation.Tests.Editor
{
    [TestFixture]
    public sealed class DungeonPortalBakedBasisStartMapSpatialContractTests
    {
        [Test]
        public void RigidDoorwayAlignment_MapsValidationAnchorWithoutChangingScale()
        {
            var root = new GameObject("PairRoot");
            var validation = new GameObject("ValidationDoorway");
            var generated = new GameObject("GeneratedDoorway");
            try
            {
                validation.transform.SetParent(root.transform, false);
                validation.transform.localPosition = new Vector3(2f, 1f, -3f);
                validation.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                generated.transform.SetPositionAndRotation(
                    new Vector3(17f, 4f, -9f),
                    Quaternion.Euler(0f, 225f, 0f));
                Vector3 scaleBefore = root.transform.lossyScale;

                bool valid = DungeonPortalBakedBasisStartMapRuntimeController
                    .TryApplyRigidDoorwayAlignmentForTest(
                        root.transform,
                        validation.transform,
                        generated.transform,
                        out string failure);

                Assert.That(valid, Is.True, failure);
                Assert.That(Vector3.Distance(
                    validation.transform.position,
                    generated.transform.position), Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(
                    validation.transform.rotation,
                    generated.transform.rotation), Is.LessThan(0.05f));
                Assert.That(
                    Vector3.Distance(root.transform.lossyScale, scaleBefore),
                    Is.LessThan(0.00001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(generated);
            }
        }

        [Test]
        public void DoorwayPose_DriftFailsClosed()
        {
            var validation = new GameObject("ValidationDoorway");
            var generated = new GameObject("GeneratedDoorway");
            try
            {
                generated.transform.position = new Vector3(0.01f, 0f, 0f);
                bool valid = DungeonPortalBakedBasisStartMapRuntimeController
                    .TryValidateDoorwayPoseForTest(
                        validation.transform,
                        generated.transform,
                        out string failure);

                Assert.That(valid, Is.False);
                StringAssert.Contains("not coincident", failure);
            }
            finally
            {
                Object.DestroyImmediate(validation);
                Object.DestroyImmediate(generated);
            }
        }

        [Test]
        public void PointContainment_RequiresTargetAndRejectsNonTargetPenetration()
        {
            var target = new Bounds(Vector3.zero, new Vector3(10f, 4f, 10f));
            var distant = new Bounds(new Vector3(20f, 0f, 0f), new Vector3(8f, 4f, 8f));
            Assert.That(
                DungeonPortalBakedBasisStartMapRuntimeController
                    .TryValidatePointInTargetTileForTest(
                        new Vector3(1f, 1f, 1f),
                        target,
                        new[] { distant },
                        out string passFailure),
                Is.True,
                passFailure);

            var overlappingNonTarget = new Bounds(
                new Vector3(1f, 1f, 1f),
                Vector3.one);
            bool penetration = DungeonPortalBakedBasisStartMapRuntimeController
                .TryValidatePointInTargetTileForTest(
                    new Vector3(1f, 1f, 1f),
                    target,
                    new[] { overlappingNonTarget },
                    out string penetrationFailure);
            Assert.That(penetration, Is.False);
            StringAssert.Contains("non-target", penetrationFailure);

            bool outside = DungeonPortalBakedBasisStartMapRuntimeController
                .TryValidatePointInTargetTileForTest(
                    new Vector3(100f, 0f, 0f),
                    target,
                    new Bounds[0],
                    out string outsideFailure);
            Assert.That(outside, Is.False);
            StringAssert.Contains("outside", outsideFailure);
        }
    }
}
