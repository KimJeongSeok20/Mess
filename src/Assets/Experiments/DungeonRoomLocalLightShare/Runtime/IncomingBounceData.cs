using System;
using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    [CreateAssetMenu(
        fileName = "IncomingBounceData",
        menuName = "Dungeon/Room Local Light Share/Incoming Bounce Data")]
    public sealed class IncomingBounceData : ScriptableObject
    {
        [Serializable]
        public sealed class RendererBinding
        {
            public string canonicalKey;
            public int lightmapIndex;
            public Vector4 lightmapScaleOffset;
        }

        [Serializable]
        public sealed class PoseCapture
        {
            public string poseId;
            public float openFraction;
            public Texture2D[] fullColor = Array.Empty<Texture2D>();
            public Texture2D[] fullDirection = Array.Empty<Texture2D>();
            public Texture2D[] directOnlyColor = Array.Empty<Texture2D>();
            public Texture2D[] directOnlyDirection = Array.Empty<Texture2D>();
        }

        [SerializeField] private string roomId;
        [SerializeField] private string doorwayPath;
        [SerializeField] private RendererBinding[] rendererBindings = Array.Empty<RendererBinding>();
        [SerializeField] private PoseCapture[] poses = Array.Empty<PoseCapture>();

        public string RoomId => roomId;
        public string DoorwayPath => doorwayPath;
        public RendererBinding[] RendererBindings =>
            rendererBindings ?? Array.Empty<RendererBinding>();
        public PoseCapture[] Poses => poses ?? Array.Empty<PoseCapture>();

        public void ConfigureAuthoring(
            string stableRoomId,
            string stableDoorwayPath,
            RendererBinding[] bindings,
            PoseCapture[] authoredPoses)
        {
            roomId = stableRoomId ?? string.Empty;
            doorwayPath = stableDoorwayPath ?? string.Empty;
            rendererBindings = bindings ?? Array.Empty<RendererBinding>();
            poses = authoredPoses ?? Array.Empty<PoseCapture>();
        }

        public bool TryGetSurroundingPoses(
            float openFraction,
            out PoseCapture lower,
            out PoseCapture upper,
            out float blend,
            out string failure)
        {
            lower = null;
            upper = null;
            blend = 0f;
            PoseCapture[] authored = Poses;
            if (authored.Length == 0)
            {
                failure = "Incoming bounce data has no authored poses.";
                return false;
            }

            var fractions = new float[authored.Length];
            for (int i = 0; i < authored.Length; i++)
            {
                if (authored[i] == null)
                {
                    failure = "Incoming bounce pose " + i + " is missing.";
                    return false;
                }

                fractions[i] = authored[i].openFraction;
            }

            RoomLocalLightShareMath.SurroundingAuthoredPoses(
                openFraction,
                fractions,
                out int lowerIndex,
                out int upperIndex,
                out blend);
            lower = authored[lowerIndex];
            upper = authored[upperIndex];
            failure = null;
            return true;
        }

        public bool TryValidate(out string failure)
        {
            if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(doorwayPath))
            {
                failure = "Incoming bounce room id or doorway path is empty.";
                return false;
            }

            PoseCapture[] authored = Poses;
            if (authored.Length == 0)
            {
                failure = "Incoming bounce data has no poses.";
                return false;
            }

            int atlasCount = -1;
            for (int i = 0; i < authored.Length; i++)
            {
                PoseCapture pose = authored[i];
                if (pose == null ||
                    pose.fullColor == null || pose.directOnlyColor == null ||
                    pose.fullDirection == null || pose.directOnlyDirection == null)
                {
                    failure = "Pose " + i + " is missing atlas arrays.";
                    return false;
                }

                if (pose.fullColor.Length != pose.directOnlyColor.Length ||
                    pose.fullColor.Length != pose.fullDirection.Length ||
                    pose.fullColor.Length != pose.directOnlyDirection.Length)
                {
                    failure = "Pose " + pose.poseId + " atlas counts do not match.";
                    return false;
                }

                if (atlasCount < 0)
                    atlasCount = pose.fullColor.Length;
                else if (atlasCount != pose.fullColor.Length)
                {
                    failure = "Pose atlas counts drifted across door poses.";
                    return false;
                }

                for (int atlas = 0; atlas < pose.fullColor.Length; atlas++)
                {
                    if (pose.fullColor[atlas] == null || pose.directOnlyColor[atlas] == null ||
                        pose.fullDirection[atlas] == null || pose.directOnlyDirection[atlas] == null)
                    {
                        failure = "Pose " + pose.poseId + " atlas " + atlas + " has a missing texture.";
                        return false;
                    }
                }
            }

            failure = null;
            return true;
        }
    }
}
