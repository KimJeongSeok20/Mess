using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    /// <summary>
    /// Adapted from DungeonPortalTransportPoC.PortalTransportMath.
    /// Hinge angle → open fraction → projected aperture. Encoded direction maps are never lerped here.
    /// </summary>
    public static class RoomLocalLightShareMath
    {
        public const float DefaultOpenAngleDegrees = 90f;
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
            float openAngleDegrees = DefaultOpenAngleDegrees)
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

        public static Color InterpolateLinearRadiance(
            Color power0Radiance,
            Color power100Radiance,
            float power01)
        {
            return Color.LerpUnclamped(
                power0Radiance,
                power100Radiance,
                Mathf.Clamp01(power01));
        }

        public static Color ComposeTransferRadiance(
            Color power0Radiance,
            Color power100Radiance,
            float sourcePower01,
            float apertureFraction)
        {
            Color captured = InterpolateLinearRadiance(
                power0Radiance,
                power100Radiance,
                sourcePower01);
            float transport = ComposeTransferWeight(sourcePower01, apertureFraction);
            return captured * transport;
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

        public static float ComputeDoorFaceRoomWeight(
            Vector3 worldFaceNormal,
            Vector3 directionIntoRoom)
        {
            if (worldFaceNormal.sqrMagnitude <= Mathf.Epsilon ||
                directionIntoRoom.sqrMagnitude <= Mathf.Epsilon)
                return 0.5f;

            float signedFacing = Vector3.Dot(
                worldFaceNormal.normalized,
                directionIntoRoom.normalized);
            return SmoothStep01(0.5f + signedFacing * 0.5f);
        }

        public static float ComputeLambertFaceResponse(
            Vector3 worldFaceNormal,
            Vector3 directionFromSurfaceToSource)
        {
            if (worldFaceNormal.sqrMagnitude <= Mathf.Epsilon ||
                directionFromSurfaceToSource.sqrMagnitude <= Mathf.Epsilon)
                return 0f;

            return Mathf.Max(0f, Vector3.Dot(
                worldFaceNormal.normalized,
                directionFromSurfaceToSource.normalized));
        }

        public static float ComputeProbeProximityWeight(
            float distanceToFirstRoom,
            float distanceToSecondRoom)
        {
            float first = Mathf.Max(0f, distanceToFirstRoom);
            float second = Mathf.Max(0f, distanceToSecondRoom);
            float total = first + second;
            if (total <= Mathf.Epsilon)
                return 0.5f;

            // A sample that is closer to the first room's baked probes should receive more of
            // that room. Smoothing avoids a visible pop when a rotating face crosses the middle.
            return SmoothStep01(second / total);
        }

        public static Vector3 ClosedWorldDirection(
            Vector3 currentWorldDirection,
            Quaternion currentLeafWorldRotation,
            Quaternion closedLeafWorldRotation)
        {
            if (currentWorldDirection.sqrMagnitude <= Mathf.Epsilon)
                return Vector3.forward;

            Quaternion fromClosedToCurrent =
                currentLeafWorldRotation * Quaternion.Inverse(closedLeafWorldRotation);
            Vector3 closed = Quaternion.Inverse(fromClosedToCurrent) * currentWorldDirection;
            return closed.sqrMagnitude > Mathf.Epsilon ? closed.normalized : Vector3.forward;
        }

        public static Quaternion LeafWorldRotation(Transform leaf, Quaternion leafLocalRotation)
        {
            if (leaf == null)
                return leafLocalRotation;
            return leaf.parent != null
                ? leaf.parent.rotation * leafLocalRotation
                : leafLocalRotation;
        }

        public static bool FaceOwnsFirstRoom(
            Vector3 closedWorldNormal,
            Vector3 directionIntoFirstRoom,
            Vector3 directionIntoSecondRoom)
        {
            if (closedWorldNormal.sqrMagnitude <= Mathf.Epsilon)
                return true;

            Vector3 normal = closedWorldNormal.normalized;
            Vector3 first = directionIntoFirstRoom.sqrMagnitude > Mathf.Epsilon
                ? directionIntoFirstRoom.normalized
                : Vector3.forward;
            Vector3 second = directionIntoSecondRoom.sqrMagnitude > Mathf.Epsilon
                ? directionIntoSecondRoom.normalized
                : -first;
            return Vector3.Dot(normal, first) >= Vector3.Dot(normal, second);
        }

        public static float DoorwayPlaneSide(
            Vector3 samplePosition,
            Vector3 doorwayPosition,
            Vector3 directionIntoFirstRoom)
        {
            if (directionIntoFirstRoom.sqrMagnitude <= Mathf.Epsilon)
                return 0f;

            return Vector3.Dot(
                samplePosition - doorwayPosition,
                directionIntoFirstRoom.normalized);
        }

        public static float DoorwayPlaneRoomWeight(float sideIntoFirstRoom, float blendDistance)
        {
            float blend = Mathf.Max(1e-4f, blendDistance);
            return SmoothStep01((sideIntoFirstRoom + blend) / (2f * blend));
        }

        public static float LocationConfidence(float sideIntoFirstRoom, float confidenceDistance)
        {
            float span = Mathf.Max(1e-4f, confidenceDistance);
            return SmoothStep01(Mathf.Abs(sideIntoFirstRoom) / span);
        }

        public static float BlendClosedAndLocationRoomWeight(
            bool closedOwnsFirstRoom,
            float locationFirstRoomWeight,
            float locationConfidence)
        {
            float closed = closedOwnsFirstRoom ? 1f : 0f;
            return Mathf.Lerp(
                closed,
                Mathf.Clamp01(locationFirstRoomWeight),
                Mathf.Clamp01(locationConfidence));
        }

        public static uint DoorCookieReceiveLayer(
            float sideIntoFirstRoom,
            float confidenceDistance)
        {
            if (LocationConfidence(sideIntoFirstRoom, confidenceDistance) < 0.5f)
                return (uint)RoomLocalLightShareContract.DoorSurfaceRenderingLayerMask;

            return sideIntoFirstRoom >= 0f
                ? (uint)RoomLocalLightShareContract.DoorReceiveStartRenderingLayerMask
                : (uint)RoomLocalLightShareContract.DoorReceiveAdministrativeRenderingLayerMask;
        }

        public static float ComputeApertureFeather(
            float normalizedCoordinate,
            float activeSize01,
            float featherFraction)
        {
            float activeSize = Mathf.Clamp01(activeSize01);
            if (activeSize <= Mathf.Epsilon)
                return 0f;

            float halfSize = activeSize * 0.5f;
            float distanceFromCenter = Mathf.Abs(Mathf.Clamp01(normalizedCoordinate) - 0.5f);
            float distanceInside = halfSize - distanceFromCenter;
            float featherWidth = Mathf.Max(activeSize * Mathf.Clamp(featherFraction, 0.001f, 0.5f), 1e-5f);
            return SmoothStep01(distanceInside / featherWidth);
        }

        public static Quaternion DoorLocalRotationForOpenFraction(
            Quaternion closedLocalRotation,
            Vector3 localHingeAxis,
            float openAngleDegrees,
            float openFraction)
        {
            Vector3 axis = localHingeAxis.sqrMagnitude > Mathf.Epsilon
                ? localHingeAxis.normalized
                : Vector3.up;
            return closedLocalRotation *
                   Quaternion.AngleAxis(openAngleDegrees * Mathf.Clamp01(openFraction), axis);
        }

        public static void SurroundingAuthoredPoses(
            float openFraction,
            float[] authoredFractions,
            out int lowerIndex,
            out int upperIndex,
            out float blend)
        {
            lowerIndex = 0;
            upperIndex = 0;
            blend = 0f;
            if (authoredFractions == null || authoredFractions.Length == 0)
                return;

            float t = Mathf.Clamp01(openFraction);
            if (t <= authoredFractions[0])
            {
                lowerIndex = 0;
                upperIndex = 0;
                blend = 0f;
                return;
            }

            int last = authoredFractions.Length - 1;
            if (t >= authoredFractions[last])
            {
                lowerIndex = last;
                upperIndex = last;
                blend = 0f;
                return;
            }

            for (int i = 1; i < authoredFractions.Length; i++)
            {
                if (t > authoredFractions[i])
                    continue;

                lowerIndex = i - 1;
                upperIndex = i;
                float span = authoredFractions[i] - authoredFractions[i - 1];
                blend = span > Mathf.Epsilon
                    ? (t - authoredFractions[i - 1]) / span
                    : 0f;
                return;
            }
        }
    }
}
