using System;
using System.Collections;
using System.IO;
using System.Linq;
using Esper.SkillWeb.UI.UGUI;
using PurrNet;
using UnityEngine;

/// <summary>Local solo morning checkpoints. World drops and active dungeon state are not part of a checkpoint.</summary>
[DisallowMultipleComponent]
public sealed class RunSaveService : MonoBehaviour
{
    [SerializeField] private TimeManager timeManager;
    [SerializeField] private CurrencyManager currencyManager;
    [SerializeField] private TaxCollectionMachine taxMachine;
    [SerializeField] private DailyContractBoard contractBoard;
    [SerializeField] private InventoryManager inventoryManager;
    [SerializeField] private InventoryManagerExtensions inventoryExpansion;
    [SerializeField] private SkillWebTerminalInteraction skillTerminal;
    private enum StartRequest { None, New, Continue }
    private static StartRequest _request;
    private static RunSaveData _prepared;
    private bool _hadRemotePlayers;
    private bool _managedSession;
    private bool _initialized;
    private Coroutine _saveRoutine;

    public static RunSaveService Instance { get; private set; }
    public static bool IsSoloStartRequested => _request != StartRequest.None;
    public static string SavePath => Path.Combine(Application.persistentDataPath, "solo-morning-checkpoint.json");
    public static bool HasSave => RunSaveStorage.TryRead(SavePath, out _, out _);
    public static string SaveSummary => RunSaveStorage.TryRead(SavePath, out var save, out _)
        ? $"Day {save.day} · 09:00 · ${save.currency:N0}" : "No morning checkpoint";
    public string LastError { get; private set; }
    public bool IsReady { get; private set; }
    // This identifies a menu-started session for startup input gating. Joining peers invalidate
    // future saves, but must not unlock gameplay while a checkpoint is still being applied.
    public bool IsSoloCheckpointSession => _managedSession;

    public static void BeginNewGame()
    {
        // Keep the existing file until a new, fully initialized checkpoint replaces it atomically.
        _prepared = null;
        _request = StartRequest.New;
        TeamProgress.ResetAll();
    }

    public static bool TryPrepareContinue(out string error)
    {
        if (!RunSaveStorage.TryRead(SavePath, out var save, out error)) return false;
        _prepared = save;
        _request = StartRequest.Continue;
        return true;
    }

    public static void BeginMultiplayerGame()
    {
        _prepared = null;
        _request = StartRequest.None;
        TeamProgress.ResetAll();
    }

    public void ConfigureReferences(TimeManager time, CurrencyManager currency, TaxCollectionMachine tax,
        DailyContractBoard contracts, InventoryManager inventory, InventoryManagerExtensions expansion,
        SkillWebTerminalInteraction terminal)
    {
        timeManager = time; currencyManager = currency; taxMachine = tax;
        contractBoard = contracts; inventoryManager = inventory; inventoryExpansion = expansion;
        skillTerminal = terminal;
    }

