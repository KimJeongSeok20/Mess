using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

namespace DungeonPortalTransportPoC.GroundTruth.JointPairTotalPilot
{
    /// <summary>
    /// Builds four isolated, pair-specific baked oracle states with the real door at
    /// 100% open: P0/P0, P100/P0, P0/P100, and P100/P100.
    /// The result is deliberately labelled a candidate: it is an O(N^2) comparison
    /// oracle, not the O(N) runtime solution.
    ///
    /// Production scenes, prefabs, lighting settings, TileSets, Flows, and MapLists
    /// are read-only inputs. The only scene ever saved is the token-owned copied
    /// scene under JointPairTotalPilot/Generated/&lt;runId&gt;.
    /// </summary>
    public static class DungeonPortalJointPairTotalPilotCapture
    {
        public const string Status = "JOINT_PAIR_TOTAL_GT_CANDIDATE";
        public const string BaselineStateId = "P000_P000__Door100";
        public const string StartPoweredStateId = "P100_P000__Door100";
        public const string AdministrativePoweredStateId = "P000_P100__Door100";
        public const string BothPoweredStateId = "P100_P100__Door100";

        private static readonly PilotState[] PilotStates =
        {
            new PilotState(BaselineStateId, false, false, true),
            new PilotState(StartPoweredStateId, true, false, false),
            new PilotState(AdministrativePoweredStateId, false, true, false),
            new PilotState(BothPoweredStateId, true, true, false)
        };

        private const string ToolSourcePath =
            "Assets/Experiments/DungeonPortalTransportPoC/GroundTruth/" +
            "JointPairTotalPilot/Editor/DungeonPortalJointPairTotalPilotCapture.cs";
        private const string ValidationScenePath =
            "Assets/Experiments/DungeonPortalTransportPoC/Scenes/" +
            "Start_Admin_PortalTransportValidation.unity";
        private const string ExpectedValidationSceneRawSha256 =
            "F087DD274822D9F8071CE8ADD0E261BC5314B2999EF4BDF3B1C1CB1B5DC4AF79";
        private const string CorrectedRealtimeManifestPath =
            "Assets/Experiments/DungeonPortalTransportPoC/Evidence/GroundTruth/" +
            "RealtimeProductionState/20260823T172008115Z/" +
            "manifest_REALTIME_DIRECT_POWER_EMISSION_GT_ONLY.txt";
        private const string CorrectedRealtimeEvidenceRoot =
            "Assets/Experiments/DungeonPortalTransportPoC/Evidence/GroundTruth/" +
            "RealtimeProductionState/20260823T172008115Z";
        private const string ExpectedCorrectedRealtimeManifestRawSha256 =
            "46DF14C9318C53FB0180D0F05F086DEF08E1B8993970E2642B1A9EDEB6941138";
        private const string ExpectedCorrectedRealtimeStatus =
            "REALTIME_DIRECT_POWER_EMISSION_GT_ONLY";
        private const string LightingSettingsPath = "Assets/New Lighting Settings.lighting";
        private const string StartPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/StartRoom_R000.prefab";
        private const string AdministrativePrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/" +
            "AdminstrativeSegregation_R000.prefab";
        private const string DoorPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/SomePicees/" +
            "Door_SM_A_Door_Placement.prefab";

        private const string GeneratedRoot =
            "Assets/Experiments/DungeonPortalTransportPoC/GroundTruth/" +
            "JointPairTotalPilot/Generated";
        private const string EvidenceRoot =
            "Assets/Experiments/DungeonPortalTransportPoC/Evidence/GroundTruth/" +
            "JointPairTotalPilot";
        private const string CopiedSceneFilename = "J.unity";
        private const string CopiedLightingSettingsFilename = "L.lighting";
        private const string GeneratedSceneArtifactFolderName = "J";

        private const string ValidationRootName = "PortalTransportValidation";
        private const string ProductionRoomsRootName = "01_ProductionRooms";
        private const string PortalTransportRootName = "02_PortalTransportPoC";
        private const string CamerasRootName = "03_FixedValidationCameras_DISABLED";
        private const string VolumeRootName = "04_ValidationGlobalVolume_StartMapParity";
        private const string StartRoomName = "StartRoom_R000_ProductionInstance";
        private const string AdministrativeRoomName =
            "AdminstrativeSegregation_R000_ProductionInstance";
        private const string AddedDoorRelativePath =
            "Doorways/Door_SM_A/DoorWayPoint/" +
            "Door_SM_A_Door_Placement_ActiveSceneInstance";
        private const string DoorLeafName = "Door_01";
        private const string DoorPositiveRendererName = "DungeonDoorProbe_PositiveZ";
        private const string DoorNegativeRendererName = "DungeonDoorProbe_NegativeZ";
        private const string DoorEdgeRendererName = "DungeonDoorProbe_Edge";
        private const string StartCameraName = "Start_to_Admin_FixedCamera_DISABLED";
        private const string AdministrativeCameraName =
            "Admin_to_Start_FixedCamera_DISABLED";

        private const int ExpectedValidationRootChildren = 4;
        private const int ExpectedRendererCount = 389;
        private const int ExpectedStartLightCount = 24;
        private const int ExpectedAdministrativeLightCount = 54;
        private const int ExpectedStartIgnoredLightCount = 0;
        private const int ExpectedAdministrativeIgnoredLightCount = 2;
        private const int ExpectedDoorRendererCount = 4;
        private const int ExpectedDoorEnabledRendererCount = 3;
        private const int ExpectedCameraCount = 2;
        private const int ExpectedStartProbeCount = 82;
        private const int ExpectedAdministrativeProbeCount = 128;
        private const int ExpectedDoorProbeCount = 8;
        private const int ExpectedTotalProbeCount =
            ExpectedStartProbeCount + ExpectedAdministrativeProbeCount + ExpectedDoorProbeCount;
        private const int ExpectedStartP100ReflectionCount = 1;
        private const int ExpectedAdministrativeP100ReflectionCount = 4;
        private const int ExpectedTotalP100ReflectionCount =
            ExpectedStartP100ReflectionCount + ExpectedAdministrativeP100ReflectionCount;
        private const int ExpectedSourceReflectionProbeCount =
            ExpectedTotalP100ReflectionCount * 2;
        private const int CaptureWidth = 1920;
        private const int CaptureHeight = 1080;
        private const int LegacyMaxPathExclusive = 260;
        private const int UnityImportSafeAbsolutePathMax = 248;
        private const float DoorSideSampleOffset = 0.35f;
        private const double MinimumValidMeanLinearLuminance = 1e-12d;
        private const float MinimumValidMaxLinearLuminance = 1e-8f;
        private static readonly Vector3 ExpectedStartCameraPosition =
            new Vector3(2.9999993f, 1.6700006f, 5.196489f);
        private static readonly Quaternion ExpectedStartCameraRotation =
            new Quaternion(0f, 0.7071068f, 0f, -0.7071068f);
        private static readonly Vector3 ExpectedAdministrativeCameraPosition =
            new Vector3(-3.0000012f, 1.6700006f, 5.1964884f);
        private static readonly Quaternion ExpectedAdministrativeCameraRotation =
            new Quaternion(0f, 0.7071068f, 0f, 0.7071068f);

        [MenuItem(
            "Tools/Dungeon/Lighting/Portal Transport PoC/" +
                    "Bake JOINT PAIR TOTAL Pilot (Four Power States Door100)")]
        public static void CaptureFromMenu()
        {
            Debug.Log(CapturePilot());
        }

        /// <summary>
        /// Executes the four-state full-open pilot. This method is intentionally not called by
        /// any initialize hook, test, builder, or batch command.
        /// </summary>
        public static string CapturePilot()
        {
            EditorSessionSnapshot session = null;
            RunArtifactTransaction transaction = null;
            SourceAssetIdentity[] sourceAssets = null;
            CorrectedRealtimeReference correctedRealtimeReference = null;
            string sourceStateFingerprint = string.Empty;
            PilotEvidence evidence = null;
            RenderFailureMonitor renderFailureMonitor = null;
            Exception workFailure = null;
            Exception restorationFailure = null;
            bool restoredAndVerified = false;

            try
            {
                renderFailureMonitor = new RenderFailureMonitor();
                Scene sourceScene = ValidateExactCleanPreflight();
                correctedRealtimeReference =
                    CorrectedRealtimeReference.CaptureAndValidate();
                GameObject sourceRoot = FindUniqueRoot(sourceScene, ValidationRootName);
                SourceContract sourceContract = SourceContract.ValidateAndCapture(
                    sourceScene,
                    sourceRoot);

                session = EditorSessionSnapshot.Capture(
                    sourceContract.CameraFingerprint,
                    sourceRoot);
                renderFailureMonitor.ThrowIfFailed("read-only source preflight");
                sourceStateFingerprint = ComputeSceneStateFingerprint(sourceScene, sourceRoot);
                sourceAssets = CaptureSourceAssetIdentities(
                    sourceContract,
                    correctedRealtimeReference);

                string runId = DateTime.UtcNow.ToString(
                    "yyyyMMdd'T'HHmmssfff'Z'",
                    CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N");
                transaction = RunArtifactTransaction.Begin(runId);
                evidence = BuildBakeAndCapture(
                    transaction,
                    sourceAssets,
                    sourceStateFingerprint,
                    renderFailureMonitor,
                    correctedRealtimeReference);
                renderFailureMonitor.ThrowIfFailed("joint-pair capture completion");
            }
            catch (Exception exception)
            {
                workFailure = exception;
            }
            finally
            {
                if (transaction != null && transaction.BakeStartedByThisRun && Lightmapping.isRunning)
                {
                    try
                    {
                        Lightmapping.Cancel();
                    }
                    catch (Exception cancelFailure)
                    {
                        workFailure = CombineFailures(workFailure, cancelFailure, "bake cancellation");
                    }
                }

                if (session != null)
                {
                    try
                    {
                        session.Restore();
                        session.AssertRestored();

                        Scene restoredScene = SceneManager.GetActiveScene();
                        GameObject restoredRoot = FindUniqueRoot(restoredScene, ValidationRootName);
                        string restoredFingerprint = ComputeSceneStateFingerprint(restoredScene, restoredRoot);
                        if (!string.Equals(
                                restoredFingerprint,
                                sourceStateFingerprint,
                                StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                "Restored source-scene state fingerprint differs from preflight.");
                        }

                        AssertSourceAssetsUnchanged(sourceAssets);
                        renderFailureMonitor?.ThrowIfFailed("source-state restoration");
                        restoredAndVerified = true;
                    }
                    catch (Exception exception)
                    {
                        restorationFailure = exception;
                    }
                }

                renderFailureMonitor?.Dispose();
            }

            if (session == null)
            {
                return "FAIL " + Status + " preflight (no mutation): " + workFailure;
            }

            if (restorationFailure != null || !restoredAndVerified)
            {
                string message =
                    "RESTORE_FAILED_BLOCKED: token-owned artifacts were preserved; " +
                    "automatic cleanup was not attempted. restoration=" + restorationFailure +
                    " work=" + workFailure;
                transaction?.WriteRestoreFailedMarker(message);
                transaction?.PreserveFailedStaging(message);
                return "FAIL " + Status + "\n" + message +
                       (transaction != null
                           ? "\ngeneratedStaging=" + transaction.GeneratedWorkingPath +
                             "\nevidenceStaging=" + transaction.EvidenceWorkingPath
                           : string.Empty);
            }

            if (workFailure != null || evidence == null)
            {
                string preserveFailure = transaction != null
                    ? transaction.PreserveFailedStaging(workFailure?.ToString() ?? "No evidence produced.")
                    : string.Empty;
                return "FAIL " + Status + ": " + workFailure +
                       (string.IsNullOrEmpty(preserveFailure)
                           ? "\nstagingArtifactsPreserved=true\n" +
                             "generatedStaging=" + transaction?.GeneratedWorkingPath + "\n" +
                             "evidenceStaging=" + transaction?.EvidenceWorkingPath
                           : "\nstagingArtifactsPreserved=unknown\npreserveFailure=" + preserveFailure);
            }

            try
            {
                AssertGeneratedArtifactsUnchanged(evidence);
                transaction.WriteEvidence(evidence);
                AssertSourceAssetsUnchanged(sourceAssets);
                AssertGeneratedArtifactsUnchanged(evidence);
                session.AssertRestored();
                transaction.Commit(
                    evidence,
                    () =>
                    {
                        AssertSourceAssetsUnchanged(sourceAssets);
                        session.AssertRestored();
                    });
                return
                    Status + "\n" +
                    "states=" + string.Join(",", PilotStates.Select(state => state.Id)) + "\n" +
                    "generated=" + transaction.GeneratedRunPath + "\n" +
                    "evidence=" + transaction.EvidenceRunPath + "\n" +
                    "stateRecords=" + evidence.States.Count + "\n" +
                    "cameraRecords=" + evidence.States.Sum(state => state.Images.Count) + "\n" +
                    "lightmaps=" + evidence.States.Sum(state => state.Lightmaps.Count) + "\n" +
                    "probeSamples=" + evidence.States.Sum(state => state.ProbeSamples.samples.Length) + "\n" +
                    "reflectionCubemaps=" + evidence.States.Sum(state => state.Reflections.Count) + "\n" +
                    "visualVerdict=UNREVIEWED\n" +
                    "equivalence=false";
            }
            catch (Exception exception)
            {
                string preserveFailure = transaction.PreserveFailedStaging(exception.ToString());
                return "FAIL " + Status + " evidence commit: " + exception +
                       (string.IsNullOrEmpty(preserveFailure)
                           ? "\nstagingArtifactsPreserved=true\n" +
                             "generatedStaging=" + transaction.GeneratedWorkingPath + "\n" +
                             "evidenceStaging=" + transaction.EvidenceWorkingPath
                           : "\nstagingArtifactsPreserved=unknown\npreserveFailure=" + preserveFailure);
            }
        }

        private static PilotEvidence BuildBakeAndCapture(
            RunArtifactTransaction transaction,
            SourceAssetIdentity[] sourceAssets,
            string sourceStateFingerprint,
            RenderFailureMonitor renderFailureMonitor,
            CorrectedRealtimeReference correctedRealtimeReference)
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));

            var stateEvidence = new List<PilotStateEvidence>(PilotStates.Length);
            for (int i = 0; i < PilotStates.Length; i++)
            {
                stateEvidence.Add(CapturePilotState(
                    transaction,
                    PilotStates[i],
                    sourceAssets,
                    renderFailureMonitor));
            }

            if (stateEvidence.Count != PilotStates.Length)
                throw new InvalidOperationException("All four full-open pilot states were not captured.");

