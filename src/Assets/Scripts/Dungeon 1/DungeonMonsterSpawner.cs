using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using PurrNet;
using DunGen;

public sealed class DungeonMonsterSpawner : NetworkBehaviour
{
    [Serializable]
    public struct MonsterEntry
    {
        public NetworkIdentity prefab;   // 몬스터 프리팹(네트워크)
        [Min(0f)] public float weight;   // 확률 가중치
    }

    [Header("Refs")]
    [SerializeField] private TimeManager timeManager;
    [Tooltip("생성된 RuntimeDungeon 루트. 던전 생성 후 ServerBindDungeonRoot로 넣어도 됨.")]
    [SerializeField] private Transform dungeonRoot;

    [Header("Monsters")]
    [SerializeField] private List<MonsterEntry> monsters = new();

    [Header("Night Scaling (18~24시 기준)")]
    [SerializeField] private float daySpawnInterval = 45f;
    [SerializeField] private float nightSpawnInterval = 12f;
    [SerializeField] private int dayMaxAlive = 2;
    [SerializeField] private int nightMaxAlive = 8;

    [Header("Curfew Surge")]
    [SerializeField, Tooltip("Night cap grows by this much per elapsed day (day 1 = +0).")]
    private int nightCapPerDay = 1;
    [SerializeField, Tooltip("Upper bound for the per-day night cap bonus.")]
    private int nightCapDayBonusMax = 4;
    [SerializeField, Range(18f, 24f), Tooltip("From this hour the dungeon goes into curfew: bigger cap, faster spawns.")]
    private float curfewStartHour = 23f;
    [SerializeField, Min(1f)] private float curfewCapMultiplier = 1.5f;
    [SerializeField, Min(1f)] private float curfewSpawnInterval = 5f;
    [SerializeField, Min(0f), Tooltip("During curfew most spawns are kept within this straight-line distance of the dungeon start point (the way out).")]
    private float curfewExitRadius = 25f;
    [SerializeField, Min(0f), Tooltip("Minimum distance to players during curfew (spawns closer than usual).")]
    private float curfewMinPlayerDistance = 6f;

    [Header("Spawn Validation")]
    [SerializeField] private float minPlayerDistance = 12f;
    [SerializeField] private float navmeshSampleRadius = 1.5f;
    [SerializeField] private float spawnCollisionRadius = 0.6f;
    [SerializeField] private LayerMask collisionMask = ~0;
    [SerializeField] private int spawnTriesPerTick = 6;
    [SerializeField] private bool requireSpawnReachableFromPlayer = true;

    [Header("No-Marker Fallback")]
    [SerializeField] private bool allowSpawnWithoutMarkers = true;
    [SerializeField] private int fallbackTileTriesPerAttempt = 3;
    [SerializeField] private int fallbackPointTriesPerTile = 5;

    [Header("Rendering Layer")]
    [SerializeField] private bool applyDungeonRenderingLayerToSpawned = true;
    [SerializeField] private string dungeonRenderingLayerName = "Dungeon";

    private uint _dungeonRenderingLayerMask;

    private readonly List<NetworkIdentity> _alive = new();
    private readonly List<Tile> _tiles = new();
    private Coroutine _loop;

