using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using PurrLobby;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum SteamRoomState { Idle, Creating, Joining, InRoom, Starting, InGame, Error }

/// <summary>Steam App ID 480 rooms, scoped to MessUP and a compatible protocol/build.</summary>
[DefaultExecutionOrder(-900)]
[DisallowMultipleComponent]
public sealed class SteamRoomService : MonoBehaviour
{
    public const string GameMarker = "MessUP";
    public const string ProtocolMarker = "messup-steam-v1";
    public const int Capacity = 4;
    private const string GameKey = "messup_game", ProtocolKey = "messup_protocol", BuildKey = "messup_build";
    private const string CodeKey = "messup_code", HostKey = "messup_host", StateKey = "messup_state";
    private const string GameplayScene = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
    private const float OperationTimeout = 25f;
    [SerializeField] private SteamManager steamManager;
    [SerializeField] private LobbyDataHolder lobbyHolder;
    private CSteamID _lobby = CSteamID.Nil;
    private CSteamID _host = CSteamID.Nil;
    private CSteamID _joiningLobby = CSteamID.Nil;
    private int _operation;
    private float _deadline;
    private float _nextRefresh;
    private bool _loadingScene;
    private bool _gameSessionRequested;
    private bool _callbacksRegistered;
    private readonly List<IDisposable> _pendingCalls = new();
    private Callback<LobbyDataUpdate_t> _dataChanged;
    private Callback<LobbyChatUpdate_t> _membersChanged;
    private Callback<LobbyGameCreated_t> _gameCreated;

    public static SteamRoomService Instance { get; private set; }
    public SteamRoomState State { get; private set; } = SteamRoomState.Idle;
    public string Status { get; private set; } = "Create a Steam room or enter a 6-digit room number.";
    public string RoomCode { get; private set; } = string.Empty;
    public int MemberCount { get; private set; }
    public bool IsOwner { get; private set; }
    public bool IsGameSession => _gameSessionRequested;
    public bool IsBusy => State == SteamRoomState.Creating || State == SteamRoomState.Joining || State == SteamRoomState.Starting;
    public event Action Changed;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Configure(SteamManager runtime, LobbyDataHolder holder) { steamManager = runtime; lobbyHolder = holder; }

    public static bool IsValidRoomCode(string code)
    {
        if (code == null || code.Length != 6) return false;
        foreach (char digit in code) if (digit < '0' || digit > '9') return false;
        return true;
    }

    public static bool MatchesRoom(string game, string protocol, string build, string code, string requestedCode, string expectedBuild)
        => game == GameMarker && protocol == ProtocolMarker && build == expectedBuild
            && IsValidRoomCode(code) && code == requestedCode;

