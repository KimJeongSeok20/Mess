using NUnit.Framework;
using UnityEngine;

namespace DungeonRoomLocalLightShare.Tests
{
    public sealed class RoomLocalLightShareMathTests
    {
        [Test]
        public void ClosedDoorHasZeroOpenFraction()
        {
            float open = RoomLocalLightShareMath.ComputeDoorOpenFraction(
                Quaternion.identity,
                Quaternion.identity,
                Vector3.up,
                90f);
            Assert.AreEqual(0f, open, 0.0001f);
        }

        [Test]
        public void FullHingeRotationIsOpen()
        {
            float open = RoomLocalLightShareMath.ComputeDoorOpenFraction(
                Quaternion.identity,
                Quaternion.AngleAxis(90f, Vector3.up),
                Vector3.up,
                90f);
            Assert.AreEqual(1f, open, 0.001f);
        }

        [Test]
        public void ApertureIsOneMinusCosine()
        {
            float half = RoomLocalLightShareMath.ComputeProjectedApertureFraction(0.5f, 90f);
            float expected = (1f - Mathf.Cos(0.5f * 0.5f * Mathf.PI)) / (1f - Mathf.Cos(0.5f * Mathf.PI));
            Assert.AreEqual(expected, half, 0.0001f);
            Assert.AreEqual(0f, RoomLocalLightShareMath.ComputeProjectedApertureFraction(0f), 0.0001f);
            Assert.AreEqual(1f, RoomLocalLightShareMath.ComputeProjectedApertureFraction(1f), 0.0001f);
        }

        [Test]
        public void TransferWeightIsPowerTimesAperture()
        {
            Assert.AreEqual(0f, RoomLocalLightShareMath.ComposeTransferWeight(1f, 0f), 0.0001f);
            Assert.AreEqual(0f, RoomLocalLightShareMath.ComposeTransferWeight(0f, 1f), 0.0001f);
            Assert.AreEqual(0.25f, RoomLocalLightShareMath.ComposeTransferWeight(0.5f, 0.5f), 0.0001f);
        }

        [Test]
        public void CapturedRadianceIsNotMultipliedByItsLuminanceAgain()
        {
            Color power0 = new Color(0.01f, 0.02f, 0.03f, 1f);
            Color power100 = new Color(0.20f, 0.18f, 0.16f, 1f);

            Color off = RoomLocalLightShareMath.InterpolateLinearRadiance(power0, power100, 0f);
            Color half = RoomLocalLightShareMath.InterpolateLinearRadiance(power0, power100, 0.5f);
            Color on = RoomLocalLightShareMath.InterpolateLinearRadiance(power0, power100, 1f);

            Assert.AreEqual(power0.r, off.r, 0.0001f);
            Assert.AreEqual(power100.r, on.r, 0.0001f);
            Assert.AreEqual(0.105f, half.r, 0.0001f);
            Assert.AreEqual(0.10f, half.g, 0.0001f);
            Assert.AreEqual(0.095f, half.b, 0.0001f);
        }

        [Test]
        public void TransferRadiancePreservesSourceRgbAndClosesWithPowerOrDoor()
        {
            Color power0 = new Color(0.01f, 0.02f, 0.03f, 1f);
            Color warmPower100 = new Color(0.24f, 0.12f, 0.03f, 1f);

            Color closed = RoomLocalLightShareMath.ComposeTransferRadiance(
                power0, warmPower100, 1f, 0f);
            Color poweredOff = RoomLocalLightShareMath.ComposeTransferRadiance(
                power0, warmPower100, 0f, 1f);
            Color halfOpen = RoomLocalLightShareMath.ComposeTransferRadiance(
                power0, warmPower100, 1f, 0.5f);

            Assert.AreEqual(0f, closed.r, 0.0001f);
            Assert.AreEqual(0f, closed.g, 0.0001f);
            Assert.AreEqual(0f, closed.b, 0.0001f);
            Assert.AreEqual(0f, poweredOff.r, 0.0001f);
            Assert.AreEqual(0f, poweredOff.g, 0.0001f);
            Assert.AreEqual(0f, poweredOff.b, 0.0001f);
            Assert.AreEqual(0.12f, halfOpen.r, 0.0001f);
            Assert.AreEqual(0.06f, halfOpen.g, 0.0001f);
            Assert.AreEqual(0.015f, halfOpen.b, 0.0001f);
            Assert.Greater(halfOpen.r, halfOpen.g);
            Assert.Greater(halfOpen.g, halfOpen.b);
        }

