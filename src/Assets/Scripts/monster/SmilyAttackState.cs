using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SmilyAttackState : MonoBehaviour
{
    [SerializeField] private bool debugLog;
    [SerializeField] private float postAttackSuppressionSeconds = 0.35f;

    private Coroutine _attackWindowRoutine;
    private string _activeReason;
    private float _lastAttackEndedAt = -999f;

    public bool IsAttackActive { get; private set; }
    public bool IsRecentlyAttacked => Time.time - _lastAttackEndedAt < Mathf.Max(0f, postAttackSuppressionSeconds);
    public string ActiveReason => IsAttackActive ? _activeReason : string.Empty;

    public void BeginAttackWindow(float duration, string reason)
    {
        if (_attackWindowRoutine != null)
            StopCoroutine(_attackWindowRoutine);

        IsAttackActive = true;
        _activeReason = string.IsNullOrEmpty(reason) ? "Attack" : reason;

        if (debugLog)
            Debug.Log($"[SmilyAttackState] Attack window opened: {_activeReason} ({duration:F2}s)", this);

        if (duration > 0f)
            _attackWindowRoutine = StartCoroutine(CloseAfter(duration));
    }

    public void EndAttackWindow(string reason = null)
    {
        if (_attackWindowRoutine != null)
        {
            StopCoroutine(_attackWindowRoutine);
            _attackWindowRoutine = null;
        }

        bool wasActive = IsAttackActive;

        if (debugLog && wasActive)
            Debug.Log($"[SmilyAttackState] Attack window closed: {reason ?? _activeReason}", this);

        IsAttackActive = false;
        _activeReason = string.Empty;

        if (wasActive)
            _lastAttackEndedAt = Time.time;
    }

    private IEnumerator CloseAfter(float duration)
    {
        yield return new WaitForSeconds(duration);
        _attackWindowRoutine = null;
        EndAttackWindow("TimedOut");
    }

    private void OnDisable()
    {
        EndAttackWindow("Disabled");
    }
}
