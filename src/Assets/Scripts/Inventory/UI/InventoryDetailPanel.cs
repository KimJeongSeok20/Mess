using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InventoryDetailPanel : MonoBehaviour
{
    private const int MaxStatRows = 6;

    [Serializable]
    public struct StatRow
    {
        public GameObject root;
        public TMP_Text labelText;
        public TMP_Text valueText;
    }

    [Header("Theme")]
    [SerializeField] private InventoryTheme theme;

    [Header("Authored References")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text categoryText;
    [SerializeField] private TMP_Text rarityText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text priceText;
    [SerializeField] private RawImage priceCurrencyIcon;
    [SerializeField] private TMP_Text weightText;
    [SerializeField] private StatRow[] statRows;
    [SerializeField] private Image[] conditionPips;

    [Header("Empty State")]
    [Tooltip("표시할 아이템이 없을 때 켜지는 안내 텍스트. 빈 패널이 미완성으로 보이지 않게 한다.")]
    [SerializeField] private TMP_Text emptyStateText;
    [SerializeField] private string emptyStateMessage = "Hover an item to inspect";
    [Tooltip("아이템이 있을 때만 보여줄 장식 요소(구분선 등). 빈 상태에서는 숨긴다.")]
    [SerializeField] private GameObject[] contentOnlyObjects;

    private InventoryItem _hoveredItem;
    private InventoryManager _inventoryManager;

    private void Awake()
    {
        ApplyTheme();
        ShowEmptyState();
    }

    private void OnEnable()
    {
        ActionSlot.OnSlotActivated += HandleActionSlotActivated;
        BindInventoryManager();
        RefreshIdlePresentation();
    }

    private void Start()
    {
        BindInventoryManager();
        RefreshIdlePresentation();
    }

    private void OnDisable()
    {
        ActionSlot.OnSlotActivated -= HandleActionSlotActivated;

        if (_inventoryManager != null)
            _inventoryManager.InventoryChanged -= HandleInventoryChanged;

        _inventoryManager = null;
    }

    public void Present(
        InventoryItem source,
        ItemDefinition definition,
        string legacyDisplayName,
        Sprite legacyIcon,
        int price,
        int upgradeTier = 0,
        ItemRarity? instanceRarity = null)
    {
        _hoveredItem = source;
        PresentContent(
            definition,
            legacyDisplayName,
            legacyIcon,
            price,
            upgradeTier,
            instanceRarity);
    }

    internal void PresentFallback(
        InventoryItem source,
        ItemDefinition definition,
        string legacyDisplayName,
        Sprite legacyIcon,
        int price,
        int upgradeTier,
        ItemRarity? instanceRarity)
    {
        if (_hoveredItem != null || source == null)
            return;

        PresentContent(
            definition,
            legacyDisplayName,
            legacyIcon,
            price,
            upgradeTier,
            instanceRarity);
    }

    private void PresentContent(
        ItemDefinition definition,
        string legacyDisplayName,
        Sprite legacyIcon,
        int price,
        int upgradeTier,
        ItemRarity? instanceRarity)
    {

        string resolvedName = definition != null && !string.IsNullOrWhiteSpace(definition.displayName)
            ? definition.displayName
            : legacyDisplayName;
        if (upgradeTier > 0)
        {
            string tierSuffix = " +" + upgradeTier.ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(resolvedName)
                || !resolvedName.EndsWith(tierSuffix, StringComparison.Ordinal))
            {
                resolvedName += tierSuffix;
            }
        }

        Sprite resolvedIcon = definition != null && definition.icon != null
            ? definition.icon
            : legacyIcon;
        SetIcon(resolvedIcon);

        if (nameText != null)
            nameText.text = resolvedName ?? string.Empty;
        if (categoryText != null)
            categoryText.text = definition != null
                ? definition.category.ToString().ToUpperInvariant()
                : string.Empty;
        // 등급은 아이템 종류가 아니라 개체의 속성이다. 넘어온 개체 등급을 우선한다.
        ItemRarity resolvedRarity = instanceRarity
            ?? (definition != null ? definition.rarity : ItemRarity.Common);

        if (rarityText != null)
            rarityText.text = definition != null || instanceRarity.HasValue
                ? resolvedRarity.ToString().ToUpperInvariant()
                : string.Empty;
        if (descriptionText != null)
            descriptionText.text = definition != null
                ? definition.description ?? string.Empty
                : string.Empty;
        if (priceText != null)
        {
            priceText.text = price.ToString("N0", CultureInfo.InvariantCulture);
            priceText.gameObject.SetActive(true);
        }
        if (priceCurrencyIcon != null)
            priceCurrencyIcon.gameObject.SetActive(true);
        LayoutPriceCurrency();
        if (weightText != null)
        {
            weightText.text = string.Empty;
            weightText.gameObject.SetActive(false);
        }

        ApplyRarityColor(resolvedRarity);
        PresentStats(definition != null ? definition.statLines : null);
        PresentCondition(definition);
        SetEmptyStateVisible(false);
    }

    public void Clear(InventoryItem source)
    {
        if (source != null && _hoveredItem != source)
            return;

        _hoveredItem = null;
        RefreshIdlePresentation();
    }

    public void Clear()
    {
        _hoveredItem = null;
        RefreshIdlePresentation();
    }

    private void BindInventoryManager()
    {
        InventoryManager manager = GetComponentInParent<InventoryManager>(true);
        if (_inventoryManager == manager)
            return;

        if (_inventoryManager != null)
            _inventoryManager.InventoryChanged -= HandleInventoryChanged;

        _inventoryManager = manager;
        if (_inventoryManager != null)
            _inventoryManager.InventoryChanged += HandleInventoryChanged;
    }

    private void HandleActionSlotActivated(ActionSlot actionSlot)
    {
        if (_hoveredItem != null)
            return;

        InventorySlot slot = actionSlot != null
            ? actionSlot.GetComponent<InventorySlot>()
            : null;
        ShowFallbackOrEmpty(slot != null ? slot.Item : null);
    }

    private void HandleInventoryChanged()
    {
        RefreshIdlePresentation();
    }

    private void RefreshIdlePresentation()
    {
        if (_hoveredItem != null)
            return;

        BindInventoryManager();

        InventoryItem activeItem = null;
        if (_inventoryManager != null)
        {
            InventoryManager.InventoryItemData? activeData = _inventoryManager.GetActiveItemData();
            if (activeData.HasValue)
                activeItem = activeData.Value.inventoryItem;
        }

        ShowFallbackOrEmpty(activeItem);
    }

    private void ShowFallbackOrEmpty(InventoryItem item)
    {
        if (item != null)
        {
            item.PresentFallback(this);
            return;
        }

        ShowEmptyState();
    }

    private void ShowEmptyState()
    {
        SetIcon(null);

        SetText(nameText, string.Empty);
        SetText(categoryText, string.Empty);
        SetText(rarityText, string.Empty);
        SetText(descriptionText, string.Empty);
        SetText(priceText, string.Empty);
        if (priceText != null)
            priceText.gameObject.SetActive(false);
        if (priceCurrencyIcon != null)
            priceCurrencyIcon.gameObject.SetActive(false);
        SetText(weightText, string.Empty);
        if (weightText != null)
            weightText.gameObject.SetActive(false);
        PresentStats(null);
        PresentCondition(null);
        SetEmptyStateVisible(true);
    }

    private void LayoutPriceCurrency()
    {
        if (priceText == null || priceCurrencyIcon == null)
            return;

        priceText.ForceMeshUpdate();
        float textWidth = Mathf.Ceil(priceText.GetPreferredValues(priceText.text).x) + 2f;
        RectTransform priceRect = priceText.rectTransform;
        priceRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);

        RectTransform iconRect = priceCurrencyIcon.rectTransform;
        iconRect.anchorMin = priceRect.anchorMin;
        iconRect.anchorMax = priceRect.anchorMax;
        iconRect.pivot = priceRect.pivot;

        const float rowStartX = 0f;
        const float spacing = 6f;
        float priceY = priceRect.anchoredPosition.y;
        float iconY = priceY + priceText.textBounds.center.y;

        priceRect.anchoredPosition = new Vector2(rowStartX, priceY);
        iconRect.anchoredPosition = new Vector2(rowStartX + textWidth + spacing, iconY);
    }

    private void SetEmptyStateVisible(bool visible)
    {
        if (emptyStateText != null)
        {
            emptyStateText.text = visible ? emptyStateMessage : string.Empty;
            if (theme != null)
                emptyStateText.color = theme.textDisabled;
            emptyStateText.gameObject.SetActive(visible);
        }

        if (contentOnlyObjects == null)
            return;

        for (int i = 0; i < contentOnlyObjects.Length; i++)
        {
            if (contentOnlyObjects[i] != null)
                contentOnlyObjects[i].SetActive(!visible);
        }
    }

    private void SetIcon(Sprite sprite)
    {
        if (iconImage == null)
            return;

        iconImage.sprite = sprite;
        iconImage.enabled = sprite != null;
    }

    private void PresentStats(StatLine[] stats)
    {
        if (statRows == null)
            return;

        int statCount = stats != null ? stats.Length : 0;
        int visibleCount = Mathf.Min(MaxStatRows, Mathf.Min(statCount, statRows.Length));

        for (int i = 0; i < statRows.Length; i++)
        {
            bool show = i < visibleCount;
            SetStatRowActive(statRows[i], show);

            if (!show)
                continue;

            SetText(statRows[i].labelText, stats[i].label ?? string.Empty);
            SetText(statRows[i].valueText, stats[i].value ?? string.Empty);
        }
    }

    private static void SetStatRowActive(StatRow row, bool active)
    {
        if (row.root != null)
        {
            row.root.SetActive(active);
            return;
        }

        if (row.labelText != null)
            row.labelText.gameObject.SetActive(active);

        GameObject labelObject = row.labelText != null
            ? row.labelText.gameObject
            : null;
        if (row.valueText != null && row.valueText.gameObject != labelObject)
            row.valueText.gameObject.SetActive(active);
    }

    private void PresentCondition(ItemDefinition definition)
    {
        if (conditionPips == null || conditionPips.Length == 0)
            return;

        bool hasItem = definition != null;
        float normalized = hasItem ? ResolveCondition(definition.statLines) : 0f;
        int litCount = hasItem
            ? Mathf.Clamp(Mathf.RoundToInt(normalized * conditionPips.Length), 1, conditionPips.Length)
            : 0;

        for (int i = 0; i < conditionPips.Length; i++)
        {
            Image pip = conditionPips[i];
            if (pip == null)
                continue;

            pip.enabled = hasItem;
            if (theme != null)
                pip.color = i < litCount ? theme.success : theme.borderLocked;
        }
    }

    private static float ResolveCondition(StatLine[] stats)
    {
        if (stats == null)
            return 1f;

        for (int i = 0; i < stats.Length; i++)
        {
            string label = stats[i].label ?? string.Empty;
            if (label.IndexOf("condition", StringComparison.OrdinalIgnoreCase) < 0
                && label.IndexOf("durability", StringComparison.OrdinalIgnoreCase) < 0
                && label.IndexOf("battery", StringComparison.OrdinalIgnoreCase) < 0
                && label.IndexOf("signal", StringComparison.OrdinalIgnoreCase) < 0
                && label.IndexOf("charge", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (TryParseCondition(stats[i].value, out float normalized))
                return normalized;
        }

        return 1f;
    }

    private static bool TryParseCondition(string value, out float normalized)
    {
        normalized = 1f;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim();
        int slashIndex = trimmed.IndexOf('/');
        if (slashIndex > 0
            && TryReadNumber(trimmed.Substring(0, slashIndex), out float current)
            && TryReadNumber(trimmed.Substring(slashIndex + 1), out float maximum)
            && maximum > Mathf.Epsilon)
        {
            normalized = Mathf.Clamp01(current / maximum);
            return true;
        }

        if (!TryReadNumber(trimmed, out float scalar))
            return false;

        normalized = Mathf.Clamp01(scalar > 1f ? scalar / 100f : scalar);
        return true;
    }

    private static bool TryReadNumber(string value, out float number)
    {
        number = 0f;
        if (string.IsNullOrEmpty(value))
            return false;

        int start = -1;
        int length = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            bool numeric = char.IsDigit(character) || character == '.' || character == ',';
            if (numeric)
            {
                if (start < 0)
                    start = i;
                length++;
            }
            else if (start >= 0)
            {
                break;
            }
        }

        if (start < 0 || length == 0)
            return false;

        string token = value.Substring(start, length).Replace(',', '.');
        return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private void ApplyTheme()
    {
        if (theme == null)
            return;

        SetColor(nameText, theme.textPrimary);
        SetColor(categoryText, theme.textSecondary);
        SetColor(rarityText, theme.rarityCommon);
        SetColor(descriptionText, theme.textSecondary);
        SetColor(priceText, theme.textPrimary);
        SetColor(weightText, theme.textSecondary);

        if (statRows == null)
            return;

        for (int i = 0; i < statRows.Length; i++)
        {
            SetColor(statRows[i].labelText, theme.textSecondary);
            SetColor(statRows[i].valueText, theme.textPrimary);
        }
    }

    private void ApplyRarityColor(ItemRarity rarity)
    {
        if (theme == null)
            return;

        Color rarityColor = GetRarityColor(rarity);
        // Common 아이템까지 이름 전체가 회색으로 죽으면 상세 패널의 시선 시작점이 사라진다.
        // 일반 등급 이름은 주 텍스트로 유지하고, 희귀 등급부터만 이름에 등급색을 준다.
        SetColor(nameText, rarity == ItemRarity.Common ? theme.textPrimary : rarityColor);
        SetColor(rarityText, rarityColor);
    }

    private Color GetRarityColor(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Uncommon => theme.rarityUncommon,
            ItemRarity.Rare => theme.rarityRare,
            ItemRarity.Epic => theme.rarityEpic,
            ItemRarity.Legendary => theme.rarityLegendary,
            _ => theme.rarityCommon
        };
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null)
            target.text = value;
    }

    private static void SetColor(TMP_Text target, Color color)
    {
        if (target != null)
            target.color = color;
    }
}
