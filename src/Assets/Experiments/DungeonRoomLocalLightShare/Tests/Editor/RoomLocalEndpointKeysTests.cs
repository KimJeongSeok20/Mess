using NUnit.Framework;

namespace DungeonRoomLocalLightShare.Tests
{
    public sealed class RoomLocalEndpointKeysTests
    {
        [Test]
        public void RotationSuffixIsStrippedFromLiveTileNames()
        {
            Assert.AreEqual("StartRoom", RoomLocalEndpointKeys.StripRotationSuffix("StartRoom_R090"));
            Assert.AreEqual("StartRoom", RoomLocalEndpointKeys.StripRotationSuffix("StartRoom_R000 (Clone)"));
            Assert.AreEqual("StartRoom", RoomLocalEndpointKeys.StripRotationSuffix("StartRoom"));
            Assert.AreEqual("AdminstrativeSegregation",
                RoomLocalEndpointKeys.StripRotationSuffix("AdminstrativeSegregation_R180"));
        }

        [Test]
        public void RoomIdsMatchIgnoresRotationAndClone()
        {
            Assert.IsTrue(RoomLocalEndpointKeys.RoomIdsMatch("StartRoom_R270 (Clone)", "StartRoom"));
            Assert.IsFalse(RoomLocalEndpointKeys.RoomIdsMatch("Cafeteria_R000", "StartRoom"));
        }

        [Test]
        public void DoorwayPathsMatchAfterStrippingRotationParents()
        {
            Assert.IsTrue(RoomLocalEndpointKeys.PathsMatch(
                "R090/Doorways/Door_SM_A/DoorWayPoint",
                "Doorways/Door_SM_A/DoorWayPoint"));
            Assert.IsTrue(RoomLocalEndpointKeys.PathsMatch(
                "Canonical/Doorways/Door_LG_A/DoorwayPoint",
                "Doorways/Door_LG_A/DoorwayPoint"));
            Assert.IsTrue(RoomLocalEndpointKeys.PathsMatch(
                "Door_LG_A/DoorwayPoint",
                "Door_LG_A/DoorwayPoint"));
            Assert.IsFalse(RoomLocalEndpointKeys.PathsMatch(
                "Doorways/Door_SM_A (1)/DoorWayPoint",
                "Doorways/Door_SM_A/DoorWayPoint"));
        }
    }
}
