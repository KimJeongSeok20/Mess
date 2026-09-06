using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

/// <summary>
/// Switches Item renderer renderingLayerMask on trigger enter/exit.
/// Detects Item component via GetComponentInParent and applies to all child Renderers.
/// Supplies dungeon probes to Items without an authored receiver while they are inside.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class DungeonItemRenderLayerTrigger : MonoBehaviour
{
    [Header("Rendering Layer Names (Project Settings > Tags and Layers > Rendering Layers)")]
    public string dungeonLayerName = "Dungeon";
    public string defaultLayerName = "Default";
    [SerializeField] private bool restoreDefaultOnExit = true;

    private uint _maskDungeon;
    private uint _maskDefault;
    private BoxCollider _trigger;
    private readonly Dictionary<int, int> _overlapCounts = new Dictionary<int, int>();
    private readonly Dictionary<int, OwnedProbeReceiver> _ownedProbeReceivers = new Dictionary<int, OwnedProbeReceiver>();
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private sealed class OwnedProbeReceiver
    {
        public GameObject targetRoot;
        public DungeonDynamicProbeReceiver receiver;
        public readonly List<(Renderer renderer, int materialIndex, MaterialPropertyBlock block)> propertyBlocks =
            new List<(Renderer renderer, int materialIndex, MaterialPropertyBlock block)>();
    }

    private void Awake()
    {
        _maskDungeon = GetRenderingLayerMask(dungeonLayerName);
        _maskDefault = GetRenderingLayerMask(defaultLayerName);

        _trigger = GetComponent<BoxCollider>();
        if (_trigger != null)
        {
            _trigger.isTrigger = true;
        }
    }

    private uint GetRenderingLayerMask(string layerName)
    {
        int idx = RenderingLayerMask.NameToRenderingLayer(layerName);
        if (idx < 0)
        {
            Debug.LogError($"[DungeonItemRenderLayerTrigger] Rendering Layer '{layerName}' not found. (Project Settings > Tags and Layers > Rendering Layers)", this);
            return 0;
        }
        return 1u << idx;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!TryGetTargetRoot(other, out var targetRoot, out var label))
            return;

        int key = targetRoot.GetInstanceID();
        _overlapCounts.TryGetValue(key, out int overlapCount);
        overlapCount++;
        _overlapCounts[key] = overlapCount;

        if (overlapCount != 1)
            return;

        ApplyRenderingLayerToTarget(targetRoot, _maskDungeon, label);

        if (targetRoot.GetComponent<Item>() == null)
            return;

        if (!_ownedProbeReceivers.TryGetValue(key, out var owned) || owned.receiver == null)
        {
            if (targetRoot.GetComponentInParent<DungeonDynamicProbeReceiver>(true) != null ||
                targetRoot.GetComponentInChildren<DungeonDynamicProbeReceiver>(true) != null)
                return;

            owned = new OwnedProbeReceiver { targetRoot = targetRoot };
            CaptureProbePropertyBlocks(owned);
            owned.receiver = targetRoot.AddComponent<DungeonDynamicProbeReceiver>();
            _ownedProbeReceivers[key] = owned;
        }
        else
            CaptureProbePropertyBlocks(owned);

        owned.receiver.enabled = true;
        owned.receiver.ForceRefresh();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!TryGetTargetRoot(other, out var targetRoot, out var label))
            return;

        int key = targetRoot.GetInstanceID();
        if (!_overlapCounts.TryGetValue(key, out int overlapCount))
            return;

        overlapCount = Mathf.Max(0, overlapCount - 1);
        if (overlapCount > 0)
        {
            _overlapCounts[key] = overlapCount;
            return;
        }

        _overlapCounts.Remove(key);

        if (restoreDefaultOnExit)
            ApplyRenderingLayerToTarget(targetRoot, _maskDefault, label);

        if (_ownedProbeReceivers.TryGetValue(key, out var owned))
            RestoreProbePropertyBlocks(owned);
    }

    private static void CaptureProbePropertyBlocks(OwnedProbeReceiver owned)
    {
        owned.propertyBlocks.Clear();
        foreach (var renderer in owned.targetRoot.GetComponentsInChildren<Renderer>(true))
        {
            int materialCount = renderer.sharedMaterials.Length;
            for (int i = 0; i < Mathf.Max(1, materialCount); i++)
            {
                int materialIndex = materialCount == 0 ? -1 : i;
                var block = new MaterialPropertyBlock();
                if (materialIndex < 0)
                    renderer.GetPropertyBlock(block);
                else
                    renderer.GetPropertyBlock(block, materialIndex);
                owned.propertyBlocks.Add((renderer, materialIndex, block));
            }
        }
    }

    private static void RestoreProbePropertyBlocks(OwnedProbeReceiver owned)
    {
        if (owned.receiver != null)
            owned.receiver.enabled = false;

        var currentBlock = new MaterialPropertyBlock();
        foreach (var snapshot in owned.propertyBlocks)
        {
            if (snapshot.renderer == null)
                continue;

            if (snapshot.materialIndex < 0)
                snapshot.renderer.GetPropertyBlock(currentBlock);
            else
                snapshot.renderer.GetPropertyBlock(currentBlock, snapshot.materialIndex);

            // Hover owns emission; keep its latest value while removing our probe overrides.
            if (currentBlock.HasColor(EmissionColorId))
                snapshot.block.SetColor(EmissionColorId, currentBlock.GetColor(EmissionColorId));

            var restoredBlock = snapshot.block.isEmpty ? null : snapshot.block;
            if (snapshot.materialIndex < 0)
                snapshot.renderer.SetPropertyBlock(restoredBlock);
            else
                snapshot.renderer.SetPropertyBlock(restoredBlock, snapshot.materialIndex);
        }

        owned.propertyBlocks.Clear();
    }

    private static bool TryGetTargetRoot(Collider other, out GameObject targetRoot, out string targetLabel)
    {
        targetRoot = null;
        targetLabel = null;

        if (other == null)
            return false;

        Item item = other.GetComponentInParent<Item>();
        if (item != null)
        {
            targetRoot = item.gameObject;
            targetLabel = $"Item '{item.ItemName}'";
            return true;
        }

        MonsterHealth monster = other.GetComponentInParent<MonsterHealth>();
        if (monster != null)
        {
            targetRoot = monster.gameObject;
            targetLabel = $"Monster '{monster.name}'";
            return true;
        }

        return false;
    }

    private void ApplyRenderingLayerToTarget(GameObject targetRoot, uint mask, string targetLabel)
    {
        if (targetRoot == null)
            return;

        int changed = 0;
        int total = 0;

        Renderer[] renderers = targetRoot.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            total++;
            if (renderer.renderingLayerMask != mask)
            {
                renderer.renderingLayerMask = mask;
                changed++;
            }
        }

        if (changed > 0)
        {
            string layerName = (mask == _maskDungeon) ? dungeonLayerName : defaultLayerName;
            Debug.Log($"[DungeonItemRenderLayerTrigger] {targetLabel} rendering layer -> {layerName} (changed {changed}/{total} renderers)");
        }
    }

    private void OnDisable()
    {
        foreach (var entry in _ownedProbeReceivers)
        {
            var owned = entry.Value;

            if (restoreDefaultOnExit && _overlapCounts.ContainsKey(entry.Key))
                ApplyRenderingLayerToTarget(owned.targetRoot, _maskDefault, owned.targetRoot != null ? owned.targetRoot.name : null);

            RestoreProbePropertyBlocks(owned);
            if (owned.receiver == null)
                continue;

            if (Application.isPlaying)
                Destroy(owned.receiver);
            else
                DestroyImmediate(owned.receiver);
        }

        _ownedProbeReceivers.Clear();
        _overlapCounts.Clear();
    }
}
