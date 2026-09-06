using UnityEngine;
using PurrNet;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

/// <summary>
/// 인벤토리 확장 상점 - 매일 4개의 가방을 테이블 위에 스폰
/// TimeManager와 연동하여 매일 9시에 리셋
/// </summary>
public class InventoryExpansionShop : ShopSpawnerBase
{
    private BagType?[] _currentLayout;
    private const int ClientLayoutRequestAttempts = 5;
    private const float ClientLayoutRequestInterval = 1.0f;

    [Header("Bag Prefabs")]
    [SerializeField] private GameObject smallBagPrefab;  // +1 슬롯 가방 프리팹
    [SerializeField] private GameObject mediumBagPrefab; // +2 슬롯 가방 프리팹
    [SerializeField] private GameObject largeBagPrefab;  // +3 슬롯 가방 프리팹

    [Header("Spawn Rules")]
    [SerializeField] private int guaranteedSmallBags = 1;  // 최소 Small 1개 보장
    [SerializeField] private int guaranteedMediumBags = 1; // 최소 Medium 1개 보장
    [SerializeField] private float largeBagChance = 0.2f;  // Large 가방 확률 (20%)
    [SerializeField] private float mediumBagChance = 0.4f; // Medium 가방 추가 확률 (40%)

    protected override string ShopLogName => "InventoryExpansionShop";

    protected override void OnSpawned()
    {
        base.OnSpawned();

        if (!isServer)
            StartCoroutine(RequestCurrentLayoutAfterDelay());
    }

    /// <summary>
    /// 프리팹 검증
    /// </summary>
    protected override void ValidateConfiguration()
    {
        ValidateSpawnPoints();
        _currentLayout = new BagType?[spawnPoints != null ? spawnPoints.Length : 0];

        if (smallBagPrefab == null)
            Debug.LogError("[InventoryExpansionShop] Small bag prefab not assigned!");
        if (mediumBagPrefab == null)
            Debug.LogError("[InventoryExpansionShop] Medium bag prefab not assigned!");
        if (largeBagPrefab == null)
            Debug.LogError("[InventoryExpansionShop] Large bag prefab not assigned!");

        // NetworkBehaviour 컴포넌트 확인 (InventoryBagItem이 NetworkBehaviour를 상속받음)
        if (smallBagPrefab != null && smallBagPrefab.GetComponent<InventoryBagItem>() == null)
            Debug.LogError("[InventoryExpansionShop] Small bag prefab missing InventoryBagItem component!");
        if (mediumBagPrefab != null && mediumBagPrefab.GetComponent<InventoryBagItem>() == null)
            Debug.LogError("[InventoryExpansionShop] Medium bag prefab missing InventoryBagItem component!");
        if (largeBagPrefab != null && largeBagPrefab.GetComponent<InventoryBagItem>() == null)
            Debug.LogError("[InventoryExpansionShop] Large bag prefab missing InventoryBagItem component!");
    }