    private void Awake() => Instance = this;
    private void OnEnable() => TimeManager.OnDayReset += QueueMorningSave;
    private void OnDisable() => TimeManager.OnDayReset -= QueueMorningSave;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private IEnumerator Start()
    {
        StartRequest request = _request;
        RunSaveData save = _prepared;
        _request = StartRequest.None;
        _prepared = null;
        _managedSession = request != StartRequest.None;
        if (!_managedSession) yield break;
        if (skillTerminal == null || !skillTerminal.PrepareForCheckpoint())
        { LastError = "The skill progression graph could not be initialized. The checkpoint was kept."; yield break; }

        float deadline = Time.realtimeSinceStartup + 45f;
        while (!SubsystemsReady())
        {
            if (Time.realtimeSinceStartup > deadline)
            { LastError = "The game did not finish connecting. The previous checkpoint was kept."; yield break; }
            yield return null;
        }
        // Inventory Start() builds its slots and SkillWeb Start() loads the authored graph.
        yield return null;
        yield return null;
        if (request == StartRequest.Continue)
        {
            if (!ValidateCurrentContent(save, out string error)) { LastError = error; yield break; }
            if (!Restore(save)) { LastError = "Checkpoint could not be applied. The save file was kept."; yield break; }
        }
        else
            TeamProgress.ResetAll();

        _initialized = true;
        if (request == StartRequest.Continue)
        {
            IsReady = true;
            yield break;
        }

        // Keep gameplay and the day clock gated until the new run has a durable checkpoint.
        // Inventory receipts and day-start handlers can settle after the components become ready.
        deadline = Time.realtimeSinceStartup + 15f;
        string initialSaveError = null;
        do
        {
            yield return new WaitForSecondsRealtime(0.5f);
            if (TrySaveCheckpoint(out initialSaveError))
            {
                IsReady = true;
                yield break;
            }
            // A temporary retry is still loading; the menu only receives the final failure.
            LastError = null;
            if (_hadRemotePlayers || timeManager.IsClockRunning()) break;
        } while (Time.realtimeSinceStartup < deadline);
        LastError = initialSaveError ?? "The new game could not be saved. The previous checkpoint was kept.";
    }

    private bool SubsystemsReady()
    {
        var manager = NetworkManager.main;
        var player = NetworkPlayer.Local;
        return manager != null && manager.isServer && manager.playerCount == 1 && player != null && player.isServer
            && player.GetComponent<PlayerVitals>() != null && player.GetComponent<PlayerPerks>() != null
            && timeManager != null && timeManager.isSpawned && currencyManager != null && currencyManager.isSpawned
            && taxMachine != null && taxMachine.isSpawned && contractBoard != null && contractBoard.isSpawned
            && inventoryManager != null && inventoryExpansion != null && WebViewUGUI.Active != null
            && WebViewUGUI.Active.web != null;
    }

    private void Update()
    {
        if (_managedSession && NetworkManager.main != null && NetworkManager.main.playerCount > 1)
            _hadRemotePlayers = true;
    }

    private void QueueMorningSave()
    {
        if (!_managedSession || !IsReady || _saveRoutine != null) return;
        _saveRoutine = StartCoroutine(SaveAfterDayHandlers());
    }

    private IEnumerator SaveAfterDayHandlers()
    {
        // Tax eviction, contracts, revives and receipt acknowledgements must finish first.
        yield return null;
        yield return null;
        float deadline = Time.realtimeSinceStartup + 15f;
        do
        {
            yield return new WaitForSecondsRealtime(0.5f);
            if (TrySaveCheckpoint(out _)) break;
            // Starting the day ends this checkpoint attempt; never capture subsequent arbitrary changes.
            if (_hadRemotePlayers || (timeManager != null && timeManager.IsClockRunning())) break;
        } while (Time.realtimeSinceStartup < deadline);
        _saveRoutine = null;
    }

