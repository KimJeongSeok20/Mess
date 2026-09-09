using UnityEngine;
using PurrNet;
using OccaSoftware.Altos.Runtime;
using System;
using System.Collections.Generic;
using PurrNet.Modules;
using System.Linq;

/// <summary>
/// Synchronizes game time and day reset state for the Sky Director.
/// Clients advance their own sky time; the server broadcasts start/stop/reset state.
/// </summary>
public class TimeManager : NetworkBehaviour
{
    [Header("Time Settings")]
    [SerializeField, Min(0.01f)] private float realMinutesFromNineToMidnight = 10f;
    [SerializeField] private float checkInterval = 1f;
    [SerializeField] private float forwardDuration = 5f;

    [Header("Daily Scaling")]
    [SerializeField, Min(0f)] private float dailyIncreaseRate = 0.05f;

    [Header("Day End Seating")]
    [SerializeField, Range(0f, 24f)] private float dayEndAvailableHour = 16f;
    [SerializeField, Min(0.01f)] private float seatedDayEndDuration = 12f;
    [SerializeField, Min(0f)] private float lyingDownHoldDuration = 0.5f;

    [Header("References")]
    [SerializeField] private AltosSkyDirector skyDirector;
    [SerializeField] private NetworkDungeonController dungeonController;

    private readonly SyncVar<float> syncedTime = new(DayStartHour);
    private readonly SyncVar<bool> isClockRunning = new(false);
    private readonly SyncVar<bool> dungeonPreparing = new(false);
    private Coroutine _pendingClockStart;
    public bool IsPreparingDungeon => dungeonPreparing.value;
    private readonly SyncVar<int> sleepVoteCount = new(0);
    private readonly SyncVar<int> totalPlayers = new(0);
    private readonly SyncVar<int> currentDay = new(1);

    public static event System.Action OnDayReset;
    /// <summary>
    /// Raised on every peer when the whole run is restarted from day one (for example after a tax eviction).
    /// The string is a short reason for logs/UI. <see cref="OnDayReset"/> is raised right after it.
    /// </summary>
    public static event System.Action<string> OnRunRestarted;
    /// <summary>
    /// Raised on every peer when the synchronized day number changes (clients receive this after
    /// their local day-change animation, once the server's SyncVar arrives). Use it for HUD text
    /// that depends on <see cref="CurrentDay"/>.
    /// </summary>
    public static event System.Action<int> OnDayChanged;
    public static event System.Action OnAllPlayersSeated;
    public static event System.Action<int, int> OnSleepVoteChanged;
    public static event System.Action<bool> OnVoteStatusChanged;
    public static event System.Action<int, bool> OnLocalSeatReservationResult;

    private bool _isForwardingTime;
    private float _forwardStartTime;
    private float _forwardTimer;
    private float _activeForwardDuration;

    private float _checkTimer;
    private float _playerCountUpdateTimer;
    private const float DayStartHour = 9f;
    private const float DayEndHour = 24f;
    private const float PlayerCountUpdateInterval = 1f;

    private HashSet<PlayerID> _votedPlayers = new();
    private readonly Dictionary<PlayerID, int> _reservedSeatByPlayer = new();
    private readonly Dictionary<int, PlayerID> _seatOccupant = new();
    private PlayersManager _playersManager;
    private static TimeManager _activeInstance;

    public static TimeManager Active => _activeInstance;
    public static int CurrentDay => _activeInstance != null ? _activeInstance.GetCurrentDay() : 1;
    public static float CurrentDailyScalingMultiplier => _activeInstance != null ? _activeInstance.GetDailyScalingMultiplier() : 1f;
    public float DayEndAvailableHour => dayEndAvailableHour;
    public bool IsSafeMorningCheckpoint => isSpawned && isServer && !_isForwardingTime
        && !isClockRunning.value && Mathf.Abs(GetCurrentTime() - DayStartHour) < 0.01f
        && (dungeonController == null || !dungeonController.IsDungeonActive);

