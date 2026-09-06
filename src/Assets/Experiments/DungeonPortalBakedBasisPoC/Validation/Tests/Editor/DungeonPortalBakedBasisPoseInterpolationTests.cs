using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DungeonPortalBakedBasisPoC.Validation.Tests.Editor
{
    /// <summary>
    /// Scene-isolated contracts for physical door-pose response interpolation. No production or
    /// validation scene object is loaded, saved, or modified by these tests.
    /// </summary>
    [TestFixture]
    public sealed class DungeonPortalBakedBasisPoseInterpolationTests
    {
        private const string ConnectionId = "PoseConnection";
        private const string ReceiverDoorId = "ReceiverDoor";
        private const string SourceDoorId = "SourceDoor";
        private const string BasisId = "K1";

        [TestCase(0f)]
        [TestCase(0.25f)]
        [TestCase(0.5f)]
        [TestCase(0.75f)]
        [TestCase(1f)]
        public void ApertureConversion_RecoversPhysicalDoorFraction(float expectedPhysicalFraction)
        {
            float aperture = 1f - Mathf.Cos(expectedPhysicalFraction * Mathf.PI * 0.5f);

            float actual = DungeonPortalBakedBasisRoomCompositor
                .ApertureToPhysicalOpenFraction(aperture);

            Assert.That(actual, Is.EqualTo(expectedPhysicalFraction).Within(0.000001f));
        }

        [Test]
        public void PoseResponses_SortAndInterpolateZeroFirstAndBrackets()
        {
            DungeonPortalBakedRoomBasisData.ReceiverResponseLobe d25 = CreateLobe(1f);
            DungeonPortalBakedRoomBasisData.ReceiverResponseLobe d50 = CreateLobe(2f);
            DungeonPortalBakedRoomBasisData.ReceiverResponseLobe d75 = CreateLobe(3f);
            DungeonPortalBakedRoomBasisData.ReceiverResponseLobe d100 = CreateLobe(4f);
            var door = new DungeonPortalBakedRoomBasisData.ReceiverDoorBasis();
            door.ConfigurePoseAuthoring(
                ReceiverDoorId,
                new[]
                {
                    CreatePose(1f, d100),
                    CreatePose(0.5f, d50),
                    CreatePose(0.25f, d25),
                    CreatePose(0.75f, d75)
                });

            Assert.That(door.PoseResponses[0].OpenFraction, Is.EqualTo(0.25f));
            Assert.That(door.PoseResponses[1].OpenFraction, Is.EqualTo(0.5f));
            Assert.That(door.PoseResponses[2].OpenFraction, Is.EqualTo(0.75f));
            Assert.That(door.PoseResponses[3].OpenFraction, Is.EqualTo(1f));

            var weighted = new List<
                DungeonPortalBakedRoomBasisData.WeightedReceiverResponseLobe>();
            Assert.That(door.TryAppendWeightedResponseLobes(0.125f, weighted, out string failure),
                Is.True, failure);
            Assert.That(weighted, Has.Count.EqualTo(1));
            Assert.That(weighted[0].Lobe, Is.SameAs(d25));
            Assert.That(weighted[0].PoseWeight, Is.EqualTo(0.5f).Within(0.000001f));

            weighted.Clear();
            Assert.That(door.TryAppendWeightedResponseLobes(0.375f, weighted, out failure),
                Is.True, failure);
            Assert.That(weighted, Has.Count.EqualTo(2));
            Assert.That(weighted[0].Lobe, Is.SameAs(d25));
            Assert.That(weighted[1].Lobe, Is.SameAs(d50));
            Assert.That(weighted[0].PoseWeight, Is.EqualTo(0.5f).Within(0.000001f));
            Assert.That(weighted[1].PoseWeight, Is.EqualTo(0.5f).Within(0.000001f));

            weighted.Clear();
            Assert.That(door.TryAppendWeightedResponseLobes(0.625f, weighted, out failure),
                Is.True, failure);
            Assert.That(weighted, Has.Count.EqualTo(2));
            Assert.That(weighted[0].Lobe, Is.SameAs(d50));
            Assert.That(weighted[1].Lobe, Is.SameAs(d75));
            Assert.That(weighted[0].PoseWeight, Is.EqualTo(0.5f).Within(0.000001f));
            Assert.That(weighted[1].PoseWeight, Is.EqualTo(0.5f).Within(0.000001f));

            weighted.Clear();
            Assert.That(door.TryAppendWeightedResponseLobes(0f, weighted, out failure),
                Is.True, failure);
            Assert.That(weighted, Is.Empty);
        }

        [Test]
        public void LegacyResponse_RemainsSyntheticD100Fallback()
        {
            DungeonPortalBakedRoomBasisData.ReceiverResponseLobe legacy = CreateLobe(1f);
            var door = new DungeonPortalBakedRoomBasisData.ReceiverDoorBasis();
            door.ConfigureAuthoring(ReceiverDoorId, new[] { legacy });
            var weighted = new List<
                DungeonPortalBakedRoomBasisData.WeightedReceiverResponseLobe>();

            bool result = door.TryAppendWeightedResponseLobes(0.4f, weighted, out string failure);

            Assert.That(result, Is.True, failure);
            Assert.That(weighted, Has.Count.EqualTo(1));
            Assert.That(weighted[0].Lobe, Is.SameAs(legacy));
            Assert.That(weighted[0].PoseWeight, Is.EqualTo(0.4f).Within(0.000001f));
        }

        [Test]
        public void DefinitionValidation_RequiresD25D50D75D100PoseSet()
        {
            Texture2D texture = CreateLinearHalfTexture();
            DungeonPortalBakedRoomBasisData valid = null;
            DungeonPortalBakedRoomBasisData incomplete = null;
            try
            {
                valid = CreateDefinition(texture, new[] { 1f, 0.5f, 0.25f, 0.75f });
                Assert.That(valid.TryValidateDefinition(out string validFailure),
                    Is.True, validFailure);

                incomplete = CreateDefinition(texture, new[] { 0.25f, 0.5f, 1f });
                Assert.That(incomplete.TryValidateDefinition(out string incompleteFailure),
                    Is.False);
                StringAssert.Contains("expected sorted D25/D50/D75/D100", incompleteFailure);
            }
            finally
            {
                DestroyImmediate(valid);
                DestroyImmediate(incomplete);
                DestroyImmediate(texture);
            }
        }

        [Test]
        public void ProbeDelta_UsesSamePoseWeightsAndSafeResponseScale()
        {
            DungeonPortalBakedRoomBasisData receiverBasis = CreateReceiverProbeBasis();
            DungeonPortalBakedRoomBasisData sourceBasis = CreateSourceProbeBasis();
            GameObject root = null;
            try
            {
                root = EditorUtility.CreateGameObjectWithHideFlags(
                    "__DPBB_PoseInterpolationTest",
                    HideFlags.HideAndDontSave,
                    typeof(DungeonPortalBakedBasisRoomCompositor));
                DungeonPortalBakedBasisRoomCompositor compositor =
                    root.GetComponent<DungeonPortalBakedBasisRoomCompositor>();

                float physicalFraction = 0.375f;
                float aperture = 1f - Mathf.Cos(physicalFraction * Mathf.PI * 0.5f);
                var incoming = new DungeonPortalBakedBasisRoomCompositor.IncomingDoorState();
                incoming.ConfigureAuthoring(
                    ConnectionId,
                    ReceiverDoorId,
                    sourceBasis,
                    SourceDoorId,
                    1f,
                    aperture,
                    2f,
                    true);
                compositor.ConfigureAuthoring(
                    receiverBasis,
                    root.transform,
                    null,
                    Array.Empty<DungeonPortalBakedBasisRoomCompositor.ExplicitRendererBinding>(),
                    new[] { incoming },
                    0f,
                    false);

                var result = new float[
                    DungeonPortalBakedRoomBasisData.SphericalHarmonicsCoefficientCount];
                Assert.That(compositor.TryEvaluateIncomingDoorwayProbeDelta(
                    ConnectionId, result, out string evaluationFailure), Is.True, evaluationFailure);
                AssertChannel(result, 0, 4f);
                AssertChannel(result, 1, 8f);
                AssertChannel(result, 2, 12f);

                int beforeScaleHash = InvokeStateHash(compositor);
                Assert.That(compositor.TrySetIncomingResponseScale(
                    ConnectionId, 0f, out string scaleFailure), Is.True, scaleFailure);
                int afterScaleHash = InvokeStateHash(compositor);
                Assert.That(afterScaleHash, Is.Not.EqualTo(beforeScaleHash));
                Assert.That(compositor.TryEvaluateIncomingDoorwayProbeDelta(
                    ConnectionId, result, out evaluationFailure), Is.True, evaluationFailure);
                Assert.That(result, Has.All.EqualTo(0f));

                Assert.That(compositor.TrySetIncomingResponseScale(
                    ConnectionId, -0.01f, out _), Is.False);
                Assert.That(compositor.TrySetIncomingResponseScale(
                    ConnectionId, float.NaN, out _), Is.False);
                Assert.That(incoming.ResponseScale, Is.EqualTo(0f));
            }
            finally
            {
                if (root != null)
                    DestroyImmediate(root);
                DestroyImmediate(receiverBasis);
                DestroyImmediate(sourceBasis);
            }
        }

        private static DungeonPortalBakedRoomBasisData CreateDefinition(
            Texture2D texture,
            float[] poseFractions)
        {
            var bucket = new DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket();
            bucket.ConfigureAuthoring("Bucket0", 0, texture, texture, texture, texture);
            var renderer = new DungeonPortalBakedRoomBasisData.CanonicalRendererEntry();
            renderer.ConfigureAuthoring(
                "Renderer#0",
                0,
                0,
                new Vector4(1f, 1f, 0f, 0f),
                0,
                new Vector4(1f, 1f, 0f, 0f));
            var sourceAtlas = new DungeonPortalBakedRoomBasisData.Power100SourceAtlas();
            sourceAtlas.ConfigureAuthoring(0, texture, texture);

            var response = new DungeonPortalBakedRoomBasisData.ResponseAtlas();
            response.ConfigureOnOffAuthoring(0, texture, texture, texture, texture);
            var poses = new DungeonPortalBakedRoomBasisData.ReceiverDoorPoseResponse[
                poseFractions.Length];
            for (int i = 0; i < poseFractions.Length; i++)
            {
                var lobe = new DungeonPortalBakedRoomBasisData.ReceiverResponseLobe();
                lobe.ConfigureEndpointAuthoring(
                    BasisId,
                    new[] { response },
                    new[] { response });
                poses[i] = CreatePose(poseFractions[i], lobe);
            }

            var receiverDoor = new DungeonPortalBakedRoomBasisData.ReceiverDoorBasis();
            receiverDoor.ConfigurePoseAuthoring(ReceiverDoorId, poses);
            DungeonPortalBakedRoomBasisData data =
                ScriptableObject.CreateInstance<DungeonPortalBakedRoomBasisData>();
            data.ConfigureAuthoring(
                "PoseDefinition",
                new[] { bucket },
                new[] { renderer },
                new[] { sourceAtlas },
                new[] { receiverDoor },
                Array.Empty<DungeonPortalBakedRoomBasisData.SourceDoorBasis>());
            return data;
        }

        private static DungeonPortalBakedRoomBasisData CreateReceiverProbeBasis()
        {
            var door = new DungeonPortalBakedRoomBasisData.ReceiverDoorBasis();
            door.ConfigurePoseAuthoring(
                ReceiverDoorId,
                new[]
                {
                    CreatePose(0.25f, CreateLobe(1f)),
                    CreatePose(0.5f, CreateLobe(3f)),
                    CreatePose(0.75f, CreateLobe(5f)),
                    CreatePose(1f, CreateLobe(7f))
                });
            DungeonPortalBakedRoomBasisData data =
                ScriptableObject.CreateInstance<DungeonPortalBakedRoomBasisData>();
            data.ConfigureAuthoring(
                "ReceiverProbe",
                Array.Empty<DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket>(),
                Array.Empty<DungeonPortalBakedRoomBasisData.CanonicalRendererEntry>(),
                Array.Empty<DungeonPortalBakedRoomBasisData.Power100SourceAtlas>(),
                new[] { door },
                Array.Empty<DungeonPortalBakedRoomBasisData.SourceDoorBasis>());
            return data;
        }

        private static DungeonPortalBakedRoomBasisData CreateSourceProbeBasis()
        {
            var coefficient = new DungeonPortalBakedRoomBasisData.SourceBasisCoefficient();
            coefficient.ConfigureAuthoring(BasisId, new Color(1f, 2f, 3f), new Color(1f, 2f, 3f));
            var door = new DungeonPortalBakedRoomBasisData.SourceDoorBasis();
            door.ConfigureAuthoring(SourceDoorId, new[] { coefficient });
            DungeonPortalBakedRoomBasisData data =
                ScriptableObject.CreateInstance<DungeonPortalBakedRoomBasisData>();
            data.ConfigureAuthoring(
                "SourceProbe",
                Array.Empty<DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket>(),
                Array.Empty<DungeonPortalBakedRoomBasisData.CanonicalRendererEntry>(),
                Array.Empty<DungeonPortalBakedRoomBasisData.Power100SourceAtlas>(),
                Array.Empty<DungeonPortalBakedRoomBasisData.ReceiverDoorBasis>(),
                new[] { door });
            return data;
        }

        private static DungeonPortalBakedRoomBasisData.ReceiverDoorPoseResponse CreatePose(
            float openFraction,
            DungeonPortalBakedRoomBasisData.ReceiverResponseLobe lobe)
        {
            var pose = new DungeonPortalBakedRoomBasisData.ReceiverDoorPoseResponse();
            pose.ConfigureAuthoring(openFraction, new[] { lobe });
            return pose;
        }

        private static DungeonPortalBakedRoomBasisData.ReceiverResponseLobe CreateLobe(
            float doorwayDelta)
        {
            float[] off = new float[
                DungeonPortalBakedRoomBasisData.SphericalHarmonicsCoefficientCount];
            float[] on = new float[
                DungeonPortalBakedRoomBasisData.SphericalHarmonicsCoefficientCount];
            for (int i = 0; i < on.Length; i++)
                on[i] = doorwayDelta;
            var probe = new DungeonPortalBakedRoomBasisData.OptionalDoorwayProbeResponse();
            probe.ConfigureAuthoring(off, on);
            var lobe = new DungeonPortalBakedRoomBasisData.ReceiverResponseLobe();
            lobe.ConfigureAuthoring(
                BasisId,
                Array.Empty<DungeonPortalBakedRoomBasisData.ResponseAtlas>(),
                probe);
            return lobe;
        }

        private static Texture2D CreateLinearHalfTexture()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBAHalf, true, true)
            {
                name = "__DPBB_PoseValidationTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 1,
                mipMapBias = 0f
            };
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(0.25f, 0.25f, 0.25f, 1f);
            texture.SetPixels(pixels);
            texture.Apply(true, false);
            return texture;
        }

        private static int InvokeStateHash(DungeonPortalBakedBasisRoomCompositor compositor)
        {
            MethodInfo method = typeof(DungeonPortalBakedBasisRoomCompositor).GetMethod(
                "ComputeStateHash",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (int)method.Invoke(compositor, null);
        }

        private static void AssertChannel(float[] coefficients, int channel, float expected)
        {
            for (int coefficient = 0; coefficient < 9; coefficient++)
            {
                Assert.That(
                    coefficients[channel * 9 + coefficient],
                    Is.EqualTo(expected).Within(0.000001f));
            }
        }

        private static void DestroyImmediate(UnityEngine.Object value)
        {
            if (value != null)
                UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
