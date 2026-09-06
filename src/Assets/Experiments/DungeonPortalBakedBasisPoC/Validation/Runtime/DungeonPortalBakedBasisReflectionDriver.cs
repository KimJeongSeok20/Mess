using System;
using DungeonPortalTransportPoC;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonPortalBakedBasisPoC.Validation
{
    /// <summary>
    /// Validation-only reflection bridge. Five DPBB-owned probes blend the existing canonical
    /// room P0-residual/P100 cubemaps from room power alone. Door aperture and the adjacent-light
    /// toggle are deliberately outside this component's input contract.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class DungeonPortalBakedBasisReflectionDriver : MonoBehaviour
    {
        public const int ExpectedStartProbeCount = 1;
        public const int ExpectedAdministrativeProbeCount = 4;
        public const int ExpectedSuppressedProductionProbeCount = 10;

        private const float PowerEpsilon = 0.0001f;

        [Serializable]
        private sealed class ProbeBinding
        {
            [SerializeField] private string stableId;
            [SerializeField] private ReflectionProbe ownedProbe;
            [SerializeField] private DungeonPortalRoomReflectionProfile profile;

            internal string StableId => stableId;
            internal ReflectionProbe OwnedProbe => ownedProbe;
            internal DungeonPortalRoomReflectionProfile Profile => profile;

            internal ProbeBinding(
                string id,
                ReflectionProbe probe,
                DungeonPortalRoomReflectionProfile roomProfile)
            {
                stableId = id ?? string.Empty;
                ownedProbe = probe;
                profile = roomProfile;
            }
        }

        private sealed class ProbeState
        {
            internal readonly ReflectionProbe Probe;
            internal readonly bool Enabled;
            internal readonly ReflectionProbeMode Mode;
            internal readonly Texture CustomBakedTexture;
            internal readonly float Intensity;

            internal ProbeState(ReflectionProbe probe)
            {
                Probe = probe;
                Enabled = probe.enabled;
                Mode = probe.mode;
                CustomBakedTexture = probe.customBakedTexture;
                Intensity = probe.intensity;
            }

            internal void Restore()
            {
                if (Probe == null)
                    return;
                Probe.enabled = Enabled;
                Probe.mode = Mode;
                Probe.customBakedTexture = CustomBakedTexture;
                Probe.intensity = Intensity;
            }
        }

        [SerializeField] private DungeonPortalBakedBasisConnectionDriver connectionDriver;
        [SerializeField] private ProbeBinding startProbe;
        [SerializeField] private ProbeBinding[] administrativeProbes = Array.Empty<ProbeBinding>();
        [SerializeField] private ReflectionProbe[] productionProbesToSuppress =
            Array.Empty<ReflectionProbe>();

        private RenderTexture[] blendTargets = Array.Empty<RenderTexture>();
        private ProbeState[] capturedProbeStates = Array.Empty<ProbeState>();
        private bool statesCaptured;
        private bool initialized;
        private bool started;
        private bool faultLatched;
        private string faultReason;
        private float lastAppliedStartPower01 = -1f;
        private float lastAppliedAdministrativePower01 = -1f;

        public DungeonPortalBakedBasisConnectionDriver ConnectionDriver => connectionDriver;
        public int OwnedProbeCount =>
            (startProbe != null ? 1 : 0) + (administrativeProbes?.Length ?? 0);
        public int SuppressedProductionProbeCount => productionProbesToSuppress?.Length ?? 0;
        public bool IsInitialized => initialized;
        public bool IsFaultLatched => faultLatched;
        public string FaultReason => faultReason;
        public float LastAppliedStartPower01 => lastAppliedStartPower01;
        public float LastAppliedAdministrativePower01 => lastAppliedAdministrativePower01;

        /// <summary>
        /// Returns the validation-owned probe geometry and profile bindings without exposing the
        /// mutable backing arrays. StartMap runtime rebinding uses this before the driver starts so
        /// the same owned probes can suppress the actual generated room's production variants.
        /// </summary>
        public bool TryGetAuthoringConfiguration(
            out string startStableId,
            out ReflectionProbe startOwnedProbe,
            out DungeonPortalRoomReflectionProfile startProfile,
            out string[] administrativeStableIds,
            out ReflectionProbe[] administrativeOwnedProbes,
            out DungeonPortalRoomReflectionProfile[] administrativeProfiles,
            out string failure)
        {
            startStableId = startProbe != null ? startProbe.StableId : string.Empty;
            startOwnedProbe = startProbe != null ? startProbe.OwnedProbe : null;
            startProfile = startProbe != null ? startProbe.Profile : null;
            int count = administrativeProbes?.Length ?? 0;
            administrativeStableIds = new string[count];
            administrativeOwnedProbes = new ReflectionProbe[count];
            administrativeProfiles = new DungeonPortalRoomReflectionProfile[count];
            for (int i = 0; i < count; i++)
            {
                ProbeBinding binding = administrativeProbes[i];
                if (binding == null)
                    continue;
                administrativeStableIds[i] = binding.StableId;
                administrativeOwnedProbes[i] = binding.OwnedProbe;
                administrativeProfiles[i] = binding.Profile;
            }

            if (string.IsNullOrWhiteSpace(startStableId) || startOwnedProbe == null ||
                startProfile == null || count != ExpectedAdministrativeProbeCount)
            {
                failure = "DPBB-owned reflection authoring bindings are incomplete.";
                return false;
            }
            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrWhiteSpace(administrativeStableIds[i]) ||
                    administrativeOwnedProbes[i] == null || administrativeProfiles[i] == null)
                {
                    failure = "DPBB-owned administrative reflection binding " + i +
                              " is incomplete.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        public void ConfigureAuthoring(
            DungeonPortalBakedBasisConnectionDriver roomPowerDriver,
            string startStableId,
            ReflectionProbe startOwnedProbe,
            DungeonPortalRoomReflectionProfile startProfile,
            string[] administrativeStableIds,
            ReflectionProbe[] administrativeOwnedProbes,
            DungeonPortalRoomReflectionProfile[] administrativeProfiles,
            ReflectionProbe[] probesToSuppress)
        {
            RestoreAndRelease();

            connectionDriver = roomPowerDriver;
            startProbe = new ProbeBinding(startStableId, startOwnedProbe, startProfile);

            int administrativeCount = administrativeOwnedProbes?.Length ?? 0;
            administrativeProbes = new ProbeBinding[administrativeCount];
            for (int i = 0; i < administrativeCount; i++)
            {
                string id = administrativeStableIds != null && i < administrativeStableIds.Length
                    ? administrativeStableIds[i]
                    : string.Empty;
                DungeonPortalRoomReflectionProfile profile =
                    administrativeProfiles != null && i < administrativeProfiles.Length
                        ? administrativeProfiles[i]
                        : null;
                administrativeProbes[i] = new ProbeBinding(
                    id,
                    administrativeOwnedProbes[i],
                    profile);
            }

            productionProbesToSuppress = probesToSuppress == null
                ? Array.Empty<ReflectionProbe>()
                : (ReflectionProbe[])probesToSuppress.Clone();
            faultLatched = false;
            faultReason = null;
        }

        private void Start()
        {
            started = true;
            if (!TryApplyConnectionPower(out string failure))
                FailClosed(failure);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying || !started || initialized || faultLatched)
                return;
            if (!TryApplyConnectionPower(out string failure))
                FailClosed(failure);
        }

        private void LateUpdate()
        {
            if (!initialized || faultLatched)
                return;
            if (connectionDriver == null)
            {
                FailClosed("The DPBB connection driver was destroyed during reflection ownership.");
                return;
            }
            if (connectionDriver.IsFaultLatched)
            {
                FailClosed("The DPBB connection driver fault-latched: " +
                           connectionDriver.FaultReason);
                return;
            }
            if (!TryApplyPowers(
                    connectionDriver.CurrentStartPower01,
                    connectionDriver.CurrentAdministrativePower01,
                    out string failure))
            {
                FailClosed(failure);
            }
        }

        private void OnDisable()
        {
            RestoreAndRelease();
        }

        private void OnDestroy()
        {
            RestoreAndRelease();
        }

        /// <summary>
        /// Deterministic validation hook. It follows the same room-power-only path as LateUpdate
        /// and intentionally has no aperture or adjacent-enable argument.
        /// </summary>
        public bool TryApplyImmediateForEvidence(
            float startPower01,
            float administrativePower01,
            out string failure)
        {
            if (faultLatched)
            {
                failure = "The reflection driver is fault-latched: " + faultReason;
                return false;
            }

            bool succeeded = initialized
                ? TryApplyPowers(startPower01, administrativePower01, out failure)
                : TryInitialize(startPower01, administrativePower01, out failure);
            if (!succeeded)
                FailClosed(failure);
            return succeeded;
        }

        /// <summary>Restores every probe property this component can mutate and releases blends.</summary>
        public void RestoreProbeStatesForEvidence()
        {
            RestoreAndRelease();
        }

        public bool TryValidateConfiguration(out string failure)
        {
            if (connectionDriver == null)
            {
                failure = "The DPBB connection driver is not assigned.";
                return false;
            }
            if (startProbe == null)
            {
                failure = "The single Start reflection binding is missing.";
                return false;
            }
            if (administrativeProbes == null ||
                administrativeProbes.Length != ExpectedAdministrativeProbeCount)
            {
                failure = "Exactly four Administrative reflection bindings are required.";
                return false;
            }
            if (productionProbesToSuppress == null ||
                productionProbesToSuppress.Length != ExpectedSuppressedProductionProbeCount)
            {
                failure = "Exactly ten production P0/P100 probe variants must be suppressed.";
                return false;
            }
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {
                failure = "ARGBHalf cubemap RenderTextures are not supported on this platform.";
                return false;
            }

            var ownedProbes = new ReflectionProbe[ExpectedStartProbeCount +
                                                  ExpectedAdministrativeProbeCount];
            var profiles = new DungeonPortalRoomReflectionProfile[ownedProbes.Length];
            var stableIds = new string[ownedProbes.Length];
            if (!TryValidateBinding(
                    startProbe, 0, ownedProbes, profiles, stableIds, out failure))
                return false;
            for (int i = 0; i < administrativeProbes.Length; i++)
            {
                if (!TryValidateBinding(
                        administrativeProbes[i], i + 1, ownedProbes, profiles, stableIds,
                        out failure))
                {
                    return false;
                }
            }

            for (int i = 0; i < productionProbesToSuppress.Length; i++)
            {
                ReflectionProbe candidate = productionProbesToSuppress[i];
                if (candidate == null)
                {
                    failure = "Production probe suppression entry " + i + " is missing.";
                    return false;
                }
                for (int ownedIndex = 0; ownedIndex < ownedProbes.Length; ownedIndex++)
                {
                    if (candidate == ownedProbes[ownedIndex])
                    {
                        failure = "A DPBB-owned reflection probe cannot suppress itself.";
                        return false;
                    }
                }
                for (int previous = 0; previous < i; previous++)
                {
                    if (candidate == productionProbesToSuppress[previous])
                    {
                        failure = "The production probe suppression list contains a duplicate.";
                        return false;
                    }
                }
            }

            failure = null;
            return true;
        }

        private bool TryValidateBinding(
            ProbeBinding binding,
            int index,
            ReflectionProbe[] seenProbes,
            DungeonPortalRoomReflectionProfile[] seenProfiles,
            string[] seenStableIds,
            out string failure)
        {
            if (binding == null || string.IsNullOrWhiteSpace(binding.StableId))
            {
                failure = "Reflection binding " + index + " has no stable id.";
                return false;
            }
            if (binding.OwnedProbe == null)
            {
                failure = "Reflection binding '" + binding.StableId + "' has no owned probe.";
                return false;
            }
            if (binding.OwnedProbe.transform == transform ||
                !binding.OwnedProbe.transform.IsChildOf(transform))
            {
                failure = "Reflection binding '" + binding.StableId +
                          "' is not owned below the DPBB reflection driver.";
                return false;
            }
            if (binding.OwnedProbe.GetComponents<ReflectionProbe>().Length != 1)
            {
                failure = "Reflection binding '" + binding.StableId +
                          "' must own exactly one ReflectionProbe component.";
                return false;
            }
            string profileFailure = binding.Profile == null ? "profile is missing" : null;
            if (binding.Profile == null || !binding.Profile.TryValidate(out profileFailure))
            {
                failure = "Reflection binding '" + binding.StableId +
                          "' profile is invalid: " + profileFailure;
                return false;
            }

            for (int previous = 0; previous < index; previous++)
            {
                if (binding.OwnedProbe == seenProbes[previous])
                {
                    failure = "DPBB reflection bindings contain a duplicate owned probe.";
                    return false;
                }
                if (binding.Profile == seenProfiles[previous])
                {
                    failure = "DPBB reflection bindings contain a duplicate profile.";
                    return false;
                }
                if (string.Equals(
                        binding.StableId, seenStableIds[previous], StringComparison.Ordinal))
                {
                    failure = "DPBB reflection bindings contain a duplicate stable id.";
                    return false;
                }
            }
            seenProbes[index] = binding.OwnedProbe;
            seenProfiles[index] = binding.Profile;
            seenStableIds[index] = binding.StableId;
            failure = null;
            return true;
        }

        private bool TryApplyConnectionPower(out string failure)
        {
            if (connectionDriver == null)
            {
                failure = "The DPBB connection driver is missing.";
                return false;
            }
            if (connectionDriver.IsFaultLatched)
            {
                failure = "The DPBB connection driver is fault-latched: " +
                          connectionDriver.FaultReason;
                return false;
            }
            return TryInitialize(
                connectionDriver.CurrentStartPower01,
                connectionDriver.CurrentAdministrativePower01,
                out failure);
        }

        private bool TryInitialize(
            float startPower01,
            float administrativePower01,
            out string failure)
        {
            if (initialized)
                return TryApplyPowers(startPower01, administrativePower01, out failure);
            if (!IsFinite01(startPower01) || !IsFinite01(administrativePower01))
            {
                failure = "Reflection room powers must be finite values in [0,1].";
                return false;
            }
            if (!TryValidateConfiguration(out failure))
                return false;

            ProbeBinding[] bindings = GetBindings();
            var candidates = new RenderTexture[bindings.Length];
            try
            {
                for (int i = 0; i < bindings.Length; i++)
                {
                    candidates[i] = CreateBlendTarget(bindings[i].Profile.Resolution, i);
                    if (candidates[i] == null)
                    {
                        failure = "Could not allocate reflection blend target " + i + ".";
                        ReleaseTargets(candidates);
                        return false;
                    }
                    float power = i == 0 ? startPower01 : administrativePower01;
                    if (!TryBlend(bindings[i], power, candidates[i], out failure))
                    {
                        ReleaseTargets(candidates);
                        return false;
                    }
                }

                CaptureProbeStates(bindings);
                blendTargets = candidates;
                for (int i = 0; i < bindings.Length; i++)
                    ApplyOwnedProbe(bindings[i], blendTargets[i]);
                SuppressProductionProbes();
                initialized = true;
                lastAppliedStartPower01 = Mathf.Clamp01(startPower01);
                lastAppliedAdministrativePower01 = Mathf.Clamp01(administrativePower01);
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                RestoreAndRelease();
                ReleaseTargets(candidates);
                failure = "Reflection initialization threw " + exception.GetType().Name +
                          ": " + exception.Message;
                return false;
            }
        }

        private bool TryApplyPowers(
            float startPower01,
            float administrativePower01,
            out string failure)
        {
            if (!initialized || blendTargets == null || blendTargets.Length != 5)
            {
                failure = "Reflection blends are not initialized.";
                return false;
            }
            if (!IsFinite01(startPower01) || !IsFinite01(administrativePower01))
            {
                failure = "Reflection room powers must be finite values in [0,1].";
                return false;
            }
            if (!TryValidateConfiguration(out failure))
                return false;

            ProbeBinding[] bindings = GetBindings();
            float clampedStart = Mathf.Clamp01(startPower01);
            float clampedAdministrative = Mathf.Clamp01(administrativePower01);
            bool startChanged = Mathf.Abs(clampedStart - lastAppliedStartPower01) > PowerEpsilon;
            bool administrativeChanged = Mathf.Abs(
                clampedAdministrative - lastAppliedAdministrativePower01) > PowerEpsilon;

            for (int i = 0; i < bindings.Length; i++)
            {
                if ((i == 0 ? startChanged : administrativeChanged) &&
                    !TryBlend(
                        bindings[i],
                        i == 0 ? clampedStart : clampedAdministrative,
                        blendTargets[i],
                        out failure))
                {
                    return false;
                }
                ApplyOwnedProbe(bindings[i], blendTargets[i]);
            }
            SuppressProductionProbes();
            lastAppliedStartPower01 = clampedStart;
            lastAppliedAdministrativePower01 = clampedAdministrative;
            failure = null;
            return true;
        }

        private static bool TryBlend(
            ProbeBinding binding,
            float power01,
            RenderTexture target,
            out string failure)
        {
            try
            {
                if (ReflectionProbe.BlendCubemap(
                        binding.Profile.Power0ResidualCubemap,
                        binding.Profile.Power100Cubemap,
                        Mathf.Clamp01(power01),
                        target))
                {
                    failure = null;
                    return true;
                }
                failure = "ReflectionProbe.BlendCubemap rejected '" +
                          binding.StableId + "'.";
                return false;
            }
            catch (Exception exception)
            {
                failure = "Reflection blend '" + binding.StableId + "' threw " +
                          exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private ProbeBinding[] GetBindings()
        {
            var result = new ProbeBinding[1 + administrativeProbes.Length];
            result[0] = startProbe;
            Array.Copy(administrativeProbes, 0, result, 1, administrativeProbes.Length);
            return result;
        }

        private void CaptureProbeStates(ProbeBinding[] bindings)
        {
            var states = new ProbeState[bindings.Length + productionProbesToSuppress.Length];
            int index = 0;
            for (int i = 0; i < bindings.Length; i++)
                states[index++] = new ProbeState(bindings[i].OwnedProbe);
            for (int i = 0; i < productionProbesToSuppress.Length; i++)
                states[index++] = new ProbeState(productionProbesToSuppress[i]);
            capturedProbeStates = states;
            statesCaptured = true;
        }

        private static void ApplyOwnedProbe(ProbeBinding binding, RenderTexture target)
        {
            ReflectionProbe probe = binding.OwnedProbe;
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = target;
            probe.intensity = binding.Profile.FixedProbeIntensity;
            probe.enabled = true;
        }

        private void SuppressProductionProbes()
        {
            for (int i = 0; i < productionProbesToSuppress.Length; i++)
                productionProbesToSuppress[i].enabled = false;
        }

        private void RestoreAndRelease()
        {
            if (statesCaptured)
            {
                for (int i = capturedProbeStates.Length - 1; i >= 0; i--)
                    capturedProbeStates[i]?.Restore();
            }
            capturedProbeStates = Array.Empty<ProbeState>();
            statesCaptured = false;

            ReleaseTargets(blendTargets);
            blendTargets = Array.Empty<RenderTexture>();
            initialized = false;
            lastAppliedStartPower01 = -1f;
            lastAppliedAdministrativePower01 = -1f;
        }

        private void FailClosed(string reason)
        {
            faultReason = string.IsNullOrWhiteSpace(reason)
                ? "Unknown DPBB reflection failure."
                : reason;
            faultLatched = true;
            RestoreAndRelease();
            if (enabled)
                enabled = false;
            Debug.LogError("[" + nameof(DungeonPortalBakedBasisReflectionDriver) +
                           "] Disabled and restored all captured probe states: " + faultReason,
                this);
        }

        private static RenderTexture CreateBlendTarget(int resolution, int index)
        {
            var target = new RenderTexture(
                resolution,
                resolution,
                0,
                RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear)
            {
                name = "DPBB_ReflectionBlend_" + index + "_" + resolution,
                dimension = TextureDimension.Cube,
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            try
            {
                target.Create();
            }
            catch (Exception)
            {
                ReleaseTarget(target);
                return null;
            }
            if (target.IsCreated())
                return target;
            ReleaseTarget(target);
            return null;
        }

        private static void ReleaseTargets(RenderTexture[] targets)
        {
            if (targets == null)
                return;
            for (int i = 0; i < targets.Length; i++)
                ReleaseTarget(targets[i]);
        }

        private static void ReleaseTarget(RenderTexture target)
        {
            if (target == null)
                return;
            if (target.IsCreated())
                target.Release();
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        private static bool IsFinite01(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                   value >= 0f && value <= 1f;
        }
    }
}
