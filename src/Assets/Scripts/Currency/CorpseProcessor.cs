using UnityEngine;
using PurrNet;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Demo.Scripts.Runtime.Character;

public class CorpseProcessor : AInteractable
{
    [Header("=== Drop Settings ===")]
    [SerializeField] private CorpseDropTable dropTable;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float spawnRadius = 1.5f;
    [SerializeField] private float spawnHeight = 0.5f;

    // 캐시된 컴포넌트들
    private InventoryManager _inventoryManager;
    private PromptPresenter _promptPresenter;

    #region Initialization

    private void Awake()
    {
        // InventoryManager 찾기 - 여러 방법 시도
        if (!InstanceHandler.TryGetInstance(out _inventoryManager))
        {
            _inventoryManager = FindObjectOfType<InventoryManager>();
        }

        if (_inventoryManager != null)
        {
            Debug.Log("[CorpseProcessor] InventoryManager found in Awake!");
        }
        else
        {
            Debug.LogWarning("[CorpseProcessor] InventoryManager not found in Awake, will try again later");
        }

        // PromptPresenter 찾기 (옵션)
        if (_promptPresenter == null)
            _promptPresenter = FindObjectOfType<PromptPresenter>();

        // SpawnPoint 기본값 설정
        if (spawnPoint == null)
        {
            spawnPoint = transform;
        }

        // 드롭 테이블 체크
        if (dropTable == null)
        {
            Debug.LogWarning("[CorpseProcessor] Drop table not assigned!");
        }
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
                Debug.Log("[CorpseProcessor] InventoryManager found in Start!");
            }
            else
            {
                Debug.LogWarning("[CorpseProcessor] InventoryManager still not found in Start");
            }
        }
    }

    #endregion

    #region Interaction

    public override void Interact()
    {
        // InventoryManager가 없으면 다시 찾기 시도
        if (_inventoryManager == null)
        {
            if (!InstanceHandler.TryGetInstance(out _inventoryManager))
            {
                _inventoryManager = FindObjectOfType<InventoryManager>();

                if (_inventoryManager == null)
                {
                    Debug.LogError("[CorpseProcessor] InventoryManager not found after multiple attempts!");
                    return;
                }
            }
            Debug.Log("[CorpseProcessor] InventoryManager found in Interact!");
        }

        // 현재 들고 있는 아이템 확인
        var activeItemData = _inventoryManager.GetActiveItemData();

        if (!activeItemData.HasValue)
        {
            Debug.Log("[CorpseProcessor] No item in hand!");
            return;
        }

        var itemData = activeItemData.Value;

        // Corpse 아이템인지 확인
        if (!IsCorpseItem(itemData))
        {
            Debug.Log($"[CorpseProcessor] '{itemData.itemName}' is not a corpse item!");
            return;
        }

        // 서버가 실제 월드 픽업으로 발급한 시체 소유권을 검증하고 소비한다.
        ProcessCorpseServerRpc(itemData.receiptToken);
    }

    public override bool CanInteract()
    {
        return true;  // 항상 hover 효과 표시
    }

    #endregion

    #region Network Processing

    [ServerRpc(requireOwnership: false)]
    private void ProcessCorpseServerRpc(string token, RPCInfo info = default)
    {
        var player = NetworkPlayer.FindPlayer(info.sender);
        if (player == null || !player.CanUseStation(this) || !player.ServerInventory.TryGet(token, out var trophy)) return;
        if (trophy.category != (int)ItemCategory.Corpse && !IsExtraTrophyName(trophy.itemName)) return;
        string requestedCorpseName = trophy.itemName;
        int trophyRarity = trophy.rarity;

        Debug.Log($"[CorpseProcessor] Processing server-authorized corpse: {requestedCorpseName}");

        // 드롭 아이템 결정 (테이블 기반)
        var itemsToSpawn = GetDropsForCorpse(requestedCorpseName);
        if (spawnPoint == null || NetworkManager.main == null || NetworkManager.main.prefabProvider == null) return;
        foreach (var drop in itemsToSpawn)
            if (drop.itemPrefab == null || drop.itemPrefab.GetComponent<Item>() == null
                || !NetworkManager.main.prefabProvider.TryGetPrefabData(drop.itemPrefab, out _)) return;
        Debug.Log($"[CorpseProcessor] Will spawn {itemsToSpawn.Count} items");

        var spawned = new List<Item>();
        foreach (var drop in itemsToSpawn)
        {
            var receipt = NetworkPlayer.ReceiptFor(drop.itemPrefab.GetComponent<Item>());
            receipt.price = Random.Range(drop.minPrice, drop.maxPrice + 1);
            Vector2 offset = Random.insideUnitCircle * spawnRadius;
            var position = spawnPoint.position + new Vector3(offset.x, spawnHeight, offset.y);
            if (NetworkPlayer.TrySpawnReceipt(drop.itemPrefab, receipt, position, out var item))
                spawned.Add(item);
            else
            {
                foreach (var created in spawned) created.Despawn();
                return; // No trophy or reward is consumed when any output fails.
            }
        }
        if (!player.ServerInventory.Consume(token, out _))
        {
            foreach (var created in spawned) created.Despawn();
            return;
        }

        // 전리품 → 스킬 포인트 (처리한 플레이어에게)
        int points = ResolveTrophyPoints(requestedCorpseName);
        // Intact trophies (head-shot kills, graded Rare or better by the server) are worth double.
        bool intact = trophyRarity >= (int)ItemRarity.Rare;
        if (intact)
            points *= 2;
        if (points > 0)
        {
            PlayerVitals vitals = FindVitalsForPlayer(info.sender);
            if (vitals != null)
                vitals.GrantSkillPoints(points, intact ? requestedCorpseName + " (intact x2)" : requestedCorpseName);
        }

        ContractGoal? goal = ContractEvents.GoalForTrophy(requestedCorpseName);
        if (goal.HasValue)
            ContractEvents.Report(goal.Value, 1);
        player.CompleteItemUse(token, $"Processed {requestedCorpseName}");
    }

    [System.Serializable]
    public struct TrophyReward
    {
        [Tooltip("아이템 이름에 포함되는 키워드 (대소문자 무시)")]
        public string keyword;
        [Min(0)] public int skillPoints;
    }

    [Header("=== Trophies → Skill Points ===")]
    [Tooltip("시체/전리품 이름 키워드별 스킬 포인트. 비우면 기본표: smily 1, clown 2, octopus 1, 그 외 1")]
    [SerializeField] private TrophyReward[] trophyRewards;
    [Tooltip("시체 카테고리가 아니어도 전리품으로 받아주는 아이템 이름")]
    [SerializeField] private string[] extraTrophyNames = { "Octopus" };

    private static readonly TrophyReward[] DefaultTrophyRewards =
    {
        new TrophyReward { keyword = "smily", skillPoints = 1 },
        new TrophyReward { keyword = "clown", skillPoints = 2 },
        new TrophyReward { keyword = "octopus", skillPoints = 1 },
    };

    private int ResolveTrophyPoints(string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
            return 0;

        string lower = itemName.ToLowerInvariant();
        TrophyReward[] table = trophyRewards != null && trophyRewards.Length > 0 ? trophyRewards : DefaultTrophyRewards;
        for (int i = 0; i < table.Length; i++)
        {
            if (!string.IsNullOrEmpty(table[i].keyword) && lower.Contains(table[i].keyword.ToLowerInvariant()))
                return table[i].skillPoints;
        }

        return 1;
    }

    private bool IsExtraTrophyName(string itemName)
    {
        if (extraTrophyNames == null || string.IsNullOrEmpty(itemName))
            return false;

        for (int i = 0; i < extraTrophyNames.Length; i++)
        {
            if (string.Equals(extraTrophyNames[i], itemName, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static PlayerVitals FindVitalsForPlayer(PlayerID player)
    {
        PlayerVitals[] all = FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            PlayerVitals vitals = all[i];
            if (vitals != null && vitals.isSpawned && vitals.owner.HasValue && vitals.owner.Value == player)
                return vitals;
        }

        return null;
    }



    #endregion

    #region Corpse Processing Logic

    private bool IsCorpseItem(InventoryManager.InventoryItemData itemData)
    {
        if (itemData.definition != null && itemData.definition.category == ItemCategory.Corpse)
            return true;

        return IsExtraTrophyName(itemData.itemName);
    }

    private List<DropData> GetDropsForCorpse(string corpseName)
    {
        List<DropData> drops = new List<DropData>();

        if (dropTable == null)
        {
            Debug.LogError("[CorpseProcessor] Drop table not assigned!");
            return drops;
        }

        string lowerName = corpseName.ToLower();

        // 테이블에서 매칭되는 시체 타입 찾기
        CorpseDropTable.CorpseTypeDrops matchedDrops = null;

        // 순서대로 체크하여 첫 번째 매칭되는 것 사용
        foreach (var corpseDrops in dropTable.corpseDrops)
        {
            if (string.IsNullOrEmpty(corpseDrops.corpseKeyword))
                continue;

            if (lowerName.Contains(corpseDrops.corpseKeyword.ToLower()))
            {
                matchedDrops = corpseDrops;
                Debug.Log($"[CorpseProcessor] Matched corpse type: {corpseDrops.corpseKeyword}");
                break;
            }
        }

        // 못 찾으면 generic 타입 사용
        if (matchedDrops == null)
        {
            matchedDrops = dropTable.corpseDrops.FirstOrDefault(
                x => x.corpseKeyword.ToLower() == "generic");

            if (matchedDrops != null)
            {
                Debug.Log("[CorpseProcessor] Using generic drop table");
            }
        }

        // 드롭 처리
        if (matchedDrops != null)
        {
            foreach (var dropInfo in matchedDrops.possibleDrops)
            {
                if (dropInfo.itemPrefab == null)
                {
                    Debug.LogWarning("[CorpseProcessor] Drop item prefab is null!");
                    continue;
                }

                // 드롭 확률 체크
                if (Random.Range(0f, 100f) <= dropInfo.dropChance)
                {
                    int quantity = Random.Range(dropInfo.minQuantity, dropInfo.maxQuantity + 1);

                    for (int i = 0; i < quantity; i++)
                    {
                        drops.Add(new DropData
                        {
                            itemPrefab = dropInfo.itemPrefab,
                            minPrice = dropInfo.minPrice,
                            maxPrice = dropInfo.maxPrice
                        });
                    }
                }
            }
        }

        return drops;
    }

    // 간단한 데이터 구조
    private class DropData
    {
        public GameObject itemPrefab;
        public int minPrice;
        public int maxPrice;
    }

    #endregion

    #region Price Setting



    #endregion

    #region Hover Prompts

    public override void OnHover()
    {
        base.OnHover();

        // 프롬프트 표시 (옵션)
        if (_promptPresenter != null)
        {
            var activeItem = _inventoryManager?.GetActiveItemData();
            if (activeItem.HasValue && IsCorpseItem(activeItem.Value))
            {
                _promptPresenter.Show("[F] Process corpse");
            }
            else
            {
                _promptPresenter.Show("Corpse items only");
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

    #endregion
}
