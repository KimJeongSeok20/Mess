using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ClownSkin2CombinedPrefabSetup
{
    private const string SourcePrefabPath = "Assets/Clown/Prefab/Clown skin2.prefab";
    private const string CombinedModelPath = "Assets/Clown/Base mesh/ClownSkin2_Combined.fbx";
    private const string TargetPrefabPath = "Assets/Clown/Prefab/Clown skin2 combined.prefab";
    private const string StartMapPath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
    private const string TargetName = "Clown skin2 combined";

    [MenuItem("Tools/Clown/Build Combined Clown Prefab")]
    public static void BuildMenu()
    {
        Debug.Log(Run());
    }

    public static string Run()
    {
        string importerResult = ConfigureCombinedModelImporter();
        string prefabResult = BuildCombinedPrefab();
        string spawnerResult = ReplaceStartMapSpawnerReference();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return importerResult + " | " + prefabResult + " | " + spawnerResult + " | " + Report();
    }

    private static string ConfigureCombinedModelImporter()
    {
        var combinedImporter = AssetImporter.GetAtPath(CombinedModelPath) as ModelImporter;
        if (combinedImporter == null)
            return $"Combined model importer missing: {CombinedModelPath}";

        bool changed = false;

        if (combinedImporter.animationType != ModelImporterAnimationType.Human)
        {
            combinedImporter.animationType = ModelImporterAnimationType.Human;
            changed = true;
        }

        if (combinedImporter.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            combinedImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            changed = true;
        }

        if (combinedImporter.sourceAvatar != null)
        {
            combinedImporter.sourceAvatar = null;
            changed = true;
        }

        if (changed)
            combinedImporter.SaveAndReimport();

        return changed ? "Configured combined FBX as humanoid self-avatar model" : "Combined FBX humanoid avatar already configured";
    }

    public static string Report()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TargetPrefabPath);
        if (prefab == null)
            return $"Combined prefab missing: {TargetPrefabPath}";

        var networkIdentity = prefab.GetComponent<PurrNet.NetworkIdentity>();
        var animator = prefab.GetComponent<Animator>();
        var audioController = prefab.GetComponent<SmilyAudioController>();
        var combinedRenderer = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(r => r.sharedMesh != null && r.sharedMesh.name.Contains("ClownSkin2_Combined", StringComparison.OrdinalIgnoreCase));

        string rendererSummary = combinedRenderer == null
            ? "Renderer missing"
            : string.Join(", ", combinedRenderer.sharedMaterials.Select(m => m != null ? m.name : "null"));

        string rendererTransform = combinedRenderer == null
            ? "n/a"
            : $"pos={combinedRenderer.transform.localPosition} rot={combinedRenderer.transform.localEulerAngles} scale={combinedRenderer.transform.localScale}";

        string sfxSummary = audioController != null ? audioController.Profile.ToString() : "missing";

        return $"Prefab={prefab.name}, NetworkIdentity={(networkIdentity != null)}, Animator={(animator != null ? animator.name : "null")}, SFX={sfxSummary}, RendererTransform={rendererTransform}, CombinedRendererMaterials=[{rendererSummary}]";
    }

    private static string BuildCombinedPrefab()
    {
        GameObject sourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CombinedModelPath);
        if (sourceAsset == null)
            return $"Source prefab missing: {SourcePrefabPath}";
        if (modelAsset == null)
            return $"Combined model missing: {CombinedModelPath}";

        GameObject sourceRoot = PrefabUtility.LoadPrefabContents(SourcePrefabPath);
        GameObject workingRoot = null;

        try
        {
            workingRoot = PrefabUtility.InstantiatePrefab(modelAsset, sourceRoot.scene) as GameObject;
            if (workingRoot == null)
                return "Failed to instantiate combined model asset";

            PrefabUtility.UnpackPrefabInstance(workingRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            workingRoot.name = TargetName;

            EnsureBoxPoint(workingRoot);
            CopyRootComponents(sourceRoot, workingRoot);
            RemapRootComponentReferences(sourceRoot, workingRoot);
            var modelAnimator = modelAsset.GetComponent<Animator>();
            ApplyCriticalReferenceFixes(workingRoot, modelAnimator != null ? modelAnimator.avatar : null);

            EditorUtility.SetDirty(workingRoot);
            PrefabUtility.SaveAsPrefabAsset(workingRoot, TargetPrefabPath);
            return "Rebuilt combined clown prefab from model root";
        }
        finally
        {
            if (workingRoot != null)
                UnityEngine.Object.DestroyImmediate(workingRoot);

            PrefabUtility.UnloadPrefabContents(sourceRoot);
        }
    }

    private static void CopyRootComponents(GameObject sourceRoot, GameObject targetRoot)
    {
        foreach (var sourceComponent in sourceRoot.GetComponents<Component>())
        {
            if (sourceComponent == null || sourceComponent is Transform)
                continue;

            var type = sourceComponent.GetType();
            var targetComponent = targetRoot.GetComponent(type);

            ComponentUtility.CopyComponent(sourceComponent);
            if (targetComponent != null)
            {
                ComponentUtility.PasteComponentValues(targetComponent);
            }
            else
            {
                ComponentUtility.PasteComponentAsNew(targetRoot);
            }
        }
    }

    private static void RemapRootComponentReferences(GameObject sourceRoot, GameObject targetRoot)
    {
        foreach (var component in targetRoot.GetComponents<Component>())
        {
            if (component == null || component is Transform)
                continue;

            var so = new SerializedObject(component);
            var iterator = so.GetIterator();
            bool enterChildren = true;
            bool changed = false;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = true;

                if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                UnityEngine.Object current = iterator.objectReferenceValue;
                if (current == null || EditorUtility.IsPersistent(current) || !BelongsToHierarchy(current, sourceRoot))
                    continue;

                UnityEngine.Object mapped = MapReference(current, sourceRoot, targetRoot);
                if (mapped == current)
                    continue;

                iterator.objectReferenceValue = mapped;
                changed = true;
            }

            if (changed)
                so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static bool BelongsToHierarchy(UnityEngine.Object obj, GameObject root)
    {
        switch (obj)
        {
            case GameObject go:
                return go == root || go.transform.IsChildOf(root.transform);
            case Component component:
                return component.gameObject == root || component.transform.IsChildOf(root.transform);
            default:
                return false;
        }
    }

    private static UnityEngine.Object MapReference(UnityEngine.Object sourceReference, GameObject sourceRoot, GameObject targetRoot)
    {
        if (sourceReference is GameObject sourceGameObject)
            return MapGameObject(sourceGameObject, sourceRoot, targetRoot);

        if (sourceReference is Transform sourceTransform)
            return MapTransform(sourceTransform, sourceRoot, targetRoot);

        if (sourceReference is Component sourceComponent)
        {
            GameObject targetGameObject = MapGameObject(sourceComponent.gameObject, sourceRoot, targetRoot);
            return targetGameObject != null ? targetGameObject.GetComponent(sourceComponent.GetType()) : null;
        }

        return sourceReference;
    }

    private static GameObject MapGameObject(GameObject sourceGameObject, GameObject sourceRoot, GameObject targetRoot)
    {
        if (sourceGameObject == sourceRoot)
            return targetRoot;

        if (sourceGameObject.name == "BoxPoint")
            return EnsureBoxPoint(targetRoot).gameObject;

        Transform mappedTransform = targetRoot.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name == sourceGameObject.name);
        return mappedTransform != null ? mappedTransform.gameObject : null;
    }

    private static Transform MapTransform(Transform sourceTransform, GameObject sourceRoot, GameObject targetRoot)
    {
        if (sourceTransform == sourceRoot.transform)
            return targetRoot.transform;

        if (sourceTransform.name == "BoxPoint")
            return EnsureBoxPoint(targetRoot);

        return targetRoot.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name == sourceTransform.name);
    }

    private static void ApplyCriticalReferenceFixes(GameObject root, Avatar modelAvatar)
    {
        var animator = root.GetComponent<Animator>();
        var rigidbody = root.GetComponent<Rigidbody>();
        var boxCollider = root.GetComponent<BoxCollider>();
        var navMeshAgent = EnsureComponent<UnityEngine.AI.NavMeshAgent>(root);
        var behaviorGraphAgent = root.GetComponent<Unity.Behavior.BehaviorGraphAgent>();
        var rangeDetector = root.GetComponent<RangeDetector>();
        var lineOfSightDetector = root.GetComponent<LineOfSightDetector>();
        var patrolSelector = root.GetComponent<MonsterPatrolSelector>();
        var deathSequence = root.GetComponent<ClownDeathSequence>();
        var actor = root.GetComponent<ClownMonsterActor>();
        var health = root.GetComponent<MonsterHealth>();
        var doorAutoOpener = root.GetComponent<DoorAutoOpener>();
        var networkAnimator = root.GetComponent<PurrNet.NetworkAnimator>();
        var item = root.GetComponent<Item>();
        var audioSource = EnsureComponent<AudioSource>(root);
        var audioController = EnsureComponent<SmilyAudioController>(root);
        var throwOrigin = EnsureBoxPoint(root);
        var combinedRenderer = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(r => r.sharedMesh != null && r.sharedMesh.name.Contains("ClownSkin2_Combined", StringComparison.OrdinalIgnoreCase));

        if (animator != null && modelAvatar != null)
            animator.avatar = modelAvatar;

        ConfigureNavMeshAgent(navMeshAgent);
        ConfigureMonsterAudio(audioSource, audioController);

        if (actor != null)
        {
            var actorSo = new SerializedObject(actor);
            SetObjectRef(actorSo, "animator", animator);
            SetObjectRef(actorSo, "health", health);
            SetObjectRef(actorSo, "rangeDetector", rangeDetector);
            SetObjectRef(actorSo, "lineOfSightDetector", lineOfSightDetector);
            SetObjectRef(actorSo, "patrolSelector", patrolSelector);
            SetObjectRef(actorSo, "doorAutoOpener", doorAutoOpener);
            SetObjectRef(actorSo, "behaviorGraphAgent", behaviorGraphAgent);
            SetObjectRef(actorSo, "agent", navMeshAgent);
            SetObjectRef(actorSo, "deathSequence", deathSequence);
            SetObjectRef(actorSo, "audioController", audioController);
            SetObjectRef(actorSo, "throwOrigin", throwOrigin);
            actorSo.ApplyModifiedPropertiesWithoutUndo();
        }

        if (health != null)
        {
            var healthSo = new SerializedObject(health);
            SetObjectRef(healthSo, "animator", animator);
            SetObjectRef(healthSo, "agent", navMeshAgent);
            SetObjectRef(healthSo, "corpsePickupCollider", boxCollider);
            SetObjectRef(healthSo, "smilyAudioController", audioController);
            var goreSimulatorComponent = root.GetComponent("GoreSimulator");
            SetObjectRef(healthSo, "_goreSimulator", goreSimulatorComponent);
            healthSo.ApplyModifiedPropertiesWithoutUndo();
        }

        if (networkAnimator != null)
        {
            var networkAnimatorSo = new SerializedObject(networkAnimator);
            SetObjectRef(networkAnimatorSo, "_animator", animator);
            networkAnimatorSo.ApplyModifiedPropertiesWithoutUndo();
        }

        if (doorAutoOpener != null)
        {
            var doorAutoOpenerSo = new SerializedObject(doorAutoOpener);
            var doorAreaName = doorAutoOpenerSo.FindProperty("doorAreaName");
            if (doorAreaName != null)
                doorAreaName.stringValue = "Door";
            SetObjectRef(doorAutoOpenerSo, "smilyAudioController", audioController);
            doorAutoOpenerSo.ApplyModifiedPropertiesWithoutUndo();
        }

        if (item != null)
        {
            typeof(Item).GetField("rb", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(item, rigidbody);
        }

        var goreSimulator = root.GetComponent("GoreSimulator");
        if (goreSimulator != null)
        {
            var goreSo = new SerializedObject(goreSimulator);
            SetObjectRef(goreSo, "smr", combinedRenderer);
            SetObjectRef(goreSo, "ragdollAnimator", animator);
            goreSo.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void SetObjectRef(SerializedObject so, string propertyName, UnityEngine.Object value)
    {
        var property = so.FindProperty(propertyName);
        if (property != null)
            property.objectReferenceValue = value;
    }

    private static T EnsureComponent<T>(GameObject root) where T : Component
    {
        T component = root.GetComponent<T>();
        return component != null ? component : root.AddComponent<T>();
    }

    private static void ConfigureMonsterAudio(AudioSource audioSource, SmilyAudioController audioController)
    {
        if (audioSource == null || audioController == null)
            return;

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.minDistance = 1f;
        audioSource.maxDistance = 18f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;

        var audioSo = new SerializedObject(audioController);
        SetEnum(audioSo, "profile", (int)SmilyAudioController.MonsterAudioProfile.Clown);
        SetObjectRef(audioSo, "audioSource", audioSource);
        SetObjectRef(audioSo, "loopSource", audioSource);
        SetObjectRef(audioSo, "oneShotSource", audioSource);
        audioSo.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetEnum(SerializedObject so, string propertyName, int value)
    {
        var property = so.FindProperty(propertyName);
        if (property != null)
            property.enumValueIndex = value;
    }

    private static void ConfigureNavMeshAgent(UnityEngine.AI.NavMeshAgent agent)
    {
        if (agent == null)
            return;

        agent.agentTypeID = ClownNavMeshConfig.AgentTypeId;
        agent.radius = ClownNavMeshConfig.AgentRadius;
        agent.height = ClownNavMeshConfig.AgentHeight;
        agent.baseOffset = ClownNavMeshConfig.AgentBaseOffset;
        // Match the fixed 1x Clown_Walk clip's measured 1.577 m/s stride.
        agent.speed = 1.6f;
        agent.angularSpeed = 120f;
        agent.acceleration = 8f;
        agent.stoppingDistance = 0f;
        agent.autoBraking = true;
        agent.autoRepath = true;
        agent.autoTraverseOffMeshLink = false;
    }

    private static Transform EnsureBoxPoint(GameObject root)
    {
        Transform existing = root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => string.Equals(t.name, "BoxPoint", StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return existing;

        Transform hand = root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name == "hand_r");
        if (hand == null)
            return root.transform;

        var boxPoint = new GameObject("BoxPoint");
        var point = boxPoint.transform;
        point.SetParent(hand, false);
        point.localPosition = new Vector3(0.09f, -0.05f, 0f);
        point.localRotation = Quaternion.identity;
        point.localScale = Vector3.one;
        return point;
    }

    private static string ReplaceStartMapSpawnerReference()
    {
        var scene = EditorSceneManager.OpenScene(StartMapPath, OpenSceneMode.Single);
        var spawner = UnityEngine.Object.FindFirstObjectByType<DungeonMonsterSpawner>();
        if (spawner == null)
            return "StartMap spawner missing";

        var oldIdentity = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath)?.GetComponent<PurrNet.NetworkIdentity>();
        var newIdentity = AssetDatabase.LoadAssetAtPath<GameObject>(TargetPrefabPath)?.GetComponent<PurrNet.NetworkIdentity>();
        if (newIdentity == null)
            return "Combined prefab NetworkIdentity missing";

        var so = new SerializedObject(spawner);
        var monsters = so.FindProperty("monsters");
        float weightToUse = 1f;
        var preservedEntries = new List<(UnityEngine.Object prefab, float weight)>();

        for (int i = 0; i < monsters.arraySize; i++)
        {
            var entry = monsters.GetArrayElementAtIndex(i);
            var prefabProp = entry.FindPropertyRelative("prefab");
            var weightProp = entry.FindPropertyRelative("weight");
            var current = prefabProp.objectReferenceValue;

            if (current == null)
                continue;

            if (current == newIdentity)
            {
                weightToUse = weightProp.floatValue;
                continue;
            }

            if (oldIdentity != null && current == oldIdentity)
            {
                weightToUse = weightProp.floatValue;
                continue;
            }

            preservedEntries.Add((current, weightProp.floatValue));
        }

        if (weightToUse <= 0f)
            weightToUse = 1f;

        preservedEntries.Add((newIdentity, weightToUse));

        monsters.arraySize = preservedEntries.Count;
        for (int i = 0; i < preservedEntries.Count; i++)
        {
            var entry = monsters.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("prefab").objectReferenceValue = preservedEntries[i].prefab;
            entry.FindPropertyRelative("weight").floatValue = preservedEntries[i].weight;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "Updated StartMap clown spawn to combined prefab";
    }
}
