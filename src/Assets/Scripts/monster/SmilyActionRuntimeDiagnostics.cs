using System.Text;
using UnityEngine;
using UnityEngine.AI;

public static class SmilyActionRuntimeDiagnostics
{
    private const string SpeedParameter = "SpeedMagnitude";

    public static string Snapshot()
    {
        StringBuilder report = new StringBuilder();
        SmilyBrain[] brains = Object.FindObjectsByType<SmilyBrain>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        report.AppendLine("=== Smily Runtime Snapshot ===");
        report.AppendLine($"Playing: {Application.isPlaying}");
        report.AppendLine($"SmilyBrain count: {brains.Length}");

        if (brains.Length == 0)
        {
            report.AppendLine("No SmilyBrain instances found in the active scene.");
            return report.ToString();
        }

        for (int i = 0; i < brains.Length; i++)
        {
            AppendBrain(report, brains[i], i);
        }

        return report.ToString();
    }

    public static string SnapshotFirst()
    {
        SmilyBrain brain = Object.FindFirstObjectByType<SmilyBrain>(FindObjectsInactive.Include);
        if (brain == null)
            return "No SmilyBrain instances found in the active scene.";

        StringBuilder report = new StringBuilder();
        AppendBrain(report, brain, 0);
        return report.ToString();
    }

    public static string DamageGateSummary()
    {
        SmilyBrain brain = Object.FindFirstObjectByType<SmilyBrain>(FindObjectsInactive.Include);
        if (brain == null)
            return "No SmilyBrain instances found in the active scene.";

        SmilyAttackState attackState = brain.GetComponent<SmilyAttackState>();
        AttackHitbox attackHitbox = brain.GetComponentInChildren<AttackHitbox>(true);
        JumpToPoint jumpToPoint = brain.GetComponent<JumpToPoint>();

        bool attackWindowOpen = attackState != null && attackState.IsAttackActive;
        bool jumping = jumpToPoint != null && jumpToPoint.IsJumping;
        bool hitFlag = attackHitbox != null && attackHitbox.HasHitThisAttack;
        bool damageAllowed = attackWindowOpen && jumping;

        return $"State={brain.CurrentStateLabel} AttackWindow={attackWindowOpen} Jumping={jumping} HitFlag={hitFlag} DamageAllowedNow={damageAllowed}";
    }

    public static string LinkSummary()
    {
        SmilyBrain brain = Object.FindFirstObjectByType<SmilyBrain>(FindObjectsInactive.Include);
        if (brain == null)
            return "No SmilyBrain instances found in the active scene.";

        DoorAutoOpener doorAutoOpener = brain.GetComponent<DoorAutoOpener>();
        NavMeshAgent agent = brain.GetComponent<NavMeshAgent>();

        if (doorAutoOpener == null && agent == null)
            return $"{brain.name}: missing DoorAutoOpener and NavMeshAgent.";

        return $"{brain.name}: State={brain.CurrentStateLabel} " +
            $"Traversing={Bool(doorAutoOpener != null && doorAutoOpener.IsTraversing)} " +
            $"MovingAcrossLink={Bool(doorAutoOpener != null && doorAutoOpener.IsMovingAcrossLink)} " +
            $"WaitingForDoor={Bool(doorAutoOpener != null && doorAutoOpener.IsWaitingForDoor)} " +
            $"AgentOnOffMeshLink={Bool(agent != null && agent.enabled && agent.isOnNavMesh && agent.isOnOffMeshLink)} " +
            $"AgentUpdatePosition={Bool(agent != null && agent.updatePosition)} " +
            $"AgentAutoTraverse={Bool(agent != null && agent.autoTraverseOffMeshLink)}";
    }

    private static void AppendBrain(StringBuilder report, SmilyBrain brain, int index)
    {
        if (brain == null)
            return;

        GameObject root = brain.gameObject;
        NavMeshAgent agent = root.GetComponent<NavMeshAgent>();
        Animator animator = root.GetComponentInChildren<Animator>(true);
        RangeDetector rangeDetector = root.GetComponent<RangeDetector>();
        LineOfSightDetector lineOfSightDetector = root.GetComponent<LineOfSightDetector>();
        DoorAutoOpener doorAutoOpener = root.GetComponent<DoorAutoOpener>();
        JumpToPoint jumpToPoint = root.GetComponent<JumpToPoint>();
        AttackHitbox attackHitbox = root.GetComponentInChildren<AttackHitbox>(true);
        FleeFromTarget fleeFromTarget = root.GetComponent<FleeFromTarget>();
        SmilyAttackState attackState = root.GetComponent<SmilyAttackState>();
        MonsterHealth health = root.GetComponent<MonsterHealth>();
        MonoBehaviour behaviorGraphAgent = FindBehaviour(root, "BehaviorGraphAgent");

        report.AppendLine();
        report.AppendLine($"[{index}] {GetPath(root.transform)} active={root.activeInHierarchy} brainEnabled={brain.enabled}");
        report.AppendLine($"State={brain.CurrentStateLabel} Target={TargetName(brain)} Dead={Bool(health != null && health.IsDead)}");
        report.AppendLine($"Ownership: BehaviorGraphAgent={EnabledLabel(behaviorGraphAgent)}");
        report.AppendLine($"Detection: RangeTarget={NameOrNone(rangeDetector != null ? rangeDetector.DetectedTarget : null)} LOS={LineOfSightLabel(lineOfSightDetector, rangeDetector)}");
        report.AppendLine($"Investigation: Memory={Bool(brain.HasLastKnownTargetPosition)} Searching={Bool(brain.IsSearchingLastKnownPosition)} LastKnown={(brain.HasLastKnownTargetPosition ? Format(brain.LastKnownTargetPosition) : "n/a")}");
        report.AppendLine($"Attack: Active={Bool(attackState != null && attackState.IsAttackActive)} Reason={AttackReason(attackState)} Jumping={Bool(jumpToPoint != null && jumpToPoint.IsJumping)} HitThisAttack={Bool(attackHitbox != null && attackHitbox.HasHitThisAttack)}");
        report.AppendLine($"Flee: Fleeing={Bool(fleeFromTarget != null && fleeFromTarget.IsFleeing)} ReAggroCooldown={Bool(fleeFromTarget != null && fleeFromTarget.IsInReAggroCooldown)}");
        report.AppendLine($"Link: Traversing={Bool(doorAutoOpener != null && doorAutoOpener.IsTraversing)} MovingAcrossLink={Bool(doorAutoOpener != null && doorAutoOpener.IsMovingAcrossLink)} WaitingForDoor={Bool(doorAutoOpener != null && doorAutoOpener.IsWaitingForDoor)}");
        report.AppendLine(GetAgentLine(agent));
        report.AppendLine(GetAnimatorLine(animator));
    }

