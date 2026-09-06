using UnityEngine;
using PurrNet;
using UnityEngine.Animations.Rigging;

public class Item : AInteractable
{
    [SerializeField] private string itemName;
    [SerializeField] private Sprite itemPicture;
    [SerializeField] private SyncVar<int> price = new(0);
    [SerializeField] private Rigidbody rb;
    [SerializeField] private ItemDefinition definition;

    [Header("Random Price Settings")]
    [SerializeField] private bool useRandomPrice = false;  // 랜덤 가격 사용 여부
    [SerializeField] private int minPrice = 100;           // 최소 가격
    [SerializeField] private int maxPrice = 1000;          // 최대 가격
    [SerializeField] private bool applyOnAwake = true;     // Awake에서 적용할지 여부

    // 캐시된 컴포넌트들
    private PromptPresenter promptPresenter;
    private InventoryManager inventoryManager;
    private readonly SyncVar<bool> _pickupLocked = new(ownerAuth: false);

    /// <summary>
    /// 이 개체의 등급. -1은 "아직 굴리지 않음"이고, 이때는 정의의 기본 등급을 쓴다.
    /// 등급은 아이템 종류가 아니라 개체의 속성이므로 price/upgradeTier와 같이 인스턴스에 산다.
    /// </summary>
    private readonly SyncVar<int> _rarity = new(-1);
    private string _pendingPickupToken;
    private string _serverPickupToken;
    private PlayerID _serverPickupPlayer;
    private bool _serverPickupHasPlayer;
    private InventoryReceipt _serverPickupReceipt;

    /// <summary>
    /// 아이템 식별자. 서버 프리팹 카탈로그의 조회 키이므로
    /// ItemDefinition으로 덮어쓰지 않는다. 화면 표시에는 DisplayName을 사용할 것.
    /// </summary>
    public string ItemName => itemName;
    public int Price => price;
    public Sprite ItemPicture => definition != null && definition.icon != null
        ? definition.icon
        : itemPicture;
    public ItemDefinition Definition => definition;

    /// <summary>
    /// 이 개체의 등급. 아직 굴리지 않았으면 정의의 기본 등급으로 폴백한다.
    /// </summary>
    public ItemRarity Rarity => _rarity.value >= 0
        ? (ItemRarity)_rarity.value
        : (definition != null ? definition.rarity : ItemRarity.Common);

    public bool HasRolledRarity => _rarity.value >= 0;

    /// <summary>
    /// 화면에 표시할 이름 (ItemDefinition.displayName 우선, 강화 무기는 "AK +5" 식으로 오버라이드)
    /// </summary>
    public virtual string DisplayName => definition != null && !string.IsNullOrWhiteSpace(definition.displayName)
        ? definition.displayName
        : itemName;

    private void Awake()
    {
        if (useRandomPrice && applyOnAwake)
        {
            // 랜덤 가격 적용
            ApplyRandomPrice();
        }
        else if (price == 0)
        {
            // 가격이 설정되지 않았으면 Min Price를 기본값으로 사용
            price.value = minPrice;
            Debug.Log($"[Item] {itemName} had no price set, using minPrice as default: {price}원");
        }
    }


    protected override void OnSpawned()
    {
        base.OnSpawned();

        // Corpse pickup items must not override monster navigation or ragdoll physics.
        if (rb != null && !TryGetComponent<MonsterHealth>(out _)) rb.isKinematic = !isServer;

        // 런타임 스폰 시 랜덤 가격 적용 (코드로 생성된 아이템용)
        if (useRandomPrice && !applyOnAwake)
        {
            // 랜덤 가격 적용
            ApplyRandomPrice();
        }
        else if (price == 0 && !applyOnAwake)
        {
            // 런타임 스폰인데 가격이 0이면 Min Price 사용
            price.value = minPrice;
            Debug.Log($"[Item] {itemName} spawned with no price, using minPrice: {price}원");
        }

        // 등급은 가격이 정해진 뒤에 굴린다(등급 배수가 가격에 곱해질 수 있으므로).
        if (isServer)
            RollRarityIfNeeded();

        if (promptPresenter == null)
            promptPresenter = PromptPresenter.Instance ?? FindObjectOfType<PromptPresenter>();

        if (inventoryManager == null)
        {
            if (!InstanceHandler.TryGetInstance(out inventoryManager))
                inventoryManager = FindObjectOfType<InventoryManager>();
        }
    }

    /// <summary>
    /// 랜덤 가격 적용
    /// </summary>
    private void ApplyRandomPrice()
    {
        if (!useRandomPrice) return;

        int randomPrice = Random.Range(minPrice, maxPrice + 1);  // maxPrice 포함
        price.value = randomPrice;
        Debug.Log($"[Item] Random price applied: {itemName} = {price}원 (Range: {minPrice}~{maxPrice})");
    }

