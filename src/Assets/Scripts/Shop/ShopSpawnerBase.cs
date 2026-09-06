using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PurrNet;

/// <summary>
/// 씬 배치형 상점 스포너의 공통 네트워크 라이프사이클.
/// - 서버에서만 초기 스폰 / 일일 리셋 / 정리 수행
/// - TimeManager.OnDayReset 구독/해제 공통화
/// - 스폰 포인트 자동 탐색 / 스폰 이펙트 공통화
/// </summary>
public abstract class ShopSpawnerBase : NetworkBehaviour
{
    [Header("Shop Configuration")]
    [SerializeField] protected Transform[] spawnPoints = new Transform[4];
    [SerializeField] protected bool autoFindSpawnPoints = true;

    [Header("Visual Settings")]
    [SerializeField] protected GameObject spawnEffectPrefab;
    [SerializeField] protected float spawnInterval = 0.2f;
    [SerializeField] protected float initialSpawnDelay = 0.5f;
    [SerializeField] protected Vector3 spawnOffset = new Vector3(0, 0.1f, 0);

    [Header("Debug")]
    [SerializeField] protected bool debugMode = false;

    protected readonly List<GameObject> spawnedEntries = new();
    protected bool isInitialized;

    protected virtual string ShopLogName => GetType().Name;

    protected virtual void Awake()
    {
        ValidateConfiguration();
    }

    protected override void OnSpawned()
    {
        base.OnSpawned();

        if (!isServer)
            return;

        Debug.Log($"[{ShopLogName}] Server initialized - preparing daily shop");
        TimeManager.OnDayReset += HandleDayReset;
        StartCoroutine(InitialSpawn());
        isInitialized = true;
    }

    protected override void OnDespawned()
    {
        base.OnDespawned();

        if (!isServer)
            return;

        TimeManager.OnDayReset -= HandleDayReset;
        ClearSpawnedEntries();
    }

    protected abstract void ValidateConfiguration();
    protected abstract IEnumerator SpawnDailyEntriesCoroutine();

    protected virtual bool ValidateSpawnPoints()
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
            return true;

        if (!autoFindSpawnPoints)
        {
            Debug.LogError($"[{ShopLogName}] No spawn points assigned!");
            return false;
        }

        Debug.Log($"[{ShopLogName}] Auto-finding spawn points from children...");

        List<Transform> childPoints = new();
        foreach (Transform child in transform)
        {
            if (child.name.Contains("SpawnPoint") || child.name.Contains("Point"))
            {
                childPoints.Add(child);
            }
        }

        if (childPoints.Count == 0)
        {
            Debug.LogError($"[{ShopLogName}] No spawn points found!");
            return false;
        }

        spawnPoints = childPoints.ToArray();
        Debug.Log($"[{ShopLogName}] Found {spawnPoints.Length} spawn points");
        return true;
    }

    protected void TrackSpawnedEntry(GameObject entry)
    {
        if (entry != null)
            spawnedEntries.Add(entry);
    }

    protected GameObject InstantiateEntryPrefab(GameObject prefab, Transform spawnPoint)
    {
        if (prefab == null || spawnPoint == null)
            return null;

        float prefabOffsetY = prefab.transform.position.y;
        Vector3 spawnPos = spawnPoint.position + spawnOffset + Vector3.up * prefabOffsetY;
        Quaternion spawnRot = prefab.transform.rotation;

        GameObject instance = Instantiate(prefab, spawnPos, spawnRot);
        instance.transform.localScale = prefab.transform.localScale;
        instance.transform.SetParent(spawnPoint, true);
        return instance;
    }

    protected bool TryGetSpawnPointIndex(Transform spawnPoint, out int index)
    {
        index = -1;
        if (spawnPoint == null || spawnPoints == null)
            return false;

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i] == spawnPoint)
            {
                index = i;
                return true;
            }
        }

        return false;
    }

    protected bool IsValidSpawnPointIndex(int index)
    {
        return spawnPoints != null && index >= 0 && index < spawnPoints.Length && spawnPoints[index] != null;
    }

    protected Transform GetSpawnPoint(int index)
    {
        return IsValidSpawnPointIndex(index) ? spawnPoints[index] : null;
    }

    protected void TriggerSpawnEffect(Vector3 position)
    {
        if (spawnEffectPrefab != null)
            PlaySpawnEffectRpc(position);
    }

    protected void TriggerManualReset()
    {
        if (Application.isPlaying && isServer)
        {
            HandleDayReset();
            return;
        }

        if (!Application.isPlaying)
        {
            Debug.LogWarning($"[{ShopLogName}] Manual reset only works in Play mode on server!");
            return;
        }

        Debug.LogWarning($"[{ShopLogName}] Manual reset only works on server!");
    }

    protected void ClearTrackedEntriesForDebug()
    {
        if (Application.isPlaying && isServer)
            ClearSpawnedEntries();
    }

    private IEnumerator InitialSpawn()
    {
        yield return new WaitForSeconds(initialSpawnDelay);
        yield return SpawnDailyEntriesCoroutine();
    }

    private void HandleDayReset()
    {
        if (!isServer || !isInitialized)
            return;

        Debug.Log($"[{ShopLogName}] Day reset triggered - refreshing shop inventory");
        StartCoroutine(ResetShopCoroutine());
    }

    private IEnumerator ResetShopCoroutine()
    {
        ClearSpawnedEntries();
        yield return new WaitForSeconds(0.5f);
        yield return SpawnDailyEntriesCoroutine();
    }

    private void ClearSpawnedEntries()
    {
        if (isServer)
            ClearClientEntriesRpc();

        foreach (var entry in spawnedEntries)
        {
            if (entry == null)
                continue;

            try
            {
                if (isServer)
                    Destroy(entry);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[{ShopLogName}] Error destroying item: {e.Message}");
            }
        }

        spawnedEntries.Clear();

        if (debugMode)
            Debug.Log($"[{ShopLogName}] Cleared all existing items");
    }

    [ObserversRpc]
    private void ClearClientEntriesRpc()
    {
        if (isServer)
            return;

        for (int i = 0; i < spawnedEntries.Count; i++)
        {
            var entry = spawnedEntries[i];
            if (entry != null)
                Destroy(entry);
        }

        spawnedEntries.Clear();
    }

    [ObserversRpc]
    private void PlaySpawnEffectRpc(Vector3 position)
    {
        if (spawnEffectPrefab == null)
            return;

        GameObject effect = Instantiate(spawnEffectPrefab, position, Quaternion.identity);
        ParticleSystem ps = effect.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            Destroy(effect, ps.main.duration + ps.main.startLifetime.constantMax);
            return;
        }

        Destroy(effect, 2f);
    }
}
