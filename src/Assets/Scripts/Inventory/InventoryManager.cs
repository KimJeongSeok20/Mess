using UnityEngine;
using System.Collections.Generic;
using PurrNet;
using System;
using System.Linq;
using PurrNet.Utils;
using UnityEngine.InputSystem;
using System.Collections;
using Demo.Scripts.Runtime.Character;
using Demo.Scripts.Runtime.Item;
using UnityEngine.UI;

public class InventoryManager : MonoBehaviour 
{
    [SerializeField] private List<Item> allItems = new();
    [SerializeField] private CanvasGroup inventoryCanvasGroup;
    [SerializeField] private CanvasGroup moneyCanvasGroup;
    [SerializeField] private PlayerInput playerInput; // PlayerInput ���� �߰�
    [SerializeField] private InventoryItem itemPrefab;
    [SerializeField] private List<InventorySlot> slots =new();
    [SerializeField] private List<ActionSlot> actionSlots = new();

    [PurrReadOnly, SerializeField] private InventoryItemData[] _inventoryData;
    private ActionSlot _activeActionSlot;

    public event Action InventoryChanged;
    private void RaiseInventoryChanged()
    {
        InventoryChanged?.Invoke();
        LootScent.ReportLocal(this);
    }

    /// <summary>Sum of the prices of every carried item (loot scent).</summary>
    public int GetCarriedValue()
    {
        long total = 0;
        for (int i = 0; i < _inventoryData.Length; i++)
        {
            if (!string.IsNullOrEmpty(_inventoryData[i].itemName))
                total += Mathf.Max(0, _inventoryData[i].price);
        }

        return (int)System.Math.Min(total, int.MaxValue);
    }

    public IReadOnlyList<InventorySlot> Slots => slots;
    public int SlotCount => slots.Count;

    public bool HasFreeSlot()
    {
        foreach (var slot in slots)
        {
            if (slot != null && slot.isEmpty)
                return true;
        }

        return false;
    }

    public float GetTotalWeight()
    {
        if (_inventoryData == null)
            return 0f;

        float total = 0f;
        for (int i = 0; i < _inventoryData.Length; i++)
        {
            InventoryItemData data = _inventoryData[i];
            if (data.inventoryItem != null && data.definition != null)
                total += Mathf.Max(0f, data.definition.weight);
        }

        return total;
    }

    private int price;

    [SerializeField] private float sprintRecoverBlockThreshold = 0.05f;

    [Header("Inventory Cursor")]
    [SerializeField] private Texture2D inventoryCursorTexture;
    [SerializeField] private Vector2 inventoryCursorHotspot = new Vector2(8f, 8f);
    [SerializeField] private CursorMode inventoryCursorMode = CursorMode.Auto;
    [SerializeField] private string inventoryCursorResourcePath = string.Empty;

    [Header("Inventory UI Theme")]
    [SerializeField] private InventoryTheme theme;
    [SerializeField] private RectTransform _panelRect;
    [SerializeField] private RectTransform _actionPanelRect;
    [SerializeField] private CanvasGroup _overlayCanvasGroup;
    [SerializeField] private Image _overlayImage;

    private Coroutine _toggleRoutine;
    private bool _audioInventoryOpen;
    private Vector3 _panelBaseScale = Vector3.one;
    private Vector3 _actionPanelBaseScale = Vector3.one;
    private readonly List<CanvasGroup> _slotRevealGroups = new();
    private readonly List<int> _slotRevealRows = new();

    private bool _inventoryCursorApplied;
    private Texture2D _cpuAccessibleInventoryCursor;
    private bool _dropKeyWasPressed;

    private void Awake()
    {
        InstanceHandler.RegisterInstance(this);
        _inventoryData = new InventoryItemData[slots.Count];
        inventoryCanvasGroup.blocksRaycasts = false;
        inventoryCanvasGroup.alpha = 0;

        moneyCanvasGroup.blocksRaycasts = false;
        moneyCanvasGroup.alpha = 0;

        // PlayerInput �ڵ� ã�� (Inspector���� �Ҵ� ������ ���)
        if (playerInput == null)
        {
            playerInput = FindAnyObjectByType<PlayerInput>();
        }

        CacheAuthoredUiState();
        SetupSlotVisuals();
    }

    private void Update()
    {
        bool dropKeyPressed = Keyboard.current != null && Keyboard.current.gKey.isPressed;
        if (dropKeyPressed && !_dropKeyWasPressed && !GameMenuController.IsOpen
            && !SkillWebTerminalInteraction.BlocksGameplayInput)
            TryDropActiveItem();

        _dropKeyWasPressed = dropKeyPressed;
    }
    

    private IEnumerator Start()
    {
        Debug.Log("1");
        // ���� ���� �� Canvas �����ϱ�
        if (slots.Count > 0 && slots[0].isEmpty)
        {
            var dummy = Instantiate(itemPrefab, slots[0].transform);
            yield return null; // 1������ ��� (Canvas rebuild)
            Destroy(dummy.gameObject);
        }
        yield return null; // 초기화 한 틱 더 여유
        TryActivateDefaultActionSlot();
    }

    public bool IsInventoryOpen()
    {
        return inventoryCanvasGroup.alpha > 0;
    }

    /*public void OnToggleInventory(InputValue value)
    {
        if (!value.isPressed) return;
        bool isOpen = canvasGroup.alpha > 0;
        ToggleInventory(!isOpen);
    }*/

    public void ToggleInventory(bool toggle)
    {
        if (toggle && SkillWebTerminalInteraction.BlocksGameplayInput)
            return;

        if (inventoryCanvasGroup != null && _audioInventoryOpen != toggle)
        {
            _audioInventoryOpen = toggle;
            var localPlayer = NetworkPlayer.Local;
            if (localPlayer != null && localPlayer.TryGetComponent<PlayerFeedbackAudio>(out var feedback))
                feedback.PlayInventoryToggle(toggle);
        }

        if (_toggleRoutine != null)
            StopCoroutine(_toggleRoutine);

        _toggleRoutine = StartCoroutine(ToggleInventoryRoutine(toggle));
    }