    /// <summary>Restore a saved morning without replaying day rewards, penalties or resets.</summary>
    public bool RestoreMorningCheckpoint(int day)
    {
        if (!IsSafeMorningCheckpoint || day < 1 || skyDirector == null || skyDirector.skyDefinition == null) return false;
        currentDay.value = day;
        syncedTime.value = DayStartHour;
        skyDirector.skyDefinition.dayNightCycleDuration = 0f;
        skyDirector.skyDefinition.SetSystemTime(DayStartHour);
        return true;
    }

    public static int ScaleDailyBudget(int baseBudget)
    {
        if (baseBudget <= 0) return 0;
        return Mathf.Max(1, Mathf.RoundToInt(baseBudget * CurrentDailyScalingMultiplier));
    }

    private void Awake()
    {
        _activeInstance = this;

        isClockRunning.onChanged += OnClockRunningChanged;
        sleepVoteCount.onChanged += OnSleepVoteCountChanged;
        currentDay.onChanged += OnCurrentDayChanged;

        if (skyDirector == null)
            skyDirector = AltosSkyDirector.Instance;

        if (skyDirector == null)
        {
            Debug.LogError("[TimeManager] AltosSkyDirector not found!");
        }
    }

    protected override void OnSpawned()
    {
        base.OnSpawned();

        if (NetworkManager.main != null)
        {
            NetworkManager.main.TryGetModule<PlayersManager>(isServer, out _playersManager);
        }

        if (isServer)
        {
            Debug.Log("[TimeManager] Server spawned - initializing time system");
            InitializeServerTime();

            totalPlayers.value = 0;
            _votedPlayers.Clear();
            _reservedSeatByPlayer.Clear();
            _seatOccupant.Clear();
        }
        else
        {
            Debug.Log("[TimeManager] Client spawned - syncing initial time");
            if (skyDirector != null && skyDirector.skyDefinition != null)
            {
                skyDirector.skyDefinition.dayNightCycleDuration = 0f;
                skyDirector.skyDefinition.SetSystemTime(syncedTime.value);
                Debug.Log($"[TimeManager] Client initial time set to {syncedTime.value:F1}");
            }
        }
    }

    private void Update()
    {
        if (isServer)
        {
            if (isClockRunning.value)
            {
                Check24HourLimit();
            }

            UpdatePlayerCountFromPlayersModule();
        }

        if (_isForwardingTime)
        {
            ForwardTimeAnimation();
        }
    }

    private void UpdatePlayerCountFromPlayersModule()
    {
        if (_playersManager == null)
        {
            if (NetworkManager.main != null)
            {
                NetworkManager.main.TryGetModule<PlayersManager>(isServer, out _playersManager);
            }
            return;
        }

        _playerCountUpdateTimer += Time.deltaTime;
        if (_playerCountUpdateTimer >= PlayerCountUpdateInterval)
        {
            _playerCountUpdateTimer = 0f;

            var currentPlayers = _playersManager.players;
            var eligible = currentPlayers.Where(IsLivingParticipant).ToList();
            int currentPlayerCount = eligible.Count;

            // Clean dead/disconnected occupants even when the total count happens to stay equal.
            foreach (var occupant in _reservedSeatByPlayer.Keys.Where(id => !eligible.Contains(id)).ToArray())
                ReleaseSeat(occupant);

            totalPlayers.value = currentPlayerCount;
            sleepVoteCount.value = _votedPlayers.Count;
            TryCompleteLivingSeatVote();
        }
    }

    private void InitializeServerTime()
    {
        if (skyDirector == null || skyDirector.skyDefinition == null)
        {
            Debug.LogError("[TimeManager] Cannot initialize - Sky Director or Sky Definition is null!");
            return;
        }

        skyDirector.skyDefinition.dayNightCycleDuration = 0f;

        skyDirector.skyDefinition.SetSystemTime(DayStartHour);
        syncedTime.value = DayStartHour;
        currentDay.value = 1;

        Debug.Log("[TimeManager] Server time initialized to 9:00");
    }

    private void Check24HourLimit()
    {
        _checkTimer += Time.deltaTime;

        if (_checkTimer >= checkInterval)
        {
            _checkTimer = 0f;

            if (skyDirector != null && skyDirector.skyDefinition != null)
            {
                float currentTime = skyDirector.skyDefinition.timeSystem;

                if (currentTime >= DayEndHour)
                {
                    Debug.Log("[TimeManager] Reached 24:00 - stopping clock");
                    StopClockRpc();
                    if (isServer && autoEndDayAtMidnight && _midnightRoutine == null)
                        _midnightRoutine = StartCoroutine(MidnightCurfewRoutine());
                }
            }
        }
    }

