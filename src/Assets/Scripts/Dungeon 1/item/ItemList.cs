using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Spawning/Item Spawn List", fileName = "ItemSpawnList")]
public class ItemSpawnlist : ScriptableObject
{
    [Serializable]
    public class ItemEntry
    {
        [Tooltip("스폰할 item prefab")]
        public GameObject prefab;

        //가치는 item.cs에서 설정

        [Min(0)]
        [Tooltip("랜덤 선택 가중치(클수록 잘 뽑힘)")]
        public float weight = 1f;

        [Tooltip("선택 필터링용 태그(예: 'kitchen', 'rare', 'medical')")]
        public List<string> tags = new List<string>();
    }

    [SerializeField] private List<SpawnSelector> spawnSelectors = new();

    public bool TryGetSpawnSelector(int index, out SpawnSelector selector)
    {
        selector = null;
        if (spawnSelectors == null) return false;
        if (index < 0 || index >= spawnSelectors.Count) return false;
        selector = spawnSelectors[index];
        return selector != null;
    }

    [SerializeField]
    private List<ItemEntry> items = new List<ItemEntry>();

    public IReadOnlyList<ItemEntry> Items => items;

    public ItemEntry Get(int index) => items[index];
}
