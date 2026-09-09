using System.Collections;
using System.Linq;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

/// <summary>
/// Server-authoritative, team-wide tax terminal. Debt is represented by two cycle markers:
/// the highest due cycle and the highest paid cycle. Local solo morning checkpoints restore
/// these markers explicitly after the server has spawned.
///
/// Tax escalates every cycle (see <see cref="TaxSchedule.TaxForCycle"/>). If the oldest unpaid
/// cycle is still unpaid after its due day (plus grace), the server evicts the team: every peer
/// sees a notice, shared currency is zeroed and the run restarts from day one.
/// </summary>
[DisallowMultipleComponent]
public sealed class TaxCollectionMachine : AInteractable
{
    [Header("Tax Schedule")]
    [SerializeField, Min(1)] private int daysPerTaxCycle = TaxSchedule.DefaultCycleDays;
    [Tooltip("Tax for the first cycle. Later cycles grow by Tax Growth Per Cycle.")]
    [SerializeField, Min(1)] private int taxPerCycle = TaxSchedule.DefaultTaxPerCycle;
    [Tooltip("0.3 = every cycle costs 30% more than the previous one.")]
    [SerializeField, Range(0f, 2f)] private float taxGrowthPerCycle = TaxSchedule.DefaultGrowthPerCycle;
    [SerializeField, Min(0.1f)] private float serverInteractionDistance = 4f;

    [Header("Eviction")]
    [Tooltip("When on, an unpaid cycle past its due day (plus grace days) restarts the run from day 1 with zero shared currency.")]
    [SerializeField] private bool evictOnUnpaidTax = true;
    [Tooltip("Extra days after the due day before eviction. 0 = must pay on the due day.")]
    [SerializeField, Min(0)] private int evictionGraceDays = 0;
    [SerializeField, Min(0.5f)] private float evictionNoticeSeconds = 6f;

    [Header("HUD")]
    [SerializeField] private bool showCompanyHud = true;

    [Header("Interaction")]
    [SerializeField] private Collider interactionCollider;
    [SerializeField] private Renderer buttonRenderer;

    [Header("Presentation")]
    [SerializeField] private Transform shutter;
    [SerializeField] private Transform cashVisual;
    [SerializeField] private GameObject amountDisplayRoot;
    [SerializeField] private Renderer amountDisplayRenderer;
    [SerializeField] private Light statusLamp;
    [SerializeField] private Renderer statusLampRenderer;
    [SerializeField] private AudioSource paymentAudioSource;
    [SerializeField] private ParticleSystem paymentMoneyEffect;
    [SerializeField] private TaxMachineScreenController screenController;
    [SerializeField] private Vector3 shutterOpenLocalPosition;
    [SerializeField] private Vector3 shutterClosedLocalPosition;
    [SerializeField] private Vector3 cashInsertStartLocalPosition;
    [SerializeField] private Vector3 cashInsertEndLocalPosition;
    [SerializeField, Min(0.05f)] private float cashInsertDuration = 0.35f;
    [SerializeField, Min(0.05f)] private float shutterClosedDuration = 0.28f;
    [SerializeField] private Color taxDueColor = new(1f, 0.12f, 0.32f);
    [SerializeField] private Color taxClearColor = new(0.15f, 1f, 0.55f);

    // Server-owned markers. Clients only render these synchronized values.
    private readonly SyncVar<int> dueThroughCycle = new(0, ownerAuth: false);
    private readonly SyncVar<int> paidThroughCycle = new(0, ownerAuth: false);
    // Civic rank: +1 for the whole team per paid tax; unlocks the special (level-gated) skill nodes.
    private readonly SyncVar<int> civicRank = new(TeamProgress.BaseCivicRank, ownerAuth: false);
    // One-time discount on the next tax payment, granted by completed contracts (0..1).
    private readonly SyncVar<float> pendingDiscount = new(0f, ownerAuth: false);