    public bool TrySaveCheckpoint(out string error)
    {
        error = null;
        if (!_initialized || !SubsystemsReady()) error = "Wait for the local game to finish connecting.";
        else if (_hadRemotePlayers) error = "Co-op sessions cannot replace the solo checkpoint.";
        else if (!timeManager.IsSafeMorningCheckpoint || taxMachine.IsEvictionInProgress)
            error = "The checkpoint saves at 09:00 before the dungeon opens. Your previous checkpoint is kept.";
        else if (NetworkPlayer.Local.GetComponent<PlayerVitals>() == null || NetworkPlayer.Local.GetComponent<PlayerVitals>().IsDead)
            error = "A checkpoint requires a living player at camp.";
        if (error != null) { LastError = error; return false; }

        var player = NetworkPlayer.Local;
        if (!inventoryManager.TryCaptureCheckpoint(player.ServerInventory, out var inventory, out error))
        { LastError = error; return false; }
        TeamProgress.ApplyToSkillWeb();
        var web = WebViewUGUI.Active.web;
        var save = new RunSaveData
        {
            savedUtc = DateTime.UtcNow.ToString("O"), day = TimeManager.CurrentDay,
            health = player.GetComponent<PlayerVitals>().CurrentHealth, stamina = player.GetComponent<PlayerVitals>().CurrentStamina,
            currency = currencyManager.SharedCurrency, unlockedCells = inventoryExpansion.GetCurrentCellSlots(),
            civicRank = taxMachine.CivicRank, dueThroughCycle = taxMachine.DueThroughCycle,
            paidThroughCycle = taxMachine.PaidThroughCycle, pendingDiscount = taxMachine.PendingDiscount,
            contract = contractBoard.CaptureCheckpoint(), inventory = inventory,
            skillPointsEarned = TeamProgress.SkillPointsEarned, skillPoints = Esper.SkillWeb.SkillWeb.skillPoints,
            skillWebName = web.graph.webName,
            skills = web.skillNodes.Values.Select(node => new RunSkillSave { nodeId = node.id, guid = node.guid, level = node.Level }).ToArray()
        };
        bool success = RunSaveStorage.TryWrite(SavePath, save, out error);
        LastError = error;
        if (success) Debug.Log($"[RunSaveService] Saved solo morning checkpoint: day {save.day}, {save.inventory.Length} carried items.");
        return success;
    }

    private bool ValidateCurrentContent(RunSaveData save, out string error)
    {
        error = "The checkpoint references unavailable inventory or skills. The save file was kept.";
        if (save == null || !save.Validate(out _) || !timeManager.IsSafeMorningCheckpoint
            || save.unlockedCells > inventoryExpansion.GetAbsoluteMaxCells()
            || !inventoryManager.CanRestoreCheckpoint(save.inventory)) return false;
        var web = WebViewUGUI.Active.web;
        if (web.graph.webName != save.skillWebName || web.skillNodes.Count != save.skills.Length) return false;
        foreach (var saved in save.skills)
            if (!web.skillNodes.TryGetValue(saved.nodeId, out var node) || node.guid != saved.guid || saved.level > node.MaxLevel) return false;
        error = null;
        return true;
    }

    private bool Restore(RunSaveData save)
    {
        if (!timeManager.RestoreMorningCheckpoint(save.day)) return false;
        currencyManager.SetCurrencyImmediateOnServer(save.currency);
        taxMachine.RestoreCheckpoint(save);
        contractBoard.RestoreCheckpoint(save.contract);
        TeamProgress.RestoreCheckpoint(save.civicRank, save.skillPointsEarned, save.skillPoints);
        var web = WebViewUGUI.Active.web;
        foreach (var saved in save.skills) web.skillNodes[saved.nodeId].SetLevel(saved.level, false);
        // Restore purchased states before evaluating dependent unpurchased nodes; dictionary order
        // must not erase a saved child just because its prerequisite has not refreshed yet.
        foreach (var saved in save.skills)
            if (saved.level > 0) web.skillNodes[saved.nodeId].UpdateState(true);
        foreach (var saved in save.skills)
            if (saved.level == 0) web.skillNodes[saved.nodeId].UpdateState();
        // SetLevel does not spend points; restore the exact unspent balance after UI refresh.
        TeamProgress.RestoreCheckpoint(save.civicRank, save.skillPointsEarned, save.skillPoints);

        var player = NetworkPlayer.Local;
        player.GetComponent<PlayerPerks>().RefreshCheckpointModifiers();
        if (!player.GetComponent<PlayerVitals>().RestoreCheckpointVitals(save.health, save.stamina)) return false;
        inventoryManager.ClearReceipts();
        player.ServerInventory.Clear();
        inventoryExpansion.RestoreCheckpointCellCount(save.unlockedCells);
        foreach (var saved in save.inventory)
        {
            if (!player.ServerInventory.Grant(saved.receipt) || !inventoryManager.RestoreCheckpointItem(saved)) return false;
        }
        Debug.Log($"[RunSaveService] Restored solo morning checkpoint: day {save.day}, {save.inventory.Length} carried items.");
        return true;
    }
}
