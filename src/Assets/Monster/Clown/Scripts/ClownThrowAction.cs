using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ClownThrow", story: "[Self] begins clown throw sequence toward [Target]", category: "Action", id: "clown_throw_action_001")]
public partial class ClownThrowAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<GameObject> Target;

    protected override Status OnStart()
    {
        GameObject self = Self?.Value;
        GameObject target = Target?.Value;

        if (self == null || target == null)
            return Status.Failure;

        ClownMonsterActor actor = self.GetComponent<ClownMonsterActor>();
        if (actor == null || actor.IsDead)
            return Status.Failure;

        return actor.BeginThrowSequence(target) ? Status.Success : Status.Failure;
    }
}
