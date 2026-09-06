using UnityEngine;

public interface IMonsterFlowTelemetry
{
    MonsterIntent CurrentIntent { get; }
    bool HasTarget { get; }
    string CurrentTargetName { get; }
    bool IsTraversing { get; }
    bool IsAttacking { get; }
    string CurrentAttackPhase { get; }
    Vector3 DesiredDestination { get; }
}
