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
    /// Captures a deliberately limited, actual-StartMap realtime-direct oracle.
    ///
    /// This is not a replacement for the DPBB total-lighting smoke.  It temporarily isolates
    /// only the 389 validation-pair renderers from baked lightmaps, realtime lightmaps, SH, and
    /// reflection sampling, then renders the original 24+54 production lights as realtime direct
    /// lights with bounceIntensity=0.  It never calls a DungeonTileLightmapSwitcher, never writes
    /// LightmapSettings, and restores every pair object it changes before returning.
    /// </summary>
    public static class DungeonPortalBakedBasisStartMapRealtimeDirectOracleCapture
    {
        private const string Status = "DPBB_STARTMAP_REALTIME_DIRECT_ORACLE_V1";
        private const string Schema = "DPBBStartMapRealtimeDirectOracle/v1";
        private const string StateSchema = "DPBBStartMapRealtimeDirectOracleState/v1";
        private const string StartMapHarnessScenePath =
            "Assets/Experiments/DungeonPortalBakedBasisPoC/Scenes/" +
            "Start_Admin_DPBB_StartMapDungeonEntryValidation.unity";
        private const string CaptureRoute = "two-scene-actual-StartMap-local-player-camera";
        private const int CaptureWidth = 1920;
        private const int CaptureHeight = 1080;
        private const float DoorTolerance = 0.002f;

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

        [MenuItem("Tools/Dungeon/Lighting/Portal Baked Basis PoC/Capture StartMap REALTIME Direct Oracle (Play Mode; No Mode Toggle)")]
        public static void CaptureFromMenu()
        {
            string result = CaptureAll();
            if (result.StartsWith("PASS", StringComparison.Ordinal))
                Debug.Log(result);
            else
                Debug.LogError(result);
        }

        /// <summary>
        /// CLI seam intentionally does not start/stop Play Mode.  The caller must have already
        /// reached the READY real StartMap two-scene harness.
        /// </summary>
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

        /// <summary>
        /// Kept small and static so EditMode tests can assert the non-negotiable runtime route
        /// without entering Play Mode.
        /// </summary>
        internal static string DescribeContractForTests()
        {
            return "entry=CaptureAll;requiresControllerReady=true;requiresActiveStartMap=true;" +
                   "captureCamera=ActualPlayerCamera;fixedCameraCaptureForbidden=true;" +
                   "frames=TrySetCaptureFrame(0|1);" +
                   "policies=ORACLE_SPOT_OFF,HUMAN_SPOT_ON;" +
                   "rendererIsolation=389:bakedLightmap,realtimeLightmap,SH,reflectionOff;" +
                   "productionLights=24+54:Realtime:bounce0;" +
                   "globalLightmapSettings=neverWritten+fingerprintAsserted;" +
                   "indirectDiffuse=false;restore=exact";
        }

        private static string CaptureAllOrThrow()
        {
            StartMapContext context = RequireStartMapHarness();
            string initialHarnessSha = DungeonPortalBakedBasisValidationContract.ComputeFileSha256(
                context.HarnessScene.path);
            string initialStartMapSha = DungeonPortalBakedBasisValidationContract.ComputeFileSha256(
                DungeonPortalBakedBasisStartMapRuntimeController.StartMapScenePath);
            KExactBasisV1EditorContract.SelectionSnapshot selection =
                KExactBasisV1EditorContract.SelectionSnapshot.Capture();
            KExactBasisV1EditorContract.ProtectedAssetSnapshot protectedAssets =
                KExactBasisV1EditorContract.ProtectedAssetSnapshot.Capture();
            GlobalLightmapFingerprint globalLightmaps = GlobalLightmapFingerprint.Capture();
            PairDirectScope scope = null;
            RenderTargets targets = null;
            RenderFailureMonitor monitor = null;
            bool restored = false;
            var records = new List<CaptureRecord>(80);
            string utc = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'",
                CultureInfo.InvariantCulture);
            string runId = utc + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string outputFolder = DungeonPortalBakedBasisValidationContract.EvidenceRoot +
                                  "/StartMapRealtimeDirectOracle/" + runId;
            string absoluteOutputFolder = DungeonPortalBakedBasisValidationContract.AssetPathToAbsolutePath(
                outputFolder);
            Directory.CreateDirectory(absoluteOutputFolder);
            if (!Directory.Exists(absoluteOutputFolder))
                throw new IOException("Could not create StartMap realtime-direct oracle evidence folder.");

            try
            {
                scope = PairDirectScope.Capture(context.Bindings, context.Controller);
                scope.Enter();
                globalLightmaps.AssertUnchanged("direct-oracle enter");
                targets = RenderTargets.Create();
                monitor = new RenderFailureMonitor();

                DungeonPortalBakedBasisEvidenceSpotPolicy[] policies =
                {
                    DungeonPortalBakedBasisEvidenceSpotPolicy.ORACLE_SPOT_OFF,
                    DungeonPortalBakedBasisEvidenceSpotPolicy.HUMAN_SPOT_ON
                };
                for (int policyIndex = 0; policyIndex < policies.Length; policyIndex++)
                {
                    DungeonPortalBakedBasisEvidenceSpotPolicy policy = policies[policyIndex];
                    context.ApplyPolicy(policy, "direct-oracle policy setup");
                    string policyFolder = outputFolder + "/" + policy;
                    Directory.CreateDirectory(
                        DungeonPortalBakedBasisValidationContract.AssetPathToAbsolutePath(policyFolder));
                    CapturePolicyMatrix(context, scope, globalLightmaps, targets, monitor,
                        policyFolder, records);
                }
                monitor.ThrowIfFailed("direct-oracle matrix");
                if (records.Count != 80)
                    throw new InvalidOperationException("Realtime-direct oracle record count is " +
                                                        records.Count + ", expected 80.");
            }
            finally
            {
                try
                {
                    targets?.Dispose();
                    monitor?.Dispose();
                    scope?.Restore();
                    globalLightmaps.AssertUnchanged("direct-oracle pair restore");
                    context.RestoreInitialCameraAndPolicy();
                    restored = true;
                }
                finally
                {
                    selection.Restore();
                }
            }

            if (!restored)
                throw new InvalidOperationException("Realtime-direct oracle restore did not complete.");
            scope.AssertRestored();
            globalLightmaps.AssertUnchanged("direct-oracle final");
            context.Validate("direct-oracle final restore");
            selection.AssertRestored();
            protectedAssets.AssertUnchanged();
            string finalHarnessSha = DungeonPortalBakedBasisValidationContract.ComputeFileSha256(
                context.HarnessScene.path);
            string finalStartMapSha = DungeonPortalBakedBasisValidationContract.ComputeFileSha256(
                DungeonPortalBakedBasisStartMapRuntimeController.StartMapScenePath);
            if (!string.Equals(initialHarnessSha, finalHarnessSha,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(initialStartMapSha, finalStartMapSha,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Realtime-direct oracle changed a saved harness or production StartMap scene file.");
            }

            string manifest = BuildManifest(utc, initialHarnessSha, initialStartMapSha, context,
                globalLightmaps, scope, records, protectedAssets.FingerprintSha256);
            string manifestPath = outputFolder + "/manifest_DPBB_STARTMAP_REALTIME_DIRECT_ORACLE.txt";
            File.WriteAllText(DungeonPortalBakedBasisValidationContract.AssetPathToAbsolutePath(manifestPath),
                manifest, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(manifestPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            string manifestSha = DungeonPortalBakedBasisValidationContract.ComputeFileSha256(manifestPath);
            return "PASS " + Status + "\n" +
                   "output=" + outputFolder + "\n" +
                   "policySetCount=2\n" +
                   "stateCameraRecordCount=80\n" +
                   "indirectDiffuse=false\n" +
                   "globalLightmapSettingsMutated=false\n" +
                   "visualVerdict=UNREVIEWED\n" +
                   "visualParityClaimed=false\n" +
                   "manifestSha256=" + manifestSha;
        }

        private static StartMapContext RequireStartMapHarness()
        {
            if (!Application.isPlaying || !EditorApplication.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying ||
                EditorApplication.isCompiling || EditorApplication.isUpdating || Lightmapping.isRunning)
            {
                throw new InvalidOperationException(
                    "Realtime-direct oracle requires already-running stable Play Mode; it never changes Play Mode.");
            }
            if (SceneManager.sceneCount != 2)
            {
                throw new InvalidOperationException(
                    "Realtime-direct oracle requires the READY two-scene StartMap harness.");
            }

            DungeonPortalBakedBasisStartMapRuntimeController[] controllers =
                Object.FindObjectsByType<DungeonPortalBakedBasisStartMapRuntimeController>(
                    FindObjectsInactive.Include);
            if (controllers.Length != 1 || controllers[0] == null)
                throw new InvalidOperationException("Expected exactly one StartMap runtime controller.");
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
                controller.ValidationPairRoot == null || controller.ValidationPairRoot.scene != harness ||
                controller.ActualPlayerCamera == null ||
                controller.ActualPlayerCamera.gameObject.scene != active ||
                controller.ActualPlayerCamera.targetTexture != null ||
                !controller.ActualPlayerCamera.enabled ||
                controller.AssignedDungeonVolume == null ||
                !controller.IsActualDungeonEnvironmentApplied)
            {
                throw new InvalidOperationException(
                    "Requires READY actual StartMap, active dungeon environment, assigned Volume, and ActualPlayerCamera.");
            }

            KExactBasisV1EditorContract.SceneBindings bindings =
                KExactBasisV1EditorContract.ResolveSceneBindings(harness, false, false, false);
            if (controller.ActualGeneratedDoorLeaf == null ||
                controller.ActualGeneratedDoorLeaf != controller.ConnectionDriver?.DoorAngleSource?.DoorLeaf ||
                controller.ActualGeneratedDoorLeaf != bindings.DoorLeaf)
            {
                throw new InvalidOperationException(
                    "The V1 validation-clone realtime oracle is deprecated for the actual-generated V2 binding. " +
                    "It is fail-closed until an actual-generated direct-only scope replaces it.");
            }
            if (bindings.Root != controller.ValidationPairRoot ||
                controller.ConnectionDriver == null || controller.DoorShDriver == null ||
                controller.ReflectionDriver == null ||
                controller.ConnectionDriver != DungeonPortalBakedBasisValidationContract.RequireDriver(harness) ||
                controller.DoorShDriver != DungeonPortalBakedBasisValidationContract.RequireDoorShDriver(harness) ||
                controller.ReflectionDriver != DungeonPortalBakedBasisValidationContract.RequireReflectionDriver(harness))
            {
                throw new InvalidOperationException("StartMap direct-oracle pair/driver binding drifted.");
            }
            for (int i = 0; i < bindings.Cameras.Length; i++)
            {
                Camera fixedCamera = bindings.Cameras[i];
                if (fixedCamera == null || fixedCamera.enabled || fixedCamera.targetTexture != null ||
                    fixedCamera == controller.ActualPlayerCamera)
                {
                    throw new InvalidOperationException(
                        "Realtime-direct oracle forbids fallback/fixed-camera rendering.");
                }
            }

            var context = new StartMapContext(controller, harness, bindings);
            context.Validate("direct-oracle preflight");
            return context;
        }

        private static void CapturePolicyMatrix(
            StartMapContext context,
            PairDirectScope scope,
            GlobalLightmapFingerprint globalLightmaps,
            RenderTargets targets,
            RenderFailureMonitor monitor,
            string outputFolder,
            List<CaptureRecord> records)
        {
            if (context.Policy != DungeonPortalBakedBasisEvidenceSpotPolicy.ORACLE_SPOT_OFF &&
                context.Policy != DungeonPortalBakedBasisEvidenceSpotPolicy.HUMAN_SPOT_ON)
            {
                throw new InvalidOperationException("Unsupported evidence spot policy.");
            }
            if (context.Policy == DungeonPortalBakedBasisEvidenceSpotPolicy.HUMAN_SPOT_ON &&
                !context.Controller.TryValidateCaptureEnvironment(out string humanFailure))
            {
                throw new InvalidOperationException(
                    "HUMAN_SPOT_ON must keep the assigned dungeon-only light enabled: " + humanFailure);
            }

            int initialCount = records.Count;
            for (int powerIndex = 0; powerIndex < PowerStates.Length; powerIndex++)
            {
                PowerState power = PowerStates[powerIndex];
                for (int poseIndex = 0; poseIndex < DoorPoses.Length; poseIndex++)
                {
                    DoorPose pose = DoorPoses[poseIndex];
                    string stateLabel = context.Policy + " " + power.Id + " D" + pose.Percent;
                    context.Validate(stateLabel + " before state");
                    globalLightmaps.AssertUnchanged(stateLabel + " before state");
                    scope.ApplyState(power, pose);
                    scope.AssertDirectState(power, pose, stateLabel + " after apply");
                    globalLightmaps.AssertUnchanged(stateLabel + " after state");
                    context.Validate(stateLabel + " after state");

                    for (int frameIndex = 0; frameIndex < 2; frameIndex++)
                    {
                        context.SetCaptureFrame(frameIndex, stateLabel + " frame=" + frameIndex);
                        context.Validate(stateLabel + " frame=" + frameIndex + " before render");
                        globalLightmaps.AssertUnchanged(stateLabel + " frame=" + frameIndex + " before render");
                        scope.AssertDirectState(power, pose,
                            stateLabel + " frame=" + frameIndex + " direct assertion");
                        records.Add(CaptureCamera(context, scope, globalLightmaps, targets, monitor,
                            outputFolder, power, pose));
                        context.Validate(stateLabel + " frame=" + frameIndex + " after render");
                        globalLightmaps.AssertUnchanged(stateLabel + " frame=" + frameIndex + " after render");
                        scope.AssertDirectState(power, pose,
                            stateLabel + " frame=" + frameIndex + " post-render assertion");
                    }
                }
            }
            if (records.Count - initialCount != 40)
                throw new InvalidOperationException("Each policy must emit exactly 40 direct-oracle records.");
            context.Validate(context.Policy + " policy after all states");
        }

        private static CaptureRecord CaptureCamera(
            StartMapContext context,
            PairDirectScope scope,
            GlobalLightmapFingerprint globalLightmaps,
            RenderTargets targets,
            RenderFailureMonitor monitor,
            string outputFolder,
            PowerState power,
            DoorPose pose)
        {
            Camera camera = context.Controller.ActualPlayerCamera;
            if (camera == null || !camera.enabled || camera.targetTexture != null ||
                camera.gameObject.scene != SceneManager.GetActiveScene() ||
                camera == context.Bindings.Cameras[0] || camera == context.Bindings.Cameras[1])
            {
                throw new InvalidOperationException("Only the actual enabled StartMap player camera may render.");
            }
            int frameIndex = context.Controller.CurrentFrameIndex;
            if (frameIndex < 0 || frameIndex > 1)
                throw new InvalidOperationException("Capture frame is outside the two-frame contract.");
            string cameraToken = frameIndex == 0 ? "S2A" : "A2S";
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            if (cameraData == null)
                throw new InvalidOperationException("Actual player camera has no URP camera data.");

            bool originalPost = cameraData.renderPostProcessing;
            RenderTexture originalActive = RenderTexture.active;
            RenderTexture originalTarget = camera.targetTexture;
            bool originalSrgbWrite = GL.sRGBWrite;
            Texture2D pngReadback = null;
            Texture2D exrReadback = null;
            try
            {
                cameraData.renderPostProcessing = true;
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
                SubmitStandardRequest(camera, targets.HdrRender, monitor, "linear HDR");
                GL.sRGBWrite = false;
                Graphics.Blit(targets.HdrRender, targets.HdrResolve);
                RenderTexture.active = targets.HdrResolve;
                exrReadback = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGBAHalf,
                    false, true) { hideFlags = HideFlags.HideAndDontSave };
                exrReadback.ReadPixels(new Rect(0f, 0f, CaptureWidth, CaptureHeight), 0, 0, false);
                exrReadback.Apply(false, false);
                ComputeLuminance(exrReadback.GetPixels(), out double meanLuminance,
                    out float maxLuminance);
                byte[] exr = ImageConversion.EncodeToEXR(exrReadback, Texture2D.EXRFlags.CompressZIP);
                VerifyNonEmpty(exr, "EXR");

                string stem = power.ShortToken + "_D" +
                              pose.Percent.ToString("000", CultureInfo.InvariantCulture) + "_" +
                              cameraToken;
                string pngPath = outputFolder + "/" + stem + ".png";
                string exrPath = outputFolder + "/" + stem + "_L.exr";
                File.WriteAllBytes(DungeonPortalBakedBasisValidationContract.AssetPathToAbsolutePath(pngPath), png);
                File.WriteAllBytes(DungeonPortalBakedBasisValidationContract.AssetPathToAbsolutePath(exrPath), exr);
                string stateContract = BuildStateContract(context, scope, globalLightmaps, power, pose,
                    cameraToken);
                string statePath = outputFolder + "/" + stem + ".state.txt";
                File.WriteAllText(DungeonPortalBakedBasisValidationContract.AssetPathToAbsolutePath(statePath),
                    stateContract, new UTF8Encoding(false));
                return new CaptureRecord(power, pose, cameraToken, Path.GetFileName(pngPath),
                    DungeonPortalBakedBasisValidationContract.ComputeSha256(png), Path.GetFileName(exrPath),
                    DungeonPortalBakedBasisValidationContract.ComputeSha256(exr), Path.GetFileName(statePath),
                    DungeonPortalBakedBasisValidationContract.ComputeSha256(
                        Encoding.UTF8.GetBytes(stateContract)), meanLuminance, maxLuminance,
                    context.CreateMetadata(), scope.StartEnabledCount, scope.AdministrativeEnabledCount,
                    scope.DirectRendererFingerprintSha256);
            }
            finally
            {
                RenderTexture.active = originalActive;
                camera.targetTexture = originalTarget;
                cameraData.renderPostProcessing = originalPost;
                GL.sRGBWrite = originalSrgbWrite;
                if (pngReadback != null)
                    Object.DestroyImmediate(pngReadback);
                if (exrReadback != null)
                    Object.DestroyImmediate(exrReadback);
            }
        }

        private static string BuildStateContract(
            StartMapContext context,
            PairDirectScope scope,
            GlobalLightmapFingerprint globalLightmaps,
            PowerState power,
            DoorPose pose,
            string cameraToken)
        {
            CaptureMetadata metadata = context.CreateMetadata();
            var builder = new StringBuilder(4096);
            builder.AppendLine("schema=" + StateSchema);
            builder.AppendLine("status=" + Status);
            builder.AppendLine("captureRoute=" + CaptureRoute);
            builder.AppendLine("spotPolicy=" + metadata.SpotPolicy);
            builder.AppendLine("environmentFingerprintSha256=" + metadata.EnvironmentFingerprintSha256);
            builder.AppendLine("actualPlayerCamera=" + metadata.ActualCamera);
            builder.AppendLine("assignedDungeonVolume=" + metadata.AssignedDungeonVolume);
            builder.AppendLine("activeScene=" + metadata.ActiveScenePath);
            builder.AppendLine("referenceFrameIndex=" + metadata.ReferenceFrameIndex);
            builder.AppendLine("cameraToken=" + cameraToken);
            builder.AppendLine("power=" + power.Id);
            builder.AppendLine("doorPercent=" + pose.Percent);
            builder.AppendLine("startPower01=" + Format(power.StartPower01));
            builder.AppendLine("administrativePower01=" + Format(power.AdministrativePower01));
            builder.AppendLine("aperture01=" + Format(PortalTransportMath.ComputeProjectedApertureFraction(
                pose.Fraction)));
            builder.AppendLine("productionRendererCount=" + scope.RendererCount);
            builder.AppendLine("additionalRendererCount=" + scope.AdditionalRendererCount);
            builder.AppendLine("productionSurfaceBaselineSha256=" + scope.OriginalSurfaceFingerprintSha256);
            builder.AppendLine("directRendererStateSha256=" + scope.DirectRendererFingerprintSha256);
            builder.AppendLine("pairBakedLightmapIsolation=true");
            builder.AppendLine("pairLightmapIndexAll=-1");
            builder.AppendLine("pairRealtimeLightmapIndexAll=-1");
            builder.AppendLine("pairLightProbeUsageAll=Off");
            builder.AppendLine("pairReflectionProbeUsageAll=Off");
            builder.AppendLine("indirectDiffuse=false");
            builder.AppendLine("bakedLightmapsCovered=false");
            builder.AppendLine("lightProbeShCovered=false");
            builder.AppendLine("reflectionProbeSamplingCovered=false");
            builder.AppendLine("realtimeGiBounceCovered=false");
            builder.AppendLine("directProductionLightCount=" + scope.LightCount);
            builder.AppendLine("directStartLightCount=" + scope.StartLightCount);
            builder.AppendLine("directAdministrativeLightCount=" + scope.AdministrativeLightCount);
            builder.AppendLine("directStartEnabledCount=" + scope.StartEnabledCount);
            builder.AppendLine("directAdministrativeEnabledCount=" + scope.AdministrativeEnabledCount);
            builder.AppendLine("directLightmapBakeType=Realtime");
            builder.AppendLine("directBounceIntensity=0");
            builder.AppendLine("ignoreLightControl=preserve-production-direct-exceptions");
            builder.AppendLine("connectionDriverPaused=true");
            builder.AppendLine("doorShPaused=true");
            builder.AppendLine("roomCompositorsInactiveRequired=true");
            builder.AppendLine("globalLightmapSettingsMutation=false");
            builder.AppendLine("globalLightmapFingerprintSha256=" + globalLightmaps.Sha256);
            builder.AppendLine("visualVerdict=UNREVIEWED");
            builder.AppendLine("visualParityClaimed=false");
            return builder.ToString();
        }

        private static string BuildManifest(
            string utc,
            string harnessSceneSha,
            string startMapSceneSha,
            StartMapContext context,
            GlobalLightmapFingerprint globalLightmaps,
            PairDirectScope scope,
            List<CaptureRecord> records,
            string protectedAssetsFingerprint)
        {
            if (records == null || records.Count != 80)
                throw new InvalidOperationException("Realtime-direct oracle manifest needs exactly 80 records.");
            var builder = new StringBuilder(96 * 1024);
            builder.AppendLine("schema=" + Schema);
            builder.AppendLine("status=" + Status);
            builder.AppendLine("capturedUtc=" + utc);
            builder.AppendLine("captureRoute=" + CaptureRoute);
            builder.AppendLine("finalEvidenceEligible=false");
            builder.AppendLine("visualVerdict=UNREVIEWED");
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
            builder.AppendLine("matrix=4 power states x 5 physical door poses x 2 actual-player frames x 2 spot policies");
            builder.AppendLine("policySetCount=2");
            builder.AppendLine("stateCameraRecordCount=80");
            builder.AppendLine("oracleScope=PAIR_ONLY_REALTIME_DIRECT");
            builder.AppendLine("indirectDiffuse=false");
            builder.AppendLine("bakedLightmapsCovered=false");
            builder.AppendLine("lightProbeShCovered=false");
            builder.AppendLine("reflectionProbeSamplingCovered=false");
            builder.AppendLine("realtimeGiBounceCovered=false");
            builder.AppendLine("directProductionLightCount=78");
            builder.AppendLine("directLightContract=24+54;lightmapBakeType=Realtime;bounceIntensity=0");
            builder.AppendLine("pairRendererIsolation=389;lightmap=-1;realtimeLightmap=-1;SH=Off;reflection=Off");
            builder.AppendLine("commonFeatureDeltaAllowlist=ORACLE_PAIR_LIGHTMAP_SH_REFLECTION,ORACLE_PAIR_EMISSION,ORACLE_PAIR_78_LIGHTS,ORACLE_DOOR");
            builder.AppendLine("commonFingerprintDrift=FAIL_CLOSED");
            builder.AppendLine("globalLightmapSettingsMutation=FORBIDDEN_AND_ASSERTED_FALSE");
            builder.AppendLine("globalLightmapFingerprintSha256=" + globalLightmaps.Sha256);
            builder.AppendLine("productionSurfaceBaselineSha256=" + scope.OriginalSurfaceFingerprintSha256);
            builder.AppendLine("rendererCount=" + scope.RendererCount);
            builder.AppendLine("additionalRendererCount=" + scope.AdditionalRendererCount);
            builder.AppendLine("protectedProductionFingerprintSha256=" + protectedAssetsFingerprint);
            builder.AppendLine("policySet[0]=ORACLE_SPOT_OFF;assignedDungeonOnlyLightOn=false;matrixRecords=40");
            builder.AppendLine("policySet[1]=HUMAN_SPOT_ON;assignedDungeonOnlyLightOn=true;matrixRecords=40");
            builder.AppendLine("recordsBegin");
            for (int i = 0; i < records.Count; i++)
                records[i].AppendManifest(builder);
            builder.AppendLine("recordsEnd");
            builder.AppendLine("d0ParityBegin");
            builder.AppendLine("d0ParityEnd");
            builder.AppendLine("screenMeanTransitionTraceBegin");
            builder.AppendLine("screenMeanTransitionTraceEnd");
            return builder.ToString();
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

        private static void VerifyNonEmpty(byte[] bytes, string label)
        {
            if (bytes == null || bytes.Length < 16)
                throw new InvalidOperationException(label + " encode produced no usable bytes.");
        }

        private static string Format(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static bool Approximately(float left, float right, float tolerance)
        {
            return Mathf.Abs(left - right) <= tolerance;
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

        private sealed class StartMapContext
        {
            internal readonly DungeonPortalBakedBasisStartMapRuntimeController Controller;
            internal readonly Scene HarnessScene;
            internal readonly KExactBasisV1EditorContract.SceneBindings Bindings;
            private readonly DungeonPortalBakedBasisEvidenceSpotPolicy initialPolicy;
            private readonly int initialFrameIndex;

            internal DungeonPortalBakedBasisEvidenceSpotPolicy Policy { get; private set; }
            internal string EnvironmentFingerprintSha256 { get; private set; }

            internal StartMapContext(DungeonPortalBakedBasisStartMapRuntimeController controller,
                Scene harnessScene, KExactBasisV1EditorContract.SceneBindings bindings)
            {
                Controller = controller ?? throw new ArgumentNullException(nameof(controller));
                HarnessScene = harnessScene;
                Bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
                initialPolicy = controller.EvidenceSpotPolicy;
                initialFrameIndex = controller.CurrentFrameIndex;
                Policy = initialPolicy;
                RefreshFingerprint();
            }

            internal void ApplyPolicy(DungeonPortalBakedBasisEvidenceSpotPolicy policy, string stage)
            {
                if (!Controller.TryApplyEvidenceSpotPolicyForCapture(policy, out string failure) ||
                    Controller.EvidenceSpotPolicy != policy)
                {
                    throw new InvalidOperationException("Could not apply evidence spot policy " + policy +
                                                        " at " + stage + ": " + failure);
                }
                Policy = policy;
                RefreshFingerprint();
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
                    throw new InvalidOperationException("StartMap environment drifted at " + stage + ": " +
                                                        failure);
                }
                if (Controller.EvidenceSpotPolicy != Policy || Controller.EnvironmentFingerprint == null ||
                    !string.Equals(Controller.EnvironmentFingerprint.Sha256,
                        EnvironmentFingerprintSha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("StartMap environment fingerprint drifted at " +
                                                        stage + ".");
                }
            }

            internal CaptureMetadata CreateMetadata()
            {
                Camera camera = Controller.ActualPlayerCamera;
                Volume volume = Controller.AssignedDungeonVolume;
                return new CaptureMetadata(CaptureRoute, Policy.ToString(), EnvironmentFingerprintSha256,
                    camera != null ? camera.gameObject.scene.path + ":" + camera.name : "MISSING",
                    volume != null ? volume.gameObject.scene.path + ":" + volume.name : "MISSING",
                    SceneManager.GetActiveScene().path, Controller.CurrentFrameIndex);
            }

            internal void RestoreInitialCameraAndPolicy()
            {
                ApplyPolicy(initialPolicy, "direct-oracle restore policy");
                SetCaptureFrame(initialFrameIndex, "direct-oracle restore frame");
            }

            private void RefreshFingerprint()
            {
                if (Controller.EnvironmentFingerprint == null ||
                    string.IsNullOrWhiteSpace(Controller.EnvironmentFingerprint.Sha256))
                {
                    throw new InvalidOperationException("StartMap READY environment fingerprint is missing.");
                }
                EnvironmentFingerprintSha256 = Controller.EnvironmentFingerprint.Sha256;
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

        private sealed class CaptureRecord
        {
            private readonly PowerState power;
            private readonly DoorPose pose;
            private readonly string cameraToken;
            private readonly string pngFile;
            private readonly string pngSha;
            private readonly string exrFile;
            private readonly string exrSha;
            private readonly string stateFile;
            private readonly string stateSha;
            private readonly double meanLuminance;
            private readonly float maxLuminance;
            private readonly CaptureMetadata metadata;
            private readonly int startEnabled;
            private readonly int administrativeEnabled;
            private readonly string directRendererFingerprint;

            internal CaptureRecord(PowerState power, DoorPose pose, string cameraToken,
                string pngFile, string pngSha, string exrFile, string exrSha, string stateFile,
                string stateSha, double meanLuminance, float maxLuminance, CaptureMetadata metadata,
                int startEnabled, int administrativeEnabled, string directRendererFingerprint)
            {
                this.power = power;
                this.pose = pose;
                this.cameraToken = cameraToken;
                this.pngFile = pngFile;
                this.pngSha = pngSha;
                this.exrFile = exrFile;
                this.exrSha = exrSha;
                this.stateFile = stateFile;
                this.stateSha = stateSha;
                this.meanLuminance = meanLuminance;
                this.maxLuminance = maxLuminance;
                this.metadata = metadata;
                this.startEnabled = startEnabled;
                this.administrativeEnabled = administrativeEnabled;
                this.directRendererFingerprint = directRendererFingerprint;
            }

            internal void AppendManifest(StringBuilder builder)
            {
                builder.Append("record=").Append(metadata.SpotPolicy).Append("/matrix|")
                    .Append(power.Id).Append('|').Append("D").Append(pose.Percent).Append('|')
                    .Append(cameraToken).Append('|').Append("png=").Append(pngFile).Append('|')
                    .Append(pngSha).Append('|').Append("exr=").Append(exrFile).Append('|')
                    .Append(exrSha).Append('|').Append("state=").Append(stateFile).Append('|')
                    .Append(stateSha).Append('|').Append("mean=").Append(
                        meanLuminance.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append("max=").Append(Format(maxLuminance)).Append('|')
                    .Append("startPower=").Append(Format(power.StartPower01)).Append('|')
                    .Append("administrativePower=").Append(Format(power.AdministrativePower01)).Append('|')
                    .Append("aperture=").Append(Format(PortalTransportMath.ComputeProjectedApertureFraction(
                        pose.Fraction))).Append('|')
                    .Append("directStartEnabledCount=").Append(startEnabled).Append('|')
                    .Append("directAdministrativeEnabledCount=").Append(administrativeEnabled).Append('|')
                    .Append("directRendererStateSha256=").Append(directRendererFingerprint).Append('|')
                    .Append("indirectDiffuse=false|")
                    .Append("captureRoute=").Append(metadata.CaptureRoute).Append('|')
                    .Append("spotPolicy=").Append(metadata.SpotPolicy).Append('|')
                    .Append("environmentFingerprintSha256=").Append(
                        metadata.EnvironmentFingerprintSha256).Append('|')
                    .Append("actualPlayerCamera=").Append(metadata.ActualCamera).Append('|')
                    .Append("assignedDungeonVolume=").Append(metadata.AssignedDungeonVolume).Append('|')
                    .Append("activeScene=").Append(metadata.ActiveScenePath).Append('|')
                    .Append("referenceFrameIndex=").Append(metadata.ReferenceFrameIndex)
                    .AppendLine();
            }
        }

        private sealed class GlobalLightmapFingerprint
        {
            internal readonly string Sha256;

            private GlobalLightmapFingerprint(string sha256)
            {
                Sha256 = sha256;
            }

            internal static GlobalLightmapFingerprint Capture()
            {
                return new GlobalLightmapFingerprint(ComputeCurrent());
            }

            internal void AssertUnchanged(string stage)
            {
                string current = ComputeCurrent();
                if (!string.Equals(Sha256, current, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Global LightmapSettings fingerprint changed at " + stage +
                        ". The direct oracle is forbidden from writing global lightmaps.");
                }
            }

            private static string ComputeCurrent()
            {
                LightmapData[] maps = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
                var builder = new StringBuilder(maps.Length * 180 + 256);
                builder.Append("mode=").Append(LightmapSettings.lightmapsMode).Append('|')
                    .Append("probes=").Append(KExactBasisV1EditorContract.GetObjectIdentity(
                        LightmapSettings.lightProbes)).Append('|').Append("count=").Append(maps.Length);
                for (int i = 0; i < maps.Length; i++)
                {
                    LightmapData map = maps[i];
                    builder.Append("|index=").Append(i).Append(";color=").Append(
                        KExactBasisV1EditorContract.GetObjectIdentity(map != null ? map.lightmapColor : null))
                        .Append(";dir=").Append(KExactBasisV1EditorContract.GetObjectIdentity(
                            map != null ? map.lightmapDir : null)).Append(";mask=").Append(
                            KExactBasisV1EditorContract.GetObjectIdentity(
                                map != null ? map.shadowMask : null));
                }
                return DungeonPortalBakedBasisValidationContract.ComputeSha256(
                    Encoding.UTF8.GetBytes(builder.ToString()));
            }
        }

        private sealed class PairDirectScope
        {
            private const int ExpectedStartIgnoredLightCount = 0;
            private const int ExpectedAdministrativeIgnoredLightCount = 2;

            private readonly KExactBasisV1EditorContract.SceneBindings bindings;
            private readonly DungeonPortalBakedBasisConnectionDriver driver;
            private readonly DungeonPortalBakedBasisDoorShDriver doorSh;
            private readonly RendererSnapshot[] renderers;
            private readonly DirectLightSnapshot[] lights;
            private readonly DoorConfiguration door;
            private readonly bool driverEnabled;
            private readonly bool driverDriveOnStart;
            private readonly bool doorShEnabled;
            private readonly Quaternion originalDoorRotation;
            private readonly string originalSurfaceFingerprint;
            private readonly int additionalRendererCount;
            private bool entered;
            private string directRendererFingerprint;
            private int startEnabledCount;
            private int administrativeEnabledCount;

            internal int RendererCount => renderers.Length;
            internal int AdditionalRendererCount => additionalRendererCount;
            internal int LightCount => lights.Length;
            internal int StartLightCount { get; }
            internal int AdministrativeLightCount { get; }
            internal int StartEnabledCount => startEnabledCount;
            internal int AdministrativeEnabledCount => administrativeEnabledCount;
            internal string OriginalSurfaceFingerprintSha256 => originalSurfaceFingerprint;
            internal string DirectRendererFingerprintSha256 => directRendererFingerprint;

            private PairDirectScope(KExactBasisV1EditorContract.SceneBindings bindings,
                DungeonPortalBakedBasisStartMapRuntimeController controller, RendererSnapshot[] renderers,
                DirectLightSnapshot[] lights, DoorConfiguration door, int additionalRendererCount,
                int startLightCount, int administrativeLightCount, string originalSurfaceFingerprint)
            {
                this.bindings = bindings;
                driver = controller.ConnectionDriver;
                doorSh = controller.DoorShDriver;
                this.renderers = renderers;
                this.lights = lights;
                this.door = door;
                this.additionalRendererCount = additionalRendererCount;
                StartLightCount = startLightCount;
                AdministrativeLightCount = administrativeLightCount;
                this.originalSurfaceFingerprint = originalSurfaceFingerprint;
                driverEnabled = driver.enabled;
                driverDriveOnStart = ReadDriverDriveOnStart(driver);
                doorShEnabled = doorSh.enabled;
                originalDoorRotation = bindings.DoorLeaf.localRotation;
            }

            internal static PairDirectScope Capture(
                KExactBasisV1EditorContract.SceneBindings bindings,
                DungeonPortalBakedBasisStartMapRuntimeController controller)
            {
                if (bindings == null || controller == null)
                    throw new ArgumentNullException(bindings == null ? nameof(bindings) : nameof(controller));
                DungeonPortalBakedBasisConnectionDriver driver = controller.ConnectionDriver;
                DungeonPortalBakedBasisDoorShDriver doorSh = controller.DoorShDriver;
                if (driver == null || doorSh == null || !driver.enabled || !doorSh.enabled ||
                    driver.IsFaultLatched || doorSh.IsFaultLatched ||
                    driver.StartRoomCompositor == null || driver.AdministrativeRoomCompositor == null ||
                    driver.StartRoomCompositor.IsActive || driver.AdministrativeRoomCompositor.IsActive ||
                    doorSh.IsActive || driver.CurrentAperture01 > 0.0001f ||
                    !Approximately(driver.DoorAngleSource.OpenFraction, 0f, DoorTolerance))
                {
                    throw new InvalidOperationException(
                        "Direct oracle requires a closed, inactive-DPBB D0 baseline. It will not tear down a live compositor or alter global lightmaps.");
                }

                Renderer[] production = bindings.ProductionRooms.GetComponentsInChildren<Renderer>(true);
                Renderer[] all = bindings.Root.GetComponentsInChildren<Renderer>(true);
                int additional = all.Length - production.Length;
                if (production.Length != KExactBasisV1EditorContract.ExpectedRendererCount || additional != 0)
                {
                    throw new InvalidOperationException("Production renderer/duplicate-renderer contract changed. production=" +
                                                        production.Length + " additional=" + additional + ".");
                }
                var rendererSnapshots = new RendererSnapshot[production.Length];
                for (int i = 0; i < production.Length; i++)
                    rendererSnapshots[i] = RendererSnapshot.Capture(bindings.ProductionRooms, production[i]);

                Light[] startLights = bindings.StartRoom.GetComponentsInChildren<Light>(true);
                Light[] administrativeLights = bindings.AdministrativeRoom.GetComponentsInChildren<Light>(true);
                if (startLights.Length != KExactBasisV1EditorContract.ExpectedStartLightCount ||
                    administrativeLights.Length != KExactBasisV1EditorContract.ExpectedAdministrativeLightCount)
                {
                    throw new InvalidOperationException("Production 24+54 light contract changed.");
                }
                var lightSnapshots = new DirectLightSnapshot[startLights.Length + administrativeLights.Length];
                int cursor = 0;
                int startIgnored = 0;
                int administrativeIgnored = 0;
                for (int i = 0; i < startLights.Length; i++)
                {
                    bool ignored = HasEnabledIgnoreLightControl(startLights[i]);
                    if (ignored) startIgnored++;
                    lightSnapshots[cursor++] = DirectLightSnapshot.Capture(startLights[i], RoomRole.Start,
                        ignored);
                }
                for (int i = 0; i < administrativeLights.Length; i++)
                {
                    bool ignored = HasEnabledIgnoreLightControl(administrativeLights[i]);
                    if (ignored) administrativeIgnored++;
                    lightSnapshots[cursor++] = DirectLightSnapshot.Capture(administrativeLights[i],
                        RoomRole.Administrative, ignored);
                }
                if (startIgnored != ExpectedStartIgnoredLightCount ||
                    administrativeIgnored != ExpectedAdministrativeIgnoredLightCount)
                {
                    throw new InvalidOperationException("IgnoreLightControl contract changed. start=" +
                                                        startIgnored + " admin=" + administrativeIgnored + ".");
                }
                DoorConfiguration door = DoorConfiguration.Capture(driver.DoorAngleSource);
                if (!ReadDriverDriveOnStart(driver))
                {
                    throw new InvalidOperationException(
                        "Direct oracle requires the normal driver to be enabled before its reversible runtime pause.");
                }
                string originalFingerprint = ComputeOriginalSurfaceFingerprint(rendererSnapshots, lightSnapshots,
                    bindings);
                return new PairDirectScope(bindings, controller, rendererSnapshots, lightSnapshots, door,
                    additional, startLights.Length, administrativeLights.Length, originalFingerprint);
            }

            internal void Enter()
            {
                if (entered)
                    throw new InvalidOperationException("Direct oracle scope is already active.");
                entered = true;
                // Do not disable the connection driver: its OnDisable calls TryRestoreOriginal,
                // which can be allowed to touch the production switchers in another state.  A
                // play-mode-only serialized pause makes its LateUpdate return before any DPBB
                // work, without touching LightmapSettings or saved authoring.  The preflight
                // requires both compositors already inactive.  Door SH's OnDisable restores its
                // captured per-renderer MPBs; it is inactive at D0, so that restore is an exact
                // no-op assertion.
                try
                {
                    WriteDriverDriveOnStart(driver, false);
                    doorSh.enabled = false;
                    for (int i = 0; i < renderers.Length; i++)
                        renderers[i].ApplyDirectIsolation();
                    for (int i = 0; i < lights.Length; i++)
                        lights[i].PrepareRealtimeDirect();
                    directRendererFingerprint = null;
                }
                catch
                {
                    Restore();
                    throw;
                }
            }

            internal void ApplyState(PowerState power, DoorPose pose)
            {
                if (!entered)
                    throw new InvalidOperationException("Direct oracle scope was not entered.");
                if (!driver.enabled || ReadDriverDriveOnStart(driver) || doorSh.enabled || driver.StartRoomCompositor.IsActive ||
                    driver.AdministrativeRoomCompositor.IsActive || doorSh.IsActive)
                {
                    throw new InvalidOperationException(
                        "A DPBB runtime component became active during direct-oracle isolation.");
                }

                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].RestoreMaterialsOnly();
                    renderers[i].ApplyDirectIsolation();
                }
                ApplyRoomEmission(bindings.StartRoom, bindings.StartPowerSet, power.StartPower01);
                ApplyRoomEmission(bindings.AdministrativeRoom, bindings.AdministrativePowerSet,
                    power.AdministrativePower01);
                startEnabledCount = 0;
                administrativeEnabledCount = 0;
                for (int i = 0; i < lights.Length; i++)
                {
                    lights[i].ApplyPower(power);
                    if (lights[i].Light.enabled)
                    {
                        if (lights[i].Room == RoomRole.Start)
                            startEnabledCount++;
                        else
                            administrativeEnabledCount++;
                    }
                }
                SetDoorPose(bindings.DoorLeaf, driver.DoorAngleSource, door, pose.Fraction);
                CaptureExpectedRendererMaterials();
                directRendererFingerprint = ComputeDirectRendererFingerprint(renderers);
            }

            internal void AssertDirectState(PowerState power, DoorPose pose, string stage)
            {
                if (!entered || !driver.enabled || ReadDriverDriveOnStart(driver) || doorSh.enabled ||
                    driver.StartRoomCompositor.IsActive || driver.AdministrativeRoomCompositor.IsActive ||
                    doorSh.IsActive)
                {
                    throw new InvalidOperationException("Direct-oracle component isolation failed at " + stage + ".");
                }
                if (!Approximately(driver.DoorAngleSource.OpenFraction, pose.Fraction, DoorTolerance) ||
                    !Approximately(driver.DoorAngleSource.ApertureFraction,
                        PortalTransportMath.ComputeProjectedApertureFraction(pose.Fraction), 0.0001f))
                {
                    throw new InvalidOperationException("Physical door pose drifted at " + stage + ".");
                }
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].AssertDirectIsolation(stage);
                int observedStart = 0;
                int observedAdministrative = 0;
                for (int i = 0; i < lights.Length; i++)
                {
                    lights[i].AssertRealtimeDirect(power, stage);
                    if (lights[i].Light.enabled)
                    {
                        if (lights[i].Room == RoomRole.Start)
                            observedStart++;
                        else
                            observedAdministrative++;
                    }
                }
                int expectedStart = power.StartPower01 >= 0.9999f ? StartLightCount :
                    ExpectedStartIgnoredLightCount;
                int expectedAdministrative = power.AdministrativePower01 >= 0.9999f
                    ? AdministrativeLightCount
                    : ExpectedAdministrativeIgnoredLightCount;
                if (observedStart != expectedStart || observedAdministrative != expectedAdministrative ||
                    startEnabledCount != observedStart || administrativeEnabledCount != observedAdministrative)
                {
                    throw new InvalidOperationException("Realtime-direct production-light count drifted at " +
                                                        stage + ". start=" + observedStart +
                                                        " admin=" + observedAdministrative + ".");
                }
                string current = ComputeDirectRendererFingerprint(renderers);
                if (!string.Equals(current, directRendererFingerprint, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Direct renderer/material state drifted at " + stage + ".");
                }
            }

            internal void Restore()
            {
                if (!entered)
                    return;
                Exception restoreFailure = null;
                try
                {
                    for (int i = 0; i < lights.Length; i++)
                    {
                        try
                        {
                            lights[i].Restore();
                        }
                        catch (Exception exception)
                        {
                            restoreFailure ??= exception;
                        }
                    }
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        try
                        {
                            renderers[i].Restore();
                        }
                        catch (Exception exception)
                        {
                            restoreFailure ??= exception;
                        }
                    }
                }
                finally
                {
                    bindings.DoorLeaf.localRotation = originalDoorRotation;
                    Physics.SyncTransforms();
                    driver.DoorAngleSource.EvaluateNow();
                    doorSh.enabled = doorShEnabled;
                    WriteDriverDriveOnStart(driver, driverDriveOnStart);
                    entered = false;
                }
                if (restoreFailure != null)
                {
                    throw new InvalidOperationException(
                        "One or more direct-oracle pair objects could not restore.", restoreFailure);
                }
            }

            internal void AssertRestored()
            {
                if (entered || driver.enabled != driverEnabled ||
                    ReadDriverDriveOnStart(driver) != driverDriveOnStart || doorSh.enabled != doorShEnabled ||
                    bindings.DoorLeaf.localRotation != originalDoorRotation ||
                    !Approximately(driver.DoorAngleSource.OpenFraction, 0f, DoorTolerance) ||
                    driver.StartRoomCompositor.IsActive || driver.AdministrativeRoomCompositor.IsActive ||
                    doorSh.IsActive)
                {
                    throw new InvalidOperationException("Direct-oracle runtime state did not restore exactly.");
                }
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].AssertRestored();
                for (int i = 0; i < lights.Length; i++)
                    lights[i].AssertRestored();
                string current = ComputeOriginalSurfaceFingerprint(renderers, lights, bindings);
                if (!string.Equals(current, originalSurfaceFingerprint, StringComparison.Ordinal))
                    throw new InvalidOperationException("Direct-oracle production surface fingerprint did not restore.");
            }

            private void CaptureExpectedRendererMaterials()
            {
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].CaptureExpectedMaterials();
            }

            private static void ApplyRoomEmission(
                Transform room,
                DungeonTilePowerBakeSet powerSet,
                float power01)
            {
                if (room == null || powerSet == null)
                    throw new InvalidOperationException("Direct-oracle emission room/power-set binding is missing.");
                DungeonTilePowerBakeSet.EmissionMaterialEntry[] entries = powerSet.EmissionMaterialEntries;
                if (entries == null || entries.Length == 0)
                    throw new InvalidOperationException("Direct-oracle emission entries are missing for " + room.name + ".");
                var buckets = new Dictionary<string, List<Renderer>>(StringComparer.Ordinal);
                Renderer[] all = room.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    Renderer renderer = all[i];
                    if (renderer == null)
                        continue;
                    string path = GetBareRelativePath(room, renderer.transform);
                    if (!buckets.TryGetValue(path, out List<Renderer> bucket))
                    {
                        bucket = new List<Renderer>(1);
                        buckets.Add(path, bucket);
                    }
                    bucket.Add(renderer);
                }
                var changed = new Dictionary<Renderer, Material[]>();
                for (int i = 0; i < entries.Length; i++)
                {
                    DungeonTilePowerBakeSet.EmissionMaterialEntry entry = entries[i];
                    if (string.IsNullOrWhiteSpace(entry.relativePath) ||
                        !buckets.TryGetValue(entry.relativePath, out List<Renderer> bucket) ||
                        bucket == null || bucket.Count == 0)
                    {
                        continue;
                    }
                    int rendererIndex = Mathf.Clamp(entry.rendererBucketIndex, 0, bucket.Count - 1);
                    Renderer renderer = bucket[rendererIndex];
                    if (renderer == null || HasEnabledIgnoreEmissionControl(renderer))
                        continue;
                    if (!changed.TryGetValue(renderer, out Material[] materials))
                    {
                        Material[] source = renderer.sharedMaterials;
                        if (source == null || source.Length == 0)
                            continue;
                        materials = (Material[])source.Clone();
                        changed.Add(renderer, materials);
                    }
                    if (entry.materialIndex < 0 || entry.materialIndex >= materials.Length)
                        continue;
                    Material replacement = power01 >= 0.9999f
                        ? FirstNonNull(entry.power100Material, entry.power00Material)
                        : FirstNonNull(entry.power00Material, entry.power100Material);
                    if (replacement != null)
                        materials[entry.materialIndex] = replacement;
                }
                foreach (KeyValuePair<Renderer, Material[]> pair in changed)
                    pair.Key.sharedMaterials = pair.Value;
            }

            private static Material FirstNonNull(Material first, Material second)
            {
                return first != null ? first : second;
            }

            private static bool HasEnabledIgnoreEmissionControl(Component component)
            {
                IgnoreEmissionControl marker = component != null
                    ? component.GetComponentInParent<IgnoreEmissionControl>(true)
                    : null;
                return marker != null && marker.enabled;
            }

            private static bool HasEnabledIgnoreLightControl(Component component)
            {
                IgnoreLightControl marker = component != null
                    ? component.GetComponentInParent<IgnoreLightControl>(true)
                    : null;
                return marker != null && marker.enabled;
            }

            private static string GetBareRelativePath(Transform root, Transform target)
            {
                if (root == target)
                    return string.Empty;
                var names = new Stack<string>();
                Transform current = target;
                while (current != null && current != root)
                {
                    names.Push(current.name);
                    current = current.parent;
                }
                if (current != root)
                    throw new InvalidOperationException("Renderer escaped room hierarchy.");
                return string.Join("/", names);
            }

            private static void SetDoorPose(Transform doorLeaf, DungeonPortalDoorAngleSource source,
                DoorConfiguration configuration, float fraction)
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

            private static string ComputeOriginalSurfaceFingerprint(RendererSnapshot[] rendererSnapshots,
                DirectLightSnapshot[] lightSnapshots, KExactBasisV1EditorContract.SceneBindings bindings)
            {
                var rows = new List<string>(rendererSnapshots.Length + lightSnapshots.Length);
                for (int i = 0; i < rendererSnapshots.Length; i++)
                    rows.Add("R|" + rendererSnapshots[i].OriginalFingerprintRow);
                for (int i = 0; i < lightSnapshots.Length; i++)
                    rows.Add("L|" + lightSnapshots[i].OriginalFingerprintRow);
                rows.Add("door=" + bindings.DoorLeaf.localRotation);
                rows.Sort(StringComparer.Ordinal);
                return DungeonPortalBakedBasisValidationContract.ComputeSha256(
                    Encoding.UTF8.GetBytes(string.Join("\n", rows)));
            }

            private static string ComputeDirectRendererFingerprint(RendererSnapshot[] rendererSnapshots)
            {
                var rows = new List<string>(rendererSnapshots.Length);
                for (int i = 0; i < rendererSnapshots.Length; i++)
                    rows.Add(rendererSnapshots[i].DirectFingerprintRow());
                rows.Sort(StringComparer.Ordinal);
                return DungeonPortalBakedBasisValidationContract.ComputeSha256(
                    Encoding.UTF8.GetBytes(string.Join("\n", rows)));
            }

            private static bool ReadDriverDriveOnStart(DungeonPortalBakedBasisConnectionDriver driver)
            {
                SerializedObject serialized = new SerializedObject(driver);
                serialized.UpdateIfRequiredOrScript();
                SerializedProperty property = serialized.FindProperty("driveOnStart");
                if (property == null || property.propertyType != SerializedPropertyType.Boolean)
                    throw new InvalidOperationException("Connection-driver driveOnStart pause seam is missing.");
                return property.boolValue;
            }

            private static void WriteDriverDriveOnStart(
                DungeonPortalBakedBasisConnectionDriver driver, bool value)
            {
                SerializedObject serialized = new SerializedObject(driver);
                serialized.UpdateIfRequiredOrScript();
                SerializedProperty property = serialized.FindProperty("driveOnStart");
                if (property == null || property.propertyType != SerializedPropertyType.Boolean)
                    throw new InvalidOperationException("Connection-driver driveOnStart pause seam is missing.");
                if (property.boolValue == value)
                    return;
                property.boolValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private sealed class DoorConfiguration
        {
            private readonly Quaternion closedRotation;
            private readonly Vector3 hingeAxis;
            private readonly float openAngleDegrees;

            private DoorConfiguration(Quaternion closedRotation, Vector3 hingeAxis,
                float openAngleDegrees)
            {
                this.closedRotation = closedRotation;
                this.hingeAxis = hingeAxis;
                this.openAngleDegrees = openAngleDegrees;
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
                    throw new InvalidOperationException("Door must use a canonical 90-degree live angle source.");
                }
                return new DoorConfiguration(closed.quaternionValue, axis.vector3Value.normalized,
                    angle.floatValue);
            }

            internal Quaternion RotationFor(float fraction)
            {
                return closedRotation * Quaternion.AngleAxis(openAngleDegrees * Mathf.Clamp01(fraction),
                    hingeAxis);
            }
        }

        private enum RoomRole
        {
            Start,
            Administrative
        }

        private sealed class RendererSnapshot
        {
            private readonly Renderer renderer;
            private readonly string stableKey;
            private readonly bool enabled;
            private readonly bool forceRenderingOff;
            private readonly ShadowCastingMode shadowCastingMode;
            private readonly bool receiveShadows;
            private readonly uint renderingLayerMask;
            private readonly int gameObjectLayer;
            private readonly int lightmapIndex;
            private readonly int realtimeLightmapIndex;
            private readonly Vector4 lightmapScaleOffset;
            private readonly Vector4 realtimeLightmapScaleOffset;
            private readonly LightProbeUsage lightProbeUsage;
            private readonly ReflectionProbeUsage reflectionProbeUsage;
            private readonly Transform probeAnchor;
            private readonly GameObject lightProbeProxyVolumeOverride;
            private readonly Material[] materials;
            private readonly Mesh mesh;
            private readonly Mesh additionalVertexStreams;
            private readonly bool hadPropertyBlock;
            private readonly string materialPropertyBlockFingerprint;
            private Material[] expectedMaterials;

            internal string OriginalFingerprintRow { get; }

            private RendererSnapshot(Transform productionRoot, Renderer renderer)
            {
                this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
                stableKey = KExactBasisV1EditorContract.GetStableComponentKey(productionRoot, renderer);
                enabled = renderer.enabled;
                forceRenderingOff = renderer.forceRenderingOff;
                shadowCastingMode = renderer.shadowCastingMode;
                receiveShadows = renderer.receiveShadows;
                renderingLayerMask = renderer.renderingLayerMask;
                gameObjectLayer = renderer.gameObject.layer;
                lightmapIndex = renderer.lightmapIndex;
                realtimeLightmapIndex = renderer.realtimeLightmapIndex;
                lightmapScaleOffset = renderer.lightmapScaleOffset;
                realtimeLightmapScaleOffset = renderer.realtimeLightmapScaleOffset;
                lightProbeUsage = renderer.lightProbeUsage;
                reflectionProbeUsage = renderer.reflectionProbeUsage;
                probeAnchor = renderer.probeAnchor;
                lightProbeProxyVolumeOverride = renderer.lightProbeProxyVolumeOverride;
                materials = CloneMaterials(renderer.sharedMaterials);
                mesh = ResolveMesh(renderer);
                additionalVertexStreams = renderer is MeshRenderer meshRenderer
                    ? meshRenderer.additionalVertexStreams
                    : null;
                hadPropertyBlock = renderer.HasPropertyBlock();
                materialPropertyBlockFingerprint =
                    KExactBasisV1EditorContract.ComputeMaterialPropertyBlockFingerprint(renderer);
                OriginalFingerprintRow = stableKey + "|mesh=" +
                    KExactBasisV1EditorContract.GetObjectIdentity(mesh) + "|enabled=" + enabled +
                    "|forceOff=" + forceRenderingOff + "|shadow=" + shadowCastingMode +
                    "|receiveShadows=" + receiveShadows + "|renderLayer=" + renderingLayerMask +
                    "|gameLayer=" + gameObjectLayer +
                    "|lm=" + lightmapIndex + "|rtLm=" + realtimeLightmapIndex + "|lmST=" +
                    KExactBasisV1EditorContract.FormatVector4(lightmapScaleOffset) + "|rtLmST=" +
                    KExactBasisV1EditorContract.FormatVector4(realtimeLightmapScaleOffset) +
                    "|probe=" + lightProbeUsage + "|reflection=" + reflectionProbeUsage +
                    "|probeAnchor=" + KExactBasisV1EditorContract.GetObjectIdentity(probeAnchor) +
                    "|probeVolume=" + KExactBasisV1EditorContract.GetObjectIdentity(
                        lightProbeProxyVolumeOverride) +
                    "|materials=" + MaterialFingerprint(materials) + "|mpb=" +
                    materialPropertyBlockFingerprint + "|additional=" +
                    KExactBasisV1EditorContract.GetObjectIdentity(additionalVertexStreams);
            }

            internal static RendererSnapshot Capture(Transform productionRoot, Renderer renderer)
            {
                return new RendererSnapshot(productionRoot, renderer);
            }

            internal void ApplyDirectIsolation()
            {
                renderer.lightmapIndex = -1;
                renderer.realtimeLightmapIndex = -1;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            internal void RestoreMaterialsOnly()
            {
                renderer.sharedMaterials = CloneMaterials(materials);
                expectedMaterials = null;
            }

            internal void CaptureExpectedMaterials()
            {
                expectedMaterials = CloneMaterials(renderer.sharedMaterials);
            }

            internal void AssertDirectIsolation(string stage)
            {
                if (renderer == null || renderer.enabled != enabled ||
                    renderer.forceRenderingOff != forceRenderingOff ||
                    renderer.shadowCastingMode != shadowCastingMode ||
                    renderer.receiveShadows != receiveShadows ||
                    renderer.renderingLayerMask != renderingLayerMask ||
                    renderer.gameObject.layer != gameObjectLayer ||
                    renderer.lightmapIndex != -1 || renderer.realtimeLightmapIndex != -1 ||
                    renderer.lightmapScaleOffset != lightmapScaleOffset ||
                    renderer.realtimeLightmapScaleOffset != realtimeLightmapScaleOffset ||
                    renderer.lightProbeUsage != LightProbeUsage.Off ||
                    renderer.reflectionProbeUsage != ReflectionProbeUsage.Off ||
                    renderer.probeAnchor != probeAnchor ||
                    renderer.lightProbeProxyVolumeOverride != lightProbeProxyVolumeOverride ||
                    renderer.HasPropertyBlock() != hadPropertyBlock ||
                    !string.Equals(KExactBasisV1EditorContract.ComputeMaterialPropertyBlockFingerprint(renderer),
                        materialPropertyBlockFingerprint, StringComparison.Ordinal) ||
                    ResolveMesh(renderer) != mesh ||
                    (renderer is MeshRenderer meshRenderer &&
                     meshRenderer.additionalVertexStreams != additionalVertexStreams) ||
                    !MaterialArraysEqual(renderer.sharedMaterials, expectedMaterials))
                {
                    throw new InvalidOperationException("Direct renderer isolation changed an unallowed field at " +
                                                        stage + ": " + stableKey);
                }
            }

            internal void Restore()
            {
                renderer.sharedMaterials = CloneMaterials(materials);
                renderer.lightmapIndex = lightmapIndex;
                renderer.realtimeLightmapIndex = realtimeLightmapIndex;
                renderer.lightmapScaleOffset = lightmapScaleOffset;
                renderer.realtimeLightmapScaleOffset = realtimeLightmapScaleOffset;
                renderer.lightProbeUsage = lightProbeUsage;
                renderer.reflectionProbeUsage = reflectionProbeUsage;
                expectedMaterials = null;
            }

            internal void AssertRestored()
            {
                if (renderer == null || renderer.enabled != enabled ||
                    renderer.forceRenderingOff != forceRenderingOff ||
                    renderer.shadowCastingMode != shadowCastingMode ||
                    renderer.receiveShadows != receiveShadows ||
                    renderer.renderingLayerMask != renderingLayerMask ||
                    renderer.gameObject.layer != gameObjectLayer ||
                    renderer.lightmapIndex != lightmapIndex ||
                    renderer.realtimeLightmapIndex != realtimeLightmapIndex ||
                    renderer.lightmapScaleOffset != lightmapScaleOffset ||
                    renderer.realtimeLightmapScaleOffset != realtimeLightmapScaleOffset ||
                    renderer.lightProbeUsage != lightProbeUsage ||
                    renderer.reflectionProbeUsage != reflectionProbeUsage ||
                    renderer.probeAnchor != probeAnchor ||
                    renderer.lightProbeProxyVolumeOverride != lightProbeProxyVolumeOverride ||
                    renderer.HasPropertyBlock() != hadPropertyBlock ||
                    !string.Equals(KExactBasisV1EditorContract.ComputeMaterialPropertyBlockFingerprint(renderer),
                        materialPropertyBlockFingerprint, StringComparison.Ordinal) ||
                    ResolveMesh(renderer) != mesh ||
                    (renderer is MeshRenderer meshRenderer &&
                     meshRenderer.additionalVertexStreams != additionalVertexStreams) ||
                    !MaterialArraysEqual(renderer.sharedMaterials, materials))
                {
                    throw new InvalidOperationException("Renderer did not restore exactly: " + stableKey);
                }
            }

            internal string DirectFingerprintRow()
            {
                return stableKey + "|lm=" + renderer.lightmapIndex + "|rtLm=" +
                    renderer.realtimeLightmapIndex + "|probe=" + renderer.lightProbeUsage +
                    "|reflection=" + renderer.reflectionProbeUsage + "|materials=" +
                    MaterialFingerprint(renderer.sharedMaterials) + "|mpb=" +
                    KExactBasisV1EditorContract.ComputeMaterialPropertyBlockFingerprint(renderer);
            }

            private static Mesh ResolveMesh(Renderer renderer)
            {
                if (renderer is SkinnedMeshRenderer skinned)
                    return skinned.sharedMesh;
                MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
                return filter != null ? filter.sharedMesh : null;
            }

            private static Material[] CloneMaterials(Material[] value)
            {
                return value != null ? (Material[])value.Clone() : Array.Empty<Material>();
            }

            private static bool MaterialArraysEqual(Material[] left, Material[] right)
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

            private static string MaterialFingerprint(Material[] values)
            {
                Material[] materials = values ?? Array.Empty<Material>();
                var builder = new StringBuilder(materials.Length * 160);
                builder.Append("count=").Append(materials.Length);
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    builder.Append(";slot=").Append(i).Append(";material=").Append(
                        KExactBasisV1EditorContract.GetObjectIdentity(material));
                    if (material == null)
                        continue;
                    string[] keywords = material.shaderKeywords ?? Array.Empty<string>();
                    keywords = (string[])keywords.Clone();
                    Array.Sort(keywords, StringComparer.Ordinal);
                    builder.Append(";shader=").Append(
                        KExactBasisV1EditorContract.GetObjectIdentity(material.shader)).Append(";queue=")
                        .Append(material.renderQueue).Append(";instancing=").Append(
                            material.enableInstancing).Append(";doubleSidedGi=").Append(
                            material.doubleSidedGI).Append(";giFlags=").Append(
                            material.globalIlluminationFlags).Append(";keywords=").Append(
                            string.Join(",", keywords));
                }
                return DungeonPortalBakedBasisValidationContract.ComputeSha256(
                    Encoding.UTF8.GetBytes(builder.ToString()));
            }
        }

        private sealed class DirectLightSnapshot
        {
            internal readonly Light Light;
            internal readonly RoomRole Room;
            private readonly bool enabled;
            private readonly bool ignoreLightControl;
            private readonly LightmapBakeType lightmapBakeType;
            private readonly float bounceIntensity;
            private readonly LightType type;
            private readonly LightShadows shadows;
            private readonly Color color;
            private readonly float intensity;
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
            private readonly Vector3 position;
            private readonly Quaternion rotation;
            private readonly Vector3 localScale;
            private readonly Component[] components;

            internal string OriginalFingerprintRow { get; }

            private DirectLightSnapshot(Light light, RoomRole room, bool ignoreLightControl)
            {
                Light = light ?? throw new ArgumentNullException(nameof(light));
                Room = room;
                this.ignoreLightControl = ignoreLightControl;
                enabled = light.enabled;
                lightmapBakeType = light.lightmapBakeType;
                bounceIntensity = light.bounceIntensity;
                type = light.type;
                shadows = light.shadows;
                color = light.color;
                intensity = light.intensity;
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
                position = light.transform.position;
                rotation = light.transform.rotation;
                localScale = light.transform.localScale;
                components = light.GetComponents<Component>();
                if (!light.gameObject.activeInHierarchy || lightmapBakeType != LightmapBakeType.Baked ||
                    shadows != LightShadows.Soft)
                {
                    throw new InvalidOperationException("Production direct-light descriptor changed: " +
                                                        light.name);
                }
                OriginalFingerprintRow = room + "|" + light.name + "|enabled=" + enabled +
                    "|bake=" + lightmapBakeType + "|bounce=" + Format(bounceIntensity) +
                    "|type=" + type + "|shadow=" + shadows + "|color=" + color + "|intensity=" +
                    Format(intensity) + "|range=" + Format(range) + "|spot=" + Format(spotAngle) +
                    "|inner=" + Format(innerSpotAngle) + "|mask=" + cullingMask + "|layer=" +
                    renderingLayerMask + "|cookie=" +
                    KExactBasisV1EditorContract.GetObjectIdentity(cookie) + "|ignore=" +
                    ignoreLightControl;
            }

            internal static DirectLightSnapshot Capture(Light light, RoomRole room, bool ignored)
            {
                return new DirectLightSnapshot(light, room, ignored);
            }

            internal void PrepareRealtimeDirect()
            {
                Light.lightmapBakeType = LightmapBakeType.Realtime;
                Light.bounceIntensity = 0f;
            }

            internal void ApplyPower(PowerState state)
            {
                bool roomOn = Room == RoomRole.Start
                    ? state.StartPower01 >= 0.9999f
                    : state.AdministrativePower01 >= 0.9999f;
                // The original direct-reference contract retains the two enabled administrative
                // IgnoreLightControl exceptions at P0.  Runtime switcher Awake has disabled the
                // physical Light components, so using their transient enabled state here would
                // silently erase that production exception.
                Light.enabled = ignoreLightControl || roomOn;
            }

            internal void AssertRealtimeDirect(PowerState state, string stage)
            {
                bool roomOn = Room == RoomRole.Start
                    ? state.StartPower01 >= 0.9999f
                    : state.AdministrativePower01 >= 0.9999f;
                bool expectedEnabled = ignoreLightControl || roomOn;
                if (Light == null || !Light.gameObject.activeInHierarchy || Light.enabled != expectedEnabled ||
                    Light.lightmapBakeType != LightmapBakeType.Realtime ||
                    !Mathf.Approximately(Light.bounceIntensity, 0f) || Light.type != type ||
                    Light.shadows != shadows || Light.color != color ||
                    !Mathf.Approximately(Light.intensity, intensity) ||
                    !Mathf.Approximately(Light.range, range) ||
                    !Mathf.Approximately(Light.spotAngle, spotAngle) ||
                    !Mathf.Approximately(Light.innerSpotAngle, innerSpotAngle) ||
                    Light.cullingMask != cullingMask || Light.renderingLayerMask != renderingLayerMask ||
                    Light.cookie != cookie || Light.renderMode != renderMode ||
                    !Mathf.Approximately(Light.shadowStrength, shadowStrength) ||
                    !Mathf.Approximately(Light.shadowBias, shadowBias) ||
                    !Mathf.Approximately(Light.shadowNormalBias, shadowNormalBias) ||
                    !Mathf.Approximately(Light.shadowNearPlane, shadowNearPlane) ||
                    Light.transform.position != position || Light.transform.rotation != rotation ||
                    Light.transform.localScale != localScale ||
                    !SameComponents(Light.GetComponents<Component>(), components))
                {
                    throw new InvalidOperationException("Realtime direct-light contract changed at " + stage +
                                                        ": " + Light.name);
                }
            }

            internal void Restore()
            {
                Light.enabled = enabled;
                Light.lightmapBakeType = lightmapBakeType;
                Light.bounceIntensity = bounceIntensity;
            }

            internal void AssertRestored()
            {
                if (Light == null || Light.enabled != enabled ||
                    Light.lightmapBakeType != lightmapBakeType ||
                    !Mathf.Approximately(Light.bounceIntensity, bounceIntensity) ||
                    Light.type != type || Light.shadows != shadows || Light.color != color ||
                    !Mathf.Approximately(Light.intensity, intensity) ||
                    !Mathf.Approximately(Light.range, range) ||
                    !Mathf.Approximately(Light.spotAngle, spotAngle) ||
                    !Mathf.Approximately(Light.innerSpotAngle, innerSpotAngle) ||
                    Light.cullingMask != cullingMask || Light.renderingLayerMask != renderingLayerMask ||
                    Light.cookie != cookie || Light.renderMode != renderMode ||
                    !Mathf.Approximately(Light.shadowStrength, shadowStrength) ||
                    !Mathf.Approximately(Light.shadowBias, shadowBias) ||
                    !Mathf.Approximately(Light.shadowNormalBias, shadowNormalBias) ||
                    !Mathf.Approximately(Light.shadowNearPlane, shadowNearPlane) ||
                    Light.transform.position != position || Light.transform.rotation != rotation ||
                    Light.transform.localScale != localScale ||
                    !SameComponents(Light.GetComponents<Component>(), components))
                {
                    throw new InvalidOperationException("Production Light did not restore exactly: " +
                                                        (Light != null ? Light.name : "MISSING"));
                }
            }

            private static bool SameComponents(Component[] left, Component[] right)
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
                    throw new InvalidOperationException("Unity logged a render error during " + stage + ": " +
                                                        failure);
            }

            private void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    failure ??= (condition ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
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
                    presentationRender = CreateTarget("DPBB_DirectOracle_Presentation_Render", 24,
                        RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    presentationResolve = CreateTarget("DPBB_DirectOracle_Presentation_Resolve", 0,
                        RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    hdrRender = CreateTarget("DPBB_DirectOracle_HDR_Render", 24,
                        RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                    hdrResolve = CreateTarget("DPBB_DirectOracle_HDR_Resolve", 0,
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
