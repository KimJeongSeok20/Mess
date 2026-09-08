using UnityEngine;
using UnityEngine.Rendering;
using PurrNet;
using DunGen;
using System.Collections;
using System.Collections.Generic;

public class NetworkDungeonController : NetworkBehaviour
{
    [Header("DunGen")]
    [SerializeField] private RuntimeDungeon runtimeDungeon;

    [Header("Runtime NavMesh")]
    [SerializeField] private DungeonRuntimeNavMeshPipeline runtimeNavMeshPipeline;

    [Header("Dynamic Probe Lighting")]
    [SerializeField] private DungeonTileProbeRegistry tileProbeRegistry;

    [Header("Map List (DungeonFlows)")]
    [SerializeField] private DungeonMapList mapList;

    private readonly SyncVar<int> _seed = new SyncVar<int>(0);
    private readonly SyncVar<int> _flowIndex = new SyncVar<int>(-1);
    private readonly SyncVar<bool> _active = new SyncVar<bool>(false);
    private readonly SyncVar<string> _flowContentId = new SyncVar<string>(string.Empty);
    private Coroutine _mapLoadRoutine;
    private bool _destroying;
    private readonly HashSet<PlayerID> _readyPlayers = new();
    private int _reportedReadySeed;
    private int _reportedReadyFlow = -1;
    public string MapLoadError { get; private set; }
    private readonly SyncVar<string> _debugRoomPowerStatePayload = new SyncVar<string>(string.Empty);
    private readonly SyncVar<string> _dungeonDoorStatePayload = new SyncVar<string>(string.Empty);

    private void OnSeedChanged(int _) => TryGenerateIfReady();
    private void OnFlowChanged(int _) => TryGenerateIfReady();
    private void OnFlowContentChanged(string _) => TryGenerateIfReady();

    // 중복 Generate 방지용
    private bool _generated;
    private int _generatedSeed;
    private int _generatedFlowIndex;

    [Header("Player Rendering Layer")]
    [SerializeField] private bool applyDungeonRenderingLayerToPlayer = true;
    [SerializeField] private string playerDungeonRenderingLayerName = "Dungeon";
    private uint _playerDungeonRenderingLayerMask;
    private uint _playerDefaultRenderingLayerMask;

    [Header("Generation Diagnostics")]
    [SerializeField] private bool enableGenerationDiagnostics = true;
    [SerializeField] private bool logGenerationStatusTransitions = true;
    [SerializeField] private bool logGenerationStepTimesOnComplete = true;
    [SerializeField] private bool forceAsyncGeneration = false;
    [SerializeField, Min(1f)] private float targetMaxAsyncFrameMilliseconds = 4f;
    [SerializeField, Min(0.5f)] private float generationStallWarningSeconds = 8f;

    [Header("Debug Room Power")]
    [SerializeField, Min(0f)] private float debugRoomPowerHorizontalPadding = 1f;
    [SerializeField, Min(0f)] private float debugRoomPowerMaxResolveDistance = 12f;
    [SerializeField] private bool logDebugRoomPowerToggle = true;

    [Header("Dungeon Door State")]
    [SerializeField] private bool logDungeonDoorStateChanges;

    [Header("Power Grid (battery)")]
    [Tooltip("던전 배터리 기본 용량. 방 하나를 켜 두면 초당 drainPerLitRoomPerSecond씩 준다.")]
    [SerializeField, Min(10f)] private float basePowerCapacity = 240f;
    [Tooltip("켜진 방 하나당 초당 소모 전력. 방을 더 켜면 그만큼 더 빨리 준다.")]
    [SerializeField, Min(0f)] private float drainPerLitRoomPerSecond = 1f;
    [Tooltip("배터리 소모품(Jerrycan) 하나로 회복되는 전력")]
    [SerializeField, Min(1f)] private float rechargeAmountPerItem = 120f;
    [Tooltip("전력 SyncVar 전송 간격(초)")]
    [SerializeField, Min(0.1f)] private float powerSyncInterval = 0.5f;

    private readonly SyncVar<float> _power = new SyncVar<float>(0f);
    private readonly SyncVar<float> _powerCapacity = new SyncVar<float>(240f);
    private float _serverPower;
    private float _nextPowerSync;

    public float Power => _power.value;
    public float PowerCapacity => Mathf.Max(1f, _powerCapacity.value);
    public float PowerNormalized => Mathf.Clamp01(Power / PowerCapacity);
    public bool HasPower => Power > 0.01f;
    public int LitRoomCount => CountLitRooms();
    public float RechargeAmountPerItem => rechargeAmountPerItem;

    private void Update()
    {
        if (!_active.value) return;
        if (isClient && IsLocalDungeonReady &&
            (_reportedReadySeed != _seed.value || _reportedReadyFlow != _flowIndex.value))
        {
            _reportedReadySeed = _seed.value;
            _reportedReadyFlow = _flowIndex.value;
            ReportMapReadyServerRpc(_seed.value, _flowIndex.value, _flowContentId.value);
        }
        if (!isServer || (TimeManager.Active != null && TimeManager.Active.IsPreparingDungeon)) return;

        TickPowerServer(Time.deltaTime);
    }

    [ServerRpc(requireOwnership: false)]
    private void ReportMapReadyServerRpc(int seed, int flowIndex, string contentId, RPCInfo info = default)
    {
        if (!_active.value || seed != _seed.value || flowIndex != _flowIndex.value || contentId != _flowContentId.value) return;
        _readyPlayers.Add(info.sender);
    }

    public bool ArePlayersReady(IEnumerable<PlayerID> participants)
    {
        if (!isServer || !IsLocalDungeonReady || participants == null) return false;
        foreach (var participant in participants)
            if (!_readyPlayers.Contains(participant)) return false;
        return true;
    }