            var evidence = new PilotEvidence
            {
                RunId = transaction.RunId,
                GeneratedRunPath = transaction.GeneratedRunPath,
                EvidenceRunPath = transaction.EvidenceRunPath,
                GeneratedWorkingPath = transaction.GeneratedWorkingPath,
                EvidenceWorkingPath = transaction.EvidenceWorkingPath,
                SourceAssets = sourceAssets,
                CorrectedRealtimeReference = correctedRealtimeReference,
                SourceStateFingerprint = sourceStateFingerprint,
                States = stateEvidence
            };
            ValidatePilotEvidence(evidence);
            evidence.Manifest = BuildManifest(evidence);
            evidence.SourceSnapshotText = BuildSourceSnapshot(evidence);
            return evidence;
        }

        private static PilotStateEvidence CapturePilotState(
            RunArtifactTransaction transaction,
            PilotState state,
            SourceAssetIdentity[] sourceAssets,
            RenderFailureMonitor renderFailureMonitor)
        {
            StateOwnedPaths paths = transaction.CopyOwnedInputs(
                state.Id,
                ValidationScenePath,
                LightingSettingsPath);

            Scene scene = EditorSceneManager.OpenScene(
                paths.CopiedScenePath,
                OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded ||
                !string.Equals(NormalizePath(scene.path), paths.CopiedScenePath, StringComparison.Ordinal) ||
                SceneManager.sceneCount != 1 ||
                !string.Equals(
                    NormalizePath(SceneManager.GetActiveScene().path),
                    paths.CopiedScenePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The token-owned copied scene was not opened as the single active scene.");
            }
            if (SceneManager.GetSceneByPath(ValidationScenePath).isLoaded)
            {
                throw new InvalidOperationException(
                    "The source validation scene remained loaded additively.");
            }

            GameObject validationRoot = FindUniqueRoot(scene, ValidationRootName);
            ValidateCopiedRootShape(validationRoot);
            Transform productionRooms = FindUniqueDescendant(
                validationRoot.transform,
                ProductionRoomsRootName,
                true);
            Transform portalRoot = FindUniqueDescendant(
                validationRoot.transform,
                PortalTransportRootName,
                true);
            Transform camerasRoot = FindUniqueDescendant(
                validationRoot.transform,
                CamerasRootName,
                true);
            Transform volumeRoot = FindUniqueDescendant(
                validationRoot.transform,
                VolumeRootName,
                true);
            GameObject startRoom = FindUniqueDescendant(
                productionRooms,
                StartRoomName,
                true).gameObject;
            GameObject administrativeRoom = FindUniqueDescendant(
                productionRooms,
                AdministrativeRoomName,
                true).gameObject;

            DoorBakeContract door = DoorBakeContract.Capture(
                portalRoot.gameObject,
                startRoom,
                transaction.Token);
            Camera[] cameras = ResolveFixedCameras(camerasRoot);
            EnvironmentContract environment = EnvironmentContract.CaptureAndAssert(volumeRoot);

            Object.DestroyImmediate(portalRoot.gameObject);
            if (FindOptionalDirectChild(validationRoot.transform, PortalTransportRootName) != null)
                throw new InvalidOperationException("PoC root removal failed in the copied scene.");
            if (validationRoot.transform.childCount != ExpectedValidationRootChildren - 1)
                throw new InvalidOperationException("Unexpected copied-scene root shape after PoC removal.");

            RoomRendererContract rendererContract = RoomRendererContract.Capture(
                startRoom,
                administrativeRoom);
            PowerApplyResult startPower = ApplyRoomPower(
                startRoom,
                state.StartPower100,
                state.StartPower100 ? "Start P100" : "Start P0");
            PowerApplyResult administrativePower = ApplyRoomPower(
                administrativeRoom,
                state.AdministrativePower100,
                state.AdministrativePower100 ? "Administrative P100" : "Administrative P0");
            rendererContract.AssertPowerMaterials(startPower, administrativePower);

            door.ApplyFullyOpenAndCreateBakeOccluder();
            ProbeStencil probeStencil = ProbeStencil.Create(
                validationRoot,
                startRoom,
                administrativeRoom,
                door);
            ReflectionBakeContract reflections = ReflectionBakeContract.Prepare(
                startRoom,
                administrativeRoom,
                state,
                paths.StateGeneratedPath + "/" + GeneratedSceneArtifactFolderName);

            LightingSettings copiedLightingSettings =
                AssetDatabase.LoadAssetAtPath<LightingSettings>(paths.CopiedLightingSettingsPath);
            if (copiedLightingSettings == null)
                throw new InvalidOperationException("Copied LightingSettings asset did not load.");
            transaction.AssertCopiedLightingSettingsEquivalent(paths, copiedLightingSettings);
            AssertNoShadowmaskSetting(copiedLightingSettings);

            Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;
            Lightmapping.lightingSettings = copiedLightingSettings;
            Lightmapping.lightingDataAsset = null;
            LightmapSettings.lightmaps = Array.Empty<LightmapData>();
            Lightmapping.Clear();

            environment.AssertUnchanged(volumeRoot);
            door.AssertBakeState();
            probeStencil.AssertPrepared();
            reflections.AssertPrepared();
            rendererContract.AssertPowerMaterials(startPower, administrativePower);
            AssertRendererIdentityUniquenessPreflight(
                startRoom,
                administrativeRoom,
                state.Id);
            AssertOnlyCopiedSceneCanBeSaved(scene, paths.CopiedScenePath);
            if (!EditorSceneManager.SaveScene(scene, paths.CopiedScenePath, false))
                throw new InvalidOperationException("Failed to save the prepared copied scene.");

            DateTime bakeStartedUtc = DateTime.UtcNow;
            transaction.BakeStartedByThisRun = true;
            bool baked;
            try
            {
                baked = Lightmapping.Bake();
            }
            finally
            {
                // A synchronous bake normally clears isRunning before returning.
                // If Unity leaves it active after an exception/false return, retain
                // ownership so the outer failure path cancels only this run's bake.
                transaction.BakeStartedByThisRun = Lightmapping.isRunning;
            }
            TimeSpan bakeElapsed = DateTime.UtcNow - bakeStartedUtc;
            if (!baked || Lightmapping.isRunning)
            {
                throw new InvalidOperationException(
                    "Synchronous Lightmapping.Bake did not complete successfully.");
            }
            transaction.AssertGeneratedStateTreePublishSafe(paths.StateGeneratedPath);

            LightingDataAsset ownedLightingData = Lightmapping.lightingDataAsset;
            if (ownedLightingData == null)
                throw new InvalidOperationException("Bake did not produce a LightingDataAsset.");
            string ownedLightingDataPath = NormalizePath(
                AssetDatabase.GetAssetPath(ownedLightingData));
            AssertOwnedGeneratedAsset(
                ownedLightingDataPath,
                paths.StateGeneratedPath + "/" + GeneratedSceneArtifactFolderName,
                "lighting data asset");
            string ownedLightingDataSha256 = ComputeFileSha256(ownedLightingDataPath);
            string ownedLightingDataDependencyHash =
                AssetDatabase.GetAssetDependencyHash(ownedLightingDataPath).ToString();

            LightmapArtifact[] lightmaps = CaptureLightmapArtifacts(
                paths.StateGeneratedPath + "/" + GeneratedSceneArtifactFolderName);
            rendererContract.AssertPowerMaterials(startPower, administrativePower);
            RendererMapFile rendererMap = BuildRendererMap(
                startRoom,
                administrativeRoom,
                state.Id);
            ProbeSampleFile probeSamples = probeStencil.SampleAfterBake(door, state.Id);
            ReflectionArtifact[] reflectionArtifacts = reflections.CaptureAfterBake();

            door.EndBakeOcclusionAndRestoreDynamicRenderers();
            door.AssertPresentationState();
            environment.AssertUnchanged(volumeRoot);
            reflections.AssertBakedPresentationState();

            List<BufferedImage> images;
            using (DoorProbeOverride doorProbeOverride = DoorProbeOverride.Apply(door))
            using (RenderTargets targets = RenderTargets.Create())
            {
                images = new List<BufferedImage>(ExpectedCameraCount);
                for (int i = 0; i < cameras.Length; i++)
                    images.Add(CaptureCamera(
                        cameras[i],
                        scene,
                        targets,
                        state.Id,
                        renderFailureMonitor));
            }

            door.AssertPresentationState();
            rendererContract.AssertPowerMaterials(startPower, administrativePower);
            rendererContract.AssertPropertyBlockPresenceRestored();
            rendererContract.AssertProductionSurfaceStateRestored();
            environment.AssertUnchanged(volumeRoot);
            probeStencil.AssertPrepared();

            AssertOnlyCopiedSceneCanBeSaved(scene, paths.CopiedScenePath);
            if (!EditorSceneManager.SaveScene(scene, paths.CopiedScenePath, false))
                throw new InvalidOperationException("Failed to save final token-owned copied scene.");
            if (scene.isDirty)
                throw new InvalidOperationException("Copied scene remained dirty after its final save.");

            string copiedSceneSha256 = ComputeFileSha256(paths.CopiedScenePath);
            string copiedSceneDependencyHash =
                AssetDatabase.GetAssetDependencyHash(paths.CopiedScenePath).ToString();
            string copiedLightingSettingsSha256 =
                ComputeFileSha256(paths.CopiedLightingSettingsPath);
            string copiedLightingSettingsDependencyHash =
                AssetDatabase.GetAssetDependencyHash(paths.CopiedLightingSettingsPath).ToString();

            AssertSourceAssetsUnchanged(sourceAssets);

            var evidence = new PilotStateEvidence
            {
                State = state,
                CopiedScenePath = paths.CopiedScenePath,
                CopiedLightingSettingsPath = paths.CopiedLightingSettingsPath,
                SourceSceneGuid = paths.SourceSceneGuid,
                CopiedSceneGuid = paths.CopiedSceneGuid,
                SourceLightingSettingsGuid = paths.SourceLightingSettingsGuid,
                CopiedLightingSettingsGuid = paths.CopiedLightingSettingsGuid,
                CopiedSceneRawSha256 = copiedSceneSha256,
                CopiedSceneDependencyHash = copiedSceneDependencyHash,
                CopiedLightingSettingsRawSha256 = copiedLightingSettingsSha256,
                CopiedLightingSettingsDependencyHash = copiedLightingSettingsDependencyHash,
                LightingDataPath = ownedLightingDataPath,
                LightingDataRawSha256 = ownedLightingDataSha256,
                LightingDataDependencyHash = ownedLightingDataDependencyHash,
                CopyStateFingerprint = ComputeSceneStateFingerprint(scene, validationRoot),
                LightingSettingsCanonicalSerializedFingerprint =
                    paths.LightingSettingsCanonicalSerializedFingerprint,
                Environment = environment,
                StartPower = startPower,
                AdministrativePower = administrativePower,
                Door = door,
                ProbeStencil = probeStencil,
                ProbeSamples = probeSamples,
                RendererMap = rendererMap,
                Lightmaps = new List<LightmapArtifact>(lightmaps),
                Reflections = new List<ReflectionArtifact>(reflectionArtifacts),
                Images = images,
                BakeStartedUtc = bakeStartedUtc,
                BakeElapsed = bakeElapsed
            };
            evidence.ProbeJson = JsonUtility.ToJson(probeSamples, true);
            evidence.RendererMapJson = JsonUtility.ToJson(rendererMap, true);
            return evidence;
        }

        private static Scene ValidateExactCleanPreflight()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exact Edit Mode is required.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Wait for compilation/import to finish.");
            if (Lightmapping.isRunning)
                throw new InvalidOperationException("Another lightmap bake is running.");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Close Prefab Mode before running the pilot.");
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32) ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.RGBA32) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf))
            {
                throw new InvalidOperationException("Required PNG/EXR render formats are unavailable.");
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene) ||
                !string.Equals(NormalizePath(scene.path), ValidationScenePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The exact validation scene must already be active: " + ValidationScenePath);
            }
            if (scene.isDirty)
                throw new InvalidOperationException("The validation scene must be clean.");
            if (SceneManager.sceneCount != 1 || CountLoadedNonPreviewScenes() != 1)
                throw new InvalidOperationException("Exactly one non-preview scene may be loaded.");

            RequireAsset<SceneAsset>(ValidationScenePath);
            RequireAsset<LightingSettings>(LightingSettingsPath);
            RequireAsset<GameObject>(StartPrefabPath);
            RequireAsset<GameObject>(AdministrativePrefabPath);
            RequireAsset<GameObject>(DoorPrefabPath);
            RequireAsset<MonoScript>(ToolSourcePath);
            AssertFileHash(ValidationScenePath, ExpectedValidationSceneRawSha256);
            RequireAsset<TextAsset>(CorrectedRealtimeManifestPath);
            return scene;
        }

        private static string ReadUniqueManifestValue(string text, string key)
        {
            string prefix = key + "=";
            string result = null;
            string[] lines = (text ?? string.Empty).Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                if (result != null)
                    throw new InvalidOperationException("Duplicate manifest key: " + key);
                result = lines[i].Substring(prefix.Length);
            }
            if (result == null)
                throw new InvalidOperationException("Missing manifest key: " + key);
            return result;
        }

        private static void ValidateCopiedRootShape(GameObject root)
        {
            if (root == null || !root.activeInHierarchy || root.transform.parent != null ||
                root.transform.childCount != ExpectedValidationRootChildren)
            {
                throw new InvalidOperationException(
                    "Copied validation root does not match the four-child source contract.");
            }
            FindUniqueDescendant(root.transform, ProductionRoomsRootName, true);
            FindUniqueDescendant(root.transform, PortalTransportRootName, true);
            FindUniqueDescendant(root.transform, CamerasRootName, true);
            FindUniqueDescendant(root.transform, VolumeRootName, true);
        }

        private static Camera[] ResolveFixedCameras(Transform camerasRoot)
        {
            if (camerasRoot == null || !camerasRoot.gameObject.scene.IsValid() ||
                !camerasRoot.gameObject.scene.isLoaded)
            {
                throw new InvalidOperationException("Fixed-camera root must belong to a loaded scene.");
            }
            Camera start = FindUniqueDescendant(camerasRoot, StartCameraName, false)
                .GetComponent<Camera>();
            Camera administrative = FindUniqueDescendant(
                camerasRoot,
                AdministrativeCameraName,
                false).GetComponent<Camera>();
            if (start == null || administrative == null)
                throw new InvalidOperationException("Both fixed Camera components are required.");
            Camera[] cameras = camerasRoot.GetComponentsInChildren<Camera>(true);
            if (cameras.Length != ExpectedCameraCount)
                throw new InvalidOperationException("Expected exactly two fixed cameras.");
            ValidateFixedCameraContract(start, 0, camerasRoot);
            ValidateFixedCameraContract(administrative, 1, camerasRoot);
            return new[] { start, administrative };
        }

        private static void ValidateFixedCameraContract(
            Camera camera,
            int index,
            Transform camerasRoot)
        {
            string expectedName = index == 0
                ? StartCameraName
                : index == 1
                    ? AdministrativeCameraName
                    : throw new ArgumentOutOfRangeException(nameof(index));
            Vector3 expectedPosition = index == 0
                ? ExpectedStartCameraPosition
                : ExpectedAdministrativeCameraPosition;
            Quaternion expectedRotation = index == 0
                ? ExpectedStartCameraRotation
                : ExpectedAdministrativeCameraRotation;
            Scene expectedScene = camerasRoot.gameObject.scene;
            if (camera == null ||
                !string.Equals(camera.name, expectedName, StringComparison.Ordinal) ||
                camera.gameObject.scene != expectedScene ||
                !camera.transform.IsChildOf(camerasRoot) ||
                camera.enabled || camera.targetTexture != null || camera.orthographic ||
                camera.usePhysicalProperties || !camera.allowHDR || !camera.allowMSAA ||
                Vector3.Distance(camera.transform.position, expectedPosition) > 0.00001f ||
                Quaternion.Angle(camera.transform.rotation, expectedRotation) > 0.001f ||
                Vector3.Distance(camera.transform.localScale, Vector3.one) > 0.00001f ||
                !Mathf.Approximately(camera.fieldOfView, 90f) ||
                Mathf.Abs(camera.nearClipPlane - 0.01f) > 0.000001f ||
                !Mathf.Approximately(camera.farClipPlane, 1000f) ||
                camera.cullingMask != 262135)
            {
                throw new InvalidOperationException(
                    "Fixed camera exact scene/pose/projection contract changed: " + expectedName);
            }
            AssertStandardPerspectiveProjection(camera);
            Component cameraData = FindUniqueUrpCameraData(camera);
            PropertyInfo postProperty = cameraData.GetType().GetProperty(
                "renderPostProcessing",
                BindingFlags.Instance | BindingFlags.Public);
            if (postProperty == null || postProperty.PropertyType != typeof(bool) ||
                !postProperty.CanRead || !(bool)postProperty.GetValue(cameraData))
            {
                throw new InvalidOperationException(
                    "Fixed camera must preserve enabled URP post-processing: " + expectedName);
            }
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
            {
                for (int column = 0; column < 4; column++)
                {
                    if (Mathf.Abs(actual[row, column] - expected[row, column]) > 0.00001f)
                    {
                        throw new InvalidOperationException(
                            "Fixed camera projection is not the standard perspective matrix.");
                    }
                }
            }
        }

        private static SourceAssetIdentity[] CaptureSourceAssetIdentities(
            SourceContract source,
            CorrectedRealtimeReference correctedRealtimeReference)
        {
            if (correctedRealtimeReference == null)
                throw new ArgumentNullException(nameof(correctedRealtimeReference));
            var paths = new List<string>
            {
                ValidationScenePath,
                LightingSettingsPath,
                StartPrefabPath,
                AdministrativePrefabPath,
                DoorPrefabPath,
                ToolSourcePath,
                CorrectedRealtimeManifestPath,
                AssetDatabase.GetAssetPath(source.StartPowerSet.Power00Bake),
                AssetDatabase.GetAssetPath(source.StartPowerSet.Power100Bake),
                AssetDatabase.GetAssetPath(source.AdministrativePowerSet.Power00Bake),
                AssetDatabase.GetAssetPath(source.AdministrativePowerSet.Power100Bake)
            };
            paths.AddRange(correctedRealtimeReference.ReferencedAssetPaths);
            return paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(SourceAssetIdentity.Capture)
                .ToArray();
        }

        private static void AssertSourceAssetsUnchanged(SourceAssetIdentity[] identities)
        {
            if (identities == null)
                throw new InvalidOperationException("Source asset identity set is missing.");
            for (int i = 0; i < identities.Length; i++)
                identities[i].AssertUnchanged();
        }

        private static Exception CombineFailures(Exception first, Exception second, string stage)
        {
            if (first == null)
                return new InvalidOperationException(stage + " failed.", second);
            return new AggregateException(first, new InvalidOperationException(stage + " failed.", second));
        }

        private static PowerApplyResult ApplyRoomPower(GameObject room, bool power100, string label)
        {
            DungeonTilePowerBakeSet set = room.GetComponent<DungeonTilePowerBakeSet>();
            if (set == null)
                throw new InvalidOperationException(label + " is missing DungeonTilePowerBakeSet.");

            DungeonTileBakeData selectedBake = power100 ? set.Power100Bake : set.Power00Bake;
            if (selectedBake == null)
                throw new InvalidOperationException(label + " selected bake data is null.");

            Light[] lights = room.GetComponentsInChildren<Light>(true);
            int ignoredCount = 0;
            int enabledAfter = 0;
            float scale = power100
                ? set.Power100LightIntensityScale
                : set.Power00LightIntensityScale;
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                bool baseEnabled = light.enabled;
                float baseIntensity = light.intensity;
                bool ignored = HasEnabledIgnoreLightControl(light);
                if (ignored)
                {
                    ignoredCount++;
                }
                else if (scale <= 0.0001f)
                {
                    light.enabled = false;
                }
                else if (baseEnabled)
                {
                    light.intensity = baseIntensity * scale;
                }

                if (light.enabled)
                    enabledAfter++;
            }

            int expectedLights = string.Equals(room.name, StartRoomName, StringComparison.Ordinal)
                ? ExpectedStartLightCount
                : ExpectedAdministrativeLightCount;
            int expectedIgnored = string.Equals(room.name, StartRoomName, StringComparison.Ordinal)
                ? ExpectedStartIgnoredLightCount
                : ExpectedAdministrativeIgnoredLightCount;
            if (lights.Length != expectedLights || ignoredCount != expectedIgnored)
            {
                throw new InvalidOperationException(
                    label + " light contract mismatch. lights=" + lights.Length +
                    " ignored=" + ignoredCount + ".");
            }
            if (!power100 && enabledAfter != ignoredCount)
            {
                throw new InvalidOperationException(
                    label + " must leave enabled only IgnoreLightControl lights.");
            }

            Dictionary<string, List<Renderer>> renderersByPath = BuildRendererBuckets(room.transform);
            var expectedSlots = new Dictionary<Renderer, Dictionary<int, Material>>();
            DungeonTilePowerBakeSet.EmissionMaterialEntry[] entries = set.EmissionMaterialEntries;
            int appliedEntries = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                DungeonTilePowerBakeSet.EmissionMaterialEntry entry = entries[i];
                if (string.IsNullOrWhiteSpace(entry.relativePath) ||
                    !renderersByPath.TryGetValue(entry.relativePath, out List<Renderer> bucket) ||
                    bucket == null || bucket.Count == 0)
                {
                    throw new InvalidOperationException(
                        label + " emission renderer path is unresolved: " + entry.relativePath);
                }
                if (entry.rendererBucketIndex < 0 || entry.rendererBucketIndex >= bucket.Count)
                {
                    throw new InvalidOperationException(
                        label + " emission renderer bucket is invalid: " + entry.relativePath);
                }

                Renderer renderer = bucket[entry.rendererBucketIndex];
                if (HasEnabledIgnoreEmissionControl(renderer))
                    continue;
                Material replacement = power100 ? entry.power100Material : entry.power00Material;
                if (replacement == null)
                    throw new InvalidOperationException(label + " emission replacement is null.");

                Material[] materials = renderer.sharedMaterials;
                if (entry.materialIndex < 0 || entry.materialIndex >= materials.Length)
                    throw new InvalidOperationException(label + " emission material index is invalid.");
                materials[entry.materialIndex] = replacement;
                renderer.sharedMaterials = materials;

                if (!expectedSlots.TryGetValue(renderer, out Dictionary<int, Material> slots))
                {
                    slots = new Dictionary<int, Material>();
                    expectedSlots.Add(renderer, slots);
                }
                slots[entry.materialIndex] = replacement;
                appliedEntries++;
            }

            return new PowerApplyResult(
                label,
                power100,
                selectedBake,
                lights.Length,
                ignoredCount,
                enabledAfter,
                entries.Length,
                appliedEntries,
                expectedSlots);
        }

        private static Dictionary<string, List<Renderer>> BuildRendererBuckets(Transform root)
        {
            var result = new Dictionary<string, List<Renderer>>(StringComparer.Ordinal);
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, root);
                if (!result.TryGetValue(path, out List<Renderer> bucket))
                {
                    bucket = new List<Renderer>();
                    result.Add(path, bucket);
                }
                bucket.Add(renderer);
            }
            return result;
        }

        private static bool HasEnabledIgnoreLightControl(Component component)
        {
            IgnoreLightControl marker = component != null
                ? component.GetComponentInParent<IgnoreLightControl>(true)
                : null;
            return marker != null && marker.enabled;
        }

        private static bool HasEnabledIgnoreEmissionControl(Component component)
        {
            IgnoreEmissionControl marker = component != null
                ? component.GetComponentInParent<IgnoreEmissionControl>(true)
                : null;
            return marker != null && marker.enabled;
        }

        private static void AssertNoShadowmaskSetting(LightingSettings settings)
        {
            var serialized = new SerializedObject(settings);
            SerializedProperty property = serialized.FindProperty("m_UsingShadowmask");
            if (property == null)
            {
                throw new InvalidOperationException(
                    "Copied LightingSettings does not expose m_UsingShadowmask.");
            }
            bool usingShadowmask = property.propertyType == SerializedPropertyType.Boolean
                ? property.boolValue
                : property.intValue != 0;
            if (usingShadowmask)
                throw new InvalidOperationException("Pilot contract requires shadowmask disabled.");
        }

        private static LightmapArtifact[] CaptureLightmapArtifacts(string generatedRunPath)
        {
            LightmapData[] maps = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
            if (maps.Length == 0 || LightmapSettings.lightmapsMode != LightmapsMode.CombinedDirectional)
            {
                throw new InvalidOperationException(
                    "Bake must produce CombinedDirectional lightmaps.");
            }

            var artifacts = new LightmapArtifact[maps.Length];
            for (int i = 0; i < maps.Length; i++)
            {
                LightmapData map = maps[i];
                if (map == null || map.lightmapColor == null || map.lightmapDir == null)
                    throw new InvalidOperationException("Lightmap " + i + " lacks color or direction.");
                if (map.shadowMask != null)
                    throw new InvalidOperationException("Shadowmask must be absent for every pilot lightmap.");

                string colorPath = NormalizePath(AssetDatabase.GetAssetPath(map.lightmapColor));
                string directionPath = NormalizePath(AssetDatabase.GetAssetPath(map.lightmapDir));
                AssertOwnedGeneratedAsset(colorPath, generatedRunPath, "lightmap color");
                AssertOwnedGeneratedAsset(directionPath, generatedRunPath, "lightmap direction");
                artifacts[i] = new LightmapArtifact
                {
                    index = i,
                    colorPath = colorPath,
                    colorSha256 = ComputeFileSha256(colorPath),
                    directionPath = directionPath,
                    directionSha256 = ComputeFileSha256(directionPath),
                    shadowMaskPath = string.Empty
                };
            }
            return artifacts;
        }

        private static RendererMapFile BuildRendererMap(
            GameObject startRoom,
            GameObject administrativeRoom,
            string stateId)
        {
            var records = new List<RendererMapRecord>(ExpectedRendererCount);
            var uv2HashCache = new Dictionary<Mesh, string>();
            var stableKeys = new HashSet<string>(StringComparer.Ordinal);
            AppendRendererMap(
                records,
                startRoom,
                "Start",
                uv2HashCache,
                stableKeys,
                stateId);
            AppendRendererMap(
                records,
                administrativeRoom,
                "Administrative",
                uv2HashCache,
                stableKeys,
                stateId);
            records.Sort((left, right) => string.CompareOrdinal(left.stableKey, right.stableKey));
            if (records.Count != ExpectedRendererCount || stableKeys.Count != records.Count)
            {
                throw new InvalidOperationException(
                    "Expected " + ExpectedRendererCount + " production renderer map records, got " +
                    records.Count + " records and " + stableKeys.Count + " unique stable keys.");
            }
            return new RendererMapFile
            {
                schema = "DungeonPortalJointPairRendererMap/v5",
                state = stateId,
                count = records.Count,
                bakedAtlasPairingContract =
                    "Exact mesh identity, UV2 hash, baked lightmap index, and baked scale-offset",
                realtimeLightmapScaleOffsetContract =
                    "Finite state-dependent diagnostic only; excluded from atlas pairing rejection",
                records = records.ToArray()
            };
        }

        private static void AssertRendererIdentityUniquenessPreflight(
            GameObject startRoom,
            GameObject administrativeRoom,
            string stateId)
        {
            // Use the exact same enumeration and stable-key construction as post-bake
            // evidence. Any duplicate regression therefore stops before Lightmapping.Bake.
            BuildRendererMap(
                startRoom,
                administrativeRoom,
                stateId + "__PRE_BAKE_RENDERER_IDENTITY");
        }

        private static void AppendRendererMap(
            List<RendererMapRecord> destination,
            GameObject room,
            string role,
            Dictionary<Mesh, string> uv2HashCache,
            HashSet<string> stableKeys,
            string stateId)
        {
            if (uv2HashCache == null)
                throw new ArgumentNullException(nameof(uv2HashCache));
            if (stableKeys == null)
                throw new ArgumentNullException(nameof(stableKeys));
            Renderer[] renderers = room.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer.name.StartsWith(
                        "__JOINT_PAIR_BAKE_ONLY_DOOR_OCCLUDER__",
                        StringComparison.Ordinal))
                {
                    continue;
                }
                Type type = renderer.GetType();
                Component[] sameType = renderer.transform.GetComponents(type);
                int ordinal = Array.IndexOf(sameType, renderer);
                Mesh mesh = ResolveRendererMesh(renderer);
                string meshGuid = string.Empty;
                long meshLocalId = 0;
                bool meshPersistent = mesh != null &&
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out meshGuid, out meshLocalId);
                bool hasUv2 = mesh != null &&
                    mesh.HasVertexAttribute(VertexAttribute.TexCoord1) &&
                    mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord1) > 0;
                string uv2ContentSha256 = string.Empty;
                if (meshPersistent)
                {
                    if (!uv2HashCache.TryGetValue(mesh, out uv2ContentSha256))
                    {
                        uv2ContentSha256 = ComputePersistentMeshUv2ContentSha256(mesh);
                        uv2HashCache.Add(mesh, uv2ContentSha256);
                    }
                    if (string.IsNullOrWhiteSpace(uv2ContentSha256))
                    {
                        throw new InvalidOperationException(
                            "Persistent mesh UV2 content hash is empty: " + mesh.name);
                    }
                }
                string relativePath = AnimationUtility.CalculateTransformPath(
                    renderer.transform,
                    room.transform);
                string siblingIndexedRelativePath =
                    GetRoomRelativeSiblingIndexedPath(renderer.transform, room.transform);
                string stableKey = role + "|" + siblingIndexedRelativePath + "|" +
                                   type.FullName + "|" +
                                   ordinal.ToString(CultureInfo.InvariantCulture);
                if (!stableKeys.Add(stableKey))
                {
                    throw new InvalidOperationException(
                        "Duplicate renderer stable key before evidence/bake. state=" + stateId +
                        " key=" + stableKey + " humanRelativePath=" + relativePath);
                }
                Vector4 bakedScaleOffset = renderer.lightmapScaleOffset;
                if (!IsFinite(bakedScaleOffset))
                {
                    throw new InvalidOperationException(
                        "Renderer baked lightmap scale-offset is non-finite. state=" + stateId +
                        " key=" + stableKey + " value=" + FormatVector4(bakedScaleOffset));
                }
                Vector4 realtimeScaleOffsetDiagnostic = renderer.realtimeLightmapScaleOffset;
                if (!IsFinite(realtimeScaleOffsetDiagnostic))
                {
                    throw new InvalidOperationException(
                        "Renderer realtime lightmap scale-offset diagnostic is non-finite. state=" +
                        stateId + " key=" + stableKey + " value=" +
                        FormatVector4(realtimeScaleOffsetDiagnostic));
                }
                destination.Add(new RendererMapRecord
                {
                    stableKey = stableKey,
                    roomRole = role,
                    relativePath = relativePath,
                    siblingIndexedRelativePath = siblingIndexedRelativePath,
                    rendererType = type.FullName,
                    componentOrdinal = ordinal,
                    meshAssetGuid = meshGuid,
                    meshLocalId = meshLocalId,
                    meshPersistent = meshPersistent,
                    hasUv2 = hasUv2,
                    uv2ContentSha256 = uv2ContentSha256,
                    lightmapIndex = renderer.lightmapIndex,
                    lightmapScaleOffset = bakedScaleOffset,
                    realtimeLightmapIndex = renderer.realtimeLightmapIndex,
                    stateDependentRealtimeLightmapScaleOffsetDiagnostic =
                        realtimeScaleOffsetDiagnostic,
                    lightProbeUsage = renderer.lightProbeUsage.ToString(),
                    reflectionProbeUsage = renderer.reflectionProbeUsage.ToString(),
                    enabled = renderer.enabled,
                    materialCount = renderer.sharedMaterials != null
                        ? renderer.sharedMaterials.Length
                        : 0
                });
            }
        }

        private static string ComputePersistentMeshUv2ContentSha256(Mesh mesh)
        {
            if (mesh == null || !EditorUtility.IsPersistent(mesh))
            {
                throw new InvalidOperationException(
                    "UV2 content hashing requires a persistent mesh asset.");
            }

            bool hasUv2 = mesh.HasVertexAttribute(VertexAttribute.TexCoord1) &&
                          mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord1) > 0;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
            {
                writer.Write("DungeonPortalPersistentMeshUv2Raw/v1");
                writer.Write(mesh.vertexCount);
                writer.Write(hasUv2);
                if (hasUv2)
                {
                    int dimension = mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord1);
                    VertexAttributeFormat format =
                        mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord1);
                    int vertexStream = mesh.GetVertexAttributeStream(VertexAttribute.TexCoord1);
                    int offset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord1);
                    int stride = mesh.GetVertexBufferStride(vertexStream);
                    int elementBytes = GetVertexAttributeFormatByteSize(format) * dimension;
                    if (dimension <= 0 || dimension > 4 || vertexStream < 0 || offset < 0 ||
                        stride <= 0 || elementBytes <= 0 || offset + elementBytes > stride)
                    {
                        throw new InvalidOperationException(
                            "Persistent mesh UV2 vertex-layout contract is invalid: " + mesh.name);
                    }

                    writer.Write(dimension);
                    writer.Write((int)format);
                    writer.Write(vertexStream);
                    writer.Write(offset);
                    writer.Write(stride);
                    Mesh.MeshDataArray meshData = Mesh.AcquireReadOnlyMeshData(mesh);
                    try
                    {
                        var raw = meshData[0].GetVertexData<byte>(vertexStream);
                        int requiredBytes = mesh.vertexCount == 0
                            ? 0
                            : (mesh.vertexCount - 1) * stride + offset + elementBytes;
                        if (raw.Length < requiredBytes)
                        {
                            throw new InvalidOperationException(
                                "Persistent mesh UV2 vertex buffer is shorter than declared: " +
                                mesh.name);
                        }
                        for (int vertex = 0; vertex < mesh.vertexCount; vertex++)
                        {
                            int start = vertex * stride + offset;
                            for (int valueByte = 0; valueByte < elementBytes; valueByte++)
                                writer.Write(raw[start + valueByte]);
                        }
                    }
                    finally
                    {
                        meshData.Dispose();
                    }
                }
                writer.Flush();
                return ComputeSha256(stream.ToArray());
            }
        }

        private static int GetVertexAttributeFormatByteSize(VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32:
                case VertexAttributeFormat.UInt32:
                case VertexAttributeFormat.SInt32:
                    return 4;
                case VertexAttributeFormat.Float16:
                case VertexAttributeFormat.UNorm16:
                case VertexAttributeFormat.SNorm16:
                case VertexAttributeFormat.UInt16:
                case VertexAttributeFormat.SInt16:
                    return 2;
                case VertexAttributeFormat.UNorm8:
                case VertexAttributeFormat.SNorm8:
                case VertexAttributeFormat.UInt8:
                case VertexAttributeFormat.SInt8:
                    return 1;
                default:
                    throw new InvalidOperationException(
                        "Unsupported persistent mesh UV2 vertex format: " + format);
            }
        }

        private static Mesh ResolveRendererMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
                return skinned.sharedMesh;
            if (renderer is MeshRenderer)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                return filter != null ? filter.sharedMesh : null;
            }
            return null;
        }

        private static void AssertOwnedGeneratedAsset(
            string assetPath,
            string generatedRunPath,
            string label)
        {
            string prefix = NormalizePath(generatedRunPath).TrimEnd('/') + "/";
            if (string.IsNullOrWhiteSpace(assetPath) ||
                !NormalizePath(assetPath).StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    label + " escaped the token-owned generated folder: " + assetPath);
            }
        }

        private static BufferedImage CaptureCamera(
            Camera camera,
            Scene scene,
            RenderTargets targets,
            string stateId,
            RenderFailureMonitor renderFailureMonitor)
        {
            if (camera == null || camera.enabled || camera.targetTexture != null ||
                camera.gameObject.scene != scene || !camera.allowHDR || !camera.allowMSAA)
            {
                throw new InvalidOperationException(
                    "Only a disabled fixed camera in the copied scene may render.");
            }
            if (renderFailureMonitor == null)
                throw new ArgumentNullException(nameof(renderFailureMonitor));

            Component cameraData = FindUniqueUrpCameraData(camera);
            PropertyInfo postProperty = cameraData.GetType().GetProperty(
                "renderPostProcessing",
                BindingFlags.Instance | BindingFlags.Public);
            if (postProperty == null || postProperty.PropertyType != typeof(bool) ||
                !postProperty.CanRead || !postProperty.CanWrite)
            {
                throw new InvalidOperationException(
                    "URP renderPostProcessing is unavailable on " + camera.name + ".");
            }

            bool originalPost = (bool)postProperty.GetValue(cameraData);
            if (!originalPost)
            {
                throw new InvalidOperationException(
                    "Fixed camera presentation post-processing must remain enabled: " +
                    camera.name + ".");
            }
            RenderTexture originalActive = RenderTexture.active;
            RenderTexture originalTarget = camera.targetTexture;
            float originalAspect = camera.aspect;
            Matrix4x4 originalProjection = camera.projectionMatrix;
            bool originalSrgbWrite = GL.sRGBWrite;
            Texture2D presentationReadback = null;
            Texture2D hdrReadback = null;
            try
            {
                camera.aspect = CaptureWidth / (float)CaptureHeight;
                camera.ResetProjectionMatrix();
                AssertStandardPerspectiveProjection(camera);

                postProperty.SetValue(cameraData, originalPost);
                SubmitStandardRenderRequest(
                    camera,
                    targets.PresentationRender,
                    renderFailureMonitor,
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
                if (png == null || png.Length == 0)
                    throw new InvalidOperationException("PNG encoding returned no bytes.");

                postProperty.SetValue(cameraData, false);
                SubmitStandardRenderRequest(
                    camera,
                    targets.HdrRender,
                    renderFailureMonitor,
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
                ComputeLuminance(hdrReadback, out double meanLuminance, out float maxLuminance);
                if (double.IsNaN(meanLuminance) || double.IsInfinity(meanLuminance) ||
                    float.IsNaN(maxLuminance) || float.IsInfinity(maxLuminance) ||
                    meanLuminance <= MinimumValidMeanLinearLuminance ||
                    maxLuminance <= MinimumValidMaxLinearLuminance)
                {
                    throw new InvalidOperationException(
                        "Render returned an invalid zero/non-finite HDR frame: state=" +
                        stateId + ", camera=" + camera.name + ", mean=" +
                        FormatDouble(meanLuminance) + ", max=" + FormatFloat(maxLuminance) + ".");
                }
                byte[] exr = ImageConversion.EncodeToEXR(
                    hdrReadback,
                    Texture2D.EXRFlags.CompressZIP);
                if (exr == null || exr.Length == 0)
                    throw new InvalidOperationException("EXR encoding returned no bytes.");

                string stem = GetShortCaptureStem(camera.name);
                return new BufferedImage
                {
                    cameraName = camera.name,
                    pngFilename = stem + ".png",
                    pngBytes = png,
                    pngSha256 = ComputeSha256(png),
                    exrFilename = stem + "_L.exr",
                    exrBytes = exr,
                    exrSha256 = ComputeSha256(exr),
                    hdrMeanLuminance = meanLuminance,
                    hdrMaxLuminance = maxLuminance,
                    position = camera.transform.position,
                    rotation = camera.transform.rotation,
                    fieldOfView = camera.fieldOfView,
                    nearClip = camera.nearClipPlane,
                    farClip = camera.farClipPlane,
                    cullingMask = camera.cullingMask,
                    allowHdr = camera.allowHDR,
                    allowMsaa = camera.allowMSAA,
                    presentationRenderTargetMsaa = targets.PresentationRender.antiAliasing,
                    presentationPostProcessing = originalPost,
                    hdrPostProcessing = false
                };
            }
            finally
            {
                RenderTexture.active = originalActive;
                camera.targetTexture = originalTarget;
                camera.aspect = originalAspect;
                camera.projectionMatrix = originalProjection;
                postProperty.SetValue(cameraData, originalPost);
                GL.sRGBWrite = originalSrgbWrite;
                if (presentationReadback != null)
                    Object.DestroyImmediate(presentationReadback);
                if (hdrReadback != null)
                    Object.DestroyImmediate(hdrReadback);
            }
        }

        private static void SubmitStandardRenderRequest(
            Camera camera,
            RenderTexture destination,
            RenderFailureMonitor renderFailureMonitor,
            string stage)
        {
            if (!(RenderPipelineManager.currentPipeline is UniversalRenderPipeline))
                throw new InvalidOperationException("Active pipeline is not initialized URP.");
            if (camera == null || destination == null || !destination.IsCreated() ||
                camera.targetTexture != null)
            {
                throw new InvalidOperationException(
                    "StandardRequest camera/destination precondition failed.");
            }

            UniversalAdditionalCameraData additional =
                camera.GetUniversalAdditionalCameraData();
            if (additional == null || additional.renderType != CameraRenderType.Base)
                throw new InvalidOperationException("StandardRequest requires a Base camera.");

            var request = new RenderPipeline.StandardRequest
            {
                destination = destination,
                mipLevel = 0,
                slice = 0,
                face = CubemapFace.Unknown
            };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                throw new InvalidOperationException("Active URP rejected StandardRequest.");

            destination.DiscardContents();
            RenderPipeline.SubmitRenderRequest(camera, request);
            renderFailureMonitor.ThrowIfFailed(stage);
            if (camera.targetTexture != null)
                throw new InvalidOperationException("StandardRequest did not preserve null Camera target.");
        }

        private static Component FindUniqueUrpCameraData(Camera camera)
        {
            Component result = null;
            Component[] components = camera.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null ||
                    !string.Equals(
                        component.GetType().FullName,
                        "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData",
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (result != null)
                    throw new InvalidOperationException("Duplicate URP camera data on " + camera.name + ".");
                result = component;
            }
            if (result == null)
                throw new InvalidOperationException("Missing URP camera data on " + camera.name + ".");
            return result;
        }

        private static bool ReadUrpPostProcessing(Camera camera)
        {
            Component cameraData = FindUniqueUrpCameraData(camera);
            PropertyInfo property = cameraData.GetType().GetProperty(
                "renderPostProcessing",
                BindingFlags.Instance | BindingFlags.Public);
            if (property == null || property.PropertyType != typeof(bool) || !property.CanRead)
                throw new InvalidOperationException("URP post-processing state is unreadable.");
            return (bool)property.GetValue(cameraData);
        }

        private static void ComputeLuminance(
            Texture2D texture,
            out double mean,
            out float maximum)
        {
            Color[] pixels = texture.GetPixels();
            if (pixels == null || pixels.Length != CaptureWidth * CaptureHeight)
                throw new InvalidOperationException("Unexpected HDR readback pixel count.");
            double sum = 0d;
            float max = 0f;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color pixel = pixels[i];
                if (!IsFinite(pixel.r) || !IsFinite(pixel.g) ||
                    !IsFinite(pixel.b) || !IsFinite(pixel.a))
                {
                    throw new InvalidOperationException(
                        "HDR readback contains a non-finite pixel at index " + i + ".");
                }
                float luminance = Mathf.Max(
                    0f,
                    pixel.r * 0.2126f + pixel.g * 0.7152f + pixel.b * 0.0722f);
                sum += luminance;
                max = Mathf.Max(max, luminance);
            }
            mean = sum / pixels.Length;
            maximum = max;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector4 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) &&
                   IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool ExactVector4Equals(Vector4 left, Vector4 right)
        {
            return left.x.Equals(right.x) && left.y.Equals(right.y) &&
                   left.z.Equals(right.z) && left.w.Equals(right.w);
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
                // The corrected realtime reference used a single-sample presentation target.
                // Camera.allowMSAA remains true, but the render-target contract is exact.
                const int msaa = 1;
                RenderTexture presentationRender = null;
                RenderTexture presentationResolve = null;
                RenderTexture hdrRender = null;
                RenderTexture hdrResolve = null;
                try
                {
                    presentationRender = CreateTarget(
                        "JointPair_Presentation_Render",
                        24,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.sRGB,
                        msaa);
                    presentationResolve = CreateTarget(
                        "JointPair_Presentation_Resolve",
                        0,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.sRGB,
                        1);
                    hdrRender = CreateTarget(
                        "JointPair_HDR_Render",
                        24,
                        RenderTextureFormat.ARGBHalf,
                        RenderTextureReadWrite.Linear,
                        1);
                    hdrResolve = CreateTarget(
                        "JointPair_HDR_Resolve",
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
                    DestroyTarget(presentationRender);
                    DestroyTarget(presentationResolve);
                    DestroyTarget(hdrRender);
                    DestroyTarget(hdrResolve);
                    throw;
                }
            }

            private static RenderTexture CreateTarget(
                string name,
                int depth,
                RenderTextureFormat format,
                RenderTextureReadWrite readWrite,
                int antiAliasing)
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
                    antiAliasing = antiAliasing,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                if (!target.Create())
                {
                    Object.DestroyImmediate(target);
                    throw new InvalidOperationException("Could not create RenderTexture " + name + ".");
                }
                return target;
            }

            public void Dispose()
            {
                DestroyTarget(PresentationRender);
                DestroyTarget(PresentationResolve);
                DestroyTarget(HdrRender);
                DestroyTarget(HdrResolve);
            }

            private static void DestroyTarget(RenderTexture target)
            {
                if (target == null)
                    return;
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private sealed class SourceContract
        {
            public DungeonTilePowerBakeSet StartPowerSet { get; private set; }
            public DungeonTilePowerBakeSet AdministrativePowerSet { get; private set; }
            public string CameraFingerprint { get; private set; }

            public static SourceContract ValidateAndCapture(Scene scene, GameObject root)
            {
                ValidateCopiedRootShape(root);
                Transform productionRooms = FindUniqueDescendant(
                    root.transform,
                    ProductionRoomsRootName,
                    true);
                Transform portalRoot = FindUniqueDescendant(
                    root.transform,
                    PortalTransportRootName,
                    true);
                Transform camerasRoot = FindUniqueDescendant(
                    root.transform,
                    CamerasRootName,
                    true);
                Transform volumeRoot = FindUniqueDescendant(
                    root.transform,
                    VolumeRootName,
                    true);
                GameObject start = FindUniqueDescendant(
                    productionRooms,
                    StartRoomName,
                    true).gameObject;
                GameObject administrative = FindUniqueDescendant(
                    productionRooms,
                    AdministrativeRoomName,
                    true).gameObject;

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length != ExpectedRendererCount)
                {
                    throw new InvalidOperationException(
                        "Preflight renderer count changed. expected=" + ExpectedRendererCount +
                        " actual=" + renderers.Length + ".");
                }

                Light[] startLights = start.GetComponentsInChildren<Light>(true);
                Light[] administrativeLights = administrative.GetComponentsInChildren<Light>(true);
                if (startLights.Length != ExpectedStartLightCount ||
                    administrativeLights.Length != ExpectedAdministrativeLightCount)
                {
                    throw new InvalidOperationException("Preflight production light counts changed.");
                }
                int startIgnored = startLights.Count(HasEnabledIgnoreLightControl);
                int administrativeIgnored = administrativeLights.Count(HasEnabledIgnoreLightControl);
                if (startIgnored != ExpectedStartIgnoredLightCount ||
                    administrativeIgnored != ExpectedAdministrativeIgnoredLightCount)
                {
                    throw new InvalidOperationException("Preflight IgnoreLightControl counts changed.");
                }
                if (startLights.Any(light => !light.enabled) ||
                    administrativeLights.Any(light => !light.enabled))
                {
                    throw new InvalidOperationException(
                        "Preflight expects every production light to retain its source-enabled state.");
                }
                if (startLights.Any(light => light.lightmapBakeType != LightmapBakeType.Baked) ||
                    administrativeLights.Any(light => light.lightmapBakeType != LightmapBakeType.Baked))
                {
                    throw new InvalidOperationException("Preflight production lights must be Baked lights.");
                }

                DungeonTilePowerBakeSet startSet = start.GetComponent<DungeonTilePowerBakeSet>();
                DungeonTilePowerBakeSet administrativeSet =
                    administrative.GetComponent<DungeonTilePowerBakeSet>();
                ValidatePowerSet(startSet, ExpectedStartProbeCount, "Start");
                ValidatePowerSet(
                    administrativeSet,
                    ExpectedAdministrativeProbeCount,
                    "Administrative");

                DungeonPortalTransportConnection[] connections =
                    portalRoot.GetComponentsInChildren<DungeonPortalTransportConnection>(true);
                if (connections.Length != 1 || connections[0].enabled)
                    throw new InvalidOperationException("Exactly one disabled portal connection is required.");

                Camera[] cameras = ResolveFixedCameras(camerasRoot);
                string cameraFingerprint = ComputeCameraFingerprint(cameras);
                EnvironmentContract.CaptureAndAssert(volumeRoot);

                Transform addedDoor = start.transform.Find(AddedDoorRelativePath);
                if (addedDoor == null)
                    throw new InvalidOperationException("Actual validation door is missing.");
                Renderer[] doorRenderers = addedDoor.GetComponentsInChildren<Renderer>(true);
                if (doorRenderers.Length != ExpectedDoorRendererCount ||
                    doorRenderers.Count(renderer => renderer.enabled) != ExpectedDoorEnabledRendererCount)
                {
                    throw new InvalidOperationException("Actual door renderer contract changed.");
                }
                if (addedDoor.GetComponentsInChildren<LightProbeGroup>(true).Length != 1 ||
                    addedDoor.GetComponentsInChildren<LightProbeGroup>(true)[0].probePositions.Length !=
                    ExpectedDoorProbeCount)
                {
                    throw new InvalidOperationException("Actual door eight-point LightProbeGroup changed.");
                }

                if (scene.isDirty)
                    throw new InvalidOperationException("Read-only preflight dirtied the validation scene.");
                return new SourceContract
                {
                    StartPowerSet = startSet,
                    AdministrativePowerSet = administrativeSet,
                    CameraFingerprint = cameraFingerprint
                };
            }

            private static void ValidatePowerSet(
                DungeonTilePowerBakeSet set,
                int expectedProbeCount,
                string label)
            {
                if (set == null || set.Power100Bake == null || set.Power00Bake == null)
                    throw new InvalidOperationException(label + " power bake set is incomplete.");
                if (set.Power100Bake.lightProbeEntries == null ||
                    set.Power100Bake.lightProbeEntries.Length != expectedProbeCount ||
                    set.Power00Bake.lightProbeEntries == null ||
                    set.Power00Bake.lightProbeEntries.Length != expectedProbeCount)
                {
                    throw new InvalidOperationException(label + " power bake probe stencil count changed.");
                }
                for (int i = 0; i < expectedProbeCount; i++)
                {
                    if ((set.Power100Bake.lightProbeEntries[i].localPosition -
                         set.Power00Bake.lightProbeEntries[i].localPosition).sqrMagnitude > 0.00000001f)
                    {
                        throw new InvalidOperationException(
                            label + " P0/P100 probe stencil positions differ at index " + i + ".");
                    }
                }
            }
        }

        private sealed class DoorBakeContract
        {
            private readonly GameObject doorRoot;
            private readonly Transform doorLeaf;
            private readonly Quaternion closedLocalRotation;
            private readonly Vector3 hingeAxis;
            private readonly float openAngleDegrees;
            private readonly MeshRenderer baseRenderer;
            private readonly MeshFilter baseFilter;
            private readonly MeshRenderer positiveRenderer;
            private readonly MeshRenderer negativeRenderer;
            private readonly MeshRenderer edgeRenderer;
            private readonly LightProbeGroup originalProbeGroup;
            private readonly string token;
            private GameObject bakeOccluder;
            private MeshRenderer bakeOccluderRenderer;

            private DoorBakeContract(
                GameObject doorRoot,
                Transform doorLeaf,
                Quaternion closedLocalRotation,
                Vector3 hingeAxis,
                float openAngleDegrees,
                MeshRenderer baseRenderer,
                MeshFilter baseFilter,
                MeshRenderer positiveRenderer,
                MeshRenderer negativeRenderer,
                MeshRenderer edgeRenderer,
                LightProbeGroup originalProbeGroup,
                string token)
            {
                this.doorRoot = doorRoot;
                this.doorLeaf = doorLeaf;
                this.closedLocalRotation = closedLocalRotation;
                this.hingeAxis = hingeAxis;
                this.openAngleDegrees = openAngleDegrees;
                this.baseRenderer = baseRenderer;
                this.baseFilter = baseFilter;
                this.positiveRenderer = positiveRenderer;
                this.negativeRenderer = negativeRenderer;
                this.edgeRenderer = edgeRenderer;
                this.originalProbeGroup = originalProbeGroup;
                this.token = token;
            }

            public Transform DoorLeaf => doorLeaf;
            public MeshRenderer PositiveRenderer => positiveRenderer;
            public MeshRenderer NegativeRenderer => negativeRenderer;
            public MeshRenderer EdgeRenderer => edgeRenderer;
            public LightProbeGroup OriginalProbeGroup => originalProbeGroup;
            public Quaternion ClosedLocalRotation => closedLocalRotation;
            public Vector3 HingeAxis => hingeAxis;
            public float OpenAngleDegrees => openAngleDegrees;

            public static DoorBakeContract Capture(
                GameObject portalRoot,
                GameObject startRoom,
                string token)
            {
                DungeonPortalDoorAngleSource[] sources =
                    portalRoot.GetComponentsInChildren<DungeonPortalDoorAngleSource>(true);
                if (sources.Length != 1)
                    throw new InvalidOperationException("Expected one copied DungeonPortalDoorAngleSource.");
                var serialized = new SerializedObject(sources[0]);
                SerializedProperty leafProperty = serialized.FindProperty("doorLeaf");
                SerializedProperty closedProperty = serialized.FindProperty("closedLocalRotation");
                SerializedProperty axisProperty = serialized.FindProperty("localHingeAxis");
                SerializedProperty angleProperty = serialized.FindProperty("openAngleDegrees");
                if (leafProperty == null || closedProperty == null || axisProperty == null ||
                    angleProperty == null)
                {
                    throw new InvalidOperationException("Door angle source serialized contract changed.");
                }

                Transform leaf = leafProperty.objectReferenceValue as Transform;
                Quaternion closed = closedProperty.quaternionValue;
                Vector3 axis = axisProperty.vector3Value;
                float angle = angleProperty.floatValue;
                Transform doorRootTransform = startRoom.transform.Find(AddedDoorRelativePath);
                if (doorRootTransform == null || leaf == null || !leaf.IsChildOf(doorRootTransform) ||
                    !string.Equals(leaf.name, DoorLeafName, StringComparison.Ordinal) ||
                    axis.sqrMagnitude <= Mathf.Epsilon || Mathf.Abs(angle) <= 0.001f)
                {
                    throw new InvalidOperationException("Door angle source does not target the actual door leaf.");
                }

                MeshRenderer baseRenderer = leaf.GetComponent<MeshRenderer>();
                MeshFilter baseFilter = leaf.GetComponent<MeshFilter>();
                if (baseRenderer == null || baseRenderer.enabled || baseFilter == null ||
                    baseFilter.sharedMesh == null)
                {
                    throw new InvalidOperationException(
                        "Disabled base Door_01 MeshRenderer/MeshFilter contract changed.");
                }

                MeshRenderer positive = RequireNamedMeshRenderer(
                    doorRootTransform,
                    DoorPositiveRendererName);
                MeshRenderer negative = RequireNamedMeshRenderer(
                    doorRootTransform,
                    DoorNegativeRendererName);
                MeshRenderer edge = RequireNamedMeshRenderer(doorRootTransform, DoorEdgeRendererName);
                if (!positive.enabled || !negative.enabled || !edge.enabled)
                    throw new InvalidOperationException("All three door split renderers must start enabled.");
                LightProbeGroup[] groups = doorRootTransform.GetComponentsInChildren<LightProbeGroup>(true);
                if (groups.Length != 1 || !groups[0].enabled ||
                    groups[0].probePositions.Length != ExpectedDoorProbeCount)
                {
                    throw new InvalidOperationException("Actual door probe group contract changed.");
                }

                return new DoorBakeContract(
                    doorRootTransform.gameObject,
                    leaf,
                    closed,
                    axis.normalized,
                    angle,
                    baseRenderer,
                    baseFilter,
                    positive,
                    negative,
                    edge,
                    groups[0],
                    token);
            }

            public void ApplyFullyOpenAndCreateBakeOccluder()
            {
                doorLeaf.localRotation = closedLocalRotation *
                                         Quaternion.AngleAxis(openAngleDegrees, hingeAxis);
                GameObjectUtility.SetStaticEditorFlags(doorRoot, 0);
                GameObjectUtility.SetStaticEditorFlags(doorLeaf.gameObject, 0);
                GameObjectUtility.SetStaticEditorFlags(positiveRenderer.gameObject, 0);
                GameObjectUtility.SetStaticEditorFlags(negativeRenderer.gameObject, 0);
                GameObjectUtility.SetStaticEditorFlags(edgeRenderer.gameObject, 0);

                positiveRenderer.enabled = false;
                negativeRenderer.enabled = false;
                edgeRenderer.enabled = false;

                bakeOccluder = new GameObject("__JOINT_PAIR_BAKE_ONLY_DOOR_OCCLUDER__" + token);
                bakeOccluder.transform.SetParent(doorLeaf, false);
                bakeOccluder.transform.localPosition = Vector3.zero;
                bakeOccluder.transform.localRotation = Quaternion.identity;
                bakeOccluder.transform.localScale = Vector3.one;
                MeshFilter filter = bakeOccluder.AddComponent<MeshFilter>();
                filter.sharedMesh = baseFilter.sharedMesh;
                bakeOccluderRenderer = bakeOccluder.AddComponent<MeshRenderer>();
                EditorUtility.CopySerialized(baseRenderer, bakeOccluderRenderer);
                bakeOccluderRenderer.enabled = true;
                bakeOccluderRenderer.forceRenderingOff = false;
                bakeOccluderRenderer.shadowCastingMode = ShadowCastingMode.On;
                bakeOccluderRenderer.receiveGI = ReceiveGI.Lightmaps;
                GameObjectUtility.SetStaticEditorFlags(
                    bakeOccluder,
                    StaticEditorFlags.ContributeGI |
                    StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.ReflectionProbeStatic);
                AssertBakeState();
            }

            public void AssertBakeState()
            {
                Quaternion expected = closedLocalRotation * Quaternion.AngleAxis(
                    openAngleDegrees,
                    hingeAxis);
                if (Quaternion.Angle(doorLeaf.localRotation, expected) > 0.01f ||
                    baseRenderer.enabled || positiveRenderer.enabled || negativeRenderer.enabled ||
                    edgeRenderer.enabled || bakeOccluder == null || bakeOccluderRenderer == null ||
                    !bakeOccluderRenderer.enabled || bakeOccluderRenderer.forceRenderingOff ||
                    bakeOccluderRenderer.sharedMaterials.Length != baseRenderer.sharedMaterials.Length ||
                    !bakeOccluderRenderer.sharedMaterials.SequenceEqual(baseRenderer.sharedMaterials) ||
                    bakeOccluder.GetComponent<MeshFilter>().sharedMesh != baseFilter.sharedMesh)
                {
                    throw new InvalidOperationException("Door bake-only occluder state is invalid.");
                }
                StaticEditorFlags expectedFlags =
                    StaticEditorFlags.ContributeGI |
                    StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.ReflectionProbeStatic;
                if (GameObjectUtility.GetStaticEditorFlags(bakeOccluder) != expectedFlags ||
                    GameObjectUtility.GetStaticEditorFlags(doorRoot) != 0 ||
                    GameObjectUtility.GetStaticEditorFlags(doorLeaf.gameObject) != 0)
                {
                    throw new InvalidOperationException("Door dynamic/static flags are invalid.");
                }
            }

            public void EndBakeOcclusionAndRestoreDynamicRenderers()
            {
                bakeOccluderRenderer.enabled = false;
                bakeOccluderRenderer.forceRenderingOff = true;
                bakeOccluderRenderer.shadowCastingMode = ShadowCastingMode.Off;
                positiveRenderer.enabled = true;
                negativeRenderer.enabled = true;
                edgeRenderer.enabled = true;
            }

            public void AssertPresentationState()
            {
                if (baseRenderer.enabled || !positiveRenderer.enabled || !negativeRenderer.enabled ||
                    !edgeRenderer.enabled || bakeOccluderRenderer == null ||
                    bakeOccluderRenderer.enabled || !bakeOccluderRenderer.forceRenderingOff ||
                    bakeOccluderRenderer.shadowCastingMode != ShadowCastingMode.Off)
                {
                    throw new InvalidOperationException("Door presentation state is invalid.");
                }
                Quaternion expected = closedLocalRotation * Quaternion.AngleAxis(
                    openAngleDegrees,
                    hingeAxis);
                if (Quaternion.Angle(doorLeaf.localRotation, expected) > 0.01f)
                    throw new InvalidOperationException("Actual door is not at D100.");
            }

            public Vector3[] GetOriginalDoorProbeWorldPositions()
            {
                Vector3[] local = originalProbeGroup.probePositions;
                var world = new Vector3[local.Length];
                for (int i = 0; i < local.Length; i++)
                    world[i] = originalProbeGroup.transform.TransformPoint(local[i]);
                return world;
            }

            private static MeshRenderer RequireNamedMeshRenderer(Transform root, string name)
            {
                Transform found = FindUniqueDescendant(root, name, false);
                MeshRenderer renderer = found.GetComponent<MeshRenderer>();
                if (renderer == null)
                    throw new InvalidOperationException("Missing MeshRenderer on " + name + ".");
                return renderer;
            }
        }

        private sealed class ProbeStencil
        {
            private readonly LightProbeGroup generatedGroup;
            private readonly LightProbeGroup originalDoorGroup;
            private readonly Vector3[] worldPositions;
            private readonly string[] roles;

            private ProbeStencil(
                LightProbeGroup generatedGroup,
                LightProbeGroup originalDoorGroup,
                Vector3[] worldPositions,
                string[] roles)
            {
                this.generatedGroup = generatedGroup;
                this.originalDoorGroup = originalDoorGroup;
                this.worldPositions = worldPositions;
                this.roles = roles;
            }

            public int TotalCount => worldPositions.Length;
            public int StartCount => ExpectedStartProbeCount;
            public int AdministrativeCount => ExpectedAdministrativeProbeCount;
            public int DoorCount => ExpectedDoorProbeCount;

            public static ProbeStencil Create(
                GameObject validationRoot,
                GameObject startRoom,
                GameObject administrativeRoom,
                DoorBakeContract door)
            {
                DungeonTilePowerBakeSet startSet = startRoom.GetComponent<DungeonTilePowerBakeSet>();
                DungeonTilePowerBakeSet administrativeSet =
                    administrativeRoom.GetComponent<DungeonTilePowerBakeSet>();
                DungeonTileBakeData.LightProbeBakeEntry[] startEntries =
                    startSet.Power00Bake.lightProbeEntries;
                DungeonTileBakeData.LightProbeBakeEntry[] administrativeEntries =
                    administrativeSet.Power00Bake.lightProbeEntries;
                if (startEntries.Length != ExpectedStartProbeCount ||
                    administrativeEntries.Length != ExpectedAdministrativeProbeCount)
                {
                    throw new InvalidOperationException("Power bake probe stencil counts changed.");
                }

                Vector3[] doorPositions = door.GetOriginalDoorProbeWorldPositions();
                if (doorPositions.Length != ExpectedDoorProbeCount)
                    throw new InvalidOperationException("Door probe stencil count changed.");

                var positions = new Vector3[ExpectedTotalProbeCount];
                var roles = new string[ExpectedTotalProbeCount];
                int cursor = 0;
                for (int i = 0; i < startEntries.Length; i++, cursor++)
                {
                    positions[cursor] = startRoom.transform.TransformPoint(startEntries[i].localPosition);
                    roles[cursor] = "Start";
                }
                for (int i = 0; i < administrativeEntries.Length; i++, cursor++)
                {
                    positions[cursor] = administrativeRoom.transform.TransformPoint(
                        administrativeEntries[i].localPosition);
                    roles[cursor] = "Administrative";
                }
                for (int i = 0; i < doorPositions.Length; i++, cursor++)
                {
                    positions[cursor] = doorPositions[i];
                    roles[cursor] = "DoorExisting8";
                }
                if (cursor != ExpectedTotalProbeCount)
                    throw new InvalidOperationException("Probe stencil assembly count mismatch.");

                LightProbeGroup[] allExisting =
                    validationRoot.GetComponentsInChildren<LightProbeGroup>(true);
                if (allExisting.Length != 1 || allExisting[0] != door.OriginalProbeGroup)
                {
                    throw new InvalidOperationException(
                        "The copied scene must contain only the existing eight-point door group before stencil creation.");
                }
                door.OriginalProbeGroup.enabled = false;

                var stencilObject = new GameObject("__JOINT_PAIR_TOTAL_PROBE_STENCIL_218__");
                stencilObject.transform.SetParent(validationRoot.transform, false);
                stencilObject.transform.localPosition = Vector3.zero;
                stencilObject.transform.localRotation = Quaternion.identity;
                stencilObject.transform.localScale = Vector3.one;
                LightProbeGroup group = stencilObject.AddComponent<LightProbeGroup>();
                var local = new Vector3[positions.Length];
                for (int i = 0; i < positions.Length; i++)
                    local[i] = group.transform.InverseTransformPoint(positions[i]);
                group.probePositions = local;
                group.enabled = true;

                var result = new ProbeStencil(group, door.OriginalProbeGroup, positions, roles);
                result.AssertPrepared();
                return result;
            }

            public void AssertPrepared()
            {
                if (generatedGroup == null || !generatedGroup.enabled ||
                    generatedGroup.probePositions == null ||
                    generatedGroup.probePositions.Length != ExpectedTotalProbeCount ||
                    originalDoorGroup == null || originalDoorGroup.enabled ||
                    worldPositions.Length != ExpectedTotalProbeCount || roles.Length != ExpectedTotalProbeCount)
                {
                    throw new InvalidOperationException("Combined 218-point probe stencil is invalid.");
                }
            }

            public ProbeSampleFile SampleAfterBake(DoorBakeContract door, string stateId)
            {
                if (LightmapSettings.lightProbes == null ||
                    LightmapSettings.lightProbes.count != ExpectedTotalProbeCount ||
                    LightmapSettings.lightProbes.bakedProbes == null ||
                    LightmapSettings.lightProbes.bakedProbes.Length != ExpectedTotalProbeCount)
                {
                    throw new InvalidOperationException(
                        "Bake did not produce exactly 218 baked light probes.");
                }

                LightProbes.Tetrahedralize();
                var sphericalHarmonics = new SphericalHarmonicsL2[worldPositions.Length];
                var occlusion = new Vector4[worldPositions.Length];
                LightProbes.CalculateInterpolatedLightAndOcclusionProbes(
                    worldPositions,
                    sphericalHarmonics,
                    occlusion);
                var samples = new ProbeSampleRecord[worldPositions.Length];
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] = ProbeSampleRecord.Create(
                        i,
                        roles[i],
                        worldPositions[i],
                        sphericalHarmonics[i],
                        occlusion[i]);
                }

                DoorSideSamples doorSides = DoorSideSamples.Sample(door);
                return new ProbeSampleFile
                {
                    schema = "DungeonPortalJointPairProbeSamples/v1",
                    state = stateId,
                    coefficientLayout = "coefficient0..8 each store RGB; occlusion is Vector4",
                    startCount = ExpectedStartProbeCount,
                    administrativeCount = ExpectedAdministrativeProbeCount,
                    doorStencilCount = ExpectedDoorProbeCount,
                    totalCount = ExpectedTotalProbeCount,
                    samples = samples,
                    doorSideSamples = doorSides
                };
            }
        }

        private sealed class ReflectionBakeContract
        {
            private readonly ReflectionProbe[] activeProbes;
            private readonly ReflectionProbe[] inactiveProbes;
            private readonly string generatedStatePath;
            private readonly string[] roles;

            private ReflectionBakeContract(
                ReflectionProbe[] activeProbes,
                ReflectionProbe[] inactiveProbes,
                string generatedStatePath,
                string[] roles)
            {
                this.activeProbes = activeProbes;
                this.inactiveProbes = inactiveProbes;
                this.generatedStatePath = generatedStatePath;
                this.roles = roles;
            }

            public static ReflectionBakeContract Prepare(
                GameObject startRoom,
                GameObject administrativeRoom,
                PilotState state,
                string generatedStatePath)
            {
                var active = new List<ReflectionProbe>(ExpectedTotalP100ReflectionCount);
                var inactive = new List<ReflectionProbe>(ExpectedTotalP100ReflectionCount);
                var roles = new List<string>(ExpectedTotalP100ReflectionCount);
                ConfigureRoom(
                    startRoom,
                    "Start",
                    state.StartPower100,
                    ExpectedStartP100ReflectionCount,
                    active,
                    inactive,
                    roles);
                ConfigureRoom(
                    administrativeRoom,
                    "Administrative",
                    state.AdministrativePower100,
                    ExpectedAdministrativeP100ReflectionCount,
                    active,
                    inactive,
                    roles);
                var contract = new ReflectionBakeContract(
                    active.ToArray(),
                    inactive.ToArray(),
                    generatedStatePath,
                    roles.ToArray());
                contract.AssertPrepared();
                return contract;
            }

            private static void ConfigureRoom(
                GameObject room,
                string role,
                bool power100,
                int expectedActive,
                List<ReflectionProbe> active,
                List<ReflectionProbe> inactive,
                List<string> roles)
            {
                ReflectionProbe[] probes = room.GetComponentsInChildren<ReflectionProbe>(true);
                var selected = new List<ReflectionProbe>();
                var rejected = new List<ReflectionProbe>();
                string selectedSuffix = power100 ? "_P100" : "_P0";
                string rejectedSuffix = power100 ? "_P0" : "_P100";
                for (int i = 0; i < probes.Length; i++)
                {
                    ReflectionProbe probe = probes[i];
                    if (probe.name.EndsWith(selectedSuffix, StringComparison.Ordinal))
                        selected.Add(probe);
                    else if (probe.name.EndsWith(rejectedSuffix, StringComparison.Ordinal))
                        rejected.Add(probe);
                    else
                        throw new InvalidOperationException(role + " has an unclassified reflection probe.");
                }
                if (selected.Count != expectedActive || rejected.Count != expectedActive)
                {
                    throw new InvalidOperationException(
                        role + " P0/P100 reflection variant counts changed.");
                }

                for (int i = 0; i < selected.Count; i++)
                {
                    ReflectionProbe probe = selected[i];
                    probe.gameObject.SetActive(true);
                    probe.enabled = true;
                    probe.mode = ReflectionProbeMode.Baked;
                    probe.customBakedTexture = null;
                    active.Add(probe);
                    roles.Add(role + (power100 ? "_P100" : "_P0_RESIDUAL"));
                }
                for (int i = 0; i < rejected.Count; i++)
                {
                    rejected[i].enabled = false;
                    inactive.Add(rejected[i]);
                }
            }

            public void AssertPrepared()
            {
                if (activeProbes.Length != ExpectedTotalP100ReflectionCount ||
                    inactiveProbes.Length != ExpectedTotalP100ReflectionCount ||
                    roles.Length != activeProbes.Length)
                {
                    throw new InvalidOperationException("Five-probe reflection bake contract is incomplete.");
                }
                for (int i = 0; i < activeProbes.Length; i++)
                {
                    ReflectionProbe probe = activeProbes[i];
                    if (probe == null || !probe.gameObject.activeInHierarchy || !probe.enabled ||
                        probe.mode != ReflectionProbeMode.Baked || probe.customBakedTexture != null)
                    {
                        throw new InvalidOperationException("Selected reflection probe is not bake-ready.");
                    }
                }
                for (int i = 0; i < inactiveProbes.Length; i++)
                {
                    if (inactiveProbes[i] == null || inactiveProbes[i].enabled)
                        throw new InvalidOperationException("Unselected reflection variant remained enabled.");
                }
            }

            public ReflectionArtifact[] CaptureAfterBake()
            {
                var artifacts = new ReflectionArtifact[activeProbes.Length];
                for (int i = 0; i < activeProbes.Length; i++)
                {
                    ReflectionProbe probe = activeProbes[i];
                    Texture texture = probe.bakedTexture;
                    if (!(texture is Cubemap))
                        throw new InvalidOperationException("Baked reflection output is not a Cubemap.");
                    string path = NormalizePath(AssetDatabase.GetAssetPath(texture));
                    AssertOwnedGeneratedAsset(path, generatedStatePath, "reflection cubemap");
                    artifacts[i] = new ReflectionArtifact
                    {
                        role = roles[i],
                        probeName = probe.name,
                        hierarchyPath = GetHierarchyPath(probe.transform),
                        cubemapPath = path,
                        cubemapSha256 = ComputeFileSha256(path),
                        resolution = probe.resolution,
                        intensity = probe.intensity,
                        boxProjection = probe.boxProjection
                    };
                }
                return artifacts;
            }

            public void AssertBakedPresentationState()
            {
                AssertPrepared();
                for (int i = 0; i < activeProbes.Length; i++)
                {
                    if (!(activeProbes[i].bakedTexture is Cubemap))
                        throw new InvalidOperationException("Baked reflection Cubemap disappeared.");
                }
            }
        }

        private sealed class DoorProbeOverride : IDisposable
        {
            private readonly DoorRendererProbeState[] states;
            private bool disposed;

            private DoorProbeOverride(DoorRendererProbeState[] states)
            {
                this.states = states;
            }

            public static DoorProbeOverride Apply(DoorBakeContract door)
            {
                DoorSideSamples samples = DoorSideSamples.Sample(door);
                var states = new[]
                {
                    DoorRendererProbeState.CaptureAndApply(
                        door.PositiveRenderer,
                        samples.positive.ToProbe(),
                        samples.positive.occlusion),
                    DoorRendererProbeState.CaptureAndApply(
                        door.NegativeRenderer,
                        samples.negative.ToProbe(),
                        samples.negative.occlusion),
                    DoorRendererProbeState.CaptureAndApply(
                        door.EdgeRenderer,
                        samples.edgeAverage.ToProbe(),
                        samples.edgeAverage.occlusion)
                };
                return new DoorProbeOverride(states);
            }

            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                for (int i = states.Length - 1; i >= 0; i--)
                    states[i].Restore();
            }
        }

        private sealed class DoorRendererProbeState
        {
            private readonly Renderer renderer;
            private readonly LightProbeUsage lightProbeUsage;
            private readonly MaterialPropertyBlock globalBlock;
            private readonly MaterialPropertyBlock[] materialBlocks;

            private DoorRendererProbeState(
                Renderer renderer,
                LightProbeUsage lightProbeUsage,
                MaterialPropertyBlock globalBlock,
                MaterialPropertyBlock[] materialBlocks)
            {
                this.renderer = renderer;
                this.lightProbeUsage = lightProbeUsage;
                this.globalBlock = globalBlock;
                this.materialBlocks = materialBlocks;
            }

            public static DoorRendererProbeState CaptureAndApply(
                Renderer renderer,
                SphericalHarmonicsL2 probe,
                Vector4 occlusion)
            {
                var global = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(global);
                int materialCount = renderer.sharedMaterials != null
                    ? renderer.sharedMaterials.Length
                    : 0;
                var materialBlocks = new MaterialPropertyBlock[materialCount];
                for (int i = 0; i < materialCount; i++)
                {
                    materialBlocks[i] = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(materialBlocks[i], i);
                }

                var state = new DoorRendererProbeState(
                    renderer,
                    renderer.lightProbeUsage,
                    global,
                    materialBlocks);
                var probes = new[] { probe };
                var occlusions = new[] { occlusion };
                renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
                if (materialCount == 0)
                {
                    var block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    block.CopySHCoefficientArraysFrom(probes);
                    block.CopyProbeOcclusionArrayFrom(occlusions);
                    renderer.SetPropertyBlock(block);
                }
                else
                {
                    for (int i = 0; i < materialCount; i++)
                    {
                        var block = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(block, i);
                        block.CopySHCoefficientArraysFrom(probes);
                        block.CopyProbeOcclusionArrayFrom(occlusions);
                        renderer.SetPropertyBlock(block, i);
                    }
                }
                return state;
            }

            public void Restore()
            {
                if (renderer == null)
                    return;
                renderer.lightProbeUsage = lightProbeUsage;
                renderer.SetPropertyBlock(globalBlock.isEmpty ? null : globalBlock);
                for (int i = 0; i < materialBlocks.Length; i++)
                {
                    renderer.SetPropertyBlock(
                        materialBlocks[i].isEmpty ? null : materialBlocks[i],
                        i);
                }
            }
        }

        private sealed class RoomRendererContract
        {
            private readonly RendererInvariantEntry[] entries;

            private RoomRendererContract(RendererInvariantEntry[] entries)
            {
                this.entries = entries;
            }

            public static RoomRendererContract Capture(
                GameObject startRoom,
                GameObject administrativeRoom)
            {
                var entries = new List<RendererInvariantEntry>(ExpectedRendererCount);
                Append(entries, startRoom, "Start");
                Append(entries, administrativeRoom, "Administrative");
                if (entries.Count != ExpectedRendererCount)
                {
                    throw new InvalidOperationException(
                        "Production renderer contract count changed before power application.");
                }
                return new RoomRendererContract(entries.ToArray());
            }

            private static void Append(
                List<RendererInvariantEntry> entries,
                GameObject room,
                string role)
            {
                Renderer[] renderers = room.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                    entries.Add(RendererInvariantEntry.Capture(renderers[i], room.transform, role));
            }

            public void AssertPowerMaterials(
                PowerApplyResult start,
                PowerApplyResult administrative)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    PowerApplyResult result = entries[i].Role == "Start" ? start : administrative;
                    entries[i].AssertAfterPower(result.ExpectedEmissionSlots);
                }
            }

            public void AssertPropertyBlockPresenceRestored()
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].AssertPropertyBlockPresence();
            }

            public void AssertProductionSurfaceStateRestored()
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].AssertRendererSurfaceState();
            }
        }

        private sealed class RendererInvariantEntry
        {
            private readonly Renderer renderer;
            private readonly Mesh mesh;
            private readonly Mesh additionalVertexStreams;
            private readonly Material[] materials;
            private readonly MaterialStructure[] materialStructures;
            private readonly bool hadPropertyBlock;
            private readonly int materialCount;
            private readonly bool enabled;
            private readonly bool forceRenderingOff;
            private readonly ShadowCastingMode shadowCastingMode;
            private readonly bool receiveShadows;
            private readonly uint renderingLayerMask;
            private readonly LightProbeUsage lightProbeUsage;
            private readonly ReflectionProbeUsage reflectionProbeUsage;
            private readonly StaticEditorFlags staticFlags;

            private RendererInvariantEntry(
                Renderer renderer,
                string role,
                string path,
                Mesh mesh,
                Mesh additionalVertexStreams,
                Material[] materials,
                MaterialStructure[] materialStructures,
                bool hadPropertyBlock,
                bool enabled,
                bool forceRenderingOff,
                ShadowCastingMode shadowCastingMode,
                bool receiveShadows,
                uint renderingLayerMask,
                LightProbeUsage lightProbeUsage,
                ReflectionProbeUsage reflectionProbeUsage,
                StaticEditorFlags staticFlags)
            {
                this.renderer = renderer;
                Role = role;
                Path = path;
                this.mesh = mesh;
                this.additionalVertexStreams = additionalVertexStreams;
                this.materials = materials;
                this.materialStructures = materialStructures;
                this.hadPropertyBlock = hadPropertyBlock;
                materialCount = materials.Length;
                this.enabled = enabled;
                this.forceRenderingOff = forceRenderingOff;
                this.shadowCastingMode = shadowCastingMode;
                this.receiveShadows = receiveShadows;
                this.renderingLayerMask = renderingLayerMask;
                this.lightProbeUsage = lightProbeUsage;
                this.reflectionProbeUsage = reflectionProbeUsage;
                this.staticFlags = staticFlags;
            }

            public string Role { get; }
            public string Path { get; }

            public static RendererInvariantEntry Capture(
                Renderer renderer,
                Transform room,
                string role)
            {
                Material[] materials = renderer.sharedMaterials != null
                    ? (Material[])renderer.sharedMaterials.Clone()
                    : Array.Empty<Material>();
                var structures = new MaterialStructure[materials.Length];
                for (int i = 0; i < materials.Length; i++)
                    structures[i] = MaterialStructure.Capture(materials[i]);
                return new RendererInvariantEntry(
                    renderer,
                    role,
                    AnimationUtility.CalculateTransformPath(renderer.transform, room),
                    ResolveRendererMesh(renderer),
                    renderer is MeshRenderer meshRenderer
                        ? meshRenderer.additionalVertexStreams
                        : null,
                    materials,
                    structures,
                    renderer.HasPropertyBlock(),
                    renderer.enabled,
                    renderer.forceRenderingOff,
                    renderer.shadowCastingMode,
                    renderer.receiveShadows,
                    renderer.renderingLayerMask,
                    renderer.lightProbeUsage,
                    renderer.reflectionProbeUsage,
                    GameObjectUtility.GetStaticEditorFlags(renderer.gameObject));
            }

            public void AssertAfterPower(
                Dictionary<Renderer, Dictionary<int, Material>> expectedEmissionSlots)
            {
                if (renderer == null || ResolveRendererMesh(renderer) != mesh)
                    throw new InvalidOperationException(Role + " renderer mesh changed at " + Path + ".");
                if (renderer is MeshRenderer meshRenderer &&
                    meshRenderer.additionalVertexStreams != additionalVertexStreams)
                {
                    throw new InvalidOperationException(
                        Role + " additionalVertexStreams changed at " + Path + ".");
                }

                Material[] current = renderer.sharedMaterials ?? Array.Empty<Material>();
                if (current.Length != materialCount)
                    throw new InvalidOperationException(Role + " material array size changed at " + Path + ".");
                expectedEmissionSlots.TryGetValue(
                    renderer,
                    out Dictionary<int, Material> allowedSlots);
                for (int i = 0; i < current.Length; i++)
                {
                    if (allowedSlots != null && allowedSlots.TryGetValue(i, out Material expected))
                    {
                        if (current[i] != expected)
                        {
                            throw new InvalidOperationException(
                                Role + " emission slot differs from DungeonTilePowerBakeSet at " +
                                Path + "[" + i + "].");
                        }
                        continue;
                    }

                    if (current[i] != materials[i])
                    {
                        throw new InvalidOperationException(
                            Role + " non-emission material changed at " + Path + "[" + i + "].");
                    }
                    materialStructures[i].AssertUnchanged(current[i], Role + "/" + Path + "[" + i + "]");
                }
                AssertPropertyBlockPresence();
            }

            public void AssertPropertyBlockPresence()
            {
                if (renderer != null && renderer.HasPropertyBlock() != hadPropertyBlock)
                {
                    throw new InvalidOperationException(
                        Role + " MaterialPropertyBlock presence changed at " + Path + ".");
                }
            }

            public void AssertRendererSurfaceState()
            {
                if (renderer == null || renderer.enabled != enabled ||
                    renderer.forceRenderingOff != forceRenderingOff ||
                    renderer.shadowCastingMode != shadowCastingMode ||
                    renderer.receiveShadows != receiveShadows ||
                    renderer.renderingLayerMask != renderingLayerMask ||
                    renderer.lightProbeUsage != lightProbeUsage ||
                    renderer.reflectionProbeUsage != reflectionProbeUsage ||
                    GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) != staticFlags)
                {
                    throw new InvalidOperationException(
                        Role + " production renderer surface state changed at " + Path + ".");
                }
            }
        }

        private sealed class MaterialStructure
        {
            private readonly Material material;
            private readonly Shader shader;
            private readonly int renderQueue;
            private readonly string[] keywords;

            private MaterialStructure(
                Material material,
                Shader shader,
                int renderQueue,
                string[] keywords)
            {
                this.material = material;
                this.shader = shader;
                this.renderQueue = renderQueue;
                this.keywords = keywords;
            }

            public static MaterialStructure Capture(Material material)
            {
                string[] keywords = material != null
                    ? (string[])material.shaderKeywords.Clone()
                    : Array.Empty<string>();
                Array.Sort(keywords, StringComparer.Ordinal);
                return new MaterialStructure(
                    material,
                    material != null ? material.shader : null,
                    material != null ? material.renderQueue : -1,
                    keywords);
            }

            public void AssertUnchanged(Material current, string label)
            {
                string[] currentKeywords = current != null
                    ? (string[])current.shaderKeywords.Clone()
                    : Array.Empty<string>();
                Array.Sort(currentKeywords, StringComparer.Ordinal);
                if (current != material ||
                    (current != null && current.shader != shader) ||
                    (current != null && current.renderQueue != renderQueue) ||
                    !keywords.SequenceEqual(currentKeywords, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException("Material structure changed: " + label + ".");
                }
            }
        }

        private sealed class EnvironmentContract
        {
            public bool fog;
            public FogMode fogMode;
            public Color fogColor;
            public float fogDensity;
            public float fogStartDistance;
            public float fogEndDistance;
            public AmbientMode ambientMode;
            public Color ambientSkyColor;
            public Color ambientEquatorColor;
            public Color ambientGroundColor;
            public Material skybox;
            public int defaultReflectionResolution;
            public float reflectionIntensity;
            public int reflectionBounces;
            public string volumeProfilePath;

            public static EnvironmentContract CaptureAndAssert(Transform volumeRoot)
            {
                Volume[] volumes = volumeRoot.GetComponentsInChildren<Volume>(true);
                if (volumes.Length != 1 || !volumes[0].enabled ||
                    !volumes[0].gameObject.activeInHierarchy || !volumes[0].isGlobal ||
                    volumes[0].sharedProfile == null)
                {
                    throw new InvalidOperationException(
                        "Validation global Volume contract changed.");
                }
                var result = new EnvironmentContract
                {
                    fog = RenderSettings.fog,
                    fogMode = RenderSettings.fogMode,
                    fogColor = RenderSettings.fogColor,
                    fogDensity = RenderSettings.fogDensity,
                    fogStartDistance = RenderSettings.fogStartDistance,
                    fogEndDistance = RenderSettings.fogEndDistance,
                    ambientMode = RenderSettings.ambientMode,
                    ambientSkyColor = RenderSettings.ambientSkyColor,
                    ambientEquatorColor = RenderSettings.ambientEquatorColor,
                    ambientGroundColor = RenderSettings.ambientGroundColor,
                    skybox = RenderSettings.skybox,
                    defaultReflectionResolution = RenderSettings.defaultReflectionResolution,
                    reflectionIntensity = RenderSettings.reflectionIntensity,
                    reflectionBounces = RenderSettings.reflectionBounces,
                    volumeProfilePath = AssetDatabase.GetAssetPath(volumes[0].sharedProfile)
                };
                if (!result.fog || result.fogMode != FogMode.ExponentialSquared ||
                    result.ambientMode != AmbientMode.Trilight || result.skybox != null ||
                    result.defaultReflectionResolution != 256 ||
                    !NearlyEqual(result.reflectionIntensity, 0.85f) ||
                    result.reflectionBounces != 1)
                {
                    throw new InvalidOperationException(
                        "Copied scene must preserve StartMap Exp2 fog, Trilight, null skybox, " +
                        "reflection resolution 256, intensity 0.85, and one bounce.");
                }
                return result;
            }

            public void AssertUnchanged(Transform volumeRoot)
            {
                EnvironmentContract current = CaptureAndAssert(volumeRoot);
                if (current.fog != fog || current.fogMode != fogMode ||
                    current.fogColor != fogColor || !NearlyEqual(current.fogDensity, fogDensity) ||
                    !NearlyEqual(current.fogStartDistance, fogStartDistance) ||
                    !NearlyEqual(current.fogEndDistance, fogEndDistance) ||
                    current.ambientMode != ambientMode ||
                    current.ambientSkyColor != ambientSkyColor ||
                    current.ambientEquatorColor != ambientEquatorColor ||
                    current.ambientGroundColor != ambientGroundColor || current.skybox != skybox ||
                    current.defaultReflectionResolution != defaultReflectionResolution ||
                    !NearlyEqual(current.reflectionIntensity, reflectionIntensity) ||
                    current.reflectionBounces != reflectionBounces ||
                    !string.Equals(current.volumeProfilePath, volumeProfilePath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Copied StartMap environment changed during pilot.");
                }
            }
        }

        private sealed class RunArtifactTransaction
        {
            private const string OwnershipFilenamePrefix = "__JOINT_PAIR_RUN_OWNER__";
            // Keep the token-owned working path below the legacy Windows MAX_PATH
            // boundary; the full timestamped runId is retained for canonical publish.
            private const string StagingFolderPrefix = "__S__";
            private const string CommitReceiptFilename = "COMMITTED.txt";
            private const string PostPublishFailureFilename =
                "POST_PUBLISH_VERIFICATION_FAILED.txt";

            private readonly string generatedOwnershipPath;
            private readonly string evidenceOwnershipPath;
            private readonly string generatedOwnershipAbsolutePath;
            private readonly string evidenceOwnershipAbsolutePath;
            private readonly string ownershipContents;
            private readonly string sourceLightingSettingsCanonicalSerializedFingerprint;
            private bool committed;

            private RunArtifactTransaction(
                string runId,
                string token,
                string generatedRunPath,
                string evidenceRunPath,
                string generatedWorkingPath,
                string evidenceWorkingPath,
                string generatedOwnershipPath,
                string evidenceOwnershipPath,
                string generatedOwnershipAbsolutePath,
                string evidenceOwnershipAbsolutePath,
                string ownershipContents,
                string sourceLightingSettingsCanonicalSerializedFingerprint)
            {
                RunId = runId;
                Token = token;
                GeneratedRunPath = generatedRunPath;
                EvidenceRunPath = evidenceRunPath;
                GeneratedWorkingPath = generatedWorkingPath;
                EvidenceWorkingPath = evidenceWorkingPath;
                this.generatedOwnershipPath = generatedOwnershipPath;
                this.evidenceOwnershipPath = evidenceOwnershipPath;
                this.generatedOwnershipAbsolutePath = generatedOwnershipAbsolutePath;
                this.evidenceOwnershipAbsolutePath = evidenceOwnershipAbsolutePath;
                this.ownershipContents = ownershipContents;
                this.sourceLightingSettingsCanonicalSerializedFingerprint =
                    sourceLightingSettingsCanonicalSerializedFingerprint;
            }

            public string RunId { get; }
            public string Token { get; }
            public string GeneratedRunPath { get; }
            public string EvidenceRunPath { get; }
            public string GeneratedWorkingPath { get; }
            public string EvidenceWorkingPath { get; }
            public bool BakeStartedByThisRun { get; set; }

            public static RunArtifactTransaction Begin(string runId)
            {
                if (string.IsNullOrWhiteSpace(runId) || runId.IndexOf('/') >= 0 ||
                    runId.IndexOf('\\') >= 0)
                {
                    throw new InvalidOperationException("Invalid pilot runId.");
                }
                EnsureAssetFolder(GeneratedRoot);
                EnsureAssetFolder(EvidenceRoot);
                string token = runId.Substring(runId.LastIndexOf('_') + 1);
                if (token.Length != 32)
                    throw new InvalidOperationException("Run ownership token is invalid.");

                string generatedRun = GeneratedRoot + "/" + runId;
                string evidenceRun = EvidenceRoot + "/" + runId;
                string stagingName = StagingFolderPrefix + token;
                string generatedWorking = GeneratedRoot + "/" + stagingName;
                string evidenceWorking = EvidenceRoot + "/" + stagingName;
                AssertPlannedRunArtifactPaths(
                    token,
                    generatedRun,
                    evidenceRun,
                    generatedWorking,
                    evidenceWorking);
                if (AssetDatabase.IsValidFolder(generatedRun) ||
                    AssetDatabase.IsValidFolder(evidenceRun) ||
                    AssetDatabase.IsValidFolder(generatedWorking) ||
                    AssetDatabase.IsValidFolder(evidenceWorking) ||
                    Directory.Exists(AssetPathToAbsolutePath(generatedRun)) ||
                    Directory.Exists(AssetPathToAbsolutePath(evidenceRun)) ||
                    Directory.Exists(AssetPathToAbsolutePath(generatedWorking)) ||
                    Directory.Exists(AssetPathToAbsolutePath(evidenceWorking)))
                {
                    throw new InvalidOperationException("Pilot canonical or staging run folders already exist.");
                }

                try
                {
                    if (!CreateExactFolder(
                            GeneratedRoot,
                            stagingName,
                            generatedWorking))
                        throw new IOException("Generated staging folder was not created.");
                    if (!CreateExactFolder(
                            EvidenceRoot,
                            stagingName,
                            evidenceWorking))
                        throw new IOException("Evidence staging folder was not created.");
                    string ownership =
                        "DungeonPortalJointPairTotalPilotOwnership/v1\n" +
                        "runId=" + runId + "\n" +
                        "token=" + token + "\n" +
                        "generatedCanonical=" + generatedRun + "\n" +
                        "evidenceCanonical=" + evidenceRun + "\n" +
                        "generatedStaging=" + generatedWorking + "\n" +
                        "evidenceStaging=" + evidenceWorking + "\n";
                    string filename = OwnershipFilenamePrefix + token + ".txt";
                    string generatedManifest = generatedWorking + "/" + filename;
                    string evidenceManifest = evidenceWorking + "/" + filename;
                    string generatedAbsolute = AssetPathToAbsolutePath(generatedManifest);
                    string evidenceAbsolute = AssetPathToAbsolutePath(evidenceManifest);
                    // Registering the second sibling through a synchronous refresh can
                    // transiently remove the first still-empty directory. Re-materialize
                    // both already validated exact parents immediately before writing the
                    // ownership files; the markers then keep the folders non-empty.
                    Directory.CreateDirectory(Path.GetDirectoryName(generatedAbsolute));
                    Directory.CreateDirectory(Path.GetDirectoryName(evidenceAbsolute));
                    if (!Directory.Exists(Path.GetDirectoryName(generatedAbsolute)) ||
                        !Directory.Exists(Path.GetDirectoryName(evidenceAbsolute)))
                    {
                        throw new IOException(
                            "Staging backing directories disappeared before ownership write.");
                    }
                    File.WriteAllText(generatedAbsolute, ownership, new UTF8Encoding(false));
                    File.WriteAllText(evidenceAbsolute, ownership, new UTF8Encoding(false));
                    AssetDatabase.ImportAsset(
                        generatedManifest,
                        ImportAssetOptions.ForceSynchronousImport);
                    AssetDatabase.ImportAsset(
                        evidenceManifest,
                        ImportAssetOptions.ForceSynchronousImport);

                    var transaction = new RunArtifactTransaction(
                        runId,
                        token,
                        generatedRun,
                        evidenceRun,
                        generatedWorking,
                        evidenceWorking,
                        generatedManifest,
                        evidenceManifest,
                        generatedAbsolute,
                        evidenceAbsolute,
                        ownership,
                        ComputeLightingSettingsCanonicalSerializedFingerprint(
                            RequireAsset<LightingSettings>(LightingSettingsPath),
                            LightingSettingsPath));
                    transaction.AssertOwnership();
                    return transaction;
                }
                catch (Exception exception)
                {
                    string contents =
                        "status=FAILED\nrunId=" + runId + "\ncanonicalPublished=false\n" +
                        "failure=" + exception + "\n";
                    WriteFailedMarkerWherePresent(generatedWorking, contents);
                    WriteFailedMarkerWherePresent(evidenceWorking, contents);
                    throw new InvalidOperationException(
                        "Pilot staging initialization failed; any created staging was preserved. " +
                        "generated=" + generatedWorking + " evidence=" + evidenceWorking,
                        exception);
                }
            }

            private static void AssertPlannedRunArtifactPaths(
                string token,
                string generatedRun,
                string evidenceRun,
                string generatedWorking,
                string evidenceWorking)
            {
                if (!string.Equals(
                        Path.GetFileNameWithoutExtension(CopiedSceneFilename),
                        GeneratedSceneArtifactFolderName,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Copied scene filename and generated artifact-folder contract differ.");
                }
                string ownerFilename = OwnershipFilenamePrefix + token + ".txt";
                string[] allRoots =
                {
                    generatedWorking,
                    evidenceWorking,
                    generatedRun,
                    evidenceRun
                };
                for (int i = 0; i < allRoots.Length; i++)
                {
                    AssertUnityImportSafeAssetPath(allRoots[i], "planned run folder");
                    AssertUnityImportSafeAssetPath(
                        allRoots[i] + "/" + ownerFilename,
                        "planned ownership marker");
                    AssertUnityImportSafeAssetPath(
                        allRoots[i] + "/CAPTURE_STATE.txt",
                        "planned capture-state marker");
                    AssertUnityImportSafeAssetPath(
                        allRoots[i] + "/FAILED.txt",
                        "planned failure marker");
                    AssertUnityImportSafeAssetPath(
                        allRoots[i] + "/RESTORE_FAILED_BLOCKED.txt",
                        "planned restore-failure marker");
                    AssertUnityImportSafeAssetPath(
                        allRoots[i] + "/" + PostPublishFailureFilename,
                        "planned post-publish marker");
                }

                string[] evidenceRoots = { evidenceWorking, evidenceRun };
                for (int i = 0; i < evidenceRoots.Length; i++)
                {
                    AssertUnityImportSafeAssetPath(
                        evidenceRoots[i] + "/manifest_JOINT_PAIR_TOTAL_GT_CANDIDATE.txt",
                        "planned evidence manifest");
                    AssertUnityImportSafeAssetPath(
                        evidenceRoots[i] + "/source_state_snapshot.txt",
                        "planned source snapshot");
                    AssertUnityImportSafeAssetPath(
                        evidenceRoots[i] + "/" + CommitReceiptFilename,
                        "planned commit receipt");
                }

                for (int stateIndex = 0; stateIndex < PilotStates.Length; stateIndex++)
                {
                    string stateId = PilotStates[stateIndex].Id;
                    string generatedStateWorking = generatedWorking + "/" + stateId;
                    string generatedStateCanonical = generatedRun + "/" + stateId;
                    string evidenceStateWorking = evidenceWorking + "/" + stateId;
                    string evidenceStateCanonical = evidenceRun + "/" + stateId;
                    string[] generatedStateRoots =
                    {
                        generatedStateWorking,
                        generatedStateCanonical
                    };
                    for (int i = 0; i < generatedStateRoots.Length; i++)
                    {
                        AssertUnityImportSafeAssetPath(
                            generatedStateRoots[i],
                            "planned generated state folder");
                        AssertUnityImportSafeAssetPath(
                            generatedStateRoots[i] + "/" + CopiedSceneFilename,
                            "planned copied scene");
                        AssertUnityImportSafeAssetPath(
                            generatedStateRoots[i] + "/" + CopiedLightingSettingsFilename,
                            "planned copied lighting settings");
                        AssertUnityImportSafeAssetPath(
                            generatedStateRoots[i] + "/" + GeneratedSceneArtifactFolderName,
                            "planned Unity-generated scene artifact folder");
                    }

                    string[] evidenceStateRoots =
                    {
                        evidenceStateWorking,
                        evidenceStateCanonical
                    };
                    for (int i = 0; i < evidenceStateRoots.Length; i++)
                    {
                        string root = evidenceStateRoots[i];
                        AssertUnityImportSafeAssetPath(root, "planned evidence state folder");
                        AssertUnityImportSafeAssetPath(
                            root + "/probe_sh_and_occlusion.json",
                            "planned probe evidence");
                        AssertUnityImportSafeAssetPath(
                            root + "/renderer_lightmap_mapping.json",
                            "planned renderer-map evidence");
                        for (int cameraIndex = 0;
                             cameraIndex < ExpectedCameraCount;
                             cameraIndex++)
                        {
                            string cameraName = cameraIndex == 0
                                ? StartCameraName
                                : AdministrativeCameraName;
                            string stem = GetShortCaptureStem(cameraName);
                            AssertUnityImportSafeAssetPath(
                                root + "/" + stem + ".png",
                                "planned presentation capture");
                            AssertUnityImportSafeAssetPath(
                                root + "/" + stem + "_L.exr",
                                "planned linear capture");
                        }
                    }
                }
            }

            public StateOwnedPaths CopyOwnedInputs(
                string stateId,
                string sourceScenePath,
                string sourceLightingSettingsPath)
            {
                AssertOwnership();
                if (string.IsNullOrWhiteSpace(stateId) || stateId.IndexOf('/') >= 0 ||
                    stateId.IndexOf('\\') >= 0)
                {
                    throw new InvalidOperationException("Invalid state identifier.");
                }
                string stateFolder = GeneratedWorkingPath + "/" + stateId;
                if (!CreateExactFolder(GeneratedWorkingPath, stateId, stateFolder))
                    throw new InvalidOperationException("Could not create state generated folder.");
                string copiedScene = stateFolder + "/" + CopiedSceneFilename;
                string copiedSettings = stateFolder + "/" + CopiedLightingSettingsFilename;
                if (!AssetDatabase.CopyAsset(sourceScenePath, copiedScene) ||
                    !AssetDatabase.CopyAsset(sourceLightingSettingsPath, copiedSettings))
                {
                    throw new InvalidOperationException("Could not copy isolated scene/lighting settings.");
                }
                AssetDatabase.ImportAsset(copiedScene, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(copiedSettings, ImportAssetOptions.ForceSynchronousImport);

                string sourceSceneGuid = AssetDatabase.AssetPathToGUID(sourceScenePath);
                string copiedSceneGuid = AssetDatabase.AssetPathToGUID(copiedScene);
                string sourceSettingsGuid = AssetDatabase.AssetPathToGUID(sourceLightingSettingsPath);
                string copiedSettingsGuid = AssetDatabase.AssetPathToGUID(copiedSettings);
                if (string.IsNullOrEmpty(copiedSceneGuid) || string.IsNullOrEmpty(copiedSettingsGuid) ||
                    string.Equals(sourceSceneGuid, copiedSceneGuid, StringComparison.Ordinal) ||
                    string.Equals(sourceSettingsGuid, copiedSettingsGuid, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Copied input GUID isolation failed.");
                }

                LightingSettings copied = AssetDatabase.LoadAssetAtPath<LightingSettings>(copiedSettings);
                if (copied == null)
                    throw new InvalidOperationException("Copied LightingSettings did not load.");
                string fingerprint = ComputeLightingSettingsCanonicalSerializedFingerprint(
                    copied,
                    copiedSettings);
                if (!string.Equals(
                        fingerprint,
                        sourceLightingSettingsCanonicalSerializedFingerprint,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Copied LightingSettings canonical serialized fingerprint changed outside " +
                        "the single validated m_Name field.");
                }
                return new StateOwnedPaths
                {
                    StateId = stateId,
                    StateGeneratedPath = stateFolder,
                    CopiedScenePath = copiedScene,
                    CopiedLightingSettingsPath = copiedSettings,
                    SourceSceneGuid = sourceSceneGuid,
                    CopiedSceneGuid = copiedSceneGuid,
                    SourceLightingSettingsGuid = sourceSettingsGuid,
                    CopiedLightingSettingsGuid = copiedSettingsGuid,
                    LightingSettingsCanonicalSerializedFingerprint = fingerprint
                };
            }

            public void AssertCopiedLightingSettingsEquivalent(
                StateOwnedPaths paths,
                LightingSettings copied)
            {
                AssertOwnership();
                if (copied == null ||
                    !string.Equals(
                        NormalizePath(AssetDatabase.GetAssetPath(copied)),
                        paths.CopiedLightingSettingsPath,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        ComputeLightingSettingsCanonicalSerializedFingerprint(
                            copied,
                            paths.CopiedLightingSettingsPath),
                        sourceLightingSettingsCanonicalSerializedFingerprint,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Owned LightingSettings equivalence failed outside the single validated " +
                        "m_Name field.");
                }
            }

            public void AssertGeneratedStateTreePublishSafe(string stateGeneratedPath)
            {
                AssertOwnership();
                string normalized = NormalizePath(stateGeneratedPath).TrimEnd('/');
                if (!normalized.StartsWith(
                        GeneratedWorkingPath + "/",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Generated state path escaped the owned staging root: " + normalized);
                }
                AssertTreePublishSafe(
                    normalized,
                    CanonicalizePublishedPath(normalized),
                    "generated state runtime artifact");
            }

            public void WriteEvidence(PilotEvidence evidence)
            {
                AssertOwnership();
                if (evidence == null || evidence.States == null ||
                    evidence.States.Count != PilotStates.Length)
                {
                    throw new InvalidOperationException("Complete four-state evidence is required.");
                }

                WriteTextAsset(
                    EvidenceWorkingPath + "/manifest_JOINT_PAIR_TOTAL_GT_CANDIDATE.txt",
                    evidence.Manifest);
                WriteTextAsset(
                    EvidenceWorkingPath + "/source_state_snapshot.txt",
                    evidence.SourceSnapshotText);

                for (int stateIndex = 0; stateIndex < evidence.States.Count; stateIndex++)
                {
                    PilotStateEvidence state = evidence.States[stateIndex];
                    string stateFolder = EvidenceWorkingPath + "/" + state.State.Id;
                    EnsureAssetFolder(stateFolder);
                    WriteTextAsset(stateFolder + "/probe_sh_and_occlusion.json", state.ProbeJson);
                    WriteTextAsset(stateFolder + "/renderer_lightmap_mapping.json", state.RendererMapJson);
                    for (int imageIndex = 0; imageIndex < state.Images.Count; imageIndex++)
                    {
                        BufferedImage image = state.Images[imageIndex];
                        WriteBinaryAsset(stateFolder + "/" + image.pngFilename, image.pngBytes);
                        WriteBinaryAsset(stateFolder + "/" + image.exrFilename, image.exrBytes);
                    }
                }

                VerifyWrittenEvidence(evidence);
            }

            private void VerifyWrittenEvidence(PilotEvidence evidence)
            {
                for (int stateIndex = 0; stateIndex < evidence.States.Count; stateIndex++)
                {
                    PilotStateEvidence state = evidence.States[stateIndex];
                    string stateFolder = EvidenceWorkingPath + "/" + state.State.Id;
                    for (int imageIndex = 0; imageIndex < state.Images.Count; imageIndex++)
                    {
                        BufferedImage image = state.Images[imageIndex];
                        string pngPath = stateFolder + "/" + image.pngFilename;
                        string exrPath = stateFolder + "/" + image.exrFilename;
                        VerifyWrittenBytes(pngPath, image.pngBytes, image.pngSha256);
                        VerifyWrittenBytes(exrPath, image.exrBytes, image.exrSha256);
                        byte[] png = File.ReadAllBytes(AssetPathToAbsolutePath(pngPath));
                        if (png.Length < 24 || png[0] != 137 || png[1] != 80 ||
                            ReadBigEndianInt32(png, 16) != CaptureWidth ||
                            ReadBigEndianInt32(png, 20) != CaptureHeight)
                        {
                            throw new InvalidOperationException("Written PNG dimensions are invalid.");
                        }
                        if (AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath) == null ||
                            AssetDatabase.LoadAssetAtPath<Texture2D>(exrPath) == null)
                        {
                            throw new InvalidOperationException("Written PNG/EXR import verification failed.");
                        }
                    }
                }
            }

            public void Commit(PilotEvidence evidence, Action postPublishVerification)
            {
                AssertOwnership();
                if (evidence == null)
                    throw new ArgumentNullException(nameof(evidence));
                string generatedCaptureState =
                    "status=PAYLOAD_COMPLETE_AWAIT_COMMITTED_RECEIPT\n" +
                    "runId=" + RunId + "\ncanonical=" + GeneratedRunPath + "\n";
                string evidenceCaptureState =
                    "status=PAYLOAD_COMPLETE_AWAIT_COMMITTED_RECEIPT\n" +
                    "runId=" + RunId + "\ncanonical=" + EvidenceRunPath + "\n";
                string receiptPlaceholder =
                    "DungeonPortalJointPairCommitReceipt/v1\n" +
                    "status=NOT_COMMITTED\nrunId=" + RunId + "\n" +
                    "consumerValidation=Canonical payload is not successful until this receipt is replaced and validated.\n";
                WriteTextAsset(
                    GeneratedWorkingPath + "/CAPTURE_STATE.txt",
                    generatedCaptureState);
                WriteTextAsset(
                    EvidenceWorkingPath + "/CAPTURE_STATE.txt",
                    evidenceCaptureState);
                WriteTextAsset(
                    EvidenceWorkingPath + "/" + CommitReceiptFilename,
                    receiptPlaceholder);
                AssertOwnership();
                AssertTreePublishSafe(
                    GeneratedWorkingPath,
                    GeneratedRunPath,
                    "complete generated payload");
                AssertTreePublishSafe(
                    EvidenceWorkingPath,
                    EvidenceRunPath,
                    "complete evidence payload");

                bool generatedPublished = false;
                bool evidencePublished = false;
                try
                {
                    AssetDatabase.DisallowAutoRefresh();
                    try
                    {
                        PublishDirectoryAtomic(GeneratedWorkingPath, GeneratedRunPath);
                        generatedPublished = true;
                        PublishDirectoryAtomic(EvidenceWorkingPath, EvidenceRunPath);
                        evidencePublished = true;
                    }
                    finally
                    {
                        AssetDatabase.AllowAutoRefresh();
                    }
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    VerifyPublishedEvidence(
                        evidence,
                        generatedCaptureState,
                        evidenceCaptureState,
                        receiptPlaceholder);
                    postPublishVerification?.Invoke();
                    string generatedPublishedOwnership =
                        CanonicalizePublishedPath(generatedOwnershipPath);
                    string evidencePublishedOwnership =
                        CanonicalizePublishedPath(evidenceOwnershipPath);
                    if (!AssetDatabase.DeleteAsset(generatedPublishedOwnership) ||
                        !AssetDatabase.DeleteAsset(evidencePublishedOwnership))
                    {
                        throw new InvalidOperationException(
                            "Could not remove committed ownership markers.");
                    }
                    postPublishVerification?.Invoke();

                    string generatedPayloadSha256 = ComputeDirectoryPayloadSha256(
                        GeneratedRunPath,
                        Array.Empty<string>());
                    string evidencePayloadSha256 = ComputeDirectoryPayloadSha256(
                        EvidenceRunPath,
                        new[] { CommitReceiptFilename });
                    string receiptPath = EvidenceRunPath + "/" + CommitReceiptFilename;
                    string manifestPath =
                        EvidenceRunPath + "/manifest_JOINT_PAIR_TOTAL_GT_CANDIDATE.txt";
                    string receipt = BuildCommitReceipt(
                        generatedPayloadSha256,
                        evidencePayloadSha256,
                        generatedCaptureState,
                        evidenceCaptureState,
                        manifestPath,
                        receiptPath);

                    // This is deliberately the final successful payload write. The pre-imported
                    // placeholder ensures no import/meta write is needed after the receipt.
                    File.WriteAllText(
                        AssetPathToAbsolutePath(receiptPath),
                        receipt,
                        new UTF8Encoding(false));
                    ValidateConsumerCommitReceipt(receiptPath);
                    committed = true;
                }
                catch (Exception exception)
                {
                    MarkPostPublishFailure(exception);
                    AssetDatabase.DisallowAutoRefresh();
                    try
                    {
                        if (evidencePublished)
                            MovePublishedDirectoryBackToStaging(
                                EvidenceRunPath,
                                EvidenceWorkingPath);
                        if (generatedPublished)
                            MovePublishedDirectoryBackToStaging(
                                GeneratedRunPath,
                                GeneratedWorkingPath);
                    }
                    finally
                    {
                        AssetDatabase.AllowAutoRefresh();
                    }
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    throw;
                }
            }

            private string BuildCommitReceipt(
                string generatedPayloadSha256,
                string evidencePayloadSha256,
                string generatedCaptureState,
                string evidenceCaptureState,
                string manifestPath,
                string receiptPath)
            {
                return
                    "DungeonPortalJointPairCommitReceipt/v1\n" +
                    "status=COMMITTED\n" +
                    "runId=" + RunId + "\n" +
                    "generatedRoot=" + GeneratedRunPath + "\n" +
                    "evidenceRoot=" + EvidenceRunPath + "\n" +
                    "receiptPath=" + receiptPath + "\n" +
                    "generatedRootPayloadSha256=" + generatedPayloadSha256 + "\n" +
                    "evidenceRootPayloadSha256ExcludingReceipt=" +
                    evidencePayloadSha256 + "\n" +
                    "generatedCaptureStatePath=" + GeneratedRunPath +
                    "/CAPTURE_STATE.txt\n" +
                    "generatedCaptureStateSha256=" +
                    ComputeSha256(Encoding.UTF8.GetBytes(generatedCaptureState)) + "\n" +
                    "evidenceCaptureStatePath=" + EvidenceRunPath +
                    "/CAPTURE_STATE.txt\n" +
                    "evidenceCaptureStateSha256=" +
                    ComputeSha256(Encoding.UTF8.GetBytes(evidenceCaptureState)) + "\n" +
                    "manifestPath=" + manifestPath + "\n" +
                    "manifestSha256=" + ComputeFileSha256(manifestPath) + "\n" +
                    "sourceScenePath=" + ValidationScenePath + "\n" +
                    "sourceSceneSha256=" + ExpectedValidationSceneRawSha256 + "\n" +
                    "correctedRealtimeManifestPath=" + CorrectedRealtimeManifestPath + "\n" +
                    "correctedRealtimeManifestSha256=" +
                    ExpectedCorrectedRealtimeManifestRawSha256 + "\n" +
                    "consumerValidation=Both canonical roots exist; staging roots and metas are absent; receipt paths, source bindings, and recomputed payload hashes match.\n";
            }

            private void ValidateConsumerCommitReceipt(string receiptPath)
            {
                string generatedAbsolute = AssetPathToAbsolutePath(GeneratedRunPath);
                string evidenceAbsolute = AssetPathToAbsolutePath(EvidenceRunPath);
                string generatedStagingAbsolute = AssetPathToAbsolutePath(GeneratedWorkingPath);
                string evidenceStagingAbsolute = AssetPathToAbsolutePath(EvidenceWorkingPath);
                if (!Directory.Exists(generatedAbsolute) || !Directory.Exists(evidenceAbsolute) ||
                    Directory.Exists(generatedStagingAbsolute) ||
                    Directory.Exists(evidenceStagingAbsolute) ||
                    !File.Exists(generatedAbsolute + ".meta") ||
                    !File.Exists(evidenceAbsolute + ".meta") ||
                    File.Exists(generatedStagingAbsolute + ".meta") ||
                    File.Exists(evidenceStagingAbsolute + ".meta") ||
                    !File.Exists(AssetPathToAbsolutePath(receiptPath)))
                {
                    throw new InvalidOperationException(
                        "Canonical root/receipt consumer boundary is invalid.");
                }

                string receipt = File.ReadAllText(
                    AssetPathToAbsolutePath(receiptPath),
                    Encoding.UTF8);
                string generatedCapturePath = GeneratedRunPath + "/CAPTURE_STATE.txt";
                string evidenceCapturePath = EvidenceRunPath + "/CAPTURE_STATE.txt";
                string manifestPath =
                    EvidenceRunPath + "/manifest_JOINT_PAIR_TOTAL_GT_CANDIDATE.txt";
                if (!string.Equals(ReadUniqueManifestValue(receipt, "status"),
                        "COMMITTED", StringComparison.Ordinal) ||
                    !string.Equals(ReadUniqueManifestValue(receipt, "runId"),
                        RunId, StringComparison.Ordinal) ||
                    !string.Equals(ReadUniqueManifestValue(receipt, "generatedRoot"),
                        GeneratedRunPath, StringComparison.Ordinal) ||
                    !string.Equals(ReadUniqueManifestValue(receipt, "evidenceRoot"),
                        EvidenceRunPath, StringComparison.Ordinal) ||
                    !string.Equals(ReadUniqueManifestValue(receipt, "receiptPath"),
                        receiptPath, StringComparison.Ordinal) ||
                    !string.Equals(ReadUniqueManifestValue(receipt, "generatedCaptureStatePath"),
                        generatedCapturePath, StringComparison.Ordinal) ||
                    !string.Equals(ReadUniqueManifestValue(receipt, "evidenceCaptureStatePath"),
                        evidenceCapturePath, StringComparison.Ordinal) ||
                    !string.Equals(ReadUniqueManifestValue(receipt, "manifestPath"),
                        manifestPath, StringComparison.Ordinal) ||
                    !string.Equals(ReadUniqueManifestValue(receipt, "sourceScenePath"),
                        ValidationScenePath, StringComparison.Ordinal) ||
                    !string.Equals(ReadUniqueManifestValue(receipt,
                            "correctedRealtimeManifestPath"),
                        CorrectedRealtimeManifestPath, StringComparison.Ordinal) ||
                    !string.Equals(
                        ReadUniqueManifestValue(receipt, "consumerValidation"),
                        "Both canonical roots exist; staging roots and metas are absent; " +
                        "receipt paths, source bindings, and recomputed payload hashes match.",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("COMMITTED receipt path binding is invalid.");
                }

                if (!string.Equals(
                        ReadUniqueManifestValue(receipt, "generatedRootPayloadSha256"),
                        ComputeDirectoryPayloadSha256(
                            GeneratedRunPath,
                            Array.Empty<string>()),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        ReadUniqueManifestValue(
                            receipt,
                            "evidenceRootPayloadSha256ExcludingReceipt"),
                        ComputeDirectoryPayloadSha256(
                            EvidenceRunPath,
                            new[] { CommitReceiptFilename }),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        ReadUniqueManifestValue(receipt, "generatedCaptureStateSha256"),
                        ComputeFileSha256(generatedCapturePath),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        ReadUniqueManifestValue(receipt, "evidenceCaptureStateSha256"),
                        ComputeFileSha256(evidenceCapturePath),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        ReadUniqueManifestValue(receipt, "manifestSha256"),
                        ComputeFileSha256(manifestPath),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        ReadUniqueManifestValue(receipt, "sourceSceneSha256"),
                        ComputeFileSha256(ValidationScenePath),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        ReadUniqueManifestValue(receipt, "correctedRealtimeManifestSha256"),
                        ComputeFileSha256(CorrectedRealtimeManifestPath),
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "COMMITTED receipt payload/source hash validation failed.");
                }
            }

            private static string ComputeDirectoryPayloadSha256(
                string assetRoot,
                IEnumerable<string> excludedRelativePaths)
            {
                string absoluteRoot = AssetPathToAbsolutePath(assetRoot).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                if (!Directory.Exists(absoluteRoot))
                    throw new InvalidOperationException("Payload root is missing: " + assetRoot);
                var excluded = new HashSet<string>(
                    (excludedRelativePaths ?? Array.Empty<string>()).Select(NormalizePath),
                    StringComparer.Ordinal);
                string[] files = Directory.GetFiles(
                    absoluteRoot,
                    "*",
                    SearchOption.AllDirectories);
                var records = new List<string>(files.Length);
                for (int i = 0; i < files.Length; i++)
                {
                    string full = Path.GetFullPath(files[i]);
                    string relative = NormalizePath(
                        full.Substring(absoluteRoot.Length).TrimStart(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar));
                    if (relative.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
                        excluded.Contains(relative))
                    {
                        continue;
                    }
                    byte[] bytes = File.ReadAllBytes(full);
                    records.Add(
                        relative + "|" + bytes.LongLength.ToString(CultureInfo.InvariantCulture) +
                        "|" + ComputeSha256(bytes));
                }
                records.Sort(StringComparer.Ordinal);
                return ComputeSha256(Encoding.UTF8.GetBytes(string.Join("\n", records)));
            }

            private void MarkPostPublishFailure(Exception exception)
            {
                string contents =
                    "status=POST_PUBLISH_VERIFICATION_FAILED\n" +
                    "runId=" + RunId + "\n" +
                    "failure=" + (exception != null ? exception.ToString() : string.Empty) + "\n";
                try
                {
                    WriteRawMarkerWherePresent(
                        GeneratedWorkingPath,
                        PostPublishFailureFilename,
                        contents);
                    WriteRawMarkerWherePresent(
                        EvidenceWorkingPath,
                        PostPublishFailureFilename,
                        contents);
                    WriteRawMarkerWherePresent(
                        GeneratedRunPath,
                        PostPublishFailureFilename,
                        contents);
                    WriteRawMarkerWherePresent(
                        EvidenceRunPath,
                        PostPublishFailureFilename,
                        contents);
                    WriteRawMarkerWherePresent(
                        EvidenceWorkingPath,
                        CommitReceiptFilename,
                        "status=INVALID_POST_PUBLISH_FAILURE\nrunId=" + RunId + "\n");
                    WriteRawMarkerWherePresent(
                        EvidenceRunPath,
                        CommitReceiptFilename,
                        "status=INVALID_POST_PUBLISH_FAILURE\nrunId=" + RunId + "\n");
                }
                catch (Exception)
                {
                    // Rollback remains mandatory even if the filesystem cannot accept a marker.
                }
            }

            public string PreserveFailedStaging(string message)
            {
                if (committed)
                    return string.Empty;
                try
                {
                    bool generatedCanonicalPresent = Directory.Exists(
                        AssetPathToAbsolutePath(GeneratedRunPath));
                    bool evidenceCanonicalPresent = Directory.Exists(
                        AssetPathToAbsolutePath(EvidenceRunPath));
                    string contents =
                        "status=FAILED\nrunId=" + RunId + "\n" +
                        "generatedCanonicalPresent=" + generatedCanonicalPresent + "\n" +
                        "evidenceCanonicalPresent=" + evidenceCanonicalPresent + "\n" +
                        "failure=" + (message ?? string.Empty) + "\n";
                    WriteRawMarkerWherePresent(
                        GeneratedWorkingPath,
                        "CAPTURE_STATE.txt",
                        contents);
                    WriteRawMarkerWherePresent(
                        EvidenceWorkingPath,
                        "CAPTURE_STATE.txt",
                        contents);
                    WriteFailedMarkerWherePresent(GeneratedWorkingPath, contents);
                    WriteFailedMarkerWherePresent(EvidenceWorkingPath, contents);
                    WriteFailedMarkerWherePresent(GeneratedRunPath, contents);
                    WriteFailedMarkerWherePresent(EvidenceRunPath, contents);
                    return string.Empty;
                }
                catch (Exception exception)
                {
                    return exception.ToString();
                }
            }

            public void WriteRestoreFailedMarker(string message)
            {
                if (committed)
                    return;
                try
                {
                    WriteRawMarkerWherePresent(
                        GeneratedWorkingPath,
                        "RESTORE_FAILED_BLOCKED.txt",
                        message);
                    WriteRawMarkerWherePresent(
                        EvidenceWorkingPath,
                        "RESTORE_FAILED_BLOCKED.txt",
                        message);
                    WriteRawMarkerWherePresent(
                        GeneratedRunPath,
                        "RESTORE_FAILED_BLOCKED.txt",
                        message);
                    WriteRawMarkerWherePresent(
                        EvidenceRunPath,
                        "RESTORE_FAILED_BLOCKED.txt",
                        message);
                }
                catch (Exception)
                {
                    // Restoration failure artifacts are intentionally preserved.
                }
            }

            private void AssertOwnership()
            {
                if (committed || string.IsNullOrWhiteSpace(Token) || Token.Length != 32 ||
                    !GeneratedRunPath.EndsWith("_" + Token, StringComparison.Ordinal) ||
                    !EvidenceRunPath.EndsWith("_" + Token, StringComparison.Ordinal) ||
                    !GeneratedWorkingPath.EndsWith("_" + Token, StringComparison.Ordinal) ||
                    !EvidenceWorkingPath.EndsWith("_" + Token, StringComparison.Ordinal) ||
                    !File.Exists(generatedOwnershipAbsolutePath) ||
                    !File.Exists(evidenceOwnershipAbsolutePath) ||
                    !string.Equals(
                        File.ReadAllText(generatedOwnershipAbsolutePath),
                        ownershipContents,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        File.ReadAllText(evidenceOwnershipAbsolutePath),
                        ownershipContents,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Run ownership token/path/manifest verification failed.");
                }
            }

            private string CanonicalizePublishedPath(string assetPath)
            {
                string normalized = NormalizePath(assetPath);
                if (normalized.Equals(GeneratedWorkingPath, StringComparison.Ordinal) ||
                    normalized.StartsWith(GeneratedWorkingPath + "/", StringComparison.Ordinal))
                {
                    return GeneratedRunPath + normalized.Substring(GeneratedWorkingPath.Length);
                }
                if (normalized.Equals(EvidenceWorkingPath, StringComparison.Ordinal) ||
                    normalized.StartsWith(EvidenceWorkingPath + "/", StringComparison.Ordinal))
                {
                    return EvidenceRunPath + normalized.Substring(EvidenceWorkingPath.Length);
                }
                throw new InvalidOperationException("Path is outside owned staging roots: " + assetPath);
            }

            private static void AssertTreePublishSafe(
                string stagingRoot,
                string canonicalRoot,
                string label)
            {
                stagingRoot = NormalizePath(stagingRoot).TrimEnd('/');
                canonicalRoot = NormalizePath(canonicalRoot).TrimEnd('/');
                string stagingAbsolute = AssetPathToAbsolutePath(stagingRoot).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                if (!Directory.Exists(stagingAbsolute))
                {
                    throw new DirectoryNotFoundException(
                        label + " staging root is missing: " + stagingRoot);
                }

                AssertUnityImportSafeAssetPath(stagingRoot, label + " staging root");
                AssertUnityImportSafeAssetPath(canonicalRoot, label + " canonical root");
                string[] directories = Directory.GetDirectories(
                    stagingAbsolute,
                    "*",
                    SearchOption.AllDirectories);
                for (int i = 0; i < directories.Length; i++)
                {
                    string relative = GetTreeRelativePath(stagingAbsolute, directories[i], label);
                    AssertUnityImportSafeAssetPath(
                        stagingRoot + "/" + relative,
                        label + " staging directory");
                    AssertUnityImportSafeAssetPath(
                        canonicalRoot + "/" + relative,
                        label + " canonical directory");
                }

                string[] files = Directory.GetFiles(
                    stagingAbsolute,
                    "*",
                    SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    string relative = GetTreeRelativePath(stagingAbsolute, files[i], label);
                    AssertUnityImportSafeAssetPath(
                        stagingRoot + "/" + relative,
                        label + " staging file");
                    AssertUnityImportSafeAssetPath(
                        canonicalRoot + "/" + relative,
                        label + " canonical file");
                }
            }

            private static string GetTreeRelativePath(
                string rootAbsolute,
                string candidateAbsolute,
                string label)
            {
                string rootPrefix = Path.GetFullPath(rootAbsolute).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string candidate = Path.GetFullPath(candidateAbsolute);
                if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        label + " tree enumeration escaped its staging root: " + candidate);
                }
                string relative = NormalizePath(candidate.Substring(rootPrefix.Length));
                if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith("../", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        label + " produced an invalid relative artifact path: " + relative);
                }
                return relative;
            }

            private void VerifyPublishedEvidence(
                PilotEvidence evidence,
                string generatedCaptureState,
                string evidenceCaptureState,
                string receiptPlaceholder)
            {
                if (!AssetDatabase.IsValidFolder(GeneratedRunPath) ||
                    !AssetDatabase.IsValidFolder(EvidenceRunPath) ||
                    AssetDatabase.IsValidFolder(GeneratedWorkingPath) ||
                    AssetDatabase.IsValidFolder(EvidenceWorkingPath))
                {
                    throw new InvalidOperationException("Canonical/staging publish boundary is invalid.");
                }
                AssertFileHash(
                    EvidenceRunPath + "/manifest_JOINT_PAIR_TOTAL_GT_CANDIDATE.txt",
                    ComputeSha256(Encoding.UTF8.GetBytes(evidence.Manifest ?? string.Empty)));
                AssertFileHash(
                    EvidenceRunPath + "/source_state_snapshot.txt",
                    ComputeSha256(Encoding.UTF8.GetBytes(
                        evidence.SourceSnapshotText ?? string.Empty)));
                AssertFileHash(
                    GeneratedRunPath + "/CAPTURE_STATE.txt",
                    ComputeSha256(Encoding.UTF8.GetBytes(generatedCaptureState)));
                AssertFileHash(
                    EvidenceRunPath + "/CAPTURE_STATE.txt",
                    ComputeSha256(Encoding.UTF8.GetBytes(evidenceCaptureState)));
                AssertFileHash(
                    EvidenceRunPath + "/" + CommitReceiptFilename,
                    ComputeSha256(Encoding.UTF8.GetBytes(receiptPlaceholder)));
                for (int stateIndex = 0; stateIndex < evidence.States.Count; stateIndex++)
                {
                    PilotStateEvidence state = evidence.States[stateIndex];
                    AssertFileHash(
                        CanonicalizePublishedPath(state.CopiedScenePath),
                        state.CopiedSceneRawSha256);
                    AssertDependencyHash(
                        CanonicalizePublishedPath(state.CopiedScenePath),
                        state.CopiedSceneDependencyHash);
                    AssertFileHash(
                        CanonicalizePublishedPath(state.CopiedLightingSettingsPath),
                        state.CopiedLightingSettingsRawSha256);
                    AssertDependencyHash(
                        CanonicalizePublishedPath(state.CopiedLightingSettingsPath),
                        state.CopiedLightingSettingsDependencyHash);
                    AssertFileHash(
                        CanonicalizePublishedPath(state.LightingDataPath),
                        state.LightingDataRawSha256);
                    AssertDependencyHash(
                        CanonicalizePublishedPath(state.LightingDataPath),
                        state.LightingDataDependencyHash);
                    for (int i = 0; i < state.Lightmaps.Count; i++)
                    {
                        AssertFileHash(
                            CanonicalizePublishedPath(state.Lightmaps[i].colorPath),
                            state.Lightmaps[i].colorSha256);
                        AssertFileHash(
                            CanonicalizePublishedPath(state.Lightmaps[i].directionPath),
                            state.Lightmaps[i].directionSha256);
                    }
                    for (int i = 0; i < state.Reflections.Count; i++)
                    {
                        AssertFileHash(
                            CanonicalizePublishedPath(state.Reflections[i].cubemapPath),
                            state.Reflections[i].cubemapSha256);
                    }
                    string stateEvidence = EvidenceRunPath + "/" + state.State.Id;
                    AssertFileHash(
                        stateEvidence + "/probe_sh_and_occlusion.json",
                        ComputeSha256(Encoding.UTF8.GetBytes(state.ProbeJson ?? string.Empty)));
                    AssertFileHash(
                        stateEvidence + "/renderer_lightmap_mapping.json",
                        ComputeSha256(Encoding.UTF8.GetBytes(state.RendererMapJson ?? string.Empty)));
                    for (int i = 0; i < state.Images.Count; i++)
                    {
                        VerifyWrittenBytes(
                            stateEvidence + "/" + state.Images[i].pngFilename,
                            state.Images[i].pngBytes,
                            state.Images[i].pngSha256);
                        VerifyWrittenBytes(
                            stateEvidence + "/" + state.Images[i].exrFilename,
                            state.Images[i].exrBytes,
                            state.Images[i].exrSha256);
                    }
                }
            }

            private static void PublishDirectoryAtomic(string stagingPath, string canonicalPath)
            {
                string stagingAbsolute = AssetPathToAbsolutePath(stagingPath);
                string canonicalAbsolute = AssetPathToAbsolutePath(canonicalPath);
                string stagingMeta = stagingAbsolute + ".meta";
                string canonicalMeta = canonicalAbsolute + ".meta";
                if (!string.Equals(
                        Path.GetDirectoryName(stagingAbsolute),
                        Path.GetDirectoryName(canonicalAbsolute),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Atomic publish requires same-parent staging and canonical folders.");
                }
                if (!Directory.Exists(stagingAbsolute) || Directory.Exists(canonicalAbsolute) ||
                    File.Exists(canonicalMeta))
                {
                    throw new InvalidOperationException(
                        "Atomic publish precondition failed: " + canonicalPath);
                }
                Directory.Move(stagingAbsolute, canonicalAbsolute);
                try
                {
                    if (File.Exists(stagingMeta))
                        File.Move(stagingMeta, canonicalMeta);
                }
                catch
                {
                    Directory.Move(canonicalAbsolute, stagingAbsolute);
                    throw;
                }
            }

            private static void MovePublishedDirectoryBackToStaging(
                string canonicalPath,
                string stagingPath)
            {
                string canonicalAbsolute = AssetPathToAbsolutePath(canonicalPath);
                string stagingAbsolute = AssetPathToAbsolutePath(stagingPath);
                string canonicalMeta = canonicalAbsolute + ".meta";
                string stagingMeta = stagingAbsolute + ".meta";
                if (!Directory.Exists(canonicalAbsolute) || Directory.Exists(stagingAbsolute))
                    return;
                Directory.Move(canonicalAbsolute, stagingAbsolute);
                if (File.Exists(canonicalMeta) && !File.Exists(stagingMeta))
                    File.Move(canonicalMeta, stagingMeta);
            }

            private static void WriteFailedMarkerWherePresent(string root, string contents)
            {
                WriteRawMarkerWherePresent(root, "FAILED.txt", contents);
            }

            private static void WriteRawMarkerWherePresent(
                string root,
                string filename,
                string contents)
            {
                string absolute = AssetPathToAbsolutePath(root);
                if (!Directory.Exists(absolute))
                    return;
                string markerAssetPath = NormalizePath(root).TrimEnd('/') + "/" + filename;
                AssertUnityImportSafeAssetPath(markerAssetPath, "failure marker");
                File.WriteAllText(
                    AssetPathToAbsolutePath(markerAssetPath),
                    contents ?? string.Empty,
                    new UTF8Encoding(false));
            }
        }

        private sealed class EditorSessionSnapshot
        {
            private readonly SceneSetup[] sceneSetup;
            private readonly string activeScenePath;
            private readonly string[] selectionGlobalIds;
            private readonly string activeSelectionGlobalId;
            private readonly Lightmapping.BakeOnSceneLoadMode bakeOnSceneLoad;
            private readonly LightingSettings lightingSettings;
            private readonly LightingDataAsset lightingDataAsset;
            private readonly LightmapData[] lightmaps;
            private readonly LightmapsMode lightmapsMode;
            private readonly RenderTexture activeRenderTexture;
            private readonly bool srgbWrite;
            private readonly string cameraFingerprint;
            private readonly SourceTransientRendererBlocks transientRendererBlocks;
            private readonly RenderSettingsSnapshot renderSettings;
            private readonly SourceReflectionProbeSnapshot reflectionProbes;

            private EditorSessionSnapshot(
                SceneSetup[] sceneSetup,
                string activeScenePath,
                string[] selectionGlobalIds,
                string activeSelectionGlobalId,
                Lightmapping.BakeOnSceneLoadMode bakeOnSceneLoad,
                LightingSettings lightingSettings,
                LightingDataAsset lightingDataAsset,
                LightmapData[] lightmaps,
                LightmapsMode lightmapsMode,
                RenderTexture activeRenderTexture,
                bool srgbWrite,
                string cameraFingerprint,
                SourceTransientRendererBlocks transientRendererBlocks,
                RenderSettingsSnapshot renderSettings,
                SourceReflectionProbeSnapshot reflectionProbes)
            {
                this.sceneSetup = sceneSetup;
                this.activeScenePath = activeScenePath;
                this.selectionGlobalIds = selectionGlobalIds;
                this.activeSelectionGlobalId = activeSelectionGlobalId;
                this.bakeOnSceneLoad = bakeOnSceneLoad;
                this.lightingSettings = lightingSettings;
                this.lightingDataAsset = lightingDataAsset;
                this.lightmaps = lightmaps;
                this.lightmapsMode = lightmapsMode;
                this.activeRenderTexture = activeRenderTexture;
                this.srgbWrite = srgbWrite;
                this.cameraFingerprint = cameraFingerprint;
                this.transientRendererBlocks = transientRendererBlocks;
                this.renderSettings = renderSettings;
                this.reflectionProbes = reflectionProbes;
            }

            public static EditorSessionSnapshot Capture(
                string cameraFingerprint,
                GameObject sourceRoot)
            {
                Object[] selected = Selection.objects ?? Array.Empty<Object>();
                return new EditorSessionSnapshot(
                    (SceneSetup[])EditorSceneManager.GetSceneManagerSetup().Clone(),
                    NormalizePath(SceneManager.GetActiveScene().path),
                    selected.Select(GetGlobalObjectIdString).ToArray(),
                    GetGlobalObjectIdString(Selection.activeObject),
                    Lightmapping.bakeOnSceneLoad,
                    Lightmapping.lightingSettings,
                    Lightmapping.lightingDataAsset,
                    LightmapSettings.lightmaps != null
                        ? (LightmapData[])LightmapSettings.lightmaps.Clone()
                        : null,
                    LightmapSettings.lightmapsMode,
                    RenderTexture.active,
                    GL.sRGBWrite,
                    cameraFingerprint,
                    SourceTransientRendererBlocks.Capture(sourceRoot),
                    RenderSettingsSnapshot.Capture(),
                    SourceReflectionProbeSnapshot.Capture(sourceRoot));
            }

            public void Restore()
            {
                Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;
                try
                {
                    RenderTexture.active = activeRenderTexture;
                    GL.sRGBWrite = srgbWrite;
                    EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
                    Scene active = SceneManager.GetSceneByPath(activeScenePath);
                    if (!active.IsValid() || !active.isLoaded)
                    {
                        throw new InvalidOperationException("Original active scene could not be restored.");
                    }
                    if (SceneManager.GetActiveScene() != active &&
                        !EditorSceneManager.SetActiveScene(active))
                    {
                        throw new InvalidOperationException("Original active scene could not be activated.");
                    }
                    Lightmapping.lightingSettings = lightingSettings;
                    Lightmapping.lightingDataAsset = lightingDataAsset;
                    LightmapSettings.lightmapsMode = lightmapsMode;
                    LightmapSettings.lightmaps = lightmaps;
                    GameObject restoredRoot = FindUniqueRoot(active, ValidationRootName);
                    renderSettings.Restore();
                    reflectionProbes.Restore(restoredRoot);
                    transientRendererBlocks.Restore(restoredRoot);
                    RestoreSelection();
                }
                finally
                {
                    Lightmapping.bakeOnSceneLoad = bakeOnSceneLoad;
                }
            }

            public void AssertRestored()
            {
                if (!string.Equals(
                        NormalizePath(SceneManager.GetActiveScene().path),
                        activeScenePath,
                        StringComparison.Ordinal) ||
                    SceneManager.sceneCount != sceneSetup.Count(setup => setup.isLoaded))
                {
                    throw new InvalidOperationException("Original SceneSetup was not restored.");
                }
                for (int i = 0; i < sceneSetup.Length; i++)
                {
                    if (!sceneSetup[i].isLoaded)
                        continue;
                    Scene scene = SceneManager.GetSceneByPath(sceneSetup[i].path);
                    if (!scene.IsValid() || !scene.isLoaded || scene.isDirty)
                    {
                        throw new InvalidOperationException(
                            "Restored source scene is invalid or dirty: " + sceneSetup[i].path);
                    }
                }
                if (Lightmapping.bakeOnSceneLoad != bakeOnSceneLoad ||
                    Lightmapping.lightingSettings != lightingSettings ||
                    Lightmapping.lightingDataAsset != lightingDataAsset ||
                    LightmapSettings.lightmapsMode != lightmapsMode ||
                    !SameLightmaps(LightmapSettings.lightmaps, lightmaps) ||
                    RenderTexture.active != activeRenderTexture || GL.sRGBWrite != srgbWrite)
                {
                    throw new InvalidOperationException("Global editor lighting/render state was not restored.");
                }
                if (!SelectionMatches())
                    throw new InvalidOperationException("GlobalObjectId selection was not restored.");

                renderSettings.AssertRestored();

                Scene source = SceneManager.GetActiveScene();
                GameObject root = FindUniqueRoot(source, ValidationRootName);
                reflectionProbes.AssertRestored(root);
                Transform camerasRoot = FindUniqueDescendant(
                    root.transform,
                    CamerasRootName,
                    true);
                if (!string.Equals(
                        ComputeCameraFingerprint(ResolveFixedCameras(camerasRoot)),
                        cameraFingerprint,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Fixed camera fingerprint was not restored.");
                }
                transientRendererBlocks.AssertRestored(root);
            }

            private void RestoreSelection()
            {
                Selection.objects = selectionGlobalIds
                    .Select(ResolveGlobalObjectId)
                    .Where(item => item != null)
                    .ToArray();
                Selection.activeObject = ResolveGlobalObjectId(activeSelectionGlobalId);
            }

            private bool SelectionMatches()
            {
                string[] current = (Selection.objects ?? Array.Empty<Object>())
                    .Select(GetGlobalObjectIdString)
                    .ToArray();
                return current.SequenceEqual(selectionGlobalIds, StringComparer.Ordinal) &&
                       string.Equals(
                           GetGlobalObjectIdString(Selection.activeObject),
                           activeSelectionGlobalId,
                           StringComparison.Ordinal);
            }
        }

        private sealed class RenderSettingsSnapshot
        {
            private readonly StaticPropertySnapshot[] properties;

            private RenderSettingsSnapshot(StaticPropertySnapshot[] properties)
            {
                this.properties = properties;
            }

            public static RenderSettingsSnapshot Capture()
            {
                PropertyInfo[] candidates = typeof(RenderSettings)
                    .GetProperties(BindingFlags.Public | BindingFlags.Static)
                    .Where(IsSnapshotProperty)
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .ToArray();
                if (candidates.Length == 0)
                    throw new InvalidOperationException("No writable RenderSettings properties found.");
                var snapshots = new StaticPropertySnapshot[candidates.Length];
                for (int i = 0; i < candidates.Length; i++)
                    snapshots[i] = StaticPropertySnapshot.Capture(candidates[i]);
                return new RenderSettingsSnapshot(snapshots);
            }

            public void Restore()
            {
                for (int i = 0; i < properties.Length; i++)
                    properties[i].RestoreIfNeeded();
            }

            public void AssertRestored()
            {
                PropertyInfo[] current = typeof(RenderSettings)
                    .GetProperties(BindingFlags.Public | BindingFlags.Static)
                    .Where(IsSnapshotProperty)
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .ToArray();
                if (current.Length != properties.Length)
                {
                    throw new InvalidOperationException(
                        "RenderSettings public property surface changed during capture.");
                }
                for (int i = 0; i < properties.Length; i++)
                {
                    if (!string.Equals(
                            current[i].Name,
                            properties[i].Name,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "RenderSettings public property ordering changed.");
                    }
                    properties[i].AssertCurrent();
                }
            }

            private static bool IsSnapshotProperty(PropertyInfo property)
            {
                if (!property.CanRead || property.GetMethod == null ||
                    !property.GetMethod.IsPublic ||
                    property.GetIndexParameters().Length != 0)
                    return false;

                // Unity 6 retains the legacy Cubemap alias even when the current
                // custom reflection is a non-Cubemap Texture. Its getter then throws.
                // customReflectionTexture is the authoritative superset and is still
                // captured/restored/asserted below; only the throwing alias is omitted.
                return !string.Equals(
                    property.Name,
                    "customReflection",
                    StringComparison.Ordinal);
            }

            private sealed class StaticPropertySnapshot
            {
                private readonly PropertyInfo property;
                private readonly object value;
                private readonly ObjectReferenceSnapshot objectValue;

                private StaticPropertySnapshot(
                    PropertyInfo property,
                    object value,
                    ObjectReferenceSnapshot objectValue)
                {
                    this.property = property;
                    this.value = value;
                    this.objectValue = objectValue;
                }

                public string Name => property.Name;

                public static StaticPropertySnapshot Capture(PropertyInfo property)
                {
                    object current = property.GetValue(null);
                    return new StaticPropertySnapshot(
                        property,
                        current is Object ? null : current,
                        typeof(Object).IsAssignableFrom(property.PropertyType)
                            ? ObjectReferenceSnapshot.Capture(current as Object)
                            : null);
                }

                public void RestoreIfNeeded()
                {
                    object expected = ExpectedValue();
                    object current = property.GetValue(null);
                    if (!ValuesEqual(current, expected) && property.CanWrite &&
                        property.SetMethod != null && property.SetMethod.IsPublic)
                    {
                        property.SetValue(null, expected);
                    }
                }

                public void AssertCurrent()
                {
                    if (!ValuesEqual(property.GetValue(null), ExpectedValue()))
                    {
                        throw new InvalidOperationException(
                            "RenderSettings property was not restored: " + property.Name);
                    }
                }

                private object ExpectedValue()
                {
                    return objectValue != null
                        ? objectValue.Resolve(property.PropertyType)
                        : value;
                }
            }
        }

        private sealed class SourceReflectionProbeSnapshot
        {
            private readonly Entry[] entries;

            private SourceReflectionProbeSnapshot(Entry[] entries)
            {
                this.entries = entries;
            }

            public static SourceReflectionProbeSnapshot Capture(GameObject root)
            {
                ReflectionProbe[] probes = root.GetComponentsInChildren<ReflectionProbe>(true);
                Entry[] entries = probes.Select(Entry.Capture)
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .ToArray();
                if (entries.Length != ExpectedSourceReflectionProbeCount)
                {
                    throw new InvalidOperationException(
                        "Source ReflectionProbe count changed. expected=" +
                        ExpectedSourceReflectionProbeCount + " actual=" + entries.Length + ".");
                }
                if (entries.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count() !=
                    entries.Length)
                {
                    throw new InvalidOperationException(
                        "Duplicate source ReflectionProbe restoration key.");
                }
                return new SourceReflectionProbeSnapshot(entries);
            }

            public void Restore(GameObject root)
            {
                Dictionary<string, ReflectionProbe> current = BuildMap(root);
                if (current.Count != entries.Length)
                    throw new InvalidOperationException("Source ReflectionProbe count changed.");
                for (int i = 0; i < entries.Length; i++)
                {
                    if (!current.TryGetValue(entries[i].Key, out ReflectionProbe probe))
                    {
                        throw new InvalidOperationException(
                            "Source ReflectionProbe key is missing: " + entries[i].Key);
                    }
                    entries[i].Restore(probe);
                }
            }

            public void AssertRestored(GameObject root)
            {
                Dictionary<string, ReflectionProbe> current = BuildMap(root);
                if (current.Count != entries.Length)
                    throw new InvalidOperationException("Source ReflectionProbe count was not restored.");
                for (int i = 0; i < entries.Length; i++)
                {
                    if (!current.TryGetValue(entries[i].Key, out ReflectionProbe probe))
                        throw new InvalidOperationException("Source ReflectionProbe key was not restored.");
                    entries[i].AssertRestored(probe);
                }
            }

            private static Dictionary<string, ReflectionProbe> BuildMap(GameObject root)
            {
                var map = new Dictionary<string, ReflectionProbe>(StringComparer.Ordinal);
                ReflectionProbe[] probes = root.GetComponentsInChildren<ReflectionProbe>(true);
                for (int i = 0; i < probes.Length; i++)
                {
                    string key = Entry.BuildKey(probes[i]);
                    if (!map.TryAdd(key, probes[i]))
                        throw new InvalidOperationException("Duplicate ReflectionProbe key: " + key);
                }
                return map;
            }

            private sealed class Entry
            {
                private readonly string serializedJson;
                private readonly bool gameObjectActiveSelf;
                private readonly bool enabled;
                private readonly RuntimePropertySnapshot[] runtimeProperties;

                private Entry(
                    string key,
                    string serializedJson,
                    bool gameObjectActiveSelf,
                    bool enabled,
                    RuntimePropertySnapshot[] runtimeProperties)
                {
                    Key = key;
                    this.serializedJson = serializedJson;
                    this.gameObjectActiveSelf = gameObjectActiveSelf;
                    this.enabled = enabled;
                    this.runtimeProperties = runtimeProperties;
                }

                public string Key { get; }

                public static Entry Capture(ReflectionProbe probe)
                {
                    RuntimePropertySnapshot[] runtime = typeof(ReflectionProbe)
                        .GetProperties(BindingFlags.Public | BindingFlags.Instance |
                                       BindingFlags.DeclaredOnly)
                        .Where(property => property.CanRead &&
                                           property.GetIndexParameters().Length == 0)
                        .OrderBy(property => property.Name, StringComparer.Ordinal)
                        .Select(property => RuntimePropertySnapshot.Capture(property, probe))
                        .ToArray();
                    return new Entry(
                        BuildKey(probe),
                        EditorJsonUtility.ToJson(probe, false),
                        probe.gameObject.activeSelf,
                        probe.enabled,
                        runtime);
                }

                public static string BuildKey(ReflectionProbe probe)
                {
                    return GetHierarchyPath(probe.transform) + "|ReflectionProbe|" +
                           GetComponentOrdinal(probe);
                }

                public void Restore(ReflectionProbe probe)
                {
                    if (probe.gameObject.activeSelf != gameObjectActiveSelf)
                        probe.gameObject.SetActive(gameObjectActiveSelf);
                    if (!string.Equals(
                            EditorJsonUtility.ToJson(probe, false),
                            serializedJson,
                            StringComparison.Ordinal))
                    {
                        EditorJsonUtility.FromJsonOverwrite(serializedJson, probe);
                    }
                    if (probe.enabled != enabled)
                        probe.enabled = enabled;
                    for (int i = 0; i < runtimeProperties.Length; i++)
                        runtimeProperties[i].RestoreIfWritable(probe);
                }

                public void AssertRestored(ReflectionProbe probe)
                {
                    if (probe.gameObject.activeSelf != gameObjectActiveSelf ||
                        probe.enabled != enabled ||
                        !string.Equals(
                            EditorJsonUtility.ToJson(probe, false),
                            serializedJson,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Source ReflectionProbe serialized state was not restored: " + Key);
                    }
                    for (int i = 0; i < runtimeProperties.Length; i++)
                        runtimeProperties[i].AssertCurrent(probe, Key);
                }
            }

            private sealed class RuntimePropertySnapshot
            {
                private readonly PropertyInfo property;
                private readonly object value;
                private readonly ObjectReferenceSnapshot objectValue;

                private RuntimePropertySnapshot(
                    PropertyInfo property,
                    object value,
                    ObjectReferenceSnapshot objectValue)
                {
                    this.property = property;
                    this.value = value;
                    this.objectValue = objectValue;
                }

                public static RuntimePropertySnapshot Capture(
                    PropertyInfo property,
                    ReflectionProbe probe)
                {
                    object current = property.GetValue(probe);
                    return new RuntimePropertySnapshot(
                        property,
                        current is Object ? null : current,
                        typeof(Object).IsAssignableFrom(property.PropertyType)
                            ? ObjectReferenceSnapshot.Capture(current as Object)
                            : null);
                }

                public void RestoreIfWritable(ReflectionProbe probe)
                {
                    if (!property.CanWrite || property.SetMethod == null ||
                        !property.SetMethod.IsPublic)
                    {
                        return;
                    }
                    object expected = ExpectedValue();
                    if (!ValuesEqual(property.GetValue(probe), expected))
                        property.SetValue(probe, expected);
                }

                public void AssertCurrent(ReflectionProbe probe, string key)
                {
                    if (!ValuesEqual(property.GetValue(probe), ExpectedValue()))
                    {
                        throw new InvalidOperationException(
                            "Source ReflectionProbe runtime property was not restored: " +
                            key + " / " + property.Name);
                    }
                }

                private object ExpectedValue()
                {
                    return objectValue != null
                        ? objectValue.Resolve(property.PropertyType)
                        : value;
                }
            }
        }

        private sealed class ObjectReferenceSnapshot
        {
            private readonly bool isNull;
            private readonly string globalObjectId;
            private readonly string assetPath;

            private ObjectReferenceSnapshot(bool isNull, string globalObjectId, string assetPath)
            {
                this.isNull = isNull;
                this.globalObjectId = globalObjectId;
                this.assetPath = assetPath;
            }

            public static ObjectReferenceSnapshot Capture(Object value)
            {
                return new ObjectReferenceSnapshot(
                    value == null,
                    GetGlobalObjectIdString(value),
                    value != null ? NormalizePath(AssetDatabase.GetAssetPath(value)) : string.Empty);
            }

            public Object Resolve(Type expectedType)
            {
                if (isNull)
                    return null;
                Object resolved = ResolveGlobalObjectId(globalObjectId);
                if (resolved == null && !string.IsNullOrWhiteSpace(assetPath))
                    resolved = AssetDatabase.LoadAssetAtPath(assetPath, expectedType);
                if (resolved == null || !expectedType.IsInstanceOfType(resolved))
                {
                    throw new InvalidOperationException(
                        "Could not resolve snapshotted Unity object: " + globalObjectId + ".");
                }
                return resolved;
            }
        }

        private static bool ValuesEqual(object left, object right)
        {
            if (left is Object || right is Object)
                return left as Object == right as Object;
            return Equals(left, right);
        }

        private sealed class SourceTransientRendererBlocks
        {
            private readonly Entry[] entries;

            private SourceTransientRendererBlocks(Entry[] entries)
            {
                this.entries = entries;
            }

            public static SourceTransientRendererBlocks Capture(GameObject root)
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                var entries = new Entry[renderers.Length];
                for (int i = 0; i < renderers.Length; i++)
                    entries[i] = Entry.Capture(renderers[i]);
                return new SourceTransientRendererBlocks(entries);
            }

            public void Restore(GameObject root)
            {
                Dictionary<string, Renderer> current = BuildMap(root);
                if (current.Count != entries.Length)
                    throw new InvalidOperationException("Restored transient renderer map count changed.");
                for (int i = 0; i < entries.Length; i++)
                {
                    if (!current.TryGetValue(entries[i].Key, out Renderer renderer))
                    {
                        throw new InvalidOperationException(
                            "Restored transient renderer key is missing: " + entries[i].Key);
                    }
                    entries[i].Restore(renderer);
                }
            }

            public void AssertRestored(GameObject root)
            {
                Dictionary<string, Renderer> current = BuildMap(root);
                if (current.Count != entries.Length)
                    throw new InvalidOperationException("Transient renderer block count was not restored.");
                for (int i = 0; i < entries.Length; i++)
                {
                    if (!current.TryGetValue(entries[i].Key, out Renderer renderer))
                        throw new InvalidOperationException("Transient renderer key was not restored.");
                    if (renderer.HasPropertyBlock() != entries[i].HadPropertyBlock)
                    {
                        throw new InvalidOperationException(
                            "Transient MaterialPropertyBlock presence was not restored: " + entries[i].Key);
                    }
                }
            }

            private static Dictionary<string, Renderer> BuildMap(GameObject root)
            {
                var map = new Dictionary<string, Renderer>(StringComparer.Ordinal);
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    string key = BuildKey(renderers[i]);
                    if (!map.TryAdd(key, renderers[i]))
                        throw new InvalidOperationException("Duplicate renderer restoration key: " + key);
                }
                return map;
            }

            private static string BuildKey(Renderer renderer)
            {
                return GetHierarchyPath(renderer.transform) + "|" +
                       renderer.GetType().FullName + "|" + GetComponentOrdinal(renderer);
            }

            private sealed class Entry
            {
                public string Key;
                public bool HadPropertyBlock;
                public MaterialPropertyBlock Global;
                public MaterialPropertyBlock[] PerMaterial;

                public static Entry Capture(Renderer renderer)
                {
                    var global = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(global);
                    int materialCount = renderer.sharedMaterials != null
                        ? renderer.sharedMaterials.Length
                        : 0;
                    var perMaterial = new MaterialPropertyBlock[materialCount];
                    for (int i = 0; i < materialCount; i++)
                    {
                        perMaterial[i] = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(perMaterial[i], i);
                    }
                    return new Entry
                    {
                        Key = BuildKey(renderer),
                        HadPropertyBlock = renderer.HasPropertyBlock(),
                        Global = global,
                        PerMaterial = perMaterial
                    };
                }

                public void Restore(Renderer renderer)
                {
                    int materialCount = renderer.sharedMaterials != null
                        ? renderer.sharedMaterials.Length
                        : 0;
                    if (materialCount != PerMaterial.Length)
                    {
                        throw new InvalidOperationException(
                            "Restored renderer material count changed: " + Key);
                    }
                    renderer.SetPropertyBlock(Global.isEmpty ? null : Global);
                    for (int i = 0; i < PerMaterial.Length; i++)
                    {
                        renderer.SetPropertyBlock(
                            PerMaterial[i].isEmpty ? null : PerMaterial[i],
                            i);
                    }
                }
            }
        }

        private static string BuildManifest(PilotEvidence evidence)
        {
            var builder = new StringBuilder(32768);
            builder.AppendLine("DungeonPortalJointPairTotalPilot/v4");
            builder.AppendLine("status=" + Status);
            builder.AppendLine("runId=" + evidence.RunId);
            builder.AppendLine("visualVerdict=UNREVIEWED");
            builder.AppendLine("equivalence=false");
            builder.AppendLine("productionWrites=false");
            builder.AppendLine("sourceScene=" + ValidationScenePath);
            builder.AppendLine("sourceSceneExpectedRawSha256=" +
                               ExpectedValidationSceneRawSha256);
            builder.AppendLine("sourceSceneActualRawSha256=" +
                               ExpectedValidationSceneRawSha256);
            builder.AppendLine("sourceLightingSettings=" + LightingSettingsPath);
            builder.AppendLine(
                "lightingSettingsEquivalence=SHA256 of EditorJson with exactly one root " +
                "LightingSettings object wrapper and exactly one validated direct-child string " +
                "m_Name replaced by a fixed token; every other serialized byte must match the source");
            builder.AppendLine("correctedRealtimeDirectManifest=" +
                               CorrectedRealtimeManifestPath);
            builder.AppendLine("correctedRealtimeDirectManifestExpectedRawSha256=" +
                               ExpectedCorrectedRealtimeManifestRawSha256);
            builder.AppendLine("correctedRealtimeDirectManifestActualRawSha256=" +
                               evidence.CorrectedRealtimeReference.ManifestRawSha256);
            builder.AppendLine("correctedRealtimeDirectManifestStatus=" +
                               evidence.CorrectedRealtimeReference.StatusValue);
            builder.AppendLine("correctedRealtimeDirectManifestValidationScene=" +
                               evidence.CorrectedRealtimeReference.ValidationScene);
            builder.AppendLine("correctedRealtimeDirectManifestSceneBindingVerified=true");
            builder.AppendLine("correctedRealtimeD100RecordCount=" +
                               evidence.CorrectedRealtimeReference.Records.Length);
            builder.AppendLine("correctedRealtimeD100BytesAndHashesVerified=true");
            builder.AppendLine("generatedRun=" + evidence.GeneratedRunPath);
            builder.AppendLine("evidenceRun=" + evidence.EvidenceRunPath);
            builder.AppendLine("copiedSceneFilename=" + CopiedSceneFilename);
            builder.AppendLine(
                "copiedLightingSettingsFilename=" + CopiedLightingSettingsFilename);
            builder.AppendLine(
                "unityGeneratedSceneArtifactFolder=" +
                GeneratedSceneArtifactFolderName);
            builder.AppendLine(
                "shortOwnedFilenameIdentityBinding=full run id remains in ownership/canonical " +
                "roots and full state id remains in manifest/state parent folders");
            builder.AppendLine("commitReceipt=" + evidence.EvidenceRunPath + "/COMMITTED.txt");
            builder.AppendLine(
                "canonicalSuccessRule=Both canonical roots plus a status=COMMITTED receipt with matching root payload hashes are required");
            builder.AppendLine(
                "postPublishFailureMarker=POST_PUBLISH_VERIFICATION_FAILED.txt");
            builder.AppendLine("sourceStateFingerprint=" + evidence.SourceStateFingerprint);
            builder.AppendLine("stateCount=" + evidence.States.Count);
            builder.AppendLine("baselineState=" + BaselineStateId);
            builder.AppendLine("stateOrder=" +
                               string.Join(",", PilotStates.Select(state => state.Id)));
            builder.AppendLine("doorPercent=100");
            builder.AppendLine("captureWidth=" + CaptureWidth);
            builder.AppendLine("captureHeight=" + CaptureHeight);
            builder.AppendLine(
                "captureFilenameConvention=C0=Start_to_Admin,C1=Admin_to_Start,.png=presentation,_L.exr=linear_no_post");
            builder.AppendLine(
                "captureIdentityBinding=full state id and full camera name are recorded per image; short filenames are deterministic path-safe storage keys only");
            builder.AppendLine("legacyMaxPathExclusive=" + LegacyMaxPathExclusive);
            builder.AppendLine(
                "unityImportSafeAbsolutePathMax=" + UnityImportSafeAbsolutePathMax);
            builder.AppendLine(
                "fixedCamera.Start.scene=" + ValidationScenePath);
            builder.AppendLine("fixedCamera.Start.name=" + StartCameraName);
            builder.AppendLine("fixedCamera.Start.position=" +
                               FormatVector3(ExpectedStartCameraPosition));
            builder.AppendLine("fixedCamera.Start.rotation=" +
                               FormatQuaternion(ExpectedStartCameraRotation));
            builder.AppendLine("fixedCamera.Administrative.scene=" + ValidationScenePath);
            builder.AppendLine("fixedCamera.Administrative.name=" + AdministrativeCameraName);
            builder.AppendLine("fixedCamera.Administrative.position=" +
                               FormatVector3(ExpectedAdministrativeCameraPosition));
            builder.AppendLine("fixedCamera.Administrative.rotation=" +
                               FormatQuaternion(ExpectedAdministrativeCameraRotation));
            builder.AppendLine(
                "fixedCamera.shared=disabled=true,targetTexture=null,orthographic=false," +
                "usePhysicalProperties=false,FOV=90,near=0.01,far=1000,cullingMask=262135," +
                "projection=standardPerspective,allowHDR=true,allowMSAA=true," +
                "presentationRenderTargetMSAA=1,presentationPostProcessing=true," +
                "hdrPostProcessing=false");
            builder.AppendLine("lightmapMode=CombinedDirectional");
            builder.AppendLine("shadowmaskExpected=false");
            builder.AppendLine(
                "rendererStableKey=room role plus room-relative sibling-index path plus renderer " +
                "type plus component ordinal");
            builder.AppendLine("rendererHumanRelativePathPreserved=true");
            builder.AppendLine("rendererPreBakeUniqueStableKeyCount=" + ExpectedRendererCount);
            builder.AppendLine(
                "rendererStructuralStableKeyAndMeshUv2BakedLightmapPairingVerified=true");
            builder.AppendLine(
                "uv2ContentHashAlgorithm=SHA256 over persistent mesh vertexCount, UV2 presence/layout, and per-vertex raw UV2 element bytes; DungeonPortalPersistentMeshUv2Raw/v1");
            builder.AppendLine(
                "rendererBakedLightmapIndexAndScaleOffsetPairingVerified=true");
            builder.AppendLine("rendererRealtimeLightmapIndexPairingVerified=true");
            builder.AppendLine(
                "rendererRealtimeLightmapScaleOffset=finite state-dependent diagnostic only; " +
                "differences do not reject pairing");
            builder.AppendLine(
                "rendererAtlasArithmeticPairingScope=baked lightmap index and baked scale-offset only");
            builder.AppendLine("lightmapAtlasIndexPairingVerified=true");
            builder.AppendLine("renderRequest=URP RenderPipeline.StandardRequest");
            builder.AppendLine("renderFailureGate=RenderGraph,ReflectionProbeManager,ForwardLights,ZBinning");
            builder.AppendLine("zeroOrNonFiniteHdrRejected=true");
            builder.AppendLine("allIdenticalOutputRejected=true");
            builder.AppendLine("probeStencilCount=" + ExpectedTotalProbeCount);
            builder.AppendLine("reflectionProbeCountPerState=" + ExpectedTotalP100ReflectionCount);
            builder.AppendLine("deltaCount=" + (PilotStates.Length - 1));
            for (int deltaIndex = 1; deltaIndex < PilotStates.Length; deltaIndex++)
            {
                string candidatePower = GetRealtimePowerId(PilotStates[deltaIndex]);
                builder.AppendLine(
                    "delta." + (deltaIndex - 1) + ".jointTotalDefinition=" +
                    PilotStates[deltaIndex].Id + "-" + BaselineStateId);
                builder.AppendLine(
                    "delta." + (deltaIndex - 1) + ".realtimeDirectDefinition=" +
                    "REALTIME_DIRECT(" + candidatePower + "_D100)-" +
                    "REALTIME_DIRECT(P000_P000_D100)");
            }
            builder.AppendLine(
                "nonRealtimeDirectResidualDefinition=jointTotalDelta-realtimeDirectDelta_for_the_same_fixed_camera_linear_HDR_pixels");
            builder.AppendLine(
                "nonRealtimeDirectResidualIncludes=indirect diffuse, reflection, probe, and any contribution absent from REALTIME_DIRECT");
            builder.AppendLine(
                "nonRealtimeDirectResidualComputed=false; corrected REALTIME_DIRECT is hash-bound but is not silently subtracted here");
            builder.AppendLine("atlasSpaceSubtraction=false");
            builder.AppendLine("baselineP0ResidualReflection=true");
            builder.AppendLine(
                "limitation.1=O(N^2) pair-specific oracle only; this is not the O(N) runtime representation");
            builder.AppendLine(
                "limitation.2=full-open D100 only; no intermediate door pose curve is validated");
            builder.AppendLine(
                "limitation.3=all four P0/P100 power combinations are covered only for this one Start/Admin pair");
            builder.AppendLine(
                "limitation.4=shadowmask is absent because the owned copied settings preserve m_UsingShadowmask=0");
            builder.AppendLine(
                "limitation.5=transparent door glass transmission is deferred");
            builder.AppendLine(
                "limitation.6=images and data remain candidate evidence until fixed-camera visual review");

            for (int i = 0; i < evidence.CorrectedRealtimeReference.Records.Length; i++)
            {
                CorrectedRealtimeD100Record record =
                    evidence.CorrectedRealtimeReference.Records[i];
                string prefix = "correctedRealtimeD100." + i + ".";
                builder.AppendLine(prefix + "manifestCaptureIndex=" + record.Index);
                builder.AppendLine(prefix + "power=" + record.Power);
                builder.AppendLine(prefix + "startPower100=" + record.StartPower100);
                builder.AppendLine(prefix + "administrativePower100=" +
                                   record.AdministrativePower100);
                builder.AppendLine(prefix + "doorPercent=" + record.DoorPercent);
                builder.AppendLine(prefix + "camera=" + record.CameraName);
                builder.AppendLine(prefix + "presentationPng=" + record.PresentationPngPath);
                builder.AppendLine(prefix + "presentationPngSha256=" +
                                   record.PresentationPngSha256);
                builder.AppendLine(prefix + "presentationPngBytes=" +
                                   record.PresentationPngBytes);
                builder.AppendLine(prefix + "linearHdrExr=" + record.LinearHdrExrPath);
                builder.AppendLine(prefix + "linearHdrExrSha256=" +
                                   record.LinearHdrExrSha256);
                builder.AppendLine(prefix + "linearHdrExrBytes=" + record.LinearHdrExrBytes);
                builder.AppendLine(prefix + "cameraPosition=" + FormatVector3(record.Position));
                builder.AppendLine(prefix + "cameraRotation=" +
                                   FormatQuaternion(record.Rotation));
                builder.AppendLine(prefix + "fieldOfView=" + FormatFloat(record.FieldOfView));
                builder.AppendLine(prefix + "nearClip=" + FormatFloat(record.NearClip));
                builder.AppendLine(prefix + "farClip=" + FormatFloat(record.FarClip));
                builder.AppendLine(prefix + "cullingMask=" + record.CullingMask);
                builder.AppendLine(prefix + "allowHDR=" + record.AllowHdr);
                builder.AppendLine(prefix + "allowMSAA=" + record.AllowMsaa);
                builder.AppendLine(prefix + "presentationRenderTargetMSAA=" +
                                   record.PresentationRenderTargetMsaa);
                builder.AppendLine(prefix + "presentationPostProcessing=" +
                                   record.PresentationPostProcessing);
                builder.AppendLine(prefix + "hdrPostProcessing=" +
                                   record.HdrPostProcessing);
            }

            builder.AppendLine("sourceAssetCount=" + evidence.SourceAssets.Length);
            for (int i = 0; i < evidence.SourceAssets.Length; i++)
            {
                SourceAssetIdentity asset = evidence.SourceAssets[i];
                builder.AppendLine("sourceAsset." + i + ".path=" + asset.assetPath);
                builder.AppendLine("sourceAsset." + i + ".guid=" + asset.guid);
                builder.AppendLine("sourceAsset." + i + ".rawSha256=" + asset.rawSha256);
                builder.AppendLine(
                    "sourceAsset." + i + ".dependencyHash=" + asset.dependencyHash);
            }

            for (int stateIndex = 0; stateIndex < evidence.States.Count; stateIndex++)
            {
                PilotStateEvidence state = evidence.States[stateIndex];
                string prefix = "state." + stateIndex + ".";
                builder.AppendLine(prefix + "id=" + state.State.Id);
                builder.AppendLine(prefix + "isBaseline=" + state.State.IsBaseline);
                builder.AppendLine(prefix + "startPower100=" + state.State.StartPower100);
                builder.AppendLine(
                    prefix + "administrativePower100=" + state.State.AdministrativePower100);
                builder.AppendLine(prefix + "copiedScene=" +
                                   CanonicalizeEvidencePath(evidence, state.CopiedScenePath));
                builder.AppendLine(prefix + "sourceSceneGuid=" + state.SourceSceneGuid);
                builder.AppendLine(prefix + "copiedSceneGuid=" + state.CopiedSceneGuid);
                builder.AppendLine(prefix + "copiedSceneRawSha256=" +
                                   state.CopiedSceneRawSha256);
                builder.AppendLine(prefix + "copiedSceneDependencyHash=" +
                                   state.CopiedSceneDependencyHash);
                builder.AppendLine(
                    prefix + "copiedLightingSettings=" +
                    CanonicalizeEvidencePath(evidence, state.CopiedLightingSettingsPath));
                builder.AppendLine(prefix + "sourceLightingSettingsGuid=" +
                                   state.SourceLightingSettingsGuid);
                builder.AppendLine(prefix + "copiedLightingSettingsGuid=" +
                                   state.CopiedLightingSettingsGuid);
                builder.AppendLine(prefix + "copiedLightingSettingsRawSha256=" +
                                   state.CopiedLightingSettingsRawSha256);
                builder.AppendLine(prefix + "copiedLightingSettingsDependencyHash=" +
                                   state.CopiedLightingSettingsDependencyHash);
                builder.AppendLine(
                    prefix + "lightingSettingsCanonicalSerializedFingerprint=" +
                    state.LightingSettingsCanonicalSerializedFingerprint);
                builder.AppendLine(prefix + "copyStateFingerprint=" + state.CopyStateFingerprint);
                builder.AppendLine(prefix + "lightingData=" +
                                   CanonicalizeEvidencePath(evidence, state.LightingDataPath));
                builder.AppendLine(prefix + "lightingDataRawSha256=" +
                                   state.LightingDataRawSha256);
                builder.AppendLine(prefix + "lightingDataDependencyHash=" +
                                   state.LightingDataDependencyHash);
                builder.AppendLine(prefix + "bakeStartedUtc=" +
                                   state.BakeStartedUtc.ToString("O", CultureInfo.InvariantCulture));
                builder.AppendLine(prefix + "bakeElapsedSeconds=" +
                                   FormatDouble(state.BakeElapsed.TotalSeconds));
                builder.AppendLine(prefix + "startLights=" + state.StartPower.LightCount);
                builder.AppendLine(prefix + "startIgnoredLights=" + state.StartPower.IgnoredLightCount);
                builder.AppendLine(prefix + "startEnabledAfter=" + state.StartPower.EnabledAfter);
                builder.AppendLine(prefix + "administrativeLights=" +
                                   state.AdministrativePower.LightCount);
                builder.AppendLine(prefix + "administrativeIgnoredLights=" +
                                   state.AdministrativePower.IgnoredLightCount);
                builder.AppendLine(prefix + "administrativeEnabledAfter=" +
                                   state.AdministrativePower.EnabledAfter);
                builder.AppendLine(prefix + "probeSamples=" + state.ProbeSamples.samples.Length);
                builder.AppendLine(prefix + "rendererMapCount=" + state.RendererMap.count);
                builder.AppendLine(
                    prefix + "realtimeLightmapScaleOffsetDifferenceFromBaselineCount=" +
                    state.RealtimeLightmapScaleOffsetDifferenceFromBaselineCount);
                builder.AppendLine(prefix + "lightmapCount=" + state.Lightmaps.Count);
                for (int i = 0; i < state.Lightmaps.Count; i++)
                {
                    LightmapArtifact map = state.Lightmaps[i];
                    builder.AppendLine(prefix + "lightmap." + i + ".color=" +
                                       CanonicalizeEvidencePath(evidence, map.colorPath));
                    builder.AppendLine(prefix + "lightmap." + i + ".colorSha256=" +
                                       map.colorSha256);
                    builder.AppendLine(prefix + "lightmap." + i + ".direction=" +
                                       CanonicalizeEvidencePath(evidence, map.directionPath));
                    builder.AppendLine(prefix + "lightmap." + i + ".directionSha256=" +
                                       map.directionSha256);
                    builder.AppendLine(prefix + "lightmap." + i + ".shadowmask=null");
                }
                builder.AppendLine(prefix + "reflectionCount=" + state.Reflections.Count);
                for (int i = 0; i < state.Reflections.Count; i++)
                {
                    ReflectionArtifact reflection = state.Reflections[i];
                    builder.AppendLine(prefix + "reflection." + i + ".role=" + reflection.role);
                    builder.AppendLine(prefix + "reflection." + i + ".probe=" +
                                       reflection.probeName);
                    builder.AppendLine(prefix + "reflection." + i + ".cubemap=" +
                                       CanonicalizeEvidencePath(evidence, reflection.cubemapPath));
                    builder.AppendLine(prefix + "reflection." + i + ".sha256=" +
                                       reflection.cubemapSha256);
                }
                builder.AppendLine(prefix + "imageCount=" + state.Images.Count);
                for (int i = 0; i < state.Images.Count; i++)
                {
                    BufferedImage image = state.Images[i];
                    builder.AppendLine(prefix + "image." + i + ".camera=" + image.cameraName);
                    builder.AppendLine(prefix + "image." + i + ".identity=" +
                                       state.State.Id + "|" + image.cameraName);
                    builder.AppendLine(prefix + "image." + i + ".png=" + image.pngFilename);
                    builder.AppendLine(prefix + "image." + i + ".pngSha256=" +
                                       image.pngSha256);
                    builder.AppendLine(prefix + "image." + i + ".linearExr=" +
                                       image.exrFilename);
                    builder.AppendLine(prefix + "image." + i + ".linearExrSha256=" +
                                       image.exrSha256);
                    builder.AppendLine(prefix + "image." + i + ".cameraPosition=" +
                                       FormatVector3(image.position));
                    builder.AppendLine(prefix + "image." + i + ".cameraRotation=" +
                                       FormatQuaternion(image.rotation));
                    builder.AppendLine(prefix + "image." + i + ".fieldOfView=" +
                                       FormatFloat(image.fieldOfView));
                    builder.AppendLine(prefix + "image." + i + ".nearClip=" +
                                       FormatFloat(image.nearClip));
                    builder.AppendLine(prefix + "image." + i + ".farClip=" +
                                       FormatFloat(image.farClip));
                    builder.AppendLine(prefix + "image." + i + ".cullingMask=" +
                                       image.cullingMask);
                    builder.AppendLine(prefix + "image." + i + ".allowHDR=" +
                                       image.allowHdr);
                    builder.AppendLine(prefix + "image." + i + ".allowMSAA=" +
                                       image.allowMsaa);
                    builder.AppendLine(prefix + "image." + i +
                                       ".presentationRenderTargetMSAA=" +
                                       image.presentationRenderTargetMsaa);
                    builder.AppendLine(prefix + "image." + i + ".presentationPostProcessing=" +
                                       image.presentationPostProcessing);
                    builder.AppendLine(prefix + "image." + i + ".hdrPostProcessing=" +
                                       image.hdrPostProcessing);
                    builder.AppendLine(prefix + "image." + i + ".hdrMeanLuminance=" +
                                       FormatDouble(image.hdrMeanLuminance));
                    builder.AppendLine(prefix + "image." + i + ".hdrMaxLuminance=" +
                                       FormatFloat(image.hdrMaxLuminance));
                }
            }

            builder.AppendLine("restoration.sceneSetup=true");
            builder.AppendLine("restoration.selectionGlobalObjectIds=true");
            builder.AppendLine("restoration.lightingState=true");
            builder.AppendLine("restoration.renderState=true");
            builder.AppendLine("restoration.sourceRenderSettingsAllPublicReadableExceptLegacyCustomReflectionCubemapAlias=true");
            builder.AppendLine("restoration.sourceRenderSettingsCustomReflectionTextureCaptured=true");
            builder.AppendLine("restoration.legacyCustomReflectionAliasExcludedBecauseUnity6GetterThrowsForNonCubemapTexture=true");
            builder.AppendLine("restoration.sourceReflectionProbeSerializedAndRuntimeProperties=true");
            builder.AppendLine("restoration.fixedCameras=true");
            builder.AppendLine("restoration.sourceAssets=true");
            return builder.ToString();
        }

        private static void ValidatePilotEvidence(PilotEvidence evidence)
        {
            if (evidence == null || evidence.States == null ||
                evidence.States.Count != PilotStates.Length ||
                evidence.CorrectedRealtimeReference == null ||
                evidence.CorrectedRealtimeReference.Records.Length !=
                PilotStates.Length * ExpectedCameraCount)
            {
                throw new InvalidOperationException("Four-state pilot evidence is incomplete.");
            }
            PilotStateEvidence baseline = evidence.States[0];
            for (int stateIndex = 0; stateIndex < PilotStates.Length; stateIndex++)
            {
                PilotState expected = PilotStates[stateIndex];
                PilotStateEvidence current = evidence.States[stateIndex];
                if (current == null || current.State == null ||
                    !string.Equals(current.State.Id, expected.Id, StringComparison.Ordinal) ||
                    current.State.StartPower100 != expected.StartPower100 ||
                    current.State.AdministrativePower100 != expected.AdministrativePower100 ||
                    current.State.IsBaseline != expected.IsBaseline ||
                    current.State.IsBaseline != (stateIndex == 0))
                {
                    throw new InvalidOperationException(
                        "Four-state power ordering/flags changed at index " + stateIndex + ".");
                }
                if (current.Images == null || current.Images.Count != ExpectedCameraCount ||
                    current.Lightmaps == null || current.Lightmaps.Count == 0 ||
                    current.Reflections == null ||
                    current.Reflections.Count != ExpectedTotalP100ReflectionCount ||
                    current.ProbeSamples == null || current.ProbeSamples.samples == null ||
                    current.ProbeSamples.samples.Length != ExpectedTotalProbeCount ||
                    current.RendererMap == null || current.RendererMap.records == null ||
                    current.RendererMap.count != ExpectedRendererCount ||
                    current.RendererMap.records.Length != ExpectedRendererCount ||
                    current.RendererMap.records.Any(record =>
                        record.meshPersistent
                            ? string.IsNullOrWhiteSpace(record.uv2ContentSha256)
                            : !string.IsNullOrEmpty(record.uv2ContentSha256)))
                {
                    throw new InvalidOperationException(
                        "Four-state artifact counts are incomplete for " + expected.Id + ".");
                }

                string expectedStartRole = expected.StartPower100
                    ? "Start_P100"
                    : "Start_P0_RESIDUAL";
                string expectedAdministrativeRole = expected.AdministrativePower100
                    ? "Administrative_P100"
                    : "Administrative_P0_RESIDUAL";
                if (current.Reflections.Count(item =>
                        string.Equals(item.role, expectedStartRole, StringComparison.Ordinal)) !=
                    ExpectedStartP100ReflectionCount ||
                    current.Reflections.Count(item =>
                        string.Equals(item.role, expectedAdministrativeRole,
                            StringComparison.Ordinal)) != ExpectedAdministrativeP100ReflectionCount ||
                    current.Reflections.Any(item =>
                        !string.Equals(item.role, expectedStartRole, StringComparison.Ordinal) &&
                        !string.Equals(item.role, expectedAdministrativeRole,
                            StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        "Reflection roles do not match state flags for " + expected.Id + ".");
                }
                if (!string.Equals(
                        baseline.LightingSettingsCanonicalSerializedFingerprint,
                        current.LightingSettingsCanonicalSerializedFingerprint,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "State lighting-settings fingerprint differs for " + expected.Id + ".");
                }

                ValidateCameraAndRealtimeBindings(
                    baseline,
                    current,
                    evidence.CorrectedRealtimeReference);
                current.RealtimeLightmapScaleOffsetDifferenceFromBaselineCount = 0;
                if (stateIndex > 0)
                {
                    current.RealtimeLightmapScaleOffsetDifferenceFromBaselineCount =
                        ValidateRendererAndAtlasPairing(baseline, current);
                    ValidateCaptureDiversityAndSignal(baseline, current);
                }
            }
        }

        private static void ValidateCameraAndRealtimeBindings(
            PilotStateEvidence baseline,
            PilotStateEvidence candidate,
            CorrectedRealtimeReference correctedRealtimeReference)
        {
            string power = GetRealtimePowerId(candidate.State);
            for (int cameraIndex = 0; cameraIndex < ExpectedCameraCount; cameraIndex++)
            {
                string expectedCameraName = cameraIndex == 0
                    ? StartCameraName
                    : AdministrativeCameraName;
                BufferedImage left = baseline.Images[cameraIndex];
                BufferedImage right = candidate.Images[cameraIndex];
                CorrectedRealtimeD100Record realtime = correctedRealtimeReference.Find(
                    power,
                    expectedCameraName);
                string expectedCaptureStem = GetShortCaptureStem(expectedCameraName);
                if (!string.Equals(left.cameraName, expectedCameraName, StringComparison.Ordinal) ||
                    !string.Equals(right.cameraName, expectedCameraName, StringComparison.Ordinal) ||
                    !string.Equals(
                        right.pngFilename,
                        expectedCaptureStem + ".png",
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        right.exrFilename,
                        expectedCaptureStem + "_L.exr",
                        StringComparison.Ordinal) ||
                    (left.position - right.position).sqrMagnitude > 0.00000001f ||
                    Quaternion.Angle(left.rotation, right.rotation) > 0.0001f ||
                    !NearlyEqual(left.fieldOfView, right.fieldOfView) ||
                    !NearlyEqual(left.nearClip, right.nearClip) ||
                    !NearlyEqual(left.farClip, right.farClip) ||
                    left.cullingMask != right.cullingMask ||
                    left.allowHdr != right.allowHdr || left.allowMsaa != right.allowMsaa ||
                    left.presentationRenderTargetMsaa != right.presentationRenderTargetMsaa ||
                    left.presentationPostProcessing != right.presentationPostProcessing ||
                    left.hdrPostProcessing != right.hdrPostProcessing)
                {
                    throw new InvalidOperationException(
                        "Baseline/candidate fixed-camera contract differs for " +
                        candidate.State.Id + "/" + expectedCameraName + ".");
                }
                if (realtime.StartPower100 != candidate.State.StartPower100 ||
                    realtime.AdministrativePower100 != candidate.State.AdministrativePower100 ||
                    realtime.DoorPercent != 100 ||
                    (right.position - realtime.Position).sqrMagnitude > 0.00000001f ||
                    Quaternion.Angle(right.rotation, realtime.Rotation) > 0.0001f ||
                    !NearlyEqual(right.fieldOfView, realtime.FieldOfView) ||
                    !NearlyEqual(right.nearClip, realtime.NearClip) ||
                    !NearlyEqual(right.farClip, realtime.FarClip) ||
                    right.cullingMask != realtime.CullingMask ||
                    right.allowHdr != realtime.AllowHdr ||
                    right.allowMsaa != realtime.AllowMsaa ||
                    right.presentationRenderTargetMsaa != realtime.PresentationRenderTargetMsaa ||
                    right.presentationPostProcessing != realtime.PresentationPostProcessing ||
                    right.hdrPostProcessing != realtime.HdrPostProcessing)
                {
                    throw new InvalidOperationException(
                        "Baked/realtime exact camera contract differs for " +
                        candidate.State.Id + "/" + expectedCameraName + ".");
                }
            }
        }

        private static int ValidateRendererAndAtlasPairing(
            PilotStateEvidence baseline,
            PilotStateEvidence candidate)
        {
            int realtimeScaleOffsetDifferenceCount = 0;
            if (baseline.Lightmaps.Count != candidate.Lightmaps.Count)
            {
                throw new InvalidOperationException(
                    "Baseline/candidate lightmap atlas counts differ for " +
                    candidate.State.Id + ".");
            }
            for (int i = 0; i < baseline.Lightmaps.Count; i++)
            {
                if (baseline.Lightmaps[i].index != i || candidate.Lightmaps[i].index != i)
                {
                    throw new InvalidOperationException(
                        "Baseline/candidate lightmap atlas indices are not paired for " +
                        candidate.State.Id + " at " + i + ".");
                }
            }

            Dictionary<string, RendererMapRecord> baselineByKey = baseline.RendererMap.records
                .ToDictionary(record => record.stableKey, record => record, StringComparer.Ordinal);
            Dictionary<string, RendererMapRecord> candidateByKey = candidate.RendererMap.records
                .ToDictionary(record => record.stableKey, record => record, StringComparer.Ordinal);
            if (baselineByKey.Count != ExpectedRendererCount ||
                candidateByKey.Count != ExpectedRendererCount ||
                !baselineByKey.Keys.OrderBy(key => key, StringComparer.Ordinal).SequenceEqual(
                    candidateByKey.Keys.OrderBy(key => key, StringComparer.Ordinal),
                    StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Baseline/candidate renderer structural stable-key sets differ for " +
                    candidate.State.Id + ".");
            }

            foreach (KeyValuePair<string, RendererMapRecord> pair in baselineByKey)
            {
                RendererMapRecord left = pair.Value;
                RendererMapRecord right = candidateByKey[pair.Key];
                if (!string.Equals(left.roomRole, right.roomRole, StringComparison.Ordinal) ||
                    !string.Equals(left.relativePath, right.relativePath, StringComparison.Ordinal) ||
                    !string.Equals(
                        left.siblingIndexedRelativePath,
                        right.siblingIndexedRelativePath,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        left.rendererType,
                        right.rendererType,
                        StringComparison.Ordinal) ||
                    left.componentOrdinal != right.componentOrdinal ||
                    !string.Equals(
                        left.meshAssetGuid,
                        right.meshAssetGuid,
                        StringComparison.Ordinal) ||
                    left.meshLocalId != right.meshLocalId ||
                    left.hasUv2 != right.hasUv2 ||
                    left.meshPersistent != right.meshPersistent ||
                    !string.Equals(
                        left.uv2ContentSha256,
                        right.uv2ContentSha256,
                        StringComparison.Ordinal) ||
                    left.lightmapIndex != right.lightmapIndex ||
                    !ExactVector4Equals(
                        left.lightmapScaleOffset,
                        right.lightmapScaleOffset) ||
                    left.realtimeLightmapIndex != right.realtimeLightmapIndex)
                {
                    throw new InvalidOperationException(
                        "Baseline/candidate renderer mesh GUID/local ID, UV2 hash, baked " +
                        "lightmap index/ST, or realtime lightmap index pairing changed for " +
                        candidate.State.Id + ": " + pair.Key);
                }
                if (!IsFinite(left.stateDependentRealtimeLightmapScaleOffsetDiagnostic) ||
                    !IsFinite(right.stateDependentRealtimeLightmapScaleOffsetDiagnostic))
                {
                    throw new InvalidOperationException(
                        "Renderer state-dependent realtime lightmap scale-offset diagnostic is " +
                        "non-finite for " + candidate.State.Id + ": " + pair.Key);
                }
                if (!ExactVector4Equals(
                        left.stateDependentRealtimeLightmapScaleOffsetDiagnostic,
                        right.stateDependentRealtimeLightmapScaleOffsetDiagnostic))
                {
                    realtimeScaleOffsetDifferenceCount++;
                }
                if (left.lightmapIndex >= baseline.Lightmaps.Count ||
                    right.lightmapIndex >= candidate.Lightmaps.Count)
                {
                    throw new InvalidOperationException(
                        "Renderer lightmap index escaped paired atlas bounds: " + pair.Key);
                }
            }
            return realtimeScaleOffsetDifferenceCount;
        }

        private static void ValidateCaptureDiversityAndSignal(
            PilotStateEvidence baseline,
            PilotStateEvidence candidate)
        {
            var pngHashes = new HashSet<string>(StringComparer.Ordinal);
            var exrHashes = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < baseline.Images.Count; i++)
            {
                pngHashes.Add(baseline.Images[i].pngSha256);
                exrHashes.Add(baseline.Images[i].exrSha256);
            }
            for (int i = 0; i < candidate.Images.Count; i++)
            {
                pngHashes.Add(candidate.Images[i].pngSha256);
                exrHashes.Add(candidate.Images[i].exrSha256);
            }
            if (pngHashes.Count <= 1 || exrHashes.Count <= 1)
            {
                throw new InvalidOperationException(
                    "All joint-pair state captures are identical; refusing publication.");
            }

            Dictionary<string, BufferedImage> baselineByCamera = baseline.Images.ToDictionary(
                image => image.cameraName,
                image => image,
                StringComparer.Ordinal);
            for (int i = 0; i < candidate.Images.Count; i++)
            {
                BufferedImage right = candidate.Images[i];
                if (!baselineByCamera.TryGetValue(right.cameraName, out BufferedImage left) ||
                    string.Equals(left.pngSha256, right.pngSha256, StringComparison.Ordinal) ||
                    string.Equals(left.exrSha256, right.exrSha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Baseline/candidate same-camera images are missing or identical for " +
                        candidate.State.Id + "/" + right.cameraName + ".");
                }
            }
        }

        private static void AssertGeneratedArtifactsUnchanged(PilotEvidence evidence)
        {
            if (evidence == null || evidence.States == null)
                throw new InvalidOperationException("Generated evidence set is missing.");
            for (int stateIndex = 0; stateIndex < evidence.States.Count; stateIndex++)
            {
                PilotStateEvidence state = evidence.States[stateIndex];
                AssertOwnedGeneratedAsset(
                    state.CopiedScenePath,
                    evidence.GeneratedWorkingPath,
                    "copied scene");
                AssertOwnedGeneratedAsset(
                    state.CopiedLightingSettingsPath,
                    evidence.GeneratedWorkingPath,
                    "copied lighting settings");
                AssertOwnedGeneratedAsset(
                    state.LightingDataPath,
                    evidence.GeneratedWorkingPath,
                    "lighting data");
                AssertFileHash(state.CopiedScenePath, state.CopiedSceneRawSha256);
                AssertDependencyHash(
                    state.CopiedScenePath,
                    state.CopiedSceneDependencyHash);
                AssertFileHash(
                    state.CopiedLightingSettingsPath,
                    state.CopiedLightingSettingsRawSha256);
                AssertDependencyHash(
                    state.CopiedLightingSettingsPath,
                    state.CopiedLightingSettingsDependencyHash);
                AssertFileHash(state.LightingDataPath, state.LightingDataRawSha256);
                AssertDependencyHash(
                    state.LightingDataPath,
                    state.LightingDataDependencyHash);
                for (int i = 0; i < state.Lightmaps.Count; i++)
                {
                    AssertFileHash(state.Lightmaps[i].colorPath, state.Lightmaps[i].colorSha256);
                    AssertFileHash(
                        state.Lightmaps[i].directionPath,
                        state.Lightmaps[i].directionSha256);
                }
                for (int i = 0; i < state.Reflections.Count; i++)
                {
                    AssertFileHash(
                        state.Reflections[i].cubemapPath,
                        state.Reflections[i].cubemapSha256);
                }
            }
        }

        private static void AssertFileHash(string assetPath, string expectedSha256)
        {
            if (!string.Equals(
                    ComputeFileSha256(assetPath),
                    expectedSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Generated artifact hash changed: " + assetPath);
            }
        }

        private static void AssertDependencyHash(string assetPath, string expectedHash)
        {
            if (!string.Equals(
                    AssetDatabase.GetAssetDependencyHash(assetPath).ToString(),
                    expectedHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Generated artifact dependency hash changed: " + assetPath);
            }
        }

        private static string BuildSourceSnapshot(PilotEvidence evidence)
        {
            var builder = new StringBuilder(8192);
            builder.AppendLine("DungeonPortalJointPairSourceSnapshot/v1");
            builder.AppendLine("capturedBeforeAnyOwnedCopy=true");
            builder.AppendLine("sourceScene=" + ValidationScenePath);
            builder.AppendLine("sourceSceneExpectedRawSha256=" +
                               ExpectedValidationSceneRawSha256);
            builder.AppendLine("sourceSceneActualRawSha256=" +
                               ExpectedValidationSceneRawSha256);
            builder.AppendLine("sourceSceneStateFingerprint=" + evidence.SourceStateFingerprint);
            builder.AppendLine("correctedRealtimeDirectManifest=" +
                               CorrectedRealtimeManifestPath);
            builder.AppendLine("correctedRealtimeDirectManifestRawSha256=" +
                               evidence.CorrectedRealtimeReference.ManifestRawSha256);
            builder.AppendLine("correctedRealtimeDirectManifestStatus=" +
                               evidence.CorrectedRealtimeReference.StatusValue);
            builder.AppendLine("correctedRealtimeDirectManifestSceneBindingVerified=true");
            builder.AppendLine("correctedRealtimeD100RecordCount=" +
                               evidence.CorrectedRealtimeReference.Records.Length);
            builder.AppendLine("correctedRealtimeD100BytesAndHashesVerified=true");
            builder.AppendLine("expectedRendererCount=" + ExpectedRendererCount);
            builder.AppendLine("expectedStartLights=" + ExpectedStartLightCount);
            builder.AppendLine("expectedAdministrativeLights=" + ExpectedAdministrativeLightCount);
            builder.AppendLine("expectedDoorRenderers=" + ExpectedDoorRendererCount);
            builder.AppendLine("expectedEnabledDoorRenderers=" + ExpectedDoorEnabledRendererCount);
            builder.AppendLine("expectedFixedCameras=" + ExpectedCameraCount);
            builder.AppendLine("expectedConnectionEnabled=false");
            for (int i = 0; i < evidence.SourceAssets.Length; i++)
            {
                SourceAssetIdentity asset = evidence.SourceAssets[i];
                builder.AppendLine("asset." + i + ".path=" + asset.assetPath);
                builder.AppendLine("asset." + i + ".guid=" + asset.guid);
                builder.AppendLine("asset." + i + ".rawSha256=" + asset.rawSha256);
                builder.AppendLine("asset." + i + ".dependencyHash=" + asset.dependencyHash);
            }
            return builder.ToString();
        }

        private static string CanonicalizeEvidencePath(PilotEvidence evidence, string path)
        {
            if (evidence == null)
                throw new ArgumentNullException(nameof(evidence));
            string normalized = NormalizePath(path);
            if (normalized.Equals(evidence.GeneratedWorkingPath, StringComparison.Ordinal) ||
                normalized.StartsWith(
                    evidence.GeneratedWorkingPath + "/",
                    StringComparison.Ordinal))
            {
                return evidence.GeneratedRunPath +
                       normalized.Substring(evidence.GeneratedWorkingPath.Length);
            }
            if (normalized.Equals(evidence.EvidenceWorkingPath, StringComparison.Ordinal) ||
                normalized.StartsWith(
                    evidence.EvidenceWorkingPath + "/",
                    StringComparison.Ordinal))
            {
                return evidence.EvidenceRunPath +
                       normalized.Substring(evidence.EvidenceWorkingPath.Length);
            }
            throw new InvalidOperationException(
                "Evidence path is outside staging publication roots: " + path);
        }

        private sealed class PilotState
        {
            public PilotState(
                string id,
                bool startPower100,
                bool administrativePower100,
                bool isBaseline)
            {
                Id = id;
                StartPower100 = startPower100;
                AdministrativePower100 = administrativePower100;
                IsBaseline = isBaseline;
            }

            public string Id { get; }
            public bool StartPower100 { get; }
            public bool AdministrativePower100 { get; }
            public bool IsBaseline { get; }
        }

        private sealed class CorrectedRealtimeReference
        {
            private CorrectedRealtimeReference(
                string manifestRawSha256,
                string status,
                string validationScene,
                CorrectedRealtimeD100Record[] records)
            {
                ManifestRawSha256 = manifestRawSha256;
                StatusValue = status;
                ValidationScene = validationScene;
                Records = records;
            }

            public string ManifestRawSha256 { get; }
            public string StatusValue { get; }
            public string ValidationScene { get; }
            public CorrectedRealtimeD100Record[] Records { get; }

            public IEnumerable<string> ReferencedAssetPaths
            {
                get
                {
                    yield return CorrectedRealtimeManifestPath;
                    for (int i = 0; i < Records.Length; i++)
                    {
                        yield return Records[i].PresentationPngPath;
                        yield return Records[i].LinearHdrExrPath;
                    }
                }
            }

            public CorrectedRealtimeD100Record Find(
                string power,
                string cameraName)
            {
                CorrectedRealtimeD100Record[] matches = Records.Where(record =>
                        string.Equals(record.Power, power, StringComparison.Ordinal) &&
                        string.Equals(record.CameraName, cameraName, StringComparison.Ordinal))
                    .ToArray();
                if (matches.Length != 1)
                {
                    throw new InvalidOperationException(
                        "Corrected realtime D100 record is not unique: " +
                        power + "/" + cameraName + ".");
                }
                return matches[0];
            }

            public static CorrectedRealtimeReference CaptureAndValidate()
            {
                string manifestSha = ComputeFileSha256(CorrectedRealtimeManifestPath);
                if (!string.Equals(
                        manifestSha,
                        ExpectedCorrectedRealtimeManifestRawSha256,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Corrected realtime manifest raw SHA256 changed.");
                }
                string text = File.ReadAllText(
                    AssetPathToAbsolutePath(CorrectedRealtimeManifestPath),
                    Encoding.UTF8);
                string status = ReadUniqueManifestValue(text, "status");
                string validationScene = ReadUniqueManifestValue(text, "validationScene");
                if (!string.Equals(status, ExpectedCorrectedRealtimeStatus, StringComparison.Ordinal) ||
                    !string.Equals(validationScene, ValidationScenePath, StringComparison.Ordinal) ||
                    !string.Equals(
                        ReadUniqueManifestValue(text, "validationSceneExpectedSha256"),
                        ExpectedValidationSceneRawSha256,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        ReadUniqueManifestValue(text, "validationSceneActualSha256"),
                        ExpectedValidationSceneRawSha256,
                        StringComparison.OrdinalIgnoreCase) ||
                    ParseBool(ReadUniqueManifestValue(text, "productionAssetWrites"),
                        "productionAssetWrites"))
                {
                    throw new InvalidOperationException(
                        "Corrected realtime manifest status/scene/write binding changed.");
                }

                Dictionary<int, Dictionary<string, string>> captureFields =
                    ParseCaptureFields(text);
                var records = new List<CorrectedRealtimeD100Record>(
                    PilotStates.Length * ExpectedCameraCount);
                var expectedD100Indices = new[] { 8, 9, 18, 19, 28, 29, 38, 39 };
                int expectedIndexCursor = 0;
                for (int stateIndex = 0; stateIndex < PilotStates.Length; stateIndex++)
                {
                    PilotState state = PilotStates[stateIndex];
                    string power = GetRealtimePowerId(state);
                    for (int cameraIndex = 0; cameraIndex < ExpectedCameraCount; cameraIndex++)
                    {
                        int expectedRecordIndex = expectedD100Indices[expectedIndexCursor++];
                        if (!captureFields.TryGetValue(
                                expectedRecordIndex,
                                out Dictionary<string, string> fields))
                        {
                            throw new InvalidOperationException(
                                "Corrected realtime D100 record is missing: capture[" +
                                expectedRecordIndex + "].");
                        }
                        CorrectedRealtimeD100Record record = ValidateD100Record(
                            expectedRecordIndex,
                            fields,
                            state,
                            cameraIndex);
                        if (records.Any(existing =>
                                string.Equals(existing.Power, record.Power, StringComparison.Ordinal) &&
                                string.Equals(
                                    existing.CameraName,
                                    record.CameraName,
                                    StringComparison.Ordinal)))
                        {
                            throw new InvalidOperationException(
                                "Duplicate corrected realtime D100 state/camera record.");
                        }
                        records.Add(record);
                    }
                }

                int parsedD100Count = captureFields.Values.Count(fields =>
                    fields.TryGetValue("doorPercent", out string value) &&
                    string.Equals(value, "100", StringComparison.Ordinal));
                if (records.Count != PilotStates.Length * ExpectedCameraCount ||
                    parsedD100Count != records.Count)
                {
                    throw new InvalidOperationException(
                        "Corrected realtime manifest must contain exactly eight bound D100 records.");
                }
                if (records.Select(record => record.PresentationPngPath)
                        .Distinct(StringComparer.Ordinal).Count() != records.Count ||
                    records.Select(record => record.LinearHdrExrPath)
                        .Distinct(StringComparer.Ordinal).Count() != records.Count)
                {
                    throw new InvalidOperationException(
                        "Corrected realtime D100 evidence paths are not unique.");
                }

                return new CorrectedRealtimeReference(
                    manifestSha,
                    status,
                    validationScene,
                    records.ToArray());
            }

            private static Dictionary<int, Dictionary<string, string>> ParseCaptureFields(
                string text)
            {
                var result = new Dictionary<int, Dictionary<string, string>>();
                string[] lines = (text ?? string.Empty).Replace("\r", string.Empty).Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (!line.StartsWith("capture[", StringComparison.Ordinal))
                        continue;
                    int closeBracket = line.IndexOf("].", StringComparison.Ordinal);
                    int equals = line.IndexOf('=', closeBracket + 2);
                    if (closeBracket <= 8 || equals <= closeBracket + 2 ||
                        !int.TryParse(
                            line.Substring(8, closeBracket - 8),
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out int index))
                    {
                        throw new InvalidOperationException(
                            "Malformed corrected realtime capture record at line " + (i + 1) + ".");
                    }
                    string field = line.Substring(closeBracket + 2, equals - closeBracket - 2);
                    string value = line.Substring(equals + 1);
                    if (!result.TryGetValue(index, out Dictionary<string, string> fields))
                    {
                        fields = new Dictionary<string, string>(StringComparer.Ordinal);
                        result.Add(index, fields);
                    }
                    if (!fields.TryAdd(field, value))
                    {
                        throw new InvalidOperationException(
                            "Duplicate corrected realtime capture field: capture[" + index + "]." +
                            field + ".");
                    }
                }
                return result;
            }

            private static CorrectedRealtimeD100Record ValidateD100Record(
                int index,
                Dictionary<string, string> fields,
                PilotState state,
                int cameraIndex)
            {
                string power = GetRealtimePowerId(state);
                string cameraName = cameraIndex == 0
                    ? StartCameraName
                    : AdministrativeCameraName;
                string cameraStem = cameraIndex == 0
                    ? "Start_to_Admin"
                    : "Admin_to_Start";
                Vector3 expectedPosition = cameraIndex == 0
                    ? ExpectedStartCameraPosition
                    : ExpectedAdministrativeCameraPosition;
                Quaternion expectedRotation = cameraIndex == 0
                    ? ExpectedStartCameraRotation
                    : ExpectedAdministrativeCameraRotation;
                string expectedPng = power + "_D100__" + cameraStem + ".png";
                string expectedExr = power + "_D100__" + cameraStem + "__LinearNoPost.exr";

                if (!string.Equals(Get(fields, "status", index),
                        ExpectedCorrectedRealtimeStatus, StringComparison.Ordinal) ||
                    !string.Equals(Get(fields, "power", index), power, StringComparison.Ordinal) ||
                    !string.Equals(Get(fields, "startPower", index),
                        state.StartPower100 ? "P100" : "P0", StringComparison.Ordinal) ||
                    !string.Equals(Get(fields, "administrativePower", index),
                        state.AdministrativePower100 ? "P100" : "P0", StringComparison.Ordinal) ||
                    ParseInt(Get(fields, "doorPercent", index), "doorPercent") != 100 ||
                    !NearlyEqual(ParseFloat(Get(fields, "doorFraction", index), "doorFraction"), 1f) ||
                    !NearlyEqual(ParseFloat(Get(fields, "doorAngleDegrees", index),
                        "doorAngleDegrees"), 90f) ||
                    !string.Equals(Get(fields, "camera", index), cameraName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Corrected realtime D100 state/door/camera contract changed at capture[" +
                        index + "].");
                }

                string pngFilename = Get(fields, "presentationPng", index);
                string exrFilename = Get(fields, "linearHdrExr", index);
                if (!IsLeafFilename(pngFilename) || !IsLeafFilename(exrFilename) ||
                    !string.Equals(pngFilename, expectedPng, StringComparison.Ordinal) ||
                    !string.Equals(exrFilename, expectedExr, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Corrected realtime D100 filenames changed at capture[" + index + "].");
                }

                string pngPath = CorrectedRealtimeEvidenceRoot + "/" + pngFilename;
                string exrPath = CorrectedRealtimeEvidenceRoot + "/" + exrFilename;
                string declaredPngSha = Get(fields, "presentationPngSha256", index).ToUpperInvariant();
                string declaredExrSha = Get(fields, "linearHdrExrSha256", index).ToUpperInvariant();
                long declaredPngBytes = ParseLong(
                    Get(fields, "presentationPngBytes", index),
                    "presentationPngBytes");
                long declaredExrBytes = ParseLong(
                    Get(fields, "linearHdrExrBytes", index),
                    "linearHdrExrBytes");
                ValidateDeclaredFile(pngPath, declaredPngBytes, declaredPngSha);
                ValidateDeclaredFile(exrPath, declaredExrBytes, declaredExrSha);

                Vector3 position = ParseVector3(
                    Get(fields, "cameraPosition", index),
                    "cameraPosition");
                Quaternion rotation = ParseQuaternion(
                    Get(fields, "cameraRotation", index),
                    "cameraRotation");
                float fieldOfView = ParseFloat(Get(fields, "fieldOfView", index), "fieldOfView");
                float nearClip = ParseFloat(Get(fields, "nearClip", index), "nearClip");
                float farClip = ParseFloat(Get(fields, "farClip", index), "farClip");
                int cullingMask = ParseInt(Get(fields, "cullingMask", index), "cullingMask");
                bool allowHdr = ParseBool(Get(fields, "allowHDR", index), "allowHDR");
                bool allowMsaa = ParseBool(Get(fields, "allowMSAA", index), "allowMSAA");
                int targetMsaa = ParseInt(
                    Get(fields, "presentationRenderTargetMSAA", index),
                    "presentationRenderTargetMSAA");
                bool presentationPost = ParseBool(
                    Get(fields, "presentationPostProcessing", index),
                    "presentationPostProcessing");
                bool hdrPost = ParseBool(
                    Get(fields, "hdrPostProcessing", index),
                    "hdrPostProcessing");
                if (Vector3.Distance(position, expectedPosition) > 0.00001f ||
                    Quaternion.Angle(rotation, expectedRotation) > 0.001f ||
                    !NearlyEqual(fieldOfView, 90f) ||
                    Mathf.Abs(nearClip - 0.01f) > 0.000001f ||
                    !NearlyEqual(farClip, 1000f) || cullingMask != 262135 ||
                    !allowHdr || !allowMsaa || targetMsaa != 1 ||
                    !presentationPost || hdrPost)
                {
                    throw new InvalidOperationException(
                        "Corrected realtime D100 camera/render contract changed at capture[" +
                        index + "].");
                }

                return new CorrectedRealtimeD100Record
                {
                    Index = index,
                    Power = power,
                    StartPower100 = state.StartPower100,
                    AdministrativePower100 = state.AdministrativePower100,
                    DoorPercent = 100,
                    CameraName = cameraName,
                    PresentationPngPath = pngPath,
                    PresentationPngSha256 = declaredPngSha,
                    PresentationPngBytes = declaredPngBytes,
                    LinearHdrExrPath = exrPath,
                    LinearHdrExrSha256 = declaredExrSha,
                    LinearHdrExrBytes = declaredExrBytes,
                    Position = position,
                    Rotation = rotation,
                    FieldOfView = fieldOfView,
                    NearClip = nearClip,
                    FarClip = farClip,
                    CullingMask = cullingMask,
                    AllowHdr = allowHdr,
                    AllowMsaa = allowMsaa,
                    PresentationRenderTargetMsaa = targetMsaa,
                    PresentationPostProcessing = presentationPost,
                    HdrPostProcessing = hdrPost
                };
            }

            private static string Get(
                Dictionary<string, string> fields,
                string field,
                int index)
            {
                if (!fields.TryGetValue(field, out string value))
                {
                    throw new InvalidOperationException(
                        "Missing corrected realtime field: capture[" + index + "]." + field + ".");
                }
                return value;
            }

            private static void ValidateDeclaredFile(
                string assetPath,
                long declaredBytes,
                string declaredSha256)
            {
                string absolute = AssetPathToAbsolutePath(assetPath);
                if (!File.Exists(absolute) || new FileInfo(absolute).Length != declaredBytes ||
                    !string.Equals(
                        ComputeFileSha256(assetPath),
                        declaredSha256,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Corrected realtime evidence byte/SHA binding failed: " + assetPath);
                }
            }

            private static bool IsLeafFilename(string value)
            {
                return !string.IsNullOrWhiteSpace(value) &&
                       value.IndexOf('/') < 0 && value.IndexOf('\\') < 0 &&
                       string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal);
            }

            private static int ParseInt(string value, string label)
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out int result))
                    throw new InvalidOperationException("Invalid realtime integer: " + label + ".");
                return result;
            }

            private static long ParseLong(string value, string label)
            {
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out long result) || result <= 0)
                    throw new InvalidOperationException("Invalid realtime byte count: " + label + ".");
                return result;
            }

            private static float ParseFloat(string value, string label)
            {
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out float result) || !IsFinite(result))
                    throw new InvalidOperationException("Invalid realtime float: " + label + ".");
                return result;
            }

            private static bool ParseBool(string value, string label)
            {
                if (!bool.TryParse(value, out bool result))
                    throw new InvalidOperationException("Invalid realtime boolean: " + label + ".");
                return result;
            }

            private static Vector3 ParseVector3(string value, string label)
            {
                string[] values = value.Split(',');
                if (values.Length != 3)
                    throw new InvalidOperationException("Invalid realtime Vector3: " + label + ".");
                return new Vector3(
                    ParseFloat(values[0], label),
                    ParseFloat(values[1], label),
                    ParseFloat(values[2], label));
            }

            private static Quaternion ParseQuaternion(string value, string label)
            {
                string[] values = value.Split(',');
                if (values.Length != 4)
                    throw new InvalidOperationException("Invalid realtime Quaternion: " + label + ".");
                return new Quaternion(
                    ParseFloat(values[0], label),
                    ParseFloat(values[1], label),
                    ParseFloat(values[2], label),
                    ParseFloat(values[3], label));
            }
        }

        private sealed class CorrectedRealtimeD100Record
        {
            public int Index;
            public string Power;
            public bool StartPower100;
            public bool AdministrativePower100;
            public int DoorPercent;
            public string CameraName;
            public string PresentationPngPath;
            public string PresentationPngSha256;
            public long PresentationPngBytes;
            public string LinearHdrExrPath;
            public string LinearHdrExrSha256;
            public long LinearHdrExrBytes;
            public Vector3 Position;
            public Quaternion Rotation;
            public float FieldOfView;
            public float NearClip;
            public float FarClip;
            public int CullingMask;
            public bool AllowHdr;
            public bool AllowMsaa;
            public int PresentationRenderTargetMsaa;
            public bool PresentationPostProcessing;
            public bool HdrPostProcessing;
        }

        private static string GetRealtimePowerId(PilotState state)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.Id) ||
                !state.Id.EndsWith("__Door100", StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid full-open pilot state identifier.");
            return state.Id.Substring(0, state.Id.Length - "__Door100".Length);
        }

        private sealed class StateOwnedPaths
        {
            public string StateId;
            public string StateGeneratedPath;
            public string CopiedScenePath;
            public string CopiedLightingSettingsPath;
            public string SourceSceneGuid;
            public string CopiedSceneGuid;
            public string SourceLightingSettingsGuid;
            public string CopiedLightingSettingsGuid;
            public string LightingSettingsCanonicalSerializedFingerprint;
        }

        private sealed class PilotEvidence
        {
            public string RunId;
            public string GeneratedRunPath;
            public string EvidenceRunPath;
            public string GeneratedWorkingPath;
            public string EvidenceWorkingPath;
            public SourceAssetIdentity[] SourceAssets;
            public CorrectedRealtimeReference CorrectedRealtimeReference;
            public string SourceStateFingerprint;
            public List<PilotStateEvidence> States;
            public string Manifest;
            public string SourceSnapshotText;
        }

        private sealed class PilotStateEvidence
        {
            public PilotState State;
            public string CopiedScenePath;
            public string CopiedLightingSettingsPath;
            public string SourceSceneGuid;
            public string CopiedSceneGuid;
            public string SourceLightingSettingsGuid;
            public string CopiedLightingSettingsGuid;
            public string CopiedSceneRawSha256;
            public string CopiedSceneDependencyHash;
            public string CopiedLightingSettingsRawSha256;
            public string CopiedLightingSettingsDependencyHash;
            public string LightingDataPath;
            public string LightingDataRawSha256;
            public string LightingDataDependencyHash;
            public string CopyStateFingerprint;
            public string LightingSettingsCanonicalSerializedFingerprint;
            public EnvironmentContract Environment;
            public PowerApplyResult StartPower;
            public PowerApplyResult AdministrativePower;
            public DoorBakeContract Door;
            public ProbeStencil ProbeStencil;
            public ProbeSampleFile ProbeSamples;
            public RendererMapFile RendererMap;
            public int RealtimeLightmapScaleOffsetDifferenceFromBaselineCount;
            public List<LightmapArtifact> Lightmaps;
            public List<ReflectionArtifact> Reflections;
            public List<BufferedImage> Images;
            public DateTime BakeStartedUtc;
            public TimeSpan BakeElapsed;
            public string ProbeJson;
            public string RendererMapJson;
        }

        private sealed class PowerApplyResult
        {
            public PowerApplyResult(
                string label,
                bool power100,
                DungeonTileBakeData selectedBake,
                int lightCount,
                int ignoredLightCount,
                int enabledAfter,
                int emissionEntryCount,
                int appliedEmissionEntryCount,
                Dictionary<Renderer, Dictionary<int, Material>> expectedEmissionSlots)
            {
                Label = label;
                Power100 = power100;
                SelectedBake = selectedBake;
                LightCount = lightCount;
                IgnoredLightCount = ignoredLightCount;
                EnabledAfter = enabledAfter;
                EmissionEntryCount = emissionEntryCount;
                AppliedEmissionEntryCount = appliedEmissionEntryCount;
                ExpectedEmissionSlots = expectedEmissionSlots;
            }

            public string Label { get; }
            public bool Power100 { get; }
            public DungeonTileBakeData SelectedBake { get; }
            public int LightCount { get; }
            public int IgnoredLightCount { get; }
            public int EnabledAfter { get; }
            public int EmissionEntryCount { get; }
            public int AppliedEmissionEntryCount { get; }
            public Dictionary<Renderer, Dictionary<int, Material>> ExpectedEmissionSlots { get; }
        }

        [Serializable]
        private sealed class RendererMapFile
        {
            public string schema;
            public string state;
            public int count;
            public string bakedAtlasPairingContract;
            public string realtimeLightmapScaleOffsetContract;
            public RendererMapRecord[] records;
        }

        [Serializable]
        private sealed class RendererMapRecord
        {
            public string stableKey;
            public string roomRole;
            public string relativePath;
            public string siblingIndexedRelativePath;
            public string rendererType;
            public int componentOrdinal;
            public string meshAssetGuid;
            public long meshLocalId;
            public bool meshPersistent;
            public bool hasUv2;
            public string uv2ContentSha256;
            public int lightmapIndex;
            public Vector4 lightmapScaleOffset;
            public int realtimeLightmapIndex;
            public Vector4 stateDependentRealtimeLightmapScaleOffsetDiagnostic;
            public string lightProbeUsage;
            public string reflectionProbeUsage;
            public bool enabled;
            public int materialCount;
        }

        [Serializable]
        private sealed class ProbeSampleFile
        {
            public string schema;
            public string state;
            public string coefficientLayout;
            public int startCount;
            public int administrativeCount;
            public int doorStencilCount;
            public int totalCount;
            public ProbeSampleRecord[] samples;
            public DoorSideSamples doorSideSamples;
        }

        [Serializable]
        private sealed class ProbeSampleRecord
        {
            public int index;
            public string role;
            public Vector3 worldPosition;
            public Vector3 coefficient0;
            public Vector3 coefficient1;
            public Vector3 coefficient2;
            public Vector3 coefficient3;
            public Vector3 coefficient4;
            public Vector3 coefficient5;
            public Vector3 coefficient6;
            public Vector3 coefficient7;
            public Vector3 coefficient8;
            public Vector4 occlusion;

            public static ProbeSampleRecord Create(
                int index,
                string role,
                Vector3 position,
                SphericalHarmonicsL2 probe,
                Vector4 occlusion)
            {
                return new ProbeSampleRecord
                {
                    index = index,
                    role = role,
                    worldPosition = position,
                    coefficient0 = GetCoefficient(probe, 0),
                    coefficient1 = GetCoefficient(probe, 1),
                    coefficient2 = GetCoefficient(probe, 2),
                    coefficient3 = GetCoefficient(probe, 3),
                    coefficient4 = GetCoefficient(probe, 4),
                    coefficient5 = GetCoefficient(probe, 5),
                    coefficient6 = GetCoefficient(probe, 6),
                    coefficient7 = GetCoefficient(probe, 7),
                    coefficient8 = GetCoefficient(probe, 8),
                    occlusion = occlusion
                };
            }

            public SphericalHarmonicsL2 ToProbe()
            {
                var probe = new SphericalHarmonicsL2();
                SetCoefficient(ref probe, 0, coefficient0);
                SetCoefficient(ref probe, 1, coefficient1);
                SetCoefficient(ref probe, 2, coefficient2);
                SetCoefficient(ref probe, 3, coefficient3);
                SetCoefficient(ref probe, 4, coefficient4);
                SetCoefficient(ref probe, 5, coefficient5);
                SetCoefficient(ref probe, 6, coefficient6);
                SetCoefficient(ref probe, 7, coefficient7);
                SetCoefficient(ref probe, 8, coefficient8);
                return probe;
            }
        }

        [Serializable]
        private sealed class DoorSideSamples
        {
            public ProbeSampleRecord positive;
            public ProbeSampleRecord negative;
            public ProbeSampleRecord edgePositive;
            public ProbeSampleRecord edgeNegative;
            public ProbeSampleRecord edgeAverage;

            public static DoorSideSamples Sample(DoorBakeContract door)
            {
                Vector3 normal = door.DoorLeaf.TransformDirection(Vector3.forward).normalized;
                Vector3 positivePosition =
                    door.PositiveRenderer.bounds.center + normal * DoorSideSampleOffset;
                Vector3 negativePosition =
                    door.NegativeRenderer.bounds.center - normal * DoorSideSampleOffset;
                Vector3 edgeCenter = door.EdgeRenderer.bounds.center;
                Vector3 edgePositivePosition = edgeCenter + normal * DoorSideSampleOffset;
                Vector3 edgeNegativePosition = edgeCenter - normal * DoorSideSampleOffset;
                var positions = new[]
                {
                    positivePosition,
                    negativePosition,
                    edgePositivePosition,
                    edgeNegativePosition
                };
                var probes = new SphericalHarmonicsL2[positions.Length];
                var occlusion = new Vector4[positions.Length];
                LightProbes.CalculateInterpolatedLightAndOcclusionProbes(
                    positions,
                    probes,
                    occlusion);
                SphericalHarmonicsL2 edge = AverageProbe(probes[2], probes[3]);
                Vector4 edgeOcclusion = (occlusion[2] + occlusion[3]) * 0.5f;
                return new DoorSideSamples
                {
                    positive = ProbeSampleRecord.Create(
                        0,
                        "DoorPositiveZ_+0.35m",
                        positions[0],
                        probes[0],
                        occlusion[0]),
                    negative = ProbeSampleRecord.Create(
                        1,
                        "DoorNegativeZ_-0.35m",
                        positions[1],
                        probes[1],
                        occlusion[1]),
                    edgePositive = ProbeSampleRecord.Create(
                        2,
                        "DoorEdge_+0.35m",
                        positions[2],
                        probes[2],
                        occlusion[2]),
                    edgeNegative = ProbeSampleRecord.Create(
                        3,
                        "DoorEdge_-0.35m",
                        positions[3],
                        probes[3],
                        occlusion[3]),
                    edgeAverage = ProbeSampleRecord.Create(
                        4,
                        "DoorEdge_Average",
                        edgeCenter,
                        edge,
                        edgeOcclusion)
                };
            }
        }

        [Serializable]
        private sealed class LightmapArtifact
        {
            public int index;
            public string colorPath;
            public string colorSha256;
            public string directionPath;
            public string directionSha256;
            public string shadowMaskPath;
        }

        [Serializable]
        private sealed class ReflectionArtifact
        {
            public string role;
            public string probeName;
            public string hierarchyPath;
            public string cubemapPath;
            public string cubemapSha256;
            public int resolution;
            public float intensity;
            public bool boxProjection;
        }

        private sealed class RenderFailureMonitor : IDisposable
        {
            private readonly List<string> failures = new List<string>();
            private bool disposed;

            public RenderFailureMonitor()
            {
                Application.logMessageReceived += OnLogMessage;
            }

            public void ThrowIfFailed(string stage)
            {
                if (failures.Count == 0)
                    return;
                throw new InvalidOperationException(
                    "Unity logged a render failure during " + stage + ": " +
                    string.Join(" || ", failures.ToArray()));
            }

            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                Application.logMessageReceived -= OnLogMessage;
            }

            private void OnLogMessage(string condition, string stackTrace, LogType type)
            {
                if (disposed || (type != LogType.Error && type != LogType.Exception &&
                                 type != LogType.Assert))
                {
                    return;
                }
                if (!Contains(condition, "Render Graph") &&
                    !Contains(condition, "ZBinningJob") &&
                    !Contains(condition, "Forward+ jobs have not completed") &&
                    !Contains(stackTrace, "ReflectionProbeManager.UpdateGpuData") &&
                    !Contains(stackTrace, "ForwardLights.PreSetup") &&
                    !Contains(
                        stackTrace,
                        nameof(DungeonPortalJointPairTotalPilotCapture)))
                {
                    return;
                }
                string summary = (condition ?? "<no condition>")
                    .Replace('\r', ' ')
                    .Replace('\n', ' ');
                if (!failures.Contains(summary))
                    failures.Add(summary);
            }

            private static bool Contains(string value, string token)
            {
                return !string.IsNullOrEmpty(value) && value.IndexOf(
                           token,
                           StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private sealed class BufferedImage
        {
            public string cameraName;
            public string pngFilename;
            public byte[] pngBytes;
            public string pngSha256;
            public string exrFilename;
            public byte[] exrBytes;
            public string exrSha256;
            public double hdrMeanLuminance;
            public float hdrMaxLuminance;
            public Vector3 position;
            public Quaternion rotation;
            public float fieldOfView;
            public float nearClip;
            public float farClip;
            public int cullingMask;
            public bool allowHdr;
            public bool allowMsaa;
            public int presentationRenderTargetMsaa;
            public bool presentationPostProcessing;
            public bool hdrPostProcessing;
        }

        [Serializable]
        private sealed class SourceAssetIdentity
        {
            public string assetPath;
            public string guid;
            public string rawSha256;
            public string dependencyHash;
            public bool wasDirty;

            public static SourceAssetIdentity Capture(string path)
            {
                path = NormalizePath(path);
                Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null)
                    throw new InvalidOperationException("Source asset is missing: " + path);
                bool dirty = EditorUtility.IsDirty(asset);
                if (dirty)
                    throw new InvalidOperationException("Source asset must be clean: " + path);
                return new SourceAssetIdentity
                {
                    assetPath = path,
                    guid = AssetDatabase.AssetPathToGUID(path),
                    rawSha256 = ComputeFileSha256(path),
                    dependencyHash = AssetDatabase.GetAssetDependencyHash(path).ToString(),
                    wasDirty = false
                };
            }

            public void AssertUnchanged()
            {
                Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (asset == null || EditorUtility.IsDirty(asset) || wasDirty ||
                    !string.Equals(
                        AssetDatabase.AssetPathToGUID(assetPath),
                        guid,
                        StringComparison.Ordinal) ||
                    !string.Equals(ComputeFileSha256(assetPath), rawSha256, StringComparison.Ordinal) ||
                    !string.Equals(
                        AssetDatabase.GetAssetDependencyHash(assetPath).ToString(),
                        dependencyHash,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Source asset changed: " + assetPath);
                }
            }
        }

        private static SphericalHarmonicsL2 AverageProbe(
            SphericalHarmonicsL2 left,
            SphericalHarmonicsL2 right)
        {
            var result = new SphericalHarmonicsL2();
            for (int rgb = 0; rgb < 3; rgb++)
            {
                for (int coefficient = 0; coefficient < 9; coefficient++)
                    result[rgb, coefficient] = (left[rgb, coefficient] + right[rgb, coefficient]) * 0.5f;
            }
            return result;
        }

        private static Vector3 GetCoefficient(SphericalHarmonicsL2 probe, int coefficient)
        {
            return new Vector3(
                probe[0, coefficient],
                probe[1, coefficient],
                probe[2, coefficient]);
        }

        private static void SetCoefficient(
            ref SphericalHarmonicsL2 probe,
            int coefficient,
            Vector3 value)
        {
            probe[0, coefficient] = value.x;
            probe[1, coefficient] = value.y;
            probe[2, coefficient] = value.z;
        }

        private static string ComputeSceneStateFingerprint(Scene scene, GameObject root)
        {
            if (!scene.IsValid() || !scene.isLoaded || root == null || root.scene != scene)
                throw new InvalidOperationException("Cannot fingerprint an invalid scene/root.");
            var lines = new List<string>(2048)
            {
                "scene=" + NormalizePath(scene.path),
                "root=" + GetHierarchyPath(root.transform),
                "rootActive=" + root.activeSelf,
                "rootChildren=" + root.transform.childCount,
                "renderSettings=" + ComputeRenderSettingsFingerprint()
            };

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Material[] materials = renderer.sharedMaterials ?? Array.Empty<Material>();
                var materialText = new StringBuilder();
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    string[] keywords = material != null
                        ? (string[])material.shaderKeywords.Clone()
                        : Array.Empty<string>();
                    Array.Sort(keywords, StringComparer.Ordinal);
                    materialText.Append(materialIndex).Append(':')
                        .Append(material != null ? AssetDatabase.GetAssetPath(material) : "null")
                        .Append(':').Append(material != null && material.shader != null
                            ? material.shader.name
                            : "null")
                        .Append(':').Append(material != null ? material.renderQueue : -1)
                        .Append(':').Append(string.Join(",", keywords)).Append(';');
                }
                Mesh mesh = ResolveRendererMesh(renderer);
                string meshGuid = string.Empty;
                long meshLocalId = 0;
                if (mesh != null)
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out meshGuid, out meshLocalId);
                Mesh streams = renderer is MeshRenderer meshRenderer
                    ? meshRenderer.additionalVertexStreams
                    : null;
                string streamsGuid = string.Empty;
                long streamsLocalId = 0;
                if (streams != null)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        streams,
                        out streamsGuid,
                        out streamsLocalId);
                }
                lines.Add(
                    "R|" + GetHierarchyPath(renderer.transform) + "|" +
                    renderer.GetType().FullName + "|" + GetComponentOrdinal(renderer) + "|" +
                    renderer.gameObject.activeSelf + "|" + renderer.enabled + "|" +
                    renderer.forceRenderingOff + "|" + renderer.shadowCastingMode + "|" +
                    renderer.receiveShadows + "|" + renderer.renderingLayerMask + "|" +
                    renderer.lightmapIndex + "|" + FormatVector4(renderer.lightmapScaleOffset) + "|" +
                    renderer.lightProbeUsage + "|" + renderer.reflectionProbeUsage + "|" +
                    renderer.HasPropertyBlock() + "|" + meshGuid + "|" + meshLocalId + "|" +
                    streamsGuid + "|" + streamsLocalId + "|" + materialText);
            }

            Light[] lights = root.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                lines.Add(
                    "L|" + GetHierarchyPath(light.transform) + "|" + GetComponentOrdinal(light) + "|" +
                    light.gameObject.activeSelf + "|" + light.enabled + "|" + light.type + "|" +
                    light.lightmapBakeType + "|" + FormatFloat(light.intensity) + "|" +
                    FormatFloat(light.bounceIntensity) + "|" + FormatFloat(light.range) + "|" +
                    FormatFloat(light.spotAngle) + "|" + FormatColor(light.color) + "|" +
                    light.shadows + "|" + light.cullingMask + "|" + light.renderingLayerMask + "|" +
                    HasEnabledIgnoreLightControl(light));
            }

            Camera[] cameras = root.GetComponentsInChildren<Camera>(true);
            lines.Add("cameraFingerprint=" + ComputeCameraFingerprint(cameras));
            Component[] behaviours = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Component component = behaviours[i];
                if (component == null ||
                    !string.Equals(
                        component.GetType().FullName,
                        typeof(DungeonPortalTransportConnection).FullName,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                Behaviour behaviour = component as Behaviour;
                lines.Add("connection=" + GetHierarchyPath(component.transform) + "|" +
                          (behaviour != null && behaviour.enabled));
            }
            lines.Sort(StringComparer.Ordinal);
            return ComputeSha256(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
        }

        private static string ComputeRenderSettingsFingerprint()
        {
            return string.Join(
                "|",
                RenderSettings.fog,
                RenderSettings.fogMode,
                FormatColor(RenderSettings.fogColor),
                FormatFloat(RenderSettings.fogDensity),
                FormatFloat(RenderSettings.fogStartDistance),
                FormatFloat(RenderSettings.fogEndDistance),
                RenderSettings.ambientMode,
                FormatColor(RenderSettings.ambientSkyColor),
                FormatColor(RenderSettings.ambientEquatorColor),
                FormatColor(RenderSettings.ambientGroundColor),
                RenderSettings.skybox != null ? AssetDatabase.GetAssetPath(RenderSettings.skybox) : "null",
                RenderSettings.defaultReflectionResolution,
                FormatFloat(RenderSettings.reflectionIntensity),
                RenderSettings.reflectionBounces);
        }

        private static string ComputeCameraFingerprint(IEnumerable<Camera> cameras)
        {
            string[] records = cameras
                .Where(camera => camera != null)
                .Select(camera =>
                    GetHierarchyPath(camera.transform) + "|" + camera.enabled + "|" +
                    (camera.targetTexture != null ? camera.targetTexture.name : "null") + "|" +
                    FormatVector3(camera.transform.position) + "|" +
                    FormatQuaternion(camera.transform.rotation) + "|" +
                    FormatFloat(camera.fieldOfView) + "|" + FormatFloat(camera.nearClipPlane) + "|" +
                    FormatFloat(camera.farClipPlane) + "|" + camera.cullingMask + "|" +
                    camera.allowHDR + "|" + camera.allowMSAA + "|" +
                    ReadUrpPostProcessing(camera))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            return ComputeSha256(Encoding.UTF8.GetBytes(string.Join("\n", records)));
        }

        private static int GetComponentOrdinal(Component component)
        {
            Component[] sameType = component.transform.GetComponents(component.GetType());
            return Array.IndexOf(sameType, component);
        }

        private static string ComputeLightingSettingsCanonicalSerializedFingerprint(
            LightingSettings settings,
            string expectedAssetPath)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            expectedAssetPath = NormalizePath(expectedAssetPath);
            string actualAssetPath = NormalizePath(AssetDatabase.GetAssetPath(settings));
            if (string.IsNullOrWhiteSpace(expectedAssetPath) ||
                !string.Equals(actualAssetPath, expectedAssetPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "LightingSettings fingerprint asset-path binding failed. expected=" +
                    expectedAssetPath + " actual=" + actualAssetPath);
            }

            string expectedName = Path.GetFileNameWithoutExtension(expectedAssetPath);
            if (string.IsNullOrWhiteSpace(expectedName) ||
                !string.Equals(settings.name, expectedName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "LightingSettings serialized-name binding failed. expected=" + expectedName +
                    " actual=" + settings.name);
            }
            for (int i = 0; i < expectedName.Length; i++)
            {
                if (expectedName[i] < ' ' || expectedName[i] == '"' || expectedName[i] == '\\')
                {
                    throw new InvalidOperationException(
                        "LightingSettings asset name requires JSON escaping; refusing to weaken " +
                        "the exact m_Name comparison.");
                }
            }

            AssertLightingSettingsCanonicalParserSelfCheck();
            string serialized = EditorJsonUtility.ToJson(settings, false);
            string canonical = CanonicalizeExactlyOneLightingSettingsName(
                serialized,
                expectedName);
            return ComputeSha256(Encoding.UTF8.GetBytes(canonical));
        }

        private static string CanonicalizeExactlyOneLightingSettingsName(
            string serialized,
            string expectedName)
        {
            if (string.IsNullOrWhiteSpace(serialized))
                throw new InvalidOperationException("LightingSettings EditorJson is empty.");

            ValidateLightingSettingsJsonWrapper(
                serialized,
                out int wrapperObjectStart,
                out int wrapperObjectEnd);
            const string nameProperty = "m_Name";
            const string canonicalName = "__JOINT_PAIR_CANONICAL_LIGHTING_SETTINGS_NAME__";
            int structuralDepth = 0;
            int nameFieldCount = 0;
            int nameValueStart = -1;
            int nameValueEndExclusive = -1;

            for (int i = 0; i < serialized.Length; i++)
            {
                char current = serialized[i];
                if (current == '{' || current == '[')
                {
                    structuralDepth++;
                    continue;
                }
                if (current == '}' || current == ']')
                {
                    structuralDepth--;
                    if (structuralDepth < 0)
                        throw new InvalidOperationException("LightingSettings EditorJson is malformed.");
                    continue;
                }
                if (current != '"')
                    continue;

                int stringEnd = FindJsonStringEnd(serialized, i);
                bool isNameToken = stringEnd - i - 1 == nameProperty.Length &&
                                   string.CompareOrdinal(
                                       serialized,
                                       i + 1,
                                       nameProperty,
                                       0,
                                       nameProperty.Length) == 0;
                if (isNameToken)
                {
                    int cursor = stringEnd + 1;
                    while (cursor < serialized.Length && char.IsWhiteSpace(serialized[cursor]))
                        cursor++;
                    if (cursor < serialized.Length && serialized[cursor] == ':')
                    {
                        nameFieldCount++;
                        if (nameFieldCount != 1 || structuralDepth != 2 ||
                            i <= wrapperObjectStart || i >= wrapperObjectEnd)
                        {
                            throw new InvalidOperationException(
                                "LightingSettings EditorJson must contain exactly one direct-child " +
                                "m_Name field inside its sole LightingSettings wrapper.");
                        }
                        cursor++;
                        while (cursor < serialized.Length && char.IsWhiteSpace(serialized[cursor]))
                            cursor++;
                        if (cursor >= serialized.Length || serialized[cursor] != '"')
                        {
                            throw new InvalidOperationException(
                                "LightingSettings m_Name must remain a serialized string.");
                        }

                        int valueEnd = FindJsonStringEnd(serialized, cursor);
                        int valueLength = valueEnd - cursor - 1;
                        if (valueLength != expectedName.Length ||
                            string.CompareOrdinal(
                                serialized,
                                cursor + 1,
                                expectedName,
                                0,
                                expectedName.Length) != 0)
                        {
                            throw new InvalidOperationException(
                                "LightingSettings serialized m_Name does not match its exact " +
                                "asset filename stem.");
                        }
                        int afterValue = valueEnd + 1;
                        while (afterValue < serialized.Length &&
                               char.IsWhiteSpace(serialized[afterValue]))
                        {
                            afterValue++;
                        }
                        if (afterValue >= serialized.Length ||
                            (serialized[afterValue] != ',' && serialized[afterValue] != '}'))
                        {
                            throw new InvalidOperationException(
                                "LightingSettings m_Name JSON value has an invalid boundary.");
                        }
                        nameValueStart = cursor;
                        nameValueEndExclusive = valueEnd + 1;
                    }
                }
                i = stringEnd;
            }

            if (structuralDepth != 0 || nameFieldCount != 1 || nameValueStart < 0 ||
                nameValueEndExclusive <= nameValueStart)
            {
                throw new InvalidOperationException(
                    "LightingSettings EditorJson must contain exactly one direct-child m_Name " +
                    "field inside its sole LightingSettings wrapper.");
            }
            return serialized.Substring(0, nameValueStart) + "\"" + canonicalName + "\"" +
                   serialized.Substring(nameValueEndExclusive);
        }

        private static void ValidateLightingSettingsJsonWrapper(
            string serialized,
            out int wrapperObjectStart,
            out int wrapperObjectEnd)
        {
            const string wrapperProperty = "LightingSettings";
            int cursor = SkipJsonWhitespace(serialized, 0);
            if (cursor >= serialized.Length || serialized[cursor] != '{')
            {
                throw new InvalidOperationException(
                    "LightingSettings EditorJson root must be an object.");
            }
            cursor = SkipJsonWhitespace(serialized, cursor + 1);
            if (cursor >= serialized.Length || serialized[cursor] != '"')
            {
                throw new InvalidOperationException(
                    "LightingSettings EditorJson must begin with its sole wrapper property.");
            }

            int wrapperPropertyEnd = FindJsonStringEnd(serialized, cursor);
            if (wrapperPropertyEnd - cursor - 1 != wrapperProperty.Length ||
                string.CompareOrdinal(
                    serialized,
                    cursor + 1,
                    wrapperProperty,
                    0,
                    wrapperProperty.Length) != 0)
            {
                throw new InvalidOperationException(
                    "LightingSettings EditorJson wrapper property must be exactly LightingSettings.");
            }
            cursor = SkipJsonWhitespace(serialized, wrapperPropertyEnd + 1);
            if (cursor >= serialized.Length || serialized[cursor] != ':')
            {
                throw new InvalidOperationException(
                    "LightingSettings EditorJson wrapper property lacks a value.");
            }
            cursor = SkipJsonWhitespace(serialized, cursor + 1);
            if (cursor >= serialized.Length || serialized[cursor] != '{')
            {
                throw new InvalidOperationException(
                    "LightingSettings EditorJson wrapper value must be an object.");
            }

            wrapperObjectStart = cursor;
            wrapperObjectEnd = FindMatchingJsonContainerEnd(serialized, wrapperObjectStart);
            cursor = SkipJsonWhitespace(serialized, wrapperObjectEnd + 1);
            if (cursor >= serialized.Length || serialized[cursor] != '}')
            {
                throw new InvalidOperationException(
                    "LightingSettings must be the only property in the EditorJson root object.");
            }
            cursor = SkipJsonWhitespace(serialized, cursor + 1);
            if (cursor != serialized.Length)
            {
                throw new InvalidOperationException(
                    "LightingSettings EditorJson contains trailing root content.");
            }
        }

        private static int FindMatchingJsonContainerEnd(string json, int openingIndex)
        {
            if (string.IsNullOrEmpty(json) || openingIndex < 0 || openingIndex >= json.Length ||
                (json[openingIndex] != '{' && json[openingIndex] != '['))
            {
                throw new InvalidOperationException("Invalid JSON container boundary.");
            }

            var expectedClosers = new Stack<char>();
            for (int i = openingIndex; i < json.Length; i++)
            {
                char current = json[i];
                if (current == '"')
                {
                    i = FindJsonStringEnd(json, i);
                    continue;
                }
                if (current == '{')
                {
                    expectedClosers.Push('}');
                    continue;
                }
                if (current == '[')
                {
                    expectedClosers.Push(']');
                    continue;
                }
                if (current != '}' && current != ']')
                    continue;
                if (expectedClosers.Count == 0 || expectedClosers.Pop() != current)
                    throw new InvalidOperationException("LightingSettings EditorJson nesting is invalid.");
                if (expectedClosers.Count == 0)
                    return i;
            }
            throw new InvalidOperationException("LightingSettings EditorJson container is unterminated.");
        }

        private static int SkipJsonWhitespace(string json, int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
                index++;
            return index;
        }

        private static void AssertLightingSettingsCanonicalParserSelfCheck()
        {
            const string fixtureName = "Fixture";
            const string valid =
                "{\"LightingSettings\":{\"m_Name\":\"Fixture\",\"m_GIWorkflowMode\":1}}";
            const string expected =
                "{\"LightingSettings\":{\"m_Name\":" +
                "\"__JOINT_PAIR_CANONICAL_LIGHTING_SETTINGS_NAME__\"," +
                "\"m_GIWorkflowMode\":1}}";
            string actual = CanonicalizeExactlyOneLightingSettingsName(valid, fixtureName);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "LightingSettings canonical parser valid-fixture self-check failed.");
            }

            string[] invalidFixtures =
            {
                "{\"m_Name\":\"Fixture\"}",
                "{\"WrongWrapper\":{\"m_Name\":\"Fixture\"}}",
                "{\"LightingSettings\":[{\"m_Name\":\"Fixture\"}]}",
                "{\"LightingSettings\":{\"Nested\":{\"m_Name\":\"Fixture\"}}}",
                "{\"LightingSettings\":{\"m_Name\":\"Fixture\",\"m_Name\":\"Fixture\"}}",
                "{\"LightingSettings\":{\"m_Name\":123}}",
                "{\"LightingSettings\":{\"m_Name\":\"Fixture\"},\"extra\":0}",
                "{\"LightingSettings\":{\"m_Name\":\"Fixture\"}," +
                "\"LightingSettings\":{\"m_Name\":\"Fixture\"}}"
            };
            for (int i = 0; i < invalidFixtures.Length; i++)
            {
                bool rejected = false;
                try
                {
                    CanonicalizeExactlyOneLightingSettingsName(
                        invalidFixtures[i],
                        fixtureName);
                }
                catch (InvalidOperationException)
                {
                    rejected = true;
                }
                if (!rejected)
                {
                    throw new InvalidOperationException(
                        "LightingSettings canonical parser accepted invalid fixture " + i + ".");
                }
            }
        }

        private static int FindJsonStringEnd(string json, int openingQuoteIndex)
        {
            if (string.IsNullOrEmpty(json) || openingQuoteIndex < 0 ||
                openingQuoteIndex >= json.Length || json[openingQuoteIndex] != '"')
            {
                throw new InvalidOperationException("Invalid JSON string boundary.");
            }

            bool escaped = false;
            for (int i = openingQuoteIndex + 1; i < json.Length; i++)
            {
                char current = json[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (current == '"')
                    return i;
            }
            throw new InvalidOperationException("Unterminated JSON string in LightingSettings.");
        }

        private static void AssertOnlyCopiedSceneCanBeSaved(Scene scene, string expectedPath)
        {
            if (!scene.IsValid() || !scene.isLoaded || SceneManager.sceneCount != 1 ||
                SceneManager.GetActiveScene() != scene ||
                !string.Equals(NormalizePath(scene.path), expectedPath, StringComparison.Ordinal) ||
                string.Equals(NormalizePath(scene.path), ValidationScenePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Save boundary rejected: only the token-owned copied scene may be saved.");
            }
        }

        private static int CountLoadedNonPreviewScenes()
        {
            int count = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene))
                    count++;
            }
            return count;
        }

        private static GameObject FindUniqueRoot(Scene scene, string name)
        {
            GameObject result = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (!string.Equals(roots[i].name, name, StringComparison.Ordinal))
                    continue;
                if (result != null)
                    throw new InvalidOperationException("Duplicate scene root: " + name);
                result = roots[i];
            }
            if (result == null)
                throw new InvalidOperationException("Missing scene root: " + name);
            return result;
        }

        private static Transform FindUniqueDescendant(
            Transform root,
            string name,
            bool directChildOnly)
        {
            Transform result = null;
            if (directChildOnly)
            {
                for (int i = 0; i < root.childCount; i++)
                {
                    Transform child = root.GetChild(i);
                    if (!string.Equals(child.name, name, StringComparison.Ordinal))
                        continue;
                    if (result != null)
                        throw new InvalidOperationException("Duplicate direct child: " + name);
                    result = child;
                }
            }
            else
            {
                Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    if (!string.Equals(transforms[i].name, name, StringComparison.Ordinal))
                        continue;
                    if (result != null)
                        throw new InvalidOperationException("Duplicate descendant: " + name);
                    result = transforms[i];
                }
            }
            if (result == null)
                throw new InvalidOperationException("Missing hierarchy object: " + name);
            return result;
        }

        private static Transform FindOptionalDirectChild(Transform root, string name)
        {
            Transform result = null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (!string.Equals(child.name, name, StringComparison.Ordinal))
                    continue;
                if (result != null)
                    throw new InvalidOperationException("Duplicate direct child: " + name);
                result = child;
            }
            return result;
        }

        private static T RequireAsset<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException("Required asset is missing: " + path);
            return asset;
        }

        private static void EnsureAssetFolder(string assetFolder)
        {
            assetFolder = NormalizePath(assetFolder).TrimEnd('/');
            if (AssetDatabase.IsValidFolder(assetFolder))
                return;
            string parent = NormalizePath(Path.GetDirectoryName(assetFolder));
            string name = Path.GetFileName(assetFolder);
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name) ||
                !assetFolder.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Invalid asset folder: " + assetFolder);
            }
            EnsureAssetFolder(parent);
            string guid = AssetDatabase.CreateFolder(parent, name);
            if (string.IsNullOrWhiteSpace(guid) || !AssetDatabase.IsValidFolder(assetFolder))
                throw new InvalidOperationException("Could not create asset folder: " + assetFolder);
        }

        private static bool CreateExactFolder(
            string parent,
            string name,
            string expectedPath)
        {
            parent = NormalizePath(parent).TrimEnd('/');
            expectedPath = NormalizePath(expectedPath).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(name) || name.IndexOf('/') >= 0 ||
                name.IndexOf('\\') >= 0 ||
                !string.Equals(
                    parent + "/" + name,
                    expectedPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Exact folder parent/name/path relation is invalid: " + expectedPath);
            }
            if (AssetDatabase.IsValidFolder(expectedPath) ||
                Directory.Exists(AssetPathToAbsolutePath(expectedPath)))
            {
                throw new InvalidOperationException("Folder already exists: " + expectedPath);
            }
            string absolute = AssetPathToAbsolutePath(expectedPath);
            Directory.CreateDirectory(absolute);
            if (!Directory.Exists(absolute))
                return false;

            // Creating two sibling folders through AssetDatabase.CreateFolder in one
            // callback can transiently invalidate the first empty backing directory.
            // Materialize the exact path first, then synchronously register it with the
            // AssetDatabase before any owner marker is written.
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            return Directory.Exists(absolute) && AssetDatabase.IsValidFolder(expectedPath);
        }

        private static void WriteTextAsset(string path, string contents)
        {
            AssertUnityImportSafeAssetPath(path, "text asset");
            File.WriteAllText(
                AssetPathToAbsolutePath(path),
                contents ?? string.Empty,
                new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (!File.Exists(AssetPathToAbsolutePath(path)))
                throw new InvalidOperationException("Text evidence write failed: " + path);
        }

        private static void WriteBinaryAsset(string path, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException("Binary evidence is empty: " + path);
            AssertUnityImportSafeAssetPath(path, "binary asset");
            File.WriteAllBytes(AssetPathToAbsolutePath(path), bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void VerifyWrittenBytes(string path, byte[] expected, string expectedSha)
        {
            AssertLegacyReadableAssetPath(path, "written evidence");
            byte[] actual = File.ReadAllBytes(AssetPathToAbsolutePath(path));
            if (actual.Length != expected.Length ||
                !string.Equals(ComputeSha256(actual), expectedSha, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Written evidence hash mismatch: " + path);
            }
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) |
                   (bytes[offset + 1] << 16) |
                   (bytes[offset + 2] << 8) |
                   bytes[offset + 3];
        }

        private static string AssetPathToAbsolutePath(string assetPath)
        {
            assetPath = NormalizePath(assetPath);
            if (!assetPath.Equals("Assets", StringComparison.Ordinal) &&
                !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Path is outside Assets: " + assetPath);
            }
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new InvalidOperationException("Project root could not be resolved.");
            string relative = assetPath.Equals("Assets", StringComparison.Ordinal)
                ? "Assets"
                : assetPath.Replace('/', Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(projectRoot, relative));
            string assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    assetsRoot.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Resolved path escaped Assets: " + assetPath);
            }
            return full;
        }

        private static string ComputeFileSha256(string assetPath)
        {
            AssertLegacyReadableAssetPath(assetPath, "SHA256 input");
            string absolute = AssetPathToAbsolutePath(assetPath);
            if (!File.Exists(absolute))
                throw new InvalidOperationException("File does not exist: " + assetPath);
            return ComputeSha256(File.ReadAllBytes(absolute));
        }

        private static void AssertLegacyReadableAssetPath(string assetPath, string label)
        {
            string absolute = AssetPathToAbsolutePath(assetPath);
            if (absolute.Length >= LegacyMaxPathExclusive)
            {
                throw new PathTooLongException(
                    label + " exceeds the legacy absolute path limit. length=" +
                    absolute.Length.ToString(CultureInfo.InvariantCulture) +
                    " limitExclusive=" + LegacyMaxPathExclusive + " path=" + absolute);
            }
        }

        private static void AssertUnityImportSafeAssetPath(string assetPath, string label)
        {
            string absolute = AssetPathToAbsolutePath(assetPath);
            AssertUnityImportSafeAbsolutePath(absolute, label);
            if (!absolute.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                AssertUnityImportSafeAbsolutePath(absolute + ".meta", label + " meta companion");
        }

        private static void AssertUnityImportSafeAbsolutePath(string absolute, string label)
        {
            if (string.IsNullOrWhiteSpace(absolute) ||
                absolute.Length >= LegacyMaxPathExclusive ||
                absolute.Length > UnityImportSafeAbsolutePathMax)
            {
                throw new PathTooLongException(
                    label + " lacks Unity import/temp path headroom. length=" +
                    (absolute != null
                        ? absolute.Length.ToString(CultureInfo.InvariantCulture)
                        : "null") +
                    " max=" + UnityImportSafeAbsolutePathMax + " path=" + absolute);
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes ?? Array.Empty<byte>());
                var builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    builder.Append(hash[i].ToString("X2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static string GetGlobalObjectIdString(Object value)
        {
            if (value == null)
                return string.Empty;
            return GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();
        }

        private static Object ResolveGlobalObjectId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !GlobalObjectId.TryParse(value, out GlobalObjectId globalId))
            {
                return null;
            }
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId);
        }

        private static bool SameLightmaps(LightmapData[] left, LightmapData[] right)
        {
            int leftLength = left != null ? left.Length : 0;
            int rightLength = right != null ? right.Length : 0;
            if (leftLength != rightLength)
                return false;
            for (int i = 0; i < leftLength; i++)
            {
                LightmapData a = left[i];
                LightmapData b = right[i];
                if (a == null || b == null)
                {
                    if (a != b)
                        return false;
                    continue;
                }
                if (a.lightmapColor != b.lightmapColor || a.lightmapDir != b.lightmapDir ||
                    a.shadowMask != b.shadowMask)
                {
                    return false;
                }
            }
            return true;
        }

        private static string GetRoomRelativeSiblingIndexedPath(
            Transform transform,
            Transform roomRoot)
        {
            if (transform == null || roomRoot == null)
                throw new ArgumentNullException(transform == null ? nameof(transform) : nameof(roomRoot));
            if (transform == roomRoot)
                return "$room";

            var siblingIndices = new List<string>();
            Transform current = transform;
            while (current != null && current != roomRoot)
            {
                siblingIndices.Add(
                    current.GetSiblingIndex().ToString(CultureInfo.InvariantCulture));
                current = current.parent;
            }
            if (current != roomRoot || siblingIndices.Count == 0)
            {
                throw new InvalidOperationException(
                    "Renderer transform is not a descendant of its declared room root.");
            }
            siblingIndices.Reverse();
            return string.Join("/", siblingIndices);
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;
            var parts = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                parts.Add(current.name + "[" + current.GetSiblingIndex() + "]");
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static bool NearlyEqual(float left, float right)
        {
            return Mathf.Abs(left - right) <= 0.0001f;
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        private static string GetShortCaptureStem(string cameraName)
        {
            if (string.Equals(cameraName, StartCameraName, StringComparison.Ordinal))
                return "C0";
            if (string.Equals(cameraName, AdministrativeCameraName, StringComparison.Ordinal))
                return "C1";
            throw new InvalidOperationException(
                "No deterministic short capture filename is assigned to camera: " + cameraName);
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatVector3(Vector3 value)
        {
            return FormatFloat(value.x) + "," + FormatFloat(value.y) + "," + FormatFloat(value.z);
        }

        private static string FormatVector4(Vector4 value)
        {
            return FormatFloat(value.x) + "," + FormatFloat(value.y) + "," +
                   FormatFloat(value.z) + "," + FormatFloat(value.w);
        }

        private static string FormatQuaternion(Quaternion value)
        {
            return FormatFloat(value.x) + "," + FormatFloat(value.y) + "," +
                   FormatFloat(value.z) + "," + FormatFloat(value.w);
        }

        private static string FormatColor(Color value)
        {
            return FormatFloat(value.r) + "," + FormatFloat(value.g) + "," +
                   FormatFloat(value.b) + "," + FormatFloat(value.a);
        }
    }
}
