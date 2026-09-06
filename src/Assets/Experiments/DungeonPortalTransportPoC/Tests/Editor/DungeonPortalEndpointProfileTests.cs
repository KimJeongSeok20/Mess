using NUnit.Framework;
using UnityEngine;

namespace DungeonPortalTransportPoC.Tests
{
    public sealed class DungeonPortalEndpointProfileTests
    {
        private DungeonPortalEndpointProfile profile;
        private Texture2D cookie;

        [SetUp]
        public void SetUp()
        {
            profile = ScriptableObject.CreateInstance<DungeonPortalEndpointProfile>();
            cookie = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
        }

        [TearDown]
        public void TearDown()
        {
            if (profile != null)
                Object.DestroyImmediate(profile);
            if (cookie != null)
                Object.DestroyImmediate(cookie);
        }

        [Test]
        public void EmptyDirectBasis_IsRejected()
        {
            profile.ConfigureAuthoring(
                "Room",
                "Doorway",
                System.Array.Empty<DungeonPortalEndpointProfile.PortalDirectLightDescriptor>(),
                System.Array.Empty<DungeonPortalEndpointProfile.PortalBounceLightDescriptor>());

            Assert.That(profile.TryValidate(out string error), Is.False);
            StringAssert.Contains("exactly one", error);
        }

        [Test]
        public void ValidK1DirectBasis_WithEmptyBounce_IsAccepted()
        {
            profile.ConfigureAuthoring(
                "Room",
                "Doorway",
                new[] { CreateValidDirectDescriptor() },
                System.Array.Empty<DungeonPortalEndpointProfile.PortalBounceLightDescriptor>());

            Assert.That(profile.TryValidate(out string error), Is.True, error);
        }

        [Test]
        public void ZeroStrengthDoorShadow_IsRejected()
        {
            DungeonPortalEndpointProfile.PortalDirectLightDescriptor descriptor =
                CreateValidDirectDescriptor();
            descriptor.shadowStrength = 0f;
            profile.ConfigureAuthoring(
                "Room",
                "Doorway",
                new[] { descriptor },
                System.Array.Empty<DungeonPortalEndpointProfile.PortalBounceLightDescriptor>());

            Assert.That(profile.TryValidate(out string error), Is.False);
            StringAssert.Contains("non-zero-strength shadows", error);
        }

        private DungeonPortalEndpointProfile.PortalDirectLightDescriptor
            CreateValidDirectDescriptor()
        {
            return new DungeonPortalEndpointProfile.PortalDirectLightDescriptor
            {
                label = "K1",
                type = LightType.Spot,
                localPosition = new Vector3(0f, 1f, -0.5f),
                localEulerAngles = Vector3.zero,
                power0Color = Color.black,
                power100Color = Color.white,
                power0Intensity = 0f,
                power100Intensity = 1f,
                range = 8f,
                spotAngle = 120f,
                innerSpotAngle = 0f,
                cookie = cookie,
                castShadows = true,
                shadowStrength = 1f,
                cullingMask = 1,
                renderingLayerMask = 2
            };
        }
    }
}