    private IEnumerator ToggleInventoryRoutine(bool toggle)
    {
        if (inventoryCanvasGroup == null)
            yield break;

        float startInventoryAlpha = inventoryCanvasGroup.alpha;
        float startMoneyAlpha = moneyCanvasGroup != null ? moneyCanvasGroup.alpha : 0f;
        float targetAlpha = toggle ? 1f : 0f;
        float startOverlayAlpha = _overlayCanvasGroup != null ? _overlayCanvasGroup.alpha : 0f;
        float targetOverlayAlpha = toggle ? 1f : 0f;
        float scaleFrom = theme != null ? theme.panelScaleFrom : 0.98f;
        float scaleTo = theme != null ? theme.panelScaleTo : 1f;
        Vector3 panelStartScale = _panelRect != null ? _panelRect.localScale : _panelBaseScale;
        Vector3 actionStartScale = _actionPanelRect != null ? _actionPanelRect.localScale : _actionPanelBaseScale;
        Vector3 panelTargetScale = _panelBaseScale * (toggle ? scaleTo : scaleFrom);
        Vector3 actionTargetScale = _actionPanelBaseScale * (toggle ? scaleTo : scaleFrom);
        bool staggerFromClosed = toggle && startInventoryAlpha <= 0.001f;

        if (staggerFromClosed)
        {
            for (int i = 0; i < _slotRevealGroups.Count; i++)
            {
                CanvasGroup group = _slotRevealGroups[i];
                if (group != null)
                    group.alpha = 0f;
            }
        }

        if (toggle)
        {
            inventoryCanvasGroup.blocksRaycasts = true;
            if (moneyCanvasGroup != null)
                moneyCanvasGroup.blocksRaycasts = true;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            ApplyInventoryCursor();
        }

        float elapsed = 0f;
        float fadeDuration = Mathf.Max(0.01f, theme != null ? theme.panelFade : 0.16f);
        float scaleDuration = Mathf.Max(0.01f, theme != null ? theme.panelScale : 0.16f);
        float slotDuration = Mathf.Max(0.01f, theme != null ? theme.slotState : 0.12f);
        float slotDelay = Mathf.Max(0f, theme != null ? theme.slotStagger : 0.008f);
        float staggerMax = Mathf.Max(0f, theme != null ? theme.slotStaggerMax : 0.2f);
        int maxRevealRow = 0;
        for (int i = 0; i < _slotRevealRows.Count; i++)
            maxRevealRow = Mathf.Max(maxRevealRow, _slotRevealRows[i]);
        float latestSlotFinish = staggerFromClosed && _slotRevealRows.Count > 0
            ? Mathf.Min(staggerMax, (maxRevealRow * slotDelay) + slotDuration)
            : 0f;
        float duration = Mathf.Max(fadeDuration, scaleDuration, latestSlotFinish);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float fadeT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / fadeDuration));
            float scaleT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / scaleDuration));

            inventoryCanvasGroup.alpha = Mathf.Lerp(startInventoryAlpha, targetAlpha, fadeT);
            if (moneyCanvasGroup != null)
                moneyCanvasGroup.alpha = Mathf.Lerp(startMoneyAlpha, targetAlpha, fadeT);

            if (_overlayCanvasGroup != null)
                _overlayCanvasGroup.alpha = Mathf.Lerp(startOverlayAlpha, targetOverlayAlpha, fadeT);

            if (_panelRect != null)
                _panelRect.localScale = Vector3.LerpUnclamped(panelStartScale, panelTargetScale, scaleT);
            if (_actionPanelRect != null)
                _actionPanelRect.localScale = Vector3.LerpUnclamped(actionStartScale, actionTargetScale, scaleT);

            if (staggerFromClosed)
                ApplySlotReveal(elapsed, slotDuration, slotDelay, staggerMax);

            yield return null;
        }

        inventoryCanvasGroup.alpha = targetAlpha;
        if (moneyCanvasGroup != null)
            moneyCanvasGroup.alpha = targetAlpha;
        if (_overlayCanvasGroup != null)
            _overlayCanvasGroup.alpha = targetOverlayAlpha;
        if (_panelRect != null)
            _panelRect.localScale = panelTargetScale;
        if (_actionPanelRect != null)
            _actionPanelRect.localScale = actionTargetScale;

        if (toggle)
        {
            for (int i = 0; i < _slotRevealGroups.Count; i++)
            {
                CanvasGroup group = _slotRevealGroups[i];
                if (group != null)
                    group.alpha = 1f;
            }
        }

        if (!toggle)
        {
            inventoryCanvasGroup.blocksRaycasts = false;
            if (moneyCanvasGroup != null)
                moneyCanvasGroup.blocksRaycasts = false;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            RestoreInventoryCursor();
        }
    }

    private void CacheAuthoredUiState()
    {
        _panelBaseScale = _panelRect != null ? _panelRect.localScale : Vector3.one;
        _actionPanelBaseScale = _actionPanelRect != null ? _actionPanelRect.localScale : Vector3.one;

        float hiddenScale = theme != null ? theme.panelScaleFrom : 0.98f;
        if (inventoryCanvasGroup != null && inventoryCanvasGroup.alpha <= 0.001f)
        {
            if (_panelRect != null)
                _panelRect.localScale = _panelBaseScale * hiddenScale;
            if (_actionPanelRect != null)
                _actionPanelRect.localScale = _actionPanelBaseScale * hiddenScale;
        }

        RefreshSlotRevealCache();

        if (theme == null)
            Debug.LogWarning("[InventoryManager] InventoryTheme is not assigned; legacy prefab presentation will be used until integration.", this);

        if (_overlayCanvasGroup != null)
        {
            _overlayCanvasGroup.alpha = 0f;
            _overlayCanvasGroup.blocksRaycasts = false;
            _overlayCanvasGroup.interactable = false;
        }

        if (_overlayImage != null)
            _overlayImage.raycastTarget = false;
    }

    private void ApplySlotReveal(float elapsed, float slotDuration, float slotDelay, float staggerMax)
    {
        for (int i = 0; i < _slotRevealGroups.Count; i++)
        {
            CanvasGroup group = _slotRevealGroups[i];
            if (group == null)
                continue;

            float delay = Mathf.Min(staggerMax, _slotRevealRows[i] * slotDelay);
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - delay) / slotDuration));
            group.alpha = t;
        }
    }

    public void RefreshSlotRevealCache()
    {
        _slotRevealGroups.Clear();
        _slotRevealRows.Clear();

        InventorySlot[] authoredSlots = GetComponentsInChildren<InventorySlot>(true);
        int columns = 1;
        int collected = 0;

        for (int i = 0; i < authoredSlots.Length; i++)
        {
            InventorySlot slot = authoredSlots[i];
            if (slot == null || slot.GetComponent<ActionSlot>() != null)
                continue;

            CanvasGroup group = slot.GetComponent<CanvasGroup>();
            if (group == null)
                continue;

            if (collected == 0)
                columns = ResolveGridColumns(slot.transform.parent as RectTransform);

            // 계층 스캔 순서(= 레이아웃 순서)로 행을 매긴다.
            // GetSiblingIndex()는 같은 프레임에 Destroy 예약된 슬롯이 남아 있으면 어긋난다.
            _slotRevealGroups.Add(group);
            _slotRevealRows.Add(collected / columns);
            collected++;
        }
    }

    /// <summary>
    /// 행 stagger에 사용할 열 수를 GridLayoutGroup 설정에서 읽는다.
    /// 레이아웃(열 수)이 바뀌어도 코드 수정 없이 따라가도록 하드코딩하지 않는다.
    /// </summary>
    private static int ResolveGridColumns(RectTransform gridRect)
    {
        if (gridRect == null || !gridRect.TryGetComponent(out GridLayoutGroup grid))
            return 1;

        if (grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount)
            return Mathf.Max(1, grid.constraintCount);

        float stride = grid.cellSize.x + grid.spacing.x;
        if (stride <= Mathf.Epsilon)
            return 1;

        float usableWidth = gridRect.rect.width - grid.padding.left - grid.padding.right;
        return Mathf.Max(1, Mathf.FloorToInt((usableWidth + grid.spacing.x) / stride));
    }

    private void SetupSlotVisuals()
    {
        foreach (var slot in slots)
        {
            if (slot == null)
                continue;

            var slotImage = slot.GetComponent<Image>();
            var visual = slot.GetComponent<InventorySlotVisual>();
            if (visual == null)
            {
                Debug.LogWarning($"[InventoryManager] Authored InventorySlotVisual is missing on {slot.name}.", slot);
                continue;
            }

            bool isActionSlot = slot.GetComponent<ActionSlot>() != null;
            visual.Initialize(slotImage, isActionSlot);
            visual.SetOccupied(!slot.isEmpty);
        }

        foreach (var actionSlot in actionSlots)
        {
            if (actionSlot == null)
                continue;

            actionSlot.ApplyThemeDefaults();
        }
    }

    public bool IsScreenPointInsideInventoryUi(Vector2 screenPoint, Camera eventCamera = null)
    {
        if (_panelRect != null && RectTransformUtility.RectangleContainsScreenPoint(_panelRect, screenPoint, eventCamera))
            return true;

        if (_actionPanelRect != null && RectTransformUtility.RectangleContainsScreenPoint(_actionPanelRect, screenPoint, eventCamera))
            return true;

        return false;
    }

    private void ApplyInventoryCursor()
    {
        var texture = ResolveInventoryCursorTexture();
        if (texture == null)
            return;

        if (!texture.isReadable)
            texture = GetCpuAccessibleCursorTexture(texture);

        if (texture == null)
            return;

        Cursor.SetCursor(texture, inventoryCursorHotspot, inventoryCursorMode);
        _inventoryCursorApplied = true;
    }

    private void RestoreInventoryCursor()
    {
        if (!_inventoryCursorApplied)
            return;

        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        _inventoryCursorApplied = false;
    }

    private Texture2D ResolveInventoryCursorTexture()
    {
        if (inventoryCursorTexture != null)
            return inventoryCursorTexture;

        if (!string.IsNullOrWhiteSpace(inventoryCursorResourcePath))
        {
            var fromResource = Resources.Load<Texture2D>(inventoryCursorResourcePath);
            if (fromResource != null)
                return fromResource;
        }

#if UNITY_EDITOR
        const string fallbackPath = "Assets/MyAsset/Adobe Express - file (3).png";
        return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(fallbackPath);
#else
        return null;
#endif
    }

    private Texture2D GetCpuAccessibleCursorTexture(Texture2D source)
    {
        if (_cpuAccessibleInventoryCursor != null)
            return _cpuAccessibleInventoryCursor;

        var temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        var previousActive = RenderTexture.active;

        Graphics.Blit(source, temporary);
        RenderTexture.active = temporary;

        _cpuAccessibleInventoryCursor = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        _cpuAccessibleInventoryCursor.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        _cpuAccessibleInventoryCursor.Apply();

        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(temporary);

        return _cpuAccessibleInventoryCursor;
    }

    private void OnDestroy()
    {
        RestoreInventoryCursor();

        if (_cpuAccessibleInventoryCursor != null)
        {
            Destroy(_cpuAccessibleInventoryCursor);
            _cpuAccessibleInventoryCursor = null;
        }

        InstanceHandler.UnregisterInstance<InventoryManager>();
    }

    public bool AddItem(Item item, int overrideUpgradeTier = -1, InventoryReceipt receipt = default)
    {
        return AddNewItem(item, overrideUpgradeTier, receipt);
    }

    private bool AddNewItem(Item item, int overrideUpgradeTier = -1, InventoryReceipt receipt = default, int preferredSlot = -1)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            if (preferredSlot >= 0 && i != preferredSlot) continue;
            var slot = slots[i];
            if (slot == null)
                continue;

            if (!slot.isEmpty)
            {
                continue;
            }

            InventoryItemData itemData;
            int tier = 0;

            // 굴리지 않은 개체는 -1을 유지한다. Rarity 프로퍼티는 이때 정의의 폴백값을 돌려주는데,
            // 그 값을 저장해 버리면 "이미 굴린 Common"과 구분되지 않아 나중에 굴릴 기회를 잃는다.
            int rolledRarity = item.HasRolledRarity ? (int)item.Rarity : -1;

            if (item is WeaponItem weaponItem)
            {
                tier = overrideUpgradeTier >= 0 ? overrideUpgradeTier : weaponItem.UpgradeTier;
                itemData = new InventoryItemData()
                {
                    itemName = item.ItemName,
                    itemPicture = item.ItemPicture,
                    price = item.Price,
                    currentAmmo = weaponItem.CurrentAmmo,
                    upgradeTier = tier,
                    weaponData = weaponItem.WeaponData,
                    definition = item.Definition,
                    rarity = rolledRarity
                };
            }
            else
            {
                itemData = new InventoryItemData()
                {
                    itemName = item.ItemName,
                    itemPicture = item.ItemPicture,
                    price = item.Price,
                    currentAmmo = 0,
                    upgradeTier = 0,
                    definition = item.Definition,
                    rarity = rolledRarity
                };
            }

            if (!string.IsNullOrEmpty(receipt.token))
            {
                itemData.receiptToken = receipt.token;
                itemData.price = receipt.price;
                itemData.rarity = receipt.rarity;
                itemData.upgradeTier = receipt.upgradeTier;
                itemData.currentAmmo = receipt.ammo;
            }
            var inventoryItem = Instantiate(itemPrefab, slot.transform);
            inventoryItem.init(item.ItemName, item.ItemPicture, itemData.price, itemData.upgradeTier, item.Definition, 1, itemData.rarity);
            itemData.inventoryItem = inventoryItem;

            _inventoryData[i] = itemData;
            slot.SetItem(inventoryItem);

            // 액션슬롯 자동 장착 로직 그대로 유지
            ActionSlot actionSlot = slot.GetComponent<ActionSlot>();
            if (actionSlot != null && actionSlot == _activeActionSlot)
            {
                actionSlot.ToggleActive(false);
                actionSlot.ToggleActive(true);
            }
            RaiseInventoryChanged();
            return true;   // ✅ 성공
        }

        Debug.LogWarning("[InventoryManager] No empty slot! Failed to add item.");
        return false;      // ✅ 실패
    }

    public void DropItem(InventoryItem inventoryitem)
    {
        var data = _inventoryData.FirstOrDefault(x => x.inventoryItem == inventoryitem);
        if (data.inventoryItem == null) return;
        SaveActiveAmmo();
        data = _inventoryData.FirstOrDefault(x => x.inventoryItem == inventoryitem);
        if (NetworkPlayer.Local != null)
        {
            if (!string.IsNullOrEmpty(data.receiptToken))
                NetworkPlayer.Local.RequestDropReceiptServerRpc(data.receiptToken);
            else PromptPresenter.ShowPrompt("Item ownership could not be verified.");
            return;
        }
        if (NetworkManager.main != null) return; // A disconnected client cannot create world items.
        var source = ResolveInventoryItem(data);
        var movement = FPSMovement.LocalPlayerPosition;
        if (source == null || movement == null) return;
        var spawned = Instantiate(source, movement.transform.position + Vector3.up + movement.transform.forward, Quaternion.identity);
        spawned.SetPriceImmediateOnServer(data.price);
        spawned.SetRarityImmediateOnServer(data.rarity);
        if (spawned is WeaponItem weapon)
        {
            weapon.SetAmmoImmediateOnServer(data.currentAmmo);
            weapon.SetUpgradeTierImmediateOnServer(data.upgradeTier);
        }
        RemoveItem(inventoryitem);
    }

    public void SaveActiveAmmo()
    {
        if (_activeActionSlot == null || FPSMovement.LocalPlayerPosition == null) return;
        var manager = FPSMovement.LocalPlayerPosition.GetComponent<FPSWeaponManager>();
        var slot = _activeActionSlot.GetComponent<InventorySlot>();
        if (manager != null && manager.GetActiveItem() is Weapon weapon)
            UpdateWeaponAmmoAtSlot(slot, weapon.CurrentAmmo);
    }

    /// <summary>
    /// Drops every carried item around <paramref name="origin"/> as networked world items and empties
    /// the inventory. Called by the owning client when its player dies, so loot stays where the
    /// body fell and teammates can recover it (Lethal-style death cost).
    /// </summary>
    public int DropAllItemsAtDeath(Vector3 origin)
    {
        // Server death handling already scatters the full ledger at the authoritative body position.
        // Retry requests are harmless and recover any item whose first spawn failed.
        var player = NetworkPlayer.Local;
        if (player == null) return 0;
        var tokens = _inventoryData.Where(x => !string.IsNullOrEmpty(x.receiptToken)).Select(x => x.receiptToken).ToArray();
        foreach (var token in tokens) player.RequestDropReceiptServerRpc(token);
        return tokens.Length;
    }

    private void RemoveItem(InventoryItem inventoryItem)
        {
        for (int i = 0; i < _inventoryData.Length; i++)
        {
            var data = _inventoryData[i];
            if (data.inventoryItem != inventoryItem)
                continue;


            NotifyDiscard(data.receiptToken);
            _inventoryData[i] = default;
            slots[i].SetItem(null);
            Destroy(inventoryItem.gameObject);
        }
        RaiseInventoryChanged();
    }
    /// <summary>
    /// 현재 활성화된 ActionSlot의 무기 탄약 업데이트
    /// </summary>
    public void UpdateWeaponAmmo(string weaponName, int currentAmmo)
    {
        if (_activeActionSlot == null) return;

        var inventorySlot = _activeActionSlot.GetComponent<InventorySlot>();
        if (inventorySlot == null) return;

        int slotIndex = slots.IndexOf(inventorySlot);
        if (slotIndex < 0 || slotIndex >= _inventoryData.Length) return;

        var data = _inventoryData[slotIndex];
        if (data.itemName == weaponName)
        {
            data.currentAmmo = currentAmmo;
            _inventoryData[slotIndex] = data;
            NetworkPlayer.Local?.ReportRemainingAmmoServerRpc(data.receiptToken, currentAmmo);
            Debug.Log($"[InventoryManager] Updated ammo for {weaponName} in slot {slotIndex}: {currentAmmo}");
        }
    }
    /// <summary>
    /// 특정 슬롯의 무기 탄약 업데이트 (슬롯 직접 지정)
    /// </summary>
    public void UpdateWeaponAmmoAtSlot(InventorySlot slot, int currentAmmo)
    {
        int slotIndex = slots.IndexOf(slot);
        if (slotIndex < 0 || slotIndex >= _inventoryData.Length) return;

        var data = _inventoryData[slotIndex];
        data.currentAmmo = currentAmmo;
        _inventoryData[slotIndex] = data;
        NetworkPlayer.Local?.ReportRemainingAmmoServerRpc(data.receiptToken, currentAmmo);

        Debug.Log($"[InventoryManager] Updated ammo at slot {slotIndex}: {currentAmmo}");
    }

    public Item GetItemByName(string itemName)
    {
        Item registered = allItems.Find(x => x != null && x.ItemName == itemName);
        if (registered != null)
            return registered;

        return FindDungeonLootPrefab(itemName);
    }

    private readonly Dictionary<string, Item> _lootPrefabCache = new();
    private bool _lootPrefabCacheBuilt;

    /// <summary>
    /// Dungeon loot (Banana, Trophy, ...) is authored in the dungeon spawn lists, not in
    /// <see cref="allItems"/>, so dropping or death-dropping it used to fail with
    /// "Failed to resolve drop prefab". Resolve those prefabs from the active map list.
    /// </summary>
    private Item FindDungeonLootPrefab(string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
            return null;

        if (!_lootPrefabCacheBuilt)
            BuildLootPrefabCache();

        return _lootPrefabCache.TryGetValue(itemName, out Item prefab) ? prefab : null;
    }

    private void BuildLootPrefabCache()
    {
        _lootPrefabCacheBuilt = true;
        _lootPrefabCache.Clear();

        NetworkDungeonController controller = FindFirstObjectByType<NetworkDungeonController>();
        DungeonMapList mapList = controller != null ? controller.MapList : null;
        if (mapList == null || mapList.Entries == null)
            return;

        foreach (var entry in mapList.Entries)
        {
            ItemSpawnlist source = entry != null && entry.selector != null ? entry.selector.Source : null;
            if (source == null || source.Items == null)
                continue;

            foreach (var lootEntry in source.Items)
            {
                if (lootEntry == null || lootEntry.prefab == null)
                    continue;

                Item item = lootEntry.prefab.GetComponent<Item>();
                if (item == null || string.IsNullOrEmpty(item.ItemName))
                    continue;

                if (!_lootPrefabCache.ContainsKey(item.ItemName))
                    _lootPrefabCache.Add(item.ItemName, item);
            }
        }
    }

