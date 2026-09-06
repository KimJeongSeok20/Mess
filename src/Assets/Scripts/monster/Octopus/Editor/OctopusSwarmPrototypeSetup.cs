using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PurrNet;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class OctopusSwarmPrototypeSetup
{
    private const string ScenePath = "Assets/test_monster.unity";
    private const string ModelPath = "Assets/ParrelSync-master/MyAsset/Octopus/hmm/Octopus.fbx";
    private const string ExistingControllerPath = "Assets/ParrelSync-master/MyAsset/Octopus/hmm/test.controller";
    private const string SwarmPrefabPath = "Assets/Monster/Octopus/OctopusSwarm.prefab";
    private const string StartMapScenePath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
    private const string NetworkPrefabsPath = "Assets/Prefabs/DoorNextToDungeonPrefab.asset";
    private const string CyberGenericPrefabPath = "Assets/FPS/Cyber_Generic.prefab";
    private const string NetworkManagerPrefabPath = "Assets/Test/NetworkManager.prefab";
    private const string GameManagerPrefabPath = "Assets/Test/GameManager.prefab";
    private const string CanvasPrefabPath = "Assets/Test/Canvas.prefab";
    private const string RootName = "OctopusSwarmRoot";
    private const string SpawnPointName = "PlayerSpawnPoint";
    private const string LoadoutRootName = "TestLoadout";
    private const string BlockersRootName = "ArenaBlockers";
    private const string ArenaPlaneName = "Plane";
    private const int DesiredMemberCount = 7;
    private const float ProductionSpawnWeight = 0.6f;
    private static readonly string[] WeaponPaths =
    {
        "Assets/Items/Weapon/AK_item.prefab",
        "Assets/Items/Weapon/Drake-12_item.prefab",
        "Assets/Items/Weapon/RPG_item.prefab",
    };

    private static readonly string[] AmmoPaths =
    {
        "Assets/Items/Weapon/AK_Mag.prefab",
        "Assets/Items/Weapon/Drake_Mag.prefab",
        "Assets/Items/Weapon/RPG_Mag.prefab",
    };

    public static void PromoteToProductionCli()
    {
        Debug.Log(PromoteToProduction());
    }

    public static string PromoteToProduction()
    {
        string prefabResult = CreateSwarmPrefab();
        AssetDatabase.Refresh();
        string networkPrefabResult = RegisterNetworkPrefab();
        string spawnerResult = RegisterProductionSpawner();
        string validationResult = ValidateProductionPrefab();
        return $"{prefabResult} | {networkPrefabResult} | {spawnerResult} | {validationResult}";
    }

    public static void ValidateProductionCli()
    {
        Debug.Log(ValidateProductionPrefab());
    }

    public static string ApplyToTestScene()
    {
        string setupResult = MonsterTestSceneSetup.RebuildScene();
        AnimatorController controllerAsset = AssetDatabase.LoadAssetAtPath<AnimatorController>(ExistingControllerPath);
        if (controllerAsset == null)
            return $"{setupResult} | Existing octopus animator controller not found: {ExistingControllerPath}";

        EnsureAttackClipEvents();
        return $"{setupResult} | {ValidateAnimatorContract(controllerAsset)} | {GetAttackClipEventReport()}";
    }

    public static string CreateSwarmPrefab()
    {
        EnsureProductionFolder();

        AnimatorController controllerAsset = AssetDatabase.LoadAssetAtPath<AnimatorController>(ExistingControllerPath);
        if (controllerAsset == null)
            return $"Existing octopus animator controller not found: {ExistingControllerPath}";

        GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelPrefab == null)
            return $"Octopus model not found: {ModelPath}";

        EnsureCyberGenericHasPlayerPawn();
        EnsureAttackClipEvents();

        GameObject root = new GameObject(RootName);
        try
        {
            OctopusSwarmController swarmController = root.AddComponent<OctopusSwarmController>();
            MonsterDoorTraversalGroup traversalGroup = root.AddComponent<MonsterDoorTraversalGroup>();
            OctopusSwarmPresentation presentation = EnsureSwarmPresentation(root);
            ConfigureSwarmController(swarmController, traversalGroup);
            ConfigureSwarmPresentation(swarmController, presentation);

            for (int i = 0; i < DesiredMemberCount; i++)
            {
                GameObject octopus = PrefabUtility.InstantiatePrefab(modelPrefab) as GameObject;
                if (octopus == null)
                    continue;

                octopus.name = $"Octopus_{i + 1:00}";
                octopus.transform.SetParent(root.transform, false);
                octopus.transform.localPosition = GetSwarmLayoutOffset(i, DesiredMemberCount);
                octopus.transform.localRotation = Quaternion.identity;
                octopus.transform.localScale = Vector3.one * 0.25f;

                OctopusSwarmMember member = octopus.GetComponent<OctopusSwarmMember>();
                if (member == null)
                    member = octopus.AddComponent<OctopusSwarmMember>();

                member.SetController(swarmController);
                ConfigureMemberAnimationParameters(member);
                ConfigureMemberTuning(member);
                EnsureMemberPresentationAudio(octopus, member);
                EnsureAttackHitbox(octopus);

                Animator animator = octopus.GetComponent<Animator>();
                if (animator == null)
                    animator = octopus.AddComponent<Animator>();

                animator.runtimeAnimatorController = controllerAsset;
                animator.applyRootMotion = false;

                NavMeshAgent agent = octopus.GetComponent<NavMeshAgent>();
                if (agent == null)
                    agent = octopus.AddComponent<NavMeshAgent>();

                ConfigureAgent(agent);
                ConfigureProductionMemberInfrastructure(octopus, member, animator, agent, traversalGroup, i);
            }

            swarmController.RebuildMembers();
            PrefabUtility.SaveAsPrefabAsset(root, SwarmPrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return $"Created octopus swarm prefab: {SwarmPrefabPath} | {ValidateAnimatorContract(controllerAsset)}";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    public static string GetAttackClipEventReport()
    {
        ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
            return "Attack clip importer missing";

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;

        if (clips == null || clips.Length == 0)
            return "Attack clips missing";

        List<string> reports = new();
        for (int i = 0; i < clips.Length; i++)
        {
            if (!clips[i].name.Contains("Attack", StringComparison.OrdinalIgnoreCase))
                continue;

            string eventNames = clips[i].events == null || clips[i].events.Length == 0
                ? "none"
                : string.Join(",", clips[i].events.Select(evt => evt.functionName));
            reports.Add($"{clips[i].name}:{eventNames}");
        }

        return reports.Count > 0 ? string.Join(" | ", reports) : "No attack clips found";
    }

    private static void EnsureSceneBootstrap(Transform spawnPoint)
    {
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CyberGenericPrefabPath);
        if (playerPrefab == null)
            return;

        GameObject networkRoot = EnsurePrefabRoot(NetworkManagerPrefabPath, "NetworkManager");
        GameObject gameManagerRoot = EnsurePrefabRoot(GameManagerPrefabPath, "GameManager");
        GameObject canvasRoot = EnsurePrefabRoot(CanvasPrefabPath, "Canvas");

        if (networkRoot != null)
        {
            var networkManager = networkRoot.GetComponent<NetworkManager>() ?? networkRoot.AddComponent<NetworkManager>();
            Component udpTransport = GetOrAddComponent(networkRoot, "PurrNet.Transports.UDPTransport");

            var playerSpawner = networkRoot.GetComponent<PlayerSpawner>() ?? networkRoot.AddComponent<PlayerSpawner>();
            RemoveComponentIfPresent(networkRoot, "PurrLobby.MyConnectionStarter");
            RemoveComponentIfPresent(networkRoot, "PurrNet.Steam.SteamTransport");

            SerializedObject networkManagerSo = new SerializedObject(networkManager);
            SerializedProperty transportProp = networkManagerSo.FindProperty("_transport");
            if (transportProp != null && udpTransport != null)
                transportProp.objectReferenceValue = udpTransport;
            networkManagerSo.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject spawnerSo = new SerializedObject(playerSpawner);
            SerializedProperty playerPrefabProp = spawnerSo.FindProperty("_playerPrefab");
            if (playerPrefabProp != null)
                playerPrefabProp.objectReferenceValue = playerPrefab;

            SerializedProperty spawnPointsProp = spawnerSo.FindProperty("spawnPoints");
            if (spawnPointsProp != null && spawnPoint != null)
            {
                spawnPointsProp.arraySize = 1;
                spawnPointsProp.GetArrayElementAtIndex(0).objectReferenceValue = spawnPoint;
            }

            spawnerSo.ApplyModifiedPropertiesWithoutUndo();

            networkRoot.transform.position = new Vector3(-10f, 0f, -26f);
            networkRoot.transform.rotation = Quaternion.identity;
            EditorUtility.SetDirty(networkRoot);
        }

        if (gameManagerRoot != null)
        {
            gameManagerRoot.transform.position = new Vector3(-14f, 0f, -24f);
            EditorUtility.SetDirty(gameManagerRoot);
        }

        if (canvasRoot != null)
        {
            canvasRoot.transform.position = Vector3.zero;
            EditorUtility.SetDirty(canvasRoot);
        }
    }

    private static GameObject EnsurePrefabRoot(string prefabPath, string rootName)
    {
        GameObject existing = FindRoot(rootName);
        if (existing != null)
            return existing;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            return null;

        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (instance == null)
            return null;

        instance.name = rootName;
        instance.transform.SetParent(null);
        return instance;
    }

    private static void CleanSceneEntryObjects()
    {
        DestroyRootIfExists("Cyber_Generic");
        DestroyRootIfExists("Main Camera");
    }

    private static void StripRootMemberComponents(GameObject swarmRoot)
    {
        OctopusSwarmMember rootMember = swarmRoot.GetComponent<OctopusSwarmMember>();
        if (rootMember != null)
            UnityEngine.Object.DestroyImmediate(rootMember, true);

        Animator rootAnimator = swarmRoot.GetComponent<Animator>();
        if (rootAnimator != null)
            UnityEngine.Object.DestroyImmediate(rootAnimator, true);
    }

    private static void DestroyRootIfExists(string rootName)
    {
        GameObject existing = FindRoot(rootName);
        if (existing != null)
            UnityEngine.Object.DestroyImmediate(existing);
    }

    private static GameObject EnsureArenaPlane()
    {
        GameObject plane = FindRoot(ArenaPlaneName);
        if (plane == null)
        {
            plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = ArenaPlaneName;
        }

        plane.transform.position = Vector3.zero;
        plane.transform.rotation = Quaternion.identity;
        plane.transform.localScale = new Vector3(16f, 1f, 16f);
        return plane;
    }

    private static void ConfigureArenaPlane(GameObject plane, UnityEngine.SceneManagement.Scene scene)
    {
        if (plane == null)
            return;

        NavMeshSurface surface = plane.GetComponent<NavMeshSurface>();
        if (surface == null)
            surface = plane.AddComponent<NavMeshSurface>();

        surface.collectObjects = CollectObjects.All;
        surface.layerMask = ~0;
        surface.defaultArea = 0;
        surface.agentTypeID = 0;

        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = 0.15f;
        settings.agentHeight = 0.5f;
        settings.agentSlope = 60f;
        settings.agentClimb = 0.4f;
        surface.RemoveData();
        surface.BuildNavMesh();

        EditorUtility.SetDirty(surface);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static Transform EnsurePlayerSpawnPoint()
    {
        GameObject existing = FindRoot(SpawnPointName);
        if (existing == null)
            existing = new GameObject(SpawnPointName);

        existing.transform.position = new Vector3(0f, 1.1f, -20f);
        existing.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        return existing.transform;
    }

    private static void EnsureTestLoadout(Transform spawnPoint)
    {
        GameObject loadoutRoot = FindRoot(LoadoutRootName);
        if (loadoutRoot == null)
            loadoutRoot = new GameObject(LoadoutRootName);

        ClearChildren(loadoutRoot.transform);
        Vector3 rackCenter = spawnPoint.position + spawnPoint.forward * 2.6f;

        for (int i = 0; i < WeaponPaths.Length; i++)
        {
            float xOffset = (i - 1) * 1.9f;
            Vector3 pedestalPosition = rackCenter + Vector3.right * xOffset;
            Vector3 ammoPedestalPosition = pedestalPosition + spawnPoint.forward * 1.15f;

            CreateBox(loadoutRoot.transform, $"WeaponPedestal_{i + 1}", pedestalPosition, new Vector3(1.6f, 0.8f, 1.6f), new Color(0.18f, 0.22f, 0.27f));
            CreateBox(loadoutRoot.transform, $"AmmoPedestal_{i + 1}", ammoPedestalPosition, new Vector3(1.2f, 0.6f, 1.2f), new Color(0.16f, 0.18f, 0.22f));

            Quaternion facingSpawn = Quaternion.LookRotation(-spawnPoint.forward, Vector3.up);
            InstantiateLoadoutPrefab(WeaponPaths[i], loadoutRoot.transform, pedestalPosition + Vector3.up * 1.05f, facingSpawn);
            InstantiateLoadoutPrefab(AmmoPaths[i], loadoutRoot.transform, ammoPedestalPosition + Vector3.up * 0.85f, facingSpawn);
        }
    }

    private static void EnsureArenaBlockers(Transform spawnPoint, Vector3 swarmArenaCenter)
    {
        GameObject blockersRoot = FindRoot(BlockersRootName);
        if (blockersRoot == null)
            blockersRoot = new GameObject(BlockersRootName);

        ClearChildren(blockersRoot.transform);
        Vector3 laneMid = Vector3.Lerp(new Vector3(spawnPoint.position.x, 0f, spawnPoint.position.z), swarmArenaCenter, 0.5f);

        CreateBox(blockersRoot.transform, "SideWall_Left", new Vector3(-15f, 1.5f, laneMid.z), new Vector3(1f, 3f, 58f), new Color(0.15f, 0.18f, 0.22f));
        CreateBox(blockersRoot.transform, "SideWall_Right", new Vector3(15f, 1.5f, laneMid.z), new Vector3(1f, 3f, 58f), new Color(0.15f, 0.18f, 0.22f));

        CreateBox(blockersRoot.transform, "BrokenWall_Left", new Vector3(-6.5f, 1.5f, -6f), new Vector3(7f, 3f, 1.3f), new Color(0.23f, 0.26f, 0.31f));
        CreateBox(blockersRoot.transform, "BrokenWall_Right", new Vector3(6.5f, 1.5f, -6f), new Vector3(7f, 3f, 1.3f), new Color(0.23f, 0.26f, 0.31f));
        CreateBox(blockersRoot.transform, "CenterPillar", new Vector3(0f, 1.8f, 3f), new Vector3(2.4f, 3.6f, 2.4f), new Color(0.23f, 0.26f, 0.31f));
        CreateBox(blockersRoot.transform, "LeftCover", new Vector3(-7f, 1.1f, 8f), new Vector3(4.8f, 2.2f, 1.5f), new Color(0.2f, 0.24f, 0.28f));
        CreateBox(blockersRoot.transform, "RightCover", new Vector3(7f, 1.1f, 8f), new Vector3(4.8f, 2.2f, 1.5f), new Color(0.2f, 0.24f, 0.28f));
        CreateBox(blockersRoot.transform, "ForwardLeftPillar", new Vector3(-4.5f, 1.5f, 15f), new Vector3(2f, 3f, 2f), new Color(0.24f, 0.28f, 0.33f));
        CreateBox(blockersRoot.transform, "ForwardRightPillar", new Vector3(4.5f, 1.5f, 15f), new Vector3(2f, 3f, 2f), new Color(0.24f, 0.28f, 0.33f));
        CreateBox(blockersRoot.transform, "RearLaneBlock", new Vector3(0f, 1.1f, 11.5f), new Vector3(5.4f, 2.2f, 1.4f), new Color(0.2f, 0.24f, 0.28f));
    }

    private static List<GameObject> EnsureOctopusRoots(GameObject modelPrefab, int desiredCount)
    {
        List<GameObject> octopi = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(t => t.parent == null && t.name.Contains("Octopus", StringComparison.OrdinalIgnoreCase) && t.name != RootName)
            .Select(t => t.gameObject)
            .OrderBy(go => go.name)
            .ToList();

        while (octopi.Count < desiredCount)
        {
            GameObject created = PrefabUtility.InstantiatePrefab(modelPrefab) as GameObject;
            if (created == null)
                break;

            created.name = $"Octopus ({octopi.Count + 1})";
            octopi.Add(created);
        }

        while (octopi.Count > desiredCount)
        {
            GameObject extra = octopi[octopi.Count - 1];
            octopi.RemoveAt(octopi.Count - 1);
            UnityEngine.Object.DestroyImmediate(extra);
        }

        return octopi.OrderBy(go => go.name).ToList();
    }

    private static Vector3 GetSwarmLayoutOffset(int index, int count)
    {
        float angle = Mathf.PI * 2f * index / Mathf.Max(1, count);
        float radius = count <= 5 ? 3.4f : 4.35f + (index % 2 == 0 ? -0.35f : 0.25f);
        return new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
    }

    private static Vector3 GetSwarmArenaCenter(Transform spawnPoint)
    {
        if (spawnPoint == null)
            return new Vector3(0f, 0f, 12f);

        Vector3 forward = spawnPoint.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        return new Vector3(spawnPoint.position.x, 0f, spawnPoint.position.z) + forward.normalized * 32f;
    }

    private static void InstantiateLoadoutPrefab(string prefabPath, Transform parent, Vector3 position, Quaternion rotation)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            return;

        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (instance == null)
            return;

        instance.transform.SetParent(parent);
        instance.transform.position = position;
        instance.transform.rotation = rotation;
        instance.transform.localScale = prefab.transform.localScale;
        EditorUtility.SetDirty(instance);
    }

    private static void EnsureCyberGenericHasPlayerPawn()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CyberGenericPrefabPath);
        if (prefab == null)
            return;

        Type playerPawnType = ResolvePlayerPawnType();
        if (playerPawnType == null)
            return;

        GameObject root = PrefabUtility.LoadPrefabContents(CyberGenericPrefabPath);
        try
        {
            if (root.GetComponent("PlayerPawn") == null)
                root.AddComponent(playerPawnType);

            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, CyberGenericPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Type ResolvePlayerPawnType()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly =>
            {
                try
                {
                    return assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    return ex.Types.Where(type => type != null);
                }
            })
            .FirstOrDefault(type => type.Name == "PlayerPawn" && typeof(Component).IsAssignableFrom(type));
    }

    private static void ConfigureSwarmController(OctopusSwarmController swarmController, MonsterDoorTraversalGroup traversalGroup)
    {
        SerializedObject serialized = new SerializedObject(swarmController);
        serialized.FindProperty("doorTraversalGroup").objectReferenceValue = traversalGroup;
        serialized.FindProperty("moveSpeed").floatValue = 4.1f;
        serialized.FindProperty("patrolMoveSpeedMultiplier").floatValue = 0.45f;
        serialized.FindProperty("turnSpeed").floatValue = 7.5f;
        serialized.FindProperty("patrolRadius").floatValue = 6.5f;
        serialized.FindProperty("retargetDistance").floatValue = 0.5f;
        serialized.FindProperty("lingerDurationRange").vector2Value = new Vector2(1.4f, 2.4f);
        serialized.FindProperty("centerAcceleration").floatValue = 6.2f;
        serialized.FindProperty("centerDeceleration").floatValue = 8.5f;
        serialized.FindProperty("centerArrivalRadius").floatValue = 3.2f;
        serialized.FindProperty("patrolFormationRadius").floatValue = 2.45f;
        serialized.FindProperty("patrolOrbitSpeed").floatValue = 0.08f;
        serialized.FindProperty("patrolSlotPhaseJitter").floatValue = 0.7f;
        serialized.FindProperty("patrolSlotRadiusJitter").floatValue = 0.55f;
        serialized.FindProperty("patrolSlotDriftStrength").floatValue = 0.38f;
        serialized.FindProperty("patrolWanderStrength").floatValue = 0.65f;
        serialized.FindProperty("patrolOuterRingOffset").floatValue = 0.55f;
        serialized.FindProperty("chaseRadius").floatValue = 26f;
        serialized.FindProperty("chaseStopDistance").floatValue = 2.85f;
        serialized.FindProperty("chaseOrbitSpeed").floatValue = 0.42f;
        serialized.FindProperty("chaseRingRadius").floatValue = 3.1f;
        serialized.FindProperty("encircleInnerRadius").floatValue = 2.55f;
        serialized.FindProperty("encircleOuterRadius").floatValue = 3.05f;
        serialized.FindProperty("alertMemoryDuration").floatValue = 5f;
        serialized.FindProperty("alertSearchRadius").floatValue = 2.2f;
        serialized.FindProperty("closeCombatRingRadius").floatValue = 1.1f;
        serialized.FindProperty("drawGizmos").boolValue = true;
        serialized.FindProperty("drawSlotGizmos").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static OctopusSwarmPresentation EnsureSwarmPresentation(GameObject root)
    {
        if (root == null)
            return null;

        OctopusSwarmPresentation presentation = root.GetComponent<OctopusSwarmPresentation>();
        if (presentation == null)
            presentation = root.AddComponent<OctopusSwarmPresentation>();

        AudioSource rootSource = root.GetComponent<AudioSource>();
        if (rootSource == null)
            rootSource = root.AddComponent<AudioSource>();

        ConfigureAudioSource(rootSource, 1f, 22f);

        Transform anchor = root.transform.Find("OctopusSwarmAudioAnchor");
        if (anchor == null)
        {
            GameObject anchorObject = new GameObject("OctopusSwarmAudioAnchor");
            anchorObject.transform.SetParent(root.transform, false);
            anchor = anchorObject.transform;
        }

        AudioSource loopSource = anchor.GetComponent<AudioSource>();
        if (loopSource == null)
            loopSource = anchor.gameObject.AddComponent<AudioSource>();

        ConfigureAudioSource(loopSource, 1f, 22f);

        SerializedObject serializedPresentation = new SerializedObject(presentation);
        serializedPresentation.FindProperty("loopAnchor").objectReferenceValue = anchor;
        serializedPresentation.FindProperty("loopSource").objectReferenceValue = loopSource;
        serializedPresentation.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(anchor.gameObject);
        return presentation;
    }

    private static void ConfigureSwarmPresentation(OctopusSwarmController swarmController, OctopusSwarmPresentation presentation)
    {
        if (swarmController == null || presentation == null)
            return;

        SerializedObject serialized = new SerializedObject(swarmController);
        SerializedProperty presentationProperty = serialized.FindProperty("presentation");
        if (presentationProperty != null)
            presentationProperty.objectReferenceValue = presentation;

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureMemberAnimationParameters(OctopusSwarmMember member)
    {
        SerializedObject serializedMember = new SerializedObject(member);
        serializedMember.FindProperty("isMovingParameter").stringValue = "walk";
        serializedMember.FindProperty("isAttackingParameter").stringValue = "attack";
        serializedMember.FindProperty("idleParameter").stringValue = "idle";
        serializedMember.FindProperty("chaseParameter").stringValue = "chase";
        serializedMember.FindProperty("hitTriggerParameter").stringValue = "hit";
        serializedMember.FindProperty("dieTriggerParameter").stringValue = "die";
        serializedMember.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureMemberTuning(OctopusSwarmMember member)
    {
        SerializedObject serialized = new SerializedObject(member);
        serialized.FindProperty("moveSpeed").floatValue = 4.6f;
        serialized.FindProperty("patrolSpeedMultiplier").floatValue = 0.68f;
        serialized.FindProperty("turnSpeed").floatValue = 10.25f;
        serialized.FindProperty("localRoamRadius").floatValue = 1.15f;
        serialized.FindProperty("maxDistanceFromCenter").floatValue = 4.9f;
        serialized.FindProperty("retargetIntervalRange").vector2Value = new Vector2(1.8f, 3.5f);
        serialized.FindProperty("formationDestinationSmoothing").floatValue = 1.8f;
        serialized.FindProperty("formationDestinationRefreshDistance").floatValue = 0.55f;
        serialized.FindProperty("targetFacingTurnMultiplier").floatValue = 1.75f;
        serialized.FindProperty("combatFaceDistance").floatValue = 12f;
        serialized.FindProperty("slotReturnEnterDistance").floatValue = 1.5f;
        serialized.FindProperty("slotReturnExitDistance").floatValue = 0.7f;
        serialized.FindProperty("alertWanderStrength").floatValue = 0.45f;
        serialized.FindProperty("sightRange").floatValue = 15f;
        serialized.FindProperty("sightHalfAngle").floatValue = 62f;
        serialized.FindProperty("sightEyeHeight").floatValue = 1.35f;
        serialized.FindProperty("sightTargetHeight").floatValue = 1f;
        serialized.FindProperty("sightRefreshInterval").floatValue = 0.1f;
        serialized.FindProperty("sightOcclusionMask").intValue = ~0;
        serialized.FindProperty("separationRadius").floatValue = 1.1f;
        serialized.FindProperty("separationStrength").floatValue = 1.2f;
        serialized.FindProperty("maxHealth").intValue = 1;
        serialized.FindProperty("currentHealth").intValue = 1;
        serialized.FindProperty("deathDisableDelay").floatValue = 0.65f;
        serialized.FindProperty("attackDamage").intValue = 1;
        serialized.FindProperty("attackEngageDistance").floatValue = 6.4f;
        serialized.FindProperty("attackHoldDistance").floatValue = 2.2f;
        serialized.FindProperty("attackSlotEnterDistance").floatValue = 0.75f;
        serialized.FindProperty("attackCommitDistance").floatValue = 0.95f;
        serialized.FindProperty("attackHitRadius").floatValue = 1.7f;
        serialized.FindProperty("attackRecoverDistance").floatValue = 2.1f;
        serialized.FindProperty("attackCooldownRange").vector2Value = new Vector2(0.8f, 1.25f);
        serialized.FindProperty("attackStaggerPerMember").floatValue = 0.14f;
        serialized.FindProperty("attackStaggerJitter").floatValue = 0.08f;
        serialized.FindProperty("attackWindupRange").vector2Value = new Vector2(0.24f, 0.38f);
        serialized.FindProperty("attackRecoverRange").vector2Value = new Vector2(0.16f, 0.28f);
        serialized.FindProperty("walkAnimationReferenceSpeed").floatValue = 2.8f;
        serialized.FindProperty("chaseAnimationReferenceSpeed").floatValue = 4.6f;
        serialized.FindProperty("minMovementAnimationSpeed").floatValue = 0.9f;
        serialized.FindProperty("maxMovementAnimationSpeed").floatValue = 1.55f;
        serialized.FindProperty("hitReactionDuration").floatValue = 0.22f;
        serialized.FindProperty("drawDebugGizmos").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureMemberPresentationAudio(GameObject octopus, OctopusSwarmMember member)
    {
        if (octopus == null || member == null)
            return;

        AudioSource source = octopus.GetComponent<AudioSource>();
        if (source == null)
            source = octopus.AddComponent<AudioSource>();

        ConfigureAudioSource(source, 1f, 22f);

        SerializedObject serializedMember = new SerializedObject(member);
        serializedMember.FindProperty("presentationAudioSource").objectReferenceValue = source;
        serializedMember.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(octopus);
    }

    private static void ConfigureAudioSource(AudioSource source, float minDistance, float maxDistance)
    {
        if (source == null)
            return;

        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.minDistance = Mathf.Max(0.01f, minDistance);
        source.maxDistance = Mathf.Max(source.minDistance, maxDistance);
        source.rolloffMode = AudioRolloffMode.Logarithmic;
    }

    private static string ValidateAnimatorContract(AnimatorController controllerAsset)
    {
        if (controllerAsset == null)
            return "Animator contract unavailable";

        List<string> missing = new();
        ValidateParameter(controllerAsset, "walk", AnimatorControllerParameterType.Bool, missing);
        ValidateParameter(controllerAsset, "chase", AnimatorControllerParameterType.Bool, missing);
        ValidateParameter(controllerAsset, "idle", AnimatorControllerParameterType.Bool, missing);
        ValidateParameter(controllerAsset, "attack", AnimatorControllerParameterType.Trigger, missing);
        ValidateParameter(controllerAsset, "hit", AnimatorControllerParameterType.Trigger, missing);
        ValidateParameter(controllerAsset, "die", AnimatorControllerParameterType.Trigger, missing);

        return missing.Count == 0
            ? "Animator contract ok"
            : $"Animator contract missing: {string.Join(", ", missing)}";
    }

    private static void ValidateParameter(AnimatorController controllerAsset, string parameterName, AnimatorControllerParameterType expectedType, List<string> missing)
    {
        bool exists = controllerAsset.parameters.Any(parameter => parameter.name == parameterName && parameter.type == expectedType);
        if (!exists)
            missing.Add($"{parameterName}:{expectedType}");
    }

    private static void EnsureProductionFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Monster/Octopus"))
            AssetDatabase.CreateFolder("Assets/Monster", "Octopus");
    }

    private static void ConfigureProductionMemberInfrastructure(
        GameObject octopus,
        OctopusSwarmMember member,
        Animator animator,
        NavMeshAgent agent,
        MonsterDoorTraversalGroup traversalGroup,
        int stableMemberId)
    {
        int monsterLayer = LayerMask.NameToLayer("Monster");
        if (monsterLayer >= 0)
            SetLayerRecursively(octopus, monsterLayer);

        CapsuleCollider damageCollider = EnsureDamageCollider(octopus);
        NetworkTransform networkTransform = octopus.GetComponent<NetworkTransform>() ?? octopus.AddComponent<NetworkTransform>();
        NetworkAnimator networkAnimator = octopus.GetComponent<NetworkAnimator>() ?? octopus.AddComponent<NetworkAnimator>();
        DoorAutoOpener doorAutoOpener = octopus.GetComponent<DoorAutoOpener>() ?? octopus.AddComponent<DoorAutoOpener>();
        doorAutoOpener.ConfigureGroup(traversalGroup, true);

        SerializedObject transformSo = new SerializedObject(networkTransform);
        transformSo.FindProperty("_syncPosition").intValue = 1;
        transformSo.FindProperty("_syncRotation").intValue = 1;
        transformSo.FindProperty("_syncScale").boolValue = false;
        transformSo.FindProperty("_syncParent").boolValue = true;
        transformSo.FindProperty("_interpolateSettings").intValue = 3;
        transformSo.FindProperty("_ownerAuth").boolValue = false;
        transformSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject animatorSo = new SerializedObject(networkAnimator);
        animatorSo.FindProperty("_animator").objectReferenceValue = animator;
        animatorSo.FindProperty("_ownerAuth").boolValue = false;
        animatorSo.FindProperty("_autoSyncParameters").boolValue = true;
        animatorSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject memberSo = new SerializedObject(member);
        memberSo.FindProperty("networkAnimator").objectReferenceValue = networkAnimator;
        memberSo.FindProperty("networkTransform").objectReferenceValue = networkTransform;
        memberSo.FindProperty("agent").objectReferenceValue = agent;
        memberSo.FindProperty("damageCollider").objectReferenceValue = damageCollider;
        memberSo.FindProperty("doorAutoOpener").objectReferenceValue = doorAutoOpener;
        memberSo.FindProperty("stableMemberId").intValue = stableMemberId;
        memberSo.FindProperty("maxHealth").intValue = 1;
        memberSo.FindProperty("currentHealth").intValue = 1;
        memberSo.FindProperty("attackDamage").intValue = 1;
        memberSo.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(octopus);
    }

    private static CapsuleCollider EnsureDamageCollider(GameObject octopus)
    {
        CapsuleCollider collider = octopus.GetComponent<CapsuleCollider>() ?? octopus.AddComponent<CapsuleCollider>();
        Renderer[] renderers = octopus.GetComponentsInChildren<Renderer>(true);

        Bounds bounds = new Bounds(octopus.transform.position + Vector3.up * 0.45f, new Vector3(0.55f, 0.9f, 0.55f));
        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        Vector3 scale = octopus.transform.lossyScale;
        float scaleY = Mathf.Max(0.0001f, Mathf.Abs(scale.y));
        float scaleXZ = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
        float worldHeight = Mathf.Clamp(bounds.size.y * 0.9f, 0.45f, 1.6f);
        float worldRadius = Mathf.Clamp(Mathf.Min(bounds.size.x, bounds.size.z) * 0.38f, 0.18f, 0.45f);

        collider.direction = 1;
        collider.center = octopus.transform.InverseTransformPoint(bounds.center);
        collider.radius = worldRadius / scaleXZ;
        collider.height = Mathf.Max(worldHeight / scaleY, collider.radius * 2f);
        collider.isTrigger = false;
        collider.enabled = true;

        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
            collider.excludeLayers = collider.excludeLayers.value | (1 << playerLayer);

        return collider;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            transforms[i].gameObject.layer = layer;
    }

    private static string RegisterProductionSpawner()
    {
        GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(SwarmPrefabPath);
        OctopusSwarmController networkPrefab = prefabRoot != null ? prefabRoot.GetComponent<OctopusSwarmController>() : null;
        if (networkPrefab == null)
            return $"Production Octopus NetworkIdentity missing: {SwarmPrefabPath}";

        Scene scene = EditorSceneManager.OpenScene(StartMapScenePath, OpenSceneMode.Single);
        DungeonMonsterSpawner spawner = UnityEngine.Object.FindFirstObjectByType<DungeonMonsterSpawner>();
        if (spawner == null)
            return $"DungeonMonsterSpawner missing: {StartMapScenePath}";

        SerializedObject spawnerSo = new SerializedObject(spawner);
        SerializedProperty monsters = spawnerSo.FindProperty("monsters");
        int entryIndex = -1;
        for (int i = 0; i < monsters.arraySize; i++)
        {
            SerializedProperty prefabProperty = monsters.GetArrayElementAtIndex(i).FindPropertyRelative("prefab");
            if (prefabProperty.objectReferenceValue == networkPrefab)
            {
                entryIndex = i;
                break;
            }
        }

        bool added = entryIndex < 0;
        if (added)
        {
            entryIndex = monsters.arraySize;
            monsters.InsertArrayElementAtIndex(entryIndex);
        }

        SerializedProperty entry = monsters.GetArrayElementAtIndex(entryIndex);
        entry.FindPropertyRelative("prefab").objectReferenceValue = networkPrefab;
        entry.FindPropertyRelative("weight").floatValue = ProductionSpawnWeight;
        spawnerSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(spawner);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return added
            ? $"Registered Octopus in production spawner (weight={ProductionSpawnWeight:0.##})"
            : $"Updated Octopus production spawner entry (weight={ProductionSpawnWeight:0.##})";
    }

    private static string RegisterNetworkPrefab()
    {
        GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(SwarmPrefabPath);
        if (prefabRoot == null)
            return $"Production Octopus prefab missing: {SwarmPrefabPath}";

        NetworkPrefabs networkPrefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabs>(NetworkPrefabsPath);
        if (networkPrefabs == null)
            return $"PurrNet NetworkPrefabs asset missing: {NetworkPrefabsPath}";

        for (int i = 0; i < networkPrefabs.prefabs.Count; i++)
        {
            if (networkPrefabs.prefabs[i].prefab == prefabRoot)
                return "Octopus already registered in PurrNet NetworkPrefabs";
        }

        networkPrefabs.prefabs.Add(new NetworkPrefabs.UserPrefabData
        {
            prefab = prefabRoot,
            pooled = false,
            warmupCount = 0
        });
        EditorUtility.SetDirty(networkPrefabs);
        AssetDatabase.SaveAssets();
        return "Registered Octopus in PurrNet NetworkPrefabs";
    }

    private static string ValidateProductionPrefab()
    {
        List<string> failures = new();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SwarmPrefabPath);
        if (prefab == null)
            return $"FAIL: prefab missing at {SwarmPrefabPath}";

        OctopusSwarmController controller = prefab.GetComponent<OctopusSwarmController>();
        if (controller == null)
            failures.Add("root OctopusSwarmController missing");
        if (prefab.GetComponent<MonsterHealth>() != null)
            failures.Add("root MonsterHealth must stay absent for independent HP");
        if (prefab.GetComponent<MonsterDoorTraversalGroup>() == null)
            failures.Add("root MonsterDoorTraversalGroup missing");

        NetworkPrefabs networkPrefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabs>(NetworkPrefabsPath);
        bool registeredForNetworkSpawn = networkPrefabs != null &&
            networkPrefabs.prefabs.Any(entry => entry.prefab == prefab);
        if (!registeredForNetworkSpawn)
            failures.Add("PurrNet NetworkPrefabs registration missing");

        OctopusSwarmMember[] members = prefab.GetComponentsInChildren<OctopusSwarmMember>(true);
        if (members.Length != DesiredMemberCount)
            failures.Add($"member count expected={DesiredMemberCount} actual={members.Length}");

        int monsterLayer = LayerMask.NameToLayer("Monster");
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer < 0)
            failures.Add("Player layer missing");

        HashSet<int> stableIds = new();
        for (int i = 0; i < members.Length; i++)
        {
            OctopusSwarmMember member = members[i];
            SerializedObject memberSo = new SerializedObject(member);
            int maxHealth = memberSo.FindProperty("maxHealth").intValue;
            int attackDamage = memberSo.FindProperty("attackDamage").intValue;
            int stableId = memberSo.FindProperty("stableMemberId").intValue;
            float slotReturnEnterDistance = memberSo.FindProperty("slotReturnEnterDistance").floatValue;
            float slotReturnExitDistance = memberSo.FindProperty("slotReturnExitDistance").floatValue;
            float patrolSpeedMultiplier = memberSo.FindProperty("patrolSpeedMultiplier").floatValue;
            Collider damageCollider = memberSo.FindProperty("damageCollider").objectReferenceValue as Collider;
            NavMeshAgent agent = member.GetComponent<NavMeshAgent>();

            if (maxHealth != 1)
                failures.Add($"{member.name}: maxHealth={maxHealth}");
            if (attackDamage != 1)
                failures.Add($"{member.name}: attackDamage={attackDamage}");
            if (!stableIds.Add(stableId))
                failures.Add($"{member.name}: duplicate stableMemberId={stableId}");
            if (slotReturnEnterDistance <= slotReturnExitDistance)
                failures.Add($"{member.name}: soft-slot hysteresis is invalid");
            if (patrolSpeedMultiplier >= 1f)
                failures.Add($"{member.name}: patrol speed must remain below chase speed");
            if (damageCollider == null || !damageCollider.enabled || damageCollider.isTrigger)
                failures.Add($"{member.name}: enabled non-trigger damage collider missing");
            if (damageCollider != null && playerLayer >= 0 &&
                (damageCollider.excludeLayers.value & (1 << playerLayer)) == 0)
                failures.Add($"{member.name}: damage collider must exclude Player contacts");
            if (monsterLayer >= 0 && member.gameObject.layer != monsterLayer)
                failures.Add($"{member.name}: not on Monster layer");
            if (member.GetComponent<NetworkTransform>() == null)
                failures.Add($"{member.name}: NetworkTransform missing");
            if (member.GetComponent<NetworkAnimator>() == null)
                failures.Add($"{member.name}: NetworkAnimator missing");
            if (member.GetComponent<DoorAutoOpener>() == null)
                failures.Add($"{member.name}: DoorAutoOpener missing");
            if (agent == null || agent.agentTypeID != ClownNavMeshConfig.AgentTypeId)
                failures.Add($"{member.name}: production NavMesh agent type mismatch");
            if (agent != null && agent.autoTraverseOffMeshLink)
                failures.Add($"{member.name}: autoTraverseOffMeshLink must be disabled");
        }

        return failures.Count == 0
            ? $"PASS: Octopus production prefab members={members.Length}, HP=1, damage=1, independent hurtboxes/network identities/navmesh"
            : $"FAIL ({failures.Count}): {string.Join(" | ", failures)}";
    }

    private static void ConfigureAgent(NavMeshAgent agent)
    {
        agent.agentTypeID = ClownNavMeshConfig.AgentTypeId;
        agent.radius = 0.2f;
        agent.height = 0.5f;
        agent.baseOffset = 0.2f;
        agent.speed = 2.6f;
        agent.angularSpeed = 600f;
        agent.acceleration = 12f;
        agent.stoppingDistance = 0.08f;
        agent.autoBraking = false;
        agent.autoTraverseOffMeshLink = false;
        agent.updateUpAxis = true;
        agent.updateRotation = false;
    }

    private static void EnsureAttackHitbox(GameObject octopus)
    {
        Transform hitboxTransform = octopus.transform.Find("AttackHitbox");
        GameObject hitboxObject = hitboxTransform != null ? hitboxTransform.gameObject : new GameObject("AttackHitbox");
        if (hitboxTransform == null)
            hitboxObject.transform.SetParent(octopus.transform, false);

        hitboxObject.transform.localPosition = new Vector3(0f, 1.15f, 1.35f);
        hitboxObject.transform.localRotation = Quaternion.identity;

        BoxCollider boxCollider = hitboxObject.GetComponent<BoxCollider>();
        if (boxCollider == null)
            boxCollider = hitboxObject.AddComponent<BoxCollider>();

        boxCollider.isTrigger = true;
        boxCollider.enabled = false;

        Rigidbody rigidbody = hitboxObject.GetComponent<Rigidbody>();
        if (rigidbody == null)
            rigidbody = hitboxObject.AddComponent<Rigidbody>();

        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;

        OctopusAttackHitbox attackHitbox = hitboxObject.GetComponent<OctopusAttackHitbox>();
        if (attackHitbox == null)
            attackHitbox = hitboxObject.AddComponent<OctopusAttackHitbox>();

        OctopusSwarmMember member = octopus.GetComponent<OctopusSwarmMember>();
        attackHitbox.SetOwner(member);
        attackHitbox.ConfigureRadius(1.7f);
        EditorUtility.SetDirty(hitboxObject);
    }

    private static void EnsureAttackClipEvents()
    {
        ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
            return;

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;

        if (clips == null || clips.Length == 0)
            return;

        bool changed = false;
        for (int i = 0; i < clips.Length; i++)
        {
            if (!clips[i].name.Contains("Attack", StringComparison.OrdinalIgnoreCase))
                continue;

            float duration = Mathf.Max(0.1f, (clips[i].lastFrame - clips[i].firstFrame) / 30f);
            AnimationEvent[] desiredEvents =
            {
                new AnimationEvent { functionName = "AnimationEvent_EnableAttackHitbox", time = duration * 0.55f },
                new AnimationEvent { functionName = "AnimationEvent_DisableAttackHitbox", time = duration * 0.68f },
            };

            if (ClipEventsMatch(clips[i].events, desiredEvents))
                continue;

            clips[i].events = desiredEvents;
            changed = true;
        }

        if (!changed)
            return;

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    private static bool ClipEventsMatch(AnimationEvent[] existing, AnimationEvent[] desired)
    {
        if (existing == null || desired == null || existing.Length != desired.Length)
            return false;

        for (int i = 0; i < existing.Length; i++)
        {
            if (!string.Equals(existing[i].functionName, desired[i].functionName, StringComparison.Ordinal))
                return false;

            if (Mathf.Abs(existing[i].time - desired[i].time) > 0.01f)
                return false;
        }

        return true;
    }

    private static GameObject FindRoot(string rootName)
    {
        return UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(t => t.parent == null && t.name == rootName)
            .Select(t => t.gameObject)
            .FirstOrDefault();
    }

    private static void ClearChildren(Transform root)
    {
        if (root == null)
            return;

        for (int i = root.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
    }

    private static GameObject CreateBox(Transform parent, string name, Vector3 position, Vector3 size, Color color)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent);
        box.transform.position = position;
        box.transform.localScale = size;

        Renderer renderer = box.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material sharedMaterial = renderer.sharedMaterial;
            if (sharedMaterial != null)
            {
                Material instanceMaterial = new Material(sharedMaterial);
                instanceMaterial.color = color;
                renderer.sharedMaterial = instanceMaterial;
            }
        }

        return box;
    }

    private static Component GetOrAddComponent(GameObject gameObject, string fullTypeName)
    {
        if (gameObject == null || string.IsNullOrWhiteSpace(fullTypeName))
            return null;

        Type type = ResolveType(fullTypeName);
        if (type == null || !typeof(Component).IsAssignableFrom(type))
            return null;

        Component existing = gameObject.GetComponent(type);
        return existing != null ? existing : gameObject.AddComponent(type);
    }

    private static void RemoveComponentIfPresent(GameObject gameObject, string fullTypeName)
    {
        if (gameObject == null || string.IsNullOrWhiteSpace(fullTypeName))
            return;

        Type type = ResolveType(fullTypeName);
        if (type == null || !typeof(Component).IsAssignableFrom(type))
            return;

        Component existing = gameObject.GetComponent(type);
        if (existing != null)
            UnityEngine.Object.DestroyImmediate(existing, true);
    }

    private static Type ResolveType(string fullTypeName)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(fullTypeName, false))
            .FirstOrDefault(type => type != null);
    }
}
