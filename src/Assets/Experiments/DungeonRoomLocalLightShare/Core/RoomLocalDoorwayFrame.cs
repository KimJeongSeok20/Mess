using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    /// <summary>
    /// Doorway framing used to place the cookie-spot and the unit injector.
    /// Geometry constants come from the V20/V21 measurements; they are not an overlay mask.
    /// </summary>
    public static class RoomLocalDoorwayFrame
    {
        public const float DefaultSocketWidth = 1f;
        public const float DefaultSocketHeight = 2f;
        public const float CompactLateralAllowance = 0.25f;
        public const float CompactVerticalAllowance = 0.50f;
        public const float CompactDepthAllowance = 0.35f;
        public const float BroadWallSeam = 0.04f;
        public const float SpotStandOff = 0.5f;
        public const float DirectRange = 3.5f;
        public const float LeafFillRange = 1.5f;
        public const float HorizontalSpotAngle = 90f;
        public const float VerticalSpotAngle = 127f;

        public struct Frame
        {
            public Vector3 position;
            public Vector3 outward;
            public Vector3 right;
            public Vector3 up;
            public float halfWidth;
            public float height;
        }

        public static Vector2 OverlappingSocketSize(Vector2 receiver, Vector2 source)
        {
            receiver = ValidSocketSize(receiver);
            source = ValidSocketSize(source);
            return new Vector2(
                Mathf.Min(receiver.x, source.x),
                Mathf.Min(receiver.y, source.y));
        }

        private static Vector2 ValidSocketSize(Vector2 size)
        {
            if (size.x <= 0.05f)
                size.x = DefaultSocketWidth;
            if (size.y <= 0.05f)
                size.y = DefaultSocketHeight;
            return size;
        }

        public static Frame FromDoorway(Transform doorway, Vector2 socketSize)
        {
            Vector2 size = socketSize;
            if (size.x <= 0.05f)
                size.x = DefaultSocketWidth;
            if (size.y <= 0.05f)
                size.y = DefaultSocketHeight;

            return new Frame
            {
                position = doorway != null ? doorway.position : Vector3.zero,
                outward = doorway != null ? doorway.forward.normalized : Vector3.forward,
                right = doorway != null ? doorway.right.normalized : Vector3.right,
                up = doorway != null ? doorway.up.normalized : Vector3.up,
                halfWidth = size.x * 0.5f,
                height = size.y
            };
        }

        public static Vector3 SpotPosition(in Frame frame)
        {
            return SpotPosition(frame, SpotStandOff);
        }

        public static Vector3 SpotPosition(in Frame frame, float sourceStandOff)
        {
            return frame.position +
                   frame.outward * Mathf.Max(0.05f, sourceStandOff) +
                   frame.up * (frame.height * 0.5f);
        }

        public static Quaternion SpotRotation(in Frame frame)
        {
            Vector3 intoReceiver = -frame.outward;
            if (intoReceiver.sqrMagnitude <= Mathf.Epsilon)
                intoReceiver = Vector3.back;
            return Quaternion.LookRotation(intoReceiver.normalized, frame.up);
        }

        public static float FittedSpotAngle(in Frame frame)
        {
            return FittedSpotAngle(frame, SpotStandOff);
        }

        public static float FittedSpotAngle(in Frame frame, float standOff)
        {
            float distance = Mathf.Max(0.05f, standOff);
            float horizontal = 2f * Mathf.Atan2(frame.halfWidth, distance) * Mathf.Rad2Deg;
            float vertical = 2f * Mathf.Atan2(frame.height * 0.5f, distance) * Mathf.Rad2Deg;
            return Mathf.Clamp(Mathf.Max(horizontal, vertical) + 4f, 40f, 110f);
        }

    }
}
