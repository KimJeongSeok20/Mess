using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ClownMoveToTarget", story: "[Self] moves toward [Target] until throw-ready using [LineOfSightDetector]", category: "Action", id: "clown_move_to_target_001")]
public partial class ClownMoveToTargetAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<GameObject> Target;
    [SerializeReference] public BlackboardVariable<LineOfSightDetector> LineOfSightDetector;

    protected override Status OnUpdate()
    {
        if (Self?.Value == null)
            return Status.Failure;

        var actor = Self.Value.GetComponent<ClownMonsterActor>();
        if (actor == null || actor.IsDead)
            return Status.Failure;

        if (Target?.Value == null)
            return Status.Failure;

        bool valid = actor.MoveTowardsTarget(Target.Value, LineOfSightDetector?.Value, out bool readyToThrow);
        if (!valid)
        {
            Target.Value = null;
            return Status.Failure;
        }

        return readyToThrow ? Status.Success : Status.Running;
    }
}
