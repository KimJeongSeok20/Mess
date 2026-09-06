using NUnit.Framework;
using UnityEngine;

namespace DungeonAdjacentLightingPoC.Tests
{
    public sealed class DungeonAdjacentLightmapProjectionTests
    {
        [Test]
        public void ProjectsPointAndAppliesAtlasScaleOffset()
        {
            Mesh mesh = CreateTriangle();
            try
            {
                bool success = DungeonAdjacentLightmapProjection.TryProjectPoint(
                    mesh,
                    Matrix4x4.identity,
                    new Vector3(0.25f, 0.2f, 0.25f),
                    new Vector4(0.5f, 0.5f, 0.25f, 0.1f),
                    1f,
                    out DungeonAdjacentLightmapProjection.ProjectionResult result);

                Assert.That(success, Is.True);
                Assert.That(result.atlasUv.x, Is.EqualTo(0.375f).Within(0.0001f));
                Assert.That(result.atlasUv.y, Is.EqualTo(0.225f).Within(0.0001f));
                Assert.That(result.distance, Is.EqualTo(0.2f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void RejectsProjectionOutsideMaximumDistance()
        {
            Mesh mesh = CreateTriangle();
            try
            {
                bool success = DungeonAdjacentLightmapProjection.TryProjectPoint(
                    mesh,
                    Matrix4x4.identity,
                    new Vector3(0.25f, 2f, 0.25f),
                    new Vector4(1f, 1f, 0f, 0f),
                    0.5f,
                    out _);

                Assert.That(success, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SurfaceAlignedProjectionRejectsPerpendicularPortalFace()
        {
            Mesh mesh = CreateTriangle();
            try
            {
                bool floorAligned = DungeonAdjacentLightmapProjection.TryProjectPoint(
                    mesh,
                    Matrix4x4.identity,
                    new Vector3(0.25f, 0.2f, 0.25f),
                    new Vector4(1f, 1f, 0f, 0f),
                    1f,
                    Vector3.up,
                    0.65f,
                    out _);
                bool wallAligned = DungeonAdjacentLightmapProjection.TryProjectPoint(
                    mesh,
                    Matrix4x4.identity,
                    new Vector3(0.25f, 0.2f, 0.25f),
                    new Vector4(1f, 1f, 0f, 0f),
                    1f,
                    Vector3.right,
                    0.65f,
                    out _);

                Assert.That(floorAligned, Is.True);
                Assert.That(wallAligned, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void AlignedPlaneProjectionExtrapolatesUvBeyondShellEdgeWithoutClamping()
        {
            Mesh mesh = CreateTriangle();
            try
            {
                bool success = DungeonAdjacentLightmapProjection.TryProjectPointOnAlignedPlane(
                    mesh,
                    Matrix4x4.identity,
                    new Vector3(1.5f, 0.2f, 0.25f),
                    new Vector4(1f, 1f, 0f, 0f),
                    Vector3.up,
                    0.65f,
                    out DungeonAdjacentLightmapProjection.ProjectionResult result);

                Assert.That(success, Is.True);
                Assert.That(result.atlasUv.x, Is.EqualTo(1.5f).Within(0.0001f));
                Assert.That(result.atlasUv.y, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(result.distance, Is.EqualTo(0.2f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [TestCase(0f, 1.35f, 0.5f)]
        [TestCase(1.35f, 1.35f, 0f)]
        [TestCase(2f, 1.35f, 0f)]
        public void SecondaryWeightHasExpectedBoundaryValues(float distance, float depth, float expected)
        {
            float weight = DungeonAdjacentLightmapProjection.ComputeSecondaryWeight(distance, depth);
            Assert.That(weight, Is.EqualTo(expected).Within(0.0001f));
        }

        [TestCase(0f, 3.5f, 1f)]
        [TestCase(1.75f, 3.5f, 0.5f)]
        [TestCase(3.5f, 3.5f, 0f)]
        public void ProjectionFalloffIsSmooth(float distance, float range, float expected)
        {
            float weight = DungeonAdjacentLightmapProjection.ComputeProjectionFalloff(distance, range);
            Assert.That(weight, Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void BuildVertexDataFallsBackForPortalFrameFaceWithoutAlignedShellPlane()
        {
            Mesh receiver = CreateTriangle();
            Mesh extension = CreateTriangle();
            receiver.normals = new[] { Vector3.right, Vector3.right, Vector3.right };
            extension.RecalculateNormals();
            var output = new Vector4[receiver.vertexCount];
            var projectionBasis = new Vector4[receiver.vertexCount];
            try
            {
                int projected = DungeonAdjacentLightmapProjection.BuildVertexData(
                    receiver,
                    Matrix4x4.identity,
                    Vector3.zero,
                    Vector3.forward,
                    -1f,
                    extension,
                    Matrix4x4.identity,
                    new Vector4(1f, 1f, 0f, 0f),
                    2.75f,
                    3.5f,
                    output,
                    1.25f,
                    projectionBasis);

                Assert.That(projected, Is.EqualTo(receiver.vertexCount));
                for (int i = 0; i < output.Length; i++)
                {
                    Assert.That(output[i].w, Is.GreaterThan(0.999f));
                    Assert.That(output[i].z, Is.GreaterThan(0.999f));
                    Assert.That(projectionBasis[i].w, Is.EqualTo(1f));
                    Assert.That(
                        Mathf.Abs(Vector3.Dot(projectionBasis[i], Vector3.up)),
                        Is.EqualTo(1f).Within(0.0001f));
                }
            }
            finally
            {
                Object.DestroyImmediate(extension);
                Object.DestroyImmediate(receiver);
            }
        }

        [TestCase(0.75f, 1f, 1.25f, 1f)]
        [TestCase(1f, 1f, 1.25f, 1f)]
        [TestCase(1.625f, 1f, 1.25f, 0.5f)]
        [TestCase(2.25f, 1f, 1.25f, 0f)]
        public void DoorwayLateralWeightKeepsOpeningAndSoftensOutside(
            float centerDistance,
            float halfWidth,
            float fadeDistance,
            float expected)
        {
            float weight = DungeonAdjacentLightmapProjection.ComputeDoorwayLateralWeight(
                centerDistance,
                halfWidth,
                fadeDistance);
            Assert.That(weight, Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void PortalFacingWeightRejectsBackFacingEntranceWall()
        {
            float weight = DungeonAdjacentLightmapProjection.ComputePortalSurfaceFacingWeight(
                new Vector3(0f, 1.25f, -0.1f),
                Vector3.back,
                new Vector3(0f, 1.25f, 0f),
                Vector3.back);

            Assert.That(weight, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void PortalFacingWeightKeepsFloorFacingOpening()
        {
            float weight = DungeonAdjacentLightmapProjection.ComputePortalSurfaceFacingWeight(
                new Vector3(0f, 0f, -1f),
                Vector3.up,
                new Vector3(0f, 1.25f, 0f),
                Vector3.back);

            Assert.That(weight, Is.GreaterThan(0.5f));
        }

        [Test]
        public void PortalApertureRevealKeepsJambAndBrightFacingTrimButRejectsBroadWallAndDarkFacingTrim()
        {
            float inwardJamb = DungeonAdjacentLightmapProjection.ComputePortalApertureRevealWeight(
                1.02f,
                1.25f,
                0.1f,
                1f,
                2.5f,
                Vector3.right,
                Vector3.back);
            float broadWall = DungeonAdjacentLightmapProjection.ComputePortalApertureRevealWeight(
                1.2f,
                1.25f,
                0.1f,
                1f,
                2.5f,
                Vector3.right,
                Vector3.back);
            float frontTrim = DungeonAdjacentLightmapProjection.ComputePortalApertureRevealWeight(
                1.02f,
                1.25f,
                0.1f,
                1f,
                2.5f,
                Vector3.forward,
                Vector3.back);
            float darkFacingTrim = DungeonAdjacentLightmapProjection.ComputePortalApertureRevealWeight(
                1.02f,
                1.25f,
                0.1f,
                1f,
                2.5f,
                Vector3.back,
                Vector3.back);

            Assert.That(inwardJamb, Is.GreaterThan(0.9f));
            Assert.That(broadWall, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(frontTrim, Is.GreaterThan(0.9f));
            Assert.That(darkFacingTrim, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void CompactPortalSubmeshBoundsKeepsFrameButRejectsWall()
        {
            Assert.That(
                DungeonAdjacentLightmapProjection.IsCompactPortalSubmeshBounds(
                    -0.662f,
                    0.642f,
                    0f,
                    2.389f,
                    -0.2f,
                    0f,
                    0.5f,
                    2f),
                Is.True);
            Assert.That(
                DungeonAdjacentLightmapProjection.IsCompactPortalSubmeshBounds(
                    -3f,
                    3f,
                    0f,
                    3f,
                    -0.2f,
                    0f,
                    0.5f,
                    2f),
                Is.False);
        }

        [TestCase(true, false, true, 2, 0)]
        [TestCase(true, true, true, 3, 1)]
        [TestCase(false, false, true, 1, 0)]
        [TestCase(false, true, true, 3, 2)]
        public void PairBakeTransferUsesSameReceiverPowerBaseline(
            bool receiverIsStartRoom,
            bool receiverPowered,
            bool sourcePowered,
            int expectedCurrent,
            int expectedBaseline)
        {
            DungeonAdjacentLightmapProjection.ResolvePairStateKeys(
                receiverIsStartRoom,
                receiverPowered,
                sourcePowered,
                out int current,
                out int baseline);

            Assert.That(current, Is.EqualTo(expectedCurrent));
            Assert.That(baseline, Is.EqualTo(expectedBaseline));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PairBakeSourceP0ProducesIdenticalCurrentAndBaseline(
            bool receiverIsStartRoom)
        {
            DungeonAdjacentLightmapProjection.ResolvePairStateKeys(
                receiverIsStartRoom,
                true,
                false,
                out int current,
                out int baseline);

            Assert.That(current, Is.EqualTo(baseline));
        }

        [Test]
        public void RendererEligibilityAcceptsGenericStaticPropNameAndShader()
        {
            GameObject gameObject = new GameObject("UnclassifiedFurnitureProp");
            Mesh mesh = CreateTriangle();
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                MeshFilter filter = gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;

                bool eligible = DungeonAdjacentRendererUtility.TryGetEligibleMesh(
                    renderer,
                    out MeshFilter resolvedFilter,
                    out Mesh resolvedMesh);

                Assert.That(eligible, Is.True);
                Assert.That(resolvedFilter, Is.SameAs(filter));
                Assert.That(resolvedMesh, Is.SameAs(mesh));
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void RendererEligibilityRejectsMeshWithoutCompleteLightmapUv()
        {
            GameObject gameObject = new GameObject("PropWithoutLightmapUv");
            Mesh mesh = CreateTriangle();
            mesh.uv2 = System.Array.Empty<Vector2>();
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;

                bool eligible = DungeonAdjacentRendererUtility.TryGetEligibleMesh(
                    renderer,
                    out _,
                    out _);

                Assert.That(eligible, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void StableDoorwayIdentityDistinguishesDuplicateSiblingNames()
        {
            GameObject root = new GameObject("Room");
            GameObject first = new GameObject("DoorWayPoint");
            GameObject second = new GameObject("DoorWayPoint");
            try
            {
                first.transform.SetParent(root.transform, false);
                second.transform.SetParent(root.transform, false);

                string firstId = DungeonAdjacentDoorwayIdentity.GetStableHierarchyId(
                    first.transform,
                    root.transform);
                string secondId = DungeonAdjacentDoorwayIdentity.GetStableHierarchyId(
                    second.transform,
                    root.transform);

                Assert.That(firstId, Is.EqualTo("DoorWayPoint[0]"));
                Assert.That(secondId, Is.EqualTo("DoorWayPoint[1]"));
                Assert.That(firstId, Is.Not.EqualTo(secondId));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PortalBandAcceptsCoarseFloorBoundsEvenWhenCornersAreOutsideFade()
        {
            var doorwayPosition = new Vector3(0f, 0f, 5.2f);
            var doorwayForward = Vector3.left;
            var nearFloorChunk = new Bounds(
                new Vector3(3.5f, 0f, 3.5f),
                new Vector3(7f, 0.02f, 7f));
            var farFloorChunk = new Bounds(
                new Vector3(10.5f, 0f, 3.5f),
                new Vector3(7f, 0.02f, 7f));

            Assert.That(
                DungeonAdjacentLightmapProjection.BoundsOverlapsPortalBand(
                    nearFloorChunk,
                    doorwayPosition,
                    doorwayForward,
                    -1f,
                    2.75f,
                    0.5f,
                    1.25f),
                Is.True);
            Assert.That(
                DungeonAdjacentLightmapProjection.BoundsOverlapsPortalBand(
                    farFloorChunk,
                    doorwayPosition,
                    doorwayForward,
                    -1f,
                    2.75f,
                    0.5f,
                    1.25f),
                Is.False);
        }

        private static Mesh CreateTriangle()
        {
            var mesh = new Mesh { name = "ProjectionTestTriangle" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 0f, 1f)
            };
            mesh.uv2 = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new[] { 0, 1, 2 };
            return mesh;
        }
    }
}