    /// <summary>
    /// 일일 가방 스폰 (코루틴)
    /// </summary>
    protected override IEnumerator SpawnDailyEntriesCoroutine()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("[InventoryExpansionShop] No spawn points available!");
            yield break;
        }

        // 스폰할 가방 종류 결정
        List<BagType> bagsToSpawn = DetermineBagsToSpawn();

        // 위치 섞기 (랜덤 배치)
        bagsToSpawn = bagsToSpawn.OrderBy(x => Random.value).ToList();

        // 순차적으로 스폰 (시각적 효과)
        int spawnCount = Mathf.Min(spawnPoints.Length, bagsToSpawn.Count);
        for (int i = 0; i < spawnCount; i++)
        {
            if (spawnPoints[i] != null)
            {
                SpawnBagAtPosition(bagsToSpawn[i], spawnPoints[i]);

                // 스폰 사이 간격 (순차적 등장 효과)
                if (spawnInterval > 0 && i < spawnCount - 1)
                    yield return new WaitForSeconds(spawnInterval);
            }
        }

        Debug.Log($"[InventoryExpansionShop] Daily spawn complete: {spawnedEntries.Count} bags spawned");
    }

    /// <summary>
    /// 스폰할 가방 종류 결정 (랜덤 + 보장)
    /// </summary>
    private List<BagType> DetermineBagsToSpawn()
    {
        List<BagType> bags = new List<BagType>();

        // 보장된 가방 추가
        for (int i = 0; i < guaranteedSmallBags && bags.Count < 4; i++)
            bags.Add(BagType.Small);

        for (int i = 0; i < guaranteedMediumBags && bags.Count < 4; i++)
            bags.Add(BagType.Medium);

        // 나머지 슬롯 랜덤 채우기 (최대 4개까지)
        while (bags.Count < 4)
        {
            float rand = Random.Range(0f, 1f);

            if (rand < largeBagChance)
            {
                bags.Add(BagType.Large);
            }
            else if (rand < largeBagChance + mediumBagChance)
            {
                bags.Add(BagType.Medium);
            }
            else
            {
                bags.Add(BagType.Small);
            }
        }

        // 4개로 제한
        if (bags.Count > 4)
            bags = bags.Take(4).ToList();

        if (debugMode)
        {
            Debug.Log($"[InventoryExpansionShop] Bags to spawn: {string.Join(", ", bags)}");
        }

        return bags;
    }

    /// <summary>
    /// 특정 위치에 가방 스폰
    /// </summary>
    private void SpawnBagAtPosition(BagType type, Transform spawnPoint)
    {
        GameObject prefabToSpawn = GetBagPrefab(type);

        if (prefabToSpawn == null)
        {
            Debug.LogError($"[InventoryExpansionShop] Prefab for {type} not found!");
            return;
        }

        if (!TryGetSpawnPointIndex(spawnPoint, out int spawnIndex))
        {
            Debug.LogError("[InventoryExpansionShop] Failed to resolve spawn point index for bag spawn");
            return;
        }

        GameObject instance = InstantiateEntryPrefab(prefabToSpawn, spawnPoint);

        InventoryBagItem bagItem = instance.GetComponent<InventoryBagItem>();

        if (bagItem != null)
        {
            TrackSpawnedEntry(instance);
            if (_currentLayout != null && spawnIndex >= 0 && spawnIndex < _currentLayout.Length)
                _currentLayout[spawnIndex] = type;

            TriggerSpawnEffect(instance.transform.position);
            SpawnBagOnClientsRpc((int)type, spawnIndex);

            if (debugMode)
                Debug.Log($"[InventoryExpansionShop] Spawned {type} bag at {spawnPoint.name} (as child)");
        }
        else
        {
            Debug.LogError($"[InventoryExpansionShop] InventoryBagItem component missing on {type} bag!");
            Destroy(instance);
        }
    }


    /// <summary>
    /// 가방 타입에 따른 프리팹 반환
    /// </summary>
    private GameObject GetBagPrefab(BagType type)
    {
        switch (type)
        {
            case BagType.Small:
                return smallBagPrefab;
            case BagType.Medium:
                return mediumBagPrefab;
            case BagType.Large:
                return largeBagPrefab;
            default:
                return smallBagPrefab;
        }
    }
    /// <summary>
    /// 가방 종류 열거형
    /// </summary>
    private enum BagType
    {
        Small,
        Medium,
        Large
    }

    private IEnumerator RequestCurrentLayoutAfterDelay()
    {
        for (int attempt = 1; attempt <= ClientLayoutRequestAttempts; attempt++)
        {
            yield return new WaitForSeconds(ClientLayoutRequestInterval);

            var networkManager = NetworkManager.main;
            if (networkManager == null || !networkManager.isClient)
                continue;

            if (GetComponentsInChildren<InventoryBagItem>(true).Length > 0)
                yield break;

            RequestCurrentLayoutRpc();
        }
    }

    [ServerRpc(requireOwnership: false)]
    private void RequestCurrentLayoutRpc()
    {
        if (_currentLayout == null)
            return;

        for (int i = 0; i < _currentLayout.Length; i++)
        {
            if (!_currentLayout[i].HasValue)
                continue;

            SpawnBagOnClientsRpc((int)_currentLayout[i].Value, i);
        }
    }

    [ObserversRpc]
    private void SpawnBagOnClientsRpc(int bagTypeIndex, int spawnIndex)
    {
        if (isServer)
            return;

        Transform spawnPoint = GetSpawnPoint(spawnIndex);
        if (spawnPoint == null)
            return;

        if (spawnPoint.GetComponentInChildren<InventoryBagItem>() != null)
            return;

        GameObject prefab = GetBagPrefab((BagType)bagTypeIndex);
        if (prefab == null)
            return;

        GameObject instance = InstantiateEntryPrefab(prefab, spawnPoint);
        if (instance == null)
            return;

        InventoryBagItem bagItem = instance.GetComponent<InventoryBagItem>();
        if (bagItem == null)
        {
            Destroy(instance);
            return;
        }

        TrackSpawnedEntry(instance);
    }

    public bool TryRequestPurchase(InventoryBagItem item, string purchaseToken)
    {
        if (item == null || item.transform.parent == null)
            return false;

        if (!TryGetSpawnPointIndex(item.transform.parent, out int spawnIndex))
            return false;

        if (isServer)
        {
            ProcessPurchaseOnServer(spawnIndex, purchaseToken);
        }
        else
        {
            RequestPurchaseAtIndexRpc(spawnIndex, purchaseToken);
        }

        return true;
    }

    public bool TryRequestPurchaseFeedback(InventoryBagItem item, bool success)
    {
        if (item == null || item.transform.parent == null)
            return false;

        if (!TryGetSpawnPointIndex(item.transform.parent, out int spawnIndex))
            return false;

        if (isServer)
            PlayPurchaseFeedbackAtIndexRpc(spawnIndex, success);
        else
            RequestPurchaseFeedbackAtIndexRpc(spawnIndex, success);

        return true;
    }

    private InventoryBagItem GetBagItemAtIndex(int spawnIndex)
    {
        Transform spawnPoint = GetSpawnPoint(spawnIndex);
        if (spawnPoint == null || spawnPoint.childCount == 0)
            return null;

        return spawnPoint.GetComponentInChildren<InventoryBagItem>();
    }

    private void ProcessPurchaseOnServer(int spawnIndex, string purchaseToken)
    {
        InventoryBagItem item = GetBagItemAtIndex(spawnIndex);
        if (item == null || item.SoldOnServer)
        {
            // Already sold (audit M1): reject the duplicate request instead of charging twice.
            ResolvePurchaseAtIndexRpc(spawnIndex, purchaseToken, false, 0);
            return;
        }

        bool success = item.TryExecutePurchaseOnServer(out int currentCurrency);
        if (success)
            item.SoldOnServer = true;
        PlayPurchaseFeedbackAtIndexRpc(spawnIndex, success);
        ResolvePurchaseAtIndexRpc(spawnIndex, purchaseToken, success, currentCurrency);

        if (success)
            RemovePurchasedEntryAtIndexRpc(spawnIndex);
    }

    [ServerRpc(requireOwnership: false)]
    private void RequestPurchaseAtIndexRpc(int spawnIndex, string purchaseToken)
    {
        ProcessPurchaseOnServer(spawnIndex, purchaseToken);
    }

    [ServerRpc(requireOwnership: false)]
    private void RequestPurchaseFeedbackAtIndexRpc(int spawnIndex, bool success)
    {
        PlayPurchaseFeedbackAtIndexRpc(spawnIndex, success);
    }

    [ObserversRpc]
    private void ResolvePurchaseAtIndexRpc(int spawnIndex, string purchaseToken, bool success, int currentCurrency)
    {
        InventoryBagItem item = GetBagItemAtIndex(spawnIndex);
        if (item == null)
            return;

        item.ResolvePurchaseLocally(purchaseToken, success, currentCurrency);
    }

    [ObserversRpc]
    private void PlayPurchaseFeedbackAtIndexRpc(int spawnIndex, bool success)
    {
        InventoryBagItem item = GetBagItemAtIndex(spawnIndex);
        if (item == null)
            return;

        item.PlayPurchaseAudioFromNetwork(success);
    }

    [ObserversRpc]
    private void RemovePurchasedEntryAtIndexRpc(int spawnIndex)
    {
        InventoryBagItem item = GetBagItemAtIndex(spawnIndex);
        if (item == null)
            return;

        item.RemovePurchasedEntryLocally();
    }

    #region Debug Methods

    /// <summary>
    /// 디버그용: 수동 리셋
    /// </summary>
    [ContextMenu("Manual Reset Shop")]
    private void ManualResetShop()
    {
        TriggerManualReset();
    }

    /// <summary>
    /// 디버그용: 모든 가방 제거
    /// </summary>
    [ContextMenu("Clear All Bags")]
    private void DebugClearAllBags()
    {
        ClearTrackedEntriesForDebug();
    }

    /// <summary>
    /// 디버그용: 가방 하나씩 스폰
    /// </summary>
    [ContextMenu("Spawn Single Small Bag")]
    private void DebugSpawnSmallBag()
    {
        if (Application.isPlaying && isServer && spawnPoints.Length > 0)
        {
            SpawnBagAtPosition(BagType.Small, spawnPoints[0]);
        }
    }

    [ContextMenu("Spawn Single Medium Bag")]
    private void DebugSpawnMediumBag()
    {
        if (Application.isPlaying && isServer && spawnPoints.Length > 0)
        {
            SpawnBagAtPosition(BagType.Medium, spawnPoints[0]);
        }
    }

    [ContextMenu("Spawn Single Large Bag")]
    private void DebugSpawnLargeBag()
    {
        if (Application.isPlaying && isServer && spawnPoints.Length > 0)
        {
            SpawnBagAtPosition(BagType.Large, spawnPoints[0]);
        }
    }

    #endregion
}
