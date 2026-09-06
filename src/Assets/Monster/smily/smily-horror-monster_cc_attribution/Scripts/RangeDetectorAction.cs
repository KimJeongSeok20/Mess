using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "RangeDetector", story: "update [RangeDetector] and assign [Target]", category: "Action", id: "d349ee97482fc86860a1fc6b09eff862")]
public partial class RangeDetectorAction : Action
{
    [SerializeReference] public BlackboardVariable<RangeDetector> RangeDetector;
    [SerializeReference] public BlackboardVariable<GameObject> Target;
    [SerializeReference] public BlackboardVariable<DoorAutoOpener> DoorAutoOpener;

    private DoorAutoOpener _cachedDoorAutoOpener;
    private SmilyBrain _cachedBrain;

    private DoorAutoOpener ResolveDoorAutoOpener()
    {
        if (DoorAutoOpener != null && DoorAutoOpener.Value != null)
            return DoorAutoOpener.Value;

        if (_cachedDoorAutoOpener != null)
            return _cachedDoorAutoOpener;

        if (RangeDetector == null || RangeDetector.Value == null)
            return null;

        _cachedDoorAutoOpener = RangeDetector.Value.GetComponent<DoorAutoOpener>();
        if (_cachedDoorAutoOpener == null)
            _cachedDoorAutoOpener = RangeDetector.Value.GetComponentInParent<DoorAutoOpener>();

        return _cachedDoorAutoOpener;
    }

    private SmilyBrain ResolveBrain()
    {
        if (_cachedBrain != null)
            return _cachedBrain;

        if (RangeDetector == null || RangeDetector.Value == null)
            return null;

        _cachedBrain = RangeDetector.Value.GetComponent<SmilyBrain>();
        if (_cachedBrain == null)
            _cachedBrain = RangeDetector.Value.GetComponentInParent<SmilyBrain>();

        return _cachedBrain;
    }


    protected override Status OnUpdate()
    {
        if (RangeDetector == null || RangeDetector.Value == null || Target == null)
            return Status.Failure;

        DoorAutoOpener doorAutoOpener = ResolveDoorAutoOpener();
        if (doorAutoOpener != null && doorAutoOpener.IsTraversing)
        {
            Target.Value = null;
            return Status.Failure;
        }

        SmilyBrain brain = ResolveBrain();
        if (brain != null && (brain.IsAmbushing || brain.IsInRecovery))
        {
            Target.Value = null;
            return Status.Failure;
        }

        GameObject detectedTarget = RangeDetector.Value.UpdateDetector();
        Target.Value = detectedTarget;
        return detectedTarget == null ? Status.Failure : Status.Success;
    }

}

