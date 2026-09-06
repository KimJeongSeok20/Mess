using UnityEngine;
using System.Collections;

/// <summary>
/// 총기 상점 진열 아이템
/// - 구매 시 WeaponItem/AmmoItem 생성
/// - 인벤토리 추가 시도 → 실패 시 바닥 드롭
/// - 상점 아이템은 구매 후 제거 (공유 상점)
/// </summary>
public class WeaponShopItem : ShopPurchaseItemBase
{
    [Header("=== Weapon Shop Settings ===")]
    [SerializeField] private WeaponData weaponData;
    [SerializeField] private GameObject ammoItemPrefab;
    [SerializeField] private ItemType itemType = ItemType.Weapon;

    public enum ItemType
    {
        Weapon,
        Ammo
    }

    public bool IsWeaponType => itemType == ItemType.Weapon;
    public GameObject PurchasedItemPrefab => IsWeaponType
        ? (weaponData != null ? weaponData.itemPrefab : null) : ammoItemPrefab;

    private WeaponShop _weaponShop;

    protected override void OnSpawned()
    {
        base.OnSpawned();

        _weaponShop = GetComponentInParent<WeaponShop>();

        Debug.Log($"[WeaponShopItem] Spawned: {ItemName} ({itemType}), Price: ${Price}");
    }

    public override void Interact()
    {
        Debug.Log($"[WeaponShopItem] Interact called - {ItemName}, Price: ${Price}");
        ProcessPurchase();
    }

    private void ProcessPurchase()
    {
        if (!TryBeginPurchaseRequest())
        {
            Debug.Log($"[WeaponShopItem] Purchase already pending for {ItemName}");
            return;
        }

        if (!TryResolvePurchaseManagers())
        {
            Debug.LogError("[WeaponShopItem] Managers not found!");
            CancelPendingPurchaseRequest();
            return;
        }

        if (!PurchaseCurrencyManager.CanAfford(Price))
        {
            ShowInsufficientFunds(Price, PurchaseCurrencyManager.SharedCurrency, playAudio: false);
            _weaponShop?.TryRequestPurchaseFeedback(this, success: false);
            CancelPendingPurchaseRequest();
            return;
        }

        if (_weaponShop == null)
        {
            _weaponShop = GetComponentInParent<WeaponShop>();
        }

        if (_weaponShop == null || !_weaponShop.TryRequestPurchase(this, PendingPurchaseToken))
        {
            Debug.LogError("[WeaponShopItem] WeaponShop not found or purchase routing failed!");
            CancelPendingPurchaseRequest();
        }
    }

    private void ApplyApprovedPurchaseLocally(InventoryReceipt receipt)
    {
        GameObject itemObj = CreateInventoryItem();
        if (itemObj == null)
        {
            Debug.LogError("[WeaponShopItem] Failed to create inventory item after server approval!");
            ShowPurchaseFailure("Purchase failed");
            return;
        }

        Item item = itemObj.GetComponent<Item>();
        if (item == null)
        {
            Debug.LogError("[WeaponShopItem] Created item has no Item component!");
            Destroy(itemObj);
            ShowPurchaseFailure("Purchase failed");
            return;
        }

        bool addedToInventory = PurchaseInventoryManager.AddItem(item, receipt: receipt);
        if (addedToInventory)
        {
            Destroy(itemObj);
            Debug.Log($"[WeaponShopItem] Added to inventory after server approval: {ItemName}");
            ShowPurchaseSuccess("Added to inventory!");
            return;
        }

        Debug.Log("[WeaponShopItem] Inventory full after server approval - dropping item on ground");
        Destroy(itemObj);
        NetworkPlayer.Local?.RequestDropReceiptServerRpc(receipt.token);
        ShowPurchaseSuccess("Purchased! (Dropped on ground)");
    }

    private GameObject CreateInventoryItem()
    {
        GameObject itemObj = null;

        if (itemType == ItemType.Weapon)
        {
            if (weaponData == null || weaponData.itemPrefab == null)
            {
                Debug.LogError("[WeaponShopItem] WeaponData or itemPrefab is null!");
                return null;
            }

            itemObj = PurrNet.UnityProxy.InstantiateDirectly(weaponData.itemPrefab);
            WeaponItem weaponItem = itemObj.GetComponent<WeaponItem>();

            if (weaponItem != null)
            {
                weaponItem.InitializeLocal(weaponData);
                Debug.Log($"[WeaponShopItem] Created weapon: {weaponData.weaponName} (empty mag)");
            }
            else
            {
                Debug.LogError("[WeaponShopItem] WeaponItem component not found on created object!");
            }
        }
        else if (itemType == ItemType.Ammo)
        {
            if (ammoItemPrefab == null)
            {
                Debug.LogError("[WeaponShopItem] AmmoItemPrefab is null!");
                return null;
            }

            itemObj = PurrNet.UnityProxy.InstantiateDirectly(ammoItemPrefab);
            Debug.Log($"[WeaponShopItem] Created ammo: {ItemName}");
        }

        return itemObj;
    }

    public bool TryExecutePurchaseOnServer(out int currentCurrency)
    {
        if (!TryResolveCurrencyManager())
        {
            Debug.LogError("[WeaponShopItem] CurrencyManager not found on server!");
            currentCurrency = 0;
            return false;
        }

        if (!PurchaseCurrencyManager.TrySpendCurrencyImmediateOnServer(Price))
        {
            Debug.LogWarning("[WeaponShopItem] Purchase rejected on server due to insufficient currency");
            currentCurrency = PurchaseCurrencyManager.SharedCurrency;
            return false;
        }

        Debug.Log($"[WeaponShopItem] Purchase complete! Spent ${Price} for {ItemName}");
        currentCurrency = PurchaseCurrencyManager.SharedCurrency;
        return true;
    }

    public void ResolvePurchaseLocally(string purchaseToken, bool success, int currentCurrency, InventoryReceipt receipt)
    {
        if (!TryResolvePurchaseToken(purchaseToken))
            return;

        if (!success)
        {
            ShowPurchaseFailure($"Need ${Price} (You have ${currentCurrency})", playAudio: false);
            return;
        }

        if (!TryResolveInventoryManager())
        {
            Debug.LogError("[WeaponShopItem] InventoryManager not found while applying approved purchase");
            ShowPurchaseFailure("Purchase failed", playAudio: false);
            return;
        }

        ApplyApprovedPurchaseLocally(receipt);
    }

    public void RemovePurchasedEntryLocally()
    {
        if (gameObject.activeInHierarchy)
            StartCoroutine(RemoveAfterPurchaseEffect());
    }

    public override void OnHover()
    {
        base.OnHover();

        EnsurePromptPresenter();
        if (PromptPresenterInstance != null)
        {
            string itemTypeName = itemType == ItemType.Weapon ? "" : " (Ammo)";
            string message = $"[F] {ItemName}{itemTypeName} - ${Price}";

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
