using System;
using DunGen;
using UnityEngine;

namespace DungeonAdjacentLightingPoC
{
    [DisallowMultipleComponent]
    public sealed class DungeonAdjacentLightmapExtension : MonoBehaviour
    {
        [Serializable]
        public struct BakedState
        {
            public Texture2D lightmapColor;
            public Texture2D lightmapDirection;
            public Vector4 lightmapScaleOffset;

            public bool IsValid => lightmapColor != null;
        }

        [SerializeField] private string doorwayId;
        [SerializeField] private Doorway doorway;
        [SerializeField] private MeshFilter extensionMeshFilter;
        [SerializeField] private MeshRenderer extensionRenderer;
        [SerializeField, Min(0.05f)] private float portalHalfWidth = 0.5f;
        [SerializeField] private BakedState power100;
        [SerializeField] private BakedState power0;
        [SerializeField] private bool hideRendererAtRuntime = true;

        public string DoorwayId => doorwayId;
        public Doorway Doorway => doorway;
        public Mesh ExtensionMesh => extensionMeshFilter != null ? extensionMeshFilter.sharedMesh : null;
        public Transform ExtensionTransform => extensionMeshFilter != null ? extensionMeshFilter.transform : transform;
        public MeshRenderer ExtensionRenderer => extensionRenderer;
        public float PortalHalfWidth => Mathf.Max(0.05f, portalHalfWidth);
        public float PortalHalfHeight
        {
            get
            {
                Vector2 socketSize = doorway != null && doorway.Socket != null
                    ? doorway.Socket.Size
                    : new Vector2(PortalHalfWidth * 2f, 2.5f);
                return Mathf.Max(0.05f, socketSize.y * 0.5f);
            }
        }

        private void Awake()
        {
            if (Application.isPlaying && hideRendererAtRuntime && extensionRenderer != null)
                extensionRenderer.enabled = false;
        }

        public BakedState GetState(DungeonTileLightmapSwitcher.PowerLevel powerLevel)
        {
            return powerLevel == DungeonTileLightmapSwitcher.PowerLevel.P0 ? power0 : power100;
        }

        public void ConfigureAuthoring(
            string stableDoorwayId,
            Doorway ownerDoorway,
            MeshFilter meshFilter,
            MeshRenderer meshRenderer,
            float authoredPortalHalfWidth)
        {
            doorwayId = stableDoorwayId;
            doorway = ownerDoorway;
            extensionMeshFilter = meshFilter;
            extensionRenderer = meshRenderer;
            portalHalfWidth = Mathf.Max(0.05f, authoredPortalHalfWidth);
        }

        public void CaptureState(
            DungeonTileLightmapSwitcher.PowerLevel powerLevel,
            Texture2D color,
            Texture2D direction,
            Vector4 scaleOffset)
        {
            var state = new BakedState
            {
                lightmapColor = color,
                lightmapDirection = direction,
                lightmapScaleOffset = scaleOffset
            };

            if (powerLevel == DungeonTileLightmapSwitcher.PowerLevel.P0)
                power0 = state;
            else
                power100 = state;
        }
    }
}
