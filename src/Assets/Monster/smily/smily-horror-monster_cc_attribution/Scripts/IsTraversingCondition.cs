using System;
using Unity.Behavior;
using UnityEngine;

[Serializable, Unity.Properties.GeneratePropertyBag]
[Condition(name: "IsTraversing", story: "Check if [DoorAutoOpener] is traversing", category: "Conditions", id: "d8e2f4a1b3c5d7e9f1a2b3c4d5e6f7a8")]
public partial class IsTraversingCondition : Condition
{
    [SerializeReference] public BlackboardVariable<DoorAutoOpener> DoorAutoOpener;

    public override bool IsTrue()
    {
        if (DoorAutoOpener == null || DoorAutoOpener.Value == null)
            return false;

        return DoorAutoOpener.Value.IsTraversing;
    }
}
