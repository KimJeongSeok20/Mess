using PurrNet;
using UnityEngine;

/// <summary>
/// Smily's jump-attack hitbox. Runs only on the server (or offline): the hit goes through
/// PlayerVitals so HP, damage reduction and the SkillWeb health boost matter, and the
/// server replicates the result to every peer.
/// </summary>
public class AttackHitbox : MonoBehaviour
{
    [HideInInspector] public bool HasHitThisAttack;

    [SerializeField] private SmilyAttackState attackState;
    [SerializeField] private JumpToPoint jumpToPoint;
    [SerializeField] private bool requireAttackState = true;
    [SerializeField] private bool requireJumping = true;

    [Header("Damage")]
    [Tooltip("Damage dealt by a landed jump. 70 leaves a full-HP player alive once; two hits kill.")]
    [SerializeField, Min(1)] private int damage = 70;
    [Tooltip("Old behaviour: ignore HP and kill on contact.")]
    [SerializeField] private bool instantKill = false;

    private bool _reportedMissingAttackState;

    private static bool HasServerAuthority => NetworkManager.main == null || NetworkManager.main.isServer;

    private void Awake()
    {
        ResolveAttackState();
    }

    private void OnEnable()
    {
        HasHitThisAttack = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryDamage(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryDamage(other);
    }

    private void TryDamage(Collider other)
    {
        if (HasHitThisAttack)
            return;

        // Only the authority decides hits; clients see the result through PlayerVitals SyncVars.
        if (!HasServerAuthority)
            return;

        if (requireAttackState && !CanDamagePlayer())
            return;

        PlayerDeath death = ResolvePlayerDeath(other);
        if (death == null || death.IsDead)
            return;

        HasHitThisAttack = true;
        IMonsterDeathSequence killerSequence = GetComponentInParent<IMonsterDeathSequence>();

        PlayerVitals vitals = death.GetComponent<PlayerVitals>();
        if (instantKill || vitals == null)
        {
            death.Kill(killerSequence);
            return;
        }

        vitals.ApplyDamage(DamageRequest.Melee(damage), killerSequence);
    }

    public void ResetHit()
    {
        HasHitThisAttack = false;
    }

    public void BeginAttackWindow(float duration)
    {
        ResolveAttackState();
        HasHitThisAttack = false;
        attackState?.BeginAttackWindow(duration, "AttackHitbox");
    }

    public void EndAttackWindow()
    {
        attackState?.EndAttackWindow("AttackHitbox");
    }

    public void AnimationEvent_BeginAttackWindow()
    {
        BeginAttackWindow(0f);
    }

    public void AnimationEvent_EndAttackWindow()
    {
        EndAttackWindow();
    }

    private bool CanDamagePlayer()
    {
        ResolveAttackState();
        if (attackState == null)
        {
            ReportMissingAttackState();
            return false;
        }

        if (!attackState.IsAttackActive)
            return false;

        if (!requireJumping)
            return true;

        ResolveJumpToPoint();
        return jumpToPoint != null && jumpToPoint.IsJumping;
    }

    private void ResolveAttackState()
    {
        if (attackState != null)
            return;

        attackState = GetComponentInParent<SmilyAttackState>();
        if (attackState == null)
            attackState = GetComponentInParent<JumpToPoint>()?.GetComponent<SmilyAttackState>();
    }

    private void ResolveJumpToPoint()
    {
        if (jumpToPoint != null)
            return;

        jumpToPoint = GetComponentInParent<JumpToPoint>();
    }

    private void ReportMissingAttackState()
    {
        if (_reportedMissingAttackState)
            return;

        _reportedMissingAttackState = true;
        Debug.LogError("[AttackHitbox] Missing SmilyAttackState. Fix the Smily prefab/setup; runtime auto-attach is disabled.", this);
    }

    private static PlayerDeath ResolvePlayerDeath(Collider other)
    {
        if (other == null)
            return null;

        PlayerDeath death = other.GetComponentInParent<PlayerDeath>();
        if (death != null)
            return death;

        PlayerPawn pawn = other.GetComponentInParent<PlayerPawn>();
        if (pawn != null)
        {
            death = pawn.GetComponentInParent<PlayerDeath>();
            if (death != null)
                return death;

            return pawn.GetComponentInChildren<PlayerDeath>(true);
        }

        PlayerVitals vitals = other.GetComponentInParent<PlayerVitals>();
        if (vitals != null)
            return vitals.GetComponentInParent<PlayerDeath>();

        Transform root = other.transform.root;
        return root != null ? root.GetComponentInChildren<PlayerDeath>(true) : null;
    }
}