    /// <summary>
    /// 수동으로 랜덤 가격 적용 (디버그용)
    /// </summary>
    [ContextMenu("Apply Random Price Now")]
    public void ApplyRandomPriceManual()
    {
        if (useRandomPrice)
        {
            ApplyRandomPrice();
        }
        else
        {
            Debug.LogWarning($"[Item] Random price is disabled for {itemName}");
        }
    }

    [ContextMenu("Test Pickup")]
    public void Pickup()
    {
        NetworkIdentity identity = GetComponent<NetworkIdentity>();
        if (identity != null && identity.isSpawned && NetworkManager.main != null)
        {
            TryBeginNetworkPickup();
            return;
        }

        if (NetworkManager.main != null) return; // Unregistered/local visuals are not transferable inventory.
        if (TryAddToInventory())
        {
            Destroy(gameObject);
        }
    }


    public override void Interact()
    {
        Pickup();
    }

    public override void OnHover()
    {
        base.OnHover();

        bool canPickup = CanInteract();

        if (promptPresenter != null)
            promptPresenter.Show(BuildHoverPrompt(canPickup));

    }

    public override void OnStopHover()
    {
        base.OnStopHover();

        if (promptPresenter != null)
            promptPresenter.Hide();

    }

    public override bool CanHover()
    {
        return base.CanHover() && gameObject.activeInHierarchy;
    }

    public override bool CanInteract()
    {
        return base.CanInteract() && gameObject.activeInHierarchy && !_pickupLocked.value && HasInventorySpace();
    }

    public void SetPrice(int newPrice)
    {
        if (isSpawned && !isServer) return;
        price.value = Mathf.Max(0, newPrice);
        Debug.Log($"[Item] Price set to {price} for {itemName}");
    }

    public void SetPriceImmediateOnServer(int newPrice)
    {
        if (isSpawned && !isServer) return;
        price.value = newPrice;
    }

    /// <summary>
    /// 드롭 재스폰처럼 이미 정해진 등급을 그대로 복원할 때 쓴다.
    /// 음수를 넣으면 "굴리지 않음" 상태로 되돌아간다.
    /// </summary>
    public void SetRarityImmediateOnServer(int rarityIndex)
    {
        if (isSpawned && !isServer) return;
        _rarity.value = rarityIndex;
    }

    /// <summary>
    /// 아직 굴리지 않은 개체라면 확률표에 따라 등급을 정한다. 서버에서만 호출한다.
    /// 이미 등급이 있는 개체(주운 뒤 다시 떨군 것 등)는 건드리지 않는다.
    /// </summary>
    private void RollRarityIfNeeded()
    {
        if (_rarity.value >= 0)
            return;

        if (definition == null || !definition.rollRarity)
            return;

        ItemRarityTable table = definition.rarityTable != null
            ? definition.rarityTable
            : ItemRarityDefaults.Table;

        if (table == null)
            return;

        ItemRarity rolled = table.Roll();
        _rarity.value = (int)rolled;

        float multiplier = table.GetPriceMultiplier(rolled);
        if (!Mathf.Approximately(multiplier, 1f))
            price.value = Mathf.Max(0, Mathf.RoundToInt(price.value * multiplier));
    }

    private void DespawnAfterSuccessfulPickup()
    {
        NetworkIdentity identity = GetComponent<NetworkIdentity>();
        if (identity != null && identity.isSpawned)
        {
            if (isServer)
            {
                identity.Despawn();
                return;
            }

            return;
        }

        Destroy(gameObject);
    }

    private void TryBeginNetworkPickup()
    {
        if (_pickupLocked.value || !string.IsNullOrEmpty(_pendingPickupToken))
            return;

        _pendingPickupToken = System.Guid.NewGuid().ToString("N");
        RequestPickupServerRpc(_pendingPickupToken);
    }

    [ServerRpc(requireOwnership: false)]
    private void RequestPickupServerRpc(string pickupToken, RPCInfo info = default)
    {
        NetworkPlayer player = NetworkPlayer.FindPlayer(info.sender);
        if (_pickupLocked.value || string.IsNullOrEmpty(pickupToken) || pickupToken.Length > 64
            || player == null || !player.CanUseStation(this))
        {
            ResolvePickupObserversRpc(pickupToken, false, default);
            return;
        }

        _pickupLocked.value = true;
        _serverPickupToken = pickupToken;
        _serverPickupPlayer = info.sender;
        _serverPickupHasPlayer = true;
        _serverPickupReceipt = NetworkPlayer.ReceiptFor(this);
        ResolvePickupObserversRpc(pickupToken, true, _serverPickupReceipt);
        StartCoroutine(ReleaseExpiredPickup(pickupToken));
    }

    private System.Collections.IEnumerator ReleaseExpiredPickup(string claim)
    {
        yield return new WaitForSecondsRealtime(10f);
        if (!_serverPickupHasPlayer || _serverPickupToken != claim) yield break;
        NetworkPlayer.FindPlayer(_serverPickupPlayer)?.CompleteItemUse(_serverPickupReceipt.token, "Pickup expired; try again.");
        _serverPickupToken = null;
        _serverPickupHasPlayer = false;
        _pickupLocked.value = false;
    }

