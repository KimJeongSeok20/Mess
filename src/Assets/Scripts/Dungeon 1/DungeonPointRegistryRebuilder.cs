using UnityEngine;

public sealed class DungeonPointRegistryRebuilder : MonoBehaviour
{
    [Tooltip("보통 RuntimeDungeon(생성된 던전 루트) Transform을 넣어줘")]
    [SerializeField] private Transform dungeonRoot;

    [ContextMenu("Rebuild Now")]
    public void RebuildNow()
    {
        if (dungeonRoot == null) dungeonRoot = transform;
        DungeonPointRegistry.RebuildFromRoot(dungeonRoot);
    }
}