    private static string GetAgentLine(NavMeshAgent agent)
    {
        if (agent == null)
            return "Agent: missing";

        bool activeOnNavMesh = agent.enabled && agent.isOnNavMesh;
        string destination = activeOnNavMesh ? Format(agent.destination) : "n/a";
        string remainingDistance = activeOnNavMesh ? agent.remainingDistance.ToString("F2") : "n/a";
        return $"Agent: enabled={Bool(agent.enabled)} onNavMesh={Bool(activeOnNavMesh)} hasPath={Bool(activeOnNavMesh && agent.hasPath)} " +
            $"pathPending={Bool(activeOnNavMesh && agent.pathPending)} onOffMeshLink={Bool(activeOnNavMesh && agent.isOnOffMeshLink)} " +
            $"stopped={Bool(activeOnNavMesh && agent.isStopped)} updatePosition={Bool(agent.updatePosition)} autoTraverse={Bool(agent.autoTraverseOffMeshLink)} " +
            $"speed={agent.speed:F2} velocity={Format(agent.velocity)} remaining={remainingDistance} destination={destination}";
    }

    private static string GetAnimatorLine(Animator animator)
    {
        if (animator == null)
            return "Animator: missing";

        string speedMagnitude = HasAnimatorParameter(animator, SpeedParameter, AnimatorControllerParameterType.Float)
            ? animator.GetFloat(SpeedParameter).ToString("F2")
            : "missing";

        return $"Animator: enabled={Bool(animator.enabled)} speed={animator.speed:F2} {SpeedParameter}={speedMagnitude}";
    }

    private static string LineOfSightLabel(LineOfSightDetector lineOfSightDetector, RangeDetector rangeDetector)
    {
        if (lineOfSightDetector == null)
            return "missing";

        if (rangeDetector == null || rangeDetector.DetectedTarget == null)
            return "no range target";

        return lineOfSightDetector.PerformDetection(rangeDetector.DetectedTarget) != null ? "visible" : "blocked";
    }

    private static string TargetName(SmilyBrain brain)
    {
        if (brain == null || !brain.HasTarget || string.IsNullOrEmpty(brain.CurrentTargetName))
            return "none";

        return brain.CurrentTargetName;
    }

    private static string AttackReason(SmilyAttackState attackState)
    {
        if (attackState == null || !attackState.IsAttackActive)
            return "none";

        return string.IsNullOrEmpty(attackState.ActiveReason) ? "active" : attackState.ActiveReason;
    }

    private static MonoBehaviour FindBehaviour(GameObject root, string typeName)
    {
        if (root == null)
            return null;

        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
                continue;

            string currentTypeName = behaviour.GetType().Name;
            string currentFullName = behaviour.GetType().FullName;
            if (currentTypeName == typeName || currentFullName != null && currentFullName.EndsWith("." + typeName))
                return behaviour;
        }

        return null;
    }

    private static bool HasAnimatorParameter(Animator animator, string name, AnimatorControllerParameterType type)
    {
        if (animator == null || string.IsNullOrEmpty(name))
            return false;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == type && parameter.name == name)
                return true;
        }

        return false;
    }

    private static string EnabledLabel(Behaviour behaviour)
    {
        if (behaviour == null)
            return "missing";

        return behaviour.enabled ? "enabled" : "disabled";
    }

    private static string NameOrNone(Object value)
    {
        return value != null ? value.name : "none";
    }

    private static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string Format(Vector3 value)
    {
        return $"({value.x:F2}, {value.y:F2}, {value.z:F2})";
    }

    private static string GetPath(Transform transform)
    {
        if (transform == null)
            return string.Empty;

        StringBuilder path = new StringBuilder(transform.name);
        Transform current = transform.parent;
        while (current != null)
        {
            path.Insert(0, current.name + "/");
            current = current.parent;
        }

        return path.ToString();
    }
}
