using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    public static class RoomLocalOutgoingFactory
    {
        public static OutgoingPortalMap CreateRuntime(
            string roomId,
            string doorwayPath,
            Transform roomRoot,
            Transform doorway,
            DungeonTileBakeData power0,
            DungeonTileBakeData power100)
        {
            Texture2D hdr0 = RoomLocalPortalSampler.Capture(
                roomRoot,
                doorway,
                power0,
                RoomLocalLightShareContract.PortalWidth,
                RoomLocalLightShareContract.PortalHeight);
            Texture2D hdr100 = RoomLocalPortalSampler.Capture(
                roomRoot,
                doorway,
                power100,
                RoomLocalLightShareContract.PortalWidth,
                RoomLocalLightShareContract.PortalHeight);
            if (hdr0 == null || hdr100 == null)
            {
                RoomLocalPortalSampler.DestroyTexture(hdr0);
                RoomLocalPortalSampler.DestroyTexture(hdr100);
                return null;
            }

            Texture2D cookie0 = RoomLocalPortalSampler.CreateCookie(hdr0, out float peak0);
            Texture2D cookie100 = RoomLocalPortalSampler.CreateCookie(hdr100, out float peak100);
            if (cookie0 == null || cookie100 == null)
            {
                RoomLocalPortalSampler.DestroyTexture(hdr0);
                RoomLocalPortalSampler.DestroyTexture(hdr100);
                RoomLocalPortalSampler.DestroyTexture(cookie0);
                RoomLocalPortalSampler.DestroyTexture(cookie100);
                return null;
            }

            var map = ScriptableObject.CreateInstance<OutgoingPortalMap>();
            map.hideFlags = HideFlags.HideAndDontSave;
            map.name = roomId + "_LiveOutgoing";
            map.ConfigureAuthoring(
                roomId,
                doorwayPath,
                hdr0,
                hdr100,
                cookie0,
                cookie100,
                RoomLocalPortalSampler.Average(hdr0),
                RoomLocalPortalSampler.Average(hdr100),
                peak0,
                peak100);
            return map;
        }
    }
}