    // Keep corpses in _alive for ServerClearAll, but do not let them reserve a spawn slot.
    public int AliveCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _alive.Count; i++)
            {
                NetworkIdentity identity = _alive[i];
                if (identity == null)
                    continue;

                if (identity.TryGetComponent<MonsterHealth>(out var health) && health.IsDead)
                    continue;
                if (identity.TryGetComponent<OctopusSwarmController>(out var swarm)
                    && swarm.CurrentIntent == MonsterIntent.Dead)
                    continue;

                count++;
            }
            return count;
        }
    }
    public int ConfiguredMonsterCount => monsters != null ? monsters.Count : 0;

    public void CollectConfiguredAgentTypeIds(List<int> results)
    {
        if (results == null || monsters == null)
            return;

        for (int i = 0; i < monsters.Count; i++)
        {
            NetworkIdentity prefab = monsters[i].prefab;
            NavMeshAgent agent = prefab != null ? prefab.GetComponentInChildren<NavMeshAgent>(true) : null;
            if (agent == null)
                continue;

            AddUniqueAgentType(results, agent.agentTypeID);
        }
    }

    private static void AddUniqueAgentType(List<int> results, int agentTypeId)
    {
        for (int i = 0; i < results.Count; i++)
        {
            if (results[i] == agentTypeId)
                return;
        }

        results.Add(agentTypeId);
    }

    protected override void OnSpawned()
    {
        base.OnSpawned();
        if (!isServer) return;

        if (timeManager == null)
            timeManager = FindFirstObjectByType<TimeManager>();

        _dungeonRenderingLayerMask = ResolveRenderingLayerMask(dungeonRenderingLayerName);

        TimeManager.OnDayReset += OnDayReset;
    }

    protected override void OnDespawned()
    {
        base.OnDespawned();
        if (!isServer) return;

        TimeManager.OnDayReset -= OnDayReset;
    }

    private void OnDayReset()
    {
        if (!isServer) return;
        ServerClearAll();
    }

    /// <summary>
    /// ✅ 던전 생성 완료(서버) 시점에 호출해줘.
    /// </summary>
    public void ServerBindDungeonRoot(Transform root)
    {
        if (!isServer) return;

        dungeonRoot = root;
        DungeonPointRegistry.RebuildFromRoot(dungeonRoot);
        RebuildTileCache();

        if (_loop == null)
            StartLoop();
    }

    private void StartLoop()
    {
        if (_loop != null) StopCoroutine(_loop);
        _loop = StartCoroutine(SpawnLoop());
    }

    private IEnumerator SpawnLoop()
    {
        while (DungeonPointRegistry.SpawnPoints.Count == 0 && !HasFallbackSpawnSource())
        {
            if (dungeonRoot != null)
            {
                // 던전이 생성되어 마커가 생겼다면 여기서 잡힘
                DungeonPointRegistry.RebuildFromRoot(dungeonRoot);
                RebuildTileCache();
            }

            Debug.Log($"[Spawner] waiting spawn source... root={(dungeonRoot ? dungeonRoot.name : "NULL")} spawnPoints={DungeonPointRegistry.SpawnPoints.Count} tiles={_tiles.Count}");
            yield return new WaitForSeconds(0.5f);
        }

        while (true)
        {
            if (timeManager == null || !timeManager.IsClockRunning())
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            CleanupAlive();

            int cap = EvaluateMaxAlive();
            int alive = AliveCount;
            if (alive < cap)
            {
                // 한 번에 너무 많이는 말고, 부족분 중 일부만
                int want = Mathf.Min(cap - alive, 2);
                for (int i = 0; i < want; i++)
                {
                    if (!TrySpawnOne())
                        break;
                }
            }

            yield return new WaitForSeconds(EvaluateSpawnInterval());
        }
    }

    private float EvaluateNight01()
    {
        // 18~24를 밤으로 보고 0~1로 변환
        float t = timeManager != null ? timeManager.GetCurrentTime() : 9f;
        return Mathf.InverseLerp(18f, 24f, t);
    }

    private bool IsCurfew()
    {
        float t = timeManager != null ? timeManager.GetCurrentTime() : 9f;
        return t >= curfewStartHour;
    }

    private float EvaluateSpawnInterval()
    {
        float n = EvaluateNight01();
        float interval = Mathf.Lerp(daySpawnInterval, nightSpawnInterval, n);
        return IsCurfew() ? Mathf.Min(interval, curfewSpawnInterval) : interval;
    }

    private int EvaluateMaxAlive()
    {
        int players = GameObject.FindGameObjectsWithTag("Player").Length;
        return ComputeMaxAlive(dayMaxAlive, nightMaxAlive, EvaluateNight01(), TimeManager.CurrentDay, players,
            IsCurfew(), nightCapPerDay, nightCapDayBonusMax, curfewCapMultiplier);
    }

    /// <summary>
    /// Curfew surge: the night cap grows with the day count and jumps again after the curfew hour,
    /// so staying late in the dungeon gets progressively more dangerous. Pure for testing.
    /// </summary>
    /// <summary>Base cap ceiling (before the per-player bonus) so late days stay playable.</summary>
    public const int MaxAliveHardCap = 14;

    public static int ComputeMaxAlive(int dayCap, int nightCap, float night01, int day, int players,
        bool curfew, int capPerDay, int dayBonusMax, float curfewMultiplier)
    {
        int dayBonus = Mathf.Clamp((Mathf.Max(1, day) - 1) * Mathf.Max(0, capPerDay), 0, Mathf.Max(0, dayBonusMax));
        float scaledNightCap = nightCap + dayBonus;
        float cap = Mathf.Lerp(dayCap, scaledNightCap, Mathf.Clamp01(night01));
        if (curfew)
            cap *= Mathf.Max(1f, curfewMultiplier);
        cap = Mathf.Min(cap, MaxAliveHardCap);
        return Mathf.RoundToInt(cap) + Mathf.Max(0, players - 1);
    }

    private void CleanupAlive()
    {
        for (int i = _alive.Count - 1; i >= 0; i--)
        {
            if (_alive[i] == null || _alive[i].gameObject == null)
                _alive.RemoveAt(i);
        }
    }

    private bool TrySpawnOne()
    {
        var points = DungeonPointRegistry.SpawnPoints;

        int failClose = 0, failNav = 0, failReach = 0, failColl = 0, failPrefab = 0, failNull = 0, failSource = 0;

        for (int attempt = 0; attempt < spawnTriesPerTick; attempt++)
        {
            var prefab = PickMonsterPrefab();
            if (prefab == null) { failPrefab++; continue; }

            if (!TryPickSpawnCandidate(points, out var candidatePos, out bool nullPoint))
            {
                if (nullPoint) failNull++;
                else failSource++;
                continue;
            }

            if (IsTooCloseToPlayer(candidatePos)) { failClose++; continue; }

            // Curfew: haunt the way out. All but the last two tries must be near the start point.
            if (!IsCurfewCandidateAllowed(candidatePos, attempt)) { failSource++; continue; }

            if (!TrySampleSpawnPosition(prefab, candidatePos, navmeshSampleRadius, out var hit))
            { failNav++; continue; }

            Vector3 pos = hit.position;
            if (!IsReachableFromAnyPlayer(prefab, pos))
            { failReach++; continue; }

            if (Physics.CheckSphere(pos, spawnCollisionRadius, collisionMask, QueryTriggerInteraction.Ignore))
            { failColl++; continue; }

            var go = Instantiate(prefab.gameObject, pos, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            ApplySpawnedRenderingLayer(go);

            if (!EnsureSpawnedAgentsOnNavMesh(go, navmeshSampleRadius, out string navFailure))
            {
                Debug.LogWarning($"[Spawner] Spawned {go.name} rejected: {navFailure}", go);
                Destroy(go);
                failNav++;
                continue;
            }

            var id = go.GetComponent<NetworkIdentity>();
            if (id == null)
            {
                Debug.LogError("[Spawner] Spawned object missing NetworkIdentity on root.");
                Destroy(go);
                return false;
            }

            if (!id.isSpawned)
                id.Spawn(prefab.gameObject);

            Debug.Log($"[Spawner] Spawned {go.name} at {pos}");
            _alive.Add(id);
            return true;
        }

        Debug.Log($"[Spawner] Spawn failed: source={failSource}, null={failNull}, close={failClose}, nav={failNav}, reach={failReach}, coll={failColl}, prefab={failPrefab}");
        return false;
    }

    private bool TryPickSpawnCandidate(List<Transform> points, out Vector3 candidatePos, out bool nullPoint)
    {
        candidatePos = default;
        nullPoint = false;

        if (points != null && points.Count > 0)
        {
            var p = points[UnityEngine.Random.Range(0, points.Count)];
            if (p == null)
            {
                nullPoint = true;
                return false;
            }

            candidatePos = p.position;
            return true;
        }

        return TryPickFallbackCandidate(out candidatePos);
    }

    private bool TryPickFallbackCandidate(out Vector3 candidatePos)
    {
        candidatePos = default;

        if (!allowSpawnWithoutMarkers || dungeonRoot == null)
            return false;

        if (_tiles.Count == 0)
            RebuildTileCache();

        if (_tiles.Count == 0)
            return false;

        int tileTries = Mathf.Max(1, fallbackTileTriesPerAttempt);
        int pointTries = Mathf.Max(1, fallbackPointTriesPerTile);

        for (int tileTry = 0; tileTry < tileTries; tileTry++)
        {
            var tile = _tiles[UnityEngine.Random.Range(0, _tiles.Count)];
            if (tile == null)
                continue;

            Bounds b = tile.Bounds;
            for (int p = 0; p < pointTries; p++)
            {
                float x = UnityEngine.Random.Range(b.min.x, b.max.x);
                float z = UnityEngine.Random.Range(b.min.z, b.max.z);
                float y = b.max.y + 0.5f;
                candidatePos = new Vector3(x, y, z);
                return true;
            }
        }

        return false;
    }

    private bool HasFallbackSpawnSource()
    {
        if (!allowSpawnWithoutMarkers || dungeonRoot == null)
            return false;

        if (_tiles.Count == 0)
            RebuildTileCache();

        return _tiles.Count > 0;
    }

    private void RebuildTileCache()
    {
        _tiles.Clear();
        if (dungeonRoot == null)
            return;

        var tiles = dungeonRoot.GetComponentsInChildren<Tile>(true);
        for (int i = 0; i < tiles.Length; i++)
        {
            if (tiles[i] != null)
                _tiles.Add(tiles[i]);
        }
    }


    private bool IsCurfewCandidateAllowed(Vector3 candidate, int attempt)
    {
        if (!IsCurfew() || DungeonStartPoint.Instance == null || curfewExitRadius <= 0f)
            return true;

        if (attempt >= Mathf.Max(0, spawnTriesPerTick - 2))
            return true;

        return (candidate - DungeonStartPoint.Instance.position).sqrMagnitude <= curfewExitRadius * curfewExitRadius;
    }

    private bool IsTooCloseToPlayer(Vector3 pos)
    {
        float minDistance = IsCurfew() ? Mathf.Min(minPlayerDistance, curfewMinPlayerDistance) : minPlayerDistance;
        float minSqr = minDistance * minDistance;
        var players = GameObject.FindGameObjectsWithTag("Player");
        for (int i = 0; i < players.Length; i++)
        {
            var tr = players[i].transform;
            if ((tr.position - pos).sqrMagnitude < minSqr)
                return true;
        }
        return false;
    }

    private NetworkIdentity PickMonsterPrefab()
    {
        if (monsters == null || monsters.Count == 0) return null;

        float total = 0f;
        for (int i = 0; i < monsters.Count; i++)
            total += Mathf.Max(0f, monsters[i].weight);

        if (total <= 0f)
        {
            // 가중치가 전부 0이면 그냥 랜덤
            for (int guard = 0; guard < 16; guard++)
            {
                var e = monsters[UnityEngine.Random.Range(0, monsters.Count)].prefab;
                if (e != null) return e;
            }
            return null;
        }

        float roll = UnityEngine.Random.value * total;
        for (int i = 0; i < monsters.Count; i++)
        {
            float w = Mathf.Max(0f, monsters[i].weight);
            roll -= w;
            if (roll <= 0f)
                return monsters[i].prefab;
        }

        return monsters[monsters.Count - 1].prefab;
    }

    private static bool TrySampleSpawnPosition(NetworkIdentity prefab, Vector3 sourcePosition, float maxDistance, out NavMeshHit hit)
    {
        NavMeshAgent prefabAgent = prefab != null ? prefab.GetComponentInChildren<NavMeshAgent>(true) : null;
        if (prefabAgent == null)
            return NavMesh.SamplePosition(sourcePosition, out hit, maxDistance, NavMesh.AllAreas);

        return TrySampleNavMeshForAgent(sourcePosition, prefabAgent.agentTypeID, prefabAgent.areaMask, maxDistance, out hit);
    }

    private static bool EnsureSpawnedAgentsOnNavMesh(GameObject instance, float maxDistance, out string failure)
    {
        failure = string.Empty;
        if (instance == null)
        {
            failure = "spawned instance is missing";
            return false;
        }

        NavMeshAgent[] agents = instance.GetComponentsInChildren<NavMeshAgent>(true);
        for (int i = 0; i < agents.Length; i++)
        {
            NavMeshAgent agent = agents[i];
            if (agent == null || !agent.enabled)
                continue;

            float sampleRadius = Mathf.Max(maxDistance, agent.radius + 1f);
            if (!TrySampleNavMeshForAgent(agent.transform.position, agent.agentTypeID, agent.areaMask, sampleRadius, out NavMeshHit hit))
            {
                failure = $"{agent.name} has no NavMesh point for agentType={agent.agentTypeID}";
                return false;
            }

            if (!agent.isOnNavMesh && !agent.Warp(hit.position))
            {
                failure = $"{agent.name} failed to warp onto NavMesh at {hit.position}";
                return false;
            }
        }

        return true;
    }

    private static bool TrySampleNavMeshForAgent(Vector3 sourcePosition, int agentTypeId, int areaMask, float maxDistance, out NavMeshHit hit)
    {
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agentTypeId,
            areaMask = areaMask
        };

        return NavMesh.SamplePosition(sourcePosition, out hit, maxDistance, filter);
    }

    public void ServerClearAll()
    {
        for (int i = _alive.Count - 1; i >= 0; i--)
        {
            if (_alive[i] != null && _alive[i].gameObject != null)
            {
                if (_alive[i].isSpawned)
                    _alive[i].Despawn();
                else
                    Destroy(_alive[i].gameObject);
            }
        }
        _alive.Clear();
    }

    public bool TrySpawnDebugMonster(string monsterQuery, Vector3 center, float radius, out GameObject spawned, out string matchedName, out string error)
    {
        spawned = null;
        matchedName = null;
        error = null;

        if (!isServer)
        {
            error = "Monster spawning requires host/server context";
            return false;
        }

        NetworkIdentity prefab = string.IsNullOrWhiteSpace(monsterQuery)
            ? PickMonsterPrefab()
            : FindMonsterPrefab(monsterQuery, out matchedName);

        if (prefab == null)
        {
            error = string.IsNullOrWhiteSpace(monsterQuery)
                ? "No monster prefab configured"
                : $"Monster '{monsterQuery}' not found";
            return false;
        }

        matchedName ??= prefab.name;

        float searchRadius = Mathf.Max(1f, radius);
        float sampleRadius = Mathf.Max(navmeshSampleRadius, searchRadius * 0.5f);
        int attempts = Mathf.Max(8, spawnTriesPerTick * 2);
        int failReach = 0;

        for (int i = 0; i < attempts; i++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * searchRadius;
            Vector3 candidate = center + new Vector3(offset.x, 1f, offset.y);

            if (!TrySampleSpawnPosition(prefab, candidate, sampleRadius, out var hit))
                continue;

            Vector3 position = hit.position;
            if (!IsReachableFromAnyPlayer(prefab, position))
            {
                failReach++;
                continue;
            }

            if (Physics.CheckSphere(position, spawnCollisionRadius, collisionMask, QueryTriggerInteraction.Ignore))
                continue;

            spawned = Instantiate(prefab.gameObject, position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            ApplySpawnedRenderingLayer(spawned);

            if (!EnsureSpawnedAgentsOnNavMesh(spawned, sampleRadius, out string navFailure))
            {
                error = navFailure;
                Destroy(spawned);
                spawned = null;
                continue;
            }

            var identity = spawned.GetComponent<NetworkIdentity>();
            if (identity != null)
            {
                if (!identity.isSpawned)
                    identity.Spawn(prefab.gameObject);
                _alive.Add(identity);
            }

            return true;
        }

        error = failReach > 0
            ? $"Unable to find player-reachable spawn point near {center} (reach rejected={failReach})"
            : $"Unable to find valid spawn point near {center}";
        return false;
    }

    private bool IsReachableFromAnyPlayer(NetworkIdentity prefab, Vector3 spawnPosition)
    {
        if (!requireSpawnReachableFromPlayer)
            return true;

        int agentTypeId = 0;
        int areaMask = NavMesh.AllAreas;
        NavMeshAgent prefabAgent = prefab != null ? prefab.GetComponentInChildren<NavMeshAgent>(true) : null;
        if (prefabAgent != null)
        {
            agentTypeId = prefabAgent.agentTypeID;
            areaMask = prefabAgent.areaMask;
        }

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agentTypeId,
            areaMask = areaMask
        };

        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        if (players == null || players.Length == 0)
            return false;

        float playerSampleRadius = Mathf.Max(navmeshSampleRadius, 3f);
        NavMeshPath path = new NavMeshPath();
        for (int i = 0; i < players.Length; i++)
        {
            GameObject player = players[i];
            if (player == null || !player.activeInHierarchy)
                continue;

            if (!NavMesh.SamplePosition(player.transform.position, out NavMeshHit playerHit, playerSampleRadius, filter))
                continue;

            if (!NavMesh.CalculatePath(playerHit.position, spawnPosition, filter, path))
                continue;

            if (path.status == NavMeshPathStatus.PathComplete)
                return true;
        }

        return false;
    }

    private NetworkIdentity FindMonsterPrefab(string monsterQuery, out string matchedName)
    {
        matchedName = null;
        if (monsters == null || monsters.Count == 0)
            return null;

        string query = monsterQuery.Trim();

        for (int pass = 0; pass < 2; pass++)
        {
            bool exact = pass == 0;
            for (int i = 0; i < monsters.Count; i++)
            {
                var prefab = monsters[i].prefab;
                if (prefab == null)
                    continue;

                string candidate = prefab.name;
                bool matched = exact
                    ? string.Equals(candidate, query, StringComparison.OrdinalIgnoreCase)
                    : candidate.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

                if (!matched)
                    continue;

                matchedName = candidate;
                return prefab;
            }
        }

        return null;
    }

    private uint ResolveRenderingLayerMask(string layerName)
    {
        int idx = RenderingLayerMask.NameToRenderingLayer(layerName);
        if (idx < 0)
        {
            Debug.LogError($"[Spawner] Rendering Layer '{layerName}' not found. (Project Settings > Tags and Layers > Rendering Layers)", this);
            return 0;
        }

        return 1u << idx;
    }

    private void ApplySpawnedRenderingLayer(GameObject spawned)
    {
        if (!applyDungeonRenderingLayerToSpawned || spawned == null || _dungeonRenderingLayerMask == 0)
            return;

        var renderers = spawned.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.renderingLayerMask = _dungeonRenderingLayerMask;
        }
    }
}
