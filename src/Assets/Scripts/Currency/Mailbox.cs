using UnityEngine;
using PurrNet;

/// <summary>
/// 우체통 - 포장된 아이템(GiftBoxItem)을 돈으로 전환
/// </summary>
public class Mailbox : AInteractable
{
    [Header("Mailbox Settings")]
    [SerializeField] private AudioClip sellSound; // 판매 사운드 (옵션)

    // 캐시된 컴포넌트들
    private InventoryManager _inventoryManager;
    private CurrencyManager _currencyManager;
    private PromptPresenter _promptPresenter;
    private AudioSource _audioSource;

    private void Awake()
    {
        // InventoryManager 찾기 - 여러 방법 시도
        if (!InstanceHandler.TryGetInstance(out _inventoryManager))
        {
            _inventoryManager = FindObjectOfType<InventoryManager>();
        }

        if (_inventoryManager != null)
        {
            Debug.Log("[Mailbox] InventoryManager found in Awake!");
        }
        else
        {
            Debug.LogWarning("[Mailbox] InventoryManager not found in Awake, will try again in Start");
        }

        // CurrencyManager 찾기
        if (!InstanceHandler.TryGetInstance(out _currencyManager))
        {
            _currencyManager = FindObjectOfType<CurrencyManager>();
        }

        if (_currencyManager != null)
        {
            Debug.Log("[Mailbox] CurrencyManager found!");
        }

        _audioSource = GetComponent<AudioSource>();

        // PromptPresenter 찾기
        if (_promptPresenter == null)
            _promptPresenter = FindObjectOfType<PromptPresenter>();
    }

    private void Start()
    {
        // Start에서 한 번 더 Manager들 찾기 시도
        if (_inventoryManager == null)
        {
            if (!InstanceHandler.TryGetInstance(out _inventoryManager))
            {
                _inventoryManager = FindObjectOfType<InventoryManager>();
            }

            if (_inventoryManager != null)
            {
                Debug.Log("[Mailbox] InventoryManager found in Start!");
            }
        }

        if (_currencyManager == null)
        {
            if (!InstanceHandler.TryGetInstance(out _currencyManager))
            {
                _currencyManager = FindObjectOfType<CurrencyManager>();
            }

            if (_currencyManager != null)
            {
                Debug.Log("[Mailbox] CurrencyManager found in Start!");
            }
        }
    }

    public override void Interact()
    {
        // Manager들이 없으면 다시 찾기 시도
        if (_inventoryManager == null)
        {
            if (!InstanceHandler.TryGetInstance(out _inventoryManager))
            {
                _inventoryManager = FindObjectOfType<InventoryManager>();

                if (_inventoryManager == null)
                {
                    Debug.LogError("[Mailbox] InventoryManager not found!");
                    return;
                }
            }
        }

        if (_currencyManager == null)
        {
            if (!InstanceHandler.TryGetInstance(out _currencyManager))
            {
                _currencyManager = FindObjectOfType<CurrencyManager>();

                if (_currencyManager == null)
                {
                    Debug.LogError("[Mailbox] CurrencyManager not found!");
                    return;
                }
            }
        }

        // 현재 손에 든 아이템 가져오기
        var activeItemData = _inventoryManager.GetActiveItemData();

        if (!activeItemData.HasValue)
        {
            Debug.Log("[Mailbox] No item in hand!");
            return;
        }

        var itemData = activeItemData.Value;

        // 잡동사니·시체·포장 상자는 바로 판매. 무기/탄약/주문서는 상점 물건이라 제외.
        if (!IsSellable(itemData))
        {
            Debug.Log($"[Mailbox] {itemData.itemName} cannot be sold here.");
            _promptPresenter?.Show("Weapons and ammo can't be sold here");
            return;
        }

        // 아이템 판매
        SellItem(itemData);
    }

