using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonPortalTransportPoC
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ReflectionProbe))]
    public sealed class DungeonPortalRoomReflectionBlend : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("P0 residual and P100 cubemaps baked for this PoC-owned probe.")]
        private DungeonPortalRoomReflectionProfile profile;

        [SerializeField]
        [Tooltip("Continuous room power source. This component subscribes only while it is active and valid.")]
        private DungeonPortalPowerEnvelope powerEnvelope;

        [SerializeField]
        [Tooltip("Only this connection may activate the blend after it owns both endpoints.")]
        private DungeonPortalTransportConnection connectionOwner;

        [SerializeField]
        [Tooltip("Production probes temporarily suppressed only while the connection owns this blend.")]
        private ReflectionProbe[] productionProbesToSuppress = Array.Empty<ReflectionProbe>();

        private ReflectionProbe ownedProbe;
        private DungeonPortalPowerEnvelope subscribedEnvelope;
        private RenderTexture blendedCubemap;
        private bool initialized;
        private bool subscribed;
        private bool connectionActivationGranted;
        private bool originalStateCaptured;
        private bool originalProbeEnabled;
        private ReflectionProbeMode originalProbeMode;
        private Texture originalCustomBakedTexture;
        private float originalProbeIntensity;
        private bool[] originalProductionProbeEnabledStates = Array.Empty<bool>();
        private bool productionProbeStatesCaptured;
        private float lastAppliedPower = -1f;

        public bool IsInitialized => initialized;
        public RenderTexture BlendedCubemap => blendedCubemap;
        public bool IsConnectionActivated => connectionActivationGranted;

        public void Configure(
            DungeonPortalRoomReflectionProfile roomProfile,
            DungeonPortalPowerEnvelope roomPower)
        {
            Configure(
                roomProfile,
                roomPower,
                Array.Empty<ReflectionProbe>());
        }

        public void Configure(
            DungeonPortalRoomReflectionProfile roomProfile,
            DungeonPortalPowerEnvelope roomPower,
            ReflectionProbe[] probesToSuppress)
        {
            TearDown();

            profile = roomProfile;
            powerEnvelope = roomPower;
            productionProbesToSuppress = CopyWithoutDuplicates(probesToSuppress);

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
                TearDown();
        }

        private void OnDestroy()
        {
            TearDown();
        }

        internal void SetConnectionOwner(DungeonPortalTransportConnection owner)
        {
            if (connectionOwner == owner)
                return;

            TearDown();
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
                failure = "The requesting connection does not own this reflection blend.";
                return false;
            }

            if (!requester.HasClaimedEndpoints)
            {
                failure = "The owning connection has not claimed both endpoints.";
                return false;
            }

            if (initialized && connectionActivationGranted)
            {
                failure = null;
                return true;
            }

            connectionActivationGranted = true;
            if (!TryInitialize(out failure))
            {
                TearDown();
                return false;
            }

            enabled = true;
            return true;
        }

        internal void Deactivate(DungeonPortalTransportConnection requester)
        {
            if (requester == null || requester != connectionOwner)
                return;

            TearDown();
            if (enabled)
                enabled = false;
        }

        private bool TryInitialize(out string failure)
        {
            if (initialized)
            {
                failure = null;
                return true;
            }

            if (!TryValidateConfiguration(out string error))
            {
                failure = error;
                return false;
            }

            RenderTexture candidate = CreateBlendTarget(profile.Resolution);
            if (candidate == null)
            {
                failure = "Could not allocate the reusable HDR cubemap RenderTexture.";
                return false;
            }

            float initialPower = Mathf.Clamp01(powerEnvelope.Power01);
            bool blendSucceeded;
            try
            {
                blendSucceeded = ReflectionProbe.BlendCubemap(
                    profile.Power0ResidualCubemap,
                    profile.Power100Cubemap,
                    initialPower,
                    candidate);
            }
            catch (Exception exception)
            {
                ReleaseBlendTarget(candidate);
                failure =
                    $"Initial cubemap blend threw {exception.GetType().Name}: {exception.Message}";
                return false;
            }

            if (!blendSucceeded)
            {
                ReleaseBlendTarget(candidate);
                failure =
                    "ReflectionProbe.BlendCubemap rejected the configured textures or render target.";
                return false;
            }

            CaptureOriginalProbeState();
            CaptureProductionProbeStates();
            blendedCubemap = candidate;

            ownedProbe.mode = ReflectionProbeMode.Custom;
            ownedProbe.customBakedTexture = blendedCubemap;
            ownedProbe.intensity = profile.FixedProbeIntensity;
            ownedProbe.enabled = true;
            SuppressProductionProbes();

            lastAppliedPower = initialPower;
            initialized = true;
            Subscribe();
            failure = null;
            return true;
        }

        private bool TryValidateConfiguration(out string error)
        {
            if (!connectionActivationGranted || connectionOwner == null ||
                !connectionOwner.HasClaimedEndpoints)
            {
                error = "A connection that owns both endpoints has not activated this blend.";
                return false;
            }

            ReflectionProbe[] localProbes = GetComponents<ReflectionProbe>();
            if (localProbes.Length != 1)
            {
                error = $"Exactly one PoC-owned ReflectionProbe must be attached to this GameObject; found {localProbes.Length}.";
                return false;
            }

            ownedProbe = localProbes[0];

            if (profile == null)
            {
                error = "The room reflection profile is not assigned.";
                return false;
            }

            if (!profile.TryValidate(out error))
                return false;

            if (powerEnvelope == null)
            {
                error = "The room power envelope is not assigned.";
                return false;
            }

            for (int i = 0; i < productionProbesToSuppress.Length; i++)
            {
                if (productionProbesToSuppress[i] == null)
                {
                    error = $"Production probe suppression entry {i} is missing.";
                    return false;
                }

                if (productionProbesToSuppress[i] == ownedProbe)
                {
                    error = "The PoC-owned blend probe cannot suppress itself.";
                    return false;
                }
            }

            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {
                error = "ARGBHalf RenderTextures are not supported on this platform.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static RenderTexture CreateBlendTarget(int resolution)
        {
            var target = new RenderTexture(
                resolution,
                resolution,
                0,
                RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear)
            {
                name = $"DungeonPortalRoomReflectionBlend_{resolution}",
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
            catch (System.Exception)
            {
                ReleaseBlendTarget(target);
                return null;
            }

            if (target.IsCreated())
                return target;

            ReleaseBlendTarget(target);
            return null;
        }

        private void HandlePowerChanged(float power01)
        {
            if (!initialized || !connectionActivationGranted ||
                connectionOwner == null || !connectionOwner.HasClaimedEndpoints ||
                blendedCubemap == null)
            {
                if (initialized || connectionActivationGranted)
                    FailClosed("The owning connection released an endpoint during a reflection update.");
                return;
            }

            float clampedPower = Mathf.Clamp01(power01);
            // The production power bridge runs first and may re-enable one variant.
            // Reassert suppression after every envelope event while this connection owns it.
            SuppressProductionProbes();
            if (Mathf.Abs(clampedPower - lastAppliedPower) < 0.0001f)
                return;

            bool blendSucceeded;
            try
            {
                blendSucceeded = ReflectionProbe.BlendCubemap(
                    profile.Power0ResidualCubemap,
                    profile.Power100Cubemap,
                    clampedPower,
                    blendedCubemap);
            }
            catch (Exception exception)
            {
                FailClosed($"Runtime cubemap blend threw {exception.GetType().Name}: {exception.Message}");
                return;
            }

            if (!blendSucceeded)
            {
                FailClosed("ReflectionProbe.BlendCubemap failed while applying a power transition.");
                return;
            }

            lastAppliedPower = clampedPower;
        }

        private void CaptureOriginalProbeState()
        {
            originalProbeEnabled = ownedProbe.enabled;
            originalProbeMode = ownedProbe.mode;
            originalCustomBakedTexture = ownedProbe.customBakedTexture;
            originalProbeIntensity = ownedProbe.intensity;
            originalStateCaptured = true;
        }

        private void RestoreOriginalProbeState()
        {
            if (!originalStateCaptured || ownedProbe == null)
                return;

            ownedProbe.enabled = originalProbeEnabled;
            ownedProbe.mode = originalProbeMode;
            ownedProbe.customBakedTexture = originalCustomBakedTexture;
            ownedProbe.intensity = originalProbeIntensity;
            originalStateCaptured = false;
        }

        private void CaptureProductionProbeStates()
        {
            originalProductionProbeEnabledStates =
                new bool[productionProbesToSuppress.Length];
            for (int i = 0; i < productionProbesToSuppress.Length; i++)
            {
                ReflectionProbe probe = productionProbesToSuppress[i];
                originalProductionProbeEnabledStates[i] =
                    probe != null && probe.enabled;
            }

            productionProbeStatesCaptured = true;
        }

        private void SuppressProductionProbes()
        {
            for (int i = 0; i < productionProbesToSuppress.Length; i++)
            {
                ReflectionProbe probe = productionProbesToSuppress[i];
                if (probe != null && probe.enabled)
                    probe.enabled = false;
            }
        }

        private void RestoreProductionProbeStates()
        {
            if (!productionProbeStatesCaptured)
                return;

            int count = Mathf.Min(
                productionProbesToSuppress.Length,
                originalProductionProbeEnabledStates.Length);
            for (int i = 0; i < count; i++)
            {
                ReflectionProbe probe = productionProbesToSuppress[i];
                if (probe != null)
                    probe.enabled = originalProductionProbeEnabledStates[i];
            }

            originalProductionProbeEnabledStates = Array.Empty<bool>();
            productionProbeStatesCaptured = false;
        }

        private void Subscribe()
        {
            if (subscribed || powerEnvelope == null)
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

        private void TearDown()
        {
            Unsubscribe();
            RestoreOriginalProbeState();
            RestoreProductionProbeStates();

            if (blendedCubemap != null)
            {
                ReleaseBlendTarget(blendedCubemap);
                blendedCubemap = null;
            }

            initialized = false;
            connectionActivationGranted = false;
            lastAppliedPower = -1f;
        }

        private void FailClosed(string reason)
        {
            DungeonPortalTransportConnection owner = connectionOwner;
            TearDown();

            if (enabled)
                enabled = false;

            Debug.LogError(
                $"[{nameof(DungeonPortalRoomReflectionBlend)}] Disabled and restored all " +
                $"captured probe states: {reason}",
                this);
            owner?.NotifyScopedEffectFailure(this, reason);
        }

        private static ReflectionProbe[] CopyWithoutDuplicates(ReflectionProbe[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<ReflectionProbe>();

            var result = new ReflectionProbe[source.Length];
            int count = 0;
            for (int i = 0; i < source.Length; i++)
            {
                ReflectionProbe candidate = source[i];
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

        private static void ReleaseBlendTarget(RenderTexture target)
        {
            if (target == null)
                return;

            if (target.IsCreated())
                target.Release();

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
