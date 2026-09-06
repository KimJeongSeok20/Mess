using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DungeonPortalBakedBasisPoC.Validation.Tests.Editor
{
    public sealed class DungeonPortalBakedBasisCompactResponseTests
    {
        private const string ComposeShaderName =
            "Hidden/DungeonPortalBakedBasisPoC/Compose";

        private readonly List<UnityEngine.Object> ownedObjects =
            new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = ownedObjects.Count - 1; i >= 0; i--)
            {
                if (ownedObjects[i] != null)
                    UnityEngine.Object.DestroyImmediate(ownedObjects[i]);
            }
            ownedObjects.Clear();
        }

        [Test]
        public void ConfigureCompactResponse_StoresAllArtifactsAndClonesScaleArrays()
        {
            RequireCompactFormats();
            Texture2D colorPositive = CreateCompressed(TextureFormat.BC6H, "ColorPositive");
            Texture2D colorNegative = CreateCompressed(TextureFormat.BC6H, "ColorNegative");
            Texture2D momentPositive = CreateCompressed(TextureFormat.BC7, "MomentPositive");
            Texture2D momentNegative = CreateCompressed(TextureFormat.BC7, "MomentNegative");
            Vector4[] colorPositiveScales = CreateScales(colorPositive.mipmapCount, 1f);
            Vector4[] colorNegativeScales = CreateScales(colorPositive.mipmapCount, 2f);
            Vector4[] momentPositiveScales = CreateScales(colorPositive.mipmapCount, 3f);
            Vector4[] momentNegativeScales = CreateScales(colorPositive.mipmapCount, 4f);

            var response = new DungeonPortalBakedRoomBasisData.ResponseAtlas();
            response.ConfigureNormalizedPositiveNegativeDeltaAuthoring(
                7,
                colorPositive,
                colorNegative,
                colorPositiveScales,
                colorNegativeScales,
                momentPositive,
                momentNegative,
                momentPositiveScales,
                momentNegativeScales);

            Assert.That(response.BucketIndex, Is.EqualTo(7));
            Assert.That(
                response.Encoding,
                Is.EqualTo(DungeonPortalBakedRoomBasisData.ResponseAtlasEncoding
                    .NormalizedPositiveNegativeDelta));
            Assert.That(response.ColorDeltaPositive, Is.SameAs(colorPositive));
            Assert.That(response.ColorDeltaNegative, Is.SameAs(colorNegative));
            Assert.That(response.DirectionalMomentDeltaPositive, Is.SameAs(momentPositive));
            Assert.That(response.DirectionalMomentDeltaNegative, Is.SameAs(momentNegative));
            Assert.That(response.ColorDelta, Is.Null);
            Assert.That(response.DirectionalMomentDelta, Is.Null);
            Assert.That(response.OffColor, Is.Null);
            Assert.That(response.OnColor, Is.Null);
            Assert.That(response.ColorDeltaPositiveScales, Is.Not.SameAs(colorPositiveScales));
            Assert.That(response.ColorDeltaNegativeScales, Is.Not.SameAs(colorNegativeScales));
            Assert.That(
                response.DirectionalMomentDeltaPositiveScales,
                Is.Not.SameAs(momentPositiveScales));
            Assert.That(
                response.DirectionalMomentDeltaNegativeScales,
                Is.Not.SameAs(momentNegativeScales));
            Assert.That(response.ColorDeltaPositiveScales, Is.EqualTo(colorPositiveScales));
            Assert.That(response.ColorDeltaNegativeScales, Is.EqualTo(colorNegativeScales));
            Assert.That(
                response.DirectionalMomentDeltaPositiveScales,
                Is.EqualTo(momentPositiveScales));
            Assert.That(
                response.DirectionalMomentDeltaNegativeScales,
                Is.EqualTo(momentNegativeScales));
        }

        [Test]
        public void CompactResponseValidation_AcceptsExactFormatsSamplingMipsAndPositiveScales()
        {
            RequireCompactFormats();
            CreateValidFixture(
                out DungeonPortalBakedRoomBasisData.ResponseAtlas response,
                out DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket);

            Assert.That(ValidateResponse(response, bucket, out string failure), Is.True, failure);
        }

        [TestCase(TextureFormat.BC6H, TextureFormat.BC6H, true)]
        [TestCase(TextureFormat.RGBAHalf, TextureFormat.RGBAHalf, true)]
        [TestCase(TextureFormat.BC6H, TextureFormat.RGBAHalf, false)]
        [TestCase(TextureFormat.RGBAHalf, TextureFormat.BC6H, false)]
        [TestCase(TextureFormat.RGBA32, TextureFormat.RGBA32, false)]
        public void CompactResponseValidation_RequiresBc6hOrRgbaHalfSameFormatColorPair(
            TextureFormat positiveFormat,
            TextureFormat negativeFormat,
            bool expectedValid)
        {
            RequireCompactFormats();
            CreateValidFixture(
                out DungeonPortalBakedRoomBasisData.ResponseAtlas response,
                out DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket);
            ConfigureColorStoragePair(response, bucket, positiveFormat, negativeFormat);

            bool actual = ValidateResponse(response, bucket, out string failure);
            Assert.That(actual, Is.EqualTo(expectedValid), failure);
            if (expectedValid)
                Assert.That(failure, Is.Null);
            else if (positiveFormat != negativeFormat)
                StringAssert.Contains("same format", failure);
            else
                StringAssert.Contains("BC6H or RGBAHalf", failure);
        }

        [TestCase(TextureFormat.BC6H, TextureFormat.BC6H, true)]
        [TestCase(TextureFormat.RGBAHalf, TextureFormat.RGBAHalf, true)]
        [TestCase(TextureFormat.BC6H, TextureFormat.RGBAHalf, false)]
        [TestCase(TextureFormat.RGBAHalf, TextureFormat.BC6H, false)]
        [TestCase(TextureFormat.RGBA32, TextureFormat.RGBA32, false)]
        public void CompactGpuValidation_RequiresBc6hOrRgbaHalfSameFormatColorPair(
            TextureFormat positiveFormat,
            TextureFormat negativeFormat,
            bool expectedValid)
        {
            RequireCompactFormats();
            CreateValidFixture(
                out DungeonPortalBakedRoomBasisData.ResponseAtlas response,
                out DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket);
            ConfigureColorStoragePair(response, bucket, positiveFormat, negativeFormat);

            bool actual = ValidateGpuResponse(response, bucket, out string failure);
            Assert.That(actual, Is.EqualTo(expectedValid), failure);
            if (expectedValid)
                Assert.That(failure, Is.Null);
            else if (positiveFormat != negativeFormat)
                StringAssert.Contains("same format", failure);
            else
                StringAssert.Contains("BC6H or RGBAHalf", failure);
        }

        [TestCase(TextureFormat.BC7, TextureFormat.BC7, true)]
        [TestCase(TextureFormat.RGBAHalf, TextureFormat.RGBAHalf, true)]
        [TestCase(TextureFormat.BC7, TextureFormat.RGBAHalf, false)]
        [TestCase(TextureFormat.RGBAHalf, TextureFormat.BC7, false)]
        [TestCase(TextureFormat.RGBA32, TextureFormat.RGBA32, false)]
        public void CompactResponseValidation_RequiresBc7OrRgbaHalfSameFormatMomentPair(
            TextureFormat positiveFormat,
            TextureFormat negativeFormat,
            bool expectedValid)
        {
            RequireCompactFormats();
            CreateValidFixture(
                out DungeonPortalBakedRoomBasisData.ResponseAtlas response,
                out DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket);
            ConfigureMomentStoragePair(response, bucket, positiveFormat, negativeFormat);

            bool actual = ValidateResponse(response, bucket, out string failure);
            Assert.That(actual, Is.EqualTo(expectedValid), failure);
            if (expectedValid)
                Assert.That(failure, Is.Null);
            else if (positiveFormat != negativeFormat)
                StringAssert.Contains("same format", failure);
            else
                StringAssert.Contains("BC7 or RGBAHalf", failure);
        }

        [TestCase(TextureFormat.BC7, TextureFormat.BC7, true)]
        [TestCase(TextureFormat.RGBAHalf, TextureFormat.RGBAHalf, true)]
        [TestCase(TextureFormat.BC7, TextureFormat.RGBAHalf, false)]
        [TestCase(TextureFormat.RGBAHalf, TextureFormat.BC7, false)]
        [TestCase(TextureFormat.RGBA32, TextureFormat.RGBA32, false)]
        public void CompactGpuValidation_RequiresBc7OrRgbaHalfSameFormatMomentPair(
            TextureFormat positiveFormat,
            TextureFormat negativeFormat,
            bool expectedValid)
        {
            RequireCompactFormats();
            CreateValidFixture(
                out DungeonPortalBakedRoomBasisData.ResponseAtlas response,
                out DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket);
            ConfigureMomentStoragePair(response, bucket, positiveFormat, negativeFormat);

            bool actual = ValidateGpuResponse(response, bucket, out string failure);
            Assert.That(actual, Is.EqualTo(expectedValid), failure);
            if (expectedValid)
                Assert.That(failure, Is.Null);
            else if (positiveFormat != negativeFormat)
                StringAssert.Contains("same format", failure);
            else
                StringAssert.Contains("BC7 or RGBAHalf", failure);
        }

        [Test]
        public void CompactResponseValidation_RejectsSamplingMismatch()
        {
            RequireCompactFormats();
            CreateValidFixture(
                out DungeonPortalBakedRoomBasisData.ResponseAtlas response,
                out DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket);
            response.ColorDeltaNegative.filterMode = FilterMode.Point;

            Assert.That(ValidateResponse(response, bucket, out string failure), Is.False);
            StringAssert.Contains("sampling state", failure);
        }

        [Test]
        public void CompactResponseValidation_RejectsMissingPerMipScale()
        {
            RequireCompactFormats();
            CreateValidFixture(
                out DungeonPortalBakedRoomBasisData.ResponseAtlas response,
                out DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket);
            Vector4[] shortScales = CreateScales(
                response.ColorDeltaPositive.mipmapCount - 1,
                1f);
            response.ConfigureNormalizedPositiveNegativeDeltaAuthoring(
                response.BucketIndex,
                response.ColorDeltaPositive,
                response.ColorDeltaNegative,
                shortScales,
                response.ColorDeltaNegativeScales,
                response.DirectionalMomentDeltaPositive,
                response.DirectionalMomentDeltaNegative,
                response.DirectionalMomentDeltaPositiveScales,
                response.DirectionalMomentDeltaNegativeScales);

            Assert.That(ValidateResponse(response, bucket, out string failure), Is.False);
            StringAssert.Contains("exactly one", failure);
        }

        [TestCase(0f)]
        [TestCase(-0.01f)]
        public void CompactResponseValidation_RejectsNonPositiveScale(float invalidScale)
        {
            RequireCompactFormats();
            CreateValidFixture(
                out DungeonPortalBakedRoomBasisData.ResponseAtlas response,
                out DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket);
            response.ColorDeltaPositiveScales[0] =
                new Vector4(invalidScale, 1f, 1f, 1f);

            Assert.That(ValidateResponse(response, bucket, out string failure), Is.False);
            StringAssert.Contains("strictly positive", failure);
        }

        [Test]
        public void CompactResponseValidation_RejectsNonFiniteScale()
        {
            RequireCompactFormats();
            CreateValidFixture(
                out DungeonPortalBakedRoomBasisData.ResponseAtlas response,
                out DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket);
            response.DirectionalMomentDeltaNegativeScales[0] =
                new Vector4(1f, float.NaN, 1f, 1f);

            Assert.That(ValidateResponse(response, bucket, out string failure), Is.False);
            StringAssert.Contains("finite", failure);
        }

        [Test]
        public void CompactResponseShader_ReconstructsPositiveMinusNegativeAtSelectedMip()
        {
            Shader shader = Shader.Find(ComposeShaderName);
            Assert.That(shader, Is.Not.Null, $"Could not find {ComposeShaderName}.");
            string assetPath = AssetDatabase.GetAssetPath(shader);
            Assert.That(assetPath, Is.Not.Empty);
            string source = File.ReadAllText(Path.GetFullPath(assetPath));
            string normalized = Regex.Replace(source, @"\s+", " ");

            StringAssert.Contains(
                "positiveNormalized * _ColorDeltaPositiveScales[mip].rgb - " +
                "negativeNormalized * _ColorDeltaNegativeScales[mip].rgb",
                normalized);
            StringAssert.Contains(
                "positiveNormalized * _DirectionalMomentDeltaPositiveScales[mip] - " +
                "negativeNormalized * _DirectionalMomentDeltaNegativeScales[mip]",
                normalized);
            StringAssert.Contains("int mip = DpbbNormalizedDeltaMip();", normalized);

            Vector4 positiveNormalized = new Vector4(0.5f, 0.25f, 0f, 1f);
            Vector4 negativeNormalized = new Vector4(0.1f, 0.5f, 0.75f, 0.25f);
            Vector4 positiveScale = new Vector4(4f, 8f, 2f, 0.5f);
            Vector4 negativeScale = new Vector4(10f, 2f, 4f, 2f);
            Vector4 reconstructed =
                Vector4.Scale(positiveNormalized, positiveScale) -
                Vector4.Scale(negativeNormalized, negativeScale);
            Assert.That(reconstructed, Is.EqualTo(new Vector4(1f, 1f, -3f, 0f)));
        }

        private void CreateValidFixture(
            out DungeonPortalBakedRoomBasisData.ResponseAtlas response,
            out DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket)
        {
            Texture2D baseColor = CreateHalf("BaseColor");
            Texture2D baseDirection = CreateHalf("BaseDirection");
            bucket = new DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket();
            bucket.ConfigureAuthoring(
                "LM0",
                0,
                baseColor,
                baseColor,
                baseDirection,
                baseDirection);

            Texture2D colorPositive = CreateCompressed(TextureFormat.BC6H, "ColorPositive");
            Texture2D colorNegative = CreateCompressed(TextureFormat.BC6H, "ColorNegative");
            Texture2D momentPositive = CreateCompressed(TextureFormat.BC7, "MomentPositive");
            Texture2D momentNegative = CreateCompressed(TextureFormat.BC7, "MomentNegative");
            CopySampling(baseColor, colorPositive);
            CopySampling(baseColor, colorNegative);
            CopySampling(baseDirection, momentPositive);
            CopySampling(baseDirection, momentNegative);

            int mipCount = baseColor.mipmapCount;
            response = new DungeonPortalBakedRoomBasisData.ResponseAtlas();
            response.ConfigureNormalizedPositiveNegativeDeltaAuthoring(
                0,
                colorPositive,
                colorNegative,
                CreateScales(mipCount, 1f),
                CreateScales(mipCount, 2f),
                momentPositive,
                momentNegative,
                CreateScales(mipCount, 3f),
                CreateScales(mipCount, 4f));
        }

        private static bool ValidateResponse(
            DungeonPortalBakedRoomBasisData.ResponseAtlas response,
            DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket,
            out string failure)
        {
            MethodInfo method = typeof(DungeonPortalBakedRoomBasisData).GetMethod(
                "TryValidateResponse",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            object[] arguments = { response, bucket, null };
            bool result = (bool)method.Invoke(null, arguments);
            failure = arguments[2] as string;
            return result;
        }

        private static bool ValidateGpuResponse(
            DungeonPortalBakedRoomBasisData.ResponseAtlas response,
            DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket,
            out string failure)
        {
            Type composerType = typeof(DungeonPortalBakedRoomBasisData).Assembly.GetType(
                "DungeonPortalBakedBasisPoC.DungeonPortalBakedBasisGpuComposer",
                false);
            Assert.That(composerType, Is.Not.Null);
            MethodInfo method = composerType.GetMethod(
                "TryValidateCompactContribution",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            object[] arguments = { response, bucket, null };
            bool result = (bool)method.Invoke(null, arguments);
            failure = arguments[2] as string;
            return result;
        }

        private void ConfigureColorStoragePair(
            DungeonPortalBakedRoomBasisData.ResponseAtlas response,
            DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket,
            TextureFormat positiveFormat,
            TextureFormat negativeFormat)
        {
            Texture2D positive = positiveFormat == TextureFormat.BC6H
                ? response.ColorDeltaPositive
                : CreateLinearTexture(positiveFormat, "ColorPositive_" + positiveFormat);
            Texture2D negative = negativeFormat == TextureFormat.BC6H
                ? response.ColorDeltaNegative
                : CreateLinearTexture(negativeFormat, "ColorNegative_" + negativeFormat);
            CopySampling(bucket.Power0Color, positive);
            CopySampling(bucket.Power0Color, negative);
            response.ConfigureNormalizedPositiveNegativeDeltaAuthoring(
                response.BucketIndex,
                positive,
                negative,
                response.ColorDeltaPositiveScales,
                response.ColorDeltaNegativeScales,
                response.DirectionalMomentDeltaPositive,
                response.DirectionalMomentDeltaNegative,
                response.DirectionalMomentDeltaPositiveScales,
                response.DirectionalMomentDeltaNegativeScales);
        }

        private void ConfigureMomentStoragePair(
            DungeonPortalBakedRoomBasisData.ResponseAtlas response,
            DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket,
            TextureFormat positiveFormat,
            TextureFormat negativeFormat)
        {
            Texture2D positive = positiveFormat == TextureFormat.BC7
                ? response.DirectionalMomentDeltaPositive
                : CreateLinearTexture(positiveFormat, "MomentPositive_" + positiveFormat);
            Texture2D negative = negativeFormat == TextureFormat.BC7
                ? response.DirectionalMomentDeltaNegative
                : CreateLinearTexture(negativeFormat, "MomentNegative_" + negativeFormat);
            CopySampling(bucket.Power0Direction, positive);
            CopySampling(bucket.Power0Direction, negative);
            response.ConfigureNormalizedPositiveNegativeDeltaAuthoring(
                response.BucketIndex,
                response.ColorDeltaPositive,
                response.ColorDeltaNegative,
                response.ColorDeltaPositiveScales,
                response.ColorDeltaNegativeScales,
                positive,
                negative,
                response.DirectionalMomentDeltaPositiveScales,
                response.DirectionalMomentDeltaNegativeScales);
        }

        private Texture2D CreateHalf(string name)
        {
            return CreateLinearTexture(TextureFormat.RGBAHalf, name);
        }

        private Texture2D CreateLinearTexture(TextureFormat format, string name)
        {
            Assert.That(
                format == TextureFormat.RGBAHalf || format == TextureFormat.RGBA32,
                Is.True,
                $"Unsupported test texture format {format}.");
            var texture = new Texture2D(8, 8, format, true, true)
            {
                name = name,
                filterMode = FilterMode.Trilinear,
                wrapModeU = TextureWrapMode.Clamp,
                wrapModeV = TextureWrapMode.Mirror,
                wrapModeW = TextureWrapMode.Repeat,
                anisoLevel = 2,
                mipMapBias = -0.25f
            };
            Color[] pixels = new Color[texture.width * texture.height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(0.25f, 0.5f, 0.75f, 0.5f);
            texture.SetPixels(pixels);
            texture.Apply(true, false);
            ownedObjects.Add(texture);
            return texture;
        }

        private Texture2D CreateCompressed(TextureFormat format, string name)
        {
            Texture2D texture = CreateHalf(name);
            EditorUtility.CompressTexture(
                texture,
                format,
                UnityEditor.TextureCompressionQuality.Best);
            Assert.That(texture.format, Is.EqualTo(format));
            return texture;
        }

        private static Vector4[] CreateScales(int mipCount, float seed)
        {
            var values = new Vector4[mipCount];
            for (int mip = 0; mip < values.Length; mip++)
            {
                float value = seed + mip * 0.125f;
                values[mip] = new Vector4(value, value + 0.01f, value + 0.02f, value + 0.03f);
            }
            return values;
        }

        private static void CopySampling(Texture2D source, Texture2D destination)
        {
            destination.filterMode = source.filterMode;
            destination.wrapModeU = source.wrapModeU;
            destination.wrapModeV = source.wrapModeV;
            destination.wrapModeW = source.wrapModeW;
            destination.anisoLevel = source.anisoLevel;
            destination.mipMapBias = source.mipMapBias;
        }

        private static void RequireCompactFormats()
        {
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.BC6H) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.BC7))
            {
                Assert.Ignore("This editor device cannot sample the required BC6H/BC7 formats.");
            }
        }
    }
}
