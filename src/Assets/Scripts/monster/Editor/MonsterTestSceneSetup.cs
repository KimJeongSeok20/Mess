using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using PurrNet;
using PurrNet.Transports;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class MonsterTestSceneSetup
{
    private const string ScenePath = "Assets/Tests/CombatFeel/StartMap_GunMonsterCombatTest.unity";
    private const string CyberGenericPrefabPath = "Assets/FPS/Cyber_Generic.prefab";
    private const string CombatTestPlayerPrefabPath = "Assets/Tests/CombatFeel/Cyber_Generic_CombatTest.prefab";
    private const string CombatTestNetworkPrefabsPath = "Assets/Tests/CombatFeel/CombatFeelNetworkPrefabs.asset";
    private const string NetworkManagerPrefabPath = "Assets/Test/NetworkManager.prefab";
    private const string GameManagerPrefabPath = "Assets/Test/GameManager.prefab";
    private const string CanvasPrefabPath = "Assets/Test/Canvas.prefab";
    private const string PlayerControlsPath = "Assets/scriptable-animation-system-main/Assets/Demo/Settings/PlayerControls.inputactions";
    private const string SmilyPrefabPath = "Assets/Monster/smily/smily-horror-monster_cc_attribution/smily_fixed.prefab";
    private const string ClownPrefabPath = "Assets/Clown/Prefab/Clown skin2 combined.prefab";
    private const string OctopusPrefabPath = "Assets/Monster/Octopus/OctopusSwarm.prefab";
    private const string DungeonDoorPrefabPath = "Assets/Prefabs/map_piece/NewPrison/SomePicees/Door_SM_A_Door_Placement.prefab";
    private const string SpawnPointName = "PlayerSpawnPoint";
    private const string LoadoutRootName = "TestLoadout";
    private const string BlockersRootName = "ArenaBlockers";
    private const string RangeMarkersRootName = "CombatRangeMarkers";
    private const string DoorTestRootName = "DungeonDoorTestLane";
    private const float DoorLinkCostModifier = 8f;
    private const float ArenaLaneDepth = 60f;
    private const float ArenaLaneCapThickness = 1.25f;
    private const float ArenaLaneOuterWidth = 25f;
    private const float DoorBarrierThickness = 1.25f;
    private const float DoorOpeningHalfWidth = 3.5f;
    private const float DoorLinkHalfDepth = 1.75f;
    private const float SideWallInnerX = 11.5f;
    private const string ArenaPlaneName = "Plane";
    private const string MonsterRigName = "MonsterTestRig";
    private const string SpawnRootName = "MonsterSpawnRoot";
    private const string SpawnAnchorName = "MonsterSpawnAnchor";
    private const string TargetDummyName = "AnalysisTargetDummy";
    private const string MarkerRootName = "MonsterAnalysisMarkers";
    private const string PanelName = "MonsterTestPanel";
    private const string EventSystemName = "EventSystem";
    private const float SpawnAnchorForwardOffset = 12f;

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

    [MenuItem("Tools/Monster Test/Rebuild StartMap Gun Monster Combat Scene")]
    public static void RebuildSceneMenu()
    {
        Debug.Log(RebuildScene());
    }

    public static void RebuildSceneBatch()
    {
        Debug.Log(RebuildScene());
    }

    [MenuItem("Tools/Monster Test/Validate Gun Combat Feel Scene")]
    public static void ValidateCombatFeelSceneMenu()
    {
        Debug.Log(ValidateCombatFeelScene());
    }

    public static void ValidateCombatFeelSceneBatch()
    {
        Debug.Log(ValidateCombatFeelScene());
    }

    [MenuItem("Tools/Monster Test/Apply Door Traversal Defaults")]
    public static void ApplyDoorTraversalDefaultsMenu()
    {
        Debug.Log(ApplyDoorTraversalDefaults());
    }

    public static string ApplyDoorTraversalDefaults()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        EnsureCurrentDoorLinks();
        ConfigureArenaPlane(FindRoot(ArenaPlaneName), scene);
        ConfigureDoorAutoOpenerPrefab(SmilyPrefabPath);
        ConfigureDoorAutoOpenerPrefab(ClownPrefabPath);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return "Applied shared monster door traversal defaults without rebuilding the monster test lane.";
    }

    public static string DescribeDoorTraversalDefaults()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        StringBuilder report = new StringBuilder();
        report.AppendLine($"Scene: {scene.path} dirty={scene.isDirty}");

        foreach (NavMeshLink link in UnityEngine.Object.FindObjectsByType<NavMeshLink>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (link == null || link.transform.parent == null || link.transform.parent.name != "DungeonDoor")
                continue;

            MonsterDoorLinkBinding binding = link.GetComponent<MonsterDoorLinkBinding>();
            float length = Vector3.Distance(link.startPoint, link.endPoint);
            report.AppendLine($"Link {link.name}: agent={link.agentTypeID}, area={link.area}, width={link.width:F2}, length={length:F2}, local={link.transform.localPosition}, binding={binding != null}, canOpen={binding != null && binding.CanOpen}");
        }

        GameObject plane = FindRoot(ArenaPlaneName);
        if (plane != null)
        {
            foreach (NavMeshSurface surface in plane.GetComponents<NavMeshSurface>())
                report.AppendLine($"Surface: agent={surface.agentTypeID}, collect={surface.collectObjects}, layerMask={surface.layerMask.value}, defaultArea={surface.defaultArea}, hasData={surface.navMeshData != null}");
        }

        int missingScriptCount = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                missingScriptCount += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
        }

        report.AppendLine($"Missing scripts: {missingScriptCount}");
        return report.ToString();
    }

    public static string ValidateCombatFeelScene()
    {
        Scene previousActiveScene = EditorSceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool wasAlreadyLoaded = scene.IsValid() && scene.isLoaded;

        if (!wasAlreadyLoaded)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        SceneManager.SetActiveScene(scene);

        try
        {
            List<string> failures = new List<string>();
            GameObject rigRoot = FindRoot(MonsterRigName);
            MonsterTestSceneController controller = rigRoot != null ? rigRoot.GetComponent<MonsterTestSceneController>() : null;
            if (controller == null)
                failures.Add("MonsterTestRig is missing MonsterTestSceneController.");

            if (FindRoot(SpawnPointName) == null)
                failures.Add("PlayerSpawnPoint is missing.");
            if (FindRoot(TargetDummyName) == null)
                failures.Add("AnalysisTargetDummy is missing.");
            if (FindRoot(LoadoutRootName) == null)
                failures.Add("TestLoadout is missing.");
            GameObject rangeMarkers = FindRoot(RangeMarkersRootName);
            if (rangeMarkers == null || rangeMarkers.transform.childCount != 3)
                failures.Add("Combat range markers for 7m / 14m / 22m are missing.");
            if (FindRoot("NetworkManager") == null)
                failures.Add("NetworkManager is missing.");

            GameObject canvasRoot = FindRoot("Canvas");
            GameObject panel = canvasRoot != null ? FindChild(canvasRoot.transform, PanelName) : null;
            if (panel == null)
            {
                failures.Add("Combat feel control panel is missing.");
            }
            else
            {
                string[] requiredControls =
                {
                    "LiveCombatButton",
                    "SafeObservationButton",
                    "CloseRangeButton",
                    "MediumRangeButton",
                    "LongRangeButton",
                    "SpawnSmilyButton",
                    "SpawnClownButton",
                    "SpawnOctopusButton",
                    "RespawnButton",
                    "MonsterStatusText",
                    "CombatFeelChecklistText"
                };

                for (int i = 0; i < requiredControls.Length; i++)
                {
                    if (FindChild(panel.transform, requiredControls[i]) == null)
                        failures.Add($"Combat feel panel is missing {requiredControls[i]}.");
                }
            }

            for (int i = 0; i < WeaponPaths.Length; i++)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(WeaponPaths[i]) == null)
                    failures.Add($"Weapon loadout asset is missing: {WeaponPaths[i]}");
                if (AssetDatabase.LoadAssetAtPath<GameObject>(AmmoPaths[i]) == null)
                    failures.Add($"Ammo loadout asset is missing: {AmmoPaths[i]}");
            }

            int missingScriptCount = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                    missingScriptCount += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            }

            if (missingScriptCount > 0)
                failures.Add($"Scene contains {missingScriptCount} missing script reference(s).");

            if (failures.Count > 0)
                throw new InvalidOperationException("Gun combat feel scene validation failed:\n- " + string.Join("\n- ", failures));

            return "PASS: gun combat feel scene has real player/network bootstrap, weapon rack, three production monster choices, two target modes, three ranges, reset, metrics UI, and no missing scripts.";
        }
        finally
        {
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                SceneManager.SetActiveScene(previousActiveScene);

            if (!wasAlreadyLoaded && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    public static string RebuildScene()
    {
        Scene previousActiveScene = EditorSceneManager.GetActiveScene();
        SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool wasAlreadyLoaded = scene.IsValid() && scene.isLoaded;

        if (!wasAlreadyLoaded)
        {
            scene = sceneAsset != null
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        }

        SceneManager.SetActiveScene(scene);

        try
        {
            EnsureCombatTestPlayerPrefab();

            GameObject plane = EnsureArenaPlane();
            Transform playerSpawnPoint = EnsurePlayerSpawnPoint();
            Vector3 analysisTargetPosition = GetAnalysisTargetPosition(playerSpawnPoint);
            Transform spawnAnchor = EnsureSpawnAnchor(analysisTargetPosition);
            Transform markerRoot = EnsureAnalysisMarkers(analysisTargetPosition);
            GameObject targetDummy = EnsureTargetDummy(analysisTargetPosition);

            GameObject canvasRoot = EnsureSceneBootstrap(playerSpawnPoint);
            EnsureTestLoadout(playerSpawnPoint);
            EnsureArenaBlockers(playerSpawnPoint, analysisTargetPosition);
            EnsureRangeMarkers(playerSpawnPoint);
            RemoveLegacyMonsterRoots();
            EnsureEventSystem();
            EnsureMonsterRig(canvasRoot, spawnAnchor, targetDummy.transform, markerRoot, playerSpawnPoint);
            ConfigureArenaPlane(plane, scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return $"Configured gun-vs-monster combat feel hub without replacing the previously active scene: {ScenePath}";
        }
        finally
        {
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                SceneManager.SetActiveScene(previousActiveScene);

            if (!wasAlreadyLoaded && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void EnsureCurrentDoorLinks()
    {
        NavMeshLink baseLink = FindDoorLinkBase();
        if (baseLink == null || baseLink.transform.parent == null)
            return;

        Transform linkParent = baseLink.transform.parent;
        foreach (NavMeshLink existingLink in linkParent.GetComponentsInChildren<NavMeshLink>(true))
            ConfigureDoorLinkBinding(existingLink);

        foreach (int agentTypeId in GetRequiredArenaAgentTypeIds())
        {
            bool exists = linkParent.GetComponentsInChildren<NavMeshLink>(true)
                .Any(link => link != null && link.agentTypeID == agentTypeId);

            if (exists)
                continue;

            NavMeshLink clonedLink = CreateDoorNavMeshLinkCopy(linkParent, baseLink, agentTypeId);
            ConfigureDoorLinkBinding(clonedLink);
        }
    }

    private static NavMeshLink FindDoorLinkBase()
    {
        NavMeshLink[] links = UnityEngine.Object.FindObjectsByType<NavMeshLink>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        NavMeshLink fallback = null;

        foreach (NavMeshLink link in links)
        {
            if (link == null)
                continue;

            if (link.name == "DoorWayPoint")
                return link;

            if (fallback == null && link.transform.parent != null && link.transform.parent.name == "DungeonDoor")
                fallback = link;
        }

        return fallback;
    }

    private static NavMeshLink CreateDoorNavMeshLinkCopy(Transform parent, NavMeshLink source, int agentTypeId)
    {
        GameObject linkObject = new GameObject(GetDoorLinkName(agentTypeId), typeof(NavMeshLink), typeof(MonsterDoorLinkBinding));
        linkObject.transform.SetParent(parent, false);
        linkObject.transform.localPosition = source.transform.localPosition;
        linkObject.transform.localRotation = source.transform.localRotation;
        linkObject.transform.localScale = source.transform.localScale;

        NavMeshLink link = linkObject.GetComponent<NavMeshLink>();
        link.agentTypeID = agentTypeId;
        link.area = source.area;
        link.costModifier = source.costModifier;
        link.bidirectional = source.bidirectional;
        link.width = source.width;
        link.startPoint = source.startPoint;
        link.endPoint = source.endPoint;
        link.autoUpdate = source.autoUpdate;
        link.UpdateLink();

        EditorUtility.SetDirty(linkObject);
        return link;
    }

    private static void ConfigureDoorLinkBinding(NavMeshLink link)
    {
        if (link == null)
            return;

        MonsterDoorLinkBinding binding = link.GetComponent<MonsterDoorLinkBinding>();
        if (binding == null)
            binding = link.gameObject.AddComponent<MonsterDoorLinkBinding>();

        Transform parent = link.transform.parent;
        Door regularDoor = link.GetComponentInParent<Door>() ?? parent?.GetComponentInChildren<Door>(true);
        SlidingDoor slidingDoor = link.GetComponentInParent<SlidingDoor>() ?? parent?.GetComponentInChildren<SlidingDoor>(true);
        DoorInteractable doorInteractable = link.GetComponentInParent<DoorInteractable>() ?? parent?.GetComponentInChildren<DoorInteractable>(true);
        DoorInteraction doorInteraction = link.GetComponentInParent<DoorInteraction>() ?? parent?.GetComponentInChildren<DoorInteraction>(true);
        DoorAnimatorOpener doorAnimatorOpener = link.GetComponentInParent<DoorAnimatorOpener>() ?? parent?.GetComponentInChildren<DoorAnimatorOpener>(true);
        DunGen.Door dungeonDoor = link.GetComponentInParent<DunGen.Door>() ?? parent?.GetComponentInChildren<DunGen.Door>(true);

        SerializedObject so = new SerializedObject(binding);
        SetObjectReference(so, "navMeshLink", link);
        SetObjectReference(so, "regularDoor", regularDoor);
        SetObjectReference(so, "slidingDoor", slidingDoor);
        SetObjectReference(so, "doorInteractable", doorInteractable);
        SetObjectReference(so, "doorInteraction", doorInteraction);
        SetObjectReference(so, "doorAnimatorOpener", doorAnimatorOpener);
        SetObjectReference(so, "dungeonDoor", dungeonDoor);
        so.ApplyModifiedPropertiesWithoutUndo();
        binding.RefreshPassageCollidersFromReferences();

        EditorUtility.SetDirty(binding);
    }

    private static void SetObjectReference(SerializedObject so, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property != null)
            property.objectReferenceValue = value;
    }

    private static void ConfigureDoorAutoOpenerPrefab(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
            return;

        try
        {
            foreach (NavMeshAgent agent in root.GetComponentsInChildren<NavMeshAgent>(true))
            {
                if (agent == null)
                    continue;

                DoorAutoOpener opener = agent.GetComponent<DoorAutoOpener>();
                if (opener == null)
                    opener = agent.gameObject.AddComponent<DoorAutoOpener>();

                SerializedObject so = new SerializedObject(opener);
                SetFloat(so, "openDelaySeconds", 0.8f);
                SetFloat(so, "waitUntilOpenSeconds", 2f);
                SetFloat(so, "doorSearchRadius", 2f);
                SetFloat(so, "destinationCancelDistance", 0.75f);
                SetBool(so, "canOpenDoors", true);
                so.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(opener);
            }

            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
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

    private static GameObject EnsureSceneBootstrap(Transform spawnPoint)
    {
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CombatTestPlayerPrefabPath);
        GameObject networkRoot = EnsurePrefabRoot(NetworkManagerPrefabPath, "NetworkManager");
        GameObject gameManagerRoot = EnsurePrefabRoot(GameManagerPrefabPath, "GameManager");
        GameObject canvasRoot = EnsurePrefabRoot(CanvasPrefabPath, "Canvas");

        if (networkRoot != null)
        {
            NetworkManager networkManager = networkRoot.GetComponent<NetworkManager>() ?? networkRoot.AddComponent<NetworkManager>();
            UDPTransport udpTransport = networkRoot.GetComponent<UDPTransport>() ?? networkRoot.AddComponent<UDPTransport>();
            PlayerSpawner playerSpawner = networkRoot.GetComponent<PlayerSpawner>() ?? networkRoot.AddComponent<PlayerSpawner>();
            NetworkPrefabs combatNetworkPrefabs = EnsureCombatTestNetworkPrefabs(playerPrefab);

            RemoveComponentIfPresent(networkRoot, "PurrLobby.MyConnectionStarter");
            RemoveComponentIfPresent(networkRoot, "PurrNet.Steam.SteamTransport");

            if (networkRoot.GetComponent<MonsterTestAutoHost>() == null)
                networkRoot.AddComponent<MonsterTestAutoHost>();

            SerializedObject networkManagerSo = new SerializedObject(networkManager);
            SerializedProperty transportProp = networkManagerSo.FindProperty("_transport");
            if (transportProp != null)
                transportProp.objectReferenceValue = udpTransport;
            SerializedProperty networkPrefabsProp = networkManagerSo.FindProperty("_networkPrefabs");
            if (networkPrefabsProp != null && combatNetworkPrefabs != null)
                networkPrefabsProp.objectReferenceValue = combatNetworkPrefabs;
            networkManagerSo.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject playerSpawnerSo = new SerializedObject(playerSpawner);
            SerializedProperty playerPrefabProp = playerSpawnerSo.FindProperty("_playerPrefab");
            if (playerPrefabProp != null)
                playerPrefabProp.objectReferenceValue = playerPrefab;
            SerializedProperty ignoreRulesProp = playerSpawnerSo.FindProperty("_ignoreNetworkRules");
            if (ignoreRulesProp != null)
                ignoreRulesProp.boolValue = true;

            SerializedProperty spawnPointsProp = playerSpawnerSo.FindProperty("spawnPoints");
            if (spawnPointsProp != null && spawnPoint != null)
            {
                spawnPointsProp.arraySize = 1;
                spawnPointsProp.GetArrayElementAtIndex(0).objectReferenceValue = spawnPoint;
            }

            playerSpawnerSo.ApplyModifiedPropertiesWithoutUndo();

            networkRoot.transform.position = new Vector3(-10f, 0f, -10f);
            networkRoot.transform.rotation = Quaternion.identity;
            EditorUtility.SetDirty(networkRoot);
        }

        if (gameManagerRoot != null)
        {
            PlayerInput playerInput = gameManagerRoot.GetComponent<PlayerInput>() ?? gameManagerRoot.AddComponent<PlayerInput>();
            InputActionAsset playerControls = AssetDatabase.LoadAssetAtPath<InputActionAsset>(PlayerControlsPath);
            if (playerControls != null && playerInput.actions != playerControls)
                playerInput.actions = playerControls;

            gameManagerRoot.transform.position = new Vector3(-13f, 0f, -8f);
            EditorUtility.SetDirty(gameManagerRoot);
        }

        if (canvasRoot != null)
        {
            canvasRoot.transform.position = Vector3.zero;
            canvasRoot.transform.rotation = Quaternion.identity;
            EditorUtility.SetDirty(canvasRoot);
        }

        return canvasRoot;
    }

    private static void EnsureMonsterRig(GameObject canvasRoot, Transform spawnAnchor, Transform analysisTarget, Transform registryRoot, Transform playerSpawnPoint)
    {
        GameObject rigRoot = FindRoot(MonsterRigName);
        if (rigRoot == null)
            rigRoot = new GameObject(MonsterRigName);

        Transform spawnRoot = EnsureChild(rigRoot.transform, SpawnRootName);
        spawnRoot.position = Vector3.zero;
        spawnRoot.rotation = Quaternion.identity;

        MonsterTestSceneController controller = rigRoot.GetComponent<MonsterTestSceneController>();
        if (controller == null)
            controller = rigRoot.AddComponent<MonsterTestSceneController>();

        Button smilyButton;
        Button clownButton;
        Button octopusButton;
        Button liveCombatButton;
        Button safeObservationButton;
        Button closeRangeButton;
        Button mediumRangeButton;
        Button longRangeButton;
        Button respawnButton;
        Text statusLabel;
        Text checklistLabel;
        EnsureMonsterControlPanel(
            canvasRoot != null ? canvasRoot.transform : null,
            out smilyButton,
            out clownButton,
            out octopusButton,
            out liveCombatButton,
            out safeObservationButton,
            out closeRangeButton,
            out mediumRangeButton,
            out longRangeButton,
            out respawnButton,
            out statusLabel,
            out checklistLabel);

        SerializedObject so = new SerializedObject(controller);
        so.FindProperty("spawnRoot").objectReferenceValue = spawnRoot;
        so.FindProperty("spawnAnchor").objectReferenceValue = spawnAnchor;
        so.FindProperty("analysisTarget").objectReferenceValue = analysisTarget;
        so.FindProperty("registryRoot").objectReferenceValue = registryRoot;
        so.FindProperty("playerSpawnPoint").objectReferenceValue = playerSpawnPoint;
        so.FindProperty("smilyPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(SmilyPrefabPath);
        so.FindProperty("clownPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ClownPrefabPath);
        so.FindProperty("octopusPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(OctopusPrefabPath);
        so.FindProperty("smilyButton").objectReferenceValue = smilyButton;
        so.FindProperty("clownButton").objectReferenceValue = clownButton;
        so.FindProperty("octopusButton").objectReferenceValue = octopusButton;
        so.FindProperty("liveCombatButton").objectReferenceValue = liveCombatButton;
        so.FindProperty("safeObservationButton").objectReferenceValue = safeObservationButton;
        so.FindProperty("closeRangeButton").objectReferenceValue = closeRangeButton;
        so.FindProperty("mediumRangeButton").objectReferenceValue = mediumRangeButton;
        so.FindProperty("longRangeButton").objectReferenceValue = longRangeButton;
        so.FindProperty("respawnButton").objectReferenceValue = respawnButton;
        so.FindProperty("statusLabel").objectReferenceValue = statusLabel;
        so.FindProperty("checklistLabel").objectReferenceValue = checklistLabel;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(controller);
    }

    private static void EnsureMonsterControlPanel(
        Transform canvasRoot,
        out Button smilyButton,
        out Button clownButton,
        out Button octopusButton,
        out Button liveCombatButton,
        out Button safeObservationButton,
        out Button closeRangeButton,
        out Button mediumRangeButton,
        out Button longRangeButton,
        out Button respawnButton,
        out Text statusLabel,
        out Text checklistLabel)
    {
        smilyButton = null;
        clownButton = null;
        octopusButton = null;
        liveCombatButton = null;
        safeObservationButton = null;
        closeRangeButton = null;
        mediumRangeButton = null;
        longRangeButton = null;
        respawnButton = null;
        statusLabel = null;
        checklistLabel = null;

        if (canvasRoot == null)
            return;

        GameObject panel = FindChild(canvasRoot, PanelName);
        if (panel == null)
        {
            panel = new GameObject(PanelName, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasRoot, false);
        }

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = new Vector2(-24f, -24f);
        panelRect.sizeDelta = new Vector2(430f, 650f);

        Image panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.05f, 0.08f, 0.11f, 0.86f);

        ClearChildren(panel.transform);

        Font font = ResolveEditorFont();
        if (font == null)
            return;
        CreateLabel(panel.transform, "Title", "GUN vs MONSTER - COMBAT FEEL", font, 18, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(14f, -12f), new Vector2(402f, 26f), Color.white);
        liveCombatButton = CreateButton(panel.transform, "LiveCombatButton", "LIVE COMBAT", font, new Vector2(14f, -48f), new Vector2(194f, 36f), new Color(0.55f, 0.22f, 0.18f, 1f));
        safeObservationButton = CreateButton(panel.transform, "SafeObservationButton", "SAFE OBSERVE", font, new Vector2(222f, -48f), new Vector2(194f, 36f), new Color(0.20f, 0.40f, 0.52f, 1f));
        closeRangeButton = CreateButton(panel.transform, "CloseRangeButton", "CLOSE 7m", font, new Vector2(14f, -92f), new Vector2(124f, 34f), new Color(0.37f, 0.33f, 0.25f, 1f));
        mediumRangeButton = CreateButton(panel.transform, "MediumRangeButton", "MEDIUM 14m", font, new Vector2(153f, -92f), new Vector2(124f, 34f), new Color(0.32f, 0.37f, 0.28f, 1f));
        longRangeButton = CreateButton(panel.transform, "LongRangeButton", "LONG 22m", font, new Vector2(292f, -92f), new Vector2(124f, 34f), new Color(0.26f, 0.32f, 0.42f, 1f));
        smilyButton = CreateButton(panel.transform, "SpawnSmilyButton", "SPAWN SMILY", font, new Vector2(14f, -134f), new Vector2(402f, 36f), new Color(0.27f, 0.44f, 0.35f, 1f));
        clownButton = CreateButton(panel.transform, "SpawnClownButton", "SPAWN CLOWN", font, new Vector2(14f, -178f), new Vector2(402f, 36f), new Color(0.46f, 0.28f, 0.26f, 1f));
        octopusButton = CreateButton(panel.transform, "SpawnOctopusButton", "SPAWN OCTOPUS SWARM", font, new Vector2(14f, -222f), new Vector2(402f, 36f), new Color(0.22f, 0.33f, 0.50f, 1f));
        respawnButton = CreateButton(panel.transform, "RespawnButton", "RESET / RESPAWN CURRENT", font, new Vector2(14f, -266f), new Vector2(402f, 34f), new Color(0.38f, 0.30f, 0.45f, 1f));
        statusLabel = CreateLabel(panel.transform, "MonsterStatusText", "Preparing combat feel test scene...", font, 13, FontStyle.Normal, TextAnchor.UpperLeft, new Vector2(14f, -310f), new Vector2(402f, 190f), new Color(0.89f, 0.93f, 0.97f, 1f));
        statusLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
        statusLabel.verticalOverflow = VerticalWrapMode.Overflow;
        checklistLabel = CreateLabel(panel.transform, "CombatFeelChecklistText", string.Empty, font, 12, FontStyle.Normal, TextAnchor.UpperLeft, new Vector2(14f, -510f), new Vector2(402f, 126f), new Color(0.98f, 0.82f, 0.47f, 1f));
        checklistLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
        checklistLabel.verticalOverflow = VerticalWrapMode.Overflow;
    }

    private static Button CreateButton(Transform parent, string name, string label, Font font, Vector2 anchoredPosition, Vector2 size, Color background)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Image image = buttonObject.GetComponent<Image>();
        image.color = background;

        GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(buttonObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 6f);
        textRect.offsetMax = new Vector2(-10f, -6f);

        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.fontSize = 15;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = label;

        return buttonObject.GetComponent<Button>();
    }

    private static Text CreateLabel(Transform parent, string name, string label, Font font, int fontSize, FontStyle fontStyle, TextAnchor anchor, Vector2 anchoredPosition, Vector2 size, Color color)
    {
        GameObject labelObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(parent, false);

        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Text text = labelObject.GetComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = anchor;
        text.color = color;
        text.text = label;
        return text;
    }

    private static Font ResolveEditorFont()
    {
        Font font = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
        if (font != null)
            return font;

        return AssetDatabase.LoadAssetAtPath<Font>("Assets/scriptable-animation-system-main/Assets/Level/Fonts/Roboto-Regular.ttf");
    }

    private static void EnsureEventSystem()
    {
        GameObject existing = FindRoot(EventSystemName);
        if (existing == null)
            existing = new GameObject(EventSystemName);

        if (existing.GetComponent<EventSystem>() == null)
            existing.AddComponent<EventSystem>();

        RemoveComponentIfPresent(existing, "UnityEngine.EventSystems.StandaloneInputModule");
        if (existing.GetComponent<InputSystemUIInputModule>() == null)
            existing.AddComponent<InputSystemUIInputModule>();
    }

    private static Transform EnsureAnalysisMarkers(Vector3 targetPosition)
    {
        GameObject root = FindRoot(MarkerRootName);
        if (root == null)
            root = new GameObject(MarkerRootName);

        ClearChildren(root.transform);

        Vector3[] patrolOffsets =
        {
            new Vector3(-6f, 0f, -2f),
            new Vector3(6f, 0f, -1f),
            new Vector3(-5f, 0f, 4f),
            new Vector3(5f, 0f, 5f),
        };

        for (int i = 0; i < patrolOffsets.Length; i++)
        {
            GameObject marker = new GameObject($"PatrolPoint_{i + 1}");
            marker.transform.SetParent(root.transform, false);
            marker.transform.position = targetPosition + patrolOffsets[i];
            marker.AddComponent<PatrolPointMarker>();
        }

        Vector3[] spawnOffsets =
        {
            new Vector3(-3f, 0f, 7f),
            new Vector3(0f, 0f, 7.5f),
            new Vector3(3f, 0f, 7f),
        };

        for (int i = 0; i < spawnOffsets.Length; i++)
        {
            GameObject marker = new GameObject($"SpawnPoint_{i + 1}");
            marker.transform.SetParent(root.transform, false);
            marker.transform.position = targetPosition + spawnOffsets[i];
            marker.AddComponent<MonsterSpawnPointMarker>();
        }

        return root.transform;
    }

    private static Transform EnsureSpawnAnchor(Vector3 targetPosition)
    {
        GameObject rigRoot = FindRoot(MonsterRigName);
        if (rigRoot == null)
            rigRoot = new GameObject(MonsterRigName);

        Transform anchor = EnsureChild(rigRoot.transform, SpawnAnchorName);
        anchor.position = targetPosition + new Vector3(0f, 0f, SpawnAnchorForwardOffset);
        anchor.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        return anchor;
    }

    private static GameObject EnsureTargetDummy(Vector3 targetPosition)
    {
        GameObject dummy = FindRoot(TargetDummyName);
        if (dummy == null)
            dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);

        dummy.name = TargetDummyName;
        dummy.transform.position = targetPosition + new Vector3(0f, 1f, 0f);
        dummy.transform.rotation = Quaternion.identity;
        dummy.transform.localScale = new Vector3(1.15f, 1.15f, 1.15f);
        dummy.tag = "Player";

        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
            dummy.layer = playerLayer;

        if (dummy.GetComponent<PlayerPawn>() == null)
            dummy.AddComponent<PlayerPawn>();

        Renderer renderer = dummy.GetComponentInChildren<Renderer>();
        if (renderer != null && renderer.sharedMaterial != null)
        {
            Material material = new Material(renderer.sharedMaterial);
            material.color = new Color(0.92f, 0.72f, 0.31f, 1f);
            renderer.sharedMaterial = material;
        }

        return dummy;
    }

    private static void RemoveLegacyMonsterRoots()
    {
        Transform[] roots = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Transform root in roots)
        {
            if (root == null || root.parent != null)
                continue;

            if (root.name.StartsWith("Octopus (", StringComparison.OrdinalIgnoreCase) ||
                root.name == "OctopusSwarmRoot" ||
                root.name == "smily_fixed" ||
                root.name == "Clown skin2 combined")
            {
                UnityEngine.Object.DestroyImmediate(root.gameObject);
            }
        }
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
        plane.transform.localScale = new Vector3(10f, 1f, 10f);
        return plane;
    }

    private static void ConfigureArenaPlane(GameObject plane, Scene scene)
    {
        if (plane == null)
            return;

        NavMeshSurface[] existingSurfaces = plane.GetComponents<NavMeshSurface>();
        HashSet<int> requiredAgentTypeIds = new HashSet<int>(GetRequiredArenaAgentTypeIds());

        foreach (NavMeshSurface surface in existingSurfaces)
        {
            if (surface == null || requiredAgentTypeIds.Contains(surface.agentTypeID))
                continue;

            UnityEngine.Object.DestroyImmediate(surface, true);
        }

        existingSurfaces = plane.GetComponents<NavMeshSurface>();
        foreach (int agentTypeId in requiredAgentTypeIds)
        {
            NavMeshSurface surface = existingSurfaces.FirstOrDefault(candidate => candidate.agentTypeID == agentTypeId);
            if (surface == null)
                surface = plane.AddComponent<NavMeshSurface>();

            surface.collectObjects = CollectObjects.All;
            surface.layerMask = ~0;
            surface.defaultArea = 0;
            surface.agentTypeID = agentTypeId;

            surface.RemoveData();
            surface.BuildNavMesh();

            EditorUtility.SetDirty(surface);
        }

        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static IEnumerable<int> GetRequiredArenaAgentTypeIds()
    {
        HashSet<int> uniqueAgentTypeIds = new HashSet<int> { 0 };
        AddAgentTypeIds(uniqueAgentTypeIds, SmilyPrefabPath);
        AddAgentTypeIds(uniqueAgentTypeIds, ClownPrefabPath);
        AddAgentTypeIds(uniqueAgentTypeIds, OctopusPrefabPath);

        foreach (int agentTypeId in uniqueAgentTypeIds)
            yield return agentTypeId;
    }

    private static void AddAgentTypeIds(HashSet<int> agentTypeIds, string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            return;

        NavMeshAgent[] agents = prefab.GetComponentsInChildren<NavMeshAgent>(true);
        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i] != null)
                agentTypeIds.Add(agents[i].agentTypeID);
        }
    }

    private static Transform EnsurePlayerSpawnPoint()
    {
        GameObject existing = FindRoot(SpawnPointName);
        if (existing == null)
            existing = new GameObject(SpawnPointName);

        existing.transform.position = new Vector3(0f, 1.1f, -6f);
        existing.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        return existing.transform;
    }

    private static Vector3 GetAnalysisTargetPosition(Transform spawnPoint)
    {
        if (spawnPoint == null)
            return new Vector3(0f, 0f, 8f);

        Vector3 forward = spawnPoint.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        return new Vector3(spawnPoint.position.x, 0f, spawnPoint.position.z) + forward.normalized * 14f;
    }

    private static void EnsureTestLoadout(Transform spawnPoint)
    {
        GameObject loadoutRoot = FindRoot(LoadoutRootName);
        if (loadoutRoot == null)
            loadoutRoot = new GameObject(LoadoutRootName);

        ClearChildren(loadoutRoot.transform);
        // Keep the combat sightline clear. The player turns around to collect
        // weapons, then faces forward toward the monster range.
        Vector3 rackCenter = spawnPoint.position - spawnPoint.forward * 3f;

        for (int i = 0; i < WeaponPaths.Length; i++)
        {
            float xOffset = (i - 1) * 1.8f;
            Vector3 pedestalPosition = rackCenter + Vector3.right * xOffset;
            Vector3 ammoPedestalPosition = pedestalPosition + spawnPoint.forward * 1.05f;

            CreateBox(loadoutRoot.transform, $"WeaponPedestal_{i + 1}", pedestalPosition, new Vector3(1.5f, 0.8f, 1.5f), new Color(0.18f, 0.22f, 0.27f));
            CreateBox(loadoutRoot.transform, $"AmmoPedestal_{i + 1}", ammoPedestalPosition, new Vector3(1.1f, 0.6f, 1.1f), new Color(0.16f, 0.18f, 0.22f));

            Quaternion facingSpawn = Quaternion.LookRotation(-spawnPoint.forward, Vector3.up);
            InstantiateLoadoutPrefab(WeaponPaths[i], loadoutRoot.transform, pedestalPosition + Vector3.up * 1.05f, facingSpawn);
            InstantiateLoadoutPrefab(AmmoPaths[i], loadoutRoot.transform, ammoPedestalPosition + Vector3.up * 0.85f, facingSpawn);
        }
    }

    private static void EnsureArenaBlockers(Transform spawnPoint, Vector3 targetPosition)
    {
        GameObject blockersRoot = FindRoot(BlockersRootName);
        if (blockersRoot == null)
            blockersRoot = new GameObject(BlockersRootName);

        ClearChildren(blockersRoot.transform);
        Vector3 laneMid = Vector3.Lerp(new Vector3(spawnPoint.position.x, 0f, spawnPoint.position.z), targetPosition, 0.5f);
        float laneHalfDepth = ArenaLaneDepth * 0.5f;
        float laneMinZ = laneMid.z - laneHalfDepth;
        float laneMaxZ = laneMid.z + laneHalfDepth;

        CreateBox(blockersRoot.transform, "SideWall_Left", new Vector3(-12f, 1.5f, laneMid.z), new Vector3(1f, 3f, ArenaLaneDepth), new Color(0.15f, 0.18f, 0.22f));
        CreateBox(blockersRoot.transform, "SideWall_Right", new Vector3(12f, 1.5f, laneMid.z), new Vector3(1f, 3f, ArenaLaneDepth), new Color(0.15f, 0.18f, 0.22f));
        CreateBox(blockersRoot.transform, "LaneCap_South", new Vector3(0f, 1.5f, laneMinZ - ArenaLaneCapThickness * 0.5f), new Vector3(ArenaLaneOuterWidth, 3f, ArenaLaneCapThickness), new Color(0.15f, 0.18f, 0.22f));
        CreateBox(blockersRoot.transform, "LaneCap_North", new Vector3(0f, 1.5f, laneMaxZ + ArenaLaneCapThickness * 0.5f), new Vector3(ArenaLaneOuterWidth, 3f, ArenaLaneCapThickness), new Color(0.15f, 0.18f, 0.22f));
        CreateBox(blockersRoot.transform, "CenterCover", targetPosition + new Vector3(0f, 1.25f, 2.2f), new Vector3(3.4f, 2.5f, 1.5f), new Color(0.23f, 0.26f, 0.31f));
        CreateBox(blockersRoot.transform, "LeftCover", targetPosition + new Vector3(-5.5f, 1.1f, -0.5f), new Vector3(3.4f, 2.2f, 1.4f), new Color(0.2f, 0.24f, 0.28f));
        CreateBox(blockersRoot.transform, "RightCover", targetPosition + new Vector3(5.5f, 1.1f, 0.5f), new Vector3(3.4f, 2.2f, 1.4f), new Color(0.2f, 0.24f, 0.28f));
        CreateBox(blockersRoot.transform, "RearBarrier", targetPosition + new Vector3(0f, 1.2f, 7.6f), new Vector3(6f, 2.4f, 1.3f), new Color(0.22f, 0.26f, 0.3f));
    }

    private static void EnsureRangeMarkers(Transform spawnPoint)
    {
        GameObject markersRoot = FindRoot(RangeMarkersRootName);
        if (markersRoot == null)
            markersRoot = new GameObject(RangeMarkersRootName);

        ClearChildren(markersRoot.transform);

        Vector3 origin = new Vector3(spawnPoint.position.x, 0.04f, spawnPoint.position.z);
        Vector3 forward = spawnPoint.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        forward.Normalize();

        CreateBox(markersRoot.transform, "CloseRange_7m", origin + forward * 7f, new Vector3(20f, 0.08f, 0.28f), new Color(0.88f, 0.34f, 0.18f));
        CreateBox(markersRoot.transform, "MediumRange_14m", origin + forward * 14f, new Vector3(20f, 0.08f, 0.28f), new Color(0.35f, 0.72f, 0.30f));
        CreateBox(markersRoot.transform, "LongRange_22m", origin + forward * 22f, new Vector3(20f, 0.08f, 0.28f), new Color(0.22f, 0.48f, 0.88f));
    }

    private static void EnsureDungeonDoorLane(Vector3 spawnPosition, Vector3 targetPosition)
    {
        GameObject laneRoot = FindRoot(DoorTestRootName);
        if (laneRoot == null)
            laneRoot = new GameObject(DoorTestRootName);

        ClearChildren(laneRoot.transform);

        GameObject doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DungeonDoorPrefabPath);
        if (doorPrefab == null)
            return;

        GameObject doorInstance = PrefabUtility.InstantiatePrefab(doorPrefab) as GameObject;
        if (doorInstance == null)
            return;

        doorInstance.name = "DungeonDoor";
        doorInstance.transform.SetParent(laneRoot.transform);
        doorInstance.transform.position = new Vector3(targetPosition.x, 0f, (spawnPosition.z + targetPosition.z) * 0.5f);
        doorInstance.transform.rotation = Quaternion.identity;
        AlignObjectBottomToFloor(doorInstance);

        ConfigureDungeonDoorState(doorInstance);

        float doorZ = doorInstance.transform.position.z;
        float barrierWidth = SideWallInnerX - DoorOpeningHalfWidth;
        float leftBarrierCenterX = -(DoorOpeningHalfWidth + barrierWidth * 0.5f);
        float rightBarrierCenterX = DoorOpeningHalfWidth + barrierWidth * 0.5f;
        Vector3 barrierSize = new Vector3(barrierWidth, 3f, DoorBarrierThickness);

        CreateBox(laneRoot.transform, "DoorBarrier_Left", new Vector3(leftBarrierCenterX, 1.5f, doorZ), barrierSize, new Color(0.15f, 0.18f, 0.22f));
        CreateBox(laneRoot.transform, "DoorBarrier_Right", new Vector3(rightBarrierCenterX, 1.5f, doorZ), barrierSize, new Color(0.15f, 0.18f, 0.22f));

        int doorArea = NavMesh.GetAreaFromName("Door");
        if (doorArea < 0)
            doorArea = 0;

        foreach (int agentTypeId in GetRequiredArenaAgentTypeIds())
            CreateDoorNavMeshLink(doorInstance.transform, agentTypeId, doorArea);
    }

    private static void ConfigureDungeonDoorState(GameObject doorInstance)
    {
        if (doorInstance == null)
            return;

        Door regularDoor = doorInstance.GetComponentInChildren<Door>(true);
        if (regularDoor != null)
        {
            regularDoor.transform.localRotation = Quaternion.identity;
            EditorUtility.SetDirty(regularDoor);
        }

        DunGen.Door dunGenDoor = doorInstance.GetComponentInChildren<DunGen.Door>(true);
        if (dunGenDoor != null)
        {
            SerializedObject dunGenDoorSo = new SerializedObject(dunGenDoor);
            SerializedProperty dunGenOpenProp = dunGenDoorSo.FindProperty("isOpen");
            if (dunGenOpenProp != null)
                dunGenOpenProp.boolValue = false;
            dunGenDoorSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(dunGenDoor);
        }

    }

    private static void CreateDoorNavMeshLink(Transform parent, int agentTypeId, int area)
    {
        GameObject linkObject = new GameObject(GetDoorLinkName(agentTypeId), typeof(NavMeshLink), typeof(MonsterDoorLinkBinding));
        linkObject.transform.SetParent(parent, false);
        linkObject.transform.localPosition = Vector3.zero;
        linkObject.transform.rotation = Quaternion.identity;

        NavMeshLink link = linkObject.GetComponent<NavMeshLink>();
        link.agentTypeID = agentTypeId;
        link.area = area;
        link.costModifier = DoorLinkCostModifier;
        link.bidirectional = true;
        link.width = DoorOpeningHalfWidth * 2f;
        link.startPoint = new Vector3(0f, 0f, -DoorLinkHalfDepth);
        link.endPoint = new Vector3(0f, 0f, DoorLinkHalfDepth);
        link.UpdateLink();

        EditorUtility.SetDirty(link);
    }

    private static string GetDoorLinkName(int agentTypeId)
    {
        if (agentTypeId == 0)
            return "DoorWayPoint";

        string settingsName = NavMesh.GetSettingsNameFromID(agentTypeId);
        if (string.IsNullOrWhiteSpace(settingsName))
            settingsName = agentTypeId.ToString();

        foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
            settingsName = settingsName.Replace(invalid, '_');

        return $"DoorWayPoint_{settingsName.Replace(' ', '_')}";
    }

    private static void AlignObjectBottomToFloor(GameObject gameObject)
    {
        if (gameObject == null)
            return;

        Collider[] colliders = gameObject.GetComponentsInChildren<Collider>(true);
        if (colliders == null || colliders.Length == 0)
            return;

        float minimumY = colliders.Min(collider => collider.bounds.min.y);
        gameObject.transform.position += Vector3.up * -minimumY;
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

    private static void EnsureCombatTestPlayerPrefab()
    {
        GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CyberGenericPrefabPath);
        if (sourcePrefab == null)
            return;

        Type playerPawnType = ResolvePlayerPawnType();
        if (playerPawnType == null)
            return;

        EnsureAssetFolder("Assets/Tests");
        EnsureAssetFolder("Assets/Tests/CombatFeel");

        GameObject existingTestPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CombatTestPlayerPrefabPath);
        if (existingTestPrefab != null)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(CombatTestPlayerPrefabPath);
            try
            {
                if (contents.GetComponent("PlayerPawn") == null)
                    contents.AddComponent(playerPawnType);

                EditorUtility.SetDirty(contents);
                PrefabUtility.SaveAsPrefabAsset(contents, CombatTestPlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return;
        }

        GameObject instance = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
        if (instance == null)
            return;

        try
        {
            instance.name = "Cyber_Generic_CombatTest";
            if (instance.GetComponent("PlayerPawn") == null)
                instance.AddComponent(playerPawnType);

            PrefabUtility.SaveAsPrefabAsset(instance, CombatTestPlayerPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static NetworkPrefabs EnsureCombatTestNetworkPrefabs(GameObject playerPrefab)
    {
        EnsureAssetFolder("Assets/Tests");
        EnsureAssetFolder("Assets/Tests/CombatFeel");

        NetworkPrefabs testPrefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabs>(CombatTestNetworkPrefabsPath);
        if (testPrefabs == null)
        {
            GameObject networkManagerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath);
            NetworkManager sourceManager = networkManagerPrefab != null ? networkManagerPrefab.GetComponent<NetworkManager>() : null;
            NetworkPrefabs sourcePrefabs = null;

            if (sourceManager != null)
            {
                SerializedObject sourceManagerSo = new SerializedObject(sourceManager);
                SerializedProperty sourcePrefabsProp = sourceManagerSo.FindProperty("_networkPrefabs");
                sourcePrefabs = sourcePrefabsProp != null ? sourcePrefabsProp.objectReferenceValue as NetworkPrefabs : null;
            }

            string sourcePath = sourcePrefabs != null ? AssetDatabase.GetAssetPath(sourcePrefabs) : string.Empty;
            if (!string.IsNullOrWhiteSpace(sourcePath))
                AssetDatabase.CopyAsset(sourcePath, CombatTestNetworkPrefabsPath);

            testPrefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabs>(CombatTestNetworkPrefabsPath);
            if (testPrefabs == null)
            {
                testPrefabs = ScriptableObject.CreateInstance<NetworkPrefabs>();
                AssetDatabase.CreateAsset(testPrefabs, CombatTestNetworkPrefabsPath);
            }
        }

        testPrefabs.autoGenerate = false;
        if (playerPrefab != null && !testPrefabs.prefabs.Any(entry => entry.prefab == playerPrefab))
        {
            testPrefabs.prefabs.Add(new NetworkPrefabs.UserPrefabData
            {
                prefab = playerPrefab,
                pooled = false,
                warmupCount = 0
            });
        }

        testPrefabs.Refresh();
        EditorUtility.SetDirty(testPrefabs);
        AssetDatabase.SaveAssets();
        return testPrefabs;
    }

    private static void EnsureAssetFolder(string assetFolderPath)
    {
        if (AssetDatabase.IsValidFolder(assetFolderPath))
            return;

        string parent = System.IO.Path.GetDirectoryName(assetFolderPath)?.Replace('\\', '/');
        string name = System.IO.Path.GetFileName(assetFolderPath);
        if (!string.IsNullOrWhiteSpace(parent) && !string.IsNullOrWhiteSpace(name))
            AssetDatabase.CreateFolder(parent, name);
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

    private static Transform EnsureChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
            return child;

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static GameObject FindRoot(string rootName)
    {
        Scene activeScene = EditorSceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !activeScene.isLoaded)
            return null;

        return activeScene.GetRootGameObjects().FirstOrDefault(root => root != null && root.name == rootName);
    }

    private static GameObject FindChild(Transform parent, string name)
    {
        if (parent == null)
            return null;

        Transform child = parent.Find(name);
        return child != null ? child.gameObject : null;
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
        if (renderer != null && renderer.sharedMaterial != null)
        {
            Material instanceMaterial = new Material(renderer.sharedMaterial);
            instanceMaterial.color = color;
            renderer.sharedMaterial = instanceMaterial;
        }

        return box;
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
