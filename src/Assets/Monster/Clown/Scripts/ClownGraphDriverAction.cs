using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ClownGraphDriver", story: "Drive clown runtime behavior", category: "Action", id: "clown_graph_driver_action_001")]
public partial class ClownGraphDriverAction : Action
{
    private ClownMonsterActor _actor;

    protected override Status OnStart()
    {
        _actor = GameObject != null ? GameObject.GetComponent<ClownMonsterActor>() : null;
        return _actor != null ? Status.Running : Status.Failure;
    }

    protected override Status OnUpdate()
    {
        if (_actor == null)
            return Status.Failure;

        _actor.TickBehaviorLoop();
        return Status.Running;
    }

    protected override void OnEnd()
    {
        _actor?.ClearTarget();
    }
}
