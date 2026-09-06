using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    /// <summary>
    /// Compensates only the Door_01 frame submesh whose authored baked texels are nearly black.
    /// The correction follows the receiver power and the neighboring room's portal transfer,
    /// so a P0/P0 doorway remains black and incoming color still follows the real door aperture.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomLocalDoorFrameLightDriver : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Renderer targetRenderer;
        [SerializeField, Min(0)] private int materialIndex = 2;
        [SerializeField] private DungeonTileLightmapSwitcher receiverLighting;
        [SerializeField] private DungeonTileLightmapSwitcher sourceLighting;
        [SerializeField] private OutgoingPortalMap receiverOutgoing;
        [SerializeField] private OutgoingPortalMap sourceOutgoing;
        [SerializeField] private RoomLocalDoorAngleSource doorAngle;
        [SerializeField, Min(0f)] private float localEmissionScale = 0.2f;
        [SerializeField, Min(0f)] private float incomingEmissionScale = 0.2f;
        [SerializeField, Min(0f)] private float maximumEmission = 0.08f;

        private MaterialPropertyBlock propertyBlock;

        public void Configure(
            Renderer renderer,
            int rendererMaterialIndex,
            DungeonTileLightmapSwitcher receiver,
            DungeonTileLightmapSwitcher source,
            OutgoingPortalMap receiverMap,
            OutgoingPortalMap sourceMap,
            RoomLocalDoorAngleSource angle)
        {
            targetRenderer = renderer;
            materialIndex = Mathf.Max(0, rendererMaterialIndex);
            receiverLighting = receiver;
            sourceLighting = source;
            receiverOutgoing = receiverMap;
            sourceOutgoing = sourceMap;
            doorAngle = angle;
        }

        private void LateUpdate()
        {
            if (!IsConfigured())
                return;

            Color local = receiverLighting.CurrentPowerLevel ==
                          DungeonTileLightmapSwitcher.PowerLevel.P100
                ? PositiveDifference(receiverOutgoing.Power100Average, receiverOutgoing.Power0Average)
                : Color.black;

            float sourcePower = sourceLighting.CurrentPowerLevel ==
                                DungeonTileLightmapSwitcher.PowerLevel.P100
                ? 1f
                : 0f;
            Color incoming = RoomLocalLightShareMath.ComposeTransferRadiance(
                sourceOutgoing.Power0Average,
                sourceOutgoing.Power100Average,
                sourcePower,
                doorAngle.ApertureFraction);

            Color emission = local * localEmissionScale + incoming * incomingEmissionScale;
            emission.r = Mathf.Min(maximumEmission, Mathf.Max(0f, emission.r));
            emission.g = Mathf.Min(maximumEmission, Mathf.Max(0f, emission.g));
            emission.b = Mathf.Min(maximumEmission, Mathf.Max(0f, emission.b));
            emission.a = 1f;

            propertyBlock ??= new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(propertyBlock, materialIndex);
            propertyBlock.SetColor(EmissionColorId, emission);
            targetRenderer.SetPropertyBlock(propertyBlock, materialIndex);
        }

        private void OnDisable()
        {
            ClearEmission();
        }

        private bool IsConfigured()
        {
            return targetRenderer != null &&
                   materialIndex < targetRenderer.sharedMaterials.Length &&
                   receiverLighting != null &&
                   sourceLighting != null &&
                   receiverOutgoing != null &&
                   sourceOutgoing != null &&
                   doorAngle != null;
        }

        private void ClearEmission()
        {
            if (targetRenderer == null || materialIndex >= targetRenderer.sharedMaterials.Length)
                return;

            propertyBlock ??= new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(propertyBlock, materialIndex);
            propertyBlock.SetColor(EmissionColorId, Color.black);
            targetRenderer.SetPropertyBlock(propertyBlock, materialIndex);
        }

        private static Color PositiveDifference(Color full, Color baseline)
        {
            return new Color(
                Mathf.Max(0f, full.r - baseline.r),
                Mathf.Max(0f, full.g - baseline.g),
                Mathf.Max(0f, full.b - baseline.b),
                1f);
        }
    }
}
