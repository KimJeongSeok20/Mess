using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DungeonPortalTransportPoC.KExactBasisV1.Editor
{
    /// <summary>
    /// Captures the real KExactBasisV1 runtime in an already-running Play Mode.
    /// This tool never enters/exits Play Mode, opens/saves a scene, or writes outside
    /// the owned Evidence folder. Every in-memory power, door, renderer, lightmap,
    /// reflection, smoothing, selection, and runtime-active state is restored.
    /// </summary>
    public static class KExactBasisV1SmokeCapture
    {
        public const string Schema = "KExactBasisV1RuntimeSmoke/v2";
        public const string Status = "KEXACT_BASIS_V1_RUNTIME_SMOKE_CANDIDATE";

        private const int CaptureWidth = 1920;
        private const int CaptureHeight = 1080;
        private const double MinimumMeanLuminance = 1e-12d;
        private const float MinimumMaximumLuminance = 1e-8f;
        private const double MinimumPoweredMeanDelta = 1e-9d;
        private const double MinimumSemanticReceiverD100Delta = 0.00025d;
        private const double SemanticReceiverMonotonicTolerance = 0.00005d;
        private const double MaximumClosedDoorSemanticMeanLeakage = 0.000001d;
        private const int PresentationMaximumRgb8ChannelDelta = 2;
        private const double PresentationMeanAbsoluteRgb8ChannelDelta = 0.5d;
        private const string ExpectedConnectionKey =
            "Start_Admin_R000_Door_SM_A_KExactBasisV1";
        private const string ExpectedSourceP100S2APresentationPngPath =
            "Assets/Experiments/DungeonPortalTransportPoC/Evidence/Smoke/" +
            "20260823_152616_283Z/P000_P000_D000_StartToAdmin.png";
        private const string ExpectedSourceP100A2SPresentationPngPath =
            "Assets/Experiments/DungeonPortalTransportPoC/Evidence/Smoke/" +
            "20260823_152616_283Z/P000_P000_D000_AdminToStart.png";
        private const string ExpectedSourceP100S2APresentationPngSha256 =
            "3fd9c6732da59fca386985922142b7b5a8ca733d2abbd9b4de9f82de86130e92";
        private const string ExpectedSourceP100A2SPresentationPngSha256 =
            "f9744b35206f115c72b87d93d29ac25cadc08f75b03202f5f1bac7a66ef2f6cb";

        // Reviewed receiver-surface regions use top-left, half-open pixel coordinates.
        // Texture2D.GetPixels uses a bottom-left origin; ComputeLuminance performs the
        // explicit Y conversion before accumulating these regions.
        private static readonly SemanticRoi[] A2SSemanticReceiverRois =
        {
            new SemanticRoi(580, 180, 790, 920),
            new SemanticRoi(1120, 180, 1400, 920),
            new SemanticRoi(580, 930, 1400, 1080)
        };

        private static readonly SemanticRoi[] S2ASemanticReceiverRois =
        {
            new SemanticRoi(400, 180, 780, 930),
            new SemanticRoi(1120, 180, 1550, 930),
            new SemanticRoi(400, 930, 1550, 1080)
        };

        private static readonly PowerState[] PowerStates =
        {
            new PowerState("P000_P000", "P00", false, false),
            new PowerState("P100_P000", "P10", true, false),
            new PowerState("P000_P100", "P01", false, true),
            new PowerState("P100_P100", "P11", true, true)
        };

        private static readonly DoorPose[] DoorPoses =
        {
            new DoorPose(0),
            new DoorPose(25),
            new DoorPose(50),
            new DoorPose(75),
            new DoorPose(100)
        };

        [MenuItem(
            "Tools/Dungeon/Lighting/Portal Transport PoC/KExactBasisV1/" +
            "Capture Runtime Smoke (Play Mode; No Mode Toggle)")]
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
                return CaptureAllOrThrow();
            }
            catch (Exception exception)
            {
                return "FAIL " + Status + ": " + exception;
            }
        }

        private static string CaptureAllOrThrow()
        {
            Scene scene = RequireRuntimeCaptureScene();
            bool initialSceneDirty = scene.isDirty;
            string initialBuiltSceneSha = KExactBasisV1EditorContract.ComputeFileSha256(
                KExactBasisV1EditorContract.BuiltScenePath);
            var selection = KExactBasisV1EditorContract.SelectionSnapshot.Capture();
            var protectedAssets =
                KExactBasisV1EditorContract.ProtectedAssetSnapshot.Capture();
            var bindings = KExactBasisV1EditorContract.ResolveSceneBindings(
                scene, false, true, false);
            RuntimeHandles runtime = ResolveRuntimeHandles(bindings);
            runtime.RequireStableActiveState();

            DungeonTileLightmapSwitcher.PowerLevel originalStartPower =
                bindings.StartSwitcher.CurrentPowerLevel;
            DungeonTileLightmapSwitcher.PowerLevel originalAdministrativePower =
                bindings.AdministrativeSwitcher.CurrentPowerLevel;
            float originalStartSource = ReadScalar(runtime.StartPower, "Start power");
            float originalAdministrativeSource = ReadScalar(
                runtime.AdministrativePower, "Administrative power");
            float originalDoorSource = ReadScalar(runtime.DoorSource, "Door openness");
            Quaternion originalDoorRotation = bindings.DoorLeaf.localRotation;
            SmoothingSnapshot smoothing = SmoothingSnapshot.Capture(runtime.Connection);
            DoorConfiguration door = DoorConfiguration.Capture(runtime.DoorAngleSource);
            DoorProbeReceiverSnapshot doorProbeReceiver =
                DoorProbeReceiverSnapshot.Capture(bindings);
            if (!Approximately(runtime.Connection.PowerAToB01, originalStartSource) ||
                !Approximately(runtime.Connection.PowerBToA01, originalAdministrativeSource) ||
                !Approximately(runtime.Connection.SmoothedDoorOpenness01, originalDoorSource))
            {
                throw new InvalidOperationException(
                    "Runtime is mid-transition. Wait for stable power/door weights before capture.");
            }

            var captures = new List<CaptureRecord>(40);
            var adjacentOffCaptures = new List<CaptureRecord>(4);
            var failureMonitor = new RenderFailureMonitor();
            RenderTargets targets = null;
            SurfaceSnapshot adjacentOffSurface = null;
            string adjacentOffFingerprint = null;
            bool cleanupCompleted = false;
            try
            {
                runtime.Manager.DeactivateAll();
                if (runtime.Manager.IsActive || runtime.Connection.IsTransportActive)
                    throw new InvalidOperationException("Could not establish Adjacent OFF baseline.");
                if (bindings.KExactRoot.GetComponentsInChildren<Renderer>(true).Length != 0)
                    throw new InvalidOperationException(
                        "Adjacent OFF has unexpected additional renderers.");
                adjacentOffSurface = SurfaceSnapshot.Capture(bindings);
                adjacentOffFingerprint =
                    KExactBasisV1EditorContract.ComputeRendererAggregateFingerprint(
                        bindings.ProductionRooms);
                doorProbeReceiver.BeginSettledCapture();
                targets = RenderTargets.Create();
                adjacentOffCaptures.AddRange(CaptureAdjacentOffD0(
                    bindings,
                    scene,
                    runtime,
                    door,
                    doorProbeReceiver,
                    targets,
                    failureMonitor));

                runtime.StartPower.SetRuntimeOverride01(originalStartSource);
                runtime.AdministrativePower.SetRuntimeOverride01(
                    originalAdministrativeSource);
                runtime.DoorSource.SetRuntimeOverride01(originalDoorSource);

                runtime.Manager.ResetFault();
                if (!runtime.Manager.TryActivateAll(out string activationFailure))
                    throw new InvalidOperationException(
                        "Runtime reactivation failed before smoke capture: " + activationFailure);
                runtime.Connection.ConfigureSmoothing(0f, 0f, 0f, 0f);

                for (int powerIndex = 0; powerIndex < PowerStates.Length; powerIndex++)
                {
                    PowerState power = PowerStates[powerIndex];
                    ApplyBasePower(bindings, power);
                    runtime.StartPower.SetRuntimeOverride01(power.StartOn ? 1f : 0f);
                    runtime.AdministrativePower.SetRuntimeOverride01(
                        power.AdministrativeOn ? 1f : 0f);

                    for (int poseIndex = 0; poseIndex < DoorPoses.Length; poseIndex++)
                    {
                        DoorPose pose = DoorPoses[poseIndex];
                        bindings.DoorLeaf.localRotation = door.RotationFor(pose.Fraction);
                        Physics.SyncTransforms();
                        runtime.DoorAngleSource.EvaluateNow();
                        if (!Approximately(
                                runtime.DoorAngleSource.OpenFraction,
                                pose.Fraction,
                                0.002f))
                        {
                            throw new InvalidOperationException(
                                "Physical door angle did not produce the requested open " +
                                "fraction for D" + pose.Percent + ".");
                        }
                        float expectedAperture =
                            PortalTransportMath.ComputeProjectedApertureFraction(
                                pose.Fraction);
                        float actualAperture =
                            runtime.DoorAngleSource.ApertureFraction;
                        if (!Approximately(actualAperture, expectedAperture, 0.0001f))
                        {
                            throw new InvalidOperationException(
                                "Physical door projected aperture mismatch for D" +
                                pose.Percent + ": actual=" +
                                actualAperture.ToString("R", CultureInfo.InvariantCulture) +
                                ", expected=" +
                                expectedAperture.ToString("R", CultureInfo.InvariantCulture) +
                                ".");
                        }
                        runtime.DoorSource.SetRuntimeOverride01(actualAperture);
                        doorProbeReceiver.ForceSettledRefresh();
                        if (!runtime.Connection.TryApplyCurrentInputsImmediately(
                                out string immediateFailure))
                        {
                            throw new InvalidOperationException(
                                "Immediate runtime state application failed: " + immediateFailure);
                        }
                        if (!runtime.Connection.TryValidateParity(out string parityFailure))
                            throw new InvalidOperationException(
                                "Active runtime parity failed: " + parityFailure);
                        AssertTelemetry(
                            runtime.Connection,
                            power,
                            pose,
                            actualAperture);

                        for (int cameraIndex = 0; cameraIndex < bindings.Cameras.Length;
                             cameraIndex++)
                        {
                            captures.Add(CaptureCamera(
                                bindings.Cameras[cameraIndex],
                                scene,
                                power,
                                pose,
                                door.OpenAngleDegrees,
                                runtime.Connection,
                                targets,
                                failureMonitor));
                        }
                    }
                }

                if (captures.Count != 40)
                    throw new InvalidOperationException(
                        "Expected exactly 40 state/pose/camera records; got " +
                        captures.Count + ".");
                failureMonitor.ThrowIfFailed("capture completion");
                ValidateAdjacentOffPixelParity(adjacentOffCaptures, captures);
                AttachSamePoseBaselines(captures);
                ValidateDiversityAndPoweredSignal(captures);
            }
            finally
            {
                if (targets != null)
                    targets.Dispose();
                failureMonitor.Dispose();
                try
                {
                    runtime.Manager.DeactivateAll();
                    bindings.StartSwitcher.SetPowerLevel(originalStartPower);
                    bindings.AdministrativeSwitcher.SetPowerLevel(originalAdministrativePower);
                    bindings.DoorLeaf.localRotation = originalDoorRotation;
                    Physics.SyncTransforms();
                    runtime.DoorAngleSource.EvaluateNow();
                    runtime.StartPower.ClearRuntimeOverride();
                    runtime.AdministrativePower.ClearRuntimeOverride();
                    runtime.DoorSource.ClearRuntimeOverride();
                    smoothing.Restore(runtime.Connection);
                    doorProbeReceiver.Restore();
                    adjacentOffSurface?.Restore();

                    if (adjacentOffSurface != null)
                    {
                        adjacentOffSurface.AssertRestored();
                        doorProbeReceiver.AssertRestored();
                        string restoredFingerprint =
                            KExactBasisV1EditorContract.ComputeRendererAggregateFingerprint(
                                bindings.ProductionRooms);
                        if (!string.Equals(restoredFingerprint, adjacentOffFingerprint,
                                StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                "Adjacent OFF production-surface fingerprint was not restored.");
                        }
                    }

                    runtime.Manager.ResetFault();
                    if (!runtime.Manager.TryActivateAll(out string restoreActivationFailure))
                        throw new InvalidOperationException(
                            "Original runtime active state was not restored: " +
                            restoreActivationFailure);
                    bool restoredInputs =
                        runtime.Connection.TryApplyCurrentInputsImmediately(
                            out string restoreInputFailure);
                    string restoreParityFailure = string.Empty;
                    bool restoredParity = restoredInputs &&
                        runtime.Connection.TryValidateParity(out restoreParityFailure);
                    if (!restoredInputs || !restoredParity)
                    {
                        throw new InvalidOperationException(
                            "Original runtime telemetry/parity was not restored: " +
                            restoreInputFailure + " " + restoreParityFailure);
                    }
                    cleanupCompleted = true;
                }
                finally
                {
                    selection.Restore();
                }
            }

            if (!cleanupCompleted)
                throw new InvalidOperationException("Runtime capture cleanup did not complete.");
            AssertEditorRuntimeIdentity(
                scene,
                initialSceneDirty,
                initialBuiltSceneSha,
                selection,
                protectedAssets);

            string utc = DateTime.UtcNow.ToString(
                "yyyyMMdd'T'HHmmssfff'Z'",
                CultureInfo.InvariantCulture);
            string runId = utc + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string outputFolder = KExactBasisV1EditorContract.EvidenceRoot + "/" + runId;
            string manifest = BuildManifest(
                utc,
                initialBuiltSceneSha,
                adjacentOffFingerprint,
                adjacentOffCaptures,
                captures);
            string manifestSha = PublishEvidence(outputFolder, captures, manifest);

            selection.Restore();
            AssertEditorRuntimeIdentity(
                scene,
                initialSceneDirty,
                initialBuiltSceneSha,
                selection,
                protectedAssets);
            return "PASS " + Status + "\n" +
                   "output=" + outputFolder + "\n" +
                   "stateCameraRecordCount=" + captures.Count + "\n" +
                   "manifestSha256=" + manifestSha + "\n" +
                   "visualVerdict=UNREVIEWED\n" +
                   "visualParityClaimed=false";
        }

        private static Scene RequireRuntimeCaptureScene()
        {
            if (!Application.isPlaying || !EditorApplication.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying)
                throw new InvalidOperationException(
                    "An already-running, stable Play Mode is required; this tool never toggles it.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                Lightmapping.isRunning)
                throw new InvalidOperationException("Unity is compiling, updating, or lightmapping.");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Close Prefab Stage before capture.");
            if (KExactBasisV1EditorContract.CountLoadedNonPreviewScenes() != 1 ||
                SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Exactly one non-preview scene is required.");

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded ||
                !string.Equals(
                    KExactBasisV1EditorContract.NormalizePath(scene.path),
                    KExactBasisV1EditorContract.BuiltScenePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The built KExactBasisV1 scene must already be active in Play Mode.");
            }
            KExactBasisV1EditorContract.ValidatePinnedInputs();
            string buildManifest = File.ReadAllText(
                KExactBasisV1EditorContract.AssetPathToAbsolutePath(
                    KExactBasisV1EditorContract.BuildManifestPath));
            string declaredSceneSha = ReadUniqueManifestValue(
                buildManifest, "builtSceneSha256");
            string actualSceneSha = KExactBasisV1EditorContract.ComputeFileSha256(
                KExactBasisV1EditorContract.BuiltScenePath);
            if (!string.Equals(declaredSceneSha, actualSceneSha,
                    StringComparison.OrdinalIgnoreCase) ||
                ReadUniqueManifestValue(buildManifest, "buildOutcome") != "COMPLETE" ||
                ReadUniqueManifestValue(buildManifest, "additionalRendererCount") != "0")
            {
                throw new InvalidOperationException(
                    "Built scene/build manifest binding is incomplete or stale.");
            }
            return scene;
        }

        private static RuntimeHandles ResolveRuntimeHandles(
            KExactBasisV1EditorContract.SceneBindings bindings)
        {
            KExactPortalRuntimeManager[] managers =
                bindings.KExactRoot.GetComponentsInChildren<KExactPortalRuntimeManager>(true);
            KExactPortalConnection[] connections =
                bindings.KExactRoot.GetComponentsInChildren<KExactPortalConnection>(true);
            if (managers.Length != 1 || connections.Length != 1 ||
                !string.Equals(connections[0].ConnectionKey, ExpectedConnectionKey,
                    StringComparison.Ordinal) ||
                managers[0].Connections.Length != 1 ||
                managers[0].Connections[0] != connections[0])
            {
                throw new InvalidOperationException("Runtime manager/connection identity changed.");
            }
            KExactPortalConnection connection = connections[0];
            KExactScalarSource door = connection.DoorOpennessSource;
            DungeonPortalDoorAngleSource liveDoor =
                door != null
                    ? door.SourceComponent as DungeonPortalDoorAngleSource
                    : null;
            if (connection.AToB == null || connection.BToA == null ||
                connection.AToB.SourcePower == null || connection.BToA.SourcePower == null ||
                door == null || liveDoor == null || !liveDoor.IsConfigured ||
                liveDoor.DoorLeaf != bindings.DoorLeaf ||
                connection.AToB.SourcePower.Mode !=
                    KExactScalarSource.SourceMode.PublicMember ||
                connection.BToA.SourcePower.Mode !=
                    KExactScalarSource.SourceMode.PublicMember ||
                door.Mode != KExactScalarSource.SourceMode.PublicMember ||
                connection.AToB.SourcePower.SourceComponent != bindings.StartSwitcher ||
                connection.BToA.SourcePower.SourceComponent !=
                    bindings.AdministrativeSwitcher ||
                connection.AToB.SourcePower.PublicMemberName !=
                    nameof(DungeonTileLightmapSwitcher.CurrentPowerLevel) ||
                connection.BToA.SourcePower.PublicMemberName !=
                    nameof(DungeonTileLightmapSwitcher.CurrentPowerLevel) ||
                door.PublicMemberName !=
                    nameof(DungeonPortalDoorAngleSource.ApertureFraction) ||
                connection.AToB.SourcePower.HasRuntimeOverride ||
                connection.BToA.SourcePower.HasRuntimeOverride ||
                door.HasRuntimeOverride ||
                connection.AToB.SelectedProductionLights.Length != 3 ||
                connection.BToA.SelectedProductionLights.Length != 2)
                throw new InvalidOperationException("Runtime scalar/K binding changed.");
            return new RuntimeHandles(
                managers[0],
                connection,
                connection.AToB.SourcePower,
                connection.BToA.SourcePower,
                door,
                liveDoor);
        }

        private static void ApplyBasePower(
            KExactBasisV1EditorContract.SceneBindings bindings,
            PowerState power)
        {
            bindings.StartSwitcher.SetPowerLevel(
                power.StartOn
                    ? DungeonTileLightmapSwitcher.PowerLevel.P100
                    : DungeonTileLightmapSwitcher.PowerLevel.P0);
            bindings.AdministrativeSwitcher.SetPowerLevel(
                power.AdministrativeOn
                    ? DungeonTileLightmapSwitcher.PowerLevel.P100
                    : DungeonTileLightmapSwitcher.PowerLevel.P0);
        }

        private static List<CaptureRecord> CaptureAdjacentOffD0(
            KExactBasisV1EditorContract.SceneBindings bindings,
            Scene scene,
            RuntimeHandles runtime,
            DoorConfiguration door,
            DoorProbeReceiverSnapshot doorProbeReceiver,
            RenderTargets targets,
            RenderFailureMonitor failures)
        {
            var result = new List<CaptureRecord>(4);
            PowerState[] powers =
            {
                PowerStates.Single(value => value.Id == "P000_P000"),
                PowerStates.Single(value => value.Id == "P100_P100")
            };
            DoorPose pose = DoorPoses.Single(value => value.Percent == 0);
            for (int powerIndex = 0; powerIndex < powers.Length; powerIndex++)
            {
                AssertAdjacentOff(runtime, "before temporary capture state");
                PowerState power = powers[powerIndex];
                ApplyBasePower(bindings, power);
                bindings.DoorLeaf.localRotation = door.RotationFor(pose.Fraction);
                Physics.SyncTransforms();
                runtime.DoorAngleSource.EvaluateNow();
                doorProbeReceiver.ForceSettledRefresh();
                AssertAdjacentOff(runtime, power.Id + " D000 settled state");

                for (int cameraIndex = 0; cameraIndex < bindings.Cameras.Length;
                     cameraIndex++)
                {
                    result.Add(CaptureCamera(
                        bindings.Cameras[cameraIndex],
                        scene,
                        power,
                        pose,
                        door.OpenAngleDegrees,
                        runtime.Connection,
                        targets,
                        failures));
                    AssertAdjacentOff(runtime, power.Id + " D000 camera capture");
                }
            }
            if (result.Count != 4)
                throw new InvalidOperationException(
                    "Expected exactly four in-memory Adjacent OFF records; got " +
                    result.Count + ".");
            failures.ThrowIfFailed("Adjacent OFF in-memory capture");
            return result;
        }

        private static void AssertAdjacentOff(RuntimeHandles runtime, string stage)
        {
            if (runtime.Manager.IsActive || runtime.Connection.IsTransportActive ||
                runtime.Connection.IsFaultLatched)
            {
                throw new InvalidOperationException(
                    "Adjacent OFF invariant failed during " + stage + ".");
            }
        }

        private static void AssertTelemetry(
            KExactPortalConnection connection,
            PowerState power,
            DoorPose pose,
            float apertureFraction)
        {
            float start = power.StartOn ? 1f : 0f;
            float administrative = power.AdministrativeOn ? 1f : 0f;
            KExactDirectedTransportBinding aToB = connection.AToB;
            KExactDirectedTransportBinding bToA = connection.BToA;
            if (aToB == null || bToA == null)
                throw new InvalidOperationException(
                    "Runtime telemetry bindings are missing.");
            float expectedDirectAToB = Mathf.Lerp(
                aToB.DirectIntensityScaleAtPower0,
                aToB.DirectIntensityScaleAtPower100,
                start) * apertureFraction;
            float expectedDirectBToA = Mathf.Lerp(
                bToA.DirectIntensityScaleAtPower0,
                bToA.DirectIntensityScaleAtPower100,
                administrative) * apertureFraction;
            float expectedReflectionAToB = Mathf.Lerp(
                aToB.ResidualReflectionAtPower0,
                aToB.ReflectionAtPower100,
                start) * apertureFraction;
            float expectedReflectionBToA = Mathf.Lerp(
                bToA.ResidualReflectionAtPower0,
                bToA.ReflectionAtPower100,
                administrative) * apertureFraction;
            float expectedBounceAToB = SumExpectedProxyIntensity(
                aToB.ReceiverBounceProxies,
                start) * apertureFraction;
            float expectedBounceBToA = SumExpectedProxyIntensity(
                bToA.ReceiverBounceProxies,
                administrative) * apertureFraction;
            float expectedDoorSurfaceAToB = SumExpectedProxyIntensity(
                aToB.DoorSurfaceProxies,
                start);
            float expectedDoorSurfaceBToA = SumExpectedProxyIntensity(
                bToA.DoorSurfaceProxies,
                administrative);
            if (!connection.IsTransportActive || connection.IsFaultLatched ||
                !string.IsNullOrEmpty(connection.FaultReason) ||
                !Approximately(connection.PowerAToB01, start) ||
                !Approximately(connection.PowerBToA01, administrative) ||
                !Approximately(connection.SmoothedDoorOpenness01, apertureFraction) ||
                !Approximately(connection.DirectScaleAToB, expectedDirectAToB) ||
                !Approximately(connection.DirectScaleBToA, expectedDirectBToA) ||
                !Approximately(connection.ReflectionWeightAToB,
                    expectedReflectionAToB) ||
                !Approximately(connection.ReflectionWeightBToA,
                    expectedReflectionBToA) ||
                !Approximately(connection.ReceiverBounceTotalIntensityAToB,
                    expectedBounceAToB) ||
                !Approximately(connection.ReceiverBounceTotalIntensityBToA,
                    expectedBounceBToA) ||
                !Approximately(connection.DoorSurfaceTotalIntensityAToB,
                    expectedDoorSurfaceAToB) ||
                !Approximately(connection.DoorSurfaceTotalIntensityBToA,
                    expectedDoorSurfaceBToA))
            {
                throw new InvalidOperationException(
                    "Runtime telemetry does not match the requested matrix state D" +
                    pose.Percent + ".");
            }
        }

        private static float SumExpectedProxyIntensity(
            KExactBounceProxyDescriptor[] descriptors,
            float sourcePower01)
        {
            float total = 0f;
            for (int i = 0; i < descriptors.Length; i++)
            {
                KExactBounceProxyDescriptor descriptor = descriptors[i];
                if (descriptor != null)
                {
                    total += Mathf.Lerp(
                        descriptor.IntensityAtPower0,
                        descriptor.IntensityAtPower100,
                        sourcePower01);
                }
            }
            return total;
        }

        private static CaptureRecord CaptureCamera(
            Camera camera,
            Scene scene,
            PowerState power,
            DoorPose pose,
            float fullOpenAngle,
            KExactPortalConnection connection,
            RenderTargets targets,
            RenderFailureMonitor failures)
        {
            if (camera == null || camera.enabled || camera.targetTexture != null ||
                camera.gameObject.scene != scene || camera.scene.IsValid())
                throw new InvalidOperationException("Fixed camera isolation changed.");
            int cameraIndex = camera.name == KExactBasisV1EditorContract.StartCameraName
                ? 0
                : camera.name == KExactBasisV1EditorContract.AdministrativeCameraName
                    ? 1
                    : throw new InvalidOperationException("Unexpected fixed camera.");
            string cameraToken = cameraIndex == 0 ? "S2A" : "A2S";
            KExactBasisV1EditorContract.ValidateFixedCamera(camera, cameraIndex);
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            bool originalPost = cameraData.renderPostProcessing;
            RenderTexture originalActive = RenderTexture.active;
            RenderTexture originalTarget = camera.targetTexture;
            float originalAspect = camera.aspect;
            Matrix4x4 originalProjection = camera.projectionMatrix;
            bool originalSrgb = GL.sRGBWrite;
            Texture2D pngReadback = null;
            Texture2D exrReadback = null;
            try
            {
                camera.aspect = CaptureWidth / (float)CaptureHeight;
                camera.ResetProjectionMatrix();
                AssertStandardProjection(camera);

                cameraData.renderPostProcessing = true;
                SubmitStandardRequest(camera, targets.PresentationRender, failures,
                    "presentation");
                GL.sRGBWrite = targets.PresentationResolve.sRGB;
                Graphics.Blit(targets.PresentationRender, targets.PresentationResolve);
                RenderTexture.active = targets.PresentationResolve;
                pngReadback = new Texture2D(
                    CaptureWidth, CaptureHeight, TextureFormat.RGBA32, false, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                pngReadback.ReadPixels(
                    new Rect(0f, 0f, CaptureWidth, CaptureHeight), 0, 0, false);
                pngReadback.Apply(false, false);
                byte[] png = ImageConversion.EncodeToPNG(pngReadback);
                VerifyPng(png, "presentation PNG");

                cameraData.renderPostProcessing = false;
                SubmitStandardRequest(camera, targets.HdrRender, failures, "linear HDR");
                GL.sRGBWrite = false;
                Graphics.Blit(targets.HdrRender, targets.HdrResolve);
                RenderTexture.active = targets.HdrResolve;
                exrReadback = new Texture2D(
                    CaptureWidth, CaptureHeight, TextureFormat.RGBAHalf, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                exrReadback.ReadPixels(
                    new Rect(0f, 0f, CaptureWidth, CaptureHeight), 0, 0, false);
                exrReadback.Apply(false, false);
                ComputeLuminance(
                    exrReadback,
                    cameraToken,
                    out double mean,
                    out float maximum,
                    out double semanticReceiverMean);
                if (!double.IsFinite(mean) || !float.IsFinite(maximum) ||
                    !double.IsFinite(semanticReceiverMean) ||
                    mean <= MinimumMeanLuminance || maximum <= MinimumMaximumLuminance)
                {
                    throw new InvalidOperationException(
                        "Zero/non-finite HDR frame: " + power.Id + " D" + pose.Percent +
                        " " + camera.name + ".");
                }
                byte[] exr = ImageConversion.EncodeToEXR(
                    exrReadback, Texture2D.EXRFlags.CompressZIP);
                VerifyExr(exr, "linear EXR");

                string stem = power.ShortToken + "_D" +
                              pose.Percent.ToString("000", CultureInfo.InvariantCulture) +
                              "_" + cameraToken;
                KExactTransportWeights weights = connection.CurrentWeights;
                return new CaptureRecord(
                    power,
                    pose,
                    fullOpenAngle * pose.Fraction,
                    camera.name,
                    cameraToken,
                    stem + ".png",
                    png,
                    KExactBasisV1EditorContract.ComputeSha256(png),
                    stem + "_L.exr",
                    exr,
                    KExactBasisV1EditorContract.ComputeSha256(exr),
                    mean,
                    maximum,
                    semanticReceiverMean,
                    camera.transform.position,
                    camera.transform.rotation,
                    camera.fieldOfView,
                    camera.nearClipPlane,
                    camera.farClipPlane,
                    camera.cullingMask,
                    camera.allowHDR,
                    camera.allowMSAA,
                    targets.PresentationRender.antiAliasing,
                    true,
                    false,
                    connection.ConnectionKey,
                    connection.IsTransportActive,
                    connection.IsFaultLatched,
                    connection.FaultReason ?? string.Empty,
                    true,
                    string.Empty,
                    weights,
                    connection.ReceiverBounceTotalIntensityAToB,
                    connection.ReceiverBounceTotalIntensityBToA,
                    connection.DoorSurfaceTotalIntensityAToB,
                    connection.DoorSurfaceTotalIntensityBToA);
            }
            finally
            {
                RenderTexture.active = originalActive;
                camera.targetTexture = originalTarget;
                camera.aspect = originalAspect;
                camera.projectionMatrix = originalProjection;
                cameraData.renderPostProcessing = originalPost;
                GL.sRGBWrite = originalSrgb;
                if (pngReadback != null)
                    Object.DestroyImmediate(pngReadback);
                if (exrReadback != null)
                    Object.DestroyImmediate(exrReadback);
            }
        }

        private static void SubmitStandardRequest(
            Camera camera,
            RenderTexture destination,
            RenderFailureMonitor failures,
            string stage)
        {
            if (!(RenderPipelineManager.currentPipeline is UniversalRenderPipeline) ||
                camera.targetTexture != null || destination == null ||
                !destination.IsCreated())
                throw new InvalidOperationException("URP StandardRequest precondition failed.");
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
            failures.ThrowIfFailed(stage);
            if (camera.targetTexture != null)
                throw new InvalidOperationException("StandardRequest changed Camera target.");
        }

        private static void AssertStandardProjection(Camera camera)
        {
            Matrix4x4 expected = Matrix4x4.Perspective(
                camera.fieldOfView,
                camera.aspect,
                camera.nearClipPlane,
                camera.farClipPlane);
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
            {
                if (Mathf.Abs(camera.projectionMatrix[row, column] -
                             expected[row, column]) > 0.00001f)
                    throw new InvalidOperationException("Camera projection is not standard.");
            }
        }

        private static void ComputeLuminance(
            Texture2D texture,
            string cameraToken,
            out double mean,
            out float maximum,
            out double semanticReceiverMean)
        {
            Color[] pixels = texture.GetPixels();
            if (pixels.Length != CaptureWidth * CaptureHeight)
                throw new InvalidOperationException("Unexpected HDR readback dimensions.");
            double total = 0d;
            maximum = 0f;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color pixel = pixels[i];
                if (!float.IsFinite(pixel.r) || !float.IsFinite(pixel.g) ||
                    !float.IsFinite(pixel.b))
                {
                    mean = double.NaN;
                    maximum = float.NaN;
                    semanticReceiverMean = double.NaN;
                    return;
                }
                float luminance = LinearLuminance(pixel);
                total += luminance;
                maximum = Mathf.Max(maximum, luminance);
            }
            mean = total / pixels.Length;

            SemanticRoi[] regions = GetSemanticReceiverRois(cameraToken);
            double semanticTotal = 0d;
            long semanticPixelCount = 0L;
            for (int regionIndex = 0; regionIndex < regions.Length; regionIndex++)
            {
                SemanticRoi region = regions[regionIndex];
                region.Validate(CaptureWidth, CaptureHeight, cameraToken);

                // Input coordinates are top-left, half-open [left, top, right, bottom).
                // GetPixels is laid out bottom-left first, so flip both Y endpoints.
                int bottomLeftY = CaptureHeight - region.Bottom;
                int topLeftYExclusive = CaptureHeight - region.Top;
                for (int y = bottomLeftY; y < topLeftYExclusive; y++)
                {
                    int row = y * CaptureWidth;
                    for (int x = region.Left; x < region.Right; x++)
                    {
                        semanticTotal += LinearLuminance(pixels[row + x]);
                        semanticPixelCount++;
                    }
                }
            }
            if (semanticPixelCount <= 0L)
                throw new InvalidOperationException(
                    "Semantic receiver ROI is empty for camera token " + cameraToken + ".");
            semanticReceiverMean = semanticTotal / semanticPixelCount;
        }

        private static float LinearLuminance(Color pixel)
        {
            return Mathf.Max(
                0f,
                pixel.r * 0.2126f + pixel.g * 0.7152f + pixel.b * 0.0722f);
        }

        private static SemanticRoi[] GetSemanticReceiverRois(string cameraToken)
        {
            if (string.Equals(cameraToken, "A2S", StringComparison.Ordinal))
                return A2SSemanticReceiverRois;
            if (string.Equals(cameraToken, "S2A", StringComparison.Ordinal))
                return S2ASemanticReceiverRois;
            throw new InvalidOperationException(
                "No semantic receiver ROI is defined for camera token " + cameraToken + ".");
        }

        private static void AttachSamePoseBaselines(List<CaptureRecord> captures)
        {
            var baselines = new Dictionary<string, CaptureRecord>(StringComparer.Ordinal);
            for (int i = 0; i < captures.Count; i++)
            {
                CaptureRecord capture = captures[i];
                if (!capture.Power.IsP0P0)
                    continue;
                string key = capture.Pose.Percent + "|" + capture.Camera;
                if (!baselines.TryAdd(key, capture))
                    throw new InvalidOperationException("Duplicate same-pose P0/P0 baseline.");
            }
            if (baselines.Count != 10)
                throw new InvalidOperationException("Exactly ten P0/P0 baselines are required.");
            for (int i = 0; i < captures.Count; i++)
            {
                CaptureRecord capture = captures[i];
                CaptureRecord baseline = baselines[capture.Pose.Percent + "|" + capture.Camera];
                capture.AttachBaseline(
                    baseline.ExrFilename,
                    baseline.MeanLuminance,
                    capture.MeanLuminance - baseline.MeanLuminance,
                    baseline.SemanticReceiverMeanLuminance,
                    capture.SemanticReceiverMeanLuminance -
                    baseline.SemanticReceiverMeanLuminance);
            }
        }

        private static void ValidateDiversityAndPoweredSignal(List<CaptureRecord> captures)
        {
            if (captures.Select(value => value.PngSha).Distinct(StringComparer.Ordinal).Count() <= 1 ||
                captures.Select(value => value.ExrSha).Distinct(StringComparer.Ordinal).Count() <= 1)
                throw new InvalidOperationException("All PNG or EXR outputs are identical.");
            RequirePoweredSignal(
                captures,
                "P100_P000",
                KExactBasisV1EditorContract.AdministrativeCameraName,
                "Start-to-Administrative");
            RequirePoweredSignal(
                captures,
                "P000_P100",
                KExactBasisV1EditorContract.StartCameraName,
                "Administrative-to-Start");
            ValidateSemanticReceiverSurfaceSignal(
                captures,
                "P100_P000",
                KExactBasisV1EditorContract.AdministrativeCameraName,
                "A2S",
                "Start-to-Administrative");
            ValidateSemanticReceiverSurfaceSignal(
                captures,
                "P000_P100",
                KExactBasisV1EditorContract.StartCameraName,
                "S2A",
                "Administrative-to-Start");
            ValidateClosedDoorReceiverLeakage(
                captures,
                "P100_P000",
                KExactBasisV1EditorContract.AdministrativeCameraName,
                "A2S",
                "Start-to-Administrative");
            ValidateClosedDoorReceiverLeakage(
                captures,
                "P000_P100",
                KExactBasisV1EditorContract.StartCameraName,
                "S2A",
                "Administrative-to-Start");
        }

        private static void ValidateClosedDoorReceiverLeakage(
            List<CaptureRecord> captures,
            string poweredState,
            string camera,
            string cameraToken,
            string label)
        {
            CaptureRecord baseline = captures.Single(value =>
                value.Power.IsP0P0 && value.Pose.Percent == 0 &&
                value.Camera == camera);
            CaptureRecord powered = captures.Single(value =>
                value.Power.Id == poweredState && value.Pose.Percent == 0 &&
                value.Camera == camera);
            LinearDifferenceMetrics metrics = CompareLinearExrInSemanticReceiver(
                baseline.ExrBytes,
                powered.ExrBytes,
                cameraToken,
                label + " closed-door receiver leakage");
            if (metrics.MeanAbsoluteLuminanceDelta >
                    MaximumClosedDoorSemanticMeanLeakage ||
                metrics.MeanPositiveLuminanceDelta >
                    MaximumClosedDoorSemanticMeanLeakage)
            {
                throw new InvalidOperationException(
                    "Closed-door receiver leakage gate failed: " + label +
                    ", meanAbs=" +
                    metrics.MeanAbsoluteLuminanceDelta.ToString(
                        "R", CultureInfo.InvariantCulture) +
                    ", meanPositive=" +
                    metrics.MeanPositiveLuminanceDelta.ToString(
                        "R", CultureInfo.InvariantCulture) +
                    ", maximum=" +
                    MaximumClosedDoorSemanticMeanLeakage.ToString(
                        "R", CultureInfo.InvariantCulture) + ".");
            }
        }

        private static LinearDifferenceMetrics CompareLinearExrInSemanticReceiver(
            byte[] baselineBytes,
            byte[] poweredBytes,
            string cameraToken,
            string label)
        {
            Texture2D baseline = null;
            Texture2D powered = null;
            try
            {
                baseline = new Texture2D(2, 2, TextureFormat.RGBAHalf, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                powered = new Texture2D(2, 2, TextureFormat.RGBAHalf, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (!ImageConversion.LoadImage(baseline, baselineBytes, false) ||
                    !ImageConversion.LoadImage(powered, poweredBytes, false) ||
                    baseline.width != CaptureWidth || baseline.height != CaptureHeight ||
                    powered.width != CaptureWidth || powered.height != CaptureHeight)
                {
                    throw new InvalidOperationException(
                        "Could not decode linear EXR pair for " + label + ".");
                }

                Color[] baselinePixels = baseline.GetPixels();
                Color[] poweredPixels = powered.GetPixels();
                SemanticRoi[] regions = GetSemanticReceiverRois(cameraToken);
                double absoluteTotal = 0d;
                double positiveTotal = 0d;
                long pixelCount = 0L;
                for (int regionIndex = 0; regionIndex < regions.Length; regionIndex++)
                {
                    SemanticRoi region = regions[regionIndex];
                    region.Validate(CaptureWidth, CaptureHeight, cameraToken);
                    int bottomLeftY = CaptureHeight - region.Bottom;
                    int topLeftYExclusive = CaptureHeight - region.Top;
                    for (int y = bottomLeftY; y < topLeftYExclusive; y++)
                    {
                        int row = y * CaptureWidth;
                        for (int x = region.Left; x < region.Right; x++)
                        {
                            int index = row + x;
                            double delta = LinearLuminance(poweredPixels[index]) -
                                           LinearLuminance(baselinePixels[index]);
                            absoluteTotal += Math.Abs(delta);
                            positiveTotal += Math.Max(0d, delta);
                            pixelCount++;
                        }
                    }
                }
                if (pixelCount <= 0L)
                    throw new InvalidOperationException(
                        "Closed-door semantic receiver ROI is empty for " + label + ".");
                return new LinearDifferenceMetrics(
                    absoluteTotal / pixelCount,
                    positiveTotal / pixelCount);
            }
            finally
            {
                if (baseline != null)
                    Object.DestroyImmediate(baseline);
                if (powered != null)
                    Object.DestroyImmediate(powered);
            }
        }

        private static void RequirePoweredSignal(
            List<CaptureRecord> captures,
            string power,
            string camera,
            string label)
        {
            CaptureRecord baseline = captures.Single(value =>
                value.Power.IsP0P0 && value.Pose.Percent == 100 && value.Camera == camera);
            CaptureRecord powered = captures.Single(value =>
                value.Power.Id == power && value.Pose.Percent == 100 && value.Camera == camera);
            if (powered.ExrSha == baseline.ExrSha ||
                powered.MeanLuminance - baseline.MeanLuminance <= MinimumPoweredMeanDelta)
                throw new InvalidOperationException("Powered signal gate failed: " + label + ".");
        }

        private static void ValidateAdjacentOffPixelParity(
            List<CaptureRecord> adjacentOffCaptures,
            List<CaptureRecord> activeCaptures)
        {
            if (adjacentOffCaptures == null || adjacentOffCaptures.Count != 4)
                throw new InvalidOperationException(
                    "Adjacent OFF pixel parity requires exactly four in-memory records.");

            string[] cameraTokens = { "S2A", "A2S" };
            for (int cameraIndex = 0; cameraIndex < cameraTokens.Length; cameraIndex++)
            {
                string cameraToken = cameraTokens[cameraIndex];
                CaptureRecord adjacentOffP00 = adjacentOffCaptures.Single(value =>
                    value.Power.Id == "P000_P000" && value.Pose.Percent == 0 &&
                    value.CameraToken == cameraToken);
                CaptureRecord activeP00 = activeCaptures.Single(value =>
                    value.Power.Id == "P000_P000" && value.Pose.Percent == 0 &&
                    value.CameraToken == cameraToken);
                bool p00ExrBytesEqual =
                    adjacentOffP00.ExrBytes.SequenceEqual(activeP00.ExrBytes);
                bool p00ExrHashEqual = string.Equals(
                    adjacentOffP00.ExrSha,
                    activeP00.ExrSha,
                    StringComparison.OrdinalIgnoreCase);
                PngDifferenceMetrics p00PresentationDifference = CompareDecodedPng(
                    adjacentOffP00.PngBytes,
                    activeP00.PngBytes,
                    cameraToken + " Adjacent OFF vs active P00/D0");
                if (adjacentOffP00.IsTransportActive || !p00ExrHashEqual ||
                    !p00ExrBytesEqual || !p00PresentationDifference.WithinTolerance)
                {
                    throw new InvalidOperationException(
                        "Adjacent OFF vs active P00/D0 pixel parity failed: " +
                        cameraToken + "; offPng=" + adjacentOffP00.PngSha +
                        "; activePng=" + activeP00.PngSha +
                        "; pngBytesEqual=" +
                        adjacentOffP00.PngBytes.SequenceEqual(activeP00.PngBytes) +
                        "; presentationDifference=" +
                        p00PresentationDifference.ToInvariantString() +
                        "; offExr=" + adjacentOffP00.ExrSha +
                        "; activeExr=" + activeP00.ExrSha +
                        "; exrHashEqual=" + p00ExrHashEqual +
                        "; exrBytesEqual=" + p00ExrBytesEqual + ".");
                }

                CaptureRecord adjacentOffP11 = adjacentOffCaptures.Single(value =>
                    value.Power.Id == "P100_P100" && value.Pose.Percent == 0 &&
                    value.CameraToken == cameraToken);
                string expectedSourceP100Path = GetExpectedSourceP100Path(cameraToken);
                string expectedSourceP100Sha = GetExpectedSourceP100Sha(cameraToken);
                byte[] expectedSourceP100Bytes = ReadAndValidatePinnedSourcePng(
                    expectedSourceP100Path,
                    expectedSourceP100Sha,
                    cameraToken);
                PngDifferenceMetrics p11PresentationDifference = CompareDecodedPng(
                    adjacentOffP11.PngBytes,
                    expectedSourceP100Bytes,
                    cameraToken + " Adjacent OFF P11/D0 vs pinned production P100");
                if (adjacentOffP11.IsTransportActive ||
                    !p11PresentationDifference.WithinTolerance)
                {
                    throw new InvalidOperationException(
                        "Adjacent OFF P11/D0 source-production presentation parity failed: " +
                        cameraToken + "; actual=" + adjacentOffP11.PngSha +
                        "; pinnedReference=" + expectedSourceP100Sha +
                        "; presentationDifference=" +
                        p11PresentationDifference.ToInvariantString() + ".");
                }
            }
        }

        private static string GetExpectedSourceP100Path(string cameraToken)
        {
            return cameraToken == "S2A"
                ? ExpectedSourceP100S2APresentationPngPath
                : ExpectedSourceP100A2SPresentationPngPath;
        }

        private static string GetExpectedSourceP100Sha(string cameraToken)
        {
            return cameraToken == "S2A"
                ? ExpectedSourceP100S2APresentationPngSha256
                : ExpectedSourceP100A2SPresentationPngSha256;
        }

        private static byte[] ReadAndValidatePinnedSourcePng(
            string assetPath,
            string expectedSha,
            string cameraToken)
        {
            string absolutePath =
                KExactBasisV1EditorContract.AssetPathToAbsolutePath(assetPath);
            if (!File.Exists(absolutePath))
                throw new FileNotFoundException(
                    "Pinned source-production PNG is missing for " + cameraToken + ".",
                    absolutePath);
            byte[] bytes = File.ReadAllBytes(absolutePath);
            VerifyPng(bytes, cameraToken + " pinned source-production P100 PNG");
            string actualSha = KExactBasisV1EditorContract.ComputeSha256(bytes);
            if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Pinned source-production PNG hash changed for " + cameraToken +
                    ": actual=" + actualSha + ", expected=" + expectedSha + ".");
            }
            return bytes;
        }

        private static PngDifferenceMetrics CompareDecodedPng(
            byte[] leftBytes,
            byte[] rightBytes,
            string label)
        {
            VerifyPng(leftBytes, label + " left");
            VerifyPng(rightBytes, label + " right");
            Texture2D left = null;
            Texture2D right = null;
            try
            {
                left = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                right = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (!ImageConversion.LoadImage(left, leftBytes, false) ||
                    !ImageConversion.LoadImage(right, rightBytes, false) ||
                    left.width != CaptureWidth || left.height != CaptureHeight ||
                    right.width != CaptureWidth || right.height != CaptureHeight)
                {
                    throw new InvalidOperationException(
                        "Could not decode 1920x1080 PNG pair: " + label + ".");
                }

                Color32[] leftPixels = left.GetPixels32();
                Color32[] rightPixels = right.GetPixels32();
                if (leftPixels.Length != rightPixels.Length || leftPixels.Length == 0)
                    throw new InvalidOperationException(
                        "Decoded PNG pixel count mismatch: " + label + ".");

                long totalAbsoluteChannelDelta = 0L;
                long changedPixelCount = 0L;
                int maximumChannelDelta = 0;
                int maximumAlphaDelta = 0;
                for (int i = 0; i < leftPixels.Length; i++)
                {
                    Color32 leftPixel = leftPixels[i];
                    Color32 rightPixel = rightPixels[i];
                    int red = Math.Abs(leftPixel.r - rightPixel.r);
                    int green = Math.Abs(leftPixel.g - rightPixel.g);
                    int blue = Math.Abs(leftPixel.b - rightPixel.b);
                    int alpha = Math.Abs(leftPixel.a - rightPixel.a);
                    totalAbsoluteChannelDelta += red + green + blue;
                    maximumChannelDelta = Math.Max(
                        maximumChannelDelta,
                        Math.Max(red, Math.Max(green, blue)));
                    maximumAlphaDelta = Math.Max(maximumAlphaDelta, alpha);
                    if (red != 0 || green != 0 || blue != 0)
                        changedPixelCount++;
                }

                return new PngDifferenceMetrics(
                    totalAbsoluteChannelDelta / (leftPixels.Length * 3d),
                    maximumChannelDelta,
                    maximumAlphaDelta,
                    changedPixelCount / (double)leftPixels.Length);
            }
            finally
            {
                if (left != null)
                    Object.DestroyImmediate(left);
                if (right != null)
                    Object.DestroyImmediate(right);
            }
        }

        private static void ValidateSemanticReceiverSurfaceSignal(
            List<CaptureRecord> captures,
            string poweredState,
            string camera,
            string cameraToken,
            string label)
        {
            double previousDelta = double.NegativeInfinity;
            double d100Delta = double.NaN;
            for (int poseIndex = 0; poseIndex < DoorPoses.Length; poseIndex++)
            {
                DoorPose pose = DoorPoses[poseIndex];
                CaptureRecord powered = captures.Single(value =>
                    value.Power.Id == poweredState &&
                    value.Pose.Percent == pose.Percent &&
                    value.Camera == camera);
                if (!string.Equals(powered.CameraToken, cameraToken,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Semantic receiver camera token changed: " + label + ".");
                }

                double delta = powered.SemanticReceiverMeanDeltaFromBaseline;
                if (!double.IsFinite(powered.SemanticReceiverMeanLuminance) ||
                    !double.IsFinite(powered.SemanticReceiverBaselineMean) ||
                    !double.IsFinite(delta))
                {
                    throw new InvalidOperationException(
                        "Semantic receiver signal is non-finite: " + label +
                        " D" + pose.Percent + ".");
                }
                if (poseIndex > 0 &&
                    delta + SemanticReceiverMonotonicTolerance < previousDelta)
                {
                    throw new InvalidOperationException(
                        "Semantic receiver signal is not nondecreasing: " + label +
                        " D" + DoorPoses[poseIndex - 1].Percent + "=" +
                        previousDelta.ToString("R", CultureInfo.InvariantCulture) +
                        ", D" + pose.Percent + "=" +
                        delta.ToString("R", CultureInfo.InvariantCulture) +
                        ", tolerance=" +
                        SemanticReceiverMonotonicTolerance.ToString(
                            "R", CultureInfo.InvariantCulture) +
                        "; A2S profile=" + FormatSemanticProfile(
                            captures,
                            "P100_P000",
                            KExactBasisV1EditorContract.AdministrativeCameraName) +
                        "; S2A profile=" + FormatSemanticProfile(
                            captures,
                            "P000_P100",
                            KExactBasisV1EditorContract.StartCameraName) + ".");
                }
                previousDelta = delta;
                if (pose.Percent == 100)
                    d100Delta = delta;
            }

            if (!double.IsFinite(d100Delta) ||
                d100Delta < MinimumSemanticReceiverD100Delta)
            {
                throw new InvalidOperationException(
                    "Semantic receiver D100 gate failed: " + label + " delta=" +
                    d100Delta.ToString("R", CultureInfo.InvariantCulture) +
                    ", minimum=" +
                    MinimumSemanticReceiverD100Delta.ToString(
                        "R", CultureInfo.InvariantCulture) +
                    "; A2S profile=" + FormatSemanticProfile(
                        captures,
                        "P100_P000",
                        KExactBasisV1EditorContract.AdministrativeCameraName) +
                    "; S2A profile=" + FormatSemanticProfile(
                        captures,
                        "P000_P100",
                        KExactBasisV1EditorContract.StartCameraName) + ".");
            }
        }

        private static string FormatSemanticProfile(
            List<CaptureRecord> captures,
            string powerState,
            string camera)
        {
            return string.Join(
                ",",
                DoorPoses.Select(pose =>
                {
                    CaptureRecord record = captures.Single(value =>
                        value.Power.Id == powerState &&
                        value.Pose.Percent == pose.Percent &&
                        value.Camera == camera);
                    return "D" + pose.Percent + "=" +
                           record.SemanticReceiverMeanDeltaFromBaseline.ToString(
                               "R", CultureInfo.InvariantCulture);
                }));
        }

        private static string BuildManifest(
            string utc,
            string builtSceneSha,
            string adjacentOffFingerprint,
            List<CaptureRecord> adjacentOffCaptures,
            List<CaptureRecord> captures)
        {
            var builder = new StringBuilder(128 * 1024);
            builder.AppendLine("schema=" + Schema);
            builder.AppendLine("status=" + Status);
            builder.AppendLine("captureOutcome=COMPLETE");
            builder.AppendLine("captureUtc=" + utc);
            builder.AppendLine("visualVerdict=UNREVIEWED");
            builder.AppendLine("visualFitClaimed=false");
            builder.AppendLine("visualParityClaimed=false");
            builder.AppendLine("bakeRealtimeEquivalenceClaimed=false");
            builder.AppendLine("captureWidth=" + CaptureWidth);
            builder.AppendLine("captureHeight=" + CaptureHeight);
            builder.AppendLine("stateCameraRecordCount=" + captures.Count);
            builder.AppendLine("powerStateCount=4");
            builder.AppendLine("doorPoseCount=5");
            builder.AppendLine("cameraCount=2");
            builder.AppendLine("doorPoseIdentity=physical hinge angle percent");
            builder.AppendLine("doorAngleFractions=0,0.25,0.5,0.75,1");
            builder.AppendLine(
                "doorProjectedApertureFractions=0,0.07612047,0.2928932," +
                "0.6173166,1");
            builder.AppendLine("presentationPngCount=40");
            builder.AppendLine("linearHdrExrCount=40");
            builder.AppendLine("semanticReceiverSurfaceGateValidated=true");
            builder.AppendLine(
                "semanticReceiverRoiCoordinateSystem=top-left;half-open;1920x1080");
            builder.AppendLine(
                "semanticReceiverRoi.A2S=[580,180,790,920];" +
                "[1120,180,1400,920];[580,930,1400,1080]");
            builder.AppendLine(
                "semanticReceiverRoi.S2A=[400,180,780,930];" +
                "[1120,180,1550,930];[400,930,1550,1080]");
            builder.AppendLine("semanticReceiverPoweredState.A2S=P100_P000");
            builder.AppendLine("semanticReceiverPoweredState.S2A=P000_P100");
            builder.AppendLine("semanticReceiverBaselineState=P000_P000;sameDoorPose");
            builder.AppendLine("semanticReceiverMinimumD100Delta=" +
                               KExactBasisV1EditorContract.FormatDouble(
                                   MinimumSemanticReceiverD100Delta));
            builder.AppendLine("semanticReceiverMonotonicTolerance=" +
                               KExactBasisV1EditorContract.FormatDouble(
                                   SemanticReceiverMonotonicTolerance));
            builder.AppendLine("closedDoorSemanticReceiverLeakageValidated=true");
            builder.AppendLine("closedDoorSemanticReceiverLeakageMetric=" +
                               "linearHDR receiver ROI mean-absolute and mean-positive");
            builder.AppendLine("closedDoorSemanticReceiverMaximumMeanLeakage=" +
                               KExactBasisV1EditorContract.FormatDouble(
                                   MaximumClosedDoorSemanticMeanLeakage));
            AppendClosedDoorLeakageProof(builder, captures);
            builder.AppendLine("validationSceneActualSha256=" +
                               KExactBasisV1EditorContract.ExpectedSourceSceneSha256.ToLowerInvariant());
            builder.AppendLine("referenceManifestPath=" +
                               KExactBasisV1EditorContract.CorrectedRealtimeManifestPath);
            builder.AppendLine("referenceManifestSha256=" +
                               KExactBasisV1EditorContract.ExpectedCorrectedRealtimeManifestSha256
                                   .ToLowerInvariant());
            builder.AppendLine("kexactBasisManifestPath=" +
                               KExactBasisV1EditorContract.KExactSelectionManifestPath);
            builder.AppendLine("kexactBasisManifestSha256=" +
                               KExactBasisV1EditorContract.ExpectedKExactSelectionManifestSha256
                                   .ToLowerInvariant());
            builder.AppendLine("selectionFingerprintSha256=" +
                               KExactBasisV1EditorContract.ExpectedSelectionFingerprintSha256
                                   .ToLowerInvariant());
            builder.AppendLine("strictSelectedStartK=3");
            builder.AppendLine("strictSelectedAdministrativeK=2");
            builder.AppendLine("builtScenePath=" + KExactBasisV1EditorContract.BuiltScenePath);
            builder.AppendLine("builtSceneSha256=" + builtSceneSha.ToLowerInvariant());
            builder.AppendLine("buildManifestPath=" +
                               KExactBasisV1EditorContract.BuildManifestPath);
            builder.AppendLine("buildManifestSha256=" +
                               KExactBasisV1EditorContract.ComputeFileSha256(
                                   KExactBasisV1EditorContract.BuildManifestPath));
            AppendRuntimeSourceHash(
                builder,
                "runtimeSource.KExactPortalConnection",
                KExactBasisV1EditorContract.OwnedRoot +
                "/Runtime/KExactPortalConnection.cs");
            AppendRuntimeSourceHash(
                builder,
                "runtimeSource.KExactRuntimeLight",
                KExactBasisV1EditorContract.OwnedRoot +
                "/Runtime/KExactRuntimeLight.cs");
            AppendRuntimeSourceHash(
                builder,
                "runtimeSource.KExactRuntimeContracts",
                KExactBasisV1EditorContract.OwnedRoot +
                "/Runtime/KExactRuntimeContracts.cs");
            AppendRuntimeSourceHash(
                builder,
                "runtimeSource.KExactRenderingLayerRegistry",
                KExactBasisV1EditorContract.OwnedRoot +
                "/Runtime/KExactRenderingLayerRegistry.cs");
            AppendRuntimeSourceHash(
                builder,
                "editorSource.KExactBasisV1SmokeCapture",
                KExactBasisV1EditorContract.OwnedRoot +
                "/Editor/KExactBasisV1SmokeCapture.cs");
            builder.AppendLine("adjacentOffRendererParityValidated=true");
            builder.AppendLine("adjacentOffAdditionalRendererCount=0");
            builder.AppendLine("adjacentOffRendererFingerprintSha256=" +
                               adjacentOffFingerprint);
            builder.AppendLine("adjacentOffLinearHdrPixelParityValidated=true");
            builder.AppendLine("adjacentOffPresentationDitherToleranceValidated=true");
            builder.AppendLine(
                "adjacentOffPixelParityContract=linearHDR-byte-and-hash-exact;" +
                "presentation-decoded-RGB8-within-temporal-dither-tolerance");
            builder.AppendLine("presentationMaximumRgb8ChannelDeltaThreshold=" +
                               PresentationMaximumRgb8ChannelDelta);
            builder.AppendLine("presentationMeanAbsoluteRgb8ChannelDeltaThreshold=" +
                               KExactBasisV1EditorContract.FormatDouble(
                                   PresentationMeanAbsoluteRgb8ChannelDelta));
            builder.AppendLine("adjacentOffManagerInactiveDuringCapture=true");
            builder.AppendLine("adjacentOffTransportInactiveDuringCapture=true");
            builder.AppendLine("adjacentOffInMemoryRecordCount=4");
            builder.AppendLine("adjacentOffPublishedImageCount=0");
            AppendAdjacentOffPixelProof(builder, adjacentOffCaptures, captures);
            builder.AppendLine("oldPocRootDisabledInCloneOnly=true");
            builder.AppendLine("sourceAndProductionFilesUnchanged=true");
            builder.AppendLine("renderApi=RenderPipeline.StandardRequest");
            builder.AppendLine("renderFailureGate=RenderGraph;ReflectionProbeManager;" +
                               "ForwardLights;ZBinning;capture-stack");
            builder.AppendLine(
                "doorTransmission=smoothed aperture scalar gates direct, receiver bounce, " +
                "and reflection; physical moving door leaf also casts realtime shadows");
            builder.AppendLine(
                "directTransportAperturePolicy=source power multiplied by actual " +
                "projected aperture fraction");
            builder.AppendLine("directTransportClosedEndpoint=0");
            builder.AppendLine("directTransportOpenEndpoint=1");
            builder.AppendLine("doorLeafReceivesDedicatedBidirectionalDoorSurfaceLayer=true");
            builder.AppendLine("doorSurfacePowerIndependentOfAperture=true");
            builder.AppendLine("doorSurfaceRuntimeIntensityValidatedPerState=true");
            builder.AppendLine("doorProbeCapture=physical pose synchronized; " +
                               "smoothing temporarily disabled; ForceRefresh per state; " +
                               "configuration/internal state/MPBs restored");
            builder.AppendLine("p0ResidualReflection=true");
            builder.AppendLine("pairOrCrossTermUsed=false");

            for (int i = 0; i < captures.Count; i++)
            {
                CaptureRecord record = captures[i];
                string prefix = "capture[" + i + "].";
                builder.AppendLine(prefix + "status=" + Status);
                builder.AppendLine(prefix + "identity=" + record.Power.Id + "|D" +
                                   record.Pose.Percent.ToString("000", CultureInfo.InvariantCulture) +
                                   "|" + record.Camera);
                builder.AppendLine(prefix + "power=" + record.Power.Id);
                builder.AppendLine(prefix + "startPower=" +
                                   (record.Power.StartOn ? "P100" : "P0"));
                builder.AppendLine(prefix + "administrativePower=" +
                                   (record.Power.AdministrativeOn ? "P100" : "P0"));
                builder.AppendLine(prefix + "doorPercent=" + record.Pose.Percent);
                builder.AppendLine(prefix + "doorAngleFraction=" +
                                   KExactBasisV1EditorContract.FormatFloat(
                                       record.Pose.Fraction));
                builder.AppendLine(prefix + "doorFraction=" +
                                   KExactBasisV1EditorContract.FormatFloat(
                                       record.Weights.DoorOpenness01));
                builder.AppendLine(prefix + "doorApertureFraction=" +
                                   KExactBasisV1EditorContract.FormatFloat(
                                       record.Weights.DoorOpenness01));
                builder.AppendLine(prefix + "doorAngleDegrees=" +
                                   KExactBasisV1EditorContract.FormatFloat(record.DoorAngleDegrees));
                builder.AppendLine(prefix + "camera=" + record.Camera);
                builder.AppendLine(prefix + "cameraToken=" + record.CameraToken);
                builder.AppendLine(prefix + "presentationPng=" + record.PngFilename);
                builder.AppendLine(prefix + "presentationPngSha256=" + record.PngSha);
                builder.AppendLine(prefix + "presentationPngBytes=" +
                                   record.PngBytes.LongLength);
                builder.AppendLine(prefix + "linearHdrExr=" + record.ExrFilename);
                builder.AppendLine(prefix + "linearHdrExrSha256=" + record.ExrSha);
                builder.AppendLine(prefix + "linearHdrExrBytes=" +
                                   record.ExrBytes.LongLength);
                builder.AppendLine(prefix + "samePoseP0P0Baseline=" +
                                   record.BaselineExrFilename);
                builder.AppendLine(prefix + "meanLinearLuminance=" +
                                   KExactBasisV1EditorContract.FormatDouble(
                                       record.MeanLuminance));
                builder.AppendLine(prefix + "maxLinearLuminance=" +
                                   KExactBasisV1EditorContract.FormatFloat(
                                       record.MaximumLuminance));
                builder.AppendLine(prefix + "p0p0BaselineMeanLinearLuminance=" +
                                   KExactBasisV1EditorContract.FormatDouble(
                                       record.BaselineMean));
                builder.AppendLine(prefix + "meanLinearLuminanceDeltaFromP0P0=" +
                                   KExactBasisV1EditorContract.FormatDouble(
                                       record.MeanDeltaFromBaseline));
                builder.AppendLine(prefix + "semanticReceiverMeanLinearLuminance=" +
                                   KExactBasisV1EditorContract.FormatDouble(
                                       record.SemanticReceiverMeanLuminance));
                builder.AppendLine(
                    prefix + "semanticReceiverP0P0BaselineMeanLinearLuminance=" +
                    KExactBasisV1EditorContract.FormatDouble(
                        record.SemanticReceiverBaselineMean));
                builder.AppendLine(
                    prefix + "semanticReceiverMeanLinearLuminanceDeltaFromP0P0=" +
                    KExactBasisV1EditorContract.FormatDouble(
                        record.SemanticReceiverMeanDeltaFromBaseline));
                builder.AppendLine(prefix + "cameraPosition=" +
                                   KExactBasisV1EditorContract.FormatVector3(
                                       record.CameraPosition));
                builder.AppendLine(prefix + "cameraRotation=" +
                                   KExactBasisV1EditorContract.FormatQuaternion(
                                       record.CameraRotation));
                builder.AppendLine(prefix + "fieldOfView=" +
                                   KExactBasisV1EditorContract.FormatFloat(record.FieldOfView));
                builder.AppendLine(prefix + "nearClip=" +
                                   KExactBasisV1EditorContract.FormatFloat(record.NearClip));
                builder.AppendLine(prefix + "farClip=" +
                                   KExactBasisV1EditorContract.FormatFloat(record.FarClip));
                builder.AppendLine(prefix + "cullingMask=" + record.CullingMask);
                builder.AppendLine(prefix + "allowHDR=" + record.AllowHdr);
                builder.AppendLine(prefix + "allowMSAA=" + record.AllowMsaa);
                builder.AppendLine(prefix + "presentationRenderTargetMSAA=" +
                                   record.PresentationMsaa);
                builder.AppendLine(prefix + "presentationPostProcessing=" +
                                   record.PresentationPost);
                builder.AppendLine(prefix + "hdrPostProcessing=" + record.HdrPost);
                builder.AppendLine(prefix + "connectionKey=" + record.ConnectionKey);
                builder.AppendLine(prefix + "isTransportActive=" +
                                   record.IsTransportActive);
                builder.AppendLine(prefix + "isFaultLatched=" + record.IsFaultLatched);
                builder.AppendLine(prefix + "faultReason=" +
                                   KExactBasisV1EditorContract.Sanitize(record.FaultReason));
                builder.AppendLine(prefix + "parityValidated=" + record.ParityValidated);
                builder.AppendLine(prefix + "parityFailure=" +
                                   KExactBasisV1EditorContract.Sanitize(record.ParityFailure));
                builder.AppendLine(prefix + "smoothedDoorOpenness01=" +
                                   KExactBasisV1EditorContract.FormatFloat(
                                       record.Weights.DoorOpenness01));
                builder.AppendLine(prefix + "powerAToB01=" +
                                   KExactBasisV1EditorContract.FormatFloat(
                                       record.Weights.PowerAToB01));
                builder.AppendLine(prefix + "powerBToA01=" +
                                   KExactBasisV1EditorContract.FormatFloat(
                                       record.Weights.PowerBToA01));
                builder.AppendLine(prefix + "directScaleAToB=" +
                                   KExactBasisV1EditorContract.FormatFloat(
                                       record.Weights.DirectScaleAToB));
                builder.AppendLine(prefix + "directScaleBToA=" +
                                   KExactBasisV1EditorContract.FormatFloat(
                                       record.Weights.DirectScaleBToA));
                builder.AppendLine(prefix + "reflectionWeightAToB=" +
                                   KExactBasisV1EditorContract.FormatFloat(
                                       record.Weights.ReflectionWeightAToB));
                builder.AppendLine(prefix + "reflectionWeightBToA=" +
                                   KExactBasisV1EditorContract.FormatFloat(
                                       record.Weights.ReflectionWeightBToA));
                builder.AppendLine(prefix + "receiverBounceTotalIntensityAToB=" +
                                   KExactBasisV1EditorContract.FormatFloat(
                                       record.ReceiverBounceTotalIntensityAToB));
                builder.AppendLine(prefix + "receiverBounceTotalIntensityBToA=" +
                                   KExactBasisV1EditorContract.FormatFloat(
                                       record.ReceiverBounceTotalIntensityBToA));
                builder.AppendLine(prefix + "doorSurfaceTotalIntensityAToB=" +
                                   KExactBasisV1EditorContract.FormatFloat(
                                       record.DoorSurfaceTotalIntensityAToB));
                builder.AppendLine(prefix + "doorSurfaceTotalIntensityBToA=" +
                                   KExactBasisV1EditorContract.FormatFloat(
                                       record.DoorSurfaceTotalIntensityBToA));
            }
            return builder.ToString();
        }

        private static void AppendClosedDoorLeakageProof(
            StringBuilder builder,
            List<CaptureRecord> captures)
        {
            AppendClosedDoorLeakageDirection(
                builder,
                captures,
                "P100_P000",
                KExactBasisV1EditorContract.AdministrativeCameraName,
                "A2S");
            AppendClosedDoorLeakageDirection(
                builder,
                captures,
                "P000_P100",
                KExactBasisV1EditorContract.StartCameraName,
                "S2A");
        }

        private static void AppendClosedDoorLeakageDirection(
            StringBuilder builder,
            List<CaptureRecord> captures,
            string poweredState,
            string camera,
            string cameraToken)
        {
            CaptureRecord baseline = captures.Single(value =>
                value.Power.IsP0P0 && value.Pose.Percent == 0 &&
                value.Camera == camera);
            CaptureRecord powered = captures.Single(value =>
                value.Power.Id == poweredState && value.Pose.Percent == 0 &&
                value.Camera == camera);
            LinearDifferenceMetrics metrics = CompareLinearExrInSemanticReceiver(
                baseline.ExrBytes,
                powered.ExrBytes,
                cameraToken,
                cameraToken + " manifest closed-door proof");
            string prefix = "closedDoorLeakage." + cameraToken + ".";
            builder.AppendLine(prefix + "poweredState=" + poweredState);
            builder.AppendLine(prefix + "baselineState=P000_P000");
            builder.AppendLine(prefix + "doorPercent=0");
            builder.AppendLine(prefix + "meanAbsoluteLinearLuminanceDelta=" +
                               KExactBasisV1EditorContract.FormatDouble(
                                   metrics.MeanAbsoluteLuminanceDelta));
            builder.AppendLine(prefix + "meanPositiveLinearLuminanceDelta=" +
                               KExactBasisV1EditorContract.FormatDouble(
                                   metrics.MeanPositiveLuminanceDelta));
            builder.AppendLine(prefix + "withinThreshold=true");
        }

        private static void AppendRuntimeSourceHash(
            StringBuilder builder,
            string prefix,
            string assetPath)
        {
            builder.AppendLine(prefix + ".path=" + assetPath);
            builder.AppendLine(prefix + ".sha256=" +
                               KExactBasisV1EditorContract.ComputeFileSha256(
                                   assetPath));
        }

        private static void AppendAdjacentOffPixelProof(
            StringBuilder builder,
            List<CaptureRecord> adjacentOffCaptures,
            List<CaptureRecord> activeCaptures)
        {
            string[] cameraTokens = { "S2A", "A2S" };
            for (int cameraIndex = 0; cameraIndex < cameraTokens.Length; cameraIndex++)
            {
                string cameraToken = cameraTokens[cameraIndex];
                string prefix = "adjacentOff." + cameraToken + ".";
                CaptureRecord adjacentOffP00 = adjacentOffCaptures.Single(value =>
                    value.Power.Id == "P000_P000" && value.Pose.Percent == 0 &&
                    value.CameraToken == cameraToken);
                CaptureRecord activeP00 = activeCaptures.Single(value =>
                    value.Power.Id == "P000_P000" && value.Pose.Percent == 0 &&
                    value.CameraToken == cameraToken);
                CaptureRecord adjacentOffP11 = adjacentOffCaptures.Single(value =>
                    value.Power.Id == "P100_P100" && value.Pose.Percent == 0 &&
                    value.CameraToken == cameraToken);
                string expectedSourceP100Path = GetExpectedSourceP100Path(cameraToken);
                string expectedSourceP100Sha = GetExpectedSourceP100Sha(cameraToken);
                byte[] expectedSourceP100Bytes = ReadAndValidatePinnedSourcePng(
                    expectedSourceP100Path,
                    expectedSourceP100Sha,
                    cameraToken);
                bool p00PresentationBytesEqual =
                    adjacentOffP00.PngBytes.SequenceEqual(activeP00.PngBytes);
                bool p00PresentationHashEqual = string.Equals(
                    adjacentOffP00.PngSha,
                    activeP00.PngSha,
                    StringComparison.OrdinalIgnoreCase);
                bool p00LinearBytesEqual =
                    adjacentOffP00.ExrBytes.SequenceEqual(activeP00.ExrBytes);
                bool p00LinearHashEqual = string.Equals(
                    adjacentOffP00.ExrSha,
                    activeP00.ExrSha,
                    StringComparison.OrdinalIgnoreCase);
                bool p11PresentationBytesEqual =
                    adjacentOffP11.PngBytes.SequenceEqual(expectedSourceP100Bytes);
                bool p11PresentationHashEqual = string.Equals(
                    adjacentOffP11.PngSha,
                    expectedSourceP100Sha,
                    StringComparison.OrdinalIgnoreCase);
                PngDifferenceMetrics p00PresentationDifference = CompareDecodedPng(
                    adjacentOffP00.PngBytes,
                    activeP00.PngBytes,
                    cameraToken + " manifest P00 presentation proof");
                PngDifferenceMetrics p11PresentationDifference = CompareDecodedPng(
                    adjacentOffP11.PngBytes,
                    expectedSourceP100Bytes,
                    cameraToken + " manifest P11 presentation proof");

                builder.AppendLine(prefix + "p00D0PresentationPngSha256=" +
                                   adjacentOffP00.PngSha);
                builder.AppendLine(prefix + "activeP00D0PresentationPngSha256=" +
                                   activeP00.PngSha);
                builder.AppendLine(prefix + "p00D0PresentationBytesEqual=" +
                                   FormatBoolean(p00PresentationBytesEqual));
                builder.AppendLine(prefix + "p00D0PresentationHashEqual=" +
                                   FormatBoolean(p00PresentationHashEqual));
                AppendPresentationDifference(
                    builder, prefix + "p00D0Presentation", p00PresentationDifference);
                builder.AppendLine(prefix + "p00D0LinearHdrExrSha256=" +
                                   adjacentOffP00.ExrSha);
                builder.AppendLine(prefix + "activeP00D0LinearHdrExrSha256=" +
                                   activeP00.ExrSha);
                builder.AppendLine(prefix + "p00D0LinearHdrBytesEqual=" +
                                   FormatBoolean(p00LinearBytesEqual));
                builder.AppendLine(prefix + "p00D0LinearHdrHashEqual=" +
                                   FormatBoolean(p00LinearHashEqual));
                builder.AppendLine(prefix + "p11D0PresentationPngSha256=" +
                                   adjacentOffP11.PngSha);
                builder.AppendLine(prefix +
                                   "sourceProductionDefaultP100PresentationPngPath=" +
                                   expectedSourceP100Path);
                builder.AppendLine(prefix +
                                   "sourceProductionDefaultP100PresentationPngSha256=" +
                                   expectedSourceP100Sha);
                builder.AppendLine(prefix +
                                   "sourceProductionDefaultP100ReferenceHashValidated=true");
                builder.AppendLine(prefix +
                                   "sourceProductionDefaultP100PresentationBytesEqual=" +
                                   FormatBoolean(p11PresentationBytesEqual));
                builder.AppendLine(prefix +
                                   "sourceProductionDefaultP100PresentationHashEqual=" +
                                   FormatBoolean(p11PresentationHashEqual));
                AppendPresentationDifference(
                    builder, prefix + "p11D0Presentation", p11PresentationDifference);
                builder.AppendLine(prefix + "p11D0LinearHdrExrSha256=" +
                                   adjacentOffP11.ExrSha);
            }
        }

        private static void AppendPresentationDifference(
            StringBuilder builder,
            string prefix,
            PngDifferenceMetrics metrics)
        {
            builder.AppendLine(prefix + "MeanAbsoluteRgb8ChannelDelta=" +
                               KExactBasisV1EditorContract.FormatDouble(
                                   metrics.MeanAbsoluteChannelDelta));
            builder.AppendLine(prefix + "MaximumRgb8ChannelDelta=" +
                               metrics.MaximumChannelDelta);
            builder.AppendLine(prefix + "MaximumAlpha8ChannelDelta=" +
                               metrics.MaximumAlphaDelta);
            builder.AppendLine(prefix + "AlphaExact=" +
                               FormatBoolean(metrics.MaximumAlphaDelta == 0));
            builder.AppendLine(prefix + "ChangedPixelFraction=" +
                               KExactBasisV1EditorContract.FormatDouble(
                                   metrics.ChangedPixelFraction));
            builder.AppendLine(prefix + "DitherToleranceValidated=" +
                               FormatBoolean(metrics.WithinTolerance));
        }

        private static string FormatBoolean(bool value)
        {
            return value ? "true" : "false";
        }

        private static string PublishEvidence(
            string outputFolder,
            List<CaptureRecord> captures,
            string manifest)
        {
            string finalAbsolute = KExactBasisV1EditorContract.AssetPathToAbsolutePath(
                outputFolder);
            string parent = Path.GetDirectoryName(finalAbsolute);
            if (string.IsNullOrEmpty(parent))
                throw new InvalidOperationException("Evidence parent path is invalid.");
            Directory.CreateDirectory(parent);
            KExactBasisV1EditorContract.AssertAbsolutePathLength(
                finalAbsolute, "canonical evidence directory");
            KExactBasisV1EditorContract.AssertAbsolutePathLength(
                finalAbsolute + ".meta", "canonical evidence directory .meta");
            if (Directory.Exists(finalAbsolute) || File.Exists(finalAbsolute))
                throw new IOException("Evidence destination already exists.");
            string staging = Path.Combine(
                parent,
                "__S_" + Guid.NewGuid().ToString("N").Substring(0, 12));
            KExactBasisV1EditorContract.AssertAbsolutePathLength(
                staging, "evidence staging directory");
            Directory.CreateDirectory(staging);
            bool published = false;
            string manifestName = "manifest_" + Status + ".txt";
            byte[] manifestBytes = new UTF8Encoding(false).GetBytes(manifest);
            string manifestSha = KExactBasisV1EditorContract.ComputeSha256(manifestBytes);
            try
            {
                var expected = new Dictionary<string, FileDeclaration>(StringComparer.Ordinal);
                for (int i = 0; i < captures.Count; i++)
                {
                    CaptureRecord record = captures[i];
                    WriteAndVerify(staging, record.PngFilename, record.PngBytes,
                        record.PngSha, FileKind.Png);
                    expected.Add(record.PngFilename,
                        new FileDeclaration(record.PngSha, record.PngBytes.LongLength));
                    WriteAndVerify(staging, record.ExrFilename, record.ExrBytes,
                        record.ExrSha, FileKind.Exr);
                    expected.Add(record.ExrFilename,
                        new FileDeclaration(record.ExrSha, record.ExrBytes.LongLength));
                }
                WriteAndVerify(staging, manifestName, manifestBytes, manifestSha, FileKind.Text);
                expected.Add(manifestName,
                    new FileDeclaration(manifestSha, manifestBytes.LongLength));
                File.WriteAllText(
                    Path.Combine(staging, "STAGING_STATE.txt"),
                    "status=STAGED\nphase=RAW_FILES_VERIFIED_BEFORE_ATOMIC_PUBLISH\n",
                    new UTF8Encoding(false));

                Directory.Move(staging, finalAbsolute);
                published = true;
                AssetDatabase.Refresh(
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);

                foreach (KeyValuePair<string, FileDeclaration> pair in expected)
                {
                    string assetPath = outputFolder + "/" + pair.Key;
                    string absolute = KExactBasisV1EditorContract.AssetPathToAbsolutePath(assetPath);
                    KExactBasisV1EditorContract.AssertAbsolutePathLength(
                        absolute, "published evidence artifact");
                    KExactBasisV1EditorContract.AssertAbsolutePathLength(
                        absolute + ".meta", "published evidence artifact .meta");
                    if (!File.Exists(absolute) ||
                        new FileInfo(absolute).Length != pair.Value.Bytes ||
                        !string.Equals(
                            KExactBasisV1EditorContract.ComputeFileSha256(assetPath),
                            pair.Value.Sha,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("Published artifact hash/length failed: " + pair.Key);
                    }
                    if ((pair.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                         pair.Key.EndsWith(".exr", StringComparison.OrdinalIgnoreCase)) &&
                        AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) == null)
                        throw new IOException("Published image import failed: " + pair.Key);
                }

                string statePath = Path.Combine(finalAbsolute, "CAPTURE_STATE.txt");
                KExactBasisV1EditorContract.AssertAbsolutePathLength(
                    statePath + ".meta", "CAPTURE_STATE .meta");
                File.WriteAllText(
                    statePath,
                    "status=COMPLETE\n" +
                    "captureStatus=" + Status + "\n" +
                    "phase=PUBLISHED_IMPORTED_AND_VERIFIED\n" +
                    "stateCameraRecordCount=40\n" +
                    "manifestSha256=" + manifestSha + "\n",
                    new UTF8Encoding(false));
                AssetDatabase.ImportAsset(
                    outputFolder + "/CAPTURE_STATE.txt",
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
                if (!File.Exists(statePath) ||
                    AssetDatabase.LoadAssetAtPath<TextAsset>(
                        outputFolder + "/CAPTURE_STATE.txt") == null)
                {
                    throw new IOException(
                        "CAPTURE_STATE was not imported after final commit receipt write.");
                }
                return manifestSha;
            }
            catch (Exception exception)
            {
                string failureFolder = published ? finalAbsolute : staging;
                try
                {
                    Directory.CreateDirectory(failureFolder);
                    File.WriteAllText(
                        Path.Combine(failureFolder, "CAPTURE_FAILED.txt"),
                        "status=FAILED\nreason=" +
                        KExactBasisV1EditorContract.Sanitize(exception.ToString()) + "\n",
                        new UTF8Encoding(false));
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
                catch
                {
                    // Preserve the original failure and the staging/canonical directory.
                }
                throw;
            }
        }

        private static void WriteAndVerify(
            string folder,
            string filename,
            byte[] bytes,
            string sha,
            FileKind kind)
        {
            if (Path.GetFileName(filename) != filename ||
                filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new IOException("Evidence filename is not a safe leaf: " + filename);
            string path = Path.Combine(folder, filename);
            KExactBasisV1EditorContract.AssertAbsolutePathLength(path, "staged artifact");
            KExactBasisV1EditorContract.AssertAbsolutePathLength(
                path + ".meta", "staged artifact .meta");
            if (kind == FileKind.Png)
                VerifyPng(bytes, filename);
            else if (kind == FileKind.Exr)
                VerifyExr(bytes, filename);
            File.WriteAllBytes(path, bytes);
            byte[] written = File.ReadAllBytes(path);
            if (written.LongLength != bytes.LongLength ||
                !string.Equals(
                    KExactBasisV1EditorContract.ComputeSha256(written),
                    sha,
                    StringComparison.OrdinalIgnoreCase))
                throw new IOException("Staged raw artifact verification failed: " + filename);
        }

        private static void VerifyPng(byte[] bytes, string label)
        {
            if (bytes == null || bytes.Length < 24 || bytes[0] != 0x89 ||
                bytes[1] != 0x50 || bytes[2] != 0x4e || bytes[3] != 0x47 ||
                ReadBigEndianInt32(bytes, 16) != CaptureWidth ||
                ReadBigEndianInt32(bytes, 20) != CaptureHeight)
                throw new IOException("Invalid 1920x1080 PNG: " + label);
        }

        private static void VerifyExr(byte[] bytes, string label)
        {
            if (bytes == null || bytes.Length < 8 || bytes[0] != 0x76 ||
                bytes[1] != 0x2f || bytes[2] != 0x31 || bytes[3] != 0x01)
                throw new IOException("Invalid EXR magic/version: " + label);
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) |
                   (bytes[offset + 2] << 8) | bytes[offset + 3];
        }

        private static void AssertEditorRuntimeIdentity(
            Scene scene,
            bool initialDirty,
            string builtSceneSha,
            KExactBasisV1EditorContract.SelectionSnapshot selection,
            KExactBasisV1EditorContract.ProtectedAssetSnapshot protectedAssets)
        {
            if (!Application.isPlaying || !EditorApplication.isPlaying ||
                SceneManager.GetActiveScene() != scene || !scene.isLoaded ||
                scene.isDirty != initialDirty ||
                KExactBasisV1EditorContract.CountLoadedNonPreviewScenes() != 1 ||
                !string.Equals(
                    KExactBasisV1EditorContract.ComputeFileSha256(
                        KExactBasisV1EditorContract.BuiltScenePath),
                    builtSceneSha,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Capture changed Play Mode, active scene, dirty state, or built scene bytes.");
            }
            selection.AssertRestored();
            protectedAssets.AssertUnchanged();
        }

        private static float ReadScalar(KExactScalarSource source, string label)
        {
            if (source == null || !source.IsConfigured || source.HasRuntimeOverride)
                throw new InvalidOperationException(label +
                    " source is missing, unconfigured, or already overridden.");
            if (!source.TryRead01(out float value, out string failure))
                throw new InvalidOperationException(label + " source is not readable: " + failure);
            return value;
        }

        private static bool Approximately(
            float left,
            float right,
            float tolerance = 0.0001f)
        {
            return Mathf.Abs(left - right) <= tolerance;
        }

        private static string ReadUniqueManifestValue(string text, string key)
        {
            string prefix = key + "=";
            string result = null;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
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

        private sealed class RuntimeHandles
        {
            internal readonly KExactPortalRuntimeManager Manager;
            internal readonly KExactPortalConnection Connection;
            internal readonly KExactScalarSource StartPower;
            internal readonly KExactScalarSource AdministrativePower;
            internal readonly KExactScalarSource DoorSource;
            internal readonly DungeonPortalDoorAngleSource DoorAngleSource;

            internal RuntimeHandles(
                KExactPortalRuntimeManager manager,
                KExactPortalConnection connection,
                KExactScalarSource startPower,
                KExactScalarSource administrativePower,
                KExactScalarSource doorSource,
                DungeonPortalDoorAngleSource doorAngleSource)
            {
                Manager = manager;
                Connection = connection;
                StartPower = startPower;
                AdministrativePower = administrativePower;
                DoorSource = doorSource;
                DoorAngleSource = doorAngleSource;
            }

            internal void RequireStableActiveState()
            {
                string failure = string.Empty;
                bool parityValid = Connection.TryValidateParity(out failure);
                if (!Manager.IsActive || Manager.IsFaultLatched ||
                    !string.IsNullOrEmpty(Manager.FaultReason) ||
                    !Connection.IsTransportActive || Connection.IsFaultLatched ||
                    !string.IsNullOrEmpty(Connection.FaultReason) ||
                    !parityValid)
                {
                    throw new InvalidOperationException(
                        "KExactBasisV1 runtime is not active/stable: " + failure);
                }
            }
        }

        private sealed class SmoothingSnapshot
        {
            private readonly float powerRise;
            private readonly float powerFall;
            private readonly float doorOpen;
            private readonly float doorClose;

            private SmoothingSnapshot(
                float powerRise,
                float powerFall,
                float doorOpen,
                float doorClose)
            {
                this.powerRise = powerRise;
                this.powerFall = powerFall;
                this.doorOpen = doorOpen;
                this.doorClose = doorClose;
            }

            internal static SmoothingSnapshot Capture(KExactPortalConnection connection)
            {
                var serialized = new SerializedObject(connection);
                serialized.UpdateIfRequiredOrScript();
                return new SmoothingSnapshot(
                    Read(serialized, "powerRiseSeconds"),
                    Read(serialized, "powerFallSeconds"),
                    Read(serialized, "doorOpenSeconds"),
                    Read(serialized, "doorCloseSeconds"));
            }

            internal void Restore(KExactPortalConnection connection)
            {
                connection.ConfigureSmoothing(powerRise, powerFall, doorOpen, doorClose);
            }

            private static float Read(SerializedObject serialized, string name)
            {
                SerializedProperty property = serialized.FindProperty(name);
                if (property == null || !float.IsFinite(property.floatValue))
                    throw new InvalidOperationException("Missing smoothing property: " + name);
                return property.floatValue;
            }
        }

        private sealed class DoorConfiguration
        {
            private readonly Quaternion closed;
            private readonly Vector3 axis;
            internal readonly float OpenAngleDegrees;

            private DoorConfiguration(Quaternion closed, Vector3 axis, float openAngle)
            {
                this.closed = closed;
                this.axis = axis;
                OpenAngleDegrees = openAngle;
            }

            internal static DoorConfiguration Capture(DungeonPortalDoorAngleSource source)
            {
                var serialized = new SerializedObject(source);
                serialized.UpdateIfRequiredOrScript();
                SerializedProperty closed = serialized.FindProperty("closedLocalRotation");
                SerializedProperty axis = serialized.FindProperty("localHingeAxis");
                SerializedProperty angle = serialized.FindProperty("openAngleDegrees");
                if (closed == null || axis == null || angle == null ||
                    axis.vector3Value.sqrMagnitude <= Mathf.Epsilon ||
                    Mathf.Abs(angle.floatValue - 90f) > 0.0001f)
                    throw new InvalidOperationException(
                        "Door configuration is unavailable or full-open angle is not exactly 90 degrees.");
                return new DoorConfiguration(
                    closed.quaternionValue,
                    axis.vector3Value.normalized,
                    angle.floatValue);
            }

            internal Quaternion RotationFor(float fraction)
            {
                return closed * Quaternion.AngleAxis(
                    OpenAngleDegrees * Mathf.Clamp01(fraction), axis);
            }
        }

        private sealed class DoorProbeReceiverSnapshot
        {
            private const BindingFlags PrivateInstance =
                BindingFlags.Instance | BindingFlags.NonPublic;

            private readonly DungeonDoorDualSideProbeReceiver receiver;
            private readonly FieldInfo smoothField;
            private readonly FieldInfo bindingsField;
            private readonly FieldInfo nextRefreshField;
            private readonly FieldInfo appliedField;
            private readonly bool smoothProbeChanges;
            private readonly Array bindings;
            private readonly float nextRefreshTime;
            private readonly bool hasAppliedCustomProbe;

            private DoorProbeReceiverSnapshot(
                DungeonDoorDualSideProbeReceiver receiver,
                FieldInfo smoothField,
                FieldInfo bindingsField,
                FieldInfo nextRefreshField,
                FieldInfo appliedField)
            {
                this.receiver = receiver;
                this.smoothField = smoothField;
                this.bindingsField = bindingsField;
                this.nextRefreshField = nextRefreshField;
                this.appliedField = appliedField;
                smoothProbeChanges = (bool)smoothField.GetValue(receiver);
                Array currentBindings = bindingsField.GetValue(receiver) as Array;
                if (currentBindings == null || currentBindings.Length == 0)
                    throw new InvalidOperationException(
                        "Door probe receiver has no runtime renderer bindings.");
                bindings = (Array)currentBindings.Clone();
                nextRefreshTime = (float)nextRefreshField.GetValue(receiver);
                hasAppliedCustomProbe = (bool)appliedField.GetValue(receiver);
            }

            internal static DoorProbeReceiverSnapshot Capture(
                KExactBasisV1EditorContract.SceneBindings scene)
            {
                DungeonDoorDualSideProbeReceiver[] receivers =
                    scene.DoorRoot.GetComponentsInChildren<
                        DungeonDoorDualSideProbeReceiver>(true);
                if (receivers.Length != 1 || !receivers[0].isActiveAndEnabled)
                    throw new InvalidOperationException(
                        "Exactly one active moving-door probe receiver is required.");

                Type type = typeof(DungeonDoorDualSideProbeReceiver);
                FieldInfo smooth = RequireField(type, "smoothProbeChanges");
                FieldInfo runtimeBindings = RequireField(type, "_bindings");
                FieldInfo nextRefresh = RequireField(type, "_nextRefreshTime");
                FieldInfo applied = RequireField(type, "_hasAppliedCustomProbe");
                if (smooth.FieldType != typeof(bool) ||
                    !runtimeBindings.FieldType.IsArray ||
                    nextRefresh.FieldType != typeof(float) ||
                    applied.FieldType != typeof(bool))
                {
                    throw new InvalidOperationException(
                        "Door probe receiver private-state contract changed.");
                }
                return new DoorProbeReceiverSnapshot(
                    receivers[0], smooth, runtimeBindings, nextRefresh, applied);
            }

            internal void BeginSettledCapture()
            {
                RequireAlive();
                smoothField.SetValue(receiver, false);
                if ((bool)smoothField.GetValue(receiver))
                    throw new InvalidOperationException(
                        "Could not disable door probe smoothing for deterministic capture.");
            }

            internal void ForceSettledRefresh()
            {
                RequireAlive();
                if ((bool)smoothField.GetValue(receiver))
                    throw new InvalidOperationException(
                        "Door probe smoothing re-enabled during deterministic capture.");
                receiver.ForceRefresh();
            }

            internal void Restore()
            {
                RequireAlive();
                bindingsField.SetValue(receiver, bindings.Clone());
                nextRefreshField.SetValue(receiver, nextRefreshTime);
                appliedField.SetValue(receiver, hasAppliedCustomProbe);
                smoothField.SetValue(receiver, smoothProbeChanges);
            }

            internal void AssertRestored()
            {
                RequireAlive();
                Array current = bindingsField.GetValue(receiver) as Array;
                if ((bool)smoothField.GetValue(receiver) != smoothProbeChanges ||
                    current == null || current.Length != bindings.Length ||
                    (float)nextRefreshField.GetValue(receiver) != nextRefreshTime ||
                    (bool)appliedField.GetValue(receiver) != hasAppliedCustomProbe)
                {
                    throw new InvalidOperationException(
                        "Door probe receiver configuration/internal state was not restored.");
                }
                for (int i = 0; i < bindings.Length; i++)
                {
                    if (!Equals(current.GetValue(i), bindings.GetValue(i)))
                    {
                        throw new InvalidOperationException(
                            "Door probe receiver binding state was not restored at index " + i + ".");
                    }
                }
            }

            private void RequireAlive()
            {
                if (receiver == null)
                    throw new InvalidOperationException(
                        "Door probe receiver was destroyed during capture.");
            }

            private static FieldInfo RequireField(Type type, string name)
            {
                FieldInfo field = type.GetField(name, PrivateInstance);
                if (field == null)
                    throw new InvalidOperationException(
                        "Missing door probe receiver private field: " + name);
                return field;
            }
        }

        private sealed class SurfaceSnapshot
        {
            private readonly RendererState[] renderers;
            private readonly ProbeState[] probes;
            private readonly LightState[] lights;
            private readonly LightmapData[] lightmaps;
            private readonly LightmapsMode lightmapsMode;
            private readonly RenderTexture activeRenderTexture;
            private readonly bool srgbWrite;

            private SurfaceSnapshot(
                RendererState[] renderers,
                ProbeState[] probes,
                LightState[] lights,
                LightmapData[] lightmaps,
                LightmapsMode lightmapsMode,
                RenderTexture activeRenderTexture,
                bool srgbWrite)
            {
                this.renderers = renderers;
                this.probes = probes;
                this.lights = lights;
                this.lightmaps = lightmaps;
                this.lightmapsMode = lightmapsMode;
                this.activeRenderTexture = activeRenderTexture;
                this.srgbWrite = srgbWrite;
            }

            internal static SurfaceSnapshot Capture(
                KExactBasisV1EditorContract.SceneBindings bindings)
            {
                Renderer[] rendererComponents =
                    bindings.ProductionRooms.GetComponentsInChildren<Renderer>(true);
                ReflectionProbe[] probeComponents =
                    bindings.ProductionRooms.GetComponentsInChildren<ReflectionProbe>(true);
                Light[] lightComponents =
                    bindings.ProductionRooms.GetComponentsInChildren<Light>(true);
                return new SurfaceSnapshot(
                    rendererComponents.Select(RendererState.Capture).ToArray(),
                    probeComponents.Select(ProbeState.Capture).ToArray(),
                    lightComponents.Select(LightState.Capture).ToArray(),
                    CloneLightmaps(
                        LightmapSettings.lightmaps ?? Array.Empty<LightmapData>()),
                    LightmapSettings.lightmapsMode,
                    RenderTexture.active,
                    GL.sRGBWrite);
            }

            internal void Restore()
            {
                // Unity reconstructs managed LightmapData wrappers when this property is
                // assigned. Preserve and verify the texture-slot contents, not wrapper
                // reference identity, which is not part of the Unity API contract.
                LightmapSettings.lightmaps = CloneLightmaps(lightmaps);
                LightmapSettings.lightmapsMode = lightmapsMode;
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].Restore();
                for (int i = 0; i < probes.Length; i++)
                    probes[i].Restore();
                for (int i = 0; i < lights.Length; i++)
                    lights[i].Restore();
                RenderTexture.active = activeRenderTexture;
                GL.sRGBWrite = srgbWrite;
            }

            internal void AssertRestored()
            {
                LightmapData[] current = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
                if (current.Length != lightmaps.Length ||
                    LightmapSettings.lightmapsMode != lightmapsMode ||
                    RenderTexture.active != activeRenderTexture || GL.sRGBWrite != srgbWrite)
                    throw new InvalidOperationException("Global surface/lightmap state was not restored.");
                for (int i = 0; i < current.Length; i++)
                {
                    LightmapData actual = current[i];
                    LightmapData expected = lightmaps[i];
                    if ((actual == null) != (expected == null) ||
                        (actual != null &&
                         (!ReferenceEquals(actual.lightmapColor, expected.lightmapColor) ||
                          !ReferenceEquals(actual.lightmapDir, expected.lightmapDir) ||
                          !ReferenceEquals(actual.shadowMask, expected.shadowMask))))
                    {
                        throw new InvalidOperationException(
                            "Global LightmapData texture slots were not restored at index " +
                            i + ".");
                    }
                }
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].AssertRestored();
                for (int i = 0; i < probes.Length; i++)
                    probes[i].AssertRestored();
                for (int i = 0; i < lights.Length; i++)
                    lights[i].AssertRestored();
            }

            private static LightmapData[] CloneLightmaps(LightmapData[] source)
            {
                var clone = new LightmapData[source.Length];
                for (int i = 0; i < source.Length; i++)
                {
                    LightmapData entry = source[i];
                    if (entry == null)
                        continue;
                    clone[i] = new LightmapData
                    {
                        lightmapColor = entry.lightmapColor,
                        lightmapDir = entry.lightmapDir,
                        shadowMask = entry.shadowMask
                    };
                }
                return clone;
            }
        }

        private sealed class RendererState
        {
            private readonly Renderer renderer;
            private readonly Material[] materials;
            private readonly int lightmapIndex;
            private readonly Vector4 lightmapSt;
            private readonly int realtimeIndex;
            private readonly Vector4 realtimeSt;
            private readonly uint renderingLayers;
            private readonly bool enabled;
            private readonly bool forceRenderingOff;
            private readonly LightProbeUsage lightProbeUsage;
            private readonly ReflectionProbeUsage reflectionProbeUsage;
            private readonly Transform probeAnchor;
            private readonly GameObject lightProbeProxyVolumeOverride;
            private readonly MaterialPropertyBlock globalPropertyBlock;
            private readonly MaterialPropertyBlock[] perMaterialPropertyBlocks;
            private readonly string propertyBlockFingerprint;

            private RendererState(Renderer renderer)
            {
                this.renderer = renderer;
                materials = (Material[])renderer.sharedMaterials.Clone();
                lightmapIndex = renderer.lightmapIndex;
                lightmapSt = renderer.lightmapScaleOffset;
                realtimeIndex = renderer.realtimeLightmapIndex;
                realtimeSt = renderer.realtimeLightmapScaleOffset;
                renderingLayers = renderer.renderingLayerMask;
                enabled = renderer.enabled;
                forceRenderingOff = renderer.forceRenderingOff;
                lightProbeUsage = renderer.lightProbeUsage;
                reflectionProbeUsage = renderer.reflectionProbeUsage;
                probeAnchor = renderer.probeAnchor;
                lightProbeProxyVolumeOverride = renderer.lightProbeProxyVolumeOverride;
                globalPropertyBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(globalPropertyBlock);
                perMaterialPropertyBlocks = new MaterialPropertyBlock[materials.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    perMaterialPropertyBlocks[i] = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(perMaterialPropertyBlocks[i], i);
                }
                propertyBlockFingerprint =
                    KExactBasisV1EditorContract.ComputeMaterialPropertyBlockFingerprint(
                        renderer);
            }

            internal static RendererState Capture(Renderer renderer)
            {
                return new RendererState(renderer);
            }

            internal void Restore()
            {
                if (renderer == null)
                    throw new InvalidOperationException("A production Renderer was destroyed.");
                renderer.sharedMaterials = (Material[])materials.Clone();
                renderer.lightmapIndex = lightmapIndex;
                renderer.lightmapScaleOffset = lightmapSt;
                renderer.realtimeLightmapIndex = realtimeIndex;
                renderer.realtimeLightmapScaleOffset = realtimeSt;
                renderer.renderingLayerMask = renderingLayers;
                renderer.enabled = enabled;
                renderer.forceRenderingOff = forceRenderingOff;
                renderer.lightProbeUsage = lightProbeUsage;
                renderer.reflectionProbeUsage = reflectionProbeUsage;
                renderer.probeAnchor = probeAnchor;
                renderer.lightProbeProxyVolumeOverride = lightProbeProxyVolumeOverride;
                renderer.SetPropertyBlock(
                    globalPropertyBlock.isEmpty ? null : globalPropertyBlock);
                for (int i = 0; i < perMaterialPropertyBlocks.Length; i++)
                {
                    renderer.SetPropertyBlock(
                        perMaterialPropertyBlocks[i].isEmpty
                            ? null
                            : perMaterialPropertyBlocks[i],
                        i);
                }
            }

            internal void AssertRestored()
            {
                if (renderer == null || renderer.lightmapIndex != lightmapIndex ||
                    renderer.lightmapScaleOffset != lightmapSt ||
                    renderer.realtimeLightmapIndex != realtimeIndex ||
                    renderer.realtimeLightmapScaleOffset != realtimeSt ||
                    renderer.renderingLayerMask != renderingLayers ||
                    renderer.enabled != enabled ||
                    renderer.forceRenderingOff != forceRenderingOff ||
                    renderer.lightProbeUsage != lightProbeUsage ||
                    renderer.reflectionProbeUsage != reflectionProbeUsage ||
                    renderer.probeAnchor != probeAnchor ||
                    renderer.lightProbeProxyVolumeOverride !=
                        lightProbeProxyVolumeOverride ||
                    !renderer.sharedMaterials.SequenceEqual(materials) ||
                    !string.Equals(
                        KExactBasisV1EditorContract
                            .ComputeMaterialPropertyBlockFingerprint(renderer),
                        propertyBlockFingerprint,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException("Production Renderer state was not restored.");
            }
        }

        private sealed class ProbeState
        {
            private readonly ReflectionProbe probe;
            private readonly bool enabled;
            private readonly ReflectionProbeMode mode;
            private readonly Texture customTexture;
            private readonly float intensity;
            private readonly Vector3 center;
            private readonly Vector3 size;
            private readonly float blendDistance;
            private readonly int importance;

            private ProbeState(ReflectionProbe probe)
            {
                this.probe = probe;
                enabled = probe.enabled;
                mode = probe.mode;
                customTexture = probe.customBakedTexture;
                intensity = probe.intensity;
                center = probe.center;
                size = probe.size;
                blendDistance = probe.blendDistance;
                importance = probe.importance;
            }

            internal static ProbeState Capture(ReflectionProbe probe)
            {
                return new ProbeState(probe);
            }

            internal void Restore()
            {
                if (probe == null)
                    throw new InvalidOperationException("A production ReflectionProbe was destroyed.");
                probe.enabled = enabled;
                probe.mode = mode;
                probe.customBakedTexture = customTexture;
                probe.intensity = intensity;
                probe.center = center;
                probe.size = size;
                probe.blendDistance = blendDistance;
                probe.importance = importance;
            }

            internal void AssertRestored()
            {
                if (probe == null || probe.enabled != enabled || probe.mode != mode ||
                    probe.customBakedTexture != customTexture ||
                    !Mathf.Approximately(probe.intensity, intensity) ||
                    probe.center != center || probe.size != size ||
                    !Mathf.Approximately(probe.blendDistance, blendDistance) ||
                    probe.importance != importance)
                    throw new InvalidOperationException("Production ReflectionProbe was not restored.");
            }
        }

        private sealed class LightState
        {
            private readonly Light light;
            private readonly bool enabled;
            private readonly float intensity;

            private LightState(Light light)
            {
                this.light = light;
                enabled = light.enabled;
                intensity = light.intensity;
            }

            internal static LightState Capture(Light light)
            {
                return new LightState(light);
            }

            internal void Restore()
            {
                if (light == null)
                    throw new InvalidOperationException("A production Light was destroyed.");
                light.enabled = enabled;
                light.intensity = intensity;
            }

            internal void AssertRestored()
            {
                if (light == null || light.enabled != enabled ||
                    !Mathf.Approximately(light.intensity, intensity))
                    throw new InvalidOperationException("Production Light was not restored.");
            }
        }

        private sealed class CaptureRecord
        {
            internal readonly PowerState Power;
            internal readonly DoorPose Pose;
            internal readonly float DoorAngleDegrees;
            internal readonly string Camera;
            internal readonly string CameraToken;
            internal readonly string PngFilename;
            internal readonly byte[] PngBytes;
            internal readonly string PngSha;
            internal readonly string ExrFilename;
            internal readonly byte[] ExrBytes;
            internal readonly string ExrSha;
            internal readonly double MeanLuminance;
            internal readonly float MaximumLuminance;
            internal readonly double SemanticReceiverMeanLuminance;
            internal readonly Vector3 CameraPosition;
            internal readonly Quaternion CameraRotation;
            internal readonly float FieldOfView;
            internal readonly float NearClip;
            internal readonly float FarClip;
            internal readonly int CullingMask;
            internal readonly bool AllowHdr;
            internal readonly bool AllowMsaa;
            internal readonly int PresentationMsaa;
            internal readonly bool PresentationPost;
            internal readonly bool HdrPost;
            internal readonly string ConnectionKey;
            internal readonly bool IsTransportActive;
            internal readonly bool IsFaultLatched;
            internal readonly string FaultReason;
            internal readonly bool ParityValidated;
            internal readonly string ParityFailure;
            internal readonly KExactTransportWeights Weights;
            internal readonly float ReceiverBounceTotalIntensityAToB;
            internal readonly float ReceiverBounceTotalIntensityBToA;
            internal readonly float DoorSurfaceTotalIntensityAToB;
            internal readonly float DoorSurfaceTotalIntensityBToA;
            internal string BaselineExrFilename { get; private set; }
            internal double BaselineMean { get; private set; }
            internal double MeanDeltaFromBaseline { get; private set; }
            internal double SemanticReceiverBaselineMean { get; private set; }
            internal double SemanticReceiverMeanDeltaFromBaseline { get; private set; }

            internal CaptureRecord(
                PowerState power,
                DoorPose pose,
                float doorAngleDegrees,
                string camera,
                string cameraToken,
                string pngFilename,
                byte[] pngBytes,
                string pngSha,
                string exrFilename,
                byte[] exrBytes,
                string exrSha,
                double meanLuminance,
                float maximumLuminance,
                double semanticReceiverMeanLuminance,
                Vector3 cameraPosition,
                Quaternion cameraRotation,
                float fieldOfView,
                float nearClip,
                float farClip,
                int cullingMask,
                bool allowHdr,
                bool allowMsaa,
                int presentationMsaa,
                bool presentationPost,
                bool hdrPost,
                string connectionKey,
                bool isTransportActive,
                bool isFaultLatched,
                string faultReason,
                bool parityValidated,
                string parityFailure,
                KExactTransportWeights weights,
                float receiverBounceTotalIntensityAToB,
                float receiverBounceTotalIntensityBToA,
                float doorSurfaceTotalIntensityAToB,
                float doorSurfaceTotalIntensityBToA)
            {
                Power = power;
                Pose = pose;
                DoorAngleDegrees = doorAngleDegrees;
                Camera = camera;
                CameraToken = cameraToken;
                PngFilename = pngFilename;
                PngBytes = pngBytes;
                PngSha = pngSha;
                ExrFilename = exrFilename;
                ExrBytes = exrBytes;
                ExrSha = exrSha;
                MeanLuminance = meanLuminance;
                MaximumLuminance = maximumLuminance;
                SemanticReceiverMeanLuminance = semanticReceiverMeanLuminance;
                CameraPosition = cameraPosition;
                CameraRotation = cameraRotation;
                FieldOfView = fieldOfView;
                NearClip = nearClip;
                FarClip = farClip;
                CullingMask = cullingMask;
                AllowHdr = allowHdr;
                AllowMsaa = allowMsaa;
                PresentationMsaa = presentationMsaa;
                PresentationPost = presentationPost;
                HdrPost = hdrPost;
                ConnectionKey = connectionKey;
                IsTransportActive = isTransportActive;
                IsFaultLatched = isFaultLatched;
                FaultReason = faultReason;
                ParityValidated = parityValidated;
                ParityFailure = parityFailure;
                Weights = weights;
                ReceiverBounceTotalIntensityAToB =
                    receiverBounceTotalIntensityAToB;
                ReceiverBounceTotalIntensityBToA =
                    receiverBounceTotalIntensityBToA;
                DoorSurfaceTotalIntensityAToB = doorSurfaceTotalIntensityAToB;
                DoorSurfaceTotalIntensityBToA = doorSurfaceTotalIntensityBToA;
            }

            internal void AttachBaseline(
                string filename,
                double mean,
                double delta,
                double semanticReceiverMean,
                double semanticReceiverDelta)
            {
                BaselineExrFilename = filename;
                BaselineMean = mean;
                MeanDeltaFromBaseline = delta;
                SemanticReceiverBaselineMean = semanticReceiverMean;
                SemanticReceiverMeanDeltaFromBaseline = semanticReceiverDelta;
            }
        }

        private sealed class RenderFailureMonitor : IDisposable
        {
            private readonly List<string> failures = new List<string>();
            private bool disposed;

            internal RenderFailureMonitor()
            {
                Application.logMessageReceived += OnLog;
            }

            internal void ThrowIfFailed(string stage)
            {
                if (failures.Count > 0)
                    throw new InvalidOperationException(
                        "Unity logged a render failure during " + stage + ": " +
                        string.Join(" || ", failures));
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
                    !Contains(stackTrace, nameof(KExactBasisV1SmokeCapture)))
                    return;
                string summary = (condition ?? "<no condition>")
                    .Replace('\r', ' ').Replace('\n', ' ');
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

        private sealed class RenderTargets : IDisposable
        {
            internal readonly RenderTexture PresentationRender;
            internal readonly RenderTexture PresentationResolve;
            internal readonly RenderTexture HdrRender;
            internal readonly RenderTexture HdrResolve;

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

            internal static RenderTargets Create()
            {
                RenderTexture presentationRender = null;
                RenderTexture presentationResolve = null;
                RenderTexture hdrRender = null;
                RenderTexture hdrResolve = null;
                try
                {
                    presentationRender = CreateTarget(
                        "KExactV1_Presentation_Render",
                        24,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.sRGB);
                    presentationResolve = CreateTarget(
                        "KExactV1_Presentation_Resolve",
                        0,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.sRGB);
                    hdrRender = CreateTarget(
                        "KExactV1_HDR_Render",
                        24,
                        RenderTextureFormat.ARGBHalf,
                        RenderTextureReadWrite.Linear);
                    hdrResolve = CreateTarget(
                        "KExactV1_HDR_Resolve",
                        0,
                        RenderTextureFormat.ARGBHalf,
                        RenderTextureReadWrite.Linear);
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
                RenderTextureReadWrite readWrite)
            {
                var target = new RenderTexture(
                    CaptureWidth, CaptureHeight, depth, format, readWrite)
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

        private readonly struct SemanticRoi
        {
            internal readonly int Left;
            internal readonly int Top;
            internal readonly int Right;
            internal readonly int Bottom;

            internal SemanticRoi(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }

            internal void Validate(int width, int height, string cameraToken)
            {
                if (Left < 0 || Top < 0 || Right > width || Bottom > height ||
                    Left >= Right || Top >= Bottom)
                {
                    throw new InvalidOperationException(
                        "Invalid semantic receiver ROI for camera token " + cameraToken +
                        ": [" + Left + "," + Top + "," + Right + "," + Bottom + "].");
                }
            }
        }

        private readonly struct PowerState
        {
            internal readonly string Id;
            internal readonly string ShortToken;
            internal readonly bool StartOn;
            internal readonly bool AdministrativeOn;
            internal bool IsP0P0 => !StartOn && !AdministrativeOn;

            internal PowerState(
                string id,
                string shortToken,
                bool startOn,
                bool administrativeOn)
            {
                Id = id;
                ShortToken = shortToken;
                StartOn = startOn;
                AdministrativeOn = administrativeOn;
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

        private readonly struct FileDeclaration
        {
            internal readonly string Sha;
            internal readonly long Bytes;

            internal FileDeclaration(string sha, long bytes)
            {
                Sha = sha;
                Bytes = bytes;
            }
        }

        private readonly struct LinearDifferenceMetrics
        {
            internal readonly double MeanAbsoluteLuminanceDelta;
            internal readonly double MeanPositiveLuminanceDelta;

            internal LinearDifferenceMetrics(
                double meanAbsoluteLuminanceDelta,
                double meanPositiveLuminanceDelta)
            {
                MeanAbsoluteLuminanceDelta = meanAbsoluteLuminanceDelta;
                MeanPositiveLuminanceDelta = meanPositiveLuminanceDelta;
            }
        }

        private readonly struct PngDifferenceMetrics
        {
            internal readonly double MeanAbsoluteChannelDelta;
            internal readonly int MaximumChannelDelta;
            internal readonly int MaximumAlphaDelta;
            internal readonly double ChangedPixelFraction;

            internal bool WithinTolerance =>
                MaximumChannelDelta <= PresentationMaximumRgb8ChannelDelta &&
                MeanAbsoluteChannelDelta <= PresentationMeanAbsoluteRgb8ChannelDelta &&
                MaximumAlphaDelta == 0;

            internal PngDifferenceMetrics(
                double meanAbsoluteChannelDelta,
                int maximumChannelDelta,
                int maximumAlphaDelta,
                double changedPixelFraction)
            {
                MeanAbsoluteChannelDelta = meanAbsoluteChannelDelta;
                MaximumChannelDelta = maximumChannelDelta;
                MaximumAlphaDelta = maximumAlphaDelta;
                ChangedPixelFraction = changedPixelFraction;
            }

            internal string ToInvariantString()
            {
                return "meanAbsRgb8=" +
                       MeanAbsoluteChannelDelta.ToString("R", CultureInfo.InvariantCulture) +
                       ", maxRgb8=" + MaximumChannelDelta +
                       ", maxAlpha8=" + MaximumAlphaDelta +
                       ", changedPixelFraction=" +
                       ChangedPixelFraction.ToString("R", CultureInfo.InvariantCulture) +
                       ", thresholds=(mean<=" +
                       PresentationMeanAbsoluteRgb8ChannelDelta.ToString(
                           "R", CultureInfo.InvariantCulture) +
                       ",max<=" + PresentationMaximumRgb8ChannelDelta + ")";
            }
        }

        private enum FileKind
        {
            Png,
            Exr,
            Text
        }
    }
}
