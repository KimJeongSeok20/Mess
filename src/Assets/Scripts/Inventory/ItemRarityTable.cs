using System;
using UnityEngine;

/// <summary>
/// 아이템 인스턴스가 스폰될 때 굴릴 등급 확률표.
///
/// 등급은 아이템 "종류"가 아니라 "개체"의 속성이다.
/// 같은 AK라도 개체마다 Common부터 Legendary까지 다르게 나온다.
/// </summary>
[CreateAssetMenu(fileName = "ItemRarityTable", menuName = "Inventory/Rarity Table")]
public class ItemRarityTable : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public ItemRarity rarity;

        [Tooltip("상대 가중치. 합이 1일 필요는 없다. 0이면 나오지 않는다.")]
        [Min(0f)] public float weight;

        [Tooltip("이 등급이 가격에 주는 배수. 1이면 가격에 영향 없음.")]
        [Min(0f)] public float priceMultiplier;
    }

    [SerializeField]
    private Entry[] entries =
    {
        new Entry { rarity = ItemRarity.Common,    weight = 55f, priceMultiplier = 1f },
        new Entry { rarity = ItemRarity.Uncommon,  weight = 27f, priceMultiplier = 1f },
        new Entry { rarity = ItemRarity.Rare,      weight = 13f, priceMultiplier = 1f },
        new Entry { rarity = ItemRarity.Epic,      weight = 4f,  priceMultiplier = 1f },
        new Entry { rarity = ItemRarity.Legendary, weight = 1f,  priceMultiplier = 1f }
    };

    public Entry[] Entries => entries;

    /// <summary>가중치에 따라 등급 하나를 뽑는다.</summary>
    public ItemRarity Roll()
    {
        if (entries == null || entries.Length == 0)
            return ItemRarity.Common;

        float total = 0f;
        for (int i = 0; i < entries.Length; i++)
            total += Mathf.Max(0f, entries[i].weight);

        if (total <= 0f)
            return ItemRarity.Common;

        float pick = UnityEngine.Random.Range(0f, total);
        float cursor = 0f;

        for (int i = 0; i < entries.Length; i++)
        {
            cursor += Mathf.Max(0f, entries[i].weight);
            if (pick < cursor)
                return entries[i].rarity;
        }

        return entries[entries.Length - 1].rarity;
    }

    public float GetPriceMultiplier(ItemRarity rarity)
    {
        if (entries == null)
            return 1f;

        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].rarity == rarity)
                return Mathf.Max(0f, entries[i].priceMultiplier);
        }

        return 1f;
    }

    /// <summary>
    /// 확률표를 사람이 읽는 문자열로. 인스펙터 확인용.
    /// </summary>
    public string Describe()
    {
        if (entries == null || entries.Length == 0)
            return "(empty)";

        float total = 0f;
        for (int i = 0; i < entries.Length; i++)
            total += Mathf.Max(0f, entries[i].weight);

        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < entries.Length; i++)
        {
            float percent = total > 0f ? Mathf.Max(0f, entries[i].weight) / total * 100f : 0f;
            builder.Append(entries[i].rarity).Append('=').Append(percent.ToString("0.#")).Append("% ");
        }

        return builder.ToString().TrimEnd();
    }
}
