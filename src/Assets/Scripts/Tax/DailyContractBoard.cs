using System;
using PurrNet;
using UnityEngine;

public enum ContractReward
{
    CivicRank,
    TaxDiscount,
}

public enum ContractState
{
    None = 0,
    Active = 1,
    Completed = 2,
}

/// <summary>
/// One company contract per day, rolled by the server at every day reset and tracked through
/// <see cref="ContractEvents"/>. Lives on the tax machine object so it shares its network identity.
/// Rewards go through the machine: civic rank (unlocks special skill nodes) or a one-time tax discount.
/// </summary>
[DisallowMultipleComponent]
public sealed class DailyContractBoard : NetworkBehaviour
{
    [Serializable]
    private struct Template
    {
        public string title;
        public ContractGoal goal;
        public int baseTarget;
        public int daysPerExtraTarget;
        public ContractReward reward;
        public string descriptionFormat; // {0} = target

        public Template(string title, ContractGoal goal, int baseTarget, int daysPerExtraTarget, ContractReward reward, string descriptionFormat)
        {
            this.title = title;
            this.goal = goal;
            this.baseTarget = baseTarget;
            this.daysPerExtraTarget = daysPerExtraTarget;
            this.reward = reward;
            this.descriptionFormat = descriptionFormat;
        }
    }

    private static readonly Template[] Templates =
    {
        new("HEAD HUNT", ContractGoal.SmilyTrophies, 2, 3, ContractReward.CivicRank, "Feed {0} Smily heads to the corpse processor"),
        new("CLOWN BOUNTY", ContractGoal.ClownTrophies, 1, 4, ContractReward.CivicRank, "Feed {0} Clown head(s) to the corpse processor"),
        new("CULL THE SWARM", ContractGoal.OctopusTrophies, 1, 6, ContractReward.CivicRank, "Process {0} Octopus trophy(s)"),
        new("SALES QUOTA", ContractGoal.SaleCredits, 1200, 0, ContractReward.TaxDiscount, "Earn ${0:N0} from sales today"),
        new("KEEP THE LIGHTS ON", ContractGoal.BatteryRecharges, 2, 0, ContractReward.TaxDiscount, "Recharge the dungeon battery {0} times"),
    };

    private const int QuotaTemplateIndex = 3;
    public const float TaxDiscountFraction = 0.2f;

    [SerializeField, Tooltip("Optional explicit link; otherwise the machine on the same object is used.")]
    private TaxCollectionMachine taxMachine;

    private readonly SyncVar<int> _templateIndex = new(-1, ownerAuth: false);
    private readonly SyncVar<int> _target = new(0, ownerAuth: false);
    private readonly SyncVar<int> _progress = new(0, ownerAuth: false);
    private readonly SyncVar<int> _day = new(0, ownerAuth: false);
    private readonly SyncVar<int> _state = new((int)ContractState.None, ownerAuth: false);

    private int _lastTemplateIndex = -1;
    private bool _serverHooked;

    public static DailyContractBoard Current { get; private set; }
    public static event Action Changed;
    public static event Action<string, string> CompletedLocally;

    public ContractState State => (ContractState)_state.value;
    public bool HasContract => _templateIndex.value >= 0 && State != ContractState.None;
    public int Day => _day.value;
    public int Target => _target.value;
    public int Progress => Mathf.Min(_progress.value, _target.value);
    public float Progress01 => _target.value > 0 ? Mathf.Clamp01((float)_progress.value / _target.value) : 0f;
    public string Title => HasContract ? Templates[_templateIndex.value].title : string.Empty;
    public ContractGoal Goal => HasContract ? Templates[_templateIndex.value].goal : ContractGoal.SaleCredits;
    public ContractReward Reward => HasContract ? Templates[_templateIndex.value].reward : ContractReward.CivicRank;

    public string Description => HasContract
        ? string.Format(Templates[_templateIndex.value].descriptionFormat, _target.value)
        : string.Empty;

    public string ProgressText => HasContract
        ? (Goal == ContractGoal.SaleCredits ? $"${Progress:N0} / ${Target:N0}" : $"{Progress} / {Target}")
        : string.Empty;

    public string RewardText => RewardLabel(Reward);

    public RunContractSave CaptureCheckpoint() => new()
    {
        templateIndex = _templateIndex.value, target = _target.value, progress = Progress,
        day = _day.value, state = _state.value, lastTemplateIndex = _lastTemplateIndex
    };

    public void RestoreCheckpoint(RunContractSave save)
    {
        if (!isServer) return;
        _templateIndex.value = save.templateIndex;
        _target.value = save.target;
        _progress.value = save.progress;
        _day.value = save.day;
        _state.value = save.state;
        _lastTemplateIndex = save.lastTemplateIndex;
        Changed?.Invoke();
    }

    public static string RewardLabel(ContractReward reward)
    {
        return reward == ContractReward.CivicRank
            ? "+1 CIVIC RANK"
            : $"-{Mathf.RoundToInt(TaxDiscountFraction * 100f)}% NEXT TAX";
    }

