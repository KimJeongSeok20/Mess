using UnityEngine;

namespace DungeonPortalTransportPoC
{
    [CreateAssetMenu(
        fileName = "DungeonPortalRoomReflectionProfile",
        menuName = "StillWorking/Experiments/Dungeon Portal Transport/Room Reflection Profile")]
    public sealed class DungeonPortalRoomReflectionProfile : ScriptableObject
    {
        [Header("Canonical room cubemaps")]
        [SerializeField]
        [Tooltip("Lights-off bake for this room. The desired weak residual reflection must already be present in this cubemap.")]
        private Cubemap power0ResidualCubemap;

        [SerializeField]
        [Tooltip("Fully-powered bake for the same probe position, box and resolution as the P0 cubemap.")]
        private Cubemap power100Cubemap;

        [Header("Probe response")]
        [SerializeField, Min(0.0001f)]
        [Tooltip("Held constant across P0 to P100. Brightness variation comes only from the two canonical cubemaps.")]
        private float fixedProbeIntensity = 1f;

        public Cubemap Power0ResidualCubemap => power0ResidualCubemap;
        public Cubemap Power100Cubemap => power100Cubemap;
        public float FixedProbeIntensity => fixedProbeIntensity;
        public int Resolution => power0ResidualCubemap != null ? power0ResidualCubemap.width : 0;

        public void Configure(
            Cubemap power0Residual,
            Cubemap power100,
            float probeIntensity = 1f)
        {
            power0ResidualCubemap = power0Residual;
            power100Cubemap = power100;
            fixedProbeIntensity = Mathf.Max(0.0001f, probeIntensity);
        }

        public bool TryValidate(out string error)
        {
            if (power0ResidualCubemap == null)
            {
                error = "The P0 residual cubemap is not assigned.";
                return false;
            }

            if (power100Cubemap == null)
            {
                error = "The P100 cubemap is not assigned.";
                return false;
            }

            if (power0ResidualCubemap == power100Cubemap)
            {
                error = "P0 and P100 reference the same cubemap. Separate canonical bakes are required.";
                return false;
            }

            if (power0ResidualCubemap.width <= 0 || power0ResidualCubemap.height <= 0)
            {
                error = "The P0 cubemap has an invalid resolution.";
                return false;
            }

            if (power0ResidualCubemap.width != power0ResidualCubemap.height)
            {
                error = "The P0 cubemap faces are not square.";
                return false;
            }

            if (power100Cubemap.width != power100Cubemap.height)
            {
                error = "The P100 cubemap faces are not square.";
                return false;
            }

            if (power0ResidualCubemap.width != power100Cubemap.width ||
                power0ResidualCubemap.height != power100Cubemap.height)
            {
                error = "P0 and P100 cubemap resolutions do not match.";
                return false;
            }

            if (power0ResidualCubemap.mipmapCount != power100Cubemap.mipmapCount)
            {
                error = "P0 and P100 cubemap mip counts do not match.";
                return false;
            }

            if (power0ResidualCubemap.mipmapCount <= 1)
            {
                error = "The canonical cubemaps have no reflection-roughness mip chain.";
                return false;
            }

            if (float.IsNaN(fixedProbeIntensity) ||
                float.IsInfinity(fixedProbeIntensity) ||
                fixedProbeIntensity <= 0f)
            {
                error = "The fixed probe intensity must be a finite value greater than zero.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void OnValidate()
        {
            if (float.IsNaN(fixedProbeIntensity) || float.IsInfinity(fixedProbeIntensity))
                fixedProbeIntensity = 1f;
            else
                fixedProbeIntensity = Mathf.Max(0.0001f, fixedProbeIntensity);
        }
    }
}
