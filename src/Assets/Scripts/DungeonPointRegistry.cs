using System.Collections.Generic;
using UnityEngine;

public static class DungeonPointRegistry
{
    public static readonly List<Transform> PatrolPoints = new();
    public static readonly List<Transform> SpawnPoints = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        PatrolPoints.Clear();
        SpawnPoints.Clear();
    }

    public static void RebuildFromRoot(Transform dungeonRoot)
    {
        PatrolPoints.Clear();
        SpawnPoints.Clear();

        if (dungeonRoot == null) return;

        var patrols = dungeonRoot.GetComponentsInChildren<PatrolPointMarker>(true);
        foreach (var p in patrols)
            PatrolPoints.Add(p.transform);

        var spawns = dungeonRoot.GetComponentsInChildren<MonsterSpawnPointMarker>(true);
        foreach (var s in spawns)
            SpawnPoints.Add(s.transform);

        Debug.Log($"[DungeonPointRegistry] Patrol={PatrolPoints.Count}, Spawn={SpawnPoints.Count}");
    }
}
