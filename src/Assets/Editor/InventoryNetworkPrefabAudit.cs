using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PurrNet;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class InventoryNetworkPrefabAudit
{
    private const string RegistryPath = "Assets/Prefabs/DoorNextToDungeonPrefab.asset";

    public static GameObject[] RequiredItems()
    {
        if (SceneManager.GetActiveScene().path != "Assets/SceneTemplateAssets/Scenes/StartMap.unity")
            throw new InvalidOperationException("Open StartMap before auditing its item catalog.");
        var items = new HashSet<GameObject>();
        void Add(GameObject prefab) { if (prefab != null && prefab.GetComponent<Item>() != null) items.Add(prefab); }
        var inventory = UnityEngine.Object.FindFirstObjectByType<InventoryManager>();
        var all = new SerializedObject(inventory).FindProperty("allItems");
        for (int i = 0; i < all.arraySize; i++)
            if (all.GetArrayElementAtIndex(i).objectReferenceValue is Item item) Add(item.gameObject);

        var controller = UnityEngine.Object.FindFirstObjectByType<NetworkDungeonController>();
        foreach (var map in controller.MapList.Entries)
            if (map != null && map.selector != null && map.selector.Source != null)
                foreach (var loot in map.selector.Source.Items) if (loot != null) Add(loot.prefab);

        foreach (var station in UnityEngine.Object.FindObjectsByType<GiftBox>(FindObjectsSortMode.None))
            if (new SerializedObject(station).FindProperty("giftBoxItemPrefab").objectReferenceValue is Item gift) Add(gift.gameObject);
        foreach (var shop in UnityEngine.Object.FindObjectsByType<WeaponShop>(FindObjectsSortMode.None))
        {
            var catalog = new SerializedObject(shop).FindProperty("shopPrefabs");
            for (int i = 0; i < catalog.arraySize; i++)
                if (catalog.GetArrayElementAtIndex(i).objectReferenceValue is GameObject prefab
                    && prefab.TryGetComponent<WeaponShopItem>(out var shopItem)) Add(shopItem.PurchasedItemPrefab);
        }
        return items.OrderBy(AssetDatabase.GetAssetPath, StringComparer.Ordinal).ToArray();
    }

    public static string[] RegisterMissing()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Register in Edit Mode, then restart all peers.");
        var registry = AssetDatabase.LoadAssetAtPath<NetworkPrefabs>(RegistryPath);
        var missing = RequiredItems().Where(p => !registry.prefabs.Any(e => e.prefab == p)).ToArray();
        Undo.RecordObject(registry, "Register production inventory items");
        // Append only: never reorder existing IDs or remove old null slots in a running protocol.
        foreach (var prefab in missing) registry.prefabs.Add(new NetworkPrefabs.UserPrefabData { prefab = prefab });
        registry.Refresh();
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssetIfDirty(registry);
        return missing.Select(AssetDatabase.GetAssetPath).ToArray();
    }

    [Test]
    public static void StartMapProcessorOrbsAreRegisteredForMultiplayer()
    {
        var registry = AssetDatabase.LoadAssetAtPath<NetworkPrefabs>(RegistryPath);
        var processors = UnityEngine.Object.FindObjectsByType<CorpseProcessor>(FindObjectsSortMode.None);
        Assert.That(processors, Is.Not.Empty);
        foreach (var processor in processors)
        {
            var orb = new SerializedObject(processor).FindProperty("processedOrbPrefab").objectReferenceValue as SkillPointOrb;
            Assert.That(orb, Is.Not.Null);
            Assert.That(registry.prefabs.Any(e => e.prefab == orb.gameObject), Is.True,
                "Processor rewards must be visible and collectible on every peer.");
        }
    }

    [Test]
    public static void StartMapItemSourcesAreRegisteredForMultiplayer()
    {
        var registry = AssetDatabase.LoadAssetAtPath<NetworkPrefabs>(RegistryPath);
        var missing = RequiredItems().Where(p => !registry.prefabs.Any(e => e.prefab == p)).Select(AssetDatabase.GetAssetPath);
        Assert.That(missing, Is.Empty, "A server-only pickup cannot receive an inventory receipt on clients.");
    }

    [Test]
    public static void StartMapEnforcesRpcOwnershipAndServerSpawning()
    {
        var manager = UnityEngine.Object.FindAnyObjectByType<NetworkManager>();
        var reference = new SerializedObject(manager).FindProperty("_networkRules").objectReferenceValue;
        Assert.That(AssetDatabase.GetAssetPath(reference), Is.EqualTo("Assets/Settings/StillWorkingNetworkRules.asset"));
        var rules = (NetworkRules)reference;
        Assert.That(rules.ShouldIgnoreRequireOwner(), Is.False);
        Assert.That(rules.ShouldIgnoreRequireServer(), Is.False);
        Assert.That(rules.HasSpawnAuthority(manager, false), Is.False);
        Assert.That(rules.HasSpawnAuthority(manager, true), Is.True);
    }
}
