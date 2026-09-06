using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ClownAcquireTarget", story: "[Self] acquires a target into [Target] using [RangeDetector] and [LineOfSightDetector]", category: "Action", id: "clown_acquire_target_001")]
public partial class ClownAcquireTargetAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<GameObject> Target;
    [SerializeReference] public BlackboardVariable<RangeDetector> RangeDetector;
    [SerializeReference] public BlackboardVariable<LineOfSightDetector> LineOfSightDetector;
    [SerializeReference] public BlackboardVariable<float> ChaseDistance = new BlackboardVariable<float> { Value = 16f };

    protected override Status OnUpdate()
    {
        if (Self?.Value == null)
            return Status.Failure;

        var actor = Self.Value.GetComponent<ClownMonsterActor>();
        if (actor == null || actor.IsDead)
            return Status.Failure;

        GameObject found = actor.AcquireTarget(RangeDetector?.Value, LineOfSightDetector?.Value, ChaseDistance?.Value ?? actor.ChaseDistance);
        if (found == null)
        {
            Target.Value = null;
            return Status.Failure;
        }

        Target.Value = found;
        return Status.Success;
    }
}
