using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "HeldItemVisualCatalog", menuName = "Inventory/Held Item Visual Catalog")]
public sealed class HeldItemVisualCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public string itemName;
        public GameObject visualPrefab;
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        public Vector3 localScale = Vector3.one;
        public string group;
    }

    [SerializeField] private Entry[] entries = Array.Empty<Entry>();
    [SerializeField, HideInInspector] private int layoutVersion;

    private Dictionary<string, Entry> _entriesByName;

    public IReadOnlyList<Entry> Entries => entries;

    public bool TryGet(string itemName, out Entry entry)
    {
        EnsureLookup();
        return _entriesByName.TryGetValue(itemName ?? string.Empty, out entry);
    }

    private void OnEnable()
    {
        _entriesByName = null;
    }

    private void OnValidate()
    {
        _entriesByName = null;
    }

    private void EnsureLookup()
    {
        if (_entriesByName != null)
            return;

        _entriesByName = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (entries == null)
            return;

        for (int i = 0; i < entries.Length; i++)
        {
            Entry candidate = entries[i];
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.itemName))
                continue;

            if (!_entriesByName.TryAdd(candidate.itemName, candidate))
                Debug.LogError($"[HeldItemVisualCatalog] Duplicate item name: {candidate.itemName}", this);
        }
    }
}
