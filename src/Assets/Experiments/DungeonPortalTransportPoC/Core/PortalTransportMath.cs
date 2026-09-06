using UnityEngine;

namespace DungeonPortalTransportPoC
{
    public static class PortalTransportMath
    {
        private const float MinimumAngleDegrees = 0.0001f;

        public static float ComputeDoorOpenFraction(
            Quaternion closedLocalRotation,
            Quaternion currentLocalRotation,
            Vector3 localHingeAxis,
            float openAngleDegrees)
        {
            float signedAngleLimit = openAngleDegrees;
            if (Mathf.Abs(signedAngleLimit) <= MinimumAngleDegrees ||
                localHingeAxis.sqrMagnitude <= Mathf.Epsilon)
                return 0f;

            Quaternion delta = Quaternion.Inverse(closedLocalRotation) * currentLocalRotation;
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
            {
                angle = 360f - angle;
                axis = -axis;
            }

            if (float.IsNaN(axis.x) || axis.sqrMagnitude <= Mathf.Epsilon)
                return 0f;

            float axisAlignment = Vector3.Dot(axis.normalized, localHingeAxis.normalized);
            float signedHingeAngle = angle * axisAlignment;
            return Mathf.Clamp01(signedHingeAngle / signedAngleLimit);
        }

        public static float ComputeProjectedApertureFraction(
            float openFraction,
            float openAngleDegrees = 90f)
        {
            float clampedFraction = Mathf.Clamp01(openFraction);
            float maximumRadians = Mathf.Clamp(Mathf.Abs(openAngleDegrees), 0f, 90f) * Mathf.Deg2Rad;
            float denominator = 1f - Mathf.Cos(maximumRadians);
            if (denominator <= Mathf.Epsilon)
                return clampedFraction;

            float currentRadians = maximumRadians * clampedFraction;
            return Mathf.Clamp01((1f - Mathf.Cos(currentRadians)) / denominator);
        }

        public static float ComposeTransferWeight(float sourcePower01, float apertureFraction)
        {
            return Mathf.Clamp01(sourcePower01) * Mathf.Clamp01(apertureFraction);
        }

        public static Color InterpolateLinearEndpoint(
            Color power0,
            Color power100,
            float power01)
        {
            return Color.LerpUnclamped(power0, power100, Mathf.Clamp01(power01));
        }

        public static Color InterpolateLinearRadiance(
            Color power0Color,
            float power0Intensity,
            Color power100Color,
            float power100Intensity,
            float power01)
        {
            Color power0Radiance = power0Color * Mathf.Max(0f, power0Intensity);
            Color power100Radiance = power100Color * Mathf.Max(0f, power100Intensity);
            return Color.LerpUnclamped(
                power0Radiance,
                power100Radiance,
                Mathf.Clamp01(power01));
        }

        public static float Luminance(Color linearColor)
        {
            return Mathf.Max(
                0f,
                linearColor.r * 0.2126f +
                linearColor.g * 0.7152f +
                linearColor.b * 0.0722f);
        }

        public static float SmoothStep01(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }
    }
}