    private void OnClockRunningChanged(bool newValue)
    {
        if (skyDirector == null || skyDirector.skyDefinition == null)
            return;

        if (newValue)
        {
            skyDirector.skyDefinition.dayNightCycleDuration = GetGameDayDurationHours();
            Debug.Log($"[TimeManager] Clock started on {(isServer ? "Server" : "Client")} day={GetCurrentDay()} realMinutesFromNineToMidnight={realMinutesFromNineToMidnight:0.##}");
        }
        else
        {
            skyDirector.skyDefinition.dayNightCycleDuration = 0f;
            Debug.Log($"[TimeManager] Clock stopped on {(isServer ? "Server" : "Client")}");
        }
    }

    private void OnSleepVoteCountChanged(int newValue)
    {
        OnSleepVoteChanged?.Invoke(newValue, totalPlayers.value);
        Debug.Log($"[TimeManager] Sleep votes updated: {newValue}/{totalPlayers.value}");
    }

    private void OnCurrentDayChanged(int newValue)
    {
        OnDayChanged?.Invoke(Mathf.Max(1, newValue));
    }

    [ServerRpc(requireOwnership: false)]
    public void StartTimeClockRpc()
    {
        var checkpoint = RunSaveService.Instance;
        if (checkpoint != null && checkpoint.IsSoloCheckpointSession && !checkpoint.IsReady)
            return;
        if (isClockRunning.value || _pendingClockStart != null)
        {
            Debug.Log("[TimeManager] Clock is already running!");
            return;
        }

        if (skyDirector == null || skyDirector.skyDefinition == null)
        {
            Debug.LogError("[TimeManager] Sky Director or Sky Definition is null!");
            return;
        }

        if (dungeonController != null)
        {
            dungeonPreparing.value = true;
            dungeonController.StartDungeonServer();
            _pendingClockStart = StartCoroutine(StartClockAfterDungeonReady());
        }
        else isClockRunning.value = true;
    }

    private System.Collections.IEnumerator StartClockAfterDungeonReady()
    {
        yield return null;
        float deadline = Time.realtimeSinceStartup + 240f;
        Debug.Log("[TimeManager] Waiting for every connected player to prepare the dungeon; clock and battery are paused.");
        while (dungeonController != null && dungeonController.IsDungeonActive && !_isForwardingTime)
        {
            if (!string.IsNullOrEmpty(dungeonController.MapLoadError) || Time.realtimeSinceStartup > deadline)
            {
                Debug.LogError("[TimeManager] Dungeon preparation failed or timed out; the day clock was not started.");
                dungeonController.ClearDungeonServer();
                break;
            }
            if (_playersManager != null && dungeonController.ArePlayersReady(_playersManager.players))
            {
                isClockRunning.value = true;
                Debug.Log("[TimeManager] All players ready; clock started.");
                break;
            }
            yield return null;
        }
        dungeonPreparing.value = false;
        _pendingClockStart = null;
    }

    [Header("Midnight Curfew")]
    [SerializeField, Tooltip("At 24:00 the company closes the shutters: after the grace period the day ends on its own.")]
    private bool autoEndDayAtMidnight = true;
    [SerializeField, Min(0f)] private float midnightGraceSeconds = 20f;
    private Coroutine _midnightRoutine;

    private System.Collections.IEnumerator MidnightCurfewRoutine()
    {
        CurfewNoticeObserversRpc(midnightGraceSeconds);
        yield return new WaitForSecondsRealtime(midnightGraceSeconds);
        _midnightRoutine = null;

        // Someone may have started the day end (bed vote) during the grace period.
        if (_isForwardingTime || isClockRunning.value)
            yield break;

        ServerForceEndDay("midnight curfew");
    }

    [ObserversRpc(runLocally: true)]
    private void CurfewNoticeObserversRpc(float seconds)
    {
        PromptPresenter.ShowPrompt($"MIDNIGHT CURFEW: the company closes the shutters in {Mathf.RoundToInt(seconds)} s");
    }

