using System;
using UnityEngine;

namespace DungeonPortalTransportPoC.KExactBasisV1
{
    /// <summary>
    /// Declares the isolated receiver domain and physical weighting contract for a
    /// portal proxy. Runtime routing deliberately has no implicit fallback: a proxy
    /// authored for a room must never light the moving door, and a proxy authored
    /// for the door must never spill into either room.
    /// </summary>
    public enum KExactProxyRole
    {
        ReceiverBounce = 0,
        DoorSurface = 1
    }

    [Serializable]
    public sealed class KExactBounceProxyDescriptor
    {
        [SerializeField] private string key;
        [SerializeField] private KExactProxyRole role = KExactProxyRole.ReceiverBounce;
        [SerializeField] private LightType type = LightType.Point;
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Vector3 localEulerAngles;
        [SerializeField] private Color color = Color.white;
        [SerializeField, Min(0f)] private float intensityAtPower0;
        [SerializeField, Min(0f)] private float intensityAtPower100 = 0.1f;
        [SerializeField, Min(0.01f)] private float range = 3f;
        [SerializeField, Range(1f, 179f)] private float spotAngle = 90f;
        [SerializeField, Range(0f, 179f)] private float innerSpotAngle;
        [SerializeField] private LightShadows shadows = LightShadows.Soft;
        [SerializeField, Range(0f, 1f)] private float shadowStrength = 1f;
        [SerializeField, Min(0f)] private float shadowBias = 0.05f;
        [SerializeField, Min(0f)] private float shadowNormalBias = 0.4f;
        [SerializeField, Min(0f)] private float shadowNearPlane = 0.2f;
        [SerializeField] private LayerMask cullingMask = ~0;

        public string Key => key;
        public KExactProxyRole Role => role;
        public LightType Type => type;
        public Vector3 LocalPosition => localPosition;
        public Quaternion LocalRotation => Quaternion.Euler(localEulerAngles);
        public Color Color => color;
        public float IntensityAtPower0 => intensityAtPower0;
        public float IntensityAtPower100 => intensityAtPower100;
        public float Range => range;
        public float SpotAngle => spotAngle;
        public float InnerSpotAngle => innerSpotAngle;
        public LightShadows Shadows => shadows;
        public float ShadowStrength => shadowStrength;
        public float ShadowBias => shadowBias;
        public float ShadowNormalBias => shadowNormalBias;
        public float ShadowNearPlane => shadowNearPlane;
        public int CullingMask => cullingMask.value;

        public void Configure(
            string descriptorKey,
            LightType lightType,
            Vector3 position,
            Vector3 eulerAngles,
            Color lightColor,
            float power0Intensity,
            float power100Intensity,
            float lightRange,
            float outerSpotAngle = 90f,
            float innerAngle = 0f,
            LightShadows lightShadows = LightShadows.Soft,
            float lightShadowStrength = 1f,
            int lightCullingMask = ~0,
            KExactProxyRole proxyRole = KExactProxyRole.ReceiverBounce)
        {
            key = descriptorKey ?? string.Empty;
            role = proxyRole;
            type = lightType;
            localPosition = position;
            localEulerAngles = eulerAngles;
            color = lightColor;
            intensityAtPower0 = Mathf.Max(0f, power0Intensity);
            intensityAtPower100 = Mathf.Max(0f, power100Intensity);
            range = Mathf.Max(0.01f, lightRange);
            spotAngle = Mathf.Clamp(outerSpotAngle, 1f, 179f);
            innerSpotAngle = Mathf.Clamp(innerAngle, 0f, spotAngle);
            shadows = lightShadows;
            shadowStrength = Mathf.Clamp01(lightShadowStrength);
            cullingMask = lightCullingMask;
        }

        internal bool TryValidate(out string failure)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                failure = "Bounce proxy key is empty.";
                return false;
            }

            if (role != KExactProxyRole.ReceiverBounce &&
                role != KExactProxyRole.DoorSurface)
            {
                failure = $"Bounce proxy '{key}' has an unsupported role '{role}'.";
                return false;
            }

            if (type != LightType.Point && type != LightType.Spot)
            {
                failure = $"Bounce proxy '{key}' uses unsupported realtime type '{type}'.";
                return false;
            }

            if (!IsFinite(intensityAtPower0) || !IsFinite(intensityAtPower100) ||
                intensityAtPower0 < 0f || intensityAtPower100 < 0f ||
                !IsFinite(range) || range <= 0f || cullingMask.value == 0)
            {
                failure = $"Bounce proxy '{key}' has invalid intensity/range/culling data.";
                return false;
            }

            failure = null;
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    [Serializable]
    public sealed class KExactDirectedTransportBinding
    {
        [SerializeField] private string directedKey;
        [SerializeField] private string layerGroupKey;
        [SerializeField] private KExactScalarSource sourcePower;
        [SerializeField] private Light[] selectedProductionLights = Array.Empty<Light>();
        [SerializeField] private Renderer[] receiverRenderers = Array.Empty<Renderer>();
        [SerializeField] private uint receiverRenderingLayerBit;
        [SerializeField, Min(0f)] private float directIntensityScaleAtPower0;
        [SerializeField, Min(0f)] private float directIntensityScaleAtPower100 = 1f;
        // Existing R2 serialized names remain the receiver-facing bounce contract.
        // A separate source-side anchor/list is required for door-surface lighting so
        // that door light never consumes room-receiver routing capacity.
        [SerializeField] private Transform bounceAnchor;
        [SerializeField] private KExactBounceProxyDescriptor[] bounceProxies =
            Array.Empty<KExactBounceProxyDescriptor>();
        [SerializeField] private Transform doorSurfaceAnchor;
        [SerializeField] private KExactBounceProxyDescriptor[] doorSurfaceProxies =
            Array.Empty<KExactBounceProxyDescriptor>();
        [SerializeField, Min(0f)] private float residualReflectionAtPower0 = 0.05f;
        [SerializeField, Min(0f)] private float reflectionAtPower100 = 1f;

