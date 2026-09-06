using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "AssignJumpPoint", story: "[CalculateJumpPoint] from [Target] and store in [JumpPoint]", category: "Action", id: "9f94fd5d8deaeb878c531f4e05d9441f")]
public partial class AssignJumpPointAction : Action
{
    [SerializeReference] public BlackboardVariable<AssignJumpPoint> CalculateJumpPoint;
    [SerializeReference] public BlackboardVariable<GameObject> Target;
    [SerializeReference] public BlackboardVariable<Vector3> JumpPoint;
    

    // Ÿ�� (��κ� �÷��̾�)
    // ���� ���� ��ġ�� ������ ������� ����
    protected override Status OnUpdate()
    {
        /*Debug.Log("[Calculate Jump]", Calculate_jump_point);
        Debug.Log("[Calculate Jump]", Calculate_jump_point.Value);
        if (Calculate_jump_point == null || Calculate_jump_point.Value == null)
            return Status.Failure;*/

        if (CalculateJumpPoint == null || CalculateJumpPoint.Value == null)
            return Status.Failure;

        if (Target == null || Target.Value == null)
            return Status.Failure;

        if (JumpPoint == null)
            return Status.Failure;

        // ���� ���� ���� ���
        if (!CalculateJumpPoint.Value.TryComputeJumpPoint(Target.Value, out Vector3 p))
            return Status.Failure;

        JumpPoint.Value = p;

        return Status.Success;
    }
}
