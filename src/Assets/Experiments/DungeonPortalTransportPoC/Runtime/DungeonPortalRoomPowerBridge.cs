using System;
using System.Reflection;
using UnityEngine;

namespace DungeonPortalTransportPoC
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class DungeonPortalRoomPowerBridge : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The production DungeonTileLightmapSwitcher kept as a MonoBehaviour so this isolated assembly does not depend on Assembly-CSharp.")]
        private MonoBehaviour productionLightmapSwitcher;

        [SerializeField] private DungeonPortalPowerEnvelope powerEnvelope;

        [SerializeField]
        [Tooltip("Only this connection may activate the bridge after it owns both endpoints.")]
        private DungeonPortalTransportConnection connectionOwner;

        [SerializeField, Range(0.05f, 0.95f)]
        [Tooltip("Static lightmaps and emission materials still have discrete P0/P100 endpoints. They switch once while proxy and reflection lighting continue to fade.")]
        private float discreteBaseSwitchThreshold = 0.5f;

        private PropertyInfo currentPowerLevelProperty;
        private MethodInfo setPowerLevelMethod;
        private MethodInfo setPowerPercentMethod;
        private DungeonPortalPowerEnvelope subscribedEnvelope;
        private object capturedPowerLevel;
        private bool connectionActivationGranted;
        private bool originalPowerCaptured;
        private bool subscribed;
        private bool bindingErrorReported;
        private int lastAppliedDiscreteState = -1;

        public bool IsConfigured =>
            connectionOwner != null &&
            productionLightmapSwitcher != null &&
            powerEnvelope != null &&
            ResolveBinding(out _);

        public bool IsConnectionActivated => connectionActivationGranted;

        public void Configure(
            MonoBehaviour lightmapSwitcher,
            DungeonPortalPowerEnvelope roomPower,
            float switchThreshold = 0.5f)
        {
            DeactivateCore();
            productionLightmapSwitcher = lightmapSwitcher;
            powerEnvelope = roomPower;
            discreteBaseSwitchThreshold = Mathf.Clamp(switchThreshold, 0.05f, 0.95f);
            ClearBindingCache();
            bindingErrorReported = false;

            // Authoring and unowned runtime instances are serialized disabled. The
            // connection is the only code path allowed to enable this component.
            if (!Application.isPlaying || connectionOwner == null)
                enabled = false;
        }

        private void Awake()
        {
            if (!connectionActivationGranted)
                enabled = false;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            if (!connectionActivationGranted || connectionOwner == null ||
                !connectionOwner.HasClaimedEndpoints)
            {
                enabled = false;
            }
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
                DeactivateCore();
        }

        private void OnDestroy()
        {
            DeactivateCore();
        }

        internal void SetConnectionOwner(DungeonPortalTransportConnection owner)
        {
            if (connectionOwner == owner)
                return;

            DeactivateCore();
            connectionOwner = owner;
            if (!Application.isPlaying || owner == null)
                enabled = false;
        }

        internal bool TryActivate(
            DungeonPortalTransportConnection requester,
            out string failure)
        {
            if (requester == null || requester != connectionOwner)
            {
                failure = "The requesting connection does not own this power bridge.";
                return false;
            }

            if (!requester.HasClaimedEndpoints)
            {
                failure = "The owning connection has not claimed both endpoints.";
                return false;
            }

            if (connectionActivationGranted)
            {
                failure = null;
                return true;
            }

            if (!ResolveBinding(out failure))
                return false;

            if (!TryCaptureOriginalPowerLevel(out failure))
                return false;

            connectionActivationGranted = true;
            Subscribe();
            if (!TryApplyPower(
                    powerEnvelope != null ? powerEnvelope.Power01 : 0f,
                    true,
                    out failure))
            {
                DeactivateCore();
                return false;
            }

            enabled = true;
            bindingErrorReported = false;
            return true;
        }

        internal void Deactivate(DungeonPortalTransportConnection requester)
        {
            if (requester == null || requester != connectionOwner)
                return;

            DeactivateCore();
            if (enabled)
                enabled = false;
        }

        private void Subscribe()
        {
            if (subscribed || powerEnvelope == null || !connectionActivationGranted)
                return;

            subscribedEnvelope = powerEnvelope;
            subscribedEnvelope.PowerChanged += HandlePowerChanged;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
                return;

            if (subscribedEnvelope != null)
                subscribedEnvelope.PowerChanged -= HandlePowerChanged;

            subscribedEnvelope = null;
            subscribed = false;
        }

        private void HandlePowerChanged(float power01)
        {
            if (!connectionActivationGranted || connectionOwner == null ||
                !connectionOwner.HasClaimedEndpoints)
            {
                DeactivateCore();
                return;
            }

            if (TryApplyPower(power01, false, out string failure))
                return;

            FailClosed(failure);
        }

        private bool TryApplyPower(float power01, bool force, out string failure)
        {
            if (!connectionActivationGranted || !originalPowerCaptured)
            {
                failure = "No connection-owned original power state was captured.";
                return false;
            }

            if (!ResolveBinding(out failure))
                return false;

            int discreteState = Mathf.Clamp01(power01) >= discreteBaseSwitchThreshold ? 1 : 0;
            if (!force && discreteState == lastAppliedDiscreteState)
            {
                failure = null;
                return true;
            }

            try
            {
                setPowerPercentMethod.Invoke(
                    productionLightmapSwitcher,
                    new object[] { discreteState == 1 ? 1f : 0f });
                lastAppliedDiscreteState = discreteState;
                bindingErrorReported = false;
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                Exception root = UnwrapException(exception);
                failure =
                    $"Production power endpoint call failed: " +
                    $"{root.GetType().Name}: {root.Message}";
                return false;
            }
        }

        private bool ResolveBinding(out string failure)
        {
            if (productionLightmapSwitcher == null || powerEnvelope == null)
            {
                failure = "A production lightmap switcher and room power envelope are required.";
                return false;
            }

            if (setPowerPercentMethod != null && currentPowerLevelProperty != null &&
                setPowerLevelMethod != null)
            {
                failure = null;
                return true;
            }

            Type switcherType = productionLightmapSwitcher.GetType();
            setPowerPercentMethod = switcherType.GetMethod(
                "SetPowerPercent",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(float) },
                null);
            currentPowerLevelProperty = switcherType.GetProperty(
                "CurrentPowerLevel",
                BindingFlags.Instance | BindingFlags.Public);

            Type powerLevelType = currentPowerLevelProperty != null &&
                                  currentPowerLevelProperty.CanRead
                ? currentPowerLevelProperty.PropertyType
                : null;
            setPowerLevelMethod = powerLevelType != null
                ? switcherType.GetMethod(
                    "SetPowerLevel",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { powerLevelType },
                    null)
                : null;

            if (setPowerPercentMethod == null || currentPowerLevelProperty == null ||
                !currentPowerLevelProperty.CanRead || setPowerLevelMethod == null)
            {
                ClearBindingCache();
                failure =
                    "The production switcher must expose SetPowerPercent(float), " +
                    "readable CurrentPowerLevel, and SetPowerLevel(CurrentPowerLevelType).";
                return false;
            }

            failure = null;
            return true;
        }

        private bool TryCaptureOriginalPowerLevel(out string failure)
        {
            if (originalPowerCaptured)
            {
                failure = null;
                return true;
            }

            try
            {
                capturedPowerLevel = currentPowerLevelProperty.GetValue(
                    productionLightmapSwitcher,
                    null);
                if (capturedPowerLevel == null)
                {
                    failure = "CurrentPowerLevel returned null; no production state was changed.";
                    return false;
                }

                originalPowerCaptured = true;
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                Exception root = UnwrapException(exception);
                failure =
                    $"Could not capture CurrentPowerLevel: " +
                    $"{root.GetType().Name}: {root.Message}";
                return false;
            }
        }

        private bool TryRestoreOriginalPowerLevel(out string failure)
        {
            if (!originalPowerCaptured)
            {
                failure = null;
                return true;
            }

            try
            {
                setPowerLevelMethod.Invoke(
                    productionLightmapSwitcher,
                    new[] { capturedPowerLevel });
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                Exception root = UnwrapException(exception);
                failure =
                    $"Could not restore captured CurrentPowerLevel: " +
                    $"{root.GetType().Name}: {root.Message}";
                return false;
            }
            finally
            {
                capturedPowerLevel = null;
                originalPowerCaptured = false;
            }
        }

        private void DeactivateCore()
        {
            Unsubscribe();
            bool restored = TryRestoreOriginalPowerLevel(out string restoreFailure);
            connectionActivationGranted = false;
            lastAppliedDiscreteState = -1;

            if (!restored && !bindingErrorReported)
            {
                bindingErrorReported = true;
                Debug.LogError(
                    $"[{nameof(DungeonPortalRoomPowerBridge)}] {restoreFailure}",
                    this);
            }
        }

        private void FailClosed(string reason)
        {
            DungeonPortalTransportConnection owner = connectionOwner;
            DeactivateCore();
            if (enabled)
                enabled = false;

            if (!bindingErrorReported)
            {
                bindingErrorReported = true;
                Debug.LogError(
                    $"[{nameof(DungeonPortalRoomPowerBridge)}] Disabled and restored " +
                    $"the captured production power state: {reason}",
                    this);
            }

            owner?.NotifyScopedEffectFailure(this, reason);
        }

        private void ClearBindingCache()
        {
            currentPowerLevelProperty = null;
            setPowerLevelMethod = null;
            setPowerPercentMethod = null;
        }

        private static Exception UnwrapException(Exception exception)
        {
            return exception is TargetInvocationException invocationException &&
                   invocationException.InnerException != null
                ? invocationException.InnerException
                : exception;
        }

        private void OnValidate()
        {
            discreteBaseSwitchThreshold = Mathf.Clamp(
                discreteBaseSwitchThreshold,
                0.05f,
                0.95f);
        }
    }
}
