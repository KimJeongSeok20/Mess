using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DungeonPortalBakedBasisPoC.Validation;
using DungeonPortalTransportPoC;
using DungeonPortalTransportPoC.KExactBasisV1.Editor;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DungeonPortalBakedBasisPoC.Validation.Editor
{
    /// <summary>
    /// Evidence capture for DPBB. The public entry point deliberately targets the two-scene
    /// StartMap dungeon-entry harness and the real local-player camera. The older isolated-scene
    /// recorder remains below as a diagnostic-only path; its output is never final visual evidence.
    /// </summary>
    public static class DungeonPortalBakedBasisSmokeCapture
    {
        private const string Status = "DPBB_SMOKE_CAPTURE_V2";
        private const string FinalStatus = "DPBB_STARTMAP_SMOKE_CAPTURE_V1";
        private const string StartMapHarnessScenePath =
            "Assets/Experiments/DungeonPortalBakedBasisPoC/Scenes/" +
            "Start_Admin_DPBB_StartMapDungeonEntryValidation.unity";
        private const string FinalCaptureRoute =
            "two-scene-actual-StartMap-local-player-camera";
        private const int CaptureWidth = 1920;
        private const int CaptureHeight = 1080;
        private const float DoorTolerance = 0.002f;
        private const float D0LeakageEpsilon = 0.00001f;
        private const string UnreviewedVisualVerdict = "UNREVIEWED";
        private const string ProductionParityReviewCandidateRole =
            "productionParityReviewCandidate";
        private const string DiagnosticEvidenceRole = "diagnostic";

        private static readonly PowerState[] PowerStates =
        {
            new PowerState("P000_P000", "P00", 0f, 0f),
            new PowerState("P100_P000", "P10", 1f, 0f),
            new PowerState("P000_P100", "P01", 0f, 1f),
            new PowerState("P100_P100", "P11", 1f, 1f)
        };

        private static readonly DoorPose[] DoorPoses =
        {
            new DoorPose(0), new DoorPose(25), new DoorPose(50), new DoorPose(75),
            new DoorPose(100)
        };

        [MenuItem("Tools/Dungeon/Lighting/Portal Baked Basis PoC/Capture FINAL StartMap Runtime Smoke (Play Mode; No Mode Toggle)")]
        public static void CaptureFromMenu()
        {
            string result = CaptureAll();
            if (result.StartsWith("PASS", StringComparison.Ordinal))
                Debug.Log(result);
            else
                Debug.LogError(result);
        }

        public static void CaptureCli()
        {
            string result = CaptureAll();
            if (!result.StartsWith("PASS", StringComparison.Ordinal))
                throw new InvalidOperationException(result);
            Debug.Log(result);
        }

        public static string CaptureAll()
        {
            try
            {
                return CaptureStartMapHarnessAllOrThrow();
            }
            catch (Exception exception)
            {
                return "FAIL " + FinalStatus + ": " + exception;
            }
        }

        [MenuItem("Tools/Dungeon/Lighting/Portal Baked Basis PoC/Capture Isolated Legacy Diagnostics (NOT Final Evidence)")]
        public static void CaptureIsolatedLegacyFromMenu()
        {
            string result = CaptureIsolatedLegacyForDiagnostics();
            if (result.StartsWith("PASS", StringComparison.Ordinal))
                Debug.Log(result);
            else
                Debug.LogError(result);
        }

        /// <summary>
        /// Retained solely for debugging the older isolated scene. Consumers must not cite this
        /// output as StartMap, gameplay-entry, or visual-acceptance evidence.
        /// </summary>
        public static string CaptureIsolatedLegacyForDiagnostics()
        {
            try
            {
                return CaptureAllOrThrow().Replace(
                    "visualParityClaimed=false",
                    "visualParityClaimed=false\nfinalEvidenceEligible=false\n" +
                    "captureRoute=isolated-fixed-cameras-legacy-diagnostics-only");
            }
            catch (Exception exception)
            {
                return "FAIL " + Status + ": " + exception;
            }
        }

        internal static string DescribeFinalCaptureContractForTests()
        {
            return "entry=CaptureAll;route=" + FinalCaptureRoute +
                   ";requiresControllerReady=true;requiresActiveStartMap=true;" +
                   "captureCamera=ActualPlayerCamera;fixedCameraCaptureForbidden=true;" +
                   "requiresOriginalPlayerCameraPostProcessingEnabled=true;" +
                   "productionParityReviewCandidate=HUMAN_SPOT_ON+PNG+postEnabled;" +
                   "diagnosticArtifacts=ORACLE_SPOT_OFF_PNG+all_postDisabled_EXR+all_state;" +
                   "rawVisualVerdict=UNREVIEWED;rawFinalEvidenceEligible=false;" +
                   "frames=TrySetCaptureFrame(0|1);" +
                   "policySets=ORACLE_SPOT_OFF,HUMAN_SPOT_ON;" +
                   "stateValidation=before,after,beforeRender,afterRender;" +
                   "commonFeatureDeltaAllowlist=DPBB_POWER,DPBB_DOOR,DPBB_REFLECTION;" +
                   "isolatedLegacyFinalEvidenceEligible=false";
        }

        internal static string DescribeRawArtifactEvidenceForTests(
            DungeonPortalBakedBasisEvidenceSpotPolicy policy,
            bool isPng,
            bool postProcessingEnabled)
        {
            ArtifactEvidenceDescriptor descriptor = isPng
                ? CreatePngEvidenceDescriptor(true, policy, postProcessingEnabled)
                : CreateExrEvidenceDescriptor(postProcessingEnabled);
            return descriptor.ToContractString();
        }

        internal static void RequireFinalPresentationPostEnabledForTests(bool postProcessingEnabled)
        {
            RequireFinalPresentationPostEnabled(postProcessingEnabled, "test-camera");
        }

        /// <summary>
        /// The only final-evidence route. It accepts the deliberately isolated DPBB pair scene
        /// only while the runtime controller has loaded the real StartMap additively, made it
        /// active, entered its dungeon environment, and supplied the actual local-player camera.
        /// </summary>
        private static string CaptureStartMapHarnessAllOrThrow()
        {
            StartMapCaptureContext context = RequireFinalStartMapHarness();
            string initialHarnessSha = DungeonPortalBakedBasisValidationContract.ComputeFileSha256(
                context.HarnessScene.path);
            string initialStartMapSha = DungeonPortalBakedBasisValidationContract.ComputeFileSha256(
                DungeonPortalBakedBasisStartMapRuntimeController.StartMapScenePath);
            KExactBasisV1EditorContract.SelectionSnapshot selection =
                KExactBasisV1EditorContract.SelectionSnapshot.Capture();
            KExactBasisV1EditorContract.ProtectedAssetSnapshot protectedAssets =
                KExactBasisV1EditorContract.ProtectedAssetSnapshot.Capture();
            DungeonPortalBakedBasisConnectionDriver driver = context.Controller.ConnectionDriver;
            DungeonPortalBakedBasisDoorShDriver doorSh = context.Controller.DoorShDriver;
            DungeonPortalBakedBasisReflectionDriver reflection = context.Controller.ReflectionDriver;
            DoorConfiguration door = DoorConfiguration.Capture(driver.DoorAngleSource);
            RuntimeSnapshot snapshot = RuntimeSnapshot.Capture(context.Bindings, driver, doorSh, reflection);
            string utc = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'",
                CultureInfo.InvariantCulture);
            string runId = utc + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string outputFolder = DungeonPortalBakedBasisValidationContract.EvidenceRoot +
                                  "/StartMapRuntime/" + runId;
            string absoluteOutputFolder = DungeonPortalBakedBasisValidationContract.AssetPathToAbsolutePath(
                outputFolder);
            Directory.CreateDirectory(absoluteOutputFolder);
            if (!Directory.Exists(absoluteOutputFolder))
                throw new IOException("Could not create final StartMap DPBB evidence folder.");

            var policySets = new List<PolicyCaptureSet>(2);
            RenderTargets targets = null;
            var monitor = new RenderFailureMonitor();
            StackTraceLogType originalLogStackTrace =
                Application.GetStackTraceLogType(LogType.Log);
            StackTraceLogType originalWarningStackTrace =
                Application.GetStackTraceLogType(LogType.Warning);
            bool restored = false;
            try
            {
                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
                Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
                targets = RenderTargets.Create();
                DungeonPortalBakedBasisEvidenceSpotPolicy[] policies =
                {
                    DungeonPortalBakedBasisEvidenceSpotPolicy.ORACLE_SPOT_OFF,
                    DungeonPortalBakedBasisEvidenceSpotPolicy.HUMAN_SPOT_ON
                };
                for (int i = 0; i < policies.Length; i++)
                {
                    context.ApplyPolicy(policies[i], "policy-set setup");
                    string policyFolder = outputFolder + "/" + policies[i];
                    Directory.CreateDirectory(
                        DungeonPortalBakedBasisValidationContract.AssetPathToAbsolutePath(policyFolder));
                    policySets.Add(CaptureFinalPolicySet(
                        context, driver, doorSh, reflection, door,
                        snapshot.StartIncomingResponseScale,
                        snapshot.AdministrativeIncomingResponseScale,
                        targets, monitor, policyFolder));
                }
                monitor.ThrowIfFailed("final StartMap matrix capture");
            }
            finally
            {
                targets?.Dispose();
                monitor.Dispose();
                try
                {
                    snapshot.Restore(context.Bindings, driver, doorSh, reflection);
                    context.RestoreInitialCameraAndPolicy();
                    restored = true;
                }
                finally
                {
                    Application.SetStackTraceLogType(LogType.Log, originalLogStackTrace);
                    Application.SetStackTraceLogType(LogType.Warning, originalWarningStackTrace);
                    selection.Restore();
                }
            }

            if (!restored)
                throw new InvalidOperationException("Final StartMap DPBB smoke restore did not complete.");
            snapshot.AssertRestored(context.Bindings, driver, doorSh, reflection);
            context.Validate("final restore");
            selection.AssertRestored();
            protectedAssets.AssertUnchanged();
            string finalHarnessSha = DungeonPortalBakedBasisValidationContract.ComputeFileSha256(
                context.HarnessScene.path);
            string finalStartMapSha = DungeonPortalBakedBasisValidationContract.ComputeFileSha256(
                DungeonPortalBakedBasisStartMapRuntimeController.StartMapScenePath);
            if (!string.Equals(initialHarnessSha, finalHarnessSha, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(initialStartMapSha, finalStartMapSha, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Final capture changed the saved StartMap harness or production StartMap scene file.");
            }

            string manifest = BuildFinalStartMapManifest(
                utc,
                initialHarnessSha,
                initialStartMapSha,
                context,
                policySets,
                protectedAssets.FingerprintSha256);
            string manifestPath = outputFolder + "/manifest_DPBB_STARTMAP_SMOKE.txt";
            File.WriteAllText(DungeonPortalBakedBasisValidationContract.AssetPathToAbsolutePath(manifestPath),
                manifest, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(manifestPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            string manifestSha = DungeonPortalBakedBasisValidationContract.ComputeFileSha256(manifestPath);
            return "PASS " + FinalStatus + "\n" +
                   "output=" + outputFolder + "\n" +
                   "policySetCount=" + policySets.Count + "\n" +
                   "stateCameraRecordCount=" + (policySets.Count * 64) + "\n" +
                   "captureRoute=" + FinalCaptureRoute + "\n" +
                   "actualStartMapActiveDuringCapture=true\n" +
                   "visualVerdict=UNREVIEWED\n" +
                   "finalEvidenceEligible=false\n" +
                   "visualParityClaimed=false\n" +
                   "manifestSha256=" + manifestSha;
        }

        private static string CaptureAllOrThrow()
        {
            DungeonPortalBakedBasisValidationContract.RequireCleanBuiltSceneActiveInPlayMode();
            Scene scene = SceneManager.GetActiveScene();
            string initialSceneSha = DungeonPortalBakedBasisValidationContract.ComputeFileSha256(
                DungeonPortalBakedBasisValidationContract.BuiltScenePath);
            KExactBasisV1EditorContract.SelectionSnapshot selection =
                KExactBasisV1EditorContract.SelectionSnapshot.Capture();
            KExactBasisV1EditorContract.ProtectedAssetSnapshot protectedAssets =
                KExactBasisV1EditorContract.ProtectedAssetSnapshot.Capture();
            KExactBasisV1EditorContract.SceneBindings bindings =
                KExactBasisV1EditorContract.ResolveSceneBindings(scene, false, false, false);
            DungeonPortalBakedBasisConnectionDriver driver =
                DungeonPortalBakedBasisValidationContract.RequireDriver(scene);
            DungeonPortalBakedBasisDoorShDriver doorSh =
                DungeonPortalBakedBasisValidationContract.RequireDoorShDriver(scene);
            DungeonPortalBakedBasisReflectionDriver reflection =
                DungeonPortalBakedBasisValidationContract.RequireReflectionDriver(scene);
            bool driverConfigured = driver.TryValidateConfiguration(out string driverFailure);
            bool doorShConfigured = doorSh.TryValidateConfiguration(out string doorShFailure);
            bool reflectionConfigured = reflection.TryValidateConfiguration(
                out string reflectionFailure);
            if (!driverConfigured || !doorShConfigured || driver.IsFaultLatched ||
                !reflectionConfigured || !reflection.IsInitialized || doorSh.IsFaultLatched ||
                reflection.IsFaultLatched)
            {
                throw new InvalidOperationException("Runtime contract is not capture-ready. Driver=" +
                                                    driverFailure + " DoorSH=" + doorShFailure +
                                                    " Reflection=" + reflectionFailure +
                                                    " ReflectionInitialized=" +
                                                    reflection.IsInitialized +
                                                    " DriverFault=" + driver.FaultReason +
                                                    " DoorSHFault=" + doorSh.FaultReason +
                                                    " ReflectionFault=" + reflection.FaultReason);
            }
            ApplyReflectionImmediate(
                reflection,
                driver.CurrentStartPower01,
                driver.CurrentAdministrativePower01,
                "initial snapshot");

            DoorConfiguration door = DoorConfiguration.Capture(driver.DoorAngleSource);
            RuntimeSnapshot snapshot = RuntimeSnapshot.Capture(
                bindings, driver, doorSh, reflection);
            string utc = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'",
                CultureInfo.InvariantCulture);
            string runId = utc + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string outputFolder = DungeonPortalBakedBasisValidationContract.EvidenceRoot + "/" + runId;
            string absoluteOutputFolder = DungeonPortalBakedBasisValidationContract.AssetPathToAbsolutePath(
                outputFolder);
            Directory.CreateDirectory(absoluteOutputFolder);
            if (!Directory.Exists(absoluteOutputFolder))
                throw new IOException("Could not create DPBB evidence folder.");

            var captures = new List<CaptureRecord>(40);
            var baselineRecords = new List<CaptureRecord>(8);
            var baseOnlyProductionRecords = new List<CaptureRecord>(8);
            var baseOnlyPrivateRecords = new List<CaptureRecord>(8);
            var d0Metrics = new List<D0Metric>(8);
            RenderTargets targets = null;
            var monitor = new RenderFailureMonitor();
            StackTraceLogType originalLogStackTrace =
                Application.GetStackTraceLogType(LogType.Log);
            StackTraceLogType originalWarningStackTrace =
                Application.GetStackTraceLogType(LogType.Warning);
            bool restored = false;
            try
            {
                // DungeonTileLightmapSwitcher reports every static-batched renderer while the
                // endpoint matrix is established. Preserve the messages and all Error/Exception
                // stacks, but avoid duplicating the same long informational stack thousands of
                // times into Editor.log during evidence capture.
                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
                Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
                targets = RenderTargets.Create();
                for (int powerIndex = 0; powerIndex < PowerStates.Length; powerIndex++)
                {
                    PowerState power = PowerStates[powerIndex];
                    SetDoorPose(bindings.DoorLeaf, driver.DoorAngleSource, door, 0f);

                    bool adjacentDisabled = driver.SetAdjacentEnabledForEvidence(
                        false, out string disabledFailure);
                    string baselineStateFailure = null;
                    bool baselineApplied = adjacentDisabled && driver.SetImmediateForEvidence(
                        power.StartPower01, power.AdministrativePower01, 0f,
                        out baselineStateFailure);
                    if (!adjacentDisabled || !baselineApplied)
                    {
                        throw new InvalidOperationException("Could not establish Adjacent-OFF D0 baseline: " +
                                                            disabledFailure + " " + baselineStateFailure);
                    }
                    ApplyReflectionImmediate(
                        reflection,
                        power.StartPower01,
                        power.AdministrativePower01,
                        "Adjacent-OFF D0 " + power.Id);
                    if (!doorSh.TryApplyNow(out string baselineDoorFailure))
                        throw new InvalidOperationException("D0 door SH restore failed: " + baselineDoorFailure);
                    AssertD0State(driver, doorSh, reflection, power, "baseline " + power.Id);

                    var baselinesForPower = new Dictionary<string, FramePayload>(StringComparer.Ordinal);
                    for (int cameraIndex = 0; cameraIndex < bindings.Cameras.Length; cameraIndex++)
                    {
                        FramePayload payload = CaptureCamera(
                            bindings.Cameras[cameraIndex],
                            scene,
                            power,
                            DoorPoses[0],
                            door.OpenAngleDegrees,
                            driver,
                            doorSh,
                            reflection,
                            bindings,
                            targets,
                            monitor,
                            outputFolder,
                            "AdjacentOff_" + power.ShortToken + "_D000_",
                            true);
                        baselineRecords.Add(payload.Record);
                        baselinesForPower.Add(payload.Record.CameraToken, payload);
                    }

                    // Base-only parity keeps the physical leaf at D100 for both variants.
                    // The Adjacent-OFF side uses exact production endpoint maps. The
                    // Adjacent-ON side keeps the private working maps active while both
                    // response gains are zero. Reflection remains active at identical room
                    // powers in both variants and is therefore common-mode.
                    DoorPose baseOnlyPose = DoorPoses[DoorPoses.Length - 1];
                    SetDoorPose(
                        bindings.DoorLeaf,
                        driver.DoorAngleSource,
                        door,
                        baseOnlyPose.Fraction);
                    if (!driver.SetImmediateForEvidence(
                            power.StartPower01,
                            power.AdministrativePower01,
                            0f,
                            out string baseProductionFailure))
                    {
                        throw new InvalidOperationException(
                            "Could not establish base-only production endpoint: " +
                            baseProductionFailure);
                    }
                    ApplyReflectionImmediate(
                        reflection,
                        power.StartPower01,
                        power.AdministrativePower01,
                        "base-only production " + power.Id);
                    if (!doorSh.TryApplyNow(out string baseProductionDoorFailure))
                    {
                        throw new InvalidOperationException(
                            "Base-only production door SH restore failed: " +
                            baseProductionDoorFailure);
                    }
                    AssertD0State(
                        driver,
                        doorSh,
                        reflection,
                        power,
                        "base-only production " + power.Id);
                    for (int cameraIndex = 0; cameraIndex < bindings.Cameras.Length; cameraIndex++)
                    {
                        FramePayload payload = CaptureCamera(
                            bindings.Cameras[cameraIndex],
                            scene,
                            power,
                            baseOnlyPose,
                            door.OpenAngleDegrees,
                            driver,
                            doorSh,
                            reflection,
                            bindings,
                            targets,
                            monitor,
                            outputFolder,
                            "BaseOnly_AdjacentOff_",
                            false);
                        baseOnlyProductionRecords.Add(payload.Record);
                        payload.DisposePixels();
                    }

                    SetIncomingResponseScales(driver, 0f, 0f, "base-only zero response");
                    if (!driver.SetAdjacentEnabledForEvidence(true, out string enabledFailure))
                        throw new InvalidOperationException("Could not re-enable DPBB transport: " + enabledFailure);
                    float baseOnlyAperture = driver.DoorAngleSource.ApertureFraction;
                    if (!driver.SetImmediateForEvidence(
                            power.StartPower01,
                            power.AdministrativePower01,
                            baseOnlyAperture,
                            out string basePrivateFailure))
                    {
                        throw new InvalidOperationException(
                            "Could not establish base-only private endpoint: " +
                            basePrivateFailure);
                    }
                    ForceRecompose(
                        driver,
                        doorSh,
                        reflection,
                        power.StartPower01,
                        power.AdministrativePower01,
                        "base-only private " + power.Id);
                    AssertMatrixState(
                        driver,
                        doorSh,
                        reflection,
                        power,
                        baseOnlyPose,
                        baseOnlyAperture);
                    for (int cameraIndex = 0; cameraIndex < bindings.Cameras.Length; cameraIndex++)
                    {
                        FramePayload payload = CaptureCamera(
                            bindings.Cameras[cameraIndex],
                            scene,
                            power,
                            baseOnlyPose,
                            door.OpenAngleDegrees,
                            driver,
                            doorSh,
                            reflection,
                            bindings,
                            targets,
                            monitor,
                            outputFolder,
                            "BaseOnly_ResponseZero_",
                            false);
                        baseOnlyPrivateRecords.Add(payload.Record);
                        payload.DisposePixels();
                    }
                    SetIncomingResponseScales(
                        driver,
                        snapshot.StartIncomingResponseScale,
                        snapshot.AdministrativeIncomingResponseScale,
                        "normal matrix restore");

                    for (int poseIndex = 0; poseIndex < DoorPoses.Length; poseIndex++)
                    {
                        DoorPose pose = DoorPoses[poseIndex];
                        SetDoorPose(bindings.DoorLeaf, driver.DoorAngleSource, door, pose.Fraction);
                        float aperture = driver.DoorAngleSource.ApertureFraction;
                        if (!Approximately(aperture, PortalTransportMath.ComputeProjectedApertureFraction(
                                pose.Fraction), 0.0001f))
                        {
                            throw new InvalidOperationException("Physical door aperture mismatch at D" +
                                                                pose.Percent + ".");
                        }
                        if (!driver.SetImmediateForEvidence(
                                power.StartPower01,
                                power.AdministrativePower01,
                                aperture,
                                out string immediateFailure))
                        {
                            throw new InvalidOperationException("DPBB immediate settle failed: " + immediateFailure);
                        }
                        ForceRecompose(
                            driver,
                            doorSh,
                            reflection,
                            power.StartPower01,
                            power.AdministrativePower01,
                            "matrix " + power.Id + " D" + pose.Percent);
                        AssertMatrixState(driver, doorSh, reflection, power, pose, aperture);

                        for (int cameraIndex = 0; cameraIndex < bindings.Cameras.Length; cameraIndex++)
                        {
                            FramePayload payload = CaptureCamera(
                                bindings.Cameras[cameraIndex],
                                scene,
                                power,
                                pose,
                                door.OpenAngleDegrees,
                                driver,
                                doorSh,
                                reflection,
                                bindings,
                                targets,
                                monitor,
                                outputFolder,
                                string.Empty,
                                pose.Percent == 0);
                            captures.Add(payload.Record);
                            if (pose.Percent == 0)
                            {
                                if (!baselinesForPower.TryGetValue(payload.Record.CameraToken,
                                        out FramePayload baseline))
                                {
                                    throw new InvalidOperationException("Missing D0 baseline camera payload.");
                                }
                                D0Metric metric = CompareD0(baseline, payload);
                                d0Metrics.Add(metric);
                                payload.Record.D0Metric = metric;
                                baseline.DisposePixels();
                                payload.DisposePixels();
                            }
                            else
                            {
                                payload.DisposePixels();
                            }
                        }
                    }

                    foreach (KeyValuePair<string, FramePayload> pair in baselinesForPower)
                        pair.Value.DisposePixels();
                }

                if (captures.Count != 40 || baselineRecords.Count != 8 ||
                    baseOnlyProductionRecords.Count != 8 || baseOnlyPrivateRecords.Count != 8 ||
                    d0Metrics.Count != 8)
                {
                    throw new InvalidOperationException("Unexpected DPBB smoke record counts. Matrix=" +
                                                        captures.Count + " baseline=" + baselineRecords.Count +
                                                        " baseProduction=" +
                                                        baseOnlyProductionRecords.Count +
                                                        " basePrivate=" + baseOnlyPrivateRecords.Count +
                                                        " d0=" + d0Metrics.Count + ".");
                }
                monitor.ThrowIfFailed("matrix capture");
            }
            finally
            {
                targets?.Dispose();
                monitor.Dispose();
                try
                {
                    snapshot.Restore(bindings, driver, doorSh, reflection);
                    restored = true;
                }
                finally
                {
                    Application.SetStackTraceLogType(LogType.Log, originalLogStackTrace);
                    Application.SetStackTraceLogType(LogType.Warning, originalWarningStackTrace);
                    selection.Restore();
                }
            }

            if (!restored)
                throw new InvalidOperationException("DPBB smoke restore did not complete.");
            snapshot.AssertRestored(bindings, driver, doorSh, reflection);
            selection.AssertRestored();
            protectedAssets.AssertUnchanged();
            string finalSceneSha = DungeonPortalBakedBasisValidationContract.ComputeFileSha256(
                DungeonPortalBakedBasisValidationContract.BuiltScenePath);
            if (!string.Equals(initialSceneSha, finalSceneSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Capture changed the saved baked-basis scene file.");

            string manifest = BuildManifest(
                utc,
                initialSceneSha,
                captures,
                baselineRecords,
                baseOnlyProductionRecords,
                baseOnlyPrivateRecords,
                d0Metrics,
                protectedAssets.FingerprintSha256);
            string manifestPath = outputFolder + "/manifest_DPBB_SMOKE.txt";
            File.WriteAllText(DungeonPortalBakedBasisValidationContract.AssetPathToAbsolutePath(manifestPath),
                manifest, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(manifestPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            string manifestSha = DungeonPortalBakedBasisValidationContract.ComputeFileSha256(manifestPath);
            return "PASS " + Status + "\n" +
                   "output=" + outputFolder + "\n" +
                   "stateCameraRecordCount=" + captures.Count + "\n" +
                   "adjacentOffD0RecordCount=" + baselineRecords.Count + "\n" +
                   "baseOnlyProductionRecordCount=" + baseOnlyProductionRecords.Count + "\n" +
                   "baseOnlyPrivateRecordCount=" + baseOnlyPrivateRecords.Count + "\n" +
                   "d0Comparisons=" + d0Metrics.Count + "\n" +
                   "manifestSha256=" + manifestSha + "\n" +
                   "visualVerdict=UNREVIEWED\n" +
                   "finalEvidenceEligible=false\n" +
                   "visualParityClaimed=false";
        }

        private static StartMapCaptureContext RequireFinalStartMapHarness()
        {
            if (!Application.isPlaying || !EditorApplication.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying ||
                EditorApplication.isCompiling || EditorApplication.isUpdating || Lightmapping.isRunning)
            {
                throw new InvalidOperationException(
                    "Final StartMap capture requires an already-running stable Play Mode; it never changes Play Mode.");
            }
            if (SceneManager.sceneCount != 2)
            {
                throw new InvalidOperationException(
                    "Final StartMap capture requires exactly the two harness scenes, not an isolated validation scene.");
            }

            DungeonPortalBakedBasisStartMapRuntimeController[] controllers =
                Object.FindObjectsByType<DungeonPortalBakedBasisStartMapRuntimeController>(
                    FindObjectsInactive.Include);
            if (controllers.Length != 1 || controllers[0] == null)
                throw new InvalidOperationException("Expected exactly one StartMap DPBB runtime controller.");
            DungeonPortalBakedBasisStartMapRuntimeController controller = controllers[0];
            Scene active = SceneManager.GetActiveScene();
            Scene harness = controller.gameObject.scene;
            if (!controller.IsReady || !controller.StartMapScene.IsValid() ||
                !controller.StartMapScene.isLoaded || active != controller.StartMapScene ||
                !string.Equals(active.path,
                    DungeonPortalBakedBasisStartMapRuntimeController.StartMapScenePath,
                    StringComparison.Ordinal) ||
                !harness.IsValid() || !harness.isLoaded ||
                !string.Equals(harness.path, StartMapHarnessScenePath, StringComparison.Ordinal) ||
                controller.ValidationPairRoot == null ||
                controller.ValidationPairRoot.scene != harness ||
                controller.ActualPlayerCamera == null ||
                controller.ActualPlayerCamera.gameObject.scene != active ||
                controller.ActualPlayerCamera.targetTexture != null ||
                !controller.ActualPlayerCamera.enabled)
            {
                throw new InvalidOperationException(
                    "Final capture requires READY two-scene StartMap harness, active real StartMap, and its actual local-player camera.");
            }

            KExactBasisV1EditorContract.SceneBindings bindings =
                KExactBasisV1EditorContract.ResolveSceneBindings(harness, false, false, false);
            if (bindings.Root.gameObject != controller.ValidationPairRoot ||
                controller.ConnectionDriver == null || controller.DoorShDriver == null ||
                controller.ReflectionDriver == null ||
                controller.ConnectionDriver != DungeonPortalBakedBasisValidationContract.RequireDriver(harness) ||
                controller.DoorShDriver != DungeonPortalBakedBasisValidationContract.RequireDoorShDriver(harness) ||
                controller.ReflectionDriver != DungeonPortalBakedBasisValidationContract.RequireReflectionDriver(harness))
            {
                throw new InvalidOperationException("StartMap harness DPBB pair/driver binding drifted.");
            }
            for (int i = 0; i < bindings.Cameras.Length; i++)
            {
                Camera reference = bindings.Cameras[i];
                if (reference == null || reference.enabled || reference == controller.ActualPlayerCamera ||
                    reference.targetTexture != null)
                {
                    throw new InvalidOperationException(
                        "Final capture forbids an enabled/fallback fixed camera; only ActualPlayerCamera may render.");
                }
            }

            if (!controller.ActualPlayerCamera.TryGetComponent(
                    out UniversalAdditionalCameraData actualPlayerCameraData))
            {
                throw new InvalidOperationException(
                    "ActualPlayerCamera is missing UniversalAdditionalCameraData; final capture cannot prove production post-processing parity.");
            }
            RequireFinalPresentationPostEnabled(
                actualPlayerCameraData.renderPostProcessing,
                controller.ActualPlayerCamera.name);
            if (controller.ActualGeneratedDoorLeaf == null ||
                controller.ConnectionDriver.DoorAngleSource == null ||
                controller.ConnectionDriver.DoorAngleSource.DoorLeaf !=
                controller.ActualGeneratedDoorLeaf)
            {
                throw new InvalidOperationException(
                    "Final capture must drive the actual generated production door leaf.");
            }

            var context = new StartMapCaptureContext(controller, harness, bindings);
            context.Validate("final harness preflight");
            return context;
        }

        private static PolicyCaptureSet CaptureFinalPolicySet(
            StartMapCaptureContext context,
            DungeonPortalBakedBasisConnectionDriver driver,
            DungeonPortalBakedBasisDoorShDriver doorSh,
            DungeonPortalBakedBasisReflectionDriver reflection,
            DoorConfiguration door,
            float normalStartResponseScale,
            float normalAdministrativeResponseScale,
            RenderTargets targets,
            RenderFailureMonitor monitor,
            string outputFolder)
        {
            if (context.Policy != DungeonPortalBakedBasisEvidenceSpotPolicy.ORACLE_SPOT_OFF &&
                context.Policy != DungeonPortalBakedBasisEvidenceSpotPolicy.HUMAN_SPOT_ON)
            {
                throw new InvalidOperationException("Unsupported StartMap evidence spot policy.");
            }

            var captures = new List<CaptureRecord>(40);
            var baselineRecords = new List<CaptureRecord>(8);
            var baseOnlyProductionRecords = new List<CaptureRecord>(8);
            var baseOnlyPrivateRecords = new List<CaptureRecord>(8);
            var d0Metrics = new List<D0Metric>(8);
            for (int powerIndex = 0; powerIndex < PowerStates.Length; powerIndex++)
            {
                PowerState power = PowerStates[powerIndex];
                string statePrefix = context.Policy + " " + power.Id;
                context.Validate(statePrefix + " Adjacent-OFF D0 before state");
                SetDoorPose(context.Controller.ActualGeneratedDoorLeaf, driver.DoorAngleSource, door, 0f);
                bool adjacentDisabled = driver.SetAdjacentEnabledForEvidence(
                    false, out string disabledFailure);
                string baselineStateFailure = null;
                bool baselineApplied = adjacentDisabled && driver.SetImmediateForEvidence(
                    power.StartPower01, power.AdministrativePower01, 0f, out baselineStateFailure);
                if (!adjacentDisabled || !baselineApplied)
                {
                    throw new InvalidOperationException("Could not establish final Adjacent-OFF D0 baseline: " +
                                                        disabledFailure + " " + baselineStateFailure);
                }
                ApplyReflectionImmediate(reflection, power.StartPower01, power.AdministrativePower01,
                    statePrefix + " Adjacent-OFF D0");
                if (!doorSh.TryApplyNow(out string baselineDoorFailure))
                    throw new InvalidOperationException("D0 door SH restore failed: " + baselineDoorFailure);
                AssertD0State(driver, doorSh, reflection, power, statePrefix + " baseline");
                context.Validate(statePrefix + " Adjacent-OFF D0 after state");

                var baselinesForPower = new Dictionary<string, FramePayload>(StringComparer.Ordinal);
                for (int frameIndex = 0; frameIndex < 2; frameIndex++)
                {
                    FramePayload payload = CaptureFinalActualPlayerFrame(
                        context, frameIndex, power, DoorPoses[0], door.OpenAngleDegrees, driver, doorSh,
                        reflection, targets, monitor, outputFolder,
                        "AdjacentOff_" + power.ShortToken + "_D000_", true);
                    baselineRecords.Add(payload.Record);
                    baselinesForPower.Add(payload.Record.CameraToken, payload);
                }

                DoorPose baseOnlyPose = DoorPoses[DoorPoses.Length - 1];
                context.Validate(statePrefix + " base-only production before state");
                SetDoorPose(context.Controller.ActualGeneratedDoorLeaf, driver.DoorAngleSource, door,
                    baseOnlyPose.Fraction);
                if (!driver.SetImmediateForEvidence(power.StartPower01, power.AdministrativePower01, 0f,
                        out string baseProductionFailure))
                {
                    throw new InvalidOperationException(
                        "Could not establish base-only production endpoint: " + baseProductionFailure);
                }
                ApplyReflectionImmediate(reflection, power.StartPower01, power.AdministrativePower01,
                    statePrefix + " base-only production");
                if (!doorSh.TryApplyNow(out string baseProductionDoorFailure))
                    throw new InvalidOperationException(
                        "Base-only production door SH restore failed: " + baseProductionDoorFailure);
                AssertD0State(driver, doorSh, reflection, power, statePrefix + " base-only production");
                context.Validate(statePrefix + " base-only production after state");
                for (int frameIndex = 0; frameIndex < 2; frameIndex++)
                {
                    FramePayload payload = CaptureFinalActualPlayerFrame(
                        context, frameIndex, power, baseOnlyPose, door.OpenAngleDegrees, driver, doorSh,
                        reflection, targets, monitor, outputFolder, "BaseOnly_AdjacentOff_", false);
                    baseOnlyProductionRecords.Add(payload.Record);
                    payload.DisposePixels();
                }

                context.Validate(statePrefix + " base-only private before state");
                SetIncomingResponseScales(driver, 0f, 0f, statePrefix + " base-only zero response");
                if (!driver.SetAdjacentEnabledForEvidence(true, out string enabledFailure))
                    throw new InvalidOperationException("Could not re-enable DPBB transport: " + enabledFailure);
                float baseOnlyAperture = driver.DoorAngleSource.ApertureFraction;
                if (!driver.SetImmediateForEvidence(power.StartPower01, power.AdministrativePower01,
                        baseOnlyAperture, out string basePrivateFailure))
                {
                    throw new InvalidOperationException(
                        "Could not establish base-only private endpoint: " + basePrivateFailure);
                }
                ForceRecompose(driver, doorSh, reflection, power.StartPower01,
                    power.AdministrativePower01, statePrefix + " base-only private");
                AssertMatrixState(driver, doorSh, reflection, power, baseOnlyPose, baseOnlyAperture);
                context.Validate(statePrefix + " base-only private after state");
                for (int frameIndex = 0; frameIndex < 2; frameIndex++)
                {
                    FramePayload payload = CaptureFinalActualPlayerFrame(
                        context, frameIndex, power, baseOnlyPose, door.OpenAngleDegrees, driver, doorSh,
                        reflection, targets, monitor, outputFolder, "BaseOnly_ResponseZero_", false);
                    baseOnlyPrivateRecords.Add(payload.Record);
                    payload.DisposePixels();
                }
                SetIncomingResponseScales(driver, normalStartResponseScale,
                    normalAdministrativeResponseScale, statePrefix + " normal matrix restore");

                for (int poseIndex = 0; poseIndex < DoorPoses.Length; poseIndex++)
                {
                    DoorPose pose = DoorPoses[poseIndex];
                    context.Validate(statePrefix + " D" + pose.Percent + " before state");
                    SetDoorPose(context.Controller.ActualGeneratedDoorLeaf, driver.DoorAngleSource, door,
                        pose.Fraction);
                    float aperture = driver.DoorAngleSource.ApertureFraction;
                    if (!Approximately(aperture,
                            PortalTransportMath.ComputeProjectedApertureFraction(pose.Fraction), 0.0001f))
                    {
                        throw new InvalidOperationException("Physical door aperture mismatch at D" +
                                                            pose.Percent + ".");
                    }
                    if (!driver.SetImmediateForEvidence(power.StartPower01, power.AdministrativePower01,
                            aperture, out string immediateFailure))
                    {
                        throw new InvalidOperationException("DPBB immediate settle failed: " + immediateFailure);
                    }
                    ForceRecompose(driver, doorSh, reflection, power.StartPower01,
                        power.AdministrativePower01, statePrefix + " D" + pose.Percent);
                    AssertMatrixState(driver, doorSh, reflection, power, pose, aperture);
                    context.Validate(statePrefix + " D" + pose.Percent + " after state");

                    for (int frameIndex = 0; frameIndex < 2; frameIndex++)
                    {
                        FramePayload payload = CaptureFinalActualPlayerFrame(
                            context, frameIndex, power, pose, door.OpenAngleDegrees, driver, doorSh,
                            reflection, targets, monitor, outputFolder, string.Empty, pose.Percent == 0);
                        captures.Add(payload.Record);
                        if (pose.Percent == 0)
                        {
                            if (!baselinesForPower.TryGetValue(payload.Record.CameraToken,
                                    out FramePayload baseline))
                            {
                                throw new InvalidOperationException("Missing D0 baseline camera payload.");
                            }
                            D0Metric metric = CompareD0(baseline, payload);
                            d0Metrics.Add(metric);
                            payload.Record.D0Metric = metric;
                            baseline.DisposePixels();
                            payload.DisposePixels();
                        }
                        else
                        {
                            payload.DisposePixels();
                        }
                    }
                }
                foreach (KeyValuePair<string, FramePayload> pair in baselinesForPower)
                    pair.Value.DisposePixels();
            }

            if (captures.Count != 40 || baselineRecords.Count != 8 ||
                baseOnlyProductionRecords.Count != 8 || baseOnlyPrivateRecords.Count != 8 ||
                d0Metrics.Count != 8)
            {
                throw new InvalidOperationException("Unexpected final StartMap smoke record counts. Matrix=" +
                                                    captures.Count + " baseline=" + baselineRecords.Count +
                                                    " baseProduction=" + baseOnlyProductionRecords.Count +
                                                    " basePrivate=" + baseOnlyPrivateRecords.Count +
                                                    " d0=" + d0Metrics.Count + ".");
            }
            context.Validate(context.Policy + " policy-set after all states");
            return new PolicyCaptureSet(context.Policy, context.EnvironmentFingerprintSha,
                captures, baselineRecords, baseOnlyProductionRecords, baseOnlyPrivateRecords, d0Metrics);
        }

        private static FramePayload CaptureFinalActualPlayerFrame(
            StartMapCaptureContext context,
            int frameIndex,
            PowerState power,
            DoorPose pose,
            float fullOpenAngle,
            DungeonPortalBakedBasisConnectionDriver driver,
            DungeonPortalBakedBasisDoorShDriver doorSh,
            DungeonPortalBakedBasisReflectionDriver reflection,
            RenderTargets targets,
            RenderFailureMonitor monitor,
            string outputFolder,
            string prefix,
            bool retainPixels)
        {
            context.SetCaptureFrame(frameIndex, power.Id + " D" + pose.Percent + " frame=" + frameIndex);
            context.Validate(power.Id + " D" + pose.Percent + " frame=" + frameIndex + " before render");
            FramePayload payload = CaptureCamera(
                context.Controller.ActualPlayerCamera,
                SceneManager.GetActiveScene(),
                power,
                pose,
                fullOpenAngle,
                driver,
                doorSh,
                reflection,
                context.Bindings,
                targets,
                monitor,
                outputFolder,
                prefix,
                retainPixels,
                context);
            context.Validate(power.Id + " D" + pose.Percent + " frame=" + frameIndex + " after render");
            return payload;
        }

        private static void SetDoorPose(
            Transform doorLeaf,
            DungeonPortalDoorAngleSource source,
            DoorConfiguration configuration,
            float fraction)
        {
            doorLeaf.localRotation = configuration.RotationFor(fraction);
            Physics.SyncTransforms();
            source.EvaluateNow();
            if (!Approximately(source.OpenFraction, fraction, DoorTolerance))
            {
                throw new InvalidOperationException("Door open fraction did not settle to " +
                                                    fraction.ToString("R", CultureInfo.InvariantCulture) + ".");
            }
        }

        private static void ForceRecompose(
            DungeonPortalBakedBasisConnectionDriver driver,
            DungeonPortalBakedBasisDoorShDriver doorSh,
            DungeonPortalBakedBasisReflectionDriver reflection,
            float startPower01,
            float administrativePower01,
            string stage)
        {
            ApplyReflectionImmediate(
                reflection, startPower01, administrativePower01, stage);
            if (driver.StartRoomCompositor.IsActive &&
                !driver.StartRoomCompositor.TryRecomposeNow(out string startFailure))
            {
                throw new InvalidOperationException("Start recompose failed: " + startFailure);
            }
            if (driver.AdministrativeRoomCompositor.IsActive &&
                !driver.AdministrativeRoomCompositor.TryRecomposeNow(out string administrativeFailure))
            {
                throw new InvalidOperationException("Administrative recompose failed: " + administrativeFailure);
            }
            if (!doorSh.TryApplyNow(out string doorFailure))
                throw new InvalidOperationException("Door SH compose failed: " + doorFailure);
        }

        private static void ApplyReflectionImmediate(
            DungeonPortalBakedBasisReflectionDriver reflection,
            float startPower01,
            float administrativePower01,
            string stage)
        {
            if (reflection == null)
            {
                throw new InvalidOperationException(
                    "Reflection immediate settle failed at " + stage +
                    ": missing reflection driver.");
            }
            if (!reflection.TryApplyImmediateForEvidence(
                    startPower01, administrativePower01, out string failure) ||
                !reflection.IsInitialized || reflection.IsFaultLatched ||
                !Approximately(reflection.LastAppliedStartPower01, startPower01, 0.0001f) ||
                !Approximately(
                    reflection.LastAppliedAdministrativePower01,
                    administrativePower01,
                    0.0001f))
            {
                throw new InvalidOperationException(
                    "Reflection immediate settle failed at " + stage + ": " +
                    (failure ?? reflection.FaultReason ?? "reflection telemetry mismatch"));
            }
        }

        private static void SetIncomingResponseScales(
            DungeonPortalBakedBasisConnectionDriver driver,
            float startIncomingScale,
            float administrativeIncomingScale,
            string stage)
        {
            if (driver == null)
                throw new InvalidOperationException(
                    "Could not set incoming response scales at " + stage +
                    ": missing connection driver.");
            if (!driver.StartRoomCompositor.TrySetIncomingResponseScale(
                    DungeonPortalBakedBasisValidationContract.AdministrativeToStartConnectionId,
                    startIncomingScale,
                    out string startFailure))
            {
                throw new InvalidOperationException(
                    "Could not set Start incoming response scale at " + stage + ": " +
                    startFailure);
            }
            if (!driver.AdministrativeRoomCompositor.TrySetIncomingResponseScale(
                    DungeonPortalBakedBasisValidationContract.StartToAdministrativeConnectionId,
                    administrativeIncomingScale,
                    out string administrativeFailure))
            {
                throw new InvalidOperationException(
                    "Could not set Administrative incoming response scale at " + stage + ": " +
                    administrativeFailure);
            }
        }

        private static float RequireIncomingResponseScale(
            DungeonPortalBakedBasisRoomCompositor compositor,
            string connectionId)
        {
            if (compositor == null || string.IsNullOrWhiteSpace(connectionId))
                throw new InvalidOperationException("Incoming response-scale query is invalid.");
            DungeonPortalBakedBasisRoomCompositor.IncomingDoorState[] values =
                compositor.IncomingDoors;
            for (int i = 0; i < values.Length; i++)
            {
                DungeonPortalBakedBasisRoomCompositor.IncomingDoorState state = values[i];
                if (state != null && string.Equals(
                        state.ConnectionId, connectionId, StringComparison.Ordinal))
                {
                    return state.ResponseScale;
                }
            }
            throw new InvalidOperationException(
                "Incoming response scale is missing for '" + connectionId + "'.");
        }

        private static void AssertD0State(
            DungeonPortalBakedBasisConnectionDriver driver,
            DungeonPortalBakedBasisDoorShDriver doorSh,
            DungeonPortalBakedBasisReflectionDriver reflection,
            PowerState power,
            string stage)
        {
            if (driver.IsFaultLatched || doorSh.IsFaultLatched ||
                reflection == null || !reflection.IsInitialized || reflection.IsFaultLatched ||
                !Approximately(reflection.LastAppliedStartPower01, power.StartPower01, 0.0001f) ||
                !Approximately(reflection.LastAppliedAdministrativePower01,
                    power.AdministrativePower01, 0.0001f) ||
                driver.StartRoomCompositor.IsActive || driver.AdministrativeRoomCompositor.IsActive ||
                doorSh.IsActive || driver.CurrentAperture01 > 0.0001f)
            {
                throw new InvalidOperationException("D0 parity state failed at " + stage + ".");
            }
        }

        private static void AssertMatrixState(
            DungeonPortalBakedBasisConnectionDriver driver,
            DungeonPortalBakedBasisDoorShDriver doorSh,
            DungeonPortalBakedBasisReflectionDriver reflection,
            PowerState power,
            DoorPose pose,
            float aperture)
        {
            if (driver.IsFaultLatched || doorSh.IsFaultLatched || reflection == null ||
                !reflection.IsInitialized || reflection.IsFaultLatched ||
                !Approximately(reflection.LastAppliedStartPower01, power.StartPower01, 0.0001f) ||
                !Approximately(reflection.LastAppliedAdministrativePower01,
                    power.AdministrativePower01, 0.0001f) ||
                !Approximately(driver.CurrentStartPower01, power.StartPower01, 0.0001f) ||
                !Approximately(driver.CurrentAdministrativePower01,
                    power.AdministrativePower01, 0.0001f) ||
                !Approximately(driver.CurrentAperture01, aperture, 0.0001f))
            {
                throw new InvalidOperationException("DPBB runtime telemetry mismatch at " +
                                                    power.Id + " D" + pose.Percent + ".");
            }
            if (pose.Percent == 0)
            {
                AssertD0State(driver, doorSh, reflection, power, power.Id + " matrix D0");
                return;
            }
            if (!driver.StartRoomCompositor.IsActive ||
                !driver.AdministrativeRoomCompositor.IsActive || !doorSh.IsActive)
            {
                throw new InvalidOperationException("Open door did not activate both room compositors and door SH.");
            }
        }

        private static FramePayload CaptureCamera(
            Camera camera,
            Scene scene,
            PowerState power,
            DoorPose pose,
            float fullOpenAngle,
            DungeonPortalBakedBasisConnectionDriver driver,
            DungeonPortalBakedBasisDoorShDriver doorSh,
            DungeonPortalBakedBasisReflectionDriver reflection,
            KExactBasisV1EditorContract.SceneBindings bindings,
            RenderTargets targets,
            RenderFailureMonitor monitor,
            string outputFolder,
            string prefix,
            bool retainPixels,
            StartMapCaptureContext finalContext = null)
        {
            if (camera == null || camera.targetTexture != null || camera.gameObject.scene != scene)
            {
                throw new InvalidOperationException("Capture camera scene/target contract changed.");
            }
            int index;
            if (finalContext != null)
            {
                if (camera != finalContext.Controller.ActualPlayerCamera || !camera.enabled ||
                    camera == finalContext.Bindings.Cameras[0] || camera == finalContext.Bindings.Cameras[1])
                {
                    throw new InvalidOperationException(
                        "Final StartMap capture must render only the enabled ActualPlayerCamera.");
                }
                index = finalContext.Controller.CurrentFrameIndex;
                if (index < 0 || index > 1)
                    throw new InvalidOperationException("Final capture frame index is outside the two-frame contract.");
            }
            else
            {
                if (camera.enabled)
                    throw new InvalidOperationException("Fixed camera isolation contract changed.");
                index = camera.name == KExactBasisV1EditorContract.StartCameraName
                    ? 0
                    : camera.name == KExactBasisV1EditorContract.AdministrativeCameraName
                        ? 1
                        : throw new InvalidOperationException("Unexpected smoke camera.");
                KExactBasisV1EditorContract.ValidateFixedCamera(camera, index);
            }
            string cameraToken = index == 0 ? "S2A" : "A2S";
            if (!camera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
                throw new InvalidOperationException("Capture camera is missing UniversalAdditionalCameraData.");
            bool originalPost = cameraData.renderPostProcessing;
            if (finalContext != null)
                RequireFinalPresentationPostEnabled(originalPost, camera.name);
            RenderTexture originalActive = RenderTexture.active;
            RenderTexture originalTarget = camera.targetTexture;
            float originalAspect = camera.aspect;
            Matrix4x4 originalProjection = camera.projectionMatrix;
            bool originalSrgbWrite = GL.sRGBWrite;
            Texture2D pngReadback = null;
            Texture2D exrReadback = null;
            try
            {
                if (finalContext == null)
                {
                    camera.aspect = CaptureWidth / (float)CaptureHeight;
                    camera.ResetProjectionMatrix();
                    AssertStandardProjection(camera);
                }

                // The legacy fixed-camera route may opt into a diagnostic presentation render.
                // The final StartMap route must inherit an already-enabled production camera state;
                // it is forbidden to manufacture a parity-looking PNG by turning post on here.
                if (finalContext == null)
                    cameraData.renderPostProcessing = true;
                if (!cameraData.renderPostProcessing)
                    throw new InvalidOperationException("Presentation PNG requires post-processing enabled.");
                bool pngPostProcessingEnabled = cameraData.renderPostProcessing;
                SubmitStandardRequest(camera, targets.PresentationRender, monitor, "presentation");
                GL.sRGBWrite = targets.PresentationResolve.sRGB;
                Graphics.Blit(targets.PresentationRender, targets.PresentationResolve);
                RenderTexture.active = targets.PresentationResolve;
                pngReadback = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGBA32,
                    false, false) { hideFlags = HideFlags.HideAndDontSave };
                pngReadback.ReadPixels(new Rect(0f, 0f, CaptureWidth, CaptureHeight), 0, 0, false);
                pngReadback.Apply(false, false);
                byte[] png = ImageConversion.EncodeToPNG(pngReadback);
                VerifyNonEmpty(png, "PNG");

                cameraData.renderPostProcessing = false;
                if (cameraData.renderPostProcessing)
                    throw new InvalidOperationException("Linear HDR EXR requires post-processing disabled.");
                bool exrPostProcessingEnabled = cameraData.renderPostProcessing;
                SubmitStandardRequest(camera, targets.HdrRender, monitor, "linear HDR");
                GL.sRGBWrite = false;
                Graphics.Blit(targets.HdrRender, targets.HdrResolve);
                RenderTexture.active = targets.HdrResolve;
                exrReadback = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGBAHalf,
                    false, true) { hideFlags = HideFlags.HideAndDontSave };
                exrReadback.ReadPixels(new Rect(0f, 0f, CaptureWidth, CaptureHeight), 0, 0, false);
                exrReadback.Apply(false, false);
                Color[] linearPixels = exrReadback.GetPixels();
                ComputeLuminance(linearPixels, out double meanLuminance, out float maxLuminance);
                byte[] rawLinear = exrReadback.GetRawTextureData<byte>().ToArray();
                byte[] exr = ImageConversion.EncodeToEXR(exrReadback, Texture2D.EXRFlags.CompressZIP);
                VerifyNonEmpty(exr, "EXR");

                ArtifactEvidenceDescriptor pngEvidence = CreatePngEvidenceDescriptor(
                    finalContext != null,
                    finalContext != null
                        ? finalContext.Policy
                        : DungeonPortalBakedBasisEvidenceSpotPolicy.ORACLE_SPOT_OFF,
                    pngPostProcessingEnabled);
                ArtifactEvidenceDescriptor exrEvidence =
                    CreateExrEvidenceDescriptor(exrPostProcessingEnabled);

                cameraData.renderPostProcessing = originalPost;
                if (cameraData.renderPostProcessing != originalPost)
                    throw new InvalidOperationException("Capture camera post-processing did not restore after EXR.");

                string stem = prefix + power.ShortToken + "_D" +
                              pose.Percent.ToString("000", CultureInfo.InvariantCulture) + "_" + cameraToken;
                string pngPath = outputFolder + "/" + stem + ".png";
                string exrPath = outputFolder + "/" + stem + "_L.exr";
                File.WriteAllBytes(DungeonPortalBakedBasisValidationContract.AssetPathToAbsolutePath(pngPath), png);
                File.WriteAllBytes(DungeonPortalBakedBasisValidationContract.AssetPathToAbsolutePath(exrPath), exr);
                string stateContract = finalContext == null
                    ? BuildStateContract(bindings, driver, doorSh, reflection)
                    : BuildFinalStartMapStateContract(bindings, driver, doorSh, reflection, finalContext);
                string statePath = outputFolder + "/" + stem + ".state.txt";
                File.WriteAllText(DungeonPortalBakedBasisValidationContract.AssetPathToAbsolutePath(statePath),
                    stateContract, new UTF8Encoding(false));

                var record = new CaptureRecord(
                    power,
                    pose,
                    fullOpenAngle * pose.Fraction,
                    camera.name,
                    cameraToken,
                    Path.GetFileName(pngPath),
                    DungeonPortalBakedBasisValidationContract.ComputeSha256(png),
                    Path.GetFileName(exrPath),
                    DungeonPortalBakedBasisValidationContract.ComputeSha256(exr),
                    Path.GetFileName(statePath),
                    DungeonPortalBakedBasisValidationContract.ComputeSha256(
                        Encoding.UTF8.GetBytes(stateContract)),
                    meanLuminance,
                    maxLuminance,
                    driver.CurrentStartPower01,
                    driver.CurrentAdministrativePower01,
                    driver.CurrentAperture01,
                    driver.StartRoomCompositor.IsActive,
                    driver.AdministrativeRoomCompositor.IsActive,
                    doorSh.IsActive,
                    driver.IsFaultLatched,
                    doorSh.IsFaultLatched,
                    reflection.IsInitialized,
                    reflection.IsFaultLatched,
                    reflection.LastAppliedStartPower01,
                    reflection.LastAppliedAdministrativePower01,
                    RequireIncomingResponseScale(
                        driver.StartRoomCompositor,
                        DungeonPortalBakedBasisValidationContract.AdministrativeToStartConnectionId),
                    RequireIncomingResponseScale(
                        driver.AdministrativeRoomCompositor,
                        DungeonPortalBakedBasisValidationContract.StartToAdministrativeConnectionId),
                    finalContext != null ? finalContext.CreateCaptureMetadata() : null,
                    pngEvidence,
                    exrEvidence);
                return new FramePayload(record, retainPixels ? linearPixels : null,
                    retainPixels ? rawLinear : null);
            }
            finally
            {
                RenderTexture.active = originalActive;
                camera.targetTexture = originalTarget;
                camera.aspect = originalAspect;
                camera.projectionMatrix = originalProjection;
                cameraData.renderPostProcessing = originalPost;
                GL.sRGBWrite = originalSrgbWrite;
                if (pngReadback != null)
                    Object.DestroyImmediate(pngReadback);
                if (exrReadback != null)
                    Object.DestroyImmediate(exrReadback);
                if (cameraData.renderPostProcessing != originalPost)
                    throw new InvalidOperationException("Capture camera post-processing restore verification failed.");
            }
        }

        private static void RequireFinalPresentationPostEnabled(
            bool postProcessingEnabled,
            string cameraName)
        {
            if (!postProcessingEnabled)
            {
                throw new InvalidOperationException(
                    "Final StartMap capture refused because ActualPlayerCamera post-processing was originally disabled: " +
                    cameraName + ". The capture path will not force-enable it.");
            }
        }

        private static ArtifactEvidenceDescriptor CreatePngEvidenceDescriptor(
            bool finalStartMapRoute,
            DungeonPortalBakedBasisEvidenceSpotPolicy policy,
            bool postProcessingEnabled)
        {
            bool candidate = finalStartMapRoute && postProcessingEnabled &&
                             policy == DungeonPortalBakedBasisEvidenceSpotPolicy.HUMAN_SPOT_ON;
            return new ArtifactEvidenceDescriptor(
                candidate ? ProductionParityReviewCandidateRole : DiagnosticEvidenceRole,
                postProcessingEnabled,
                candidate);
        }

        private static ArtifactEvidenceDescriptor CreateExrEvidenceDescriptor(
            bool postProcessingEnabled)
        {
            if (postProcessingEnabled)
                throw new InvalidOperationException("Raw linear EXR evidence must be post-disabled diagnostic data.");
            return new ArtifactEvidenceDescriptor(DiagnosticEvidenceRole, false, false);
        }

        private static D0Metric CompareD0(FramePayload baseline, FramePayload active)
        {
            if (baseline.LinearPixels == null || active.LinearPixels == null ||
                baseline.RawLinear == null || active.RawLinear == null ||
                baseline.LinearPixels.Length != active.LinearPixels.Length)
            {
                throw new InvalidOperationException("D0 HDR payload is incomplete.");
            }
            bool exactRaw = EqualBytes(baseline.RawLinear, active.RawLinear);
            double sum = 0d;
            float maximum = 0f;
            for (int i = 0; i < baseline.LinearPixels.Length; i++)
            {
                Color left = baseline.LinearPixels[i];
                Color right = active.LinearPixels[i];
                float dr = Mathf.Abs(left.r - right.r);
                float dg = Mathf.Abs(left.g - right.g);
                float db = Mathf.Abs(left.b - right.b);
                maximum = Mathf.Max(maximum, dr, dg, db);
                sum += (dr + dg + db) / 3d;
            }
            return new D0Metric(active.Record.Power.Id, active.Record.CameraToken, exactRaw,
                sum / baseline.LinearPixels.Length, maximum);
        }

        private static string BuildStateContract(
            KExactBasisV1EditorContract.SceneBindings bindings,
            DungeonPortalBakedBasisConnectionDriver driver,
            DungeonPortalBakedBasisDoorShDriver doorSh,
            DungeonPortalBakedBasisReflectionDriver reflection)
        {
            Renderer[] production = bindings.ProductionRooms.GetComponentsInChildren<Renderer>(true);
            Renderer[] all = bindings.Root.GetComponentsInChildren<Renderer>(true);
            var canonical = new StringBuilder(production.Length * 220);
            var materialCanonical = new StringBuilder(production.Length * 180);
            LightmapData[] maps = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
            for (int i = 0; i < production.Length; i++)
            {
                Renderer renderer = production[i];
                string key = KExactBasisV1EditorContract.GetStableComponentKey(
                    bindings.ProductionRooms, renderer);
                LightmapData map = renderer.lightmapIndex >= 0 && renderer.lightmapIndex < maps.Length
                    ? maps[renderer.lightmapIndex]
                    : null;
                string materials = BuildMaterialArrayFingerprint(renderer.sharedMaterials);
                materialCanonical.Append(key).Append('|').Append(materials).Append('\n');
                canonical.Append(key).Append('|')
                    .Append("rendererType=").Append(renderer.GetType().FullName).Append('|')
                    .Append("lmIndex=").Append(renderer.lightmapIndex).Append('|')
                    .Append("lmST=").Append(KExactBasisV1EditorContract.FormatVector4(
                        renderer.lightmapScaleOffset)).Append('|')
                    .Append("lmColor=").Append(KExactBasisV1EditorContract.GetObjectIdentity(
                        map != null ? map.lightmapColor : null)).Append('|')
                    .Append("lmDir=").Append(KExactBasisV1EditorContract.GetObjectIdentity(
                        map != null ? map.lightmapDir : null)).Append('|')
                    .Append("shadowMask=").Append(KExactBasisV1EditorContract.GetObjectIdentity(
                        map != null ? map.shadowMask : null)).Append('|')
                    .Append("lightProbeUsage=").Append(renderer.lightProbeUsage).Append('|')
                    .Append("reflectionProbeUsage=").Append(renderer.reflectionProbeUsage).Append('|')
                    .Append("materials=").Append(materials).Append('|')
                    .Append("mpb=").Append(KExactBasisV1EditorContract.ComputeMaterialPropertyBlockFingerprint(
                        renderer)).Append('\n');
            }
            string fullContract = canonical.ToString();
            var builder = new StringBuilder(fullContract.Length + 1024);
            builder.AppendLine("schema=DPBBState/v1");
            builder.AppendLine("productionRendererCount=" + production.Length);
            builder.AppendLine("additionalRendererCount=" + (all.Length - production.Length));
            builder.AppendLine("rendererAggregateFingerprint=" +
                               KExactBasisV1EditorContract.ComputeRendererAggregateFingerprint(
                                   bindings.ProductionRooms));
            builder.AppendLine("rendererMaterialFingerprintSha256=" +
                               DungeonPortalBakedBasisValidationContract.ComputeSha256(
                                   Encoding.UTF8.GetBytes(materialCanonical.ToString())));
            builder.AppendLine("lightmapIndexStMpbReferenceSha256=" +
                               DungeonPortalBakedBasisValidationContract.ComputeSha256(
                                   Encoding.UTF8.GetBytes(fullContract)));
            builder.AppendLine("driverStartPower01=" + Format(driver.CurrentStartPower01));
            builder.AppendLine("driverAdministrativePower01=" + Format(
                driver.CurrentAdministrativePower01));
            builder.AppendLine("driverAperture01=" + Format(driver.CurrentAperture01));
            builder.AppendLine("startCompositorActive=" + driver.StartRoomCompositor.IsActive);
            builder.AppendLine("administrativeCompositorActive=" +
                               driver.AdministrativeRoomCompositor.IsActive);
            builder.AppendLine("startCompositorFault=" + driver.StartRoomCompositor.IsFaultLatched);
            builder.AppendLine("administrativeCompositorFault=" +
                               driver.AdministrativeRoomCompositor.IsFaultLatched);
            builder.AppendLine("driverFault=" + driver.IsFaultLatched);
            builder.AppendLine("driverFaultReason=" + Sanitize(driver.FaultReason));
            builder.AppendLine("doorShActive=" + doorSh.IsActive);
            builder.AppendLine("doorShFault=" + doorSh.IsFaultLatched);
            builder.AppendLine("doorShFaultReason=" + Sanitize(doorSh.FaultReason));
            builder.AppendLine("doorShBoundRendererCount=" + doorSh.BoundRendererCount);
            builder.AppendLine("reflectionInitialized=" + reflection.IsInitialized);
            builder.AppendLine("reflectionFault=" + reflection.IsFaultLatched);
            builder.AppendLine("reflectionFaultReason=" + Sanitize(reflection.FaultReason));
            builder.AppendLine("reflectionStartPower01=" + Format(
                reflection.LastAppliedStartPower01));
            builder.AppendLine("reflectionAdministrativePower01=" + Format(
                reflection.LastAppliedAdministrativePower01));
            builder.AppendLine("startIncomingResponseScale=" + Format(
                RequireIncomingResponseScale(
                    driver.StartRoomCompositor,
                    DungeonPortalBakedBasisValidationContract.AdministrativeToStartConnectionId)));
            builder.AppendLine("administrativeIncomingResponseScale=" + Format(
                RequireIncomingResponseScale(
                    driver.AdministrativeRoomCompositor,
                    DungeonPortalBakedBasisValidationContract.StartToAdministrativeConnectionId)));
            builder.AppendLine("rendererReferencesBegin");
            builder.Append(fullContract);
            builder.AppendLine("rendererReferencesEnd");
            return builder.ToString();
        }

        private static string BuildFinalStartMapStateContract(
            KExactBasisV1EditorContract.SceneBindings bindings,
            DungeonPortalBakedBasisConnectionDriver driver,
            DungeonPortalBakedBasisDoorShDriver doorSh,
            DungeonPortalBakedBasisReflectionDriver reflection,
            StartMapCaptureContext context)
        {
            var builder = new StringBuilder(BuildStateContract(bindings, driver, doorSh, reflection));
            builder.Append(BuildActualGeneratedSurfaceContract(driver));
            CaptureMetadata metadata = context.CreateCaptureMetadata();
            builder.AppendLine("stateEvidenceRole=diagnostic");
            builder.AppendLine("statePostProcessingEnabled=notApplicable");
            builder.AppendLine("visualVerdict=" + UnreviewedVisualVerdict);
            builder.AppendLine("finalEvidenceEligible=false");
            builder.AppendLine("captureRoute=" + metadata.CaptureRoute);
            builder.AppendLine("spotPolicy=" + metadata.SpotPolicy);
            builder.AppendLine("environmentFingerprintSha256=" + metadata.EnvironmentFingerprintSha256);
            builder.AppendLine("commonRendererMaterialFingerprintSha256=" +
                               context.CommonRendererMaterialFingerprintSha256);
            builder.AppendLine("commonRendererMaterialFingerprintScope=hidden-validation-support-only");
            builder.AppendLine("actualGeneratedSurfaceGate=controller-exact-allowlist+per-state-full-fingerprint");
            builder.AppendLine("featureDeltaAllowlist=DPBB_POWER,DPBB_DOOR,DPBB_REFLECTION");
            builder.AppendLine("activeScene=" + metadata.ActiveScenePath);
            builder.AppendLine("actualPlayerCamera=" + metadata.ActualCamera);
            builder.AppendLine("assignedDungeonVolume=" + metadata.AssignedDungeonVolume);
            builder.AppendLine("referenceFrameIndex=" + metadata.ReferenceFrameIndex);
            return builder.ToString();
        }

        private static string BuildActualGeneratedSurfaceContract(
            DungeonPortalBakedBasisConnectionDriver driver)
        {
            DungeonPortalBakedBasisRoomCompositor start = driver.StartRoomCompositor;
            DungeonPortalBakedBasisRoomCompositor administrative =
                driver.AdministrativeRoomCompositor;
            if (start == null || administrative == null || start.RoomRoot == null ||
                administrative.RoomRoot == null)
            {
                throw new InvalidOperationException(
                    "Actual generated room roots are missing from the StartMap compositor binding.");
            }

            Transform[] roots = { start.RoomRoot, administrative.RoomRoot };
            string[] prefixes = { "Start", "Administrative" };
            var rows = new List<string>();
            LightmapData[] maps = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Renderer[] renderers = roots[rootIndex].GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    string key = prefixes[rootIndex] + "/" +
                                 KExactBasisV1EditorContract.GetStableComponentKey(
                                     roots[rootIndex], renderer);
                    LightmapData map = renderer.lightmapIndex >= 0 &&
                                       renderer.lightmapIndex < maps.Length
                        ? maps[renderer.lightmapIndex]
                        : null;
                    rows.Add(key + "|type=" + renderer.GetType().FullName +
                             "|mesh=" + KExactBasisV1EditorContract.GetObjectIdentity(
                                 ResolveSurfaceMesh(renderer)) +
                             "|enabled=" + renderer.enabled +
                             "|forceOff=" + renderer.forceRenderingOff +
                             "|lmIndex=" + renderer.lightmapIndex +
                             "|lmST=" + KExactBasisV1EditorContract.FormatVector4(
                                 renderer.lightmapScaleOffset) +
                             "|lmColor=" + KExactBasisV1EditorContract.GetObjectIdentity(
                                 map != null ? map.lightmapColor : null) +
                             "|lmDir=" + KExactBasisV1EditorContract.GetObjectIdentity(
                                 map != null ? map.lightmapDir : null) +
                             "|shadowMask=" + KExactBasisV1EditorContract.GetObjectIdentity(
                                 map != null ? map.shadowMask : null) +
                             "|lightProbe=" + renderer.lightProbeUsage +
                             "|reflection=" + renderer.reflectionProbeUsage +
                             "|renderLayer=" + renderer.renderingLayerMask +
                             "|materials=" + BuildMaterialArrayFingerprint(renderer.sharedMaterials) +
                             "|mpb=" + KExactBasisV1EditorContract.ComputeMaterialPropertyBlockFingerprint(
                                 renderer));
                }
            }
            rows.Sort(StringComparer.Ordinal);
            int boundCount = start.ExplicitRenderers.Length + administrative.ExplicitRenderers.Length;
            var builder = new StringBuilder(rows.Count * 220);
            builder.AppendLine("actualGeneratedSurfaceSchema=DPBBActualGeneratedSurface/v1");
            builder.AppendLine("actualGeneratedRendererCount=" + rows.Count);
            builder.AppendLine("actualGeneratedCanonicalBoundRendererCount=" + boundCount);
            builder.AppendLine("actualGeneratedAdditionalRendererCount=" + (rows.Count - boundCount));
            builder.AppendLine("actualGeneratedSurfaceFingerprintSha256=" +
                               DungeonPortalBakedBasisValidationContract.ComputeSha256(
                                   Encoding.UTF8.GetBytes(string.Join("\n", rows))));
            builder.AppendLine("actualGeneratedRendererRowsBegin");
            for (int i = 0; i < rows.Count; i++) builder.AppendLine(rows[i]);
            builder.AppendLine("actualGeneratedRendererRowsEnd");
            return builder.ToString();
        }

        private static string BuildMaterialArrayFingerprint(Material[] materials)
        {
            Material[] values = materials ?? Array.Empty<Material>();
            var builder = new StringBuilder(values.Length * 160);
            builder.Append("count=").Append(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                Material material = values[i];
                builder.Append(";slot=").Append(i).Append(";material=")
                    .Append(KExactBasisV1EditorContract.GetObjectIdentity(material));
                if (material == null)
                    continue;

                string[] keywords = material.shaderKeywords ?? Array.Empty<string>();
                keywords = (string[])keywords.Clone();
                Array.Sort(keywords, StringComparer.Ordinal);
                builder.Append(";shader=")
                    .Append(KExactBasisV1EditorContract.GetObjectIdentity(material.shader))
                    .Append(";queue=").Append(material.renderQueue)
                    .Append(";keywords=").Append(string.Join(",", keywords));
            }
            return DungeonPortalBakedBasisValidationContract.ComputeSha256(
                Encoding.UTF8.GetBytes(builder.ToString()));
        }

        private static string BuildManifest(
            string utc,
            string sceneSha,
            List<CaptureRecord> captures,
            List<CaptureRecord> baselines,
            List<CaptureRecord> baseOnlyProduction,
            List<CaptureRecord> baseOnlyPrivate,
            List<D0Metric> d0Metrics,
            string protectedAssetsFingerprint)
        {
            var builder = new StringBuilder(64 * 1024);
            builder.AppendLine("schema=DPBBSmoke/v2");
            builder.AppendLine("status=" + Status);
            builder.AppendLine("capturedUtc=" + utc);
            builder.AppendLine("visualVerdict=" + UnreviewedVisualVerdict);
            builder.AppendLine("finalEvidenceEligible=false");
            builder.AppendLine("rawCaptureFinalEvidenceEligible=false");
            builder.AppendLine("artifactRolePolicy=isolatedLegacyPNG:diagnostic;allEXR:diagnostic;allState:diagnostic");
            builder.AppendLine("visualParityClaimed=false");
            builder.AppendLine("builtScenePath=" + DungeonPortalBakedBasisValidationContract.BuiltScenePath);
            builder.AppendLine("builtSceneSha256=" + sceneSha);
            builder.AppendLine("captureResolution=" + CaptureWidth + "x" + CaptureHeight);
            builder.AppendLine("matrix=4 power states x 5 physical door poses x 2 fixed cameras");
            builder.AppendLine("stateCameraRecordCount=" + captures.Count);
            builder.AppendLine("adjacentOffD0RecordCount=" + baselines.Count);
            builder.AppendLine("baseOnlyProductionRecordCount=" + baseOnlyProduction.Count);
            builder.AppendLine("baseOnlyPrivateRecordCount=" + baseOnlyPrivate.Count);
            builder.AppendLine(
                "baseOnlyScope=static-lightmap-with-common-DPBB-reflection;pose=D100;" +
                "doorPixelsExcludedFromMetric=true;numericComparison=EXTERNAL_REQUIRED");
            builder.AppendLine("d0MetricCount=" + d0Metrics.Count);
            bool d0RawExactAll = true;
            bool d0WithinEpsilonAll = true;
            for (int i = 0; i < d0Metrics.Count; i++)
            {
                d0RawExactAll &= d0Metrics[i].ExactRaw;
                d0WithinEpsilonAll &= d0Metrics[i].MaxAbsolute <= D0LeakageEpsilon;
            }
            builder.AppendLine("d0RawExactAll=" + d0RawExactAll);
            builder.AppendLine("d0LeakageEpsilon=" + Format(D0LeakageEpsilon));
            builder.AppendLine("d0WithinEpsilonAll=" + d0WithinEpsilonAll);
            builder.AppendLine("transitionTrace=deterministic-door-pose-sweep;aperture-monotonic=true;" +
                               "HDR-response-monotonic-not-claimed");
            builder.AppendLine("partialPoseManualReview=REQUIRED;poses=D025,D050,D075;" +
                               "surfaces=doorframe-both-directions,oblique-floor,wall,pillar,props;" +
                               "automaticNaturalLightingPass=false");
            builder.AppendLine("protectedProductionFingerprintSha256=" + protectedAssetsFingerprint);
            AppendScreenMeanTransitionTraces(builder, captures);
            builder.AppendLine("recordsBegin");
            for (int i = 0; i < captures.Count; i++)
                captures[i].AppendManifest(builder, "matrix");
            for (int i = 0; i < baselines.Count; i++)
                baselines[i].AppendManifest(builder, "adjacentOff");
            for (int i = 0; i < baseOnlyProduction.Count; i++)
                baseOnlyProduction[i].AppendManifest(builder, "baseOnlyProduction");
            for (int i = 0; i < baseOnlyPrivate.Count; i++)
                baseOnlyPrivate[i].AppendManifest(builder, "baseOnlyPrivate");
            builder.AppendLine("recordsEnd");
            builder.AppendLine("d0ParityBegin");
            for (int i = 0; i < d0Metrics.Count; i++)
                d0Metrics[i].AppendManifest(builder);
            builder.AppendLine("d0ParityEnd");
            return builder.ToString();
        }

        private static string BuildFinalStartMapManifest(
            string utc,
            string harnessSceneSha,
            string startMapSceneSha,
            StartMapCaptureContext context,
            List<PolicyCaptureSet> policySets,
            string protectedAssetsFingerprint)
        {
            if (policySets == null || policySets.Count != 2)
                throw new InvalidOperationException("Final StartMap manifest requires both spot-policy evidence sets.");
            var builder = new StringBuilder(128 * 1024);
            builder.AppendLine("schema=DPBBStartMapSmoke/v1");
            builder.AppendLine("status=" + FinalStatus);
            builder.AppendLine("capturedUtc=" + utc);
            builder.AppendLine("captureRoute=" + FinalCaptureRoute);
            builder.AppendLine("visualVerdict=" + UnreviewedVisualVerdict);
            builder.AppendLine("finalEvidenceEligible=false");
            builder.AppendLine("rawCaptureFinalEvidenceEligible=false");
            builder.AppendLine("humanVisualReviewRequired=true");
            builder.AppendLine("captureSuccessDoesNotImplyVisualAcceptance=true");
            builder.AppendLine(
                "productionParityReviewCandidateRule=HUMAN_SPOT_ON+PNG+postProcessingEnabled:true");
            builder.AppendLine(
                "diagnosticArtifactRule=ORACLE_SPOT_OFF_PNG+all_postDisabled_EXR+all_state");
            builder.AppendLine("visualParityClaimed=false");
            builder.AppendLine("actualStartMapPath=" +
                               DungeonPortalBakedBasisStartMapRuntimeController.StartMapScenePath);
            builder.AppendLine("actualStartMapSha256=" + startMapSceneSha);
            builder.AppendLine("harnessScenePath=" + context.HarnessScene.path);
            builder.AppendLine("harnessSceneSha256=" + harnessSceneSha);
            builder.AppendLine("activeSceneDuringCapture=" +
                               DungeonPortalBakedBasisStartMapRuntimeController.StartMapScenePath);
            builder.AppendLine("captureCamera=ActualPlayerCamera;fixedReferenceCamerasRender=false");
            builder.AppendLine("captureResolution=" + CaptureWidth + "x" + CaptureHeight);
            builder.AppendLine("matrixPerPolicy=4 power states x 5 physical door poses x 2 actual-player frames");
            builder.AppendLine("policySetCount=" + policySets.Count);
            builder.AppendLine("featureDeltaAllowlist=DPBB_POWER,DPBB_DOOR,DPBB_REFLECTION");
            builder.AppendLine("commonRendererMaterialFingerprintSha256=" +
                               context.CommonRendererMaterialFingerprintSha256);
            builder.AppendLine("commonFingerprintDrift=FAIL_CLOSED");
            builder.AppendLine("baseOnlyScope=static-lightmap-with-common-DPBB-reflection;pose=D100;" +
                               "doorPixelsExcludedFromMetric=true;numericComparison=EXTERNAL_REQUIRED");
            builder.AppendLine("partialPoseManualReview=REQUIRED;poses=D025,D050,D075;" +
                               "surfaces=doorframe-both-directions,oblique-floor,wall,pillar,props;" +
                               "automaticNaturalLightingPass=false");
            builder.AppendLine("isolatedLegacyEvidenceEligible=false");
            builder.AppendLine("protectedProductionFingerprintSha256=" + protectedAssetsFingerprint);
            builder.AppendLine("policySetsBegin");
            int totalRecords = 0;
            for (int i = 0; i < policySets.Count; i++)
            {
                PolicyCaptureSet set = policySets[i];
                if (set.Captures.Count != 40 || set.Baselines.Count != 8 ||
                    set.BaseOnlyProduction.Count != 8 || set.BaseOnlyPrivate.Count != 8 ||
                    set.D0Metrics.Count != 8)
                {
                    throw new InvalidOperationException("Final policy evidence-set count contract failed: " +
                                                        set.Policy + ".");
                }
                bool d0RawExactAll = true;
                bool d0WithinEpsilonAll = true;
                for (int metricIndex = 0; metricIndex < set.D0Metrics.Count; metricIndex++)
                {
                    d0RawExactAll &= set.D0Metrics[metricIndex].ExactRaw;
                    d0WithinEpsilonAll &= set.D0Metrics[metricIndex].MaxAbsolute <= D0LeakageEpsilon;
                }
                builder.AppendLine("policy=" + set.Policy + "|environmentFingerprintSha256=" +
                                   set.EnvironmentFingerprintSha256 + "|matrixRecords=" +
                                   set.Captures.Count + "|adjacentOffD0Records=" + set.Baselines.Count +
                                   "|baseOnlyProductionRecords=" + set.BaseOnlyProduction.Count +
                                   "|baseOnlyPrivateRecords=" + set.BaseOnlyPrivate.Count +
                                   "|d0ExactRawAll=" + d0RawExactAll + "|d0WithinEpsilonAll=" +
                                   d0WithinEpsilonAll + "|humanAssignedDungeonOnlyLightOn=" +
                                   (set.Policy == DungeonPortalBakedBasisEvidenceSpotPolicy.HUMAN_SPOT_ON));
                AppendScreenMeanTransitionTraces(builder, set.Captures);
                totalRecords += set.Captures.Count + set.Baselines.Count +
                                set.BaseOnlyProduction.Count + set.BaseOnlyPrivate.Count;
            }
            builder.AppendLine("policySetsEnd");
            builder.AppendLine("stateCameraRecordCount=" + totalRecords);
            builder.AppendLine("recordsBegin");
            for (int i = 0; i < policySets.Count; i++)
            {
                PolicyCaptureSet set = policySets[i];
                string categoryPrefix = set.Policy.ToString();
                for (int recordIndex = 0; recordIndex < set.Captures.Count; recordIndex++)
                    set.Captures[recordIndex].AppendManifest(builder, categoryPrefix + "/matrix");
                for (int recordIndex = 0; recordIndex < set.Baselines.Count; recordIndex++)
                    set.Baselines[recordIndex].AppendManifest(builder, categoryPrefix + "/adjacentOff");
                for (int recordIndex = 0; recordIndex < set.BaseOnlyProduction.Count; recordIndex++)
                    set.BaseOnlyProduction[recordIndex].AppendManifest(builder,
                        categoryPrefix + "/baseOnlyProduction");
                for (int recordIndex = 0; recordIndex < set.BaseOnlyPrivate.Count; recordIndex++)
                    set.BaseOnlyPrivate[recordIndex].AppendManifest(builder,
                        categoryPrefix + "/baseOnlyPrivate");
            }
            builder.AppendLine("recordsEnd");
            builder.AppendLine("d0ParityBegin");
            for (int i = 0; i < policySets.Count; i++)
            for (int metricIndex = 0; metricIndex < policySets[i].D0Metrics.Count; metricIndex++)
                policySets[i].D0Metrics[metricIndex].AppendManifest(builder);
            builder.AppendLine("d0ParityEnd");
            return builder.ToString();
        }

        private static void AppendScreenMeanTransitionTraces(
            StringBuilder builder,
            List<CaptureRecord> captures)
        {
            builder.AppendLine("screenMeanTransitionTraceBegin");
            for (int powerIndex = 0; powerIndex < PowerStates.Length; powerIndex++)
            {
                PowerState power = PowerStates[powerIndex];
                for (int cameraIndex = 0; cameraIndex < 2; cameraIndex++)
                {
                    string cameraToken = cameraIndex == 0 ? "S2A" : "A2S";
                    CaptureRecord previous = null;
                    bool nondecreasing = true;
                    builder.Append("screenMeanTransition=").Append(power.Id).Append('|')
                        .Append(cameraToken);
                    for (int poseIndex = 0; poseIndex < DoorPoses.Length; poseIndex++)
                    {
                        DoorPose pose = DoorPoses[poseIndex];
                        CaptureRecord current = FindRecord(captures, power.Id, pose.Percent,
                            cameraToken);
                        if (current == null)
                            throw new InvalidOperationException(
                                "Transition trace is missing " + power.Id + " D" +
                                pose.Percent + " " + cameraToken + ".");
                        if (previous != null && current.MeanLuminance + 0.000000001d <
                            previous.MeanLuminance)
                        {
                            nondecreasing = false;
                        }
                        builder.Append('|').Append("D").Append(pose.Percent.ToString(
                            "000", CultureInfo.InvariantCulture)).Append('=').Append(
                            current.MeanLuminance.ToString("R", CultureInfo.InvariantCulture));
                        previous = current;
                    }
                    builder.Append("|screenMeanNondecreasing=").Append(nondecreasing)
                        .Append("|notVisualPassCriterion=true").AppendLine();
                }
            }
            builder.AppendLine("screenMeanTransitionTraceEnd");
        }

        private static CaptureRecord FindRecord(
            List<CaptureRecord> captures,
            string powerId,
            int posePercent,
            string cameraToken)
        {
            for (int i = 0; i < captures.Count; i++)
            {
                CaptureRecord candidate = captures[i];
                if (string.Equals(candidate.Power.Id, powerId, StringComparison.Ordinal) &&
                    candidate.Pose.Percent == posePercent &&
                    string.Equals(candidate.CameraToken, cameraToken, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static void SubmitStandardRequest(
            Camera camera,
            RenderTexture destination,
            RenderFailureMonitor monitor,
            string stage)
        {
            if (!(RenderPipelineManager.currentPipeline is UniversalRenderPipeline) ||
                camera.targetTexture != null || destination == null || !destination.IsCreated())
            {
                throw new InvalidOperationException("URP StandardRequest precondition failed.");
            }
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
            monitor.ThrowIfFailed(stage);
            if (camera.targetTexture != null)
                throw new InvalidOperationException("StandardRequest changed camera target texture.");
        }

        private static void AssertStandardProjection(Camera camera)
        {
            Matrix4x4 expected = Matrix4x4.Perspective(camera.fieldOfView, camera.aspect,
                camera.nearClipPlane, camera.farClipPlane);
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
            {
                if (Mathf.Abs(camera.projectionMatrix[row, column] -
                              expected[row, column]) > 0.00001f)
                {
                    throw new InvalidOperationException("Fixed camera projection is not standard.");
                }
            }
        }

        private static void ComputeLuminance(Color[] pixels, out double mean, out float maximum)
        {
            if (pixels == null || pixels.Length != CaptureWidth * CaptureHeight)
                throw new InvalidOperationException("HDR readback dimensions are invalid.");
            mean = 0d;
            maximum = 0f;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color value = pixels[i];
                if (!float.IsFinite(value.r) || !float.IsFinite(value.g) || !float.IsFinite(value.b))
                    throw new InvalidOperationException("HDR readback has non-finite pixels.");
                float luminance = Mathf.Max(0f, value.r * 0.2126f + value.g * 0.7152f +
                                                    value.b * 0.0722f);
                mean += luminance;
                maximum = Mathf.Max(maximum, luminance);
            }
            mean /= pixels.Length;
        }

        private static bool EqualBytes(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }
            return true;
        }

        private static void VerifyNonEmpty(byte[] bytes, string label)
        {
            if (bytes == null || bytes.Length < 16)
                throw new InvalidOperationException(label + " encode produced no usable bytes.");
        }

        private static bool Approximately(float left, float right, float tolerance)
        {
            return Mathf.Abs(left - right) <= tolerance;
        }

        private static string Format(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Sanitize(string value)
        {
            return (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
        }

        private readonly struct PowerState
        {
            internal readonly string Id;
            internal readonly string ShortToken;
            internal readonly float StartPower01;
            internal readonly float AdministrativePower01;

            internal PowerState(string id, string shortToken, float startPower01,
                float administrativePower01)
            {
                Id = id;
                ShortToken = shortToken;
                StartPower01 = startPower01;
                AdministrativePower01 = administrativePower01;
            }
        }

        private readonly struct DoorPose
        {
            internal readonly int Percent;
            internal float Fraction => Percent / 100f;

            internal DoorPose(int percent)
            {
                Percent = percent;
            }
        }

        private sealed class DoorConfiguration
        {
            private readonly Quaternion closedRotation;
            private readonly Vector3 hingeAxis;
            internal readonly float OpenAngleDegrees;

            private DoorConfiguration(Quaternion closed, Vector3 axis, float openAngle)
            {
                closedRotation = closed;
                hingeAxis = axis;
                OpenAngleDegrees = openAngle;
            }

            internal static DoorConfiguration Capture(DungeonPortalDoorAngleSource source)
            {
                if (source == null || !source.IsConfigured)
                    throw new InvalidOperationException("Live door-angle source is not configured.");
                SerializedObject serialized = new SerializedObject(source);
                serialized.UpdateIfRequiredOrScript();
                SerializedProperty closed = serialized.FindProperty("closedLocalRotation");
                SerializedProperty axis = serialized.FindProperty("localHingeAxis");
                SerializedProperty angle = serialized.FindProperty("openAngleDegrees");
                if (closed == null || axis == null || angle == null ||
                    axis.vector3Value.sqrMagnitude <= Mathf.Epsilon ||
                    Mathf.Abs(angle.floatValue - 90f) > 0.0001f)
                {
                    throw new InvalidOperationException("Door must use the canonical 90-degree live angle source.");
                }
                return new DoorConfiguration(closed.quaternionValue, axis.vector3Value.normalized,
                    angle.floatValue);
            }

            internal Quaternion RotationFor(float fraction)
            {
                return closedRotation * Quaternion.AngleAxis(
                    OpenAngleDegrees * Mathf.Clamp01(fraction), hingeAxis);
            }
        }

        private sealed class RuntimeSnapshot
        {
            private readonly DungeonTileLightmapSwitcher.PowerLevel startPower;
            private readonly DungeonTileLightmapSwitcher.PowerLevel administrativePower;
            private readonly float startTarget;
            private readonly float administrativeTarget;
            private readonly float startCurrent;
            private readonly float administrativeCurrent;
            private readonly float apertureCurrent;
            private readonly bool adjacentEnabled;
            private readonly bool startCompositorActive;
            private readonly bool administrativeCompositorActive;
            private readonly bool doorShActive;
            private readonly Quaternion doorRotation;
            private readonly float startIncomingResponseScale;
            private readonly float administrativeIncomingResponseScale;
            private readonly bool reflectionInitialized;
            private readonly float reflectionStartPower;
            private readonly float reflectionAdministrativePower;

            internal float StartIncomingResponseScale => startIncomingResponseScale;
            internal float AdministrativeIncomingResponseScale =>
                administrativeIncomingResponseScale;

            private RuntimeSnapshot(
                DungeonTileLightmapSwitcher.PowerLevel startPower,
                DungeonTileLightmapSwitcher.PowerLevel administrativePower,
                float startTarget,
                float administrativeTarget,
                float startCurrent,
                float administrativeCurrent,
                float apertureCurrent,
                bool adjacentEnabled,
                bool startCompositorActive,
                bool administrativeCompositorActive,
                bool doorShActive,
                Quaternion doorRotation,
                float startResponseScale,
                float administrativeResponseScale,
                bool capturedReflectionInitialized,
                float capturedReflectionStartPower,
                float capturedReflectionAdministrativePower)
            {
                this.startPower = startPower;
                this.administrativePower = administrativePower;
                this.startTarget = startTarget;
                this.administrativeTarget = administrativeTarget;
                this.startCurrent = startCurrent;
                this.administrativeCurrent = administrativeCurrent;
                this.apertureCurrent = apertureCurrent;
                this.adjacentEnabled = adjacentEnabled;
                this.startCompositorActive = startCompositorActive;
                this.administrativeCompositorActive = administrativeCompositorActive;
                this.doorShActive = doorShActive;
                this.doorRotation = doorRotation;
                startIncomingResponseScale = startResponseScale;
                administrativeIncomingResponseScale = administrativeResponseScale;
                reflectionInitialized = capturedReflectionInitialized;
                reflectionStartPower = capturedReflectionStartPower;
                reflectionAdministrativePower = capturedReflectionAdministrativePower;
            }

            internal static RuntimeSnapshot Capture(
                KExactBasisV1EditorContract.SceneBindings bindings,
                DungeonPortalBakedBasisConnectionDriver driver,
                DungeonPortalBakedBasisDoorShDriver doorSh,
                DungeonPortalBakedBasisReflectionDriver reflection)
            {
                if (driver.IsEvidenceOverrideActive)
                {
                    throw new InvalidOperationException(
                        "Capture refuses an existing evidence override; clear it and let the live state settle first.");
                }
                if (!DungeonPortalBakedBasisConnectionDriver.TryGetEndpoint(
                        driver.StartPower01, out _) ||
                    !DungeonPortalBakedBasisConnectionDriver.TryGetEndpoint(
                        driver.AdministrativePower01, out _) ||
                    !DungeonPortalBakedBasisConnectionDriver.TryGetEndpoint(
                        driver.CurrentStartPower01, out _) ||
                    !DungeonPortalBakedBasisConnectionDriver.TryGetEndpoint(
                        driver.CurrentAdministrativePower01, out _))
                {
                    throw new InvalidOperationException(
                        "Capture requires settled P0/P100 target and current power endpoints.");
                }
                float expectedAperture = driver.AdjacentTransportEnabled
                    ? driver.DoorAngleSource.ApertureFraction
                    : 0f;
                if (!Approximately(driver.CurrentAperture01, expectedAperture, 0.0001f))
                {
                    throw new InvalidOperationException(
                        "Capture requires a settled physical door aperture; do not start during a transition.");
                }
                Transform liveDoorLeaf = driver.DoorAngleSource != null
                    ? driver.DoorAngleSource.DoorLeaf
                    : null;
                if (liveDoorLeaf == null)
                    throw new InvalidOperationException("Capture has no live door leaf bound to its angle source.");
                return new RuntimeSnapshot(
                    bindings.StartSwitcher.CurrentPowerLevel,
                    bindings.AdministrativeSwitcher.CurrentPowerLevel,
                    driver.StartPower01,
                    driver.AdministrativePower01,
                    driver.CurrentStartPower01,
                    driver.CurrentAdministrativePower01,
                    driver.CurrentAperture01,
                    driver.AdjacentTransportEnabled,
                    driver.StartRoomCompositor.IsActive,
                    driver.AdministrativeRoomCompositor.IsActive,
                    doorSh.IsActive,
                    liveDoorLeaf.localRotation,
                    RequireIncomingResponseScale(
                        driver.StartRoomCompositor,
                        DungeonPortalBakedBasisValidationContract.AdministrativeToStartConnectionId),
                    RequireIncomingResponseScale(
                        driver.AdministrativeRoomCompositor,
                        DungeonPortalBakedBasisValidationContract.StartToAdministrativeConnectionId),
                    reflection.IsInitialized,
                    reflection.LastAppliedStartPower01,
                    reflection.LastAppliedAdministrativePower01);
            }

            internal void Restore(
                KExactBasisV1EditorContract.SceneBindings bindings,
                DungeonPortalBakedBasisConnectionDriver driver,
                DungeonPortalBakedBasisDoorShDriver doorSh,
                DungeonPortalBakedBasisReflectionDriver reflection)
            {
                Transform liveDoorLeaf = driver.DoorAngleSource != null
                    ? driver.DoorAngleSource.DoorLeaf
                    : null;
                if (liveDoorLeaf == null)
                    throw new InvalidOperationException("Snapshot restore has no live door leaf.");
                liveDoorLeaf.localRotation = doorRotation;
                Physics.SyncTransforms();
                driver.DoorAngleSource.EvaluateNow();
                SetIncomingResponseScales(
                    driver,
                    startIncomingResponseScale,
                    administrativeIncomingResponseScale,
                    "snapshot restore");
                bool adjacentRestored = driver.SetAdjacentEnabledForEvidence(
                    adjacentEnabled, out string adjacentFailure);
                string immediateFailure = null;
                bool immediateRestored = adjacentRestored && driver.SetImmediateForEvidence(
                    startTarget,
                    administrativeTarget,
                    adjacentEnabled ? apertureCurrent : 0f,
                    out immediateFailure);
                if (!adjacentRestored || !immediateRestored)
                {
                    throw new InvalidOperationException("Could not restore DPBB runtime state: " +
                                                        adjacentFailure + " " + immediateFailure);
                }
                ApplyReflectionImmediate(
                    reflection,
                    reflectionStartPower,
                    reflectionAdministrativePower,
                    "snapshot restore");
                driver.ClearEvidenceOverride();
                if (!doorSh.TryApplyNow(out string doorFailure))
                    throw new InvalidOperationException("Could not restore door SH state: " + doorFailure);
                if (bindings.StartSwitcher.CurrentPowerLevel != startPower ||
                    bindings.AdministrativeSwitcher.CurrentPowerLevel != administrativePower)
                {
                    throw new InvalidOperationException("Production switcher endpoint did not restore.");
                }
            }

            internal void AssertRestored(
                KExactBasisV1EditorContract.SceneBindings bindings,
                DungeonPortalBakedBasisConnectionDriver driver,
                DungeonPortalBakedBasisDoorShDriver doorSh,
                DungeonPortalBakedBasisReflectionDriver reflection)
            {
                Transform liveDoorLeaf = driver.DoorAngleSource != null
                    ? driver.DoorAngleSource.DoorLeaf
                    : null;
                if (liveDoorLeaf == null ||
                    Quaternion.Angle(liveDoorLeaf.localRotation, doorRotation) > 0.001f ||
                    bindings.StartSwitcher.CurrentPowerLevel != startPower ||
                    bindings.AdministrativeSwitcher.CurrentPowerLevel != administrativePower ||
                    !Approximately(driver.StartPower01, startTarget, 0.0001f) ||
                    !Approximately(driver.AdministrativePower01, administrativeTarget, 0.0001f) ||
                    !Approximately(driver.CurrentStartPower01, startCurrent, 0.0001f) ||
                    !Approximately(driver.CurrentAdministrativePower01,
                        administrativeCurrent, 0.0001f) ||
                    !Approximately(driver.CurrentAperture01, apertureCurrent, 0.0001f) ||
                    driver.AdjacentTransportEnabled != adjacentEnabled ||
                    driver.IsEvidenceOverrideActive ||
                    driver.StartRoomCompositor.IsActive != startCompositorActive ||
                    driver.AdministrativeRoomCompositor.IsActive != administrativeCompositorActive ||
                    doorSh.IsActive != doorShActive ||
                    !Approximately(
                        RequireIncomingResponseScale(
                            driver.StartRoomCompositor,
                            DungeonPortalBakedBasisValidationContract.AdministrativeToStartConnectionId),
                        startIncomingResponseScale,
                        0.0001f) ||
                    !Approximately(
                        RequireIncomingResponseScale(
                            driver.AdministrativeRoomCompositor,
                            DungeonPortalBakedBasisValidationContract.StartToAdministrativeConnectionId),
                        administrativeIncomingResponseScale,
                        0.0001f) ||
                    reflection.IsInitialized != reflectionInitialized ||
                    reflection.IsFaultLatched ||
                    !Approximately(reflection.LastAppliedStartPower01,
                        reflectionStartPower, 0.0001f) ||
                    !Approximately(reflection.LastAppliedAdministrativePower01,
                        reflectionAdministrativePower, 0.0001f) ||
                    driver.IsFaultLatched || doorSh.IsFaultLatched)
                {
                    throw new InvalidOperationException("DPBB runtime snapshot did not restore exactly.");
                }
            }
        }

        private sealed class StartMapCaptureContext
        {
            internal readonly DungeonPortalBakedBasisStartMapRuntimeController Controller;
            internal readonly Scene HarnessScene;
            internal readonly KExactBasisV1EditorContract.SceneBindings Bindings;
            private readonly DungeonPortalBakedBasisEvidenceSpotPolicy initialPolicy;
            private readonly int initialFrameIndex;
            private readonly CommonProductionSurfaceInvariant commonInvariant;

            internal DungeonPortalBakedBasisEvidenceSpotPolicy Policy { get; private set; }
            internal string EnvironmentFingerprintSha { get; private set; }
            internal string CommonRendererMaterialFingerprintSha256 => commonInvariant.FingerprintSha256;

            internal StartMapCaptureContext(
                DungeonPortalBakedBasisStartMapRuntimeController controller,
                Scene harnessScene,
                KExactBasisV1EditorContract.SceneBindings bindings)
            {
                Controller = controller ?? throw new ArgumentNullException(nameof(controller));
                HarnessScene = harnessScene;
                Bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
                initialPolicy = controller.EvidenceSpotPolicy;
                initialFrameIndex = controller.CurrentFrameIndex;
                Policy = initialPolicy;
                commonInvariant = CommonProductionSurfaceInvariant.Capture(bindings);
                RefreshEnvironmentFingerprint();
            }

            internal void ApplyPolicy(DungeonPortalBakedBasisEvidenceSpotPolicy policy, string stage)
            {
                if (!Controller.TryApplyEvidenceSpotPolicyForCapture(policy, out string failure) ||
                    Controller.EvidenceSpotPolicy != policy)
                {
                    throw new InvalidOperationException("Could not apply StartMap evidence policy " +
                                                        policy + " at " + stage + ": " + failure);
                }
                Policy = policy;
                RefreshEnvironmentFingerprint();
                Validate(stage + " after policy");
            }

            internal void SetCaptureFrame(int frameIndex, string stage)
            {
                if (!Controller.TrySetCaptureFrame(frameIndex, out string failure))
                {
                    throw new InvalidOperationException("Could not set actual-player capture frame " +
                                                        frameIndex + " at " + stage + ": " + failure);
                }
                Validate(stage + " after frame");
            }

            internal void Validate(string stage)
            {
                if (!Controller.TryValidateCaptureEnvironment(out string failure))
                {
                    throw new InvalidOperationException("StartMap capture environment drifted at " +
                                                        stage + ": " + failure);
                }
                if (Controller.EvidenceSpotPolicy != Policy ||
                    Controller.EnvironmentFingerprint == null ||
                    !string.Equals(Controller.EnvironmentFingerprint.Sha256,
                        EnvironmentFingerprintSha, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("StartMap common environment fingerprint drifted at " +
                                                        stage + ".");
                }
                commonInvariant.AssertUnchanged(Bindings, stage);
            }

            internal CaptureMetadata CreateCaptureMetadata()
            {
                Camera camera = Controller.ActualPlayerCamera;
                Volume volume = Controller.AssignedDungeonVolume;
                return new CaptureMetadata(
                    FinalCaptureRoute,
                    Policy.ToString(),
                    EnvironmentFingerprintSha,
                    camera != null ? camera.gameObject.scene.path + ":" + camera.name : "MISSING",
                    volume != null ? volume.gameObject.scene.path + ":" + volume.name : "MISSING",
                    SceneManager.GetActiveScene().path,
                    Controller.CurrentFrameIndex);
            }

            internal void RestoreInitialCameraAndPolicy()
            {
                ApplyPolicy(initialPolicy, "final restore policy");
                SetCaptureFrame(initialFrameIndex, "final restore frame");
            }

            private void RefreshEnvironmentFingerprint()
            {
                if (Controller.EnvironmentFingerprint == null ||
                    string.IsNullOrWhiteSpace(Controller.EnvironmentFingerprint.Sha256))
                {
                    throw new InvalidOperationException("StartMap harness has no captured environment fingerprint.");
                }
                EnvironmentFingerprintSha = Controller.EnvironmentFingerprint.Sha256;
            }
        }

        private sealed class CommonProductionSurfaceInvariant
        {
            internal readonly int ProductionRendererCount;
            internal readonly int AdditionalRendererCount;
            internal readonly string FingerprintSha256;

            private CommonProductionSurfaceInvariant(int productionRendererCount,
                int additionalRendererCount, string fingerprintSha256)
            {
                ProductionRendererCount = productionRendererCount;
                AdditionalRendererCount = additionalRendererCount;
                FingerprintSha256 = fingerprintSha256;
            }

            internal static CommonProductionSurfaceInvariant Capture(
                KExactBasisV1EditorContract.SceneBindings bindings)
            {
                Renderer[] production = bindings.ProductionRooms.GetComponentsInChildren<Renderer>(true);
                Renderer[] all = bindings.Root.GetComponentsInChildren<Renderer>(true);
                int additional = all.Length - production.Length;
                if (production.Length != KExactBasisV1EditorContract.ExpectedRendererCount || additional != 0)
                {
                    throw new InvalidOperationException("Original production renderer/duplicate-render contract failed. production=" +
                                                        production.Length + " additional=" + additional + ".");
                }
                var rows = new List<string>(production.Length);
                for (int i = 0; i < production.Length; i++)
                {
                    Renderer renderer = production[i];
                    if (renderer == null) throw new InvalidOperationException("Null production renderer.");
                    string key = KExactBasisV1EditorContract.GetStableComponentKey(bindings.ProductionRooms, renderer);
                    Mesh mesh = ResolveSurfaceMesh(renderer);
                    rows.Add(key + "|type=" + renderer.GetType().FullName + "|mesh=" +
                             KExactBasisV1EditorContract.GetObjectIdentity(mesh) + "|enabled=" + renderer.enabled +
                             "|forceOff=" + renderer.forceRenderingOff + "|reflection=" +
                             renderer.reflectionProbeUsage + "|lightProbe=" + renderer.lightProbeUsage +
                             "|renderLayer=" + renderer.renderingLayerMask + "|materials=" +
                             BuildMaterialArrayFingerprint(renderer.sharedMaterials) + "|mpb=" +
                             KExactBasisV1EditorContract.ComputeMaterialPropertyBlockFingerprint(renderer));
                }
                rows.Sort(StringComparer.Ordinal);
                string canonical = string.Join("\n", rows);
                return new CommonProductionSurfaceInvariant(production.Length, additional,
                    DungeonPortalBakedBasisValidationContract.ComputeSha256(Encoding.UTF8.GetBytes(canonical)));
            }

            internal void AssertUnchanged(KExactBasisV1EditorContract.SceneBindings bindings, string stage)
            {
                CommonProductionSurfaceInvariant current = Capture(bindings);
                if (current.ProductionRendererCount != ProductionRendererCount ||
                    current.AdditionalRendererCount != AdditionalRendererCount ||
                    !string.Equals(current.FingerprintSha256, FingerprintSha256,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Original renderer/material common fingerprint changed outside " +
                                                        "the DPBB power/door/reflection allowlist at " + stage + ".");
                }
            }
        }

        private static Mesh ResolveSurfaceMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
                return skinned.sharedMesh;
            MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            return filter != null ? filter.sharedMesh : null;
        }

        private sealed class ArtifactEvidenceDescriptor
        {
            internal readonly string EvidenceRole;
            internal readonly bool PostProcessingEnabled;
            internal readonly bool ProductionParityReviewCandidate;
            internal readonly string VisualVerdict;
            internal readonly bool FinalEvidenceEligible;

            internal ArtifactEvidenceDescriptor(
                string evidenceRole,
                bool postProcessingEnabled,
                bool productionParityReviewCandidate)
            {
                if (string.IsNullOrWhiteSpace(evidenceRole))
                    throw new ArgumentException("Evidence role is required.", nameof(evidenceRole));
                EvidenceRole = evidenceRole;
                PostProcessingEnabled = postProcessingEnabled;
                ProductionParityReviewCandidate = productionParityReviewCandidate;
                VisualVerdict = UnreviewedVisualVerdict;
                // A raw capture is never final evidence. A separate human review/promotion step
                // would have to create a different, reviewed artifact.
                FinalEvidenceEligible = false;
            }

            internal string ToContractString()
            {
                return "evidenceRole=" + EvidenceRole +
                       ";postProcessingEnabled=" + PostProcessingEnabled.ToString().ToLowerInvariant() +
                       ";productionParityReviewCandidate=" +
                       ProductionParityReviewCandidate.ToString().ToLowerInvariant() +
                       ";visualVerdict=" + VisualVerdict +
                       ";finalEvidenceEligible=" + FinalEvidenceEligible.ToString().ToLowerInvariant();
            }

            internal void AppendManifest(StringBuilder builder, string prefix)
            {
                builder.Append('|').Append(prefix).Append("EvidenceRole=").Append(EvidenceRole)
                    .Append('|').Append(prefix).Append("PostProcessingEnabled=")
                    .Append(PostProcessingEnabled)
                    .Append('|').Append(prefix).Append("ProductionParityReviewCandidate=")
                    .Append(ProductionParityReviewCandidate)
                    .Append('|').Append(prefix).Append("VisualVerdict=").Append(VisualVerdict)
                    .Append('|').Append(prefix).Append("FinalEvidenceEligible=")
                    .Append(FinalEvidenceEligible);
            }
        }

        private sealed class CaptureMetadata
        {
            internal readonly string CaptureRoute;
            internal readonly string SpotPolicy;
            internal readonly string EnvironmentFingerprintSha256;
            internal readonly string ActualCamera;
            internal readonly string AssignedDungeonVolume;
            internal readonly string ActiveScenePath;
            internal readonly int ReferenceFrameIndex;

            internal CaptureMetadata(string captureRoute, string spotPolicy,
                string environmentFingerprintSha256, string actualCamera,
                string assignedDungeonVolume, string activeScenePath, int referenceFrameIndex)
            {
                CaptureRoute = captureRoute;
                SpotPolicy = spotPolicy;
                EnvironmentFingerprintSha256 = environmentFingerprintSha256;
                ActualCamera = actualCamera;
                AssignedDungeonVolume = assignedDungeonVolume;
                ActiveScenePath = activeScenePath;
                ReferenceFrameIndex = referenceFrameIndex;
            }
        }

        private sealed class PolicyCaptureSet
        {
            internal readonly DungeonPortalBakedBasisEvidenceSpotPolicy Policy;
            internal readonly string EnvironmentFingerprintSha256;
            internal readonly List<CaptureRecord> Captures;
            internal readonly List<CaptureRecord> Baselines;
            internal readonly List<CaptureRecord> BaseOnlyProduction;
            internal readonly List<CaptureRecord> BaseOnlyPrivate;
            internal readonly List<D0Metric> D0Metrics;

            internal PolicyCaptureSet(DungeonPortalBakedBasisEvidenceSpotPolicy policy,
                string environmentFingerprintSha256, List<CaptureRecord> captures,
                List<CaptureRecord> baselines, List<CaptureRecord> baseOnlyProduction,
                List<CaptureRecord> baseOnlyPrivate, List<D0Metric> d0Metrics)
            {
                Policy = policy;
                EnvironmentFingerprintSha256 = environmentFingerprintSha256;
                Captures = captures;
                Baselines = baselines;
                BaseOnlyProduction = baseOnlyProduction;
                BaseOnlyPrivate = baseOnlyPrivate;
                D0Metrics = d0Metrics;
            }
        }

        private sealed class CaptureRecord
        {
            internal readonly PowerState Power;
            internal readonly DoorPose Pose;
            internal readonly float DoorAngleDegrees;
            internal readonly string CameraName;
            internal readonly string CameraToken;
            internal readonly string PngFile;
            internal readonly string PngSha;
            internal readonly string ExrFile;
            internal readonly string ExrSha;
            internal readonly string StateFile;
            internal readonly string StateSha;
            internal readonly double MeanLuminance;
            internal readonly float MaxLuminance;
            internal readonly float StartPower;
            internal readonly float AdministrativePower;
            internal readonly float Aperture;
            internal readonly bool StartCompositorActive;
            internal readonly bool AdministrativeCompositorActive;
            internal readonly bool DoorShActive;
            internal readonly bool DriverFault;
            internal readonly bool DoorShFault;
            internal readonly bool ReflectionInitialized;
            internal readonly bool ReflectionFault;
            internal readonly float ReflectionStartPower;
            internal readonly float ReflectionAdministrativePower;
            internal readonly float StartIncomingResponseScale;
            internal readonly float AdministrativeIncomingResponseScale;
            internal readonly CaptureMetadata Metadata;
            internal readonly ArtifactEvidenceDescriptor PngEvidence;
            internal readonly ArtifactEvidenceDescriptor ExrEvidence;
            internal D0Metric D0Metric;

            internal CaptureRecord(
                PowerState power, DoorPose pose, float doorAngleDegrees, string cameraName,
                string cameraToken, string pngFile, string pngSha, string exrFile, string exrSha,
                string stateFile, string stateSha, double meanLuminance, float maxLuminance,
                float startPower, float administrativePower, float aperture,
                bool startCompositorActive, bool administrativeCompositorActive, bool doorShActive,
                bool driverFault, bool doorShFault, bool reflectionInitialized,
                bool reflectionFault, float reflectionStartPower,
                float reflectionAdministrativePower, float startIncomingResponseScale,
                float administrativeIncomingResponseScale, CaptureMetadata metadata,
                ArtifactEvidenceDescriptor pngEvidence,
                ArtifactEvidenceDescriptor exrEvidence)
            {
                Power = power;
                Pose = pose;
                DoorAngleDegrees = doorAngleDegrees;
                CameraName = cameraName;
                CameraToken = cameraToken;
                PngFile = pngFile;
                PngSha = pngSha;
                ExrFile = exrFile;
                ExrSha = exrSha;
                StateFile = stateFile;
                StateSha = stateSha;
                MeanLuminance = meanLuminance;
                MaxLuminance = maxLuminance;
                StartPower = startPower;
                AdministrativePower = administrativePower;
                Aperture = aperture;
                StartCompositorActive = startCompositorActive;
                AdministrativeCompositorActive = administrativeCompositorActive;
                DoorShActive = doorShActive;
                DriverFault = driverFault;
                DoorShFault = doorShFault;
                ReflectionInitialized = reflectionInitialized;
                ReflectionFault = reflectionFault;
                ReflectionStartPower = reflectionStartPower;
                ReflectionAdministrativePower = reflectionAdministrativePower;
                StartIncomingResponseScale = startIncomingResponseScale;
                AdministrativeIncomingResponseScale = administrativeIncomingResponseScale;
                Metadata = metadata;
                PngEvidence = pngEvidence ?? throw new ArgumentNullException(nameof(pngEvidence));
                ExrEvidence = exrEvidence ?? throw new ArgumentNullException(nameof(exrEvidence));
            }

            internal void AppendManifest(StringBuilder builder, string category)
            {
                builder.Append("record=").Append(category).Append('|')
                    .Append(Power.Id).Append('|').Append("D").Append(Pose.Percent).Append('|')
                    .Append(CameraToken).Append('|')
                    .Append("png=").Append(PngFile).Append('|').Append(PngSha).Append('|')
                    .Append("exr=").Append(ExrFile).Append('|').Append(ExrSha).Append('|')
                    .Append("state=").Append(StateFile).Append('|').Append(StateSha).Append('|')
                    .Append("mean=").Append(MeanLuminance.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append("max=").Append(Format(MaxLuminance)).Append('|')
                    .Append("startPower=").Append(Format(StartPower)).Append('|')
                    .Append("administrativePower=").Append(Format(AdministrativePower)).Append('|')
                    .Append("aperture=").Append(Format(Aperture)).Append('|')
                    .Append("startActive=").Append(StartCompositorActive).Append('|')
                    .Append("administrativeActive=").Append(AdministrativeCompositorActive).Append('|')
                    .Append("doorShActive=").Append(DoorShActive).Append('|')
                    .Append("driverFault=").Append(DriverFault).Append('|')
                    .Append("doorShFault=").Append(DoorShFault).Append('|')
                    .Append("reflectionInitialized=").Append(ReflectionInitialized).Append('|')
                    .Append("reflectionFault=").Append(ReflectionFault).Append('|')
                    .Append("reflectionStartPower=").Append(Format(ReflectionStartPower)).Append('|')
                    .Append("reflectionAdministrativePower=").Append(
                        Format(ReflectionAdministrativePower)).Append('|')
                    .Append("startIncomingResponseScale=").Append(
                        Format(StartIncomingResponseScale)).Append('|')
                    .Append("administrativeIncomingResponseScale=").Append(
                        Format(AdministrativeIncomingResponseScale));
                PngEvidence.AppendManifest(builder, "png");
                ExrEvidence.AppendManifest(builder, "exr");
                builder.Append('|').Append("stateEvidenceRole=").Append(DiagnosticEvidenceRole)
                    .Append('|').Append("statePostProcessingEnabled=notApplicable")
                    .Append('|').Append("stateVisualVerdict=").Append(UnreviewedVisualVerdict)
                    .Append('|').Append("stateFinalEvidenceEligible=false");
                if (Metadata != null)
                {
                    builder.Append('|').Append("captureRoute=").Append(Metadata.CaptureRoute)
                        .Append('|').Append("spotPolicy=").Append(Metadata.SpotPolicy)
                        .Append('|').Append("environmentFingerprintSha256=").Append(
                            Metadata.EnvironmentFingerprintSha256)
                        .Append('|').Append("actualPlayerCamera=").Append(Metadata.ActualCamera)
                        .Append('|').Append("assignedDungeonVolume=").Append(
                            Metadata.AssignedDungeonVolume)
                        .Append('|').Append("activeScene=").Append(Metadata.ActiveScenePath)
                        .Append('|').Append("referenceFrameIndex=").Append(
                            Metadata.ReferenceFrameIndex);
                }
                if (D0Metric != null)
                    builder.Append('|').Append("d0ExactRaw=").Append(D0Metric.ExactRaw)
                        .Append('|').Append("d0MeanAbs=").Append(
                            D0Metric.MeanAbsolute.ToString("R", CultureInfo.InvariantCulture))
                        .Append('|').Append("d0MaxAbs=").Append(Format(D0Metric.MaxAbsolute));
                builder.AppendLine();
            }
        }

        private sealed class FramePayload
        {
            internal readonly CaptureRecord Record;
            internal Color[] LinearPixels;
            internal byte[] RawLinear;

            internal FramePayload(CaptureRecord record, Color[] linearPixels, byte[] rawLinear)
            {
                Record = record;
                LinearPixels = linearPixels;
                RawLinear = rawLinear;
            }

            internal void DisposePixels()
            {
                LinearPixels = null;
                RawLinear = null;
            }
        }

        private sealed class D0Metric
        {
            internal readonly string PowerId;
            internal readonly string CameraToken;
            internal readonly bool ExactRaw;
            internal readonly double MeanAbsolute;
            internal readonly float MaxAbsolute;

            internal D0Metric(string powerId, string cameraToken, bool exactRaw,
                double meanAbsolute, float maxAbsolute)
            {
                PowerId = powerId;
                CameraToken = cameraToken;
                ExactRaw = exactRaw;
                MeanAbsolute = meanAbsolute;
                MaxAbsolute = maxAbsolute;
            }

            internal void AppendManifest(StringBuilder builder)
            {
                builder.AppendLine("d0=" + PowerId + "|" + CameraToken + "|exactRaw=" + ExactRaw +
                                   "|meanAbsolute=" + MeanAbsolute.ToString("R", CultureInfo.InvariantCulture) +
                                   "|maxAbsolute=" + Format(MaxAbsolute));
            }
        }

        private sealed class RenderFailureMonitor : IDisposable
        {
            private string failure;

            internal RenderFailureMonitor()
            {
                Application.logMessageReceived += OnLog;
            }

            internal void ThrowIfFailed(string stage)
            {
                if (!string.IsNullOrEmpty(failure))
                    throw new InvalidOperationException("Unity logged a render error during " + stage + ": " + failure);
            }

            private void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    failure ??= Sanitize(condition);
            }

            public void Dispose()
            {
                Application.logMessageReceived -= OnLog;
            }
        }

        private sealed class RenderTargets : IDisposable
        {
            internal readonly RenderTexture PresentationRender;
            internal readonly RenderTexture PresentationResolve;
            internal readonly RenderTexture HdrRender;
            internal readonly RenderTexture HdrResolve;

            private RenderTargets(RenderTexture presentationRender, RenderTexture presentationResolve,
                RenderTexture hdrRender, RenderTexture hdrResolve)
            {
                PresentationRender = presentationRender;
                PresentationResolve = presentationResolve;
                HdrRender = hdrRender;
                HdrResolve = hdrResolve;
            }

            internal static RenderTargets Create()
            {
                RenderTexture presentationRender = null;
                RenderTexture presentationResolve = null;
                RenderTexture hdrRender = null;
                RenderTexture hdrResolve = null;
                try
                {
                    presentationRender = CreateTarget("DPBB_Presentation_Render", 24,
                        RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    presentationResolve = CreateTarget("DPBB_Presentation_Resolve", 0,
                        RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    hdrRender = CreateTarget("DPBB_HDR_Render", 24,
                        RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                    hdrResolve = CreateTarget("DPBB_HDR_Resolve", 0,
                        RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                    return new RenderTargets(presentationRender, presentationResolve, hdrRender,
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

            private static RenderTexture CreateTarget(string name, int depth, RenderTextureFormat format,
                RenderTextureReadWrite readWrite)
            {
                var target = new RenderTexture(CaptureWidth, CaptureHeight, depth, format, readWrite)
                {
                    name = name,
                    antiAliasing = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (!target.Create())
                {
                    Object.DestroyImmediate(target);
                    throw new InvalidOperationException("Could not create " + name + ".");
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
    }
}
