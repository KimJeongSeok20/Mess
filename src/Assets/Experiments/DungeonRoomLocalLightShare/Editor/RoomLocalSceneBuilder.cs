using System;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DungeonRoomLocalLightShare.Editor
{
    public static class RoomLocalSceneBuilder
    {
        public static string BuildIsolatedScene()
        {
            string idle = RoomLocalEditorUtil.RequireIdleEditor();
            if (idle != null)
                return idle;

            string hash = RoomLocalHashGuard.CaptureOrVerify();
            if (hash.StartsWith("FAIL", StringComparison.Ordinal))
                return hash;

            GameObject startPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                RoomLocalLightShareContract.StartPrefabPath);
            GameObject adminPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                RoomLocalLightShareContract.AdministrativePrefabPath);
            GameObject doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                RoomLocalLightShareContract.DoorPrefabPath);
            if (startPrefab == null || adminPrefab == null || doorPrefab == null)
                return "FAIL: V2 Start, Admin, or door prefab is missing.";

            Scene previous = SceneManager.GetActiveScene();
            string previousPath = previous.IsValid() ? previous.path : string.Empty;
            Scene preview = default;
            Scene startMapScene = default;
            bool closeStartMap = false;
            bool keepPreviewOpen = false;
            try
            {
                RoomLocalEditorUtil.EnsureFolder(RoomLocalLightShareContract.SceneFolder);
                startMapScene = SceneManager.GetSceneByPath(RoomLocalLightShareContract.StartMapScenePath);
                if (!startMapScene.IsValid() || !startMapScene.isLoaded)
                {
                    startMapScene = EditorSceneManager.OpenScene(
                        RoomLocalLightShareContract.StartMapScenePath,
                        OpenSceneMode.Additive);
                    closeStartMap = true;
                }

                // Unity cannot close the last loaded scene. Keep StartMap as a temporary
                // anchor before replacing an already-open isolated preview scene.
                Scene existing = SceneManager.GetSceneByPath(RoomLocalLightShareContract.IsolatedScenePath);
                if (existing.IsValid() && existing.isLoaded &&
                    !EditorSceneManager.CloseScene(existing, true))
                    return "FAIL: could not close the existing isolated scene.";

                DungeonZoneManager startMapZone =
                    RoomLocalEditorUtil.FindInScene<DungeonZoneManager>(startMapScene);

                preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(preview);
                RoomLocalEnvironment.ApplyIndoorContract();

                GameObject start = (GameObject)PrefabUtility.InstantiatePrefab(startPrefab, preview);
                GameObject admin = (GameObject)PrefabUtility.InstantiatePrefab(adminPrefab, preview);
                start.name = RoomLocalLightShareContract.StartRoomId;
                admin.name = RoomLocalLightShareContract.AdministrativeRoomId;
                start.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                admin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                // These rooms receive their lightmap index/ST at runtime. If they are saved
                // into this preview scene as Batching Static, Unity burns the prefab ST into
                // a static batch before Awake and later lightmapScaleOffset assignments are
                // ignored. Keep every other authoring flag, but make this isolated preview's
                // room instances eligible for runtime lightmap remapping.
                int startBatchingFlagsCleared = ClearBatchingStatic(start);
                int adminBatchingFlagsCleared = ClearBatchingStatic(admin);

                if (!TryResolveDoorways(start, admin, out Transform startDoor, out Transform adminDoor))
                    return "FAIL: no compatible Start/Admin doorway pair.";

                AlignRootToDoorway(admin.transform, adminDoor, startDoor);
                OpenPassage(startDoor);
                OpenPassage(adminDoor);
                RoomLocalEditorUtil.ApplyDungeonRenderingLayer(start);
                RoomLocalEditorUtil.ApplyDungeonRenderingLayer(admin);
                SetSerializedStartPower(start, DungeonTileLightmapSwitcher.PowerLevel.P0);
                SetSerializedStartPower(admin, DungeonTileLightmapSwitcher.PowerLevel.P100);
                DisableRoomRealtimeLights(start);
                DisableRoomRealtimeLights(admin);

                GameObject door = PlaceDoor(doorPrefab, preview, startDoor.GetComponent<Doorway>());
                Transform leaf = door.transform.Find(RoomLocalLightShareContract.DoorLeafPath);
                if (leaf == null)
                    return "FAIL: door leaf '" + RoomLocalLightShareContract.DoorLeafPath + "' is missing.";

                GameObject host = new GameObject("RoomLocalLightShare");
                SceneManager.MoveGameObjectToScene(host, preview);
                var angle = host.AddComponent<RoomLocalDoorAngleSource>();
                angle.Configure(leaf, leaf.localRotation, Vector3.up, 90f);
                angle.ApplyOpenFraction(1f);

                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(RoomLocalLightShareContract.ShaderPath);
                var connection = host.AddComponent<RoomLocalConnection>();
                connection.Configure(
                    start.transform,
                    admin.transform,
                    startDoor,
                    adminDoor,
                    AssetDatabase.LoadAssetAtPath<OutgoingPortalMap>(
                        RoomLocalLightShareContract.StartOutgoingPath),
                    AssetDatabase.LoadAssetAtPath<OutgoingPortalMap>(
                        RoomLocalLightShareContract.AdministrativeOutgoingPath),
                    AssetDatabase.LoadAssetAtPath<IncomingBounceData>(
                        RoomLocalLightShareContract.StartBouncePath),
                    AssetDatabase.LoadAssetAtPath<IncomingBounceData>(
                        RoomLocalLightShareContract.AdministrativeBouncePath),
                    shader,
                    angle);

                // A generated dungeon normally builds DungeonTileProbeRegistry and assigns the
                // connected TileA/TileB pair to the door. This isolated preview has neither, so
                // its production dual-side receiver would leave the dynamic door completely
                // black. Drive the split door faces directly from the two cloned rooms' baked SH
                // probe entries, and keep the portal cookie spots focused on receiver geometry.
                var productionDoorReceiver =
                    leaf.GetComponent<DungeonDoorDualSideProbeReceiver>();
                if (productionDoorReceiver != null)
                    productionDoorReceiver.enabled = false;
                var doorProbeDriver = host.AddComponent<RoomLocalDoorProbeDriver>();
                doorProbeDriver.Configure(
                    leaf,
                    start.transform,
                    admin.transform,
                    startDoor,
                    adminDoor,
                    start.GetComponent<DungeonTileLightmapSwitcher>(),
                    admin.GetComponent<DungeonTileLightmapSwitcher>(),
                    AssetDatabase.LoadAssetAtPath<OutgoingPortalMap>(
                        RoomLocalLightShareContract.StartOutgoingPath),
                    AssetDatabase.LoadAssetAtPath<OutgoingPortalMap>(
                        RoomLocalLightShareContract.AdministrativeOutgoingPath));
                host.AddComponent<RoomLocalPreviewController>();
                host.AddComponent<RoomLocalPreviewHud>();

                CreatePreviewCamera(
                    preview,
                    startDoor,
                    startMapZone,
                    start.GetComponent<DungeonTileLightmapSwitcher>(),
                    admin.GetComponent<DungeonTileLightmapSwitcher>());
                if (!EditorSceneManager.SaveScene(
                        preview, RoomLocalLightShareContract.IsolatedScenePath, false))
                    return "FAIL: could not save isolated scene.";

                AssetDatabase.SaveAssets();
                keepPreviewOpen = true;
                return
                    "PASS isolated Start/Admin scene\n" +
                    "scene=" + RoomLocalLightShareContract.IsolatedScenePath + "\n" +
                    "startDoor=" + startDoor.name + "\n" +
                    "adminDoor=" + adminDoor.name + "\n" +
                    "batchingStaticCleared=" +
                    (startBatchingFlagsCleared + adminBatchingFlagsCleared) +
                    " (start=" + startBatchingFlagsCleared +
                    ", admin=" + adminBatchingFlagsCleared + ")\n" +
                    "overlayRenderers=0\n" +
                    "default=Start P0 / Admin P100 / D100\n" +
                    "activeSceneLeftOpen=true\n" +
                    "keys=1 OFF, 2 ON, 3-6 power, 7-9 door";
            }
            catch (Exception exception)
            {
                return "FAIL: " + exception;
            }
            finally
            {
                if (startMapScene.IsValid() && startMapScene.isLoaded &&
                    (closeStartMap || keepPreviewOpen))
                    EditorSceneManager.CloseScene(startMapScene, true);
                if (!keepPreviewOpen && preview.IsValid() && preview.isLoaded)
                    EditorSceneManager.CloseScene(preview, true);
                if (keepPreviewOpen && preview.IsValid() && preview.isLoaded)
                    SceneManager.SetActiveScene(preview);
                else if (!string.IsNullOrEmpty(previousPath))
                {
                    Scene restore = SceneManager.GetSceneByPath(previousPath);
                    if (restore.IsValid() && restore.isLoaded)
                        SceneManager.SetActiveScene(restore);
                }
            }
        }

        public static void AlignRootToDoorway(
            Transform movingRoot,
            Transform movingDoorway,
            Transform fixedDoorway)
        {
            Quaternion target = Quaternion.LookRotation(-fixedDoorway.forward, fixedDoorway.up);
            Quaternion delta = target * Quaternion.Inverse(movingDoorway.rotation);
            movingRoot.rotation = delta * movingRoot.rotation;
            movingRoot.position += fixedDoorway.position - movingDoorway.position;
        }

        public static void OpenPassage(Transform doorway)
        {
            Transform anchor = doorway != null ? doorway.parent : null;
            if (anchor == null)
                return;
            Transform blocker = anchor.Find("Blocker_SM_A");
            Transform open = anchor.Find("No_Door_Placement");
            if (blocker != null)
                blocker.gameObject.SetActive(false);
            if (open != null)
                open.gameObject.SetActive(true);
        }

        public static bool TryResolveDoorways(
            GameObject start,
            GameObject admin,
            out Transform startDoor,
            out Transform adminDoor)
        {
            startDoor = start.transform.Find(RoomLocalLightShareContract.PreferredDoorwayPath);
            adminDoor = admin.transform.Find(RoomLocalLightShareContract.PreferredDoorwayPath);
            if (IsUsablePair(startDoor, adminDoor))
                return true;

            Doorway[] startDoors = start.GetComponentsInChildren<Doorway>(true);
            Doorway[] adminDoors = admin.GetComponentsInChildren<Doorway>(true);
            float bestOverlap = float.PositiveInfinity;
            startDoor = null;
            adminDoor = null;
            Vector3 adminPos = admin.transform.position;
            Quaternion adminRot = admin.transform.rotation;
            for (int i = 0; i < startDoors.Length; i++)
            {
                for (int j = 0; j < adminDoors.Length; j++)
                {
                    if (!DoorwaySocket.CanSocketsConnect(startDoors[i].Socket, adminDoors[j].Socket))
                        continue;
                    admin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    AlignRootToDoorway(
                        admin.transform,
                        adminDoors[j].transform,
                        startDoors[i].transform);
                    float overlap = OverlapVolume(RendererBounds(start), RendererBounds(admin));
                    if (overlap >= bestOverlap)
                        continue;
                    bestOverlap = overlap;
                    startDoor = startDoors[i].transform;
                    adminDoor = adminDoors[j].transform;
                }
            }

            admin.transform.SetPositionAndRotation(adminPos, adminRot);
            return startDoor != null && adminDoor != null;
        }

        private static bool IsUsablePair(Transform startDoor, Transform adminDoor)
        {
            if (startDoor == null || adminDoor == null)
                return false;
            Doorway start = startDoor.GetComponent<Doorway>();
            Doorway admin = adminDoor.GetComponent<Doorway>();
            return start != null && admin != null &&
                   DoorwaySocket.CanSocketsConnect(start.Socket, admin.Socket);
        }

        private static GameObject PlaceDoor(GameObject doorPrefab, Scene scene, Doorway owner)
        {
            GameObject door = (GameObject)PrefabUtility.InstantiatePrefab(doorPrefab, scene);
            door.name = "Door_SM_A_Door_Placement";
            door.transform.SetParent(owner.transform, false);
            door.transform.localPosition = owner.DoorPrefabPositionOffset;
            if (owner.AvoidRotatingDoorPrefab)
                door.transform.rotation = Quaternion.Euler(owner.DoorPrefabRotationOffset);
            else
                door.transform.localRotation = Quaternion.Euler(owner.DoorPrefabRotationOffset);
            door.transform.localScale = Vector3.one;
            door.SetActive(true);
            RoomLocalEditorUtil.ApplyDungeonRenderingLayer(door);
            return door;
        }

        private static void DisableRoomRealtimeLights(GameObject room)
        {
            var switcher = room.GetComponent<DungeonTileLightmapSwitcher>();
            if (switcher != null)
                switcher.RefreshControlledLights();
        }

        private static int ClearBatchingStatic(GameObject root)
        {
            if (root == null)
                return 0;

            int changed = 0;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                GameObject gameObject = transforms[i].gameObject;
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(gameObject);
                if ((flags & StaticEditorFlags.BatchingStatic) == 0)
                    continue;

                GameObjectUtility.SetStaticEditorFlags(
                    gameObject,
                    flags & ~StaticEditorFlags.BatchingStatic);
                PrefabUtility.RecordPrefabInstancePropertyModifications(gameObject);
                changed++;
            }

            return changed;
        }

        private static void SetSerializedStartPower(
            GameObject room,
            DungeonTileLightmapSwitcher.PowerLevel power)
        {
            var switcher = room.GetComponent<DungeonTileLightmapSwitcher>();
            if (switcher == null)
                return;
            SerializedObject serialized = new SerializedObject(switcher);
            SerializedProperty property = serialized.FindProperty("startPowerLevel");
            if (property != null)
            {
                property.enumValueIndex = (int)power;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void CreatePreviewCamera(
            Scene preview,
            Transform doorway,
            DungeonZoneManager startMapZone,
            DungeonTileLightmapSwitcher startLighting,
            DungeonTileLightmapSwitcher adminLighting)
        {
            var cameraObject = new GameObject("RoomLocalPreviewCamera");
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 70f;
            camera.nearClipPlane = 0.05f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<UniversalAdditionalCameraData>();

            Volume volume = cameraObject.AddComponent<Volume>();
            volume.enabled = true;
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                RoomLocalLightShareContract.DroneViewVolumePath);

            DungeonZoneManager zone = null;
            if (startMapZone != null)
            {
                var zoneObject = new GameObject("StartMapDungeonEnvironmentParity");
                SceneManager.MoveGameObjectToScene(zoneObject, preview);
                zone = zoneObject.AddComponent<DungeonZoneManager>();
                EditorUtility.CopySerialized(startMapZone, zone);
                SerializedObject serializedZone = new SerializedObject(zone);
                SerializedProperty overrideAmbient = serializedZone.FindProperty("overrideIndoorAmbient");
                if (overrideAmbient != null)
                    overrideAmbient.boolValue = false;
                SerializedProperty controlFog = serializedZone.FindProperty("controlFogOnDungeonTransition");
                if (controlFog != null)
                    controlFog.boolValue = false;
                serializedZone.ApplyModifiedPropertiesWithoutUndo();
            }

            var environment = cameraObject.AddComponent<RoomLocalEnvironment>();
            environment.Configure(zone, volume, startLighting, adminLighting);

            cameraObject.transform.position =
                doorway.position - doorway.forward * 3.2f + doorway.up * 1.62f;
            Vector3 lookTarget = doorway.position + doorway.up * 1.1f;
            cameraObject.transform.rotation = Quaternion.LookRotation(
                lookTarget - cameraObject.transform.position,
                Vector3.up);
        }

        private static Bounds RendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default;
            bool initialized = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is ParticleSystemRenderer)
                    continue;
                if (!initialized)
                {
                    bounds = renderers[i].bounds;
                    initialized = true;
                }
                else
                    bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static float OverlapVolume(Bounds first, Bounds second)
        {
            Vector3 min = Vector3.Max(first.min, second.min);
            Vector3 max = Vector3.Min(first.max, second.max);
            Vector3 overlap = max - min;
            if (overlap.x <= 0f || overlap.y <= 0f || overlap.z <= 0f)
                return 0f;
            return overlap.x * overlap.y * overlap.z;
        }

        private static void CaptureRenderSettings(
            out bool fog,
            out Color fogColor,
            out FogMode fogMode,
            out float fogDensity,
            out AmbientMode ambientMode,
            out Color ambientSky,
            out float ambientIntensity,
            out Material skybox,
            out float reflectionIntensity)
        {
            fog = RenderSettings.fog;
            fogColor = RenderSettings.fogColor;
            fogMode = RenderSettings.fogMode;
            fogDensity = RenderSettings.fogDensity;
            ambientMode = RenderSettings.ambientMode;
            ambientSky = RenderSettings.ambientSkyColor;
            ambientIntensity = RenderSettings.ambientIntensity;
            skybox = RenderSettings.skybox;
            reflectionIntensity = RenderSettings.reflectionIntensity;
        }

        private static void ApplyRenderSettings(
            bool fog,
            Color fogColor,
            FogMode fogMode,
            float fogDensity,
            AmbientMode ambientMode,
            Color ambientSky,
            float ambientIntensity,
            Material skybox,
            float reflectionIntensity)
        {
            RenderSettings.fog = fog;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.skybox = skybox;
            RenderSettings.sun = null;
            RenderSettings.reflectionIntensity = reflectionIntensity;
        }
    }
}
