using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DungeonPortalTransportPoC
{
    [DefaultExecutionOrder(-60)]
    [DisallowMultipleComponent]
    public sealed class DungeonPortalEndpoint : MonoBehaviour
    {
        [SerializeField] private Transform doorwayFrame;
        [SerializeField] private DungeonPortalEndpointProfile profile;
        [SerializeField] private DungeonPortalPowerEnvelope powerEnvelope;

        private readonly List<Light> outgoingLights = new List<Light>();
        private readonly List<Light> bounceLights = new List<Light>();
        private DungeonPortalTransportConnection connectionOwner;

        public float Power01 => powerEnvelope != null ? powerEnvelope.Power01 : 0f;
        public Transform DoorwayFrame => doorwayFrame != null ? doorwayFrame : transform;
        public DungeonPortalEndpointProfile Profile => profile;
        public bool IsConnectionActive =>
            connectionOwner != null && isActiveAndEnabled && IsConfigured;
        public bool IsConfigured => profile != null && powerEnvelope != null;

        private void OnEnable()
        {
            if (Application.isPlaying)
                BuildRuntimeLights();
        }

        private void OnDisable()
        {
            SetAllLightsEnabled(false);
            DestroyRuntimeLights();
        }

        public void Configure(
            Transform doorway,
            DungeonPortalEndpointProfile endpointProfile,
            DungeonPortalPowerEnvelope roomPower)
        {
            doorwayFrame = doorway;
            profile = endpointProfile;
            powerEnvelope = roomPower;

            if (Application.isPlaying && isActiveAndEnabled)
                BuildRuntimeLights();
        }

        internal bool TryClaim(DungeonPortalTransportConnection connection)
        {
            if (connection == null || !IsConfigured || !isActiveAndEnabled)
                return false;

            if (connectionOwner != null && connectionOwner != connection)
                return false;

            connectionOwner = connection;
            if (Application.isPlaying && outgoingLights.Count == 0 && bounceLights.Count == 0)
                BuildRuntimeLights();

            ApplyOutgoingPower();
            ClearIncomingBounce();
            return true;
        }

        internal void Release(DungeonPortalTransportConnection connection)
        {
            if (connectionOwner != connection)
                return;

            connectionOwner = null;
            SetAllLightsEnabled(false);
        }

        public void ApplyOutgoingPower()
        {
            if (!IsConnectionActive || profile == null)
            {
                DisableLights(outgoingLights);
                return;
            }

            DungeonPortalEndpointProfile.PortalDirectLightDescriptor[] descriptors =
                profile.OutgoingDirectLights;
            int count = Mathf.Min(descriptors.Length, outgoingLights.Count);
            for (int i = 0; i < count; i++)
            {
                DungeonPortalEndpointProfile.PortalDirectLightDescriptor descriptor = descriptors[i];
                Light light = outgoingLights[i];
                if (light == null)
                    continue;

                Color radiance = PortalTransportMath.InterpolateLinearRadiance(
                    descriptor.power0Color,
                    descriptor.power0Intensity,
                    descriptor.power100Color,
                    descriptor.power100Intensity,
                    Power01);
                ApplyRadiance(light, radiance);
            }
        }

        public Color EvaluateOutgoingRadiance()
        {
            if (!IsConnectionActive || profile == null)
                return Color.black;

            Color radiance = Color.black;
            DungeonPortalEndpointProfile.PortalDirectLightDescriptor[] descriptors =
                profile.OutgoingDirectLights;
            int count = Mathf.Min(descriptors.Length, outgoingLights.Count);
            for (int i = 0; i < count; i++)
            {
                if (outgoingLights[i] == null)
                    continue;

                DungeonPortalEndpointProfile.PortalDirectLightDescriptor descriptor = descriptors[i];
                radiance += PortalTransportMath.InterpolateLinearRadiance(
                    descriptor.power0Color,
                    descriptor.power0Intensity,
                    descriptor.power100Color,
                    descriptor.power100Intensity,
                    Power01);
            }

            return radiance;
        }

        public void ApplyIncomingBounce(Color sourceRadiance, float apertureFraction)
        {
            if (!IsConnectionActive || profile == null)
            {
                ClearIncomingBounce();
                return;
            }

            float transferWeight = PortalTransportMath.ComposeTransferWeight(
                1f,
                apertureFraction);
            DungeonPortalEndpointProfile.PortalBounceLightDescriptor[] descriptors =
                profile.IncomingBounceLights;
            int count = Mathf.Min(descriptors.Length, bounceLights.Count);
            for (int i = 0; i < count; i++)
            {
                DungeonPortalEndpointProfile.PortalBounceLightDescriptor descriptor = descriptors[i];
                Light light = bounceLights[i];
                if (light == null)
                    continue;

                Color bounceRadiance = MultiplyRgb(sourceRadiance, descriptor.responseTint) *
                                       descriptor.responseGainPerUnitSourceRadiance *
                                       transferWeight;
                ApplyRadiance(light, bounceRadiance);
            }
        }

        public void ClearIncomingBounce()
        {
            for (int i = 0; i < bounceLights.Count; i++)
            {
                Light light = bounceLights[i];
                if (light == null)
                    continue;

                light.intensity = 0f;
                light.enabled = false;
            }
        }

        private void BuildRuntimeLights()
        {
            DestroyRuntimeLights();
            if (profile == null)
                return;

            Transform anchor = DoorwayFrame;
            DungeonPortalEndpointProfile.PortalDirectLightDescriptor[] directDescriptors =
                profile.OutgoingDirectLights;
            for (int i = 0; i < directDescriptors.Length; i++)
            {
                DungeonPortalEndpointProfile.PortalDirectLightDescriptor descriptor = directDescriptors[i];
                outgoingLights.Add(CreateProxyLight(
                    anchor,
                    descriptor.label,
                    descriptor.type,
                    descriptor.localPosition,
                    descriptor.localEulerAngles,
                    descriptor.range,
                    descriptor.spotAngle,
                    descriptor.innerSpotAngle,
                    descriptor.cookie,
                    true,
                    descriptor.castShadows,
                    descriptor.shadowStrength,
                    descriptor.cullingMask,
                    descriptor.renderingLayerMask));
            }

            DungeonPortalEndpointProfile.PortalBounceLightDescriptor[] bounceDescriptors =
                profile.IncomingBounceLights;
            for (int i = 0; i < bounceDescriptors.Length; i++)
            {
                DungeonPortalEndpointProfile.PortalBounceLightDescriptor descriptor = bounceDescriptors[i];
                Light light = CreateProxyLight(
                    anchor,
                    descriptor.label,
                    descriptor.type,
                    descriptor.localPosition,
                    descriptor.localEulerAngles,
                    descriptor.range,
                    descriptor.spotAngle,
                    descriptor.innerSpotAngle,
                    null,
                    false,
                    descriptor.castShadows,
                    descriptor.shadowStrength,
                    descriptor.cullingMask,
                    descriptor.renderingLayerMask);
                if (light != null)
                    light.enabled = false;
                bounceLights.Add(light);
            }

            if (IsConnectionActive)
                ApplyOutgoingPower();
            else
                SetAllLightsEnabled(false);
        }

        private static Light CreateProxyLight(
            Transform parent,
            string label,
            LightType requestedType,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            float range,
            float spotAngle,
            float innerSpotAngle,
            Texture cookie,
            bool requiresSpotCookie,
            bool castShadows,
            float shadowStrength,
            LayerMask cullingMask,
            int renderingLayerMask)
        {
            if ((requestedType != LightType.Point && requestedType != LightType.Spot) ||
                (requiresSpotCookie && (requestedType != LightType.Spot || cookie == null)) ||
                !castShadows || shadowStrength <= 0.0001f ||
                cullingMask.value == 0 || renderingLayerMask == 0)
                return null;

            var lightObject = new GameObject(string.IsNullOrWhiteSpace(label)
                ? "PortalProxyLight"
                : label);
            lightObject.hideFlags = HideFlags.DontSave;
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localPosition = localPosition;
            lightObject.transform.localEulerAngles = localEulerAngles;

            Light light = lightObject.AddComponent<Light>();
            light.type = requestedType;
            light.range = Mathf.Max(0.01f, range);
            light.spotAngle = Mathf.Clamp(spotAngle, 1f, 179f);
            light.innerSpotAngle = Mathf.Clamp(innerSpotAngle, 0f, light.spotAngle);
            light.cookie = cookie;
            light.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = Mathf.Clamp01(shadowStrength);
            light.cullingMask = cullingMask.value;
            light.renderingLayerMask = renderingLayerMask;
#if UNITY_EDITOR
            light.lightmapBakeType = LightmapBakeType.Realtime;
#endif
            light.bounceIntensity = 0f;

            UniversalAdditionalLightData additionalLightData =
                light.GetUniversalAdditionalLightData();
            uint renderingLayers = unchecked((uint)renderingLayerMask);
            additionalLightData.renderingLayers = renderingLayers;
            additionalLightData.shadowRenderingLayers = renderingLayers;
            return light;
        }

        private void DestroyRuntimeLights()
        {
            DestroyLights(outgoingLights);
            DestroyLights(bounceLights);
        }

        private static void DestroyLights(List<Light> lights)
        {
            for (int i = 0; i < lights.Count; i++)
            {
                Light light = lights[i];
                if (light == null)
                    continue;

                light.enabled = false;
                if (Application.isPlaying)
                    Destroy(light.gameObject);
                else
                    DestroyImmediate(light.gameObject);
            }

            lights.Clear();
        }

        private void SetAllLightsEnabled(bool enabled)
        {
            if (!enabled)
            {
                DisableLights(outgoingLights);
                DisableLights(bounceLights);
            }
        }

        private static void DisableLights(List<Light> lights)
        {
            for (int i = 0; i < lights.Count; i++)
            {
                Light light = lights[i];
                if (light != null)
                    light.enabled = false;
            }
        }

        private static void ApplyRadiance(Light light, Color linearRadiance)
        {
            float maximumChannel = Mathf.Max(
                0f,
                Mathf.Max(linearRadiance.r, Mathf.Max(linearRadiance.g, linearRadiance.b)));
            if (maximumChannel <= 0.0001f)
            {
                light.intensity = 0f;
                light.enabled = false;
                return;
            }

            light.color = new Color(
                Mathf.Max(0f, linearRadiance.r) / maximumChannel,
                Mathf.Max(0f, linearRadiance.g) / maximumChannel,
                Mathf.Max(0f, linearRadiance.b) / maximumChannel,
                1f);
            light.intensity = maximumChannel;
            light.enabled = true;
        }

        private static Color MultiplyRgb(Color left, Color right)
        {
            return new Color(
                left.r * right.r,
                left.g * right.g,
                left.b * right.b,
                left.a * right.a);
        }
    }
}