    /// <summary>
    /// The old flow forced every item through the GiftBox first (a click with no decision).
    /// Now any loot sells directly; wrapping stays as an optional step.
    /// </summary>
    private bool IsSellable(InventoryManager.InventoryItemData itemData)
    {
        if (string.IsNullOrEmpty(itemData.itemName))
            return false;

        if (itemData.itemName.Contains("GiftBox") || itemData.itemName.Contains("선물"))
            return true;

        if (itemData.weaponData != null)
            return false;

        ItemDefinition definition = itemData.definition;
        if (definition == null && _inventoryManager != null)
        {
            Item worldItem = _inventoryManager.GetItemByName(itemData.itemName);
            definition = worldItem != null ? worldItem.Definition : null;
        }

        if (definition == null)
            return itemData.price > 0;

        switch (definition.category)
        {
            case ItemCategory.Weapon:
            case ItemCategory.Ammo:
            case ItemCategory.Spell:
                return false;
            default:
                return itemData.price > 0;
        }
    }

    private void SellItem(InventoryManager.InventoryItemData itemData)
    {
        if (isSpawned)
        {
            SellReceiptServerRpc(itemData.receiptToken);
            return;
        }
        int sellPrice = itemData.price;

        Debug.Log($"[Mailbox] Selling GiftBox for {sellPrice} credits");

        // 1. 아이템 먼저 제거 (제거 실패 시 돈이 지급되면 안 된다)
        if (!_inventoryManager.RemoveActiveItem())
        {
            Debug.LogError("[Mailbox] Failed to remove active item!");
            return;
        }

        // 2. 돈 추가
        _currencyManager.AddCurrency(sellPrice);

        // 3. 사운드 재생 (옵션)
        if (_audioSource != null && sellSound != null)
        {
            _audioSource.PlayOneShot(sellSound);
        }

        Debug.Log($"[Mailbox] Item sold successfully! +{sellPrice} credits");
    }

    [ServerRpc(requireOwnership: false)]
    private void SellReceiptServerRpc(string token, RPCInfo info = default)
    {
        var player = NetworkPlayer.FindPlayer(info.sender);
        if (player == null || !player.CanUseStation(this) || !player.ServerInventory.TryGet(token, out var entry)) return;
        if (!string.IsNullOrEmpty(entry.weaponDataName) || entry.category == (int)ItemCategory.Weapon
            || entry.category == (int)ItemCategory.Ammo || entry.category == (int)ItemCategory.Spell || entry.price <= 0) return;
        if (!InstanceHandler.TryGetInstance(out CurrencyManager currency) || !currency.CanCredit(entry.price)) return;
        if (!player.ServerInventory.Consume(token, out entry)) return;
        currency.AddCurrency(entry.price);
        ContractEvents.Report(ContractGoal.SaleCredits, entry.price);
        player.CompleteItemUse(token, $"Sold for ${entry.price:N0}");
        PlaySaleFeedbackTargetRpc(info.sender);
    }

    [TargetRpc]
    private void PlaySaleFeedbackTargetRpc(PlayerID target)
    {
        if (_audioSource != null && sellSound != null) _audioSource.PlayOneShot(sellSound);
    }

    public override void OnHover()
    {
        base.OnHover();

        // 프롬프트 표시
        if (_promptPresenter != null)
        {
            var activeItem = _inventoryManager?.GetActiveItemData();
            if (activeItem.HasValue && IsSellable(activeItem.Value))
            {
                _promptPresenter.Show($"[F] Sell {activeItem.Value.itemName} (${activeItem.Value.price:N0})");
            }
            else if (activeItem.HasValue)
            {
                _promptPresenter.Show("Can't sell this here");
            }
            else
            {
                _promptPresenter.Show("Hold an item to sell it");
            }
        }
    }

    public override void OnStopHover()
    {
        base.OnStopHover();

        // 프롬프트 숨김
        if (_promptPresenter != null)
            _promptPresenter.Hide();
    }

    public override bool CanInteract()
    {
        // GiftBox와 동일하게 Hover 효과는 항상 표시
        return true;
    }
}
