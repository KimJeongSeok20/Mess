using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace DungeonPortalBakedBasisPoC.Validation.Tests.Editor
{
    public sealed class DungeonPortalBakedBasisBaseTransitionTests
    {
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                if (owned[i] != null)
                    UnityEngine.Object.DestroyImmediate(owned[i]);
            }
            owned.Clear();
        }

        [Test]
        public void DualLayouts_RetainCapturedEndpointAndExposeRealPowerPair()
        {
            Texture2D p0ExactColor = CreateTexture(4, "P0ExactColor");
            Texture2D p0ExactDirection = CreateTexture(4, "P0ExactDirection");
            Texture2D p100InP0Color = CreateTexture(4, "P100InP0Color");
            Texture2D p100InP0Direction = CreateTexture(
                4, "P100InP0Direction", TextureFormat.RGBA32);
            Texture2D p100ExactColor = CreateTexture(8, "P100ExactColor");
            Texture2D p100ExactDirection = CreateTexture(8, "P100ExactDirection");
            Texture2D p0InP100Color = CreateTexture(8, "P0InP100Color");
            Texture2D p0InP100Direction = CreateTexture(
                8, "P0InP100Direction", TextureFormat.RGBA32);

            DungeonPortalBakedRoomBasisData data = CreateData(
                p0ExactColor,
                p0ExactDirection,
                p100InP0Color,
                p100InP0Direction,
                p100ExactColor,
                p100ExactDirection,
                p0InP100Color,
                p0InP100Direction,
                null,
                null);

            Assert.That(data.TryValidateBaseTransitionLayouts(out string failure), Is.True, failure);
            Assert.That(
                data.TryGetBaseTransitionAtlas(
                    DungeonPortalBakedRoomBasisData.ProductionEndpoint.Power0,
                    0,
                    out DungeonPortalBakedRoomBasisData.BaseTransitionAtlas p0Layout,
                    out failure),
                Is.True,
                failure);
            Assert.That(p0Layout.LayoutEndpoint,
                Is.EqualTo(DungeonPortalBakedRoomBasisData.ProductionEndpoint.Power0));
            Assert.That(p0Layout.Power0Color, Is.SameAs(p0ExactColor));
            Assert.That(p0Layout.Power100Color, Is.SameAs(p100InP0Color));
            Assert.That(p0Layout.Power100Color.format, Is.EqualTo(TextureFormat.RGBAHalf));
            Assert.That(
                GraphicsFormatUtility.IsSRGBFormat(p0Layout.Power100Color.graphicsFormat),
                Is.False);
            AssertMatchingLayoutSampling(p0ExactColor, p0Layout.Power100Color);
            Assert.That(p0Layout.Power100Direction.format, Is.EqualTo(TextureFormat.RGBA32));
            AssertMatchingLayoutSampling(p0ExactDirection, p0Layout.Power100Direction);

            Assert.That(
                data.TryGetBaseTransitionAtlas(
                    DungeonPortalBakedRoomBasisData.ProductionEndpoint.Power100,
                    0,
                    out DungeonPortalBakedRoomBasisData.BaseTransitionAtlas p100Layout,
                    out failure),
                Is.True,
                failure);
            Assert.That(p100Layout.LayoutEndpoint,
                Is.EqualTo(DungeonPortalBakedRoomBasisData.ProductionEndpoint.Power100));
            Assert.That(p100Layout.Power0Direction.format, Is.EqualTo(TextureFormat.RGBA32));
            AssertMatchingLayoutSampling(p100ExactDirection, p100Layout.Power0Direction);
            Assert.That(p100Layout.Power0Color, Is.SameAs(p0InP100Color));
            Assert.That(p100Layout.Power0Color.format, Is.EqualTo(TextureFormat.RGBAHalf));
            Assert.That(
                GraphicsFormatUtility.IsSRGBFormat(p100Layout.Power0Color.graphicsFormat),
                Is.False);
            AssertMatchingLayoutSampling(p100ExactColor, p100Layout.Power0Color);
            Assert.That(p100Layout.Power100Color, Is.SameAs(p100ExactColor));
        }

        [Test]
        public void DualLayouts_AcceptBc6hRepackedTransitionColorAndRgba32Direction()
        {
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.BC6H))
                Assert.Ignore("This editor device cannot compress/sample BC6H.");

            Texture2D p0ExactColor = CreateTexture(4, "P0ExactColor_Bc6hCase");
            Texture2D p0ExactDirection = CreateTexture(4, "P0ExactDirection_Bc6hCase");
            Texture2D p100InP0Color = CreateBc6hTexture(4, "P100InP0Color_Bc6h");
            Texture2D p100InP0Direction = CreateTexture(
                4, "P100InP0Direction_Rgba32", TextureFormat.RGBA32);
            Texture2D p100ExactColor = CreateTexture(8, "P100ExactColor_Bc6hCase");
            Texture2D p100ExactDirection = CreateTexture(8, "P100ExactDirection_Bc6hCase");
            Texture2D p0InP100Color = CreateBc6hTexture(8, "P0InP100Color_Bc6h");
            Texture2D p0InP100Direction = CreateTexture(
                8, "P0InP100Direction_Rgba32", TextureFormat.RGBA32);

            DungeonPortalBakedRoomBasisData data = CreateData(
                p0ExactColor,
                p0ExactDirection,
                p100InP0Color,
                p100InP0Direction,
                p100ExactColor,
                p100ExactDirection,
                p0InP100Color,
                p0InP100Direction,
                null,
                null);

            Assert.That(data.TryValidateBaseTransitionLayouts(out string failure), Is.True, failure);
            Assert.That(
                data.TryGetBaseTransitionAtlas(
                    DungeonPortalBakedRoomBasisData.ProductionEndpoint.Power0,
                    0,
                    out DungeonPortalBakedRoomBasisData.BaseTransitionAtlas p0Layout,
                    out failure),
                Is.True,
                failure);
            Assert.That(p0Layout.Power100Color.format, Is.EqualTo(TextureFormat.BC6H));
            Assert.That(p0Layout.Power100Direction.format, Is.EqualTo(TextureFormat.RGBA32));
            AssertMatchingLayoutSampling(p0ExactColor, p0Layout.Power100Color);
            AssertMatchingLayoutSampling(p0ExactDirection, p0Layout.Power100Direction);

            Assert.That(
                data.TryGetBaseTransitionAtlas(
                    DungeonPortalBakedRoomBasisData.ProductionEndpoint.Power100,
                    0,
                    out DungeonPortalBakedRoomBasisData.BaseTransitionAtlas p100Layout,
                    out failure),
                Is.True,
                failure);
            Assert.That(p100Layout.Power0Color.format, Is.EqualTo(TextureFormat.BC6H));
            Assert.That(p100Layout.Power0Direction.format, Is.EqualTo(TextureFormat.RGBA32));
            AssertMatchingLayoutSampling(p100ExactColor, p100Layout.Power0Color);
            AssertMatchingLayoutSampling(p100ExactDirection, p100Layout.Power0Direction);
        }

        [Test]
        public void NonNullShadowMaskMismatch_FailsClosed()
        {
            Texture2D p0Shadow = CreateTexture(4, "P0Shadow");
            Texture2D p100Shadow = CreateTexture(4, "P100Shadow");
            DungeonPortalBakedRoomBasisData data = CreateData(
                CreateTexture(4, "P0C"),
                CreateTexture(4, "P0D"),
                CreateTexture(4, "P100InP0C"),
                CreateTexture(4, "P100InP0D"),
                CreateTexture(4, "P100C"),
                CreateTexture(4, "P100D"),
                CreateTexture(4, "P0InP100C"),
                CreateTexture(4, "P0InP100D"),
                p0Shadow,
                p100Shadow);

            Assert.That(data.TryValidateDefinition(out string definitionFailure), Is.True, definitionFailure);
            Assert.That(data.TryValidateBaseTransitionLayouts(out string failure), Is.False);
            StringAssert.Contains("shadow-mask mismatch", failure);
        }

        private DungeonPortalBakedRoomBasisData CreateData(
            Texture2D p0Color,
            Texture2D p0Direction,
            Texture2D p100InP0Color,
            Texture2D p100InP0Direction,
            Texture2D p100Color,
            Texture2D p100Direction,
            Texture2D p0InP100Color,
            Texture2D p0InP100Direction,
            Texture2D p0Shadow,
            Texture2D p100Shadow)
        {
            var p0 = new DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket();
            p0.ConfigureAuthoring(
                "P0_LM00",
                0,
                p0Color,
                p100InP0Color,
                p0Direction,
                p100InP0Direction,
                p0Shadow);
            var renderer = new DungeonPortalBakedRoomBasisData.CanonicalRendererEntry();
            renderer.ConfigureAuthoring("Geometry/Wall#0", 0, 0, Vector4.one, 7, Vector4.one);
            var p100 = new DungeonPortalBakedRoomBasisData.Power100SourceAtlas();
            p100.ConfigureTransitionAuthoring(
                7,
                p100Color,
                p100Direction,
                p0InP100Color,
                p0InP100Direction,
                p100Shadow);
            var data = ScriptableObject.CreateInstance<DungeonPortalBakedRoomBasisData>();
            owned.Add(data);
            data.ConfigureAuthoring(
                "TransitionRoom",
                new[] { p0 },
                new[] { renderer },
                new[] { p100 },
                Array.Empty<DungeonPortalBakedRoomBasisData.ReceiverDoorBasis>(),
                Array.Empty<DungeonPortalBakedRoomBasisData.SourceDoorBasis>());
            return data;
        }

        private Texture2D CreateTexture(
            int size,
            string name,
            TextureFormat format = TextureFormat.RGBAHalf)
        {
            var texture = new Texture2D(size, size, format, true, true)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapModeU = TextureWrapMode.Clamp,
                wrapModeV = TextureWrapMode.Clamp,
                wrapModeW = TextureWrapMode.Clamp,
                anisoLevel = 1,
                mipMapBias = 0f
            };
            texture.Apply(true, false);
            owned.Add(texture);
            return texture;
        }

        private Texture2D CreateBc6hTexture(int size, string name)
        {
            Texture2D texture = CreateTexture(size, name);
            EditorUtility.CompressTexture(
                texture,
                TextureFormat.BC6H,
                TextureCompressionQuality.Best);
            Assert.That(texture.format, Is.EqualTo(TextureFormat.BC6H));
            Assert.That(GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat), Is.False);
            return texture;
        }

        private static void AssertMatchingLayoutSampling(
            Texture2D nativeLayout,
            Texture2D repacked)
        {
            Assert.That(repacked.width, Is.EqualTo(nativeLayout.width));
            Assert.That(repacked.height, Is.EqualTo(nativeLayout.height));
            Assert.That(repacked.mipmapCount, Is.EqualTo(nativeLayout.mipmapCount));
            Assert.That(repacked.filterMode, Is.EqualTo(nativeLayout.filterMode));
            Assert.That(repacked.wrapModeU, Is.EqualTo(nativeLayout.wrapModeU));
            Assert.That(repacked.wrapModeV, Is.EqualTo(nativeLayout.wrapModeV));
            Assert.That(repacked.wrapModeW, Is.EqualTo(nativeLayout.wrapModeW));
            Assert.That(repacked.anisoLevel, Is.EqualTo(nativeLayout.anisoLevel));
            Assert.That(repacked.mipMapBias, Is.EqualTo(nativeLayout.mipMapBias));
        }
    }
}
