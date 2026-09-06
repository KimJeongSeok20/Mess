using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using DoorShDriver =
    DungeonPortalBakedBasisPoC.Validation.DungeonPortalBakedBasisDoorShDriver;

namespace DungeonPortalBakedBasisPoC.Validation.Tests.Editor
{
    [TestFixture]
    public sealed class DungeonPortalBakedBasisDoorShPoseBridgeTests
    {
        private const string BasisId = "K1";
        private const string StartDoorId = "StartDoor";
        private const string AdministrativeDoorId = "AdministrativeDoor";
        private const string AdministrativeToStartId =
            "PoseBridge/AdministrativeToStart";
        private const string StartToAdministrativeId =
            "PoseBridge/StartToAdministrative";

        [Test]
        public void ComposeProbe_UsesReciprocalPoseAwareStatesAndFailsClosedOnMismatchedIds()
        {
            DungeonPortalBakedRoomBasisData startBasis = CreateRoomBasis(
                "Start",
                StartDoorId,
                10f,
                20f,
                1f,
                3f,
                5f,
                7f);
            DungeonPortalBakedRoomBasisData administrativeBasis = CreateRoomBasis(
                "Administrative",
                AdministrativeDoorId,
                30f,
                50f,
                2f,
                4f,
                6f,
                8f);
            GameObject startObject = null;
            GameObject administrativeObject = null;
            GameObject driverObject = null;
            try
            {
                float physicalOpenFraction = 0.375f;
                float aperture = 1f - Mathf.Cos(physicalOpenFraction * Mathf.PI * 0.5f);
                DungeonPortalBakedBasisRoomCompositor startCompositor = CreateCompositor(
                    ref startObject,
                    "__DPBB_StartShPoseBridgeTest",
                    startBasis,
                    AdministrativeToStartId,
                    StartDoorId,
                    administrativeBasis,
                    AdministrativeDoorId,
                    aperture,
                    2f);
                DungeonPortalBakedBasisRoomCompositor administrativeCompositor = CreateCompositor(
                    ref administrativeObject,
                    "__DPBB_AdministrativeShPoseBridgeTest",
                    administrativeBasis,
                    StartToAdministrativeId,
                    AdministrativeDoorId,
                    startBasis,
                    StartDoorId,
                    aperture,
                    1f);
                driverObject = EditorUtility.CreateGameObjectWithHideFlags(
                    "__DPBB_DoorShPoseBridgeTest",
                    HideFlags.HideAndDontSave,
                    typeof(DoorShDriver));
                DoorShDriver driver = driverObject.GetComponent<DoorShDriver>();

                Assert.That(InvokeComposeProbe(
                    driver,
                    startCompositor,
                    AdministrativeToStartId,
                    startBasis,
                    0.25f,
                    out SphericalHarmonicsL2 startProbe,
                    out string startFailure), Is.True, startFailure);
                AssertChannel(startProbe, 0, 16.5f);
                AssertChannel(startProbe, 1, 20.5f);
                AssertChannel(startProbe, 2, 24.5f);

                Assert.That(InvokeComposeProbe(
                    driver,
                    administrativeCompositor,
                    StartToAdministrativeId,
                    administrativeBasis,
                    0.5f,
                    out SphericalHarmonicsL2 administrativeProbe,
                    out string administrativeFailure), Is.True, administrativeFailure);
                AssertChannel(administrativeProbe, 0, 43f);
                AssertChannel(administrativeProbe, 1, 46f);
                AssertChannel(administrativeProbe, 2, 49f);

                Assert.That(InvokeComposeProbe(
                    driver,
                    startCompositor,
                    StartToAdministrativeId,
                    startBasis,
                    0.25f,
                    out SphericalHarmonicsL2 wrongIdProbe,
                    out string wrongIdFailure), Is.False);
                StringAssert.Contains("failed closed", wrongIdFailure);
                AssertProbeIsZero(wrongIdProbe);

                Assert.That(InvokeValidateIncomingConnection(
                    startCompositor,
                    AdministrativeToStartId,
                    "WrongReceiverDoor",
                    administrativeBasis,
                    AdministrativeDoorId,
                    out string mismatchFailure), Is.False);
                StringAssert.Contains("does not exactly match", mismatchFailure);
            }
            finally
            {
                DestroyImmediate(driverObject);
                DestroyImmediate(administrativeObject);
                DestroyImmediate(startObject);
                DestroyImmediate(administrativeBasis);
                DestroyImmediate(startBasis);
            }
        }

        private static DungeonPortalBakedBasisRoomCompositor CreateCompositor(
            ref GameObject owner,
            string name,
            DungeonPortalBakedRoomBasisData receiverBasis,
            string connectionId,
            string receiverDoorId,
            DungeonPortalBakedRoomBasisData sourceBasis,
            string sourceDoorId,
            float aperture,
            float responseScale)
        {
            owner = EditorUtility.CreateGameObjectWithHideFlags(
                name,
                HideFlags.HideAndDontSave,
                typeof(DungeonPortalBakedBasisRoomCompositor));
            DungeonPortalBakedBasisRoomCompositor compositor =
                owner.GetComponent<DungeonPortalBakedBasisRoomCompositor>();
            var incoming = new DungeonPortalBakedBasisRoomCompositor.IncomingDoorState();
            incoming.ConfigureAuthoring(
                connectionId,
                receiverDoorId,
                sourceBasis,
                sourceDoorId,
                1f,
                aperture,
                responseScale,
                true);
            compositor.ConfigureAuthoring(
                receiverBasis,
                owner.transform,
                null,
                Array.Empty<DungeonPortalBakedBasisRoomCompositor.ExplicitRendererBinding>(),
                new[] { incoming },
                0f,
                false);
            return compositor;
        }

