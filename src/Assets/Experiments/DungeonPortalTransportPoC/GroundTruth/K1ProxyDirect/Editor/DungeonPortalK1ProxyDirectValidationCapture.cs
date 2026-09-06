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

namespace DungeonPortalTransportPoC.GroundTruth.K1ProxyDirect
{
    /// <summary>
    /// Isolated comparison capture for the two serialized K=1 endpoint descriptors.
    ///
    /// This is deliberately not a bake/realtime equivalence verdict. It removes all
    /// room lightmaps, probe sampling, reflections, and production Lights from a deep
    /// preview clone, then renders only the two manually constructed K=1 Spot proxies.
    /// Source materials (including emission), renderer layers, rendering-layer masks,
    /// and MaterialPropertyBlocks are never changed.
    /// </summary>
    public static class DungeonPortalK1ProxyDirectValidationCapture
    {
        public const string Status = "K1_PROXY_DIRECT_VALIDATION_ONLY";

        private const string ToolSourcePath =
            "Assets/Experiments/DungeonPortalTransportPoC/GroundTruth/K1ProxyDirect/Editor/" +
            "DungeonPortalK1ProxyDirectValidationCapture.cs";
        private const string ValidationScenePath =
            "Assets/Experiments/DungeonPortalTransportPoC/Scenes/" +
            "Start_Admin_PortalTransportValidation.unity";
        private const string StartProfilePath =
            "Assets/Experiments/DungeonPortalTransportPoC/Generated/EndpointProfiles/" +
            "StartRoom_R000_EndpointProfile.asset";
        private const string AdministrativeProfilePath =
            "Assets/Experiments/DungeonPortalTransportPoC/Generated/EndpointProfiles/" +
            "AdminstrativeSegregation_R000_EndpointProfile.asset";
        private const string ObjectIdShaderPath =
            "Assets/Experiments/DungeonPortalTransportPoC/Shaders/" +
            "DungeonPortalReceiverBounceObjectIdMrt.shader";
        private const string ObjectIdShaderName =
            "Hidden/DungeonPortalTransportPoC/ReceiverBounceObjectIdMrt";
        private const string EvidenceRoot =
            "Assets/Experiments/DungeonPortalTransportPoC/Evidence/GroundTruth/" +
            "K1ProxyDirect";
        private const string ExpectedValidationSceneSha256 =
            "F087DD274822D9F8071CE8ADD0E261BC5314B2999EF4BDF3B1C1CB1B5DC4AF79";

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
        private const string AddedDoorName =
            "Door_SM_A_Door_Placement_ActiveSceneInstance";
        private const string DoorLeafName = "Door_01";
        private const string DoorPositiveRendererName = "DungeonDoorProbe_PositiveZ";
        private const string DoorNegativeRendererName = "DungeonDoorProbe_NegativeZ";
        private const string DoorEdgeRendererName = "DungeonDoorProbe_Edge";
        private const string StartCameraName = "Start_to_Admin_FixedCamera_DISABLED";
        private const string AdministrativeCameraName =
            "Admin_to_Start_FixedCamera_DISABLED";
        private const string OwnedCloneRootName =
            ValidationRootName + "__K1ProxyDirectClone";

        private const int ExpectedValidationRootChildren = 4;
        private const int ExpectedRendererCount = 389;
        private const int ExpectedMask2RendererCount = 322;
        private const int ExpectedMask1RendererCount = 67;
        private const int ExpectedStartLightCount = 24;
        private const int ExpectedAdministrativeLightCount = 54;
        private const int ExpectedProductionLightCount =
            ExpectedStartLightCount + ExpectedAdministrativeLightCount;
        private const int ExpectedDoorRendererCount = 4;
        private const int ExpectedDoorEnabledRendererCount = 3;
        private const int ExpectedDoorEnabledMask1Count = 3;
        private const int ExpectedEndpointCount = 2;
        private const int ExpectedCameraCount = 2;
        private const int CaptureWidth = 1920;
        private const int CaptureHeight = 1080;
        private const int UnsupportedObjectId = 0xFF00FF;
        private const double MinimumValidMeanLinearLuminance = 1e-12d;
        private const float MinimumValidMaxLinearLuminance = 1e-8f;
        private const double MinimumPoweredMeanDelta = 1e-9d;

        private static readonly PowerState[] PowerStates =
        {
            new PowerState("P000_P000", 0f, 0f),
            new PowerState("P100_P000", 1f, 0f),
            new PowerState("P000_P100", 0f, 1f),
            new PowerState("P100_P100", 1f, 1f)
        };

        private static readonly DoorPose[] DoorPoses =
        {
            new DoorPose(0),
            new DoorPose(25),
            new DoorPose(50),
            new DoorPose(75),
            new DoorPose(100)
        };

        private static readonly Variant[] Variants =
        {
            new Variant("PROFILE_EXACT_MASK2", false),
            new Variant("DOOR_RECEIVER_MASK3_CANDIDATE", true)
        };

        private static readonly string[] CameraNames =
        {
            StartCameraName,
            AdministrativeCameraName
        };

        [MenuItem(
            "Tools/Dungeon/Lighting/Portal Transport PoC/" +
            "Capture K1 Proxy Direct Validation (Edit Mode)")]
        public static void CaptureFromMenu()
        {
            Debug.Log(CaptureAll());
        }

        /// <summary>
        /// Captures 2 variants x 4 room-power states x 5 door poses x 2 fixed
        /// cameras. It never opens/saves a scene, enters Play Mode, or starts a bake.
        /// </summary>
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

            SourceSceneSnapshot sourceSnapshot = SourceSceneSnapshot.Capture(
                sourceScene,
                sourceRoot);
            SelectionSnapshot selectionSnapshot = SelectionSnapshot.Capture();
            GlobalRenderSnapshot globalSnapshot = GlobalRenderSnapshot.Capture();
            CaptureIdentity identity = CaptureIdentity.Capture();
            string timestamp = DateTime.UtcNow.ToString(
                "yyyyMMdd'T'HHmmssfff'Z'",
                CultureInfo.InvariantCulture);
            string outputFolder = EvidenceRoot + "/" + timestamp;

            Scene previewScene = default;
            GameObject staging = null;
            GameObject previewRoot = null;
            RenderTargets targets = null;
            MaskCaptureSession maskSession = null;
            RenderFailureMonitor renderFailureMonitor = null;
            PreviewContract contract = null;
            ContractReport contractReport = null;
            var captures = new List<CaptureEvidence>(
                Variants.Length * PowerStates.Length * DoorPoses.Length * CameraNames.Length);
            var masks = new List<MaskEvidence>(DoorPoses.Length * CameraNames.Length);
            var blockers = new List<string>();
            bool restorationCompleted = false;

            try
            {
                renderFailureMonitor = new RenderFailureMonitor();
                previewScene = EditorSceneManager.NewPreviewScene();
                if (!previewScene.IsValid() || !previewScene.isLoaded ||
                    !EditorSceneManager.IsPreviewScene(previewScene))
                {
                    throw new InvalidOperationException(
                        "Unity did not create the required isolated preview scene.");
                }

                // The inactive staging parent prevents any cloned PoC MonoBehaviour
                // from observing an active hierarchy before it is explicitly disabled.
                staging = new GameObject("__K1ProxyDirect_InactiveCloneStaging")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                staging.SetActive(false);
                SceneManager.MoveGameObjectToScene(staging, previewScene);

                previewRoot = Object.Instantiate(
                    sourceRoot,
                    staging.transform,
                    false);
                previewRoot.name = OwnedCloneRootName;
                previewRoot.hideFlags = HideFlags.HideAndDontSave;

                if (sourceScene.isDirty)
                    throw new InvalidOperationException("Deep cloning dirtied the source scene.");

                contract = PreviewContract.CreateInactive(previewRoot, previewScene);
                contract.PrepareInactiveClone();
                globalSnapshot.ApplyNoLightmaps();

                previewRoot.SetActive(false);
                previewRoot.transform.SetParent(null, true);
                SceneManager.MoveGameObjectToScene(previewRoot, previewScene);
                Object.DestroyImmediate(staging);
                staging = null;
                previewRoot.SetActive(true);
                contract.AssertActivatedPreparedClone();

                targets = RenderTargets.Create();
                maskSession = MaskCaptureSession.TryCreate(contract, blockers);

                if (maskSession != null)
                {
                    try
                    {
                        for (int poseIndex = 0; poseIndex < DoorPoses.Length; poseIndex++)
                        {
                            DoorPose pose = DoorPoses[poseIndex];
                            contract.Door.ApplyPose(pose);
                            for (int cameraIndex = 0;
                                 cameraIndex < contract.Cameras.Length;
                                 cameraIndex++)
                            {
                                masks.Add(maskSession.Capture(
                                    contract.Cameras[cameraIndex],
                                    pose,
                                    contract.GetDirectionForCamera(
                                        contract.Cameras[cameraIndex].name)));
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        blockers.Add("MASK_CAPTURE_RUNTIME_FAILED: " + exception.Message);
                        masks.Clear();
                        maskSession.Dispose();
                        maskSession = null;
                    }
                }

                for (int variantIndex = 0; variantIndex < Variants.Length; variantIndex++)
                {
                    Variant variant = Variants[variantIndex];
                    contract.Proxies.ApplyVariant(variant);

                    for (int powerIndex = 0; powerIndex < PowerStates.Length; powerIndex++)
                    {
                        PowerState power = PowerStates[powerIndex];
                        contract.Proxies.ApplyPower(power);

                        for (int poseIndex = 0; poseIndex < DoorPoses.Length; poseIndex++)
                        {
                            DoorPose pose = DoorPoses[poseIndex];
                            contract.Door.ApplyPose(pose);
                            contract.AssertState(variant, power, pose);

                            for (int cameraIndex = 0;
                                 cameraIndex < contract.Cameras.Length;
                                 cameraIndex++)
                            {
                                captures.Add(CaptureCameraPair(
                                    contract.Cameras[cameraIndex],
                                    previewScene,
                                    variant,
                                    power,
                                    pose,
                                    targets,
                                    renderFailureMonitor));
                                contract.AssertState(variant, power, pose);
                            }
                        }
                    }
                }

                int expectedCaptureCount =
                    Variants.Length * PowerStates.Length * DoorPoses.Length *
                    CameraNames.Length;
                if (captures.Count != expectedCaptureCount)
                {
                    throw new InvalidOperationException(
                        $"Expected {expectedCaptureCount} state-camera captures; " +
                        $"got {captures.Count}.");
                }

                if (maskSession == null)
                    blockers.Add("MASK_CAPTURE_UNAVAILABLE");
                else if (masks.Count != DoorPoses.Length * CameraNames.Length)
                    blockers.Add("MASK_CAPTURE_COUNT_MISMATCH");

                contract.AssertExactMaterialsAndPreparedStructure();
                renderFailureMonitor.ThrowIfFailed("capture completion");
                contractReport = contract.CreateReport(maskSession);
            }
            finally
            {
                if (renderFailureMonitor != null)
                    renderFailureMonitor.Dispose();
                try
                {
                    if (maskSession != null)
                        maskSession.Dispose();
                }
                finally
                {
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
                                if (staging != null)
                                    Object.DestroyImmediate(staging);
                                if (previewRoot != null)
                                {
                                    Object.DestroyImmediate(previewRoot);
                                    previewRoot = null;
                                }
                                if (previewScene.IsValid() && previewScene.isLoaded)
                                    EditorSceneManager.ClosePreviewScene(previewScene);
                            }
                            finally
                            {
                                try
                                {
                                    AssertNoOwnedCloneArtifacts();
                                }
                                finally
                                {
                                    selectionSnapshot.RestoreIfChanged();
                                    restorationCompleted = true;
                                }
                            }
                        }
                    }
                }
            }

            if (!restorationCompleted)
                throw new InvalidOperationException("Capture restoration did not complete.");
            AssertSourceEditorState(sourceScene);
            sourceSnapshot.AssertUnchanged();
            selectionSnapshot.AssertRestored();
            globalSnapshot.AssertRestored();
            ValidateAndAttachBaselines(captures);
            ValidateCaptureDiversityAndPoweredSignal(captures);

            string manifest = BuildManifest(
                timestamp,
                identity,
                contractReport,
                captures,
                masks,
                blockers,
                globalSnapshot);

            // No evidence file is created until the preview scene is gone and all
            // source/global state has been restored and re-verified.
            WriteEvidence(outputFolder, captures, masks, manifest);

            try
            {
                AssertSourceEditorState(sourceScene);
                sourceSnapshot.AssertUnchanged();
                selectionSnapshot.AssertRestored();
                globalSnapshot.AssertRestored();
                MarkPublishedEvidenceComplete(outputFolder, captures.Count, masks.Count);
                AssertSourceEditorState(sourceScene);
                sourceSnapshot.AssertUnchanged();
                selectionSnapshot.AssertRestored();
                globalSnapshot.AssertRestored();
            }
            catch (Exception exception)
            {
                MarkPublishedEvidenceFailed(
                    outputFolder,
                    "POST_PUBLISH_RESTORATION_ASSERTION",
                    exception);
                throw;
            }