        public string DirectedKey => directedKey;
        public string LayerGroupKey => layerGroupKey;
        public KExactScalarSource SourcePower => sourcePower;
        public Light[] SelectedProductionLights => selectedProductionLights ?? Array.Empty<Light>();
        public Renderer[] ReceiverRenderers => receiverRenderers ?? Array.Empty<Renderer>();
        public uint ReceiverRenderingLayerBit => receiverRenderingLayerBit;
        public float DirectIntensityScaleAtPower0 => directIntensityScaleAtPower0;
        public float DirectIntensityScaleAtPower100 => directIntensityScaleAtPower100;
        public Transform ReceiverBounceAnchor => bounceAnchor;
        public KExactBounceProxyDescriptor[] ReceiverBounceProxies =>
            bounceProxies ?? Array.Empty<KExactBounceProxyDescriptor>();
        public Transform BounceAnchor => bounceAnchor;
        public KExactBounceProxyDescriptor[] BounceProxies =>
            bounceProxies ?? Array.Empty<KExactBounceProxyDescriptor>();
        public Transform DoorSurfaceAnchor => doorSurfaceAnchor;
        public KExactBounceProxyDescriptor[] DoorSurfaceProxies =>
            doorSurfaceProxies ?? Array.Empty<KExactBounceProxyDescriptor>();
        public float ResidualReflectionAtPower0 => residualReflectionAtPower0;
        public float ReflectionAtPower100 => reflectionAtPower100;

        public void Configure(
            string key,
            string renderingLayerGroupKey,
            KExactScalarSource powerSource,
            Light[] productionLights,
            Renderer[] receivingRenderers,
            uint receiverLayerBit,
            float power0DirectScale = 0f,
            float power100DirectScale = 1f,
            float power0ResidualReflection = 0.05f,
            float power100Reflection = 1f)
        {
            directedKey = key ?? string.Empty;
            layerGroupKey = renderingLayerGroupKey ?? string.Empty;
            sourcePower = powerSource;
            selectedProductionLights = CopyArray(productionLights);
            receiverRenderers = CopyArray(receivingRenderers);
            receiverRenderingLayerBit = receiverLayerBit;
            directIntensityScaleAtPower0 = Mathf.Max(0f, power0DirectScale);
            directIntensityScaleAtPower100 = Mathf.Max(0f, power100DirectScale);
            residualReflectionAtPower0 = Mathf.Max(0f, power0ResidualReflection);
            reflectionAtPower100 = Mathf.Max(0f, power100Reflection);
        }

        public void ConfigureBounceProxies(
            Transform doorLocalAnchor,
            KExactBounceProxyDescriptor[] descriptors)
        {
            ConfigureReceiverBounceProxies(doorLocalAnchor, descriptors);
        }

        public void ConfigureReceiverBounceProxies(
            Transform receiverFacingDoorAnchor,
            KExactBounceProxyDescriptor[] descriptors)
        {
            bounceAnchor = receiverFacingDoorAnchor;
            bounceProxies = CopyArray(descriptors);
        }

        public void ConfigureDoorSurfaceProxies(
            Transform sourceSideDoorAnchor,
            KExactBounceProxyDescriptor[] descriptors)
        {
            doorSurfaceAnchor = sourceSideDoorAnchor;
            doorSurfaceProxies = CopyArray(descriptors);
        }

        private static T[] CopyArray<T>(T[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<T>();

            var copy = new T[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }

    [Serializable]
    public struct KExactTransportWeights
    {
        [SerializeField] private float powerAToB01;
        [SerializeField] private float powerBToA01;
        [SerializeField] private float doorOpenness01;
        [SerializeField] private float directScaleAToB;
        [SerializeField] private float directScaleBToA;
        [SerializeField] private float reflectionWeightAToB;
        [SerializeField] private float reflectionWeightBToA;

        public float PowerAToB01 => powerAToB01;
        public float PowerBToA01 => powerBToA01;
        public float DoorOpenness01 => doorOpenness01;
        public float DirectScaleAToB => directScaleAToB;
        public float DirectScaleBToA => directScaleBToA;
        public float ReflectionWeightAToB => reflectionWeightAToB;
        public float ReflectionWeightBToA => reflectionWeightBToA;

        public KExactTransportWeights(
            float aToBPower,
            float bToAPower,
            float doorOpen,
            float aToBDirect,
            float bToADirect,
            float aToBReflection,
            float bToAReflection)
        {
            powerAToB01 = aToBPower;
            powerBToA01 = bToAPower;
            doorOpenness01 = doorOpen;
            directScaleAToB = aToBDirect;
            directScaleBToA = bToADirect;
            reflectionWeightAToB = aToBReflection;
            reflectionWeightBToA = bToAReflection;
        }
    }
}