        [Test]
        public void ApertureFeatherIsSoftInsideAndZeroAtOrOutsideTheEdge()
        {
            Assert.AreEqual(1f, RoomLocalLightShareMath.ComputeApertureFeather(0.5f, 0.5f, 0.1f), 0.0001f);
            Assert.AreEqual(0f, RoomLocalLightShareMath.ComputeApertureFeather(0.25f, 0.5f, 0.1f), 0.0001f);
            Assert.AreEqual(0f, RoomLocalLightShareMath.ComputeApertureFeather(0.1f, 0.5f, 0.1f), 0.0001f);

            float transition = RoomLocalLightShareMath.ComputeApertureFeather(0.275f, 0.5f, 0.1f);
            Assert.Greater(transition, 0f);
            Assert.Less(transition, 1f);
        }

        [Test]
        public void ConnectedDoorwayFrameUsesTheSmallerPhysicalOpening()
        {
            Vector2 overlap = RoomLocalDoorwayFrame.OverlappingSocketSize(
                new Vector2(4f, 3f),
                new Vector2(2f, 5f));

            Assert.AreEqual(2f, overlap.x, 0.0001f);
            Assert.AreEqual(3f, overlap.y, 0.0001f);
        }

        [Test]
        public void DoorwayShadowMaskIncludesDungeonAndDoorCasters()
        {
            int mask = RoomLocalLightShareContract.DoorwayShadowRenderingLayerMask;
            Assert.AreNotEqual(0, mask & RoomLocalLightShareContract.DungeonRenderingLayerMask);
            Assert.AreNotEqual(0, mask & RoomLocalLightShareContract.DoorShadowRenderingLayerMask);
            Assert.AreEqual(6, mask);
        }

        [Test]
        public void CookieLightsRoomsNotTheDefaultCharacterLayer()
        {
            int startIncoming = RoomLocalLightShareContract.StartIncomingCookieLightingLayerMask;
            int adminIncoming =
                RoomLocalLightShareContract.AdministrativeIncomingCookieLightingLayerMask;
            int dungeon = RoomLocalLightShareContract.DungeonRenderingLayerMask;
            int cookieEnv = RoomLocalLightShareContract.CookieEnvironmentRenderingLayerMask;
            Assert.AreEqual(0, startIncoming & 1);
            Assert.AreEqual(0, adminIncoming & 1);
            Assert.AreEqual(0, startIncoming & dungeon);
            Assert.AreEqual(0, adminIncoming & dungeon);
            Assert.AreNotEqual(0, startIncoming & cookieEnv);
            Assert.AreNotEqual(0, adminIncoming & cookieEnv);
        }

        [Test]
        public void DoorPerFaceDirectResponseUsesADedicatedLayer()
        {
            int doorSurface = RoomLocalLightShareContract.DoorSurfaceRenderingLayerMask;
            Assert.AreNotEqual(0, doorSurface);
            Assert.AreEqual(
                0,
                doorSurface & RoomLocalLightShareContract.DungeonRenderingLayerMask);
            Assert.AreEqual(
                0,
                doorSurface & RoomLocalLightShareContract.DoorShadowRenderingLayerMask);
            Assert.AreEqual(8, doorSurface);
        }

        [Test]
        public void LambertFaceResponseFollowsEachRotatedFaceNormal()
        {
            Assert.AreEqual(
                1f,
                RoomLocalLightShareMath.ComputeLambertFaceResponse(
                    Vector3.forward,
                    Vector3.forward),
                0.0001f);
            Assert.AreEqual(
                0f,
                RoomLocalLightShareMath.ComputeLambertFaceResponse(
                    Vector3.back,
                    Vector3.forward),
                0.0001f);
            Assert.AreEqual(
                Mathf.Sqrt(0.5f),
                RoomLocalLightShareMath.ComputeLambertFaceResponse(
                    Quaternion.AngleAxis(45f, Vector3.up) * Vector3.forward,
                    Vector3.forward),
                0.0001f);
            Assert.AreEqual(
                0f,
                RoomLocalLightShareMath.ComputeLambertFaceResponse(
                    Vector3.up,
                    Vector3.forward),
                0.0001f);
        }

