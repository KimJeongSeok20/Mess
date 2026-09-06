using PurrNet;
using UnityEngine;

/// <summary>
/// Revive counter on the StartRoom reception wall. Dead players are locked in the booth behind
/// the window; a living teammate presses F on the window from the main room, the team's shared
/// currency pays, and the ghost steps out next to the window. Cost rises with every paid revive
/// of the day (ReviveLedger) and Haggler lowers it.
/// Attached at runtime by <see cref="DungeonStartPoint"/> to "Wall_Interior_Reception" (or to a
/// fallback pillar when that wall is missing).
/// </summary>
public class ReviveStation : AInteractable
{
    public static ReviveStation Active { get; private set; }
    public Vector3 ServerRevivePosition => transform.TransformPoint(_reviveLocal);
    /// <summary>Where the ghost waits (inside the booth). Null when no station exists yet.</summary>
    public static Vector3? WaitingRoomPoint { get; private set; }

    /// <summary>Where a revived player stands up (outside the window, main-room side).</summary>
    public static Vector3? RevivePoint { get; private set; }

    private Vector3 _waitingLocal;
    private Vector3 _reviveLocal;

    public override int InteractPriority => 90;

    // Dead players may not use anything else, but they also cannot free themselves here:
    // the counter only responds to living players (solo death = run over).
    public override bool AllowInteractionWhileDead => false;

    public override bool CanInteract() => true;

    public void Configure(Vector3 waitingLocal, Vector3 reviveLocal)
    {
        _waitingLocal = waitingLocal;
        _reviveLocal = reviveLocal;
        RefreshStaticPoints();
    }

    private void OnEnable()
    {
        Active = this;
        RefreshStaticPoints();
    }

    private void OnDisable()
    {
        if (Active != this) return;
        Active = null;
        WaitingRoomPoint = null;
        RevivePoint = null;
    }

    private void RefreshStaticPoints()
    {
        WaitingRoomPoint = transform.TransformPoint(_waitingLocal);
        RevivePoint = transform.TransformPoint(_reviveLocal);
    }

    public override void OnHover()
    {
        PlayerVitals target = PickReviveTarget();
        int cost = ReviveLedger.CurrentCost;

        if (target == null)
        {
            PromptPresenter.ShowPrompt($"Reception — nobody is waiting (next revive ${cost:N0})");
            return;
        }

        string who = target.name.Replace("(Clone)", string.Empty);
        PromptPresenter.ShowPrompt($"[F] Pay ${cost:N0} to release {who}");
    }

    public override void OnStopHover()
    {
        PromptPresenter.HidePrompt();
    }

    public override void Interact(InteractionManager interactor)
    {
        PlayerVitals target = PickReviveTarget();
        if (target == null)
        {
            PromptPresenter.ShowPrompt("Nobody is waiting to be revived.");
            return;
        }

        NetworkPlayer local = FindLocalNetworkPlayer();
        RefreshStaticPoints();
        Vector3 revivePosition = RevivePoint ?? transform.position;

        if (local == null || !local.isSpawned)
        {
            // Offline / test scene: revive directly.
            PlayerDeath death = target.GetComponent<PlayerDeath>();
            if (death != null)
                death.ReviveNow(revivePosition);
            return;
        }

        if (!target.owner.HasValue)
        {
            PromptPresenter.ShowPrompt("Target has no owner.");
            return;
        }

        local.RequestReviveServerRpc(target.owner.Value, revivePosition);
    }

    /// <summary>The teammate who has been dead the longest (never yourself: ghosts cannot pay).</summary>
    private static PlayerVitals PickReviveTarget()
    {
        PlayerVitals[] all = FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None);
        PlayerVitals longestDead = null;

        for (int i = 0; i < all.Length; i++)
        {
            PlayerVitals vitals = all[i];
            if (vitals == null || !vitals.IsDead)
                continue;

            if (longestDead == null || vitals.DeadForSeconds > longestDead.DeadForSeconds)
                longestDead = vitals;
        }

        return longestDead;
    }

    private static NetworkPlayer FindLocalNetworkPlayer()
    {
        NetworkPlayer[] all = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].isSpawned && all[i].isOwner)
                return all[i];
        }

        return null;
    }
}
