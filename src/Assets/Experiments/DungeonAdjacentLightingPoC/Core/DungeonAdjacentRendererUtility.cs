using UnityEngine;

namespace DungeonAdjacentLightingPoC
{
    public static class DungeonAdjacentRendererUtility
    {
        public static bool TryGetEligibleMesh(
            MeshRenderer renderer,
            out MeshFilter filter,
            out Mesh mesh)
        {
            filter = null;
            mesh = null;
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                return false;

            filter = renderer.GetComponent<MeshFilter>();
            mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
                return false;

            Vector2[] lightmapUv = mesh.uv2;
            if (lightmapUv == null || lightmapUv.Length != mesh.vertexCount)
                return false;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
                return false;

            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null)
                    return true;
            }

            return false;
        }
    }
}
