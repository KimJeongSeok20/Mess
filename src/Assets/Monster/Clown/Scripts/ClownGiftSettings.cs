using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class ClownGiftEffectWeight
{
    public GiftEffectType effect = GiftEffectType.Knockback;
    [Min(0f)] public float weight = 1f;
}

[Serializable]
public sealed class ClownGiftItemDropWeight
{
    public Item item;
    [Min(0f)] public float weight = 1f;
}

[Serializable]
public sealed class ClownGiftSettings
{
    [Header("Explosion")]
    [Min(0.05f)] public float fuseDelay = 1.5f;
    [Min(0.1f)] public float explosionRadius = 3f;
    public GameObject explosionEffectPrefab;
    [Min(0.1f)] public float explosionEffectLifetime = 3f;
    [Min(0.01f)] public float explosionEffectBaseRadius = 8f;
    public Color flashColor = new(1f, 1f, 1f, 1f);
    [Range(0f, 1f)] public float flashMaxAlpha = 0.45f;
    [Min(0.05f)] public float flashDuration = 0.25f;
    [Min(0f)] public float minArmDelay = 0.18f;
    [Min(0f)] public float minArmImpactSpeed = 2.5f;
    [Min(0.01f)] public float playerImpactHoldDuration = 0.12f;

    [Header("Effect Weights")]
    public ClownGiftEffectWeight[] effectWeights = CreateDefaultEffectWeights();

    [Header("Damage")]
    [Min(1)] public int damage = 28;

    [Header("Knockback")]
    [Min(0f)] public float knockbackDistance = 3.5f;
    [Min(0.05f)] public float knockbackDuration = 0.25f;
    [Min(0f)] public float knockbackImpulse = 12f;
    [Min(0f)] public float knockbackUpwardImpulse = 2.5f;
    [Min(0.01f)] public float knockbackDamping = 10f;

    [Header("Slow")]
    [Range(0.1f, 1f)] public float slowMultiplier = 0.45f;
    [Min(0.05f)] public float slowDuration = 2.5f;

    [Header("Teleport")]
    public float teleportVerticalOffset = 0.15f;
    [Min(0.1f)] public float teleportNavMeshSampleRadius = 5f;

    [Header("Random Item Drops")]
    public SpawnSelector randomItemSpawnSelector;
    public ItemSpawnlist randomItemSpawnList;
    public ClownGiftItemDropWeight[] randomItemDropWeights = Array.Empty<ClownGiftItemDropWeight>();

    public void SyncRandomItemDropWeightsFromSources()
    {
        if (randomItemSpawnSelector == null && randomItemSpawnList == null)
            return;

        ItemSpawnlist source = randomItemSpawnList;
        if (randomItemSpawnSelector != null)
            source = randomItemSpawnSelector.Source;

        if (source == null || source.Items == null)
            return;

        var entries = new List<ClownGiftItemDropWeight>();
        var seen = new HashSet<Item>();

        for (int i = 0; i < source.Items.Count; i++)
        {
            var entry = source.Items[i];
            GameObject prefab = entry?.prefab;
            if (prefab == null || !prefab.TryGetComponent<Item>(out var item) || item == null)
                continue;

            if (!seen.Add(item))
                continue;

            entries.Add(new ClownGiftItemDropWeight
            {
                item = item,
                weight = Mathf.Max(0f, entry.weight)
            });
        }

        randomItemDropWeights = entries.ToArray();
    }

    public static ClownGiftEffectWeight[] CreateDefaultEffectWeights()
    {
        return new[]
        {
            new ClownGiftEffectWeight { effect = GiftEffectType.Knockback, weight = 4f },
            new ClownGiftEffectWeight { effect = GiftEffectType.Slow, weight = 2.5f },
            new ClownGiftEffectWeight { effect = GiftEffectType.Teleport, weight = 1f },
            new ClownGiftEffectWeight { effect = GiftEffectType.RandomItem, weight = 1f }
        };
    }
}
