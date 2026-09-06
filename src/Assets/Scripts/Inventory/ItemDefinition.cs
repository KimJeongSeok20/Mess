using System;
using UnityEngine;

public enum ItemRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}

public enum ItemCategory
{
    Weapon,
    Spell,
    Ammo,
    Consumable,
    Material,
    Quest,
    Corpse
}

[Serializable]
public struct StatLine
{
    public string label;
    public string value;

    public StatLine(string label, string value)
    {
        this.label = label;
        this.value = value;
    }
}

[CreateAssetMenu(fileName = "ItemDefinition", menuName = "Inventory/Item Definition")]
public class ItemDefinition : ScriptableObject
{
    public string displayName;
    public Sprite icon;

    [Tooltip("등급을 개체마다 무작위로 굴릴지 여부. 끄면 아래 rarity 값으로 고정된다.\n" +
             "탄약·퀘스트 아이템처럼 등급 개념이 없는 것은 꺼 둔다.")]
    public bool rollRarity = true;

    [Tooltip("rollRarity가 꺼져 있거나 아직 굴리지 않은 개체가 쓸 기본 등급.")]
    public ItemRarity rarity;

    [Tooltip("비워 두면 프로젝트 기본 확률표를 쓴다. 아이템별로 다른 표를 주고 싶을 때만 지정.")]
    public ItemRarityTable rarityTable;

    public ItemCategory category;
    [TextArea] public string description;
    public float weight;
    public int maxStack = 1;
    public StatLine[] statLines;
}
