using UnityEngine;
using PurrNet;
using Demo.Scripts.Runtime.Character;

/// <summary>
/// 선물박스 - 플레이어가 들고 있는 아이템을 포장된 아이템으로 변환
/// </summary>
public class GiftBox : AInteractable
{
    [Header("Gift Box Settings")]
    [SerializeField] private GiftBoxItem giftBoxItemPrefab; // GiftBoxItem GameObject 프리팹

    [Header("Packaging Audio")]
    [SerializeField] private AudioSource packagingAudioSource;
    [SerializeField] private AudioClip packagingSound;
    [SerializeField, Range(0f, 1f)] private float packagingSoundVolume = 0.4f;

    // 캐시된 컴포넌트들
    private InventoryManager _inventoryManager;
    private PromptPresenter _promptPresenter;

private void Awake()
    {
        // InventoryManager 싱글톤 가져오기 - 여러 방법 시도
        if (!InstanceHandler.TryGetInstance(out _inventoryManager))
        {
            // 싱글톤이 아직 준비 안됐으면 직접 찾기
            _inventoryManager = FindObjectOfType<InventoryManager>();
        }
        
        if (_inventoryManager != null)
        {
            Debug.Log("[GiftBox] InventoryManager found in Awake!");
        }
        else
        {
            Debug.LogWarning("[GiftBox] InventoryManager not found in Awake, will try again in Interact");
        }

        // PromptPresenter 찾기 (옵션)
        if (_promptPresenter == null)
            _promptPresenter = FindObjectOfType<PromptPresenter>();
    }

private void Start()
    {
        // Start에서 한 번 더 InventoryManager 찾기 시도
        if (_inventoryManager == null)
        {
            if (!InstanceHandler.TryGetInstance(out _inventoryManager))
            {
                _inventoryManager = FindObjectOfType<InventoryManager>();
            }
            
            if (_inventoryManager != null)
            {
                Debug.Log("[GiftBox] InventoryManager found in Start!");
            }
            else
            {
                Debug.LogWarning("[GiftBox] InventoryManager still not found in Start");
            }
        }
    }



public override void Interact()
    {
        // InventoryManager가 없으면 다시 찾기 시도
        if (_inventoryManager == null)
        {
            if (!InstanceHandler.TryGetInstance(out _inventoryManager))
            {
                // 그래도 없으면 FindObjectOfType으로 찾기
                _inventoryManager = FindObjectOfType<InventoryManager>();
                
                if (_inventoryManager == null)
                {
                    Debug.LogError("[GiftBox] InventoryManager not found after multiple attempts!");
                    return;
                }
            }
            Debug.Log("[GiftBox] InventoryManager found in Interact!");
        }

        // 현재 손에 든 아이템 가져오기
        var activeItemData = _inventoryManager.GetActiveItemData();

        if (!activeItemData.HasValue)
        {
            Debug.Log("[GiftBox] No item in hand to wrap!");
            return;
        }

        var itemData = activeItemData.Value;

        // 이미 GiftBoxItem이면 무시
        if (itemData.itemName.Contains("GiftBox") || itemData.itemName.Contains("선물"))
        {
            Debug.Log("[GiftBox] Already a gift box item!");
            return;
        }

        // 아이템 변환
        WrapItem(itemData);
    }

    private void WrapItem(InventoryManager.InventoryItemData itemData)
    {
        WrapReceiptServerRpc(itemData.receiptToken);
    }

    [ServerRpc(requireOwnership: false)]
    private void WrapReceiptServerRpc(string token, RPCInfo info = default)
    {
        var player = NetworkPlayer.FindPlayer(info.sender);
        if (player == null || !player.CanUseStation(this) || giftBoxItemPrefab == null
            || !player.ServerInventory.TryGet(token, out var entry)) return;
        if (entry.itemName.Contains("GiftBox") || entry.itemName.Contains("선물")) return;
        var wrapped = NetworkPlayer.ReceiptFor(giftBoxItemPrefab);
        wrapped.price = entry.price;
        if (!NetworkPlayer.TrySpawnReceipt(giftBoxItemPrefab.gameObject, wrapped,
            player.transform.position + player.transform.forward + Vector3.up)) return;
        player.ServerInventory.Consume(token, out _);
        player.CompleteItemUse(token, "Item packaged");
        PlayPackagingSoundObserversRpc();
    }

    [ObserversRpc]
    private void PlayPackagingSoundObserversRpc()
    {
        if (packagingAudioSource == null || packagingSound == null)
            return;

        packagingAudioSource.PlayOneShot(packagingSound, packagingSoundVolume);
    }

    public override void OnHover()
    {
        base.OnHover();

        // 프롬프트 표시 (옵션)
        if (_promptPresenter != null)
            _promptPresenter.Show("[F] Package item");
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
        // Hover 효과는 항상 표시
        return true;
    }
}
