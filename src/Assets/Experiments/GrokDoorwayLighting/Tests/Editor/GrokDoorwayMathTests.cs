using GrokDoorwayLighting;
using NUnit.Framework;
using UnityEngine;

namespace GrokDoorwayLighting.Tests
{
    public sealed class GrokDoorwayMathTests
    {
        private static GrokDoorwayMath.DoorwayFrame Frame()
        {
            return new GrokDoorwayMath.DoorwayFrame
            {
                position = Vector3.zero,
                inward = Vector3.forward,
                right = Vector3.right,
                up = Vector3.up,
                halfWidth = 0.5f,
                halfHeight = 1.25f,
                blendDepth = 2.5f,
                lateralFade = 0.12f,
                verticalPadding = 0.18f,
                jambAllowance = 0.15f
            };
        }

        [Test]
        public void FloorJustInsideDoorReceivesStrongWeight()
        {
            var door = Frame();
            float weight = GrokDoorwayMath.ComputeWeight(
                new Vector3(0f, 0.02f, 0.2f),
                Vector3.up,
                door);
            Assert.Greater(weight, 0.7f);
        }

        [Test]
        public void FloorFarFromDoorFadesOut()
        {
            var door = Frame();
            float near = GrokDoorwayMath.ComputeWeight(new Vector3(0f, 0.02f, 0.2f), Vector3.up, door);
            float far = GrokDoorwayMath.ComputeWeight(new Vector3(0f, 0.02f, 2.4f), Vector3.up, door);
            Assert.Greater(near, far);
            Assert.Less(far, 0.15f);
        }

        [Test]
        public void BroadEntranceWallDoesNotGlow()
        {
            var door = Frame();
            float weight = GrokDoorwayMath.ComputeWeight(
                new Vector3(1.6f, 1.2f, 0.01f),
                Vector3.forward,
                door);
            Assert.Less(weight, 0.02f);
        }

        [Test]
        public void JambFacingOpeningIsEligible()
        {
            var door = Frame();
            float weight = GrokDoorwayMath.ComputeWeight(
                new Vector3(0.5f, 1.2f, 0.05f),
                Vector3.left,
                door);
            Assert.Greater(weight, 0.4f);
        }

        [Test]
        public void DarkFacingFrameIsRejected()
        {
            var door = Frame();
            float weight = GrokDoorwayMath.ComputeWeight(
                new Vector3(0.55f, 1.2f, 0.02f),
                Vector3.forward,
                door);
            Assert.Less(weight, 0.05f);
        }

        [Test]
        public void BehindTheDoorIsRejected()
        {
            var door = Frame();
            float weight = GrokDoorwayMath.ComputeWeight(
                new Vector3(0f, 0.02f, -0.4f),
                Vector3.up,
                door);
            Assert.AreEqual(0f, weight);
        }

        [Test]
        public void DoorPlaneDoesNotReceiveSpill()
        {
            var door = Frame();
            float weight = GrokDoorwayMath.ComputeWeight(
                new Vector3(0f, 1.2f, 0.01f),
                Vector3.forward,
                door);
            Assert.AreEqual(0f, weight);
        }

        [Test]
        public void TransferOnlyWhenSourceOnAndReceiverOff()
        {
            Assert.IsTrue(GrokDoorwayMath.IsTransferActive(
                true,
                DungeonTileLightmapSwitcher.PowerLevel.P100,
                DungeonTileLightmapSwitcher.PowerLevel.P0));
            Assert.IsFalse(GrokDoorwayMath.IsTransferActive(
                true,
                DungeonTileLightmapSwitcher.PowerLevel.P100,
                DungeonTileLightmapSwitcher.PowerLevel.P100));
            Assert.IsFalse(GrokDoorwayMath.IsTransferActive(
                true,
                DungeonTileLightmapSwitcher.PowerLevel.P0,
                DungeonTileLightmapSwitcher.PowerLevel.P0));
            Assert.IsFalse(GrokDoorwayMath.IsTransferActive(
                false,
                DungeonTileLightmapSwitcher.PowerLevel.P100,
                DungeonTileLightmapSwitcher.PowerLevel.P0));
        }
    }
}
