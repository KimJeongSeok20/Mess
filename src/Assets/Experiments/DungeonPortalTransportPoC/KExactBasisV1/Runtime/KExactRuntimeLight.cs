using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace DungeonPortalTransportPoC.KExactBasisV1
{
    internal sealed class KExactRuntimeLightMarker : MonoBehaviour
    {
        public KExactPortalConnection Owner { get; private set; }
        public string RuntimeKey { get; private set; }

        public void Initialize(KExactPortalConnection owner, string runtimeKey)
        {
            Owner = owner;
            RuntimeKey = runtimeKey ?? string.Empty;
        }
    }

    internal sealed class KExactRuntimeLight
    {
        private static readonly string[] LightPropertyCopyOrder =
        {
            "renderMode",
            "type",
            "shape",
            "lightUnit",
            "enableSpotReflector",
            "luxAtDistance",
            "range",
            "spotAngle",
            "innerSpotAngle",
            "shapeRadius",
            "areaSize",
            "color",
            "useColorTemperature",
            "colorTemperature",
            "intensity",
            "bounceIntensity",
            "cookie",
            "cookieSize2D",
            "flare",
            "shadows",
            "shadowStrength",
            "shadowResolution",
            "shadowCustomResolution",
            "shadowBias",
            "shadowNormalBias",
            "shadowNearPlane",
            "shadowAngle",
            "attenuate",
            "shadowConstantBias",
            "shadowObjectSizeBias",
            "shadowRadius",
            "shadowSoftness",
            "shadowSoftnessFade",
            "lightShadowCasterMode",
            "layerShadowCullDistances",
            "cullingMask",
            "renderingLayerMask",
            "lightmapBakeType",
            "useBoundingSphereOverride",
            "boundingSphereOverride",
            "useShadowMatrixOverride",
            "shadowMatrixOverride",
            "useViewFrustumForShadowCasterCull",
            "forceVisible"
        };

        private static readonly HashSet<string> MutablePropertyNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "intensity"
            };

        private readonly KExactPortalConnection owner;
        private readonly Light light;
        private readonly UniversalAdditionalLightData additionalData;
        private readonly Transform expectedParent;
        private readonly Vector3 expectedLocalPosition;
        private readonly Quaternion expectedLocalRotation;
        private readonly Vector3 expectedLocalScale;
        private readonly Dictionary<string, object> invariantLightProperties;
        private readonly float directBaseIntensity;
        private readonly KExactBounceProxyDescriptor bounceDescriptor;
        private readonly KExactProxyRole? proxyRole;
        private readonly uint receiverBit;
        private readonly uint casterBit;
        private readonly bool isDirect;
        private readonly Transform directSourceTransform;
        private readonly bool expectedAdditionalEnabled;
        private readonly bool expectedUsePipelineSettings;
        private readonly Vector2 expectedCookieSize;
        private readonly Vector2 expectedCookieOffset;
        private readonly SoftShadowQuality expectedSoftShadowQuality;
        private readonly int expectedShadowResolutionTier;
        private float expectedIntensity;

        public Light Light => light;

        private KExactRuntimeLight(
            KExactPortalConnection connection,
            Light runtimeLight,
            UniversalAdditionalLightData runtimeAdditionalData,
            float baseIntensity,
            KExactBounceProxyDescriptor proxyDescriptor,
            KExactProxyRole? runtimeProxyRole,
            uint receivingBit,
            uint shadowCasterBit,
            bool direct,
            Transform directSource)
        {
            owner = connection;
            light = runtimeLight;
            additionalData = runtimeAdditionalData;
            directBaseIntensity = baseIntensity;
            bounceDescriptor = proxyDescriptor;
            proxyRole = runtimeProxyRole;
            receiverBit = receivingBit;
            casterBit = shadowCasterBit;
            isDirect = direct;
            directSourceTransform = directSource;
            expectedParent = runtimeLight.transform.parent;
            expectedLocalPosition = runtimeLight.transform.localPosition;
            expectedLocalRotation = runtimeLight.transform.localRotation;
            expectedLocalScale = runtimeLight.transform.localScale;
            invariantLightProperties = CaptureInvariantProperties(runtimeLight);
            expectedAdditionalEnabled = runtimeAdditionalData.enabled;
            expectedUsePipelineSettings = runtimeAdditionalData.usePipelineSettings;
            expectedCookieSize = runtimeAdditionalData.lightCookieSize;
            expectedCookieOffset = runtimeAdditionalData.lightCookieOffset;
            expectedSoftShadowQuality = runtimeAdditionalData.softShadowQuality;
            expectedShadowResolutionTier =
                runtimeAdditionalData.additionalLightsShadowResolutionTier;
            expectedIntensity = runtimeLight.intensity;
        }

        public static bool TryCreateDirect(
            KExactPortalConnection owner,
            string runtimeKey,
            Light source,
            uint receiverBit,
            uint casterBit,
            out KExactRuntimeLight result,
            out string failure)
        {
            result = null;
            if (owner == null || source == null)
            {
                failure = "Direct light clone owner or source is missing.";
                return false;
            }

            if (source.type != LightType.Point && source.type != LightType.Spot)
            {
                failure = $"Production light '{source.name}' uses unsupported realtime " +
                          $"transport type '{source.type}'.";
                return false;
            }

            if (source.cullingMask == 0 || source.commandBufferCount != 0)
            {
                failure = $"Production light '{source.name}' has an empty culling mask or " +
                          "attached command buffers, which cannot be cloned exactly and safely.";
                return false;
            }

            UniversalAdditionalLightData sourceAdditional =
                source.GetComponent<UniversalAdditionalLightData>();
            if (sourceAdditional == null)
            {
                failure = $"Production light '{source.name}' is missing " +
                          nameof(UniversalAdditionalLightData) + ".";
                return false;
            }

            GameObject runtimeObject = CreateInactiveObject(
                owner,
                runtimeKey,
                source.transform.parent,
                source.transform.localPosition,
                source.transform.localRotation,
                source.transform.localScale,
                source.gameObject.layer);

            try
            {
                if (!WorldTransformMatches(source.transform, runtimeObject.transform))
                    throw new InvalidOperationException(
                        "Direct runtime Light world-transform parity failed.");
                Light clone = runtimeObject.AddComponent<Light>();
                if (!TryCopyLightProperties(source, clone, out failure))
                    throw new InvalidOperationException(failure);

                UniversalAdditionalLightData cloneAdditional =
                    runtimeObject.AddComponent<UniversalAdditionalLightData>();
                if (!TryCopyAdditionalData(sourceAdditional, cloneAdditional, out failure))
                    throw new InvalidOperationException(failure);

#if UNITY_EDITOR
                clone.lightmapBakeType = LightmapBakeType.Realtime;
#endif
                clone.bounceIntensity = 0f;
                if (!TryApplyRouting(clone, cloneAdditional, receiverBit, casterBit, out failure))
                    throw new InvalidOperationException(failure);

                float baseIntensity = Mathf.Max(0f, source.intensity);
                clone.intensity = 0f;
                clone.enabled = false;
                runtimeObject.SetActive(true);
                clone.enabled = false;

                result = new KExactRuntimeLight(
                    owner,
                    clone,
                    cloneAdditional,
                    baseIntensity,
                    null,
                    null,
                    receiverBit,
                    casterBit,
                    true,
                    source.transform);
                if (!result.TryValidate(out failure))
                    throw new InvalidOperationException(failure);

                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                DestroyObject(runtimeObject);
                result = null;
                failure = $"Could not create exact clone for '{source.name}': {exception.Message}";
                return false;
            }
        }

        public static bool TryCreateProxy(
            KExactPortalConnection owner,
            string runtimeKey,
            Transform anchor,
            KExactBounceProxyDescriptor descriptor,
            uint receiverBit,
            uint casterBit,
            out KExactRuntimeLight result,
            out string failure)
        {
            result = null;
            if (owner == null || anchor == null || descriptor == null)
            {
                failure = "Bounce proxy owner, anchor, or descriptor is missing.";
                return false;
            }

            if (!descriptor.TryValidate(out failure))
            {
                return false;
            }

            GameObject runtimeObject = CreateInactiveObject(
                owner,
                runtimeKey,
                owner.transform,
                Vector3.zero,
                Quaternion.identity,
                Vector3.one,
                anchor.gameObject.layer);
            // Keep every generated object inside the PoC-owned hierarchy. Parenting a
            // proxy to the production doorway would change production Light counts and
            // make Adjacent OFF fail its hierarchy-parity contract. The doorway is only
            // the spatial basis used to resolve the proxy's world pose at activation.
            runtimeObject.transform.SetPositionAndRotation(
                anchor.TransformPoint(descriptor.LocalPosition),
                anchor.rotation * descriptor.LocalRotation);
            runtimeObject.transform.localScale = Vector3.one;

            try
            {
                Light proxy = runtimeObject.AddComponent<Light>();
                proxy.type = descriptor.Type;
                proxy.color = descriptor.Color;
                proxy.intensity = 0f;
                proxy.bounceIntensity = 0f;
                proxy.range = descriptor.Range;
                proxy.spotAngle = descriptor.SpotAngle;
                proxy.innerSpotAngle = Mathf.Min(
                    descriptor.InnerSpotAngle,
                    descriptor.SpotAngle);
                proxy.shadows = descriptor.Shadows;
                proxy.shadowStrength = descriptor.ShadowStrength;
                proxy.shadowBias = descriptor.ShadowBias;
                proxy.shadowNormalBias = descriptor.ShadowNormalBias;
                proxy.shadowNearPlane = descriptor.ShadowNearPlane;
                proxy.cullingMask = descriptor.CullingMask;
#if UNITY_EDITOR
                proxy.lightmapBakeType = LightmapBakeType.Realtime;
#endif

                UniversalAdditionalLightData proxyAdditional =
                    runtimeObject.AddComponent<UniversalAdditionalLightData>();
                if (!TryApplyRouting(proxy, proxyAdditional, receiverBit, casterBit, out failure))
                    throw new InvalidOperationException(failure);

                proxy.enabled = false;
                runtimeObject.SetActive(true);
                proxy.enabled = false;
                result = new KExactRuntimeLight(
                    owner,
                    proxy,
                    proxyAdditional,
                    0f,
                    descriptor,
                    descriptor.Role,
                    receiverBit,
                    casterBit,
                    false,
                    null);
                if (!result.TryValidate(out failure))
                    throw new InvalidOperationException(failure);

                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                DestroyObject(runtimeObject);
                result = null;
                failure = $"Could not create portal proxy '{descriptor.Key}': {exception.Message}";
                return false;
            }
        }

        // Kept as an assembly-local compatibility alias for authored R2 data while
        // the runtime routes through the explicit descriptor role above.
        public static bool TryCreateBounce(
            KExactPortalConnection owner,
            string runtimeKey,
            Transform anchor,
            KExactBounceProxyDescriptor descriptor,
            uint receiverBit,
            uint casterBit,
            out KExactRuntimeLight result,
            out string failure)
        {
            return TryCreateProxy(
                owner,
                runtimeKey,
                anchor,
                descriptor,
                receiverBit,
                casterBit,
                out result,
                out failure);
        }

        public void ApplyDirectScale(float directScale)
        {
            if (!isDirect)
                return;
            ApplyIntensity(directBaseIntensity * Mathf.Max(0f, directScale));
        }

        public void ApplyProxy(float sourcePower01, float doorOpenness01)
        {
            if (isDirect || bounceDescriptor == null || !proxyRole.HasValue)
                return;

            float intensity = Mathf.Lerp(
                bounceDescriptor.IntensityAtPower0,
                bounceDescriptor.IntensityAtPower100,
                Mathf.Clamp01(sourcePower01));
            switch (proxyRole.Value)
            {
                case KExactProxyRole.ReceiverBounce:
                    ApplyIntensity(intensity * Mathf.Clamp01(doorOpenness01));
                    return;
                case KExactProxyRole.DoorSurface:
                    // The moving leaf must be lit by its source side even while it
                    // blocks room-to-room transmission. Door openness only gates the
                    // receiver-room bounce path, not this local surface response.
                    ApplyIntensity(intensity);
                    return;
                default:
                    // Descriptor validation makes this unreachable, but fail closed
                    // rather than letting an unknown serialized enum emit light.
                    ApplyIntensity(0f);
                    return;
            }
        }

        public void ApplyBounce(float sourcePower01, float doorOpenness01)
        {
            ApplyProxy(sourcePower01, doorOpenness01);
        }

        public bool TryValidate(out string failure)
        {
            if (light == null || additionalData == null || light.gameObject == null)
            {
                failure = "A runtime K-exact light was destroyed.";
                return false;
            }

            if (isDirect)
            {
                if (bounceDescriptor != null || proxyRole.HasValue ||
                    directSourceTransform == null ||
                    !WorldTransformMatches(directSourceTransform, light.transform))
                {
                    failure = $"Direct runtime light '{light.name}' has source-transform " +
                              "or proxy-role drift.";
                    return false;
                }
            }
            else if (bounceDescriptor == null || !proxyRole.HasValue ||
                     bounceDescriptor.Role != proxyRole.Value)
            {
                failure = $"Runtime proxy '{light.name}' descriptor-role parity drifted.";
                return false;
            }
            else if (!bounceDescriptor.TryValidate(out string descriptorFailure))
            {
                failure = $"Runtime proxy '{light.name}' descriptor parity drifted: " +
                          descriptorFailure;
                return false;
            }

            if (light.GetComponent<KExactRuntimeLightMarker>() == null ||
                light.GetComponent<Renderer>() != null ||
                light.GetComponents<Component>().Length != 4 ||
                light.transform.childCount != 0 ||
                light.transform.parent != expectedParent ||
                light.transform.localPosition != expectedLocalPosition ||
                light.transform.localRotation != expectedLocalRotation ||
                light.transform.localScale != expectedLocalScale)
            {
                failure = $"Runtime light '{light.name}' hierarchy/component parity drifted.";
                return false;
            }

            bool bakeModeDrifted = false;
#if UNITY_EDITOR
            bakeModeDrifted = light.lightmapBakeType != LightmapBakeType.Realtime;
#endif

            if (bakeModeDrifted ||
                Mathf.Abs(light.bounceIntensity) > 0.000001f ||
                Mathf.Abs(light.intensity - expectedIntensity) > 0.000001f ||
                light.enabled != (expectedIntensity > 0.000001f) ||
                additionalData.renderingLayers.value != receiverBit ||
                !additionalData.customShadowLayers ||
                additionalData.shadowRenderingLayers.value != casterBit ||
                unchecked((uint)light.renderingLayerMask) != casterBit ||
                additionalData.enabled != expectedAdditionalEnabled ||
                additionalData.usePipelineSettings != expectedUsePipelineSettings ||
                additionalData.lightCookieSize != expectedCookieSize ||
                additionalData.lightCookieOffset != expectedCookieOffset ||
                additionalData.softShadowQuality != expectedSoftShadowQuality ||
                additionalData.additionalLightsShadowResolutionTier !=
                    expectedShadowResolutionTier)
            {
                failure = $"Runtime light '{light.name}' routing or bake-mode parity drifted.";
                return false;
            }

            foreach (KeyValuePair<string, object> expected in invariantLightProperties)
            {
                PropertyInfo property = typeof(Light).GetProperty(
                    expected.Key,
                    BindingFlags.Instance | BindingFlags.Public);
                if (property == null || !property.CanRead)
                    continue;
                object current = property.GetValue(light, null);
                if (!ValuesEqual(expected.Value, current))
                {
                    failure = $"Runtime light '{light.name}' property '{expected.Key}' drifted.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        public void Destroy()
        {
            if (light == null)
                return;
            light.enabled = false;
            DestroyObject(light.gameObject);
        }

        private void ApplyIntensity(float intensity)
        {
            if (light == null)
                return;
            float finiteIntensity = IsFinite(intensity) ? Mathf.Max(0f, intensity) : 0f;
            expectedIntensity = finiteIntensity;
            light.intensity = finiteIntensity;
            light.enabled = finiteIntensity > 0.000001f;
        }

        private static GameObject CreateInactiveObject(
            KExactPortalConnection owner,
            string runtimeKey,
            Transform parent,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale,
            int layer)
        {
            string safeKey = string.IsNullOrWhiteSpace(runtimeKey)
                ? "Unnamed"
                : runtimeKey.Replace('/', '_').Replace('\\', '_');
            var runtimeObject = new GameObject("__KExactRuntimeLight__" + safeKey)
            {
                hideFlags = HideFlags.DontSave,
                layer = layer
            };
            runtimeObject.SetActive(false);
            runtimeObject.transform.SetParent(parent, false);
            runtimeObject.transform.localPosition = localPosition;
            runtimeObject.transform.localRotation = localRotation;
            runtimeObject.transform.localScale = localScale;
            runtimeObject.AddComponent<KExactRuntimeLightMarker>().Initialize(owner, runtimeKey);
            return runtimeObject;
        }

        private static bool WorldTransformMatches(Transform left, Transform right)
        {
            return left != null && right != null &&
                   Vector3.Distance(left.position, right.position) <= 0.0001f &&
                   Quaternion.Angle(left.rotation, right.rotation) <= 0.001f &&
                   Vector3.Distance(left.lossyScale, right.lossyScale) <= 0.0001f;
        }

        private static bool TryCopyLightProperties(
            Light source,
            Light target,
            out string failure)
        {
            for (int i = 0; i < LightPropertyCopyOrder.Length; i++)
            {
                string propertyName = LightPropertyCopyOrder[i];
                PropertyInfo property = typeof(Light).GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public);
                if (property == null || !property.CanRead || !property.CanWrite)
                    continue;

                try
                {
                    object value = CloneValue(property.GetValue(source, null));
                    property.SetValue(target, value, null);
                }
                catch (Exception exception)
                {
                    failure = $"Light property '{propertyName}' could not be copied exactly: " +
                              exception.Message;
                    return false;
                }
            }

            target.enabled = false;
            failure = null;
            return true;
        }

        private static bool TryCopyAdditionalData(
            UniversalAdditionalLightData source,
            UniversalAdditionalLightData target,
            out string failure)
        {
            try
            {
                target.enabled = source.enabled;
                target.usePipelineSettings = source.usePipelineSettings;
                target.lightCookieSize = source.lightCookieSize;
                target.lightCookieOffset = source.lightCookieOffset;
                target.softShadowQuality = source.softShadowQuality;

                FieldInfo tierField = typeof(UniversalAdditionalLightData).GetField(
                    "m_AdditionalLightsShadowResolutionTier",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (tierField == null)
                {
                    if (source.additionalLightsShadowResolutionTier !=
                        target.additionalLightsShadowResolutionTier)
                    {
                        failure = "URP shadow-resolution tier could not be copied exactly.";
                        return false;
                    }
                }
                else
                {
                    tierField.SetValue(target, tierField.GetValue(source));
                }

                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                failure = "URP additional-light data could not be copied exactly: " +
                          exception.Message;
                return false;
            }
        }

        private static bool TryApplyRouting(
            Light target,
            UniversalAdditionalLightData additional,
            uint receiverBit,
            uint casterBit,
            out string failure)
        {
            additional.renderingLayers = receiverBit;
            additional.customShadowLayers = true;
            additional.shadowRenderingLayers = casterBit;
            additional.renderingLayers = receiverBit;

            if (additional.renderingLayers.value != receiverBit ||
                !additional.customShadowLayers ||
                additional.shadowRenderingLayers.value != casterBit ||
                unchecked((uint)target.renderingLayerMask) != casterBit)
            {
                failure = "URP custom lighting/shadow rendering-layer routing did not stick.";
                return false;
            }

            failure = null;
            return true;
        }

        private static Dictionary<string, object> CaptureInvariantProperties(Light target)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i < LightPropertyCopyOrder.Length; i++)
            {
                string name = LightPropertyCopyOrder[i];
                if (MutablePropertyNames.Contains(name))
                    continue;

                PropertyInfo property = typeof(Light).GetProperty(
                    name,
                    BindingFlags.Instance | BindingFlags.Public);
                if (property != null && property.CanRead)
                    result[name] = CloneValue(property.GetValue(target, null));
            }

            return result;
        }

        private static object CloneValue(object value)
        {
            return value is Array array ? array.Clone() : value;
        }

        private static bool ValuesEqual(object left, object right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;
            if (left is Array leftArray && right is Array rightArray)
            {
                if (leftArray.Length != rightArray.Length)
                    return false;
                for (int i = 0; i < leftArray.Length; i++)
                {
                    if (!Equals(leftArray.GetValue(i), rightArray.GetValue(i)))
                        return false;
                }
                return true;
            }

            return Equals(left, right);
        }

        private static void DestroyObject(GameObject target)
        {
            if (target == null)
                return;
            target.SetActive(false);
            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
