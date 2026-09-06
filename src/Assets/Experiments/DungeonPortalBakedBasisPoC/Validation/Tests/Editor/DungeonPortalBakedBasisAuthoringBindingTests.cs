using System;
using DungeonPortalBakedBasisPoC.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DungeonPortalBakedBasisPoC.Validation.Tests.Editor
{
    [TestFixture]
    public sealed class DungeonPortalBakedBasisAuthoringBindingTests
    {
        [Test]
        public void RepeatedNameBinding_PreservesBaseOccurrencesAndMatchesCaptureByExactIdentity()
        {
            const string relativePath = "Pillars/Pillar";
            string[] runtimeTraversal =
            {
                "Pillars[4]/Pillar[10]",
                "Pillars[4]/Pillar[2]",
                "Pillars[4]/Pillar[7]"
            };
            DungeonTileBakeData.RendererBakeEntry[] p0 =
            {
                Entry(relativePath, 11, new Vector4(0.11f, 0.12f, 0.13f, 0.14f)),
                Entry(relativePath, 12, new Vector4(0.21f, 0.22f, 0.23f, 0.24f)),
                Entry(relativePath, 13, new Vector4(0.31f, 0.32f, 0.33f, 0.34f))
            };
            DungeonTileBakeData.RendererBakeEntry[] p100 =
            {
                Entry(relativePath, 21, new Vector4(0.41f, 0.42f, 0.43f, 0.44f)),
                Entry(relativePath, 22, new Vector4(0.51f, 0.52f, 0.53f, 0.54f)),
                Entry(relativePath, 23, new Vector4(0.61f, 0.62f, 0.63f, 0.64f))
            };

            DungeonPortalBakedBasisAuthoring.BaseOccurrenceBindingForTest[] actual =
                DungeonPortalBakedBasisAuthoring.BindBaseOccurrencesForTest(
                    relativePath,
                    runtimeTraversal,
                    p0,
                    p100);

            Assert.That(actual, Has.Length.EqualTo(3));
            for (int occurrence = 0; occurrence < actual.Length; occurrence++)
            {
                Assert.That(actual[occurrence].CanonicalKey,
                    Is.EqualTo(relativePath + "#" + occurrence));
                Assert.That(actual[occurrence].ProductionStablePath,
                    Is.EqualTo(runtimeTraversal[occurrence]));
                Assert.That(actual[occurrence].P0LightmapIndex,
                    Is.EqualTo(p0[occurrence].lightmapIndex));
                Assert.That(actual[occurrence].P0ScaleOffset,
                    Is.EqualTo(p0[occurrence].lightmapScaleOffset));
                Assert.That(actual[occurrence].P100LightmapIndex,
                    Is.EqualTo(p100[occurrence].lightmapIndex));
                Assert.That(actual[occurrence].P100ScaleOffset,
                    Is.EqualTo(p100[occurrence].lightmapScaleOffset));
            }

            DungeonPortalBakedBasisAuthoring.RendererStableIdentity[] productionIdentities =
            {
                new DungeonPortalBakedBasisAuthoring.RendererStableIdentity(runtimeTraversal[0], 0),
                new DungeonPortalBakedBasisAuthoring.RendererStableIdentity(runtimeTraversal[1], 1),
                new DungeonPortalBakedBasisAuthoring.RendererStableIdentity(runtimeTraversal[2], 0)
            };
            DungeonPortalBakedBasisAuthoring.RendererStableIdentity[] capturesInIndependentOrder =
            {
                productionIdentities[2],
                productionIdentities[0],
                productionIdentities[1]
            };

            int[] matchedProductionOccurrences =
                DungeonPortalBakedBasisAuthoring.MatchCaptureIdentitiesToProductionOccurrences(
                    "Pillars regression",
                    productionIdentities,
                    capturesInIndependentOrder);

            CollectionAssert.AreEqual(new[] { 2, 0, 1 }, matchedProductionOccurrences);
            Assert.Throws<System.InvalidOperationException>(() =>
                DungeonPortalBakedBasisAuthoring.MatchCaptureIdentitiesToProductionOccurrences(
                    "Pillars regression",
                    productionIdentities,
                    new[]
                    {
                        new DungeonPortalBakedBasisAuthoring.RendererStableIdentity(
                            runtimeTraversal[0],
                            99)
                    }));
            Assert.Throws<System.InvalidOperationException>(() =>
                DungeonPortalBakedBasisAuthoring.MatchCaptureIdentitiesToProductionOccurrences(
                    "Pillars regression",
                    productionIdentities,
                    new[] { productionIdentities[0], productionIdentities[0] }));
        }

        [Test]
        public void ConservativeMipCoverage_UsesTwoByTwoOrWithoutMutatingMip0()
        {
            var mip0 = new bool[16];
            mip0[0] = true;
            mip0[10] = true;

            bool[][] levels = DungeonPortalBakedBasisAuthoring.BuildConservativeMipMasksForTest(
                mip0,
                4,
                4,
                3);

            Assert.That(levels, Has.Length.EqualTo(3));
            CollectionAssert.AreEqual(mip0, levels[0]);
            CollectionAssert.AreEqual(new[] { true, false, false, true }, levels[1]);
            CollectionAssert.AreEqual(new[] { true }, levels[2]);
            levels[0][0] = false;
            Assert.That(mip0[0], Is.True, "The derived evidence must not alias the mip0 owner mask.");
        }

        [Test]
        public void SampledCoverage_DilatesOneBcBlockThenOrDownsamples()
        {
            var core = new bool[64];
            core[3 + 3 * 8] = true;
            bool[][] actual = DungeonPortalBakedBasisAuthoring.BuildSampledCoverageMipMasksForTest(
                core,
                8,
                8,
                4);

            var expectedMip0 = new bool[64];
            for (int texel = 0; texel < expectedMip0.Length; texel++)
                expectedMip0[texel] = true;
            var expectedMip1 = new bool[16];
            for (int texel = 0; texel < expectedMip1.Length; texel++)
                expectedMip1[texel] = true;

            CollectionAssert.AreEqual(expectedMip0, actual[0]);
            CollectionAssert.AreEqual(expectedMip1, actual[1]);
            CollectionAssert.AreEqual(new[] { true, true, true, true }, actual[2]);
            CollectionAssert.AreEqual(new[] { true }, actual[3]);
        }

        [Test]
        public void GeneratedMipChainValidation_AcceptsIndependentTwoByTwoBoxAverage()
        {
            Color[] mip0 =
            {
                new Color(0f, 2f, 4f, 6f),
                new Color(2f, 4f, 6f, 8f),
                new Color(4f, 6f, 8f, 10f),
                new Color(6f, 8f, 10f, 12f)
            };
            Color[][] levels =
            {
                mip0,
                new[] { new Color(3f, 5f, 7f, 9f) }
            };

            Assert.DoesNotThrow(() =>
                DungeonPortalBakedBasisAuthoring.ValidateGeneratedMipChainForTest(
                    2,
                    2,
                    levels,
                    false));
        }

        [Test]
        public void GeneratedMipChainValidation_FailsClosedOnCardinalityFiniteAndParityDrift()
        {
            Color[] finiteMip0 =
            {
                Color.black,
                Color.white,
                Color.white,
                Color.black
            };

            Assert.Throws<System.InvalidOperationException>(() =>
                DungeonPortalBakedBasisAuthoring.ValidateGeneratedMipChainForTest(
                    2,
                    2,
                    new[] { finiteMip0 },
                    false));
            Assert.Throws<System.InvalidOperationException>(() =>
                DungeonPortalBakedBasisAuthoring.ValidateGeneratedMipChainForTest(
                    2,
                    2,
                    new[]
                    {
                        new[]
                        {
                            new Color(float.NaN, 0f, 0f, 0f),
                            Color.black,
                            Color.black,
                            Color.black
                        },
                        new[] { Color.black }
                    },
                    false));
            Assert.Throws<System.InvalidOperationException>(() =>
                DungeonPortalBakedBasisAuthoring.ValidateGeneratedMipChainForTest(
                    2,
                    2,
                    new[]
                    {
                        finiteMip0,
                        new[] { new Color(0.75f, 0.5f, 0.5f, 0.5f) }
                    },
                    false));
        }

        [TestCase(1024, 1024, 5, 0.018690254d, 0.105520648d, 0.203613281f, true)]
        [TestCase(1024, 1024, 4, 0.018690254d, 0.105520648d, 0.203613281f, false)]
        [TestCase(1024, 1024, 5, 0.020772957240748305d, 0.11046730097997734d, 0.233398438f, true)]
        [TestCase(1024, 1024, 5, 0.025d, 0.105520648d, 0.203613281f, true)]
        [TestCase(1024, 1024, 5, 0.02501d, 0.105520648d, 0.203613281f, false)]
        [TestCase(1024, 1024, 4, 0.020772957240748305d, 0.11046730097997734d, 0.233398438f, false)]
        [TestCase(1024, 1024, 5, 0.018690254d, 0.105520648d, 0.25001f, false)]
        [TestCase(1024, 1024, 0, 0.5d, 0.0799d, 0.999f, true)]
        [TestCase(1024, 1024, 1, 0.00099d, 0.12d, 0.049f, true)]
        [TestCase(1024, 1024, 1, 0.00101d, 0.12d, 0.049f, false)]
        [TestCase(1024, 1024, 1, 0.00099d, 0.12d, 0.05001f, false)]
        [TestCase(1024, 1024, 0, 0.00099d, 0.12d, 0.049f, false)]
        public void BaseColorCompressionGate_OnlyAllowsBoundedLowEnergyCoarseMips(
            int width,
            int height,
            int mip,
            double absoluteRmse,
            double relativeRmse,
            float maxAbsoluteError,
            bool expected)
        {
            bool actual = DungeonPortalBakedBasisAuthoring
                .IsBaseColorMipCompressionWithinBoundsForTest(
                    width,
                    height,
                    mip,
                    absoluteRmse,
                    relativeRmse,
                    maxAbsoluteError);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCase(0f, false, 0f)]
        [TestCase(-0f, true, 0f)]
        [TestCase(2f, false, 0f)]
        [TestCase(-2f, false, 2f)]
        [TestCase(2f, true, 2f)]
        public void UnsignedCompactMagnitude_CanonicalizesZeroBeforeBc6h(
            float signed,
            bool positive,
            float expected)
        {
            float actual = DungeonPortalBakedBasisAuthoring
                .ComputeUnsignedMagnitudeForTest(signed, positive);

            Assert.That(actual, Is.EqualTo(expected));
            if (expected == 0f)
                Assert.That(BitConverter.SingleToInt32Bits(actual), Is.EqualTo(0));
        }

        [Test]
        public void CompactMagnitudeScale_ClampsNonzeroSubMinimumChannelsToRuntimeContract()
        {
            float minimum = DungeonPortalBakedRoomBasisData.NormalizedDeltaZeroChannelScale;
            Vector4 actual = DungeonPortalBakedBasisAuthoring.ComputeMagnitudeScaleForTest(
                new[]
                {
                    new Color(minimum * 0.5f, -minimum * 0.25f, minimum * 2f, minimum * 0.75f)
                },
                true,
                true);

            Assert.That(actual.x, Is.EqualTo(minimum));
            Assert.That(actual.y, Is.EqualTo(minimum));
            Assert.That(actual.z, Is.EqualTo(minimum * 2f));
            Assert.That(actual.w, Is.EqualTo(minimum));
        }

        [Test]
        public void CanonicalPositiveZero_RemainsZeroThroughUnsignedBc6hCompression()
        {
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.BC6H))
                Assert.Ignore("This editor device cannot compress/sample BC6H.");

            float canonicalZero = DungeonPortalBakedBasisAuthoring
                .ComputeUnsignedMagnitudeForTest(0f, false);
            var texture = new Texture2D(4, 4, TextureFormat.RGBAHalf, false, true);
            try
            {
                var pixels = new Color[16];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color(canonicalZero, canonicalZero, canonicalZero, 0f);
                texture.SetPixels(pixels);
                texture.Apply(false, false);
                EditorUtility.CompressTexture(
                    texture,
                    TextureFormat.BC6H,
                    TextureCompressionQuality.Best);

                Assert.That(texture.format, Is.EqualTo(TextureFormat.BC6H));
                foreach (Color pixel in texture.GetPixels())
                {
                    Assert.That(pixel.r, Is.EqualTo(0f));
                    Assert.That(pixel.g, Is.EqualTo(0f));
                    Assert.That(pixel.b, Is.EqualTo(0f));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void CompactColorStorage_Bc6hReconstructionPass_SelectsBc6h()
        {
            TextureFormat selected = DungeonPortalBakedBasisAuthoring
                .SelectCompactColorStorageFormatForTest(true);

            Assert.That(selected, Is.EqualTo(TextureFormat.BC6H));
        }

        [Test]
        public void CompactColorStorage_Bc6hReconstructionFailure_SelectsRgbaHalf()
        {
            TextureFormat selected = DungeonPortalBakedBasisAuthoring
                .SelectCompactColorStorageFormatForTest(false);

            Assert.That(selected, Is.EqualTo(TextureFormat.RGBAHalf));
        }

        [Test]
        public void CompactMomentStorage_Bc7ReconstructionPass_SelectsBc7()
        {
            TextureFormat selected = DungeonPortalBakedBasisAuthoring
                .SelectCompactMomentStorageFormatForTest(true);

            Assert.That(selected, Is.EqualTo(TextureFormat.BC7));
        }

        [Test]
        public void CompactMomentStorage_Bc7ReconstructionFailure_SelectsRgbaHalf()
        {
            TextureFormat selected = DungeonPortalBakedBasisAuthoring
                .SelectCompactMomentStorageFormatForTest(false);

            Assert.That(selected, Is.EqualTo(TextureFormat.RGBAHalf));
        }

        [Test]
        public void BaseTransitionColorStorage_Bc6hReconstructionPass_SelectsBc6h()
        {
            TextureFormat selected = DungeonPortalBakedBasisAuthoring
                .SelectBaseTransitionColorStorageFormatForTest(true);

            Assert.That(selected, Is.EqualTo(TextureFormat.BC6H));
        }

        [Test]
        public void BaseTransitionColorStorage_Bc6hReconstructionFailure_SelectsRgbaHalf()
        {
            TextureFormat selected = DungeonPortalBakedBasisAuthoring
                .SelectBaseTransitionColorStorageFormatForTest(false);

            Assert.That(selected, Is.EqualTo(TextureFormat.RGBAHalf));
        }

        [TestCase(0.001f, 0f,
            DungeonPortalBakedBasisAuthoring.CompactSignClassification.QuantizedToZero)]
        [TestCase(-0.001f, 0f,
            DungeonPortalBakedBasisAuthoring.CompactSignClassification.QuantizedToZero)]
        [TestCase(0.001f, -0.001f,
            DungeonPortalBakedBasisAuthoring.CompactSignClassification.Opposite)]
        [TestCase(-0.001f, 0.001f,
            DungeonPortalBakedBasisAuthoring.CompactSignClassification.Opposite)]
        [TestCase(0.001f, 0.00075f,
            DungeonPortalBakedBasisAuthoring.CompactSignClassification.Preserved)]
        [TestCase(0.0001f, -1f,
            DungeonPortalBakedBasisAuthoring.CompactSignClassification.Preserved)]
        public void CompactSignGate_DistinguishesZeroQuantizationFromInversion(
            float expected,
            float reconstructed,
            DungeonPortalBakedBasisAuthoring.CompactSignClassification classification)
        {
            Assert.That(
                DungeonPortalBakedBasisAuthoring.ClassifyCompactSign(
                    expected,
                    reconstructed),
                Is.EqualTo(classification));
        }

        private static DungeonTileBakeData.RendererBakeEntry Entry(
            string relativePath,
            int lightmapIndex,
            Vector4 scaleOffset)
        {
            return new DungeonTileBakeData.RendererBakeEntry
            {
                relativePath = relativePath,
                lightmapIndex = lightmapIndex,
                lightmapScaleOffset = scaleOffset
            };
        }
    }
}
