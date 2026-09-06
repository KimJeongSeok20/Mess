using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DungeonPortalTransportPoC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DungeonPortalTransportPoC.GroundTruth.KExactProductionLights
{
    /// <summary>
    /// Direct-only diagnostic which keeps the exact cloned production Light
    /// components whose geometric influence reaches a deterministic doorway grid.
    /// This is a K-selection pilot, not a baked/realtime or visual-parity verdict.
    /// </summary>
    public static class DungeonPortalKExactProductionLightsCapture
    {
        public const string Status = "K_EXACT_PRODUCTION_LIGHTS_DIRECT_PILOT_ONLY";

        private const string ToolSourcePath =
            "Assets/Experiments/DungeonPortalTransportPoC/GroundTruth/" +
            "KExactProductionLights/Editor/" +
            "DungeonPortalKExactProductionLightsCapture.cs";
        private const string ValidationScenePath =
            "Assets/Experiments/DungeonPortalTransportPoC/Scenes/" +
            "Start_Admin_PortalTransportValidation.unity";
        private const string ReferenceManifestPath =
            "Assets/Experiments/DungeonPortalTransportPoC/Evidence/GroundTruth/" +
            "RealtimeProductionState/20260823T172008115Z/" +
            "manifest_REALTIME_DIRECT_POWER_EMISSION_GT_ONLY.txt";
        private const string ExpectedReferenceManifestSha256 =
            "46DF14C9318C53FB0180D0F05F086DEF08E1B8993970E2642B1A9EDEB6941138";
        private const string EvidenceRoot =
            "Assets/Experiments/DungeonPortalTransportPoC/GroundTruth/" +
            "KExactProductionLights/Evidence";
        private const string ExpectedValidationSceneSha256 =
            "F087DD274822D9F8071CE8ADD0E261BC5314B2999EF4BDF3B1C1CB1B5DC4AF79";

        private const string ValidationRootName = "PortalTransportValidation";
        private const string ProductionRoomsRootName = "01_ProductionRooms";
        private const string PortalTransportRootName = "02_PortalTransportPoC";
        private const string CamerasRootName = "03_FixedValidationCameras_DISABLED";
        private const string StartRoomName = "StartRoom_R000_ProductionInstance";
        private const string AdministrativeRoomName =
            "AdminstrativeSegregation_R000_ProductionInstance";
        private const string AddedDoorRelativePath =
            "Doorways/Door_SM_A/DoorWayPoint/" +
            "Door_SM_A_Door_Placement_ActiveSceneInstance";
        private const string DoorLeafName = "Door_01";
        private const string StartCameraName = "Start_to_Admin_FixedCamera_DISABLED";
        private const string AdministrativeCameraName =
            "Admin_to_Start_FixedCamera_DISABLED";
        private const string OwnedCloneRootName =
            ValidationRootName + "__KExactProductionLightsClone";

        private const int ExpectedValidationRootChildren = 4;
        private const int ExpectedRendererCount = 389;
        private const int ExpectedStartLightCount = 24;
        private const int ExpectedAdministrativeLightCount = 54;
        private const int ExpectedTotalLightCount = 78;
        private const int ExpectedStartEmissionEntryCount = 6;
        private const int ExpectedAdministrativeEmissionEntryCount = 48;
        private const int ExpectedDoorRendererCount = 4;
        private const int ExpectedDoorEnabledRendererCount = 3;
        private const int ExpectedCameraCount = 2;
        private const int ApertureColumns = 9;
        private const int ApertureRows = 13;
        private const float EndpointApertureAlignmentTolerance = 0.01f;
        private const float EndpointNormalAlignmentMinimum = 0.9999f;
        private const int PilotGameObjectLayer = 0;
        private const uint PilotRenderingLayerBit = 1u;
        private const int CaptureWidth = 1920;
        private const int CaptureHeight = 1080;
        private const double MinimumValidMeanLinearLuminance = 1e-12d;
        private const float MinimumValidMaxLinearLuminance = 1e-8f;
        private const double MinimumPoweredMeanDelta = 1e-9d;

        private static readonly PowerState[] PowerStates =
        {
            new PowerState("P000_P000", false, false),
            new PowerState("P100_P000", true, false),
            new PowerState("P000_P100", false, true),
            new PowerState("P100_P100", true, true)
        };

        private static readonly DoorPose[] DoorPoses =
        {
            new DoorPose(0),
            new DoorPose(25),
            new DoorPose(50),
            new DoorPose(75),
            new DoorPose(100)
        };

        private static readonly string[] CameraNames =
        {
            StartCameraName,
            AdministrativeCameraName
        };

        [MenuItem(
            "Tools/Dungeon/Lighting/Portal Transport PoC/" +
            "Capture K-Exact Production Lights Direct Pilot (Edit Mode)")]
        public static void CaptureFromMenu()
        {
            Debug.Log(CaptureAll());
        }

        public static string CaptureAll()
        {
            try
            {
                return CaptureAllOrThrow();
            }
            catch (Exception exception)
            {
                return "FAIL " + Status + " capture: " + exception;
            }
        }

        private static string CaptureAllOrThrow()
        {
            Scene sourceScene = ValidateEditorPreconditions();
            GameObject sourceRoot = FindUniqueRoot(sourceScene, ValidationRootName);
            ValidateRootShape(sourceRoot, true);

            SourceSnapshot sourceSnapshot = SourceSnapshot.Capture(sourceScene, sourceRoot);
            SelectionSnapshot selectionSnapshot = SelectionSnapshot.Capture();
            GlobalRenderSnapshot globalSnapshot = GlobalRenderSnapshot.Capture();
            CaptureIdentity identity = CaptureIdentity.Capture();
            string timestamp = DateTime.UtcNow.ToString(
                "yyyyMMdd'T'HHmmssfff'Z'",
                CultureInfo.InvariantCulture);
            string outputFolder = EvidenceRoot + "/" + timestamp;

            Scene previewScene = default;
            GameObject inactiveStaging = null;
            GameObject previewRoot = null;
            PreviewContract contract = null;
            DetachedReport report = null;
            RenderTargets targets = null;
            RenderFailureMonitor failureMonitor = null;
            var captures = new List<CaptureEvidence>(
                PowerStates.Length * DoorPoses.Length * CameraNames.Length);
            bool restorationCompleted = false;

            try
            {
                failureMonitor = new RenderFailureMonitor();
                previewScene = EditorSceneManager.NewPreviewScene();
                if (!previewScene.IsValid() || !previewScene.isLoaded ||
                    !EditorSceneManager.IsPreviewScene(previewScene))
                {
                    throw new InvalidOperationException(
                        "Unity did not create an isolated preview scene.");
                }

                inactiveStaging = new GameObject("__KExact_InactiveCloneStaging")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                inactiveStaging.SetActive(false);
                SceneManager.MoveGameObjectToScene(inactiveStaging, previewScene);

                previewRoot = Object.Instantiate(
                    sourceRoot,
                    inactiveStaging.transform,
                    false);
                previewRoot.name = OwnedCloneRootName;
                previewRoot.hideFlags = HideFlags.HideAndDontSave;
                if (sourceScene.isDirty)
                    throw new InvalidOperationException("Deep clone dirtied the source scene.");

                contract = PreviewContract.CreateInactive(previewRoot, previewScene);
                contract.PrepareInactiveClone();
                globalSnapshot.ApplyNoLightmaps();

                previewRoot.SetActive(false);
                previewRoot.transform.SetParent(null, true);
                SceneManager.MoveGameObjectToScene(previewRoot, previewScene);
                Object.DestroyImmediate(inactiveStaging);
                inactiveStaging = null;
                previewRoot.SetActive(true);
                contract.AssertActivatedPreparedClone();

                targets = RenderTargets.Create();
                for (int powerIndex = 0; powerIndex < PowerStates.Length; powerIndex++)
                {
                    PowerState power = PowerStates[powerIndex];
                    contract.Emissions.Apply(power);
                    contract.Lights.ApplyPower(power);

                    for (int poseIndex = 0; poseIndex < DoorPoses.Length; poseIndex++)
                    {
                        DoorPose pose = DoorPoses[poseIndex];
                        contract.Door.ApplyPose(pose);
                        contract.AssertState(power, pose);

                        for (int cameraIndex = 0;
                             cameraIndex < contract.Cameras.Length;
                             cameraIndex++)
                        {
                            captures.Add(CaptureCameraPair(
                                contract.Cameras[cameraIndex],
                                previewScene,
                                power,
                                pose,
                                targets,
                                failureMonitor));
                            contract.AssertState(power, pose);
                        }
                    }
                }

                int expected = PowerStates.Length * DoorPoses.Length * CameraNames.Length;
                if (captures.Count != expected)
                {
                    throw new InvalidOperationException(
                        $"Expected {expected} captures; got {captures.Count}.");
                }
                contract.AssertExactAssetsAndStructure();
                failureMonitor.ThrowIfFailed("capture completion");
                report = DetachedReport.Capture(contract);
            }
            finally
            {
                if (failureMonitor != null)
                    failureMonitor.Dispose();
                try
                {
                    if (targets != null)
                        targets.Dispose();
                }
                finally
                {
                    try
                    {
                        globalSnapshot.Restore();
                    }
                    finally
                    {
                        try
                        {
                            if (inactiveStaging != null)
                                Object.DestroyImmediate(inactiveStaging);
                            if (previewRoot != null)
                                Object.DestroyImmediate(previewRoot);
                            if (previewScene.IsValid() && previewScene.isLoaded)
                                EditorSceneManager.ClosePreviewScene(previewScene);
                        }
                        finally
                        {
                            selectionSnapshot.RestoreIfChanged();
                            restorationCompleted = true;
                        }
                    }
                }
            }

            if (!restorationCompleted || report == null)
                throw new InvalidOperationException("Capture cleanup/report did not complete.");
            AssertSourceEditorState(sourceScene);
            globalSnapshot.AssertRestored();
            selectionSnapshot.AssertRestored();
            sourceSnapshot.AssertUnchanged();
            ValidateAndAttachBaselines(captures);
            ValidateCaptureDiversityAndPoweredSignal(captures);

            string manifest = BuildManifest(timestamp, identity, report, captures);
            WriteEvidenceAtomically(outputFolder, captures, manifest);

            AssertSourceEditorState(sourceScene);
            globalSnapshot.AssertRestored();
            selectionSnapshot.AssertRestored();
            sourceSnapshot.AssertUnchanged();

            return Status + "\n" +
                   "output=" + outputFolder + "\n" +
                   "selectedStartK=" + report.SelectedStartCount + "\n" +
                   "selectedAdministrativeK=" + report.SelectedAdministrativeCount + "\n" +
                   "stateCameraRecords=" + captures.Count + "\n" +
                   "visualFitClaimed=false\n" +
                   "acceptance=BLOCKED_PENDING_PIXEL_COMPARISON";
        }

        private static Scene ValidateEditorPreconditions()
        {
            if (Application.isPlaying || EditorApplication.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stable clean Edit Mode is required.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Unity is compiling or updating.");
            if (Lightmapping.isRunning)
                throw new InvalidOperationException("Lightmapping is running.");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Close Prefab Stage before capture.");
            if (SceneManager.sceneCount != 1 || CountLoadedNonPreviewScenes() != 1)
                throw new InvalidOperationException("Exactly one non-preview scene is required.");

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.isDirty ||
                !string.Equals(
                    NormalizePath(scene.path),
                    ValidationScenePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The exact clean portal validation scene must be active.");
            }
            string actualSha = ComputeFileSha256(ValidationScenePath);
            if (!string.Equals(
                    actualSha,
                    ExpectedValidationSceneSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Validation scene raw SHA-256 changed: " + actualSha + ".");
            }
            ValidateReferenceManifest();
            AssertNoOwnedCloneArtifacts();
            return scene;
        }

        private static void ValidateReferenceManifest()
        {
            string absolute = AssetPathToAbsolutePath(ReferenceManifestPath);
            if (!File.Exists(absolute))
                throw new FileNotFoundException("Reference manifest is missing.", absolute);
            string text = File.ReadAllText(absolute);
            string actualSha = ComputeFileSha256(ReferenceManifestPath);
            if (!string.Equals(
                    actualSha,
                    ExpectedReferenceManifestSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                text.IndexOf(
                    "status=REALTIME_DIRECT_POWER_EMISSION_GT_ONLY",
                    StringComparison.Ordinal) < 0 ||
                text.IndexOf("stateCameraRecordCount=40", StringComparison.Ordinal) < 0 ||
                text.IndexOf("presentationPngCount=40", StringComparison.Ordinal) < 0 ||
                text.IndexOf("linearHdrExrCount=40", StringComparison.Ordinal) < 0 ||
                text.IndexOf(
                    "validationSceneActualSha256=" + ExpectedValidationSceneSha256,
                    StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException(
                    "Reference manifest is not the accepted complete production-state run.");
            }
        }

        private static void AssertSourceEditorState(Scene expected)
        {
            if (SceneManager.GetActiveScene() != expected || expected.isDirty ||
                SceneManager.sceneCount != 1 || CountLoadedNonPreviewScenes() != 1)
            {
                throw new InvalidOperationException("Source editor scene state changed.");
            }
            AssertNoOwnedCloneArtifacts();
        }

        private static int CountLoadedNonPreviewScenes()
        {
            int count = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene))
                    count++;
            }
            return count;
        }

        private static void AssertNoOwnedCloneArtifacts()
        {
            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < all.Length; i++)
            {
                GameObject candidate = all[i];
                if (candidate != null &&
                    (string.Equals(candidate.name, OwnedCloneRootName, StringComparison.Ordinal) ||
                     string.Equals(
                         candidate.name,
                         "__KExact_InactiveCloneStaging",
                         StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        "A stale K-exact clone/staging object exists: " + candidate.name + ".");
                }
            }
        }

        private static GameObject FindUniqueRoot(Scene scene, string name)
        {
            GameObject match = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (!string.Equals(roots[i].name, name, StringComparison.Ordinal))
                    continue;
                if (match != null)
                    throw new InvalidOperationException("Duplicate root: " + name);
                match = roots[i];
            }
            if (match == null)
                throw new InvalidOperationException("Missing root: " + name);
            return match;
        }

        private static Transform FindUniqueDescendant(
            Transform root,
            string name,
            bool directChild)
        {
            Transform match = null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform candidate = all[i];
                if (candidate == root ||
                    !string.Equals(candidate.name, name, StringComparison.Ordinal) ||
                    (directChild && candidate.parent != root))
                    continue;
                if (match != null)
                    throw new InvalidOperationException("Duplicate transform: " + name);
                match = candidate;
            }
            if (match == null)
                throw new InvalidOperationException("Missing transform: " + name);
            return match;
        }

        private static void ValidateRootShape(GameObject root, bool requireSceneRoot)
        {
            if (root == null || !root.activeSelf ||
                (requireSceneRoot && root.transform.parent != null) ||
                root.transform.childCount != ExpectedValidationRootChildren)
                throw new InvalidOperationException("Validation root shape changed.");
            FindUniqueDescendant(root.transform, ProductionRoomsRootName, true);
            FindUniqueDescendant(root.transform, PortalTransportRootName, true);
            FindUniqueDescendant(root.transform, CamerasRootName, true);
            FindUniqueDescendant(
                root.transform,
                "04_ValidationGlobalVolume_StartMapParity",
                true);
        }

        private static Camera FindUniqueCamera(Transform root, string name)
        {
            Transform transform = FindUniqueDescendant(root, name, true);
            Camera camera = transform.GetComponent<Camera>();
            if (camera == null)
                throw new InvalidOperationException("Missing Camera on " + name + ".");
            return camera;
        }

        private static UniversalAdditionalCameraData RequireUrpCameraData(Camera camera)
        {
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (data == null || data.renderType != CameraRenderType.Base)
                throw new InvalidOperationException("Fixed Camera lacks Base URP data.");
            return data;
        }

        private static void ValidateFixedCameraKnownProperties(Camera camera, int index)
        {
            string expectedName = index == 0
                ? StartCameraName
                : index == 1
                    ? AdministrativeCameraName
                    : throw new ArgumentOutOfRangeException(nameof(index));
            Vector3 expectedPosition = index == 0
                ? new Vector3(2.9999993f, 1.6700006f, 5.196489f)
                : new Vector3(-3.0000012f, 1.6700006f, 5.1964884f);
            Quaternion expectedRotation = index == 0
                ? new Quaternion(0f, 0.7071068f, 0f, -0.7071068f)
                : new Quaternion(0f, 0.7071068f, 0f, 0.7071068f);

            if (camera == null || camera.name != expectedName || camera.enabled ||
                camera.targetTexture != null || camera.orthographic ||
                camera.usePhysicalProperties ||
                Vector3.Distance(camera.transform.position, expectedPosition) > 0.00001f ||
                Quaternion.Angle(camera.transform.rotation, expectedRotation) > 0.001f ||
                Vector3.Distance(camera.transform.localScale, Vector3.one) > 0.00001f ||
                !Mathf.Approximately(camera.fieldOfView, 90f) ||
                Mathf.Abs(camera.nearClipPlane - 0.01f) > 0.000001f ||
                !Mathf.Approximately(camera.farClipPlane, 1000f) ||
                camera.cullingMask != 262135)
            {
                throw new InvalidOperationException(
                    "Fixed Camera exact contract changed: " + expectedName + ".");
            }
            RequireUrpCameraData(camera);
        }

        private sealed class PreviewContract
        {
            public readonly GameObject Root;
            public readonly GameObject StartRoom;
            public readonly GameObject AdministrativeRoom;
            public readonly GameObject PortalRoot;
            public readonly Camera[] Cameras;
            public readonly DoorContract Door;
            public readonly RendererContract Renderers;
            public readonly EmissionController Emissions;
            public readonly ExactLightController Lights;
            public readonly ApertureGrid Aperture;
            private readonly DungeonTileLightmapSwitcher[] switchers;
            private readonly Scene previewScene;
            private int removedReflectionProbeCount;

            private PreviewContract(
                Scene previewScene,
                GameObject root,
                GameObject startRoom,
                GameObject administrativeRoom,
                GameObject portalRoot,
                Camera[] cameras,
                DoorContract door,
                RendererContract renderers,
                EmissionController emissions,
                ExactLightController lights,
                ApertureGrid aperture,
                DungeonTileLightmapSwitcher[] switchers)
            {
                this.previewScene = previewScene;
                Root = root;
                StartRoom = startRoom;
                AdministrativeRoom = administrativeRoom;
                PortalRoot = portalRoot;
                Cameras = cameras;
                Door = door;
                Renderers = renderers;
                Emissions = emissions;
                Lights = lights;
                Aperture = aperture;
                this.switchers = switchers;
            }

            public static PreviewContract CreateInactive(GameObject root, Scene previewScene)
            {
                ValidateRootShape(root, false);
                if (root.activeInHierarchy || root.gameObject.scene != previewScene)
                    throw new InvalidOperationException("Clone is not inactive/isolated.");

                Transform rooms = FindUniqueDescendant(
                    root.transform,
                    ProductionRoomsRootName,
                    true);
                if (rooms.childCount != 2)
                    throw new InvalidOperationException("Production-room root shape changed.");
                GameObject start = FindUniqueDescendant(
                    rooms,
                    StartRoomName,
                    true).gameObject;
                GameObject administrative = FindUniqueDescendant(
                    rooms,
                    AdministrativeRoomName,
                    true).gameObject;
                GameObject portal = FindUniqueDescendant(
                    root.transform,
                    PortalTransportRootName,
                    true).gameObject;
                Transform cameraRoot = FindUniqueDescendant(
                    root.transform,
                    CamerasRootName,
                    true);

                var cameras = new Camera[CameraNames.Length];
                for (int i = 0; i < cameras.Length; i++)
                {
                    Camera camera = FindUniqueCamera(cameraRoot, CameraNames[i]);
                    ValidateFixedCameraKnownProperties(camera, i);
                    camera.scene = previewScene;
                    cameras[i] = camera;
                }
                if (root.GetComponentsInChildren<Camera>(true).Length != ExpectedCameraCount)
                    throw new InvalidOperationException("Unexpected cloned Camera count.");

                Transform doorRoot = start.transform.Find(AddedDoorRelativePath);
                DungeonPortalDoorAngleSource[] angleSources =
                    portal.GetComponentsInChildren<DungeonPortalDoorAngleSource>(true);
                if (doorRoot == null || angleSources.Length != 1 ||
                    !angleSources[0].IsConfigured)
                    throw new InvalidOperationException("Moving-door contract changed.");
                DoorContract door = DoorContract.Capture(doorRoot, angleSources[0]);

                DungeonPortalEndpoint[] endpoints =
                    portal.GetComponentsInChildren<DungeonPortalEndpoint>(true);
                if (endpoints.Length != 2)
                    throw new InvalidOperationException("Expected exactly two endpoints.");
                Transform startFrame = null;
                Transform administrativeFrame = null;
                for (int i = 0; i < endpoints.Length; i++)
                {
                    Transform frame = endpoints[i].DoorwayFrame;
                    if (frame == null)
                        throw new InvalidOperationException("Endpoint doorway frame is missing.");
                    if (frame.IsChildOf(start.transform))
                        startFrame = AssignUnique(startFrame, frame, "Start doorway frame");
                    else if (frame.IsChildOf(administrative.transform))
                    {
                        administrativeFrame = AssignUnique(
                            administrativeFrame,
                            frame,
                            "Administrative doorway frame");
                    }
                    else
                        throw new InvalidOperationException("Endpoint doorway ownership changed.");
                }
                if (startFrame == null || administrativeFrame == null)
                    throw new InvalidOperationException("Both doorway frames are required.");

                ApertureGrid aperture = ApertureGrid.Build(
                    door,
                    startFrame,
                    administrativeFrame);
                RendererContract renderers = RendererContract.Capture(root);
                EmissionController emissions = EmissionController.Capture(
                    root,
                    start,
                    administrative);
                ExactLightController lights = ExactLightController.Capture(
                    root,
                    start,
                    administrative,
                    startFrame,
                    administrativeFrame,
                    aperture);
                DungeonTileLightmapSwitcher[] switchers =
                    root.GetComponentsInChildren<DungeonTileLightmapSwitcher>(true);
                if (switchers.Length != 2)
                    throw new InvalidOperationException("Expected two cloned lightmap switchers.");

                return new PreviewContract(
                    previewScene,
                    root,
                    start,
                    administrative,
                    portal,
                    cameras,
                    door,
                    renderers,
                    emissions,
                    lights,
                    aperture,
                    switchers);
            }

            private static Transform AssignUnique(
                Transform current,
                Transform value,
                string label)
            {
                if (current != null)
                    throw new InvalidOperationException("Duplicate " + label + ".");
                return value;
            }

            public void PrepareInactiveClone()
            {
                if (Root.activeInHierarchy)
                    throw new InvalidOperationException("Clone activated before preparation.");
                ReflectionProbe[] probes = Root.GetComponentsInChildren<ReflectionProbe>(true);
                removedReflectionProbeCount = probes.Length;
                for (int i = 0; i < probes.Length; i++)
                {
                    if (probes[i] == null || probes[i].gameObject.scene != previewScene)
                        throw new InvalidOperationException("Clone ReflectionProbe escaped isolation.");
                    Object.DestroyImmediate(probes[i]);
                }
                if (removedReflectionProbeCount <= 0 ||
                    Root.GetComponentsInChildren<ReflectionProbe>(true).Length != 0)
                    throw new InvalidOperationException("ReflectionProbe removal gate failed.");

                Renderers.PrepareDirectOnly();
                Lights.PrepareDirectOnly();
                for (int i = 0; i < switchers.Length; i++)
                    switchers[i].enabled = false;
                PortalRoot.SetActive(false);
                if (PortalRoot.activeSelf || PortalRoot.activeInHierarchy)
                    throw new InvalidOperationException("Portal runtime root remained active.");
            }

            public void AssertActivatedPreparedClone()
            {
                if (!Root.activeInHierarchy || Root.gameObject.scene != previewScene ||
                    PortalRoot.activeSelf || PortalRoot.activeInHierarchy ||
                    Root.GetComponentsInChildren<ReflectionProbe>(true).Length != 0)
                    throw new InvalidOperationException("Prepared clone activation failed.");
                for (int i = 0; i < switchers.Length; i++)
                {
                    if (switchers[i] == null || switchers[i].enabled)
                        throw new InvalidOperationException("A cloned switcher became enabled.");
                }
                Renderers.AssertPrepared(false);
                Lights.AssertPreparedAllDisabled();
            }

            public void AssertState(PowerState power, DoorPose pose)
            {
                if (!Root.activeInHierarchy || PortalRoot.activeSelf ||
                    Root.GetComponentsInChildren<ReflectionProbe>(true).Length != 0)
                    throw new InvalidOperationException("Clone isolation changed.");
                Renderers.AssertPrepared(false);
                Emissions.Assert(power);
                Lights.AssertPower(power);
                Door.AssertPose(pose);
                for (int i = 0; i < Cameras.Length; i++)
                {
                    if (Cameras[i] == null || Cameras[i].enabled ||
                        Cameras[i].targetTexture != null || Cameras[i].scene != previewScene)
                        throw new InvalidOperationException("Fixed clone Camera changed.");
                }
            }

            public void AssertExactAssetsAndStructure()
            {
                Renderers.AssertPrepared(true);
                Emissions.AssertAssetsUnchanged();
                Lights.AssertDescriptorStructure();
                if (Root.GetComponentsInChildren<ReflectionProbe>(true).Length != 0)
                    throw new InvalidOperationException("A clone ReflectionProbe reappeared.");
            }

            public int RemovedReflectionProbeCount => removedReflectionProbeCount;
        }

        private sealed class ApertureGrid
        {
            public readonly Vector2 Minimum;
            public readonly Vector2 Maximum;
            public readonly float LocalPlaneZ;
            public readonly Vector3[] LocalSamples;
            public readonly Vector3[] InsetLocalSamples;
            public readonly Vector3[] WorldSamples;
            public readonly Vector3[] WorldInsetSamples;
            public readonly Vector3 StartWorldCenter;
            public readonly Vector3 AdministrativeWorldCenter;
            public readonly Vector3 StartWorldNormal;
            public readonly Vector3 AdministrativeWorldNormal;
            public readonly float EndpointCenterDistance;
            public readonly float EndpointPlaneSeparation;
            public readonly float EndpointTangentialOffset;
            public readonly float EndpointNormalAbsDot;
            public readonly float EndpointMaxNearestSampleDistance;

            private ApertureGrid(
                Vector2 minimum,
                Vector2 maximum,
                float localPlaneZ,
                Vector3[] samples,
                Vector3[] insetSamples,
                Transform startFrame,
                Transform administrativeFrame)
            {
                Minimum = minimum;
                Maximum = maximum;
                LocalPlaneZ = localPlaneZ;
                LocalSamples = samples;
                InsetLocalSamples = insetSamples;
                WorldSamples = TransformLocalSamples(samples, startFrame);
                WorldInsetSamples = TransformLocalSamples(insetSamples, startFrame);
                Vector3 localCenter = new Vector3(
                    (minimum.x + maximum.x) * 0.5f,
                    (minimum.y + maximum.y) * 0.5f,
                    localPlaneZ);
                StartWorldCenter = startFrame.TransformPoint(localCenter);
                // There is one physical aperture.  Its asymmetric door-mesh AABB is
                // expressed in the Start frame only; reapplying those local coordinates
                // to the opposite-facing endpoint mirrors and offsets the aperture.
                // Both directions therefore consume the same canonical world samples.
                AdministrativeWorldCenter = StartWorldCenter;
                StartWorldNormal = startFrame.forward.normalized;
                AdministrativeWorldNormal = administrativeFrame.forward.normalized;
                Vector3 centerDelta = administrativeFrame.position - startFrame.position;
                EndpointCenterDistance = centerDelta.magnitude;
                EndpointPlaneSeparation = Mathf.Abs(
                    Vector3.Dot(centerDelta, StartWorldNormal));
                Vector3 tangentDelta = centerDelta -
                                       Vector3.Dot(centerDelta, StartWorldNormal) *
                                       StartWorldNormal;
                EndpointTangentialOffset = tangentDelta.magnitude;
                EndpointNormalAbsDot = Mathf.Abs(Vector3.Dot(
                    StartWorldNormal,
                    AdministrativeWorldNormal));
                float halfWidth = (maximum.x - minimum.x) * 0.5f;
                Vector3[] symmetricFrameSamples = BuildSamples(
                    -halfWidth,
                    halfWidth,
                    minimum.y,
                    maximum.y,
                    0f);
                EndpointMaxNearestSampleDistance = ComputeSymmetricMaxNearestDistance(
                    TransformLocalSamples(symmetricFrameSamples, startFrame),
                    TransformLocalSamples(symmetricFrameSamples, administrativeFrame));

                if (EndpointPlaneSeparation > EndpointApertureAlignmentTolerance ||
                    EndpointTangentialOffset > EndpointApertureAlignmentTolerance ||
                    EndpointMaxNearestSampleDistance > EndpointApertureAlignmentTolerance ||
                    EndpointNormalAbsDot < EndpointNormalAlignmentMinimum)
                {
                    throw new InvalidOperationException(
                        "Endpoint doorway frames do not describe the same world aperture: " +
                        "centerDistance=" + FormatFloat(EndpointCenterDistance) +
                        ", planeSeparation=" + FormatFloat(EndpointPlaneSeparation) +
                        ", tangentialOffset=" + FormatFloat(EndpointTangentialOffset) +
                        ", normalAbsDot=" + FormatFloat(EndpointNormalAbsDot) +
                        ", maxNearestSampleDistance=" +
                        FormatFloat(EndpointMaxNearestSampleDistance) + ".");
                }
            }

            public static ApertureGrid Build(
                DoorContract door,
                Transform doorwayFrame,
                Transform administrativeFrame)
            {
                Quaternion original = door.Leaf.localRotation;
                door.Leaf.localRotation = door.ClosedLocalRotation;
                try
                {
                    bool any = false;
                    Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                    Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                    Renderer[] renderers = door.Root.GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        Renderer renderer = renderers[i];
                        if (!renderer.enabled)
                            continue;
                        MeshFilter filter = renderer.GetComponent<MeshFilter>();
                        if (filter == null || filter.sharedMesh == null)
                            throw new InvalidOperationException("Door aperture mesh is missing.");
                        Bounds bounds = filter.sharedMesh.bounds;
                        for (int x = 0; x < 2; x++)
                        for (int y = 0; y < 2; y++)
                        for (int z = 0; z < 2; z++)
                        {
                            Vector3 local = new Vector3(
                                x == 0 ? bounds.min.x : bounds.max.x,
                                y == 0 ? bounds.min.y : bounds.max.y,
                                z == 0 ? bounds.min.z : bounds.max.z);
                            Vector3 world = renderer.transform.TransformPoint(local);
                            Vector3 frame = doorwayFrame.InverseTransformPoint(world);
                            minimum = Vector3.Min(minimum, frame);
                            maximum = Vector3.Max(maximum, frame);
                            any = true;
                        }
                    }
                    if (!any)
                        throw new InvalidOperationException("No enabled door mesh defines aperture.");

                    float width = maximum.x - minimum.x;
                    float height = maximum.y - minimum.y;
                    if (width < 0.5f || width > 3f || height < 1f || height > 4f)
                    {
                        throw new InvalidOperationException(
                            "Derived doorway aperture is implausible: width=" +
                            width.ToString("R", CultureInfo.InvariantCulture) +
                            ", height=" + height.ToString("R", CultureInfo.InvariantCulture) + ".");
                    }

                    float zPlane = (minimum.z + maximum.z) * 0.5f;
                    Vector3[] samples = BuildSamples(
                        minimum.x,
                        maximum.x,
                        minimum.y,
                        maximum.y,
                        zPlane);
                    float insetX = width * 0.02f;
                    float insetY = height * 0.02f;
                    Vector3[] insetSamples = BuildSamples(
                        minimum.x + insetX,
                        maximum.x - insetX,
                        minimum.y + insetY,
                        maximum.y - insetY,
                        zPlane);
                    return new ApertureGrid(
                        new Vector2(minimum.x, minimum.y),
                        new Vector2(maximum.x, maximum.y),
                        zPlane,
                        samples,
                        insetSamples,
                        doorwayFrame,
                        administrativeFrame);
                }
                finally
                {
                    door.Leaf.localRotation = original;
                }
            }

            private static Vector3[] BuildSamples(
                float minX,
                float maxX,
                float minY,
                float maxY,
                float zPlane)
            {
                var samples = new Vector3[ApertureColumns * ApertureRows];
                int index = 0;
                for (int row = 0; row < ApertureRows; row++)
                {
                    float v = ApertureRows == 1 ? 0.5f : row / (ApertureRows - 1f);
                    for (int column = 0; column < ApertureColumns; column++)
                    {
                        float u = ApertureColumns == 1
                            ? 0.5f
                            : column / (ApertureColumns - 1f);
                        samples[index++] = new Vector3(
                            Mathf.Lerp(minX, maxX, u),
                            Mathf.Lerp(minY, maxY, v),
                            zPlane);
                    }
                }
                return samples;
            }

            public Vector3[] GetWorldSamples()
            {
                return (Vector3[])WorldSamples.Clone();
            }

            public Vector3[] GetWorldInsetSamples()
            {
                return (Vector3[])WorldInsetSamples.Clone();
            }

            private static Vector3[] TransformLocalSamples(
                Vector3[] localSamples,
                Transform frame)
            {
                var world = new Vector3[localSamples.Length];
                for (int i = 0; i < localSamples.Length; i++)
                    world[i] = frame.TransformPoint(localSamples[i]);
                return world;
            }

            private static float ComputeSymmetricMaxNearestDistance(
                Vector3[] left,
                Vector3[] right)
            {
                return Mathf.Max(
                    ComputeDirectedMaxNearestDistance(left, right),
                    ComputeDirectedMaxNearestDistance(right, left));
            }

            private static float ComputeDirectedMaxNearestDistance(
                Vector3[] source,
                Vector3[] target)
            {
                if (source == null || target == null || source.Length == 0 ||
                    target.Length == 0)
                    throw new InvalidOperationException("Endpoint aperture sample set is empty.");
                float maximum = 0f;
                for (int i = 0; i < source.Length; i++)
                {
                    float nearest = float.PositiveInfinity;
                    for (int j = 0; j < target.Length; j++)
                        nearest = Mathf.Min(nearest, Vector3.Distance(source[i], target[j]));
                    maximum = Mathf.Max(maximum, nearest);
                }
                return maximum;
            }
        }

        private sealed class ExactLightController
        {
            private readonly ExactLightEntry[] entries;
            public int SelectedStartCount { get; }
            public int SelectedAdministrativeCount { get; }
            public int LooseStartCount { get; }
            public int LooseAdministrativeCount { get; }
            public string SelectionFingerprint { get; }
            public ExactLightEntry[] Entries => (ExactLightEntry[])entries.Clone();

            private ExactLightController(
                ExactLightEntry[] entries,
                int selectedStart,
                int selectedAdministrative,
                int looseStart,
                int looseAdministrative,
                string selectionFingerprint)
            {
                this.entries = entries;
                SelectedStartCount = selectedStart;
                SelectedAdministrativeCount = selectedAdministrative;
                LooseStartCount = looseStart;
                LooseAdministrativeCount = looseAdministrative;
                SelectionFingerprint = selectionFingerprint;
            }

            public static ExactLightController Capture(
                GameObject root,
                GameObject startRoom,
                GameObject administrativeRoom,
                Transform startFrame,
                Transform administrativeFrame,
                ApertureGrid aperture)
            {
                Light[] all = root.GetComponentsInChildren<Light>(true);
                Light[] start = startRoom.GetComponentsInChildren<Light>(true);
                Light[] administrative =
                    administrativeRoom.GetComponentsInChildren<Light>(true);
                if (all.Length != ExpectedTotalLightCount ||
                    start.Length != ExpectedStartLightCount ||
                    administrative.Length != ExpectedAdministrativeLightCount ||
                    start.Length + administrative.Length != all.Length)
                {
                    throw new InvalidOperationException(
                        $"Production Light count changed: all={all.Length}, " +
                        $"start={start.Length}, admin={administrative.Length}.");
                }

                var startSet = new HashSet<Light>(start);
                var administrativeSet = new HashSet<Light>(administrative);
                Array.Sort(all, (left, right) => string.CompareOrdinal(
                    GetStableComponentKey(root.transform, left),
                    GetStableComponentKey(root.transform, right)));
                Vector3[] startSamples = aperture.GetWorldSamples();
                Vector3[] administrativeSamples = aperture.GetWorldSamples();
                Vector3[] startInsetSamples = aperture.GetWorldInsetSamples();
                Vector3[] administrativeInsetSamples = aperture.GetWorldInsetSamples();
                var entries = new ExactLightEntry[all.Length];
                int selectedStart = 0;
                int selectedAdministrative = 0;
                int looseStart = 0;
                int looseAdministrative = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    Light light = all[i];
                    bool inStart = startSet.Contains(light);
                    bool inAdministrative = administrativeSet.Contains(light);
                    if (inStart == inAdministrative)
                        throw new InvalidOperationException("Light room ownership is ambiguous.");
                    RoomRole role = inStart ? RoomRole.Start : RoomRole.Administrative;
                    Vector3[] samples = inStart ? startSamples : administrativeSamples;
                    Vector3[] insetSamples = inStart
                        ? startInsetSamples
                        : administrativeInsetSamples;
                    LightInfluence influence = EvaluateInfluence(
                        light,
                        samples,
                        insetSamples);
                    LightInfluence repeated = EvaluateInfluence(
                        light,
                        samples,
                        insetSamples);
                    if (!influence.Equals(repeated))
                        throw new InvalidOperationException("Light filter was non-deterministic.");
                    bool affectsPilotGameObjectLayer =
                        (light.cullingMask & (1 << PilotGameObjectLayer)) != 0;
                    uint lightingRenderingLayers = GetLightingRenderingLayers(light);
                    bool affectsPilotRenderingLayer =
                        (lightingRenderingLayers & PilotRenderingLayerBit) != 0u;
                    bool activeRelativeToCloneRoot = IsBranchActiveRelativeToRoot(
                        light.transform,
                        root.transform);
                    bool participatesInPilotLayers =
                        affectsPilotGameObjectLayer && affectsPilotRenderingLayer &&
                        activeRelativeToCloneRoot;
                    bool selected = influence.HitCount > 0 && participatesInPilotLayers;
                    bool loose = influence.RangeHitCount > 0 && participatesInPilotLayers;
                    if (selected && light.type != LightType.Spot)
                    {
                        throw new InvalidOperationException(
                            "A non-Spot production Light reached the aperture: " +
                            GetHierarchyPath(light.transform) + ".");
                    }
                    if (selected && inStart)
                        selectedStart++;
                    if (selected && inAdministrative)
                        selectedAdministrative++;
                    if (loose && inStart)
                        looseStart++;
                    if (loose && inAdministrative)
                        looseAdministrative++;
                    entries[i] = ExactLightEntry.Capture(
                        light,
                        role,
                        selected,
                        influence,
                        GetStableComponentKey(root.transform, light),
                        root.transform,
                        affectsPilotGameObjectLayer,
                        affectsPilotRenderingLayer,
                        lightingRenderingLayers);
                }

                if (selectedStart <= 0 || selectedAdministrative <= 0 ||
                    selectedStart >= start.Length ||
                    selectedAdministrative >= administrative.Length ||
                    selectedStart > looseStart ||
                    selectedAdministrative > looseAdministrative)
                {
                    throw new InvalidOperationException(
                        "Deterministic aperture filter produced an invalid selection: strict=" +
                        selectedStart + "/" + selectedAdministrative + ", loose=" +
                        looseStart + "/" + looseAdministrative + ".");
                }
                string fingerprint = ComputeSelectionFingerprint(entries);
                if (!string.Equals(
                        fingerprint,
                        ComputeSelectionFingerprint(entries),
                        StringComparison.Ordinal))
                    throw new InvalidOperationException("Selection fingerprint is unstable.");
                return new ExactLightController(
                    entries,
                    selectedStart,
                    selectedAdministrative,
                    looseStart,
                    looseAdministrative,
                    fingerprint);
            }

            private static string ComputeSelectionFingerprint(ExactLightEntry[] entries)
            {
                var builder = new StringBuilder(entries.Length * 128);
                for (int i = 0; i < entries.Length; i++)
                {
                    ExactLightEntry entry = entries[i];
                    builder.Append(entry.StablePath).Append('|')
                        .Append(entry.Selected).Append('|')
                        .Append(entry.AffectsPilotGameObjectLayer).Append('|')
                        .Append(entry.AffectsPilotRenderingLayer).Append('|')
                        .Append(entry.LightingRenderingLayers).Append('|')
                        .Append(entry.SourceActiveRelativeToCloneRoot).Append('|')
                        .Append(entry.Influence.HitCount).Append('|')
                        .Append(entry.Influence.InsetHitCount).Append('|')
                        .Append(entry.Influence.RangeHitCount).Append('\n');
                }
                return ComputeSha256(Encoding.UTF8.GetBytes(builder.ToString()));
            }

            private static uint GetLightingRenderingLayers(Light light)
            {
                UniversalAdditionalLightData data =
                    light.GetComponent<UniversalAdditionalLightData>();
                return data != null
                    ? data.renderingLayers.value
                    : unchecked((uint)light.renderingLayerMask);
            }

            private static LightInfluence EvaluateInfluence(
                Light light,
                Vector3[] fullSamples,
                Vector3[] insetSamples)
            {
                EvaluateSampleSet(
                    light,
                    fullSamples,
                    out int hits,
                    out int rangeHits,
                    out float minimumDistance,
                    out float maximumConeDot,
                    out float requiredConeDot);
                EvaluateSampleSet(
                    light,
                    insetSamples,
                    out int insetHits,
                    out _,
                    out _,
                    out _,
                    out _);
                return new LightInfluence(
                    hits,
                    insetHits,
                    rangeHits,
                    minimumDistance,
                    maximumConeDot,
                    requiredConeDot);
            }

            private static void EvaluateSampleSet(
                Light light,
                Vector3[] samples,
                out int hits,
                out int rangeHits,
                out float minimumDistance,
                out float maximumConeDot,
                out float requiredConeDot)
            {
                hits = 0;
                rangeHits = 0;
                minimumDistance = float.PositiveInfinity;
                maximumConeDot = -1f;
                requiredConeDot = light.type == LightType.Spot
                    ? Mathf.Cos(light.spotAngle * 0.5f * Mathf.Deg2Rad)
                    : -1f;
                for (int i = 0; i < samples.Length; i++)
                {
                    Vector3 delta = samples[i] - light.transform.position;
                    float distance = delta.magnitude;
                    minimumDistance = Mathf.Min(minimumDistance, distance);
                    bool hit;
                    if (light.type == LightType.Directional)
                    {
                        maximumConeDot = 1f;
                        hit = true;
                    }
                    else if (distance > light.range + 0.0001f)
                        hit = false;
                    else if (light.type == LightType.Point)
                    {
                        rangeHits++;
                        maximumConeDot = 1f;
                        hit = true;
                    }
                    else if (light.type == LightType.Spot)
                    {
                        rangeHits++;
                        float dot = distance <= 0.000001f
                            ? 1f
                            : Vector3.Dot(light.transform.forward, delta / distance);
                        maximumConeDot = Mathf.Max(maximumConeDot, dot);
                        hit = dot + 0.000001f >= requiredConeDot;
                    }
                    else
                        hit = false;
                    if (light.type == LightType.Directional)
                        rangeHits++;
                    if (hit)
                        hits++;
                }
            }

            public void PrepareDirectOnly()
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].PrepareDirectOnly();
                AssertPreparedAllDisabled();
            }

            public void ApplyPower(PowerState power)
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].ApplyPower(power);
                AssertPower(power);
            }

            public void AssertPreparedAllDisabled()
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].AssertPrepared(false);
            }

            public void AssertPower(PowerState power)
            {
                int enabledStart = 0;
                int enabledAdministrative = 0;
                for (int i = 0; i < entries.Length; i++)
                {
                    entries[i].AssertPower(power);
                    if (!entries[i].Light.enabled)
                        continue;
                    if (entries[i].Role == RoomRole.Start)
                        enabledStart++;
                    else
                        enabledAdministrative++;
                }
                int expectedStart = power.StartOn ? SelectedStartCount : 0;
                int expectedAdministrative =
                    power.AdministrativeOn ? SelectedAdministrativeCount : 0;
                if (enabledStart != expectedStart ||
                    enabledAdministrative != expectedAdministrative)
                    throw new InvalidOperationException("K-exact enabled-light count changed.");
            }

            public void AssertDescriptorStructure()
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].AssertDescriptorStructure();
            }
        }

        private readonly struct LightInfluence : IEquatable<LightInfluence>
        {
            public readonly int HitCount;
            public readonly int InsetHitCount;
            public readonly int RangeHitCount;
            public readonly float MinimumDistance;
            public readonly float MaximumConeDot;
            public readonly float RequiredConeDot;

            public LightInfluence(
                int hitCount,
                int insetHitCount,
                int rangeHitCount,
                float minimumDistance,
                float maximumConeDot,
                float requiredConeDot)
            {
                HitCount = hitCount;
                InsetHitCount = insetHitCount;
                RangeHitCount = rangeHitCount;
                MinimumDistance = minimumDistance;
                MaximumConeDot = maximumConeDot;
                RequiredConeDot = requiredConeDot;
            }

            public bool Equals(LightInfluence other)
            {
                return HitCount == other.HitCount &&
                       InsetHitCount == other.InsetHitCount &&
                       RangeHitCount == other.RangeHitCount &&
                       MinimumDistance.Equals(other.MinimumDistance) &&
                       MaximumConeDot.Equals(other.MaximumConeDot) &&
                       RequiredConeDot.Equals(other.RequiredConeDot);
            }
        }

        private sealed class ExactLightEntry
        {
            public readonly Light Light;
            public readonly RoomRole Role;
            public readonly bool Selected;
            public readonly LightInfluence Influence;
            public readonly string StablePath;
            public readonly bool AffectsPilotGameObjectLayer;
            public readonly bool AffectsPilotRenderingLayer;
            public readonly uint LightingRenderingLayers;
            private readonly bool sourceEnabled;
            private readonly LightmapBakeType sourceBakeType;
            private readonly LightType type;
            private readonly LightShadows shadows;
            private readonly Color color;
            private readonly bool useColorTemperature;
            private readonly float colorTemperature;
            private readonly float intensity;
            private readonly float bounceIntensity;
            private readonly float range;
            private readonly float spotAngle;
            private readonly float innerSpotAngle;
            private readonly int cullingMask;
            private readonly int renderingLayerMask;
            private readonly Texture cookie;
            private readonly LightRenderMode renderMode;
            private readonly float shadowStrength;
            private readonly float shadowBias;
            private readonly float shadowNormalBias;
            private readonly float shadowNearPlane;
            private readonly LightShadowResolution shadowResolution;
            private readonly int shadowCustomResolution;
            private readonly Flare flare;
            private readonly Vector3 position;
            private readonly Quaternion rotation;
            private readonly Vector3 scale;
            private readonly int gameObjectLayer;
            private readonly bool gameObjectActiveSelf;
            private readonly bool activeRelativeToCloneRoot;
            private readonly string activeSelfChainFingerprint;
            private readonly Transform cloneRoot;
            private readonly Component[] components;
            private readonly UniversalAdditionalLightData additionalData;
            private readonly string additionalDataSerializedSha256;
            private readonly bool additionalUsePipelineSettings;
            private readonly int additionalShadowResolutionTier;
            private readonly bool additionalCustomShadowLayers;
            private readonly uint additionalRenderingLayers;
            private readonly uint additionalShadowRenderingLayers;
            private readonly SoftShadowQuality additionalSoftShadowQuality;
            private readonly Vector2 additionalCookieSize;
            private readonly Vector2 additionalCookieOffset;

            private ExactLightEntry(
                Light light,
                RoomRole role,
                bool selected,
                LightInfluence influence,
                string stableKey,
                Transform cloneRoot,
                bool affectsPilotGameObjectLayer,
                bool affectsPilotRenderingLayer,
                uint lightingRenderingLayers)
            {
                Light = light;
                Role = role;
                Selected = selected;
                Influence = influence;
                StablePath = stableKey;
                AffectsPilotGameObjectLayer = affectsPilotGameObjectLayer;
                AffectsPilotRenderingLayer = affectsPilotRenderingLayer;
                LightingRenderingLayers = lightingRenderingLayers;
                this.cloneRoot = cloneRoot;
                sourceEnabled = light.enabled;
                sourceBakeType = light.lightmapBakeType;
                type = light.type;
                shadows = light.shadows;
                color = light.color;
                useColorTemperature = light.useColorTemperature;
                colorTemperature = light.colorTemperature;
                intensity = light.intensity;
                bounceIntensity = light.bounceIntensity;
                range = light.range;
                spotAngle = light.spotAngle;
                innerSpotAngle = light.innerSpotAngle;
                cullingMask = light.cullingMask;
                renderingLayerMask = light.renderingLayerMask;
                cookie = light.cookie;
                renderMode = light.renderMode;
                shadowStrength = light.shadowStrength;
                shadowBias = light.shadowBias;
                shadowNormalBias = light.shadowNormalBias;
                shadowNearPlane = light.shadowNearPlane;
                shadowResolution = light.shadowResolution;
                shadowCustomResolution = light.shadowCustomResolution;
                flare = light.flare;
                position = light.transform.position;
                rotation = light.transform.rotation;
                scale = light.transform.lossyScale;
                gameObjectLayer = light.gameObject.layer;
                gameObjectActiveSelf = light.gameObject.activeSelf;
                activeRelativeToCloneRoot =
                    DungeonPortalKExactProductionLightsCapture.IsBranchActiveRelativeToRoot(
                    light.transform,
                    cloneRoot);
                activeSelfChainFingerprint = ComputeActiveSelfChainFingerprint(
                    light.transform,
                    cloneRoot);
                components = light.GetComponents<Component>();
                additionalData = light.GetComponent<UniversalAdditionalLightData>();
                if (additionalData != null)
                {
                    additionalDataSerializedSha256 = SerializedSha(additionalData);
                    additionalUsePipelineSettings = additionalData.usePipelineSettings;
                    additionalShadowResolutionTier =
                        additionalData.additionalLightsShadowResolutionTier;
                    additionalCustomShadowLayers = additionalData.customShadowLayers;
                    additionalRenderingLayers = additionalData.renderingLayers.value;
                    additionalShadowRenderingLayers =
                        additionalData.shadowRenderingLayers.value;
                    additionalSoftShadowQuality = additionalData.softShadowQuality;
                    additionalCookieSize = additionalData.lightCookieSize;
                    additionalCookieOffset = additionalData.lightCookieOffset;
                }
                else
                {
                    additionalDataSerializedSha256 = "<absent>";
                    additionalUsePipelineSettings = false;
                    additionalShadowResolutionTier = int.MinValue;
                    additionalCustomShadowLayers = false;
                    additionalRenderingLayers = unchecked((uint)light.renderingLayerMask);
                    additionalShadowRenderingLayers = unchecked((uint)light.renderingLayerMask);
                    additionalSoftShadowQuality = SoftShadowQuality.UsePipelineSettings;
                    additionalCookieSize = Vector2.zero;
                    additionalCookieOffset = Vector2.zero;
                }

                if (!sourceEnabled || sourceBakeType != LightmapBakeType.Baked ||
                    shadows != LightShadows.Soft)
                {
                    throw new InvalidOperationException(
                        "Expected enabled Baked Soft-shadow production Light: " + StablePath);
                }
            }

            public static ExactLightEntry Capture(
                Light light,
                RoomRole role,
                bool selected,
                LightInfluence influence,
                string stableKey,
                Transform cloneRoot,
                bool affectsPilotGameObjectLayer,
                bool affectsPilotRenderingLayer,
                uint lightingRenderingLayers)
            {
                return new ExactLightEntry(
                    light,
                    role,
                    selected,
                    influence,
                    stableKey,
                    cloneRoot,
                    affectsPilotGameObjectLayer,
                    affectsPilotRenderingLayer,
                    lightingRenderingLayers);
            }

            public void PrepareDirectOnly()
            {
                Light.enabled = false;
                Light.lightmapBakeType = LightmapBakeType.Realtime;
                Light.bounceIntensity = 0f;
            }

            public void ApplyPower(PowerState power)
            {
                bool roomOn = Role == RoomRole.Start
                    ? power.StartOn
                    : power.AdministrativeOn;
                Light.enabled = Selected && roomOn;
            }

            public void AssertPrepared(bool expectedEnabled)
            {
                if (Light == null || Light.enabled != expectedEnabled ||
                    Light.lightmapBakeType != LightmapBakeType.Realtime ||
                    !Mathf.Approximately(Light.bounceIntensity, 0f))
                    throw new InvalidOperationException("Prepared Light state changed: " + StablePath);
                AssertDescriptorStructure();
            }

            public void AssertPower(PowerState power)
            {
                bool roomOn = Role == RoomRole.Start
                    ? power.StartOn
                    : power.AdministrativeOn;
                AssertPrepared(Selected && roomOn);
            }

            public void AssertDescriptorStructure()
            {
                if (Light == null || Light.type != type || Light.shadows != shadows ||
                    Light.color != color ||
                    Light.useColorTemperature != useColorTemperature ||
                    !Mathf.Approximately(Light.colorTemperature, colorTemperature) ||
                    !Mathf.Approximately(Light.intensity, intensity) ||
                    !Mathf.Approximately(Light.range, range) ||
                    !Mathf.Approximately(Light.spotAngle, spotAngle) ||
                    !Mathf.Approximately(Light.innerSpotAngle, innerSpotAngle) ||
                    Light.cullingMask != cullingMask ||
                    Light.renderingLayerMask != renderingLayerMask ||
                    Light.cookie != cookie || Light.renderMode != renderMode ||
                    !Mathf.Approximately(Light.shadowStrength, shadowStrength) ||
                    !Mathf.Approximately(Light.shadowBias, shadowBias) ||
                    !Mathf.Approximately(Light.shadowNormalBias, shadowNormalBias) ||
                    !Mathf.Approximately(Light.shadowNearPlane, shadowNearPlane) ||
                    Light.shadowResolution != shadowResolution ||
                    Light.shadowCustomResolution != shadowCustomResolution ||
                    Light.flare != flare || Light.transform.position != position ||
                    Light.transform.rotation != rotation ||
                    Light.transform.lossyScale != scale ||
                    Light.gameObject.layer != gameObjectLayer ||
                    Light.gameObject.activeSelf != gameObjectActiveSelf ||
                    Light.gameObject.activeInHierarchy !=
                    (cloneRoot.gameObject.activeInHierarchy && activeRelativeToCloneRoot) ||
                    !string.Equals(
                        ComputeActiveSelfChainFingerprint(Light.transform, cloneRoot),
                        activeSelfChainFingerprint,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Exact cloned Light descriptor changed: " + StablePath);
                }
                Component[] current = Light.GetComponents<Component>();
                if (current.Length != components.Length)
                    throw new InvalidOperationException("Light component count changed: " + StablePath);
                for (int i = 0; i < components.Length; i++)
                {
                    if (current[i] != components[i])
                        throw new InvalidOperationException("Light components changed: " + StablePath);
                }
                UniversalAdditionalLightData currentAdditional =
                    Light.GetComponent<UniversalAdditionalLightData>();
                if (currentAdditional != additionalData)
                    throw new InvalidOperationException(
                        "URP additional Light data presence changed: " + StablePath);
                if (additionalData != null &&
                    (!string.Equals(
                         SerializedSha(additionalData),
                         additionalDataSerializedSha256,
                         StringComparison.Ordinal) ||
                     additionalData.usePipelineSettings != additionalUsePipelineSettings ||
                     additionalData.additionalLightsShadowResolutionTier !=
                     additionalShadowResolutionTier ||
                     additionalData.customShadowLayers != additionalCustomShadowLayers ||
                     additionalData.renderingLayers.value != additionalRenderingLayers ||
                     additionalData.shadowRenderingLayers.value !=
                     additionalShadowRenderingLayers ||
                     additionalData.softShadowQuality != additionalSoftShadowQuality ||
                     additionalData.lightCookieSize != additionalCookieSize ||
                     additionalData.lightCookieOffset != additionalCookieOffset))
                {
                    throw new InvalidOperationException(
                        "Exact URP additional Light data changed: " + StablePath);
                }
            }

            public string SourceBakeType => sourceBakeType.ToString();
            public float SourceBounceIntensity => bounceIntensity;
            public string SourceShadowResolution => shadowResolution.ToString();
            public int SourceShadowCustomResolution => shadowCustomResolution;
            public bool SourceActiveSelf => gameObjectActiveSelf;
            public bool SourceActiveRelativeToCloneRoot => activeRelativeToCloneRoot;
            public bool HasUniversalAdditionalData => additionalData != null;
            public string AdditionalDataSerializedSha256 =>
                additionalDataSerializedSha256;
            public bool AdditionalUsePipelineSettings => additionalUsePipelineSettings;
            public int AdditionalShadowResolutionTier => additionalShadowResolutionTier;
            public bool AdditionalCustomShadowLayers => additionalCustomShadowLayers;
            public uint AdditionalRenderingLayers => additionalRenderingLayers;
            public uint AdditionalShadowRenderingLayers =>
                additionalShadowRenderingLayers;
            public string AdditionalSoftShadowQuality =>
                additionalSoftShadowQuality.ToString();
            public Vector2 AdditionalCookieSize => additionalCookieSize;
            public Vector2 AdditionalCookieOffset => additionalCookieOffset;

            private static string ComputeActiveSelfChainFingerprint(
                Transform transform,
                Transform root)
            {
                var builder = new StringBuilder();
                Transform current = transform;
                while (current != null && current != root)
                {
                    builder.Append(current.GetSiblingIndex()).Append(':')
                        .Append(current.gameObject.activeSelf ? '1' : '0').Append('/');
                    current = current.parent;
                }
                if (current != root)
                    throw new InvalidOperationException("Light is outside the clone root.");
                return ComputeSha256(Encoding.UTF8.GetBytes(builder.ToString()));
            }
        }

        private sealed class RendererContract
        {
            private readonly RendererEntry[] entries;
            public int Count => entries.Length;

            private RendererContract(RendererEntry[] entries)
            {
                this.entries = entries;
            }

            public static RendererContract Capture(GameObject root)
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length != ExpectedRendererCount)
                    throw new InvalidOperationException("Production Renderer count changed.");
                var entries = new RendererEntry[renderers.Length];
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (!(renderers[i] is MeshRenderer) || renderers[i].HasPropertyBlock())
                    {
                        throw new InvalidOperationException(
                            "Exact validation requires MeshRenderer and no source MPB: " +
                            GetHierarchyPath(renderers[i].transform));
                    }
                    entries[i] = RendererEntry.Capture(renderers[i]);
                }
                return new RendererContract(entries);
            }

            public void PrepareDirectOnly()
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].PrepareDirectOnly();
            }

            public void AssertPrepared(bool exactMaterials)
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].AssertPrepared(exactMaterials);
            }
        }

        private sealed class RendererEntry
        {
            private readonly Renderer renderer;
            private readonly bool enabled;
            private readonly bool forceRenderingOff;
            private readonly ShadowCastingMode shadowCastingMode;
            private readonly bool receiveShadows;
            private readonly uint renderingLayerMask;
            private readonly int gameObjectLayer;
            private readonly Vector4 lightmapScaleOffset;
            private readonly Vector4 realtimeLightmapScaleOffset;
            private readonly Transform probeAnchor;
            private readonly GameObject proxyVolumeOverride;
            private readonly MeshFilter meshFilter;
            private readonly Mesh mesh;
            private readonly MeshRenderer meshRenderer;
            private readonly Mesh additionalStreams;
            private readonly MaterialFingerprint[] materials;

            private RendererEntry(Renderer renderer)
            {
                this.renderer = renderer;
                enabled = renderer.enabled;
                forceRenderingOff = renderer.forceRenderingOff;
                shadowCastingMode = renderer.shadowCastingMode;
                receiveShadows = renderer.receiveShadows;
                renderingLayerMask = renderer.renderingLayerMask;
                gameObjectLayer = renderer.gameObject.layer;
                lightmapScaleOffset = renderer.lightmapScaleOffset;
                realtimeLightmapScaleOffset = renderer.realtimeLightmapScaleOffset;
                probeAnchor = renderer.probeAnchor;
                proxyVolumeOverride = renderer.lightProbeProxyVolumeOverride;
                meshFilter = renderer.GetComponent<MeshFilter>();
                mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                meshRenderer = renderer as MeshRenderer;
                additionalStreams = meshRenderer != null
                    ? meshRenderer.additionalVertexStreams
                    : null;
                Material[] shared = renderer.sharedMaterials;
                materials = new MaterialFingerprint[shared.Length];
                for (int i = 0; i < shared.Length; i++)
                    materials[i] = MaterialFingerprint.Capture(shared[i]);
                if (meshFilter == null || mesh == null || meshRenderer == null)
                    throw new InvalidOperationException("Renderer mesh contract changed.");
            }

            public static RendererEntry Capture(Renderer renderer)
            {
                return new RendererEntry(renderer);
            }

            public void PrepareDirectOnly()
            {
                renderer.lightmapIndex = -1;
                renderer.realtimeLightmapIndex = -1;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            public void AssertPrepared(bool exactMaterials)
            {
                if (renderer == null || renderer.enabled != enabled ||
                    renderer.forceRenderingOff != forceRenderingOff ||
                    renderer.shadowCastingMode != shadowCastingMode ||
                    renderer.receiveShadows != receiveShadows ||
                    renderer.renderingLayerMask != renderingLayerMask ||
                    renderer.gameObject.layer != gameObjectLayer ||
                    renderer.lightmapIndex != -1 || renderer.realtimeLightmapIndex != -1 ||
                    renderer.lightProbeUsage != LightProbeUsage.Off ||
                    renderer.reflectionProbeUsage != ReflectionProbeUsage.Off ||
                    renderer.lightmapScaleOffset != lightmapScaleOffset ||
                    renderer.realtimeLightmapScaleOffset != realtimeLightmapScaleOffset ||
                    renderer.probeAnchor != probeAnchor ||
                    renderer.lightProbeProxyVolumeOverride != proxyVolumeOverride ||
                    renderer.HasPropertyBlock() || meshFilter.sharedMesh != mesh ||
                    meshRenderer.additionalVertexStreams != additionalStreams)
                {
                    throw new InvalidOperationException(
                        "Renderer material/mesh/direct-only contract changed: " +
                        GetHierarchyPath(renderer != null ? renderer.transform : null));
                }
                Material[] current = renderer.sharedMaterials;
                if (current.Length != materials.Length)
                    throw new InvalidOperationException("Renderer material slot count changed.");
                if (exactMaterials)
                {
                    for (int i = 0; i < current.Length; i++)
                        materials[i].AssertAssetUnchanged(current[i], false);
                }
            }
        }

        private sealed class MaterialFingerprint
        {
            private readonly Material material;
            private readonly Shader shader;
            private readonly int renderQueue;
            private readonly string[] keywords;
            private readonly string assetPath;
            private readonly string serializedJson;
            private readonly string serializedSha256;

            private MaterialFingerprint(Material material)
            {
                this.material = material;
                shader = material != null ? material.shader : null;
                renderQueue = material != null ? material.renderQueue : 0;
                keywords = material != null
                    ? (string[])material.shaderKeywords.Clone()
                    : Array.Empty<string>();
                Array.Sort(keywords, StringComparer.Ordinal);
                assetPath = material != null
                    ? NormalizePath(AssetDatabase.GetAssetPath(material))
                    : "<null>";
                serializedJson = material != null
                    ? EditorJsonUtility.ToJson(material, false)
                    : "<null>";
                serializedSha256 = material != null
                    ? ComputeSha256(Encoding.UTF8.GetBytes(serializedJson))
                    : "<null>";
            }

            public static MaterialFingerprint Capture(Material material)
            {
                return new MaterialFingerprint(material);
            }

            public void AssertAssetUnchanged(Material current, bool requireReference)
            {
                if (requireReference && current != material)
                    throw new InvalidOperationException("Material reference changed.");
                Material target = requireReference ? current : material;
                if (target == null)
                {
                    if (material != null)
                        throw new InvalidOperationException("Material became null.");
                    return;
                }
                string[] currentKeywords = (string[])target.shaderKeywords.Clone();
                Array.Sort(currentKeywords, StringComparer.Ordinal);
                if (target.shader != shader || target.renderQueue != renderQueue ||
                    currentKeywords.Length != keywords.Length)
                    throw new InvalidOperationException("Material shader/render state changed.");
                for (int i = 0; i < keywords.Length; i++)
                {
                    if (currentKeywords[i] != keywords[i])
                        throw new InvalidOperationException("Material keyword set changed.");
                }
                string actualJson = EditorJsonUtility.ToJson(target, false);
                string actual = ComputeSha256(Encoding.UTF8.GetBytes(actualJson));
                if (!string.Equals(actual, serializedSha256, StringComparison.Ordinal))
                {
                    int firstDifference = FirstDifference(serializedJson, actualJson);
                    throw new InvalidOperationException(
                        "Serialized Material properties changed: path=" + assetPath +
                        ", capturedSha256=" + serializedSha256 +
                        ", actualSha256=" + actual +
                        ", firstDifference=" + firstDifference +
                        ", capturedContext=" + JsonContext(serializedJson, firstDifference) +
                        ", actualContext=" + JsonContext(actualJson, firstDifference) + ".");
                }
            }

            private static int FirstDifference(string left, string right)
            {
                int length = Math.Min(left.Length, right.Length);
                for (int i = 0; i < length; i++)
                {
                    if (left[i] != right[i])
                        return i;
                }
                return left.Length == right.Length ? -1 : length;
            }

            private static string JsonContext(string value, int index)
            {
                if (string.IsNullOrEmpty(value))
                    return "<empty>";
                int center = index < 0 ? 0 : Math.Min(index, value.Length - 1);
                int start = Math.Max(0, center - 48);
                int length = Math.Min(128, value.Length - start);
                return value.Substring(start, length)
                    .Replace('\r', ' ')
                    .Replace('\n', ' ');
            }
        }

        private sealed class EmissionController
        {
            private readonly Renderer[] renderers;
            private readonly Dictionary<Renderer, Material[]> baseMaterials;
            private readonly EmissionRoom start;
            private readonly EmissionRoom administrative;
            private readonly MaterialFingerprint[] materialAssets;
            private Dictionary<Renderer, Material[]> expected;
            private PowerState currentPower;
            private bool applied;

            public int StartEntryCount => start.EntryCount;
            public int AdministrativeEntryCount => administrative.EntryCount;

            private EmissionController(
                Renderer[] renderers,
                Dictionary<Renderer, Material[]> baseMaterials,
                EmissionRoom start,
                EmissionRoom administrative,
                MaterialFingerprint[] materialAssets)
            {
                this.renderers = renderers;
                this.baseMaterials = baseMaterials;
                this.start = start;
                this.administrative = administrative;
                this.materialAssets = materialAssets;
            }

            public static EmissionController Capture(
                GameObject root,
                GameObject startRoom,
                GameObject administrativeRoom)
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length != ExpectedRendererCount)
                    throw new InvalidOperationException("Emission Renderer count changed.");
                var baseMaterials = new Dictionary<Renderer, Material[]>();
                var assets = new HashSet<Material>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    Material[] materials = renderers[i].sharedMaterials ?? Array.Empty<Material>();
                    baseMaterials.Add(renderers[i], (Material[])materials.Clone());
                    for (int m = 0; m < materials.Length; m++)
                    {
                        if (materials[m] != null)
                            assets.Add(materials[m]);
                    }
                }

                EmissionRoom start = EmissionRoom.Capture(
                    "Start",
                    startRoom,
                    ExpectedStartEmissionEntryCount,
                    baseMaterials,
                    assets);
                EmissionRoom administrative = EmissionRoom.Capture(
                    "Administrative",
                    administrativeRoom,
                    ExpectedAdministrativeEmissionEntryCount,
                    baseMaterials,
                    assets);
                var fingerprints = new MaterialFingerprint[assets.Count];
                int index = 0;
                foreach (Material material in assets)
                    fingerprints[index++] = MaterialFingerprint.Capture(material);
                return new EmissionController(
                    renderers,
                    baseMaterials,
                    start,
                    administrative,
                    fingerprints);
            }

            public void Apply(PowerState power)
            {
                start.AssertSchema();
                administrative.AssertSchema();
                var next = new Dictionary<Renderer, Material[]>(renderers.Length);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    next.Add(renderer, (Material[])baseMaterials[renderer].Clone());
                }
                start.Apply(power.StartOn, next);
                administrative.Apply(power.AdministrativeOn, next);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    Material[] target = next[renderer];
                    if (!SameMaterials(renderer.sharedMaterials, target))
                        renderer.sharedMaterials = target;
                }
                expected = next;
                currentPower = power;
                applied = true;
                Assert(power);
            }

            public void Assert(PowerState power)
            {
                if (!applied || expected == null ||
                    currentPower.StartOn != power.StartOn ||
                    currentPower.AdministrativeOn != power.AdministrativeOn)
                    throw new InvalidOperationException("Emission power was not applied.");
                start.AssertSchema();
                administrative.AssertSchema();
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (!SameMaterials(renderers[i].sharedMaterials, expected[renderers[i]]))
                    {
                        throw new InvalidOperationException(
                            "Exact production emission references drifted: " +
                            GetHierarchyPath(renderers[i].transform));
                    }
                }
                AssertAssetsUnchanged();
            }

            public void AssertAssetsUnchanged()
            {
                for (int i = 0; i < materialAssets.Length; i++)
                    materialAssets[i].AssertAssetUnchanged(null, false);
            }

            private static bool SameMaterials(Material[] left, Material[] right)
            {
                left ??= Array.Empty<Material>();
                right ??= Array.Empty<Material>();
                if (left.Length != right.Length)
                    return false;
                for (int i = 0; i < left.Length; i++)
                {
                    if (left[i] != right[i])
                        return false;
                }
                return true;
            }
        }

        private sealed class EmissionRoom
        {
            private readonly string role;
            private readonly DungeonTilePowerBakeSet bakeSet;
            private readonly DungeonTilePowerBakeSet.EmissionMaterialEntry[] schema;
            private readonly EmissionSlot[] slots;
            public int EntryCount => slots.Length;

            private EmissionRoom(
                string role,
                DungeonTilePowerBakeSet bakeSet,
                DungeonTilePowerBakeSet.EmissionMaterialEntry[] schema,
                EmissionSlot[] slots)
            {
                this.role = role;
                this.bakeSet = bakeSet;
                this.schema = schema;
                this.slots = slots;
            }

            public static EmissionRoom Capture(
                string role,
                GameObject room,
                int expectedCount,
                Dictionary<Renderer, Material[]> baseMaterials,
                HashSet<Material> materialAssets)
            {
                DungeonTilePowerBakeSet[] sets =
                    room.GetComponentsInChildren<DungeonTilePowerBakeSet>(true);
                DungeonTileLightmapSwitcher[] switchers =
                    room.GetComponentsInChildren<DungeonTileLightmapSwitcher>(true);
                if (sets.Length != 1 || switchers.Length != 1 ||
                    sets[0].transform != room.transform ||
                    switchers[0].transform != room.transform ||
                    !Mathf.Approximately(sets[0].Power100LightIntensityScale, 1f) ||
                    !Mathf.Approximately(sets[0].Power00LightIntensityScale, 0f))
                    throw new InvalidOperationException(role + " power/emission schema changed.");

                DungeonTilePowerBakeSet.EmissionMaterialEntry[] source =
                    sets[0].EmissionMaterialEntries;
                if (source.Length != expectedCount)
                    throw new InvalidOperationException(role + " emission count changed.");
                var schema =
                    (DungeonTilePowerBakeSet.EmissionMaterialEntry[])source.Clone();
                Dictionary<string, List<Renderer>> buckets = BuildRendererBuckets(room.transform);
                var claimed = new Dictionary<Renderer, HashSet<int>>();
                var slots = new EmissionSlot[schema.Length];
                for (int i = 0; i < schema.Length; i++)
                {
                    DungeonTilePowerBakeSet.EmissionMaterialEntry entry = schema[i];
                    if (string.IsNullOrEmpty(entry.relativePath) ||
                        !buckets.TryGetValue(entry.relativePath, out List<Renderer> bucket) ||
                        entry.rendererBucketIndex < 0 ||
                        entry.rendererBucketIndex >= bucket.Count)
                        throw new InvalidOperationException(role + " emission renderer mapping changed.");
                    Renderer renderer = bucket[entry.rendererBucketIndex];
                    if (!baseMaterials.TryGetValue(renderer, out Material[] materials) ||
                        entry.materialIndex < 0 || entry.materialIndex >= materials.Length ||
                        entry.power100Material == null || entry.power00Material == null)
                        throw new InvalidOperationException(role + " emission material mapping changed.");
                    if (!claimed.TryGetValue(renderer, out HashSet<int> materialSlots))
                    {
                        materialSlots = new HashSet<int>();
                        claimed.Add(renderer, materialSlots);
                    }
                    if (!materialSlots.Add(entry.materialIndex))
                        throw new InvalidOperationException(role + " controls one emission slot twice.");
                    ValidateExactEmissionPair(entry.power100Material, entry.power00Material);
                    materialAssets.Add(entry.power100Material);
                    materialAssets.Add(entry.power00Material);
                    IgnoreEmissionControl marker =
                        renderer.GetComponentInParent<IgnoreEmissionControl>(true);
                    slots[i] = new EmissionSlot(
                        renderer,
                        entry.materialIndex,
                        entry.power100Material,
                        entry.power00Material,
                        marker != null && marker.enabled);
                }
                return new EmissionRoom(role, sets[0], schema, slots);
            }

            private static Dictionary<string, List<Renderer>> BuildRendererBuckets(
                Transform root)
            {
                var result = new Dictionary<string, List<Renderer>>(StringComparer.Ordinal);
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    string path = AnimationUtility.CalculateTransformPath(
                        renderers[i].transform,
                        root);
                    if (!result.TryGetValue(path, out List<Renderer> bucket))
                    {
                        bucket = new List<Renderer>();
                        result.Add(path, bucket);
                    }
                    bucket.Add(renderers[i]);
                }
                return result;
            }

            private static void ValidateExactEmissionPair(Material p100, Material p0)
            {
                string on = NormalizePath(AssetDatabase.GetAssetPath(p100));
                string off = NormalizePath(AssetDatabase.GetAssetPath(p0));
                bool allowed =
                    PairIs(on, off, "Lamps_02.mat", "Lamps_02_off.mat") ||
                    PairIs(on, off, "Lamps_05.mat", "Lamps_05_Off.mat") ||
                    PairIs(on, off, "Lamps_01.mat", "Lamps_01_Off.mat");
                if (!allowed || p100.shader == null || p100.shader != p0.shader ||
                    p100.shader.name != "Universal Render Pipeline/Lit" ||
                    p100.renderQueue != 2000 || p0.renderQueue != 2000)
                    throw new InvalidOperationException(
                        "Production P100/P0 emission asset pair changed: " + on + " | " + off);
            }

            private static bool PairIs(
                string on,
                string off,
                string onFilename,
                string offFilename)
            {
                return on.EndsWith("/" + onFilename, StringComparison.Ordinal) &&
                       off.EndsWith("/" + offFilename, StringComparison.Ordinal);
            }

            public void Apply(
                bool p100,
                Dictionary<Renderer, Material[]> target)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    EmissionSlot slot = slots[i];
                    if (slot.Ignore)
                        continue;
                    target[slot.Renderer][slot.MaterialIndex] = p100
                        ? slot.Power100
                        : slot.Power0;
                }
            }

            public void AssertSchema()
            {
                DungeonTilePowerBakeSet.EmissionMaterialEntry[] current =
                    bakeSet.EmissionMaterialEntries;
                if (current.Length != schema.Length)
                    throw new InvalidOperationException(role + " emission schema count drifted.");
                for (int i = 0; i < schema.Length; i++)
                {
                    DungeonTilePowerBakeSet.EmissionMaterialEntry left = current[i];
                    DungeonTilePowerBakeSet.EmissionMaterialEntry right = schema[i];
                    if (left.relativePath != right.relativePath ||
                        left.rendererBucketIndex != right.rendererBucketIndex ||
                        left.materialIndex != right.materialIndex ||
                        left.power100Material != right.power100Material ||
                        left.power00Material != right.power00Material)
                        throw new InvalidOperationException(role + " emission schema drifted.");
                }
            }
        }

        private readonly struct EmissionSlot
        {
            public readonly Renderer Renderer;
            public readonly int MaterialIndex;
            public readonly Material Power100;
            public readonly Material Power0;
            public readonly bool Ignore;

            public EmissionSlot(
                Renderer renderer,
                int materialIndex,
                Material power100,
                Material power0,
                bool ignore)
            {
                Renderer = renderer;
                MaterialIndex = materialIndex;
                Power100 = power100;
                Power0 = power0;
                Ignore = ignore;
            }
        }

        private sealed class DoorContract
        {
            public readonly Transform Root;
            public readonly Transform Leaf;
            public readonly Quaternion ClosedLocalRotation;
            public readonly Vector3 HingeAxis;
            public readonly float OpenAngleDegrees;
            private readonly Vector3 rootPosition;
            private readonly Quaternion rootRotation;
            private readonly Vector3 rootScale;
            private readonly Renderer[] renderers;
            private readonly bool[] rendererEnabled;
            private readonly Mesh[] meshes;

            private DoorContract(
                Transform root,
                Transform leaf,
                Quaternion closed,
                Vector3 axis,
                float angle,
                Renderer[] renderers,
                bool[] rendererEnabled,
                Mesh[] meshes)
            {
                Root = root;
                Leaf = leaf;
                ClosedLocalRotation = closed;
                HingeAxis = axis;
                OpenAngleDegrees = angle;
                rootPosition = root.localPosition;
                rootRotation = root.localRotation;
                rootScale = root.localScale;
                this.renderers = renderers;
                this.rendererEnabled = rendererEnabled;
                this.meshes = meshes;
            }

            public static DoorContract Capture(
                Transform root,
                DungeonPortalDoorAngleSource angleSource)
            {
                Transform leaf = root != null ? root.Find(DoorLeafName) : null;
                if (leaf == null || angleSource.DoorLeaf != leaf || !root.gameObject.activeSelf)
                    throw new InvalidOperationException("Door leaf binding changed.");
                ReadDoorConfiguration(
                    angleSource,
                    out Quaternion closed,
                    out Vector3 axis,
                    out float angle);
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length != ExpectedDoorRendererCount)
                    throw new InvalidOperationException("Door Renderer count changed.");
                var enabled = new bool[renderers.Length];
                var meshes = new Mesh[renderers.Length];
                int enabledCount = 0;
                for (int i = 0; i < renderers.Length; i++)
                {
                    enabled[i] = renderers[i].enabled;
                    if (enabled[i])
                        enabledCount++;
                    MeshFilter filter = renderers[i].GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                        throw new InvalidOperationException("Door Renderer mesh changed.");
                    meshes[i] = filter.sharedMesh;
                }
                if (enabledCount != ExpectedDoorEnabledRendererCount)
                    throw new InvalidOperationException("Door enabled Renderer count changed.");
                return new DoorContract(
                    root,
                    leaf,
                    closed,
                    axis,
                    angle,
                    renderers,
                    enabled,
                    meshes);
            }

            public void ApplyPose(DoorPose pose)
            {
                Leaf.localRotation = ClosedLocalRotation * Quaternion.AngleAxis(
                    OpenAngleDegrees * pose.Fraction,
                    HingeAxis);
                AssertPose(pose);
            }

            public void AssertPose(DoorPose pose)
            {
                Quaternion expected = ClosedLocalRotation * Quaternion.AngleAxis(
                    OpenAngleDegrees * pose.Fraction,
                    HingeAxis);
                if (Root == null || Leaf == null || !Root.gameObject.activeInHierarchy ||
                    Root.localPosition != rootPosition || Root.localRotation != rootRotation ||
                    Root.localScale != rootScale ||
                    Quaternion.Angle(Leaf.localRotation, expected) > 0.01f)
                    throw new InvalidOperationException("Door pose/placement changed.");
                Renderer[] current = Root.GetComponentsInChildren<Renderer>(true);
                if (current.Length != renderers.Length)
                    throw new InvalidOperationException("Door Renderer structure changed.");
                for (int i = 0; i < current.Length; i++)
                {
                    MeshFilter filter = current[i].GetComponent<MeshFilter>();
                    if (current[i] != renderers[i] ||
                        current[i].enabled != rendererEnabled[i] ||
                        filter == null || filter.sharedMesh != meshes[i])
                        throw new InvalidOperationException("Door Renderer state changed.");
                }
            }
        }

        private static void ReadDoorConfiguration(
            DungeonPortalDoorAngleSource source,
            out Quaternion closed,
            out Vector3 axis,
            out float openAngle)
        {
            var serialized = new SerializedObject(source);
            serialized.UpdateIfRequiredOrScript();
            SerializedProperty closedProperty = serialized.FindProperty("closedLocalRotation");
            SerializedProperty axisProperty = serialized.FindProperty("localHingeAxis");
            SerializedProperty angleProperty = serialized.FindProperty("openAngleDegrees");
            if (closedProperty == null || axisProperty == null || angleProperty == null)
                throw new InvalidOperationException("Door serialized fields are unavailable.");
            closed = closedProperty.quaternionValue;
            axis = axisProperty.vector3Value;
            openAngle = angleProperty.floatValue;
            if (axis.sqrMagnitude <= Mathf.Epsilon || Mathf.Abs(openAngle) <= 0.001f)
                throw new InvalidOperationException("Door serialized configuration is invalid.");
            axis.Normalize();
        }

        private static CaptureEvidence CaptureCameraPair(
            Camera camera,
            Scene previewScene,
            PowerState power,
            DoorPose pose,
            RenderTargets targets,
            RenderFailureMonitor failureMonitor)
        {
            if (camera == null || camera.enabled || camera.targetTexture != null ||
                camera.gameObject.scene != previewScene || camera.scene != previewScene)
                throw new InvalidOperationException("Fixed clone Camera isolation changed.");
            UniversalAdditionalCameraData cameraData = RequireUrpCameraData(camera);
            bool originalPost = cameraData.renderPostProcessing;
            RenderTexture originalActive = RenderTexture.active;
            RenderTexture originalTarget = camera.targetTexture;
            float originalAspect = camera.aspect;
            Matrix4x4 originalProjection = camera.projectionMatrix;
            bool originalSrgb = GL.sRGBWrite;
            Texture2D presentationReadback = null;
            Texture2D hdrReadback = null;

            try
            {
                camera.aspect = CaptureWidth / (float)CaptureHeight;
                camera.ResetProjectionMatrix();
                AssertStandardPerspectiveProjection(camera);

                cameraData.renderPostProcessing = originalPost;
                SubmitStandardRenderRequest(
                    camera,
                    targets.PresentationRender,
                    failureMonitor,
                    "presentation StandardRequest");
                GL.sRGBWrite = targets.PresentationResolve.sRGB;
                Graphics.Blit(targets.PresentationRender, targets.PresentationResolve);
                RenderTexture.active = targets.PresentationResolve;
                presentationReadback = new Texture2D(
                    CaptureWidth,
                    CaptureHeight,
                    TextureFormat.RGBA32,
                    false,
                    false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                presentationReadback.ReadPixels(
                    new Rect(0f, 0f, CaptureWidth, CaptureHeight),
                    0,
                    0,
                    false);
                presentationReadback.Apply(false, false);
                byte[] png = ImageConversion.EncodeToPNG(presentationReadback);
                VerifyPng(png, "presentation PNG");

                cameraData.renderPostProcessing = false;
                SubmitStandardRenderRequest(
                    camera,
                    targets.HdrRender,
                    failureMonitor,
                    "linear HDR StandardRequest");
                GL.sRGBWrite = false;
                Graphics.Blit(targets.HdrRender, targets.HdrResolve);
                RenderTexture.active = targets.HdrResolve;
                hdrReadback = new Texture2D(
                    CaptureWidth,
                    CaptureHeight,
                    TextureFormat.RGBAHalf,
                    false,
                    true)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                hdrReadback.ReadPixels(
                    new Rect(0f, 0f, CaptureWidth, CaptureHeight),
                    0,
                    0,
                    false);
                hdrReadback.Apply(false, false);
                ComputeLuminance(hdrReadback, out double mean, out float maximum);
                if (double.IsNaN(mean) || double.IsInfinity(mean) ||
                    float.IsNaN(maximum) || float.IsInfinity(maximum) ||
                    mean <= MinimumValidMeanLinearLuminance ||
                    maximum <= MinimumValidMaxLinearLuminance)
                {
                    throw new InvalidOperationException(
                        "Zero/non-finite HDR frame: power=" + power.Id +
                        ", door=" + pose.Percent + ", camera=" + camera.name +
                        ", mean=" + FormatDouble(mean) + ", max=" +
                        maximum.ToString("R", CultureInfo.InvariantCulture) + ".");
                }
                byte[] exr = ImageConversion.EncodeToEXR(
                    hdrReadback,
                    Texture2D.EXRFlags.CompressZIP);
                VerifyExr(exr, "linear EXR");

                string stem = power.Id + "_D" +
                              pose.Percent.ToString("000", CultureInfo.InvariantCulture) +
                              "__" + CameraToken(camera.name);
                return new CaptureEvidence(
                    power,
                    pose,
                    camera.name,
                    stem + ".png",
                    png,
                    ComputeSha256(png),
                    stem + "__LinearNoPost.exr",
                    exr,
                    ComputeSha256(exr),
                    mean,
                    maximum,
                    originalPost,
                    camera.transform.position,
                    camera.transform.rotation,
                    camera.fieldOfView,
                    camera.nearClipPlane,
                    camera.farClipPlane,
                    camera.cullingMask);
            }
            finally
            {
                RenderTexture.active = originalActive;
                camera.targetTexture = originalTarget;
                camera.aspect = originalAspect;
                camera.projectionMatrix = originalProjection;
                cameraData.renderPostProcessing = originalPost;
                GL.sRGBWrite = originalSrgb;
                if (presentationReadback != null)
                    Object.DestroyImmediate(presentationReadback);
                if (hdrReadback != null)
                    Object.DestroyImmediate(hdrReadback);
            }
        }

        private static void SubmitStandardRenderRequest(
            Camera camera,
            RenderTexture destination,
            RenderFailureMonitor failureMonitor,
            string stage)
        {
            if (!(RenderPipelineManager.currentPipeline is UniversalRenderPipeline))
                throw new InvalidOperationException("The active render pipeline is not URP.");
            if (camera == null || destination == null || !destination.IsCreated() ||
                camera.targetTexture != null)
                throw new InvalidOperationException("StandardRequest precondition failed.");

            var request = new RenderPipeline.StandardRequest
            {
                destination = destination,
                mipLevel = 0,
                slice = 0,
                face = CubemapFace.Unknown
            };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                throw new InvalidOperationException("URP rejected StandardRequest.");
            destination.DiscardContents();
            RenderPipeline.SubmitRenderRequest(camera, request);
            failureMonitor.ThrowIfFailed(stage);
            if (camera.targetTexture != null)
                throw new InvalidOperationException("StandardRequest changed Camera target.");
        }

        private static void AssertStandardPerspectiveProjection(Camera camera)
        {
            Matrix4x4 expected = Matrix4x4.Perspective(
                camera.fieldOfView,
                camera.aspect,
                camera.nearClipPlane,
                camera.farClipPlane);
            Matrix4x4 actual = camera.projectionMatrix;
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
            {
                if (Mathf.Abs(actual[row, column] - expected[row, column]) > 0.00001f)
                    throw new InvalidOperationException("Camera projection is not standard perspective.");
            }
        }

        private static void ComputeLuminance(
            Texture2D texture,
            out double mean,
            out float maximum)
        {
            Color[] pixels = texture.GetPixels();
            if (pixels.Length != CaptureWidth * CaptureHeight)
                throw new InvalidOperationException("Unexpected HDR readback size.");
            double total = 0d;
            maximum = 0f;
            for (int i = 0; i < pixels.Length; i++)
            {
                float luminance = PortalTransportMath.Luminance(pixels[i]);
                if (float.IsNaN(luminance) || float.IsInfinity(luminance))
                {
                    mean = double.NaN;
                    maximum = float.NaN;
                    return;
                }
                total += luminance;
                maximum = Mathf.Max(maximum, luminance);
            }
            mean = pixels.Length == 0 ? 0d : total / pixels.Length;
        }

        private static void ValidateAndAttachBaselines(List<CaptureEvidence> captures)
        {
            var baselines = new Dictionary<string, CaptureEvidence>(StringComparer.Ordinal);
            for (int i = 0; i < captures.Count; i++)
            {
                CaptureEvidence capture = captures[i];
                if (!capture.Power.IsP0P0)
                    continue;
                string key = BaselineKey(capture.Door, capture.CameraName);
                if (baselines.ContainsKey(key))
                    throw new InvalidOperationException("Duplicate same-pose P0/P0 baseline.");
                baselines.Add(key, capture);
            }
            if (baselines.Count != DoorPoses.Length * CameraNames.Length)
                throw new InvalidOperationException("Missing same-pose P0/P0 baseline.");
            for (int i = 0; i < captures.Count; i++)
            {
                CaptureEvidence capture = captures[i];
                CaptureEvidence baseline = baselines[BaselineKey(
                    capture.Door,
                    capture.CameraName)];
                capture.AttachBaseline(
                    baseline.HdrFilename,
                    baseline.MeanLinearLuminance,
                    capture.MeanLinearLuminance - baseline.MeanLinearLuminance);
            }
        }

        private static void ValidateCaptureDiversityAndPoweredSignal(
            List<CaptureEvidence> captures)
        {
            var png = new HashSet<string>(StringComparer.Ordinal);
            var exr = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < captures.Count; i++)
            {
                png.Add(captures[i].PngSha256);
                exr.Add(captures[i].HdrSha256);
            }
            if (png.Count <= 1 || exr.Count <= 1)
                throw new InvalidOperationException("All capture outputs are identical.");
            RequirePoweredReceiverSignal(
                captures,
                "P100_P000",
                AdministrativeCameraName,
                "START_TO_ADMIN");
            RequirePoweredReceiverSignal(
                captures,
                "P000_P100",
                StartCameraName,
                "ADMIN_TO_START");
        }

        private static void RequirePoweredReceiverSignal(
            List<CaptureEvidence> captures,
            string powerId,
            string cameraName,
            string direction)
        {
            CaptureEvidence baseline = null;
            CaptureEvidence powered = null;
            for (int i = 0; i < captures.Count; i++)
            {
                CaptureEvidence capture = captures[i];
                if (capture.Door.Percent != 100 || capture.CameraName != cameraName)
                    continue;
                if (capture.Power.IsP0P0)
                    baseline = capture;
                else if (capture.Power.Id == powerId)
                    powered = capture;
            }
            if (baseline == null || powered == null ||
                powered.HdrSha256 == baseline.HdrSha256 ||
                powered.MeanLinearLuminance - baseline.MeanLinearLuminance <=
                MinimumPoweredMeanDelta)
            {
                throw new InvalidOperationException(
                    "Powered receiver signal gate failed: " + direction + ".");
            }
        }

        private static string BaselineKey(DoorPose pose, string cameraName)
        {
            return pose.Percent.ToString(CultureInfo.InvariantCulture) + "|" + cameraName;
        }

        private sealed class RenderFailureMonitor : IDisposable
        {
            private readonly List<string> failures = new List<string>();
            private bool disposed;

            public RenderFailureMonitor()
            {
                Application.logMessageReceived += OnLog;
            }

            public void ThrowIfFailed(string stage)
            {
                if (failures.Count > 0)
                {
                    throw new InvalidOperationException(
                        "Unity logged a render failure during " + stage + ": " +
                        string.Join(" || ", failures));
                }
            }

            private void OnLog(string condition, string stackTrace, LogType type)
            {
                if (disposed || (type != LogType.Error && type != LogType.Exception &&
                                 type != LogType.Assert))
                    return;
                if (!Contains(condition, "Render Graph") &&
                    !Contains(condition, "ZBinningJob") &&
                    !Contains(condition, "Forward+ jobs have not completed") &&
                    !Contains(stackTrace, "ReflectionProbeManager.UpdateGpuData") &&
                    !Contains(stackTrace, "ForwardLights.PreSetup") &&
                    !Contains(
                        stackTrace,
                        nameof(DungeonPortalKExactProductionLightsCapture)))
                    return;
                string summary = (condition ?? "<no condition>")
                    .Replace('\r', ' ')
                    .Replace('\n', ' ');
                if (!failures.Contains(summary))
                    failures.Add(summary);
            }

            private static bool Contains(string value, string token)
            {
                return !string.IsNullOrEmpty(value) &&
                       value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                Application.logMessageReceived -= OnLog;
            }
        }

        private sealed class CaptureEvidence
        {
            public readonly PowerState Power;
            public readonly DoorPose Door;
            public readonly string CameraName;
            public readonly string PngFilename;
            public readonly byte[] PngBytes;
            public readonly string PngSha256;
            public readonly string HdrFilename;
            public readonly byte[] HdrBytes;
            public readonly string HdrSha256;
            public readonly double MeanLinearLuminance;
            public readonly float MaxLinearLuminance;
            public readonly bool PresentationPostProcessing;
            public readonly Vector3 CameraPosition;
            public readonly Quaternion CameraRotation;
            public readonly float FieldOfView;
            public readonly float NearClip;
            public readonly float FarClip;
            public readonly int CullingMask;
            public string BaselineHdrFilename { get; private set; }
            public double BaselineMeanLinearLuminance { get; private set; }
            public double MeanDeltaFromBaseline { get; private set; }

            public CaptureEvidence(
                PowerState power,
                DoorPose door,
                string cameraName,
                string pngFilename,
                byte[] pngBytes,
                string pngSha256,
                string hdrFilename,
                byte[] hdrBytes,
                string hdrSha256,
                double meanLinearLuminance,
                float maxLinearLuminance,
                bool presentationPostProcessing,
                Vector3 cameraPosition,
                Quaternion cameraRotation,
                float fieldOfView,
                float nearClip,
                float farClip,
                int cullingMask)
            {
                Power = power;
                Door = door;
                CameraName = cameraName;
                PngFilename = pngFilename;
                PngBytes = pngBytes;
                PngSha256 = pngSha256;
                HdrFilename = hdrFilename;
                HdrBytes = hdrBytes;
                HdrSha256 = hdrSha256;
                MeanLinearLuminance = meanLinearLuminance;
                MaxLinearLuminance = maxLinearLuminance;
                PresentationPostProcessing = presentationPostProcessing;
                CameraPosition = cameraPosition;
                CameraRotation = cameraRotation;
                FieldOfView = fieldOfView;
                NearClip = nearClip;
                FarClip = farClip;
                CullingMask = cullingMask;
            }

            public void AttachBaseline(string filename, double mean, double delta)
            {
                BaselineHdrFilename = filename;
                BaselineMeanLinearLuminance = mean;
                MeanDeltaFromBaseline = delta;
            }
        }

        private sealed class DetachedReport
        {
            public int RendererCount;
            public int RemovedReflectionProbeCount;
            public int SelectedStartCount;
            public int SelectedAdministrativeCount;
            public int LooseStartCount;
            public int LooseAdministrativeCount;
            public string SelectionFingerprint;
            public int StartEmissionEntryCount;
            public int AdministrativeEmissionEntryCount;
            public Vector2 ApertureMinimum;
            public Vector2 ApertureMaximum;
            public float AperturePlaneZ;
            public Vector3 StartApertureWorldCenter;
            public Vector3 AdministrativeApertureWorldCenter;
            public Vector3 StartApertureWorldNormal;
            public Vector3 AdministrativeApertureWorldNormal;
            public float EndpointApertureCenterDistance;
            public float EndpointAperturePlaneSeparation;
            public float EndpointApertureTangentialOffset;
            public float EndpointApertureNormalAbsDot;
            public float EndpointApertureMaxNearestSampleDistance;
            public LightSelectionRecord[] Lights;

            public static DetachedReport Capture(PreviewContract contract)
            {
                ExactLightEntry[] entries = contract.Lights.Entries;
                var records = new LightSelectionRecord[entries.Length];
                for (int i = 0; i < entries.Length; i++)
                    records[i] = LightSelectionRecord.Capture(entries[i]);
                return new DetachedReport
                {
                    RendererCount = contract.Renderers.Count,
                    RemovedReflectionProbeCount = contract.RemovedReflectionProbeCount,
                    SelectedStartCount = contract.Lights.SelectedStartCount,
                    SelectedAdministrativeCount =
                        contract.Lights.SelectedAdministrativeCount,
                    LooseStartCount = contract.Lights.LooseStartCount,
                    LooseAdministrativeCount = contract.Lights.LooseAdministrativeCount,
                    SelectionFingerprint = contract.Lights.SelectionFingerprint,
                    StartEmissionEntryCount = contract.Emissions.StartEntryCount,
                    AdministrativeEmissionEntryCount =
                        contract.Emissions.AdministrativeEntryCount,
                    ApertureMinimum = contract.Aperture.Minimum,
                    ApertureMaximum = contract.Aperture.Maximum,
                    AperturePlaneZ = contract.Aperture.LocalPlaneZ,
                    StartApertureWorldCenter = contract.Aperture.StartWorldCenter,
                    AdministrativeApertureWorldCenter =
                        contract.Aperture.AdministrativeWorldCenter,
                    StartApertureWorldNormal = contract.Aperture.StartWorldNormal,
                    AdministrativeApertureWorldNormal =
                        contract.Aperture.AdministrativeWorldNormal,
                    EndpointApertureCenterDistance =
                        contract.Aperture.EndpointCenterDistance,
                    EndpointAperturePlaneSeparation =
                        contract.Aperture.EndpointPlaneSeparation,
                    EndpointApertureTangentialOffset =
                        contract.Aperture.EndpointTangentialOffset,
                    EndpointApertureNormalAbsDot =
                        contract.Aperture.EndpointNormalAbsDot,
                    EndpointApertureMaxNearestSampleDistance =
                        contract.Aperture.EndpointMaxNearestSampleDistance,
                    Lights = records
                };
            }
        }

        private readonly struct LightSelectionRecord
        {
            public readonly string StablePath;
            public readonly string Room;
            public readonly bool SelectedStrict;
            public readonly bool SelectedLoose;
            public readonly bool AffectsPilotGameObjectLayer;
            public readonly bool AffectsPilotRenderingLayer;
            public readonly uint LightingRenderingLayers;
            public readonly int StrictHitCount;
            public readonly int InsetStrictHitCount;
            public readonly int RangeHitCount;
            public readonly float MinimumDistance;
            public readonly float MaximumConeDot;
            public readonly float RequiredConeDot;
            public readonly string Type;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Color Color;
            public readonly float Intensity;
            public readonly float Range;
            public readonly float SpotAngle;
            public readonly float InnerSpotAngle;
            public readonly string CookiePath;
            public readonly string CookieDependencyHash;
            public readonly int CullingMask;
            public readonly int RenderingLayerMask;
            public readonly string Shadows;
            public readonly float ShadowStrength;
            public readonly string ShadowResolution;
            public readonly int ShadowCustomResolution;
            public readonly bool ActiveSelf;
            public readonly bool ActiveRelativeToCloneRoot;
            public readonly bool HasUniversalAdditionalData;
            public readonly string UniversalAdditionalDataSerializedSha256;
            public readonly bool UniversalUsePipelineSettings;
            public readonly int UniversalShadowResolutionTier;
            public readonly bool UniversalCustomShadowLayers;
            public readonly uint UniversalRenderingLayers;
            public readonly uint UniversalShadowRenderingLayers;
            public readonly string UniversalSoftShadowQuality;
            public readonly Vector2 UniversalCookieSize;
            public readonly Vector2 UniversalCookieOffset;
            public readonly string SourceBakeType;
            public readonly float SourceBounceIntensity;

            private LightSelectionRecord(
                string stablePath,
                string room,
                bool selectedStrict,
                bool selectedLoose,
                bool affectsPilotGameObjectLayer,
                bool affectsPilotRenderingLayer,
                uint lightingRenderingLayers,
                int strictHitCount,
                int insetStrictHitCount,
                int rangeHitCount,
                float minimumDistance,
                float maximumConeDot,
                float requiredConeDot,
                string type,
                Vector3 position,
                Quaternion rotation,
                Color color,
                float intensity,
                float range,
                float spotAngle,
                float innerSpotAngle,
                string cookiePath,
                string cookieDependencyHash,
                int cullingMask,
                int renderingLayerMask,
                string shadows,
                float shadowStrength,
                string shadowResolution,
                int shadowCustomResolution,
                bool activeSelf,
                bool activeRelativeToCloneRoot,
                bool hasUniversalAdditionalData,
                string universalAdditionalDataSerializedSha256,
                bool universalUsePipelineSettings,
                int universalShadowResolutionTier,
                bool universalCustomShadowLayers,
                uint universalRenderingLayers,
                uint universalShadowRenderingLayers,
                string universalSoftShadowQuality,
                Vector2 universalCookieSize,
                Vector2 universalCookieOffset,
                string sourceBakeType,
                float sourceBounceIntensity)
            {
                StablePath = stablePath;
                Room = room;
                SelectedStrict = selectedStrict;
                SelectedLoose = selectedLoose;
                AffectsPilotGameObjectLayer = affectsPilotGameObjectLayer;
                AffectsPilotRenderingLayer = affectsPilotRenderingLayer;
                LightingRenderingLayers = lightingRenderingLayers;
                StrictHitCount = strictHitCount;
                InsetStrictHitCount = insetStrictHitCount;
                RangeHitCount = rangeHitCount;
                MinimumDistance = minimumDistance;
                MaximumConeDot = maximumConeDot;
                RequiredConeDot = requiredConeDot;
                Type = type;
                Position = position;
                Rotation = rotation;
                Color = color;
                Intensity = intensity;
                Range = range;
                SpotAngle = spotAngle;
                InnerSpotAngle = innerSpotAngle;
                CookiePath = cookiePath;
                CookieDependencyHash = cookieDependencyHash;
                CullingMask = cullingMask;
                RenderingLayerMask = renderingLayerMask;
                Shadows = shadows;
                ShadowStrength = shadowStrength;
                ShadowResolution = shadowResolution;
                ShadowCustomResolution = shadowCustomResolution;
                ActiveSelf = activeSelf;
                ActiveRelativeToCloneRoot = activeRelativeToCloneRoot;
                HasUniversalAdditionalData = hasUniversalAdditionalData;
                UniversalAdditionalDataSerializedSha256 =
                    universalAdditionalDataSerializedSha256;
                UniversalUsePipelineSettings = universalUsePipelineSettings;
                UniversalShadowResolutionTier = universalShadowResolutionTier;
                UniversalCustomShadowLayers = universalCustomShadowLayers;
                UniversalRenderingLayers = universalRenderingLayers;
                UniversalShadowRenderingLayers = universalShadowRenderingLayers;
                UniversalSoftShadowQuality = universalSoftShadowQuality;
                UniversalCookieSize = universalCookieSize;
                UniversalCookieOffset = universalCookieOffset;
                SourceBakeType = sourceBakeType;
                SourceBounceIntensity = sourceBounceIntensity;
            }

            public static LightSelectionRecord Capture(ExactLightEntry entry)
            {
                Light light = entry.Light;
                string cookiePath = light.cookie != null
                    ? NormalizePath(AssetDatabase.GetAssetPath(light.cookie))
                    : string.Empty;
                string cookieHash = string.IsNullOrEmpty(cookiePath)
                    ? string.Empty
                    : AssetDatabase.GetAssetDependencyHash(cookiePath).ToString();
                return new LightSelectionRecord(
                    entry.StablePath,
                    entry.Role.ToString(),
                    entry.Selected,
                    entry.Influence.RangeHitCount > 0 &&
                    entry.AffectsPilotGameObjectLayer &&
                    entry.AffectsPilotRenderingLayer &&
                    entry.SourceActiveRelativeToCloneRoot,
                    entry.AffectsPilotGameObjectLayer,
                    entry.AffectsPilotRenderingLayer,
                    entry.LightingRenderingLayers,
                    entry.Influence.HitCount,
                    entry.Influence.InsetHitCount,
                    entry.Influence.RangeHitCount,
                    entry.Influence.MinimumDistance,
                    entry.Influence.MaximumConeDot,
                    entry.Influence.RequiredConeDot,
                    light.type.ToString(),
                    light.transform.position,
                    light.transform.rotation,
                    light.color,
                    light.intensity,
                    light.range,
                    light.spotAngle,
                    light.innerSpotAngle,
                    cookiePath,
                    cookieHash,
                    light.cullingMask,
                    light.renderingLayerMask,
                    light.shadows.ToString(),
                    light.shadowStrength,
                    entry.SourceShadowResolution,
                    entry.SourceShadowCustomResolution,
                    entry.SourceActiveSelf,
                    entry.SourceActiveRelativeToCloneRoot,
                    entry.HasUniversalAdditionalData,
                    entry.AdditionalDataSerializedSha256,
                    entry.AdditionalUsePipelineSettings,
                    entry.AdditionalShadowResolutionTier,
                    entry.AdditionalCustomShadowLayers,
                    entry.AdditionalRenderingLayers,
                    entry.AdditionalShadowRenderingLayers,
                    entry.AdditionalSoftShadowQuality,
                    entry.AdditionalCookieSize,
                    entry.AdditionalCookieOffset,
                    entry.SourceBakeType,
                    entry.SourceBounceIntensity);
            }
        }

        private static string BuildManifest(
            string timestamp,
            CaptureIdentity identity,
            DetachedReport report,
            List<CaptureEvidence> captures)
        {
            var builder = new StringBuilder(128 * 1024);
            builder.AppendLine("status=" + Status);
            builder.AppendLine("captureOutcome=COMPLETE");
            builder.AppendLine("captureUtc=" + timestamp);
            builder.AppendLine("purpose=Determine whether the exact production Lights whose influence reaches each doorway can reproduce the full 78-Light REALTIME_DIRECT_POWER_EMISSION reference.");
            builder.AppendLine("acceptance=BLOCKED_PENDING_PIXEL_COMPARISON");
            builder.AppendLine("visualFitClaimed=false");
            builder.AppendLine("visualParityClaimed=false");
            builder.AppendLine("bakeRealtimeEquivalenceClaimed=false");
            builder.AppendLine("selectionUsesNames=false");
            builder.AppendLine("stableLightKey=Scene-root-relative name plus sibling-index chain and same-type component ordinal.");
            builder.AppendLine("strictSelection=At least one full-opening 9x13 doorway sample lies inside both Light.range and the Spot outer cone, and the enabled Light has an active relative hierarchy and participates in pilot GameObject/rendering layers.");
            builder.AppendLine("strictSelectionGrid=Full derived door opening bounds including boundary samples; no inset controls selection.");
            builder.AppendLine("sharedPhysicalApertureWorldSamples=true");
            builder.AppendLine("canonicalApertureFrame=Start endpoint; the asymmetric door-mesh bounds are transformed once and the identical world sample set is evaluated for both directions.");
            builder.AppendLine("insetSelectionDiagnostic=Two-percent XY-inset 9x13 hit count is recorded only as a boundary-sensitivity diagnostic.");
            builder.AppendLine("looseSelection=At least one full-opening doorway sample lies inside Light.range and the enabled Light has an active relative hierarchy and participates in pilot layers; recorded for the original K4/K3 hypothesis but not enabled.");
            builder.AppendLine("selectionDeterminismGate=Every Light influence result is evaluated twice and must match exactly.");
            builder.AppendLine("pilotGameObjectLayer=" + PilotGameObjectLayer);
            builder.AppendLine("pilotRenderingLayerBit=" + PilotRenderingLayerBit);
            builder.AppendLine("layerParticipationGate=Selected Lights must include GameObject layer 0 in Light.cullingMask and rendering-layer bit 1 in UniversalAdditionalLightData.renderingLayers (or Light.renderingLayerMask when additional data is absent).");
            builder.AppendLine("layerPilotLimit=This validates the current layer-0/rendering-layer-1 validation assets only; it is not a general receiver-routing proof.");
            builder.AppendLine("receiverOnlyRoutingValidated=false");
            builder.AppendLine("finalReceiverRoutingGateRequired=true");
            builder.AppendLine("lightCullingMatrixInspectionSupported=false");
            builder.AppendLine("lightCullingMatrixInspectionReason=Unity exposes no stable public per-Light culling-matrix descriptor used here; private or volatile APIs are intentionally not claimed.");
            builder.AppendLine("cloneOnly=true");
            builder.AppendLine("productionAssetsWritten=false");
            builder.AppendLine("playModeUsed=false");
            builder.AppendLine("sceneOpenedOrSaved=false");
            builder.AppendLine("lightmappingStarted=false");
            builder.AppendLine("renderApi=RenderPipeline.StandardRequest");
            builder.AppendLine("renderFailureGate=RenderGraph ReflectionProbeManager ForwardLights ZBinning and capture-stack errors abort publication.");
            builder.AppendLine("frameGate=Every HDR frame finite and nonzero; PNG and EXR sets not all-identical; both D100 receiver directions positive versus same-pose P0/P0.");
            builder.AppendLine("publicationProtocol=Same-parent unique staging directory, raw header/hash verification, Directory.Move atomic publish, synchronous Unity import verification, CAPTURE_STATE COMPLETE last.");
            builder.AppendLine("validationSceneExpectedSha256=" + ExpectedValidationSceneSha256);
            builder.AppendLine("validationSceneActualSha256=" +
                               ComputeFileSha256(ValidationScenePath));
            builder.AppendLine("referenceManifestPath=" + ReferenceManifestPath);
            builder.AppendLine("referenceManifestSha256=" +
                               ComputeFileSha256(ReferenceManifestPath));
            builder.AppendLine("referenceStatus=REALTIME_DIRECT_POWER_EMISSION_GT_ONLY");
            builder.AppendLine("rendererLightmapsDisabled=true");
            builder.AppendLine("rendererLightProbeSampling=false");
            builder.AppendLine("rendererReflectionProbeSampling=false");
            builder.AppendLine("cloneReflectionProbesRemoved=" +
                               report.RemovedReflectionProbeCount);
            builder.AppendLine("selectedLightsUseExactClonedProductionComponents=true");
            builder.AppendLine("selectedLightMutations=enabled by room P0/P100, lightmapBakeType Realtime, bounceIntensity 0; all other descriptor fields asserted unchanged.");
            builder.AppendLine("universalAdditionalLightDataParity=Full EditorJsonUtility serialized SHA-256 plus public rendering-layer shadow-tier soft-quality and cookie fields asserted unchanged before/after clone capture.");
            builder.AppendLine("lightShadowParity=Light.shadowResolution and Light.shadowCustomResolution asserted unchanged before/after clone capture.");
            builder.AppendLine("lightActiveHierarchyParity=Each Light activeSelf chain relative to the clone root is fingerprinted; activeInHierarchy is asserted against clone-root activation.");
            builder.AppendLine("unselectedProductionLightsEnabled=false");
            builder.AppendLine("exactProductionP0P100EmissionReferences=true");
            builder.AppendLine("sourceMaterialsUntouched=true");
            builder.AppendLine("sourceMaterialPropertyBlocksUntouched=true");
            builder.AppendLine("sourceMpbContract=All 389 source Renderers have no MaterialPropertyBlock before and after capture.");
            builder.AppendLine("captureWidth=" + CaptureWidth);
            builder.AppendLine("captureHeight=" + CaptureHeight);
            builder.AppendLine("powerStateCount=" + PowerStates.Length);
            builder.AppendLine("doorPoseCount=" + DoorPoses.Length);
            builder.AppendLine("cameraCount=" + CameraNames.Length);
            builder.AppendLine("stateCameraRecordCount=" + captures.Count);
            builder.AppendLine("rendererCount=" + report.RendererCount);
            builder.AppendLine("strictSelectedStartK=" + report.SelectedStartCount);
            builder.AppendLine("strictSelectedAdministrativeK=" +
                               report.SelectedAdministrativeCount);
            builder.AppendLine("selectionFingerprintSha256=" +
                               report.SelectionFingerprint);
            builder.AppendLine("looseRangeOnlyStartK=" + report.LooseStartCount);
            builder.AppendLine("looseRangeOnlyAdministrativeK=" +
                               report.LooseAdministrativeCount);
            builder.AppendLine("startEmissionEntryCount=" + report.StartEmissionEntryCount);
            builder.AppendLine("administrativeEmissionEntryCount=" +
                               report.AdministrativeEmissionEntryCount);
            builder.AppendLine("apertureGridColumns=" + ApertureColumns);
            builder.AppendLine("apertureGridRows=" + ApertureRows);
            builder.AppendLine("apertureSampleCount=" + (ApertureColumns * ApertureRows));
            builder.AppendLine("apertureLocalMinimum=" + FormatVector2(report.ApertureMinimum));
            builder.AppendLine("apertureLocalMaximum=" + FormatVector2(report.ApertureMaximum));
            builder.AppendLine("apertureLocalPlaneZ=" +
                               report.AperturePlaneZ.ToString("R", CultureInfo.InvariantCulture));
            builder.AppendLine("startApertureWorldCenter=" +
                               FormatVector3(report.StartApertureWorldCenter));
            builder.AppendLine("administrativeApertureWorldCenter=" +
                               FormatVector3(report.AdministrativeApertureWorldCenter));
            builder.AppendLine("startApertureWorldNormal=" +
                               FormatVector3(report.StartApertureWorldNormal));
            builder.AppendLine("administrativeApertureWorldNormal=" +
                               FormatVector3(report.AdministrativeApertureWorldNormal));
            builder.AppendLine("endpointApertureAlignmentTolerance=" +
                               FormatFloat(EndpointApertureAlignmentTolerance));
            builder.AppendLine("endpointApertureNormalAlignmentMinimum=" +
                               FormatFloat(EndpointNormalAlignmentMinimum));
            builder.AppendLine("endpointFrameOriginDistance=" +
                               FormatFloat(report.EndpointApertureCenterDistance));
            builder.AppendLine("endpointAperturePlaneSeparation=" +
                               FormatFloat(report.EndpointAperturePlaneSeparation));
            builder.AppendLine("endpointApertureTangentialOffset=" +
                               FormatFloat(report.EndpointApertureTangentialOffset));
            builder.AppendLine("endpointApertureNormalAbsDot=" +
                               FormatFloat(report.EndpointApertureNormalAbsDot));
            builder.AppendLine("endpointSymmetricFrameSampleMaxNearestDistance=" +
                               FormatFloat(report.EndpointApertureMaxNearestSampleDistance));
            builder.AppendLine("unityVersion=" + Application.unityVersion);
            builder.AppendLine("qualityLevel=" + identity.QualityLevel);
            builder.AppendLine("qualityName=" + identity.QualityName);
            builder.AppendLine("renderPipelineAsset=" + identity.RenderPipelinePath);
            builder.AppendLine("renderPipelineDependencyHash=" +
                               identity.RenderPipelineDependencyHash);
            identity.Append(builder);

            for (int i = 0; i < report.Lights.Length; i++)
            {
                LightSelectionRecord light = report.Lights[i];
                string prefix = "light." + i + ".";
                builder.AppendLine(prefix + "stableKey=" + SanitizeManifest(light.StablePath));
                builder.AppendLine(prefix + "room=" + light.Room);
                builder.AppendLine(prefix + "selectedStrict=" + light.SelectedStrict);
                builder.AppendLine(prefix + "selectedLoose=" + light.SelectedLoose);
                builder.AppendLine(prefix + "affectsPilotGameObjectLayer=" +
                                   light.AffectsPilotGameObjectLayer);
                builder.AppendLine(prefix + "affectsPilotRenderingLayer=" +
                                   light.AffectsPilotRenderingLayer);
                builder.AppendLine(prefix + "lightingRenderingLayers=" +
                                   light.LightingRenderingLayers);
                builder.AppendLine(prefix + "strictHitCount=" + light.StrictHitCount);
                builder.AppendLine(prefix + "insetStrictHitCount=" +
                                   light.InsetStrictHitCount);
                builder.AppendLine(prefix + "rangeHitCount=" + light.RangeHitCount);
                builder.AppendLine(prefix + "minimumDistance=" + FormatFloat(light.MinimumDistance));
                builder.AppendLine(prefix + "maximumConeDot=" + FormatFloat(light.MaximumConeDot));
                builder.AppendLine(prefix + "requiredConeDot=" + FormatFloat(light.RequiredConeDot));
                builder.AppendLine(prefix + "type=" + light.Type);
                builder.AppendLine(prefix + "position=" + FormatVector3(light.Position));
                builder.AppendLine(prefix + "rotation=" + FormatQuaternion(light.Rotation));
                builder.AppendLine(prefix + "color=" + FormatColor(light.Color));
                builder.AppendLine(prefix + "intensity=" + FormatFloat(light.Intensity));
                builder.AppendLine(prefix + "range=" + FormatFloat(light.Range));
                builder.AppendLine(prefix + "spotAngle=" + FormatFloat(light.SpotAngle));
                builder.AppendLine(prefix + "innerSpotAngle=" + FormatFloat(light.InnerSpotAngle));
                builder.AppendLine(prefix + "cookie=" + light.CookiePath);
                builder.AppendLine(prefix + "cookieDependencyHash=" +
                                   light.CookieDependencyHash);
                builder.AppendLine(prefix + "cullingMask=" + light.CullingMask);
                builder.AppendLine(prefix + "renderingLayerMask=" +
                                   light.RenderingLayerMask);
                builder.AppendLine(prefix + "shadows=" + light.Shadows);
                builder.AppendLine(prefix + "shadowStrength=" +
                                   FormatFloat(light.ShadowStrength));
                builder.AppendLine(prefix + "shadowResolution=" +
                                   light.ShadowResolution);
                builder.AppendLine(prefix + "shadowCustomResolution=" +
                                   light.ShadowCustomResolution);
                builder.AppendLine(prefix + "activeSelf=" + light.ActiveSelf);
                builder.AppendLine(prefix + "activeRelativeToCloneRoot=" +
                                   light.ActiveRelativeToCloneRoot);
                builder.AppendLine(prefix + "hasUniversalAdditionalLightData=" +
                                   light.HasUniversalAdditionalData);
                builder.AppendLine(prefix +
                                   "universalAdditionalLightDataSerializedSha256=" +
                                   light.UniversalAdditionalDataSerializedSha256);
                builder.AppendLine(prefix + "universalUsePipelineSettings=" +
                                   light.UniversalUsePipelineSettings);
                builder.AppendLine(prefix + "universalShadowResolutionTier=" +
                                   light.UniversalShadowResolutionTier);
                builder.AppendLine(prefix + "universalCustomShadowLayers=" +
                                   light.UniversalCustomShadowLayers);
                builder.AppendLine(prefix + "universalRenderingLayers=" +
                                   light.UniversalRenderingLayers);
                builder.AppendLine(prefix + "universalShadowRenderingLayers=" +
                                   light.UniversalShadowRenderingLayers);
                builder.AppendLine(prefix + "universalSoftShadowQuality=" +
                                   light.UniversalSoftShadowQuality);
                builder.AppendLine(prefix + "universalCookieSize=" +
                                   FormatVector2(light.UniversalCookieSize));
                builder.AppendLine(prefix + "universalCookieOffset=" +
                                   FormatVector2(light.UniversalCookieOffset));
                builder.AppendLine(prefix + "sourceBakeType=" + light.SourceBakeType);
                builder.AppendLine(prefix + "sourceBounceIntensity=" +
                                   FormatFloat(light.SourceBounceIntensity));
            }

            for (int i = 0; i < captures.Count; i++)
            {
                CaptureEvidence capture = captures[i];
                string prefix = "capture." + i + ".";
                builder.AppendLine(prefix + "power=" + capture.Power.Id);
                builder.AppendLine(prefix + "doorPercent=" + capture.Door.Percent);
                builder.AppendLine(prefix + "camera=" + capture.CameraName);
                builder.AppendLine(prefix + "png=" + capture.PngFilename);
                builder.AppendLine(prefix + "pngSha256=" + capture.PngSha256);
                builder.AppendLine(prefix + "pngBytes=" + capture.PngBytes.LongLength);
                builder.AppendLine(prefix + "linearExr=" + capture.HdrFilename);
                builder.AppendLine(prefix + "linearExrSha256=" + capture.HdrSha256);
                builder.AppendLine(prefix + "linearExrBytes=" +
                                   capture.HdrBytes.LongLength);
                builder.AppendLine(prefix + "samePoseP0P0Baseline=" +
                                   capture.BaselineHdrFilename);
                builder.AppendLine(prefix + "meanLinearLuminance=" +
                                   FormatDouble(capture.MeanLinearLuminance));
                builder.AppendLine(prefix + "maxLinearLuminance=" +
                                   FormatFloat(capture.MaxLinearLuminance));
                builder.AppendLine(prefix + "baselineMeanLinearLuminance=" +
                                   FormatDouble(capture.BaselineMeanLinearLuminance));
                builder.AppendLine(prefix + "meanDeltaFromBaseline=" +
                                   FormatDouble(capture.MeanDeltaFromBaseline));
                builder.AppendLine(prefix + "presentationPostProcessing=" +
                                   capture.PresentationPostProcessing);
                builder.AppendLine(prefix + "cameraPosition=" +
                                   FormatVector3(capture.CameraPosition));
                builder.AppendLine(prefix + "cameraRotation=" +
                                   FormatQuaternion(capture.CameraRotation));
                builder.AppendLine(prefix + "fieldOfView=" +
                                   FormatFloat(capture.FieldOfView));
                builder.AppendLine(prefix + "nearClip=" + FormatFloat(capture.NearClip));
                builder.AppendLine(prefix + "farClip=" + FormatFloat(capture.FarClip));
                builder.AppendLine(prefix + "cullingMask=" + capture.CullingMask);
            }
            return builder.ToString();
        }

        private static void WriteEvidenceAtomically(
            string outputFolder,
            List<CaptureEvidence> captures,
            string manifest)
        {
            string finalAbsolute = AssetPathToAbsolutePath(outputFolder);
            string parentAbsolute = Path.GetDirectoryName(finalAbsolute);
            if (string.IsNullOrEmpty(parentAbsolute))
                throw new InvalidOperationException("Evidence parent is invalid.");
            Directory.CreateDirectory(parentAbsolute);
            if (Directory.Exists(finalAbsolute) || File.Exists(finalAbsolute))
                throw new IOException("Evidence destination already exists.");
            string stagingAbsolute = Path.Combine(
                parentAbsolute,
                ".__KEXACT_STAGING_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingAbsolute);
            bool published = false;
            try
            {
                var expected = new Dictionary<string, string>(StringComparer.Ordinal);
                var expectedLengths = new Dictionary<string, long>(StringComparer.Ordinal);
                for (int i = 0; i < captures.Count; i++)
                {
                    CaptureEvidence capture = captures[i];
                    WriteAndVerify(
                        stagingAbsolute,
                        capture.PngFilename,
                        capture.PngBytes,
                        capture.PngSha256,
                        true);
                    expected.Add(capture.PngFilename, capture.PngSha256);
                    expectedLengths.Add(
                        capture.PngFilename,
                        capture.PngBytes.LongLength);
                    WriteAndVerify(
                        stagingAbsolute,
                        capture.HdrFilename,
                        capture.HdrBytes,
                        capture.HdrSha256,
                        false);
                    expected.Add(capture.HdrFilename, capture.HdrSha256);
                    expectedLengths.Add(
                        capture.HdrFilename,
                        capture.HdrBytes.LongLength);
                }
                string manifestName = "manifest_" + Status + ".txt";
                byte[] manifestBytes = new UTF8Encoding(false).GetBytes(manifest);
                string manifestSha = ComputeSha256(manifestBytes);
                WriteAndVerify(
                    stagingAbsolute,
                    manifestName,
                    manifestBytes,
                    manifestSha,
                    null);
                expected.Add(manifestName, manifestSha);
                WriteCaptureState(
                    stagingAbsolute,
                    "STAGED",
                    "RAW_FILES_VERIFIED_BEFORE_ATOMIC_PUBLISH",
                    captures.Count,
                    null);

                Directory.Move(stagingAbsolute, finalAbsolute);
                published = true;
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                foreach (KeyValuePair<string, string> pair in expected)
                {
                    string path = outputFolder + "/" + pair.Key;
                    string absolutePath = AssetPathToAbsolutePath(path);
                    if (!File.Exists(absolutePath) ||
                        !string.Equals(
                            ComputeFileSha256(path),
                            pair.Value,
                            StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Published evidence hash mismatch: " + pair.Key);
                    if (expectedLengths.TryGetValue(pair.Key, out long expectedLength) &&
                        new FileInfo(absolutePath).Length != expectedLength)
                    {
                        throw new IOException(
                            "Published evidence byte-length mismatch: " + pair.Key);
                    }
                    if (pair.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                        pair.Key.EndsWith(".exr", StringComparison.OrdinalIgnoreCase))
                    {
                        Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        // The raw PNG/EXR header above is the authoritative source-size
                        // gate. Unity's default NPOT importer may expose a 1920x1080 PNG
                        // as a 2048x1024 Texture2D, so imported dimensions are not a valid
                        // byte-integrity assertion.
                        if (imported == null)
                            throw new IOException("Published image import failed: " + pair.Key);
                    }
                }

                WriteCaptureState(
                    finalAbsolute,
                    "COMPLETE",
                    "PUBLISHED_IMPORTED_AND_VERIFIED",
                    captures.Count,
                    null);
                AssetDatabase.ImportAsset(
                    outputFolder + "/CAPTURE_STATE.txt",
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
            }
            catch (Exception exception)
            {
                string failureFolder = published ? finalAbsolute : stagingAbsolute;
                TryWriteFailureMarker(failureFolder, exception, captures.Count);
                try
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
                catch
                {
                    // The filesystem failure marker remains authoritative.
                }
                throw;
            }
        }

        private static void WriteAndVerify(
            string folder,
            string filename,
            byte[] bytes,
            string expectedSha,
            bool? png)
        {
            if (png == true)
                VerifyPng(bytes, filename);
            else if (png == false)
                VerifyExr(bytes, filename);
            string path = Path.Combine(folder, filename);
            File.WriteAllBytes(path, bytes);
            byte[] written = File.ReadAllBytes(path);
            if (!string.Equals(
                    ComputeSha256(written),
                    expectedSha,
                    StringComparison.OrdinalIgnoreCase))
                throw new IOException("Raw evidence hash mismatch: " + filename);
            if (png == true)
                VerifyPng(written, filename);
            else if (png == false)
                VerifyExr(written, filename);
        }

        private static void WriteCaptureState(
            string folder,
            string state,
            string phase,
            int captureCount,
            Exception exception)
        {
            var builder = new StringBuilder();
            builder.AppendLine("status=" + state);
            builder.AppendLine("captureStatus=" + Status);
            builder.AppendLine("phase=" + phase);
            builder.AppendLine("utc=" + DateTime.UtcNow.ToString(
                "O",
                CultureInfo.InvariantCulture));
            builder.AppendLine("stateCameraRecordCount=" + captureCount);
            if (exception != null)
                builder.AppendLine("reason=" + SanitizeManifest(exception.Message));
            builder.AppendLine("visualFitClaimed=false");
            builder.AppendLine("visualParityClaimed=false");
            builder.AppendLine("bakeRealtimeEquivalenceClaimed=false");
            File.WriteAllText(
                Path.Combine(folder, "CAPTURE_STATE.txt"),
                builder.ToString(),
                new UTF8Encoding(false));
        }

        private static void TryWriteFailureMarker(
            string folder,
            Exception exception,
            int captureCount)
        {
            try
            {
                Directory.CreateDirectory(folder);
                WriteCaptureState(
                    folder,
                    "FAILED",
                    "STAGING_OR_PUBLISH_VERIFICATION",
                    captureCount,
                    exception);
                File.WriteAllText(
                    Path.Combine(folder, "CAPTURE_FAILED.txt"),
                    "status=FAILED\nreason=" + SanitizeManifest(exception.ToString()) + "\n",
                    new UTF8Encoding(false));
            }
            catch
            {
                // Preserve the original exception.
            }
        }

        private static void VerifyPng(byte[] bytes, string label)
        {
            if (bytes == null || bytes.Length < 24 || bytes[0] != 0x89 ||
                bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47 ||
                ReadBigEndianInt32(bytes, 16) != CaptureWidth ||
                ReadBigEndianInt32(bytes, 20) != CaptureHeight)
                throw new IOException("Invalid PNG: " + label);
        }

        private static void VerifyExr(byte[] bytes, string label)
        {
            if (bytes == null || bytes.Length < 4 || bytes[0] != 0x76 ||
                bytes[1] != 0x2F || bytes[2] != 0x31 || bytes[3] != 0x01)
                throw new IOException("Invalid EXR: " + label);
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) |
                   (bytes[offset + 2] << 8) | bytes[offset + 3];
        }

        private sealed class RenderTargets : IDisposable
        {
            public readonly RenderTexture PresentationRender;
            public readonly RenderTexture PresentationResolve;
            public readonly RenderTexture HdrRender;
            public readonly RenderTexture HdrResolve;

            private RenderTargets(
                RenderTexture presentationRender,
                RenderTexture presentationResolve,
                RenderTexture hdrRender,
                RenderTexture hdrResolve)
            {
                PresentationRender = presentationRender;
                PresentationResolve = presentationResolve;
                HdrRender = hdrRender;
                HdrResolve = hdrResolve;
            }

            public static RenderTargets Create()
            {
                int msaa = Mathf.Max(1, QualitySettings.antiAliasing);
                if (msaa != 1 && msaa != 2 && msaa != 4 && msaa != 8)
                    msaa = 1;
                RenderTexture presentationRender = null;
                RenderTexture presentationResolve = null;
                RenderTexture hdrRender = null;
                RenderTexture hdrResolve = null;
                try
                {
                    presentationRender = CreateTarget(
                        "KExact_Presentation_Render",
                        24,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.sRGB,
                        msaa);
                    presentationResolve = CreateTarget(
                        "KExact_Presentation_Resolve",
                        0,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.sRGB,
                        1);
                    hdrRender = CreateTarget(
                        "KExact_HDR_Render",
                        24,
                        RenderTextureFormat.ARGBHalf,
                        RenderTextureReadWrite.Linear,
                        1);
                    hdrResolve = CreateTarget(
                        "KExact_HDR_Resolve",
                        0,
                        RenderTextureFormat.ARGBHalf,
                        RenderTextureReadWrite.Linear,
                        1);
                    return new RenderTargets(
                        presentationRender,
                        presentationResolve,
                        hdrRender,
                        hdrResolve);
                }
                catch
                {
                    Destroy(presentationRender);
                    Destroy(presentationResolve);
                    Destroy(hdrRender);
                    Destroy(hdrResolve);
                    throw;
                }
            }

            private static RenderTexture CreateTarget(
                string name,
                int depth,
                RenderTextureFormat format,
                RenderTextureReadWrite readWrite,
                int msaa)
            {
                var target = new RenderTexture(
                    CaptureWidth,
                    CaptureHeight,
                    depth,
                    format,
                    readWrite)
                {
                    name = name,
                    hideFlags = HideFlags.HideAndDontSave,
                    antiAliasing = msaa,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                if (!target.Create())
                {
                    Object.DestroyImmediate(target);
                    throw new InvalidOperationException("Unable to create " + name + ".");
                }
                return target;
            }

            public void Dispose()
            {
                Destroy(PresentationRender);
                Destroy(PresentationResolve);
                Destroy(HdrRender);
                Destroy(HdrResolve);
            }

            private static void Destroy(RenderTexture target)
            {
                if (target == null)
                    return;
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private sealed class GlobalRenderSnapshot
        {
            private readonly LightmapData[] lightmaps;
            private readonly LightmapsMode mode;
            private readonly RenderTexture active;
            private readonly bool srgbWrite;

            private GlobalRenderSnapshot(
                LightmapData[] lightmaps,
                LightmapsMode mode,
                RenderTexture active,
                bool srgbWrite)
            {
                this.lightmaps = lightmaps;
                this.mode = mode;
                this.active = active;
                this.srgbWrite = srgbWrite;
            }

            public static GlobalRenderSnapshot Capture()
            {
                LightmapData[] current = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
                return new GlobalRenderSnapshot(
                    (LightmapData[])current.Clone(),
                    LightmapSettings.lightmapsMode,
                    RenderTexture.active,
                    GL.sRGBWrite);
            }

            public void ApplyNoLightmaps()
            {
                LightmapSettings.lightmaps = Array.Empty<LightmapData>();
                LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
            }

            public void Restore()
            {
                LightmapSettings.lightmaps = lightmaps;
                LightmapSettings.lightmapsMode = mode;
                RenderTexture.active = active;
                GL.sRGBWrite = srgbWrite;
            }

            public void AssertRestored()
            {
                LightmapData[] current = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
                if (current.Length != lightmaps.Length ||
                    LightmapSettings.lightmapsMode != mode ||
                    RenderTexture.active != active || GL.sRGBWrite != srgbWrite)
                    throw new InvalidOperationException("Global render state was not restored.");
                for (int i = 0; i < current.Length; i++)
                {
                    if (!ReferenceEquals(current[i], lightmaps[i]))
                        throw new InvalidOperationException("Global lightmaps were not restored.");
                }
            }
        }

        private sealed class SelectionSnapshot
        {
            private readonly Object[] objects;
            private readonly Object active;

            private SelectionSnapshot(Object[] objects, Object active)
            {
                this.objects = objects;
                this.active = active;
            }

            public static SelectionSnapshot Capture()
            {
                return new SelectionSnapshot(Selection.objects, Selection.activeObject);
            }

            public void RestoreIfChanged()
            {
                if (Matches())
                    return;
                Selection.objects = objects;
                Selection.activeObject = active;
            }

            public void AssertRestored()
            {
                if (!Matches())
                    throw new InvalidOperationException("Editor selection was not restored.");
            }

            private bool Matches()
            {
                Object[] current = Selection.objects;
                if (Selection.activeObject != active || current.Length != objects.Length)
                    return false;
                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i] != objects[i])
                        return false;
                }
                return true;
            }
        }

        private sealed class SourceSnapshot
        {
            private readonly Scene scene;
            private readonly GameObject root;
            private readonly string sceneSha256;
            private readonly SourceRendererSnapshot[] renderers;
            private readonly SourceComponentSnapshot[] lights;
            private readonly SourceCameraSnapshot[] cameras;

            private SourceSnapshot(
                Scene scene,
                GameObject root,
                string sceneSha256,
                SourceRendererSnapshot[] renderers,
                SourceComponentSnapshot[] lights,
                SourceCameraSnapshot[] cameras)
            {
                this.scene = scene;
                this.root = root;
                this.sceneSha256 = sceneSha256;
                this.renderers = renderers;
                this.lights = lights;
                this.cameras = cameras;
            }

            public static SourceSnapshot Capture(Scene scene, GameObject root)
            {
                Renderer[] sourceRenderers = root.GetComponentsInChildren<Renderer>(true);
                Light[] sourceLights = root.GetComponentsInChildren<Light>(true);
                Camera[] sourceCameras = root.GetComponentsInChildren<Camera>(true);
                if (sourceRenderers.Length != ExpectedRendererCount ||
                    sourceLights.Length != ExpectedTotalLightCount ||
                    sourceCameras.Length != ExpectedCameraCount)
                    throw new InvalidOperationException("Source count contract changed.");
                var renderers = new SourceRendererSnapshot[sourceRenderers.Length];
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i] = SourceRendererSnapshot.Capture(sourceRenderers[i]);
                var lights = new SourceComponentSnapshot[sourceLights.Length];
                for (int i = 0; i < lights.Length; i++)
                    lights[i] = SourceComponentSnapshot.Capture(sourceLights[i]);
                var cameras = new SourceCameraSnapshot[sourceCameras.Length];
                for (int i = 0; i < cameras.Length; i++)
                    cameras[i] = SourceCameraSnapshot.Capture(sourceCameras[i]);
                return new SourceSnapshot(
                    scene,
                    root,
                    ComputeFileSha256(ValidationScenePath),
                    renderers,
                    lights,
                    cameras);
            }

            public void AssertUnchanged()
            {
                if (!scene.IsValid() || !scene.isLoaded || scene.isDirty || root == null ||
                    root.gameObject.scene != scene || !root.activeSelf ||
                    root.transform.parent != null ||
                    !string.Equals(
                        ComputeFileSha256(ValidationScenePath),
                        sceneSha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Source scene/root changed.");
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].AssertUnchanged();
                for (int i = 0; i < lights.Length; i++)
                    lights[i].AssertUnchanged();
                for (int i = 0; i < cameras.Length; i++)
                    cameras[i].AssertUnchanged();
            }
        }

        private sealed class SourceRendererSnapshot
        {
            private readonly Renderer renderer;
            private readonly string serializedSha256;
            private readonly bool hadPropertyBlock;
            private readonly Material[] materials;
            private readonly MaterialFingerprint[] materialFingerprints;
            private readonly MeshFilter filter;
            private readonly Mesh mesh;
            private readonly MeshRenderer meshRenderer;
            private readonly Mesh additionalStreams;

            private SourceRendererSnapshot(Renderer renderer)
            {
                this.renderer = renderer;
                serializedSha256 = SerializedSha(renderer);
                hadPropertyBlock = renderer.HasPropertyBlock();
                if (hadPropertyBlock)
                {
                    throw new InvalidOperationException(
                        "Source MPB contract changed: " + GetHierarchyPath(renderer.transform));
                }
                materials = renderer.sharedMaterials;
                materialFingerprints = new MaterialFingerprint[materials.Length];
                for (int i = 0; i < materials.Length; i++)
                    materialFingerprints[i] = MaterialFingerprint.Capture(materials[i]);
                filter = renderer.GetComponent<MeshFilter>();
                mesh = filter != null ? filter.sharedMesh : null;
                meshRenderer = renderer as MeshRenderer;
                additionalStreams = meshRenderer != null
                    ? meshRenderer.additionalVertexStreams
                    : null;
            }

            public static SourceRendererSnapshot Capture(Renderer renderer)
            {
                return new SourceRendererSnapshot(renderer);
            }

            public void AssertUnchanged()
            {
                if (renderer == null || SerializedSha(renderer) != serializedSha256 ||
                    renderer.HasPropertyBlock() != hadPropertyBlock || filter == null ||
                    filter.sharedMesh != mesh || meshRenderer == null ||
                    meshRenderer.additionalVertexStreams != additionalStreams)
                    throw new InvalidOperationException("Source Renderer/MPB/mesh changed.");
                Material[] current = renderer.sharedMaterials;
                if (current.Length != materials.Length)
                    throw new InvalidOperationException("Source material slot count changed.");
                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i] != materials[i])
                        throw new InvalidOperationException("Source material reference changed.");
                    materialFingerprints[i].AssertAssetUnchanged(current[i], true);
                }
            }
        }

        private sealed class SourceComponentSnapshot
        {
            private readonly Component component;
            private readonly string serializedSha256;
            private readonly Vector3 position;
            private readonly Quaternion rotation;
            private readonly Vector3 scale;
            private readonly Component[] components;
            private readonly string[] componentSerializedSha256;

            private SourceComponentSnapshot(Component component)
            {
                this.component = component;
                serializedSha256 = SerializedSha(component);
                position = component.transform.position;
                rotation = component.transform.rotation;
                scale = component.transform.lossyScale;
                components = component.GetComponents<Component>();
                componentSerializedSha256 = new string[components.Length];
                for (int i = 0; i < components.Length; i++)
                {
                    componentSerializedSha256[i] = components[i] != null
                        ? SerializedSha(components[i])
                        : "<missing-script>";
                }
            }

            public static SourceComponentSnapshot Capture(Component component)
            {
                return new SourceComponentSnapshot(component);
            }

            public void AssertUnchanged()
            {
                if (component == null || SerializedSha(component) != serializedSha256 ||
                    component.transform.position != position ||
                    component.transform.rotation != rotation ||
                    component.transform.lossyScale != scale)
                    throw new InvalidOperationException("Source component/transform changed.");
                Component[] current = component.GetComponents<Component>();
                if (current.Length != components.Length)
                    throw new InvalidOperationException("Source component structure changed.");
                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i] != components[i] ||
                        (current[i] != null
                            ? SerializedSha(current[i])
                            : "<missing-script>") != componentSerializedSha256[i])
                    {
                        throw new InvalidOperationException(
                            "Source component ordering/serialized state changed.");
                    }
                }
            }
        }

        private sealed class SourceCameraSnapshot
        {
            private readonly Camera camera;
            private readonly SourceComponentSnapshot snapshot;
            private readonly RenderTexture target;
            private readonly float aspect;
            private readonly Matrix4x4 projection;

            private SourceCameraSnapshot(Camera camera)
            {
                this.camera = camera;
                snapshot = SourceComponentSnapshot.Capture(camera);
                target = camera.targetTexture;
                aspect = camera.aspect;
                projection = camera.projectionMatrix;
            }

            public static SourceCameraSnapshot Capture(Camera camera)
            {
                return new SourceCameraSnapshot(camera);
            }

            public void AssertUnchanged()
            {
                snapshot.AssertUnchanged();
                if (camera.targetTexture != target ||
                    !Mathf.Approximately(camera.aspect, aspect) ||
                    camera.projectionMatrix != projection)
                    throw new InvalidOperationException("Source Camera changed.");
            }
        }

        private sealed class CaptureIdentity
        {
            public readonly int QualityLevel;
            public readonly string QualityName;
            public readonly string RenderPipelinePath;
            public readonly string RenderPipelineDependencyHash;
            private readonly string toolGuid;
            private readonly string toolDependencyHash;
            private readonly string toolSha256;
            private readonly string sceneGuid;
            private readonly string sceneDependencyHash;
            private readonly string referenceGuid;
            private readonly string referenceDependencyHash;

            private CaptureIdentity(
                int qualityLevel,
                string qualityName,
                string renderPipelinePath,
                string renderPipelineDependencyHash,
                string toolGuid,
                string toolDependencyHash,
                string toolSha256,
                string sceneGuid,
                string sceneDependencyHash,
                string referenceGuid,
                string referenceDependencyHash)
            {
                QualityLevel = qualityLevel;
                QualityName = qualityName;
                RenderPipelinePath = renderPipelinePath;
                RenderPipelineDependencyHash = renderPipelineDependencyHash;
                this.toolGuid = toolGuid;
                this.toolDependencyHash = toolDependencyHash;
                this.toolSha256 = toolSha256;
                this.sceneGuid = sceneGuid;
                this.sceneDependencyHash = sceneDependencyHash;
                this.referenceGuid = referenceGuid;
                this.referenceDependencyHash = referenceDependencyHash;
            }

            public static CaptureIdentity Capture()
            {
                RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
                string pipelinePath = pipeline != null
                    ? NormalizePath(AssetDatabase.GetAssetPath(pipeline))
                    : string.Empty;
                int quality = QualitySettings.GetQualityLevel();
                string[] qualityNames = QualitySettings.names;
                return new CaptureIdentity(
                    quality,
                    quality >= 0 && quality < qualityNames.Length
                        ? qualityNames[quality]
                        : string.Empty,
                    pipelinePath,
                    string.IsNullOrEmpty(pipelinePath)
                        ? string.Empty
                        : AssetDatabase.GetAssetDependencyHash(pipelinePath).ToString(),
                    AssetDatabase.AssetPathToGUID(ToolSourcePath),
                    AssetDatabase.GetAssetDependencyHash(ToolSourcePath).ToString(),
                    ComputeFileSha256(ToolSourcePath),
                    AssetDatabase.AssetPathToGUID(ValidationScenePath),
                    AssetDatabase.GetAssetDependencyHash(ValidationScenePath).ToString(),
                    AssetDatabase.AssetPathToGUID(ReferenceManifestPath),
                    AssetDatabase.GetAssetDependencyHash(ReferenceManifestPath).ToString());
            }

            public void Append(StringBuilder builder)
            {
                builder.AppendLine("asset.tool.path=" + ToolSourcePath);
                builder.AppendLine("asset.tool.guid=" + toolGuid);
                builder.AppendLine("asset.tool.dependencyHash=" + toolDependencyHash);
                builder.AppendLine("asset.tool.sha256=" + toolSha256);
                builder.AppendLine("asset.scene.path=" + ValidationScenePath);
                builder.AppendLine("asset.scene.guid=" + sceneGuid);
                builder.AppendLine("asset.scene.dependencyHash=" + sceneDependencyHash);
                builder.AppendLine("asset.referenceManifest.path=" + ReferenceManifestPath);
                builder.AppendLine("asset.referenceManifest.guid=" + referenceGuid);
                builder.AppendLine("asset.referenceManifest.dependencyHash=" +
                                   referenceDependencyHash);
            }
        }

        private readonly struct PowerState
        {
            public readonly string Id;
            public readonly bool StartOn;
            public readonly bool AdministrativeOn;
            public bool IsP0P0 => !StartOn && !AdministrativeOn;

            public PowerState(string id, bool startOn, bool administrativeOn)
            {
                Id = id;
                StartOn = startOn;
                AdministrativeOn = administrativeOn;
            }
        }

        private readonly struct DoorPose
        {
            public readonly int Percent;
            public float Fraction => Percent / 100f;

            public DoorPose(int percent)
            {
                Percent = percent;
            }
        }

        private enum RoomRole
        {
            Start,
            Administrative
        }

        private static string SerializedSha(Object value)
        {
            return ComputeSha256(
                Encoding.UTF8.GetBytes(EditorJsonUtility.ToJson(value, false)));
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return "<null>";
            var names = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        private static bool IsBranchActiveRelativeToRoot(
            Transform transform,
            Transform root)
        {
            Transform current = transform;
            while (current != null && current != root)
            {
                if (!current.gameObject.activeSelf)
                    return false;
                current = current.parent;
            }
            if (current != root)
                throw new InvalidOperationException("Component is outside the clone root.");
            return true;
        }

        private static string GetStableComponentKey(Transform sceneRoot, Component component)
        {
            if (sceneRoot == null || component == null ||
                !component.transform.IsChildOf(sceneRoot))
                throw new InvalidOperationException("Stable component key input is invalid.");
            var segments = new List<string>();
            Transform current = component.transform;
            while (current != null)
            {
                segments.Add(
                    current.name + "[sibling=" + current.GetSiblingIndex() + "]");
                if (current == sceneRoot)
                    break;
                current = current.parent;
            }
            if (current != sceneRoot)
                throw new InvalidOperationException("Component is outside the scene root.");
            segments.Reverse();
            Component[] components = component.GetComponents<Component>();
            int ordinal = 0;
            bool found = false;
            for (int i = 0; i < components.Length; i++)
            {
                Component candidate = components[i];
                if (candidate == null || candidate.GetType() != component.GetType())
                    continue;
                if (candidate == component)
                {
                    found = true;
                    break;
                }
                ordinal++;
            }
            if (!found)
                throw new InvalidOperationException("Component ordinal cannot be resolved.");
            return string.Join("/", segments) + "|" +
                   component.GetType().FullName + "[ordinal=" + ordinal + "]";
        }

        private static string CameraToken(string cameraName)
        {
            if (cameraName == StartCameraName)
                return "Start_to_Admin";
            if (cameraName == AdministrativeCameraName)
                return "Admin_to_Start";
            throw new InvalidOperationException("Unexpected fixed Camera name.");
        }

        private static string AssetPathToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string ComputeFileSha256(string assetPath)
        {
            using (FileStream stream = File.OpenRead(AssetPathToAbsolutePath(assetPath)))
            using (SHA256 sha = SHA256.Create())
                return BytesToHex(sha.ComputeHash(stream));
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return BytesToHex(sha.ComputeHash(bytes));
        }

        private static string BytesToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        private static string SanitizeManifest(string value)
        {
            return (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatVector2(Vector2 value)
        {
            return "(" + FormatFloat(value.x) + "," + FormatFloat(value.y) + ")";
        }

        private static string FormatVector3(Vector3 value)
        {
            return "(" + FormatFloat(value.x) + "," + FormatFloat(value.y) + "," +
                   FormatFloat(value.z) + ")";
        }

        private static string FormatQuaternion(Quaternion value)
        {
            return "(" + FormatFloat(value.x) + "," + FormatFloat(value.y) + "," +
                   FormatFloat(value.z) + "," + FormatFloat(value.w) + ")";
        }

        private static string FormatColor(Color value)
        {
            return "(" + FormatFloat(value.r) + "," + FormatFloat(value.g) + "," +
                   FormatFloat(value.b) + "," + FormatFloat(value.a) + ")";
        }
    }
}
