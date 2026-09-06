using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "FleeFromTarget", story: "Flee from [Target]", category: "Action", id: "flee_from_target_20radius_1234")]
public partial class FleeFromTargetAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Target;
    [SerializeReference] public BlackboardVariable<FleeFromTarget> FleeComponent;
    // �� �׷������� ������ ���� �߰�
    [SerializeReference] public BlackboardVariable<float> FleeRadius;
    [SerializeReference] public BlackboardVariable<float> MinDistFromTarget;
    [SerializeReference] public BlackboardVariable<float> FleeDuration;

    protected override Status OnStart()
    {
        if (FleeComponent == null || FleeComponent.Value == null)
            return Status.Failure;
        if (Target == null || Target.Value == null)
            return Status.Failure;

        // �� �׷������� ������ ������ ������Ʈ�� �ݿ�
        var flee = FleeComponent.Value;

        if (FleeRadius != null)
            flee.fleeRadius = FleeRadius.Value;

        if (MinDistFromTarget != null)
            flee.minDistFromTarget = MinDistFromTarget.Value;

        if (FleeDuration != null)
            flee.fleeDuration = FleeDuration.Value;

        // ���� �ϴ� ���� ����
        flee.StartFlee(Target.Value.transform);
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (FleeComponent == null || FleeComponent.Value == null)
            return Status.Failure;

        return FleeComponent.Value.IsFleeing ? Status.Running : Status.Success;
    }
}
