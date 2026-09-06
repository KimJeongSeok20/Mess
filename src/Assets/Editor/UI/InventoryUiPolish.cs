using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 리뷰 지적사항 P5·P6·P7 및 디자인 개선(대비 사다리 / 서체 / 빈 상태) 적용 도구.
///
/// ⚠️ 일회성 도구다. 실행하면 InventoryTheme.asset과 InventoryCanvas.prefab의 해당 항목을 덮어쓴다.
/// 값을 조정하고 싶으면 이 스크립트가 아니라 InventoryTheme.asset을 Inspector에서 수정할 것.
/// </summary>
public static class InventoryUiPolish
{
    private const string ThemePath = "Assets/UI/Inventory/InventoryTheme.asset";
    private const string CanvasPath = "Assets/UI/Inventory/InventoryCanvas.prefab";
    private const string DefinitionFolder = "Assets/UI/Inventory/Definitions";
    private const string ScenePath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";

    private const string BodyFontPath = "Assets/Runtime Debugger Toolkit/Resources/Font/Inter SDF.asset";
    private const string TitleFontPath = "Assets/Runtime Debugger Toolkit/Resources/Font/InterDisplay-Bold SDF.asset";

    // ─────────────────────────────────────────────────────────────
    // 1. 디자인 폴리시 (토큰 + 서체 + 잠금 슬롯 + 빈 상태)
    // ─────────────────────────────────────────────────────────────

    [MenuItem("Tools/UI/Inventory: Apply Design Polish")]
    public static void ApplyDesignPolish()
    {
        InventoryTheme theme = AssetDatabase.LoadAssetAtPath<InventoryTheme>(ThemePath);
        if (theme == null)
        {
            Debug.LogError($"[InventoryUiPolish] Theme not found at {ThemePath}");
            return;
        }

        ApplyContrastLadder(theme);
        ApplyFonts(theme);
        ApplyPanelOutlineSprite(theme);
        EditorUtility.SetDirty(theme);

        GameObject root = PrefabUtility.LoadPrefabContents(CanvasPath);
        try
        {
            RetypeAllText(root, theme);
            RestyleLegacyText(root, theme);
            EnsurePanelOutline(root, theme);
            EnsureDetailEmptyState(root, theme);
            PrefabUtility.SaveAsPrefabAsset(root, CanvasPath, out bool saved);
            Debug.Log($"[InventoryUiPolish] Canvas polished. saved={saved}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// 서페이스 명도 간격을 넓힌다. 기존 값은 단계 차이가 8 안팎이라 화면에서 전부 같은 톤으로 뭉개졌다.
    /// 아이템이 든 칸이 가장 밝아지도록 사다리를 재배치해 시선이 아이템으로 향하게 한다.
    /// </summary>
    private static void ApplyContrastLadder(InventoryTheme theme)
    {
        theme.dim = Rgba("07080A", 0.74f);
        theme.surface0 = Rgba("0E1114", 0.985f);   // 패널 (가장 어둡게)
        theme.surface1 = Rgba("1C2126", 1f);       // 빈 슬롯
        theme.surface2 = Rgba("2B3138", 1f);       // 아이템 있는 슬롯 (가장 밝게)
        theme.surface3 = Rgba("39414A", 1f);       // 호버
        theme.surfaceLocked = Rgba("0E1114", 0.35f); // 잠금: 패널에 거의 동화

        theme.borderSubtle = Rgba("333A41", 1f);
        theme.borderStrong = Rgba("4C555E", 1f);
        theme.borderLocked = Rgba("333A41", 0.32f);

        theme.textPrimary = Rgba("ECEEF0", 1f);
        theme.textSecondary = Rgba("9AA3AB", 1f);
        theme.textDisabled = Rgba("5B646C", 1f);
    }

    private static void ApplyFonts(InventoryTheme theme)
    {
        theme.bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath);
        theme.titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath);

        if (theme.bodyFont == null)
            Debug.LogWarning($"[InventoryUiPolish] Body font missing at {BodyFontPath}; keeping current font.");
        if (theme.titleFont == null)
            Debug.LogWarning($"[InventoryUiPolish] Title font missing at {TitleFontPath}; falling back to body font.");
    }

    private static void ApplyPanelOutlineSprite(InventoryTheme theme)
    {
        string path = NineSliceBaker.GeneratedFolder + "/ui_panel_outline_r10.png";
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite != null)
            theme.panelOutlineSprite = sprite;
        else
            Debug.LogWarning($"[InventoryUiPolish] Panel outline sprite missing at {path}. Run Tools/UI/Bake Inventory Sprites first.");
    }