    private void TickPowerServer(float deltaTime)
    {
        float capacity = ResolveTeamPowerCapacity();
        if (!Mathf.Approximately(capacity, _powerCapacity.value))
        {
            _powerCapacity.value = capacity;
            _serverPower = Mathf.Min(_serverPower, capacity);
        }

        int lit = CountLitRooms();
        if (lit > 0 && _serverPower > 0f)
        {
            _serverPower = Mathf.Max(0f, _serverPower - lit * drainPerLitRoomPerSecond * deltaTime);
            if (_serverPower <= 0f)
            {
                Debug.Log("[NetworkDungeonController] Battery depleted: forcing every room to P0.", this);
                ForceAllRoomsDarkServer();
            }
        }

        if (Time.time >= _nextPowerSync)
        {
            _nextPowerSync = Time.time + powerSyncInterval;
            if (!Mathf.Approximately(_power.value, _serverPower))
                _power.value = _serverPower;
        }
    }

    /// <summary>Base capacity plus every connected player's Battery Cell perks (team pool).</summary>
    private float ResolveTeamPowerCapacity()
    {
        float bonus = 0f;
        PlayerVitals[] players = FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && players[i].isSpawned)
                bonus += players[i].ServerBatteryBonus;
        }

        return basePowerCapacity + bonus;
    }

    private int CountLitRooms()
    {
        int lit = 0;
        foreach (var pair in _debugRoomPowerStates)
        {
            if (pair.Value.Level == DungeonTileLightmapSwitcher.PowerLevel.P100)
                lit++;
        }

        return lit;
    }

    private void ForceAllRoomsDarkServer()
    {
        if (!isServer)
            return;

        foreach (var pair in _debugRoomPowerStates)
        {
            if (TryFindGeneratedTileNearCenter(pair.Value.Center, out Tile tile))
                ApplyRoomPowerLocally(tile, DungeonTileLightmapSwitcher.PowerLevel.P0);
        }

        _debugRoomPowerStates.Clear();
        _debugRoomPowerStatePayload.value = string.Empty;
    }

    /// <summary>Owner client used a battery item; the server tops the grid up.</summary>
    [ServerRpc(requireOwnership: false)]
    public void RequestRechargeServerRpc(string token, RPCInfo info = default)
    {
        if (!isServer || !_active.value)
            return;

        var player = NetworkPlayer.FindPlayer(info.sender);
        if (player == null || player.GetComponent<PlayerVitals>() == null || player.GetComponent<PlayerVitals>().IsDead
            || !player.ServerInventory.TryGet(token, out var battery) || battery.itemName != "Jerrycan") return;
        float capacity = ResolveTeamPowerCapacity();
        if (_serverPower >= capacity || !player.ServerInventory.Consume(token, out _)) return;
        _serverPower = Mathf.Min(capacity, _serverPower + rechargeAmountPerItem);
        _power.value = _serverPower;
        ContractEvents.Report(ContractGoal.BatteryRecharges, 1);
        player.CompleteItemUse(token, $"Battery recharged +{rechargeAmountPerItem:0}");
        Debug.Log($"[NetworkDungeonController] {info.sender} recharged the grid: {_serverPower:0}/{capacity:0}", this);
    }

    private bool _generatorHooksRegistered;
    private LightmapData[] _campLightmaps;
    private LightmapsMode _campLightmapsMode;
    private Coroutine _generationWatchdogCoroutine;
    private GenerationStatus _lastGenerationStatus = GenerationStatus.NotStarted;
    private float _generationStartedRealtime;
    private float _lastStatusChangedRealtime;
    private int _generationRunId;
    private readonly Dictionary<string, RoomPowerState> _debugRoomPowerStates = new Dictionary<string, RoomPowerState>();
    private readonly HashSet<string> _openDungeonDoorKeys = new HashSet<string>();

    public bool IsDungeonActive => _active.value;

    /// <summary>
    /// True once THIS peer has finished generating the synced dungeon. The server flips
    /// <see cref="IsDungeonActive"/> the moment generation starts, but each client generates
    /// locally from the seed, so the entrance must not teleport anyone before this is true.
    /// </summary>
    public bool IsLocalDungeonReady =>
        _active.value
        && _generated
        && _generatedSeed == _seed.value
        && _generatedFlowIndex == _flowIndex.value
        && string.IsNullOrEmpty(MapLoadError)
        && runtimeDungeon != null
        && runtimeDungeon.Generator != null
        && runtimeDungeon.Generator.Status == GenerationStatus.Complete
        // Wait for the runtime NavMesh bake to finish, but do not require validation to pass:
        // a rejected bake still yields a walkable dungeon; only monster binding is affected.
        && (runtimeNavMeshPipeline == null || runtimeNavMeshPipeline.IsRunFinished);
    public int CurrentFlowIndex => _flowIndex.value;
    public int CurrentSeed => _seed.value;
    public DungeonMapList MapList => mapList;

    protected override void OnSpawned()
    {
        base.OnSpawned();

        _playerDungeonRenderingLayerMask = ResolveRenderingLayerMask(playerDungeonRenderingLayerName);
        _playerDefaultRenderingLayerMask = ResolveRenderingLayerMask("Default");

        _seed.onChanged += OnSeedChanged;
        _flowIndex.onChanged += OnFlowChanged;
        _flowContentId.onChanged += OnFlowContentChanged;
        _active.onChanged += OnActiveChanged;
        _debugRoomPowerStatePayload.onChanged += OnDebugRoomPowerStatePayloadChanged;
        _dungeonDoorStatePayload.onChanged += OnDungeonDoorStatePayloadChanged;
        RegisterGeneratorHooks();
        if (tileProbeRegistry != null)
            tileProbeRegistry.Configure(runtimeDungeon);

        Debug.Log($"[NetworkDungeonController] spawned server={isServer} active={_active.value} seed={_seed.value} flow={_flowIndex.value}", this);

        // 늦게 들어온 클라도 현재 상태 반영
        if (_active.value) TryGenerateIfReady();
        else ClearLocalGenerated();
        if (mapList != null && mapList.UsesDeferredLoading && mapList.Count == 1)
            StartCoroutine(WarmSingleMapAfterCampReady());
    }

    protected override void OnDestroy()
    {
        _destroying = true;
        if (mapList != null) mapList.ReleaseLoadedFlow();
        _seed.onChanged -= OnSeedChanged;
        _flowIndex.onChanged -= OnFlowChanged;
        _flowContentId.onChanged -= OnFlowContentChanged;
        _active.onChanged -= OnActiveChanged;
        _debugRoomPowerStatePayload.onChanged -= OnDebugRoomPowerStatePayloadChanged;
        _dungeonDoorStatePayload.onChanged -= OnDungeonDoorStatePayloadChanged;
        UnregisterGeneratorHooks();
        StopGenerationWatchdog();
        if (runtimeNavMeshPipeline != null)
            runtimeNavMeshPipeline.CancelCurrentGeneration("NetworkDungeonController destroyed");

        base.OnDestroy();
    }

    private void OnActiveChanged(bool isActive)
    {
        if (!isActive)
        {
            _reportedReadySeed = 0;
            _reportedReadyFlow = -1;
            _generated = false;
            ClearDebugRoomPowerState();
            _openDungeonDoorKeys.Clear();
            ClearLocalGenerated();
            StopGenerationWatchdog();
            return;
        }

        TryGenerateIfReady();
    }

    private void TryGenerateIfReady()
    {
        if (!_active.value)
        {
            Debug.Log($"[NetworkDungeonController] generate skipped: inactive (server={isServer})", this);
            return;
        }

        if (_seed.value == 0)
        {
            Debug.Log($"[NetworkDungeonController] generate skipped: seed not synced yet (server={isServer})", this);
            return;
        }

        if (mapList == null || _flowIndex.value < 0 || _flowIndex.value >= mapList.Count)
        {
            MapLoadError = "The selected dungeon is not in this build's map catalog.";
            return;
        }
        if (string.IsNullOrEmpty(_flowContentId.value)) return; // Wait for the complete network selection.
        if (_flowContentId.value != mapList.GetContentId(_flowIndex.value))
        {
            MapLoadError = "Dungeon catalogs differ between players. Use the same game build.";
            Debug.LogError("[DungeonContent] " + MapLoadError, this);
            return;
        }

        // Assets must be ready on this peer before any dungeon generation/spawning starts.
        if (!ApplyFlowLocal(_flowIndex.value))
        {
            if (_mapLoadRoutine == null)
                _mapLoadRoutine = StartCoroutine(LoadRequestedMaps());
            return;
        }

        // 같은 조합이면 중복 생성 방지
        if (_generated && _generatedSeed == _seed.value && _generatedFlowIndex == _flowIndex.value)
            return;

        Debug.Log($"[NetworkDungeonController] generating locally seed={_seed.value} flow={_flowIndex.value} server={isServer}", this);
        GenerateLocal(_seed.value);

        _generated = true;
        _generatedSeed = _seed.value;
        _generatedFlowIndex = _flowIndex.value;
        ApplyRenderingLayerToLocalPlayer(_playerDungeonRenderingLayerMask);
    }

    private bool ApplyFlowLocal(int index)
    {
        if (runtimeDungeon == null) return false;
        if (mapList == null || mapList.Count == 0) return false;
        if (!mapList.TryGetFlow(index, out var flow)) return false;

        runtimeDungeon.Generator.DungeonFlow = flow; 
        return runtimeDungeon.Generator.DungeonFlow != null;
    }

    private IEnumerator WarmSingleMapAfterCampReady()
    {
        while (!_destroying && (NetworkPlayer.Local == null || GameMenuController.IsOpen)) yield return null;
        yield return new WaitForSecondsRealtime(1f);
        // With multiple maps the selected index is not yet known. Never preload the whole catalog.
        if (!_destroying && !_active.value && !mapList.TryGetFlow(0, out _) && _mapLoadRoutine == null)
            _mapLoadRoutine = StartCoroutine(LoadRequestedMaps());
    }

    private IEnumerator LoadRequestedMaps()
    {
        yield return null; // Assign the coroutine handle before any immediate error/completion.
        MapLoadError = null;
        while (!_destroying)
        {
            int index = _active.value ? _flowIndex.value : mapList.Count == 1 ? 0 : -1;
            if (index < 0 || index >= mapList.Count) break;
            if (mapList.LoadedFlowIndex >= 0 && mapList.LoadedFlowIndex != index)
            {
                // Release the old graph before collecting; existing inventory/network items keep their own references.
                ClearLocalGenerated();
                runtimeDungeon.Generator.DungeonFlow = null;
                mapList.ReleaseLoadedFlow();
                yield return null;
                yield return Resources.UnloadUnusedAssets();
            }
            float started = Time.realtimeSinceStartup;
            Debug.Log($"[DungeonContent] begin index={index} id={mapList.GetContentId(index)}");
            yield return mapList.LoadFlowAsync(index);
            if (!string.IsNullOrEmpty(mapList.LoadError))
            {
                MapLoadError = mapList.LoadError;
                Debug.LogError("[DungeonContent] " + MapLoadError, this);
                break;
            }
            Debug.Log($"[DungeonContent] ready index={index} seconds={Time.realtimeSinceStartup - started:F3}");
            // A clear, changed seed or late-join update may arrive while the disk request is in flight.
            if (_active.value && _flowIndex.value != index) continue;
            if (_active.value) TryGenerateIfReady();
            break;
        }
        _mapLoadRoutine = null;
    }

    public void StartDungeonServer()
    {
        if (!isServer) return;
        if (_active.value) return;

        int flowIndex = (mapList != null && mapList.Count > 0) ? Random.Range(0, mapList.Count) : -1;
        if (flowIndex < 0)
        {
            MapLoadError = "No dungeon maps are registered.";
            Debug.LogError("[DungeonContent] " + MapLoadError, this);
            return;
        }
        int seed = Random.Range(int.MinValue, int.MaxValue);
        if (seed == 0) seed = 1;
        MapLoadError = null;
        _readyPlayers.Clear();
        ClearDungeonDoorStateServer();

        // 새 던전 = 배터리 가득
        _serverPower = ResolveTeamPowerCapacity();
        _powerCapacity.value = _serverPower;
        _power.value = _serverPower;

        // ✅ 핵심: prerequisites 먼저, active는 마지막에
        _flowIndex.value = flowIndex;
        _flowContentId.value = mapList.GetContentId(flowIndex);
        _seed.value = seed;
        _active.value = true;

        // 호스트(서버 로컬) 즉시 생성
        TryGenerateIfReady();
    }

    public void ClearDungeonServer()
    {
        if (!isServer) return;

        _readyPlayers.Clear();
        ClearDungeonDoorStateServer();
        _active.value = false;
        _seed.value = 0;
        _flowIndex.value = -1;
        _flowContentId.value = string.Empty;

        // 서버 로컬 즉시 정리
        ClearLocalGenerated();
    }

    private void GenerateLocal(int seed)
    {
        if (runtimeDungeon == null) return;

        var gen = runtimeDungeon.Generator;

        if (forceAsyncGeneration)
        {
            gen.GenerateAsynchronously = true;
            gen.MaxAsyncFrameMilliseconds = targetMaxAsyncFrameMilliseconds;
        }

        if (runtimeNavMeshPipeline != null)
            runtimeNavMeshPipeline.BeginGeneration(seed, _flowIndex.value);

        gen.Clear(stopCoroutines: true);
        ClearDebugRoomPowerState();

        gen.ShouldRandomizeSeed = false;
        gen.Seed = seed;

        if (enableGenerationDiagnostics)
            BeginGenerationDiagnostics(gen, seed);

        if (!gen.IsGenerating)
            runtimeDungeon.Generate();
    }

    private void ClearLocalGenerated()
    {
        if (runtimeDungeon == null) return;
        if (runtimeNavMeshPipeline != null)
            runtimeNavMeshPipeline.CancelCurrentGeneration("Dungeon cleared");

        runtimeDungeon.Generator.Clear(stopCoroutines: true);
        StopGenerationWatchdog();
        ApplyRenderingLayerToLocalPlayer(_playerDefaultRenderingLayerMask);
    }

    public bool RequestDungeonDoorState(Door door, bool open)
    {
        if (door == null)
            return false;

        string key = door.StableNetworkKey;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (!isSpawned)
        {
            door.ApplyNetworkDoorState(open);
            return true;
        }

        if (isServer)
            SetDungeonDoorStateServer(key, open);
        else
            RequestDungeonDoorStateServerRpc(key, open);

        return true;
    }

    [ServerRpc(requireOwnership: false)]
    private void RequestDungeonDoorStateServerRpc(string key, bool open)
    {
        SetDungeonDoorStateServer(key, open);
    }

    private void SetDungeonDoorStateServer(string key, bool open)
    {
        if (!isServer || string.IsNullOrWhiteSpace(key))
            return;

        if (!TryFindDungeonDoor(key, out Door door))
        {
            Debug.LogWarning($"[NetworkDungeonController] Rejected unknown dungeon door key: {key}", this);
            return;
        }

        if (open)
            _openDungeonDoorKeys.Add(key);
        else
            _openDungeonDoorKeys.Remove(key);

        door.ApplyNetworkDoorState(open);
        _dungeonDoorStatePayload.value = BuildDungeonDoorStatePayload();

        if (logDungeonDoorStateChanges)
            Debug.Log($"[NetworkDungeonController] Dungeon door state key={key} open={open}", door);
    }

    private void OnDungeonDoorStatePayloadChanged(string payload)
    {
        ApplyDungeonDoorStatePayload(payload);
    }

    private void ClearDungeonDoorStateServer()
    {
        _openDungeonDoorKeys.Clear();
        if (isServer && !string.IsNullOrEmpty(_dungeonDoorStatePayload.value))
            _dungeonDoorStatePayload.value = string.Empty;
    }

    private string BuildDungeonDoorStatePayload()
    {
        if (_openDungeonDoorKeys.Count == 0)
            return string.Empty;

        var keys = new List<string>(_openDungeonDoorKeys);
        keys.Sort(System.StringComparer.Ordinal);
        return string.Join("\n", keys);
    }

    private void ApplyDungeonDoorStatePayload(string payload)
    {
        _openDungeonDoorKeys.Clear();
        if (!string.IsNullOrWhiteSpace(payload))
        {
            string[] keys = payload.Split('\n');
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i].Trim();
                if (!string.IsNullOrWhiteSpace(key))
                    _openDungeonDoorKeys.Add(key);
            }
        }

        Transform root = CurrentDungeonRoot();
        if (root == null)
            return;

        Door[] doors = root.GetComponentsInChildren<Door>(true);
        for (int i = 0; i < doors.Length; i++)
        {
            Door door = doors[i];
            if (door != null)
                door.ApplyNetworkDoorState(_openDungeonDoorKeys.Contains(door.StableNetworkKey));
        }
    }

    private bool TryFindDungeonDoor(string key, out Door door)
    {
        door = null;
        Transform root = CurrentDungeonRoot();
        if (root == null)
            return false;

        Door[] doors = root.GetComponentsInChildren<Door>(true);
        for (int i = 0; i < doors.Length; i++)
        {
            Door candidate = doors[i];
            if (candidate == null || !string.Equals(candidate.StableNetworkKey, key, System.StringComparison.Ordinal))
                continue;

            door = candidate;
            return true;
        }

        return false;
    }

    private Transform CurrentDungeonRoot()
    {
        if (runtimeDungeon == null || runtimeDungeon.Generator == null)
            return null;

        if (runtimeDungeon.Generator.CurrentDungeon != null)
            return runtimeDungeon.Generator.CurrentDungeon.transform;

        return runtimeDungeon.Generator.Root != null ? runtimeDungeon.Generator.Root.transform : null;
    }

    public string DebugToggleRoomPowerAtLocalPlayer()
    {
        if (!TryGetLocalPlayerPosition(out Vector3 worldPosition))
            return "[NetworkDungeonController] Local player position not found.";

        return DebugToggleRoomPowerAtPosition(worldPosition);
    }

    public bool TryGetRoomPowerAtPosition(
        Vector3 worldPosition,
        out DungeonTileLightmapSwitcher.PowerLevel level,
        out string roomName)
    {
        level = DungeonTileLightmapSwitcher.PowerLevel.P0;
        roomName = string.Empty;

        if (!TryFindGeneratedTileAtPosition(worldPosition, out Tile tile) || !HasPowerToggleSwitcher(tile))
            return false;

        level = ResolveTilePowerLevel(tile);
        roomName = tile.name;
        return true;
    }

    public string DebugToggleRoomPowerAtPosition(Vector3 worldPosition)
    {
        if (!isServer)
        {
            RequestToggleRoomPowerServerRpc(worldPosition);
            return $"[NetworkDungeonController] Sent room power toggle request at {worldPosition}.";
        }

        return ToggleRoomPowerServer(worldPosition);
    }

    [ServerRpc(requireOwnership: false)]
    private void RequestToggleRoomPowerServerRpc(Vector3 worldPosition)
    {
        ToggleRoomPowerServer(worldPosition);
    }

    private string ToggleRoomPowerServer(Vector3 worldPosition)
    {
        if (!TryFindGeneratedTileAtPosition(worldPosition, out Tile tile))
        {
            string fail = $"[NetworkDungeonController] No generated tile found near {worldPosition}.";
            if (logDebugRoomPowerToggle)
                Debug.LogWarning(fail, this);
            return fail;
        }

        if (!HasPowerToggleSwitcher(tile))
        {
            string fixedState =
                $"[NetworkDungeonController] room={tile.name} uses a single baked lighting state; toggle ignored.";
            if (logDebugRoomPowerToggle)
                Debug.Log(fixedState, this);
            return fixedState;
        }

        DungeonTileLightmapSwitcher.PowerLevel current = ResolveTilePowerLevel(tile);
        DungeonTileLightmapSwitcher.PowerLevel next = current == DungeonTileLightmapSwitcher.PowerLevel.P0
            ? DungeonTileLightmapSwitcher.PowerLevel.P100
            : DungeonTileLightmapSwitcher.PowerLevel.P0;

        // Turning a room on costs battery; an empty grid refuses.
        if (next == DungeonTileLightmapSwitcher.PowerLevel.P100 && _serverPower <= 0.01f)
        {
            string noPower = $"[NetworkDungeonController] room={tile.name} cannot power up: battery empty.";
            if (logDebugRoomPowerToggle)
                Debug.Log(noPower, this);
            return noPower;
        }

        int switcherCount = ApplyRoomPowerLocally(tile, next);
        Vector3 center = ResolveTileCenter(tile);
        UpsertDebugRoomPowerState(center, next);

        string result =
            $"[NetworkDungeonController] toggled room={tile.name} center={center} " +
            $"{current}->{next} switchers={switcherCount}";

        if (logDebugRoomPowerToggle)
            Debug.Log(result, this);

        return result;
    }

    private void OnDebugRoomPowerStatePayloadChanged(string payload)
    {
        // An empty payload means "everything dark" (battery depleted / cleared), so clients
        // must actively reset their rooms instead of keeping the last lit state.
        if (string.IsNullOrWhiteSpace(payload))
        {
            _debugRoomPowerStates.Clear();
            ApplyGeneratedRoomsToPower0();
            return;
        }

        ApplyDebugRoomPowerStatePayload(payload);
    }

    private void ClearDebugRoomPowerState()
    {
        _debugRoomPowerStates.Clear();
        if (isServer && !string.IsNullOrEmpty(_debugRoomPowerStatePayload.value))
            _debugRoomPowerStatePayload.value = string.Empty;
    }

    private void UpsertDebugRoomPowerState(Vector3 tileCenter, DungeonTileLightmapSwitcher.PowerLevel level)
    {
        if (!isServer)
            return;

        string key = BuildRoomPowerKey(tileCenter);
        _debugRoomPowerStates[key] = new RoomPowerState
        {
            Center = tileCenter,
            Level = level
        };

        _debugRoomPowerStatePayload.value = BuildDebugRoomPowerStatePayload();
    }

    private string BuildDebugRoomPowerStatePayload()
    {
        if (_debugRoomPowerStates.Count == 0)
            return string.Empty;

        var payload = new System.Text.StringBuilder();
        foreach (var pair in _debugRoomPowerStates)
        {
            if (payload.Length > 0)
                payload.Append(';');

            payload.Append(pair.Key);
            payload.Append(':');
            payload.Append((int)pair.Value.Level);
        }

        return payload.ToString();
    }

    private void ApplyDebugRoomPowerStatePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return;

        string[] entries = payload.Split(';');
        for (int i = 0; i < entries.Length; i++)
        {
            string entry = entries[i];
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            string[] parts = entry.Split(':');
            if (parts.Length != 2)
                continue;

            if (!TryParseRoomPowerKey(parts[0], out Vector3 tileCenter))
                continue;

            if (!int.TryParse(parts[1], out int rawLevel))
                continue;

            DungeonTileLightmapSwitcher.PowerLevel level = ToPowerLevel(rawLevel);
            if (TryFindGeneratedTileNearCenter(tileCenter, out Tile tile))
                ApplyRoomPowerLocally(tile, level);
        }
    }

    private int ApplyRoomPowerLocally(Tile tile, DungeonTileLightmapSwitcher.PowerLevel level)
    {
        if (tile == null)
            return 0;

        DungeonTileLightmapSwitcher[] switchers = tile.GetComponentsInChildren<DungeonTileLightmapSwitcher>(true);
        int applied = 0;
        bool foundAssignedBakeData = false;

        for (int i = 0; i < switchers.Length; i++)
        {
            DungeonTileLightmapSwitcher switcher = switchers[i];
            if (switcher != null && switcher.HasAssignedBakeData)
            {
                foundAssignedBakeData = true;
                break;
            }
        }

        for (int i = 0; i < switchers.Length; i++)
        {
            DungeonTileLightmapSwitcher switcher = switchers[i];
            if (switcher == null)
                continue;

            if (!switcher.SupportsPowerToggle)
                continue;

            if (foundAssignedBakeData && !switcher.HasAssignedBakeData)
                continue;

            switcher.SetPowerLevel(level);
            applied++;
        }

        DungeonTileLightSwitch[] lightSwitches = tile.GetComponentsInChildren<DungeonTileLightSwitch>(true);
        for (int i = 0; i < lightSwitches.Length; i++)
        {
            DungeonTileLightSwitch lightSwitch = lightSwitches[i];
            if (lightSwitch != null)
                lightSwitch.ApplyExternalPowerLevel(level, false);
        }

        return applied;
    }

    private void ApplyGeneratedRoomsToPower0()
    {
        var generator = runtimeDungeon != null ? runtimeDungeon.Generator : null;
        var dungeon = generator != null ? generator.CurrentDungeon : null;
        var tiles = dungeon != null ? dungeon.AllTiles : null;
        if (tiles == null || tiles.Count == 0)
            return;

        for (int i = 0; i < tiles.Count; i++)
        {
            Tile tile = tiles[i];
            if (!HasPowerToggleSwitcher(tile))
                continue;

            ApplyRoomPowerLocally(tile, DungeonTileLightmapSwitcher.PowerLevel.P0);
        }
    }

    private static bool HasPowerToggleSwitcher(Tile tile)
    {
        if (tile == null)
            return false;

        DungeonTileLightmapSwitcher[] switchers =
            tile.GetComponentsInChildren<DungeonTileLightmapSwitcher>(true);
        for (int i = 0; i < switchers.Length; i++)
        {
            DungeonTileLightmapSwitcher switcher = switchers[i];
            if (switcher != null && switcher.SupportsPowerToggle && switcher.HasAssignedBakeData)
                return true;
        }

        return false;
    }

    private DungeonTileLightmapSwitcher.PowerLevel ResolveTilePowerLevel(Tile tile)
    {
        if (tile == null)
            return DungeonTileLightmapSwitcher.PowerLevel.P100;

        DungeonTileLightmapSwitcher[] switchers = tile.GetComponentsInChildren<DungeonTileLightmapSwitcher>(true);
        for (int i = 0; i < switchers.Length; i++)
        {
            DungeonTileLightmapSwitcher switcher = switchers[i];
            if (switcher != null && switcher.HasAssignedBakeData)
                return switcher.CurrentPowerLevel;
        }

        for (int i = 0; i < switchers.Length; i++)
        {
            if (switchers[i] != null)
                return switchers[i].CurrentPowerLevel;
        }

        return DungeonTileLightmapSwitcher.PowerLevel.P100;
    }

    private bool TryFindGeneratedTileAtPosition(Vector3 worldPosition, out Tile tile)
    {
        tile = null;

        var generator = runtimeDungeon != null ? runtimeDungeon.Generator : null;
        var dungeon = generator != null ? generator.CurrentDungeon : null;
        var tiles = dungeon != null ? dungeon.AllTiles : null;
        if (tiles == null || tiles.Count == 0)
            return false;

        float bestScore = float.PositiveInfinity;
        float bestOutsideDistance = float.PositiveInfinity;
        Tile bestTile = null;

        for (int i = 0; i < tiles.Count; i++)
        {
            Tile candidate = tiles[i];
            if (candidate == null)
                continue;

            Bounds bounds = ResolveTileBounds(candidate);
            float outsideDistance = CalculateHorizontalOutsideDistance(bounds, worldPosition, debugRoomPowerHorizontalPadding);
            float centerDistance = HorizontalDistanceSqr(bounds.center, worldPosition);
            float score = outsideDistance <= 0.0001f ? centerDistance * 0.001f : outsideDistance * outsideDistance + centerDistance * 0.0001f;

            if (score >= bestScore)
                continue;

            bestScore = score;
            bestOutsideDistance = outsideDistance;
            bestTile = candidate;
        }

        if (bestTile == null)
            return false;

        if (bestOutsideDistance > debugRoomPowerMaxResolveDistance)
            return false;

        tile = bestTile;
        return true;
    }

    private bool TryFindGeneratedTileNearCenter(Vector3 tileCenter, out Tile tile)
    {
        tile = null;

        var generator = runtimeDungeon != null ? runtimeDungeon.Generator : null;
        var dungeon = generator != null ? generator.CurrentDungeon : null;
        var tiles = dungeon != null ? dungeon.AllTiles : null;
        if (tiles == null || tiles.Count == 0)
            return false;

        float bestDistance = float.PositiveInfinity;
        Tile bestTile = null;
        for (int i = 0; i < tiles.Count; i++)
        {
            Tile candidate = tiles[i];
            if (candidate == null)
                continue;

            float distance = HorizontalDistanceSqr(ResolveTileCenter(candidate), tileCenter);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestTile = candidate;
        }

        tile = bestTile;
        return tile != null;
    }

    private static DungeonTileLightmapSwitcher.PowerLevel ToPowerLevel(int rawLevel)
    {
        return rawLevel == (int)DungeonTileLightmapSwitcher.PowerLevel.P0
            ? DungeonTileLightmapSwitcher.PowerLevel.P0
            : DungeonTileLightmapSwitcher.PowerLevel.P100;
    }

    private static Bounds ResolveTileBounds(Tile tile)
    {
        Bounds bounds = tile.Bounds;
        if (bounds.size.sqrMagnitude > 0.0001f)
            return bounds;

        Renderer[] renderers = tile.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds ? bounds : new Bounds(tile.transform.position, Vector3.one);
    }

    private static Vector3 ResolveTileCenter(Tile tile)
    {
        return ResolveTileBounds(tile).center;
    }

    private static float CalculateHorizontalOutsideDistance(Bounds bounds, Vector3 point, float padding)
    {
        float minX = bounds.min.x - padding;
        float maxX = bounds.max.x + padding;
        float minZ = bounds.min.z - padding;
        float maxZ = bounds.max.z + padding;

        float dx = point.x < minX ? minX - point.x : point.x > maxX ? point.x - maxX : 0f;
        float dz = point.z < minZ ? minZ - point.z : point.z > maxZ ? point.z - maxZ : 0f;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static float HorizontalDistanceSqr(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private static string BuildRoomPowerKey(Vector3 tileCenter)
    {
        int x = Mathf.RoundToInt(tileCenter.x * 10f);
        int y = Mathf.RoundToInt(tileCenter.y * 10f);
        int z = Mathf.RoundToInt(tileCenter.z * 10f);
        return $"{x},{y},{z}";
    }

    private static bool TryParseRoomPowerKey(string key, out Vector3 tileCenter)
    {
        tileCenter = default;

        if (string.IsNullOrWhiteSpace(key))
            return false;

        string[] parts = key.Split(',');
        if (parts.Length != 3)
            return false;

        if (!int.TryParse(parts[0], out int x) ||
            !int.TryParse(parts[1], out int y) ||
            !int.TryParse(parts[2], out int z))
        {
            return false;
        }

        tileCenter = new Vector3(x / 10f, y / 10f, z / 10f);
        return true;
    }

    private static bool TryGetLocalPlayerPosition(out Vector3 position)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            Transform root = mainCamera.transform.root;
            position = root != null ? root.position : mainCamera.transform.position;
            return true;
        }

        position = default;
        return false;
    }

    private uint ResolveRenderingLayerMask(string layerName)
    {
        int idx = RenderingLayerMask.NameToRenderingLayer(layerName);
        if (idx < 0)
        {
            Debug.LogWarning($"[NetworkDungeonController] Rendering Layer '{layerName}' not found.", this);
            return 0;
        }
        return 1u << idx;
    }

    private void ApplyRenderingLayerToLocalPlayer(uint mask)
    {
        if (!applyDungeonRenderingLayerToPlayer || mask == 0) return;
        
        // Find local player camera → get root transform for the player hierarchy
        if (Camera.main == null) return;
        Transform playerRoot = Camera.main.transform.root;
        if (playerRoot == null) return;
        
        var renderers = playerRoot.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r != null)
                r.renderingLayerMask = mask;
        }
    }

    public bool TryGetLoot(out SpawnSelector selector, out int budget)
    {
        selector = null;
        budget = 0;
        if (mapList == null) return false;
        return mapList.TryGetLoot(CurrentFlowIndex, out selector, out budget);
    }

    private void RegisterGeneratorHooks()
    {
        if (_generatorHooksRegistered || runtimeDungeon == null)
            return;

        var gen = runtimeDungeon.Generator;
        // This scene has one active dungeon. Preserve the camp's lightmap indices before
        // generated tiles append their rotation/power maps to Unity's global table.
        _campLightmaps = LightmapSettings.lightmaps;
        _campLightmapsMode = LightmapSettings.lightmapsMode;
        gen.Cleared += RestoreCampLightmaps;
        gen.OnGenerationStarted += OnGeneratorStarted;
        gen.OnGenerationStatusChanged += OnGeneratorStatusChanged;
        gen.OnGenerationComplete += OnGeneratorComplete;
        gen.Retrying += OnGeneratorRetrying;
        _generatorHooksRegistered = true;
    }

    private void UnregisterGeneratorHooks()
    {
        if (!_generatorHooksRegistered || runtimeDungeon == null)
            return;

        var gen = runtimeDungeon.Generator;
        gen.Cleared -= RestoreCampLightmaps;
        gen.OnGenerationStarted -= OnGeneratorStarted;
        gen.OnGenerationStatusChanged -= OnGeneratorStatusChanged;
        gen.OnGenerationComplete -= OnGeneratorComplete;
        gen.Retrying -= OnGeneratorRetrying;
        _generatorHooksRegistered = false;
    }

    private void RestoreCampLightmaps()
    {
        if (_campLightmaps == null) return;
        LightmapSettings.lightmaps = _campLightmaps;
        LightmapSettings.lightmapsMode = _campLightmapsMode;
    }

    private void BeginGenerationDiagnostics(DunGen.DungeonGenerator gen, int seed)
    {
        _generationRunId++;
        _generationStartedRealtime = Time.realtimeSinceStartup;
        _lastStatusChangedRealtime = _generationStartedRealtime;
        _lastGenerationStatus = gen.Status;

        Debug.Log(
            $"[DungeonGenDiag] run={_generationRunId} start seed={seed} flowIndex={_flowIndex.value} " +
            $"async={gen.GenerateAsynchronously} asyncBudgetMs={gen.MaxAsyncFrameMilliseconds:0.##} " +
            $"maxAttempts={gen.MaxAttemptCount} lengthMultiplier={gen.LengthMultiplier:0.##}",
            this);

        StopGenerationWatchdog();
        _generationWatchdogCoroutine = StartCoroutine(GenerationWatchdog(gen, _generationRunId));
    }

    private IEnumerator GenerationWatchdog(DunGen.DungeonGenerator gen, int runId)
    {
        var wait = new WaitForSecondsRealtime(1f);

        while (gen != null && gen.IsGenerating && runId == _generationRunId)
        {
            float now = Time.realtimeSinceStartup;
            float stalledFor = now - _lastStatusChangedRealtime;

            if (stalledFor >= generationStallWarningSeconds)
            {
                float total = now - _generationStartedRealtime;
                Debug.LogWarning(
                    $"[DungeonGenDiag] run={runId} stall status={gen.Status} stalledFor={stalledFor:0.00}s total={total:0.00}s",
                    this);

                _lastStatusChangedRealtime = now;
            }

            yield return wait;
        }
    }

    private void StopGenerationWatchdog()
    {
        if (_generationWatchdogCoroutine == null)
            return;

        StopCoroutine(_generationWatchdogCoroutine);
        _generationWatchdogCoroutine = null;
    }

    private void OnGeneratorStarted(DunGen.DungeonGenerator gen)
    {
        if (!enableGenerationDiagnostics)
            return;

        _lastStatusChangedRealtime = Time.realtimeSinceStartup;
        Debug.Log($"[DungeonGenDiag] run={_generationRunId} generator-start status={gen.Status}", this);
    }

    private void OnGeneratorRetrying()
    {
        if (!enableGenerationDiagnostics || runtimeDungeon == null)
            return;

        var gen = runtimeDungeon.Generator;
        int retries = gen.GenerationStats != null ? gen.GenerationStats.TotalRetries : -1;
        Debug.Log($"[DungeonGenDiag] run={_generationRunId} retry totalRetries={retries}", this);
    }

    private void OnGeneratorStatusChanged(DunGen.DungeonGenerator gen, GenerationStatus status)
    {
        if (!enableGenerationDiagnostics)
            return;

        float now = Time.realtimeSinceStartup;
        float totalMs = (now - _generationStartedRealtime) * 1000f;
        float phaseMs = (now - _lastStatusChangedRealtime) * 1000f;

        if (logGenerationStatusTransitions)
        {
            Debug.Log(
                $"[DungeonGenDiag] run={_generationRunId} status {_lastGenerationStatus} -> {status} " +
                $"phaseMs={phaseMs:0.0} totalMs={totalMs:0.0}",
                this);
        }

        _lastGenerationStatus = status;
        _lastStatusChangedRealtime = now;

        if (status == GenerationStatus.Failed)
        {
            StopGenerationWatchdog();
            Debug.LogWarning($"[DungeonGenDiag] run={_generationRunId} generation failed after {totalMs:0.0}ms", this);
        }
    }

    private void OnGeneratorComplete(DunGen.DungeonGenerator gen)
    {
        StopGenerationWatchdog();
        Debug.Log($"[NetworkDungeonController] generation complete server={isServer} seed={_seed.value} rooms={(gen.CurrentDungeon != null ? gen.CurrentDungeon.AllTiles.Count : -1)}", this);

        if (runtimeNavMeshPipeline != null)
            runtimeNavMeshPipeline.RunAfterGeneration(gen, _seed.value, _flowIndex.value);

        ApplyGeneratedRoomsToPower0();
        ApplyDebugRoomPowerStatePayload(_debugRoomPowerStatePayload.value);
        ApplyDungeonDoorStatePayload(_dungeonDoorStatePayload.value);

        if (!enableGenerationDiagnostics)
            return;

        float totalMs = (Time.realtimeSinceStartup - _generationStartedRealtime) * 1000f;
        var stats = gen.GenerationStats;
        int retries = stats != null ? stats.TotalRetries : -1;
        int rooms = stats != null ? stats.TotalRoomCount : -1;
        int branchRooms = stats != null ? stats.BranchPathRoomCount : -1;

        Debug.Log(
            $"[DungeonGenDiag] run={_generationRunId} complete totalMs={totalMs:0.0} rooms={rooms} branchRooms={branchRooms} retries={retries}",
            this);

        if (!logGenerationStepTimesOnComplete || stats == null)
            return;

        foreach (var pair in stats.GenerationStepTimes)
            Debug.Log($"[DungeonGenDiag] run={_generationRunId} step={pair.Key} ms={pair.Value:0.0}", this);
    }

    private struct RoomPowerState
    {
        public Vector3 Center;
        public DungeonTileLightmapSwitcher.PowerLevel Level;
    }
}
