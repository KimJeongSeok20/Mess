using NUnit.Framework;
using UnityEngine;

namespace DungeonPortalTransportPoC.Tests
{
    public sealed class PortalTransportMathTests
    {
        [Test]
        public void DoorOpenFraction_TracksActualHingeRotation()
        {
            Quaternion closed = Quaternion.Euler(0f, 17f, 0f);

            Assert.That(
                PortalTransportMath.ComputeDoorOpenFraction(
                    closed,
                    closed,
                    Vector3.up,
                    90f),
                Is.EqualTo(0f).Within(0.0001f));
            Assert.That(
                PortalTransportMath.ComputeDoorOpenFraction(
                    closed,
                    closed * Quaternion.Euler(0f, 45f, 0f),
                    Vector3.up,
                    90f),
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(
                PortalTransportMath.ComputeDoorOpenFraction(
                    closed,
                    closed * Quaternion.Euler(0f, 90f, 0f),
                    Vector3.up,
                    90f),
                Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void DoorOpenFraction_RejectsRotationInOppositeDirection()
        {
            Quaternion closed = Quaternion.identity;

            Assert.That(
                PortalTransportMath.ComputeDoorOpenFraction(
                    closed,
                    Quaternion.Euler(0f, -45f, 0f),
                    Vector3.up,
                    90f),
                Is.Zero.Within(0.0001f));
            Assert.That(
                PortalTransportMath.ComputeDoorOpenFraction(
                    closed,
                    Quaternion.Euler(0f, -45f, 0f),
                    Vector3.up,
                    -90f),
                Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void ProjectedApertureFraction_UsesDoorProjectionInsteadOfLinearToggle()
        {
            Assert.That(
                PortalTransportMath.ComputeProjectedApertureFraction(0f),
                Is.EqualTo(0f).Within(0.0001f));
            Assert.That(
                PortalTransportMath.ComputeProjectedApertureFraction(0.5f),
                Is.EqualTo(1f - Mathf.Cos(45f * Mathf.Deg2Rad)).Within(0.0001f));
            Assert.That(
                PortalTransportMath.ComputeProjectedApertureFraction(1f),
                Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void TransferWeight_MultipliesSourcePowerAndAperture()
        {
            Assert.That(
                PortalTransportMath.ComposeTransferWeight(0.4f, 0.25f),
                Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(PortalTransportMath.ComposeTransferWeight(1f, 0f), Is.Zero);
            Assert.That(PortalTransportMath.ComposeTransferWeight(0f, 1f), Is.Zero);
        }

        [Test]
        public void PowerTransition_ReversesFromCurrentValueWithoutJump()
        {
            var transition = new PortalPowerTransition(1f);
            transition.SetTarget(0f, 2f);
            float halfwayDown = transition.Tick(1f);

            transition.SetTarget(1f, 1f);

            Assert.That(transition.Current, Is.EqualTo(halfwayDown).Within(0.0001f));
            Assert.That(transition.Tick(0.5f), Is.GreaterThan(halfwayDown));
            Assert.That(transition.Tick(0.5f), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void EndpointInterpolation_PreservesBothExactEndpoints()
        {
            Color p0 = new Color(0.05f, 0.1f, 0.2f, 1f);
            Color p100 = new Color(2f, 1f, 0.5f, 1f);

            Assert.That(PortalTransportMath.InterpolateLinearEndpoint(p0, p100, 0f), Is.EqualTo(p0));
            Assert.That(PortalTransportMath.InterpolateLinearEndpoint(p0, p100, 1f), Is.EqualTo(p100));
        }

        [Test]
        public void RadianceInterpolation_DoesNotMultiplyTwoHalfwayValues()
        {
            Color radiance = PortalTransportMath.InterpolateLinearRadiance(
                Color.black,
                0f,
                Color.white,
                1f,
                0.5f);

            Assert.That(radiance.r, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(radiance.g, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(radiance.b, Is.EqualTo(0.5f).Within(0.0001f));
        }
    }
}