    /// <summary>
    /// 인벤토리 캔버스의 모든 TMP 텍스트 서체를 교체한다.
    /// 기본 LiberationSans는 "Unity 기본 UI"로 읽혀 디자인이 미완성처럼 보이는 주요 원인이었다.
    /// </summary>
    private static void RetypeAllText(GameObject root, InventoryTheme theme)
    {
        if (theme.bodyFont == null)
            return;

        TMP_FontAsset titleFont = theme.titleFont != null ? theme.titleFont : theme.bodyFont;
        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);

        foreach (TMP_Text text in texts)
        {
            bool isTitle = text.name == "TitleText" || text.name == "DetailName";
            text.font = isTitle ? titleFont : theme.bodyFont;
        }

        Debug.Log($"[InventoryUiPolish] Retyped {texts.Length} text elements.");
    }

    /// <summary>
    /// P6: 통화/탄약 텍스트가 손글씨·장식 서체라 타이포 시스템과 충돌하던 문제.
    /// </summary>
    private static void RestyleLegacyText(GameObject root, InventoryTheme theme)
    {
        StyleByName(root, "CurrencyText", theme, theme.titleSize, theme.textPrimary, TextAlignmentOptions.Right);
        StyleByName(root, "AmmoText", theme, theme.titleSize, theme.textPrimary, TextAlignmentOptions.Right);
        StyleByName(root, "TotalWeightText", theme, theme.labelSize, theme.textSecondary, TextAlignmentOptions.Left);
    }

    private static void StyleByName(
        GameObject root,
        string name,
        InventoryTheme theme,
        float size,
        Color color,
        TextAlignmentOptions alignment)
    {
        Transform target = FindDeep(root.transform, name);
        if (target == null || !target.TryGetComponent(out TMP_Text text))
            return;

        if (theme.bodyFont != null)
            text.font = theme.bodyFont;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.fontStyle = FontStyles.Normal;
    }

    /// <summary>
    /// 패널 가장자리에 1px 윤곽을 넣어 배경과의 경계를 분명히 한다(figure/ground).
    /// </summary>
    private static void EnsurePanelOutline(GameObject root, InventoryTheme theme)
    {
        if (theme.panelOutlineSprite == null)
            return;

        Transform panel = FindDeep(root.transform, "Panel");
        if (panel == null)
            return;

        Transform existing = panel.Find("PanelOutline");
        GameObject outlineObject;
        if (existing != null)
        {
            outlineObject = existing.gameObject;
        }
        else
        {
            outlineObject = new GameObject("PanelOutline", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            outlineObject.transform.SetParent(panel, false);
        }

        RectTransform rect = outlineObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.SetAsLastSibling();

        Image image = outlineObject.GetComponent<Image>();
        image.sprite = theme.panelOutlineSprite;
        image.type = Image.Type.Sliced;
        image.color = theme.borderSubtle;
        image.raycastTarget = false;
    }

    /// <summary>
    /// 상세 패널이 비었을 때 구분선만 떠 있어 "고장난 화면"으로 보이던 문제를 고친다.
    /// </summary>
    private static void EnsureDetailEmptyState(GameObject root, InventoryTheme theme)
    {
        Transform detail = FindDeep(root.transform, "DetailPanel");
        if (detail == null)
            return;

        Transform existing = detail.Find("EmptyStateText");
        GameObject textObject;
        if (existing != null)
        {
            textObject = existing.gameObject;
        }
        else
        {
            textObject = new GameObject("EmptyStateText", typeof(RectTransform), typeof(CanvasRenderer));
            textObject.transform.SetParent(detail, false);
            textObject.AddComponent<TextMeshProUGUI>();
        }

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = new Vector2(theme.spacing24, theme.spacing24);
        rect.offsetMax = new Vector2(-theme.spacing24, -theme.spacing24);

        TMP_Text text = textObject.GetComponent<TMP_Text>();
        if (theme.bodyFont != null)
            text.font = theme.bodyFont;
        text.fontSize = theme.labelSize;
        text.color = theme.textDisabled;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.text = "Hover an item to inspect";

        InventoryDetailPanel panel = detail.GetComponent<InventoryDetailPanel>();
        if (panel == null)
            return;

        var serialized = new SerializedObject(panel);
        serialized.FindProperty("emptyStateText").objectReferenceValue = text;

        // 아이템이 없을 때는 구분선도 숨긴다.
        SerializedProperty contentOnly = serialized.FindProperty("contentOnlyObjects");
        var hidden = new List<GameObject>();
        Transform divider = FindDeep(detail, "DetailDivider");
        if (divider != null)
            hidden.Add(divider.gameObject);

        contentOnly.arraySize = hidden.Count;
        for (int i = 0; i < hidden.Count; i++)
            contentOnly.GetArrayElementAtIndex(i).objectReferenceValue = hidden[i];

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // ─────────────────────────────────────────────────────────────
    // 2. P5 — 등록된 아이템 전체에 ItemDefinition 생성/연결
    // ─────────────────────────────────────────────────────────────

    [MenuItem("Tools/UI/Inventory: Generate Item Definitions")]
    public static void GenerateItemDefinitions()
    {
        Directory.CreateDirectory(DefinitionFolder);

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        int created = 0;
        int filled = 0;
        int linked = 0;

        try
        {
            InventoryManager manager = FindManager(scene);
            if (manager == null)
            {
                Debug.LogError("[InventoryUiPolish] InventoryManager not found in StartMap.");
                return;
            }

            var serialized = new SerializedObject(manager);
            SerializedProperty allItems = serialized.FindProperty("allItems");

            for (int i = 0; i < allItems.arraySize; i++)
            {
                var item = allItems.GetArrayElementAtIndex(i).objectReferenceValue as Item;
                if (item == null)
                    continue;

                string assetPath = AssetDatabase.GetAssetPath(item);
                if (string.IsNullOrEmpty(assetPath))
                    continue;

                var itemSerialized = new SerializedObject(item);
                SerializedProperty definitionProperty = itemSerialized.FindProperty("definition");
                var definition = definitionProperty.objectReferenceValue as ItemDefinition;

                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    string path = AssetDatabase.GenerateUniqueAssetPath(
                        $"{DefinitionFolder}/{Sanitize(item.ItemName)}.asset");
                    AssetDatabase.CreateAsset(definition, path);
                    created++;

                    definitionProperty.objectReferenceValue = definition;
                    itemSerialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(item);
                    linked++;
                }

                if (PopulateDefinition(definition, item))
                {
                    EditorUtility.SetDirty(definition);
                    filled++;
                }
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }

        int swept = SweepOrphanDefinitions();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[InventoryUiPolish] Definitions created={created} linked={linked} populated={filled} orphansFixed={swept}");
    }

    /// <summary>
    /// StartMap의 allItems에 없는(다른 프리팹에만 연결된) 정의 자산의 빈 필드를 보충한다.
    /// weight=0이면 상세 패널에 "0 kg"으로 표시되고 총중량 계산에서도 빠지기 때문.
    /// </summary>
    private static int SweepOrphanDefinitions()
    {
        int fixedCount = 0;
        string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { DefinitionFolder });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (definition == null)
                continue;

            bool changed = false;

            if (definition.weight <= 0f)
            {
                definition.weight = DefaultWeight(definition.category);
                changed = true;
            }

            if (definition.maxStack <= 0)
            {
                definition.maxStack = definition.category == ItemCategory.Ammo
                    || definition.category == ItemCategory.Material ? 20 : 1;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(definition.description))
            {
                definition.description = DefaultDescription(definition.category, definition.displayName);
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(definition);
                fixedCount++;
            }
        }

        return fixedCount;
    }

    /// <summary>
    /// 비어 있는 필드만 채운다. 이미 손으로 넣은 값은 덮어쓰지 않는다.
    /// rarity/description은 가격·종류 기반 자동 추정값이므로 최종 확인이 필요하다.
    /// </summary>
    private static bool PopulateDefinition(ItemDefinition definition, Item item)
    {
        bool changed = false;

        if (string.IsNullOrWhiteSpace(definition.displayName))
        {
            definition.displayName = item.ItemName;
            changed = true;
        }

        if (definition.icon == null && item.ItemPicture != null)
        {
            definition.icon = item.ItemPicture;
            changed = true;
        }

        ItemCategory category = ResolveCategory(item);
        if (definition.category != category)
        {
            definition.category = category;
            changed = true;
        }

        ItemRarity rarity = ResolveRarity(item);
        if (definition.rarity != rarity)
        {
            definition.rarity = rarity;
            changed = true;
        }

        if (definition.weight <= 0f)
        {
            definition.weight = DefaultWeight(category);
            changed = true;
        }

        if (definition.maxStack <= 0)
        {
            definition.maxStack = category == ItemCategory.Ammo || category == ItemCategory.Material ? 20 : 1;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(definition.description))
        {
            definition.description = DefaultDescription(category, definition.displayName);
            changed = true;
        }

        if ((definition.statLines == null || definition.statLines.Length == 0)
            && item is WeaponItem weaponItem
            && weaponItem.WeaponData != null)
        {
            definition.statLines = weaponItem.WeaponData.CreateStatLines();
            changed = true;
        }

        return changed;
    }

    private static ItemCategory ResolveCategory(Item item)
    {
        string name = item.ItemName != null ? item.ItemName.ToLowerInvariant() : string.Empty;

        // 이름 기반 판정을 타입 판정보다 먼저 한다.
        // 주문 아이템도 프리팹에서는 WeaponItem + WeaponData 조합이라 타입만으로는 구분되지 않는다.
        if (name.Contains("ammo") || name.Contains("magazine") || name.Contains("탄약"))
            return ItemCategory.Ammo;
        if (name.Contains("spell") || name.Contains("scroll") || name.Contains("fireball"))
            return ItemCategory.Spell;
        if (item is WeaponItem)
            return ItemCategory.Weapon;
        if (name.Contains("potion") || name.Contains("medkit") || name.Contains("bandage") || name.Contains("food"))
            return ItemCategory.Consumable;
        if (name.Contains("key") || name.Contains("quest"))
            return ItemCategory.Quest;

        return ItemCategory.Material;
    }

    /// <summary>
    /// 등급 추정. Item.price는 SyncVar라 프리팹 자산에서는 0으로 읽히므로,
    /// 직렬화된 minPrice/maxPrice의 중앙값을 우선 사용한다.
    /// 어디까지나 자동 추정값이며 최종 밸런스는 사람이 확인해야 한다.
    /// </summary>
    private static ItemRarity ResolveRarity(Item item)
    {
        int reference = item.Price;

        if (reference <= 0)
        {
            var serialized = new SerializedObject(item);
            SerializedProperty min = serialized.FindProperty("minPrice");
            SerializedProperty max = serialized.FindProperty("maxPrice");

            if (min != null && max != null)
                reference = Mathf.RoundToInt((min.intValue + max.intValue) * 0.5f);
            else if (min != null)
                reference = min.intValue;
        }

        if (reference >= 5000) return ItemRarity.Legendary;
        if (reference >= 2000) return ItemRarity.Epic;
        if (reference >= 800) return ItemRarity.Rare;
        if (reference >= 300) return ItemRarity.Uncommon;
        return ItemRarity.Common;
    }

    private static float DefaultWeight(ItemCategory category)
    {
        switch (category)
        {
            case ItemCategory.Weapon: return 3.5f;
            case ItemCategory.Ammo: return 0.4f;
            case ItemCategory.Consumable: return 0.3f;
            case ItemCategory.Spell: return 0.5f;
            case ItemCategory.Quest: return 0.1f;
            default: return 1f;
        }
    }

    private static string DefaultDescription(ItemCategory category, string displayName)
    {
        switch (category)
        {
            case ItemCategory.Weapon:
                return $"{displayName}. Standard-issue firearm recovered from the field.";
            case ItemCategory.Ammo:
                return "Ammunition. Reloads a compatible weapon.";
            case ItemCategory.Consumable:
                return "Single-use supply. Consumed on activation.";
            case ItemCategory.Spell:
                return "Bound spell. Expends charges when cast.";
            case ItemCategory.Quest:
                return "Objective item. Cannot be sold.";
            default:
                return "Salvaged material. Sells for scrap value.";
        }
    }

    // ─────────────────────────────────────────────────────────────

    private static InventoryManager FindManager(Scene scene)
    {
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            var manager = rootObject.GetComponentInChildren<InventoryManager>(true);
            if (manager != null)
                return manager;
        }

        return null;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Item";

        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return value;
    }

    private static Color Rgba(string hex, float alpha)
    {
        int r = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Color(r / 255f, g / 255f, b / 255f, alpha);
    }
}
