using System;
using System.Collections.Generic;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DungeonPortalTransportPoC.Editor
{
    /// <summary>
    /// Builds only the isolated Start/Admin portal-transport validation scene.
    /// This authoring path never opens or copies a previous adjacent-lighting scene and
    /// never applies scene-instance overrides back to any production prefab.
    /// </summary>
    public static class DungeonPortalTransportPoCSceneBuilder
    {
        public const string ValidationScenePath =
            "Assets/Experiments/DungeonPortalTransportPoC/Scenes/Start_Admin_PortalTransportValidation.unity";

        private const string SceneFolder =
            "Assets/Experiments/DungeonPortalTransportPoC/Scenes";
        private const string StartRoomPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/StartRoom_R000.prefab";
        private const string AdminRoomPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/AdminstrativeSegregation_R000.prefab";
        private const string DoorPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/SomePicees/Door_SM_A_Door_Placement.prefab";
        private const string DoorwayPath = "Doorways/Door_SM_A/DoorWayPoint";
        private const string DoorLeafPath = "Door_01";
        private const string ForbiddenOverlayName = "AdjacentConnectionOverlay";
        private const string LightingSettingsPath =
            "Assets/New Lighting Settings.lighting";
        private const string ValidationVolumeProfilePath =
            "Assets/URP Volume Post-Processing Preset Pack/URP VolumePresets/5_Sci-fi _Future/37_Drone View.asset";

        private static readonly Vector3 StartRoomPosition = Vector3.zero;
        private static readonly Quaternion StartRoomRotation = Quaternion.identity;
        private static readonly Vector3 AdminRoomPosition =
            new Vector3(-14.000000f, 1.693980f, 7.058475f);
        private static readonly Quaternion AdminRoomRotation =
            Quaternion.Euler(0f, 270f, 0f);
        private static readonly Vector3 ExpectedPortalPosition =
            new Vector3(-0.000001f, 0.000001f, 5.196488f);
        private static readonly Vector3 StartCameraPosition =
            new Vector3(2.9999993f, 1.6700006f, 5.196489f);
        private static readonly Quaternion StartCameraRotation =
            Quaternion.Euler(0f, 270f, 0f);

        [MenuItem("Tools/Dungeon/Lighting/Portal Transport PoC/Build Start-Admin Validation Scene")]
        public static void BuildFromMenu()
        {
            string result = BuildValidationScene();
            if (result.StartsWith("PASS", StringComparison.Ordinal))
                Debug.Log(result);
            else
                Debug.LogError(result);
        }

        /// <summary>
        /// Unity -executeMethod entry point. It is intentionally parameterless and static.
        /// </summary>
        public static void BuildCli()
        {
            string result = BuildValidationScene();
            if (!result.StartsWith("PASS", StringComparison.Ordinal))
                throw new InvalidOperationException(result);

            Debug.Log(result);
        }

        /// <summary>
        /// Idempotently replaces the generated scene asset while leaving every scene that
        /// was already loaded untouched. The generated scene is always opened additively,
        /// saved, and closed before this method returns.
        /// </summary>
        public static string BuildValidationScene()
        {
            if (!TryValidateEditorState(out string stateFailure))
                return $"FAIL: {stateFailure}";

            Scene alreadyLoaded = SceneManager.GetSceneByPath(ValidationScenePath);
            if (alreadyLoaded.IsValid() && alreadyLoaded.isLoaded)
            {
                return
                    $"FAIL: generated validation scene is already loaded: {ValidationScenePath}. " +
                    "Close it without unsaved changes before rebuilding.";
            }

            GameObject startPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(StartRoomPrefabPath);
            GameObject adminPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(AdminRoomPrefabPath);
            GameObject doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefabPath);
            if (startPrefab == null || adminPrefab == null || doorPrefab == null)
            {
                return
                    "FAIL: a required production prefab is missing. " +
                    $"start={startPrefab != null} admin={adminPrefab != null} door={doorPrefab != null}";
            }

            EnsureAssetFolder(SceneFolder);

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene generatedScene = default;
            try
            {
                generatedScene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Additive);
                if (!generatedScene.IsValid() || !generatedScene.isLoaded)
                    throw new InvalidOperationException("Unity did not create a loaded validation scene.");

                EditorSceneManager.SetActiveScene(generatedScene);
                if (EditorSceneManager.GetActiveScene() != generatedScene)
                    throw new InvalidOperationException("Unable to make the generated scene active.");

                ConfigureSceneEnvironment();

                GameObject validationRoot = CreateSceneObject(
                    "PortalTransportValidation",
                    null);
                GameObject productionRoomsRoot = CreateSceneObject(
                    "01_ProductionRooms",
                    validationRoot.transform);
                GameObject pocRoot = CreateSceneObject(
                    "02_PortalTransportPoC",
                    validationRoot.transform);
                GameObject camerasRoot = CreateSceneObject(
                    "03_FixedValidationCameras_DISABLED",
                    validationRoot.transform);

                GameObject startRoom = InstantiateProductionPrefab(
                    startPrefab,
                    generatedScene,
                    productionRoomsRoot.transform,
                    "StartRoom_R000_ProductionInstance",
                    StartRoomPosition,
                    StartRoomRotation);
                GameObject adminRoom = InstantiateProductionPrefab(
                    adminPrefab,
                    generatedScene,
                    productionRoomsRoot.transform,
                    "AdminstrativeSegregation_R000_ProductionInstance",
                    AdminRoomPosition,
                    AdminRoomRotation);

                AssertRoomTransform(
                    startRoom.transform,
                    StartRoomPosition,
                    StartRoomRotation,
                    "StartRoom_R000");
                AssertRoomTransform(
                    adminRoom.transform,
                    AdminRoomPosition,
                    AdminRoomRotation,
                    "AdminstrativeSegregation_R000");

                Doorway startDoorway = ResolveDoorway(startRoom, "StartRoom_R000");
                Doorway adminDoorway = ResolveDoorway(adminRoom, "AdminstrativeSegregation_R000");
                AssertDoorwaysMeet(startDoorway.transform, adminDoorway.transform);
                ConfigureConnectedDoorwayVisuals(startDoorway.transform);
                ConfigureConnectedDoorwayVisuals(adminDoorway.transform);

                Doorway placementOwner = ResolvePlacementOwner(
                    startDoorway,
                    adminDoorway,
                    doorPrefab);
                GameObject doorInstance = InstantiateDoorUsingDunGenRules(
                    doorPrefab,
                    generatedScene,
                    placementOwner);
                Transform doorLeaf = doorInstance.transform.Find(DoorLeafPath);
                if (doorLeaf == null)
                {
                    throw new InvalidOperationException(
                        $"Door leaf '{DoorLeafPath}' is missing from '{DoorPrefabPath}'.");
                }

                ConfigureExistingDoorState(
                    doorInstance,
                    startDoorway,
                    adminDoorway);
                AssertSingleActiveDoorPrefabInstance(
                    generatedScene,
                    doorPrefab,
                    doorInstance);
                AssertExistingDualSideProbeReceiver(doorInstance);
                AssertProductionLightingLayerParity(startRoom, adminRoom);

                DungeonPortalPowerEnvelope startPower;
                DungeonPortalRoomPowerBridge startPowerBridge;
                DungeonPortalEndpoint startEndpoint = CreateEndpoint(
                    pocRoot.transform,
                    "StartRoom_Endpoint_ProfilePending",
                    startRoom,
                    startDoorway.transform,
                    out startPower,
                    out startPowerBridge);
                DungeonPortalPowerEnvelope adminPower;
                DungeonPortalRoomPowerBridge adminPowerBridge;
                DungeonPortalEndpoint adminEndpoint = CreateEndpoint(
                    pocRoot.transform,
                    "AdminRoom_Endpoint_ProfilePending",
                    adminRoom,
                    adminDoorway.transform,
                    out adminPower,
                    out adminPowerBridge);

                GameObject connectionObject = CreateSceneObject(
                    "A_to_B_and_B_to_A_Connection_ProfileGate_DISABLED",
                    pocRoot.transform);
                connectionObject.transform.SetPositionAndRotation(
                    ExpectedPortalPosition,
                    startDoorway.transform.rotation);
                DungeonPortalDoorAngleSource angleSource =
                    connectionObject.AddComponent<DungeonPortalDoorAngleSource>();
                angleSource.Configure(
                    doorLeaf,
                    doorLeaf.localRotation,
                    Vector3.up,
                    90f);
                DungeonPortalTransportConnection connection =
                    connectionObject.AddComponent<DungeonPortalTransportConnection>();
                connection.Configure(startEndpoint, adminEndpoint, angleSource);
                connection.ConfigureScopedEffects(
                    new[] { startPowerBridge, adminPowerBridge },
                    Array.Empty<DungeonPortalRoomReflectionBlend>());

                // Profiles are deliberately null in this structural scene. Keeping the
                // connection disabled avoids a misleading runtime binding error and makes
                // the pre-profile scene strictly fail closed. Connection-owned power and
                // reflection effects are also serialized disabled, so OFF is production
                // surface/lightmap/probe parity rather than merely zero proxy lights.
                connection.enabled = false;
                EditorUtility.SetDirty(startPower);
                EditorUtility.SetDirty(adminPower);
                EditorUtility.SetDirty(startEndpoint);
                EditorUtility.SetDirty(adminEndpoint);
                EditorUtility.SetDirty(angleSource);
                EditorUtility.SetDirty(connection);

                CreateFixedValidationCameras(
                    camerasRoot.transform,
                    startDoorway.transform);
                CreateValidationGlobalVolume(validationRoot.transform);

                AssertNoForbiddenOverlayNames(generatedScene);
                EditorSceneManager.MarkSceneDirty(generatedScene);
                if (!EditorSceneManager.SaveScene(
                        generatedScene,
                        ValidationScenePath,
                        false))
                {
                    throw new InvalidOperationException(
                        $"Unable to save generated scene '{ValidationScenePath}'.");
                }

                return
                    "PASS DungeonPortalTransportPoC isolated validation scene\n" +
                    $"scene={ValidationScenePath}\n" +
                    $"startDoorway={startDoorway.transform.position:F6}\n" +
                    $"adminDoorway={adminDoorway.transform.position:F6}\n" +
                    $"doorPlacementOwner={placementOwner.transform.GetHierarchyPath()}\n" +
                    "profiles=null (intentional fail-closed)\n" +
                    "connection=disabled until generated profiles are assigned\n" +
                    "surfaceOverlayRenderers=0\n" +
                    "productionPrefabsApplied=0";
            }
            catch (Exception exception)
            {
                return $"FAIL: isolated portal validation scene build threw {exception}";
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    EditorSceneManager.SetActiveScene(previousActiveScene);

                if (generatedScene.IsValid() && generatedScene.isLoaded)
                    EditorSceneManager.CloseScene(generatedScene, true);
            }
        }

        private static bool TryValidateEditorState(out string failure)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                failure = "Unity is in Play Mode or is transitioning Play Mode; no scene was touched.";
                return false;
            }

            if (EditorApplication.isCompiling)
            {
                failure = "Unity is compiling; no scene was touched.";
                return false;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                {
                    failure =
                        $"loaded scene has unsaved changes: '{scene.name}' ({scene.path}). " +
                        "No scene was touched.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private static GameObject InstantiateProductionPrefab(
            GameObject prefab,
            Scene scene,
            Transform parent,
            string instanceName,
            Vector3 position,
            Quaternion rotation)
        {
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
                throw new InvalidOperationException($"Unable to instantiate '{prefab.name}'.");

            instance.name = instanceName;
            instance.transform.SetParent(parent, true);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.localScale = Vector3.one;
            RecordSceneInstanceTransform(instance.transform);
            EditorUtility.SetDirty(instance);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
            return instance;
        }

        private static Doorway ResolveDoorway(GameObject room, string label)
        {
            Transform doorwayTransform = room.transform.Find(DoorwayPath);
            Doorway doorway = doorwayTransform != null
                ? doorwayTransform.GetComponent<Doorway>()
                : null;
            if (doorway == null)
            {
                throw new InvalidOperationException(
                    $"{label} doorway '{DoorwayPath}' is missing or has no DunGen.Doorway.");
            }

            return doorway;
        }

        private static void AssertRoomTransform(
            Transform room,
            Vector3 expectedPosition,
            Quaternion expectedRotation,
            string label)
        {
            if (Vector3.Distance(room.position, expectedPosition) > 0.00001f ||
                Quaternion.Angle(room.rotation, expectedRotation) > 0.001f)
            {
                throw new InvalidOperationException(
                    $"{label} transform drifted. position={room.position:F6} " +
                    $"rotation={room.eulerAngles:F6}");
            }
        }

        private static void AssertDoorwaysMeet(
            Transform startDoorway,
            Transform adminDoorway)
        {
            const float positionTolerance = 0.0005f;
            if (Vector3.Distance(startDoorway.position, ExpectedPortalPosition) > positionTolerance ||
                Vector3.Distance(adminDoorway.position, ExpectedPortalPosition) > positionTolerance ||
                Vector3.Distance(startDoorway.position, adminDoorway.position) > positionTolerance)
            {
                throw new InvalidOperationException(
                    "Fixed Start/Admin transforms no longer produce the canonical portal point. " +
                    $"expected={ExpectedPortalPosition:F6} start={startDoorway.position:F6} " +
                    $"admin={adminDoorway.position:F6}");
            }

            if (Vector3.Dot(startDoorway.forward, adminDoorway.forward) > -0.999f)
            {
                throw new InvalidOperationException(
                    "Connected doorway forward vectors are not opposed. " +
                    $"dot={Vector3.Dot(startDoorway.forward, adminDoorway.forward):F6}");
            }
        }

        private static void ConfigureConnectedDoorwayVisuals(Transform doorway)
        {
            Transform doorwayAnchor = doorway.parent;
            Transform blocker = doorwayAnchor != null
                ? doorwayAnchor.Find("Blocker_SM_A")
                : null;
            Transform openPassage = doorwayAnchor != null
                ? doorwayAnchor.Find("No_Door_Placement")
                : null;
            if (blocker == null || openPassage == null)
            {
                throw new InvalidOperationException(
                    $"Connected doorway visuals are missing below '{doorway.GetHierarchyPath()}'.");
            }

            blocker.gameObject.SetActive(false);
            openPassage.gameObject.SetActive(true);
            PrefabUtility.RecordPrefabInstancePropertyModifications(blocker.gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(openPassage.gameObject);
        }

        private static Doorway ResolvePlacementOwner(
            Doorway startDoorway,
            Doorway adminDoorway,
            GameObject requiredDoorPrefab)
        {
            bool startHasDoor = ContainsViablePrefab(
                startDoorway.ConnectorPrefabWeights,
                requiredDoorPrefab);
            bool adminHasDoor = ContainsViablePrefab(
                adminDoorway.ConnectorPrefabWeights,
                requiredDoorPrefab);

            if (!startHasDoor && !adminHasDoor)
            {
                throw new InvalidOperationException(
                    $"Neither canonical doorway offers '{DoorPrefabPath}' as a viable connector.");
            }

            if (startHasDoor && adminHasDoor)
            {
                return startDoorway.DoorPrefabPriority >= adminDoorway.DoorPrefabPriority
                    ? startDoorway
                    : adminDoorway;
            }

            return startHasDoor ? startDoorway : adminDoorway;
        }

        private static bool ContainsViablePrefab(
            List<GameObjectWeight> weights,
            GameObject prefab)
        {
            if (weights == null)
                return false;

            for (int i = 0; i < weights.Count; i++)
            {
                GameObjectWeight entry = weights[i];
                if (entry != null && entry.GameObject == prefab && entry.Weight > 0f)
                    return true;
            }

            return false;
        }

        private static GameObject InstantiateDoorUsingDunGenRules(
            GameObject doorPrefab,
            Scene scene,
            Doorway placementOwner)
        {
            GameObject door = PrefabUtility.InstantiatePrefab(doorPrefab, scene) as GameObject;
            if (door == null)
                throw new InvalidOperationException($"Unable to instantiate '{DoorPrefabPath}'.");

            door.name = "Door_SM_A_Door_Placement_ActiveSceneInstance";
            door.transform.SetParent(placementOwner.transform, false);
            door.transform.localPosition = placementOwner.DoorPrefabPositionOffset;
            if (placementOwner.AvoidRotatingDoorPrefab)
                door.transform.rotation = Quaternion.Euler(placementOwner.DoorPrefabRotationOffset);
            else
                door.transform.localRotation = Quaternion.Euler(placementOwner.DoorPrefabRotationOffset);

            door.transform.localScale = Vector3.one;
            door.SetActive(true);
            RecordSceneInstanceTransform(door.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(door);
            return door;
        }

        private static void ConfigureExistingDoorState(
            GameObject doorInstance,
            Doorway startDoorway,
            Doorway adminDoorway)
        {
            DunGen.Door[] doorComponents =
                doorInstance.GetComponentsInChildren<DunGen.Door>(true);
            if (doorComponents.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one existing DunGen.Door, found {doorComponents.Length}.");
            }

            DunGen.Door door = doorComponents[0];
            Tile startTile = startDoorway.GetComponentInParent<Tile>();
            Tile adminTile = adminDoorway.GetComponentInParent<Tile>();
            if (startTile == null || adminTile == null)
                throw new InvalidOperationException("Canonical doorway tile references are missing.");

            door.DoorwayA = startDoorway;
            door.DoorwayB = adminDoorway;
            door.TileA = startTile;
            door.TileB = adminTile;
            EditorUtility.SetDirty(door);
            PrefabUtility.RecordPrefabInstancePropertyModifications(door);
        }

        private static void AssertExistingDualSideProbeReceiver(GameObject doorInstance)
        {
            const string receiverTypeName = "DungeonDoorDualSideProbeReceiver";
            int receiverCount = 0;
            MonoBehaviour[] behaviours =
                doorInstance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour != null &&
                    string.Equals(
                        behaviour.GetType().Name,
                        receiverTypeName,
                        StringComparison.Ordinal))
                {
                    receiverCount++;
                }
            }

            if (receiverCount != 1)
            {
                throw new InvalidOperationException(
                    "The production door's existing DungeonDoorDualSideProbeReceiver was not " +
                    $"preserved exactly once; count={receiverCount}.");
            }
        }

        private static void AssertSingleActiveDoorPrefabInstance(
            Scene scene,
            GameObject doorPrefab,
            GameObject expectedInstance)
        {
            int sourceRootCount = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(
                        transforms[i].gameObject);
                    if (source == doorPrefab)
                        sourceRootCount++;
                }
            }

            if (sourceRootCount != 1 || !expectedInstance.activeInHierarchy)
            {
                throw new InvalidOperationException(
                    "Expected one active Door_SM_A_Door_Placement scene instance; " +
                    $"sourceRoots={sourceRootCount} active={expectedInstance.activeInHierarchy}.");
            }
        }

        private static void AssertProductionLightingLayerParity(
            GameObject startRoom,
            GameObject adminRoom)
        {
            AssertProductionLightingLayerParity(startRoom);
            AssertProductionLightingLayerParity(adminRoom);
        }

        private static void AssertProductionLightingLayerParity(GameObject room)
        {
            if (room == null)
                throw new ArgumentNullException(nameof(room));

            string productionPrefabPath =
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(room);
            if (string.IsNullOrEmpty(productionPrefabPath))
            {
                throw new InvalidOperationException(
                    $"Room '{room.name}' has no production prefab asset path; " +
                    "production lighting parity cannot be proven.");
            }

            Renderer[] renderers = room.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Renderer source = renderer != null
                    ? PrefabUtility.GetCorrespondingObjectFromSourceAtPath<Renderer>(
                        renderer,
                        productionPrefabPath)
                    : null;
                if (source == null)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{GetHierarchyPath(renderer != null ? renderer.transform : room.transform)}' " +
                        "has no prefab source; production renderer parity cannot be proven.");
                }

                if (renderer.renderingLayerMask != source.renderingLayerMask)
                {
                    throw new InvalidOperationException(
                        $"Renderer rendering-layer parity failed at '{GetHierarchyPath(renderer.transform)}': " +
                        $"instance={renderer.renderingLayerMask} source={source.renderingLayerMask}. " +
                        "The validation scene must not rewrite production renderer light layers.");
                }
            }

            Light[] lights = room.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                Light source = light != null
                    ? PrefabUtility.GetCorrespondingObjectFromSourceAtPath<Light>(
                        light,
                        productionPrefabPath)
                    : null;
                if (source == null)
                {
                    throw new InvalidOperationException(
                        $"Light '{GetHierarchyPath(light != null ? light.transform : room.transform)}' " +
                        "has no prefab source; production light parity cannot be proven.");
                }

                if (light.renderingLayerMask != source.renderingLayerMask)
                {
                    throw new InvalidOperationException(
                        $"Light rendering-layer parity failed at '{GetHierarchyPath(light.transform)}': " +
                        $"instance={light.renderingLayerMask} source={source.renderingLayerMask}. " +
                        "The validation scene must not rewrite production light layers.");
                }

                UniversalAdditionalLightData additionalLightData =
                    light.GetComponent<UniversalAdditionalLightData>();
                UniversalAdditionalLightData sourceAdditionalLightData =
                    source.GetComponent<UniversalAdditionalLightData>();
                if ((additionalLightData == null) != (sourceAdditionalLightData == null))
                {
                    throw new InvalidOperationException(
                        $"URP additional-light component parity failed at '{GetHierarchyPath(light.transform)}'. " +
                        "The validation scene must not add or remove production light components.");
                }

                if (additionalLightData == null)
                    continue;

                uint instanceRenderingLayers =
                    unchecked((uint)additionalLightData.renderingLayers);
                uint sourceRenderingLayers =
                    unchecked((uint)sourceAdditionalLightData.renderingLayers);
                uint instanceShadowRenderingLayers =
                    unchecked((uint)additionalLightData.shadowRenderingLayers);
                uint sourceShadowRenderingLayers =
                    unchecked((uint)sourceAdditionalLightData.shadowRenderingLayers);
                if (instanceRenderingLayers != sourceRenderingLayers ||
                    instanceShadowRenderingLayers != sourceShadowRenderingLayers)
                {
                    throw new InvalidOperationException(
                        $"URP light-layer parity failed at '{GetHierarchyPath(light.transform)}': " +
                        $"instance={instanceRenderingLayers}/{instanceShadowRenderingLayers} " +
                        $"source={sourceRenderingLayers}/{sourceShadowRenderingLayers}. " +
                        "The validation scene must preserve production light and shadow layers.");
                }
            }
        }

        private static DungeonPortalEndpoint CreateEndpoint(
            Transform parent,
            string name,
            GameObject productionRoom,
            Transform doorway,
            out DungeonPortalPowerEnvelope powerEnvelope,
            out DungeonPortalRoomPowerBridge powerBridge)
        {
            GameObject endpointObject = CreateSceneObject(name, parent);
            endpointObject.transform.SetPositionAndRotation(
                doorway.position,
                doorway.rotation);
            powerEnvelope = endpointObject.AddComponent<DungeonPortalPowerEnvelope>();
            DungeonPortalEndpoint endpoint =
                endpointObject.AddComponent<DungeonPortalEndpoint>();
            endpoint.Configure(doorway, null, powerEnvelope);
            MonoBehaviour productionSwitcher = FindUniqueBehaviourByTypeName(
                productionRoom,
                "DungeonTileLightmapSwitcher");
            powerBridge = endpointObject.AddComponent<DungeonPortalRoomPowerBridge>();
            powerBridge.Configure(productionSwitcher, powerEnvelope, 0.5f);
            powerBridge.enabled = false;
            EditorUtility.SetDirty(powerBridge);
            return endpoint;
        }

        private static MonoBehaviour FindUniqueBehaviourByTypeName(
            GameObject root,
            string typeName)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            MonoBehaviour result = null;
            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour candidate = behaviours[i];
                if (candidate == null ||
                    !string.Equals(candidate.GetType().Name, typeName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (result != null)
                {
                    throw new InvalidOperationException(
                        $"'{root.name}' contains more than one '{typeName}' component.");
                }

                result = candidate;
            }

            if (result == null)
            {
                throw new InvalidOperationException(
                    $"'{root.name}' does not contain the required '{typeName}' component.");
            }

            return result;
        }

        private static void CreateFixedValidationCameras(
            Transform parent,
            Transform portalFrame)
        {
            CreateDisabledCamera(
                parent,
                "Start_to_Admin_FixedCamera_DISABLED",
                StartCameraPosition,
                StartCameraRotation);

            Vector3 planeNormal = portalFrame.forward.normalized;
            float signedDistance = Vector3.Dot(
                StartCameraPosition - ExpectedPortalPosition,
                planeNormal);
            Vector3 reversePosition =
                StartCameraPosition - 2f * signedDistance * planeNormal;
            Quaternion reverseRotation = Quaternion.LookRotation(
                -(StartCameraRotation * Vector3.forward),
                Vector3.up);
            CreateDisabledCamera(
                parent,
                "Admin_to_Start_FixedCamera_DISABLED",
                reversePosition,
                reverseRotation);
        }

        private static void CreateDisabledCamera(
            Transform parent,
            string name,
            Vector3 position,
            Quaternion rotation)
        {
            GameObject cameraObject = CreateSceneObject(name, parent);
            cameraObject.transform.SetPositionAndRotation(position, rotation);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(0.19215687f, 0.3019608f, 0.4745098f, 0f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 1000f;
            camera.fieldOfView = 90f;
            camera.cullingMask = 262135;
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.allowDynamicResolution = false;
            camera.useOcclusionCulling = true;

            UniversalAdditionalCameraData cameraData =
                camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            cameraData.stopNaN = false;
            cameraData.dithering = false;
            cameraData.volumeLayerMask = 1;
            camera.enabled = false;
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(cameraData);
        }

        private static void ConfigureSceneEnvironment()
        {
            LightingSettings lightingSettings =
                AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (lightingSettings == null)
            {
                throw new InvalidOperationException(
                    $"Missing production lighting settings '{LightingSettingsPath}'.");
            }

            Lightmapping.lightingSettings = lightingSettings;
            RenderSettings.fog = true;
            RenderSettings.fogColor =
                new Color(0.43137255f, 0.44225493f, 0.47058824f, 1f);
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0075f;
            RenderSettings.ambientSkyColor =
                new Color(0.053696696f, 0.07457874f, 0.17600583f, 0.76070315f);
            RenderSettings.ambientEquatorColor =
                new Color(0.36692742f, 0.4564219f, 0.5160849f, 0.76070315f);
            RenderSettings.ambientGroundColor =
                new Color(0.041764095f, 0.035797797f, 0.029831497f, 0.76070315f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.subtractiveShadowColor =
                new Color(0.42f, 0.478f, 0.627f, 1f);
            RenderSettings.skybox = null;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 256;
            RenderSettings.reflectionBounces = 1;
            RenderSettings.reflectionIntensity = 0.85f;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.sun = null;
        }

        private static void CreateValidationGlobalVolume(Transform parent)
        {
            VolumeProfile profile =
                AssetDatabase.LoadAssetAtPath<VolumeProfile>(ValidationVolumeProfilePath);
            if (profile == null)
            {
                throw new InvalidOperationException(
                    $"Missing validation volume profile '{ValidationVolumeProfilePath}'.");
            }

            GameObject volumeObject = CreateSceneObject(
                "04_ValidationGlobalVolume_StartMapParity",
                parent);
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.blendDistance = 0f;
            volume.weight = 1f;
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);
        }

        private static void AssertNoForbiddenOverlayNames(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    if (transforms[i].name.IndexOf(
                            ForbiddenOverlayName,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        throw new InvalidOperationException(
                            $"Forbidden duplicate overlay found: {transforms[i].GetHierarchyPath()}");
                    }
                }
            }
        }

        private static GameObject CreateSceneObject(string name, Transform parent)
        {
            var sceneObject = new GameObject(name);
            if (parent != null)
                sceneObject.transform.SetParent(parent, false);
            return sceneObject;
        }

        private static void RecordSceneInstanceTransform(Transform transform)
        {
            EditorUtility.SetDirty(transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(transform);
        }

        private static void EnsureAssetFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            int separator = folder.LastIndexOf('/');
            if (separator <= 0 || separator >= folder.Length - 1)
                throw new InvalidOperationException($"Invalid asset folder '{folder}'.");

            string parent = folder.Substring(0, separator);
            string child = folder.Substring(separator + 1);
            EnsureAssetFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, child)))
                throw new InvalidOperationException($"Unable to create asset folder '{folder}'.");
        }

        private static string GetHierarchyPath(this Transform transform)
        {
            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