        private static DungeonPortalBakedRoomBasisData CreateRoomBasis(
            string roomId,
            string doorId,
            float ambientP0,
            float ambientP100,
            float d25,
            float d50,
            float d75,
            float d100)
        {
            var receiverDoor = new DungeonPortalBakedRoomBasisData.ReceiverDoorBasis();
            receiverDoor.ConfigurePoseAuthoring(
                doorId,
                new[]
                {
                    CreatePose(0.25f, d25),
                    CreatePose(0.5f, d50),
                    CreatePose(0.75f, d75),
                    CreatePose(1f, d100)
                });
            var sourceCoefficient = new DungeonPortalBakedRoomBasisData.SourceBasisCoefficient();
            sourceCoefficient.ConfigureAuthoring(
                BasisId,
                new Color(1f, 2f, 3f),
                new Color(1f, 2f, 3f));
            var sourceDoor = new DungeonPortalBakedRoomBasisData.SourceDoorBasis();
            sourceDoor.ConfigureAuthoring(doorId, new[] { sourceCoefficient });
            var ambient = new DungeonPortalBakedRoomBasisData.OptionalAmbientProbeStates();
            ambient.ConfigureAuthoring(
                true,
                FilledSh(ambientP0),
                FilledSh(ambientP100));

            DungeonPortalBakedRoomBasisData basis =
                ScriptableObject.CreateInstance<DungeonPortalBakedRoomBasisData>();
            basis.ConfigureAuthoring(
                roomId,
                Array.Empty<DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket>(),
                Array.Empty<DungeonPortalBakedRoomBasisData.CanonicalRendererEntry>(),
                Array.Empty<DungeonPortalBakedRoomBasisData.Power100SourceAtlas>(),
                new[] { receiverDoor },
                new[] { sourceDoor },
                ambient);
            return basis;
        }

        private static DungeonPortalBakedRoomBasisData.ReceiverDoorPoseResponse CreatePose(
            float openFraction,
            float probeDelta)
        {
            var response = new DungeonPortalBakedRoomBasisData.OptionalDoorwayProbeResponse();
            response.ConfigureAuthoring(FilledSh(0f), FilledSh(probeDelta));
            var lobe = new DungeonPortalBakedRoomBasisData.ReceiverResponseLobe();
            lobe.ConfigureAuthoring(
                BasisId,
                Array.Empty<DungeonPortalBakedRoomBasisData.ResponseAtlas>(),
                response);
            var pose = new DungeonPortalBakedRoomBasisData.ReceiverDoorPoseResponse();
            pose.ConfigureAuthoring(openFraction, new[] { lobe });
            return pose;
        }

        private static float[] FilledSh(float value)
        {
            var result = new float[
                DungeonPortalBakedRoomBasisData.SphericalHarmonicsCoefficientCount];
            for (int i = 0; i < result.Length; i++)
                result[i] = value;
            return result;
        }

        private static bool InvokeComposeProbe(
            DoorShDriver driver,
            DungeonPortalBakedBasisRoomCompositor receiverCompositor,
            string connectionId,
            DungeonPortalBakedRoomBasisData receiverBasis,
            float receiverPower,
            out SphericalHarmonicsL2 probe,
            out string failure)
        {
            MethodInfo method = typeof(DoorShDriver).GetMethod(
                "TryComposeProbe",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments =
            {
                receiverCompositor,
                connectionId,
                receiverBasis,
                receiverPower,
                default(SphericalHarmonicsL2),
                null
            };
            bool result = (bool)method.Invoke(driver, arguments);
            probe = (SphericalHarmonicsL2)arguments[4];
            failure = arguments[5] as string;
            return result;
        }

        private static bool InvokeValidateIncomingConnection(
            DungeonPortalBakedBasisRoomCompositor receiverCompositor,
            string connectionId,
            string receiverDoorId,
            DungeonPortalBakedRoomBasisData sourceBasis,
            string sourceDoorId,
            out string failure)
        {
            MethodInfo method = typeof(DoorShDriver).GetMethod(
                "TryValidateIncomingConnection",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments =
            {
                receiverCompositor,
                connectionId,
                receiverDoorId,
                sourceBasis,
                sourceDoorId,
                null
            };
            bool result = (bool)method.Invoke(null, arguments);
            failure = arguments[5] as string;
            return result;
        }

        private static void AssertChannel(
            SphericalHarmonicsL2 probe,
            int channel,
            float expected)
        {
            for (int coefficient = 0; coefficient < 9; coefficient++)
            {
                Assert.That(
                    probe[channel, coefficient],
                    Is.EqualTo(expected).Within(0.000001f));
            }
        }

        private static void AssertProbeIsZero(SphericalHarmonicsL2 probe)
        {
            for (int channel = 0; channel < 3; channel++)
            for (int coefficient = 0; coefficient < 9; coefficient++)
                Assert.That(probe[channel, coefficient], Is.EqualTo(0f));
        }

        private static void DestroyImmediate(UnityEngine.Object value)
        {
            if (value != null)
                UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
