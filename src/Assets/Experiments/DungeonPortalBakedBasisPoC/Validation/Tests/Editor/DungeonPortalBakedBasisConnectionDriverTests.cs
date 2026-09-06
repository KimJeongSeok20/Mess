using DungeonPortalBakedBasisPoC.Validation;
using DungeonPortalTransportPoC;
using NUnit.Framework;

namespace DungeonPortalBakedBasisPoC.Validation.Tests.Editor
{
    /// <summary>
    /// Pure, scene-free contract checks only. These tests are intentionally not invoked by the
    /// validation builder or smoke capture, so they cannot dirty the isolated validation scene.
    /// </summary>
    [TestFixture]
    public sealed class DungeonPortalBakedBasisConnectionDriverTests
    {
        [Test]
        public void SmoothTowards01_UsesDurationAsMultiFrameRate()
        {
            float next = DungeonPortalBakedBasisConnectionDriver.SmoothTowards01ForTest(
                0f, 1f, 0.1f, 0.5f);

            Assert.That(next, Is.EqualTo(0.2f).Within(0.000001f));
        }

        [Test]
        public void SmoothTowards01_ZeroDurationSettlesImmediately()
        {
            float next = DungeonPortalBakedBasisConnectionDriver.SmoothTowards01ForTest(
                0.2f, 0.8f, 0.016f, 0f);

            Assert.That(next, Is.EqualTo(0.8f).Within(0.000001f));
        }

        [TestCase(0f, DungeonTileLightmapSwitcher.PowerLevel.P0)]
        [TestCase(0.0001f, DungeonTileLightmapSwitcher.PowerLevel.P0)]
        [TestCase(0.9999f, DungeonTileLightmapSwitcher.PowerLevel.P100)]
        [TestCase(1f, DungeonTileLightmapSwitcher.PowerLevel.P100)]
        public void TryGetEndpoint_AcceptsOnlyCanonicalEndpointTolerance(
            float value01,
            DungeonTileLightmapSwitcher.PowerLevel expected)
        {
            bool result = DungeonPortalBakedBasisConnectionDriver.TryGetEndpoint(
                value01, out DungeonTileLightmapSwitcher.PowerLevel endpoint);

            Assert.That(result, Is.True);
            Assert.That(endpoint, Is.EqualTo(expected));
        }

        [Test]
        public void TryGetEndpoint_RejectsIntermediatePower()
        {
            bool result = DungeonPortalBakedBasisConnectionDriver.TryGetEndpoint(
                0.5f, out _);

            Assert.That(result, Is.False);
        }
    }
}