        [Test]
        public void SurroundingPosesPickAuthoredKnots()
        {
            float[] authored = { 0.5f, 1f };
            RoomLocalLightShareMath.SurroundingAuthoredPoses(
                0.25f, authored, out int lower, out int upper, out float blend);
            Assert.AreEqual(0, lower);
            Assert.AreEqual(0, upper);
            Assert.AreEqual(0f, blend, 0.0001f);

            RoomLocalLightShareMath.SurroundingAuthoredPoses(
                0.75f, authored, out lower, out upper, out blend);
            Assert.AreEqual(0, lower);
            Assert.AreEqual(1, upper);
            Assert.AreEqual(0.5f, blend, 0.0001f);
        }

        [Test]
        public void DoorRotationRoundTrip()
        {
            Quaternion closed = Quaternion.identity;
            Quaternion posed = RoomLocalLightShareMath.DoorLocalRotationForOpenFraction(
                closed, Vector3.up, 90f, 1f);
            float open = RoomLocalLightShareMath.ComputeDoorOpenFraction(
                closed, posed, Vector3.up, 90f);
            Assert.AreEqual(1f, open, 0.01f);
        }

        [Test]
        public void DoorFaceRoomWeightUsesDoorwayAxis()
        {
            Vector3 directionIntoRoom = new Vector3(1f, 0f, 1f).normalized;

            Assert.AreEqual(
                1f,
                RoomLocalLightShareMath.ComputeDoorFaceRoomWeight(
                    directionIntoRoom,
                    directionIntoRoom),
                0.0001f);
            Assert.AreEqual(
                0f,
                RoomLocalLightShareMath.ComputeDoorFaceRoomWeight(
                    -directionIntoRoom,
                    directionIntoRoom),
                0.0001f);
            Assert.AreEqual(
                0.5f,
                RoomLocalLightShareMath.ComputeDoorFaceRoomWeight(
                    Vector3.up,
                    directionIntoRoom),
                0.0001f);
        }

        [Test]
        public void ProbeProximityWeightFavorsTheNearestRoom()
        {
            Assert.AreEqual(
                0.5f,
                RoomLocalLightShareMath.ComputeProbeProximityWeight(1f, 1f),
                0.0001f);
            Assert.Greater(
                RoomLocalLightShareMath.ComputeProbeProximityWeight(0.25f, 1f),
                0.5f);
            Assert.Less(
                RoomLocalLightShareMath.ComputeProbeProximityWeight(1f, 0.25f),
                0.5f);
            Assert.AreEqual(
                1f,
                RoomLocalLightShareMath.ComputeProbeProximityWeight(0f, 1f),
                0.0001f);
        }

        [Test]
        public void ClosedWorldDirectionUndoesHingeRotation()
        {
            Quaternion closed = Quaternion.identity;
            Quaternion open = Quaternion.AngleAxis(90f, Vector3.up);
            Vector3 closedNormal = Vector3.forward;
            Vector3 currentNormal = open * closedNormal;

            Vector3 recovered = RoomLocalLightShareMath.ClosedWorldDirection(
                currentNormal,
                open,
                closed);

            Assert.AreEqual(closedNormal.x, recovered.x, 0.0001f);
            Assert.AreEqual(closedNormal.y, recovered.y, 0.0001f);
            Assert.AreEqual(closedNormal.z, recovered.z, 0.0001f);
            Assert.AreEqual(1f, currentNormal.x, 0.0001f);
        }

        [Test]
        public void ClosedFaceOwnerStaysWithTheRoomTheFaceLookedIntoWhenClosed()
        {
            Vector3 intoA = Vector3.forward;
            Vector3 intoB = Vector3.back;

            Assert.IsTrue(
                RoomLocalLightShareMath.FaceOwnsFirstRoom(Vector3.forward, intoA, intoB));
            Assert.IsFalse(
                RoomLocalLightShareMath.FaceOwnsFirstRoom(Vector3.back, intoA, intoB));

            Vector3 rotatedIntoNeither = Quaternion.AngleAxis(90f, Vector3.up) * Vector3.forward;
            Assert.AreEqual(
                0.5f,
                RoomLocalLightShareMath.ComputeDoorFaceRoomWeight(rotatedIntoNeither, intoA),
                0.0001f);
            Assert.IsTrue(
                RoomLocalLightShareMath.FaceOwnsFirstRoom(Vector3.forward, intoA, intoB));
        }