    [ObserversRpc]
    private void ResolvePickupObserversRpc(string pickupToken, bool success, InventoryReceipt receipt)
    {
        if (_pendingPickupToken != pickupToken)
            return;

        _pendingPickupToken = null;

        if (!success)
        {
            ShowInventoryFullPrompt("Already taken!");
            return;
        }

        if (TryAddToInventory(receipt))
        {
            ConfirmPickupConsumedServerRpc(pickupToken);
            return;
        }

        CancelPickupClaimServerRpc(pickupToken);
    }

    [ServerRpc(requireOwnership: false)]
    private void ConfirmPickupConsumedServerRpc(string pickupToken, RPCInfo info = default)
    {
        if (_serverPickupToken != pickupToken
            || !_serverPickupHasPlayer
            || !_serverPickupPlayer.Equals(info.sender))
            return;

        var player = NetworkPlayer.FindPlayer(info.sender);
        if (player == null || !player.CanUseStation(this))
        {
            player?.CompleteItemUse(_serverPickupReceipt.token, "Pickup cancelled.");
            _serverPickupToken = null;
            _serverPickupHasPlayer = false;
            _pickupLocked.value = false;
            return;
        }
        if (!player.ServerInventory.Grant(_serverPickupReceipt)) return;
        _serverPickupToken = null;
        _serverPickupHasPlayer = false;

        DespawnAfterSuccessfulPickup();
    }

    [ServerRpc(requireOwnership: false)]
    private void CancelPickupClaimServerRpc(string pickupToken, RPCInfo info = default)
    {
        if (_serverPickupToken != pickupToken
            || !_serverPickupHasPlayer
            || !_serverPickupPlayer.Equals(info.sender))
            return;

        _serverPickupToken = null;
        _serverPickupHasPlayer = false;
        _pickupLocked.value = false;
    }

    private bool TryAddToInventory(InventoryReceipt receipt = default)
    {
        if (!TryGetInventoryManager(out InventoryManager cachedInventoryManager))
        {
            Debug.LogError($"Couldn't get inventory manager for item {itemName}", this);
            return false;
        }

        if (this is WeaponItem weaponItem)
        {
            Debug.Log($"[Item.Pickup] WeaponItem picked up: {itemName}, currentAmmo={weaponItem.CurrentAmmo}");
        }

        bool added = cachedInventoryManager.AddItem(this, receipt: receipt);
        if (added)
        {
            var localPlayer = NetworkPlayer.Local;
            if (localPlayer != null && localPlayer.TryGetComponent<PlayerFeedbackAudio>(out var feedback))
                feedback.PlayPickup();
            return true;
        }

        ShowInventoryFullPrompt("Inventory is full!");
        Debug.Log("[Item.Pickup] Inventory full, item not picked up.");
        return false;
    }

    private void ShowInventoryFullPrompt(string message)
    {
        if (promptPresenter == null)
            promptPresenter = PromptPresenter.Instance ?? FindObjectOfType<PromptPresenter>();

        if (promptPresenter != null)
            promptPresenter.Show(message);
    }

    private string BuildHoverPrompt(bool canPickup)
    {
        string header = canPickup
            ? "<color=#9FDBFF>[F]</color>  <color=#EAFBFF>PICK UP</color>"
            : "<color=#FF8A75>[F]</color>  <color=#FFD7D1>UNAVAILABLE</color>";

        string detailColor = canPickup ? "#D8F7FF" : "#FFB4A7";
        string priceLabel = price > 0 ? $"   <color=#7FA9B8>• {price}</color>" : string.Empty;
        string status = canPickup ? string.Empty : $"   <color=#FF8A75>• {GetUnavailableLabel()}</color>";
        return $"{header}\n<size=70%><color={detailColor}>{DisplayName}</color>{priceLabel}{status}</size>";
    }

    private string GetUnavailableLabel()
    {
        if (_pickupLocked.value)
            return "ALREADY TAKEN";

        return HasInventorySpace() ? "UNAVAILABLE" : "PACK FULL";
    }

    private bool HasInventorySpace()
    {
        if (!TryGetInventoryManager(out InventoryManager cachedInventoryManager))
            return true;

        return cachedInventoryManager.HasFreeSlot();
    }

    private bool TryGetInventoryManager(out InventoryManager cachedInventoryManager)
    {
        if (inventoryManager != null)
        {
            cachedInventoryManager = inventoryManager;
            return true;
        }

        if (!InstanceHandler.TryGetInstance(out inventoryManager))
            inventoryManager = FindObjectOfType<InventoryManager>();

        cachedInventoryManager = inventoryManager;
        return cachedInventoryManager != null;
    }

}
