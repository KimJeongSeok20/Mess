using System;
using System.Collections.Generic;
using UnityEngine;
using DunGen;
using DunGen.Graph;

[CreateAssetMenu(menuName = "DunGen/Map List", fileName = "DungeonMapList")]
public class DungeonMapList : ScriptableObject
{
    [Serializable]
    public class MapEntry
    {
        public DungeonFlow flow;
        public SpawnSelector selector;
        public int budget = 0;
    }

    [Header("Maps")]
    [SerializeField] private List<MapEntry> entries = new List<MapEntry>();

    public int Count => entries == null ? 0 : entries.Count;
    public IReadOnlyList<MapEntry> Entries => entries;

    public bool TryGetFlow(int index, out DungeonFlow flow)
    {
        flow = null;
        if (entries == null) return false;
        if (index < 0 || index >= entries.Count) return false;

        flow = entries[index]?.flow;
        return flow != null;
    }

    public bool TryGetLoot(int index, out SpawnSelector selector, out int budget)
    {
        selector = null;
        budget = 0;

        if (entries == null) return false;
        if (index < 0 || index >= entries.Count) return false;

        var e = entries[index];
        if (e == null) return false;

        selector = e.selector;
        budget = e.budget;
        return true; // selector가 null이어도 "이 맵은 아이템 없음"으로 쓸 수 있게 true 유지
    }

}
