using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Spawning/Spawn Selector", fileName = "SpawnSelector")]
public class SpawnSelector : ScriptableObject
{
    [SerializeField] private ItemSpawnlist source;          // 전역 itemlist
    [SerializeField] private List<string> includeTags = new();
    [SerializeField] private List<string> excludeTags = new();


    public ItemSpawnlist Source => source;

    public bool TryPick(out ItemSpawnlist.ItemEntry picked, System.Random rng = null)
    {
        picked = null;
        if (source?.Items == null || source.Items.Count == 0) return false;

        rng ??= new System.Random();

        float total = 0f;

        foreach (var e in source.Items)
        {
            if (e == null || e.prefab == null || e.weight <= 0f) continue;
            if (includeTags.Count > 0 && !HasAnyTag(e.tags, includeTags)) continue;
            if (excludeTags.Count > 0 && HasAnyTag(e.tags, excludeTags)) continue;

            total += e.weight;

            // 확률: e.weight / total 로 picked 교체
            if (rng.NextDouble() * total < e.weight)
                picked = e;
        }

        return picked != null;
    }

    private static bool HasAnyTag(List<string> itemTags, List<string> filter)
    {
        if (itemTags == null || itemTags.Count == 0) return false;
        for (int i = 0; i < filter.Count; i++)
            if (itemTags.Contains(filter[i]))
                return true;
        return false;
    }
}
