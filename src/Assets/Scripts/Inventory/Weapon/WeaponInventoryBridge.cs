using UnityEngine;
using PurrNet;
using Demo.Scripts.Runtime.Character;
using System.Collections;
using System.Collections.Generic;
using Demo.Scripts.Runtime.Item;

public class WeaponInventoryBridge : NetworkBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private FPSWeaponManager weaponManager;
    [SerializeField] private HeldItemPresenter heldItemPresenter;

    [Header("Upgrade System")]
    [Tooltip("강화 레시피 목록 (무기별 스탯 스케일링 참조용)")]
    [SerializeField] private List<ItemUpgradeRecipe> upgradeRecipes = new();

    private InventoryManager _inventoryManager;
    private InventorySlot _currentWeaponSlot;
    private Coroutine _pendingSelectionRoutine;
    private int _selectionVersion;

    public IReadOnlyList<ItemUpgradeRecipe> UpgradeRecipes => upgradeRecipes;

    private bool HasEquipAuthority
    {
        get
        {
            if (isSpawned)
                return isOwner;

            return NetworkManager.main == null || NetworkManager.main.isServer || isOwner;
        }
    }

    private void Start()
    {
        if (!InstanceHandler.TryGetInstance(out _inventoryManager))
        {
            Debug.LogError("[WeaponInventoryBridge] InventoryManager를 찾을 수 없습니다!");
            return;
        }

        if (heldItemPresenter == null)
        {
            Debug.LogError("[WeaponInventoryBridge] HeldItemPresenter is not assigned!", this);
            return;
        }

        ActionSlot.OnSlotActivated += HandleActionSlotActivated;
    }

    private void OnDestroy()
    {
        ActionSlot.OnSlotActivated -= HandleActionSlotActivated;
    }

    private void HandleActionSlotActivated(ActionSlot actionSlot)
    {
        if (!HasEquipAuthority) return;

        int selectionVersion = ++_selectionVersion;
        if (_pendingSelectionRoutine != null)
        {
            StopCoroutine(_pendingSelectionRoutine);
            _pendingSelectionRoutine = null;
        }

        // ========== 이전 무기 저장 ==========
        if (_currentWeaponSlot != null)
        {
            var activeItem = weaponManager.GetActiveItem();
            if (activeItem is Weapon currentWeapon)
            {
                SaveCurrentWeaponAmmoToInventory(currentWeapon, _currentWeaponSlot);
            }
            else if (activeItem is SpellItem currentSpell && currentSpell.RuntimeState != null)
            {
                _inventoryManager.UpdateWeaponAmmoAtSlot(_currentWeaponSlot, currentSpell.RuntimeState.ChargesRemaining);
            }
        }

        InventorySlot inventorySlot = actionSlot.GetComponent<InventorySlot>();
        if (inventorySlot == null)
        {
            _currentWeaponSlot = null;
            heldItemPresenter.Clear();
            ReturnToFist();
            return;
        }

        Item item = _inventoryManager.GetItemFromInventorySlot(inventorySlot);
        if (item == null)
        {
            _currentWeaponSlot = null;
            Debug.Log("[WIB] NULL - Fist");
            heldItemPresenter.Clear();
            ReturnToFist();
            return;
        }

        if (item is WeaponItem weaponItem)
        {
            heldItemPresenter.Clear();
            _currentWeaponSlot = inventorySlot;

            int savedAmmo = _inventoryManager.GetWeaponAmmoFromSlot(inventorySlot);
            int upgradeTier = _inventoryManager.GetUpgradeTierFromSlot(inventorySlot);

            if (string.IsNullOrEmpty(heldItemPresenter.DisplayedItemName))
                EquipWeaponFromSlot(weaponItem, savedAmmo, upgradeTier);
            else
                _pendingSelectionRoutine = StartCoroutine(
                    EquipWeaponAfterHeldItemClears(weaponItem, savedAmmo, upgradeTier, selectionVersion));
        }
        else
        {
            _currentWeaponSlot = null;
            Debug.Log($"[WIB] Held item: {item.ItemName} ({item.GetType().Name})");

            if (!weaponManager.IsFistEquipped)
            {
                ReturnToFist();
                _pendingSelectionRoutine = StartCoroutine(
                    ShowHeldItemAfterWeaponClears(item.ItemName, selectionVersion));
            }
            else
            {
                heldItemPresenter.Show(item.ItemName);
            }
        }
    }

    private IEnumerator EquipWeaponAfterHeldItemClears(WeaponItem weaponItem, int savedAmmo,
        int upgradeTier, int selectionVersion)
    {
        while (selectionVersion == _selectionVersion
               && !string.IsNullOrEmpty(heldItemPresenter.DisplayedItemName))
        {
            yield return null;
        }

        // Destroy() removes the lowered visual at the end of the frame.
        yield return null;

        if (selectionVersion == _selectionVersion)
            EquipWeaponFromSlot(weaponItem, savedAmmo, upgradeTier);

        _pendingSelectionRoutine = null;
    }

    private IEnumerator ShowHeldItemAfterWeaponClears(string itemName, int selectionVersion)
    {
        // ReturnToFist destroys the previous weapon at the end of the frame.
        yield return null;

        if (selectionVersion == _selectionVersion && weaponManager.IsFistEquipped)
            heldItemPresenter.Show(itemName);

        _pendingSelectionRoutine = null;
    }

    private void EquipWeaponFromSlot(WeaponItem weaponItem, int savedAmmo, int upgradeTier)
    {
        if (weaponItem == null || weaponItem.WeaponData == null)
        {
            ReturnToFist();
            return;
        }

        if (savedAmmo >= 0)
        {
            weaponManager.EquipWeapon(weaponItem.WeaponData, savedAmmo, upgradeTier);
            Debug.Log($"[WIB] Equipped: {weaponItem.WeaponData.weaponName} +{upgradeTier} with saved ammo: {savedAmmo}");
        }
        else
        {
            weaponManager.EquipWeapon(weaponItem.WeaponData, weaponItem.CurrentAmmo, upgradeTier);
            Debug.Log($"[WIB] Equipped: {weaponItem.WeaponData.weaponName} +{upgradeTier}");
        }
    }

    private void SaveCurrentWeaponAmmoToInventory(Weapon weapon, InventorySlot slot)
    {
        _inventoryManager.UpdateWeaponAmmoAtSlot(slot, weapon.CurrentAmmo);
        Debug.Log($"[WIB] Saved {weapon.CurrentAmmo} ammo to specific slot");
    }

    private void ReturnToFist()
    {
        weaponManager.ReturnToFist();
        Debug.Log("[WIB] Fist");
    }
}
