using System;
using System.IO;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GrokDoorwayLighting.Editor
{
    public static class GrokDoorwaySceneBuilder
    {
        public const string AssetRoot = "Assets/Experiments/GrokDoorwayLighting";
        public const string SceneFolder = AssetRoot + "/Scenes";
        public const string PreviewScenePath = SceneFolder + "/Start_Admin_IsolatedPreview.unity";
        public const string StartPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/StartRoom_R000.prefab";
        public const string AdminPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/AdminstrativeSegregation_R000.prefab";
        public const string PreferredDoorwayPath = "Doorways/Door_SM_A/DoorWayPoint";
        private const string StartMapScenePath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
        private const string StartMapLightingSettingsPath = "Assets/New Lighting Settings.lighting";
        private const string PlayerPrefabPath = "Assets/FPS/Cyber_Generic.prefab";

        public static string BuildIsolatedPreviewScene()
        {
            if (Application.isPlaying)
                return "FAIL: Unity is in Play Mode. Stop Play Mode first.";
            if (Lightmapping.isRunning)
                return "FAIL: a lightmap bake is already running. Do not interrupt it.";

            GameObject startPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StartPrefabPath);
            GameObject adminPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AdminPrefabPath);
            if (startPrefab == null || adminPrefab == null)
                return "FAIL: rotated StartRoom_R000 or AdminstrativeSegregation_R000 is missing.";

            Scene previous = SceneManager.GetActiveScene();
            string previousPath = previous.IsValid() ? previous.path : string.Empty;
            LightingSettings previousLightingSettings = null;
            try
            {
                previousLightingSettings = Lightmapping.lightingSettings;
            }
            catch (Exception)
            {
                previousLightingSettings = null;
            }
            Scene preview = default;
            Scene startMapScene = default;
            bool closeStartMap = false;
            try
            {
                EnsureFolder(SceneFolder);
                Scene existing = SceneManager.GetSceneByPath(PreviewScenePath);
                if (existing.IsValid() && existing.isLoaded &&
                    !EditorSceneManager.CloseScene(existing, true))
                    return $"FAIL: could not close existing preview '{PreviewScenePath}'.";

                startMapScene = SceneManager.GetSceneByPath(StartMapScenePath);
                if (!startMapScene.IsValid() || !startMapScene.isLoaded)
                {
                    startMapScene = EditorSceneManager.OpenScene(StartMapScenePath, OpenSceneMode.Additive);
                    closeStartMap = true;
                }

                DungeonZoneManager startMapZone =
                    FindComponentInScene<DungeonZoneManager>(startMapScene);
                if (startMapZone == null)
                    return "FAIL: StartMap DungeonZoneManager is missing.";

                SceneManager.SetActiveScene(startMapScene);
                GrokDoorwayEnvironment.LightingEnvironment startMapLighting =
                    GrokDoorwayEnvironment.LightingEnvironment.Capture();

                preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(preview);
                startMapLighting.Apply();

                GameObject start = (GameObject)PrefabUtility.InstantiatePrefab(startPrefab, preview);
                GameObject admin = (GameObject)PrefabUtility.InstantiatePrefab(adminPrefab, preview);
                start.name = "StartRoom_R000";
                admin.name = "AdminstrativeSegregation_R000";
                start.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                admin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                if (!TryResolveDoorways(start, admin, out Transform startDoor, out Transform adminDoor))
                    return "FAIL: no compatible Start/Admin doorway pair.";

                AlignRootToDoorway(admin.transform, adminDoor, startDoor);
                OpenPassage(startDoor);
                OpenPassage(adminDoor);
                ApplyDungeonRenderingLayer(start);
                ApplyDungeonRenderingLayer(admin);

                DungeonZoneManager previewZone = CreatePreviewZoneManager(startMapZone, preview);
                string cameraReport = CreateDungeonEntryCamera(
                    preview,
                    startDoor,
                    previewZone,
                    startMapLighting);

                var host = new GameObject("GrokDoorwayLighting");
                SceneManager.MoveGameObjectToScene(host, preview);
                var bridge = host.AddComponent<GrokDoorwayLightingBridge>();
                host.AddComponent<GrokDoorwayPreviewHud>();
                bridge.Configure(
                    start.transform,
                    admin.transform,
                    startDoor,
                    adminDoor,
                    AssetDatabase.LoadAssetAtPath<GrokDoorwayPortalMap>(
                        GrokDoorwayPortalMapBaker.StartPortalPath),
                    AssetDatabase.LoadAssetAtPath<GrokDoorwayPortalMap>(
                        GrokDoorwayPortalMapBaker.AdminPortalPath));

                if (!EditorSceneManager.SaveScene(preview, PreviewScenePath, true))
                    return $"FAIL: could not save '{PreviewScenePath}'.";

                AssetDatabase.SaveAssets();
                return
                    "PASS GrokDoorwayLighting isolated preview\n" +
                    $"scene={PreviewScenePath}\n" +
                    $"startDoor={AnimationUtility.CalculateTransformPath(startDoor, start.transform)}\n" +
                    $"adminDoor={AnimationUtility.CalculateTransformPath(adminDoor, admin.transform)}\n" +
                    cameraReport + "\n" +
                    "note=StartMap dungeon-entry environment copied, StartMap not saved";
            }
            catch (Exception exception)
            {
                return $"FAIL: {exception}";
            }
            finally
            {
                if (preview.IsValid() && preview.isLoaded)
                    EditorSceneManager.CloseScene(preview, true);
                if (closeStartMap && startMapScene.IsValid() && startMapScene.isLoaded)
                    EditorSceneManager.CloseScene(startMapScene, true);
                if (previousLightingSettings != null)
                {
                    try
                    {
                        Lightmapping.lightingSettings = previousLightingSettings;
                    }
                    catch (Exception)
                    {
                    }
                }

                if (!string.IsNullOrEmpty(previousPath))
                {
                    Scene restore = SceneManager.GetSceneByPath(previousPath);
                    if (restore.IsValid() && restore.isLoaded)
                        SceneManager.SetActiveScene(restore);
                }
            }
        }

        public static bool TryResolveDoorways(
            GameObject start,
            GameObject admin,
            out Transform startDoor,
            out Transform adminDoor)
        {
            startDoor = start.transform.Find(PreferredDoorwayPath);
            adminDoor = admin.transform.Find(PreferredDoorwayPath);
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
                    AlignRootToDoorway(admin.transform, adminDoors[j].transform, startDoors[i].transform);
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

        public static void AlignRootToDoorway(Transform movingRoot, Transform movingDoorway, Transform fixedDoorway)
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

        private static bool IsUsablePair(Transform startDoor, Transform adminDoor)
        {
            if (startDoor == null || adminDoor == null)
                return false;
            Doorway start = startDoor.GetComponent<Doorway>();
            Doorway admin = adminDoor.GetComponent<Doorway>();
            return start != null && admin != null &&
                   DoorwaySocket.CanSocketsConnect(start.Socket, admin.Socket);
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
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
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

        private static DungeonZoneManager CreatePreviewZoneManager(DungeonZoneManager source, Scene preview)
        {
            var managerObject = new GameObject("StartMapDungeonEnvironmentParity");
            SceneManager.MoveGameObjectToScene(managerObject, preview);
            DungeonZoneManager target = managerObject.AddComponent<DungeonZoneManager>();
            EditorUtility.CopySerialized(source, target);
            SerializedObject serializedTarget = new SerializedObject(target);
            serializedTarget.FindProperty("exteriorDirectionalLights").arraySize = 0;
            serializedTarget.FindProperty("cloudShadowBehaviours").arraySize = 0;
            serializedTarget.FindProperty("overrideIndoorAmbient").boolValue = false;
            serializedTarget.FindProperty("controlFogOnDungeonTransition").boolValue = false;
            serializedTarget.ApplyModifiedPropertiesWithoutUndo();
            return target;
        }

        private static VolumeProfile CreateReadableDungeonVolume(Volume sourceVolume)
        {
            if (sourceVolume.sharedProfile == null)
                return null;

            EnsureFolder(AssetRoot + "/Generated");
            const string path = AssetRoot + "/Generated/StartMapDungeonEntryVolume.asset";
            VolumeProfile existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (existing != null)
                AssetDatabase.DeleteAsset(path);

            VolumeProfile clone = UnityEngine.Object.Instantiate(sourceVolume.sharedProfile);
            clone.name = "StartMapDungeonEntryVolume";
            for (int i = 0; i < clone.components.Count; i++)
            {
                if (clone.components[i] is LensDistortion distortion)
                    distortion.active = false;
            }

            AssetDatabase.CreateAsset(clone, path);
            EditorUtility.SetDirty(clone);
            return clone;
        }

        private static string CreateDungeonEntryCamera(
            Scene preview,
            Transform doorway,
            DungeonZoneManager previewZone,
            GrokDoorwayEnvironment.LightingEnvironment startMapLighting)
        {
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null)
                throw new InvalidOperationException("Cyber_Generic prefab is missing.");

            Camera sourceCamera = playerPrefab.GetComponentInChildren<Camera>(true);
            Volume sourceVolume = sourceCamera != null ? sourceCamera.GetComponent<Volume>() : null;
            Light sourceDungeonLight = sourceCamera != null
                ? sourceCamera.GetComponentInChildren<Light>(true)
                : null;
            if (sourceCamera == null || sourceVolume == null)
                throw new InvalidOperationException("Cyber_Generic camera or dungeon Volume is missing.");

            var cameraObject = new GameObject("GrokPreviewCamera");
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            EditorUtility.CopySerialized(sourceCamera, camera);
            camera.targetTexture = null;
            camera.usePhysicalProperties = false;
            camera.fieldOfView = 70f;
            camera.nearClipPlane = 0.05f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<GrokDoorwayFlyCamera>();

            UniversalAdditionalCameraData sourceCameraData =
                sourceCamera.GetComponent<UniversalAdditionalCameraData>();
            UniversalAdditionalCameraData cameraData =
                cameraObject.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null)
                cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            if (sourceCameraData != null)
                EditorUtility.CopySerialized(sourceCameraData, cameraData);

            Volume volume = cameraObject.AddComponent<Volume>();
            EditorUtility.CopySerialized(sourceVolume, volume);
            volume.enabled = true;
            VolumeProfile readableProfile = CreateReadableDungeonVolume(sourceVolume);
            if (readableProfile != null)
                volume.sharedProfile = readableProfile;

            Light dungeonOnlyLight = null;
            if (sourceDungeonLight != null)
            {
                var lightObject = new GameObject(sourceDungeonLight.name);
                lightObject.transform.SetParent(cameraObject.transform, false);
                lightObject.transform.localPosition = sourceDungeonLight.transform.localPosition;
                lightObject.transform.localRotation = sourceDungeonLight.transform.localRotation;
                dungeonOnlyLight = lightObject.AddComponent<Light>();
                EditorUtility.CopySerialized(sourceDungeonLight, dungeonOnlyLight);
                dungeonOnlyLight.enabled = false;
                UniversalAdditionalLightData sourceLightData =
                    sourceDungeonLight.GetComponent<UniversalAdditionalLightData>();
                if (sourceLightData != null)
                {
                    UniversalAdditionalLightData lightData =
                        lightObject.GetComponent<UniversalAdditionalLightData>();
                    if (lightData == null)
                        lightData = lightObject.AddComponent<UniversalAdditionalLightData>();
                    EditorUtility.CopySerialized(sourceLightData, lightData);
                }
            }

            var environment = cameraObject.AddComponent<GrokDoorwayEnvironment>();
            environment.Configure(previewZone, volume, dungeonOnlyLight, startMapLighting);

            cameraObject.transform.position =
                doorway.position - doorway.forward * 3.2f + doorway.up * 1.62f;
            Vector3 lookTarget = doorway.position + doorway.up * 1.1f;
            cameraObject.transform.rotation = Quaternion.LookRotation(
                lookTarget - cameraObject.transform.position,
                Vector3.up);

            string profileName = volume.sharedProfile != null ? volume.sharedProfile.name : "MISSING";
            return
                $"volume={profileName}\n" +
                $"skybox={(startMapLighting.skybox != null ? startMapLighting.skybox.name : "none")}\n" +
                $"ambientMode={startMapLighting.ambientMode}\n" +
                $"fog={startMapLighting.fog} density={startMapLighting.fogDensity}\n" +
                $"reflectionIntensity={startMapLighting.reflectionIntensity}\n" +
                $"fov={camera.fieldOfView}\n" +
                "dungeonSpot=OFF lensDistortion=OFF(copy)";
        }

        private static void ApplyDungeonRenderingLayer(GameObject root)
        {
            int layerIndex = RenderingLayerMask.NameToRenderingLayer("Dungeon");
            if (layerIndex < 0)
                return;

            uint mask = 1u << layerIndex;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].renderingLayerMask = mask;
        }

        private static T FindComponentInScene<T>(Scene scene) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T component = roots[i].GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }

            return null;
        }

        public static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder))
                return;

            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string name = Path.GetFileName(assetFolder);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