/// <summary>
    /// 아이템 프리펛 GameObject 반환 (네트워크 드롭용)
    /// </summary>
    public GameObject GetItemPrefab(string itemName)
    {
        return GetItemPrefab(itemName, null);
    }

    public GameObject GetItemPrefab(string itemName, string weaponDataName)
    {
        if (!string.IsNullOrEmpty(weaponDataName))
        {
            WeaponData exactWeaponData = GetWeaponDataByAssetName(weaponDataName);
            if (exactWeaponData != null && exactWeaponData.itemPrefab != null)
                return exactWeaponData.itemPrefab;
        }

        var item = GetItemByName(itemName);
        return item != null ? item.gameObject : null;
    }


    private Item GetItemByActionSlot(ActionSlot actionSlot)
    {
        var inventorySlot = actionSlot.GetComponent<InventorySlot>();
        for(int i = slots.Count - 1; i >= 0; i--)
        {
            if (slots[i] == inventorySlot)
                return ResolveInventoryItem(_inventoryData[i]);
        }
        return null;
    }

    /* /// <summary>
     /// InventorySlot에서 Item 인스턴스 가져오기 (Phase 5용)
     /// </summary>
     public Item GetItemFromInventorySlot(InventorySlot slot)
     {
         int slotIndex = slots.IndexOf(slot);
         if (slotIndex < 0 || slotIndex >= _inventoryData.Length)
             return null;

         var data = _inventoryData[slotIndex];
         if (string.IsNullOrEmpty(data.itemName))
             return null;

         // itemName으로 Item 찾기
         return GetItemByName(data.itemName); //jeongseok 지금 여기서 초기 ammo를 가지고 잇는 정보를 가져오기 떄문에 문제
     }*/

    public Item GetItemFromInventorySlot(InventorySlot slot)
    {
        int slotIndex = slots.IndexOf(slot);
        if (slotIndex < 0 || slotIndex >= _inventoryData.Length)
            return null;

        var data = _inventoryData[slotIndex];
        if (string.IsNullOrEmpty(data.itemName))
            return null;

        // 프리팹 반환 (기존 그대로)
        return ResolveInventoryItem(data);
    }

    // 탄약 정보를 별도로 가져오는 메소드 추가
    public int GetWeaponAmmoFromSlot(InventorySlot slot)
    {
        int slotIndex = slots.IndexOf(slot);
        if (slotIndex < 0 || slotIndex >= _inventoryData.Length)
            return -1;

        return _inventoryData[slotIndex].currentAmmo;
    }

    /// <summary>
    /// 특정 슬롯의 강화 티어 가져오기
    /// </summary>
    public int GetUpgradeTierFromSlot(InventorySlot slot)
    {
        int slotIndex = slots.IndexOf(slot);
        if (slotIndex < 0 || slotIndex >= _inventoryData.Length)
            return 0;

        return _inventoryData[slotIndex].upgradeTier;
    }


    public void ItemMoved(InventoryItem item, InventorySlot newslot)
    {
        Debug.Log($"[ItemMoved] {item.name} -> {newslot.name}");
        // 리로드 중에는 이동 금지 (기존 로직 유지)
        if (FPSMovement.LocalPlayerPosition != null &&
            FPSController._fpsconnect != null &&
            FPSController._fpsconnect.IsReloading)
        {
            Debug.Log("[Inventory] Cannot move item to/from ActionSlot during reload.");
            return;
        }

        int newSlotIndex = slots.IndexOf(newslot);
        int oldSlotIndex = Array.FindIndex(_inventoryData, x => x.inventoryItem == item);

        if (newSlotIndex < 0)
        {
            Debug.LogError("[Inventory] New slot not found in slots list!", this);
            return;
        }

        if (oldSlotIndex == -1)
        {
            Debug.LogError($"[Inventory] Couldn't find item {item.name} in inventory data!", this);
            return;
        }

        InventorySlot oldSlot = slots[oldSlotIndex];

        var oldData = _inventoryData[oldSlotIndex];
        var newData = _inventoryData[newSlotIndex];

        bool newSlotEmpty = string.IsNullOrEmpty(newData.itemName) || newData.inventoryItem == null;

        if (newSlotEmpty)
        {
            // ========== 단순 이동 ==========
            _inventoryData[oldSlotIndex] = default;
            _inventoryData[newSlotIndex] = oldData;

            // UI도 같이 갱신
            oldSlot.SetItem(null);
            newslot.SetItem(oldData.inventoryItem);
        }
        else
        {
            // ========== 스왑(swap) ==========
            _inventoryData[oldSlotIndex] = newData;
            _inventoryData[newSlotIndex] = oldData;

            // UI 슬롯도 서로 교체
            oldSlot.SetItem(newData.inventoryItem);
            newslot.SetItem(oldData.inventoryItem);
        }

        // ======= 액션 슬롯 관련 처리 (기존 로직을 swap 후 기준으로 유지) =======
        ActionSlot oldActionSlot = oldSlot.GetComponent<ActionSlot>();
        ActionSlot newActionSlot = newslot.GetComponent<ActionSlot>();

        // 옛날 슬롯이 활성 액션 슬롯이었는데 아이템이 빠져나간 경우 → Fist로 복귀
        if (oldActionSlot != null && oldActionSlot == _activeActionSlot)
        {
            oldActionSlot.ToggleActive(false);
            oldActionSlot.ToggleActive(true);
        }

        // 새 슬롯이 활성 액션 슬롯인 경우 → 장착 갱신
        if (newActionSlot != null && newActionSlot == _activeActionSlot)
        {
            newActionSlot.ToggleActive(false);
            newActionSlot.ToggleActive(true);
        }
    }

    // ---- ActionSlot 변경 제한 (Sprint/Slide) ----
    private FPSMovement GetLocalMovement()
    {
        // 멀티플레이에서 로컬 플레이어를 확실히 잡기 위해 localInventory 우선 사용
        if (PlayerInventory.localInventory != null)
        {
            var mv = PlayerInventory.localInventory.GetComponent<FPSMovement>();
            if (mv != null) return mv;
        }

        // 폴백(기존 코드 호환)
        return FPSMovement.LocalPlayerPosition;
    }

    private bool IsLocalSprintingOrSliding()
    {
        var mv = GetLocalMovement();
        if (mv == null) return false;

        return mv.MovementState == FPSMovementState.Sprinting
            || mv.MovementState == FPSMovementState.Sliding;
    }

    private FPSController GetLocalController()
    {
        // 로컬 플레이어 기준으로 FPSController 찾기 (추천)
        if (PlayerInventory.localInventory != null)
        {
            var ctrl = PlayerInventory.localInventory.GetComponentInParent<FPSController>();
            if (ctrl != null) return ctrl;

            ctrl = PlayerInventory.localInventory.GetComponent<FPSController>();
            if (ctrl != null) return ctrl;
        }

        // 폴백 (단, _fpsconnect는 다른 플레이어로 덮일 수 있음)
        return FPSController._fpsconnect;
    }

    private bool IsLocalReloading()
    {
        var ctrl = GetLocalController();
        return ctrl != null && ctrl.IsReloading;
    }
    private bool IsLocalSprintRecovering()
    {
        var ctrl = GetLocalController();
        return ctrl != null && ctrl.IsSprintRecovering(sprintRecoverBlockThreshold);
    }
    private bool CanSwitchNow()
    {
        if (IsLocalReloading()) return false;

        // Sprint/Slide/Recovery 가드는 제거한다.
        // 달리기 중에도 ActionSlot을 즉시 바꿀 수 있어야 한다.
        return true;
    }

    /*public void SetActionSlotActive(ActionSlot actionSlot)
    {
        if (IsLocalReloading()) return;

        if (IsLocalSprintingOrSliding() || IsLocalSprintRecovering())
        {
            Debug.Log("[Inventory] Cannot switch action slot during sprint recovery.");
            return;
        }

        if (IsLocalReloading())
        {
            Debug.Log("[Inventory] Cannot switch action slot during reload.");
            return;
        }

        if (IsLocalSprintingOrSliding())
        {
            Debug.Log("[Inventory] Cannot switch action slot while sprinting.");
            return;
        }

        // ↓ 조건이 안 맞으면 여기부터 계속 실행됨! (끊기지 않음)
        if (_activeActionSlot == actionSlot) return;

        if (_activeActionSlot != null)
            _activeActionSlot.ToggleActive(false);

        actionSlot.ToggleActive(true);
        _activeActionSlot = actionSlot;
    }*/
    public void SetActionSlotActive(ActionSlot actionSlot)
    {
        if (!CanSwitchNow())
        {
            Debug.Log("[Inventory] Cannot switch action slot right now.");
            return;
        }

        ApplyActionSlot(actionSlot);
    }

    public bool ActivateActionSlotByIndex(int slotIndex)
    {
        ActionSlot actionSlot = FindActionSlotByIndex(slotIndex);
        if (actionSlot == null)
        {
            Debug.LogWarning($"[Inventory] Action slot {slotIndex} not found.");
            return false;
        }

        SetActionSlotActive(actionSlot);
        return _activeActionSlot == actionSlot;
    }

    private ActionSlot FindActionSlotByIndex(int slotIndex)
    {
        if (actionSlots == null)
            actionSlots = new List<ActionSlot>();

        for (int i = 0; i < actionSlots.Count; i++)
        {
            ActionSlot slot = actionSlots[i];
            if (slot != null && slot.Index == slotIndex)
                return slot;
        }

        ActionSlot[] discoveredSlots = GetComponentsInChildren<ActionSlot>(true);
        for (int i = 0; i < discoveredSlots.Length; i++)
        {
            ActionSlot slot = discoveredSlots[i];
            if (slot == null)
                continue;

            if (!actionSlots.Contains(slot))
                actionSlots.Add(slot);

            if (slot.Index == slotIndex)
                return slot;
        }

        return null;
    }

    private void ApplyActionSlot(ActionSlot actionSlot)
    {
        if (_activeActionSlot == actionSlot) return;

        if (_activeActionSlot != null)
            _activeActionSlot.ToggleActive(false);

        actionSlot.ToggleActive(true);
        _activeActionSlot = actionSlot;
    }


    /// 특정 이름의 아이템 개수 세기 (Phase 6.5)
    /// </summary>
    public int CountItemsByName(string itemName)
    {
        int count = 0;
        for (int i = 0; i < _inventoryData.Length; i++)
        {
            if (_inventoryData[i].itemName == itemName)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// 특정 이름의 아이템 1개 제거 (Phase 6.5)
    /// </summary>
    public bool RemoveItemByName(string itemName)
    {
        for (int i = 0; i < _inventoryData.Length; i++)
        {
            if (_inventoryData[i].itemName == itemName)
            {
                var slot = slots[i];
                var item = _inventoryData[i].inventoryItem;
                
                NotifyDiscard(_inventoryData[i].receiptToken);
                Destroy(item.gameObject);
                _inventoryData[i] = default;
                slot.SetItem(null);
                
                Debug.Log($"[InventoryManager] Removed item: {itemName}");
                RaiseInventoryChanged();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 특정 이름의 아이템 중 최고 강화 티어 반환 (-1 = 없음)
    /// </summary>
    public int GetHighestUpgradeTier(string itemName)
    {
        int highest = -1;
        for (int i = 0; i < _inventoryData.Length; i++)
        {
            if (_inventoryData[i].itemName == itemName)
                highest = Mathf.Max(highest, _inventoryData[i].upgradeTier);
        }
        return highest;
    }

    /// <summary>
    /// 특정 이름 + 특정 티어의 아이템 1개 제거
    /// </summary>
    public bool RemoveItemWithTier(string itemName, int tier)
    {
        for (int i = 0; i < _inventoryData.Length; i++)
        {
            if (_inventoryData[i].itemName == itemName && _inventoryData[i].upgradeTier == tier)
            {
                var slot = slots[i];
                var item = _inventoryData[i].inventoryItem;

                NotifyDiscard(_inventoryData[i].receiptToken);
                Destroy(item.gameObject);
                _inventoryData[i] = default;
                slot.SetItem(null);

                Debug.Log($"[InventoryManager] Removed item: {itemName} +{tier}");
                RaiseInventoryChanged();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 특정 이름의 아이템 중 가장 낮은 티어 1개 제거 (재료 소비용)
    /// </summary>
    public bool RemoveItemLowestTier(string itemName)
    {
        int bestIndex = -1;
        int bestTier = int.MaxValue;

        for (int i = 0; i < _inventoryData.Length; i++)
        {
            if (_inventoryData[i].itemName == itemName && _inventoryData[i].upgradeTier < bestTier)
            {
                bestTier = _inventoryData[i].upgradeTier;
                bestIndex = i;
            }
        }

        if (bestIndex < 0) return false;

        var slot = slots[bestIndex];
        var item = _inventoryData[bestIndex].inventoryItem;

        NotifyDiscard(_inventoryData[bestIndex].receiptToken);
        Destroy(item.gameObject);
        _inventoryData[bestIndex] = default;
        slot.SetItem(null);

        Debug.Log($"[InventoryManager] Removed lowest tier item: {itemName} +{bestTier}");
        RaiseInventoryChanged();
        return true;
    }

    /// <summary>
    /// 특정 이름의 아이템 슬롯 데이터 가져오기 (강화 시스템용)
    /// </summary>
    public InventoryItemData GetItemDataByNameAndTier(string itemName, int tier)
    {
        for (int i = 0; i < _inventoryData.Length; i++)
        {
            if (_inventoryData[i].itemName == itemName && _inventoryData[i].upgradeTier == tier)
                return _inventoryData[i];
        }
        return default;
    }

    [ContextMenu("Debug: Print Active Item")]
    private void DebugPrintActiveItem()
    {
        if (_activeActionSlot == null)
        {
            Debug.Log("[DEBUG] No active ActionSlot!");
            return;
        }

        var inventorySlot = _activeActionSlot.GetComponent<InventorySlot>();
        if (inventorySlot == null)
        {
            Debug.Log("[DEBUG] Active ActionSlot has no InventorySlot!");
            return;
        }

        int slotIndex = slots.IndexOf(inventorySlot);
        if (slotIndex < 0 || slotIndex >= _inventoryData.Length)
        {
            Debug.Log("[DEBUG] Slot index out of range!");
            return;
        }

        var data = _inventoryData[slotIndex];
        if (string.IsNullOrEmpty(data.itemName))
        {
            Debug.Log("[DEBUG] Active slot is empty (Fist)");
            return;
        }

        Debug.Log($"[DEBUG] Active Item: {data.itemName}, Price: {data.price}, Ammo: {data.currentAmmo}");
    }

    /// <summary>
    /// 현재 손에 든 아이템 데이터 가져오기 (외부 접근용)
    /// </summary>
    public InventoryItemData? GetActiveItemData()
    {
        if (_activeActionSlot == null) return null;

        var inventorySlot = _activeActionSlot.GetComponent<InventorySlot>();
        if (inventorySlot == null) return null;

        int slotIndex = slots.IndexOf(inventorySlot);
        if (slotIndex < 0 || slotIndex >= _inventoryData.Length) return null;

        var data = _inventoryData[slotIndex];
        if (string.IsNullOrEmpty(data.itemName)) return null;

        return data;
    }

    /// <summary>
    /// 현재 손에 든 아이템 제거 (판매용)
    /// </summary>
    public bool RemoveActiveItem()
    {
        if (_activeActionSlot == null) return false;

        var inventorySlot = _activeActionSlot.GetComponent<InventorySlot>();
        if (inventorySlot == null) return false;

        int slotIndex = slots.IndexOf(inventorySlot);
        if (slotIndex < 0 || slotIndex >= _inventoryData.Length) return false;

        var data = _inventoryData[slotIndex];
        if (string.IsNullOrEmpty(data.itemName)) return false;

        // 아이템 제거
        NotifyDiscard(data.receiptToken);
        Destroy(data.inventoryItem.gameObject);
        _inventoryData[slotIndex] = default;
        inventorySlot.SetItem(null);

        // Fist로 복귀
        _activeActionSlot.ToggleActive(false);
        _activeActionSlot.ToggleActive(true);

        Debug.Log($"[InventoryManager] Removed active item: {data.itemName}");
        RaiseInventoryChanged();
        return true;
    }

    public bool TryDropActiveItem()
    {
        if (SkillWebTerminalInteraction.BlocksGameplayInput)
            return false;

        if (_activeActionSlot == null)
            return false;

        InventorySlot inventorySlot = _activeActionSlot.GetComponent<InventorySlot>();
        if (inventorySlot == null)
            return false;

        int slotIndex = slots.IndexOf(inventorySlot);
        if (slotIndex < 0 || slotIndex >= _inventoryData.Length)
            return false;

        InventoryItem inventoryItem = _inventoryData[slotIndex].inventoryItem;
        if (inventoryItem == null)
            return false;

        DropItem(inventoryItem);
        return true;
    }

    private void TryActivateDefaultActionSlot()
    {
        if (actionSlots == null || actionSlots.Count == 0) return;

        // 인덱스 보장이 확실하면 [0], 아니면 Index == 1을 찾아도 됨
        var first = actionSlots[0]; // 또는 actionSlots.Find(s => s.Index == 1);
        if (first != null)
            first.RequestActiveSlot();   // ← 여기만 교체
    }

    /// <summary>
    /// 인벤토리 슬롯 동적 추가 (확장용)
    /// </summary>
    public void RegisterExtraSlot(InventorySlot slot)
    {
        if (slot == null) return;
        if (slots.Contains(slot)) return;

        slots.Add(slot);

        // _inventoryData 배열 크기를 slots.Count에 맞춰 확장
        if (_inventoryData == null)
        {
            _inventoryData = new InventoryItemData[slots.Count];
        }
        else
        {
            System.Array.Resize(ref _inventoryData, slots.Count);
        }

        var slotImage = slot.GetComponent<Image>();
        var visual = slot.GetComponent<InventorySlotVisual>();
        if (visual != null)
        {
            visual.Initialize(slotImage, false);
            visual.SetOccupied(!slot.isEmpty);
        }
        else
        {
            Debug.LogError($"[InventoryManager] Authored InventorySlotVisual is missing on {slot.name}.", slot);
        }

        Debug.Log($"[InventoryManager] Extra slot registered. Now slots = {slots.Count}");
    }

    /// <summary>
    /// Removes a dynamically replaced slot while preserving the slots/data index contract.
    /// Call this before destroying the slot GameObject.
    /// </summary>
    public void UnregisterSlot(InventorySlot slot)
    {
        if (slot == null)
            return;

        int index = slots.IndexOf(slot);
        if (index < 0)
            return;

        slots.RemoveAt(index);

        if (_inventoryData != null && index < _inventoryData.Length)
        {
            for (int i = index; i < _inventoryData.Length - 1; i++)
                _inventoryData[i] = _inventoryData[i + 1];

            System.Array.Resize(ref _inventoryData, _inventoryData.Length - 1);
        }

        if (slot.TryGetComponent(out ActionSlot actionSlot))
        {
            actionSlots.Remove(actionSlot);
            if (_activeActionSlot == actionSlot)
                _activeActionSlot = null;
        }
    }

    [Serializable]
    public struct InventoryItemData
    {
        public string receiptToken;
        public string itemName;
        public Sprite itemPicture;
        public InventoryItem inventoryItem;
        public int price;
        public int currentAmmo;
        public int upgradeTier;
        public WeaponData weaponData;
        public ItemDefinition definition;

        /// <summary>이 개체가 굴린 등급. -1이면 아직 굴리지 않음.</summary>
        public int rarity;
    }

    public bool ConsumeReloadAmmo(string ammoName)
    {
        if (NetworkPlayer.Local == null) return RemoveItemByName(ammoName);
        var weapon = GetActiveItemData();
        string ammoToken = FindReceipt(ammoName);
        if (!weapon.HasValue || string.IsNullOrEmpty(weapon.Value.receiptToken) || string.IsNullOrEmpty(ammoToken)) return false;
        SaveActiveAmmo();
        NetworkPlayer.Local.ReloadReceiptServerRpc(weapon.Value.receiptToken, ammoToken);
        return true;
    }

    private static void NotifyDiscard(string token)
    {
        if (!string.IsNullOrEmpty(token)) NetworkPlayer.Local?.DiscardReceiptServerRpc(token);
    }

    public string FindReceipt(string name, string excluding = null, bool highest = false)
    {
        InventoryItemData? result = null;
        foreach (var data in _inventoryData)
            if (data.itemName == name && !string.IsNullOrEmpty(data.receiptToken) && data.receiptToken != excluding
                && (!result.HasValue || (highest ? data.upgradeTier > result.Value.upgradeTier : data.upgradeTier < result.Value.upgradeTier)))
                result = data;
        return result.HasValue ? result.Value.receiptToken : null;
    }

    public bool RemoveReceipt(string token)
    {
        if (string.IsNullOrEmpty(token) || _inventoryData == null) return false;
        for (int i = 0; i < _inventoryData.Length; i++)
        {
            var data = _inventoryData[i];
            if (data.receiptToken != token) continue;
            // An acknowledgement may arrive after the slot visual has been destroyed.
            // Clear by receipt/index without sending another discard RPC back to the server.
            _inventoryData[i] = default;
            var slot = i < slots.Count ? slots[i] : null;
            if (slot != null) slot.SetItem(null);
            if (data.inventoryItem != null) Destroy(data.inventoryItem.gameObject);
            if (slot != null && _activeActionSlot != null && slot.GetComponent<ActionSlot>() == _activeActionSlot)
            {
                _activeActionSlot.ToggleActive(false);
                _activeActionSlot.ToggleActive(true);
            }
            RaiseInventoryChanged();
            return true;
        }
        return false;
    }

    public void ClearReceipts()
    {
        if (_inventoryData == null) return;
        var tokens = _inventoryData.Select(d => d.receiptToken).Where(t => !string.IsNullOrEmpty(t)).ToArray();
        foreach (string token in tokens) RemoveReceipt(token);
    }

    public bool TryCaptureCheckpoint(ServerInventoryLedger ledger, out RunInventorySave[] saved, out string error)
    {
        saved = null;
        error = "The local inventory and server receipts have not synchronized yet.";
        SaveActiveAmmo();
        var entries = new List<RunInventorySave>();
        for (int i = 0; i < _inventoryData.Length; i++)
        {
            var data = _inventoryData[i];
            if (string.IsNullOrEmpty(data.itemName)) continue;
            if (!ledger.TryGet(data.receiptToken, out var receipt)) return false;
            if (data.weaponData != null)
            {
                ledger.ReduceAmmo(receipt.token, Mathf.Max(0, data.currentAmmo));
                ledger.TryGet(receipt.token, out receipt);
            }
            entries.Add(new RunInventorySave { slot = i, receipt = receipt });
        }
        if (entries.Count != ledger.Count) return false;
        saved = entries.ToArray();
        error = null;
        return true;
    }

    public bool CanRestoreCheckpoint(RunInventorySave[] saved)
    {
        foreach (var entry in saved)
        {
            var prefab = GetItemPrefab(entry.receipt.itemName, entry.receipt.weaponDataName);
            if (prefab == null || prefab.GetComponent<Item>() == null) return false;
        }
        return true;
    }

    public bool RestoreCheckpointItem(RunInventorySave saved)
    {
        var prefab = GetItemPrefab(saved.receipt.itemName, saved.receipt.weaponDataName);
        return prefab != null && AddNewItem(prefab.GetComponent<Item>(), saved.receipt.upgradeTier, saved.receipt, saved.slot);
    }

    public void ReplaceReceipt(string previous, string material, InventoryReceipt next)
    {
        for (int i = 0; i < _inventoryData.Length; i++)
        {
            var data = _inventoryData[i];
            if (data.receiptToken != previous || string.IsNullOrEmpty(previous)) continue;
            RemoveReceipt(material);
            data.receiptToken = next.token;
            data.price = next.price;
            data.rarity = next.rarity;
            data.upgradeTier = next.upgradeTier;
            data.currentAmmo = next.ammo;
            _inventoryData[i] = data;
            data.inventoryItem.init(data.itemName, data.itemPicture, data.price, data.upgradeTier, data.definition, 1, data.rarity);
            if (slots[i].GetComponent<ActionSlot>() == _activeActionSlot)
            {
                _activeActionSlot.ToggleActive(false);
                _activeActionSlot.ToggleActive(true);
            }
            RaiseInventoryChanged();
            return;
        }
    }

    private Item ResolveInventoryItem(InventoryItemData data)
    {
        if (data.weaponData != null && data.weaponData.itemPrefab != null
            && data.weaponData.itemPrefab.TryGetComponent<Item>(out Item exactWeaponItem))
        {
            return exactWeaponItem;
        }

        return GetItemByName(data.itemName);
    }

    private WeaponData GetWeaponDataByAssetName(string weaponDataName)
    {
        if (string.IsNullOrEmpty(weaponDataName))
            return null;

        for (int i = 0; i < allItems.Count; i++)
        {
            if (allItems[i] is not WeaponItem weaponItem)
                continue;

            WeaponData data = weaponItem.WeaponData;
            if (data != null && data.name == weaponDataName)
                return data;
        }

        return null;
    }
}