        [Test]
        public void DoorwayPlaneSideIsPositiveInsideTheFirstRoom()
        {
            Vector3 doorway = Vector3.zero;
            Vector3 intoA = Vector3.right;
            Assert.Greater(
                RoomLocalLightShareMath.DoorwayPlaneSide(new Vector3(0.5f, 0f, 0f), doorway, intoA),
                0f);
            Assert.Less(
                RoomLocalLightShareMath.DoorwayPlaneSide(new Vector3(-0.5f, 0f, 0f), doorway, intoA),
                0f);
            Assert.AreEqual(
                0f,
                RoomLocalLightShareMath.DoorwayPlaneSide(doorway, doorway, intoA),
                0.0001f);
        }

        [Test]
        public void SwungOpenFaceTakesLocationOverClosedOwnership()
        {
            const float confidenceDistance = 0.65f;
            float nearDoorway = RoomLocalLightShareMath.LocationConfidence(0.20f, confidenceDistance);
            float swungIntoRoom = RoomLocalLightShareMath.LocationConfidence(0.64f, confidenceDistance);

            float darkFaceNearDoor = RoomLocalLightShareMath.BlendClosedAndLocationRoomWeight(
                false,
                1f,
                nearDoorway);
            float darkFaceSwungOpen = RoomLocalLightShareMath.BlendClosedAndLocationRoomWeight(
                false,
                1f,
                swungIntoRoom);

            Assert.Less(darkFaceNearDoor, 0.4f);
            Assert.Greater(darkFaceSwungOpen, 0.85f);
        }

        [Test]
        public void DoorCookieReceiveLayersDoNotOverlapPortalOrDungeon()
        {
            int start = RoomLocalLightShareContract.DoorReceiveStartRenderingLayerMask;
            int admin = RoomLocalLightShareContract.DoorReceiveAdministrativeRenderingLayerMask;
            int dungeon = RoomLocalLightShareContract.DungeonRenderingLayerMask;
            int doorSurface = RoomLocalLightShareContract.DoorSurfaceRenderingLayerMask;
            Assert.AreEqual(16, start);
            Assert.AreEqual(32, admin);
            Assert.AreEqual(0, start & admin);
            Assert.AreEqual(0, start & dungeon);
            Assert.AreEqual(0, admin & dungeon);
            Assert.AreEqual(0, start & doorSurface);
            Assert.AreEqual(
                RoomLocalLightShareContract.CookieEnvironmentRenderingLayerMask | start,
                RoomLocalLightShareContract.StartIncomingCookieLightingLayerMask);
        }

        [Test]
        public void DoorVisibleFacesKeepDungeonSoPlayerFlashlightCanLand()
        {
            uint opening = (uint)RoomLocalLightShareContract.DungeonRenderingLayerMask |
                           (uint)RoomLocalLightShareContract.DoorSurfaceRenderingLayerMask;
            uint startSide = (uint)RoomLocalLightShareContract.DungeonRenderingLayerMask |
                             (uint)RoomLocalLightShareContract.DoorReceiveStartRenderingLayerMask;
            int cookie = RoomLocalLightShareContract.StartIncomingCookieLightingLayerMask;
            Assert.AreNotEqual(0u, opening & (uint)RoomLocalLightShareContract.DungeonRenderingLayerMask);
            Assert.AreEqual(0, cookie & (int)opening);
            Assert.AreNotEqual(0, cookie & (int)startSide);
        }

        [Test]
        public void DoorInTheOpeningDoesNotReceiveEitherCookie()
        {
            uint layer = RoomLocalLightShareMath.DoorCookieReceiveLayer(0.20f, 0.65f);
            Assert.AreEqual(
                (uint)RoomLocalLightShareContract.DoorSurfaceRenderingLayerMask,
                layer);
        }

        [Test]
        public void DoorSwungIntoARoomReceivesOnlyThatRoomsIncomingCookie()
        {
            uint startSide = RoomLocalLightShareMath.DoorCookieReceiveLayer(0.64f, 0.65f);
            uint adminSide = RoomLocalLightShareMath.DoorCookieReceiveLayer(-0.64f, 0.65f);
            Assert.AreEqual(
                (uint)RoomLocalLightShareContract.DoorReceiveStartRenderingLayerMask,
                startSide);
            Assert.AreEqual(
                (uint)RoomLocalLightShareContract.DoorReceiveAdministrativeRenderingLayerMask,
                adminSide);
        }
    }
}
