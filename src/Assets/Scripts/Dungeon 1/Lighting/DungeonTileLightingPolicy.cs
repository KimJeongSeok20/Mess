using UnityEngine;

/// <summary>
/// Source-prefab lighting policy that survives V2 canonical/output regeneration.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Dungeon/Lighting/Tile Lighting Policy")]
public sealed class DungeonTileLightingPolicy : MonoBehaviour
{
    [SerializeField] private DungeonTileLightmapSwitcher.LightingMode mode =
        DungeonTileLightmapSwitcher.LightingMode.PowerToggle;

    public DungeonTileLightmapSwitcher.LightingMode Mode => mode;

    public void Configure(DungeonTileLightmapSwitcher.LightingMode value)
    {
        mode = value;
    }
}