    [ObserversRpc]
    private void StopClockRpc()
    {
        if (isServer)
        {
            isClockRunning.value = false;
        }
    }

    /// <summary>
    /// Server-only: end the current day right now (team wipe with insurance, debug). Plays the
    /// normal day-change fade on every peer, clears the dungeon and advances the day counter.
    /// </summary>
    public void ServerForceEndDay(string reason)
    {
        if (isSpawned && !isServer)
            return;

        if (_isForwardingTime)
            return;

        Debug.Log($"[TimeManager] Forcing day end. reason={reason}");
        ResetTimeRpc(0f);
    }

    /// <summary>
    /// Server-only: abandon the current run and restart from day one at 09:00 on every peer.
    /// Clears the dungeon, the clock, and every seat/vote. Used by the tax machine on eviction
    /// and by offline test scenes that respawn with a time reset.
    /// </summary>
    public void ServerRestartFromDayOne(string reason)
    {
        if (isSpawned && !isServer)
        {
            Debug.LogWarning("[TimeManager] ServerRestartFromDayOne called on a client; ignored.");
            return;
        }

        Debug.Log($"[TimeManager] Restarting run from day 1. reason={reason}");

        if (dungeonController != null)
            dungeonController.ClearDungeonServer();

        // Stop a running day-change animation right now so it cannot bump currentDay after the reset.
        _isForwardingTime = false;
        isClockRunning.value = false;
        sleepVoteCount.value = 0;
        _votedPlayers.Clear();
        _reservedSeatByPlayer.Clear();
        _seatOccupant.Clear();
        currentDay.value = 1;
        syncedTime.value = DayStartHour;
        if (dungeonController != null)
            dungeonController.RestorePowerServer();

        if (isSpawned)
            ApplyRunRestartRpc(reason);
        else
            ApplyRunRestartLocal(reason);
    }

    // runLocally: the server applies the restart itself too, so a dedicated server (no local player
    // in the observer list) still resets its sky time and raises OnDayReset.
    [ObserversRpc(runLocally: true)]
    private void ApplyRunRestartRpc(string reason)
    {
        ApplyRunRestartLocal(reason);
    }

    private void ApplyRunRestartLocal(string reason)
    {
        _isForwardingTime = false;
        _checkTimer = 0f;

        StartMapReturnPoint.ReturnLocalPlayerFromDungeon(reason);

        if (skyDirector != null && skyDirector.skyDefinition != null)
        {
            skyDirector.skyDefinition.dayNightCycleDuration = 0f;
            skyDirector.skyDefinition.SetSystemTime(DayStartHour);
        }

        OnVoteStatusChanged?.Invoke(false);
        OnRunRestarted?.Invoke(reason);
        OnDayReset?.Invoke();
    }

    // ToggleSleepVoteRpc (legacy BedInteraction hold-to-vote) was removed: it trusted a client-sent
    // PlayerID and shared _votedPlayers with the seat flow, so counts drifted (audit M6/M7).
    // Day end now goes only through the DayEndSeatPlayer seat RPCs below.

    [ServerRpc(requireOwnership: false)]
    public void RequestSeatReservationRpc(int seatId, RPCInfo info = default)
    {
        PlayerID player = info.sender;

        if (!CanPlayerEndDay(player))
        {
            NotifySeatReservationResultRpc(player, seatId, false);
            return;
        }

        if (_reservedSeatByPlayer.TryGetValue(player, out int existingSeat))
        {
            NotifySeatReservationResultRpc(player, seatId, existingSeat == seatId);
            return;
        }

        if (_seatOccupant.ContainsKey(seatId))
        {
            NotifySeatReservationResultRpc(player, seatId, false);
            return;
        }

        _reservedSeatByPlayer.Add(player, seatId);
        _seatOccupant.Add(seatId, player);
        NotifySeatReservationResultRpc(player, seatId, true);
        Debug.Log($"[TimeManager] Player {player} reserved day-end seat {seatId}");
    }

