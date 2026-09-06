using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    public static class RoomLocalRenderingLayers
    {
        public static uint EnvironmentMask =>
            (uint)(RoomLocalLightShareContract.DungeonRenderingLayerMask |
                   RoomLocalLightShareContract.CookieEnvironmentRenderingLayerMask);

        public static uint DoorVisibleMask(uint cookieFaceMask)
        {
            return (uint)RoomLocalLightShareContract.DungeonRenderingLayerMask | cookieFaceMask;
        }

        public static void AddCookieEnvironment(Transform root)
        {
            if (root == null)
                return;

            uint cookieEnv = (uint)RoomLocalLightShareContract.CookieEnvironmentRenderingLayerMask;
            uint doorBits = (uint)RoomLocalLightShareContract.DoorExclusiveRenderingLayerMask;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;
                if ((renderer.renderingLayerMask & doorBits) != 0)
                    continue;
                if (renderer.GetComponent<DungeonDoorProbeRendererGroup>() != null)
                    continue;
                if (IsDoorHierarchy(renderer.transform))
                    continue;

                renderer.renderingLayerMask =
                    (uint)renderer.renderingLayerMask | cookieEnv;
            }
        }

        private static bool IsDoorHierarchy(Transform current)
        {
            while (current != null)
            {
                if (current.name == RoomLocalLightShareContract.DoorLeafPath)
                    return true;
                current = current.parent;
            }

            return false;
        }
    }
}
