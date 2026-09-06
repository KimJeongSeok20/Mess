using System;
using Unity.Behavior;
using UnityEngine;

[Serializable, Unity.Properties.GeneratePropertyBag]
[Condition(name: "LineOfSightCheck", story: "Check [Target] with [LineOfSightDetector]", category: "Conditions", id: "1800a66bbbb6660a5258c065cdc9eae6")]
public partial class LineOfSightCheckCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Target;
    [SerializeReference] public BlackboardVariable<LineOfSightDetector> LineOfSightDetector;

    private FleeFromTarget _cachedFlee;

    public override bool IsTrue()
    {
        if (Target == null || Target.Value == null || LineOfSightDetector == null || LineOfSightDetector.Value == null)
            return false;

        if (_cachedFlee == null && LineOfSightDetector != null && LineOfSightDetector.Value != null)
        {
            _cachedFlee = LineOfSightDetector.Value.GetComponent<FleeFromTarget>();
            if (_cachedFlee == null)
                _cachedFlee = LineOfSightDetector.Value.GetComponentInParent<FleeFromTarget>();
        }

        if (_cachedFlee != null && _cachedFlee.IsInReAggroCooldown)
            return false;

        return LineOfSightDetector.Value.PerformDetection(Target.Value) != null;
    }
}
