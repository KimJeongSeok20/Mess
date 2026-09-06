using System;
using UnityEngine;

namespace DungeonPortalTransportPoC
{
    [CreateAssetMenu(
        fileName = "DungeonPortalEndpointProfile",
        menuName = "Dungeon/Lighting/Portal Transport Endpoint Profile")]
    public sealed class DungeonPortalEndpointProfile : ScriptableObject
    {
        [SerializeField] private string roomId;
        [SerializeField] private string doorwayId;
        [SerializeField] private PortalDirectLightDescriptor[] outgoingDirectLights =
            Array.Empty<PortalDirectLightDescriptor>();
        [SerializeField] private PortalBounceLightDescriptor[] incomingBounceLights =
            Array.Empty<PortalBounceLightDescriptor>();

        public string RoomId => roomId;
        public string DoorwayId => doorwayId;
        public PortalDirectLightDescriptor[] OutgoingDirectLights =>
            outgoingDirectLights ?? Array.Empty<PortalDirectLightDescriptor>();
        public PortalBounceLightDescriptor[] IncomingBounceLights =>
            incomingBounceLights ?? Array.Empty<PortalBounceLightDescriptor>();

        public void ConfigureAuthoring(
            string stableRoomId,
            string stableDoorwayId,
            PortalDirectLightDescriptor[] directLights,
            PortalBounceLightDescriptor[] bounceLights)
        {
            roomId = stableRoomId != null ? stableRoomId.Trim() : string.Empty;
            doorwayId = stableDoorwayId != null ? stableDoorwayId.Trim() : string.Empty;
            outgoingDirectLights = directLights != null
                ? (PortalDirectLightDescriptor[])directLights.Clone()
                : Array.Empty<PortalDirectLightDescriptor>();
            incomingBounceLights = bounceLights != null
                ? (PortalBounceLightDescriptor[])bounceLights.Clone()
                : Array.Empty<PortalBounceLightDescriptor>();
        }

        public bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(roomId))
            {
                error = "The stable room id is empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(doorwayId))
            {
                error = "The stable doorway id is empty.";
                return false;
            }

            PortalDirectLightDescriptor[] directLights = OutgoingDirectLights;
            if (directLights.Length != 1)
            {
                error =
                    $"K=1 endpoint profiles require exactly one outgoing direct light; " +
                    $"found {directLights.Length}.";
                return false;
            }

            for (int i = 0; i < directLights.Length; i++)
            {
                if (!TryValidateDirectLight(directLights[i], i, out error))
                    return false;
            }

            PortalBounceLightDescriptor[] bounceLights = IncomingBounceLights;
            for (int i = 0; i < bounceLights.Length; i++)
            {
                if (!TryValidateBounceLight(bounceLights[i], i, out error))
                    return false;
            }

            error = string.Empty;
            return true;
        }

        [Serializable]
        public struct PortalDirectLightDescriptor
        {
            public string label;
            public LightType type;
            public Vector3 localPosition;
            public Vector3 localEulerAngles;
            [ColorUsage(true, true)] public Color power0Color;
            [ColorUsage(true, true)] public Color power100Color;
            [Min(0f)] public float power0Intensity;
            [Min(0f)] public float power100Intensity;
            [Min(0.01f)] public float range;
            [Range(1f, 179f)] public float spotAngle;
            [Range(0f, 179f)] public float innerSpotAngle;
            [Tooltip("Per-door outgoing radiance/aperture cookie. Direct transport is fail-closed without it.")]
            public Texture2D cookie;
            public bool castShadows;
            [Range(0f, 1f)] public float shadowStrength;
            public LayerMask cullingMask;
            public int renderingLayerMask;
        }

        [Serializable]
        public struct PortalBounceLightDescriptor
        {
            public string label;
            public LightType type;
            public Vector3 localPosition;
            public Vector3 localEulerAngles;
            [ColorUsage(true, true)] public Color responseTint;
            [Min(0f)]
            [Tooltip("Linear response gain multiplied component-wise by the source RGB radiance.")]
            public float responseGainPerUnitSourceRadiance;
            [Min(0.01f)] public float range;
            [Range(1f, 179f)] public float spotAngle;
            [Range(0f, 179f)] public float innerSpotAngle;
            public bool castShadows;
            [Range(0f, 1f)] public float shadowStrength;
            public LayerMask cullingMask;
            public int renderingLayerMask;
        }

