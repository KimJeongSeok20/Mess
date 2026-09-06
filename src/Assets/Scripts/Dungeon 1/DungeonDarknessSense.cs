using UnityEngine;

/// <summary>
/// Ties the dungeon battery to monster perception: a player standing in an unpowered (dark) room
/// is noticed from farther away, a player in a lit room from closer. Outside the dungeon, or
/// while no dungeon is active, perception is unchanged. Monsters call this per candidate target,
/// so the answer is cached per frame per position bucket.
/// </summary>
public static class DungeonDarknessSense
{
    /// <summary>Detection range multiplier for targets in a dark (P0) room.</summary>
    public const float DarkMultiplier = 1.5f;

    /// <summary>Detection range multiplier for targets in a lit (P100) room.</summary>
    public const float LitMultiplier = 0.7f;

    private static NetworkDungeonController s_controller;
    private static float s_nextControllerLookup;

    private struct CacheEntry
    {
        public int frame;
        public Vector3Int bucket;
        public float multiplier;
        public bool inDungeon;
        public bool lit;
    }

    private static readonly CacheEntry[] s_cache = new CacheEntry[8];
    private static int s_cacheCursor;

    public static float DetectionMultiplierAt(Vector3 worldPosition)
    {
        Query(worldPosition, out CacheEntry entry);
        return entry.multiplier;
    }

    /// <summary>True when the position is inside a generated power-toggle room; <paramref name="lit"/> tells its state.</summary>
    public static bool TryGetRoomState(Vector3 worldPosition, out bool lit)
    {
        Query(worldPosition, out CacheEntry entry);
        lit = entry.lit;
        return entry.inDungeon;
    }

    private static void Query(Vector3 worldPosition, out CacheEntry result)
    {
        int frame = Time.frameCount;
        var bucket = new Vector3Int(
            Mathf.FloorToInt(worldPosition.x * 0.5f),
            Mathf.FloorToInt(worldPosition.y * 0.5f),
            Mathf.FloorToInt(worldPosition.z * 0.5f));

        for (int i = 0; i < s_cache.Length; i++)
        {
            if (s_cache[i].frame == frame && s_cache[i].bucket == bucket)
            {
                result = s_cache[i];
                return;
            }
        }

        result = new CacheEntry { frame = frame, bucket = bucket, multiplier = 1f, inDungeon = false, lit = true };

        NetworkDungeonController controller = ResolveController();
        if (controller != null
            && controller.TryGetRoomPowerAtPosition(worldPosition, out DungeonTileLightmapSwitcher.PowerLevel level, out _))
        {
            result.inDungeon = true;
            result.lit = level == DungeonTileLightmapSwitcher.PowerLevel.P100;
            result.multiplier = result.lit ? LitMultiplier : DarkMultiplier;
        }

        s_cache[s_cacheCursor] = result;
        s_cacheCursor = (s_cacheCursor + 1) % s_cache.Length;
    }

    private static NetworkDungeonController ResolveController()
    {
        if (s_controller != null)
            return s_controller;

        if (Time.unscaledTime < s_nextControllerLookup)
            return null;

        s_nextControllerLookup = Time.unscaledTime + 1f;
        s_controller = Object.FindAnyObjectByType<NetworkDungeonController>();
        return s_controller;
    }
}