    [ServerRpc(requireOwnership: false)]
    public void ConfirmSeatedRpc(int seatId, RPCInfo info = default)
    {
        PlayerID player = info.sender;
        if (!IsLivingParticipant(player)) return;
        if (!_reservedSeatByPlayer.TryGetValue(player, out int reservedSeat) || reservedSeat != seatId)
            return;

        if (!_votedPlayers.Add(player))
            return;

        sleepVoteCount.value = _votedPlayers.Count;
        NotifyVoteStatusRpc(player, true);
        Debug.Log($"[TimeManager] Player {player} finished sitting in seat {seatId}: {sleepVoteCount.value}/{totalPlayers.value}");

        TryCompleteLivingSeatVote();
    }

    private static bool IsLivingParticipant(PlayerID id)
    {
        var player = NetworkPlayer.FindPlayer(id);
        var vitals = player != null ? player.GetComponent<PlayerVitals>() : null;
        return vitals != null && !vitals.IsDead;
    }

    private void TryCompleteLivingSeatVote()
    {
        if (_isForwardingTime || _playersManager == null || GetCurrentTime() < dayEndAvailableHour) return;
        var living = _playersManager.players.Where(IsLivingParticipant).ToArray();
        if (living.Length > 0 && living.All(id => _votedPlayers.Contains(id)))
        {
            Debug.Log("[TimeManager] All players are seated - lying down and resetting time");
            float minimumForwardDuration = GetDayEndForwardDuration();
            NotifyAllPlayersSeatedRpc();
            ResetTimeRpc(minimumForwardDuration);
        }
    }

    private float GetDayEndForwardDuration()
    {
        DayEndSeatPlayer[] seatPlayers = FindObjectsByType<DayEndSeatPlayer>(FindObjectsInactive.Include);
        float longestLyingDownDuration = 0f;

        for (int i = 0; i < seatPlayers.Length; i++)
        {
            longestLyingDownDuration = Mathf.Max(
                longestLyingDownDuration,
                seatPlayers[i].LyingDownDuration);
        }

        return Mathf.Max(
            seatedDayEndDuration,
            longestLyingDownDuration + lyingDownHoldDuration);
    }

    [ObserversRpc]
    private void NotifyAllPlayersSeatedRpc()
    {
        OnAllPlayersSeated?.Invoke();
    }

    [ServerRpc(requireOwnership: false)]
    public void LeaveSeatRpc(int seatId, RPCInfo info = default)
    {
        PlayerID player = info.sender;
        if (!_reservedSeatByPlayer.TryGetValue(player, out int reservedSeat) || reservedSeat != seatId)
            return;

        ReleaseSeat(player);
        sleepVoteCount.value = _votedPlayers.Count;
        NotifyVoteStatusRpc(player, false);
        Debug.Log($"[TimeManager] Player {player} left day-end seat {seatId}: {sleepVoteCount.value}/{totalPlayers.value}");
    }

    [TargetRpc]
    private void NotifySeatReservationResultRpc(PlayerID target, int seatId, bool accepted)
    {
        OnLocalSeatReservationResult?.Invoke(seatId, accepted);
    }

    private bool CanPlayerEndDay(PlayerID player)
    {
        if (_isForwardingTime || GetCurrentTime() < dayEndAvailableHour)
            return false;

        return _playersManager != null && _playersManager.players.Contains(player) && IsLivingParticipant(player);
    }

    private void ReleaseSeat(PlayerID player)
    {
        if (_reservedSeatByPlayer.TryGetValue(player, out int seatId))
        {
            _reservedSeatByPlayer.Remove(player);
            _seatOccupant.Remove(seatId);
        }

        _votedPlayers.Remove(player);
    }

    [TargetRpc]
    private void NotifyVoteStatusRpc(PlayerID target, bool hasVoted)
    {
        OnVoteStatusChanged?.Invoke(hasVoted);
        Debug.Log($"[TimeManager] Vote status updated for local player: {hasVoted}");
    }

