using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 등급 무작위 굴림 설정 도구.
///
/// 등급은 아이템 "종류"가 아니라 "개체"의 속성이다. 같은 AK라도 개체마다 등급이 다르게 나온다.
/// 이 도구는 프로젝트 기본 확률표를 만들고, 어떤 카테고리가 등급을 굴릴지 정리한다.
///
/// 메뉴: Tools/UI/Inventory: Setup Rarity Rolls
/// </summary>
public static class InventoryRaritySetup
{
    private const string ResourcesFolder = "Assets/Resources";
    private const string TablePath = ResourcesFolder + "/ItemRarityTable.asset";
    private const string DefinitionFolder = "Assets/UI/Inventory/Definitions";

    [MenuItem("Tools/UI/Inventory: Setup Rarity Rolls")]
    public static void Setup()
    {
        ItemRarityTable table = CreateOrLoadTable();
        int rolling = 0;
        int fixedRarity = 0;

        string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { DefinitionFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (definition == null)
                continue;

            bool shouldRoll = ShouldRollRarity(definition.category);
            if (definition.rollRarity != shouldRoll)
            {
                definition.rollRarity = shouldRoll;
                EditorUtility.SetDirty(definition);
            }

            if (shouldRoll)
            {
                rolling++;

                // 굴리는 아이템의 rarity 필드는 "아직 굴리지 않은 개체"의 폴백일 뿐이므로
                // 가장 흔한 등급으로 맞춰 둔다. 실제 표시 등급은 개체가 정한다.
                if (definition.rarity != ItemRarity.Common)
                {
                    definition.rarity = ItemRarity.Common;
                    EditorUtility.SetDirty(definition);
                }
            }
            else
            {
                fixedRarity++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[InventoryRaritySetup] table={TablePath} rolling={rolling} fixed={fixedRarity}\n"
                  + $"  distribution: {table.Describe()}");
    }

    /// <summary>
    /// 무기·주문만 등급을 굴린다.
    /// 탄약·재료·퀘스트 아이템은 "전설급 탄약" 같은 게 말이 되지 않으므로 고정한다.
    /// 소모품까지 굴리고 싶으면 여기만 고치면 된다.
    /// </summary>
    private static bool ShouldRollRarity(ItemCategory category)
    {
        return category == ItemCategory.Weapon || category == ItemCategory.Spell;
    }

    private static ItemRarityTable CreateOrLoadTable()
    {
        var existing = AssetDatabase.LoadAssetAtPath<ItemRarityTable>(TablePath);
        if (existing != null)
            return existing;

        Directory.CreateDirectory(ResourcesFolder);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var table = ScriptableObject.CreateInstance<ItemRarityTable>();
        AssetDatabase.CreateAsset(table, TablePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[InventoryRaritySetup] Created default rarity table at {TablePath}");
        return table;
    }

    /// <summary>
    /// 확률표가 의도대로 나오는지 표본을 뽑아 확인한다. 밸런스 감 잡을 때 쓴다.
    /// </summary>
    [MenuItem("Tools/UI/Inventory: Sample Rarity Distribution (10k rolls)")]
    public static void SampleDistribution()
    {
        var table = AssetDatabase.LoadAssetAtPath<ItemRarityTable>(TablePath);
        if (table == null)
        {
            Debug.LogError($"[InventoryRaritySetup] {TablePath} not found. Run Setup Rarity Rolls first.");
            return;
        }

        const int samples = 10000;
        var counts = new int[System.Enum.GetValues(typeof(ItemRarity)).Length];

        for (int i = 0; i < samples; i++)
            counts[(int)table.Roll()]++;

        var builder = new System.Text.StringBuilder();
        builder.Append($"[InventoryRaritySetup] {samples} rolls → ");
        for (int i = 0; i < counts.Length; i++)
        {
            builder.Append((ItemRarity)i).Append('=')
                .Append((counts[i] / (float)samples * 100f).ToString("0.##")).Append("%  ");
        }

        Debug.Log(builder.ToString().TrimEnd());
    }
}
