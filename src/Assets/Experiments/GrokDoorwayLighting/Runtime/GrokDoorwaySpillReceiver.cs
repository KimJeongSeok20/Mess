using UnityEngine;
using UnityEngine.Rendering;

namespace GrokDoorwayLighting
{
    [DisallowMultipleComponent]
    public sealed class GrokDoorwaySpillReceiver : MonoBehaviour
    {
        [SerializeField] private MeshRenderer sourceRenderer;
        [SerializeField] private MeshFilter sourceFilter;
        [SerializeField] private MeshRenderer overlayRenderer;

        public MeshRenderer SourceRenderer => sourceRenderer;

        public static GrokDoorwaySpillReceiver Create(MeshRenderer source, MeshFilter filter)
        {
            if (source == null || filter == null || filter.sharedMesh == null)
                return null;

            Transform existing = source.transform.Find(GrokDoorwayMaterialUtility.OverlayName);
            if (existing != null)
                DestroySafe(existing.gameObject);

            var overlayObject = new GameObject(GrokDoorwayMaterialUtility.OverlayName);
            overlayObject.transform.SetParent(source.transform, false);
            overlayObject.layer = source.gameObject.layer;

            var overlayFilter = overlayObject.AddComponent<MeshFilter>();
            overlayFilter.sharedMesh = filter.sharedMesh;

            var overlay = overlayObject.AddComponent<MeshRenderer>();
            overlay.shadowCastingMode = ShadowCastingMode.Off;
            overlay.receiveShadows = false;
            overlay.lightProbeUsage = LightProbeUsage.Off;
            overlay.reflectionProbeUsage = ReflectionProbeUsage.Off;
            overlay.allowOcclusionWhenDynamic = false;
            overlay.renderingLayerMask = source.renderingLayerMask;
            overlay.lightmapIndex = -1;
            overlay.realtimeLightmapIndex = -1;
            overlay.enabled = false;

            Material[] sourceMaterials = source.sharedMaterials;
            var overlayMaterials = new Material[sourceMaterials.Length];
            for (int i = 0; i < sourceMaterials.Length; i++)
                overlayMaterials[i] = GrokDoorwayMaterialUtility.CreateOverlayMaterial(sourceMaterials[i]);
            overlay.sharedMaterials = overlayMaterials;

            var receiver = overlayObject.AddComponent<GrokDoorwaySpillReceiver>();
            receiver.sourceRenderer = source;
            receiver.sourceFilter = filter;
            receiver.overlayRenderer = overlay;
            return receiver;
        }

        public void Apply(
            in GrokDoorwayMath.DoorwayFrame door,
            Texture portal,
            float spillScale,
            float bounceReflectance,
            float bounceRange,
            float bounceScale,
            float transferMode,
            float revealScale,
            bool enabled)
        {
            if (overlayRenderer == null)
                return;

            if (!enabled || portal == null)
            {
                overlayRenderer.enabled = false;
                return;
            }

            Material[] materials = overlayRenderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                GrokDoorwayMaterialUtility.ApplyDoorway(
                    materials[i],
                    door,
                    portal,
                    spillScale,
                    bounceReflectance,
                    bounceRange,
                    bounceScale,
                    transferMode,
                    revealScale,
                    true);
            }

            overlayRenderer.enabled = true;
        }

        public void Hide()
        {
            if (overlayRenderer != null)
                overlayRenderer.enabled = false;
        }

        public void DestroyOverlay()
        {
            if (overlayRenderer == null)
                return;

            DestroySafe(overlayRenderer.gameObject);
            overlayRenderer = null;
        }

        private void OnDestroy()
        {
            if (overlayRenderer == null)
                return;

            Material[] materials = overlayRenderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null)
                    DestroySafe(materials[i]);
            }
        }

        private static void DestroySafe(Object obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }
    }
}
