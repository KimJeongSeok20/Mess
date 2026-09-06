using System;
using System.Collections;
using System.Collections.Generic;
using DunGen;
using UnityEngine;
using UnityEngine.Rendering;

public class DungeonLootPostProcessor : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private RuntimeDungeon runtimeDungeon;
    [SerializeField] private NetworkDungeonController controller;
    [SerializeField] private ItemSpawner pointSampler;
    [SerializeField] private DungeonRuntimeNavMeshPipeline runtimeNavMeshPipeline;

    [Header("Spawn Budget Loop")]
    [SerializeField] private int maxItems = 500;
    [Tooltip("Price multiplier at the deepest tile: price *= 1 + bonus * NormalizedDepth. 0.6 = deepest room pays 60% more.")]
    [SerializeField, Range(0f, 3f)] private float depthPriceBonus = 0.6f;
    [SerializeField, Min(1)] private int maxConsecutiveSpawnPointFailures = 32;

    [Tooltip("아이템 1개를 놓을 때, 타일을 다시 뽑아보는 횟수(가중치 적용). 1이면 타일 1개 고정.")]
    [SerializeField] private int maxTileTriesPerItem = 3;

    [Tooltip("선택된 타일에서 포인트를 몇 번 시도할지")]
    [SerializeField] private int maxPointTries = 30;

    [Header("Anchor Spawn")]
    [SerializeField] private bool useItemSpawnAnchors = true;
    [SerializeField] private bool includeInactiveAnchors;
    [SerializeField] private bool allowAnchorReuse;
    [SerializeField] private bool fallbackToSurfaceSampling = true;

    [Header("Debug")]
    [SerializeField] private bool logSpawnPointFailures = true;
    [SerializeField] private bool enableStepTimingDiagnostics = true;

    [SerializeField] private string lootRootName = "__LootRoot";

    [Header("Delay (frames)")]
    [Tooltip("PostProcess 직후 몇 프레임 기다렸다가 스폰할지 (1~2 권장)")]
    [SerializeField] private int delayFrames = 3;
    [SerializeField] private bool deferSpawnUntilRuntimeNavMeshReady = true;

    [Header("Async Spawn Smoothing")]
    [SerializeField] private bool spreadSpawnAcrossFrames = true;
    [SerializeField, Min(0.5f)] private float spawnWorkBudgetMsPerFrame = 2.5f;
    [SerializeField, Min(1)] private int minItemsPerFrameBeforeYield = 4;

    [Header("Item Render Layer Trigger")]
    [SerializeField] private string itemRenderLayerZoneName = "__ItemRenderLayerZone";
    [Tooltip("Optional padding to expand the trigger volume beyond tile bounds")]
    [SerializeField] private Vector3 triggerPadding = new Vector3(1f, 1f, 1f);
    [SerializeField] private bool applyDungeonLayerOnSpawn = true;
    [SerializeField] private string dungeonRenderingLayerName = "Dungeon";

    private Transform _lootRoot;
    private Coroutine _spawnRoutine;
    private readonly Dictionary<Tile, ItemSpawnAnchor[]> _tileAnchors = new Dictionary<Tile, ItemSpawnAnchor[]>();
    private readonly HashSet<int> _usedAnchors = new HashSet<int>();
    private uint _dungeonRenderingLayerMask;
    private DunGen.DungeonGenerator _deferredGenerator;
    private int _deferredRunId;

    public int LootCount => _lootRoot ? _lootRoot.childCount : 0;

    private enum SpawnPointSource
    {
        None,
        Anchor,
        Surface
    }

    private enum SpawnPointFailureReason
    {
        None,
        NoTileCandidate,
        AnchorFailed,
        SurfaceFailed,
        AnchorAndSurfaceFailed,
        NoSpawnMethodAvailable
    }

    private void Awake()
    {
        if (!runtimeDungeon) runtimeDungeon = GetComponent<RuntimeDungeon>();
        if (!controller) controller = GetComponent<NetworkDungeonController>();
        if (!pointSampler) pointSampler = GetComponent<ItemSpawner>();

        _dungeonRenderingLayerMask = ResolveRenderingLayerMask(dungeonRenderingLayerName);
    }

    private void OnEnable()
    {
        if (!runtimeDungeon) return;
        runtimeDungeon.Generator.RegisterPostProcessStep(OnPostProcess, 10, PostProcessPhase.AfterBuiltIn);

        if (runtimeNavMeshPipeline != null)
            runtimeNavMeshPipeline.RuntimeNavMeshReady += OnRuntimeNavMeshReady;
    }

    private void OnDisable()
    {
        if (runtimeDungeon)
            runtimeDungeon.Generator.UnregisterPostProcessStep(OnPostProcess);

        if (runtimeNavMeshPipeline != null)
            runtimeNavMeshPipeline.RuntimeNavMeshReady -= OnRuntimeNavMeshReady;

        _deferredGenerator = null;
        _deferredRunId = 0;
        StopSpawnRoutine();
    }

    private void OnPostProcess(DunGen.DungeonGenerator generator)
    {
        float totalStart = Time.realtimeSinceStartup;

        // Always build/update the item render layer trigger volume
        float triggerStart = Time.realtimeSinceStartup;
        BuildItemRenderLayerTrigger(generator);
        float triggerMs = (Time.realtimeSinceStartup - triggerStart) * 1000f;

        if (ShouldDeferForRuntimeNavMesh(generator))
        {
            _deferredGenerator = generator;
            _deferredRunId = runtimeNavMeshPipeline != null ? runtimeNavMeshPipeline.CurrentRunId : 0;

            if (enableStepTimingDiagnostics)
            {
                float totalMs = (Time.realtimeSinceStartup - totalStart) * 1000f;
                Debug.Log($"[DungeonGenDiag][LootPost] totalMs={totalMs:0.0} triggerMs={triggerMs:0.0} deferredUntilRuntimeNavMesh=true", this);
            }

            return;
        }

        float canSpawnStart = Time.realtimeSinceStartup;
        if (!CanSpawn(generator, out var selector, out var budget))
        {
            if (enableStepTimingDiagnostics)
            {
                float totalMs = (Time.realtimeSinceStartup - totalStart) * 1000f;
                Debug.Log($"[DungeonGenDiag][LootPost] totalMs={totalMs:0.0} triggerMs={triggerMs:0.0} canSpawn=false", this);
            }
            return;
        }
        int baseBudget = budget;
        budget = TimeManager.ScaleDailyBudget(baseBudget);
        float canSpawnMs = (Time.realtimeSinceStartup - canSpawnStart) * 1000f;

        int seed = controller.CurrentSeed;

        float queueStart = Time.realtimeSinceStartup;
        StopSpawnRoutine();
        _spawnRoutine = StartCoroutine(SpawnLootAfterFrames(generator, selector, budget, seed));
        float queueMs = (Time.realtimeSinceStartup - queueStart) * 1000f;

        if (enableStepTimingDiagnostics)
        {
            float totalMs = (Time.realtimeSinceStartup - totalStart) * 1000f;
            Debug.Log(
                $"[DungeonGenDiag][LootPost] totalMs={totalMs:0.0} triggerMs={triggerMs:0.0} canSpawnMs={canSpawnMs:0.0} queueMs={queueMs:0.0} baseBudget={baseBudget} budget={budget} seed={seed}",
                this);
        }
    }

    private bool ShouldDeferForRuntimeNavMesh(DunGen.DungeonGenerator generator)
    {
        return deferSpawnUntilRuntimeNavMeshReady &&
               generator != null &&
               runtimeNavMeshPipeline != null &&
               !runtimeNavMeshPipeline.IsReady;
    }

    private void OnRuntimeNavMeshReady(DunGen.DungeonGenerator generator, int runId)
    {
        if (_deferredGenerator == null)
            return;

        if (_deferredRunId != 0 && _deferredRunId != runId)
            return;

        DunGen.DungeonGenerator pending = _deferredGenerator;
        _deferredGenerator = null;
        _deferredRunId = 0;
        OnPostProcess(pending);
    }

    private IEnumerator SpawnLootAfterFrames(DunGen.DungeonGenerator generator, SpawnSelector selector, int budget, int seed)
    {
        float totalStart = Time.realtimeSinceStartup;
        int frames = Mathf.Clamp(delayFrames, 0, 10);
        for (int i = 0; i < frames; i++)
            yield return null;

        float waitMs = (Time.realtimeSinceStartup - totalStart) * 1000f;

        if (!CanSpawn(generator, out _, out _))
        {
            _spawnRoutine = null;

            if (enableStepTimingDiagnostics)
                Debug.Log($"[DungeonGenDiag][LootPost] delayedSpawn aborted waitMs={waitMs:0.0} frames={frames}", this);

            yield break;
        }

        float spawnStart = Time.realtimeSinceStartup;
        yield return SpawnLoot(generator, selector, budget, seed);
        float spawnMs = (Time.realtimeSinceStartup - spawnStart) * 1000f;
        _spawnRoutine = null;

        if (enableStepTimingDiagnostics)
        {
            float totalMs = (Time.realtimeSinceStartup - totalStart) * 1000f;
            Debug.Log($"[DungeonGenDiag][LootPost] delayedSpawn totalMs={totalMs:0.0} waitMs={waitMs:0.0} spawnMs={spawnMs:0.0} frames={frames}", this);
        }
    }

    private IEnumerator SpawnLoot(DunGen.DungeonGenerator generator, SpawnSelector selector, int budget, int seed)
    {
        float totalStart = Time.realtimeSinceStartup;

        float setupStart = Time.realtimeSinceStartup;
        EnsureLootRoot();
        ClearLootServer();
        _tileAnchors.Clear();
        _usedAnchors.Clear();
        float setupMs = (Time.realtimeSinceStartup - setupStart) * 1000f;

        var tiles = generator.CurrentDungeon.AllTiles;
        if (tiles == null || tiles.Count == 0)
            yield break;

        var rng = new System.Random(seed);

        int spent = 0;
        int spawned = 0;
        int failCount = 0;
        int consecutiveSpawnPointFailures = 0;
        bool stoppedByFailureLimit = false;
        int anchorSpawned = 0;
        int surfaceSpawned = 0;

        int failNoTile = 0;
        int failAnchorOnly = 0;
        int failSurfaceOnly = 0;
        int failBoth = 0;
        int failNoMethod = 0;
        int floorAdjusted = 0;
        int colliderLifted = 0;

        float findSpawnPointMs = 0f;
        float floorAdjustMs = 0f;
        float instantiateMs = 0f;
        float renderLayerMs = 0f;
        float colliderLiftMs = 0f;
        int yieldedFrames = 0;
        int spawnedThisFrame = 0;
        float frameStart = Time.realtimeSinceStartup;

        float loopStart = Time.realtimeSinceStartup;

        while (spent < budget && spawned < maxItems)
        {
            if (!selector.TryPick(out var picked, rng) || picked?.prefab == null)
                break;

            float findPointStart = Time.realtimeSinceStartup;
            if (!TryFindSpawnPoint(tiles, rng, picked.tags, out var pos, out var spawnTile, out var spawnSource, out var failReason))
            {
                findSpawnPointMs += (Time.realtimeSinceStartup - findPointStart) * 1000f;
                failCount++;
                consecutiveSpawnPointFailures++;

                switch (failReason)
                {
                    case SpawnPointFailureReason.NoTileCandidate:
                        failNoTile++;
                        break;
                    case SpawnPointFailureReason.AnchorFailed:
                        failAnchorOnly++;
                        break;
                    case SpawnPointFailureReason.SurfaceFailed:
                        failSurfaceOnly++;
                        break;
                    case SpawnPointFailureReason.AnchorAndSurfaceFailed:
                        failBoth++;
                        break;
                    case SpawnPointFailureReason.NoSpawnMethodAvailable:
                        failNoMethod++;
                        break;
                }

                if (logSpawnPointFailures)
                {
                    string itemName = picked.prefab ? picked.prefab.name : "null";
                    string tagsText = (picked.tags != null && picked.tags.Count > 0)
                        ? string.Join(",", picked.tags)
                        : "-";
                    Debug.LogWarning($"[Loot] Spawn point failed item={itemName}, tags={tagsText}, reason={failReason}");
                }

                if (consecutiveSpawnPointFailures >= Mathf.Max(1, maxConsecutiveSpawnPointFailures))
                {
                    stoppedByFailureLimit = true;
                    Debug.LogWarning(
                        $"[Loot] Stopped after {consecutiveSpawnPointFailures} consecutive spawn-point failures " +
                        $"to prevent an infinite budget loop. spawned={spawned}, spent={spent}, budget={budget}.",
                        this);
                    break;
                }

                continue;
            }
            findSpawnPointMs += (Time.realtimeSinceStartup - findPointStart) * 1000f;
            consecutiveSpawnPointFailures = 0;

            if (spawnSource == SpawnPointSource.Anchor)
                anchorSpawned++;
            else if (spawnSource == SpawnPointSource.Surface)
                surfaceSpawned++;

            float floorStart = Time.realtimeSinceStartup;
            if (pointSampler && spawnTile != null && pointSampler.TryEnsurePointAboveFloor(spawnTile, pos, out var adjustedPos) && adjustedPos.y > pos.y)
            {
                pos = adjustedPos;
                floorAdjusted++;
            }
            floorAdjustMs += (Time.realtimeSinceStartup - floorStart) * 1000f;

            float yaw = (float)rng.NextDouble() * 360f;
            var rot = Quaternion.Euler(0f, yaw, 0f);

            float instantiateStart = Time.realtimeSinceStartup;
            var go = Instantiate(picked.prefab, pos, rot, _lootRoot);
            instantiateMs += (Time.realtimeSinceStartup - instantiateStart) * 1000f;

            float layerStart = Time.realtimeSinceStartup;
            ApplyDungeonRenderingLayer(go);
            renderLayerMs += (Time.realtimeSinceStartup - layerStart) * 1000f;

            float liftStart = Time.realtimeSinceStartup;
            if (go && TryLiftSpawnedObjectAboveReference(go.transform, pos))
                colliderLifted++;
            colliderLiftMs += (Time.realtimeSinceStartup - liftStart) * 1000f;

            int cost = 1;
            if (go && go.TryGetComponent<Item>(out var item))
            {
                // Deeper tiles pay more: the "one more room?" decision needs a price gradient.
                if (depthPriceBonus > 0f && spawnTile != null && spawnTile.Placement != null)
                {
                    float depth = Mathf.Clamp01(spawnTile.Placement.NormalizedDepth);
                    int depthPrice = Mathf.RoundToInt(item.Price * (1f + depthPriceBonus * depth));
                    if (depthPrice > item.Price)
                        item.SetPriceImmediateOnServer(depthPrice);
                }

                cost = Mathf.Max(1, item.Price);
            }

            spent += cost;
            spawned++;
            spawnedThisFrame++;

            if (spreadSpawnAcrossFrames)
            {
                bool reachedMinSpawnCount = spawnedThisFrame >= minItemsPerFrameBeforeYield;
                float frameMs = (Time.realtimeSinceStartup - frameStart) * 1000f;
                if (reachedMinSpawnCount && frameMs >= spawnWorkBudgetMsPerFrame)
                {
                    yieldedFrames++;
                    spawnedThisFrame = 0;
                    frameStart = Time.realtimeSinceStartup;
                    yield return null;
                }
            }
        }

        float loopMs = (Time.realtimeSinceStartup - loopStart) * 1000f;

        int sourceTotal = anchorSpawned + surfaceSpawned;
        float anchorRatio = sourceTotal > 0 ? (anchorSpawned * 100f) / sourceTotal : 0f;
        float surfaceRatio = sourceTotal > 0 ? (surfaceSpawned * 100f) / sourceTotal : 0f;

        Debug.Log($"[Loot] spawned={spawned}, spent={spent}, budget={budget}, fails={failCount}, stoppedByFailureLimit={stoppedByFailureLimit}, delayFrames={delayFrames}, anchor={anchorSpawned} ({anchorRatio:F1}%), surface={surfaceSpawned} ({surfaceRatio:F1}%), floorAdjusted={floorAdjusted}, colliderLifted={colliderLifted}, failNoTile={failNoTile}, failAnchor={failAnchorOnly}, failSurface={failSurfaceOnly}, failBoth={failBoth}, failNoMethod={failNoMethod}");

        if (enableStepTimingDiagnostics)
        {
            float totalMs = (Time.realtimeSinceStartup - totalStart) * 1000f;
            Debug.Log(
                $"[DungeonGenDiag][LootSpawn] totalMs={totalMs:0.0} setupMs={setupMs:0.0} loopMs={loopMs:0.0} " +
                $"findPointMs={findSpawnPointMs:0.0} floorMs={floorAdjustMs:0.0} instantiateMs={instantiateMs:0.0} " +
                $"layerMs={renderLayerMs:0.0} liftMs={colliderLiftMs:0.0} spawned={spawned} fails={failCount} " +
                $"yieldedFrames={yieldedFrames} spread={spreadSpawnAcrossFrames} budgetMs={spawnWorkBudgetMsPerFrame:0.0} tiles={tiles.Count}",
                this);
        }

        yield break;
    }

    private bool TryFindSpawnPoint(IList<Tile> tiles, System.Random rng, IReadOnlyList<string> itemTags, out Vector3 pos, out Tile spawnTile, out SpawnPointSource source, out SpawnPointFailureReason failureReason)
    {
        pos = default;
        spawnTile = null;
        source = SpawnPointSource.None;
        failureReason = SpawnPointFailureReason.None;
        if (tiles == null || tiles.Count == 0)
        {
            failureReason = SpawnPointFailureReason.NoTileCandidate;
            return false;
        }

        int tileTries = Mathf.Max(1, maxTileTriesPerItem);
        int pointTries = Mathf.Max(1, maxPointTries);
        bool canAnchorSample = useItemSpawnAnchors;
        bool canSurfaceSample = pointSampler && (!useItemSpawnAnchors || fallbackToSurfaceSampling);
        bool sawTileCandidate = false;
        bool triedAnchor = false;
        bool triedSurface = false;

        for (int tileTry = 0; tileTry < tileTries; tileTry++)
        {
            Tile chosenTile = PickWeightedTile(tiles, rng);
            if (chosenTile == null) continue;
            sawTileCandidate = true;

            bool hasAnchors = canAnchorSample && GetAnchors(chosenTile).Length > 0;

            if (hasAnchors && canSurfaceSample)
            {
                bool tryAnchorFirst = rng.NextDouble() < 0.5d;

                if (tryAnchorFirst)
                {
                    triedAnchor = true;
                    if (TryFindAnchorPoint(chosenTile, rng, itemTags, out pos))
                    {
                        spawnTile = chosenTile;
                        source = SpawnPointSource.Anchor;
                        return true;
                    }

                    triedSurface = true;
                    if (TryFindSurfacePoint(chosenTile, rng, pointTries, out pos))
                    {
                        spawnTile = chosenTile;
                        source = SpawnPointSource.Surface;
                        return true;
                    }
                }
                else
                {
                    triedSurface = true;
                    if (TryFindSurfacePoint(chosenTile, rng, pointTries, out pos))
                    {
                        spawnTile = chosenTile;
                        source = SpawnPointSource.Surface;
                        return true;
                    }

                    triedAnchor = true;
                    if (TryFindAnchorPoint(chosenTile, rng, itemTags, out pos))
                    {
                        spawnTile = chosenTile;
                        source = SpawnPointSource.Anchor;
                        return true;
                    }
                }

                continue;
            }

            if (hasAnchors)
            {
                triedAnchor = true;
                if (TryFindAnchorPoint(chosenTile, rng, itemTags, out pos))
                {
                    spawnTile = chosenTile;
                    source = SpawnPointSource.Anchor;
                    return true;
                }
            }

            if (canSurfaceSample)
            {
                triedSurface = true;
                if (TryFindSurfacePoint(chosenTile, rng, pointTries, out pos))
                {
                    spawnTile = chosenTile;
                    source = SpawnPointSource.Surface;
                    return true;
                }
            }
        }

        if (!sawTileCandidate)
        {
            failureReason = SpawnPointFailureReason.NoTileCandidate;
            return false;
        }

        if (triedAnchor && triedSurface)
        {
            failureReason = SpawnPointFailureReason.AnchorAndSurfaceFailed;
            return false;
        }

        if (triedAnchor)
        {
            failureReason = SpawnPointFailureReason.AnchorFailed;
            return false;
        }

        if (triedSurface)
        {
            failureReason = SpawnPointFailureReason.SurfaceFailed;
            return false;
        }

        if (!canAnchorSample && !canSurfaceSample)
        {
            failureReason = SpawnPointFailureReason.NoSpawnMethodAvailable;
            return false;
        }

        failureReason = SpawnPointFailureReason.NoSpawnMethodAvailable;
        return false;
    }

    private static bool TryLiftSpawnedObjectAboveReference(Transform spawned, Vector3 referencePos)
    {
        if (!spawned)
            return false;

        if (!TryGetObjectBounds(spawned, out var bounds))
            return false;

        float minSafeY = referencePos.y + 0.01f;
        float liftAmount = minSafeY - bounds.min.y;
        if (liftAmount <= 0f)
            return false;

        spawned.position += Vector3.up * liftAmount;
        return true;
    }

    private static bool TryGetObjectBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool found = false;

        var colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider c = colliders[i];
            if (!c || c.isTrigger) continue;

            if (!found)
            {
                bounds = c.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(c.bounds);
            }
        }

        if (found)
            return true;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (!r) continue;

            if (!found)
            {
                bounds = r.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return found;
    }

    private bool TryFindSurfacePoint(Tile tile, System.Random rng, int pointTries, out Vector3 pos)
    {
        pos = default;
        if (!pointSampler || tile == null)
            return false;

        int tries = Mathf.Max(1, pointTries);
        for (int p = 0; p < tries; p++)
        {
            if (pointSampler.TryFindPointInTile(tile, rng, out pos))
                return true;
        }

        return false;
    }

    private bool TryFindAnchorPoint(Tile tile, System.Random rng, IReadOnlyList<string> itemTags, out Vector3 pos)
    {
        pos = default;
        bool hasTagFilter = itemTags != null && itemTags.Count > 0;

        var anchors = GetAnchors(tile);
        if (anchors == null || anchors.Length == 0)
            return false;

        if (TryPickAnchor(anchors, rng, itemTags, hasTagFilter, out var matchedAnchor))
        {
            pos = matchedAnchor.Position;
            return true;
        }

        if (hasTagFilter && TryPickAnchor(anchors, rng, itemTags, false, out var fallbackAnchor))
        {
            pos = fallbackAnchor.Position;
            return true;
        }

        return false;
    }

    private ItemSpawnAnchor[] GetAnchors(Tile tile)
    {
        if (tile == null)
            return Array.Empty<ItemSpawnAnchor>();

        if (_tileAnchors.TryGetValue(tile, out var cached))
            return cached;

        var found = tile.GetComponentsInChildren<ItemSpawnAnchor>(includeInactiveAnchors);
        _tileAnchors[tile] = found;
        return found;
    }

    private bool TryPickAnchor(ItemSpawnAnchor[] anchors, System.Random rng, IReadOnlyList<string> itemTags, bool requireTagMatch, out ItemSpawnAnchor picked)
    {
        picked = null;
        double totalWeight = 0d;

        for (int i = 0; i < anchors.Length; i++)
        {
            ItemSpawnAnchor anchor = anchors[i];
            if (!anchor) continue;

            int anchorId = anchor.GetInstanceID();
            if (!allowAnchorReuse && _usedAnchors.Contains(anchorId))
                continue;

            float weight = anchor.Weight;
            if (weight <= 0f) continue;

            if (requireTagMatch && (itemTags == null || itemTags.Count == 0 || !anchor.HasAnyTag(itemTags)))
                continue;

            totalWeight += weight;
            if (rng.NextDouble() * totalWeight < weight)
                picked = anchor;
        }

        if (!picked)
            return false;

        if (!allowAnchorReuse)
            _usedAnchors.Add(picked.GetInstanceID());

        return true;
    }

    // ✅ area 고려 없음: weight 값 그대로만 사용
    // - 컴포넌트 없으면 1
    // - weight가 1.5면 1.5, 0.5면 0.5 그대로
    private static float GetTileWeight(Tile tile)
    {
        if (tile == null) return 0f;

        var w = tile.GetComponent<TileItemSpawnWeight>();
        if (!w) return 1f;

        return Mathf.Max(0f, w.weight);
    }

    private static Tile PickWeightedTile(IList<Tile> tiles, System.Random rng)
    {
        Tile picked = null;
        double total = 0.0;

        for (int i = 0; i < tiles.Count; i++)
        {
            Tile t = tiles[i];
            if (t == null) continue;

            float w = GetTileWeight(t);
            if (w <= 0f) continue;

            total += w;
            if (rng.NextDouble() * total < w)
                picked = t;
        }

        if (picked != null) return picked;

        // fallback: 전부 weight 0이거나 전부 null이면 균등 랜덤
        for (int k = 0; k < 8; k++)
        {
            Tile t = tiles[rng.Next(tiles.Count)];
            if (t != null) return t;
        }
        return null;
    }

    private bool CanSpawn(DunGen.DungeonGenerator generator, out SpawnSelector selector, out int budget)
    {
        selector = null;
        budget = 0;

        if (!controller || !controller.isServer) return false;
        if (!controller.IsDungeonActive) return false;

        bool canAnchorSpawn = useItemSpawnAnchors;
        bool canSurfaceSpawn = pointSampler && (!useItemSpawnAnchors || fallbackToSurfaceSampling);
        if (!canAnchorSpawn && !canSurfaceSpawn) return false;

        if (generator == null || generator.CurrentDungeon == null) return false;

        if (!controller.TryGetLoot(out selector, out budget)) return false;
        if (selector == null || budget <= 0) return false;

        return true;
    }

    private void StopSpawnRoutine()
    {
        if (_spawnRoutine != null)
        {
            StopCoroutine(_spawnRoutine);
            _spawnRoutine = null;
        }
    }

    private void EnsureLootRoot()
    {
        if (_lootRoot) return;

        var existing = transform.Find(lootRootName);
        if (existing)
        {
            _lootRoot = existing;
            return;
        }

        var root = new GameObject(lootRootName);
        root.transform.SetParent(transform, false);
        _lootRoot = root.transform;
    }

    public void ClearLootServer()
    {
        if (!controller || !controller.isServer) return;

        EnsureLootRoot();
        if (!_lootRoot) return;

        for (int i = _lootRoot.childCount - 1; i >= 0; i--)
            Destroy(_lootRoot.GetChild(i).gameObject);
    }

    public bool TrySpawnDebugItem(string itemQuery, Vector3 center, float radius, out GameObject spawned, out string matchedName, out string error)
    {
        spawned = null;
        matchedName = null;
        error = null;

        if (!controller || !controller.isServer)
        {
            error = "Item spawning requires host/server context";
            return false;
        }

        if (!TryFindDebugItemPrefab(itemQuery, out var prefab, out matchedName))
        {
            error = string.IsNullOrWhiteSpace(itemQuery)
                ? "No item prefab available"
                : $"Item '{itemQuery}' not found";
            return false;
        }

        EnsureLootRoot();
        if (!TryFindDebugItemSpawnPoint(center, radius, out var spawnPoint))
        {
            error = $"Unable to find valid item spawn point near {center}";
            return false;
        }

        spawned = Instantiate(prefab, spawnPoint, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f), _lootRoot);
        ApplyDungeonRenderingLayer(spawned);
        TryLiftSpawnedObjectAboveReference(spawned.transform, spawnPoint);
        return true;
    }

    private bool TryFindDebugItemPrefab(string itemQuery, out GameObject prefab, out string matchedName)
    {
        prefab = null;
        matchedName = null;

        var candidates = new List<ItemSpawnlist.ItemEntry>();

        if (controller && controller.TryGetLoot(out var selector, out _))
            AppendSelectorEntries(selector, candidates);

        var loadedLists = Resources.FindObjectsOfTypeAll<ItemSpawnlist>();
        for (int i = 0; i < loadedLists.Length; i++)
        {
            var list = loadedLists[i];
            if (list == null || list.Items == null)
                continue;

            for (int j = 0; j < list.Items.Count; j++)
            {
                var entry = list.Items[j];
                if (entry != null && entry.prefab != null)
                    candidates.Add(entry);
            }
        }

        if (candidates.Count == 0)
            return false;

        if (string.IsNullOrWhiteSpace(itemQuery))
        {
            prefab = candidates[UnityEngine.Random.Range(0, candidates.Count)].prefab;
            matchedName = GetItemDisplayName(prefab);
            return prefab != null;
        }

        string query = itemQuery.Trim();
        for (int pass = 0; pass < 2; pass++)
        {
            bool exact = pass == 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                var entry = candidates[i];
                if (entry == null || entry.prefab == null)
                    continue;

                string displayName = GetItemDisplayName(entry.prefab);
                string prefabName = entry.prefab.name;
                bool matched = exact
                    ? string.Equals(displayName, query, StringComparison.OrdinalIgnoreCase)
                      || string.Equals(prefabName, query, StringComparison.OrdinalIgnoreCase)
                    : displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                      || prefabName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

                if (!matched)
                    continue;

                prefab = entry.prefab;
                matchedName = displayName;
                return true;
            }
        }

        return false;
    }

    private static void AppendSelectorEntries(SpawnSelector selector, List<ItemSpawnlist.ItemEntry> destination)
    {
        if (selector == null || selector.Source == null || selector.Source.Items == null)
            return;

        for (int i = 0; i < selector.Source.Items.Count; i++)
        {
            var entry = selector.Source.Items[i];
            if (entry != null && entry.prefab != null)
                destination.Add(entry);
        }
    }

    private static string GetItemDisplayName(GameObject prefab)
    {
        if (prefab != null && prefab.TryGetComponent<Item>(out var item) && !string.IsNullOrWhiteSpace(item.ItemName))
            return item.ItemName;

        return prefab != null ? prefab.name : string.Empty;
    }

    private bool TryFindDebugItemSpawnPoint(Vector3 center, float radius, out Vector3 spawnPoint)
    {
        spawnPoint = default;
        float searchRadius = Mathf.Max(1f, radius);
        var rng = new System.Random(Environment.TickCount);

        for (int i = 0; i < 10; i++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * searchRadius;
            Vector3 candidate = center + new Vector3(offset.x, 1f, offset.y);
            float sampleRadius = Mathf.Max(0.5f, searchRadius * 0.5f);
            bool found = pointSampler != null
                ? pointSampler.TrySampleNavMesh(candidate, sampleRadius, out var localHit)
                : UnityEngine.AI.NavMesh.SamplePosition(candidate, out localHit, sampleRadius, UnityEngine.AI.NavMesh.AllAreas);

            if (found)
            {
                spawnPoint = localHit.position + Vector3.up * 0.05f;
                return true;
            }
        }

        var tiles = runtimeDungeon != null ? runtimeDungeon.Generator.CurrentDungeon?.AllTiles : null;
        Tile nearestTile = FindNearestTile(tiles, center);
        if (nearestTile != null && pointSampler != null)
        {
            int tries = Mathf.Max(6, maxPointTries / 2);
            for (int i = 0; i < tries; i++)
            {
                if (pointSampler.TryFindPointInTile(nearestTile, rng, out spawnPoint))
                    return true;
            }
        }

        return false;
    }

    private static Tile FindNearestTile(IList<Tile> tiles, Vector3 center)
    {
        if (tiles == null || tiles.Count == 0)
            return null;

        Tile nearest = null;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            if (tile == null)
                continue;

            Vector3 closest = tile.Bounds.ClosestPoint(center);
            float sqrDistance = (closest - center).sqrMagnitude;
            if (sqrDistance >= bestDistance)
                continue;

            bestDistance = sqrDistance;
            nearest = tile;
        }

        return nearest;
    }

    /// <summary>
    /// Builds or updates the item render layer trigger volume from all tile bounds.
    /// Creates/updates a child GameObject under RuntimeDungeon with BoxCollider + DungeonItemRenderLayerTrigger.
    /// </summary>
    private void BuildItemRenderLayerTrigger(DunGen.DungeonGenerator generator)
    {
        if (generator == null || generator.CurrentDungeon == null)
        {
            Debug.LogWarning("[DungeonLootPostProcessor] Cannot build item render layer trigger: generator or dungeon is null");
            return;
        }

        var tiles = generator.CurrentDungeon.AllTiles;
        if (tiles == null || tiles.Count == 0)
        {
            Debug.LogWarning("[DungeonLootPostProcessor] Cannot build item render layer trigger: no tiles found");
            return;
        }

        // Calculate combined bounds from all tiles
        Bounds combinedBounds = new Bounds();
        bool initialized = false;

        foreach (var tile in tiles)
        {
            if (tile == null) continue;

            if (!initialized)
            {
                combinedBounds = tile.Bounds;
                initialized = true;
            }
            else
            {
                combinedBounds.Encapsulate(tile.Bounds);
            }
        }

        if (!initialized)
        {
            Debug.LogWarning("[DungeonLootPostProcessor] Cannot build item render layer trigger: no valid tile bounds");
            return;
        }

        // Apply padding
        combinedBounds.Expand(triggerPadding * 2f); // Expand expands in all directions

        // Find or create the trigger zone GameObject under RuntimeDungeon
        Transform root = runtimeDungeon != null ? runtimeDungeon.transform : transform;
        Transform zoneTransform = root.Find(itemRenderLayerZoneName);
        GameObject zoneObject;

        if (zoneTransform == null)
        {
            zoneObject = new GameObject(itemRenderLayerZoneName);
            zoneObject.transform.SetParent(root, false);
            zoneTransform = zoneObject.transform;
        }
        else
        {
            zoneObject = zoneTransform.gameObject;
        }

        // Position at bounds center (world space)
        zoneTransform.position = combinedBounds.center;

        // Get or add BoxCollider
        BoxCollider boxCollider = zoneObject.GetComponent<BoxCollider>();
        if (boxCollider == null)
        {
            boxCollider = zoneObject.AddComponent<BoxCollider>();
        }

        boxCollider.isTrigger = true;

        // Calculate size accounting for lossyScale
        Vector3 lossyScale = zoneTransform.lossyScale;
        Vector3 localSize = new Vector3(
            combinedBounds.size.x / Mathf.Max(lossyScale.x, 0.0001f),
            combinedBounds.size.y / Mathf.Max(lossyScale.y, 0.0001f),
            combinedBounds.size.z / Mathf.Max(lossyScale.z, 0.0001f)
        );

        boxCollider.center = Vector3.zero; // Center is at transform position
        boxCollider.size = localSize;

        // Get or add DungeonItemRenderLayerTrigger
        DungeonItemRenderLayerTrigger trigger = zoneObject.GetComponent<DungeonItemRenderLayerTrigger>();
        if (trigger == null)
        {
            trigger = zoneObject.AddComponent<DungeonItemRenderLayerTrigger>();
        }

        Debug.Log($"[DungeonLootPostProcessor] Item render layer trigger built: center={combinedBounds.center}, size={combinedBounds.size}, tiles={tiles.Count}");
    }

    private uint ResolveRenderingLayerMask(string layerName)
    {
        int idx = RenderingLayerMask.NameToRenderingLayer(layerName);
        if (idx < 0)
        {
            Debug.LogError($"[DungeonLootPostProcessor] Rendering Layer '{layerName}' not found. (Project Settings > Tags and Layers > Rendering Layers)", this);
            return 0;
        }

        return 1u << idx;
    }

    private void ApplyDungeonRenderingLayer(GameObject spawned)
    {
        if (!applyDungeonLayerOnSpawn || spawned == null || _dungeonRenderingLayerMask == 0)
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
