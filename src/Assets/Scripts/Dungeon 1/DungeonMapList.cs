using System;
using System.Collections;
using System.Collections.Generic;
using DunGen.Graph;
using UnityEngine;

[CreateAssetMenu(menuName = "DunGen/Map List", fileName = "DungeonMapList")]
public class DungeonMapList : ScriptableObject
{
    [Serializable]
    public class MapEntry
    {
        public DungeonFlow flow;
        public SpawnSelector selector;
        public int budget;
        [HideInInspector] public string contentId;
        [HideInInspector] public string resourceKey;
        [HideInInspector] public string flowAssetPath;
    }

    [Header("Maps")]
    [SerializeField] private List<MapEntry> entries = new();
    [SerializeField, HideInInspector] private bool deferredLoading;
    [NonSerialized] private DungeonMapContent loadedContent;
    [NonSerialized] private ResourceRequest request;
    [NonSerialized] private int requestedIndex = -1;
    [NonSerialized] private int loadedIndex = -1;

    public int Count => entries == null ? 0 : entries.Count;
    public IReadOnlyList<MapEntry> Entries => entries;
    public bool UsesDeferredLoading => deferredLoading;
    public int LoadedFlowIndex => loadedIndex;
    public bool IsLoading => request != null;
    public string LoadError { get; private set; }

    public string GetContentId(int index)
    {
        if (index < 0 || index >= Count || entries[index] == null) return string.Empty;
#if UNITY_EDITOR
        if (!deferredLoading && entries[index].flow != null)
        {
            string path = UnityEditor.AssetDatabase.GetAssetPath(entries[index].flow);
            return UnityEditor.AssetDatabase.AssetPathToGUID(path) + "-" + UnityEditor.AssetDatabase.GetAssetDependencyHash(path);
        }
#endif
        return entries[index].contentId ?? string.Empty;
    }

    public string GetFlowAssetPath(int index)
    {
        if (index < 0 || index >= Count || entries[index] == null) return string.Empty;
#if UNITY_EDITOR
        if (!deferredLoading && entries[index].flow != null)
            return UnityEditor.AssetDatabase.GetAssetPath(entries[index].flow);
#endif
        return entries[index].flowAssetPath ?? string.Empty;
    }

    public bool TryGetFlow(int index, out DungeonFlow flow)
    {
        flow = null;
        if (index < 0 || index >= Count) return false;
        flow = entries[index]?.flow;
        return flow != null;
    }

    /// <summary>One resident map per catalog. Call after clearing the previous dungeon.</summary>
    public IEnumerator LoadFlowAsync(int index)
    {
        LoadError = null;
        if (index < 0 || index >= Count || entries[index] == null)
        { LoadError = $"Invalid dungeon map index {index}."; yield break; }
        if (TryGetFlow(index, out _)) yield break;
        var entry = entries[index];
        if (!deferredLoading || string.IsNullOrEmpty(entry.resourceKey))
        { LoadError = $"Dungeon map {index} has no generated content key. Rebuild the game."; yield break; }
        // Duplicate callers share an in-flight request; different maps are serialized.
        while (request != null && requestedIndex != index) yield return null;
        if (TryGetFlow(index, out _)) yield break;
        if (request == null)
        {
            ReleaseLoadedFlow();
            requestedIndex = index;
            request = Resources.LoadAsync<DungeonMapContent>(entry.resourceKey);
        }
        var pending = request;
        yield return pending;
        if (request != pending) yield break; // Another caller already published this request.
        var content = pending.asset as DungeonMapContent;
        request = null;
        requestedIndex = -1;
        if (content == null || content.flow == null || content.contentId != entry.contentId)
        { LoadError = $"Dungeon map content is missing or mismatched: {entry.resourceKey}."; yield break; }
        loadedContent = content;
        loadedIndex = index;
        entry.flow = content.flow;
    }

    public void ReleaseLoadedFlow()
    {
        if (!deferredLoading) return;
        if (loadedIndex >= 0 && loadedIndex < Count) entries[loadedIndex].flow = null;
        loadedIndex = -1;
        loadedContent = null;
    }

    public bool TryGetLoot(int index, out SpawnSelector selector, out int budget)
    {
        selector = null;
        budget = 0;
        if (index < 0 || index >= Count || entries[index] == null) return false;
        selector = entries[index].selector;
        budget = entries[index].budget;
        return true;
    }

#if UNITY_EDITOR
    // Only build-scene copies are stripped. Authored Inspector references stay intact.
    public void ConfigureDeferredBuild(List<MapEntry> buildEntries)
    {
        entries = buildEntries;
        deferredLoading = true;
    }
#endif
}
