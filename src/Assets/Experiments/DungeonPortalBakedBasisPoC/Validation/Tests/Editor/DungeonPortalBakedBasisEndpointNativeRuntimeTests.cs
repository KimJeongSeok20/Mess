using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace DungeonPortalBakedBasisPoC.Validation.Tests.Editor
{
    public sealed class DungeonPortalBakedBasisEndpointNativeRuntimeTests
    {
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
        public void NativeEndpointDescriptors_PreserveIndependentAtlasIdentityAndLayout()
        {
            Texture2D p0Color = CreateLinearHalfTexture(4, "P0_Color");
            Texture2D p0Direction = CreateLinearHalfTexture(4, "P0_Direction");
            Texture2D p0Shadow = CreateShadowTexture(4, "P0_Shadow");
            Texture2D p100Color = CreateLinearHalfTexture(8, "P100_Color");
            Texture2D p100Direction = CreateLinearHalfTexture(8, "P100_Direction");
            Texture2D p100Shadow = CreateShadowTexture(8, "P100_Shadow");
            Vector4 p0St = new Vector4(0.25f, 0.5f, 0.1f, 0.2f);
            Vector4 p100St = new Vector4(0.5f, 0.25f, 0.3f, 0.4f);

            DungeonPortalBakedRoomBasisData data = CreateEndpointData(
                p0Color,
                p0Direction,
                p0Shadow,
                p100Color,
                p100Direction,
                p100Shadow,
                p0St,
                p100St,
                out DungeonPortalBakedRoomBasisData.CanonicalRendererEntry renderer,
                out _);

            Assert.That(
                data.TryGetNativeEndpointAtlas(
                    DungeonPortalBakedRoomBasisData.ProductionEndpoint.Power0,
                    0,
                    out DungeonPortalBakedRoomBasisData.NativeEndpointAtlas p0),
                Is.True);
            Assert.That(p0.NativeLocalLightmapIndex, Is.EqualTo(0));
            Assert.That(p0.Color, Is.SameAs(p0Color));
            Assert.That(p0.Direction, Is.SameAs(p0Direction));
            Assert.That(p0.ShadowMask, Is.SameAs(p0Shadow));
            Assert.That(p0.Color.width, Is.EqualTo(4));
            Assert.That(p0.Color.mipmapCount, Is.EqualTo(p0Color.mipmapCount));

            Assert.That(
                data.TryGetNativeEndpointAtlas(
                    DungeonPortalBakedRoomBasisData.ProductionEndpoint.Power100,
                    0,
                    out DungeonPortalBakedRoomBasisData.NativeEndpointAtlas p100),
                Is.True);
            Assert.That(p100.NativeLocalLightmapIndex, Is.EqualTo(7));
            Assert.That(p100.Color, Is.SameAs(p100Color));
            Assert.That(p100.Direction, Is.SameAs(p100Direction));
            Assert.That(p100.ShadowMask, Is.SameAs(p100Shadow));
            Assert.That(p100.Color.width, Is.EqualTo(8));
            Assert.That(p100.Color.mipmapCount, Is.EqualTo(p100Color.mipmapCount));

            Assert.That(
                renderer.GetNativeLocalLightmapIndex(
                    DungeonPortalBakedRoomBasisData.ProductionEndpoint.Power0),
                Is.EqualTo(0));
            Assert.That(
                renderer.GetNativeLocalLightmapIndex(
                    DungeonPortalBakedRoomBasisData.ProductionEndpoint.Power100),
                Is.EqualTo(7));
            Assert.That(
                renderer.GetNativeScaleOffset(
                    DungeonPortalBakedRoomBasisData.ProductionEndpoint.Power0),
                Is.EqualTo(p0St));
            Assert.That(
                renderer.GetNativeScaleOffset(
                    DungeonPortalBakedRoomBasisData.ProductionEndpoint.Power100),
                Is.EqualTo(p100St));
        }

        [Test]
        public void EndpointResponseAndSourcePower_FormIndependentBinaryMatrix()
        {
            var p0Response = new DungeonPortalBakedRoomBasisData.ResponseAtlas();
            p0Response.ConfigureOnOffAuthoring(0, null, null, null, null);
            var p100Response = new DungeonPortalBakedRoomBasisData.ResponseAtlas();
            p100Response.ConfigureOnOffAuthoring(0, null, null, null, null);
            var lobe = new DungeonPortalBakedRoomBasisData.ReceiverResponseLobe();
            lobe.ConfigureEndpointAuthoring(
                "K1",
                new[] { p0Response },
                new[] { p100Response });
            var source = new DungeonPortalBakedRoomBasisData.SourceBasisCoefficient();
            source.ConfigureAuthoring(
                "K1",
                new Color(1f, 2f, 3f),
                new Color(4f, 5f, 6f));

            DungeonPortalBakedRoomBasisData.ProductionEndpoint[] endpoints =
            {
                DungeonPortalBakedRoomBasisData.ProductionEndpoint.Power0,
                DungeonPortalBakedRoomBasisData.ProductionEndpoint.Power100
            };
            float[] sourcePowers = { 0f, 1f };
            for (int endpointIndex = 0; endpointIndex < endpoints.Length; endpointIndex++)
            {
                for (int sourceIndex = 0; sourceIndex < sourcePowers.Length; sourceIndex++)
                {
                    Assert.That(
                        lobe.TryGetAtlas(endpoints[endpointIndex], 0, out var selected),
                        Is.True,
                        $"receiver endpoint={endpoints[endpointIndex]}, source=P{sourceIndex * 100}");
                    Assert.That(
                        selected,
                        Is.SameAs(endpointIndex == 0 ? p0Response : p100Response));
                    Assert.That(
                        source.Evaluate(sourcePowers[sourceIndex]),
                        Is.EqualTo(sourceIndex == 0
                            ? new Color(1f, 2f, 3f)
                            : new Color(4f, 5f, 6f)));
                }
            }
        }

        [Test]
        public void RuntimeWorkingTexture_PreservesCapturedNativeMipAndSamplingContract()
        {
            Texture2D prototype = CreateLinearHalfTexture(8, "P100_Prototype");
            prototype.filterMode = FilterMode.Trilinear;
            prototype.wrapModeU = TextureWrapMode.Clamp;
            prototype.wrapModeV = TextureWrapMode.Mirror;
            prototype.wrapModeW = TextureWrapMode.Repeat;
            prototype.anisoLevel = 4;
            prototype.mipMapBias = 0.375f;

            Type composerType = typeof(DungeonPortalBakedBasisRoomCompositor).Assembly.GetType(
                "DungeonPortalBakedBasisPoC.DungeonPortalBakedBasisGpuComposer");
            Assert.That(composerType, Is.Not.Null);
            MethodInfo create = composerType.GetMethod(
                "CreateRuntimeLightmapTexture",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            Assert.That(create, Is.Not.Null);

            var working = (Texture2D)create.Invoke(
                null,
                new object[] { prototype, "__EndpointNativeTest" });
            ownedObjects.Add(working);

            Assert.That(working.width, Is.EqualTo(prototype.width));
            Assert.That(working.height, Is.EqualTo(prototype.height));
            Assert.That(working.mipmapCount, Is.EqualTo(prototype.mipmapCount));
            Assert.That(working.format, Is.EqualTo(TextureFormat.RGBAHalf));
            Assert.That(working.filterMode, Is.EqualTo(prototype.filterMode));
            Assert.That(working.wrapModeU, Is.EqualTo(prototype.wrapModeU));
            Assert.That(working.wrapModeV, Is.EqualTo(prototype.wrapModeV));
            Assert.That(working.wrapModeW, Is.EqualTo(prototype.wrapModeW));
            Assert.That(working.anisoLevel, Is.EqualTo(prototype.anisoLevel));
            Assert.That(working.mipMapBias, Is.EqualTo(prototype.mipMapBias));
        }

        [Test]
        public void DefinitionValidation_RequiresEachResponseToMatchItsNativeEndpointLayout()
        {
            Texture2D p0Color = CreateLinearHalfTexture(4, "P0_Color");
            Texture2D p0Direction = CreateLinearHalfTexture(4, "P0_Direction");
            Texture2D p0Shadow = CreateShadowTexture(4, "P0_Shadow");
            Texture2D p100Color = CreateLinearHalfTexture(8, "P100_Color");
            Texture2D p100Direction = CreateLinearHalfTexture(8, "P100_Direction");
            Texture2D p100Shadow = CreateShadowTexture(8, "P100_Shadow");

            DungeonPortalBakedRoomBasisData data = CreateEndpointData(
                p0Color,
                p0Direction,
                p0Shadow,
                p100Color,
                p100Direction,
                p100Shadow,
                Vector4.one,
                new Vector4(0.5f, 0.5f, 0.25f, 0.25f),
                out _,
                out DungeonPortalBakedRoomBasisData.ReceiverResponseLobe lobe);

            Assert.That(data.TryValidateDefinition(out string validFailure), Is.True, validFailure);

            var wrongP100 = new DungeonPortalBakedRoomBasisData.ResponseAtlas();
            wrongP100.ConfigureOnOffAuthoring(
                0,
                p0Color,
                p0Color,
                p0Direction,
                p0Direction);
            lobe.ConfigureEndpointAuthoring(
                "K1",
                lobe.Power0AtlasResponses,
                new[] { wrongP100 });

            Assert.That(data.TryValidateDefinition(out string failure), Is.False);
            StringAssert.Contains("P100", failure);
        }

        [Test]
        public void MatchOriginalEndpoint_RequiresExactTextureStAndShadowmask()
        {
            Texture2D p0Color = CreateLinearHalfTexture(4, "P0_Color");
            Texture2D p0Direction = CreateLinearHalfTexture(4, "P0_Direction");
            Texture2D p0Shadow = CreateShadowTexture(4, "P0_Shadow");
            Texture2D p100Color = CreateLinearHalfTexture(8, "P100_Color");
            Texture2D p100Direction = CreateLinearHalfTexture(8, "P100_Direction");
            Texture2D p100Shadow = CreateShadowTexture(8, "P100_Shadow");
            Vector4 p0St = new Vector4(0.25f, 0.5f, 0.1f, 0.2f);
            Vector4 p100St = new Vector4(0.5f, 0.25f, 0.3f, 0.4f);

            DungeonPortalBakedRoomBasisData data = CreateEndpointData(
                p0Color,
                p0Direction,
                p0Shadow,
                p100Color,
                p100Direction,
                p100Shadow,
                p0St,
                p100St,
                out DungeonPortalBakedRoomBasisData.CanonicalRendererEntry renderer,
                out _);
            DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket =
                data.CanonicalAtlases[0];
            DungeonPortalBakedRoomBasisData.Power100SourceAtlas p100Source =
                data.Power100SourceAtlases[0];

            MethodInfo match = typeof(DungeonPortalBakedBasisRoomCompositor).GetMethod(
                "MatchOriginalEndpoint",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(match, Is.Not.Null);

            Assert.That(
                InvokeEndpointMatch(
                    match,
                    new LightmapData
                    {
                        lightmapColor = p0Color,
                        lightmapDir = p0Direction,
                        shadowMask = p0Shadow
                    },
                    p0St,
                    bucket,
                    renderer,
                    p100Source),
                Is.EqualTo(0));
            Assert.That(
                InvokeEndpointMatch(
                    match,
                    new LightmapData
                    {
                        lightmapColor = p100Color,
                        lightmapDir = p100Direction,
                        shadowMask = p100Shadow
                    },
                    p100St,
                    bucket,
                    renderer,
                    p100Source),
                Is.EqualTo(1));
            Assert.That(
                InvokeEndpointMatch(
                    match,
                    new LightmapData
                    {
                        lightmapColor = p100Color,
                        lightmapDir = p100Direction,
                        shadowMask = p0Shadow
                    },
                    p100St,
                    bucket,
                    renderer,
                    p100Source),
                Is.EqualTo(-1));
        }

        private DungeonPortalBakedRoomBasisData CreateEndpointData(
            Texture2D p0Color,
            Texture2D p0Direction,
            Texture2D p0Shadow,
            Texture2D p100Color,
            Texture2D p100Direction,
            Texture2D p100Shadow,
            Vector4 p0St,
            Vector4 p100St,
            out DungeonPortalBakedRoomBasisData.CanonicalRendererEntry renderer,
            out DungeonPortalBakedRoomBasisData.ReceiverResponseLobe lobe)
        {
            var bucket = new DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket();
            bucket.ConfigureAuthoring(
                "P0_LM0",
                0,
                p0Color,
                null,
                p0Direction,
                null,
                p0Shadow);

            renderer = new DungeonPortalBakedRoomBasisData.CanonicalRendererEntry();
            renderer.ConfigureAuthoring("Geometry/Wall#0", 0, 0, p0St, 7, p100St);

            var p100Source = new DungeonPortalBakedRoomBasisData.Power100SourceAtlas();
            p100Source.ConfigureAuthoring(7, p100Color, p100Direction, p100Shadow);

            var p0Response = new DungeonPortalBakedRoomBasisData.ResponseAtlas();
            p0Response.ConfigureOnOffAuthoring(
                0,
                p0Color,
                p0Color,
                p0Direction,
                p0Direction);
            var p100Response = new DungeonPortalBakedRoomBasisData.ResponseAtlas();
            p100Response.ConfigureOnOffAuthoring(
                0,
                p100Color,
                p100Color,
                p100Direction,
                p100Direction);
            lobe = new DungeonPortalBakedRoomBasisData.ReceiverResponseLobe();
            lobe.ConfigureEndpointAuthoring(
                "K1",
                new[] { p0Response },
                new[] { p100Response });
            var receiverDoor = new DungeonPortalBakedRoomBasisData.ReceiverDoorBasis();
            receiverDoor.ConfigureAuthoring("Door", new[] { lobe });

            var data = ScriptableObject.CreateInstance<DungeonPortalBakedRoomBasisData>();
            ownedObjects.Add(data);
            data.ConfigureAuthoring(
                "EndpointRoom",
                new[] { bucket },
                new[] { renderer },
                new[] { p100Source },
                new[] { receiverDoor },
                Array.Empty<DungeonPortalBakedRoomBasisData.SourceDoorBasis>());
            return data;
        }

        private static int InvokeEndpointMatch(
            MethodInfo match,
            LightmapData current,
            Vector4 st,
            DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket,
            DungeonPortalBakedRoomBasisData.CanonicalRendererEntry renderer,
            DungeonPortalBakedRoomBasisData.Power100SourceAtlas p100Source)
        {
            return (int)match.Invoke(
                null,
                new object[] { current, st, bucket, renderer, p100Source });
        }

        private Texture2D CreateLinearHalfTexture(int size, string name)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBAHalf, true, true)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 2,
                mipMapBias = -0.25f
            };
            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(0.25f, 0.5f, 0.75f, 1f);
            texture.SetPixels(pixels);
            texture.Apply(true, false);
            ownedObjects.Add(texture);
            return texture;
        }

        private Texture2D CreateShadowTexture(int size, string name)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = name
            };
            ownedObjects.Add(texture);
            return texture;
        }
    }
}
