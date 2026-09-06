using System.Linq;
using System.Reflection;
using PurrNet;
using Unity.Behavior;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static class ClownProductionSetup
{
    private const string SourcePrefabPath = "Assets/Clown/Prefab/Clown skin2.prefab";
    private const string RuntimePrefabPath = "Assets/Clown/Prefab/Clown skin2 combined.prefab";
    private static readonly string[] ClownPrefabPaths =
    {
        SourcePrefabPath,
        RuntimePrefabPath
    };
    private static readonly string[] TestScenePaths =
    {
        "Assets/Clown/Scenes/ClownTest.unity",
        "Assets/Clown/Scenes/ClownThrowQA.unity"
    };
    private const string StartMapPath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
    private const string ControllerPath = "Assets/Monster/Clown/ClownAnimator.controller";
    private const string GraphPath = "Assets/Monster/Clown/ClownBehaviorGraph.asset";
    private const string GiftVisualPath = "Assets/Scripts/Currency/Prefab/GiftBox3_visual.prefab";
    private const string ClownSceneInstanceName = "ClownSkin2_Test";

    public static string ApplyAll()
    {
        string prefab = ConfigurePrefab();
        string graph = ClownBehaviorGraphBuilder.BuildAndAssign();
        string scene = ConfigureTestScene();
        string startMap = ConfigureStartMapSpawner();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return prefab + " | " + graph + " | " + scene + " | " + startMap;
    }

    public static string ApplyWithoutGraphRebuild()
    {
        string prefab = ConfigurePrefab();
        string scene = ConfigureTestScene();
        string startMap = ConfigureStartMapSpawner();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return prefab + " | " + scene + " | " + startMap;
    }

    public static string ApplyNavMeshAgentOnly()
    {
        string[] results = new string[ClownPrefabPaths.Length];
        for (int i = 0; i < ClownPrefabPaths.Length; i++)
            results[i] = ConfigurePrefabNavMeshAgent(ClownPrefabPaths[i]);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return string.Join(" | ", results);
    }

    private static string ConfigurePrefabNavMeshAgent(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            NavMeshAgent agent = EnsureComponent<NavMeshAgent>(root);
            ConfigureNavMeshAgent(agent);
            bool actorWired = TrySetObjectReference(
                root.GetComponentInChildren<ClownMonsterActor>(true),
                "agent",
                agent);
            bool healthWired = TrySetObjectReference(
                root.GetComponentInChildren<MonsterHealth>(true),
                "agent",
                agent);

            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(agent);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return $"{prefabPath}: agentType={agent.agentTypeID}, actorWired={actorWired}, healthWired={healthWired}";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool TrySetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object reference)
    {
        if (target == null)
            return false;

        var so = new SerializedObject(target);
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop == null)
            return false;

        prop.objectReferenceValue = reference;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        return true;
    }

    private static string ConfigurePrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(RuntimePrefabPath);
        try
        {
            Animator animator = EnsureComponent<Animator>(root);
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            animator.applyRootMotion = false;
            ConfigureNavMeshAgent(EnsureComponent<NavMeshAgent>(root));

            var graphAgent = EnsureComponent<BehaviorGraphAgent>(root);
            var netAnimator = EnsureComponent<NetworkAnimator>(root);
            var netTransform = EnsureComponent<NetworkTransform>(root);
            EnsureComponent<NetworkIdentity>(root);

            var actor = EnsureComponent<ClownMonsterActor>(root);
            var health = EnsureComponent<MonsterHealth>(root);
            var death = EnsureComponent<ClownDeathSequence>(root);
            var item = EnsureComponent<Item>(root);
            var rigidbody = EnsureComponent<Rigidbody>(root);
            var patrolSelector = EnsureComponent<MonsterPatrolSelector>(root);
            var doorAutoOpener = EnsureComponent<DoorAutoOpener>(root);
            var audioSource = EnsureComponent<AudioSource>(root);
            var audioController = EnsureComponent<SmilyAudioController>(root);
            ConfigureMonsterAudio(audioSource, audioController);

            GameObject giftVisual = AssetDatabase.LoadAssetAtPath<GameObject>(GiftVisualPath);
            Item[] drops = LoadGiftDrops();
            Transform throwOrigin = EnsureBoxPoint(root);

            SerializedObject actorSo = new SerializedObject(actor);
            actorSo.FindProperty("health").objectReferenceValue = health;
            actorSo.FindProperty("agent").objectReferenceValue = root.GetComponent<UnityEngine.AI.NavMeshAgent>();
            actorSo.FindProperty("patrolSelector").objectReferenceValue = patrolSelector;
            actorSo.FindProperty("doorAutoOpener").objectReferenceValue = doorAutoOpener;
            actorSo.FindProperty("deathSequence").objectReferenceValue = death;
            actorSo.FindProperty("audioController").objectReferenceValue = audioController;
            actorSo.FindProperty("animator").objectReferenceValue = animator;
            actorSo.FindProperty("throwOrigin").objectReferenceValue = throwOrigin;
            actorSo.FindProperty("thrownGiftVisualPrefab").objectReferenceValue = giftVisual;
            actorSo.FindProperty("heldGiftVisualPrefab").objectReferenceValue = giftVisual;
            actorSo.FindProperty("heldGiftLocalPosition").vector3Value = Vector3.zero;
            actorSo.FindProperty("heldGiftLocalEulerAngles").vector3Value = Vector3.zero;
            actorSo.FindProperty("heldGiftLocalScale").vector3Value = Vector3.one;
            actorSo.FindProperty("throwSpawnForwardOffset").floatValue = 0f;
            actorSo.FindProperty("throwSpawnUpOffset").floatValue = 0f;
            SerializedProperty dropProp = actorSo.FindProperty("randomItemDrops");
            dropProp.arraySize = drops.Length;
            for (int i = 0; i < drops.Length; i++)
                dropProp.GetArrayElementAtIndex(i).objectReferenceValue = drops[i];
            actorSo.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject healthSo = new SerializedObject(health);
            healthSo.FindProperty("agent").objectReferenceValue = root.GetComponent<NavMeshAgent>();
            healthSo.FindProperty("smilyAudioController").objectReferenceValue = audioController;
            healthSo.ApplyModifiedPropertiesWithoutUndo();

            var graphSo = new SerializedObject(graphAgent);
            graphSo.FindProperty("m_Graph").objectReferenceValue = LoadRuntimeGraph();
            graphSo.FindProperty("m_Enabled").boolValue = false;
            graphSo.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject netAnimatorSo = new SerializedObject(netAnimator);
            netAnimatorSo.FindProperty("_ownerAuth").boolValue = false;
            netAnimatorSo.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject netTransformSo = new SerializedObject(netTransform);
            netTransformSo.FindProperty("_ownerAuth").boolValue = false;
            netTransformSo.ApplyModifiedPropertiesWithoutUndo();

            ConfigureDoorAutoOpener(doorAutoOpener);

            typeof(Item).GetField("rb", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(item, rigidbody);
            typeof(MonsterHealth).GetField("disableBehavioursOnDeath", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(health, new Behaviour[] { actor, graphAgent, netAnimator, netTransform, doorAutoOpener });

            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, RuntimePrefabPath);
            return "Configured clown prefab";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Transform EnsureBoxPoint(GameObject root)
    {
        Transform existing = root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => string.Equals(t.name, "BoxPoint", System.StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return existing;

        Transform hand = root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name == "hand_r");
        if (hand == null)
            return root.transform;

        GameObject boxPoint = new GameObject("BoxPoint");
        Transform point = boxPoint.transform;
        point.SetParent(hand, false);
        point.localPosition = new Vector3(0.09f, -0.05f, 0f);
        point.localRotation = Quaternion.identity;
        point.localScale = Vector3.one;
        return point;
    }

    private static string ConfigureTestScene()
    {
        string[] results = new string[TestScenePaths.Length];
        for (int i = 0; i < TestScenePaths.Length; i++)
            results[i] = ConfigureTestScene(TestScenePaths[i]);

        return string.Join(" | ", results);
    }

    private static string ConfigureTestScene(string scenePath)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        GameObject clown = EnsureSceneClownInstance(scene);
        if (clown == null)
            return $"{scenePath}: Clown runtime prefab missing";

        var actor = EnsureComponent<ClownMonsterActor>(clown);
        var patrolSelector = EnsureComponent<MonsterPatrolSelector>(clown);
        var doorAutoOpener = EnsureComponent<DoorAutoOpener>(clown);
        var graphAgent = EnsureComponent<BehaviorGraphAgent>(clown);
        var audioSource = EnsureComponent<AudioSource>(clown);
        var audioController = EnsureComponent<SmilyAudioController>(clown);
        ConfigureMonsterAudio(audioSource, audioController);
        var testController = clown.GetComponent<ClownTestController>();
        if (testController != null)
            Object.DestroyImmediate(testController, true);

        var netTransform = clown.GetComponent<NetworkTransform>();
        var netAnimator = clown.GetComponent<NetworkAnimator>();
        if (netTransform != null) netTransform.enabled = false;
        if (netAnimator != null) netAnimator.enabled = false;

        var graphSo = new SerializedObject(graphAgent);
        graphSo.FindProperty("m_Graph").objectReferenceValue = LoadRuntimeGraph();
        graphSo.ApplyModifiedPropertiesWithoutUndo();

        Animator animator = clown.GetComponent<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        animator.applyRootMotion = false;
        ConfigureNavMeshAgent(EnsureComponent<NavMeshAgent>(clown));
        ConfigureDoorAutoOpener(doorAutoOpener);

        GameObject giftVisual = AssetDatabase.LoadAssetAtPath<GameObject>(GiftVisualPath);
        Item[] drops = LoadGiftDrops();
        Transform throwOrigin = EnsureBoxPoint(clown);
        SerializedObject actorSo = new SerializedObject(actor);
        actorSo.FindProperty("health").objectReferenceValue = clown.GetComponent<MonsterHealth>();
        actorSo.FindProperty("rangeDetector").objectReferenceValue = clown.GetComponent<RangeDetector>();
        actorSo.FindProperty("lineOfSightDetector").objectReferenceValue = clown.GetComponent<LineOfSightDetector>();
        actorSo.FindProperty("patrolSelector").objectReferenceValue = patrolSelector;
        actorSo.FindProperty("doorAutoOpener").objectReferenceValue = doorAutoOpener;
        actorSo.FindProperty("behaviorGraphAgent").objectReferenceValue = clown.GetComponent<BehaviorGraphAgent>();
        actorSo.FindProperty("agent").objectReferenceValue = clown.GetComponent<UnityEngine.AI.NavMeshAgent>();
        actorSo.FindProperty("deathSequence").objectReferenceValue = clown.GetComponent<ClownDeathSequence>();
        actorSo.FindProperty("audioController").objectReferenceValue = audioController;
        actorSo.FindProperty("animator").objectReferenceValue = animator;
        actorSo.FindProperty("throwOrigin").objectReferenceValue = throwOrigin;
        actorSo.FindProperty("thrownGiftVisualPrefab").objectReferenceValue = giftVisual;
        actorSo.FindProperty("heldGiftVisualPrefab").objectReferenceValue = giftVisual;
        actorSo.FindProperty("heldGiftLocalPosition").vector3Value = Vector3.zero;
        actorSo.FindProperty("heldGiftLocalEulerAngles").vector3Value = Vector3.zero;
        actorSo.FindProperty("heldGiftLocalScale").vector3Value = Vector3.one;
        actorSo.FindProperty("throwSpawnForwardOffset").floatValue = 0f;
        actorSo.FindProperty("throwSpawnUpOffset").floatValue = 0f;
        SerializedProperty dropProp = actorSo.FindProperty("randomItemDrops");
        dropProp.arraySize = drops.Length;
        for (int i = 0; i < drops.Length; i++)
            dropProp.GetArrayElementAtIndex(i).objectReferenceValue = drops[i];
        actorSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject healthSo = new SerializedObject(clown.GetComponent<MonsterHealth>());
        healthSo.FindProperty("agent").objectReferenceValue = clown.GetComponent<NavMeshAgent>();
        healthSo.FindProperty("smilyAudioController").objectReferenceValue = audioController;
        healthSo.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(clown);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"Configured {scenePath}";
    }

    private static string ConfigureStartMapSpawner()
    {
        var scene = EditorSceneManager.OpenScene(StartMapPath, OpenSceneMode.Single);
        var spawner = Object.FindFirstObjectByType<DungeonMonsterSpawner>();
        if (spawner == null)
            return "StartMap spawner missing";

        NetworkIdentity clownIdentity = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimePrefabPath)?.GetComponent<NetworkIdentity>();
        if (clownIdentity == null)
            return "Clown prefab missing NetworkIdentity";

        SerializedObject so = new SerializedObject(spawner);
        SerializedProperty monsters = so.FindProperty("monsters");
        bool exists = false;
        for (int i = 0; i < monsters.arraySize; i++)
        {
            var entry = monsters.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("prefab").objectReferenceValue == clownIdentity)
            {
                exists = true;
                entry.FindPropertyRelative("weight").floatValue = 1f;
                break;
            }
        }

        if (!exists)
        {
            int index = monsters.arraySize;
            monsters.InsertArrayElementAtIndex(index);
            var entry = monsters.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("prefab").objectReferenceValue = clownIdentity;
            entry.FindPropertyRelative("weight").floatValue = 1f;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        int removedPlaced = RemovePlacedRuntimeClowns(scene, clownIdentity.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return removedPlaced > 0 ? $"Configured StartMap spawner; removed {removedPlaced} placed Clown instance(s)" : "Configured StartMap spawner";
    }

    private static GameObject EnsureSceneClownInstance(UnityEngine.SceneManagement.Scene scene)
    {
        GameObject runtimePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimePrefabPath);
        if (runtimePrefab == null)
            return null;

        GameObject clown = GameObject.Find(ClownSceneInstanceName);
        if (clown == null)
        {
            clown = PrefabUtility.InstantiatePrefab(runtimePrefab, scene) as GameObject;
            if (clown != null)
                clown.name = ClownSceneInstanceName;
            return clown;
        }

        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(clown);
        if (source == runtimePrefab)
            return clown;

        Transform oldTransform = clown.transform;
        Transform parent = oldTransform.parent;
        Vector3 localPosition = oldTransform.localPosition;
        Quaternion localRotation = oldTransform.localRotation;
        Vector3 localScale = oldTransform.localScale;

        Object.DestroyImmediate(clown);
        GameObject replacement = PrefabUtility.InstantiatePrefab(runtimePrefab, scene) as GameObject;
        if (replacement == null)
            return null;

        replacement.name = ClownSceneInstanceName;
        replacement.transform.SetParent(parent, false);
        replacement.transform.localPosition = localPosition;
        replacement.transform.localRotation = localRotation;
        replacement.transform.localScale = localScale;
        return replacement;
    }

    private static int RemovePlacedRuntimeClowns(UnityEngine.SceneManagement.Scene scene, GameObject runtimePrefab)
    {
        if (runtimePrefab == null)
            return 0;

        int removed = 0;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            removed += RemovePlacedRuntimeClownsRecursive(roots[i], runtimePrefab);

        return removed;
    }

    private static int RemovePlacedRuntimeClownsRecursive(GameObject current, GameObject runtimePrefab)
    {
        if (current == null)
            return 0;

        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(current);
        if (source == runtimePrefab)
        {
            Object.DestroyImmediate(current);
            return 1;
        }

        int removed = 0;
        for (int i = current.transform.childCount - 1; i >= 0; i--)
            removed += RemovePlacedRuntimeClownsRecursive(current.transform.GetChild(i).gameObject, runtimePrefab);

        return removed;
    }

    private static T EnsureComponent<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : go.AddComponent<T>();
    }

    private static void ConfigureNavMeshAgent(NavMeshAgent agent)
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

    private static void ConfigureDoorAutoOpener(DoorAutoOpener opener)
    {
        if (opener == null)
            return;

        SerializedObject so = new SerializedObject(opener);
        SetString(so, "doorAreaName", "Door");
        SetFloat(so, "openDelaySeconds", 0.5f);
        SetFloat(so, "waitUntilOpenSeconds", 2f);
        SetFloat(so, "traverseSpeedMultiplier", 1f);
        SetBool(so, "preserveCurrentYDuringManualLink", true);
        SetBool(so, "rotateAlongManualLink", true);
        SetFloat(so, "manualLinkTurnSpeedMultiplier", 1f);
        SetBool(so, "forceAnimatorSpeedOnManualLink", true);
        SetString(so, "animatorSpeedParameter", "MoveSpeed");
        so.ApplyModifiedPropertiesWithoutUndo();
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

        SerializedObject so = new SerializedObject(audioController);
        SetEnum(so, "profile", (int)SmilyAudioController.MonsterAudioProfile.Clown);
        SetObjectRef(so, "audioSource", audioSource);
        SetObjectRef(so, "loopSource", audioSource);
        SetObjectRef(so, "oneShotSource", audioSource);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetString(SerializedObject so, string propertyName, string value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property != null)
            property.stringValue = value;
    }

    private static void SetFloat(SerializedObject so, string propertyName, float value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property != null)
            property.floatValue = value;
    }

    private static void SetBool(SerializedObject so, string propertyName, bool value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property != null)
            property.boolValue = value;
    }

    private static void SetEnum(SerializedObject so, string propertyName, int value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property != null)
            property.enumValueIndex = value;
    }

    private static void SetObjectRef(SerializedObject so, string propertyName, Object value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property != null)
            property.objectReferenceValue = value;
    }

    private static Item[] LoadGiftDrops()
    {
        return new[]
        {
            AssetDatabase.LoadAssetAtPath<Item>("Assets/Scripts/Currency/Prefab/GiftBox1_item.prefab"),
            AssetDatabase.LoadAssetAtPath<Item>("Assets/Scripts/Currency/Prefab/GiftBox2_item.prefab"),
            AssetDatabase.LoadAssetAtPath<Item>("Assets/Scripts/Currency/Prefab/GiftBox3_item.prefab"),
            AssetDatabase.LoadAssetAtPath<Item>("Assets/Scripts/Currency/Prefab/GiftBox4_item.prefab")
        };
    }

    private static BehaviorGraph LoadRuntimeGraph()
    {
        return AssetDatabase.LoadAllAssetsAtPath(GraphPath).OfType<BehaviorGraph>().FirstOrDefault();
    }
}
