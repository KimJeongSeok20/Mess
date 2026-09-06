using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ClownRunThrowSequence", story: "[Self] runs its throw sequence and clears [Target] when complete", category: "Action", id: "clown_run_throw_sequence_001")]
public partial class ClownRunThrowSequenceAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<GameObject> Target;

    protected override Status OnUpdate()
    {
        if (Self?.Value == null)
            return Status.Failure;

        var actor = Self.Value.GetComponent<ClownMonsterActor>();
        if (actor == null || actor.IsDead)
            return Status.Failure;

        bool completed = actor.TickThrowSequence();
        if (!completed)
            return Status.Running;

        if (actor.HasPendingRepeatThrow)
            return Status.Success;

        Target.Value = null;
        actor.ClearTarget();
        return Status.Success;
    }
}
