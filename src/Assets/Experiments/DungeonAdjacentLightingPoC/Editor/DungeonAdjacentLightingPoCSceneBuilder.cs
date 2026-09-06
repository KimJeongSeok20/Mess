using System;
using System.Collections.Generic;
using System.IO;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DungeonAdjacentLightingPoC.Editor
{
    public static class DungeonAdjacentLightingPoCSceneBuilder
    {
        private const string RootFolder = "Assets/Experiments/DungeonAdjacentLightingPoC";
        private const string SceneFolder = RootFolder + "/Scenes";
        private const string ReportPath = RootFolder + "/Generated/SceneBuildReport.txt";
        private const string BakeLightingSettingsPath = "Assets/OfficalLightmap.lighting";
        private const string StartMapLightingSettingsPath = "Assets/New Lighting Settings.lighting";
        private const string StartMapScenePath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
        private const string PlayerPrefabPath = "Assets/FPS/Cyber_Generic.prefab";
        private const string StartPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tile_modified/StartRoom.prefab";
        private const string AdminPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tile_modified/AdminstrativeSegregation.prefab";
        private const string StartRuntimePrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/StartRoom_R000.prefab";
        private const string AdminRuntimePrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/AdminstrativeSegregation_R000.prefab";
        private const string StartBakeScenePath =
            SceneFolder + "/StartRoom_Doorways_Door_SM_A_DoorWayPoint_ExtensionBake.unity";
        private const string AdminBakeScenePath =
            SceneFolder + "/Admin_Doorways_Door_SM_A_DoorWayPoint_ExtensionBake.unity";
        private const string StartPreviewDoorwayPath = "Doorways/Door_SM_A/DoorWayPoint";
        private const string AdminPreviewDoorwayPath = "Doorways/Door_SM_A/DoorWayPoint";
        private const string PreviewMaterialFolder = RootFolder + "/Generated/PreviewMaterials";
        private const string PreviewReceiverMeshFolder = RootFolder + "/Generated/ReceiverMeshes";
        private const string PairBakeDataPath =
            RootFolder + "/Generated/PairBake/Start_Admin_R000_PairBakeData.asset";
        private const string PreviewScenePath =
            SceneFolder + "/Start_Admin_WiredRuntimePreview.unity";

        [MenuItem("Tools/Dungeon/Lighting/Adjacent Lightmap PoC/Build Isolated Start-Admin Scenes")]
        public static void BuildFromMenu()
        {
            Debug.Log(BuildIsolatedScenes());
        }

        public static string BuildIsolatedScenes()
        {
            GameObject startPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StartPrefabPath);
            GameObject adminPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AdminPrefabPath);
            GameObject startRuntimePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StartRuntimePrefabPath);
            GameObject adminRuntimePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AdminRuntimePrefabPath);
            if (startPrefab == null || adminPrefab == null ||
                startRuntimePrefab == null || adminRuntimePrefab == null)
                return "FAIL: StartRoom or AdminstrativeSegregation source prefab is missing.";

            EnsureFolder(SceneFolder);
            EnsureFolder(Path.GetDirectoryName(ReportPath)?.Replace('\\', '/'));

            DungeonAdjacentLightingPoCEditorState originalState =
                DungeonAdjacentLightingPoCEditorState.Capture();
            PairSelection pair;
            try
            {
                pair = FindBestPair(startPrefab, adminPrefab);
                if (!pair.IsValid)
                    return "FAIL: no compatible Start/Admin doorway pair was found.";

                string startScenePath = $"{SceneFolder}/StartRoom_{Sanitize(pair.startDoorwayPath)}_ExtensionBake.unity";
                string adminScenePath = $"{SceneFolder}/Admin_{Sanitize(pair.adminDoorwayPath)}_ExtensionBake.unity";
                string referenceScenePath = $"{SceneFolder}/Start_Admin_PairReference.unity";

                BuildSingleRoomScene(startPrefab, pair.startDoorwayPath, startScenePath);
                BuildSingleRoomScene(adminPrefab, pair.adminDoorwayPath, adminScenePath);
                BuildPairReferenceScene(startRuntimePrefab, adminRuntimePrefab, pair, referenceScenePath);

                string report =
                    "PASS DungeonAdjacentLightingPoC isolated scenes\n" +
                    $"startDoorway={pair.startDoorwayPath}\n" +
                    $"adminDoorway={pair.adminDoorwayPath}\n" +
                    $"overlapVolume={pair.overlapVolume:F6}\n" +
                    $"startScene={startScenePath}\n" +
                    $"adminScene={adminScenePath}\n" +
                    $"referenceScene={referenceScenePath}\n";
                File.WriteAllText(ReportPath, report);
                AssetDatabase.ImportAsset(ReportPath);
                AssetDatabase.SaveAssets();
                return report;
            }
            catch (Exception exception)
            {
                return $"FAIL: {exception}";
            }
            finally
            {
                originalState.Restore();
            }
        }

        [MenuItem("Tools/Dungeon/Lighting/Adjacent Lightmap PoC/Build Wired Runtime Preview")]
        public static void BuildWiredPreviewFromMenu()
        {
            Debug.Log(BuildWiredRuntimePreview());
        }

        public static string BuildWiredRuntimePreview()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(StartBakeScenePath) == null ||
                AssetDatabase.LoadAssetAtPath<SceneAsset>(AdminBakeScenePath) == null)
                return "FAIL: extension bake scenes are missing.";

            GameObject startRuntimePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StartRuntimePrefabPath);
            GameObject adminRuntimePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AdminRuntimePrefabPath);
            if (startRuntimePrefab == null || adminRuntimePrefab == null)
                return "FAIL: rotated runtime prefabs are missing.";

            // The preview is generated output. Close a previously opened copy before
            // creating the replacement; Unity refuses to save a second loaded scene
            // to the same asset path even when the new scene is otherwise valid.
            Scene existingPreviewScene = SceneManager.GetSceneByPath(PreviewScenePath);
            if (existingPreviewScene.IsValid() && existingPreviewScene.isLoaded &&
                !EditorSceneManager.CloseScene(existingPreviewScene, true))
            {
                return $"FAIL: unable to close the existing generated preview '{PreviewScenePath}'.";
            }

            DungeonAdjacentLightingPoCEditorState originalState =
                DungeonAdjacentLightingPoCEditorState.Capture();
            Scene startBakeScene = default;
            Scene adminBakeScene = default;
            Scene startMapSourceScene = default;
            Scene previewScene = default;
            bool closeStartMapSourceScene = false;
            try
            {
                startMapSourceScene = SceneManager.GetSceneByPath(StartMapScenePath);
                if (!startMapSourceScene.IsValid() || !startMapSourceScene.isLoaded)
                {
                    startMapSourceScene = EditorSceneManager.OpenScene(
                        StartMapScenePath,
                        OpenSceneMode.Additive);
                    closeStartMapSourceScene = true;
                }

                DungeonZoneManager startMapZoneManagerSource =
                    FindComponentInScene<DungeonZoneManager>(startMapSourceScene);
                if (startMapZoneManagerSource == null)
                    return "FAIL: StartMap DungeonZoneManager source is missing.";
                StartMapSceneEnvironment startMapEnvironment =
                    CaptureStartMapSceneEnvironment(startMapSourceScene);

                startBakeScene = EditorSceneManager.OpenScene(StartBakeScenePath, OpenSceneMode.Additive);
                adminBakeScene = EditorSceneManager.OpenScene(AdminBakeScenePath, OpenSceneMode.Additive);
                DungeonAdjacentLightmapExtension startSourceExtension =
                    FindExtensionInScene(startBakeScene, StartPreviewDoorwayPath);
                DungeonAdjacentLightmapExtension adminSourceExtension =
                    FindExtensionInScene(adminBakeScene, AdminPreviewDoorwayPath);
                if (!HasBothStates(startSourceExtension) || !HasBothStates(adminSourceExtension))
                    return "FAIL: both Start/Admin extensions must contain P100 and P0 captures.";

                ExtensionStatePair startStates = CopyStates(startSourceExtension);
                ExtensionStatePair adminStates = CopyStates(adminSourceExtension);

                previewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(previewScene);
                ApplyStartMapSceneEnvironment(startMapEnvironment);

                DungeonZoneManager previewZoneManager =
                    CreatePreviewZoneManager(startMapZoneManagerSource);

                GameObject start = (GameObject)PrefabUtility.InstantiatePrefab(startRuntimePrefab, previewScene);
                GameObject admin = (GameObject)PrefabUtility.InstantiatePrefab(adminRuntimePrefab, previewScene);
                start.name = "StartRoom_AdjacentLightmapPreview";
                admin.name = "AdminstrativeSegregation_AdjacentLightmapPreview";
                start.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                admin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                Transform startDoorway = start.transform.Find(StartPreviewDoorwayPath);
                Transform adminDoorway = admin.transform.Find(AdminPreviewDoorwayPath);
                if (startDoorway == null || adminDoorway == null)
                    return "FAIL: preview doorway paths are missing.";
                AlignRootToDoorway(admin.transform, adminDoorway, startDoorway);
                ConfigureConnectedDoorway(startDoorway);
                ConfigureConnectedDoorway(adminDoorway);

                DungeonAdjacentLightmapExtension startExtension =
                    DungeonAdjacentLightmapExtensionAuthoring.CreateConnectionExtensionForDoorway(
                        startDoorway.GetComponent<Doorway>());
                DungeonAdjacentLightmapExtension adminExtension =
                    DungeonAdjacentLightmapExtensionAuthoring.CreateConnectionExtensionForDoorway(
                        adminDoorway.GetComponent<Doorway>());
                ApplyStates(startExtension, startStates);
                ApplyStates(adminExtension, adminStates);

                int startDungeonRenderers = ApplyDungeonRenderingLayer(start, "Dungeon");
                int adminDungeonRenderers = ApplyDungeonRenderingLayer(admin, "Dungeon");

                DungeonTileLightmapSwitcher startLighting =
                    start.GetComponentInChildren<DungeonTileLightmapSwitcher>(true);
                DungeonTileLightmapSwitcher adminLighting =
                    admin.GetComponentInChildren<DungeonTileLightmapSwitcher>(true);
                if (startLighting == null || adminLighting == null)
                    return "FAIL: runtime lightmap switcher is missing from preview rooms.";

                List<ReceiverCandidate> startReceivers = FindReceivers(
                    start,
                    startDoorway,
                    adminExtension);
                List<ReceiverCandidate> adminReceivers = FindReceivers(
                    admin,
                    adminDoorway,
                    startExtension);
                if (startReceivers.Count == 0 || adminReceivers.Count == 0)
                    return $"FAIL: receiver search start={startReceivers.Count} admin={adminReceivers.Count}";

                int startProjectedVertices = 0;
                int adminProjectedVertices = 0;
                for (int i = 0; i < startReceivers.Count; i++)
                {
                    startProjectedVertices += ConfigureExtensionOverlayReceiver(
                        startReceivers[i],
                        startDoorway,
                        adminExtension,
                        adminLighting,
                        $"StartReceiver_{i}_{Sanitize(startReceivers[i].renderer.name)}");
                }
                for (int i = 0; i < adminReceivers.Count; i++)
                {
                    adminProjectedVertices += ConfigureExtensionOverlayReceiver(
                        adminReceivers[i],
                        adminDoorway,
                        startExtension,
                        startLighting,
                        $"AdminReceiver_{i}_{Sanitize(adminReceivers[i].renderer.name)}");
                }
                if (startProjectedVertices <= 1 || adminProjectedVertices <= 1)
                {
                    return
                        $"FAIL: receiver projection is still too sparse " +
                        $"start={startProjectedVertices} admin={adminProjectedVertices}.";
                }
                PreviewCameraEnvironment cameraEnvironment = CreatePreviewCamera(
                    startDoorway,
                    startLighting,
                    adminLighting,
                    previewZoneManager);

                EditorSceneManager.MarkSceneDirty(previewScene);
                if (!EditorSceneManager.SaveScene(previewScene, PreviewScenePath, true))
                    return $"FAIL: unable to save '{PreviewScenePath}'.";

                AssetDatabase.SaveAssets();
                string report =
                    "PASS DungeonAdjacentLightingPoC wired runtime preview\n" +
                    $"scene={PreviewScenePath}\n" +
                    $"startReceivers={startReceivers.Count}\n" +
                    $"startProjectedVertices={startProjectedVertices}\n" +
                    $"adminReceivers={adminReceivers.Count}\n" +
                    $"adminProjectedVertices={adminProjectedVertices}\n" +
                    $"mappingMode=room-local-extension-secondary-uv-overlay\n" +
                    $"pairBakeData=NONE\n" +
                    $"bakePasses=O(roomCount*powerStateCount)\n" +
                    $"extensionData=O(totalDoorwayCount*powerStateCount)\n" +
                    $"startDungeonRenderers={startDungeonRenderers}\n" +
                    $"adminDungeonRenderers={adminDungeonRenderers}\n" +
                    $"lightingSettings={AssetDatabase.GetAssetPath(startMapEnvironment.lightingSettings)}\n" +
                    $"volumeProfile={AssetDatabase.GetAssetPath(cameraEnvironment.volume.sharedProfile)}\n" +
                    $"dungeonOnlyLight={cameraEnvironment.dungeonOnlyLight.name}/forced-OFF\n" +
                    $"ambientMode={previewZoneManager.indoorAmbientMode}\n" +
                    $"ambientLight={previewZoneManager.indoorAmbientLight}\n" +
                    $"ambientIntensity={previewZoneManager.indoorAmbientIntensity}\n";
                File.WriteAllText(RootFolder + "/Generated/WiredPreviewReport.txt", report);
                AssetDatabase.ImportAsset(RootFolder + "/Generated/WiredPreviewReport.txt");
                return report;
            }
            catch (Exception exception)
            {
                return $"FAIL: wired preview threw {exception}";
            }
            finally
            {
                if (startBakeScene.IsValid() && startBakeScene.isLoaded)
                    EditorSceneManager.CloseScene(startBakeScene, true);
                if (adminBakeScene.IsValid() && adminBakeScene.isLoaded)
                    EditorSceneManager.CloseScene(adminBakeScene, true);
                if (previewScene.IsValid() && previewScene.isLoaded)
                    EditorSceneManager.CloseScene(previewScene, true);
                if (closeStartMapSourceScene &&
                    startMapSourceScene.IsValid() && startMapSourceScene.isLoaded)
                    EditorSceneManager.CloseScene(startMapSourceScene, true);
                originalState.Restore();
            }
        }

        private static PairSelection FindBestPair(GameObject startPrefab, GameObject adminPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                GameObject start = (GameObject)PrefabUtility.InstantiatePrefab(startPrefab, scene);
                GameObject admin = (GameObject)PrefabUtility.InstantiatePrefab(adminPrefab, scene);
                start.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                admin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                Doorway[] startDoorways = start.GetComponentsInChildren<Doorway>(true);
                Doorway[] adminDoorways = admin.GetComponentsInChildren<Doorway>(true);
                PairSelection best = default;
                best.overlapVolume = float.PositiveInfinity;

                for (int startIndex = 0; startIndex < startDoorways.Length; startIndex++)
                {
                    for (int adminIndex = 0; adminIndex < adminDoorways.Length; adminIndex++)
                    {
                        Doorway startDoorway = startDoorways[startIndex];
                        Doorway adminDoorway = adminDoorways[adminIndex];
                        if (!DoorwaySocket.CanSocketsConnect(startDoorway.Socket, adminDoorway.Socket))
                            continue;

                        admin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        AlignRootToDoorway(admin.transform, adminDoorway.transform, startDoorway.transform);
                        float overlap = CalculateOverlapVolume(
                            CalculateRendererBounds(start),
                            CalculateRendererBounds(admin));

                        if (overlap >= best.overlapVolume)
                            continue;

                        best = new PairSelection
                        {
                            startDoorwayPath = AnimationUtility.CalculateTransformPath(startDoorway.transform, start.transform),
                            adminDoorwayPath = AnimationUtility.CalculateTransformPath(adminDoorway.transform, admin.transform),
                            overlapVolume = overlap
                        };
                    }
                }

                return best;
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void BuildSingleRoomScene(
            GameObject prefab,
            string doorwayPath,
            string scenePath)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                GameObject room = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                SceneManager.SetActiveScene(scene);
                AssignLightingSettings(BakeLightingSettingsPath);
                room.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Transform doorwayTransform = room.transform.Find(doorwayPath);
                Doorway doorway = doorwayTransform != null ? doorwayTransform.GetComponent<Doorway>() : null;
                if (doorway == null)
                    throw new InvalidOperationException($"Doorway '{doorwayPath}' not found in {prefab.name}.");

                IReadOnlyList<DungeonAdjacentLightmapExtension> extensions =
                    DungeonAdjacentLightmapExtensionAuthoring.CreateExtensionsForAllDoorways(room);
                if (extensions.Count == 0)
                    throw new InvalidOperationException($"No doorway extensions were created for {prefab.name}.");
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, scenePath, true))
                    throw new InvalidOperationException($"Failed to save scene '{scenePath}'.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void BuildPairReferenceScene(
            GameObject startPrefab,
            GameObject adminPrefab,
            PairSelection pair,
            string scenePath)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                GameObject start = (GameObject)PrefabUtility.InstantiatePrefab(startPrefab, scene);
                GameObject admin = (GameObject)PrefabUtility.InstantiatePrefab(adminPrefab, scene);
                SceneManager.SetActiveScene(scene);
                AssignLightingSettings(BakeLightingSettingsPath);
                start.name = "StartRoom_PairReference";
                admin.name = "AdminstrativeSegregation_PairReference";
                start.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                admin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                Transform startDoorway = start.transform.Find(pair.startDoorwayPath);
                Transform adminDoorway = admin.transform.Find(pair.adminDoorwayPath);
                if (startDoorway == null || adminDoorway == null)
                    throw new InvalidOperationException("Pair reference doorway path could not be resolved.");

                AlignRootToDoorway(admin.transform, adminDoorway, startDoorway);
                ConfigureConnectedDoorway(startDoorway);
                ConfigureConnectedDoorway(adminDoorway);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, scenePath, true))
                    throw new InvalidOperationException($"Failed to save scene '{scenePath}'.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void AlignRootToDoorway(
            Transform movingRoot,
            Transform movingDoorway,
            Transform fixedDoorway)
        {
            Quaternion targetDoorwayRotation = Quaternion.LookRotation(
                -fixedDoorway.forward,
                fixedDoorway.up);
            Quaternion rotationDelta = targetDoorwayRotation * Quaternion.Inverse(movingDoorway.rotation);
            movingRoot.rotation = rotationDelta * movingRoot.rotation;
            movingRoot.position += fixedDoorway.position - movingDoorway.position;
        }

        private static void ConfigureConnectedDoorway(Transform doorway)
        {
            Transform doorwayAnchor = doorway != null ? doorway.parent : null;
            if (doorwayAnchor == null)
                throw new InvalidOperationException("Connected doorway anchor is missing.");

            Transform blocker = doorwayAnchor.Find("Blocker_SM_A");
            Transform openPassage = doorwayAnchor.Find("No_Door_Placement");
            if (blocker == null || openPassage == null)
            {
                throw new InvalidOperationException(
                    $"Expected blocker/open-passage children are missing under '{doorwayAnchor.name}'.");
            }

            blocker.gameObject.SetActive(false);
            openPassage.gameObject.SetActive(true);
        }

        private static Bounds CalculateRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default;
            bool initialized = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is ParticleSystemRenderer || renderers[i] is TrailRenderer)
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

        private static float CalculateOverlapVolume(Bounds first, Bounds second)
        {
            Vector3 minimum = Vector3.Max(first.min, second.min);
            Vector3 maximum = Vector3.Min(first.max, second.max);
            Vector3 overlap = maximum - minimum;
            if (overlap.x <= 0f || overlap.y <= 0f || overlap.z <= 0f)
                return 0f;
            return overlap.x * overlap.y * overlap.z;
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder))
                return;

            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string name = Path.GetFileName(assetFolder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new InvalidOperationException($"Invalid asset folder '{assetFolder}'.");

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static void AssignLightingSettings(string lightingSettingsPath)
        {
            LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(lightingSettingsPath);
            if (settings == null)
                throw new InvalidOperationException($"Lighting settings not found: {lightingSettingsPath}");
            Lightmapping.lightingSettings = settings;
        }

        private static StartMapSceneEnvironment CaptureStartMapSceneEnvironment(Scene sourceScene)
        {
            Scene previousActiveScene = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(sourceScene);
            try
            {
                LightingSettings lightingSettings =
                    AssetDatabase.LoadAssetAtPath<LightingSettings>(StartMapLightingSettingsPath);
                if (lightingSettings == null)
                    throw new InvalidOperationException(
                        $"StartMap lighting settings not found: {StartMapLightingSettingsPath}");

                return new StartMapSceneEnvironment
                {
                    lightingSettings = lightingSettings,
                    skybox = RenderSettings.skybox,
                    ambientMode = RenderSettings.ambientMode,
                    ambientLight = RenderSettings.ambientLight,
                    ambientSkyColor = RenderSettings.ambientSkyColor,
                    ambientEquatorColor = RenderSettings.ambientEquatorColor,
                    ambientGroundColor = RenderSettings.ambientGroundColor,
                    ambientIntensity = RenderSettings.ambientIntensity,
                    fog = RenderSettings.fog,
                    fogColor = RenderSettings.fogColor,
                    fogMode = RenderSettings.fogMode,
                    fogDensity = RenderSettings.fogDensity,
                    fogStartDistance = RenderSettings.fogStartDistance,
                    fogEndDistance = RenderSettings.fogEndDistance,
                    defaultReflectionMode = RenderSettings.defaultReflectionMode,
                    defaultReflectionResolution = RenderSettings.defaultReflectionResolution,
                    reflectionBounces = RenderSettings.reflectionBounces,
                    reflectionIntensity = RenderSettings.reflectionIntensity,
                    // StartMap serializes no custom cubemap. In Unity 6 the getter can
                    // still throw when a stale non-Cubemap runtime reference exists.
                    customReflection = null,
                    subtractiveShadowColor = RenderSettings.subtractiveShadowColor
                };
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static void ApplyStartMapSceneEnvironment(StartMapSceneEnvironment environment)
        {
            Lightmapping.lightingSettings = environment.lightingSettings;
            RenderSettings.skybox = environment.skybox;
            RenderSettings.sun = null;
            RenderSettings.ambientMode = environment.ambientMode;
            RenderSettings.ambientLight = environment.ambientLight;
            RenderSettings.ambientSkyColor = environment.ambientSkyColor;
            RenderSettings.ambientEquatorColor = environment.ambientEquatorColor;
            RenderSettings.ambientGroundColor = environment.ambientGroundColor;
            RenderSettings.ambientIntensity = environment.ambientIntensity;
            RenderSettings.fog = environment.fog;
            RenderSettings.fogColor = environment.fogColor;
            RenderSettings.fogMode = environment.fogMode;
            RenderSettings.fogDensity = environment.fogDensity;
            RenderSettings.fogStartDistance = environment.fogStartDistance;
            RenderSettings.fogEndDistance = environment.fogEndDistance;
            RenderSettings.defaultReflectionMode = environment.defaultReflectionMode;
            RenderSettings.defaultReflectionResolution = environment.defaultReflectionResolution;
            RenderSettings.reflectionBounces = environment.reflectionBounces;
            RenderSettings.reflectionIntensity = environment.reflectionIntensity;
            RenderSettings.customReflection = environment.customReflection;
            RenderSettings.subtractiveShadowColor = environment.subtractiveShadowColor;
        }

        private static DungeonZoneManager CreatePreviewZoneManager(DungeonZoneManager source)
        {
            GameObject managerObject = new GameObject("StartMapDungeonEnvironmentParity");
            DungeonZoneManager target = managerObject.AddComponent<DungeonZoneManager>();
            EditorUtility.CopySerialized(source, target);

            SerializedObject serializedTarget = new SerializedObject(target);
            serializedTarget.FindProperty("exteriorDirectionalLights").arraySize = 0;
            serializedTarget.FindProperty("cloudShadowBehaviours").arraySize = 0;
            serializedTarget.ApplyModifiedPropertiesWithoutUndo();
            return target;
        }

        private static int ApplyDungeonRenderingLayer(GameObject root, string layerName)
        {
            int layerIndex = RenderingLayerMask.NameToRenderingLayer(layerName);
            if (layerIndex < 0)
                throw new InvalidOperationException($"Rendering layer '{layerName}' is missing.");

            uint mask = 1u << layerIndex;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].renderingLayerMask = mask;
            return renderers.Length;
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

        private static DungeonAdjacentLightmapExtension FindExtensionInScene(
            Scene scene,
            string doorwayPath = null)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                DungeonAdjacentLightmapExtension[] extensions =
                    roots[i].GetComponentsInChildren<DungeonAdjacentLightmapExtension>(true);
                for (int extensionIndex = 0; extensionIndex < extensions.Length; extensionIndex++)
                {
                    DungeonAdjacentLightmapExtension extension = extensions[extensionIndex];
                    if (extension == null)
                        continue;
                    if (string.IsNullOrEmpty(doorwayPath))
                        return extension;
                    if (extension.Doorway == null)
                        continue;

                    Tile tile = extension.Doorway.Tile != null
                        ? extension.Doorway.Tile
                        : extension.Doorway.GetComponentInParent<Tile>(true);
                    Transform root = tile != null ? tile.transform : roots[i].transform;
                    string candidatePath = AnimationUtility.CalculateTransformPath(
                        extension.Doorway.transform,
                        root);
                    if (string.Equals(candidatePath, doorwayPath, StringComparison.Ordinal))
                        return extension;
                }
            }
            return null;
        }

        private static bool HasBothStates(DungeonAdjacentLightmapExtension extension)
        {
            return extension != null &&
                   extension.GetState(DungeonTileLightmapSwitcher.PowerLevel.P100).IsValid &&
                   extension.GetState(DungeonTileLightmapSwitcher.PowerLevel.P0).IsValid;
        }

        private static ExtensionStatePair CopyStates(DungeonAdjacentLightmapExtension extension)
        {
            return new ExtensionStatePair
            {
                power100 = extension.GetState(DungeonTileLightmapSwitcher.PowerLevel.P100),
                power0 = extension.GetState(DungeonTileLightmapSwitcher.PowerLevel.P0)
            };
        }

        private static void ApplyStates(
            DungeonAdjacentLightmapExtension destination,
            ExtensionStatePair states)
        {
            destination.CaptureState(
                DungeonTileLightmapSwitcher.PowerLevel.P100,
                states.power100.lightmapColor,
                states.power100.lightmapDirection,
                states.power100.lightmapScaleOffset);
            destination.CaptureState(
                DungeonTileLightmapSwitcher.PowerLevel.P0,
                states.power0.lightmapColor,
                states.power0.lightmapDirection,
                states.power0.lightmapScaleOffset);
            EditorUtility.SetDirty(destination);
        }

        private static List<ReceiverCandidate> FindPairReceivers(
            GameObject room,
            DungeonAdjacentPairLightmapData pairBakeData,
            DungeonAdjacentPairLightmapData.RoomRole receiverRoom)
        {
            MeshRenderer[] renderers = room.GetComponentsInChildren<MeshRenderer>(true);
            var candidates = new List<ReceiverCandidate>();
            var pathUseCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                string path = GetPath(room.transform, renderer.transform);
                pathUseCounts.TryGetValue(path, out int bucketIndex);
                pathUseCounts[path] = bucketIndex + 1;

                if (!DungeonAdjacentRendererUtility.TryGetEligibleMesh(
                        renderer,
                        out MeshFilter filter,
                        out Mesh mesh))
                    continue;
                if (!pairBakeData.TryGetRendererStates(
                        receiverRoom,
                        path,
                        bucketIndex,
                        out DungeonAdjacentPairLightmapData.RendererStateSet states) ||
                    !states.IsComplete ||
                    states.vertexCount != mesh.vertexCount)
                    continue;

                candidates.Add(new ReceiverCandidate
                {
                    filter = filter,
                    renderer = renderer,
                    projectedVertices = mesh.vertexCount,
                    requiresTessellation = false,
                    path = path,
                    rendererBucketIndex = bucketIndex
                });
            }

            candidates.Sort((left, right) =>
            {
                int pathComparison = string.CompareOrdinal(left.path, right.path);
                return pathComparison != 0
                    ? pathComparison
                    : left.rendererBucketIndex.CompareTo(right.rendererBucketIndex);
            });
            return candidates;
        }

        private static List<ReceiverCandidate> FindReceivers(
            GameObject room,
            Transform doorway,
            DungeonAdjacentLightmapExtension neighborExtension)
        {
            MeshRenderer[] renderers = room.GetComponentsInChildren<MeshRenderer>(true);
            var candidates = new List<ReceiverCandidate>();
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                if (renderer == neighborExtension.ExtensionRenderer ||
                    renderer.GetComponentInParent<DungeonAdjacentLightmapExtension>(true) != null)
                    continue;
                if (!DungeonAdjacentRendererUtility.TryGetEligibleMesh(
                        renderer,
                        out MeshFilter filter,
                        out Mesh receiverMesh))
                    continue;

                float doorwayRightScale = doorway.TransformVector(Vector3.right).magnitude;
                float doorwayHalfWidth =
                    neighborExtension.PortalHalfWidth * doorwayRightScale;
                if (!DungeonAdjacentLightmapProjection.BoundsOverlapsPortalBand(
                        renderer.bounds,
                        doorway.position,
                        doorway.forward,
                        -1f,
                        2.75f,
                        doorwayHalfWidth,
                        1.25f))
                {
                    continue;
                }

                string path = GetPath(room.transform, renderer.transform);
                Vector4[] uv4 = new Vector4[receiverMesh.vertexCount];
                int count;
                try
                {
                    count = DungeonAdjacentLightmapProjection.BuildVertexData(
                        receiverMesh,
                        filter.transform.localToWorldMatrix,
                        doorway.position,
                        doorway.forward,
                        -1f,
                        neighborExtension.ExtensionMesh,
                        neighborExtension.ExtensionTransform.localToWorldMatrix,
                        neighborExtension.GetState(DungeonTileLightmapSwitcher.PowerLevel.P100).lightmapScaleOffset,
                        2.75f,
                        3.5f,
                        uv4,
                        1.25f);
                }
                catch
                {
                    continue;
                }

                if (count <= 0)
                    continue;

                candidates.Add(new ReceiverCandidate
                {
                    filter = filter,
                    renderer = renderer,
                    projectedVertices = count,
                    requiresTessellation = false,
                    path = path
                });
            }

            candidates.Sort((left, right) => string.CompareOrdinal(left.path, right.path));
            return candidates;
        }

        private static bool HasSupportedPreviewMaterial(MeshRenderer renderer)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && materials[i].shader != null &&
                    (materials[i].shader.name == "Universal Render Pipeline/Lit" ||
                     materials[i].shader.name == "GabroMedia/ColorChange"))
                    return true;
            }
            return false;
        }

        private static int ConfigurePairReceiver(
            ReceiverCandidate candidate,
            Transform doorway,
            DungeonAdjacentLightmapExtension neighborExtension,
            DungeonTileLightmapSwitcher receiverLighting,
            DungeonTileLightmapSwitcher neighborLighting,
            DungeonAdjacentPairLightmapData pairBakeData,
            DungeonAdjacentPairLightmapData.RoomRole receiverRoom,
            string label)
        {
            ReceiverCandidate overlay = CreatePairOverlay(candidate, label);
            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(overlay.renderer.gameObject);
            GameObjectUtility.SetStaticEditorFlags(
                overlay.renderer.gameObject,
                flags & ~StaticEditorFlags.BatchingStatic);

            DungeonAdjacentLightmapReceiver receiver =
                overlay.renderer.gameObject.AddComponent<DungeonAdjacentLightmapReceiver>();
            receiver.ConfigurePairBake(
                null,
                doorway,
                overlay.filter,
                overlay.renderer,
                neighborExtension,
                receiverLighting,
                neighborLighting,
                pairBakeData,
                receiverRoom,
                candidate.path,
                candidate.rendererBucketIndex);
            EditorUtility.SetDirty(receiver);
            return overlay.filter.sharedMesh.vertexCount;
        }

        private static int ConfigureExtensionOverlayReceiver(
            ReceiverCandidate candidate,
            Transform doorway,
            DungeonAdjacentLightmapExtension neighborExtension,
            DungeonTileLightmapSwitcher neighborLighting,
            string label)
        {
            ReceiverCandidate overlay = CreatePairOverlay(candidate, label);
            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(overlay.renderer.gameObject);
            GameObjectUtility.SetStaticEditorFlags(
                overlay.renderer.gameObject,
                flags & ~StaticEditorFlags.BatchingStatic);

            DungeonAdjacentLightmapReceiver receiver =
                overlay.renderer.gameObject.AddComponent<DungeonAdjacentLightmapReceiver>();
            receiver.Configure(
                null,
                doorway,
                overlay.filter,
                overlay.renderer,
                neighborExtension,
                neighborLighting);
            EditorUtility.SetDirty(receiver);
            return candidate.projectedVertices;
        }

        private static ReceiverCandidate CreatePairOverlay(
            ReceiverCandidate source,
            string label)
        {
            GameObject overlayObject = new GameObject($"{source.renderer.name}_AdjacentConnectionOverlay");
            overlayObject.layer = source.renderer.gameObject.layer;
            overlayObject.transform.SetParent(source.renderer.transform, false);

            MeshFilter overlayFilter = overlayObject.AddComponent<MeshFilter>();
            overlayFilter.sharedMesh = source.filter.sharedMesh;

            MeshRenderer overlayRenderer = overlayObject.AddComponent<MeshRenderer>();
            overlayRenderer.sharedMaterials = CreateOverlayMaterials(
                source.renderer.sharedMaterials,
                label);
            overlayRenderer.enabled = source.renderer.enabled;
            overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
            overlayRenderer.receiveShadows = false;
            overlayRenderer.lightProbeUsage = LightProbeUsage.Off;
            overlayRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            overlayRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            overlayRenderer.renderingLayerMask = source.renderer.renderingLayerMask;
            overlayRenderer.lightmapIndex = source.renderer.lightmapIndex;
            overlayRenderer.lightmapScaleOffset = source.renderer.lightmapScaleOffset;
            overlayRenderer.realtimeLightmapIndex = source.renderer.realtimeLightmapIndex;
            overlayRenderer.realtimeLightmapScaleOffset = source.renderer.realtimeLightmapScaleOffset;

            return new ReceiverCandidate
            {
                filter = overlayFilter,
                renderer = overlayRenderer,
                projectedVertices = source.projectedVertices,
                requiresTessellation = false,
                path = source.path,
                rendererBucketIndex = source.rendererBucketIndex
            };
        }

        private static Material[] CreateOverlayMaterials(Material[] sources, string label)
        {
            EnsureFolder(PreviewMaterialFolder);
            Shader shader = Shader.Find(
                "StillWorking/Experiments/Dungeon Adjacent Connection Light Overlay");
            if (shader == null)
                throw new InvalidOperationException("Adjacent pair overlay shader was not found.");

            Material[] overlays = new Material[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                Material source = sources[i];
                if (source == null || source.shader == null)
                {
                    overlays[i] = null;
                    continue;
                }

                string path = $"{PreviewMaterialFolder}/{label}_{i}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
                    AssetDatabase.DeleteAsset(path);

                Material overlay = new Material(shader) { name = $"{label}_{i}" };
                overlay.CopyPropertiesFromMaterial(source);
                overlay.shader = shader;
                CopyCommonOverlayProperties(source, overlay);
                if (source.shader.name == "GabroMedia/ColorChange")
                    CopyColorChangeProperties(source, overlay);

                overlay.SetFloat("_AdjacentOverlayOnly", 1f);
                overlay.renderQueue = (int)RenderQueue.Transparent;

                AssetDatabase.CreateAsset(overlay, path);
                overlays[i] = overlay;
            }

            return overlays;
        }

        private static int ConfigureReceiver(
            ReceiverCandidate candidate,
            Transform doorway,
            DungeonAdjacentLightmapExtension neighborExtension,
            DungeonTileLightmapSwitcher neighborLighting,
            string label)
        {
            Mesh receiverMesh = candidate.filter.sharedMesh;
            if (candidate.requiresTessellation)
            {
                receiverMesh = CreateTessellatedReceiverMesh(receiverMesh, label);
                candidate.filter.sharedMesh = receiverMesh;
            }
            AssignPreviewMaterials(candidate.renderer, label);
            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(candidate.renderer.gameObject);
            GameObjectUtility.SetStaticEditorFlags(
                candidate.renderer.gameObject,
                flags & ~StaticEditorFlags.BatchingStatic);

            DungeonAdjacentLightmapReceiver receiver =
                candidate.renderer.gameObject.AddComponent<DungeonAdjacentLightmapReceiver>();
            receiver.Configure(
                null,
                doorway,
                candidate.filter,
                candidate.renderer,
                neighborExtension,
                neighborLighting);
            EditorUtility.SetDirty(receiver);

            DungeonAdjacentLightmapExtension.BakedState state = neighborExtension.GetState(
                DungeonTileLightmapSwitcher.PowerLevel.P100);
            var uv4 = new Vector4[receiverMesh.vertexCount];
            return DungeonAdjacentLightmapProjection.BuildVertexData(
                receiverMesh,
                candidate.filter.transform.localToWorldMatrix,
                doorway.position,
                doorway.forward,
                -1f,
                neighborExtension.ExtensionMesh,
                neighborExtension.ExtensionTransform.localToWorldMatrix,
                state.lightmapScaleOffset,
                2.75f,
                3.5f,
                uv4,
                1.25f);
        }

        private static Mesh CreateTessellatedReceiverMesh(Mesh source, string label)
        {
            EnsureFolder(PreviewReceiverMeshFolder);
            string path = $"{PreviewReceiverMeshFolder}/{label}_Tessellated.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
                AssetDatabase.DeleteAsset(path);

            Mesh tessellated = DungeonAdjacentLightingPoCReceiverMeshTessellator.Tessellate(
                source,
                16,
                $"{label}_Tessellated");
            AssetDatabase.CreateAsset(tessellated, path);
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        private static void AssignPreviewMaterials(MeshRenderer renderer, string label)
        {
            EnsureFolder(PreviewMaterialFolder);
            Shader shader = Shader.Find("StillWorking/Experiments/Dungeon Adjacent Lightmap Lit");
            if (shader == null)
                throw new InvalidOperationException("Adjacent lightmap shader was not found.");

            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material source = materials[i];
                if (source == null || source.shader == null ||
                    (source.shader.name != "Universal Render Pipeline/Lit" &&
                     source.shader.name != "GabroMedia/ColorChange"))
                    continue;

                string path = $"{PreviewMaterialFolder}/{label}_{i}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
                    AssetDatabase.DeleteAsset(path);
                Material replacement = new Material(shader) { name = $"{label}_{i}" };
                replacement.CopyPropertiesFromMaterial(source);
                replacement.shader = shader;
                if (source.shader.name == "GabroMedia/ColorChange")
                    CopyColorChangeProperties(source, replacement);
                AssetDatabase.CreateAsset(replacement, path);
                materials[i] = replacement;
            }
            renderer.sharedMaterials = materials;
        }

        private static void CopyCommonOverlayProperties(Material source, Material destination)
        {
            CopyFirstTexture(
                source,
                destination,
                "_BaseMap",
                "_BaseMap",
                "_Basecolor",
                "_MainTex",
                "_Albedo_OpacityA",
                "_AlbedoMap",
                "_BaseColorMap",
                "_DiffuseMap");

            if (TryGetFirstColor(
                    source,
                    out Color baseColor,
                    "_BaseColor",
                    "_Color",
                    "_Tint",
                    "_Decal_Color",
                    "_AlbedoTint",
                    "_Custom_Color"))
            {
                destination.SetColor("_BaseColor", baseColor);
            }

            if (CopyFirstTexture(
                    source,
                    destination,
                    "_BumpMap",
                    "_BumpMap",
                    "_Normal",
                    "_NormalMap"))
            {
                destination.EnableKeyword("_NORMALMAP");
            }

            if (CopyFirstTexture(
                    source,
                    destination,
                    "_OcclusionMap",
                    "_OcclusionMap",
                    "_AO_Map"))
            {
                destination.EnableKeyword("_OCCLUSIONMAP");
            }

            if (source.HasProperty("_Metallic"))
                destination.SetFloat("_Metallic", source.GetFloat("_Metallic"));
            if (source.HasProperty("_Smoothness"))
                destination.SetFloat("_Smoothness", source.GetFloat("_Smoothness"));
            if (source.HasProperty("_OcclusionStrength"))
                destination.SetFloat("_OcclusionStrength", source.GetFloat("_OcclusionStrength"));
            if (source.HasProperty("_BumpScale"))
                destination.SetFloat("_BumpScale", source.GetFloat("_BumpScale"));
            else if (source.HasProperty("_NormalScale"))
                destination.SetFloat("_BumpScale", source.GetFloat("_NormalScale"));

            if (source.HasProperty("_Cull"))
                destination.SetFloat("_Cull", source.GetFloat("_Cull"));
            else if (source.HasProperty("_CullMode"))
                destination.SetFloat("_Cull", source.GetFloat("_CullMode"));
        }

        private static bool CopyFirstTexture(
            Material source,
            Material destination,
            string destinationProperty,
            params string[] sourceProperties)
        {
            for (int i = 0; i < sourceProperties.Length; i++)
            {
                string sourceProperty = sourceProperties[i];
                if (!source.HasProperty(sourceProperty))
                    continue;

                Texture texture = source.GetTexture(sourceProperty);
                if (texture == null)
                    continue;

                destination.SetTexture(destinationProperty, texture);
                destination.SetTextureScale(
                    destinationProperty,
                    source.GetTextureScale(sourceProperty));
                destination.SetTextureOffset(
                    destinationProperty,
                    source.GetTextureOffset(sourceProperty));
                return true;
            }

            return false;
        }

        private static bool TryGetFirstColor(
            Material source,
            out Color color,
            params string[] sourceProperties)
        {
            for (int i = 0; i < sourceProperties.Length; i++)
            {
                if (!source.HasProperty(sourceProperties[i]))
                    continue;

                color = source.GetColor(sourceProperties[i]);
                return true;
            }

            color = Color.white;
            return false;
        }

        private static void CopyColorChangeProperties(Material source, Material destination)
        {
            if (source.HasProperty("_Basecolor"))
                destination.SetTexture("_BaseMap", source.GetTexture("_Basecolor"));
            if (source.HasProperty("_ColorMask"))
                destination.SetTexture("_ColorMask", source.GetTexture("_ColorMask"));
            if (source.HasProperty("_Normal"))
            {
                destination.SetTexture("_BumpMap", source.GetTexture("_Normal"));
                destination.EnableKeyword("_NORMALMAP");
            }
            if (source.HasProperty("_MetallicSmoothness"))
            {
                destination.SetTexture("_MetallicGlossMap", source.GetTexture("_MetallicSmoothness"));
                destination.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            if (source.HasProperty("_AO_Map"))
            {
                destination.SetTexture("_OcclusionMap", source.GetTexture("_AO_Map"));
                destination.EnableKeyword("_OCCLUSIONMAP");
            }

            destination.SetFloat("_UseGabroColorChange", 1f);
            if (source.HasProperty("_Custom_Color"))
                destination.SetColor("_Custom_Color", source.GetColor("_Custom_Color"));
            if (source.HasProperty("_ColorChange"))
                destination.SetFloat("_ColorChange", source.GetFloat("_ColorChange"));
            if (source.HasProperty("_HueShiftOnly"))
                destination.SetFloat("_HueShiftOnly", source.GetFloat("_HueShiftOnly"));
            if (source.HasProperty("_HueShift"))
                destination.SetFloat("_HueShift", source.GetFloat("_HueShift"));

            // The Gabro shader reads metallic and smoothness directly from the
            // texture channels. URP Lit multiplies them by these scalar values.
            destination.SetFloat("_Metallic", 1f);
            destination.SetFloat("_Smoothness", 1f);
            destination.SetFloat("_OcclusionStrength", 1f);
            destination.SetFloat("_BumpScale", 1f);
            destination.SetColor("_BaseColor", Color.white);
        }

        private static PreviewCameraEnvironment CreatePreviewCamera(
            Transform doorway,
            DungeonTileLightmapSwitcher startLighting,
            DungeonTileLightmapSwitcher adminLighting,
            DungeonZoneManager previewZoneManager)
        {
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null)
                throw new InvalidOperationException($"Player prefab not found: {PlayerPrefabPath}");
            Camera sourceCamera = playerPrefab.GetComponentInChildren<Camera>(true);
            Volume sourceVolume = sourceCamera != null ? sourceCamera.GetComponent<Volume>() : null;
            Light sourceDungeonLight = sourceCamera != null
                ? sourceCamera.GetComponentInChildren<Light>(true)
                : null;
            if (sourceCamera == null || sourceVolume == null || sourceDungeonLight == null)
                throw new InvalidOperationException(
                    "Cyber_Generic camera, dungeon Volume, or dungeon-only Light is missing.");

            GameObject playerObject = new GameObject("AdjacentLightmapPreviewPlayer");
            CharacterController characterController = playerObject.AddComponent<CharacterController>();
            characterController.height = 1.8f;
            characterController.radius = 0.3f;
            characterController.center = new Vector3(0f, 0.9f, 0f);
            characterController.skinWidth = 0.03f;
            DungeonAdjacentLightingPoCPreviewController previewController =
                playerObject.AddComponent<DungeonAdjacentLightingPoCPreviewController>();
            previewController.Configure(startLighting, adminLighting);
            DungeonAdjacentLightingPoCFirstPersonController movement =
                playerObject.AddComponent<DungeonAdjacentLightingPoCFirstPersonController>();

            playerObject.transform.position =
                doorway.position - doorway.forward * 3f + doorway.up * 0.05f;
            Vector3 flatLookDirection = Vector3.ProjectOnPlane(doorway.forward, doorway.up).normalized;
            playerObject.transform.rotation = Quaternion.LookRotation(flatLookDirection, doorway.up);

            GameObject cameraObject = new GameObject("AdjacentLightmapPreviewCamera");
            cameraObject.transform.SetParent(playerObject.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.62f, 0f);
            cameraObject.transform.localRotation = Quaternion.identity;
            Camera camera = cameraObject.AddComponent<Camera>();
            EditorUtility.CopySerialized(sourceCamera, camera);
            cameraObject.AddComponent<AudioListener>();
            movement.Configure(camera);
            cameraObject.tag = "MainCamera";

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

            GameObject lightObject = new GameObject(sourceDungeonLight.name);
            lightObject.transform.SetParent(cameraObject.transform, false);
            lightObject.transform.localPosition = sourceDungeonLight.transform.localPosition;
            lightObject.transform.localRotation = sourceDungeonLight.transform.localRotation;
            lightObject.transform.localScale = sourceDungeonLight.transform.localScale;
            Light dungeonOnlyLight = lightObject.AddComponent<Light>();
            EditorUtility.CopySerialized(sourceDungeonLight, dungeonOnlyLight);
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

            DungeonAdjacentLightingPoCStartMapEnvironmentController environmentController =
                playerObject.AddComponent<DungeonAdjacentLightingPoCStartMapEnvironmentController>();
            environmentController.Configure(previewZoneManager, volume, dungeonOnlyLight);

            return new PreviewCameraEnvironment
            {
                volume = volume,
                dungeonOnlyLight = dungeonOnlyLight
            };
        }

        private static string GetPath(Transform root, Transform target)
        {
            return AnimationUtility.CalculateTransformPath(target, root);
        }

        private static string Sanitize(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value.Replace('/', '_').Replace('\\', '_');
        }

        private struct PairSelection
        {
            public string startDoorwayPath;
            public string adminDoorwayPath;
            public float overlapVolume;
            public bool IsValid => !string.IsNullOrEmpty(startDoorwayPath) && !string.IsNullOrEmpty(adminDoorwayPath);
        }

        private struct ExtensionStatePair
        {
            public DungeonAdjacentLightmapExtension.BakedState power100;
            public DungeonAdjacentLightmapExtension.BakedState power0;
        }

        private struct ReceiverCandidate
        {
            public MeshFilter filter;
            public MeshRenderer renderer;
            public int projectedVertices;
            public bool requiresTessellation;
            public string path;
            public int rendererBucketIndex;
        }

        private struct PreviewCameraEnvironment
        {
            public Volume volume;
            public Light dungeonOnlyLight;
        }

        private struct StartMapSceneEnvironment
        {
            public LightingSettings lightingSettings;
            public Material skybox;
            public AmbientMode ambientMode;
            public Color ambientLight;
            public Color ambientSkyColor;
            public Color ambientEquatorColor;
            public Color ambientGroundColor;
            public float ambientIntensity;
            public bool fog;
            public Color fogColor;
            public FogMode fogMode;
            public float fogDensity;
            public float fogStartDistance;
            public float fogEndDistance;
            public DefaultReflectionMode defaultReflectionMode;
            public int defaultReflectionResolution;
            public int reflectionBounces;
            public float reflectionIntensity;
            public Cubemap customReflection;
            public Color subtractiveShadowColor;
        }
    }
}
