using System;
using UnityEngine;

namespace DungeonPortalTransportPoC
{
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class DungeonPortalTransportConnection : MonoBehaviour
    {
        [SerializeField] private DungeonPortalEndpoint endpointA;
        [SerializeField] private DungeonPortalEndpoint endpointB;
        [SerializeField] private DungeonPortalDoorAngleSource doorAngleSource;
        [SerializeField] private DungeonPortalRoomPowerBridge[] scopedPowerBridges =
            Array.Empty<DungeonPortalRoomPowerBridge>();
        [SerializeField] private DungeonPortalRoomReflectionBlend[] scopedReflectionBlends =
            Array.Empty<DungeonPortalRoomReflectionBlend>();

        private bool endpointsClaimed;
        private bool scopedEffectsActive;
        private bool bindingErrorReported;
        private bool faultLatched;

        internal bool HasClaimedEndpoints =>
            endpointsClaimed &&
            isActiveAndEnabled &&
            Application.isPlaying &&
            endpointA != null &&
            endpointB != null &&
            endpointA.IsConnectionActive &&
            endpointB.IsConnectionActive;

        public void Configure(
            DungeonPortalEndpoint firstEndpoint,
            DungeonPortalEndpoint secondEndpoint,
            DungeonPortalDoorAngleSource movingDoor)
        {
            ClearFaultLatch();
            DeactivateScopedEffects();
            ReleaseEndpoints();
            endpointA = firstEndpoint;
            endpointB = secondEndpoint;
            doorAngleSource = movingDoor;

            if (Application.isPlaying && isActiveAndEnabled)
                TryClaimEndpoints();
        }

        public void ConfigureScopedEffects(
            DungeonPortalRoomPowerBridge[] powerBridges,
            DungeonPortalRoomReflectionBlend[] reflectionBlends)
        {
            ClearFaultLatch();
            DeactivateScopedEffects();

            scopedPowerBridges = CopyWithoutDuplicates(powerBridges);
            scopedReflectionBlends = CopyWithoutDuplicates(reflectionBlends);
            BindScopedEffectsToThisConnection();

            if (!Application.isPlaying)
            {
                DisableScopedEffectComponents();
                return;
            }

            if (HasClaimedEndpoints && !TryActivateScopedEffects(out string failure))
                FailClosed(failure);
        }

        public void ConfigureReflectionBlends(
            DungeonPortalRoomReflectionBlend[] reflectionBlends)
        {
            ConfigureScopedEffects(scopedPowerBridges, reflectionBlends);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            ClearFaultLatch();
            DeactivateScopedEffects();
            if (!TryClaimEndpoints())
                DisableScopedEffectComponents();
        }

        private void OnDisable()
        {
            DeactivateScopedEffects();
            ReleaseEndpoints();
        }

        private void LateUpdate()
        {
            if (faultLatched)
                return;

            if (endpointsClaimed && !HasClaimedEndpoints)
            {
                FailClosed("A claimed endpoint became inactive or invalid.");
                return;
            }

            if (doorAngleSource == null || !doorAngleSource.IsConfigured)
            {
                DeactivateScopedEffects();
                ReleaseEndpoints();
                ReportBindingErrorOnce();
                return;
            }

            if (!endpointsClaimed && !TryClaimEndpoints())
                return;

            if (!scopedEffectsActive && !TryActivateScopedEffects(out string failure))
            {
                FailClosed(failure);
                return;
            }

            if (scopedEffectsActive && !AreScopedEffectsStillActive(out failure))
            {
                FailClosed(failure);
                return;
            }

            doorAngleSource.EvaluateNow();
            endpointA.ApplyOutgoingPower();
            endpointB.ApplyOutgoingPower();

            float effectiveAperture = doorAngleSource.ApertureFraction;

            endpointA.ApplyIncomingBounce(
                endpointB.EvaluateOutgoingRadiance(),
                effectiveAperture);
            endpointB.ApplyIncomingBounce(
                endpointA.EvaluateOutgoingRadiance(),
                effectiveAperture);
        }

        private bool TryClaimEndpoints()
        {
            if (endpointA == null || endpointB == null || endpointA == endpointB ||
                doorAngleSource == null || !doorAngleSource.IsConfigured ||
                !endpointA.IsConfigured || !endpointB.IsConfigured)
            {
                ReportBindingErrorOnce();
                return false;
            }

            if (!endpointA.TryClaim(this))
            {
                ReportBindingErrorOnce();
                return false;
            }

            if (!endpointB.TryClaim(this))
            {
                endpointA.Release(this);
                ReportBindingErrorOnce();
                return false;
            }

            endpointsClaimed = true;
            if (!TryActivateScopedEffects(out string activationFailure))
            {
                FailClosed(activationFailure);
                return false;
            }

            bindingErrorReported = false;
            return true;
        }

        private void ReleaseEndpoints()
        {
            if (endpointA != null)
                endpointA.Release(this);
            if (endpointB != null)
                endpointB.Release(this);

            endpointsClaimed = false;
        }

        internal void NotifyScopedEffectFailure(Component effect, string reason)
        {
            if (!Application.isPlaying || !HasClaimedEndpoints)
                return;

            string effectLabel = effect != null ? effect.name : "<missing>";
            FailClosed($"Scoped effect '{effectLabel}' failed: {reason}");
        }

        private bool TryActivateScopedEffects(out string failure)
        {
            if (scopedEffectsActive)
            {
                failure = null;
                return true;
            }

            if (!HasClaimedEndpoints)
            {
                failure = "Both endpoints must be claimed before scoped room effects can activate.";
                return false;
            }

            BindScopedEffectsToThisConnection();

            for (int i = 0; i < scopedPowerBridges.Length; i++)
            {
                DungeonPortalRoomPowerBridge bridge = scopedPowerBridges[i];
                if (bridge == null)
                {
                    failure = $"Scoped power bridge index {i} is missing.";
                    DeactivateScopedEffects();
                    return false;
                }

                if (!bridge.TryActivate(this, out failure))
                {
                    failure = $"Power bridge '{bridge.name}' could not activate: {failure}";
                    DeactivateScopedEffects();
                    return false;
                }
            }

            for (int i = 0; i < scopedReflectionBlends.Length; i++)
            {
                DungeonPortalRoomReflectionBlend blend = scopedReflectionBlends[i];
                if (blend == null)
                {
                    failure = $"Scoped reflection blend index {i} is missing.";
                    DeactivateScopedEffects();
                    return false;
                }

                if (!blend.TryActivate(this, out failure))
                {
                    failure = $"Reflection blend '{blend.name}' could not activate: {failure}";
                    DeactivateScopedEffects();
                    return false;
                }
            }

            scopedEffectsActive = true;
            failure = null;
            return true;
        }

        private void DeactivateScopedEffects()
        {
            // Reverse activation order. Reflection suppression is restored before the
            // production switcher restores its exact captured PowerLevel.
            for (int i = scopedReflectionBlends.Length - 1; i >= 0; i--)
            {
                DungeonPortalRoomReflectionBlend blend = scopedReflectionBlends[i];
                if (blend != null)
                    blend.Deactivate(this);
            }

            for (int i = scopedPowerBridges.Length - 1; i >= 0; i--)
            {
                DungeonPortalRoomPowerBridge bridge = scopedPowerBridges[i];
                if (bridge != null)
                    bridge.Deactivate(this);
            }

            scopedEffectsActive = false;
        }

        private void BindScopedEffectsToThisConnection()
        {
            for (int i = 0; i < scopedPowerBridges.Length; i++)
            {
                if (scopedPowerBridges[i] != null)
                    scopedPowerBridges[i].SetConnectionOwner(this);
            }

            for (int i = 0; i < scopedReflectionBlends.Length; i++)
            {
                if (scopedReflectionBlends[i] != null)
                    scopedReflectionBlends[i].SetConnectionOwner(this);
            }
        }

        private void DisableScopedEffectComponents()
        {
            for (int i = 0; i < scopedPowerBridges.Length; i++)
            {
                if (scopedPowerBridges[i] != null)
                    scopedPowerBridges[i].enabled = false;
            }

            for (int i = 0; i < scopedReflectionBlends.Length; i++)
            {
                if (scopedReflectionBlends[i] != null)
                    scopedReflectionBlends[i].enabled = false;
            }
        }

        private bool AreScopedEffectsStillActive(out string failure)
        {
            for (int i = 0; i < scopedPowerBridges.Length; i++)
            {
                DungeonPortalRoomPowerBridge bridge = scopedPowerBridges[i];
                if (bridge == null || !bridge.IsConnectionActivated || !bridge.isActiveAndEnabled)
                {
                    failure = $"Scoped power bridge index {i} is no longer active.";
                    return false;
                }
            }

            for (int i = 0; i < scopedReflectionBlends.Length; i++)
            {
                DungeonPortalRoomReflectionBlend blend = scopedReflectionBlends[i];
                if (blend == null || !blend.IsConnectionActivated || !blend.isActiveAndEnabled)
                {
                    failure = $"Scoped reflection blend index {i} is no longer active.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private void FailClosed(string reason)
        {
            faultLatched = true;
            DeactivateScopedEffects();
            ReleaseEndpoints();
            DisableScopedEffectComponents();
            ReportBindingErrorOnce(reason);
        }

        private void ClearFaultLatch()
        {
            faultLatched = false;
            bindingErrorReported = false;
        }

        private static T[] CopyWithoutDuplicates<T>(T[] source)
            where T : UnityEngine.Object
        {
            if (source == null || source.Length == 0)
                return Array.Empty<T>();

            var result = new T[source.Length];
            int count = 0;
            for (int i = 0; i < source.Length; i++)
            {
                T candidate = source[i];
                if (candidate == null)
                    continue;

                bool duplicate = false;
                for (int existingIndex = 0; existingIndex < count; existingIndex++)
                {
                    if (result[existingIndex] == candidate)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                    result[count++] = candidate;
            }

            if (count == result.Length)
                return result;

            Array.Resize(ref result, count);
            return result;
        }

        private void ReportBindingErrorOnce(string detail = null)
        {
            if (bindingErrorReported || !Application.isPlaying)
                return;

            bindingErrorReported = true;
            Debug.LogError(
                "DungeonPortalTransportConnection is incomplete or an endpoint is already owned. " +
                "Transport remains disabled (fail closed)." +
                (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" Detail: {detail}"),
                this);
        }
    }
}