        private static bool TryValidateDirectLight(
            PortalDirectLightDescriptor descriptor,
            int index,
            out string error)
        {
            string prefix = $"Outgoing direct light {index}";
            if (string.IsNullOrWhiteSpace(descriptor.label))
            {
                error = $"{prefix} has an empty label.";
                return false;
            }

            if (descriptor.type != LightType.Spot)
            {
                error = $"{prefix} must be a Spot light for cookie-gated K=1 transport.";
                return false;
            }

            if (!IsFinite(descriptor.localPosition) ||
                !IsFinite(descriptor.localEulerAngles))
            {
                error = $"{prefix} has a non-finite local transform.";
                return false;
            }

            if (!IsFiniteNonNegative(descriptor.power0Color) ||
                !IsFiniteNonNegative(descriptor.power100Color) ||
                !IsFiniteNonNegative(descriptor.power0Intensity) ||
                !IsFiniteNonNegative(descriptor.power100Intensity))
            {
                error = $"{prefix} has invalid linear radiance data.";
                return false;
            }

            if (descriptor.power100Intensity <= 0f ||
                Mathf.Max(
                    descriptor.power100Color.r,
                    descriptor.power100Color.g,
                    descriptor.power100Color.b) <= 0f)
            {
                error = $"{prefix} has no positive P100 radiance.";
                return false;
            }

            if (!TryValidateLightShape(
                    prefix,
                    descriptor.range,
                    descriptor.spotAngle,
                    descriptor.innerSpotAngle,
                    descriptor.castShadows,
                    descriptor.shadowStrength,
                    descriptor.cullingMask,
                    descriptor.renderingLayerMask,
                    out error))
            {
                return false;
            }

            if (descriptor.cookie == null ||
                descriptor.cookie.width <= 0 ||
                descriptor.cookie.height <= 0)
            {
                error = $"{prefix} is missing its generated 2D radiance cookie.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidateBounceLight(
            PortalBounceLightDescriptor descriptor,
            int index,
            out string error)
        {
            string prefix = $"Incoming bounce light {index}";
            if (string.IsNullOrWhiteSpace(descriptor.label))
            {
                error = $"{prefix} has an empty label.";
                return false;
            }

            if (descriptor.type != LightType.Point && descriptor.type != LightType.Spot)
            {
                error = $"{prefix} must be a Point or Spot light.";
                return false;
            }

            if (!IsFinite(descriptor.localPosition) ||
                !IsFinite(descriptor.localEulerAngles) ||
                !IsFiniteNonNegative(descriptor.responseTint) ||
                !IsFiniteNonNegative(descriptor.responseGainPerUnitSourceRadiance))
            {
                error = $"{prefix} has invalid transform or response data.";
                return false;
            }

            if (descriptor.responseGainPerUnitSourceRadiance <= 0f ||
                Mathf.Max(
                    descriptor.responseTint.r,
                    descriptor.responseTint.g,
                    descriptor.responseTint.b) <= 0f)
            {
                error = $"{prefix} has no positive response; omit the descriptor instead.";
                return false;
            }

            return TryValidateLightShape(
                prefix,
                descriptor.range,
                descriptor.spotAngle,
                descriptor.innerSpotAngle,
                descriptor.castShadows,
                descriptor.shadowStrength,
                descriptor.cullingMask,
                descriptor.renderingLayerMask,
                out error);
        }

        private static bool TryValidateLightShape(
            string prefix,
            float range,
            float spotAngle,
            float innerSpotAngle,
            bool castShadows,
            float shadowStrength,
            LayerMask cullingMask,
            int renderingLayerMask,
            out string error)
        {
            if (!IsFinite(range) || range < 0.01f)
            {
                error = $"{prefix} has an invalid range.";
                return false;
            }

            if (!IsFinite(spotAngle) || spotAngle < 1f || spotAngle > 179f ||
                !IsFinite(innerSpotAngle) || innerSpotAngle < 0f ||
                innerSpotAngle > spotAngle)
            {
                error = $"{prefix} has invalid spot cone angles.";
                return false;
            }

            if (!castShadows || !IsFinite(shadowStrength) || shadowStrength <= 0f ||
                shadowStrength > 1f)
            {
                error = $"{prefix} must cast non-zero-strength shadows.";
                return false;
            }

            if (cullingMask.value == 0 || renderingLayerMask == 0)
            {
                error = $"{prefix} has an empty GameObject or rendering-layer mask.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFiniteNonNegative(Color value)
        {
            return IsFiniteNonNegative(value.r) &&
                   IsFiniteNonNegative(value.g) &&
                   IsFiniteNonNegative(value.b) &&
                   IsFinite(value.a);
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return IsFinite(value) && value >= 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
