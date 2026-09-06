using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DungeonPortalTransportPoC.KExactBasisV1
{
    [DefaultExecutionOrder(-80)]
    [DisallowMultipleComponent]
    public sealed class KExactPortalConnection : MonoBehaviour
    {
        [SerializeField] private string connectionKey;
        [SerializeField] private KExactDirectedTransportBinding aToB = new KExactDirectedTransportBinding();
        [SerializeField] private KExactDirectedTransportBinding bToA = new KExactDirectedTransportBinding();
        [SerializeField] private Renderer[] shadowCasters = Array.Empty<Renderer>();
        [SerializeField] private Renderer[] doorLeafRenderers = Array.Empty<Renderer>();
        [SerializeField] private KExactScalarSource doorOpenness;
        [SerializeField] private uint casterRenderingLayerBit;
        [SerializeField] private uint doorReceiverRenderingLayerBit;
        [SerializeField] private KExactReflectionProbeBlend[] reflectionBlends =
            Array.Empty<KExactReflectionProbeBlend>();
        [SerializeField] private bool activateOnEnable;
        [SerializeField, Min(0f)] private float powerRiseSeconds = 0.35f;
        [SerializeField, Min(0f)] private float powerFallSeconds = 0.5f;
        [SerializeField, Min(0f)] private float doorOpenSeconds = 0.2f;
        [SerializeField, Min(0f)] private float doorCloseSeconds = 0.2f;
        [SerializeField, Min(0.05f)] private float parityValidationInterval = 0.5f;

        private readonly List<KExactRuntimeLight> directAToB =
            new List<KExactRuntimeLight>();
        private readonly List<KExactRuntimeLight> directBToA =
            new List<KExactRuntimeLight>();
        private readonly List<KExactRuntimeLight> receiverBounceAToB =
            new List<KExactRuntimeLight>();
        private readonly List<KExactRuntimeLight> receiverBounceBToA =
            new List<KExactRuntimeLight>();
        private readonly List<KExactRuntimeLight> doorSurfaceAToB =
            new List<KExactRuntimeLight>();
        private readonly List<KExactRuntimeLight> doorSurfaceBToA =
            new List<KExactRuntimeLight>();

        private bool transportActive;
        private bool faultLatched;
        private string faultReason;
        private float powerAToB01;
        private float powerBToA01;
        private float doorOpenness01;
        private float powerAToBVelocity;
        private float powerBToAVelocity;
        private float doorVelocity;
        private float nextParityValidationTime;
        private KExactTransportWeights currentWeights;
        private KExactTransportWeights lastNotifiedWeights;
        private bool hasNotifiedWeights;

        public event Action<KExactTransportWeights> WeightsChanged;

        public string ConnectionKey => connectionKey;
        public KExactDirectedTransportBinding AToB => aToB;
        public KExactDirectedTransportBinding BToA => bToA;
        public KExactScalarSource DoorOpennessSource => doorOpenness;
        public uint CasterRenderingLayerBit => casterRenderingLayerBit;
        public uint DoorReceiverRenderingLayerBit => doorReceiverRenderingLayerBit;
        public bool IsTransportActive => transportActive;
        public bool IsFaultLatched => faultLatched;
        public string FaultReason => faultReason;
        public float PowerAToB01 => powerAToB01;
        public float PowerBToA01 => powerBToA01;
        public float SmoothedDoorOpenness01 => doorOpenness01;
        public float DirectScaleAToB => currentWeights.DirectScaleAToB;
        public float DirectScaleBToA => currentWeights.DirectScaleBToA;
        public float ReflectionWeightAToB => currentWeights.ReflectionWeightAToB;
        public float ReflectionWeightBToA => currentWeights.ReflectionWeightBToA;
        public float ReceiverBounceTotalIntensityAToB =>
            SumRuntimeLightIntensities(receiverBounceAToB);
        public float ReceiverBounceTotalIntensityBToA =>
            SumRuntimeLightIntensities(receiverBounceBToA);
        public float DoorSurfaceTotalIntensityAToB =>
            SumRuntimeLightIntensities(doorSurfaceAToB);
        public float DoorSurfaceTotalIntensityBToA =>
            SumRuntimeLightIntensities(doorSurfaceBToA);
        public KExactTransportWeights CurrentWeights => currentWeights;

        public void Configure(
            string key,
            KExactDirectedTransportBinding firstDirection,
            KExactDirectedTransportBinding secondDirection,
            Renderer[] allShadowCasters,
            Renderer[] movingDoorLeafRenderers,
            KExactScalarSource doorOpenSource,
            uint casterLayerBit,
            uint doorReceiverLayerBit)
        {
            Deactivate();
            connectionKey = key ?? string.Empty;
            aToB = firstDirection ?? new KExactDirectedTransportBinding();
            bToA = secondDirection ?? new KExactDirectedTransportBinding();
            shadowCasters = CopyArray(allShadowCasters);
            doorLeafRenderers = CopyArray(movingDoorLeafRenderers);
            doorOpenness = doorOpenSource;
            casterRenderingLayerBit = casterLayerBit;
            doorReceiverRenderingLayerBit = doorReceiverLayerBit;
            ResetFault();
        }

        // Retained only so pre-R3 authoring code can compile while it is upgraded.
        // It deliberately leaves the new bit unset; R3 configuration validation then
        // fails closed instead of quietly routing door light through a room bit.
        public void Configure(
            string key,
            KExactDirectedTransportBinding firstDirection,
            KExactDirectedTransportBinding secondDirection,
            Renderer[] allShadowCasters,
            Renderer[] movingDoorLeafRenderers,
            KExactScalarSource doorOpenSource,
            uint casterLayerBit)
        {
            Configure(
                key,
                firstDirection,
                secondDirection,
                allShadowCasters,
                movingDoorLeafRenderers,
                doorOpenSource,
                casterLayerBit,
                0u);
        }

        public void ConfigureReflectionBlends(KExactReflectionProbeBlend[] blends)
        {
            if (transportActive)
                Deactivate();
            reflectionBlends = CopyArray(blends);
        }

        public void ConfigureSmoothing(
            float powerRise,
            float powerFall,
            float doorOpen,
            float doorClose)
        {
            powerRiseSeconds = Mathf.Max(0f, powerRise);
            powerFallSeconds = Mathf.Max(0f, powerFall);
            doorOpenSeconds = Mathf.Max(0f, doorOpen);
            doorCloseSeconds = Mathf.Max(0f, doorClose);
        }

        public void SetActivateOnEnable(bool value)
        {
            activateOnEnable = value;
        }

        public bool TryActivate(out string failure)
        {
            if (!Application.isPlaying)
            {
                failure = "K-exact transport activates only in Play Mode.";
                return false;
            }

            if (transportActive)
            {
                if (TryValidateParity(out failure))
                    return true;
                LatchFault(failure, true);
                return false;
            }

            if (faultLatched)
            {
                failure = "Connection is fault-latched. Call ResetFault before retrying: " +
                          faultReason;
                return false;
            }

            if (!TryValidateConfigurationOnly(out failure) ||
                !TryReadInputs(out float rawAToB, out float rawBToA, out float rawDoor, out failure))
            {
                LatchFault(failure, false);
                return false;
            }

            if (!KExactRenderingLayerRegistry.TryReserve(
                    this,
                    connectionKey,
                    aToB,
                    bToA,
                    casterRenderingLayerBit,
                    doorReceiverRenderingLayerBit,
                    out failure))
            {
                LatchFault(failure, false);
                return false;
            }

            if (!TryBuildRuntimeLights(
                    aToB,
                    directAToB,
                    receiverBounceAToB,
                    doorSurfaceAToB,
                    out failure) ||
                !TryBuildRuntimeLights(
                    bToA,
                    directBToA,
                    receiverBounceBToA,
                    doorSurfaceBToA,
                    out failure))
            {
                TearDownRuntime(false, out _);
                LatchFault(failure, false);
                return false;
            }

            Dictionary<Renderer, uint> leasePlan = BuildRendererLeasePlan();
            if (!KExactRendererLayerLeaseTable.TryAcquire(this, leasePlan, out failure))
            {
                TearDownRuntime(false, out _);
                LatchFault(failure, false);
                return false;
            }

            transportActive = true;
            powerAToB01 = rawAToB;
            powerBToA01 = rawBToA;
            doorOpenness01 = rawDoor;
            powerAToBVelocity = 0f;
            powerBToAVelocity = 0f;
            doorVelocity = 0f;
            currentWeights = BuildWeights();
            ApplyLightWeights();

            for (int i = 0; i < reflectionBlends.Length; i++)
            {
                KExactReflectionProbeBlend blend = reflectionBlends[i];
                if (blend != null && blend.TryActivate(this, currentWeights, out failure))
                    continue;

                failure = blend == null
                    ? $"Reflection blend index {i} is missing."
                    : $"Reflection blend '{blend.BlendKey}' activation failed: {failure}";
                TearDownRuntime(true, out _);
                LatchFault(failure, false);
                return false;
            }

            nextParityValidationTime = Time.unscaledTime + parityValidationInterval;
            NotifyWeights(true);
            if (!TryValidateParity(out failure))
            {
                LatchFault(failure, true);
                return false;
            }

            return true;
        }

        public bool TryValidateConfigurationOnly(out string failure)
        {
            if (!TryValidateConfiguration(out failure))
                return false;

            uint bitA = aToB.ReceiverRenderingLayerBit;
            uint bitB = bToA.ReceiverRenderingLayerBit;
            if (!KExactRenderingLayerRegistry.IsSingleAllowedBit(bitA) ||
                !KExactRenderingLayerRegistry.IsSingleAllowedBit(bitB) ||
                !KExactRenderingLayerRegistry.IsSingleAllowedBit(casterRenderingLayerBit) ||
                !KExactRenderingLayerRegistry.IsSingleAllowedBit(doorReceiverRenderingLayerBit) ||
                bitA == bitB || bitA == casterRenderingLayerBit ||
                bitB == casterRenderingLayerBit || bitA == doorReceiverRenderingLayerBit ||
                bitB == doorReceiverRenderingLayerBit ||
                casterRenderingLayerBit == doorReceiverRenderingLayerBit)
            {
                failure = "Directed receiver, caster, and door receiver layers must be four " +
                          "distinct " +
                          "single portal bits in defined bits 2..7.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(connectionKey) ||
                string.Equals(connectionKey, aToB.DirectedKey, StringComparison.Ordinal) ||
                string.Equals(connectionKey, bToA.DirectedKey, StringComparison.Ordinal) ||
                string.Equals(aToB.DirectedKey, bToA.DirectedKey, StringComparison.Ordinal))
            {
                failure = "Connection and directed keys must be non-empty and unique.";
                return false;
            }

            failure = null;
            return true;
        }

        public bool TryApplyCurrentInputsImmediately(out string failure)
        {
            if (!transportActive)
            {
                failure = "Connection must be active before applying inputs immediately.";
                return false;
            }

            if (!TryReadInputs(out powerAToB01, out powerBToA01, out doorOpenness01,
                    out failure))
            {
                LatchFault(failure, true);
                return false;
            }

            powerAToBVelocity = 0f;
            powerBToAVelocity = 0f;
            doorVelocity = 0f;
            currentWeights = BuildWeights();
            ApplyLightWeights();
            for (int i = 0; i < reflectionBlends.Length; i++)
            {
                if (reflectionBlends[i] != null &&
                    reflectionBlends[i].TryApplyWeights(this, currentWeights, out failure))
                {
                    continue;
                }

                LatchFault(
                    $"Reflection blend index {i} immediate update failed: {failure}",
                    true);
                return false;
            }

            NotifyWeights(true);
            if (TryValidateParity(out failure))
                return true;
            LatchFault(failure, true);
            return false;
        }

        public void Deactivate()
        {
            TearDownRuntime(true, out string restoreFailure);
            if (!string.IsNullOrEmpty(restoreFailure))
            {
                Debug.LogError(
                    $"[{nameof(KExactPortalConnection)}] Exact rendering-layer restore failed: " +
                    restoreFailure,
                    this);
            }
        }

        public void ResetFault()
        {
            if (transportActive)
                return;
            faultLatched = false;
            faultReason = null;
        }

        public bool TryValidateParity(out string failure)
        {
            if (!transportActive)
            {
                failure = "Connection is not active.";
                return false;
            }

            if (!KExactRenderingLayerRegistry.TryValidateOwner(this, out failure) ||
                !KExactRendererLayerLeaseTable.TryValidateOwner(this, out failure) ||
                !ValidateLights(directAToB, out failure) ||
                !ValidateLights(directBToA, out failure) ||
                !ValidateLights(receiverBounceAToB, out failure) ||
                !ValidateLights(receiverBounceBToA, out failure) ||
                !ValidateLights(doorSurfaceAToB, out failure) ||
                !ValidateLights(doorSurfaceBToA, out failure))
            {
                return false;
            }

            for (int i = 0; i < reflectionBlends.Length; i++)
            {
                if (reflectionBlends[i] == null ||
                    !reflectionBlends[i].TryValidate(this, out failure))
                {
                    failure = $"Reflection blend index {i} parity failed: {failure}";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private void OnEnable()
        {
            if (Application.isPlaying && activateOnEnable && !transportActive && !faultLatched)
            {
                if (!TryActivate(out string failure))
                    Debug.LogError($"[{nameof(KExactPortalConnection)}] {failure}", this);
            }
        }

        private void OnDisable()
        {
            if (transportActive || KExactRenderingLayerRegistry.IsReservedBy(this))
                Deactivate();
        }

        private void OnDestroy()
        {
            TearDownRuntime(true, out _);
        }

        private void LateUpdate()
        {
            if (!transportActive)
                return;

            if (!TryReadInputs(out float rawAToB, out float rawBToA, out float rawDoor,
                    out string failure))
            {
                LatchFault(failure, true);
                return;
            }

            float deltaTime = Mathf.Max(0f, Time.unscaledDeltaTime);
            powerAToB01 = Smooth(
                powerAToB01,
                rawAToB,
                ref powerAToBVelocity,
                powerRiseSeconds,
                powerFallSeconds,
                deltaTime);
            powerBToA01 = Smooth(
                powerBToA01,
                rawBToA,
                ref powerBToAVelocity,
                powerRiseSeconds,
                powerFallSeconds,
                deltaTime);
            doorOpenness01 = Smooth(
                doorOpenness01,
                rawDoor,
                ref doorVelocity,
                doorOpenSeconds,
                doorCloseSeconds,
                deltaTime);

            currentWeights = BuildWeights();
            ApplyLightWeights();
            for (int i = 0; i < reflectionBlends.Length; i++)
            {
                if (reflectionBlends[i] != null &&
                    reflectionBlends[i].TryApplyWeights(this, currentWeights, out failure))
                {
                    continue;
                }

                LatchFault(
                    $"Reflection blend index {i} update failed: {failure}",
                    true);
                return;
            }

            NotifyWeights(false);
            if (Time.unscaledTime >= nextParityValidationTime)
            {
                nextParityValidationTime = Time.unscaledTime + parityValidationInterval;
                if (!TryValidateParity(out failure))
                    LatchFault(failure, true);
            }
        }

        private bool TryValidateConfiguration(out string failure)
        {
            if (aToB == null || bToA == null || doorOpenness == null ||
                !doorOpenness.IsConfigured)
            {
                failure = "Both directed bindings and a configured door openness source are required.";
                return false;
            }

            if (!TryValidateBinding(aToB, "A->B", out failure) ||
                !TryValidateBinding(bToA, "B->A", out failure))
            {
                return false;
            }

            if (!TryValidateAdditionalLightBudget(out failure))
                return false;

            var sourceLights = new HashSet<Light>();
            if (!AddUnique(aToB.SelectedProductionLights, sourceLights) ||
                !AddUnique(bToA.SelectedProductionLights, sourceLights))
            {
                failure = "Selected production Light references must be non-null and unique.";
                return false;
            }

            if (!TryValidateUniqueRenderers(aToB.ReceiverRenderers, "A->B receiver", out failure) ||
                !TryValidateUniqueRenderers(bToA.ReceiverRenderers, "B->A receiver", out failure) ||
                !TryValidateUniqueRenderers(shadowCasters, "shadow caster", out failure) ||
                !TryValidateUniqueRenderers(doorLeafRenderers, "door leaf", out failure))
            {
                return false;
            }

            var aReceivers = new HashSet<Renderer>(aToB.ReceiverRenderers);
            for (int i = 0; i < bToA.ReceiverRenderers.Length; i++)
            {
                if (aReceivers.Contains(bToA.ReceiverRenderers[i]))
                {
                    failure = "A renderer cannot belong to both directed receiver sets.";
                    return false;
                }
            }

            var receiverUnion = new HashSet<Renderer>(aToB.ReceiverRenderers);
            receiverUnion.UnionWith(bToA.ReceiverRenderers);
            int activeDoorCasterCount = 0;
            for (int i = 0; i < doorLeafRenderers.Length; i++)
            {
                Renderer doorRenderer = doorLeafRenderers[i];
                if (receiverUnion.Contains(doorRenderer))
                {
                    failure = "Door renderers must be bound only through doorLeafRenderers, " +
                              "not a one-direction receiver set.";
                    return false;
                }

                if (!doorRenderer.enabled || !doorRenderer.gameObject.activeInHierarchy)
                    continue;
                activeDoorCasterCount++;
                if (doorRenderer.shadowCastingMode == ShadowCastingMode.Off ||
                    !doorRenderer.receiveShadows)
                {
                    failure = $"Active door renderer '{doorRenderer.name}' must both cast " +
                              "and receive transported light.";
                    return false;
                }
            }

            if (activeDoorCasterCount == 0)
            {
                failure = "At least one active door renderer must cast and receive light.";
                return false;
            }

            var blendKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < reflectionBlends.Length; i++)
            {
                KExactReflectionProbeBlend blend = reflectionBlends[i];
                if (blend == null || string.IsNullOrWhiteSpace(blend.BlendKey) ||
                    !blendKeys.Add(blend.BlendKey))
                {
                    failure = "Reflection blend entries and keys must be non-null and unique.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private static bool TryValidateBinding(
            KExactDirectedTransportBinding binding,
            string label,
            out string failure)
        {
            if (string.IsNullOrWhiteSpace(binding.DirectedKey) ||
                string.IsNullOrWhiteSpace(binding.LayerGroupKey) ||
                binding.SourcePower == null || !binding.SourcePower.IsConfigured ||
                binding.SelectedProductionLights.Length == 0 ||
                binding.ReceiverRenderers.Length == 0)
            {
                failure = $"{label} key, group, power, source Lights, or receivers are incomplete.";
                return false;
            }

            if (!IsFiniteNonNegative(binding.DirectIntensityScaleAtPower0) ||
                !IsFiniteNonNegative(binding.DirectIntensityScaleAtPower100) ||
                !IsFiniteNonNegative(binding.ResidualReflectionAtPower0) ||
                !IsFiniteNonNegative(binding.ReflectionAtPower100))
            {
                failure = $"{label} direct/reflection coefficients are invalid.";
                return false;
            }

            Light[] sources = binding.SelectedProductionLights;
            for (int i = 0; i < sources.Length; i++)
            {
                Light source = sources[i];
                if (source == null ||
                    (source.type != LightType.Point && source.type != LightType.Spot) ||
                    source.cullingMask == 0 || source.commandBufferCount != 0 ||
                    source.GetComponent<UniversalAdditionalLightData>() == null)
                {
                    failure = $"{label} source Light index {i} is missing or cannot be " +
                              "cloned as an isolated URP Point/Spot Light.";
                    return false;
                }
            }

            KExactBounceProxyDescriptor[] receiverBounces = binding.ReceiverBounceProxies;
            KExactBounceProxyDescriptor[] doorSurfaces = binding.DoorSurfaceProxies;
            if (receiverBounces.Length == 0 || binding.ReceiverBounceAnchor == null)
            {
                failure = $"{label} requires receiver-facing bounce proxies and their " +
                          "receiver-side doorway anchor.";
                return false;
            }

            if (doorSurfaces.Length == 0 || binding.DoorSurfaceAnchor == null)
            {
                failure = $"{label} requires source-side door-surface proxies and their " +
                          "source-side doorway anchor.";
                return false;
            }

            if (binding.ReceiverBounceAnchor == binding.DoorSurfaceAnchor)
            {
                failure = $"{label} receiver-bounce and source-side door-surface anchors " +
                          "must be distinct.";
                return false;
            }

            var proxyKeys = new HashSet<string>(StringComparer.Ordinal);
            if (!TryValidateProxySet(
                    receiverBounces,
                    KExactProxyRole.ReceiverBounce,
                    proxyKeys,
                    label,
                    "receiver bounce",
                    out failure) ||
                !TryValidateProxySet(
                    doorSurfaces,
                    KExactProxyRole.DoorSurface,
                    proxyKeys,
                    label,
                    "door surface",
                    out failure))
            {
                return false;
            }

            failure = null;
            return true;
        }

        private static bool TryValidateProxySet(
            KExactBounceProxyDescriptor[] descriptors,
            KExactProxyRole expectedRole,
            HashSet<string> uniqueKeys,
            string bindingLabel,
            string proxyLabel,
            out string failure)
        {
            for (int i = 0; i < descriptors.Length; i++)
            {
                KExactBounceProxyDescriptor descriptor = descriptors[i];
                if (descriptor == null)
                {
                    failure = $"{bindingLabel} {proxyLabel} proxy index {i} is missing.";
                    return false;
                }

                if (!descriptor.TryValidate(out string descriptorFailure))
                {
                    failure = $"{bindingLabel} {proxyLabel} proxy index {i} is invalid: " +
                              descriptorFailure;
                    return false;
                }

                if (descriptor.Role != expectedRole)
                {
                    failure = $"{bindingLabel} {proxyLabel} proxy '{descriptor.Key}' has role " +
                              $"'{descriptor.Role}', expected '{expectedRole}'.";
                    return false;
                }

                if (!uniqueKeys.Add(descriptor.Key))
                {
                    failure = $"{bindingLabel} proxy key '{descriptor.Key}' is duplicated across " +
                              "receiver-bounce and door-surface lists.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private bool TryValidateAdditionalLightBudget(out string failure)
        {
            const int maxAdditionalLightsPerRenderer = 4;
            int aToBRoomLightCount = aToB.SelectedProductionLights.Length +
                                     aToB.ReceiverBounceProxies.Length;
            int bToARoomLightCount = bToA.SelectedProductionLights.Length +
                                     bToA.ReceiverBounceProxies.Length;
            int doorLightCount = aToB.DoorSurfaceProxies.Length +
                                 bToA.DoorSurfaceProxies.Length;
            if (aToBRoomLightCount > maxAdditionalLightsPerRenderer ||
                bToARoomLightCount > maxAdditionalLightsPerRenderer ||
                doorLightCount > maxAdditionalLightsPerRenderer)
            {
                failure = "The KExact routing would exceed URP's four additional-lights-per-object " +
                          "budget (A->B room=" + aToBRoomLightCount +
                          ", B->A room=" + bToARoomLightCount +
                          ", shared door=" + doorLightCount + ").";
                return false;
            }

            failure = null;
            return true;
        }

        private bool TryReadInputs(
            out float rawAToB,
            out float rawBToA,
            out float rawDoor,
            out string failure)
        {
            if (!aToB.SourcePower.TryRead01(out rawAToB, out failure))
            {
                failure = "A->B source power failed: " + failure;
                rawBToA = 0f;
                rawDoor = 0f;
                return false;
            }

            if (!bToA.SourcePower.TryRead01(out rawBToA, out failure))
            {
                failure = "B->A source power failed: " + failure;
                rawDoor = 0f;
                return false;
            }

            if (!doorOpenness.TryRead01(out rawDoor, out failure))
            {
                failure = "Door openness failed: " + failure;
                return false;
            }

            failure = null;
            return true;
        }

        private bool TryBuildRuntimeLights(
            KExactDirectedTransportBinding binding,
            List<KExactRuntimeLight> direct,
            List<KExactRuntimeLight> receiverBounce,
            List<KExactRuntimeLight> doorSurface,
            out string failure)
        {
            Light[] sources = binding.SelectedProductionLights;
            for (int i = 0; i < sources.Length; i++)
            {
                if (!KExactRuntimeLight.TryCreateDirect(
                        this,
                        binding.DirectedKey + "/Direct/" + i + "/" + sources[i].name,
                        sources[i],
                        binding.ReceiverRenderingLayerBit,
                        casterRenderingLayerBit,
                        out KExactRuntimeLight runtimeLight,
                        out failure))
                {
                    return false;
                }

                direct.Add(runtimeLight);
            }

            KExactBounceProxyDescriptor[] receiverDescriptors =
                binding.ReceiverBounceProxies;
            for (int i = 0; i < receiverDescriptors.Length; i++)
            {
                if (!KExactRuntimeLight.TryCreateProxy(
                        this,
                        binding.DirectedKey + "/ReceiverBounce/" +
                        receiverDescriptors[i].Key,
                        binding.ReceiverBounceAnchor,
                        receiverDescriptors[i],
                        binding.ReceiverRenderingLayerBit,
                        casterRenderingLayerBit,
                        out KExactRuntimeLight runtimeLight,
                        out failure))
                {
                    return false;
                }

                receiverBounce.Add(runtimeLight);
            }

            KExactBounceProxyDescriptor[] doorDescriptors = binding.DoorSurfaceProxies;
            for (int i = 0; i < doorDescriptors.Length; i++)
            {
                if (!KExactRuntimeLight.TryCreateProxy(
                        this,
                        binding.DirectedKey + "/DoorSurface/" + doorDescriptors[i].Key,
                        binding.DoorSurfaceAnchor,
                        doorDescriptors[i],
                        doorReceiverRenderingLayerBit,
                        casterRenderingLayerBit,
                        out KExactRuntimeLight runtimeLight,
                        out failure))
                {
                    return false;
                }

                doorSurface.Add(runtimeLight);
            }

            failure = null;
            return true;
        }

        private Dictionary<Renderer, uint> BuildRendererLeasePlan()
        {
            var plan = new Dictionary<Renderer, uint>();
            AddRendererBits(
                plan,
                aToB.ReceiverRenderers,
                aToB.ReceiverRenderingLayerBit | casterRenderingLayerBit);
            AddRendererBits(
                plan,
                bToA.ReceiverRenderers,
                bToA.ReceiverRenderingLayerBit | casterRenderingLayerBit);
            AddRendererBits(plan, shadowCasters, casterRenderingLayerBit);
            AddRendererBits(
                plan,
                doorLeafRenderers,
                casterRenderingLayerBit |
                doorReceiverRenderingLayerBit);
            return plan;
        }

        private KExactTransportWeights BuildWeights()
        {
            // The cloned source Light exists on the receiver layer, so physical shadowing
            // alone cannot guarantee a zero-transfer closed aperture. Gate direct transport
            // by the same smoothed aperture scalar used by bounce/reflection; the moving door
            // still casts realtime shadows and shapes the spatial result. Door-surface proxies
            // remain source-power-only so the leaf itself is lit while closed.
            float directA = Mathf.Lerp(
                aToB.DirectIntensityScaleAtPower0,
                aToB.DirectIntensityScaleAtPower100,
                powerAToB01) * doorOpenness01;
            float directB = Mathf.Lerp(
                bToA.DirectIntensityScaleAtPower0,
                bToA.DirectIntensityScaleAtPower100,
                powerBToA01) * doorOpenness01;
            float reflectionA = Mathf.Lerp(
                aToB.ResidualReflectionAtPower0,
                aToB.ReflectionAtPower100,
                powerAToB01) * doorOpenness01;
            float reflectionB = Mathf.Lerp(
                bToA.ResidualReflectionAtPower0,
                bToA.ReflectionAtPower100,
                powerBToA01) * doorOpenness01;
            return new KExactTransportWeights(
                powerAToB01,
                powerBToA01,
                doorOpenness01,
                directA,
                directB,
                reflectionA,
                reflectionB);
        }

        private void ApplyLightWeights()
        {
            for (int i = 0; i < directAToB.Count; i++)
                directAToB[i].ApplyDirectScale(currentWeights.DirectScaleAToB);
            for (int i = 0; i < directBToA.Count; i++)
                directBToA[i].ApplyDirectScale(currentWeights.DirectScaleBToA);
            for (int i = 0; i < receiverBounceAToB.Count; i++)
                receiverBounceAToB[i].ApplyProxy(powerAToB01, doorOpenness01);
            for (int i = 0; i < receiverBounceBToA.Count; i++)
                receiverBounceBToA[i].ApplyProxy(powerBToA01, doorOpenness01);
            for (int i = 0; i < doorSurfaceAToB.Count; i++)
                doorSurfaceAToB[i].ApplyProxy(powerAToB01, doorOpenness01);
            for (int i = 0; i < doorSurfaceBToA.Count; i++)
                doorSurfaceBToA[i].ApplyProxy(powerBToA01, doorOpenness01);
        }

        private static float SumRuntimeLightIntensities(
            List<KExactRuntimeLight> runtimeLights)
        {
            float total = 0f;
            for (int i = 0; i < runtimeLights.Count; i++)
            {
                KExactRuntimeLight runtimeLight = runtimeLights[i];
                if (runtimeLight != null && runtimeLight.Light != null)
                    total += runtimeLight.Light.intensity;
            }
            return total;
        }

        private void TearDownRuntime(bool releaseLeases, out string restoreFailure)
        {
            transportActive = false;
            for (int i = reflectionBlends.Length - 1; i >= 0; i--)
            {
                if (reflectionBlends[i] != null)
                    reflectionBlends[i].Deactivate(this);
            }

            DestroyLights(doorSurfaceBToA);
            DestroyLights(doorSurfaceAToB);
            DestroyLights(receiverBounceBToA);
            DestroyLights(receiverBounceAToB);
            DestroyLights(directBToA);
            DestroyLights(directAToB);

            restoreFailure = null;
            if (releaseLeases)
                KExactRendererLayerLeaseTable.ReleaseOwner(this, out restoreFailure);
            else
                KExactRendererLayerLeaseTable.ReleaseOwner(this, out _);
            KExactRenderingLayerRegistry.Release(this);

            powerAToBVelocity = 0f;
            powerBToAVelocity = 0f;
            doorVelocity = 0f;
            currentWeights = default;
            NotifyWeights(true);
        }

        private void LatchFault(string reason, bool tearDown)
        {
            string resolvedReason = string.IsNullOrWhiteSpace(reason)
                ? "Unknown K-exact fail-closed condition."
                : reason;
            string restoreFailure;
            if (tearDown)
                TearDownRuntime(true, out restoreFailure);
            else
                restoreFailure = null;
            if (!string.IsNullOrEmpty(restoreFailure))
                resolvedReason += " Restore: " + restoreFailure;

            faultLatched = true;
            faultReason = resolvedReason;
            Debug.LogError(
                $"[{nameof(KExactPortalConnection)}] '{connectionKey}' disabled fail-closed: " +
                resolvedReason,
                this);
        }

        private void NotifyWeights(bool force)
        {
            if (!force && hasNotifiedWeights && WeightsApproximatelyEqual(
                    currentWeights,
                    lastNotifiedWeights))
            {
                return;
            }

            lastNotifiedWeights = currentWeights;
            hasNotifiedWeights = true;
            Delegate[] subscribers = WeightsChanged?.GetInvocationList();
            if (subscribers == null)
                return;
            for (int i = 0; i < subscribers.Length; i++)
            {
                try
                {
                    ((Action<KExactTransportWeights>)subscribers[i]).Invoke(currentWeights);
                }
                catch (Exception exception)
                {
                    // Observers must not interrupt activation, rollback, or teardown.
                    Debug.LogException(exception, this);
                }
            }
        }

        private static float Smooth(
            float current,
            float target,
            ref float velocity,
            float riseSeconds,
            float fallSeconds,
            float deltaTime)
        {
            target = Mathf.Clamp01(target);
            float smoothTime = target >= current ? riseSeconds : fallSeconds;
            if (smoothTime <= 0f || deltaTime <= 0f)
            {
                velocity = 0f;
                return target;
            }

            float value = Mathf.SmoothDamp(
                current,
                target,
                ref velocity,
                Mathf.Max(0.0001f, smoothTime),
                Mathf.Infinity,
                deltaTime);
            if (Mathf.Abs(value - target) < 0.0001f)
            {
                velocity = 0f;
                return target;
            }

            return Mathf.Clamp01(value);
        }

        private static bool ValidateLights(
            List<KExactRuntimeLight> lights,
            out string failure)
        {
            for (int i = 0; i < lights.Count; i++)
            {
                if (lights[i] == null)
                {
                    failure = $"Runtime light index {i} is missing.";
                    return false;
                }

                if (lights[i].TryValidate(out failure))
                    continue;
                failure = $"Runtime light index {i} parity failed: {failure}";
                return false;
            }

            failure = null;
            return true;
        }

        private static void DestroyLights(List<KExactRuntimeLight> lights)
        {
            for (int i = lights.Count - 1; i >= 0; i--)
                lights[i]?.Destroy();
            lights.Clear();
        }

        private static void AddRendererBits(
            Dictionary<Renderer, uint> plan,
            Renderer[] renderers,
            uint bits)
        {
            if (renderers == null)
                return;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;
                plan.TryGetValue(renderer, out uint existing);
                plan[renderer] = existing | bits;
            }
        }

        private static bool TryValidateUniqueRenderers(
            Renderer[] renderers,
            string label,
            out string failure)
        {
            if (renderers == null || renderers.Length == 0)
            {
                failure = $"The {label} renderer array is empty.";
                return false;
            }

            var seen = new HashSet<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !seen.Add(renderer))
                {
                    failure = $"The {label} renderer array has a missing/duplicate entry at {i}.";
                    return false;
                }

                Material[] materials = renderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    if (materials[materialIndex] == null || materials[materialIndex].shader == null)
                    {
                        failure = $"Renderer '{renderer.name}' has a missing material/shader at " +
                                  $"slot {materialIndex}.";
                        return false;
                    }
                }
            }

            failure = null;
            return true;
        }

        private static bool AddUnique<T>(T[] values, HashSet<T> target)
            where T : UnityEngine.Object
        {
            if (values == null || values.Length == 0)
                return false;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == null || !target.Add(values[i]))
                    return false;
            }
            return true;
        }

        private static T[] CopyArray<T>(T[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<T>();
            var copy = new T[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }

        private static bool WeightsApproximatelyEqual(
            KExactTransportWeights left,
            KExactTransportWeights right)
        {
            const float epsilon = 0.0001f;
            return Mathf.Abs(left.PowerAToB01 - right.PowerAToB01) < epsilon &&
                   Mathf.Abs(left.PowerBToA01 - right.PowerBToA01) < epsilon &&
                   Mathf.Abs(left.DoorOpenness01 - right.DoorOpenness01) < epsilon &&
                   Mathf.Abs(left.DirectScaleAToB - right.DirectScaleAToB) < epsilon &&
                   Mathf.Abs(left.DirectScaleBToA - right.DirectScaleBToA) < epsilon &&
                   Mathf.Abs(left.ReflectionWeightAToB - right.ReflectionWeightAToB) < epsilon &&
                   Mathf.Abs(left.ReflectionWeightBToA - right.ReflectionWeightBToA) < epsilon;
        }

        private void OnValidate()
        {
            powerRiseSeconds = Mathf.Max(0f, powerRiseSeconds);
            powerFallSeconds = Mathf.Max(0f, powerFallSeconds);
            doorOpenSeconds = Mathf.Max(0f, doorOpenSeconds);
            doorCloseSeconds = Mathf.Max(0f, doorCloseSeconds);
            parityValidationInterval = Mathf.Max(0.05f, parityValidationInterval);
        }
    }
}