    /// <summary>The spawned machine of this session (HUD lookup).</summary>
    public static TaxCollectionMachine Active { get; private set; }
    public int CivicRank => civicRank.value;
    public float PendingDiscount => pendingDiscount.value;
    // The server resolves the display state so clients never infer a countdown from local time.
    private readonly SyncVar<int> screenState = new((int)TaxMachineScreenState.Days2, ownerAuth: false);

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int AmountId = Shader.PropertyToID("_Amount");
    private static readonly int DigitCountId = Shader.PropertyToID("_DigitCount");
    private static readonly Color HudWarningColor = new(1f, 0.72f, 0.2f);

    private PlayersManager _playersManager;
    private CurrencyManager _currencyManager;
    private MaterialPropertyBlock _statusLampPropertyBlock;
    private MaterialPropertyBlock _amountDisplayPropertyBlock;
    private Coroutine _presentationRoutine;
    private Coroutine _evictionRoutine;
    private CompanyHud _hud;
    private bool _syncEventsBound;
    private bool _serverDayResetSubscribed;
    private bool _presentationDayResetSubscribed;
    private bool _serverPaymentInProgress;

    public int DueThroughCycle => dueThroughCycle.value;
    public int PaidThroughCycle => paidThroughCycle.value;
    public int OutstandingCycleCount => TaxSchedule.OutstandingCycles(dueThroughCycle.value, paidThroughCycle.value);
    public TaxMachineScreenState ScreenState => (TaxMachineScreenState)screenState.value;
    public bool IsEvictionInProgress => _evictionRoutine != null;

    private void Awake()
    {
        _statusLampPropertyBlock = new MaterialPropertyBlock();
        _amountDisplayPropertyBlock = new MaterialPropertyBlock();

        if (interactionCollider == null)
            interactionCollider = GetComponent<Collider>();

        if (shutter != null && shutterOpenLocalPosition == Vector3.zero && shutterClosedLocalPosition == Vector3.zero)
        {
            shutterOpenLocalPosition = shutter.localPosition;
            shutterClosedLocalPosition = shutter.localPosition + Vector3.down * 0.16f;
        }

        if (cashVisual != null && cashInsertStartLocalPosition == Vector3.zero && cashInsertEndLocalPosition == Vector3.zero)
        {
            cashInsertStartLocalPosition = cashVisual.localPosition;
            cashInsertEndLocalPosition = cashVisual.localPosition + Vector3.forward * 0.28f;
        }

        SetCashVisible(false);
        ApplySynchronizedScreenState();
    }

    protected override void OnSpawned()
    {
        base.OnSpawned();
        Active = this;

        BindSyncEvents();

        if (isServer)
        {
            // The time event also fires on peers, so only this server path ever derives due state.
            UpdateDueThroughCycleFromServerDay();
            TimeManager.OnDayReset += HandleDayReset;
            TimeManager.OnRunRestarted += HandleRunRestarted;
            _serverDayResetSubscribed = true;
        }

        // Every peer re-renders the HUD countdown when the synced day advances. Clients finish their
        // local day-change animation before the server's currentDay SyncVar lands, so listen to both.
        TimeManager.OnDayReset += HandleDayResetPresentation;
        TimeManager.OnDayChanged += HandleDayChangedPresentation;
        _presentationDayResetSubscribed = true;

        if (showCompanyHud)
        {
            _hud = CompanyHud.GetOrCreate();
            _hud.SetVisible(true);
        }

        RefreshPresentation();
    }

    protected override void OnDespawned()
    {
        if (Active == this)
            Active = null;
        UnsubscribeServerDayReset();
        UnsubscribePresentationDayReset();
        UnbindSyncEvents();

        // The HUD is scene-independent; hide it so a lobby/menu after disconnect does not keep stale tax text.
        if (_hud != null)
            _hud.SetVisible(false);

        base.OnDespawned();
    }

    protected override void OnDestroy()
    {
        UnsubscribeServerDayReset();
        UnsubscribePresentationDayReset();
        UnbindSyncEvents();
        base.OnDestroy();
    }

    public override void Interact()
    {
        // Every requester, including the host's local player, goes through the server RPC.
        // PurrNet supplies RPCInfo.sender; no client-provided day, amount, or player transform is trusted.
        RequestTeamTaxPaymentServerRpc();
    }

    public override bool CanInteract()
    {
        return true;
    }

    public override void OnHover()
    {
        PromptPresenter.ShowPrompt(BuildHoverPrompt());
    }