    [ObserversRpc]
    private void ResetTimeRpc(float minimumForwardDuration)
    {
        if (skyDirector == null || skyDirector.skyDefinition == null)
            return;

        if (isServer)
        {
            isClockRunning.value = false;
            sleepVoteCount.value = 0;
            _votedPlayers.Clear();
            _reservedSeatByPlayer.Clear();
            _seatOccupant.Clear();
        }

        // Anyone still inside must be out before the geometry disappears (audit M5).
        StartMapReturnPoint.ReturnLocalPlayerFromDungeon("day ended");

        if (dungeonController != null)
        {
            dungeonController.ClearDungeonServer();
        }

        OnVoteStatusChanged?.Invoke(false);

        float currentTime = skyDirector.skyDefinition.timeSystem;

        _isForwardingTime = true;
        _forwardTimer = 0f;
        _forwardStartTime = currentTime;
        _activeForwardDuration = Mathf.Max(0.01f, Mathf.Max(forwardDuration, minimumForwardDuration));

        skyDirector.skyDefinition.dayNightCycleDuration = 0f;

        Debug.Log($"[TimeManager] Starting time forward animation from {currentTime:F1} to 9:00 over {_activeForwardDuration:F2}s on {(isServer ? "Server" : "Client")}");
    }

    private void ForwardTimeAnimation()
    {
        _forwardTimer += Time.deltaTime;
        float progress = _forwardTimer / _activeForwardDuration;

        if (progress >= 1f)
        {
            _isForwardingTime = false;
            skyDirector.skyDefinition.SetSystemTime(DayStartHour);

            if (isServer)
            {
                syncedTime.value = DayStartHour;
                currentDay.value = Mathf.Max(1, currentDay.value + 1);
                if (dungeonController != null)
                    dungeonController.RestorePowerServer();
            }

            Debug.Log($"[TimeManager] Time forward animation completed on {(isServer ? "Server" : "Client")} day={GetCurrentDay()} scaling={GetDailyScalingMultiplier():0.###}");

            OnDayReset?.Invoke();
        }
        else
        {
            float currentTime;

            if (_forwardStartTime >= DayStartHour)
            {
                float totalHours = (DayEndHour - _forwardStartTime) + DayStartHour;
                float elapsedHours = totalHours * progress;
                currentTime = _forwardStartTime + elapsedHours;

                if (currentTime >= DayEndHour)
                {
                    currentTime -= DayEndHour - 1f;
                }
            }
            else
            {
                currentTime = DayStartHour;
                _isForwardingTime = false;
            }

            skyDirector.skyDefinition.SetSystemTime(currentTime);

            if (isServer)
            {
                syncedTime.value = currentTime;
            }
        }
    }

    public float GetCurrentTime()
    {
        if (skyDirector != null && skyDirector.skyDefinition != null)
        {
            return skyDirector.skyDefinition.timeSystem;
        }
        return syncedTime.value;
    }

    public int GetCurrentDay()
    {
        return Mathf.Max(1, currentDay.value);
    }

    public float GetDailyScalingMultiplier()
    {
        int elapsedDays = Mathf.Max(0, GetCurrentDay() - 1);
        return Mathf.Pow(1f + Mathf.Max(0f, dailyIncreaseRate), elapsedDays);
    }

    private float GetGameDayDurationHours()
    {
        float playableGameHours = Mathf.Max(0.01f, DayEndHour - DayStartHour);
        float fullCycleMinutes = Mathf.Max(0.01f, realMinutesFromNineToMidnight) * (24f / playableGameHours);
        return fullCycleMinutes / 60f;
    }

    public bool IsClockRunning()
    {
        return isClockRunning.value;
    }

    public (int current, int total) GetSleepVoteStatus()
    {
        return (sleepVoteCount.value, totalPlayers.value);
    }

    public bool IsForwardingTime()
    {
        return _isForwardingTime;
    }

    public bool CanBeginDayEnd()
    {
        return !_isForwardingTime && GetCurrentTime() >= dayEndAvailableHour;
    }

    protected override void OnDespawned()
    {
        base.OnDespawned();
        isClockRunning.onChanged -= OnClockRunningChanged;
        sleepVoteCount.onChanged -= OnSleepVoteCountChanged;
        currentDay.onChanged -= OnCurrentDayChanged;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        isClockRunning.onChanged -= OnClockRunningChanged;
        sleepVoteCount.onChanged -= OnSleepVoteCountChanged;
        currentDay.onChanged -= OnCurrentDayChanged;

        if (_activeInstance == this)
            _activeInstance = null;
    }
}
