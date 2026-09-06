using UnityEngine;

/// <summary>
/// 인벤토리 확장 가방 아이템 - 구매 시 인벤토리 슬롯 증가
/// Item.cs를 상속받아 기본 아이템 기능 활용
/// </summary>
public class InventoryBagItem : ShopPurchaseItemBase
{
    [Header("Bag Expansion Settings")]
    [SerializeField] private int slotExpansion = 1;
    [SerializeField] private BagTier bagTier = BagTier.Small;

    public enum BagTier
    {
        Small = 1,
        Medium = 2,
        Large = 3
    }

    public int SlotExpansion => slotExpansion;
    public BagTier Tier => bagTier;

    private InventoryExpansionShop _inventoryExpansionShop;

    protected override void OnSpawned()
    {
        base.OnSpawned();

        _inventoryExpansionShop = GetComponentInParent<InventoryExpansionShop>();

        SetupPriceByTier();
    }

    private void SetupPriceByTier()
    {
        Debug.Log($"[InventoryBagItem] {bagTier} bag spawned with price: ${Price}");
    }

    public override void Interact()
    {
        Debug.Log($"[InventoryBagItem] Interact called - {bagTier} bag, Price: ${Price}");
        ProcessPurchase();
    }

    private void ProcessPurchase()
    {
        if (!TryBeginPurchaseRequest())
        {
            Debug.Log($"[InventoryBagItem] Purchase already pending for {bagTier} bag");
            return;
        }

        if (!TryResolvePurchaseManagers())
        {
            Debug.LogError("[InventoryBagItem] Managers not found!");
            CancelPendingPurchaseRequest();
            return;
        }

        if (!PurchaseCurrencyManager.CanAfford(Price))
        {
            ShowInsufficientFunds(Price, PurchaseCurrencyManager.SharedCurrency, playAudio: false);
            _inventoryExpansionShop?.TryRequestPurchaseFeedback(this, success: false);
            CancelPendingPurchaseRequest();
            return;
        }

        if (InventoryManagerExtensions.Instance != null &&
            !InventoryManagerExtensions.Instance.CanExpandInventory(slotExpansion))
        {
            _inventoryExpansionShop?.TryRequestPurchaseFeedback(this, success: false);
            ShowMaxSlotsReached();
            CancelPendingPurchaseRequest();
            return;
        }

        if (_inventoryExpansionShop == null)
        {
            _inventoryExpansionShop = GetComponentInParent<InventoryExpansionShop>();
        }

        if (_inventoryExpansionShop == null || !_inventoryExpansionShop.TryRequestPurchase(this, PendingPurchaseToken))
        {
            Debug.LogError("[InventoryBagItem] InventoryExpansionShop not found or purchase routing failed!");
            CancelPendingPurchaseRequest();
        }
    }

    public bool TryExecutePurchaseOnServer(out int currentCurrency)
    {
        if (!TryResolveCurrencyManager())
        {
            Debug.LogError("[InventoryBagItem] CurrencyManager not found on server!");
            currentCurrency = 0;
            return false;
        }

        if (!PurchaseCurrencyManager.TrySpendCurrencyImmediateOnServer(Price))
        {
            Debug.LogWarning("[InventoryBagItem] Purchase rejected on server due to insufficient currency");
            currentCurrency = PurchaseCurrencyManager.SharedCurrency;
            return false;
        }

        Debug.Log($"[InventoryBagItem] Purchase successful! Spent ${Price} for +{slotExpansion} slots");
        currentCurrency = PurchaseCurrencyManager.SharedCurrency;
        return true;
    }

    public void ResolvePurchaseLocally(string purchaseToken, bool success, int currentCurrency)
    {
        if (!TryResolvePurchaseToken(purchaseToken))
            return;

        if (!success)
        {
            ShowInsufficientFunds(Price, currentCurrency, playAudio: false);
            return;
        }

        if (InventoryManagerExtensions.Instance != null)
        {
            InventoryManagerExtensions.Instance.ExpandLocalPlayerInventory(slotExpansion, showPrompt: false);
            ShowPurchaseSuccess($"Inventory expanded by +{slotExpansion} slots!", playAudio: false);
        }
        else
        {
            Debug.LogWarning("[InventoryBagItem] InventoryManagerExtensions.Instance is null during approved purchase");
            ShowPurchaseFailure("Purchase failed", playAudio: false);
        }
    }

    public void RemovePurchasedEntryLocally()
    {
        if (gameObject.activeInHierarchy)
            StartCoroutine(RemoveAfterPurchaseEffect());
    }

    private void ShowMaxSlotsReached()
    {
        if (InventoryManagerExtensions.Instance != null)
        {
            ShowPromptMessage($"Maximum inventory size reached! ({InventoryManagerExtensions.Instance.GetAbsoluteMaxCells()} cells)");
        }
    }

    public override void OnHover()
    {
        base.OnHover();

        EnsurePromptPresenter();
        if (PromptPresenterInstance != null)
        {
            string bagName = GetBagDisplayName();
            string message = $"[F] {bagName} (+{slotExpansion} slot{(slotExpansion > 1 ? "s" : "")}) - ${Price}";

            if (PurchaseCurrencyManager != null && !PurchaseCurrencyManager.CanAfford(Price))
                message += " (Not enough money)";

            PromptPresenterInstance.Show(message);
        }
    }

    public override void OnStopHover()
    {
        base.OnStopHover();

        if (PromptPresenterInstance != null)
            PromptPresenterInstance.Hide();
    }

    private string GetBagDisplayName()
    {
        switch (bagTier)
        {
            case BagTier.Small:
                return "Small Bag";
            case BagTier.Medium:
                return "Medium Bag";
            case BagTier.Large:
                return "Large Bag";
            default:
                return "Inventory Bag";
        }
    }

    public override bool CanInteract()
    {
        return base.CanInteract() && gameObject.activeSelf;
    }

    [ContextMenu("Debug - Force Purchase")]
    private void DebugForcePurchase()
    {
        if (Application.isPlaying)
            ProcessPurchase();
    }
}
