using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PurrNet;

public class InventoryItem : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Theme")]
    [SerializeField] private InventoryTheme theme;

    [Header("Authored Cell References")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text quantityLabel;
    [SerializeField] private TMP_Text upgradeLabel;
    [SerializeField] private Image rarityBorder;

    [Header("Detail Panel")]
    [SerializeField] private InventoryDetailPanel detailPanel;

    private RectTransform _rectTransform;
    private CanvasGroup _canvasGroup;
    private Transform _originalParent;
    private Canvas _canvas;
    private Image _fallbackRootImage;

    private ItemDefinition _definition;
    private Sprite _legacyIcon;
    private string _legacyItemName;
    private string _baseItemName;
    private string _displayName;
    private int _quantity = 1;
    private int _upgradeTier;
    private int _price;
    private int _rarity = -1;

    public string DisplayName => _displayName;
    public int UpgradeTier => _upgradeTier;
    public int Quantity => _quantity;
    public ItemDefinition Definition => _definition;

    /// <summary>
    /// 이 슬롯이 들고 있는 개체의 등급. 정의의 기본 등급이 아니라 개체가 굴린 값이다.
    /// </summary>
    public ItemRarity Rarity => _rarity >= 0
        ? (ItemRarity)_rarity
        : (_definition != null ? _definition.rarity : ItemRarity.Common);

    private void Awake()
    {
        CacheRequiredComponents();
        ApplyAuthoredLabelTheme();
        UpdateQuantityLabel(_quantity);
        UpdateUpgradeLabel(_upgradeTier);
    }

    private void OnDisable()
    {
        HideDetail();
    }

    private void CacheRequiredComponents()
    {
        if (_rectTransform == null)
            _rectTransform = GetComponent<RectTransform>();
        if (_canvasGroup == null)
            _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvas == null)
            _canvas = GetComponentInParent<Canvas>();
        if (_fallbackRootImage == null)
            _fallbackRootImage = GetComponent<Image>();
    }

    private void ApplyAuthoredLabelTheme()
    {
        if (theme == null)
            return;

        if (quantityLabel != null)
            quantityLabel.color = theme.textPrimary;
        if (upgradeLabel != null)
            upgradeLabel.color = theme.success;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _originalParent = transform.parent;
        if (_canvasGroup != null)
            _canvasGroup.blocksRaycasts = false;
        _rectTransform.SetParent(_rectTransform.root);

        InventoryTooltip.Hide();
        HideDetail();
    }

    public void OnDrag(PointerEventData eventData)
    {
        float scaleFactor = _canvas != null && _canvas.scaleFactor > 0f
            ? _canvas.scaleFactor
            : 1f;
        _rectTransform.anchoredPosition += eventData.delta / scaleFactor;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_canvasGroup != null)
            _canvasGroup.blocksRaycasts = true;

        if (_rectTransform.parent == _rectTransform.root)
        {
            if (InstanceHandler.TryGetInstance(out InventoryManager inventoryManager))
            {
                bool insideInventoryUi = inventoryManager.IsScreenPointInsideInventoryUi(
                    eventData.position,
                    eventData.pressEventCamera);
                if (!insideInventoryUi)
                {
                    inventoryManager.DropItem(this);
                    return;
                }
            }

            _rectTransform.SetParent(_originalParent);
            _rectTransform.anchoredPosition = Vector2.zero;
        }
    }

    public void SetAvailiable()
    {
        if (_canvasGroup != null)
            _canvasGroup.blocksRaycasts = true;
        _rectTransform.anchoredPosition = Vector2.zero;
    }

    // Existing call contract retained for InventoryManager and external callers.
    public void init(string itemName, Sprite itemPicture, int price, int upgradeTier = 0)
    {
        Initialize(
            itemName,
            itemPicture,
            price,
            upgradeTier,
            ResolveDefinition(itemName),
            1);
    }

    // Additive overload for callers that already own the exact serialized definition.
    public void init(
        string itemName,
        Sprite itemPicture,
        int price,
        int upgradeTier,
        ItemDefinition definition,
        int quantity = 1,
        int rarity = -1)
    {
        Initialize(itemName, itemPicture, price, upgradeTier, definition, quantity, rarity);
    }

    public void SetDefinition(ItemDefinition definition)
    {
        _definition = definition;
        RefreshDefinitionDrivenVisuals();
    }

    public void SetQuantity(int quantity)
    {
        _quantity = Mathf.Max(0, quantity);
        UpdateQuantityLabel(_quantity);
    }

    private void Initialize(
        string itemName,
        Sprite itemPicture,
        int price,
        int upgradeTier,
        ItemDefinition definition,
        int quantity,
        int rarity = -1)
    {
        CacheRequiredComponents();

        _definition = definition;
        _legacyIcon = itemPicture;
        _legacyItemName = itemName;
        _price = price;
        _upgradeTier = upgradeTier;
        _rarity = rarity;

        SetQuantity(quantity);
        UpdateUpgradeLabel(upgradeTier);
        RefreshDefinitionDrivenVisuals();
    }

    private ItemDefinition ResolveDefinition(string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
            return null;

        if (!InstanceHandler.TryGetInstance(out InventoryManager inventoryManager))
            return null;

        Item sourceItem = inventoryManager.GetItemByName(itemName);
        return sourceItem != null ? sourceItem.Definition : null;
    }

    private void RefreshDefinitionDrivenVisuals()
    {
        _baseItemName = _definition != null && !string.IsNullOrWhiteSpace(_definition.displayName)
            ? _definition.displayName
            : _legacyItemName;
        _displayName = _upgradeTier > 0
            ? $"{_baseItemName} +{_upgradeTier}"
            : _baseItemName;

        Sprite resolvedIcon = _definition != null && _definition.icon != null
            ? _definition.icon
            : _legacyIcon;

        Image targetImage = iconImage != null ? iconImage : _fallbackRootImage;
        if (targetImage != null)
            targetImage.sprite = resolvedIcon;

        // The root Image is also the legacy raycast surface, so only the authored
        // child icon may be disabled when no sprite is available.
        if (iconImage != null)
            iconImage.enabled = resolvedIcon != null;

        if (theme != null && rarityBorder != null)
            rarityBorder.color = GetRarityColor(Rarity);
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

    private void UpdateQuantityLabel(int count)
    {
        if (quantityLabel == null)
            return;

        bool showQuantity = count > 1;
        quantityLabel.gameObject.SetActive(showQuantity);
        if (showQuantity)
            quantityLabel.text = count.ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateUpgradeLabel(int tier)
    {
        if (upgradeLabel == null)
            return;

        bool showTier = tier > 0;
        upgradeLabel.gameObject.SetActive(showTier);
        if (!showTier)
            return;

        if (theme != null)
            upgradeLabel.color = theme.success;

        upgradeLabel.text = "+" + tier.ToString(CultureInfo.InvariantCulture);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (string.IsNullOrEmpty(_displayName))
            return;

        // 인벤토리가 열려 있으면 그리드와 ActionSlot 모두 우측 상세 패널을 사용한다.
        // 인벤토리가 닫힌 ActionSlot만 기존 툴팁을 유지한다.
        if (ShouldUseDetailPanel())
            ShowDetail();
        else
            InventoryTooltip.Show(_displayName, _price, _upgradeTier, eventData.position);
    }

    private bool ShouldUseDetailPanel()
    {
        if (IsInsideInventoryGrid())
            return true;

        InventoryManager inventoryManager = GetComponentInParent<InventoryManager>(true);
        return inventoryManager != null && inventoryManager.IsInventoryOpen();
    }

    /// <summary>
    /// 이 아이템이 인벤토리 그리드(=상세 패널이 함께 보이는 영역) 안에 있는지.
    /// 퀵슬롯은 인벤토리를 닫은 상태에서도 보이므로 툴팁이 필요하다.
    /// </summary>
    private bool IsInsideInventoryGrid()
    {
        InventorySlot slot = GetComponentInParent<InventorySlot>(true);
        return slot != null && slot.GetComponent<ActionSlot>() == null;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        InventoryTooltip.Hide();
        HideDetail();
    }

    private void ShowDetail()
    {
        InventoryDetailPanel panel = ResolveDetailPanel();
        if (panel == null)
            return;

        panel.Present(
            this,
            _definition,
            _baseItemName,
            _legacyIcon,
            _price,
            _upgradeTier,
            Rarity);
    }

    internal void PresentFallback(InventoryDetailPanel panel)
    {
        if (panel == null)
            return;

        panel.PresentFallback(
            this,
            _definition,
            _baseItemName,
            _legacyIcon,
            _price,
            _upgradeTier,
            Rarity);
    }

    private void HideDetail()
    {
        if (detailPanel != null)
            detailPanel.Clear(this);
    }

    private InventoryDetailPanel ResolveDetailPanel()
    {
        if (detailPanel != null)
            return detailPanel;

        InventoryManager inventoryManager = GetComponentInParent<InventoryManager>(true);
        if (inventoryManager != null)
            detailPanel = inventoryManager.GetComponentInChildren<InventoryDetailPanel>(true);

        if (detailPanel == null)
        {
            Canvas parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas != null)
                detailPanel = parentCanvas.GetComponentInChildren<InventoryDetailPanel>(true);
        }

        return detailPanel;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right)
            return;

        InventoryTooltip.Hide();
        HideDetail();

        if (!InstanceHandler.TryGetInstance(out InventoryManager inventoryManager))
        {
            Debug.LogError("Failed to get inventory manager to drop item!");
            return;
        }

        inventoryManager.DropItem(this);
    }
}
