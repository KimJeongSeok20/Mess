using System;
using DungeonPortalBakedBasisPoC;
using DungeonPortalTransportPoC;
using UnityEngine;

namespace DungeonPortalBakedBasisPoC.Validation
{
    /// <summary>
    /// Runtime-only scalar bridge for one physical doorway. It owns no Light, Renderer, Material,
    /// or MaterialPropertyBlock. The two room compositors own the private lightmap slots; this
    /// component only feeds their source-power and aperture scalars.
    ///
    /// The closed-door path is deliberately an exact production-lightmap bypass. Before a P0/P100
    /// endpoint changes while transfer is zero, it restores the captured production endpoint, asks
    /// the production switcher to apply the requested endpoint, and then synchronizes the
    /// compositor's base scalar. This order prevents a private atlas or stale index/ST from
    /// surviving at D0.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class DungeonPortalBakedBasisConnectionDriver : MonoBehaviour
    {
        private const float ZeroApertureEpsilon = 0.0001f;

        [Header("Stable connection contract")]
        [SerializeField] private string connectionId = "Start_Admin_BakedBasisV1";
        [SerializeField] private DungeonPortalBakedBasisRoomCompositor startRoomCompositor;
        [SerializeField] private DungeonPortalBakedBasisRoomCompositor administrativeRoomCompositor;
        [SerializeField] private DungeonPortalBakedRoomBasisData startRoomBasis;
        [SerializeField] private DungeonPortalBakedRoomBasisData administrativeRoomBasis;
        [SerializeField] private string startDoorId;
        [SerializeField] private string administrativeDoorId;
        [SerializeField] private DungeonPortalDoorAngleSource doorAngleSource;
        [SerializeField] private DungeonTileLightmapSwitcher startPowerSwitcher;
        [SerializeField] private DungeonTileLightmapSwitcher administrativePowerSwitcher;

        [Header("Live scalar targets")]
        [SerializeField, Range(0f, 1f)] private float startPower01 = 1f;
        [SerializeField, Range(0f, 1f)] private float administrativePower01 = 1f;
        [SerializeField] private bool adjacentTransportEnabled = true;
        [SerializeField, Min(0f)] private float powerTransitionSeconds = 0.35f;
        [SerializeField, Min(0f)] private float apertureTransitionSeconds = 0.25f;
        [SerializeField] private bool driveOnStart = true;

        private float smoothedStartPower01;
        private float smoothedAdministrativePower01;
        private float smoothedAperture01;
        private bool initialized;
        private bool evidenceOverride;
        private float evidenceAperture01;
        private bool faultLatched;
        private string faultReason;

        public string ConnectionId => connectionId;
        public float StartPower01 => startPower01;
        public float AdministrativePower01 => administrativePower01;
        public float CurrentStartPower01 => smoothedStartPower01;
        public float CurrentAdministrativePower01 => smoothedAdministrativePower01;
        public float CurrentAperture01 => smoothedAperture01;
        public bool AdjacentTransportEnabled => adjacentTransportEnabled;
        /// <summary>True only while the deterministic smoke tool owns the scalar inputs.</summary>
        public bool IsEvidenceOverrideActive => evidenceOverride;
        public bool IsFaultLatched => faultLatched;
        public string FaultReason => faultReason;
        public DungeonPortalDoorAngleSource DoorAngleSource => doorAngleSource;
        public DungeonPortalBakedBasisRoomCompositor StartRoomCompositor => startRoomCompositor;
        public DungeonPortalBakedBasisRoomCompositor AdministrativeRoomCompositor =>
            administrativeRoomCompositor;

        public void ConfigureAuthoring(
            string stableConnectionId,
            DungeonPortalBakedBasisRoomCompositor startCompositor,
            DungeonPortalBakedBasisRoomCompositor administrativeCompositor,
            DungeonPortalBakedRoomBasisData startBasis,
            DungeonPortalBakedRoomBasisData administrativeBasis,
            string stableStartDoorId,
            string stableAdministrativeDoorId,
            DungeonPortalDoorAngleSource liveDoorAngleSource,
            DungeonTileLightmapSwitcher startSwitcher,
            DungeonTileLightmapSwitcher administrativeSwitcher,
            float initialStartPower01,
            float initialAdministrativePower01,
            float powerSeconds = 0.35f,
            float apertureSeconds = 0.25f)
        {
            connectionId = stableConnectionId ?? string.Empty;
            startRoomCompositor = startCompositor;
            administrativeRoomCompositor = administrativeCompositor;
            startRoomBasis = startBasis;
            administrativeRoomBasis = administrativeBasis;
            startDoorId = stableStartDoorId ?? string.Empty;
            administrativeDoorId = stableAdministrativeDoorId ?? string.Empty;
            doorAngleSource = liveDoorAngleSource;
            startPowerSwitcher = startSwitcher;
            administrativePowerSwitcher = administrativeSwitcher;
            startPower01 = Mathf.Clamp01(initialStartPower01);
            administrativePower01 = Mathf.Clamp01(initialAdministrativePower01);
            powerTransitionSeconds = Mathf.Max(0f, powerSeconds);
            apertureTransitionSeconds = Mathf.Max(0f, apertureSeconds);
            smoothedStartPower01 = startPower01;
            smoothedAdministrativePower01 = administrativePower01;
            smoothedAperture01 = 0f;
            initialized = false;
            evidenceOverride = false;
            faultLatched = false;
            faultReason = null;
        }

        private void Start()
        {
            if (!driveOnStart)
                return;
            if (!TryInitialize(out string failure))
                LatchFault(failure);
        }

        private void LateUpdate()
        {
            if (!driveOnStart || faultLatched)
                return;
            if (!initialized && !TryInitialize(out string initializationFailure))
            {
                LatchFault(initializationFailure);
                return;
            }

            float targetAperture = ResolveTargetAperture();
            // A room compositor captures the production endpoint currently installed on its
            // switcher when it first installs private working atlases.  If a caller opens the
            // door and changes an endpoint target in the same frame, applying the normal power
            // interpolation first would create an intermediate base value before that capture.
            // That is neither a legal production endpoint nor a valid activation source.  Prime
            // both inactive compositors from the *installed* endpoints, activate from those
            // exact refs/index/ST values, then let the following frames interpolate toward the
            // new targets while the private maps are active.
            if (!evidenceOverride && targetAperture > ZeroApertureEpsilon &&
                (!startRoomCompositor.IsActive || !administrativeRoomCompositor.IsActive))
            {
                if (startRoomCompositor.IsActive != administrativeRoomCompositor.IsActive)
                {
                    LatchFault("Only one room compositor is active while opening the shared portal.");
                    return;
                }

                smoothedStartPower01 = PowerLevelTo01(startPowerSwitcher.CurrentPowerLevel);
                smoothedAdministrativePower01 = PowerLevelTo01(
                    administrativePowerSwitcher.CurrentPowerLevel);
                float activationDeltaTime = Mathf.Max(0f, Time.unscaledDeltaTime);
                smoothedAperture01 = MoveTowards01(
                    smoothedAperture01,
                    targetAperture,
                    activationDeltaTime,
                    apertureTransitionSeconds);

                if (!TryApplyCurrentState(out string activationFailure))
                    LatchFault(activationFailure);
                return;
            }

            if (targetAperture <= ZeroApertureEpsilon && !evidenceOverride)
            {
                // D0 is an exact parity boundary. Keep a closed portal on a production endpoint
                // instead of inventing an intermediate material/lightmap state.
                smoothedStartPower01 = RequireEndpointOrFault(startPower01, "Start target power");
                smoothedAdministrativePower01 = RequireEndpointOrFault(
                    administrativePower01,
                    "Administrative target power");
                smoothedAperture01 = 0f;
            }
            else
            {
                float dt = Mathf.Max(0f, Time.unscaledDeltaTime);
                smoothedStartPower01 = MoveTowards01(
                    smoothedStartPower01, startPower01, dt, powerTransitionSeconds);
                smoothedAdministrativePower01 = MoveTowards01(
                    smoothedAdministrativePower01, administrativePower01, dt,
                    powerTransitionSeconds);
                smoothedAperture01 = MoveTowards01(
                    smoothedAperture01, targetAperture, dt, apertureTransitionSeconds);
            }

            if (faultLatched)
                return;
            if (!TryApplyCurrentState(out string failure))
                LatchFault(failure);
        }

        public void SetStartPower01(float value01)
        {
            startPower01 = Mathf.Clamp01(value01);
        }

        public void SetAdministrativePower01(float value01)
        {
            administrativePower01 = Mathf.Clamp01(value01);
        }

        public void SetPower01(float startValue01, float administrativeValue01)
        {
            startPower01 = Mathf.Clamp01(startValue01);
            administrativePower01 = Mathf.Clamp01(administrativeValue01);
        }

        /// <summary>
        /// Deterministic evidence hook. The power matrix is endpoint-only by design, so this
        /// method rejects intermediate values rather than silently selecting P100 as the legacy
        /// switcher would do.
        /// </summary>
        public bool SetImmediateForEvidence(
            float requestedStartPower01,
            float requestedAdministrativePower01,
            float requestedAperture01,
            out string failure)
        {
            if (!TryGetEndpoint(requestedStartPower01, out _) ||
                !TryGetEndpoint(requestedAdministrativePower01, out _) ||
                !IsFinite01(requestedAperture01))
            {
                failure = "Evidence powers must be canonical P0/P100 endpoints and aperture must be finite [0,1].";
                return false;
            }

            if (!initialized && !TryInitialize(out failure))
                return false;
            if (faultLatched)
            {
                failure = "The baked-basis driver is fault-latched: " + faultReason;
                return false;
            }

            startPower01 = smoothedStartPower01 = Mathf.Clamp01(requestedStartPower01);
            administrativePower01 = smoothedAdministrativePower01 =
                Mathf.Clamp01(requestedAdministrativePower01);
            evidenceOverride = true;
            evidenceAperture01 = Mathf.Clamp01(requestedAperture01);
            smoothedAperture01 = evidenceAperture01;

            if (!TryApplyD0EndpointState(
                    smoothedStartPower01,
                    smoothedAdministrativePower01,
                    out failure))
            {
                LatchFault(failure);
                return false;
            }

            if (smoothedAperture01 <= ZeroApertureEpsilon || !adjacentTransportEnabled)
            {
                failure = null;
                return true;
            }

            if (!TryApplyOpenState(out failure))
            {
                LatchFault(failure);
                return false;
            }

            failure = null;
            return true;
        }

        public bool SetAdjacentEnabledForEvidence(bool enabled, out string failure)
        {
            if (!initialized && !TryInitialize(out failure))
                return false;
            if (faultLatched)
            {
                failure = "The baked-basis driver is fault-latched: " + faultReason;
                return false;
            }

            adjacentTransportEnabled = enabled;
            evidenceOverride = true;
            evidenceAperture01 = enabled ? ResolvePhysicalAperture() : 0f;
            smoothedAperture01 = evidenceAperture01;
            if (!enabled)
            {
                if (!TryApplyD0EndpointState(
                        RequireEndpointOrFault(startPower01, "Start target power"),
                        RequireEndpointOrFault(administrativePower01,
                            "Administrative target power"),
                        out failure))
                {
                    LatchFault(failure);
                    return false;
                }

                failure = null;
                return true;
            }

            return TryApplyCurrentState(out failure);
        }

        public void ClearEvidenceOverride()
        {
            evidenceOverride = false;
        }

        public bool TryRestoreOriginal(out string failure)
        {
            adjacentTransportEnabled = false;
            evidenceOverride = true;
            evidenceAperture01 = 0f;
            smoothedAperture01 = 0f;
            if (!TryZeroIncoming(out failure))
            {
                LatchFault(failure);
                return false;
            }

            bool startRestored = TryRestoreCompositor(startRoomCompositor, out string startFailure);
            bool administrativeRestored = TryRestoreCompositor(
                administrativeRoomCompositor, out string administrativeFailure);
            if (!startRestored || !administrativeRestored)
            {
                failure = "Could not restore both room compositors. Start=" + startFailure +
                          " Administrative=" + administrativeFailure;
                LatchFault(failure);
                return false;
            }

            failure = null;
            return true;
        }

        public bool TryValidateConfiguration(out string failure)
        {
            if (string.IsNullOrWhiteSpace(connectionId) || startRoomCompositor == null ||
                administrativeRoomCompositor == null || startRoomCompositor == administrativeRoomCompositor ||
                startRoomBasis == null || administrativeRoomBasis == null ||
                startDoorId == null || administrativeDoorId == null ||
                string.IsNullOrWhiteSpace(startDoorId) ||
                string.IsNullOrWhiteSpace(administrativeDoorId) ||
                doorAngleSource == null || !doorAngleSource.IsConfigured ||
                startPowerSwitcher == null || administrativePowerSwitcher == null)
            {
                failure = "The connection driver has an incomplete stable room/door/switcher contract.";
                return false;
            }

            bool startBasisValid = startRoomBasis.TryValidateDefinition(out string startBasisFailure);
            bool administrativeBasisValid = administrativeRoomBasis.TryValidateDefinition(
                out string administrativeBasisFailure);
            if (startRoomCompositor.RoomBasis != startRoomBasis ||
                administrativeRoomCompositor.RoomBasis != administrativeRoomBasis ||
                !startBasisValid || !administrativeBasisValid)
            {
                failure = "Room-basis definition mismatch. Start=" + startBasisFailure +
                          " Administrative=" + administrativeBasisFailure;
                return false;
            }

            if (!startRoomBasis.TryGetReceiverDoor(startDoorId, out _) ||
                !startRoomBasis.TryGetSourceDoor(startDoorId, out _) ||
                !administrativeRoomBasis.TryGetReceiverDoor(administrativeDoorId, out _) ||
                !administrativeRoomBasis.TryGetSourceDoor(administrativeDoorId, out _))
            {
                failure = "One room basis does not contain the required source and receiver doorway ids.";
                return false;
            }

            if (!IsFinite01(startPower01) || !IsFinite01(administrativePower01) ||
                !IsFinite(powerTransitionSeconds) || powerTransitionSeconds < 0f ||
                !IsFinite(apertureTransitionSeconds) || apertureTransitionSeconds < 0f)
            {
                failure = "Connection driver scalars are non-finite or out of range.";
                return false;
            }

            failure = null;
            return true;
        }

        private bool TryInitialize(out string failure)
        {
            if (initialized)
            {
                failure = null;
                return true;
            }

            if (!TryValidateConfiguration(out failure))
                return false;

            startPower01 = smoothedStartPower01 =
                PowerLevelTo01(startPowerSwitcher.CurrentPowerLevel);
            administrativePower01 = smoothedAdministrativePower01 =
                PowerLevelTo01(administrativePowerSwitcher.CurrentPowerLevel);
            smoothedAperture01 = ResolveTargetAperture();
            initialized = true;
            if (smoothedAperture01 <= ZeroApertureEpsilon)
                return TryApplyD0EndpointState(
                    smoothedStartPower01, smoothedAdministrativePower01, out failure);
            return TryApplyOpenState(out failure);
        }

        private bool TryApplyCurrentState(out string failure)
        {
            if (!adjacentTransportEnabled || smoothedAperture01 <= ZeroApertureEpsilon)
            {
                if (!TryGetEndpoint(smoothedStartPower01, out _) ||
                    !TryGetEndpoint(smoothedAdministrativePower01, out _))
                {
                    failure = "D0 is an exact P0/P100 bypass; an intermediate base power may " +
                              "only exist while transfer aperture is non-zero.";
                    return false;
                }

                return TryApplyD0EndpointState(
                    smoothedStartPower01, smoothedAdministrativePower01, out failure);
            }

            return TryApplyOpenState(out failure);
        }

        private bool TryApplyOpenState(out string failure)
        {
            // An inactive compositor can only establish its private working atlas from the
            // production endpoint currently installed by the switcher. Evidence always calls
            // TryApplyD0EndpointState first; live startup does the same when the door is closed.
            if (!startRoomCompositor.IsActive &&
                !CurrentSwitcherMatches(startPowerSwitcher, smoothedStartPower01))
            {
                failure = "Start compositor cannot activate from a non-canonical production endpoint.";
                return false;
            }

            if (!administrativeRoomCompositor.IsActive &&
                !CurrentSwitcherMatches(administrativePowerSwitcher,
                    smoothedAdministrativePower01))
            {
                failure = "Administrative compositor cannot activate from a non-canonical production endpoint.";
                return false;
            }

            if (!startRoomCompositor.TrySetBasePower(smoothedStartPower01, out failure) ||
                !administrativeRoomCompositor.TrySetBasePower(
                    smoothedAdministrativePower01, out failure))
            {
                return false;
            }

            if (!startRoomCompositor.TrySetIncomingState(
                    connectionId + "/AdministrativeToStart",
                    smoothedAdministrativePower01,
                    smoothedAperture01,
                    adjacentTransportEnabled,
                    out failure) ||
                !administrativeRoomCompositor.TrySetIncomingState(
                    connectionId + "/StartToAdministrative",
                    smoothedStartPower01,
                    smoothedAperture01,
                    adjacentTransportEnabled,
                    out failure))
            {
                return false;
            }

            if (startRoomCompositor.IsFaultLatched || administrativeRoomCompositor.IsFaultLatched)
            {
                failure = "A room compositor faulted. Start=" + startRoomCompositor.FaultReason +
                          " Administrative=" + administrativeRoomCompositor.FaultReason;
                return false;
            }

            failure = null;
            return true;
        }

        private bool TryApplyD0EndpointState(
            float requestedStartPower,
            float requestedAdministrativePower,
            out string failure)
        {
            if (!TryGetEndpoint(requestedStartPower, out DungeonTileLightmapSwitcher.PowerLevel startEndpoint) ||
                !TryGetEndpoint(requestedAdministrativePower,
                    out DungeonTileLightmapSwitcher.PowerLevel administrativeEndpoint))
            {
                failure = "D0 endpoint synchronization requires exact P0/P100 powers.";
                return false;
            }

            if (!TryZeroIncoming(out failure))
                return false;

            bool startDeactivated = TryDeactivateToCapturedEndpoint(
                startRoomCompositor, out _, out string startFailure);
            bool administrativeDeactivated = TryDeactivateToCapturedEndpoint(
                administrativeRoomCompositor, out _, out string administrativeFailure);
            if (!startDeactivated || !administrativeDeactivated)
            {
                failure = "D0 captured-endpoint restore failed. Start=" + startFailure +
                          " Administrative=" + administrativeFailure;
                return false;
            }

            try
            {
                startPowerSwitcher.SetPowerLevel(startEndpoint);
                administrativePowerSwitcher.SetPowerLevel(administrativeEndpoint);
            }
            catch (Exception exception)
            {
                failure = "Production endpoint switcher threw during D0 synchronization: " +
                          exception.GetType().Name + ": " + exception.Message;
                return false;
            }

            if (!startRoomCompositor.TrySetBasePower(requestedStartPower, out failure) ||
                !administrativeRoomCompositor.TrySetBasePower(requestedAdministrativePower,
                    out failure) ||
                !TryZeroIncoming(out failure))
            {
                return false;
            }

            failure = null;
            return true;
        }

        private bool TryZeroIncoming(out string failure)
        {
            if (!startRoomCompositor.TrySetIncomingState(
                    connectionId + "/AdministrativeToStart",
                    smoothedAdministrativePower01,
                    0f,
                    false,
                    out failure) ||
                !administrativeRoomCompositor.TrySetIncomingState(
                    connectionId + "/StartToAdministrative",
                    smoothedStartPower01,
                    0f,
                    false,
                    out failure))
            {
                return false;
            }

            failure = null;
            return true;
        }

        private static bool TryDeactivateToCapturedEndpoint(
            DungeonPortalBakedBasisRoomCompositor compositor,
            out int endpoint,
            out string failure)
        {
            endpoint = -1;
            if (compositor == null)
            {
                failure = "Room compositor is missing.";
                return false;
            }

            if (!compositor.IsActive)
            {
                failure = null;
                return true;
            }

            return compositor.TryDeactivateToCapturedEndpoint(out endpoint, out failure);
        }

        private static bool TryRestoreCompositor(
            DungeonPortalBakedBasisRoomCompositor compositor,
            out string failure)
        {
            if (compositor == null)
            {
                failure = "Room compositor is missing.";
                return false;
            }

            if (!compositor.IsActive)
            {
                failure = null;
                return true;
            }

            return compositor.TryDeactivateToCapturedEndpoint(out _, out failure);
        }

        private float ResolveTargetAperture()
        {
            if (evidenceOverride)
                return adjacentTransportEnabled ? Mathf.Clamp01(evidenceAperture01) : 0f;
            return adjacentTransportEnabled ? ResolvePhysicalAperture() : 0f;
        }

        private float ResolvePhysicalAperture()
        {
            if (doorAngleSource == null)
                return 0f;
            doorAngleSource.EvaluateNow();
            return Mathf.Clamp01(doorAngleSource.ApertureFraction);
        }

        private float RequireEndpointOrFault(float value, string label)
        {
            if (TryGetEndpoint(value, out _))
                return Mathf.Clamp01(value);
            LatchFault(label + " must be exactly P0 or P100 while the door is closed.");
            return Mathf.Clamp01(value);
        }

        private static bool CurrentSwitcherMatches(
            DungeonTileLightmapSwitcher switcher,
            float power01)
        {
            if (switcher == null || !TryGetEndpoint(power01, out var endpoint))
                return false;
            return switcher.CurrentPowerLevel == endpoint;
        }

        private static float MoveTowards01(float current, float target, float deltaTime, float seconds)
        {
            if (!IsFinite01(current) || !IsFinite01(target))
                return 0f;
            if (seconds <= 0f)
                return target;
            return Mathf.MoveTowards(current, target, Mathf.Max(0f, deltaTime) / seconds);
        }

        public static float SmoothTowards01ForTest(
            float current,
            float target,
            float deltaTime,
            float seconds)
        {
            return MoveTowards01(current, target, deltaTime, seconds);
        }

        public static bool TryGetEndpoint(
            float value01,
            out DungeonTileLightmapSwitcher.PowerLevel endpoint)
        {
            if (value01 <= ZeroApertureEpsilon)
            {
                endpoint = DungeonTileLightmapSwitcher.PowerLevel.P0;
                return true;
            }

            if (value01 >= 1f - ZeroApertureEpsilon)
            {
                endpoint = DungeonTileLightmapSwitcher.PowerLevel.P100;
                return true;
            }

            endpoint = DungeonTileLightmapSwitcher.PowerLevel.P0;
            return false;
        }

        private static float PowerLevelTo01(DungeonTileLightmapSwitcher.PowerLevel level)
        {
            return level == DungeonTileLightmapSwitcher.PowerLevel.P100 ? 1f : 0f;
        }

        private static bool IsFinite01(float value)
        {
            return IsFinite(value) && value >= 0f && value <= 1f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void LatchFault(string reason)
        {
            if (faultLatched)
                return;

            faultLatched = true;
            faultReason = string.IsNullOrWhiteSpace(reason)
                ? "Unknown baked-basis driver failure."
                : reason;
            adjacentTransportEnabled = false;
            evidenceOverride = true;
            evidenceAperture01 = 0f;
            smoothedAperture01 = 0f;

            // Best effort only: never create substitute lighting after a contract failure.
            TryZeroIncoming(out _);
            TryRestoreCompositor(startRoomCompositor, out _);
            TryRestoreCompositor(administrativeRoomCompositor, out _);
            Debug.LogError("[DungeonPortalBakedBasisConnectionDriver] '" + name +
                           "' disabled fail-closed: " + faultReason, this);
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
                TryRestoreOriginal(out _);
        }

        private void OnValidate()
        {
            startPower01 = Mathf.Clamp01(startPower01);
            administrativePower01 = Mathf.Clamp01(administrativePower01);
            if (!IsFinite(powerTransitionSeconds) || powerTransitionSeconds < 0f)
                powerTransitionSeconds = 0.35f;
            if (!IsFinite(apertureTransitionSeconds) || apertureTransitionSeconds < 0f)
                apertureTransitionSeconds = 0.25f;
        }
    }
}