    public override void OnStopHover()
    {
        PromptPresenter.HidePrompt();
    }

    /// <summary>
    /// Called only by the editor authoring entry point to wire the generated prefab.
    /// It deliberately does not create persistence or modify project-wide input/UI state.
    /// </summary>
    public void ConfigureAuthoring(
        Collider configuredInteractionCollider,
        Renderer configuredButtonRenderer,
        Transform configuredShutter,
        Transform configuredCashVisual,
        GameObject configuredAmountDisplayRoot,
        Renderer configuredAmountDisplayRenderer,
        Light configuredStatusLamp,
        Renderer configuredStatusLampRenderer,
        AudioSource configuredPaymentAudioSource,
        float configuredInteractionDistance)
    {
        interactionCollider = configuredInteractionCollider;
        buttonRenderer = configuredButtonRenderer;
        shutter = configuredShutter;
        cashVisual = configuredCashVisual;
        amountDisplayRoot = configuredAmountDisplayRoot;
        amountDisplayRenderer = configuredAmountDisplayRenderer;
        statusLamp = configuredStatusLamp;
        statusLampRenderer = configuredStatusLampRenderer;
        paymentAudioSource = configuredPaymentAudioSource;

        daysPerTaxCycle = TaxSchedule.DefaultCycleDays;
        taxPerCycle = TaxSchedule.DefaultTaxPerCycle;
        taxGrowthPerCycle = TaxSchedule.DefaultGrowthPerCycle;
        serverInteractionDistance = Mathf.Max(0.1f, configuredInteractionDistance);

        if (shutter != null)
        {
            shutterOpenLocalPosition = shutter.localPosition;
            shutterClosedLocalPosition = shutter.localPosition + Vector3.down * 0.16f;
        }

        if (cashVisual != null)
        {
            cashInsertStartLocalPosition = cashVisual.localPosition;
            cashInsertEndLocalPosition = cashVisual.localPosition + Vector3.forward * 0.28f;
        }
    }

    /// <summary>
    /// Keeps the existing authoring call contract intact while wiring the optional dedicated
    /// monitor controller used by the generated prefab.
    /// </summary>
    public void ConfigureScreenController(TaxMachineScreenController configuredScreenController)
    {
        screenController = configuredScreenController;
        ApplySynchronizedScreenState();
    }

    [ServerRpc(requireOwnership: false)]
    private void RequestTeamTaxPaymentServerRpc(RPCInfo info = default)
    {
        if (!isServer)
            return;

        if (!TryResolveConnectedRequester(info.sender, out NetworkPlayer requester))
        {
            Debug.LogWarning($"[TaxCollectionMachine] Rejected tax payment from non-connected or unresolved sender {info.sender}.", this);
            return;
        }

        if (!IsRequesterWithinInteractionDistance(requester))
        {
            SendRequestFeedback(info.sender, "Move closer to the tax machine.", false);
            return;
        }

        if (_evictionRoutine != null)
        {
            SendRequestFeedback(info.sender, "Too late. The team is being evicted.", false);
            return;
        }

        // RPC handlers execute on Unity's server main thread, but this guard also makes the intended
        // transaction boundary explicit: no second request can spend while this one is being resolved.
        if (_serverPaymentInProgress)
        {
            SendRequestFeedback(info.sender, "Team tax payment is already being processed.", false);
            return;
        }

        _serverPaymentInProgress = true;
        try
        {
            int outstandingCycles = TaxSchedule.OutstandingCycles(dueThroughCycle.value, paidThroughCycle.value);
            if (outstandingCycles <= 0)
            {
                SendRequestFeedback(info.sender, "The team has no tax due.", false);
                return;
            }

            if (!TryCalculateOutstandingAmount(out int amount))
            {
                // Do not charge a saturated partial amount and then clear debt.
                SendRequestFeedback(info.sender, "The accrued team tax exceeds the supported currency range.", false);
                return;
            }

            if (!TryResolveCurrencyManager(out CurrencyManager currencyManager))
            {
                SendRequestFeedback(info.sender, "Shared currency is unavailable right now.", false);
                return;
            }

            int undiscounted = amount;
            amount = ApplyPendingDiscount(amount);
            if (!currencyManager.CanAfford(amount))
            {
                SendRequestFeedback(
                    info.sender,
                    $"Team funds are insufficient. Need ${amount:N0}; shared balance is ${currencyManager.SharedCurrency:N0}.",
                    false);
                return;
            }

            // This is immediate server-side state mutation; it does not enqueue a client spend request.
            if (!currencyManager.TrySpendCurrencyImmediateOnServer(amount))
            {
                SendRequestFeedback(info.sender, "Shared currency changed before the tax payment could finish.", false);
                return;
            }

            // A team payment clears every currently accrued cycle in one atomic server transaction.
            paidThroughCycle.value = dueThroughCycle.value;
            if (amount != undiscounted)
                Debug.Log($"[TaxCollectionMachine] Contract discount applied: ${undiscounted:N0} -> ${amount:N0}.", this);
            pendingDiscount.value = 0f;
            // Paying the company earns civic rank for everyone (special skill nodes need it).
            civicRank.value = civicRank.value + outstandingCycles;
            // Hold the success face for the rest of this day. The next server day reset resumes
            // the new cycle's D-2/D-1/PAY schedule.
            screenState.value = (int)TaxMachineScreenState.Smile;

            SendRequestFeedback(
                info.sender,
                $"Team tax paid: ${amount:N0} for {outstandingCycles} cycle{(outstandingCycles == 1 ? string.Empty : "s")}.",
                true);
            PlaySuccessfulPaymentPresentationObserversRpc();
        }
        finally
        {
            _serverPaymentInProgress = false;
        }
    }

