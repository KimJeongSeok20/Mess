using System;
using System.Text;
using PurrNet;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class SmilyActionVerificationChecklist
{
    private const string SmilyPrefabPath = "Assets/Monster/smily/smily-horror-monster_cc_attribution/smily_fixed.prefab";
    private const string TestScenePath = "Assets/test_monster.unity";
    private const string StartMapPath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
    private const string SpeedParameter = "SpeedMagnitude";
    private const string AttackTrigger = "Attack";
    private const string TeleportSmileTrigger = "TeleportSmile";
    private const string DieTrigger = "Die";

    [MenuItem("Tools/Monster Test/Smily Action Verification Checklist")]
    public static void LogChecklistMenu()
    {
        Debug.Log(BuildReport(true));
    }

    [MenuItem("Tools/Monster Test/Apply Smily Verification Defaults")]
    public static void ApplyPrefabVerificationDefaultsMenu()
    {
        Debug.Log(ApplyPrefabVerificationDefaults());
    }

    public static string BuildReport()
    {
        return BuildReport(true);
    }

    public static string ApplyPrefabVerificationDefaults()
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(SmilyPrefabPath);
        if (prefabRoot == null)
            return $"Unable to load Smily prefab: {SmilyPrefabPath}";

        try
        {
            SmilyBrain brain = prefabRoot.GetComponent<SmilyBrain>();
            DoorAutoOpener doorAutoOpener = prefabRoot.GetComponent<DoorAutoOpener>();
            NavMeshAgent agent = prefabRoot.GetComponent<NavMeshAgent>();
            Animator animator = prefabRoot.GetComponentInChildren<Animator>(true);
            RangeDetector rangeDetector = prefabRoot.GetComponent<RangeDetector>();
            LineOfSightDetector lineOfSightDetector = prefabRoot.GetComponent<LineOfSightDetector>();
            AssignJumpPoint assignJumpPoint = prefabRoot.GetComponent<AssignJumpPoint>();
            JumpToPoint jumpToPoint = prefabRoot.GetComponent<JumpToPoint>();
            AttackHitbox attackHitbox = prefabRoot.GetComponentInChildren<AttackHitbox>(true);
            FleeFromTarget fleeFromTarget = prefabRoot.GetComponent<FleeFromTarget>();
            SmilyAttackState attackState = GetOrAdd<SmilyAttackState>(prefabRoot);
            NetworkAnimator networkAnimator = prefabRoot.GetComponent<NetworkAnimator>();
            SmilyAudioController audioController = prefabRoot.GetComponent<SmilyAudioController>();
            MonsterHealth health = prefabRoot.GetComponent<MonsterHealth>();
            SmilyWallClearanceController wallClearance = GetOrAdd<SmilyWallClearanceController>(prefabRoot);
            AudioSource audioSource = prefabRoot.GetComponent<AudioSource>();
            if (audioController != null && audioSource == null)
                audioSource = prefabRoot.AddComponent<AudioSource>();

            if (agent != null)
                agent.autoTraverseOffMeshLink = false;

            if (audioSource != null)
            {
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f;
                audioSource.minDistance = 1f;
                audioSource.maxDistance = Mathf.Max(audioSource.minDistance, 18f);
                audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            }

            if (brain != null)
            {
                AssignReference(brain, "health", health);
                AssignReference(brain, "doorAutoOpener", doorAutoOpener);
                AssignReference(brain, "rangeDetector", rangeDetector);
                AssignReference(brain, "lineOfSightDetector", lineOfSightDetector);
                AssignReference(brain, "assignJumpPoint", assignJumpPoint);
                AssignReference(brain, "jumpToPoint", jumpToPoint);
                AssignReference(brain, "attackHitbox", attackHitbox);
                AssignReference(brain, "fleeFromTarget", fleeFromTarget);
                AssignReference(brain, "attackState", attackState);
                AssignReference(brain, "agent", agent);
                AssignReference(brain, "animator", animator);
                AssignReference(brain, "networkAnimator", networkAnimator);
                AssignReference(brain, "audioController", audioController);
                SetSerializedBoolValue(brain, "enableLinkWarningTeleport", true);
                SetSerializedBoolValue(brain, "hideRenderersDuringLinkSnap", true);
                SetSerializedBoolValue(brain, "playWarningBeforeAttackTeleport", true);
            }

            if (doorAutoOpener != null)
            {
                SetSerializedBoolValue(doorAutoOpener, "canOpenDoors", true);
            }

            if (attackHitbox != null)
            {
                AssignReference(attackHitbox, "attackState", attackState);
                AssignReference(attackHitbox, "jumpToPoint", jumpToPoint);
                SetSerializedBoolValue(attackHitbox, "requireAttackState", true);
                SetSerializedBoolValue(attackHitbox, "requireJumping", true);
            }

            if (wallClearance != null)
            {
                AssignReference(wallClearance, "agent", agent);
                AssignReference(wallClearance, "jumpToPoint", jumpToPoint);
                AssignReference(wallClearance, "doorAutoOpener", doorAutoOpener);
            }

            if (audioController != null)
            {
                AssignReference(audioController, "audioSource", audioSource);
                AssignReference(audioController, "loopSource", audioSource);
                AssignReference(audioController, "oneShotSource", audioSource);
            }

            if (health != null)
            {
                AssignReference(health, "smilyAudioController", audioController);
                ClearNullOnlyArray(health, "disableBehavioursOnDeath");
            }

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, SmilyPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return "Applied Smily prefab verification defaults.";
    }

    public static string BuildReport(bool includeCurrentSceneChecks)
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine("# Smily Action Verification Checklist");
        report.AppendLine();
        report.AppendLine("Primary runtime: SmilyBrain. Disabled Behavior Graph assets remain for legacy asset inspection only.");
        report.AppendLine();

        AppendStaticPreflight(report);

        if (includeCurrentSceneChecks)
            AppendCurrentScenePreflight(report);

        AppendRuntimeChecklist(report);
        AppendLegacyChecklist(report);
        AppendCliPlan(report);
        return report.ToString();
    }

    private static void AppendStaticPreflight(StringBuilder report)
    {
        report.AppendLine("## Static Prefab Preflight");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SmilyPrefabPath);
        AppendCheck(report, prefab != null, "Smily prefab asset exists", SmilyPrefabPath);
        if (prefab == null)
        {
            report.AppendLine();
            return;
        }

        SmilyBrain brain = prefab.GetComponent<SmilyBrain>();
        DoorAutoOpener doorAutoOpener = prefab.GetComponent<DoorAutoOpener>();
        NavMeshAgent agent = prefab.GetComponent<NavMeshAgent>();
        Animator animator = prefab.GetComponentInChildren<Animator>(true);
        RangeDetector rangeDetector = prefab.GetComponent<RangeDetector>();
        LineOfSightDetector lineOfSightDetector = prefab.GetComponent<LineOfSightDetector>();
        JumpToPoint jumpToPoint = prefab.GetComponent<JumpToPoint>();
        AttackHitbox attackHitbox = prefab.GetComponentInChildren<AttackHitbox>(true);
        FleeFromTarget fleeFromTarget = prefab.GetComponent<FleeFromTarget>();
        SmilyAttackState attackState = prefab.GetComponent<SmilyAttackState>();
        NetworkAnimator networkAnimator = prefab.GetComponent<NetworkAnimator>();
        SmilyAudioController audioController = prefab.GetComponent<SmilyAudioController>();
        MonsterHealth health = prefab.GetComponent<MonsterHealth>();
        SmilyWallClearanceController wallClearance = prefab.GetComponent<SmilyWallClearanceController>();
        MonoBehaviour behaviorGraphAgent = FindBehaviour(prefab, "BehaviorGraphAgent");

        AppendCheck(report, brain != null, "SmilyBrain component present");
        AppendCheck(report, brain != null && brain.enabled, "SmilyBrain enabled");
        AppendCheck(report, behaviorGraphAgent == null || !behaviorGraphAgent.enabled, "BehaviorGraphAgent disabled or absent", EnabledLabel(behaviorGraphAgent));
        AppendCheck(report, agent != null, "NavMeshAgent present");
        AppendCheck(report, animator != null, "Animator present");
        AppendCheck(report, rangeDetector != null, "RangeDetector present");
        AppendCheck(report, lineOfSightDetector != null, "LineOfSightDetector present");
        AppendCheck(report, jumpToPoint != null, "JumpToPoint present");
        AppendCheck(report, attackHitbox != null, "AttackHitbox present");
        AppendCheck(report, fleeFromTarget != null, "FleeFromTarget present");
        AppendCheck(report, attackState != null, "SmilyAttackState present");
        AppendCheck(report, networkAnimator != null, "NetworkAnimator present");
        AppendCheck(report, doorAutoOpener != null, "DoorAutoOpener present");
        AppendCheck(report, audioController != null, "SmilyAudioController present");
        AppendCheck(report, health != null, "MonsterHealth present");
        AppendCheck(report, wallClearance != null, "SmilyWallClearanceController present");

        if (brain != null)
        {
            AppendSerializedBool(report, brain, "hideRenderersDuringLinkSnap", true, "LinkSnap hides renderers before restore");
            AppendSerializedBool(report, brain, "playWarningBeforeAttackTeleport", true, "Teleport attack uses warning smile windup");
            AppendSerializedBool(report, brain, "enableLinkWarningTeleport", true, "SmilyBrain can react to link traversal encounters");
            AppendSerializedBool(report, brain, "useTeleportSmileAnimationLength", true, "Teleport warning can wait for assigned smile clip length");
            AppendObjectReferencePresence(report, brain, "teleportOutEffectPrefab", true, "Teleport-out smoke prefab explicitly assigned");
            AppendObjectReferencePresence(report, brain, "teleportSmileAnimationClip", false, "Teleport smile animation clip slot exposed");
            AppendObjectReferencePresence(report, brain, "teleportInEffectPrefab", false, "Teleport-in VFX slot exposed");
            AppendObjectReferencePresence(report, brain, "attackPortalEffectPrefab", true, "Attack portal VFX prefab assigned");
            AppendSerializedFloatRange(report, brain, "targetMemoryDuration", 0.25f, 10f, "Smily target memory has a bounded investigation window");
            AppendSerializedFloatRange(report, brain, "investigationSearchDuration", 0.1f, 5f, "Smily pauses to search at the last known position");
            AppendSerializedFloatRange(report, brain, "investigationArrivalDistance", 0.1f, 3f, "Smily investigation arrival threshold is usable");
            AppendSerializedFloatMinimum(report, brain, "investigationTurnSpeed", 1f, "Smily visibly scans during investigation");
            AppendSerializedFloatMinimum(report, brain, "bodyLookTurnSpeed", 1f, "Body look turn speed is non-snap capable");
            AppendSerializedFloatRange(report, brain, "attackFacingAngleTolerance", 1f, 45f, "Attack windup has facing tolerance");
            AppendObjectReference(report, brain, "networkAnimator", networkAnimator, "SmilyBrain uses NetworkAnimator for speed and triggers");
        }

        if (doorAutoOpener != null)
        {
            AppendSerializedBool(report, doorAutoOpener, "canOpenDoors", true, "DoorAutoOpener can request door open");
            AppendCheck(report, agent == null || !agent.autoTraverseOffMeshLink, "NavMeshAgent autoTraverseOffMeshLink disabled on prefab", agent == null ? "missing agent" : $"autoTraverse={agent.autoTraverseOffMeshLink}", true);
        }

        if (attackHitbox != null)
        {
            AppendObjectReference(report, attackHitbox, "jumpToPoint", jumpToPoint, "AttackHitbox has JumpToPoint gate reference");
            AppendSerializedBool(report, attackHitbox, "requireAttackState", true, "AttackHitbox requires SmilyAttackState gate");
            AppendSerializedBool(report, attackHitbox, "requireJumping", true, "AttackHitbox requires active jump gate");
        }

        if (health != null)
        {
            AppendSerializedBool(report, health, "disableAnimatorOnDeath", false, "Death animation is not cut by immediate Animator disable");
            AppendObjectReference(report, health, "smilyAudioController", audioController, "MonsterHealth can trigger Smily death/gore audio");
            AppendNullOnlyArrayCheck(report, health, "disableBehavioursOnDeath", "Death AI disable list is empty or has real entries");
        }

        if (audioController != null)
        {
            AudioSource source = prefab.GetComponent<AudioSource>();
            AppendCheck(report, source != null, "AudioSource present for SmilyAudioController", source != null ? AudioSourceLabel(source) : "missing AudioSource", true);
            if (source != null)
                AppendCheck(report, Mathf.Approximately(source.spatialBlend, 1f), "AudioSource is 3D", AudioSourceLabel(source), true);

            AppendObjectReference(report, audioController, "loopSource", source, "SmilyAudioController loop source assigned");
            AppendObjectReference(report, audioController, "oneShotSource", source, "SmilyAudioController one-shot source assigned");
            AppendArraySlot(report, audioController, "idleLoopClips", "Idle loop SFX slot exposed");
            AppendArraySlot(report, audioController, "patrolLoopClips", "Patrol loop SFX slot exposed");
            AppendArraySlot(report, audioController, "attackWindupClips", "Attack windup SFX slot exposed");
            AppendArraySlot(report, audioController, "attackCommitClips", "Attack commit SFX slot exposed");
            AppendArraySlot(report, audioController, "deathClips", "Death SFX slot exposed");
            AppendArraySlot(report, audioController, "goreExplosionClips", "Gore explosion SFX slot exposed");
        }

        if (animator != null)
        {
            AppendCheck(report, HasAnimatorParameter(animator, SpeedParameter, AnimatorControllerParameterType.Float), $"Animator has {SpeedParameter} float");
            AppendCheck(report, HasAnimatorParameter(animator, AttackTrigger, AnimatorControllerParameterType.Trigger), $"Animator has {AttackTrigger} trigger");
            AppendCheck(report, HasAnimatorParameter(animator, TeleportSmileTrigger, AnimatorControllerParameterType.Trigger), $"Animator has {TeleportSmileTrigger} trigger");
            AppendCheck(report, HasAnimatorParameter(animator, DieTrigger, AnimatorControllerParameterType.Trigger), $"Animator has {DieTrigger} trigger");
        }

        report.AppendLine();
    }

    private static void AppendCurrentScenePreflight(StringBuilder report)
    {
        report.AppendLine("## Current Scene Preflight");
        Scene scene = SceneManager.GetActiveScene();
        string scenePath = string.IsNullOrEmpty(scene.path) ? "(unsaved scene)" : scene.path;
        AppendInfo(report, $"Active scene: {scenePath}");
        AppendInfo(report, $"Expected focused scene: {TestScenePath}");
        AppendInfo(report, $"Dungeon smoke scene: {StartMapPath}");

        int missingScriptCount = CountMissingScripts(scene);
        AppendCheck(report, missingScriptCount == 0, "Current scene has no missing scripts", $"missing={missingScriptCount}");

        MonsterTestSceneController controller = UnityEngine.Object.FindFirstObjectByType<MonsterTestSceneController>(FindObjectsInactive.Include);
        AppendCheck(report, scene.path != TestScenePath || controller != null, "test_monster scene has MonsterTestSceneController", controller != null ? controller.name : "not found", scene.path != TestScenePath);

        NavMeshLink[] links = UnityEngine.Object.FindObjectsByType<NavMeshLink>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int doorBindings = 0;
        int linksWithBinding = 0;
        int doorBindingsMissingPassage = 0;
        for (int i = 0; i < links.Length; i++)
        {
            NavMeshLink link = links[i];
            if (link == null)
                continue;

            MonsterDoorLinkBinding binding = link.GetComponent<MonsterDoorLinkBinding>();
            if (binding != null)
                linksWithBinding++;

            if (binding != null && binding.CanOpen)
            {
                doorBindings++;
                if (!binding.HasPassageCollider)
                    doorBindingsMissingPassage++;
            }
        }

        AppendCheck(report, scene.path != TestScenePath || links.Length > 0, "test_monster scene has NavMeshLink fixtures", $"links={links.Length}", scene.path != TestScenePath);
        AppendCheck(report, scene.path != TestScenePath || linksWithBinding > 0, "door links have MonsterDoorLinkBinding", $"bindings={linksWithBinding}", scene.path != TestScenePath);
        AppendCheck(report, scene.path != TestScenePath || doorBindings > 0, "door link binding can open at least one door", $"canOpen={doorBindings}", scene.path != TestScenePath);
        AppendCheck(report, scene.path != TestScenePath || doorBindingsMissingPassage == 0, "door link bindings declare passage colliders", $"missingPassage={doorBindingsMissingPassage}", scene.path != TestScenePath);
        report.AppendLine();
    }

    private static void AppendRuntimeChecklist(StringBuilder report)
    {
        report.AppendLine("## Runtime Action Checks");
        AppendTodo(report, "Spawn/Ownership", "Spawn Smily in Assets/test_monster.unity; SmilyBrain is enabled and BehaviorGraphAgent is disabled or absent.");
        AppendTodo(report, "Spawn/Ownership", "Runtime refs resolve: NavMeshAgent, Animator, RangeDetector, LineOfSightDetector, JumpToPoint, AttackHitbox, FleeFromTarget, SmilyAttackState.");
        AppendTodo(report, "Idle/Patrol/Chase", "No target keeps Idle/Patrol without broken path; detected target enters Chase; no LOS prevents attack/teleport.");
        AppendTodo(report, "Idle/Patrol/Chase", "LOS plus chase/seen time starts attack flow; SpeedMagnitude changes for idle, patrol, chase, flee.");
        AppendTodo(report, "Investigate", "After LOS loss, destination stays at the last visible position even while the hidden player keeps moving.");
        AppendTodo(report, "Investigate", "Smily scans at the remembered position, reacquires a visible player, or returns to Patrol after the search expires.");
        AppendTodo(report, "Look/Turn", "Body rotates smoothly toward player without snapping during chase and windups.");
        AppendTodo(report, "Look/Turn", "LinkWarningSmile, TeleportWindup, AttackWindup keep target facing; AttackWindup waits for facing tolerance before jump.");
        AppendTodo(report, "Look/Turn", "Head look follows target within max angle.");
        AppendTodo(report, "General Teleport Attack", "Detection enters TeleportWindup; TeleportSmile trigger fires; movement stopped; damage inactive during warning.");
        AppendTodo(report, "General Teleport Attack", "Warning start has no smoke; actual teleport moment has smoke/SFX; sequence continues AttackWindup -> JumpAttack -> AttackRecover.");
        AppendTodo(report, "NavMeshLink/Door Traversal", "Closed door link waits, requests open, then traverses manually after door opens.");
        AppendTodo(report, "NavMeshLink/Door Traversal", "During link: IsTraversing true, IsMovingAcrossLink true, agent.updatePosition false.");
        AppendTodo(report, "NavMeshLink/Door Traversal", "After link: CompleteOffMeshLink called, updatePosition restored, no stuck state.");
        AppendTodo(report, "NavMeshLink/Door Traversal", "Door link relaxes door-frame clearance; generic/jump link keeps body clearance.");
        AppendTodo(report, "NavMeshLink/Door Traversal", "Player detection during link routes LinkWarningSmile -> LinkSnap -> AttackWindup -> JumpAttack.");
        AppendTodo(report, "NavMeshLink/Door Traversal", "LinkSnap hides renderers then restores them; snap/teleport grace ignores player collision then restores.");
        AppendTodo(report, "Jump/Attack Damage", "AttackWindup, TeleportWindup, LinkWarningSmile, LinkSnap, Patrol, Chase, Flee never damage.");
        AppendTodo(report, "Jump/Attack Damage", "JumpAttack opens SmilyAttackState.IsAttackActive; jump complete/cancel/timeout closes it.");
        AppendTodo(report, "Jump/Attack Damage", "AttackHitbox damages/kills only while attack window is open and prevents duplicate hit per attack.");
        AppendTodo(report, "Jump/Attack Damage", "Hit flag resets after BeginFleeOrReevaluate.");
        AppendTodo(report, "Combo/Flee", "Miss/non-hit can repeat by chance; max consecutive attacks forces flee; hit forces immediate flee.");
        AppendTodo(report, "Combo/Flee", "Flee speed uses patrol multiplier; flee animation playback speed applies; re-aggro cooldown starts only after flee.");
        AppendTodo(report, "Death", "MonsterHealth.TakeDamage reaches Dead state, Die trigger fires, death animation is not cut by immediate Animator disable.");
        AppendTodo(report, "Death", "Corpse, gore, and drop behavior remain intact.");
        AppendTodo(report, "Audio/Visual Hooks", "PlayDetect, PlayWarningSmile, PlayTeleport, PlayJumpAttack, PlayJumpLand, PlayFleeStart, PlayDoorOpen produce no console error, including empty clip arrays.");
        AppendTodo(report, "Audio/Visual Hooks", "TeleportSmile and Die placeholders do not pop to bind pose; smoke/steam appears at actual teleport moment.");
        AppendTodo(report, "Wall Clearance", "Normal movement keeps head/front/body/arms out of walls; door traversal tolerates frame but not walls.");
        AppendTodo(report, "Wall Clearance", "Blocked cases avoid jitter, forward/back loops, and permanent stop.");
        report.AppendLine();
    }

    private static void AppendLegacyChecklist(StringBuilder report)
    {
        report.AppendLine("## Legacy Behavior Graph Action Checks");
        AppendTodo(report, "RangeDetectorAction", "SmilyBrain ambush/recovery target clear does not corrupt graph flow.");
        AppendTodo(report, "LineOfSightCheckCondition", "Face-origin LOS and wall/door blockers are respected.");
        AppendTodo(report, "AssignJumpPointAction", "Null guards and jump point calculation failure are handled.");
        AppendTodo(report, "JumpToPointAction", "Target look direction is passed so jump facing is correct.");
        AppendTodo(report, "AttackHitAction", "No successful hit outside attack window.");
        AppendTodo(report, "FleeFromTargetAction", "Flee start/end, door handling, and cooldown remain correct.");
        report.AppendLine();
    }

    private static void AppendCliPlan(StringBuilder report)
    {
        report.AppendLine("## Suggested unity-cli Verification");
        report.AppendLine("- unity-cli status");
        report.AppendLine("- unity-cli editor refresh --compile");
        report.AppendLine("- unity-cli console --type error --stacktrace user");
        report.AppendLine("- unity-cli exec \"return SmilyActionVerificationChecklist.BuildReport(false);\"");
        report.AppendLine("- unity-cli editor play --wait");
        report.AppendLine("- unity-cli exec \"return SmilyActionRuntimeDiagnostics.Snapshot();\"");
        report.AppendLine("- unity-cli exec \"return SmilyActionRuntimeDiagnostics.LinkSummary();\"");
        report.AppendLine("- unity-cli exec \"return SmilyActionRuntimeDiagnostics.DamageGateSummary();\"");
        report.AppendLine("- unity-cli editor stop");
    }

    private static void AppendSerializedBool(StringBuilder report, Component component, string propertyName, bool expected, string label)
    {
        SerializedProperty property = FindSerializedProperty(component, propertyName);
        if (property == null || property.propertyType != SerializedPropertyType.Boolean)
        {
            AppendCheck(report, false, label, $"missing bool {propertyName}", true);
            return;
        }

        AppendCheck(report, property.boolValue == expected, label, $"{propertyName}={property.boolValue}");
    }

    private static void AppendObjectReference(StringBuilder report, Component component, string propertyName, UnityEngine.Object expected, string label)
    {
        SerializedProperty property = FindSerializedProperty(component, propertyName);
        if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
        {
            AppendCheck(report, false, label, $"missing object reference {propertyName}", true);
            return;
        }

        bool hasExpectedReference = expected != null && property.objectReferenceValue == expected;
        string detail = property.objectReferenceValue != null
            ? $"{propertyName}={property.objectReferenceValue.name}"
            : $"{propertyName}=missing";
        AppendCheck(report, hasExpectedReference, label, detail);
    }

    private static void AppendObjectReferencePresence(StringBuilder report, Component component, string propertyName, bool required, string label)
    {
        SerializedProperty property = FindSerializedProperty(component, propertyName);
        if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
        {
            AppendCheck(report, false, label, $"missing object reference {propertyName}", true);
            return;
        }

        bool hasReference = property.objectReferenceValue != null;
        string detail = hasReference
            ? $"{propertyName}={property.objectReferenceValue.name}"
            : $"{propertyName}=empty";
        AppendCheck(report, !required || hasReference, label, detail, !required);
    }

    private static void AppendArraySlot(StringBuilder report, Component component, string propertyName, string label)
    {
        SerializedProperty property = FindSerializedProperty(component, propertyName);
        if (property == null || !property.isArray)
        {
            AppendCheck(report, false, label, $"missing array {propertyName}", true);
            return;
        }

        AppendCheck(report, true, label, $"{propertyName}.size={property.arraySize}");
    }

    private static void AppendNullOnlyArrayCheck(StringBuilder report, Component component, string propertyName, string label)
    {
        SerializedProperty property = FindSerializedProperty(component, propertyName);
        if (property == null || !property.isArray)
        {
            AppendCheck(report, false, label, $"missing array {propertyName}", true);
            return;
        }

        bool hasRealEntry = false;
        for (int i = 0; i < property.arraySize; i++)
        {
            SerializedProperty element = property.GetArrayElementAtIndex(i);
            if (element != null && element.propertyType == SerializedPropertyType.ObjectReference && element.objectReferenceValue != null)
            {
                hasRealEntry = true;
                break;
            }
        }

        bool passes = property.arraySize == 0 || hasRealEntry;
        AppendCheck(report, passes, label, $"{propertyName}.size={property.arraySize}");
    }

    private static T GetOrAdd<T>(GameObject root) where T : Component
    {
        if (root == null)
            return null;

        T component = root.GetComponent<T>();
        if (component != null)
            return component;

        return root.AddComponent<T>();
    }

    private static void AssignReference(Component component, string propertyName, UnityEngine.Object value)
    {
        if (component == null || string.IsNullOrEmpty(propertyName))
            return;

        SerializedObject serializedObject = new SerializedObject(component);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
            return;

        property.objectReferenceValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetSerializedBoolValue(Component component, string propertyName, bool value)
    {
        if (component == null || string.IsNullOrEmpty(propertyName))
            return;

        SerializedObject serializedObject = new SerializedObject(component);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || property.propertyType != SerializedPropertyType.Boolean)
            return;

        property.boolValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ClearNullOnlyArray(Component component, string propertyName)
    {
        if (component == null || string.IsNullOrEmpty(propertyName))
            return;

        SerializedObject serializedObject = new SerializedObject(component);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || !property.isArray || property.arraySize == 0)
            return;

        for (int i = 0; i < property.arraySize; i++)
        {
            SerializedProperty element = property.GetArrayElementAtIndex(i);
            if (element != null && element.propertyType == SerializedPropertyType.ObjectReference && element.objectReferenceValue != null)
                return;
        }

        property.ClearArray();
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AppendSerializedFloatMinimum(StringBuilder report, Component component, string propertyName, float minimum, string label)
    {
        SerializedProperty property = FindSerializedProperty(component, propertyName);
        if (property == null || property.propertyType != SerializedPropertyType.Float)
        {
            AppendCheck(report, false, label, $"missing float {propertyName}", true);
            return;
        }

        AppendCheck(report, property.floatValue >= minimum, label, $"{propertyName}={property.floatValue:F2}");
    }

    private static void AppendSerializedFloatRange(StringBuilder report, Component component, string propertyName, float minimum, float maximum, string label)
    {
        SerializedProperty property = FindSerializedProperty(component, propertyName);
        if (property == null || property.propertyType != SerializedPropertyType.Float)
        {
            AppendCheck(report, false, label, $"missing float {propertyName}", true);
            return;
        }

        bool inRange = property.floatValue >= minimum && property.floatValue <= maximum;
        AppendCheck(report, inRange, label, $"{propertyName}={property.floatValue:F2}");
    }

    private static SerializedProperty FindSerializedProperty(Component component, string propertyName)
    {
        if (component == null || string.IsNullOrEmpty(propertyName))
            return null;

        SerializedObject serializedObject = new SerializedObject(component);
        return serializedObject.FindProperty(propertyName);
    }

    private static void AppendCheck(StringBuilder report, bool passed, string label, string detail = null, bool warningWhenFailed = false)
    {
        string status = passed ? "PASS" : warningWhenFailed ? "WARN" : "FAIL";
        report.Append("- [").Append(status).Append("] ").Append(label);
        if (!string.IsNullOrEmpty(detail))
            report.Append(" - ").Append(detail);
        report.AppendLine();
    }

    private static void AppendTodo(StringBuilder report, string group, string label)
    {
        report.Append("- [TODO] ").Append(group).Append(": ").Append(label).AppendLine();
    }

    private static void AppendInfo(StringBuilder report, string label)
    {
        report.Append("- [INFO] ").AppendLine(label);
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

            Type type = behaviour.GetType();
            if (type.Name == typeName || type.FullName != null && type.FullName.EndsWith("." + typeName, StringComparison.Ordinal))
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

    private static int CountMissingScripts(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return 0;

        int count = 0;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform[] transforms = roots[i].GetComponentsInChildren<Transform>(true);
            for (int j = 0; j < transforms.Length; j++)
            {
                Transform current = transforms[j];
                if (current != null)
                    count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(current.gameObject);
            }
        }

        return count;
    }

    private static string EnabledLabel(Behaviour behaviour)
    {
        if (behaviour == null)
            return "missing";

        return behaviour.enabled ? "enabled" : "disabled";
    }

    private static string AudioSourceLabel(AudioSource source)
    {
        if (source == null)
            return "missing";

        return $"spatialBlend={source.spatialBlend:F2} min={source.minDistance:F2} max={source.maxDistance:F2}";
    }
}