    protected override void OnSpawned()
    {
        base.OnSpawned();
        Current = this;

        _templateIndex.onChanged += OnSyncChanged;
        _target.onChanged += OnSyncChanged;
        _progress.onChanged += OnSyncChanged;
        _day.onChanged += OnSyncChanged;
        _state.onChanged += OnSyncChanged;

        if (isServer && !_serverHooked)
        {
            _serverHooked = true;
            TimeManager.OnDayReset += ServerRollDaily;
            TimeManager.OnRunRestarted += ServerOnRunRestarted;
            ContractEvents.Reported += ServerOnReported;
            ServerRollDaily();
        }

        if (!isServer && HasContract)
        {
            // Initial SyncVar values on a late joiner arrive without onChanged callbacks.
            _lastLoggedTemplate = _templateIndex.value;
            Debug.Log($"[DailyContractBoard] Joined with day {Day} contract: {Title} target={Target} progress={Progress} reward={Reward}", this);
        }

        Changed?.Invoke();
    }

    protected override void OnDespawned()
    {
        Unhook();
        base.OnDespawned();
    }

    protected override void OnDestroy()
    {
        Unhook();
        base.OnDestroy();
    }

    private void Unhook()
    {
        _templateIndex.onChanged -= OnSyncChanged;
        _target.onChanged -= OnSyncChanged;
        _progress.onChanged -= OnSyncChanged;
        _day.onChanged -= OnSyncChanged;
        _state.onChanged -= OnSyncChanged;

        if (_serverHooked)
        {
            _serverHooked = false;
            TimeManager.OnDayReset -= ServerRollDaily;
            TimeManager.OnRunRestarted -= ServerOnRunRestarted;
            ContractEvents.Reported -= ServerOnReported;
        }

        if (Current == this)
            Current = null;
    }

    private int _lastLoggedTemplate = -1;

    private void OnSyncChanged(int _)
    {
        if (!isServer && HasContract && _templateIndex.value != _lastLoggedTemplate)
        {
            _lastLoggedTemplate = _templateIndex.value;
            Debug.Log($"[DailyContractBoard] Synced day {Day} contract: {Title} target={Target} reward={Reward}", this);
        }

        Changed?.Invoke();
    }

    // ───────────────────────── Server ─────────────────────────

    private void ServerOnRunRestarted(string _)
    {
        _lastTemplateIndex = -1;
    }

    private void ServerRollDaily()
    {
        if (!isServer)
            return;

        int day = Mathf.Max(1, TimeManager.CurrentDay);
        int index = PickTemplate(day);
        Template template = Templates[index];

        int target = template.baseTarget;
        if (template.goal == ContractGoal.SaleCredits)
            target = Mathf.Max(100, Mathf.RoundToInt(template.baseTarget * TimeManager.CurrentDailyScalingMultiplier / 50f) * 50);
        else if (template.daysPerExtraTarget > 0)
            target += (day - 1) / template.daysPerExtraTarget;

        _lastTemplateIndex = index;
        _templateIndex.value = index;
        _target.value = target;
        _progress.value = 0;
        _day.value = day;
        _state.value = (int)ContractState.Active;

        Debug.Log($"[DailyContractBoard] Day {day} contract: {template.title} target={target} reward={template.reward}", this);
    }

    private int PickTemplate(int day)
    {
        if (day <= 1)
            return QuotaTemplateIndex;

        // Deterministic per day, never the same contract two days in a row.
        uint hash = (uint)day * 2654435761u ^ 0x9E3779B9u;
        int index = (int)(hash % (uint)Templates.Length);
        if (index == _lastTemplateIndex)
            index = (index + 1) % Templates.Length;
        return index;
    }

    private void ServerOnReported(ContractGoal goal, int amount)
    {
        if (!isServer || State != ContractState.Active || !HasContract || goal != Goal)
            return;

        _progress.value = _progress.value + amount;
        if (_progress.value < _target.value)
            return;

        _state.value = (int)ContractState.Completed;
        TaxCollectionMachine machine = taxMachine != null ? taxMachine : GetComponent<TaxCollectionMachine>();
        if (machine == null)
            machine = FindAnyObjectByType<TaxCollectionMachine>();

        if (machine != null)
        {
            if (Reward == ContractReward.CivicRank)
                machine.ServerGrantCivicRank(1);
            else
                machine.ServerAddTaxDiscount(TaxDiscountFraction);
        }
        else
        {
            Debug.LogWarning("[DailyContractBoard] Contract completed but no TaxCollectionMachine to pay the reward.", this);
        }

        Debug.Log($"[DailyContractBoard] Contract '{Title}' completed on day {Day}; reward {Reward}.", this);
        ContractCompletedObserversRpc(Title, RewardText);
    }

    [ObserversRpc(runLocally: true)]
    private void ContractCompletedObserversRpc(string title, string rewardText)
    {
        PromptPresenter.ShowPrompt($"CONTRACT COMPLETE: {title}  {rewardText}");
        CompletedLocally?.Invoke(title, rewardText);
    }
}