    [ObserversRpc]
    private void PlaySuccessfulPaymentPresentationObserversRpc()
    {
        RefreshPresentation();

        if (_presentationRoutine != null)
            StopCoroutine(_presentationRoutine);

        _presentationRoutine = StartCoroutine(PlaySuccessfulPaymentPresentation());
    }

    [TargetRpc]
    private void SendRequestFeedback(PlayerID target, string message, bool success)
    {
        PromptPresenter.ShowPrompt(message);
    }

    private IEnumerator PlaySuccessfulPaymentPresentation()
    {
        if (shutter != null)
            shutter.localPosition = shutterOpenLocalPosition;

        if (cashVisual != null)
        {
            cashVisual.localPosition = cashInsertStartLocalPosition;
            SetCashVisible(true);
        }

        if (paymentAudioSource != null && paymentAudioSource.clip != null)
            paymentAudioSource.Play();

        if (paymentMoneyEffect != null)
        {
            paymentMoneyEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            paymentMoneyEffect.Play(true);
        }

        float elapsed = 0f;
        float duration = Mathf.Max(0.05f, cashInsertDuration);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (cashVisual != null)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                cashVisual.localPosition = Vector3.Lerp(
                    cashInsertStartLocalPosition,
                    cashInsertEndLocalPosition,
                    progress);
            }

