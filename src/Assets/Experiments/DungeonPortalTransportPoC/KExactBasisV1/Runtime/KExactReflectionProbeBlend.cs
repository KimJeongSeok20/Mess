using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DungeonPortalTransportPoC.KExactBasisV1
{
    /// <summary>
    /// Optional PoC-owned reflection basis. It never suppresses or mutates production
    /// probes; authoring controls overlap through the owned probe's bounds, intensity,
    /// and importance.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ReflectionProbe))]
    public sealed class KExactReflectionProbeBlend : MonoBehaviour
    {
        public enum TransportDirection
        {
            AToB = 0,
            BToA = 1
        }

        [SerializeField] private string blendKey;
        [SerializeField] private TransportDirection direction;
        [SerializeField] private Cubemap power0ResidualCubemap;
        [SerializeField] private Cubemap power100Cubemap;
        [SerializeField, Range(16, 2048)] private int outputResolution = 128;
        [SerializeField, Min(0f)] private float intensityAtWeightOne = 0.25f;
        [SerializeField, Min(0f)] private float updateThreshold = 0.001f;

        private KExactPortalConnection owner;
        private ReflectionProbe ownedProbe;
        private RenderTexture blendedCubemap;
        private bool active;
        private bool originalCaptured;
        private bool originalEnabled;
        private ReflectionProbeMode originalMode;
        private Texture originalCustomTexture;
        private float originalIntensity;
        private float lastBlend = -1f;
        private float lastWeight = -1f;

        public string BlendKey => blendKey;
        public TransportDirection Direction => direction;
        public bool IsActive => active;
        public RenderTexture BlendedCubemap => blendedCubemap;
        public float IntensityAtWeightOne => intensityAtWeightOne;

        public void Configure(
            string key,
            TransportDirection transportDirection,
            Cubemap p0Residual,
            Cubemap p100,
            int resolution,
            float probeIntensityAtWeightOne)
        {
            Deactivate(owner);
            blendKey = key ?? string.Empty;
            direction = transportDirection;
            power0ResidualCubemap = p0Residual;
            power100Cubemap = p100;
            outputResolution = Mathf.Clamp(resolution, 16, 2048);
            intensityAtWeightOne = Mathf.Max(0f, probeIntensityAtWeightOne);
        }

        internal bool TryActivate(
            KExactPortalConnection requester,
            KExactTransportWeights weights,
            out string failure)
        {
            if (active)
            {
                if (owner == requester)
                {
                    failure = null;
                    return true;
                }

                failure = "Reflection basis is already owned by a different connection.";
                return false;
            }

            if (requester == null || !requester.IsTransportActive ||
                string.IsNullOrWhiteSpace(blendKey) ||
                power0ResidualCubemap == null || power100Cubemap == null ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {
                failure = "Reflection basis configuration is incomplete or ARGBHalf is unsupported.";
                return false;
            }

            ReflectionProbe[] probes = GetComponents<ReflectionProbe>();
            if (probes.Length != 1 || GetComponent<Renderer>() != null)
            {
                failure = "A reflection basis object must own exactly one ReflectionProbe and no Renderer.";
                return false;
            }

            ownedProbe = probes[0];
            RenderTexture candidate = CreateTarget(outputResolution, blendKey);
            if (candidate == null)
            {
                failure = "Could not allocate the ARGBHalf reflection cubemap target.";
                return false;
            }

            float blend = direction == TransportDirection.AToB
                ? weights.PowerAToB01
                : weights.PowerBToA01;
            float weight = direction == TransportDirection.AToB
                ? weights.ReflectionWeightAToB
                : weights.ReflectionWeightBToA;
            if (!TryBlend(blend, candidate, out failure))
            {
                ReleaseTarget(candidate);
                return false;
            }

            CaptureOriginal();
            owner = requester;
            blendedCubemap = candidate;
            ownedProbe.mode = ReflectionProbeMode.Custom;
            ownedProbe.customBakedTexture = blendedCubemap;
            ownedProbe.intensity = intensityAtWeightOne * Mathf.Max(0f, weight);
            ownedProbe.enabled = ownedProbe.intensity > 0.000001f;
            lastBlend = blend;
            lastWeight = weight;
            active = true;
            failure = null;
            return true;
        }

        internal bool TryApplyWeights(
            KExactPortalConnection requester,
            KExactTransportWeights weights,
            out string failure)
        {
            if (!active || requester == null || requester != owner || ownedProbe == null ||
                blendedCubemap == null)
            {
                failure = "Reflection basis lost its owning connection or runtime resources.";
                return false;
            }

            float blend = direction == TransportDirection.AToB
                ? weights.PowerAToB01
                : weights.PowerBToA01;
            float weight = direction == TransportDirection.AToB
                ? weights.ReflectionWeightAToB
                : weights.ReflectionWeightBToA;

            bool needsBlend = Mathf.Abs(blend - lastBlend) >= updateThreshold;
            if (needsBlend && !TryBlend(blend, blendedCubemap, out failure))
            {
                return false;
            }

            ownedProbe.mode = ReflectionProbeMode.Custom;
            ownedProbe.customBakedTexture = blendedCubemap;
            ownedProbe.intensity = intensityAtWeightOne * Mathf.Max(0f, weight);
            ownedProbe.enabled = ownedProbe.intensity > 0.000001f;
            // Preserve the last value actually written to the cubemap. Otherwise
            // sub-threshold frame deltas reset the accumulator and a smoothed power
            // transition can stop short of its final P0/P100 basis.
            if (needsBlend)
                lastBlend = blend;
            lastWeight = weight;
            failure = null;
            return true;
        }

        internal bool TryValidate(KExactPortalConnection requester, out string failure)
        {
            if (!active || owner != requester || ownedProbe == null || blendedCubemap == null ||
                !blendedCubemap.IsCreated() || ownedProbe.mode != ReflectionProbeMode.Custom ||
                ownedProbe.customBakedTexture != blendedCubemap ||
                GetComponent<Renderer>() != null)
            {
                failure = $"Reflection basis '{blendKey}' runtime parity failed.";
                return false;
            }

            float expectedIntensity = intensityAtWeightOne * Mathf.Max(0f, lastWeight);
            if (Mathf.Abs(ownedProbe.intensity - expectedIntensity) > 0.0001f ||
                ownedProbe.enabled != (expectedIntensity > 0.000001f))
            {
                failure = $"Reflection basis '{blendKey}' intensity/enabled parity failed.";
                return false;
            }

            failure = null;
            return true;
        }

        internal void Deactivate(KExactPortalConnection requester)
        {
            if (requester != null && owner != null && requester != owner)
                return;

            active = false;
            RestoreOriginal();
            ReleaseTarget(blendedCubemap);
            blendedCubemap = null;
            owner = null;
            ownedProbe = null;
            lastBlend = -1f;
            lastWeight = -1f;
        }

        private bool TryBlend(float value01, RenderTexture target, out string failure)
        {
            try
            {
                if (ReflectionProbe.BlendCubemap(
                        power0ResidualCubemap,
                        power100Cubemap,
                        Mathf.Clamp01(value01),
                        target))
                {
                    failure = null;
                    return true;
                }

                failure = "ReflectionProbe.BlendCubemap rejected the configured basis textures.";
                return false;
            }
            catch (Exception exception)
            {
                failure = $"Reflection cubemap blend threw {exception.GetType().Name}: " +
                          exception.Message;
                return false;
            }
        }

        private void CaptureOriginal()
        {
            originalEnabled = ownedProbe.enabled;
            originalMode = ownedProbe.mode;
            originalCustomTexture = ownedProbe.customBakedTexture;
            originalIntensity = ownedProbe.intensity;
            originalCaptured = true;
        }

        private void RestoreOriginal()
        {
            if (!originalCaptured || ownedProbe == null)
                return;
            ownedProbe.enabled = originalEnabled;
            ownedProbe.mode = originalMode;
            ownedProbe.customBakedTexture = originalCustomTexture;
            ownedProbe.intensity = originalIntensity;
            originalCaptured = false;
        }

        private static RenderTexture CreateTarget(int resolution, string key)
        {
            var target = new RenderTexture(
                resolution,
                resolution,
                0,
                RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear)
            {
                name = "KExactReflection_" + key,
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
                if (target.IsCreated())
                    return target;
            }
            catch (Exception)
            {
                // The caller reports the fail-closed allocation result.
            }

            ReleaseTarget(target);
            return null;
        }

        private static void ReleaseTarget(RenderTexture target)
        {
            if (target == null)
                return;
            if (target.IsCreated())
                target.Release();
            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }

        private void OnDisable()
        {
            if (active)
                Deactivate(owner);
        }

        private void OnDestroy()
        {
            Deactivate(owner);
        }

        private void OnValidate()
        {
            outputResolution = Mathf.Clamp(outputResolution, 16, 2048);
            intensityAtWeightOne = Mathf.Max(0f, intensityAtWeightOne);
            updateThreshold = Mathf.Max(0f, updateThreshold);
        }
    }
}