            int fileCount = captures.Count * 2 + masks.Count * 5 + 2;
            return Status + "\n" +
                   "output=" + outputFolder + "\n" +
                   "stateCameraRecords=" + captures.Count + "\n" +
                   "presentationPng=" + captures.Count + "\n" +
                   "linearHdrExr=" + captures.Count + "\n" +
                   "maskCameraPoseRecords=" + masks.Count + "\n" +
                   "totalFiles=" + fileCount + "\n" +
                   "visualFitClaimed=false\n" +
                   "bakeRealtimeEquivalenceClaimed=false\n" +
                   "acceptance=BLOCKED_PENDING_COMPARISON";
        }

        private static Scene ValidateEditorPreconditions()
        {
            if (Application.isPlaying || EditorApplication.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Capture requires stable Edit Mode.");
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Unity is compiling or updating assets.");
            if (Lightmapping.isRunning)
                throw new InvalidOperationException("Lightmapping is running.");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Close Prefab Stage before capture.");

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded ||
                !string.Equals(scene.path, ValidationScenePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The exact validation scene must already be the active scene.");
            }
            AssertSourceEditorState(scene);

            string sceneHash = ComputeFileSha256(ValidationScenePath);
            if (!string.Equals(
                    sceneHash,
                    ExpectedValidationSceneSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Validation scene raw SHA-256 changed. Expected " +
                    ExpectedValidationSceneSha256 + ", actual " + sceneHash + ".");
            }
            AssertNoOwnedCloneArtifacts();

            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32) ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {
                throw new InvalidOperationException("Required render texture formats are unsupported.");
            }
            return scene;
        }

        private static void AssertNoOwnedCloneArtifacts()
        {
            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            int count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                GameObject candidate = all[i];
                if (candidate == null || EditorUtility.IsPersistent(candidate) ||
                    !string.Equals(candidate.name, OwnedCloneRootName, StringComparison.Ordinal))
                {
                    continue;
                }
                count++;
            }
            if (count != 0)
            {
                throw new InvalidOperationException(
                    "Owned K1 proxy clone artifact count must be zero, found " + count + ".");
            }
        }

        private static void AssertSourceEditorState(Scene expectedScene)
        {
            if (SceneManager.GetActiveScene() != expectedScene ||
                !expectedScene.IsValid() || !expectedScene.isLoaded || expectedScene.isDirty)
            {
                throw new InvalidOperationException("The source validation scene changed or is dirty.");
            }
            if (SceneManager.sceneCount != 1 || CountLoadedNonPreviewScenes() != 1)
                throw new InvalidOperationException("Exactly one non-preview scene must be loaded.");
            if (Application.isPlaying || EditorApplication.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
            {
                throw new InvalidOperationException("Editor mode changed during capture.");
            }
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

        private static void ValidateRootShape(GameObject root, bool requireSceneRoot)
        {
            if (root == null || !root.activeSelf ||
                (requireSceneRoot && root.transform.parent != null) ||
                root.transform.childCount != ExpectedValidationRootChildren)
            {
                throw new InvalidOperationException("Validation root shape changed.");
            }
            FindUniqueDescendant(root.transform, ProductionRoomsRootName, true);
            FindUniqueDescendant(root.transform, PortalTransportRootName, true);
            FindUniqueDescendant(root.transform, CamerasRootName, true);
            FindUniqueDescendant(root.transform, VolumeRootName, true);
        }

        private static Transform FindUniqueDescendant(
            Transform root,
            string name,
            bool includeInactive)
        {
            Transform match = null;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(includeInactive);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (!string.Equals(transforms[i].name, name, StringComparison.Ordinal))
                    continue;
                if (match != null)
                    throw new InvalidOperationException("Duplicate transform: " + name);
                match = transforms[i];
            }
            if (match == null)
                throw new InvalidOperationException("Missing transform: " + name);
            return match;
        }

        private static Camera FindUniqueCamera(Transform root, string name)
        {
            Transform transform = FindUniqueDescendant(root, name, true);
            Camera camera = transform.GetComponent<Camera>();
            if (camera == null)
                throw new InvalidOperationException("Missing Camera on " + name);
            return camera;
        }

        private static Component FindUniqueUrpCameraData(Camera camera)
        {
            Component match = null;
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
                if (match != null)
                    throw new InvalidOperationException("Duplicate URP Camera data.");
                match = component;
            }
            if (match == null)
                throw new InvalidOperationException("Fixed Camera lacks URP Camera data.");
            return match;
        }

        private static CaptureEvidence CaptureCameraPair(
            Camera camera,
            Scene previewScene,
            Variant variant,
            PowerState power,
            DoorPose pose,
            RenderTargets targets,
            RenderFailureMonitor renderFailureMonitor)
        {
            if (camera == null || camera.enabled || camera.targetTexture != null ||
                camera.gameObject.scene != previewScene || camera.scene != previewScene)
            {
                throw new InvalidOperationException("Fixed clone Camera isolation changed.");
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
                throw new InvalidOperationException("URP post-processing API is unavailable.");
            }

            bool originalPost = (bool)postProperty.GetValue(cameraData);
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
                RequireBytes(png, "presentation PNG");

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
                ComputeLuminance(hdrReadback, out double mean, out float maximum);
                if (double.IsNaN(mean) || double.IsInfinity(mean) ||
                    float.IsNaN(maximum) || float.IsInfinity(maximum) ||
                    mean <= MinimumValidMeanLinearLuminance ||
                    maximum <= MinimumValidMaxLinearLuminance)
                {
                    throw new InvalidOperationException(
                        "Render returned an invalid zero/non-finite HDR frame: variant=" +
                        variant.Id + ", power=" + power.Id + ", door=" + pose.Percent +
                        ", camera=" + camera.name + ", mean=" + FormatDouble(mean) +
                        ", max=" + maximum.ToString("R", CultureInfo.InvariantCulture) + ".");
                }
                byte[] exr = ImageConversion.EncodeToEXR(
                    hdrReadback,
                    Texture2D.EXRFlags.CompressZIP);
                RequireBytes(exr, "linear EXR");

                string variantToken = variant.IncludeRendererBit1 ? "M3" : "M2";
                string stem = variantToken + "__" + power.Id + "_D" +
                              pose.Percent.ToString("000", CultureInfo.InvariantCulture) +
                              "__" + FixedCameraToken(camera.name);
                return new CaptureEvidence(
                    variant,
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
                    camera.cullingMask,
                    camera.allowHDR,
                    camera.allowMSAA,
                    targets.PresentationRender.antiAliasing);
            }
            finally
            {
                RenderTexture.active = originalActive;
                camera.targetTexture = originalTarget;
                camera.aspect = originalAspect;
                camera.projectionMatrix = originalProjection;
                postProperty.SetValue(cameraData, originalPost);
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
                throw new InvalidOperationException("StandardRequest did not restore Camera target.");
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
                CaptureEvidence record = captures[i];
                if (!record.Power.IsP0P0)
                    continue;
                string key = BaselineKey(record.Variant, record.Door, record.CameraName);
                if (baselines.ContainsKey(key))
                    throw new InvalidOperationException("Duplicate same-pose P0/P0 baseline.");
                baselines.Add(key, record);
            }

            int expected = Variants.Length * DoorPoses.Length * CameraNames.Length;
            if (baselines.Count != expected)
                throw new InvalidOperationException("Missing same-pose P0/P0 baseline(s).");

            for (int i = 0; i < captures.Count; i++)
            {
                CaptureEvidence record = captures[i];
                string key = BaselineKey(record.Variant, record.Door, record.CameraName);
                if (!baselines.TryGetValue(key, out CaptureEvidence baseline))
                    throw new InvalidOperationException("Unable to resolve P0/P0 baseline.");
                record.AttachBaseline(
                    baseline.HdrFilename,
                    baseline.MeanLinearLuminance,
                    record.MeanLinearLuminance - baseline.MeanLinearLuminance);
            }
        }

        private static void ValidateCaptureDiversityAndPoweredSignal(
            List<CaptureEvidence> captures)
        {
            var pngHashes = new HashSet<string>(StringComparer.Ordinal);
            var hdrHashes = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < captures.Count; i++)
            {
                CaptureEvidence record = captures[i];
                pngHashes.Add(record.PngSha256);
                hdrHashes.Add(record.HdrSha256);
            }
            if (pngHashes.Count <= 1 || hdrHashes.Count <= 1)
            {
                throw new InvalidOperationException(
                    "All state captures are identical; refusing to publish a failed render: " +
                    "uniquePng=" + pngHashes.Count + ", uniqueExr=" + hdrHashes.Count + ".");
            }

            for (int variantIndex = 0; variantIndex < Variants.Length; variantIndex++)
            {
                Variant variant = Variants[variantIndex];
                RequirePoweredReceiverSignal(
                    captures,
                    variant,
                    "P100_P000",
                    AdministrativeCameraName,
                    "START_TO_ADMIN");
                RequirePoweredReceiverSignal(
                    captures,
                    variant,
                    "P000_P100",
                    StartCameraName,
                    "ADMIN_TO_START");
            }
        }

        private static void RequirePoweredReceiverSignal(
            List<CaptureEvidence> captures,
            Variant variant,
            string poweredStateId,
            string receiverCameraName,
            string direction)
        {
            CaptureEvidence baseline = null;
            CaptureEvidence powered = null;
            for (int i = 0; i < captures.Count; i++)
            {
                CaptureEvidence record = captures[i];
                if (!string.Equals(record.Variant.Id, variant.Id, StringComparison.Ordinal) ||
                    record.Door.Percent != 100 ||
                    !string.Equals(
                        record.CameraName,
                        receiverCameraName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (record.Power.IsP0P0)
                    baseline = record;
                else if (string.Equals(
                             record.Power.Id,
                             poweredStateId,
                             StringComparison.Ordinal))
                    powered = record;
            }

            if (baseline == null || powered == null)
                throw new InvalidOperationException("Missing powered-signal gate records: " + direction);
            double delta = powered.MeanLinearLuminance - baseline.MeanLinearLuminance;
            if (string.Equals(powered.HdrSha256, baseline.HdrSha256, StringComparison.Ordinal) ||
                delta <= MinimumPoweredMeanDelta)
            {
                throw new InvalidOperationException(
                    "Powered receiver did not differ from same-pose P0/P0: direction=" +
                    direction + ", variant=" + variant.Id + ", door=100, delta=" +
                    FormatDouble(delta) + ".");
            }
        }

        private static string BaselineKey(Variant variant, DoorPose pose, string cameraName)
        {
            return variant.Id + "|" + pose.Percent.ToString(CultureInfo.InvariantCulture) +
                   "|" + cameraName;
        }

        private static string BuildManifest(
            string timestamp,
            CaptureIdentity identity,
            ContractReport report,
            List<CaptureEvidence> captures,
            List<MaskEvidence> masks,
            List<string> blockers,
            GlobalRenderSnapshot globalSnapshot)
        {
            if (report == null)
                throw new InvalidOperationException("Missing detached contract report.");

            var builder = new StringBuilder(128 * 1024);
            builder.AppendLine("status=" + Status);
            builder.AppendLine("captureUtc=" + timestamp);
            builder.AppendLine("captureOutcome=COMPLETE");
            builder.AppendLine("publicationProtocol=Write to unique staging folder; raw hash/header verification; directory publish; import/load-only verification; source/global restoration assertion; CAPTURE_STATE.txt COMPLETE last.");
            builder.AppendLine("failureProtocol=CAPTURE_STATE.txt FAILED plus CAPTURE_FAILED.txt; incomplete/failed staging is retained and never presented as accepted evidence.");
            builder.AppendLine("renderErrorGate=Any RenderGraph, ReflectionProbeManager, ForwardLights, ZBinning, or capture-stack Error/Exception/Assert aborts before evidence publication.");
            builder.AppendLine("zeroFrameGate=Every HDR frame must have finite nonzero mean and max luminance.");
            builder.AppendLine("diversityGate=PNG unique hashes > 1, EXR unique hashes > 1, and both D100 receiver directions differ positively from same-pose P0/P0 for each variant.");
            builder.AppendLine("acceptance=BLOCKED_PENDING_REALTIME_DIRECT_AND_JOINT_PAIR_COMPARISON");
            builder.AppendLine("visualFitClaimed=false");
            builder.AppendLine("visualParityClaimed=false");
            builder.AppendLine("bakeRealtimeEquivalenceClaimed=false");
            builder.AppendLine("k1BasisAccepted=false");
            builder.AppendLine("purpose=Compare serialized K=1 outgoing Spot proxies against separately captured REALTIME_DIRECT_GT and future JOINT_PAIR_TOTAL_GT.");
            builder.AppendLine(
                "validationSceneExpectedSha256=" + ExpectedValidationSceneSha256);
            builder.AppendLine(
                "validationSceneActualSha256=" + ComputeFileSha256(ValidationScenePath));
            builder.AppendLine("previewSceneDeepClone=true");
            builder.AppendLine("playModeUsed=false");
            builder.AppendLine("sceneOpenedOrSaved=false");
            builder.AppendLine("lightmappingStarted=false");
            builder.AppendLine("productionAssetWrite=false");
            builder.AppendLine("portalRuntimeEnabled=false");
            builder.AppendLine("runtimeEndpointUsed=false");
            builder.AppendLine("productionLightsEnabledOnClone=false");
            builder.AppendLine("bakedLightmapContributionOnClone=false");
            builder.AppendLine("realtimeLightmapContributionOnClone=false");
            builder.AppendLine("rendererLightProbeSampling=false");
            builder.AppendLine("rendererReflectionProbeSampling=false");
            builder.AppendLine("cloneReflectionProbeComponentsRemoved=true");
            builder.AppendLine("cloneReflectionProbeComponentCountRemoved=" +
                               report.RemovedReflectionProbeCount);
            builder.AppendLine("cloneReflectionProbeRemovalReason=This direct-only K1 capture excludes reflection and destroys clone-only ReflectionProbe components before activation so stale preview-scene native probe entries cannot contaminate URP culling.");
            builder.AppendLine("lightingSettingsRealtimeGiMutated=false");
            builder.AppendLine("realtimeGiAbsoluteExclusionClaimed=false");
            builder.AppendLine("realtimeGiDirectOnlyControls=All cloned production Lights disabled; both manually owned proxy Lights use bounceIntensity=0; every renderer realtimeLightmapIndex=-1 and lightProbeUsage=Off; global realtime/baked lightmap array empty; DynamicGI update is never invoked.");
            builder.AppendLine("realtimeGiLimitation=The source LightingSettings realtimeGI flag is recorded without mutation. Same-pose P0/P0 subtraction remains required; this tool does not claim engine-wide realtime-GI equivalence.");
            builder.AppendLine("proxyBounceIntensity=0");
            builder.AppendLine("indirectBounceCovered=false");
            builder.AppendLine("reflectionCovered=false");
            builder.AppendLine("doorReceivesOnlyRealtimeDirectWhenItsRenderingMaskMatchesVariant=true");
            builder.AppendLine("sourceMaterialsPreserved=true");
            builder.AppendLine("sourceEmissionPreserved=true");
            builder.AppendLine("rendererMaterialsChanged=false");
            builder.AppendLine("rendererGameObjectLayersChanged=false");
            builder.AppendLine("rendererRenderingLayerMasksChanged=false");
            builder.AppendLine("rendererMaterialPropertyBlocksChanged=false");
            builder.AppendLine("p0p0Baseline=Every record links the same variant, same door pose, same camera P000_P000 linear EXR.");
            builder.AppendLine("p0p0Caveat=Serialized profile P0 radiance is retained; this is a same-pose reference, not an absolute black frame.");
            builder.AppendLine("ambientAndMaterialEmission=Preserved identically across all states; subtract same-pose P0/P0 before judging proxy delta.");
            builder.AppendLine("captureWidth=" + CaptureWidth);
            builder.AppendLine("captureHeight=" + CaptureHeight);
            builder.AppendLine("variantCount=" + Variants.Length);
            builder.AppendLine("powerStateCount=" + PowerStates.Length);
            builder.AppendLine("doorPoseCount=" + DoorPoses.Length);
            builder.AppendLine("cameraCount=" + CameraNames.Length);
            builder.AppendLine("stateCameraRecordCount=" + captures.Count);
            builder.AppendLine("maskCameraPoseRecordCount=" + masks.Count);
            builder.AppendLine("rendererCount=" + report.RendererCount);
            builder.AppendLine("rendererMask2Count=" + report.Mask2Count);
            builder.AppendLine("rendererMask1Count=" + report.Mask1Count);
            builder.AppendLine("rendererOtherMaskCount=" + report.OtherMaskCount);
            builder.AppendLine("knownMaskDistribution=322 mask2,67 mask1");
            builder.AppendLine("doorRendererCount=" + report.DoorRendererCount);
            builder.AppendLine("doorEnabledRendererCount=" + report.DoorEnabledRendererCount);
            builder.AppendLine("doorEnabledMask1Count=" + report.DoorEnabledMask1Count);
            builder.AppendLine("knownDoorMaskDistribution=3 enabled split renderers use mask1");
            builder.AppendLine("productionLightCount=" + report.ProductionLightCount);
            builder.AppendLine("proxyLightCount=" + report.ProxyLightCount);
            builder.AppendLine("startDoorwayFrame=" + report.StartDoorwayFramePath);
            builder.AppendLine("administrativeDoorwayFrame=" + report.AdministrativeDoorwayFramePath);
            builder.AppendLine("startDescriptorMask=" + report.StartDescriptorMask);
            builder.AppendLine("administrativeDescriptorMask=" + report.AdministrativeDescriptorMask);
            builder.AppendLine("startDescriptorCookie=" + report.StartCookiePath);
            builder.AppendLine("startDescriptorCookieDependencyHash=" +
                               report.StartCookieDependencyHash);
            builder.AppendLine("administrativeDescriptorCookie=" +
                               report.AdministrativeCookiePath);
            builder.AppendLine("administrativeDescriptorCookieDependencyHash=" +
                               report.AdministrativeCookieDependencyHash);
            builder.AppendLine("startDescriptorCullingMask=" + report.StartCullingMask);
            builder.AppendLine("administrativeDescriptorCullingMask=" +
                               report.AdministrativeCullingMask);
            builder.AppendLine("startDescriptorRange=" +
                               report.StartRange.ToString("R", CultureInfo.InvariantCulture));
            builder.AppendLine("administrativeDescriptorRange=" +
                               report.AdministrativeRange.ToString("R", CultureInfo.InvariantCulture));
            builder.AppendLine("startDescriptorSpotAngle=" +
                               report.StartSpotAngle.ToString("R", CultureInfo.InvariantCulture));
            builder.AppendLine("administrativeDescriptorSpotAngle=" +
                               report.AdministrativeSpotAngle.ToString("R", CultureInfo.InvariantCulture));
            builder.AppendLine("profileExactAppliedMask=2");
            builder.AppendLine("doorReceiverCandidateAppliedMask=3");
            builder.AppendLine("candidateDefinition=descriptor renderingLayerMask OR 1; no renderer/material/layer mutation.");
            builder.AppendLine("maskCaptureStatus=" + report.MaskCaptureStatus);
            builder.AppendLine("maskFitGate=" + report.MaskFitGate);
            builder.AppendLine("maskStableObjectIdCount=" + report.MaskStableObjectIdCount);
            builder.AppendLine("maskUnsupportedRendererCount=" + report.MaskUnsupportedRendererCount);
            builder.AppendLine("maskSemantics=Start_to_Admin: Start source, Admin receiver; Admin_to_Start: Admin source, Start receiver; door is the 3 enabled split renderers; forbidden is source-or-unsupported geometry.");
            builder.AppendLine("maskFitClaimed=false");
            builder.AppendLine("maskCaveat=Unsupported transparent, alpha-test, LOD, deformed, or non-opaque silhouettes are magenta and force MASK_FIT_GATE=BLOCKED.");
            builder.AppendLine("unityVersion=" + Application.unityVersion);
            builder.AppendLine("qualityLevel=" + identity.QualityLevel);
            builder.AppendLine("qualityName=" + identity.QualityName);
            builder.AppendLine("renderPipelineAsset=" + identity.RenderPipelinePath);
            builder.AppendLine("renderPipelineDependencyHash=" + identity.RenderPipelineDependencyHash);
            builder.AppendLine("lightingSettingsAsset=" + identity.LightingSettingsPath);
            builder.AppendLine("lightingSettingsDependencyHash=" + identity.LightingSettingsDependencyHash);
            builder.AppendLine("lightingSettingsRealtimeGI=" + identity.LightingSettingsRealtimeGi);
            builder.AppendLine("globalLightmapsRestored=" + globalSnapshot.AreLightmapsRestored());
            builder.AppendLine("globalLightmapsModeRestored=" + globalSnapshot.IsModeRestored());
            builder.AppendLine("renderTextureActiveRestored=" + globalSnapshot.IsRenderTextureActiveRestored());
            builder.AppendLine("glSrgbWriteRestored=" + globalSnapshot.IsSrgbWriteRestored());
            builder.AppendLine("maskShaderGlobalsRestored=" + globalSnapshot.AreMaskGlobalsRestored());
            identity.AppendAsset(builder, "asset.tool", ToolSourcePath);
            identity.AppendAsset(builder, "asset.scene", ValidationScenePath);
            identity.AppendAsset(builder, "asset.startProfile", StartProfilePath);
            identity.AppendAsset(builder, "asset.administrativeProfile", AdministrativeProfilePath);
            identity.AppendAsset(builder, "asset.objectIdShader", ObjectIdShaderPath);

            builder.AppendLine("blockerCount=" + blockers.Count);
            for (int i = 0; i < blockers.Count; i++)
                builder.AppendLine("blocker." + i + "=" + SanitizeManifest(blockers[i]));
            builder.AppendLine("maskBlockerCount=" + report.MaskBlockers.Length);
            for (int i = 0; i < report.MaskBlockers.Length; i++)
                builder.AppendLine("maskBlocker." + i + "=" + SanitizeManifest(report.MaskBlockers[i]));

            for (int i = 0; i < Variants.Length; i++)
            {
                Variant variant = Variants[i];
                builder.AppendLine("variant." + i + ".id=" + variant.Id);
                builder.AppendLine("variant." + i + ".includeRendererBit1=" +
                                   variant.IncludeRendererBit1);
            }

            for (int i = 0; i < captures.Count; i++)
            {
                CaptureEvidence record = captures[i];
                string prefix = "capture." + i + ".";
                builder.AppendLine(prefix + "variant=" + record.Variant.Id);
                builder.AppendLine(prefix + "power=" + record.Power.Id);
                builder.AppendLine(prefix + "doorPercent=" + record.Door.Percent);
                builder.AppendLine(prefix + "camera=" + record.CameraName);
                builder.AppendLine(prefix + "png=" + record.PngFilename);
                builder.AppendLine(prefix + "pngSha256=" + record.PngSha256);
                builder.AppendLine(prefix + "linearExr=" + record.HdrFilename);
                builder.AppendLine(prefix + "linearExrSha256=" + record.HdrSha256);
                builder.AppendLine(prefix + "samePoseP0P0Baseline=" +
                                   record.BaselineHdrFilename);
                builder.AppendLine(prefix + "meanLinearLuminance=" +
                                   FormatDouble(record.MeanLinearLuminance));
                builder.AppendLine(prefix + "maxLinearLuminance=" +
                                   record.MaxLinearLuminance.ToString("R", CultureInfo.InvariantCulture));
                builder.AppendLine(prefix + "baselineMeanLinearLuminance=" +
                                   FormatDouble(record.BaselineMeanLinearLuminance));
                builder.AppendLine(prefix + "meanLinearLuminanceDeltaFromP0P0=" +
                                   FormatDouble(record.MeanLinearLuminanceDelta));
                builder.AppendLine(prefix + "presentationPostProcessing=" +
                                   record.PresentationPostProcessing);
                builder.AppendLine(prefix + "cameraPosition=" + FormatVector3(record.Position));
                builder.AppendLine(prefix + "cameraRotation=" + FormatQuaternion(record.Rotation));
                builder.AppendLine(prefix + "fieldOfView=" +
                                   record.FieldOfView.ToString("R", CultureInfo.InvariantCulture));
                builder.AppendLine(prefix + "nearClip=" +
                                   record.NearClip.ToString("R", CultureInfo.InvariantCulture));
                builder.AppendLine(prefix + "farClip=" +
                                   record.FarClip.ToString("R", CultureInfo.InvariantCulture));
                builder.AppendLine(prefix + "cullingMask=" + record.CullingMask);
                builder.AppendLine(prefix + "allowHdr=" + record.AllowHdr);
                builder.AppendLine(prefix + "allowMsaa=" + record.AllowMsaa);
                builder.AppendLine(prefix + "presentationMsaa=" + record.PresentationMsaa);
            }

            for (int i = 0; i < masks.Count; i++)
            {
                MaskEvidence record = masks[i];
                string prefix = "mask." + i + ".";
                builder.AppendLine(prefix + "doorPercent=" + record.Door.Percent);
                builder.AppendLine(prefix + "camera=" + record.CameraName);
                builder.AppendLine(prefix + "direction=" + record.Direction.Id);
                builder.AppendLine(prefix + "objectId=" + record.ObjectIdFilename);
                builder.AppendLine(prefix + "objectIdSha256=" + record.ObjectIdSha256);
                builder.AppendLine(prefix + "source=" + record.SourceFilename);
                builder.AppendLine(prefix + "sourceSha256=" + record.SourceSha256);
                builder.AppendLine(prefix + "receiver=" + record.ReceiverFilename);
                builder.AppendLine(prefix + "receiverSha256=" + record.ReceiverSha256);
                builder.AppendLine(prefix + "door=" + record.DoorFilename);
                builder.AppendLine(prefix + "doorSha256=" + record.DoorSha256);
                builder.AppendLine(prefix + "forbidden=" + record.ForbiddenFilename);
                builder.AppendLine(prefix + "forbiddenSha256=" + record.ForbiddenSha256);
                builder.AppendLine(prefix + "knownPixelCount=" + record.KnownPixelCount);
                builder.AppendLine(prefix + "unsupportedPixelCount=" +
                                   record.UnsupportedPixelCount);
            }

            return builder.ToString();
        }

        private static void WriteEvidence(
            string outputFolder,
            List<CaptureEvidence> captures,
            List<MaskEvidence> masks,
            string manifest)
        {
            string absoluteFolder = AssetPathToAbsolutePath(outputFolder);
            if (Directory.Exists(absoluteFolder))
                throw new IOException("Evidence folder already exists: " + absoluteFolder);
            string stagingFolder = absoluteFolder + ".__STAGING_" +
                                   Guid.NewGuid().ToString("N").Substring(0, 8);
            bool published = false;
            try
            {
                Directory.CreateDirectory(stagingFolder);
                WriteCaptureState(
                    stagingFolder,
                    "INCOMPLETE",
                    "STAGING_AND_RAW_VERIFICATION",
                    null,
                    captures.Count,
                    masks.Count);

                var expectedFiles = new List<WrittenFile>(
                    captures.Count * 2 + masks.Count * 5);
                for (int i = 0; i < captures.Count; i++)
                {
                    CaptureEvidence record = captures[i];
                    WriteBytes(stagingFolder, record.PngFilename, record.PngBytes);
                    WriteBytes(stagingFolder, record.HdrFilename, record.HdrBytes);
                    expectedFiles.Add(
                        new WrittenFile(record.PngFilename, record.PngSha256, true));
                    expectedFiles.Add(
                        new WrittenFile(record.HdrFilename, record.HdrSha256, false));
                }
                for (int i = 0; i < masks.Count; i++)
                    masks[i].Write(stagingFolder, expectedFiles);

                string manifestName = "manifest_" + Status + ".txt";
                File.WriteAllText(
                    Path.Combine(stagingFolder, manifestName),
                    manifest,
                    new UTF8Encoding(false));

                for (int i = 0; i < expectedFiles.Count; i++)
                {
                    WrittenFile file = expectedFiles[i];
                    string absolutePath = Path.Combine(stagingFolder, file.Filename);
                    byte[] bytes = File.ReadAllBytes(absolutePath);
                    if (!string.Equals(
                            ComputeSha256(bytes),
                            file.Sha256,
                            StringComparison.Ordinal))
                    {
                        throw new IOException("Raw evidence hash mismatch: " + file.Filename);
                    }
                    if (file.IsPng)
                        VerifyPng(bytes, file.Filename);
                    else
                        VerifyExr(bytes, file.Filename);
                }

                // The success path becomes visible at the canonical output path only
                // after every raw byte, hash, PNG header/dimensions, and EXR magic
                // number has been verified inside the private staging directory.
                Directory.Move(stagingFolder, absoluteFolder);
                published = true;

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                for (int i = 0; i < expectedFiles.Count; i++)
                {
                    string assetPath = outputFolder + "/" + expectedFiles[i].Filename;
                    if (AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) == null)
                    {
                        throw new IOException(
                            "Evidence import/load-only verification failed: " + assetPath);
                    }
                }
            }
            catch (Exception exception)
            {
                if (published)
                {
                    TryWriteFailureMarker(
                        absoluteFolder,
                        "PUBLISHED_IMPORT_OR_LOAD_VERIFICATION",
                        exception);
                }
                else
                {
                    PublishFailedStagingFolder(
                        stagingFolder,
                        absoluteFolder,
                        exception);
                }
                TryRefreshAssetDatabaseAfterFailure();
                throw;
            }
        }

        private static void MarkPublishedEvidenceFailed(
            string outputFolder,
            string phase,
            Exception exception)
        {
            string absoluteFolder = AssetPathToAbsolutePath(outputFolder);
            TryWriteFailureMarker(absoluteFolder, phase, exception);
            TryRefreshAssetDatabaseAfterFailure();
        }

        private static void MarkPublishedEvidenceComplete(
            string outputFolder,
            int captureCount,
            int maskCount)
        {
            string absoluteFolder = AssetPathToAbsolutePath(outputFolder);
            if (!Directory.Exists(absoluteFolder))
                throw new DirectoryNotFoundException("Published evidence folder is missing.");
            WriteCaptureState(
                absoluteFolder,
                "COMPLETE",
                "PUBLISHED_IMPORTED_AND_RESTORATION_VERIFIED",
                null,
                captureCount,
                maskCount);
            AssetDatabase.ImportAsset(
                outputFolder + "/CAPTURE_STATE.txt",
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
        }

        private static void PublishFailedStagingFolder(
            string stagingFolder,
            string intendedFolder,
            Exception exception)
        {
            if (!Directory.Exists(stagingFolder))
                return;
            TryWriteFailureMarker(stagingFolder, "STAGING_OR_RAW_VERIFICATION", exception);
            try
            {
                if (!Directory.Exists(intendedFolder))
                {
                    Directory.Move(stagingFolder, intendedFolder);
                    return;
                }

                string failedFolder = intendedFolder + ".__FAILED_" +
                                      Guid.NewGuid().ToString("N");
                Directory.Move(stagingFolder, failedFolder);
            }
            catch
            {
                // The staging directory already contains CAPTURE_FAILED.txt. It is
                // intentionally retained for diagnosis; no destructive cleanup runs.
            }
        }

        private static void TryWriteFailureMarker(
            string absoluteFolder,
            string phase,
            Exception exception)
        {
            try
            {
                if (!Directory.Exists(absoluteFolder))
                    Directory.CreateDirectory(absoluteFolder);
                var builder = new StringBuilder();
                builder.AppendLine("status=FAILED");
                builder.AppendLine("captureStatus=" + Status);
                builder.AppendLine("phase=" + phase);
                builder.AppendLine("utc=" + DateTime.UtcNow.ToString(
                    "O",
                    CultureInfo.InvariantCulture));
                builder.AppendLine("reason=" + SanitizeManifest(exception.Message));
                builder.AppendLine("exception=" + SanitizeManifest(exception.ToString()));
                builder.AppendLine("visualFitClaimed=false");
                builder.AppendLine("visualParityClaimed=false");
                builder.AppendLine("bakeRealtimeEquivalenceClaimed=false");
                File.WriteAllText(
                    Path.Combine(absoluteFolder, "CAPTURE_FAILED.txt"),
                    builder.ToString(),
                    new UTF8Encoding(false));
                WriteCaptureState(
                    absoluteFolder,
                    "FAILED",
                    phase,
                    exception,
                    -1,
                    -1);
            }
            catch
            {
                // Best effort only; the original capture exception remains primary.
            }
        }

        private static void TryRefreshAssetDatabaseAfterFailure()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            catch
            {
                // The filesystem marker remains authoritative even if import fails.
            }
        }

        private static void WriteCaptureState(
            string absoluteFolder,
            string state,
            string phase,
            Exception exception,
            int captureCount,
            int maskCount)
        {
            var builder = new StringBuilder();
            builder.AppendLine("status=" + state);
            builder.AppendLine("captureStatus=" + Status);
            builder.AppendLine("phase=" + phase);
            builder.AppendLine("utc=" + DateTime.UtcNow.ToString(
                "O",
                CultureInfo.InvariantCulture));
            if (captureCount >= 0)
                builder.AppendLine("stateCameraRecordCount=" + captureCount);
            if (maskCount >= 0)
                builder.AppendLine("maskCameraPoseRecordCount=" + maskCount);
            if (exception != null)
                builder.AppendLine("reason=" + SanitizeManifest(exception.Message));
            builder.AppendLine("visualFitClaimed=false");
            builder.AppendLine("visualParityClaimed=false");
            builder.AppendLine("bakeRealtimeEquivalenceClaimed=false");
            File.WriteAllText(
                Path.Combine(absoluteFolder, "CAPTURE_STATE.txt"),
                builder.ToString(),
                new UTF8Encoding(false));
        }

        private static void WriteBytes(string folder, string filename, byte[] bytes)
        {
            RequireBytes(bytes, filename);
            File.WriteAllBytes(Path.Combine(folder, filename), bytes);
        }

        private static void VerifyPng(byte[] bytes, string label)
        {
            if (bytes.Length < 24 || bytes[0] != 0x89 || bytes[1] != 0x50 ||
                bytes[2] != 0x4E || bytes[3] != 0x47)
            {
                throw new IOException("Invalid PNG: " + label);
            }
            int width = ReadBigEndianInt32(bytes, 16);
            int height = ReadBigEndianInt32(bytes, 20);
            if (width != CaptureWidth || height != CaptureHeight)
                throw new IOException("PNG dimensions changed: " + label);
        }

        private static void VerifyExr(byte[] bytes, string label)
        {
            if (bytes.Length < 4 || bytes[0] != 0x76 || bytes[1] != 0x2F ||
                bytes[2] != 0x31 || bytes[3] != 0x01)
            {
                throw new IOException("Invalid EXR: " + label);
            }
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) |
                   (bytes[offset + 2] << 8) | bytes[offset + 3];
        }

        private static void RequireBytes(byte[] bytes, string label)
        {
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException(label + " returned no bytes.");
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

            if (camera == null ||
                !string.Equals(camera.name, expectedName, StringComparison.Ordinal) ||
                camera.enabled || camera.targetTexture != null || camera.orthographic ||
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
                    "Fixed camera known transform/FOV/projection inputs changed: " +
                    expectedName);
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
                            "Fixed camera capture projection is no longer standard perspective.");
                    }
                }
            }
        }

        private static string SanitizeFilename(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "unnamed";
            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
                builder.Append(Array.IndexOf(invalid, value[i]) >= 0 ? '_' : value[i]);
            return builder.ToString();
        }

        private static string FixedCameraToken(string cameraName)
        {
            if (string.Equals(cameraName, StartCameraName, StringComparison.Ordinal))
                return "Start_to_Admin";
            if (string.Equals(cameraName, AdministrativeCameraName, StringComparison.Ordinal))
                return "Admin_to_Start";
            throw new InvalidOperationException(
                "Unexpected fixed-camera name during evidence filename creation.");
        }

        private static string SanitizeManifest(string value)
        {
            return (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatVector3(Vector3 value)
        {
            return value.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                   value.y.ToString("R", CultureInfo.InvariantCulture) + "," +
                   value.z.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatQuaternion(Quaternion value)
        {
            return value.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                   value.y.ToString("R", CultureInfo.InvariantCulture) + "," +
                   value.z.ToString("R", CultureInfo.InvariantCulture) + "," +
                   value.w.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return "<missing>";
            var names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names.ToArray());
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || (target != root && !target.IsChildOf(root)))
                throw new InvalidOperationException("Target is outside the requested path root.");
            if (target == root)
                return ".";
            var names = new Stack<string>();
            Transform current = target;
            while (current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names.ToArray());
        }

        private static int GetRendererComponentOrdinal(Renderer renderer)
        {
            Renderer[] components = renderer.GetComponents<Renderer>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == renderer)
                    return i;
            }
            throw new InvalidOperationException("Renderer component ordinal is unavailable.");
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct PowerState
        {
            public readonly string Id;
            public readonly float StartPower;
            public readonly float AdministrativePower;
            public bool IsP0P0 => StartPower <= 0f && AdministrativePower <= 0f;

            public PowerState(string id, float startPower, float administrativePower)
            {
                Id = id;
                StartPower = startPower;
                AdministrativePower = administrativePower;
            }
        }

        private readonly struct DoorPose
        {
            public readonly int Percent;
            public readonly float Fraction;

            public DoorPose(int percent)
            {
                Percent = percent;
                Fraction = Mathf.Clamp01(percent / 100f);
            }
        }

        private readonly struct Variant
        {
            public readonly string Id;
            public readonly bool IncludeRendererBit1;

            public Variant(string id, bool includeRendererBit1)
            {
                Id = id;
                IncludeRendererBit1 = includeRendererBit1;
            }
        }

        private readonly struct DirectionSemantic
        {
            public readonly string Id;
            public readonly RoomRole Source;
            public readonly RoomRole Receiver;

            public DirectionSemantic(string id, RoomRole source, RoomRole receiver)
            {
                Id = id;
                Source = source;
                Receiver = receiver;
            }
        }

        private enum RoomRole
        {
            Start,
            Administrative
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
                        nameof(DungeonPortalK1ProxyDirectValidationCapture)))
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

        private sealed class CaptureEvidence
        {
            public readonly Variant Variant;
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
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly float FieldOfView;
            public readonly float NearClip;
            public readonly float FarClip;
            public readonly int CullingMask;
            public readonly bool AllowHdr;
            public readonly bool AllowMsaa;
            public readonly int PresentationMsaa;

            public string BaselineHdrFilename { get; private set; }
            public double BaselineMeanLinearLuminance { get; private set; }
            public double MeanLinearLuminanceDelta { get; private set; }

            public CaptureEvidence(
                Variant variant,
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
                Vector3 position,
                Quaternion rotation,
                float fieldOfView,
                float nearClip,
                float farClip,
                int cullingMask,
                bool allowHdr,
                bool allowMsaa,
                int presentationMsaa)
            {
                Variant = variant;
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
                Position = position;
                Rotation = rotation;
                FieldOfView = fieldOfView;
                NearClip = nearClip;
                FarClip = farClip;
                CullingMask = cullingMask;
                AllowHdr = allowHdr;
                AllowMsaa = allowMsaa;
                PresentationMsaa = presentationMsaa;
                BaselineHdrFilename = string.Empty;
            }

            public void AttachBaseline(string filename, double mean, double delta)
            {
                BaselineHdrFilename = filename;
                BaselineMeanLinearLuminance = mean;
                MeanLinearLuminanceDelta = delta;
            }
        }

        private sealed class PreviewContract
        {
            public readonly GameObject Root;
            public readonly GameObject StartRoom;
            public readonly GameObject AdministrativeRoom;
            public readonly GameObject PortalTransportRoot;
            public readonly Camera[] Cameras;
            public readonly DoorContract Door;
            public readonly RendererContract Renderers;
            public readonly ProductionLightContract ProductionLights;
            public readonly EndpointMapping StartEndpoint;
            public readonly EndpointMapping AdministrativeEndpoint;
            public ProxySet Proxies { get; private set; }

            private readonly Scene previewScene;
            private int removedReflectionProbeCount;

            private PreviewContract(
                Scene previewScene,
                GameObject root,
                GameObject startRoom,
                GameObject administrativeRoom,
                GameObject portalTransportRoot,
                Camera[] cameras,
                DoorContract door,
                RendererContract renderers,
                ProductionLightContract productionLights,
                EndpointMapping startEndpoint,
                EndpointMapping administrativeEndpoint)
            {
                this.previewScene = previewScene;
                Root = root;
                StartRoom = startRoom;
                AdministrativeRoom = administrativeRoom;
                PortalTransportRoot = portalTransportRoot;
                Cameras = cameras;
                Door = door;
                Renderers = renderers;
                ProductionLights = productionLights;
                StartEndpoint = startEndpoint;
                AdministrativeEndpoint = administrativeEndpoint;
            }

            public static PreviewContract CreateInactive(GameObject root, Scene previewScene)
            {
                ValidateRootShape(root, false);
                if (root.activeInHierarchy)
                    throw new InvalidOperationException("Clone must remain under inactive staging.");
                if (root.gameObject.scene != previewScene)
                    throw new InvalidOperationException("Clone is outside the preview scene.");

                Transform productionRooms = FindUniqueDescendant(
                    root.transform,
                    ProductionRoomsRootName,
                    true);
                if (productionRooms.childCount != 2)
                    throw new InvalidOperationException("Production rooms root shape changed.");
                GameObject startRoom = FindUniqueDescendant(
                    productionRooms,
                    StartRoomName,
                    true).gameObject;
                GameObject administrativeRoom = FindUniqueDescendant(
                    productionRooms,
                    AdministrativeRoomName,
                    true).gameObject;
                GameObject portalRoot = FindUniqueDescendant(
                    root.transform,
                    PortalTransportRootName,
                    true).gameObject;
                Transform camerasRoot = FindUniqueDescendant(
                    root.transform,
                    CamerasRootName,
                    true);
                if (camerasRoot.childCount != ExpectedCameraCount)
                    throw new InvalidOperationException("Fixed Camera hierarchy changed.");

                var cameras = new Camera[CameraNames.Length];
                for (int i = 0; i < CameraNames.Length; i++)
                {
                    Camera camera = FindUniqueCamera(camerasRoot, CameraNames[i]);
                    ValidateFixedCameraKnownProperties(camera, i);
                    FindUniqueUrpCameraData(camera);
                    camera.scene = previewScene;
                    cameras[i] = camera;
                }
                if (root.GetComponentsInChildren<Camera>(true).Length != ExpectedCameraCount)
                    throw new InvalidOperationException("Unexpected clone Camera count.");

                Transform exactDoor = startRoom.transform.Find(AddedDoorRelativePath);
                if (exactDoor == null ||
                    !string.Equals(exactDoor.name, AddedDoorName, StringComparison.Ordinal) ||
                    FindUniqueDescendant(root.transform, AddedDoorName, true) != exactDoor ||
                    exactDoor.IsChildOf(portalRoot.transform))
                {
                    throw new InvalidOperationException(
                        "The exact unique moving production door is unavailable.");
                }

                DungeonPortalDoorAngleSource[] angleSources =
                    portalRoot.GetComponentsInChildren<DungeonPortalDoorAngleSource>(true);
                if (angleSources.Length != 1 || !angleSources[0].IsConfigured)
                    throw new InvalidOperationException("Door angle source contract changed.");
                DoorContract door = DoorContract.Capture(exactDoor, angleSources[0]);

                DungeonPortalEndpoint[] endpoints =
                    portalRoot.GetComponentsInChildren<DungeonPortalEndpoint>(true);
                if (endpoints.Length != ExpectedEndpointCount)
                    throw new InvalidOperationException("Expected exactly two cloned endpoints.");

                EndpointMapping startEndpoint = null;
                EndpointMapping administrativeEndpoint = null;
                for (int i = 0; i < endpoints.Length; i++)
                {
                    EndpointMapping mapping = EndpointMapping.Capture(
                        endpoints[i],
                        startRoom.transform,
                        administrativeRoom.transform,
                        portalRoot.transform);
                    if (mapping.Role == RoomRole.Start)
                    {
                        if (startEndpoint != null)
                            throw new InvalidOperationException("Duplicate Start endpoint.");
                        startEndpoint = mapping;
                    }
                    else
                    {
                        if (administrativeEndpoint != null)
                            throw new InvalidOperationException("Duplicate Admin endpoint.");
                        administrativeEndpoint = mapping;
                    }
                }
                if (startEndpoint == null || administrativeEndpoint == null)
                    throw new InvalidOperationException("Both endpoint roles are required.");

                RendererContract renderers = RendererContract.Capture(
                    root,
                    startRoom,
                    administrativeRoom,
                    door);
                ProductionLightContract productionLights = ProductionLightContract.Capture(
                    root,
                    startRoom,
                    administrativeRoom);

                return new PreviewContract(
                    previewScene,
                    root,
                    startRoom,
                    administrativeRoom,
                    portalRoot,
                    cameras,
                    door,
                    renderers,
                    productionLights,
                    startEndpoint,
                    administrativeEndpoint);
            }

            public void PrepareInactiveClone()
            {
                if (Root.activeInHierarchy)
                    throw new InvalidOperationException("Inactive clone unexpectedly activated.");

                RemoveCloneReflectionProbes();
                Renderers.PrepareNoLightmapsOrProbes();
                ProductionLights.DisableAll();
                PortalTransportRoot.SetActive(false);
                if (PortalTransportRoot.activeSelf || PortalTransportRoot.activeInHierarchy)
                    throw new InvalidOperationException("PoC root did not deactivate.");

                Proxies = ProxySet.Create(StartEndpoint, AdministrativeEndpoint);
                Proxies.DisableAll();
                if (Root.GetComponentsInChildren<Light>(true).Length !=
                    ExpectedProductionLightCount + ExpectedEndpointCount)
                {
                    throw new InvalidOperationException("Unexpected Light count after K=1 proxy creation.");
                }
            }

            public void AssertActivatedPreparedClone()
            {
                if (!Root.activeInHierarchy || Root.gameObject.scene != previewScene ||
                    PortalTransportRoot.activeSelf || PortalTransportRoot.activeInHierarchy)
                {
                    throw new InvalidOperationException(
                        "Prepared clone activation/isolation failed: rootActiveSelf=" +
                        Root.activeSelf + ", rootActiveInHierarchy=" + Root.activeInHierarchy +
                        ", rootScene=" + Root.gameObject.scene.handle +
                        ", previewScene=" + previewScene.handle +
                        ", portalActiveSelf=" + PortalTransportRoot.activeSelf +
                        ", portalActiveInHierarchy=" +
                        PortalTransportRoot.activeInHierarchy + ".");
                }
                if (!Door.Root.gameObject.activeInHierarchy ||
                    Door.Root.IsChildOf(PortalTransportRoot.transform))
                {
                    throw new InvalidOperationException("PoC deactivation affected the real door.");
                }
                Behaviour[] behaviours =
                    PortalTransportRoot.GetComponentsInChildren<Behaviour>(true);
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] != null && behaviours[i].isActiveAndEnabled)
                        throw new InvalidOperationException("A cloned PoC Behaviour remained active.");
                }
                Renderers.AssertPrepared(false);
                ProductionLights.AssertDisabled();
                Proxies.AssertDisabledAndOwned();
                AssertCloneReflectionProbesRemoved();
            }

            public void AssertState(Variant variant, PowerState power, DoorPose pose)
            {
                if (!Root.activeInHierarchy || PortalTransportRoot.activeSelf ||
                    PortalTransportRoot.activeInHierarchy)
                {
                    throw new InvalidOperationException("Clone/PoC activation state changed.");
                }
                Renderers.AssertPrepared(false);
                ProductionLights.AssertDisabled();
                Proxies.AssertState(variant, power);
                Door.AssertPose(pose);
                AssertCloneReflectionProbesRemoved();
                for (int i = 0; i < Cameras.Length; i++)
                {
                    if (Cameras[i] == null || Cameras[i].enabled ||
                        Cameras[i].targetTexture != null || Cameras[i].scene != previewScene)
                    {
                        throw new InvalidOperationException("Fixed clone Camera changed.");
                    }
                }
            }

            public void AssertExactMaterialsAndPreparedStructure()
            {
                Renderers.AssertPrepared(true);
                ProductionLights.AssertDisabled();
                Proxies.AssertOwnedStructure();
                AssertCloneReflectionProbesRemoved();
            }

            private void RemoveCloneReflectionProbes()
            {
                ReflectionProbe[] probes = Root.GetComponentsInChildren<ReflectionProbe>(true);
                removedReflectionProbeCount = probes.Length;
                for (int i = 0; i < probes.Length; i++)
                {
                    ReflectionProbe probe = probes[i];
                    if (probe == null || probe.gameObject.scene != previewScene)
                    {
                        throw new InvalidOperationException(
                            "Clone ReflectionProbe isolation changed before removal.");
                    }
                    Object.DestroyImmediate(probe);
                }
                AssertCloneReflectionProbesRemoved();
            }

            private void AssertCloneReflectionProbesRemoved()
            {
                if (removedReflectionProbeCount <= 0 ||
                    Root.GetComponentsInChildren<ReflectionProbe>(true).Length != 0)
                {
                    throw new InvalidOperationException(
                        "Direct-only clone ReflectionProbe removal gate failed.");
                }
            }

            public DirectionSemantic GetDirectionForCamera(string cameraName)
            {
                if (string.Equals(cameraName, StartCameraName, StringComparison.Ordinal))
                {
                    return new DirectionSemantic(
                        "START_TO_ADMIN",
                        RoomRole.Start,
                        RoomRole.Administrative);
                }
                if (string.Equals(cameraName, AdministrativeCameraName, StringComparison.Ordinal))
                {
                    return new DirectionSemantic(
                        "ADMIN_TO_START",
                        RoomRole.Administrative,
                        RoomRole.Start);
                }
                throw new InvalidOperationException("Unknown fixed Camera direction.");
            }

            public Transform GetSourceDoorwayFrame(DirectionSemantic direction)
            {
                return direction.Source == RoomRole.Start
                    ? StartEndpoint.DoorwayFrame
                    : AdministrativeEndpoint.DoorwayFrame;
            }

            public ContractReport CreateReport(MaskCaptureSession session)
            {
                string[] maskBlockers = session != null
                    ? session.Blockers
                    : new[] { "MASK_CAPTURE_UNAVAILABLE" };
                return new ContractReport
                {
                    RendererCount = Renderers.Count,
                    Mask2Count = Renderers.Mask2Count,
                    Mask1Count = Renderers.Mask1Count,
                    OtherMaskCount = Renderers.OtherMaskCount,
                    DoorRendererCount = Door.RendererCount,
                    DoorEnabledRendererCount = Door.EnabledRendererCount,
                    DoorEnabledMask1Count = Renderers.DoorEnabledMask1Count,
                    ProductionLightCount = ProductionLights.Count,
                    ProxyLightCount = Proxies.Count,
                    RemovedReflectionProbeCount = removedReflectionProbeCount,
                    StartDoorwayFramePath = GetHierarchyPath(StartEndpoint.DoorwayFrame),
                    AdministrativeDoorwayFramePath =
                        GetHierarchyPath(AdministrativeEndpoint.DoorwayFrame),
                    StartDescriptorMask = StartEndpoint.Descriptor.renderingLayerMask,
                    AdministrativeDescriptorMask =
                        AdministrativeEndpoint.Descriptor.renderingLayerMask,
                    StartCookiePath = NormalizePath(
                        AssetDatabase.GetAssetPath(StartEndpoint.Descriptor.cookie)),
                    AdministrativeCookiePath = NormalizePath(
                        AssetDatabase.GetAssetPath(
                            AdministrativeEndpoint.Descriptor.cookie)),
                    StartCookieDependencyHash = AssetDatabase.GetAssetDependencyHash(
                        AssetDatabase.GetAssetPath(StartEndpoint.Descriptor.cookie)).ToString(),
                    AdministrativeCookieDependencyHash =
                        AssetDatabase.GetAssetDependencyHash(
                            AssetDatabase.GetAssetPath(
                                AdministrativeEndpoint.Descriptor.cookie)).ToString(),
                    StartCullingMask = StartEndpoint.Descriptor.cullingMask.value,
                    AdministrativeCullingMask =
                        AdministrativeEndpoint.Descriptor.cullingMask.value,
                    StartRange = StartEndpoint.Descriptor.range,
                    AdministrativeRange = AdministrativeEndpoint.Descriptor.range,
                    StartSpotAngle = StartEndpoint.Descriptor.spotAngle,
                    AdministrativeSpotAngle =
                        AdministrativeEndpoint.Descriptor.spotAngle,
                    MaskCaptureStatus = session != null ? "CAPTURED" : "BLOCKED",
                    MaskFitGate = session != null && session.ExactSilhouetteInventory
                        ? "STRUCTURALLY_EXACT_BUT_NOT_VISUAL_FIT"
                        : "BLOCKED",
                    MaskStableObjectIdCount = session != null ? session.StableObjectIdCount : 0,
                    MaskUnsupportedRendererCount =
                        session != null ? session.UnsupportedRendererCount : ExpectedRendererCount,
                    MaskBlockers = maskBlockers
                };
            }
        }

        private sealed class ContractReport
        {
            public int RendererCount;
            public int Mask2Count;
            public int Mask1Count;
            public int OtherMaskCount;
            public int DoorRendererCount;
            public int DoorEnabledRendererCount;
            public int DoorEnabledMask1Count;
            public int ProductionLightCount;
            public int ProxyLightCount;
            public int RemovedReflectionProbeCount;
            public string StartDoorwayFramePath;
            public string AdministrativeDoorwayFramePath;
            public int StartDescriptorMask;
            public int AdministrativeDescriptorMask;
            public string StartCookiePath;
            public string AdministrativeCookiePath;
            public string StartCookieDependencyHash;
            public string AdministrativeCookieDependencyHash;
            public int StartCullingMask;
            public int AdministrativeCullingMask;
            public float StartRange;
            public float AdministrativeRange;
            public float StartSpotAngle;
            public float AdministrativeSpotAngle;
            public string MaskCaptureStatus;
            public string MaskFitGate;
            public int MaskStableObjectIdCount;
            public int MaskUnsupportedRendererCount;
            public string[] MaskBlockers;
        }

        private sealed class EndpointMapping
        {
            public readonly DungeonPortalEndpoint Endpoint;
            public readonly RoomRole Role;
            public readonly DungeonPortalEndpointProfile Profile;
            public readonly Transform DoorwayFrame;
            public readonly DungeonPortalEndpointProfile.PortalDirectLightDescriptor Descriptor;
            public readonly string ProfilePath;

            private EndpointMapping(
                DungeonPortalEndpoint endpoint,
                RoomRole role,
                DungeonPortalEndpointProfile profile,
                Transform doorwayFrame,
                DungeonPortalEndpointProfile.PortalDirectLightDescriptor descriptor,
                string profilePath)
            {
                Endpoint = endpoint;
                Role = role;
                Profile = profile;
                DoorwayFrame = doorwayFrame;
                Descriptor = descriptor;
                ProfilePath = profilePath;
            }

            public static EndpointMapping Capture(
                DungeonPortalEndpoint endpoint,
                Transform startRoom,
                Transform administrativeRoom,
                Transform portalRoot)
            {
                if (endpoint == null || !endpoint.enabled ||
                    !endpoint.transform.IsChildOf(portalRoot))
                {
                    throw new InvalidOperationException("Endpoint placement/enabled state changed.");
                }
                DungeonPortalEndpointProfile profile = endpoint.Profile;
                Transform frame = endpoint.DoorwayFrame;
                if (profile == null || frame == null || frame.IsChildOf(portalRoot))
                    throw new InvalidOperationException("Endpoint profile/frame is unavailable.");
                if (!profile.TryValidate(out string validationError))
                    throw new InvalidOperationException("Endpoint profile invalid: " + validationError);
                if (profile.OutgoingDirectLights.Length != 1)
                    throw new InvalidOperationException("K=1 requires one outgoing descriptor.");

                RoomRole role;
                string expectedPath;
                string expectedRoomId;
                if (frame == startRoom || frame.IsChildOf(startRoom))
                {
                    role = RoomRole.Start;
                    expectedPath = StartProfilePath;
                    expectedRoomId = "StartRoom_R000";
                }
                else if (frame == administrativeRoom || frame.IsChildOf(administrativeRoom))
                {
                    role = RoomRole.Administrative;
                    expectedPath = AdministrativeProfilePath;
                    expectedRoomId = "AdminstrativeSegregation_R000";
                }
                else
                {
                    throw new InvalidOperationException("Endpoint doorway frame is outside both rooms.");
                }

                string actualPath = NormalizePath(AssetDatabase.GetAssetPath(profile));
                if (!string.Equals(actualPath, expectedPath, StringComparison.Ordinal) ||
                    !string.Equals(profile.RoomId, expectedRoomId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Endpoint profile identity changed.");
                }

                DungeonPortalEndpointProfile.PortalDirectLightDescriptor descriptor =
                    profile.OutgoingDirectLights[0];
                if (descriptor.type != LightType.Spot || descriptor.cookie == null ||
                    descriptor.renderingLayerMask != 2)
                {
                    throw new InvalidOperationException(
                        "Known K=1 descriptor Spot/cookie/mask2 contract changed.");
                }

                return new EndpointMapping(
                    endpoint,
                    role,
                    profile,
                    frame,
                    descriptor,
                    actualPath);
            }
        }

        private sealed class RendererContract
        {
            private readonly RendererEntry[] entries;
            public int Count => entries.Length;
            public int Mask2Count { get; }
            public int Mask1Count { get; }
            public int OtherMaskCount { get; }
            public int DoorEnabledMask1Count { get; }
            public RendererEntry[] Entries => entries;

            private RendererContract(
                RendererEntry[] entries,
                int mask2Count,
                int mask1Count,
                int otherMaskCount,
                int doorEnabledMask1Count)
            {
                this.entries = entries;
                Mask2Count = mask2Count;
                Mask1Count = mask1Count;
                OtherMaskCount = otherMaskCount;
                DoorEnabledMask1Count = doorEnabledMask1Count;
            }

            public static RendererContract Capture(
                GameObject root,
                GameObject startRoom,
                GameObject administrativeRoom,
                DoorContract door)
            {
                Renderer[] all = root.GetComponentsInChildren<Renderer>(true);
                Renderer[] start = startRoom.GetComponentsInChildren<Renderer>(true);
                Renderer[] administrative =
                    administrativeRoom.GetComponentsInChildren<Renderer>(true);
                if (all.Length != ExpectedRendererCount ||
                    start.Length + administrative.Length != all.Length)
                {
                    throw new InvalidOperationException(
                        $"Renderer contract changed: all={all.Length}, " +
                        $"rooms={start.Length + administrative.Length}.");
                }

                var startSet = new HashSet<Renderer>(start);
                var administrativeSet = new HashSet<Renderer>(administrative);
                var entries = new RendererEntry[all.Length];
                int mask2 = 0;
                int mask1 = 0;
                int other = 0;
                int doorEnabledMask1 = 0;

                for (int i = 0; i < all.Length; i++)
                {
                    Renderer renderer = all[i];
                    bool inStart = startSet.Contains(renderer);
                    bool inAdministrative = administrativeSet.Contains(renderer);
                    if (inStart == inAdministrative || !(renderer is MeshRenderer))
                        throw new InvalidOperationException("Renderer ownership/type is ambiguous.");
                    if (renderer.HasPropertyBlock())
                    {
                        throw new InvalidOperationException(
                            "K1 validation refuses a pre-existing MaterialPropertyBlock: " +
                            GetHierarchyPath(renderer.transform));
                    }

                    bool isDoor = door.ContainsRenderer(renderer);
                    RoomRole role = inStart ? RoomRole.Start : RoomRole.Administrative;
                    Transform roomRoot = inStart ? startRoom.transform : administrativeRoom.transform;
                    entries[i] = RendererEntry.Capture(
                        renderer,
                        role,
                        isDoor,
                        GetRelativePath(roomRoot, renderer.transform),
                        GetRendererComponentOrdinal(renderer));

                    if (renderer.renderingLayerMask == 2u)
                        mask2++;
                    else if (renderer.renderingLayerMask == 1u)
                        mask1++;
                    else
                        other++;
                    if (isDoor && renderer.enabled && renderer.renderingLayerMask == 1u)
                        doorEnabledMask1++;
                }

                if (mask2 != ExpectedMask2RendererCount ||
                    mask1 != ExpectedMask1RendererCount || other != 0 ||
                    doorEnabledMask1 != ExpectedDoorEnabledMask1Count)
                {
                    throw new InvalidOperationException(
                        $"Known rendering-layer distribution changed: mask2={mask2}, " +
                        $"mask1={mask1}, other={other}, doorEnabledMask1={doorEnabledMask1}.");
                }
                door.AssertRendererMembership(new HashSet<Renderer>(all));
                return new RendererContract(entries, mask2, mask1, other, doorEnabledMask1);
            }

            public void PrepareNoLightmapsOrProbes()
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].PrepareNoLightmapsOrProbes();
            }

            public void AssertPrepared(bool exactMaterialFingerprint)
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].AssertPrepared(exactMaterialFingerprint);
            }
        }

        private sealed class RendererEntry
        {
            public readonly Renderer Renderer;
            public readonly RoomRole Role;
            public readonly bool IsDoor;
            public readonly string StablePath;
            public readonly int ComponentOrdinal;
            public int StableObjectId { get; set; }

            private readonly bool enabled;
            private readonly bool forceRenderingOff;
            private readonly ShadowCastingMode shadowCastingMode;
            private readonly bool receiveShadows;
            private readonly uint renderingLayerMask;
            private readonly int gameObjectLayer;
            private readonly Vector4 lightmapScaleOffset;
            private readonly Vector4 realtimeLightmapScaleOffset;
            private readonly Transform probeAnchor;
            private readonly GameObject lightProbeProxyVolumeOverride;
            private readonly MeshFilter meshFilter;
            private readonly Mesh mesh;
            private readonly MeshRenderer meshRenderer;
            private readonly Mesh additionalVertexStreams;
            private readonly MaterialState[] materials;

            private RendererEntry(
                Renderer renderer,
                RoomRole role,
                bool isDoor,
                string stablePath,
                int componentOrdinal)
            {
                Renderer = renderer;
                Role = role;
                IsDoor = isDoor;
                StablePath = stablePath;
                ComponentOrdinal = componentOrdinal;
                enabled = renderer.enabled;
                forceRenderingOff = renderer.forceRenderingOff;
                shadowCastingMode = renderer.shadowCastingMode;
                receiveShadows = renderer.receiveShadows;
                renderingLayerMask = renderer.renderingLayerMask;
                gameObjectLayer = renderer.gameObject.layer;
                lightmapScaleOffset = renderer.lightmapScaleOffset;
                realtimeLightmapScaleOffset = renderer.realtimeLightmapScaleOffset;
                probeAnchor = renderer.probeAnchor;
                lightProbeProxyVolumeOverride = renderer.lightProbeProxyVolumeOverride;
                meshRenderer = renderer as MeshRenderer;
                meshFilter = renderer.GetComponent<MeshFilter>();
                mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                additionalVertexStreams =
                    meshRenderer != null ? meshRenderer.additionalVertexStreams : null;
                Material[] shared = renderer.sharedMaterials;
                materials = new MaterialState[shared.Length];
                for (int i = 0; i < shared.Length; i++)
                    materials[i] = MaterialState.Capture(shared[i]);
            }

            public static RendererEntry Capture(
                Renderer renderer,
                RoomRole role,
                bool isDoor,
                string stablePath,
                int componentOrdinal)
            {
                if (renderer.GetComponent<MeshFilter>() == null ||
                    renderer.GetComponent<MeshFilter>().sharedMesh == null)
                {
                    throw new InvalidOperationException(
                        "Renderer MeshFilter/sharedMesh is missing: " +
                        GetHierarchyPath(renderer.transform));
                }
                return new RendererEntry(
                    renderer,
                    role,
                    isDoor,
                    stablePath,
                    componentOrdinal);
            }

            public bool SourceEnabled => enabled;
            public Mesh Mesh => mesh;
            public Material[] SourceMaterials => Renderer.sharedMaterials;
            public Mesh AdditionalVertexStreams => additionalVertexStreams;

            public void PrepareNoLightmapsOrProbes()
            {
                Renderer.lightmapIndex = -1;
                Renderer.realtimeLightmapIndex = -1;
                Renderer.lightProbeUsage = LightProbeUsage.Off;
                Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            public void AssertPrepared(bool exactMaterialFingerprint)
            {
                if (Renderer == null || Renderer.enabled != enabled ||
                    Renderer.forceRenderingOff != forceRenderingOff ||
                    Renderer.shadowCastingMode != shadowCastingMode ||
                    Renderer.receiveShadows != receiveShadows ||
                    Renderer.renderingLayerMask != renderingLayerMask ||
                    Renderer.gameObject.layer != gameObjectLayer ||
                    Renderer.lightmapIndex != -1 || Renderer.realtimeLightmapIndex != -1 ||
                    Renderer.lightProbeUsage != LightProbeUsage.Off ||
                    Renderer.reflectionProbeUsage != ReflectionProbeUsage.Off ||
                    Renderer.lightmapScaleOffset != lightmapScaleOffset ||
                    Renderer.realtimeLightmapScaleOffset != realtimeLightmapScaleOffset ||
                    Renderer.probeAnchor != probeAnchor ||
                    Renderer.lightProbeProxyVolumeOverride != lightProbeProxyVolumeOverride ||
                    Renderer.HasPropertyBlock())
                {
                    throw new InvalidOperationException(
                        "Renderer structure/material-parity guard changed: " + StablePath);
                }
                if (meshFilter == null || meshFilter.sharedMesh != mesh ||
                    meshRenderer == null ||
                    meshRenderer.additionalVertexStreams != additionalVertexStreams)
                {
                    throw new InvalidOperationException("Renderer mesh/streams changed: " + StablePath);
                }

                Material[] current = Renderer.sharedMaterials;
                if (current.Length != materials.Length)
                    throw new InvalidOperationException("Material slot count changed: " + StablePath);
                for (int i = 0; i < current.Length; i++)
                    materials[i].AssertUnchanged(current[i], exactMaterialFingerprint);
            }
        }

        private sealed class MaterialState
        {
            private readonly Material material;
            private readonly Shader shader;
            private readonly int renderQueue;
            private readonly bool enableInstancing;
            private readonly bool doubleSidedGi;
            private readonly MaterialGlobalIlluminationFlags giFlags;
            private readonly string[] keywords;
            private readonly string serializedSha256;
            private readonly bool hasEmissionColor;
            private readonly Color emissionColor;
            private readonly bool hasEmissionMap;
            private readonly Texture emissionMap;
            private readonly Vector2 emissionScale;
            private readonly Vector2 emissionOffset;

            private MaterialState(Material material)
            {
                this.material = material;
                shader = material != null ? material.shader : null;
                renderQueue = material != null ? material.renderQueue : 0;
                enableInstancing = material != null && material.enableInstancing;
                doubleSidedGi = material != null && material.doubleSidedGI;
                giFlags = material != null
                    ? material.globalIlluminationFlags
                    : MaterialGlobalIlluminationFlags.None;
                keywords = material != null
                    ? (string[])material.shaderKeywords.Clone()
                    : Array.Empty<string>();
                Array.Sort(keywords, StringComparer.Ordinal);
                serializedSha256 = ComputeSerializedMaterialSha256(material);
                hasEmissionColor = material != null && material.HasProperty("_EmissionColor");
                emissionColor = hasEmissionColor
                    ? material.GetColor("_EmissionColor")
                    : default;
                hasEmissionMap = material != null && material.HasProperty("_EmissionMap");
                emissionMap = hasEmissionMap ? material.GetTexture("_EmissionMap") : null;
                emissionScale = hasEmissionMap
                    ? material.GetTextureScale("_EmissionMap")
                    : default;
                emissionOffset = hasEmissionMap
                    ? material.GetTextureOffset("_EmissionMap")
                    : default;
            }

            public static MaterialState Capture(Material material)
            {
                return new MaterialState(material);
            }

            public void AssertUnchanged(Material current, bool exactFingerprint)
            {
                if (current != material)
                    throw new InvalidOperationException("Material reference changed.");
                if (material == null)
                    return;
                if (material.shader != shader || material.renderQueue != renderQueue ||
                    material.enableInstancing != enableInstancing ||
                    material.doubleSidedGI != doubleSidedGi ||
                    material.globalIlluminationFlags != giFlags ||
                    material.HasProperty("_EmissionColor") != hasEmissionColor ||
                    material.HasProperty("_EmissionMap") != hasEmissionMap ||
                    (hasEmissionColor && material.GetColor("_EmissionColor") != emissionColor) ||
                    (hasEmissionMap &&
                     (material.GetTexture("_EmissionMap") != emissionMap ||
                      material.GetTextureScale("_EmissionMap") != emissionScale ||
                      material.GetTextureOffset("_EmissionMap") != emissionOffset)))
                {
                    throw new InvalidOperationException("Material shader/emission state changed.");
                }
                string[] currentKeywords = (string[])material.shaderKeywords.Clone();
                Array.Sort(currentKeywords, StringComparer.Ordinal);
                if (currentKeywords.Length != keywords.Length)
                    throw new InvalidOperationException("Material keyword count changed.");
                for (int i = 0; i < keywords.Length; i++)
                {
                    if (!string.Equals(currentKeywords[i], keywords[i], StringComparison.Ordinal))
                        throw new InvalidOperationException("Material keyword set changed.");
                }
                if (exactFingerprint && !string.Equals(
                        ComputeSerializedMaterialSha256(material),
                        serializedSha256,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Serialized material properties changed.");
                }
            }

            private static string ComputeSerializedMaterialSha256(Material material)
            {
                if (material == null)
                    return "<null>";
                string json = EditorJsonUtility.ToJson(material, false);
                return ComputeSha256(Encoding.UTF8.GetBytes(json));
            }
        }

        private sealed class ProductionLightContract
        {
            private readonly ProductionLightEntry[] entries;
            public int Count => entries.Length;

            private ProductionLightContract(ProductionLightEntry[] entries)
            {
                this.entries = entries;
            }

            public static ProductionLightContract Capture(
                GameObject root,
                GameObject startRoom,
                GameObject administrativeRoom)
            {
                Light[] all = root.GetComponentsInChildren<Light>(true);
                Light[] start = startRoom.GetComponentsInChildren<Light>(true);
                Light[] administrative =
                    administrativeRoom.GetComponentsInChildren<Light>(true);
                if (all.Length != ExpectedProductionLightCount ||
                    start.Length != ExpectedStartLightCount ||
                    administrative.Length != ExpectedAdministrativeLightCount ||
                    start.Length + administrative.Length != all.Length)
                {
                    throw new InvalidOperationException(
                        $"Production Light contract changed: all={all.Length}, " +
                        $"start={start.Length}, admin={administrative.Length}.");
                }
                var ownership = new HashSet<Light>(start);
                ownership.UnionWith(administrative);
                if (ownership.Count != all.Length)
                    throw new InvalidOperationException("Production Light ownership is ambiguous.");

                var entries = new ProductionLightEntry[all.Length];
                for (int i = 0; i < all.Length; i++)
                {
                    if (!ownership.Contains(all[i]))
                        throw new InvalidOperationException("Non-room Light found in clone.");
                    entries[i] = ProductionLightEntry.Capture(all[i]);
                }
                return new ProductionLightContract(entries);
            }

            public void DisableAll()
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].Disable();
                AssertDisabled();
            }

            public void AssertDisabled()
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].AssertDisabled();
            }
        }

        private sealed class ProductionLightEntry
        {
            private readonly Light light;
            private readonly bool sourceEnabled;
            private readonly LightmapBakeType bakeType;
            private readonly LightType type;
            private readonly LightShadows shadows;
            private readonly Color color;
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
            private readonly Vector3 position;
            private readonly Quaternion rotation;
            private readonly Vector3 scale;
            private readonly Component[] components;

            private ProductionLightEntry(Light light)
            {
                this.light = light;
                sourceEnabled = light.enabled;
                bakeType = light.lightmapBakeType;
                type = light.type;
                shadows = light.shadows;
                color = light.color;
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
                position = light.transform.position;
                rotation = light.transform.rotation;
                scale = light.transform.lossyScale;
                components = light.GetComponents<Component>();

                if (!sourceEnabled || bakeType != LightmapBakeType.Baked ||
                    shadows != LightShadows.Soft)
                {
                    throw new InvalidOperationException(
                        "Expected enabled Baked Soft-shadow production Light: " +
                        GetHierarchyPath(light.transform));
                }
            }

            public static ProductionLightEntry Capture(Light light)
            {
                return new ProductionLightEntry(light);
            }

            public void Disable()
            {
                light.enabled = false;
            }

            public void AssertDisabled()
            {
                if (light == null || light.enabled || light.lightmapBakeType != bakeType ||
                    light.type != type || light.shadows != shadows || light.color != color ||
                    !Mathf.Approximately(light.intensity, intensity) ||
                    !Mathf.Approximately(light.bounceIntensity, bounceIntensity) ||
                    !Mathf.Approximately(light.range, range) ||
                    !Mathf.Approximately(light.spotAngle, spotAngle) ||
                    !Mathf.Approximately(light.innerSpotAngle, innerSpotAngle) ||
                    light.cullingMask != cullingMask ||
                    light.renderingLayerMask != renderingLayerMask ||
                    light.cookie != cookie || light.renderMode != renderMode ||
                    !Mathf.Approximately(light.shadowStrength, shadowStrength) ||
                    light.transform.position != position ||
                    light.transform.rotation != rotation ||
                    light.transform.lossyScale != scale)
                {
                    throw new InvalidOperationException(
                        "Disabled production Light structure changed: " +
                        GetHierarchyPath(light != null ? light.transform : null));
                }
                Component[] current = light.GetComponents<Component>();
                if (current.Length != components.Length)
                    throw new InvalidOperationException("Production Light components changed.");
                for (int i = 0; i < components.Length; i++)
                {
                    if (current[i] != components[i])
                        throw new InvalidOperationException("Production Light component order changed.");
                }
            }
        }

        private sealed class ProxySet
        {
            private readonly ProxyLight start;
            private readonly ProxyLight administrative;
            public int Count => 2;

            private ProxySet(ProxyLight start, ProxyLight administrative)
            {
                this.start = start;
                this.administrative = administrative;
            }

            public static ProxySet Create(
                EndpointMapping startEndpoint,
                EndpointMapping administrativeEndpoint)
            {
                if (startEndpoint == null || administrativeEndpoint == null ||
                    startEndpoint.Role != RoomRole.Start ||
                    administrativeEndpoint.Role != RoomRole.Administrative)
                {
                    throw new InvalidOperationException("Endpoint roles are invalid for proxy creation.");
                }
                return new ProxySet(
                    ProxyLight.Create(startEndpoint),
                    ProxyLight.Create(administrativeEndpoint));
            }

            public void DisableAll()
            {
                start.Disable();
                administrative.Disable();
            }

            public void ApplyVariant(Variant variant)
            {
                start.ApplyVariant(variant);
                administrative.ApplyVariant(variant);
            }

            public void ApplyPower(PowerState power)
            {
                start.ApplyPower(power.StartPower);
                administrative.ApplyPower(power.AdministrativePower);
            }

            public void AssertDisabledAndOwned()
            {
                start.AssertDisabledAndOwned();
                administrative.AssertDisabledAndOwned();
            }

            public void AssertState(Variant variant, PowerState power)
            {
                start.AssertState(variant, power.StartPower);
                administrative.AssertState(variant, power.AdministrativePower);
            }

            public void AssertOwnedStructure()
            {
                start.AssertOwnedStructure();
                administrative.AssertOwnedStructure();
            }
        }

        private sealed class ProxyLight
        {
            private readonly EndpointMapping endpoint;
            private readonly GameObject gameObject;
            private readonly Light light;
            private readonly UniversalAdditionalLightData additionalData;

            private ProxyLight(
                EndpointMapping endpoint,
                GameObject gameObject,
                Light light,
                UniversalAdditionalLightData additionalData)
            {
                this.endpoint = endpoint;
                this.gameObject = gameObject;
                this.light = light;
                this.additionalData = additionalData;
            }

            public static ProxyLight Create(EndpointMapping endpoint)
            {
                DungeonPortalEndpointProfile.PortalDirectLightDescriptor descriptor =
                    endpoint.Descriptor;
                var lightObject = new GameObject(
                    "__K1ProxyDirect_" + endpoint.Role + "_" + descriptor.label)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = endpoint.DoorwayFrame.gameObject.layer
                };
                Scene proxyScene = endpoint.DoorwayFrame.gameObject.scene;
                if (!proxyScene.IsValid() || !proxyScene.isLoaded ||
                    !EditorSceneManager.IsPreviewScene(proxyScene))
                {
                    Object.DestroyImmediate(lightObject);
                    throw new InvalidOperationException(
                        "K=1 proxy doorway frame is outside the isolated preview scene.");
                }
                SceneManager.MoveGameObjectToScene(lightObject, proxyScene);
                lightObject.transform.SetParent(endpoint.DoorwayFrame, false);
                lightObject.transform.localPosition = descriptor.localPosition;
                lightObject.transform.localEulerAngles = descriptor.localEulerAngles;
                lightObject.transform.localScale = Vector3.one;

                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Spot;
                light.range = descriptor.range;
                light.spotAngle = descriptor.spotAngle;
                light.innerSpotAngle = descriptor.innerSpotAngle;
                light.cookie = descriptor.cookie;
                light.shadows = descriptor.castShadows
                    ? LightShadows.Soft
                    : LightShadows.None;
                light.shadowStrength = descriptor.shadowStrength;
                light.cullingMask = descriptor.cullingMask.value;
                light.renderingLayerMask = descriptor.renderingLayerMask;
                light.lightmapBakeType = LightmapBakeType.Realtime;
                light.bounceIntensity = 0f;
                light.enabled = false;

                UniversalAdditionalLightData additional =
                    light.GetUniversalAdditionalLightData();
                uint mask = unchecked((uint)descriptor.renderingLayerMask);
                additional.renderingLayers = mask;
                additional.shadowRenderingLayers = mask;
                return new ProxyLight(endpoint, lightObject, light, additional);
            }

            public void Disable()
            {
                light.intensity = 0f;
                light.enabled = false;
            }

            public void ApplyVariant(Variant variant)
            {
                int mask = endpoint.Descriptor.renderingLayerMask |
                           (variant.IncludeRendererBit1 ? 1 : 0);
                light.renderingLayerMask = mask;
                uint unsignedMask = unchecked((uint)mask);
                additionalData.renderingLayers = unsignedMask;
                additionalData.shadowRenderingLayers = unsignedMask;
            }

            public void ApplyPower(float power01)
            {
                DungeonPortalEndpointProfile.PortalDirectLightDescriptor descriptor =
                    endpoint.Descriptor;
                Color radiance = PortalTransportMath.InterpolateLinearRadiance(
                    descriptor.power0Color,
                    descriptor.power0Intensity,
                    descriptor.power100Color,
                    descriptor.power100Intensity,
                    power01);
                ApplyLinearRadiance(light, radiance);
            }

            public void AssertDisabledAndOwned()
            {
                AssertOwnedStructure();
                if (light.enabled || !Mathf.Approximately(light.intensity, 0f))
                    throw new InvalidOperationException("Owned proxy did not remain disabled.");
            }

            public void AssertState(Variant variant, float power01)
            {
                AssertOwnedStructure();
                int expectedMask = endpoint.Descriptor.renderingLayerMask |
                                   (variant.IncludeRendererBit1 ? 1 : 0);
                uint expectedUnsignedMask = unchecked((uint)expectedMask);
                if (light.renderingLayerMask != expectedMask ||
                    additionalData.renderingLayers != expectedUnsignedMask ||
                    additionalData.shadowRenderingLayers != expectedUnsignedMask)
                {
                    throw new InvalidOperationException("Proxy rendering-layer variant changed.");
                }

                DungeonPortalEndpointProfile.PortalDirectLightDescriptor descriptor =
                    endpoint.Descriptor;
                Color expectedRadiance = PortalTransportMath.InterpolateLinearRadiance(
                    descriptor.power0Color,
                    descriptor.power0Intensity,
                    descriptor.power100Color,
                    descriptor.power100Intensity,
                    power01);
                float maximum = Mathf.Max(
                    0f,
                    Mathf.Max(
                        expectedRadiance.r,
                        Mathf.Max(expectedRadiance.g, expectedRadiance.b)));
                bool expectedEnabled = maximum > 0.0001f;
                if (light.enabled != expectedEnabled)
                    throw new InvalidOperationException("Proxy enabled state differs from radiance.");
                if (!expectedEnabled)
                {
                    if (!Mathf.Approximately(light.intensity, 0f) || light.isActiveAndEnabled)
                        throw new InvalidOperationException("Disabled proxy retained intensity.");
                    return;
                }

                if (!gameObject.activeInHierarchy || !light.isActiveAndEnabled)
                    throw new InvalidOperationException("Powered proxy is not active in the clone.");

                Color expectedColor = new Color(
                    Mathf.Max(0f, expectedRadiance.r) / maximum,
                    Mathf.Max(0f, expectedRadiance.g) / maximum,
                    Mathf.Max(0f, expectedRadiance.b) / maximum,
                    1f);
                if (!ApproximatelyColor(light.color, expectedColor) ||
                    !Mathf.Approximately(light.intensity, maximum))
                {
                    throw new InvalidOperationException("Proxy linear radiance application changed.");
                }
            }

            public void AssertOwnedStructure()
            {
                DungeonPortalEndpointProfile.PortalDirectLightDescriptor descriptor =
                    endpoint.Descriptor;
                if (gameObject == null || light == null || additionalData == null ||
                    gameObject.scene != endpoint.DoorwayFrame.gameObject.scene ||
                    !EditorSceneManager.IsPreviewScene(gameObject.scene) ||
                    !gameObject.activeSelf ||
                    gameObject.transform.parent != endpoint.DoorwayFrame ||
                    gameObject.transform.localPosition != descriptor.localPosition ||
                    Quaternion.Angle(
                        gameObject.transform.localRotation,
                        Quaternion.Euler(descriptor.localEulerAngles)) > 0.001f ||
                    gameObject.transform.localScale != Vector3.one ||
                    light.type != LightType.Spot ||
                    !Mathf.Approximately(light.range, descriptor.range) ||
                    !Mathf.Approximately(light.spotAngle, descriptor.spotAngle) ||
                    !Mathf.Approximately(light.innerSpotAngle, descriptor.innerSpotAngle) ||
                    light.cookie != descriptor.cookie ||
                    light.shadows != (descriptor.castShadows
                        ? LightShadows.Soft
                        : LightShadows.None) ||
                    !Mathf.Approximately(light.shadowStrength, descriptor.shadowStrength) ||
                    light.cullingMask != descriptor.cullingMask.value ||
                    light.lightmapBakeType != LightmapBakeType.Realtime ||
                    !Mathf.Approximately(light.bounceIntensity, 0f))
                {
                    throw new InvalidOperationException(
                        "Owned K=1 proxy structure changed for " + endpoint.Role + ".");
                }
                Component[] components = gameObject.GetComponents<Component>();
                if (components.Length != 3 || components[0] != gameObject.transform ||
                    components[1] != light || components[2] != additionalData)
                {
                    throw new InvalidOperationException("Unexpected proxy component structure.");
                }
            }

            private static void ApplyLinearRadiance(Light target, Color radiance)
            {
                float maximum = Mathf.Max(
                    0f,
                    Mathf.Max(radiance.r, Mathf.Max(radiance.g, radiance.b)));
                if (maximum <= 0.0001f)
                {
                    target.intensity = 0f;
                    target.enabled = false;
                    return;
                }
                target.color = new Color(
                    Mathf.Max(0f, radiance.r) / maximum,
                    Mathf.Max(0f, radiance.g) / maximum,
                    Mathf.Max(0f, radiance.b) / maximum,
                    1f);
                target.intensity = maximum;
                target.enabled = true;
            }

            private static bool ApproximatelyColor(Color left, Color right)
            {
                return Mathf.Approximately(left.r, right.r) &&
                       Mathf.Approximately(left.g, right.g) &&
                       Mathf.Approximately(left.b, right.b) &&
                       Mathf.Approximately(left.a, right.a);
            }
        }

        private sealed class DoorContract
        {
            public readonly Transform Root;
            public readonly Transform Leaf;
            public readonly Quaternion ClosedLocalRotation;
            public readonly Vector3 HingeAxis;
            public readonly float OpenAngleDegrees;
            public readonly int RendererCount;
            public readonly int EnabledRendererCount;

            private readonly Vector3 rootLocalPosition;
            private readonly Quaternion rootLocalRotation;
            private readonly Vector3 rootLocalScale;
            private readonly Renderer[] renderers;
            private readonly bool[] rendererEnabled;
            private readonly Mesh[] rendererMeshes;

            private DoorContract(
                Transform root,
                Transform leaf,
                Quaternion closedLocalRotation,
                Vector3 hingeAxis,
                float openAngleDegrees,
                Renderer[] renderers,
                bool[] rendererEnabled,
                Mesh[] rendererMeshes)
            {
                Root = root;
                Leaf = leaf;
                ClosedLocalRotation = closedLocalRotation;
                HingeAxis = hingeAxis;
                OpenAngleDegrees = openAngleDegrees;
                RendererCount = renderers.Length;
                int enabledCount = 0;
                for (int i = 0; i < rendererEnabled.Length; i++)
                {
                    if (rendererEnabled[i])
                        enabledCount++;
                }
                EnabledRendererCount = enabledCount;
                rootLocalPosition = root.localPosition;
                rootLocalRotation = root.localRotation;
                rootLocalScale = root.localScale;
                this.renderers = renderers;
                this.rendererEnabled = rendererEnabled;
                this.rendererMeshes = rendererMeshes;
            }

            public static DoorContract Capture(
                Transform root,
                DungeonPortalDoorAngleSource angleSource)
            {
                if (root == null || !root.gameObject.activeSelf)
                    throw new InvalidOperationException("Actual moving door is inactive.");
                Transform leaf = root.Find(DoorLeafName);
                if (leaf == null || leaf.parent != root || angleSource.DoorLeaf != leaf)
                    throw new InvalidOperationException("Door leaf binding changed.");

                ReadDoorConfiguration(
                    angleSource,
                    out Quaternion closed,
                    out Vector3 axis,
                    out float openAngle);
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length != ExpectedDoorRendererCount)
                    throw new InvalidOperationException("Door renderer count changed.");
                RequireNamedRenderer(root, DoorPositiveRendererName);
                RequireNamedRenderer(root, DoorNegativeRendererName);
                RequireNamedRenderer(root, DoorEdgeRendererName);

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
                        throw new InvalidOperationException("Door renderer mesh is missing.");
                    meshes[i] = filter.sharedMesh;
                }
                if (enabledCount != ExpectedDoorEnabledRendererCount)
                    throw new InvalidOperationException("Door enabled-renderer count changed.");
                return new DoorContract(
                    root,
                    leaf,
                    closed,
                    axis,
                    openAngle,
                    renderers,
                    enabled,
                    meshes);
            }

            public bool ContainsRenderer(Renderer renderer)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == renderer)
                        return true;
                }
                return false;
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
                    Root.localPosition != rootLocalPosition ||
                    Root.localRotation != rootLocalRotation ||
                    Root.localScale != rootLocalScale ||
                    Quaternion.Angle(Leaf.localRotation, expected) > 0.01f)
                {
                    throw new InvalidOperationException("Door pose/placement changed.");
                }
                Renderer[] current = Root.GetComponentsInChildren<Renderer>(true);
                if (current.Length != renderers.Length)
                    throw new InvalidOperationException("Door renderer structure changed.");
                for (int i = 0; i < current.Length; i++)
                {
                    MeshFilter filter = current[i].GetComponent<MeshFilter>();
                    if (current[i] != renderers[i] ||
                        current[i].enabled != rendererEnabled[i] ||
                        filter == null || filter.sharedMesh != rendererMeshes[i])
                    {
                        throw new InvalidOperationException("Door renderer state changed.");
                    }
                }
            }

            public void AssertRendererMembership(HashSet<Renderer> roomRenderers)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (!roomRenderers.Contains(renderers[i]))
                        throw new InvalidOperationException("Door renderer left production rooms.");
                }
            }

            private static Renderer RequireNamedRenderer(Transform root, string name)
            {
                Transform transform = FindUniqueDescendant(root, name, true);
                Renderer renderer = transform.GetComponent<Renderer>();
                if (renderer == null)
                    throw new InvalidOperationException("Missing door split renderer: " + name);
                return renderer;
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
                throw new InvalidOperationException("Canonical door fields are unavailable.");
            closed = closedProperty.quaternionValue;
            axis = axisProperty.vector3Value;
            openAngle = angleProperty.floatValue;
            if (axis.sqrMagnitude <= Mathf.Epsilon || Mathf.Abs(openAngle) <= 0.001f)
                throw new InvalidOperationException("Canonical door configuration is invalid.");
            axis.Normalize();
        }

        private sealed class MaskCaptureSession : IDisposable
        {
            private static readonly int CaptureGpuVpId =
                Shader.PropertyToID("_StageA_CaptureGpuVP");
            private static readonly int DoorwayWorldToLocalId =
                Shader.PropertyToID("_StageA_DoorwayWorldToLocal");
            private static readonly int ObjectIdColorId =
                Shader.PropertyToID("_StageA_ObjectIdColor");
            private static readonly int UnsupportedId =
                Shader.PropertyToID("_StageA_Unsupported");
            private static readonly int CullId =
                Shader.PropertyToID("_StageA_Cull");

            private readonly PreviewContract contract;
            private readonly MaskRendererInfo[] inventory;
            private readonly Dictionary<int, MaskRendererInfo> byStableId;
            private readonly Dictionary<int, Material> materials;
            private readonly List<string> blockers;
            private readonly Material unsupportedMaterial;
            private readonly RenderTexture objectIdTarget;
            private readonly RenderTexture normalTarget;
            private readonly RenderTexture depthTarget;

            public bool ExactSilhouetteInventory => UnsupportedRendererCount == 0;
            public int StableObjectIdCount => inventory.Length;
            public int UnsupportedRendererCount { get; }
            public string[] Blockers => blockers.ToArray();

            private MaskCaptureSession(
                PreviewContract contract,
                Shader shader,
                out int unsupportedRendererCount)
            {
                this.contract = contract;
                blockers = new List<string>();
                materials = new Dictionary<int, Material>();
                byStableId = new Dictionary<int, MaskRendererInfo>();

                var sorted = new List<RendererEntry>(contract.Renderers.Entries);
                sorted.Sort(CompareRendererEntries);
                inventory = new MaskRendererInfo[sorted.Count];
                int unsupported = 0;
                for (int i = 0; i < sorted.Count; i++)
                {
                    RendererEntry entry = sorted[i];
                    entry.StableObjectId = i + 1;
                    MaskRendererInfo info = Classify(entry);
                    inventory[i] = info;
                    byStableId.Add(info.StableObjectId, info);
                    if (!info.Supported)
                    {
                        unsupported++;
                        blockers.Add(
                            "UNSUPPORTED_RENDERER " + info.StableKey + ": " + info.Reason);
                    }
                }
                UnsupportedRendererCount = unsupported;
                unsupportedRendererCount = unsupported;

                Material localUnsupportedMaterial = null;
                RenderTexture localObjectIdTarget = null;
                RenderTexture localNormalTarget = null;
                RenderTexture localDepthTarget = null;
                try
                {
                    localUnsupportedMaterial = CreateMaskMaterial(
                        shader,
                        "__K1ProxyMask_Unsupported",
                        new Color(1f, 0f, 1f, 1f),
                        true,
                        CullMode.Off);
                    for (int i = 0; i < inventory.Length; i++)
                    {
                        MaskRendererInfo info = inventory[i];
                        if (!info.Supported)
                            continue;
                        materials.Add(
                            info.StableObjectId,
                            CreateMaskMaterial(
                                shader,
                                "__K1ProxyMask_ID_" + info.StableObjectId,
                                EncodeStableObjectId(info.StableObjectId),
                                false,
                                info.CullMode));
                    }

                    localObjectIdTarget = CreateMaskTarget(
                        "K1Proxy_ObjectId",
                        RenderTextureFormat.ARGB32,
                        0);
                    localNormalTarget = CreateMaskTarget(
                        "K1Proxy_DoorwayZNormal",
                        RenderTextureFormat.ARGBHalf,
                        0);
                    localDepthTarget = CreateMaskTarget(
                        "K1Proxy_MaskDepth",
                        RenderTextureFormat.Depth,
                        24);
                }
                catch
                {
                    foreach (Material material in materials.Values)
                    {
                        if (material != null)
                            Object.DestroyImmediate(material);
                    }
                    materials.Clear();
                    if (localUnsupportedMaterial != null)
                        Object.DestroyImmediate(localUnsupportedMaterial);
                    DestroyRenderTexture(localObjectIdTarget);
                    DestroyRenderTexture(localNormalTarget);
                    DestroyRenderTexture(localDepthTarget);
                    throw;
                }

                unsupportedMaterial = localUnsupportedMaterial;
                objectIdTarget = localObjectIdTarget;
                normalTarget = localNormalTarget;
                depthTarget = localDepthTarget;
            }

            public static MaskCaptureSession TryCreate(
                PreviewContract contract,
                List<string> globalBlockers)
            {
                if (SystemInfo.supportedRenderTargetCount < 2 ||
                    SystemInfo.graphicsShaderLevel < 45 ||
                    !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Depth))
                {
                    globalBlockers.Add(
                        "MASK_CAPTURE_UNAVAILABLE: MRT2, shader model 4.5, or a Depth target is unsupported.");
                    return null;
                }
                Shader shader = Shader.Find(ObjectIdShaderName);
                if (shader == null || !shader.isSupported ||
                    !string.Equals(
                        NormalizePath(AssetDatabase.GetAssetPath(shader)),
                        ObjectIdShaderPath,
                        StringComparison.Ordinal))
                {
                    globalBlockers.Add(
                        "MASK_CAPTURE_UNAVAILABLE: exact self-owned object-ID shader missing/unsupported.");
                    return null;
                }

                MaskCaptureSession session = null;
                try
                {
                    session = new MaskCaptureSession(contract, shader, out int unsupported);
                    if (unsupported > 0)
                    {
                        globalBlockers.Add(
                            "MASK_FIT_GATE=BLOCKED: " + unsupported +
                            " renderer(s) cannot prove an exact silhouette.");
                    }
                    return session;
                }
                catch (Exception exception)
                {
                    if (session != null)
                        session.Dispose();
                    globalBlockers.Add("MASK_CAPTURE_SETUP_FAILED: " + exception.Message);
                    return null;
                }
            }

            public MaskEvidence Capture(
                Camera camera,
                DoorPose pose,
                DirectionSemantic direction)
            {
                if (camera == null || camera.enabled || camera.targetTexture != null)
                    throw new InvalidOperationException("Mask capture requires disabled fixed Camera.");
                contract.Door.AssertPose(pose);

                RenderTexture originalActive = RenderTexture.active;
                float originalAspect = camera.aspect;
                Matrix4x4 originalProjection = camera.projectionMatrix;
                bool originalSrgb = GL.sRGBWrite;
                Texture2D readback = null;
                CommandBuffer command = null;

                try
                {
                    camera.aspect = CaptureWidth / (float)CaptureHeight;
                    camera.ResetProjectionMatrix();
                    AssertStandardPerspectiveProjection(camera);
                    Matrix4x4 view = camera.worldToCameraMatrix;
                    Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(
                        camera.projectionMatrix,
                        true);
                    Matrix4x4 gpuVp = gpuProjection * view;
                    Transform doorwayFrame = contract.GetSourceDoorwayFrame(direction);

                    command = new CommandBuffer
                    {
                        name = "K1 Proxy Direct Stable Object-ID Mask"
                    };
                    var colorTargets = new[]
                    {
                        new RenderTargetIdentifier(objectIdTarget),
                        new RenderTargetIdentifier(normalTarget)
                    };
                    command.SetRenderTarget(
                        colorTargets,
                        new RenderTargetIdentifier(depthTarget));
                    command.ClearRenderTarget(true, true, Color.clear);
                    command.SetViewport(new Rect(0f, 0f, CaptureWidth, CaptureHeight));
                    command.SetViewProjectionMatrices(view, gpuProjection);
                    command.SetGlobalMatrix(CaptureGpuVpId, gpuVp);
                    command.SetGlobalMatrix(
                        DoorwayWorldToLocalId,
                        doorwayFrame.worldToLocalMatrix);

                    for (int i = 0; i < inventory.Length; i++)
                    {
                        MaskRendererInfo info = inventory[i];
                        Renderer renderer = info.Entry.Renderer;
                        if (!ShouldDraw(renderer, camera))
                            continue;
                        Material material = info.Supported
                            ? materials[info.StableObjectId]
                            : unsupportedMaterial;
                        int submeshCount = info.Entry.Mesh.subMeshCount;
                        for (int submesh = 0; submesh < submeshCount; submesh++)
                            command.DrawRenderer(renderer, material, submesh, 0);
                    }

                    GL.sRGBWrite = false;
                    Graphics.ExecuteCommandBuffer(command);
                    RenderTexture.active = objectIdTarget;
                    readback = new Texture2D(
                        CaptureWidth,
                        CaptureHeight,
                        TextureFormat.RGBA32,
                        false,
                        true)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    readback.ReadPixels(
                        new Rect(0f, 0f, CaptureWidth, CaptureHeight),
                        0,
                        0,
                        false);
                    readback.Apply(false, false);
                    Color32[] pixels = readback.GetPixels32();
                    byte[] objectIdPng = ImageConversion.EncodeToPNG(readback);
                    RequireBytes(objectIdPng, "object-ID PNG");

                    byte[] source = EncodeSemanticMask(
                        pixels,
                        direction,
                        SemanticMask.Source,
                        out int knownPixels,
                        out int unsupportedPixels);
                    byte[] receiver = EncodeSemanticMask(
                        pixels,
                        direction,
                        SemanticMask.Receiver,
                        out _,
                        out _);
                    byte[] door = EncodeSemanticMask(
                        pixels,
                        direction,
                        SemanticMask.Door,
                        out _,
                        out _);
                    byte[] forbidden = EncodeSemanticMask(
                        pixels,
                        direction,
                        SemanticMask.Forbidden,
                        out _,
                        out _);

                    string stem = "MASK_D" + pose.Percent.ToString(
                                      "000",
                                      CultureInfo.InvariantCulture) +
                                  "__" + FixedCameraToken(camera.name);
                    return new MaskEvidence(
                        pose,
                        camera.name,
                        direction,
                        stem + "__ID.png",
                        objectIdPng,
                        stem + "__SOURCE.png",
                        source,
                        stem + "__RECEIVER.png",
                        receiver,
                        stem + "__DOOR.png",
                        door,
                        stem + "__FORBIDDEN.png",
                        forbidden,
                        knownPixels,
                        unsupportedPixels);
                }
                finally
                {
                    if (command != null)
                        command.Dispose();
                    RenderTexture.active = originalActive;
                    camera.aspect = originalAspect;
                    camera.projectionMatrix = originalProjection;
                    GL.sRGBWrite = originalSrgb;
                    if (readback != null)
                        Object.DestroyImmediate(readback);
                }
            }

            private byte[] EncodeSemanticMask(
                Color32[] pixels,
                DirectionSemantic direction,
                SemanticMask requested,
                out int knownPixelCount,
                out int unsupportedPixelCount)
            {
                if (pixels == null || pixels.Length != CaptureWidth * CaptureHeight)
                    throw new InvalidOperationException("Unexpected object-ID readback size.");
                var output = new Color32[pixels.Length];
                knownPixelCount = 0;
                unsupportedPixelCount = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 pixel = pixels[i];
                    if (pixel.a == 0)
                    {
                        output[i] = new Color32(0, 0, 0, 255);
                        continue;
                    }

                    int id = DecodeStableObjectId(pixel);
                    bool unsupported = id == UnsupportedObjectId;
                    bool foreground = false;
                    if (unsupported)
                    {
                        unsupportedPixelCount++;
                        foreground = requested == SemanticMask.Forbidden;
                    }
                    else if (byStableId.TryGetValue(id, out MaskRendererInfo info))
                    {
                        knownPixelCount++;
                        switch (requested)
                        {
                            case SemanticMask.Source:
                                foreground = !info.Entry.IsDoor &&
                                             info.Entry.Role == direction.Source;
                                break;
                            case SemanticMask.Receiver:
                                foreground = !info.Entry.IsDoor &&
                                             info.Entry.Role == direction.Receiver;
                                break;
                            case SemanticMask.Door:
                                foreground = info.Entry.IsDoor;
                                break;
                            case SemanticMask.Forbidden:
                                foreground = !info.Entry.IsDoor &&
                                             info.Entry.Role == direction.Source;
                                break;
                        }
                    }
                    else
                    {
                        unsupportedPixelCount++;
                        foreground = requested == SemanticMask.Forbidden;
                    }
                    output[i] = foreground
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(0, 0, 0, 255);
                }

                Texture2D texture = null;
                try
                {
                    texture = new Texture2D(
                        CaptureWidth,
                        CaptureHeight,
                        TextureFormat.RGBA32,
                        false,
                        true)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    texture.SetPixels32(output);
                    texture.Apply(false, false);
                    byte[] png = ImageConversion.EncodeToPNG(texture);
                    RequireBytes(png, "semantic mask PNG");
                    return png;
                }
                finally
                {
                    if (texture != null)
                        Object.DestroyImmediate(texture);
                }
            }

            private static bool ShouldDraw(Renderer renderer, Camera camera)
            {
                if (renderer == null || !renderer.enabled || renderer.forceRenderingOff ||
                    !renderer.gameObject.activeInHierarchy ||
                    renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                {
                    return false;
                }
                return (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0;
            }

            private static MaskRendererInfo Classify(RendererEntry entry)
            {
                string stableKey = entry.Role + "|" + entry.StablePath + "|" +
                                   entry.ComponentOrdinal.ToString(CultureInfo.InvariantCulture);
                if (!entry.SourceEnabled || entry.Renderer.forceRenderingOff ||
                    entry.Renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                {
                    return new MaskRendererInfo(
                        entry,
                        stableKey,
                        true,
                        string.Empty,
                        CullMode.Back);
                }

                if (entry.Mesh == null)
                    return Unsupported(entry, stableKey, "missing Mesh");
                if (entry.AdditionalVertexStreams != null)
                    return Unsupported(entry, stableKey, "additionalVertexStreams present");
                if (entry.Mesh.blendShapeCount != 0)
                    return Unsupported(entry, stableKey, "blend shapes present");
                if (BelongsToAnyLod(entry.Renderer))
                    return Unsupported(entry, stableKey, "LODGroup selection is not reproduced");

                Material[] materials = entry.SourceMaterials;
                if (materials.Length < entry.Mesh.subMeshCount)
                    return Unsupported(entry, stableKey, "material slots are fewer than submeshes");
                CullMode? sharedCull = null;
                for (int i = 0; i < entry.Mesh.subMeshCount; i++)
                {
                    Material material = materials[i];
                    string reason = ClassifyMaterial(material, out CullMode cull);
                    if (!string.IsNullOrEmpty(reason))
                        return Unsupported(entry, stableKey, "submesh " + i + ": " + reason);
                    if (sharedCull.HasValue && sharedCull.Value != cull)
                        return Unsupported(entry, stableKey, "submesh cull modes differ");
                    sharedCull = cull;
                }
                return new MaskRendererInfo(
                    entry,
                    stableKey,
                    true,
                    string.Empty,
                    sharedCull ?? CullMode.Back);
            }

            private static string ClassifyMaterial(Material material, out CullMode cull)
            {
                cull = CullMode.Back;
                if (material == null || material.shader == null)
                    return "null material/shader";
                string shaderName = material.shader.name;
                if (!string.Equals(shaderName, "Universal Render Pipeline/Lit", StringComparison.Ordinal) &&
                    !string.Equals(shaderName, "Universal Render Pipeline/Simple Lit", StringComparison.Ordinal) &&
                    !string.Equals(shaderName, "Universal Render Pipeline/Baked Lit", StringComparison.Ordinal) &&
                    !string.Equals(shaderName, "Universal Render Pipeline/Unlit", StringComparison.Ordinal))
                {
                    return "unsupported shader " + shaderName;
                }
                string renderType = material.GetTag("RenderType", false, string.Empty);
                if (material.renderQueue >= (int)RenderQueue.AlphaTest ||
                    string.Equals(renderType, "Transparent", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(renderType, "TransparentCutout", StringComparison.OrdinalIgnoreCase) ||
                    material.IsKeywordEnabled("_ALPHATEST_ON") ||
                    material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
                {
                    return "transparent/alpha-test silhouette";
                }

                float rawCull = (float)CullMode.Back;
                if (material.HasProperty("_Cull"))
                    rawCull = material.GetFloat("_Cull");
                else if (material.HasProperty("_CullMode"))
                    rawCull = material.GetFloat("_CullMode");
                else if (material.HasProperty("_CullModeForward"))
                    rawCull = material.GetFloat("_CullModeForward");
                int rounded = Mathf.RoundToInt(rawCull);
                if (!IsFinite(rawCull) || rounded < (int)CullMode.Off ||
                    rounded > (int)CullMode.Back ||
                    Mathf.Abs(rawCull - rounded) > 0.001f)
                {
                    return "invalid cull mode";
                }
                cull = (CullMode)rounded;
                return string.Empty;
            }

            private static bool BelongsToAnyLod(Renderer renderer)
            {
                LODGroup[] groups = renderer.GetComponentsInParent<LODGroup>(true);
                for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                {
                    LOD[] lods = groups[groupIndex].GetLODs();
                    for (int lodIndex = 0; lodIndex < lods.Length; lodIndex++)
                    {
                        Renderer[] renderers = lods[lodIndex].renderers ?? Array.Empty<Renderer>();
                        for (int rendererIndex = 0;
                             rendererIndex < renderers.Length;
                             rendererIndex++)
                        {
                            if (renderers[rendererIndex] == renderer)
                                return true;
                        }
                    }
                }
                return false;
            }

            private static MaskRendererInfo Unsupported(
                RendererEntry entry,
                string stableKey,
                string reason)
            {
                return new MaskRendererInfo(
                    entry,
                    stableKey,
                    false,
                    reason,
                    CullMode.Off);
            }

            private static int CompareRendererEntries(RendererEntry left, RendererEntry right)
            {
                int result = left.Role.CompareTo(right.Role);
                if (result != 0)
                    return result;
                result = string.Compare(left.StablePath, right.StablePath, StringComparison.Ordinal);
                if (result != 0)
                    return result;
                return left.ComponentOrdinal.CompareTo(right.ComponentOrdinal);
            }

            private static Material CreateMaskMaterial(
                Shader shader,
                string name,
                Color objectId,
                bool unsupported,
                CullMode cull)
            {
                var material = new Material(shader)
                {
                    name = name,
                    hideFlags = HideFlags.HideAndDontSave
                };
                material.SetColor(ObjectIdColorId, objectId);
                material.SetFloat(UnsupportedId, unsupported ? 1f : 0f);
                material.SetFloat(CullId, (float)cull);
                return material;
            }

            private static RenderTexture CreateMaskTarget(
                string name,
                RenderTextureFormat format,
                int depth)
            {
                var target = new RenderTexture(
                    CaptureWidth,
                    CaptureHeight,
                    depth,
                    format,
                    RenderTextureReadWrite.Linear)
                {
                    name = name,
                    hideFlags = HideFlags.HideAndDontSave,
                    antiAliasing = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                if (!target.Create())
                {
                    Object.DestroyImmediate(target);
                    throw new InvalidOperationException("Unable to create mask target: " + name);
                }
                return target;
            }

            private static Color EncodeStableObjectId(int id)
            {
                if (id <= 0 || id >= UnsupportedObjectId)
                    throw new ArgumentOutOfRangeException(nameof(id));
                return new Color(
                    (id & 0xFF) / 255f,
                    ((id >> 8) & 0xFF) / 255f,
                    ((id >> 16) & 0xFF) / 255f,
                    1f);
            }

            private static int DecodeStableObjectId(Color32 color)
            {
                return color.r | (color.g << 8) | (color.b << 16);
            }

            public void Dispose()
            {
                foreach (Material material in materials.Values)
                {
                    if (material != null)
                        Object.DestroyImmediate(material);
                }
                materials.Clear();
                if (unsupportedMaterial != null)
                    Object.DestroyImmediate(unsupportedMaterial);
                DestroyRenderTexture(objectIdTarget);
                DestroyRenderTexture(normalTarget);
                DestroyRenderTexture(depthTarget);
            }

            private static void DestroyRenderTexture(RenderTexture target)
            {
                if (target == null)
                    return;
                target.Release();
                Object.DestroyImmediate(target);
            }

            private enum SemanticMask
            {
                Source,
                Receiver,
                Door,
                Forbidden
            }
        }

        private sealed class MaskRendererInfo
        {
            public readonly RendererEntry Entry;
            public readonly string StableKey;
            public readonly bool Supported;
            public readonly string Reason;
            public readonly CullMode CullMode;
            public int StableObjectId => Entry.StableObjectId;

            public MaskRendererInfo(
                RendererEntry entry,
                string stableKey,
                bool supported,
                string reason,
                CullMode cullMode)
            {
                Entry = entry;
                StableKey = stableKey;
                Supported = supported;
                Reason = reason;
                CullMode = cullMode;
            }
        }

        private sealed class MaskEvidence
        {
            public readonly DoorPose Door;
            public readonly string CameraName;
            public readonly DirectionSemantic Direction;
            public readonly string ObjectIdFilename;
            public readonly byte[] ObjectIdBytes;
            public readonly string ObjectIdSha256;
            public readonly string SourceFilename;
            public readonly byte[] SourceBytes;
            public readonly string SourceSha256;
            public readonly string ReceiverFilename;
            public readonly byte[] ReceiverBytes;
            public readonly string ReceiverSha256;
            public readonly string DoorFilename;
            public readonly byte[] DoorBytes;
            public readonly string DoorSha256;
            public readonly string ForbiddenFilename;
            public readonly byte[] ForbiddenBytes;
            public readonly string ForbiddenSha256;
            public readonly int KnownPixelCount;
            public readonly int UnsupportedPixelCount;

            public MaskEvidence(
                DoorPose door,
                string cameraName,
                DirectionSemantic direction,
                string objectIdFilename,
                byte[] objectIdBytes,
                string sourceFilename,
                byte[] sourceBytes,
                string receiverFilename,
                byte[] receiverBytes,
                string doorFilename,
                byte[] doorBytes,
                string forbiddenFilename,
                byte[] forbiddenBytes,
                int knownPixelCount,
                int unsupportedPixelCount)
            {
                Door = door;
                CameraName = cameraName;
                Direction = direction;
                ObjectIdFilename = objectIdFilename;
                ObjectIdBytes = objectIdBytes;
                ObjectIdSha256 = ComputeSha256(objectIdBytes);
                SourceFilename = sourceFilename;
                SourceBytes = sourceBytes;
                SourceSha256 = ComputeSha256(sourceBytes);
                ReceiverFilename = receiverFilename;
                ReceiverBytes = receiverBytes;
                ReceiverSha256 = ComputeSha256(receiverBytes);
                DoorFilename = doorFilename;
                DoorBytes = doorBytes;
                DoorSha256 = ComputeSha256(doorBytes);
                ForbiddenFilename = forbiddenFilename;
                ForbiddenBytes = forbiddenBytes;
                ForbiddenSha256 = ComputeSha256(forbiddenBytes);
                KnownPixelCount = knownPixelCount;
                UnsupportedPixelCount = unsupportedPixelCount;
            }

            public void Write(string absoluteFolder, List<WrittenFile> expectedFiles)
            {
                WriteBytes(absoluteFolder, ObjectIdFilename, ObjectIdBytes);
                WriteBytes(absoluteFolder, SourceFilename, SourceBytes);
                WriteBytes(absoluteFolder, ReceiverFilename, ReceiverBytes);
                WriteBytes(absoluteFolder, DoorFilename, DoorBytes);
                WriteBytes(absoluteFolder, ForbiddenFilename, ForbiddenBytes);
                expectedFiles.Add(new WrittenFile(ObjectIdFilename, ObjectIdSha256, true));
                expectedFiles.Add(new WrittenFile(SourceFilename, SourceSha256, true));
                expectedFiles.Add(new WrittenFile(ReceiverFilename, ReceiverSha256, true));
                expectedFiles.Add(new WrittenFile(DoorFilename, DoorSha256, true));
                expectedFiles.Add(new WrittenFile(ForbiddenFilename, ForbiddenSha256, true));
            }
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
                        "K1Proxy_Presentation_Render",
                        24,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.sRGB,
                        msaa);
                    presentationResolve = CreateTarget(
                        "K1Proxy_Presentation_Resolve",
                        0,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.sRGB,
                        1);
                    hdrRender = CreateTarget(
                        "K1Proxy_HDR_Render",
                        24,
                        RenderTextureFormat.ARGBHalf,
                        RenderTextureReadWrite.Linear,
                        1);
                    hdrResolve = CreateTarget(
                        "K1Proxy_HDR_Resolve",
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
                    throw new InvalidOperationException("Unable to create target: " + name);
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
            private readonly LightmapsMode lightmapsMode;
            private readonly RenderTexture activeRenderTexture;
            private readonly bool srgbWrite;
            private readonly Matrix4x4 maskGpuVp;
            private readonly Matrix4x4 maskDoorwayWorldToLocal;

            private GlobalRenderSnapshot(
                LightmapData[] lightmaps,
                LightmapsMode lightmapsMode,
                RenderTexture activeRenderTexture,
                bool srgbWrite,
                Matrix4x4 maskGpuVp,
                Matrix4x4 maskDoorwayWorldToLocal)
            {
                this.lightmaps = lightmaps;
                this.lightmapsMode = lightmapsMode;
                this.activeRenderTexture = activeRenderTexture;
                this.srgbWrite = srgbWrite;
                this.maskGpuVp = maskGpuVp;
                this.maskDoorwayWorldToLocal = maskDoorwayWorldToLocal;
            }

            public static GlobalRenderSnapshot Capture()
            {
                LightmapData[] current = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
                return new GlobalRenderSnapshot(
                    (LightmapData[])current.Clone(),
                    LightmapSettings.lightmapsMode,
                    RenderTexture.active,
                    GL.sRGBWrite,
                    Shader.GetGlobalMatrix("_StageA_CaptureGpuVP"),
                    Shader.GetGlobalMatrix("_StageA_DoorwayWorldToLocal"));
            }

            public void ApplyNoLightmaps()
            {
                LightmapSettings.lightmaps = Array.Empty<LightmapData>();
                LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
            }

            public void Restore()
            {
                LightmapSettings.lightmaps = lightmaps;
                LightmapSettings.lightmapsMode = lightmapsMode;
                RenderTexture.active = activeRenderTexture;
                GL.sRGBWrite = srgbWrite;
                Shader.SetGlobalMatrix("_StageA_CaptureGpuVP", maskGpuVp);
                Shader.SetGlobalMatrix(
                    "_StageA_DoorwayWorldToLocal",
                    maskDoorwayWorldToLocal);
            }

            public bool AreLightmapsRestored()
            {
                LightmapData[] current = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
                if (current.Length != lightmaps.Length)
                    return false;
                for (int i = 0; i < current.Length; i++)
                {
                    if (!ReferenceEquals(current[i], lightmaps[i]))
                        return false;
                }
                return true;
            }

            public bool IsModeRestored()
            {
                return LightmapSettings.lightmapsMode == lightmapsMode;
            }

            public bool IsRenderTextureActiveRestored()
            {
                return RenderTexture.active == activeRenderTexture;
            }

            public bool IsSrgbWriteRestored()
            {
                return GL.sRGBWrite == srgbWrite;
            }

            public bool AreMaskGlobalsRestored()
            {
                return Shader.GetGlobalMatrix("_StageA_CaptureGpuVP") == maskGpuVp &&
                       Shader.GetGlobalMatrix("_StageA_DoorwayWorldToLocal") ==
                       maskDoorwayWorldToLocal;
            }

            public void AssertRestored()
            {
                if (!AreLightmapsRestored() || !IsModeRestored() ||
                    !IsRenderTextureActiveRestored() || !IsSrgbWriteRestored() ||
                    !AreMaskGlobalsRestored())
                {
                    throw new InvalidOperationException(
                        "Global lightmap/render/mask shader state was not restored.");
                }
            }
        }

        private sealed class SelectionSnapshot
        {
            private readonly Object[] objects;
            private readonly Object activeObject;

            private SelectionSnapshot(Object[] objects, Object activeObject)
            {
                this.objects = objects;
                this.activeObject = activeObject;
            }

            public static SelectionSnapshot Capture()
            {
                return new SelectionSnapshot(Selection.objects, Selection.activeObject);
            }

            public void RestoreIfChanged()
            {
                if (MatchesCurrent())
                    return;
                Selection.objects = objects;
                Selection.activeObject = activeObject;
            }

            public void AssertRestored()
            {
                if (!MatchesCurrent())
                    throw new InvalidOperationException("User selection was not restored.");
            }

            private bool MatchesCurrent()
            {
                Object[] current = Selection.objects;
                if (Selection.activeObject != activeObject || current.Length != objects.Length)
                    return false;
                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i] != objects[i])
                        return false;
                }
                return true;
            }
        }

        private sealed class SourceSceneSnapshot
        {
            private readonly Scene scene;
            private readonly GameObject root;
            private readonly SourceRendererEntry[] renderers;
            private readonly SourceLightEntry[] lights;
            private readonly SourceCameraEntry[] cameras;
            private readonly SourceEndpointEntry[] endpoints;
            private readonly DungeonPortalTransportConnection connection;
            private readonly bool connectionEnabled;
            private readonly Transform doorRoot;
            private readonly Transform doorLeaf;
            private readonly Vector3 doorRootLocalPosition;
            private readonly Quaternion doorRootLocalRotation;
            private readonly Quaternion doorLeafLocalRotation;

            private SourceSceneSnapshot(
                Scene scene,
                GameObject root,
                SourceRendererEntry[] renderers,
                SourceLightEntry[] lights,
                SourceCameraEntry[] cameras,
                SourceEndpointEntry[] endpoints,
                DungeonPortalTransportConnection connection,
                Transform doorRoot,
                Transform doorLeaf)
            {
                this.scene = scene;
                this.root = root;
                this.renderers = renderers;
                this.lights = lights;
                this.cameras = cameras;
                this.endpoints = endpoints;
                this.connection = connection;
                connectionEnabled = connection.enabled;
                this.doorRoot = doorRoot;
                this.doorLeaf = doorLeaf;
                doorRootLocalPosition = doorRoot.localPosition;
                doorRootLocalRotation = doorRoot.localRotation;
                doorLeafLocalRotation = doorLeaf.localRotation;
            }

            public static SourceSceneSnapshot Capture(Scene scene, GameObject root)
            {
                Renderer[] sourceRenderers = root.GetComponentsInChildren<Renderer>(true);
                Light[] sourceLights = root.GetComponentsInChildren<Light>(true);
                Camera[] sourceCameras = root.GetComponentsInChildren<Camera>(true);
                DungeonPortalEndpoint[] sourceEndpoints =
                    root.GetComponentsInChildren<DungeonPortalEndpoint>(true);
                DungeonPortalTransportConnection[] connections =
                    root.GetComponentsInChildren<DungeonPortalTransportConnection>(true);
                if (sourceRenderers.Length != ExpectedRendererCount ||
                    sourceLights.Length != ExpectedProductionLightCount ||
                    sourceCameras.Length != ExpectedCameraCount ||
                    sourceEndpoints.Length != ExpectedEndpointCount || connections.Length != 1)
                {
                    throw new InvalidOperationException("Source scene count contract changed.");
                }
                if (connections[0].enabled)
                    throw new InvalidOperationException("Source connection must remain disabled.");

                var rendererEntries = new SourceRendererEntry[sourceRenderers.Length];
                for (int i = 0; i < sourceRenderers.Length; i++)
                    rendererEntries[i] = SourceRendererEntry.Capture(sourceRenderers[i]);
                var lightEntries = new SourceLightEntry[sourceLights.Length];
                for (int i = 0; i < sourceLights.Length; i++)
                    lightEntries[i] = SourceLightEntry.Capture(sourceLights[i]);
                var cameraEntries = new SourceCameraEntry[sourceCameras.Length];
                for (int i = 0; i < sourceCameras.Length; i++)
                    cameraEntries[i] = SourceCameraEntry.Capture(sourceCameras[i]);
                var endpointEntries = new SourceEndpointEntry[sourceEndpoints.Length];
                for (int i = 0; i < sourceEndpoints.Length; i++)
                    endpointEntries[i] = SourceEndpointEntry.Capture(sourceEndpoints[i]);

                Transform productionRooms = FindUniqueDescendant(
                    root.transform,
                    ProductionRoomsRootName,
                    true);
                Transform startRoom = FindUniqueDescendant(
                    productionRooms,
                    StartRoomName,
                    true);
                Transform doorRoot = startRoom.Find(AddedDoorRelativePath);
                Transform doorLeaf = doorRoot != null ? doorRoot.Find(DoorLeafName) : null;
                if (doorRoot == null || doorLeaf == null)
                    throw new InvalidOperationException("Source moving door is missing.");

                return new SourceSceneSnapshot(
                    scene,
                    root,
                    rendererEntries,
                    lightEntries,
                    cameraEntries,
                    endpointEntries,
                    connections[0],
                    doorRoot,
                    doorLeaf);
            }

            public void AssertUnchanged()
            {
                if (!scene.IsValid() || !scene.isLoaded || scene.isDirty || root == null ||
                    root.gameObject.scene != scene || !root.activeSelf ||
                    root.transform.parent != null)
                {
                    throw new InvalidOperationException("Source validation hierarchy changed.");
                }
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].AssertUnchanged();
                for (int i = 0; i < lights.Length; i++)
                    lights[i].AssertUnchanged();
                for (int i = 0; i < cameras.Length; i++)
                    cameras[i].AssertUnchanged();
                for (int i = 0; i < endpoints.Length; i++)
                    endpoints[i].AssertUnchanged();
                if (connection == null || connection.enabled != connectionEnabled ||
                    connectionEnabled || doorRoot == null || doorLeaf == null ||
                    doorRoot.localPosition != doorRootLocalPosition ||
                    doorRoot.localRotation != doorRootLocalRotation ||
                    doorLeaf.localRotation != doorLeafLocalRotation)
                {
                    throw new InvalidOperationException("Source connection/door state changed.");
                }
            }
        }

        private sealed class SourceRendererEntry
        {
            private readonly Renderer renderer;
            private readonly bool enabled;
            private readonly bool forceRenderingOff;
            private readonly int lightmapIndex;
            private readonly int realtimeLightmapIndex;
            private readonly Vector4 lightmapScaleOffset;
            private readonly Vector4 realtimeLightmapScaleOffset;
            private readonly LightProbeUsage lightProbeUsage;
            private readonly ReflectionProbeUsage reflectionProbeUsage;
            private readonly uint renderingLayerMask;
            private readonly int gameObjectLayer;
            private readonly bool hadPropertyBlock;
            private readonly MeshFilter meshFilter;
            private readonly Mesh mesh;
            private readonly MeshRenderer meshRenderer;
            private readonly Mesh additionalStreams;
            private readonly MaterialState[] materials;

            private SourceRendererEntry(Renderer renderer)
            {
                this.renderer = renderer;
                enabled = renderer.enabled;
                forceRenderingOff = renderer.forceRenderingOff;
                lightmapIndex = renderer.lightmapIndex;
                realtimeLightmapIndex = renderer.realtimeLightmapIndex;
                lightmapScaleOffset = renderer.lightmapScaleOffset;
                realtimeLightmapScaleOffset = renderer.realtimeLightmapScaleOffset;
                lightProbeUsage = renderer.lightProbeUsage;
                reflectionProbeUsage = renderer.reflectionProbeUsage;
                renderingLayerMask = renderer.renderingLayerMask;
                gameObjectLayer = renderer.gameObject.layer;
                hadPropertyBlock = renderer.HasPropertyBlock();
                meshRenderer = renderer as MeshRenderer;
                meshFilter = renderer.GetComponent<MeshFilter>();
                mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                additionalStreams =
                    meshRenderer != null ? meshRenderer.additionalVertexStreams : null;
                Material[] shared = renderer.sharedMaterials;
                materials = new MaterialState[shared.Length];
                for (int i = 0; i < shared.Length; i++)
                    materials[i] = MaterialState.Capture(shared[i]);
            }

            public static SourceRendererEntry Capture(Renderer renderer)
            {
                return new SourceRendererEntry(renderer);
            }

            public void AssertUnchanged()
            {
                if (renderer == null || renderer.enabled != enabled ||
                    renderer.forceRenderingOff != forceRenderingOff ||
                    renderer.lightmapIndex != lightmapIndex ||
                    renderer.realtimeLightmapIndex != realtimeLightmapIndex ||
                    renderer.lightmapScaleOffset != lightmapScaleOffset ||
                    renderer.realtimeLightmapScaleOffset != realtimeLightmapScaleOffset ||
                    renderer.lightProbeUsage != lightProbeUsage ||
                    renderer.reflectionProbeUsage != reflectionProbeUsage ||
                    renderer.renderingLayerMask != renderingLayerMask ||
                    renderer.gameObject.layer != gameObjectLayer ||
                    renderer.HasPropertyBlock() != hadPropertyBlock ||
                    meshFilter == null || meshFilter.sharedMesh != mesh ||
                    meshRenderer == null ||
                    meshRenderer.additionalVertexStreams != additionalStreams)
                {
                    throw new InvalidOperationException("Source Renderer changed.");
                }
                Material[] current = renderer.sharedMaterials;
                if (current.Length != materials.Length)
                    throw new InvalidOperationException("Source material slots changed.");
                for (int i = 0; i < current.Length; i++)
                    materials[i].AssertUnchanged(current[i], true);
            }
        }

        private sealed class SourceLightEntry
        {
            private readonly Light light;
            private readonly bool enabled;
            private readonly LightmapBakeType bakeType;
            private readonly float bounceIntensity;
            private readonly float intensity;
            private readonly Color color;
            private readonly int cullingMask;
            private readonly int renderingLayerMask;
            private readonly Component[] components;

            private SourceLightEntry(Light light)
            {
                this.light = light;
                enabled = light.enabled;
                bakeType = light.lightmapBakeType;
                bounceIntensity = light.bounceIntensity;
                intensity = light.intensity;
                color = light.color;
                cullingMask = light.cullingMask;
                renderingLayerMask = light.renderingLayerMask;
                components = light.GetComponents<Component>();
            }

            public static SourceLightEntry Capture(Light light)
            {
                return new SourceLightEntry(light);
            }

            public void AssertUnchanged()
            {
                if (light == null || light.enabled != enabled ||
                    light.lightmapBakeType != bakeType ||
                    !Mathf.Approximately(light.bounceIntensity, bounceIntensity) ||
                    !Mathf.Approximately(light.intensity, intensity) || light.color != color ||
                    light.cullingMask != cullingMask ||
                    light.renderingLayerMask != renderingLayerMask)
                {
                    throw new InvalidOperationException("Source Light changed.");
                }
                Component[] current = light.GetComponents<Component>();
                if (current.Length != components.Length)
                    throw new InvalidOperationException("Source Light components changed.");
                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i] != components[i])
                        throw new InvalidOperationException("Source Light component order changed.");
                }
            }
        }

        private sealed class SourceCameraEntry
        {
            private readonly Camera camera;
            private readonly bool enabled;
            private readonly RenderTexture targetTexture;
            private readonly Vector3 position;
            private readonly Quaternion rotation;
            private readonly int cullingMask;
            private readonly float fieldOfView;
            private readonly float nearClip;
            private readonly float farClip;
            private readonly bool allowHdr;
            private readonly bool allowMsaa;

            private SourceCameraEntry(Camera camera)
            {
                this.camera = camera;
                enabled = camera.enabled;
                targetTexture = camera.targetTexture;
                position = camera.transform.position;
                rotation = camera.transform.rotation;
                cullingMask = camera.cullingMask;
                fieldOfView = camera.fieldOfView;
                nearClip = camera.nearClipPlane;
                farClip = camera.farClipPlane;
                allowHdr = camera.allowHDR;
                allowMsaa = camera.allowMSAA;
                if (enabled || targetTexture != null)
                    throw new InvalidOperationException("Source fixed Camera must be disabled.");
                FindUniqueUrpCameraData(camera);
            }

            public static SourceCameraEntry Capture(Camera camera)
            {
                return new SourceCameraEntry(camera);
            }

            public void AssertUnchanged()
            {
                if (camera == null || camera.enabled != enabled ||
                    camera.targetTexture != targetTexture ||
                    camera.transform.position != position ||
                    camera.transform.rotation != rotation ||
                    camera.cullingMask != cullingMask ||
                    !Mathf.Approximately(camera.fieldOfView, fieldOfView) ||
                    !Mathf.Approximately(camera.nearClipPlane, nearClip) ||
                    !Mathf.Approximately(camera.farClipPlane, farClip) ||
                    camera.allowHDR != allowHdr || camera.allowMSAA != allowMsaa)
                {
                    throw new InvalidOperationException("Source fixed Camera changed.");
                }
            }
        }

        private sealed class SourceEndpointEntry
        {
            private readonly DungeonPortalEndpoint endpoint;
            private readonly bool enabled;
            private readonly DungeonPortalEndpointProfile profile;
            private readonly Transform doorwayFrame;
            private readonly int childCount;

            private SourceEndpointEntry(DungeonPortalEndpoint endpoint)
            {
                this.endpoint = endpoint;
                enabled = endpoint.enabled;
                profile = endpoint.Profile;
                doorwayFrame = endpoint.DoorwayFrame;
                childCount = endpoint.transform.childCount;
            }

            public static SourceEndpointEntry Capture(DungeonPortalEndpoint endpoint)
            {
                if (endpoint == null || endpoint.Profile == null || endpoint.DoorwayFrame == null)
                    throw new InvalidOperationException("Source endpoint is not configured.");
                return new SourceEndpointEntry(endpoint);
            }

            public void AssertUnchanged()
            {
                if (endpoint == null || endpoint.enabled != enabled ||
                    endpoint.Profile != profile || endpoint.DoorwayFrame != doorwayFrame ||
                    endpoint.transform.childCount != childCount)
                {
                    throw new InvalidOperationException("Source endpoint changed.");
                }
            }
        }

        private sealed class CaptureIdentity
        {
            private readonly Dictionary<string, AssetIdentity> assets;
            public readonly int QualityLevel;
            public readonly string QualityName;
            public readonly string RenderPipelinePath;
            public readonly string RenderPipelineDependencyHash;
            public readonly string LightingSettingsPath;
            public readonly string LightingSettingsDependencyHash;
            public readonly bool LightingSettingsRealtimeGi;

            private CaptureIdentity(
                Dictionary<string, AssetIdentity> assets,
                int qualityLevel,
                string qualityName,
                string renderPipelinePath,
                string renderPipelineDependencyHash,
                string lightingSettingsPath,
                string lightingSettingsDependencyHash,
                bool lightingSettingsRealtimeGi)
            {
                this.assets = assets;
                QualityLevel = qualityLevel;
                QualityName = qualityName;
                RenderPipelinePath = renderPipelinePath;
                RenderPipelineDependencyHash = renderPipelineDependencyHash;
                LightingSettingsPath = lightingSettingsPath;
                LightingSettingsDependencyHash = lightingSettingsDependencyHash;
                LightingSettingsRealtimeGi = lightingSettingsRealtimeGi;
            }

            public static CaptureIdentity Capture()
            {
                string[] paths =
                {
                    ToolSourcePath,
                    ValidationScenePath,
                    StartProfilePath,
                    AdministrativeProfilePath,
                    ObjectIdShaderPath
                };
                var assets = new Dictionary<string, AssetIdentity>(StringComparer.Ordinal);
                for (int i = 0; i < paths.Length; i++)
                    assets.Add(paths[i], AssetIdentity.Capture(paths[i]));

                RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
                string pipelinePath = pipeline != null
                    ? NormalizePath(AssetDatabase.GetAssetPath(pipeline))
                    : string.Empty;
                string pipelineHash = string.IsNullOrEmpty(pipelinePath)
                    ? string.Empty
                    : AssetDatabase.GetAssetDependencyHash(pipelinePath).ToString();
                LightingSettings settings = Lightmapping.lightingSettings;
                string lightingPath = settings != null
                    ? NormalizePath(AssetDatabase.GetAssetPath(settings))
                    : string.Empty;
                string lightingHash = string.IsNullOrEmpty(lightingPath)
                    ? string.Empty
                    : AssetDatabase.GetAssetDependencyHash(lightingPath).ToString();
                int quality = QualitySettings.GetQualityLevel();
                string[] names = QualitySettings.names;
                string qualityName = quality >= 0 && quality < names.Length
                    ? names[quality]
                    : string.Empty;
                return new CaptureIdentity(
                    assets,
                    quality,
                    qualityName,
                    pipelinePath,
                    pipelineHash,
                    lightingPath,
                    lightingHash,
                    settings != null && settings.realtimeGI);
            }

            public void AppendAsset(StringBuilder builder, string prefix, string path)
            {
                AssetIdentity identity = assets[path];
                builder.AppendLine(prefix + ".path=" + identity.Path);
                builder.AppendLine(prefix + ".guid=" + identity.Guid);
                builder.AppendLine(prefix + ".dependencyHash=" + identity.DependencyHash);
                builder.AppendLine(prefix + ".sha256=" + identity.Sha256);
            }
        }

        private readonly struct AssetIdentity
        {
            public readonly string Path;
            public readonly string Guid;
            public readonly string DependencyHash;
            public readonly string Sha256;

            private AssetIdentity(
                string path,
                string guid,
                string dependencyHash,
                string sha256)
            {
                Path = path;
                Guid = guid;
                DependencyHash = dependencyHash;
                Sha256 = sha256;
            }

            public static AssetIdentity Capture(string path)
            {
                if (!File.Exists(AssetPathToAbsolutePath(path)))
                    throw new InvalidOperationException("Required source file is missing: " + path);
                return new AssetIdentity(
                    path,
                    AssetDatabase.AssetPathToGUID(path),
                    AssetDatabase.GetAssetDependencyHash(path).ToString(),
                    ComputeFileSha256(path));
            }
        }

        private readonly struct WrittenFile
        {
            public readonly string Filename;
            public readonly string Sha256;
            public readonly bool IsPng;

            public WrittenFile(string filename, string sha256, bool isPng)
            {
                Filename = filename;
                Sha256 = sha256;
                IsPng = isPng;
            }
        }
    }
}
