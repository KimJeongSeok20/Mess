using UnityEngine;

namespace GrokDoorwayLighting
{
    [CreateAssetMenu(fileName = "GrokDoorwayPortalMap", menuName = "Grok/Doorway Portal Map")]
    public sealed class GrokDoorwayPortalMap : ScriptableObject
    {
        public string roomName;
        public string doorwayPath;
        public Texture2D power100;
        public Texture2D power0;
        public Color power100Average = Color.black;
        public Color power0Average = Color.black;

        public Texture2D GetTexture(DungeonTileLightmapSwitcher.PowerLevel power)
        {
            return power == DungeonTileLightmapSwitcher.PowerLevel.P0 ? power0 : power100;
        }

        public Color GetAverage(DungeonTileLightmapSwitcher.PowerLevel power)
        {
            return power == DungeonTileLightmapSwitcher.PowerLevel.P0 ? power0Average : power100Average;
        }

        public bool HasTexture(DungeonTileLightmapSwitcher.PowerLevel power)
        {
            return GetTexture(power) != null;
        }
    }
}