    public void CreateRoom()
    {
        if (IsBusy || _lobby.IsValid() || _loadingScene) return;
        if (!PrepareSteam()) return;
        int operation = Begin(SteamRoomState.Creating, "Creating Steam room...");
        var result = CallResult<LobbyCreated_t>.Create();
        _pendingCalls.Add(result);
        result.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, Capacity), (created, failed) =>
        {
            _pendingCalls.Remove(result);
            result.Dispose();
            CSteamID lobby = new(created.m_ulSteamIDLobby);
            if (operation != _operation)
            { if (!failed && created.m_eResult == EResult.k_EResultOK) SteamMatchmaking.LeaveLobby(lobby); return; }
            if (failed || created.m_eResult != EResult.k_EResultOK)
            { Fail("Steam could not create the room: " + created.m_eResult); return; }
            _lobby = lobby;
            _host = SteamUser.GetSteamID();
            if (!SetData(GameKey, GameMarker) || !SetData(ProtocolKey, ProtocolMarker) || !SetData(BuildKey, Application.version)
                || !SetData(HostKey, _host.m_SteamID.ToString(CultureInfo.InvariantCulture)) || !SetData(StateKey, "allocating"))
            { Fail("Steam could not publish the room details."); return; }
            AssignRoomCode(operation, 0);
        });
    }

    private void AssignRoomCode(int operation, int attempt)
    {
        if (operation != _operation) return;
        if (attempt >= 5) { Fail("Room numbers are busy. Please create a room again."); return; }
        string code = UnityEngine.Random.Range(100000, 1000000).ToString(CultureInfo.InvariantCulture);
        if (!SetData(CodeKey, code)) { Fail("Steam could not assign a room number."); return; }
        Search(code, operation, matches =>
        {
            if (matches.Exists(id => id != _lobby)) { AssignRoomCode(operation, attempt + 1); return; }
            if (!SetData(StateKey, "open")) { Fail("Steam could not open the room."); return; }
            RoomCode = code;
            RefreshLobby();
            SetState(SteamRoomState.InRoom, "Share this room number. The host starts the game when everyone has joined.");
        });
    }

    public void JoinRoom(string input)
    {
        if (IsBusy || _lobby.IsValid() || _loadingScene) return;
        string code = input?.Trim();
        if (!IsValidRoomCode(code)) { SetState(SteamRoomState.Error, "Enter exactly 6 digits for the room number."); return; }
        if (!PrepareSteam()) return;
        int operation = Begin(SteamRoomState.Joining, "Finding Steam room " + code + "...");
        Search(code, operation, matches =>
        {
            if (matches.Count == 0) { Fail("Room not found. Check the number; the room may be full, closed, or use a different build."); return; }
            if (matches.Count != 1) { Fail("That number matches multiple rooms. Ask the host to create a new room."); return; }
            CSteamID lobby = matches[0];
            _joiningLobby = lobby;
            if (SteamMatchmaking.GetLobbyData(lobby, StateKey) != "open") { Fail("This room is not ready to join. Try again shortly."); return; }
            var result = CallResult<LobbyEnter_t>.Create();
            _pendingCalls.Add(result);
            _deadline = Time.realtimeSinceStartup + OperationTimeout;
            result.Set(SteamMatchmaking.JoinLobby(lobby), (entered, failed) =>
            {
                _pendingCalls.Remove(result);
                result.Dispose();
                bool success = !failed && entered.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess;
                if (operation != _operation)
                {
                    CSteamID joined = new(entered.m_ulSteamIDLobby);
                    if (success && joined != _lobby && joined != _joiningLobby) SteamMatchmaking.LeaveLobby(joined);
                    return;
                }
                if (!success) { Fail("Steam could not join the room: " + (EChatRoomEnterResponse)entered.m_EChatRoomEnterResponse); return; }
                _lobby = new CSteamID(entered.m_ulSteamIDLobby);
                _joiningLobby = CSteamID.Nil;
                string roomState = SteamMatchmaking.GetLobbyData(_lobby, StateKey);
                if (!IsMatchingLobby(_lobby, code) || (roomState != "open" && roomState != "starting")
                    || !ulong.TryParse(SteamMatchmaking.GetLobbyData(_lobby, HostKey), out ulong host))
                { Fail("The room changed while joining. Please try again."); return; }
                _host = new CSteamID(host);
                RoomCode = code;
                if (!RefreshLobby()) return;
                SetState(SteamRoomState.InRoom, "Joined. Waiting for the host to start the game.");
                if (roomState == "starting" && SteamMatchmaking.GetLobbyGameServer(_lobby, out _, out _, out CSteamID server) && server == _host)
                    BeginGameplay();
            });
        });
    }

    private void Search(string code, int operation, Action<List<CSteamID>> complete)
    {
        SteamMatchmaking.AddRequestLobbyListStringFilter(GameKey, GameMarker, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter(ProtocolKey, ProtocolMarker, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter(BuildKey, Application.version, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter(CodeKey, code, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
        SteamMatchmaking.AddRequestLobbyListResultCountFilter(10);
        var result = CallResult<LobbyMatchList_t>.Create();
        _pendingCalls.Add(result);
        _deadline = Time.realtimeSinceStartup + OperationTimeout;
        result.Set(SteamMatchmaking.RequestLobbyList(), (list, failed) =>
        {
            _pendingCalls.Remove(result);
            result.Dispose();
            if (operation != _operation) return;
            if (failed) { Fail("Steam room search failed. Check your Steam connection and retry."); return; }
            var matches = new List<CSteamID>();
            for (int i = 0; i < list.m_nLobbiesMatching; i++)
            {
                var lobby = SteamMatchmaking.GetLobbyByIndex(i);
                if (IsMatchingLobby(lobby, code)) matches.Add(lobby);
            }
            complete(matches);
        });
    }

    public void StartGame()
    {
        if (State != SteamRoomState.InRoom || _loadingScene || !PrepareSteam() || !RefreshLobby()) return;
        if (!IsOwner) { Status = "Only the host can start the game."; Changed?.Invoke(); return; }
        if (!Application.CanStreamedLevelBeLoaded(GameplayScene)) { Fail("The gameplay scene is missing from this build."); return; }
        if (!SteamMatchmaking.SetLobbyJoinable(_lobby, false) || !SetData(StateKey, "starting"))
        { Fail("Steam could not start the room."); return; }
        Begin(SteamRoomState.Starting, "Starting game...");
        SteamMatchmaking.SetLobbyGameServer(_lobby, 0, 0, _host);
        BeginGameplay();
    }

    private void OnGameCreated(LobbyGameCreated_t created)
    {
        if (_lobby.m_SteamID != created.m_ulSteamIDLobby || _loadingScene || State != SteamRoomState.InRoom) return;
        if (created.m_ulSteamIDGameServer != _host.m_SteamID) { Fail("The room host changed. Create or join a new room."); return; }
        BeginGameplay();
    }

    private void BeginGameplay()
    {
        if (_loadingScene || !RefreshLobby()) return;
        if (!Application.CanStreamedLevelBeLoaded(GameplayScene)) { Fail("The gameplay scene is missing from this build."); return; }
        RunSaveService.BeginMultiplayerGame();
        _gameSessionRequested = true;
        _loadingScene = true;
        Begin(SteamRoomState.Starting, "Loading game...");
        StartCoroutine(LoadGameplay());
    }

    private IEnumerator LoadGameplay()
    {
        AsyncOperation loading = null;
        string error = null;
        try { loading = SceneManager.LoadSceneAsync(GameplayScene); }
        catch (Exception exception) { error = exception.Message; }
        if (loading == null) { _loadingScene = false; Fail("Unable to load gameplay. " + error); yield break; }
        while (!loading.isDone) yield return null;
        _loadingScene = false;
        if (_lobby.IsValid() && State != SteamRoomState.Error)
            SetState(SteamRoomState.InGame, "Steam room connected. Waiting for the game connection...");
    }

    public void LeaveRoom()
    {
        if (_loadingScene) return;
        _gameSessionRequested = false;
        ClearLobby();
        SetState(SteamRoomState.Idle, "Left the room. Your solo checkpoint was kept.");
    }

    private void ClearLobby()
    {
        _operation++;
        if (_lobby.IsValid() && steamManager != null && steamManager.Initialized) SteamMatchmaking.LeaveLobby(_lobby);
        _lobby = CSteamID.Nil;
        _host = CSteamID.Nil;
        _joiningLobby = CSteamID.Nil;
        RoomCode = string.Empty;
        MemberCount = 0;
        IsOwner = false;
        if (lobbyHolder != null) lobbyHolder.SetCurrentLobby(default);
    }

    private bool PrepareSteam()
    {
        if (steamManager == null || lobbyHolder == null) { SetState(SteamRoomState.Error, "Steam room components are not configured."); return false; }
        if (!steamManager.Initialize(out string error)) { SetState(SteamRoomState.Error, error); return false; }
        if (!_callbacksRegistered)
        {
            _callbacksRegistered = true;
            _dataChanged = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataChanged);
            _membersChanged = Callback<LobbyChatUpdate_t>.Create(update => { if (update.m_ulSteamIDLobby == _lobby.m_SteamID) RefreshLobby(); });
            _gameCreated = Callback<LobbyGameCreated_t>.Create(OnGameCreated);
        }
        return true;
    }

    private void OnLobbyDataChanged(LobbyDataUpdate_t update)
    {
        if (update.m_ulSteamIDLobby != _lobby.m_SteamID || update.m_bSuccess == 0) return;
        if (!RefreshLobby()) return;
        // Metadata recovery also handles a game-created callback that preceded our join result.
        if (State == SteamRoomState.InRoom && SteamMatchmaking.GetLobbyData(_lobby, StateKey) == "starting"
            && SteamMatchmaking.GetLobbyGameServer(_lobby, out _, out _, out CSteamID server) && server == _host)
            BeginGameplay();
    }

    private bool RefreshLobby()
    {
        if (!_lobby.IsValid() || steamManager == null || !steamManager.Initialized) return false;
        if (SteamMatchmaking.GetLobbyOwner(_lobby) != _host) { Fail("The host left the room. Please create or join a new room."); return false; }
        var members = new List<LobbyUser>();
        int count = SteamMatchmaking.GetNumLobbyMembers(_lobby);
        bool containsLocal = false;
        for (int i = 0; i < count; i++)
        {
            CSteamID id = SteamMatchmaking.GetLobbyMemberByIndex(_lobby, i);
            containsLocal |= id == SteamUser.GetSteamID();
            members.Add(new LobbyUser { Id = id.m_SteamID.ToString(CultureInfo.InvariantCulture), DisplayName = SteamFriends.GetFriendPersonaName(id) });
        }
        if (!containsLocal || count > Capacity) { Fail("You are no longer in a valid Steam room."); return false; }
        MemberCount = count;
        IsOwner = _host == SteamUser.GetSteamID();
        lobbyHolder.SetCurrentLobby(LobbyFactory.Create(GameMarker + " " + RoomCode, _lobby.m_SteamID.ToString(CultureInfo.InvariantCulture),
            RoomCode, Capacity, IsOwner, members, new Dictionary<string, string> { { GameKey, GameMarker }, { ProtocolKey, ProtocolMarker } }));
        Changed?.Invoke();
        return true;
    }

    private bool IsMatchingLobby(CSteamID lobby, string code) => MatchesRoom(SteamMatchmaking.GetLobbyData(lobby, GameKey),
        SteamMatchmaking.GetLobbyData(lobby, ProtocolKey), SteamMatchmaking.GetLobbyData(lobby, BuildKey),
        SteamMatchmaking.GetLobbyData(lobby, CodeKey), code, Application.version);
    private bool SetData(string key, string value) => SteamMatchmaking.SetLobbyData(_lobby, key, value);
    private int Begin(SteamRoomState state, string status)
    { _deadline = Time.realtimeSinceStartup + OperationTimeout; SetState(state, status); return ++_operation; }
    private void SetState(SteamRoomState state, string status) { State = state; Status = status; Changed?.Invoke(); }
    private void Fail(string error) { ClearLobby(); SetState(SteamRoomState.Error, error); }

    private void Update()
    {
        if (IsBusy && !_loadingScene && Time.realtimeSinceStartup > _deadline) { Fail("Steam room request timed out. Please try again."); return; }
        if (_lobby.IsValid() && steamManager != null && steamManager.Initialized && Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + 1f;
            if (!SteamUser.BLoggedOn()) { Fail("Steam disconnected. Sign in and join a new room."); return; }
            RefreshLobby();
        }
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        _operation++;
        if (_lobby.IsValid() && steamManager != null && steamManager.Initialized) SteamMatchmaking.LeaveLobby(_lobby);
        _dataChanged?.Dispose(); _membersChanged?.Dispose(); _gameCreated?.Dispose();
        foreach (var call in _pendingCalls) call.Dispose();
        _pendingCalls.Clear();
        Instance = null;
    }
}