            yield return null;
        }

        if (shutter != null)
            shutter.localPosition = shutterClosedLocalPosition;

        yield return new WaitForSeconds(Mathf.Max(0.05f, shutterClosedDuration));

        if (shutter != null)
            shutter.localPosition = shutterOpenLocalPosition;

        yield return new WaitForSeconds(0.12f);

        if (cashVisual != null)
        {
            cashVisual.localPosition = cashInsertStartLocalPosition;
            SetCashVisible(false);
        }

        _presentationRoutine = null;
    }

    // ───────────────────────── Day transitions / eviction (server) ─────────────────────────

    private void HandleDayReset()
    {
        if (!isServer)
            return;

        UpdateDueThroughCycleFromServerDay();
        ApplyOverdueRankPenalty();
        TryBeginEviction();
    }

    private void HandleRunRestarted(string reason)
    {
        if (!isServer) return;
        paidThroughCycle.value = 0;
        dueThroughCycle.value = 0;
        civicRank.value = TeamProgress.BaseCivicRank;
        pendingDiscount.value = 0f;
        UpdateScreenStateFromServerDay(1);
        RefreshPresentation();
    }

    public void RestoreCheckpoint(RunSaveData save)
    {
        if (!isServer) return;
        dueThroughCycle.value = save.dueThroughCycle;
        paidThroughCycle.value = save.paidThroughCycle;
        civicRank.value = save.civicRank;
        pendingDiscount.value = save.pendingDiscount;
        UpdateScreenStateFromServerDay(save.day);
        RefreshPresentation();
    }

    /// <summary>
    /// Every day that starts with unpaid tax past its due day costs the team one civic rank
    /// (never below the base rank). Rank gates the special skill nodes, so slacking on tax has
    /// a cost before the eviction hammer falls.
    /// </summary>
    private void ApplyOverdueRankPenalty()
    {
        if (!isServer || _evictionRoutine != null)
            return;

        int outstandingCycles = TaxSchedule.OutstandingCycles(dueThroughCycle.value, paidThroughCycle.value);
        if (outstandingCycles <= 0)
            return;

        int currentDay = TimeManager.CurrentDay;
        int daysPerCycle = Mathf.Max(1, daysPerTaxCycle);
        bool dueToday = TaxSchedule.CycleDayForDay(currentDay, daysPerCycle) == daysPerCycle;
        if (dueToday || civicRank.value <= TeamProgress.BaseCivicRank)
            return;

        civicRank.value = civicRank.value - 1;
        Debug.Log($"[TaxCollectionMachine] Overdue tax on day {currentDay}: civic rank -1 -> {civicRank.value}.", this);
        OverdueRankLostObserversRpc(civicRank.value);
    }

    [ObserversRpc(runLocally: true)]
    private void OverdueRankLostObserversRpc(int newRank)
    {
        PromptPresenter.ShowPrompt($"TAX OVERDUE: civic rank dropped to {newRank}");
    }

    private void HandleDayResetPresentation()
    {
        RefreshPresentation();
    }

    private void HandleDayChangedPresentation(int _)
    {
        RefreshPresentation();
    }

    private void UpdateDueThroughCycleFromServerDay()
    {
        if (!isServer)
            return;

        // This is the only place that reads the time source. Outstanding debt itself is
        // subsequently calculated from SyncVar state, including in payment RPCs and clients.
        int currentDay = TimeManager.CurrentDay;
        dueThroughCycle.value = TaxSchedule.DueThroughCycleForDay(
            currentDay,
            Mathf.Max(1, daysPerTaxCycle));
        UpdateScreenStateFromServerDay(currentDay);
    }

    private void UpdateScreenStateFromServerDay(int currentDay)
    {
        if (!isServer)
            return;

        screenState.value = (int)TaxSchedule.ResolveScreenState(
            currentDay,
            dueThroughCycle.value,
            paidThroughCycle.value,
            Mathf.Max(1, daysPerTaxCycle));
    }

    private void TryBeginEviction()
    {
        if (!isServer || !evictOnUnpaidTax || _evictionRoutine != null)
            return;

        int currentDay = TimeManager.CurrentDay;
        bool shouldEvict = TaxSchedule.ShouldEvict(
            currentDay,
            dueThroughCycle.value,
            paidThroughCycle.value,
            Mathf.Max(1, daysPerTaxCycle),
            evictionGraceDays);
        if (!shouldEvict)
            return;

        TryCalculateOutstandingAmount(out int unpaidAmount);
        Debug.Log($"[TaxCollectionMachine] Evicting team on day {currentDay}: unpaid ${unpaidAmount:N0} " +
                  $"(due={dueThroughCycle.value}, paid={paidThroughCycle.value}).", this);

        _evictionRoutine = StartCoroutine(EvictTeamRoutine(unpaidAmount, currentDay));
    }

    private IEnumerator EvictTeamRoutine(int unpaidAmount, int currentDay)
    {
        // Let this frame's day-reset handlers finish before touching the run state again.
        yield return null;

        TeamEvictedObserversRpc(unpaidAmount, currentDay);

        // Give every peer time to read the notice before the world resets under them.
        yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, evictionNoticeSeconds * 0.75f));

        if (TryResolveCurrencyManager(out CurrencyManager currencyManager))
            currencyManager.SetCurrencyImmediateOnServer(0);

        paidThroughCycle.value = 0;
        dueThroughCycle.value = 0;
        civicRank.value = TeamProgress.BaseCivicRank;
        screenState.value = (int)TaxMachineScreenState.Days2;

        TimeManager timeManager = TimeManager.Active;
        if (timeManager != null)
            timeManager.ServerRestartFromDayOne("tax eviction");
        else
            Debug.LogWarning("[TaxCollectionMachine] No TimeManager to restart the run after eviction.", this);

        _evictionRoutine = null;
        RefreshPresentation();
    }

    [ObserversRpc]
    private void TeamEvictedObserversRpc(int unpaidAmount, int day)
    {
        string unpaidText = unpaidAmount == int.MaxValue ? "over the currency limit" : $"${unpaidAmount:N0}";
        string body = $"Unpaid team tax: {unpaidText} (day {day}).\n" +
                      "The company seized the shared account.\nThe run restarts from day 1.";
        PromptPresenter.ShowPrompt("TEAM EVICTED");

        if (showCompanyHud)
        {
            _hud = CompanyHud.GetOrCreate();
            _hud.ShowEvictionNotice("EVICTED", body, evictionNoticeSeconds);
        }
    }

    // ───────────────────────── Contract rewards (server) ─────────────────────────

    /// <summary>Server only: contracts and other company favours raise the whole team's civic rank.</summary>
    public void ServerGrantCivicRank(int amount)
    {
        if (!isServer || amount <= 0)
            return;

        civicRank.value = civicRank.value + amount;
        Debug.Log($"[TaxCollectionMachine] Civic rank +{amount} -> {civicRank.value}.", this);
    }

    /// <summary>Server only: stack a one-time discount (fraction) onto the next tax payment, capped at 50%.</summary>
    public void ServerAddTaxDiscount(float fraction)
    {
        if (!isServer || fraction <= 0f)
            return;

        pendingDiscount.value = Mathf.Clamp(pendingDiscount.value + fraction, 0f, 0.5f);
        Debug.Log($"[TaxCollectionMachine] Next tax discount is now {pendingDiscount.value:P0}.", this);
    }

    private int ApplyPendingDiscount(int amount)
    {
        float discount = pendingDiscount.value;
        if (discount <= 0.001f || amount <= 0 || amount == int.MaxValue)
            return amount;

        return Mathf.Max(0, Mathf.RoundToInt(amount * (1f - discount)));
    }

    // ───────────────────────── Amount helpers ─────────────────────────

    private bool TryCalculateOutstandingAmount(out int amount)
    {
        return TaxSchedule.TryCalculateOutstandingCost(
            dueThroughCycle.value,
            paidThroughCycle.value,
            taxPerCycle,
            taxGrowthPerCycle,
            out amount);
    }

    /// <summary>
    /// The amount the machine displays: every unpaid cycle while debt exists, otherwise a preview
    /// of the cycle that becomes due next. Zero once the current cycle is paid (Smile).
    /// </summary>
    private bool TryCalculateDisplayedAmount(out int amount)
    {
        int outstandingCycles = TaxSchedule.OutstandingCycles(dueThroughCycle.value, paidThroughCycle.value);
        if (outstandingCycles > 0)
            return TryCalculateOutstandingAmount(out amount);

        int displayedCycles = TaxSchedule.DisplayedPaymentCycles(0, (TaxMachineScreenState)screenState.value);
        if (displayedCycles <= 0)
        {
            amount = 0;
            return true;
        }

        amount = TaxSchedule.TaxForCycle(dueThroughCycle.value + 1, taxPerCycle, taxGrowthPerCycle);
        return amount != int.MaxValue;
    }

    private int NextDueAmount()
    {
        return TaxSchedule.TaxForCycle(dueThroughCycle.value + 1, taxPerCycle, taxGrowthPerCycle);
    }

    // ───────────────────────── Lookups ─────────────────────────

    private bool TryResolveConnectedRequester(PlayerID sender, out NetworkPlayer requester)
    {
        requester = null;

        if (!TryResolvePlayersManager(out PlayersManager playersManager)
            || !playersManager.players.Contains(sender))
        {
            return false;
        }

        NetworkPlayer[] candidates = FindObjectsByType<NetworkPlayer>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < candidates.Length; i++)
        {
            NetworkPlayer candidate = candidates[i];
            if (candidate == null
                || !candidate.isSpawned
                || !candidate.owner.HasValue
                || candidate.owner.Value != sender)
            {
                continue;
            }

            requester = candidate;
            return true;
        }

        return false;
    }

    private bool IsRequesterWithinInteractionDistance(NetworkPlayer requester)
    {
        float maximumDistance = Mathf.Max(0.1f, serverInteractionDistance);
        Vector3 machinePoint = interactionCollider != null
            ? interactionCollider.ClosestPoint(requester.transform.position)
            : transform.position;

        return (requester.transform.position - machinePoint).sqrMagnitude <= maximumDistance * maximumDistance;
    }

    private bool TryResolvePlayersManager(out PlayersManager playersManager)
    {
        playersManager = _playersManager;
        if (playersManager != null)
            return true;

        if (NetworkManager.main == null
            || !NetworkManager.main.TryGetModule<PlayersManager>(true, out playersManager))
        {
            return false;
        }

        _playersManager = playersManager;
        return true;
    }

    private bool TryResolveCurrencyManager(out CurrencyManager currencyManager)
    {
        currencyManager = _currencyManager;
        if (currencyManager != null)
            return true;

        if (!InstanceHandler.TryGetInstance(out currencyManager))
            currencyManager = FindFirstObjectByType<CurrencyManager>();

        _currencyManager = currencyManager;
        return currencyManager != null;
    }

    // ───────────────────────── Event wiring ─────────────────────────

    private void BindSyncEvents()
    {
        if (_syncEventsBound)
            return;

        dueThroughCycle.onChanged += HandleTaxStateChanged;
        paidThroughCycle.onChanged += HandleTaxStateChanged;
        screenState.onChanged += HandleScreenStateChanged;
        civicRank.onChanged += HandleCivicRankChanged;
        HandleCivicRankChanged(civicRank.value);
        _syncEventsBound = true;
    }

    private void HandleCivicRankChanged(int rank)
    {
        TeamProgress.SetCivicRank(rank);
    }

    private void UnbindSyncEvents()
    {
        if (!_syncEventsBound)
            return;

        dueThroughCycle.onChanged -= HandleTaxStateChanged;
        paidThroughCycle.onChanged -= HandleTaxStateChanged;
        screenState.onChanged -= HandleScreenStateChanged;
        civicRank.onChanged -= HandleCivicRankChanged;
        _syncEventsBound = false;
    }

    private void UnsubscribeServerDayReset()
    {
        if (!_serverDayResetSubscribed)
            return;

        TimeManager.OnDayReset -= HandleDayReset;
        TimeManager.OnRunRestarted -= HandleRunRestarted;
        _serverDayResetSubscribed = false;
    }

    private void UnsubscribePresentationDayReset()
    {
        if (!_presentationDayResetSubscribed)
            return;

        TimeManager.OnDayReset -= HandleDayResetPresentation;
        TimeManager.OnDayChanged -= HandleDayChangedPresentation;
        _presentationDayResetSubscribed = false;
    }

    private void HandleTaxStateChanged(int _)
    {
        RefreshPresentation();
    }

    private void HandleScreenStateChanged(int _)
    {
        RefreshPresentation();
    }

    // ───────────────────────── Presentation ─────────────────────────

    private void RefreshPresentation()
    {
        ApplySynchronizedScreenState();

        int outstandingCycles = TaxSchedule.OutstandingCycles(dueThroughCycle.value, paidThroughCycle.value);
        bool taxDue = outstandingCycles > 0;
        bool representable = TryCalculateDisplayedAmount(out int displayedAmount);
        bool showAmountDisplay = displayedAmount > 0;

        if (amountDisplayRoot != null)
            amountDisplayRoot.SetActive(showAmountDisplay);

        if (showAmountDisplay)
            ApplyAmountDisplay(representable ? displayedAmount : int.MaxValue);

        Color statusColor = taxDue ? taxDueColor : taxClearColor;
        if (statusLamp != null)
        {
            statusLamp.color = statusColor;
            statusLamp.intensity = taxDue ? 3.5f : 1.6f;
        }

        if (statusLampRenderer != null)
        {
            statusLampRenderer.GetPropertyBlock(_statusLampPropertyBlock);
            _statusLampPropertyBlock.SetColor(EmissionColorId, statusColor * (taxDue ? 2.2f : 1.1f));
            statusLampRenderer.SetPropertyBlock(_statusLampPropertyBlock);
        }

        RefreshHud(outstandingCycles);
    }

    private void RefreshHud(int outstandingCycles)
    {
        if (!showCompanyHud)
            return;

        if (_hud == null)
            _hud = CompanyHud.GetOrCreate();

        int currentDay = TimeManager.CurrentDay;
        int daysPerCycle = Mathf.Max(1, daysPerTaxCycle);
        int daysUntilDue = TaxSchedule.DaysUntilNextDue(currentDay, daysPerCycle);
        var state = (TaxMachineScreenState)screenState.value;

        if (outstandingCycles > 0)
        {
            TryCalculateOutstandingAmount(out int owed);
            string owedText = owed == int.MaxValue ? "over limit" : $"${owed:N0}";
            bool dueToday = TaxSchedule.CycleDayForDay(currentDay, daysPerCycle) == daysPerCycle;
            string detail;
            if (dueToday)
            {
                detail = evictOnUnpaidTax && evictionGraceDays == 0
                    ? "DUE TODAY. Unpaid by midnight = eviction."
                    : "DUE TODAY. Pay at the tax machine.";
            }
            else if (evictOnUnpaidTax)
            {
                detail = $"OVERDUE. Eviction in {Mathf.Max(0, EvictionDayFor(currentDay) - currentDay)} day(s).";
            }
            else
            {
                detail = "OVERDUE. Pay at the tax machine.";
            }
            _hud.SetTax(owedText, detail, taxDueColor);
            return;
        }

        if (state == TaxMachineScreenState.Smile)
        {
            _hud.SetTax("PAID", $"Next: ${NextDueAmount():N0} in {Mathf.Max(1, daysPerCycle)} day(s)", taxClearColor);
            return;
        }

        string countdown = daysUntilDue <= 0 ? "due today" : $"due in {daysUntilDue} day(s)";
        _hud.SetTax($"${NextDueAmount():N0}", $"Cycle {dueThroughCycle.value + 1} {countdown}", daysUntilDue <= 1 ? HudWarningColor : taxClearColor);
    }

    private int EvictionDayFor(int currentDay)
    {
        int daysPerCycle = Mathf.Max(1, daysPerTaxCycle);
        int oldestUnpaid = Mathf.Max(1, paidThroughCycle.value + 1);
        return oldestUnpaid * daysPerCycle + evictionGraceDays + 1;
    }

    private void ApplySynchronizedScreenState()
    {
        if (screenController != null)
            screenController.SetRuntimeState((TaxMachineScreenState)screenState.value);
    }

    private void ApplyAmountDisplay(int materialAmount)
    {
        if (amountDisplayRenderer == null || materialAmount <= 0)
            return;

        amountDisplayRenderer.GetPropertyBlock(_amountDisplayPropertyBlock);
        _amountDisplayPropertyBlock.SetFloat(AmountId, materialAmount);
        _amountDisplayPropertyBlock.SetFloat(DigitCountId, DecimalDigitCount(materialAmount));
        amountDisplayRenderer.SetPropertyBlock(_amountDisplayPropertyBlock);
    }

    private static int DecimalDigitCount(int value)
    {
        int positive = Mathf.Max(0, value);
        int digits = 1;
        while (positive >= 10 && digits < 10)
        {
            positive /= 10;
            digits++;
        }

        return digits;
    }

    private string BuildHoverPrompt()
    {
        int outstandingCycles = TaxSchedule.OutstandingCycles(dueThroughCycle.value, paidThroughCycle.value);
        if (outstandingCycles <= 0)
            return $"[F] Team tax status: clear (next ${NextDueAmount():N0})";

        return TryCalculateOutstandingAmount(out int amount)
            ? $"[F] Pay team tax (${amount:N0})"
            : "[F] Team tax exceeds currency range";
    }


    private void SetCashVisible(bool visible)
    {
        if (cashVisual != null)
            cashVisual.gameObject.SetActive(visible);
    }
}
