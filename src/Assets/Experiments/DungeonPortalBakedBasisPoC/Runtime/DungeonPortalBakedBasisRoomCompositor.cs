using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DungeonPortalBakedBasisPoC
{
    /// <summary>
    /// Creates one private color/direction lightmap slot per canonical room atlas and remaps
    /// only the explicitly assigned room renderers to those slots. Meshes, shared materials,
    /// shaders, material keywords, render queues, and MaterialPropertyBlocks are never read
    /// for mutation and never written by this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DungeonPortalBakedBasisRoomCompositor : MonoBehaviour
    {
        [Serializable]
        public sealed class ExplicitRendererBinding
        {
            [Tooltip("Exact roomRoot-relative path#occurrence key from canonical renderer traversal.")]
            [SerializeField] private string canonicalRendererKey;
            [SerializeField] private Renderer renderer;

            public string CanonicalRendererKey => canonicalRendererKey;
            public Renderer Renderer => renderer;

            public void ConfigureAuthoring(string rendererKey, Renderer assignedRenderer)
            {
                canonicalRendererKey = rendererKey ?? string.Empty;
                renderer = assignedRenderer;
            }
        }

        [Serializable]
        public sealed class IncomingDoorState
        {
            [SerializeField] private string connectionId;
            [SerializeField] private string receiverDoorId;
            [SerializeField] private DungeonPortalBakedRoomBasisData sourceRoomBasis;
            [SerializeField] private string sourceDoorId;
            [SerializeField] private bool contributes = true;
            [SerializeField, Range(0f, 1f)] private float sourcePower01 = 1f;
            [SerializeField, Range(0f, 1f)] private float aperture01 = 1f;
            [SerializeField, Min(0f)] private float responseScale = 1f;

            public string ConnectionId => connectionId;
            public string ReceiverDoorId => receiverDoorId;
            public DungeonPortalBakedRoomBasisData SourceRoomBasis => sourceRoomBasis;
            public string SourceDoorId => sourceDoorId;
            public bool Contributes => contributes;
            public float SourcePower01 => sourcePower01;
            public float Aperture01 => aperture01;
            public float ResponseScale => responseScale;

            public void ConfigureAuthoring(
                string id,
                string receivingDoorId,
                DungeonPortalBakedRoomBasisData sourceBasis,
                string emittingDoorId,
                float power01,
                float openness01,
                float scale = 1f,
                bool enabled = true)
            {
                connectionId = id ?? string.Empty;
                receiverDoorId = receivingDoorId ?? string.Empty;
                sourceRoomBasis = sourceBasis;
                sourceDoorId = emittingDoorId ?? string.Empty;
                sourcePower01 = Mathf.Clamp01(power01);
                aperture01 = Mathf.Clamp01(openness01);
                responseScale = IsFinite(scale) ? Mathf.Max(0f, scale) : 0f;
                contributes = enabled;
            }

            internal void SetRuntimeState(float power01, float openness01, bool enabled)
            {
                sourcePower01 = Mathf.Clamp01(power01);
                aperture01 = Mathf.Clamp01(openness01);
                contributes = enabled;
            }

            internal void SetResponseScale(float scale)
            {
                responseScale = scale;
            }

            internal void ClampSerializedValues()
            {
                sourcePower01 = Mathf.Clamp01(sourcePower01);
                aperture01 = Mathf.Clamp01(aperture01);
                if (!IsFinite(responseScale) || responseScale < 0f)
                    responseScale = 0f;
            }

            private static bool IsFinite(float value)
            {
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }
        }

        private sealed class RuntimeBucket
        {
            public DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket ComposeBase;
            public Texture2D Color;
            public Texture2D Direction;
            public LightmapData Slot;
        }

        [Header("Canonical room contract")]
        [SerializeField] private DungeonPortalBakedRoomBasisData roomBasis;
        [SerializeField] private Transform roomRoot;
        [Tooltip("Must be Hidden/DungeonPortalBakedBasisPoC/Compose and explicitly assigned " +
                 "so player shader stripping cannot turn a missing shader into a silent fallback.")]
        [SerializeField] private Shader compositionShader;
        [SerializeField] private ExplicitRendererBinding[] explicitRenderers =
            Array.Empty<ExplicitRendererBinding>();

        [Header("Runtime lighting state")]
        [SerializeField, Range(0f, 1f)] private float basePower01;
        [SerializeField] private IncomingDoorState[] incomingDoors = Array.Empty<IncomingDoorState>();
        [SerializeField] private bool activateOnStart = true;
        [SerializeField, Min(0.05f)] private float registryValidationInterval = 0.5f;

        private readonly List<DungeonPortalBakedBasisGpuComposer.Contribution> contributions =
            new List<DungeonPortalBakedBasisGpuComposer.Contribution>();
        private readonly List<DungeonPortalBakedRoomBasisData.WeightedReceiverResponseLobe>
            weightedResponseLobes =
                new List<DungeonPortalBakedRoomBasisData.WeightedReceiverResponseLobe>();
        private RuntimeBucket[] runtimeBuckets = Array.Empty<RuntimeBucket>();
        private DungeonPortalBakedBasisGpuComposer gpuComposer;
        private DungeonPortalBakedBasisLightmapRegistry.Registration registration;
        private bool active;
        private bool faultLatched;
        private bool compositionDirty = true;
        private string faultReason;
        private int lastStateHash;
        private int capturedOriginalEndpoint = -1;
        private DungeonPortalBakedRoomBasisData.ProductionEndpoint activeEndpoint;
        private bool activeEndpointResolved;
        private float nextRegistryValidationTime;

        public DungeonPortalBakedRoomBasisData RoomBasis => roomBasis;
        public Transform RoomRoot => roomRoot;
        public ExplicitRendererBinding[] ExplicitRenderers =>
            explicitRenderers ?? Array.Empty<ExplicitRendererBinding>();
        public IncomingDoorState[] IncomingDoors => incomingDoors ?? Array.Empty<IncomingDoorState>();
        public float BasePower01 => basePower01;
        public bool IsActive => active;
        public bool IsFaultLatched => faultLatched;
        public string FaultReason => faultReason;

        /// <summary>
        /// Converts the projected aperture used by the connection driver back to the physical
        /// normalized door angle used by D25/D50/D75/D100 baked response poses. The caller must
        /// supply a finite value; runtime state setters and contract validation enforce that.
        /// </summary>
        public static float ApertureToPhysicalOpenFraction(float aperture01)
        {
            float aperture = Mathf.Clamp01(aperture01);
            return (2f / Mathf.PI) * Mathf.Acos(1f - aperture);
        }

        /// <summary>
        /// Builds the exact explicit binding set for an arbitrary production room instance.
        /// Runtime-added renderers that are not declared by the canonical basis are returned to
        /// the caller and are deliberately left untouched.  No renderer, material, shader,
        /// keyword, property block, or lightmap state is changed by this query.
        /// </summary>
        public static bool TryCreateExplicitBindings(
            DungeonPortalBakedRoomBasisData basis,
            Transform productionRoomRoot,
            out ExplicitRendererBinding[] bindings,
            out Renderer[] unboundRenderers,
            out string failure)
        {
            bindings = Array.Empty<ExplicitRendererBinding>();
            unboundRenderers = Array.Empty<Renderer>();
            if (basis == null)
            {
                failure = "Cannot bind a missing room basis.";
                return false;
            }
            if (!basis.TryValidateDefinition(out failure))
            {
                failure = "Cannot bind an invalid room basis: " + failure;
                return false;
            }
            if (!TryBuildCanonicalRendererKeyMap(
                    productionRoomRoot,
                    out Dictionary<Renderer, string> computedKeys,
                    out failure))
            {
                return false;
            }

            var rendererByKey = new Dictionary<string, Renderer>(StringComparer.Ordinal);
            foreach (KeyValuePair<Renderer, string> pair in computedKeys)
            {
                if (!rendererByKey.TryAdd(pair.Value, pair.Key))
                {
                    failure = "Production room traversal produced duplicate canonical key '" +
                              pair.Value + "'.";
                    return false;
                }
            }

            DungeonPortalBakedRoomBasisData.CanonicalRendererEntry[] entries =
                basis.CanonicalRenderers;
            var result = new ExplicitRendererBinding[entries.Length];
            var boundRenderers = new HashSet<Renderer>();
            var declaredKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                DungeonPortalBakedRoomBasisData.CanonicalRendererEntry entry = entries[i];
                string key = entry != null ? entry.CanonicalRendererKey : string.Empty;
                if (string.IsNullOrWhiteSpace(key) || !declaredKeys.Add(key) ||
                    !rendererByKey.TryGetValue(key, out Renderer renderer) || renderer == null ||
                    !boundRenderers.Add(renderer))
                {
                    failure = "Canonical production renderer binding cannot resolve unique key '" +
                              (string.IsNullOrWhiteSpace(key) ? "<null>" : key) + "'.";
                    return false;
                }

                var binding = new ExplicitRendererBinding();
                binding.ConfigureAuthoring(key, renderer);
                result[i] = binding;
            }

            var extras = new List<Renderer>();
            foreach (KeyValuePair<Renderer, string> pair in computedKeys)
            {
                if (!boundRenderers.Contains(pair.Key))
                    extras.Add(pair.Key);
            }

            bindings = result;
            unboundRenderers = extras.ToArray();
            failure = null;
            return true;
        }

        public void ConfigureAuthoring(
            DungeonPortalBakedRoomBasisData basis,
            Transform canonicalRoomRoot,
            Shader gpuCompositionShader,
            ExplicitRendererBinding[] rendererBindings,
            IncomingDoorState[] incomingDoorStates,
            float initialBasePower01,
            bool autoActivate = true,
            float validationInterval = 0.5f)
        {
            roomBasis = basis;
            roomRoot = canonicalRoomRoot;
            compositionShader = gpuCompositionShader;
            explicitRenderers = CloneArray(rendererBindings);
            incomingDoors = CloneArray(incomingDoorStates);
            basePower01 = Mathf.Clamp01(initialBasePower01);
            activateOnStart = autoActivate;
            registryValidationInterval = Mathf.Max(0.05f, validationInterval);
            compositionDirty = true;
        }

        private void Start()
        {
            // D0 is a hard bypass: keep the original production LightmapData references and
            // renderer indices/ST untouched until an aperture actually contributes.
            if (!HasOpenIncomingAperture())
            {
                if (!CanRestoreOriginalEndpoint(out string endpointFailure))
                    LatchFault(endpointFailure, false);
                return;
            }

            if (activateOnStart && HasOpenIncomingAperture() && !active && !faultLatched &&
                !TryActivate(out string failure))
            {
                LatchFault(failure, false);
            }
        }

        private void LateUpdate()
        {
            if (!active)
            {
                if (activateOnStart && !faultLatched && HasOpenIncomingAperture() &&
                    !TryActivate(out string activationFailure))
                {
                    LatchFault(activationFailure, false);
                }
                return;
            }

            if (!HasOpenIncomingAperture())
            {
                if (!CanRestoreOriginalEndpoint(out string bypassFailure))
                {
                    LatchFault(bypassFailure, true);
                    return;
                }

                if (!TearDown(false, out string restoreFailure))
                    LatchFault(restoreFailure, false);
                return;
            }

            int stateHash = ComputeStateHash();
            if (compositionDirty || stateHash != lastStateHash)
            {
                if (!TryRecompose(out string failure))
                {
                    LatchFault(failure, true);
                    return;
                }

                lastStateHash = stateHash;
                compositionDirty = false;
            }

            if (Time.unscaledTime < nextRegistryValidationTime)
                return;
            nextRegistryValidationTime = Time.unscaledTime + registryValidationInterval;
            if (!DungeonPortalBakedBasisLightmapRegistry.TryValidate(registration, out string parityFailure))
                LatchFault(parityFailure, true);
        }

        private void OnDisable()
        {
            TearDown(true);
        }

        private void OnDestroy()
        {
            TearDown(true);
        }

        public bool TryActivate(out string failure)
        {
            if (active)
            {
                failure = null;
                return true;
            }

            if (faultLatched)
            {
                failure = "A previous baked-basis fault is latched: " + faultReason;
                return false;
            }

            if (!HasOpenIncomingAperture())
            {
                failure = "D0 bypass is active. A private RGBAHalf working lightmap may only " +
                          "be installed while at least one configured aperture is open.";
                return false;
            }

            if (!TryValidateAndResolve(
                    out List<DungeonPortalBakedBasisLightmapRegistry.AssignmentRequest> assignments,
                    out DungeonPortalBakedRoomBasisData.NativeEndpointAtlas[] nativeAtlases,
                    out DungeonPortalBakedRoomBasisData.ProductionEndpoint originalEndpoint,
                    out failure))
            {
                return false;
            }

            if (!DungeonPortalBakedBasisGpuComposer.TryCreate(
                    compositionShader,
                    out gpuComposer,
                    out failure))
            {
                return false;
            }

            capturedOriginalEndpoint = (int)originalEndpoint;
            activeEndpoint = originalEndpoint;
            activeEndpointResolved = true;
            runtimeBuckets = new RuntimeBucket[nativeAtlases.Length];
            try
            {
                for (int i = 0; i < nativeAtlases.Length; i++)
                {
                    DungeonPortalBakedRoomBasisData.NativeEndpointAtlas nativeAtlas =
                        nativeAtlases[i];
                    if (!roomBasis.TryGetBaseTransitionAtlas(
                            originalEndpoint,
                            i,
                            out DungeonPortalBakedRoomBasisData.BaseTransitionAtlas transition,
                            out string transitionFailure))
                    {
                        throw new InvalidOperationException(transitionFailure);
                    }
                    if (transition.LayoutEndpoint != originalEndpoint ||
                        transition.EndpointBucketIndex != i ||
                        transition.NativeLocalLightmapIndex != nativeAtlas.NativeLocalLightmapIndex ||
                        transition.UnchangedShadowMask != nativeAtlas.ShadowMask)
                    {
                        throw new InvalidOperationException(
                            $"Captured endpoint transition bucket {i} does not preserve its exact " +
                            "native lightmap/shadow-mask identity.");
                    }
                    var composeBase = new DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket();
                    // Both bases are expressed in the exact captured endpoint layout.  The
                    // captured endpoint texture is never repacked; only the opposite endpoint is.
                    // Keeping this layout for the whole open interval prevents renderer index/ST
                    // churn while allowing basePower01 to perform a real continuous lerp.
                    composeBase.ConfigureAuthoring(
                        transition.BucketId,
                        transition.NativeLocalLightmapIndex,
                        transition.Power0Color,
                        transition.Power100Color,
                        transition.Power0Direction,
                        transition.Power100Direction,
                        transition.UnchangedShadowMask);
                    var runtimeBucket = new RuntimeBucket
                    {
                        ComposeBase = composeBase,
                        Color = DungeonPortalBakedBasisGpuComposer.CreateRuntimeLightmapTexture(
                            nativeAtlas.Color,
                            $"__DPBB_{roomBasis.RoomId}_{GetEntityId()}_" +
                            $"{nativeAtlas.BucketId}_Color"),
                        Direction = DungeonPortalBakedBasisGpuComposer.CreateRuntimeLightmapTexture(
                            nativeAtlas.Direction,
                            $"__DPBB_{roomBasis.RoomId}_{GetEntityId()}_" +
                            $"{nativeAtlas.BucketId}_Direction")
                    };
                    runtimeBucket.Slot = new LightmapData
                    {
                        lightmapColor = runtimeBucket.Color,
                        lightmapDir = runtimeBucket.Direction,
                        shadowMask = nativeAtlas.ShadowMask
                    };
                    runtimeBuckets[i] = runtimeBucket;
                }

                if (!TryRecompose(out failure))
                    throw new InvalidOperationException(failure);

                var slots = new LightmapData[runtimeBuckets.Length];
                for (int i = 0; i < runtimeBuckets.Length; i++)
                    slots[i] = runtimeBuckets[i].Slot;

                if (!DungeonPortalBakedBasisLightmapRegistry.TryRegister(
                        this,
                        slots,
                        assignments,
                        out registration,
                        out failure))
                {
                    throw new InvalidOperationException(failure);
                }

                active = true;
                compositionDirty = false;
                lastStateHash = ComputeStateHash();
                nextRegistryValidationTime = Time.unscaledTime + registryValidationInterval;
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                failure = exception is InvalidOperationException &&
                          !string.IsNullOrEmpty(exception.Message)
                    ? exception.Message
                    : $"Baked-basis activation failed: {exception.GetType().Name}: {exception.Message}";
                TearDown(false);
                return false;
            }
        }

        public bool TryClearFaultAndActivate(out string failure)
        {
            if (active)
                TearDown(true);
            faultLatched = false;
            faultReason = null;
            compositionDirty = true;
            if (TryActivate(out failure))
                return true;
            LatchFault(failure, false);
            return false;
        }

        public bool TrySetBasePower(float value01, out string failure)
        {
            if (!IsFinite(value01))
            {
                failure = "Base power must be finite.";
                return false;
            }

            float clamped = Mathf.Clamp01(value01);
            if (!active && !HasOpenIncomingAperture() && GetPowerEndpoint(clamped) < 0)
            {
                failure = "An intermediate base-power blend requires an open aperture and a " +
                          "private working lightmap. D0 must remain at canonical P0 or P100.";
                return false;
            }

            basePower01 = clamped;
            compositionDirty = true;
            failure = null;
            return true;
        }

        public bool TrySetIncomingState(
            string connectionId,
            float sourcePower,
            float aperture,
            bool contributes,
            out string failure)
        {
            if (string.IsNullOrWhiteSpace(connectionId) || !IsFinite(sourcePower) ||
                !IsFinite(aperture))
            {
                failure = "Connection id is empty or its source/aperture scalar is non-finite.";
                return false;
            }

            IncomingDoorState[] values = IncomingDoors;
            for (int i = 0; i < values.Length; i++)
            {
                IncomingDoorState state = values[i];
                if (state != null &&
                    string.Equals(state.ConnectionId, connectionId, StringComparison.Ordinal))
                {
                    state.SetRuntimeState(sourcePower, aperture, contributes);
                    compositionDirty = true;
                    if (!active && HasOpenIncomingAperture())
                    {
                        if (TryActivate(out failure))
                            return true;
                        LatchFault(failure, false);
                        return false;
                    }

                    failure = null;
                    return true;
                }
            }

            failure = $"Incoming connection '{connectionId}' is not explicitly configured.";
            return false;
        }

        /// <summary>
        /// Changes only the authored response gain for an existing connection. A zero scale is
        /// intentionally allowed while aperture remains open: this keeps the private canonical
        /// working maps installed for a true base-only parity comparison.
        /// </summary>
        public bool TrySetIncomingResponseScale(
            string connectionId,
            float scale,
            out string failure)
        {
            if (string.IsNullOrWhiteSpace(connectionId) || !IsFinite(scale) || scale < 0f)
            {
                failure = "Connection id is empty or response scale is non-finite/negative.";
                return false;
            }

            IncomingDoorState[] values = IncomingDoors;
            for (int i = 0; i < values.Length; i++)
            {
                IncomingDoorState state = values[i];
                if (state == null ||
                    !string.Equals(state.ConnectionId, connectionId, StringComparison.Ordinal))
                {
                    continue;
                }

                state.SetResponseScale(scale);
                compositionDirty = true;
                failure = null;
                return true;
            }

            failure = $"Incoming connection '{connectionId}' is not explicitly configured.";
            return false;
        }

        /// <summary>
        /// Evaluates only the incoming doorway SH delta for one configured connection. The same
        /// physical-pose weights used by lightmap composition are applied here, including
        /// zero-to-D25 and bracket interpolation. Receiver ambient SH is deliberately excluded.
        /// </summary>
        public bool TryEvaluateIncomingDoorwayProbeDelta(
            string connectionId,
            float[] outputSh27,
            out string failure)
        {
            if (outputSh27 == null ||
                outputSh27.Length !=
                DungeonPortalBakedRoomBasisData.SphericalHarmonicsCoefficientCount)
            {
                failure = "Doorway-probe output must be an exact 27-float array.";
                return false;
            }
            Array.Clear(outputSh27, 0, outputSh27.Length);

            if (string.IsNullOrWhiteSpace(connectionId))
            {
                failure = "Incoming connection id is empty.";
                return false;
            }

            IncomingDoorState state = null;
            IncomingDoorState[] values = IncomingDoors;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != null &&
                    string.Equals(values[i].ConnectionId, connectionId, StringComparison.Ordinal))
                {
                    state = values[i];
                    break;
                }
            }
            if (state == null)
            {
                failure = $"Incoming connection '{connectionId}' is not explicitly configured.";
                return false;
            }
            if (!IsFinite(state.SourcePower01) || !IsFinite(state.Aperture01) ||
                !IsFinite(state.ResponseScale) || state.ResponseScale < 0f)
            {
                failure = $"Incoming connection '{connectionId}' has a non-finite/negative state.";
                return false;
            }
            if (!state.Contributes || state.Aperture01 <= 0f || state.ResponseScale <= 0f)
            {
                failure = null;
                return true;
            }

            if (roomBasis == null ||
                !roomBasis.TryGetReceiverDoor(
                    state.ReceiverDoorId,
                    out DungeonPortalBakedRoomBasisData.ReceiverDoorBasis receiverDoor) ||
                state.SourceRoomBasis == null ||
                !state.SourceRoomBasis.TryGetSourceDoor(
                    state.SourceDoorId,
                    out DungeonPortalBakedRoomBasisData.SourceDoorBasis sourceDoor))
            {
                failure = $"Connection '{connectionId}' no longer resolves its source or " +
                          "receiver door contract.";
                return false;
            }

            weightedResponseLobes.Clear();
            float physicalOpenFraction = ApertureToPhysicalOpenFraction(state.Aperture01);
            if (!receiverDoor.TryAppendWeightedResponseLobes(
                    physicalOpenFraction,
                    weightedResponseLobes,
                    out failure))
            {
                return false;
            }

            for (int weightedIndex = 0;
                 weightedIndex < weightedResponseLobes.Count;
                 weightedIndex++)
            {
                DungeonPortalBakedRoomBasisData.WeightedReceiverResponseLobe weighted =
                    weightedResponseLobes[weightedIndex];
                DungeonPortalBakedRoomBasisData.ReceiverResponseLobe lobe = weighted.Lobe;
                DungeonPortalBakedRoomBasisData.OptionalDoorwayProbeResponse response =
                    lobe != null ? lobe.DoorwayProbeResponse : null;
                if (lobe == null || response == null || !response.Authored ||
                    response.OffCoefficients.Length !=
                    DungeonPortalBakedRoomBasisData.SphericalHarmonicsCoefficientCount ||
                    response.OnCoefficients.Length !=
                    DungeonPortalBakedRoomBasisData.SphericalHarmonicsCoefficientCount ||
                    !sourceDoor.TryGetCoefficient(
                        lobe.BasisId,
                        out DungeonPortalBakedRoomBasisData.SourceBasisCoefficient coefficient))
                {
                    Array.Clear(outputSh27, 0, outputSh27.Length);
                    failure = $"Door SH lobe '{(lobe != null ? lobe.BasisId : "<null>")}' is " +
                              "missing authored 27-float OFF/ON response or source coefficient.";
                    return false;
                }

                Color rgb = coefficient.Evaluate(state.SourcePower01);
                float poseAndResponseWeight = weighted.PoseWeight * state.ResponseScale;
                for (int channel = 0; channel < 3; channel++)
                {
                    float coefficientScale = channel == 0 ? rgb.r : channel == 1 ? rgb.g : rgb.b;
                    for (int shCoefficient = 0; shCoefficient < 9; shCoefficient++)
                    {
                        int shIndex = channel * 9 + shCoefficient;
                        float delta = response.OnCoefficients[shIndex] -
                                      response.OffCoefficients[shIndex];
                        if (!IsFinite(delta))
                        {
                            Array.Clear(outputSh27, 0, outputSh27.Length);
                            failure = $"Door SH lobe '{lobe.BasisId}' contains a non-finite " +
                                      $"coefficient at {shIndex}.";
                            return false;
                        }
                        outputSh27[shIndex] +=
                            delta * coefficientScale * poseAndResponseWeight;
                    }
                }
            }

            failure = null;
            return true;
        }

        /// <summary>
        /// Removes all private working slots and restores the exact production texture
        /// references plus renderer index/ST snapshots. Public deactivation is intentionally
        /// allowed only at a zero-transfer canonical endpoint; OnDisable/OnDestroy still force
        /// teardown for safety from any state.
        /// </summary>
        public bool TryDeactivate(out string failure)
        {
            if (!active)
                return CanRestoreOriginalEndpoint(out failure);

            if (!CanRestoreOriginalEndpoint(out failure))
                return false;
            return TearDown(false, out failure);
        }

        public bool RestoreOriginal(out string failure)
        {
            return TryDeactivate(out failure);
        }

        /// <summary>
        /// Zero-transfer transition handoff. Unlike strict TryDeactivate, this restores the
        /// exact captured production endpoint even when the private-map base power is currently
        /// intermediate or targets the other endpoint. The caller can then switch the production
        /// room to its target endpoint and update BasePower01 in the same frame.
        /// </summary>
        public bool TryDeactivateToCapturedEndpoint(
            out int restoredEndpoint,
            out string failure)
        {
            restoredEndpoint = -1;
            if (!active || capturedOriginalEndpoint < 0)
            {
                failure = "There is no active captured production endpoint to restore.";
                return false;
            }

            if (HasOpenIncomingAperture())
            {
                failure = "Captured-endpoint restore requires every incoming transfer to have " +
                          "zero effective aperture (aperture=0 or contributes=false).";
                return false;
            }

            int captured = capturedOriginalEndpoint;
            if (captured == 2)
            {
                int requestedEndpoint = GetPowerEndpoint(basePower01);
                captured = requestedEndpoint >= 0 ? requestedEndpoint : 0;
            }

            if (!TearDown(false, out failure))
                return false;
            restoredEndpoint = captured;
            return true;
        }

        public bool TryRecomposeNow(out string failure)
        {
            if (!active)
            {
                failure = "The room compositor is not active.";
                return false;
            }

            if (!TryRecompose(out failure))
            {
                LatchFault(failure, true);
                return false;
            }

            compositionDirty = false;
            lastStateHash = ComputeStateHash();
            return true;
        }

        private bool TryRecompose(out string failure)
        {
            if (gpuComposer == null || runtimeBuckets == null ||
                !activeEndpointResolved ||
                runtimeBuckets.Length != roomBasis.GetNativeEndpointAtlasCount(activeEndpoint))
            {
                failure = "GPU composer or runtime bucket layout is not initialized.";
                return false;
            }

            if (active &&
                !DungeonPortalBakedBasisLightmapRegistry.TryValidate(registration, out failure))
            {
                return false;
            }

            for (int bucketIndex = 0; bucketIndex < runtimeBuckets.Length; bucketIndex++)
            {
                if (!TryBuildContributions(bucketIndex, contributions, out failure))
                    return false;

                RuntimeBucket runtimeBucket = runtimeBuckets[bucketIndex];
                if (!gpuComposer.TryCompose(
                        runtimeBucket.ComposeBase,
                        basePower01,
                        contributions,
                        runtimeBucket.Color,
                        runtimeBucket.Direction,
                        out failure))
                {
                    failure = $"Bucket '{runtimeBucket.ComposeBase.BucketId}' failed: {failure}";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private bool TryBuildContributions(
            int bucketIndex,
            List<DungeonPortalBakedBasisGpuComposer.Contribution> output,
            out string failure)
        {
            output.Clear();
            IncomingDoorState[] states = IncomingDoors;
            for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
            {
                IncomingDoorState state = states[stateIndex];
                if (state == null || !state.Contributes || state.Aperture01 <= 0f ||
                    state.ResponseScale <= 0f)
                {
                    continue;
                }

                if (!roomBasis.TryGetReceiverDoor(
                        state.ReceiverDoorId,
                        out DungeonPortalBakedRoomBasisData.ReceiverDoorBasis receiverDoor) ||
                    state.SourceRoomBasis == null ||
                    !state.SourceRoomBasis.TryGetSourceDoor(
                        state.SourceDoorId,
                        out DungeonPortalBakedRoomBasisData.SourceDoorBasis sourceDoor))
                {
                    failure = $"Connection '{state.ConnectionId}' no longer resolves its source " +
                              "or receiver door contract.";
                    return false;
                }

                weightedResponseLobes.Clear();
                float physicalOpenFraction = ApertureToPhysicalOpenFraction(state.Aperture01);
                if (!receiverDoor.TryAppendWeightedResponseLobes(
                        physicalOpenFraction,
                        weightedResponseLobes,
                        out failure))
                {
                    failure = $"Connection '{state.ConnectionId}' pose interpolation failed: " +
                              failure;
                    return false;
                }

                for (int lobeIndex = 0;
                     lobeIndex < weightedResponseLobes.Count;
                     lobeIndex++)
                {
                    DungeonPortalBakedRoomBasisData.WeightedReceiverResponseLobe weighted =
                        weightedResponseLobes[lobeIndex];
                    DungeonPortalBakedRoomBasisData.ReceiverResponseLobe lobe = weighted.Lobe;
                    if (!sourceDoor.TryGetCoefficient(
                            lobe.BasisId,
                            out DungeonPortalBakedRoomBasisData.SourceBasisCoefficient coefficient) ||
                        !lobe.TryGetAtlas(
                            activeEndpoint,
                            bucketIndex,
                            out DungeonPortalBakedRoomBasisData.ResponseAtlas atlas))
                    {
                        failure = $"Connection '{state.ConnectionId}' is missing basis " +
                                  $"'{lobe.BasisId}' for bucket {bucketIndex}.";
                        return false;
                    }

                    Color rgb = coefficient.Evaluate(state.SourcePower01);
                    output.Add(new DungeonPortalBakedBasisGpuComposer.Contribution(
                        atlas,
                        rgb,
                        weighted.PoseWeight * state.ResponseScale));
                }
            }

            failure = null;
            return true;
        }

        private bool TryValidateAndResolve(
            out List<DungeonPortalBakedBasisLightmapRegistry.AssignmentRequest> assignments,
            out DungeonPortalBakedRoomBasisData.NativeEndpointAtlas[] nativeAtlases,
            out DungeonPortalBakedRoomBasisData.ProductionEndpoint originalEndpoint,
            out string failure)
        {
            assignments = null;
            nativeAtlases = null;
            originalEndpoint = default;
            if (!Application.isPlaying)
            {
                failure = "The room compositor is runtime-only and cannot activate in Edit Mode.";
                return false;
            }

            if (roomBasis == null)
            {
                failure = "Room basis asset is missing.";
                return false;
            }

            if (!roomBasis.TryValidateDefinition(out failure))
            {
                return false;
            }
            if (!roomBasis.TryValidateBaseTransitionLayouts(out failure))
            {
                failure = "Continuous base-transition layout is not runtime-ready: " + failure;
                return false;
            }

            if (roomRoot == null || compositionShader == null ||
                !string.Equals(
                    compositionShader.name,
                    "Hidden/DungeonPortalBakedBasisPoC/Compose",
                    StringComparison.Ordinal))
            {
                failure = "Room root or the exact DPBB composition shader is not assigned.";
                return false;
            }

            if (!IsFinite(basePower01) || basePower01 < 0f || basePower01 > 1f ||
                !IsFinite(registryValidationInterval) || registryValidationInterval < 0.05f)
            {
                failure = "Base power or registry validation interval is invalid.";
                return false;
            }

            if (!TryValidateIncomingContracts(out failure))
                return false;

            DungeonPortalBakedRoomBasisData.CanonicalRendererEntry[] entries =
                roomBasis.CanonicalRenderers;
            ExplicitRendererBinding[] bindings = ExplicitRenderers;
            if (bindings.Length != entries.Length)
            {
                failure = $"Explicit renderer count {bindings.Length} does not match canonical " +
                          $"entry count {entries.Length}.";
                return false;
            }

            if (!TryBuildCanonicalRendererKeyMap(
                    roomRoot,
                    out Dictionary<Renderer, string> computedKeys,
                    out failure))
            {
                return false;
            }

            var bindingByKey = new Dictionary<string, Renderer>(StringComparer.Ordinal);
            var rendererSet = new HashSet<Renderer>();
            for (int i = 0; i < bindings.Length; i++)
            {
                ExplicitRendererBinding binding = bindings[i];
                if (binding == null || string.IsNullOrWhiteSpace(binding.CanonicalRendererKey) ||
                    binding.Renderer == null ||
                    !bindingByKey.TryAdd(binding.CanonicalRendererKey, binding.Renderer) ||
                    !rendererSet.Add(binding.Renderer))
                {
                    failure = $"Explicit renderer binding {i} is missing or duplicated.";
                    return false;
                }

                if (!computedKeys.TryGetValue(binding.Renderer, out string actualKey) ||
                    !string.Equals(
                        actualKey,
                        binding.CanonicalRendererKey,
                        StringComparison.Ordinal))
                {
                    failure = $"Renderer binding '{binding.CanonicalRendererKey}' resolves to " +
                              $"'{actualKey ?? "<outside room traversal>"}'. Canonical keys must " +
                              "be the directly computed relativePath#occurrence string.";
                    return false;
                }

                Material[] materials = binding.Renderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    if (materials[materialIndex] == null || materials[materialIndex].shader == null)
                    {
                        failure = $"Renderer '{binding.CanonicalRendererKey}' has a missing " +
                                  $"material/shader at slot {materialIndex}.";
                        return false;
                    }
                }
            }

            LightmapData[] currentLightmaps =
                LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
            DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket[] buckets =
                roomBasis.CanonicalAtlases;
            var rendererByEntry = new Renderer[entries.Length];
            int detectedEndpoint = -1;

            for (int i = 0; i < entries.Length; i++)
            {
                DungeonPortalBakedRoomBasisData.CanonicalRendererEntry entry = entries[i];
                if (!bindingByKey.TryGetValue(entry.CanonicalRendererKey, out Renderer renderer))
                {
                    failure = $"Canonical renderer '{entry.CanonicalRendererKey}' is not bound.";
                    return false;
                }
                rendererByEntry[i] = renderer;

                if (renderer.lightmapIndex < 0 || renderer.lightmapIndex >= currentLightmaps.Length)
                {
                    failure = $"Renderer '{entry.CanonicalRendererKey}' has no valid production " +
                              "lightmap slot before activation.";
                    return false;
                }

                LightmapData current = currentLightmaps[renderer.lightmapIndex];
                DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket =
                    buckets[entry.BucketIndex];
                if (!roomBasis.TryGetPower100SourceAtlas(
                        entry.Power100SourceLocalLightmapIndex,
                        out DungeonPortalBakedRoomBasisData.Power100SourceAtlas p100Source))
                {
                    failure = $"Renderer '{entry.CanonicalRendererKey}' P100 source atlas is missing.";
                    return false;
                }

                int endpoint = MatchOriginalEndpoint(
                    current,
                    renderer.lightmapScaleOffset,
                    bucket,
                    entry,
                    p100Source);
                if (endpoint < 0)
                {
                    failure = $"Renderer '{entry.CanonicalRendererKey}' current lightmap slot " +
                              "does not exactly match its original P0 mapping or original P100 " +
                              "source-atlas mapping.";
                    return false;
                }

                if (detectedEndpoint < 0)
                    detectedEndpoint = endpoint;
                else if (endpoint != detectedEndpoint && endpoint != 2 && detectedEndpoint != 2)
                {
                    failure = "Canonical room buckets are split across P0 and P100 before activation.";
                    return false;
                }
                else if (detectedEndpoint == 2)
                    detectedEndpoint = endpoint;
            }

            if (detectedEndpoint < 0)
            {
                failure = "No exact production endpoint could be detected before activation.";
                return false;
            }
            if (detectedEndpoint == 2)
            {
                // When every texture/ST/shadow reference is literally identical at both
                // endpoints, use an exact requested endpoint if available. At an intermediate
                // power both native layouts are the same, so P0 is the deterministic tie-break.
                int requestedEndpoint = GetPowerEndpoint(basePower01);
                detectedEndpoint = requestedEndpoint >= 0 ? requestedEndpoint : 0;
            }

            originalEndpoint = detectedEndpoint == 1
                ? DungeonPortalBakedRoomBasisData.ProductionEndpoint.Power100
                : DungeonPortalBakedRoomBasisData.ProductionEndpoint.Power0;
            int nativeBucketCount = roomBasis.GetNativeEndpointAtlasCount(originalEndpoint);
            if (nativeBucketCount <= 0)
            {
                failure = $"Captured P{detectedEndpoint * 100} endpoint has no native atlases.";
                return false;
            }

            nativeAtlases =
                new DungeonPortalBakedRoomBasisData.NativeEndpointAtlas[nativeBucketCount];
            for (int i = 0; i < nativeAtlases.Length; i++)
            {
                if (!roomBasis.TryGetNativeEndpointAtlas(
                        originalEndpoint,
                        i,
                        out nativeAtlases[i]) ||
                    nativeAtlases[i].Color == null || nativeAtlases[i].Direction == null)
                {
                    failure = $"Captured endpoint native atlas {i} is incomplete.";
                    return false;
                }
            }

            var observedBuckets = new bool[nativeBucketCount];
            assignments = new List<DungeonPortalBakedBasisLightmapRegistry.AssignmentRequest>(
                entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                DungeonPortalBakedRoomBasisData.CanonicalRendererEntry entry = entries[i];
                int nativeLocalIndex = entry.GetNativeLocalLightmapIndex(originalEndpoint);
                if (!roomBasis.TryGetNativeEndpointBucketIndex(
                        originalEndpoint,
                        nativeLocalIndex,
                        out int nativeBucketIndex))
                {
                    failure = $"Renderer '{entry.CanonicalRendererKey}' native P" +
                              $"{detectedEndpoint * 100} lightmap {nativeLocalIndex} is missing.";
                    return false;
                }

                observedBuckets[nativeBucketIndex] = true;
                assignments.Add(new DungeonPortalBakedBasisLightmapRegistry.AssignmentRequest(
                    rendererByEntry[i],
                    nativeBucketIndex,
                    entry.GetNativeScaleOffset(originalEndpoint)));
            }

            for (int i = 0; i < observedBuckets.Length; i++)
            {
                if (!observedBuckets[i])
                {
                    failure = $"Captured endpoint native atlas {i} has no explicitly assigned " +
                              "renderer.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private bool TryValidateIncomingContracts(out string failure)
        {
            var connectionIds = new HashSet<string>(StringComparer.Ordinal);
            var receiverDoorIds = new HashSet<string>(StringComparer.Ordinal);
            IncomingDoorState[] states = IncomingDoors;
            for (int i = 0; i < states.Length; i++)
            {
                IncomingDoorState state = states[i];
                if (state == null || string.IsNullOrWhiteSpace(state.ConnectionId) ||
                    !connectionIds.Add(state.ConnectionId) ||
                    string.IsNullOrWhiteSpace(state.ReceiverDoorId) ||
                    !receiverDoorIds.Add(state.ReceiverDoorId) ||
                    state.SourceRoomBasis == null || string.IsNullOrWhiteSpace(state.SourceDoorId) ||
                    !IsFinite(state.SourcePower01) || !IsFinite(state.Aperture01) ||
                    !IsFinite(state.ResponseScale) || state.ResponseScale < 0f)
                {
                    failure = $"Incoming door binding {i} is incomplete, duplicated, or non-finite.";
                    return false;
                }

                if (!state.SourceRoomBasis.TryValidateDefinition(out string sourceFailure))
                {
                    failure = $"Incoming connection '{state.ConnectionId}' source room is invalid: " +
                              sourceFailure;
                    return false;
                }

                if (!roomBasis.TryGetReceiverDoor(
                        state.ReceiverDoorId,
                        out DungeonPortalBakedRoomBasisData.ReceiverDoorBasis receiverDoor) ||
                    !state.SourceRoomBasis.TryGetSourceDoor(
                        state.SourceDoorId,
                        out DungeonPortalBakedRoomBasisData.SourceDoorBasis sourceDoor))
                {
                    failure = $"Incoming connection '{state.ConnectionId}' cannot resolve its " +
                              "receiver or source door id.";
                    return false;
                }

                DungeonPortalBakedRoomBasisData.ReceiverResponseLobe[] lobes =
                    receiverDoor.ContractLobes;
                if (lobes.Length != sourceDoor.Coefficients.Length)
                {
                    failure = $"Incoming connection '{state.ConnectionId}' has K={lobes.Length} " +
                              $"receiver lobes but K={sourceDoor.Coefficients.Length} source coefficients.";
                    return false;
                }

                for (int lobeIndex = 0; lobeIndex < lobes.Length; lobeIndex++)
                {
                    if (!sourceDoor.TryGetCoefficient(lobes[lobeIndex].BasisId, out _))
                    {
                        failure = $"Incoming connection '{state.ConnectionId}' source door lacks " +
                                  $"basis coefficient '{lobes[lobeIndex].BasisId}'.";
                        return false;
                    }
                }
            }

            failure = null;
            return true;
        }

        private static int MatchOriginalEndpoint(
            LightmapData current,
            Vector4 currentScaleOffset,
            DungeonPortalBakedRoomBasisData.CanonicalAtlasBucket bucket,
            DungeonPortalBakedRoomBasisData.CanonicalRendererEntry entry,
            DungeonPortalBakedRoomBasisData.Power100SourceAtlas p100Source)
        {
            if (current == null || p100Source == null)
                return -1;
            bool p0 = currentScaleOffset == entry.LightmapScaleOffset &&
                      current.lightmapColor == bucket.Power0Color &&
                      current.lightmapDir == bucket.Power0Direction &&
                      current.shadowMask == bucket.UnchangedShadowMask;
            bool p100 = currentScaleOffset == entry.Power100SourceScaleOffset &&
                         current.lightmapColor == p100Source.Color &&
                         current.lightmapDir == p100Source.Direction &&
                         current.shadowMask == p100Source.ShadowMask;
            if (p0 && p100)
                return 2;
            if (p0)
                return 0;
            return p100 ? 1 : -1;
        }

        private static bool TryGetRelativePath(
            Transform root,
            Transform target,
            out string path)
        {
            if (root == null || target == null)
            {
                path = null;
                return false;
            }

            if (root == target)
            {
                path = string.Empty;
                return true;
            }

            var names = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }

            if (current != root)
            {
                path = null;
                return false;
            }

            path = string.Join("/", names);
            return true;
        }

        private static bool TryBuildCanonicalRendererKeyMap(
            Transform root,
            out Dictionary<Renderer, string> result,
            out string failure)
        {
            result = new Dictionary<Renderer, string>();
            if (root == null)
            {
                failure = "Cannot compute canonical renderer keys without a room root.";
                return false;
            }

            Renderer[] traversal = root.GetComponentsInChildren<Renderer>(true);
            var occurrenceByBarePath = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < traversal.Length; i++)
            {
                Renderer renderer = traversal[i];
                if (renderer == null ||
                    !TryGetRelativePath(root, renderer.transform, out string barePath))
                {
                    failure = $"Renderer traversal entry {i} is missing or outside the room root.";
                    result = null;
                    return false;
                }

                occurrenceByBarePath.TryGetValue(barePath, out int occurrence);
                occurrenceByBarePath[barePath] = occurrence + 1;
                // Do not parse user strings: an object name may itself contain '#'. Equality
                // is checked only against this directly computed key.
                string key = barePath + "#" + occurrence;
                if (!result.TryAdd(renderer, key))
                {
                    failure = $"Renderer traversal contains a duplicate reference at index {i}.";
                    result = null;
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private int ComputeStateHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + basePower01.GetHashCode();
                IncomingDoorState[] values = IncomingDoors;
                hash = hash * 31 + values.Length;
                for (int i = 0; i < values.Length; i++)
                {
                    IncomingDoorState state = values[i];
                    if (state == null)
                    {
                        hash = hash * 31;
                        continue;
                    }

                    hash = hash * 31 + (state.Contributes ? 1 : 0);
                    hash = hash * 31 + state.SourcePower01.GetHashCode();
                    hash = hash * 31 + state.Aperture01.GetHashCode();
                    hash = hash * 31 + state.ResponseScale.GetHashCode();
                    hash = hash * 31 +
                           (state.SourceRoomBasis != null
                               ? state.SourceRoomBasis.GetEntityId().GetHashCode()
                               : 0);
                }

                return hash;
            }
        }

        private bool HasOpenIncomingAperture()
        {
            IncomingDoorState[] values = IncomingDoors;
            for (int i = 0; i < values.Length; i++)
            {
                IncomingDoorState state = values[i];
                if (state != null && state.Contributes && state.Aperture01 > 0.0001f)
                    return true;
            }

            return false;
        }

        private bool CanRestoreOriginalEndpoint(out string failure)
        {
            if (HasOpenIncomingAperture())
            {
                failure = "Original production lightmaps cannot be restored while an incoming " +
                          "connection still has a non-zero aperture.";
                return false;
            }

            int targetEndpoint = GetPowerEndpoint(basePower01);
            if (targetEndpoint < 0)
            {
                failure = "D0 restore requires basePower01 to be exactly canonical P0 or P100.";
                return false;
            }

            if (capturedOriginalEndpoint >= 0 && capturedOriginalEndpoint != 2 &&
                capturedOriginalEndpoint != targetEndpoint)
            {
                failure = $"D0 restore would return the captured P{capturedOriginalEndpoint * 100} " +
                          $"production references while basePower01 requests P{targetEndpoint * 100}.";
                return false;
            }

            failure = null;
            return true;
        }

        private static int GetPowerEndpoint(float value01)
        {
            if (value01 <= 0.0001f)
                return 0;
            if (value01 >= 0.9999f)
                return 1;
            return -1;
        }

        private void LatchFault(string reason, bool tearDown)
        {
            string resolved = string.IsNullOrWhiteSpace(reason)
                ? "Unknown baked-basis fail-closed condition."
                : reason;
            if (tearDown && !TearDown(false, out string restoreFailure) &&
                !string.IsNullOrWhiteSpace(restoreFailure))
            {
                resolved += " Teardown: " + restoreFailure;
            }

            active = false;
            faultLatched = true;
            faultReason = resolved;
            Debug.LogError(
                $"[{nameof(DungeonPortalBakedBasisRoomCompositor)}] '{name}' disabled " +
                $"fail-closed: {resolved}",
                this);
        }

        private void TearDown(bool logFailure)
        {
            if (!TearDown(logFailure, out string failure) && logFailure)
                Debug.LogError($"[{nameof(DungeonPortalBakedBasisRoomCompositor)}] {failure}", this);
        }

        private bool TearDown(bool logFailure, out string failure)
        {
            active = false;
            bool restored = DungeonPortalBakedBasisLightmapRegistry.TryUnregister(
                registration,
                out failure);
            registration = null;

            if (runtimeBuckets != null)
            {
                for (int i = runtimeBuckets.Length - 1; i >= 0; i--)
                {
                    RuntimeBucket bucket = runtimeBuckets[i];
                    if (bucket == null)
                        continue;
                    DestroyRuntimeObject(bucket.Direction);
                    DestroyRuntimeObject(bucket.Color);
                    bucket.Direction = null;
                    bucket.Color = null;
                    bucket.Slot = null;
                    bucket.ComposeBase = null;
                }
            }

            runtimeBuckets = Array.Empty<RuntimeBucket>();
            gpuComposer?.Dispose();
            gpuComposer = null;
            capturedOriginalEndpoint = -1;
            activeEndpoint = default;
            activeEndpointResolved = false;
            compositionDirty = true;
            if (!restored && logFailure && !string.IsNullOrWhiteSpace(failure))
                Debug.LogError($"[{nameof(DungeonPortalBakedBasisRoomCompositor)}] {failure}", this);
            return restored;
        }

        private static void DestroyRuntimeObject(Object value)
        {
            if (value == null)
                return;
            if (Application.isPlaying)
                Destroy(value);
            else
                DestroyImmediate(value);
        }

        private void OnValidate()
        {
            basePower01 = Mathf.Clamp01(basePower01);
            if (!IsFinite(registryValidationInterval) || registryValidationInterval < 0.05f)
                registryValidationInterval = 0.5f;
            IncomingDoorState[] values = IncomingDoors;
            for (int i = 0; i < values.Length; i++)
                values[i]?.ClampSerializedValues();
            compositionDirty = true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static T[] CloneArray<T>(T[] values)
        {
            if (values == null || values.Length == 0)
                return Array.Empty<T>();
            var clone = new T[values.Length];
            Array.Copy(values, clone, values.Length);
            return clone;
        }
    }
}
