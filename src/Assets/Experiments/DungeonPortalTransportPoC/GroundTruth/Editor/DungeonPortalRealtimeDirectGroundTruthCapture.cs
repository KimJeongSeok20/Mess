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
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DungeonPortalTransportPoC.GroundTruth
{
    /// <summary>
    /// Captures a deliberately narrow reference made from the real Start/Admin
    /// production Light components running as realtime, direct-only lights.
    ///
    /// This is not a baked/realtime equivalence verdict. The preview clone has no
    /// baked lightmaps, realtime lightmaps, light-probe SH, or reflection-probe
    /// sampling. Realtime light bounce is forced to zero. The P0/P0 images remain
    /// the explicit fixed-environment/emission/ignored-light baseline for deltas.
    /// </summary>
    public static class DungeonPortalRealtimeDirectGroundTruthCapture
    {
        public const string Status = "REALTIME_DIRECT_GT_ONLY";

        private const string ToolSourcePath =
            "Assets/Experiments/DungeonPortalTransportPoC/GroundTruth/Editor/" +
            "DungeonPortalRealtimeDirectGroundTruthCapture.cs";
        private const string ValidationScenePath =
            "Assets/Experiments/DungeonPortalTransportPoC/Scenes/" +
            "Start_Admin_PortalTransportValidation.unity";
        private const string StartRoomPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/StartRoom_R000.prefab";
        private const string AdministrativeRoomPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/" +
            "AdminstrativeSegregation_R000.prefab";
        private const string DoorPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/SomePicees/" +
            "Door_SM_A_Door_Placement.prefab";
        private const string EvidenceRoot =
            "Assets/Experiments/DungeonPortalTransportPoC/Evidence/GroundTruth/RealtimeDirect";

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
        private const string DoorSplitRootName = "DungeonDoorProbeSplitRoot";
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
        private const int ExpectedTotalLightCount =
            ExpectedStartLightCount + ExpectedAdministrativeLightCount;
        private const int ExpectedStartIgnoredLightCount = 0;
        private const int ExpectedAdministrativeIgnoredLightCount = 2;
        private const int ExpectedDoorRendererCount = 4;
        private const int ExpectedDoorEnabledRendererCount = 3;
        private const int ExpectedCameraCount = 2;
        private const int CaptureWidth = 1920;
        private const int CaptureHeight = 1080;

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

        private static readonly Vector3 ExpectedDoorRootLocalPosition =
            new Vector3(-0.5820876f, 0f, -0.06643671f);

        [MenuItem(
            "Tools/Dungeon/Lighting/Portal Transport PoC/" +
            "Capture REALTIME DIRECT Ground Truth (Edit Mode)")]
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
            Scene activeScene = ValidateEditorPreconditions();
            GameObject sourceRoot = FindUniqueRoot(activeScene, ValidationRootName);
            ValidateSourceRootShape(sourceRoot);

            SourceSceneSnapshot sourceSnapshot = SourceSceneSnapshot.Capture(
                activeScene,
                sourceRoot);
            SelectionSnapshot selectionSnapshot = SelectionSnapshot.Capture();
            GlobalRenderSnapshot globalSnapshot = GlobalRenderSnapshot.Capture();
            CaptureIdentity identity = CaptureIdentity.Capture();
            string utcTimestamp = DateTime.UtcNow.ToString(
                "yyyyMMdd'T'HHmmssfff'Z'",
                CultureInfo.InvariantCulture);
            string outputFolder = EvidenceRoot + "/" + utcTimestamp;

            Scene previewScene = default;
            RenderTargets targets = null;
            var evidence = new List<CaptureEvidence>(
                PowerStates.Length * DoorPoses.Length * CameraNames.Length);
            PreviewContract previewContract = null;
            bool restorationCompleted = false;

            try
            {
                previewScene = EditorSceneManager.NewPreviewScene();
                if (!previewScene.IsValid() || !previewScene.isLoaded ||
                    !EditorSceneManager.IsPreviewScene(previewScene))
                {
                    throw new InvalidOperationException(
                        "Unity did not create the required isolated preview scene.");
                }

                GameObject previewRoot = Object.Instantiate(sourceRoot);
                previewRoot.name = ValidationRootName + "__RealtimeDirectGroundTruthClone";
                previewRoot.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(previewRoot, previewScene);

                if (activeScene.isDirty)
                {
                    throw new InvalidOperationException(
                        "Deep cloning unexpectedly dirtied the validation scene.");
                }

                previewContract = PreviewContract.Create(previewRoot, previewScene);
                previewContract.PrepareDirectOnlyClone();
                globalSnapshot.ApplyNoBakedLightmaps();
                targets = RenderTargets.Create();

                for (int powerIndex = 0; powerIndex < PowerStates.Length; powerIndex++)
                {
                    PowerState power = PowerStates[powerIndex];
                    previewContract.Lights.ApplyPower(power);

                    for (int poseIndex = 0; poseIndex < DoorPoses.Length; poseIndex++)
                    {
                        DoorPose pose = DoorPoses[poseIndex];
                        previewContract.Door.ApplyPose(pose);
                        previewContract.AssertPreparedState(power, pose);

                        for (int cameraIndex = 0;
                             cameraIndex < previewContract.Cameras.Length;
                             cameraIndex++)
                        {
                            CaptureEvidence record = CaptureCameraPair(
                                previewContract.Cameras[cameraIndex],
                                previewScene,
                                power,
                                pose,
                                targets);
                            evidence.Add(record);
                            previewContract.AssertPreparedState(power, pose);
                        }
                    }
                }

                int expectedEvidenceCount =
                    PowerStates.Length * DoorPoses.Length * CameraNames.Length;
                if (evidence.Count != expectedEvidenceCount)
                {
                    throw new InvalidOperationException(
                        $"Expected {expectedEvidenceCount} camera-state records, " +
                        $"captured {evidence.Count}.");
                }
            }
            finally
            {
                // Every restoration stage must be attempted even when an earlier
                // cleanup stage throws. The source scene is never the mutation
                // target, but global render state, the preview scene, and user
                // selection still need independent fail-closed cleanup.
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

            if (!restorationCompleted)
                throw new InvalidOperationException("Capture restoration did not complete.");
            if (SceneManager.GetActiveScene() != activeScene)
                throw new InvalidOperationException("The active validation scene changed.");
            if (SceneManager.sceneCount != 1 || CountLoadedNonPreviewScenes() != 1)
                throw new InvalidOperationException("The loaded scene set changed during capture.");
            if (activeScene.isDirty)
                throw new InvalidOperationException("The validation scene became dirty.");

            globalSnapshot.AssertRestored();
            selectionSnapshot.AssertRestored();
            sourceSnapshot.AssertUnchanged();
            ValidateEvidenceAndAttachBaselines(evidence);

            string manifest = BuildManifest(
                utcTimestamp,
                identity,
                previewContract,
                evidence,
                globalSnapshot);

            // The first filesystem write occurs only here: after the preview scene was
            // closed and global render/lightmap state, selection, and source state were
            // restored and verified.
            WriteEvidence(outputFolder, evidence, manifest);

            if (SceneManager.GetActiveScene() != activeScene || activeScene.isDirty)
            {
                throw new InvalidOperationException(
                    "Evidence import changed the validation scene state.");
            }
            sourceSnapshot.AssertUnchanged();
            selectionSnapshot.AssertRestored();
            globalSnapshot.AssertRestored();

            return
                Status + "\n" +
                "output=" + outputFolder + "\n" +
                "stateCameraRecords=" + evidence.Count + "\n" +
                "presentationPng=" + evidence.Count + "\n" +
                "linearHdrExr=" + evidence.Count + "\n" +
                "totalFiles=" + (evidence.Count * 2 + 1) + "\n" +
                "equivalenceVerdict=false";
        }

        private static Scene ValidateEditorPreconditions()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exact clean Edit Mode is required.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Wait for compilation/import to finish.");
            if (Lightmapping.isRunning)
                throw new InvalidOperationException("Lightmapping is running.");
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32) ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.RGBA32) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf))
            {
                throw new InvalidOperationException(
                    "Required LDR/HDR render or readback formats are unavailable.");
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded ||
                EditorSceneManager.IsPreviewScene(activeScene) ||
                !string.Equals(
                    NormalizePath(activeScene.path),
                    ValidationScenePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The exact validation scene must already be the active scene: " +
                    ValidationScenePath);
            }
            if (activeScene.isDirty)
                throw new InvalidOperationException("The validation scene must be clean.");
            if (SceneManager.sceneCount != 1 || CountLoadedNonPreviewScenes() != 1)
            {
                throw new InvalidOperationException(
                    "Exactly one scene, the non-preview validation scene, may be loaded.");
            }

            return activeScene;
        }

        private static int CountLoadedNonPreviewScenes()
        {
            int count = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded &&
                    !EditorSceneManager.IsPreviewScene(scene))
                {
                    count++;
                }
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

        private static void ValidateSourceRootShape(GameObject root)
        {
            if (root == null || !root.activeInHierarchy)
                throw new InvalidOperationException("Validation root is missing or inactive.");
            if (root.transform.parent != null ||
                root.transform.childCount != ExpectedValidationRootChildren)
            {
                throw new InvalidOperationException(
                    $"Validation root must have exactly {ExpectedValidationRootChildren} " +
                    "direct children and no parent.");
            }

            FindUniqueDescendant(root.transform, ProductionRoomsRootName, true);
            FindUniqueDescendant(root.transform, PortalTransportRootName, true);
            FindUniqueDescendant(root.transform, CamerasRootName, true);
            FindUniqueDescendant(root.transform, VolumeRootName, true);
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
                Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < descendants.Length; i++)
                {
                    Transform candidate = descendants[i];
                    if (!string.Equals(candidate.name, name, StringComparison.Ordinal))
                        continue;
                    if (result != null)
                        throw new InvalidOperationException("Duplicate descendant: " + name);
                    result = candidate;
                }
            }

            if (result == null)
                throw new InvalidOperationException("Missing hierarchy object: " + name);
            return result;
        }

        private static Camera FindUniqueCamera(Transform root, string name)
        {
            Transform transform = FindUniqueDescendant(root, name, false);
            Camera camera = transform.GetComponent<Camera>();
            if (camera == null)
                throw new InvalidOperationException("Missing Camera on " + name);
            return camera;
        }

        private static CaptureEvidence CaptureCameraPair(
            Camera camera,
            Scene previewScene,
            PowerState power,
            DoorPose pose,
            RenderTargets targets)
        {
            if (camera == null || camera.enabled)
                throw new InvalidOperationException("Only a disabled fixed clone camera may render.");
            if (camera.gameObject.scene != previewScene || camera.scene != previewScene)
                throw new InvalidOperationException("Camera is not isolated to the preview scene.");

            Component cameraData = FindUniqueUrpCameraData(camera);
            PropertyInfo postProperty = cameraData.GetType().GetProperty(
                "renderPostProcessing",
                BindingFlags.Instance | BindingFlags.Public);
            if (postProperty == null || postProperty.PropertyType != typeof(bool) ||
                !postProperty.CanRead || !postProperty.CanWrite)
            {
                throw new InvalidOperationException(
                    "URP renderPostProcessing property is unavailable on the clone camera.");
            }

            bool originalPostProcessing = (bool)postProperty.GetValue(cameraData);
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

                postProperty.SetValue(cameraData, originalPostProcessing);
                camera.targetTexture = targets.PresentationRender;
                targets.PresentationRender.DiscardContents();
                camera.Render();
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

                // This EXR path is technically scoped: ARGBHalf linear target,
                // RGBAHalf linear readback, and camera post-processing disabled.
                // It is not claimed to be a calibrated radiometric measurement.
                postProperty.SetValue(cameraData, false);
                camera.targetTexture = targets.HdrRender;
                targets.HdrRender.DiscardContents();
                camera.Render();
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
                byte[] exr = ImageConversion.EncodeToEXR(
                    hdrReadback,
                    Texture2D.EXRFlags.CompressZIP);
                if (exr == null || exr.Length == 0)
                    throw new InvalidOperationException("EXR encoding returned no bytes.");

                string stem = power.Id + "_D" + pose.Percent.ToString("000") + "__" +
                              SanitizeFilename(camera.name) + "__" + Status;
                return new CaptureEvidence(
                    power,
                    pose,
                    camera.name,
                    stem + ".png",
                    png,
                    ComputeSha256(png),
                    stem + "__LinearCameraColor_NoPost.exr",
                    exr,
                    ComputeSha256(exr),
                    meanLuminance,
                    maxLuminance,
                    originalPostProcessing,
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
                postProperty.SetValue(cameraData, originalPostProcessing);
                GL.sRGBWrite = originalSrgbWrite;
                if (presentationReadback != null)
                    Object.DestroyImmediate(presentationReadback);
                if (hdrReadback != null)
                    Object.DestroyImmediate(hdrReadback);
            }
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
                float luminance = Mathf.Max(
                    0f,
                    pixel.r * 0.2126f + pixel.g * 0.7152f + pixel.b * 0.0722f);
                sum += luminance;
                if (luminance > max)
                    max = luminance;
            }

            mean = sum / pixels.Length;
            maximum = max;
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
                    throw new InvalidOperationException("Duplicate URP camera data.");
                result = component;
            }
            if (result == null)
                throw new InvalidOperationException("Missing URP camera data on " + camera.name);
            return result;
        }

        private static void ValidateEvidenceAndAttachBaselines(List<CaptureEvidence> evidence)
        {
            var baselines = new Dictionary<string, CaptureEvidence>(StringComparer.Ordinal);
            for (int i = 0; i < evidence.Count; i++)
            {
                CaptureEvidence record = evidence[i];
                string key = record.Door.Percent.ToString(CultureInfo.InvariantCulture) + "|" +
                             record.CameraName;
                if (!record.Power.StartOn && !record.Power.AdministrativeOn)
                {
                    if (baselines.ContainsKey(key))
                        throw new InvalidOperationException("Duplicate P0/P0 baseline: " + key);
                    baselines.Add(key, record);
                }
            }

            int expectedBaselines = DoorPoses.Length * CameraNames.Length;
            if (baselines.Count != expectedBaselines)
            {
                throw new InvalidOperationException(
                    $"Expected {expectedBaselines} P0/P0 baselines, found {baselines.Count}.");
            }

            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < evidence.Count; i++)
            {
                CaptureEvidence record = evidence[i];
                string identity = record.Power.Id + "|" + record.Door.Percent + "|" +
                                  record.CameraName;
                if (!identities.Add(identity))
                    throw new InvalidOperationException("Duplicate evidence identity: " + identity);

                string key = record.Door.Percent.ToString(CultureInfo.InvariantCulture) + "|" +
                             record.CameraName;
                CaptureEvidence baseline = baselines[key];
                record.AttachBaseline(
                    baseline.HdrFilename,
                    baseline.MeanLinearLuminance,
                    record.MeanLinearLuminance - baseline.MeanLinearLuminance);
            }
        }

        private static string BuildManifest(
            string utcTimestamp,
            CaptureIdentity identity,
            PreviewContract contract,
            List<CaptureEvidence> evidence,
            GlobalRenderSnapshot globalSnapshot)
        {
            var builder = new StringBuilder(65536);
            builder.AppendLine("status=" + Status);
            builder.AppendLine("createdUtc=" + utcTimestamp);
            builder.AppendLine("equivalenceVerdict=false");
            builder.AppendLine("scope=Actual Start/Admin production Light components converted on an isolated clone to Realtime direct-only reference.");
            builder.AppendLine("limitation=No realtime GI bounce and no claim of total baked/realtime equivalence.");
            builder.AppendLine("validationScene=" + ValidationScenePath);
            builder.AppendLine("sourceSceneOpened=false");
            builder.AppendLine("sourceSceneSaved=false");
            builder.AppendLine("playModeChanged=false");
            builder.AppendLine("lightmappingInvoked=false");
            builder.AppendLine("productionAssetWrites=false");
            builder.AppendLine("previewSceneDeepClone=true");
            builder.AppendLine("previewSceneClosedBeforeEvidenceWrite=true");
            builder.AppendLine("fixedCamerasRemainDisabled=true");
            builder.AppendLine("portalTransportRootDisabledOnClone=true");
            builder.AppendLine("actualMovingDoorPreserved=true");
            builder.AppendLine("doorCovered=true");
            builder.AppendLine("doorPosePercents=0,25,50,75,100");
            builder.AppendLine("doorPoseAnglesDegrees=0,22.5,45,67.5,90");
            builder.AppendLine("doorProbeSplitStructurePreserved=true");
            builder.AppendLine("doorMaterialsAndMeshesPreserved=true");
            builder.AppendLine("doorRealtimeDirectLightAndShadowCovered=true");
            builder.AppendLine("glassTransmissionCovered=false");
            builder.AppendLine("bakedLightmapsCovered=false");
            builder.AppendLine("bakedLightmapContributionOnClone=false");
            builder.AppendLine("realtimeLightmapsCovered=false");
            builder.AppendLine("realtimeGiBounceCovered=false");
            builder.AppendLine("realtimeLightBounceIntensity=0");
            builder.AppendLine("lightProbeShCovered=false");
            builder.AppendLine("reflectionProbeSamplingCovered=false");
            builder.AppendLine("rendererMaterialMutation=false");
            builder.AppendLine("rendererShaderMutation=false");
            builder.AppendLine("rendererKeywordMutation=false");
            builder.AppendLine("materialPropertyBlockMutation=false");
            builder.AppendLine("additionalVertexStreamsMutation=false");
            builder.AppendLine("cloneRendererMutation=lightmapIndex=-1,realtimeLightmapIndex=-1,lightProbeUsage=Off,reflectionProbeUsage=Off only");
            builder.AppendLine("emissionMaterialsPreserved=true");
            builder.AppendLine("powerControl=Production Light.enabled only; enabled IgnoreLightControl lights retain their production baseline state.");
            builder.AppendLine("p0p0Baseline=Captured for every door pose and camera.");
            builder.AppendLine("deltaDocumentation=Each record links its same-door/same-camera P0P0 EXR and reports mean-linear-luminance delta; no pixel-difference artifact is claimed.");
            builder.AppendLine("presentationPng=URP fixed-camera output with the cloned camera post-processing setting preserved.");
            builder.AppendLine("linearHdrExr=ARGBHalf Linear render target to RGBAHalf linear readback, camera post-processing disabled, ZIP-compressed half-float EXR.");
            builder.AppendLine("linearHdrCaveat=Linear camera color is useful for comparison but is not a calibrated radiometric measurement.");
            builder.AppendLine("sceneEnvironment=The cloned hierarchy Volume and emission are preserved, but preview-scene RenderSettings are not asserted identical; use same-pose P0P0 subtraction for the direct-light delta.");
            builder.AppendLine("captureWidth=" + CaptureWidth);
            builder.AppendLine("captureHeight=" + CaptureHeight);
            builder.AppendLine("stateCameraRecordCount=" + evidence.Count);
            builder.AppendLine("presentationPngCount=" + evidence.Count);
            builder.AppendLine("linearHdrExrCount=" + evidence.Count);
            builder.AppendLine("expectedRendererCount=" + ExpectedRendererCount);
            builder.AppendLine("actualRendererCount=" + contract.Renderers.Count);
            builder.AppendLine("expectedStartLightCount=" + ExpectedStartLightCount);
            builder.AppendLine("actualStartLightCount=" + contract.Lights.StartCount);
            builder.AppendLine("expectedAdministrativeLightCount=" + ExpectedAdministrativeLightCount);
            builder.AppendLine("actualAdministrativeLightCount=" + contract.Lights.AdministrativeCount);
            builder.AppendLine("startIgnoreLightControlCount=" + contract.Lights.StartIgnoredCount);
            builder.AppendLine("administrativeIgnoreLightControlCount=" + contract.Lights.AdministrativeIgnoredCount);
            builder.AppendLine("doorRendererCount=" + contract.Door.RendererCount);
            builder.AppendLine("doorEnabledRendererCount=" + contract.Door.EnabledRendererCount);
            builder.AppendLine("globalLightmapsRestored=" + globalSnapshot.AreLightmapsRestored());
            builder.AppendLine("globalLightmapsModeRestored=" + globalSnapshot.IsModeRestored());
            builder.AppendLine("renderTextureActiveRestored=" + globalSnapshot.IsRenderTextureActiveRestored());
            builder.AppendLine("glSrgbWriteRestored=" + globalSnapshot.IsSrgbWriteRestored());
            builder.AppendLine("unityVersion=" + Application.unityVersion);
            builder.AppendLine("qualityLevel=" + identity.QualityLevel);
            builder.AppendLine("qualityName=" + identity.QualityName);
            builder.AppendLine("renderPipelineAsset=" + identity.RenderPipelinePath);
            builder.AppendLine("renderPipelineDependencyHash=" + identity.RenderPipelineDependencyHash);
            builder.AppendLine("lightingSettings=" + identity.LightingSettingsPath);
            builder.AppendLine("lightingSettingsDependencyHash=" + identity.LightingSettingsDependencyHash);
            builder.AppendLine("lightingSettingsRealtimeGI=" + identity.LightingSettingsRealtimeGi);
            identity.AppendAsset(builder, "toolSource", ToolSourcePath);
            identity.AppendAsset(builder, "validationScene", ValidationScenePath);
            identity.AppendAsset(builder, "startRoomPrefab", StartRoomPrefabPath);
            identity.AppendAsset(builder, "administrativeRoomPrefab", AdministrativeRoomPrefabPath);
            identity.AppendAsset(builder, "doorPrefab", DoorPrefabPath);

            for (int i = 0; i < PowerStates.Length; i++)
            {
                PowerState state = PowerStates[i];
                string prefix = "powerState[" + i + "].";
                builder.AppendLine(prefix + "id=" + state.Id);
                builder.AppendLine(prefix + "start=" + (state.StartOn ? "P100" : "P0"));
                builder.AppendLine(prefix + "administrative=" +
                                   (state.AdministrativeOn ? "P100" : "P0"));
                builder.AppendLine(prefix + "expectedStartEnabledLights=" +
                                   contract.Lights.ExpectedEnabledStart(state));
                builder.AppendLine(prefix + "expectedAdministrativeEnabledLights=" +
                                   contract.Lights.ExpectedEnabledAdministrative(state));
            }

            for (int i = 0; i < evidence.Count; i++)
            {
                CaptureEvidence record = evidence[i];
                string prefix = "capture[" + i + "].";
                builder.AppendLine(prefix + "status=" + Status);
                builder.AppendLine(prefix + "power=" + record.Power.Id);
                builder.AppendLine(prefix + "startPower=" +
                                   (record.Power.StartOn ? "P100" : "P0"));
                builder.AppendLine(prefix + "administrativePower=" +
                                   (record.Power.AdministrativeOn ? "P100" : "P0"));
                builder.AppendLine(prefix + "doorPercent=" + record.Door.Percent);
                builder.AppendLine(prefix + "doorFraction=" +
                                   FormatDouble(record.Door.Fraction));
                builder.AppendLine(prefix + "doorAngleDegrees=" +
                                   FormatDouble(contract.Door.OpenAngleDegrees *
                                                record.Door.Fraction));
                builder.AppendLine(prefix + "camera=" + record.CameraName);
                builder.AppendLine(prefix + "presentationPng=" + record.PngFilename);
                builder.AppendLine(prefix + "presentationPngSha256=" + record.PngSha256);
                builder.AppendLine(prefix + "presentationPngBytes=" + record.PngBytes.LongLength);
                builder.AppendLine(prefix + "linearHdrExr=" + record.HdrFilename);
                builder.AppendLine(prefix + "linearHdrExrSha256=" + record.HdrSha256);
                builder.AppendLine(prefix + "linearHdrExrBytes=" + record.HdrBytes.LongLength);
                builder.AppendLine(prefix + "meanLinearLuminance=" +
                                   FormatDouble(record.MeanLinearLuminance));
                builder.AppendLine(prefix + "maxLinearLuminance=" +
                                   FormatDouble(record.MaxLinearLuminance));
                builder.AppendLine(prefix + "p0p0BaselineExr=" + record.BaselineHdrFilename);
                builder.AppendLine(prefix + "p0p0BaselineMeanLinearLuminance=" +
                                   FormatDouble(record.BaselineMeanLinearLuminance));
                builder.AppendLine(prefix + "meanLinearLuminanceDeltaFromP0P0=" +
                                   FormatDouble(record.MeanLinearLuminanceDelta));
                builder.AppendLine(prefix + "pixelDifferenceArtifactWritten=false");
                builder.AppendLine(prefix + "cameraPosition=" + FormatVector3(record.Position));
                builder.AppendLine(prefix + "cameraRotation=" + FormatQuaternion(record.Rotation));
                builder.AppendLine(prefix + "fieldOfView=" + FormatDouble(record.FieldOfView));
                builder.AppendLine(prefix + "nearClip=" + FormatDouble(record.NearClip));
                builder.AppendLine(prefix + "farClip=" + FormatDouble(record.FarClip));
                builder.AppendLine(prefix + "cullingMask=" + record.CullingMask);
                builder.AppendLine(prefix + "allowHDR=" + record.AllowHdr);
                builder.AppendLine(prefix + "allowMSAA=" + record.AllowMsaa);
                builder.AppendLine(prefix + "presentationRenderTargetMSAA=" +
                                   record.PresentationMsaa);
                builder.AppendLine(prefix + "presentationPostProcessing=" +
                                   record.PresentationPostProcessing);
                builder.AppendLine(prefix + "hdrPostProcessing=false");
            }

            return builder.ToString();
        }

        private static void WriteEvidence(
            string outputFolder,
            List<CaptureEvidence> evidence,
            string manifest)
        {
            string absoluteFolder = AssetPathToAbsolutePath(outputFolder);
            string absoluteRoot = AssetPathToAbsolutePath(EvidenceRoot);
            string rootPrefix = absoluteRoot.TrimEnd(Path.DirectorySeparatorChar) +
                                Path.DirectorySeparatorChar;
            if (!absoluteFolder.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Evidence output escaped its isolated root.");
            if (Directory.Exists(absoluteFolder))
                throw new IOException("Refusing to overwrite evidence: " + outputFolder);

            Directory.CreateDirectory(absoluteFolder);
            for (int i = 0; i < evidence.Count; i++)
            {
                CaptureEvidence record = evidence[i];
                string pngPath = Path.Combine(absoluteFolder, record.PngFilename);
                string exrPath = Path.Combine(absoluteFolder, record.HdrFilename);
                File.WriteAllBytes(pngPath, record.PngBytes);
                File.WriteAllBytes(exrPath, record.HdrBytes);
                VerifyWrittenPng(pngPath, record.PngBytes, record.PngSha256);
                VerifyWrittenExr(exrPath, record.HdrBytes, record.HdrSha256);
            }

            const string manifestName = "manifest_REALTIME_DIRECT_GT_ONLY.txt";
            File.WriteAllText(
                Path.Combine(absoluteFolder, manifestName),
                manifest,
                new UTF8Encoding(false));

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            for (int i = 0; i < evidence.Count; i++)
            {
                CaptureEvidence record = evidence[i];
                VerifyImportedTexture(
                    outputFolder + "/" + record.PngFilename,
                    "PNG");
                VerifyImportedTexture(
                    outputFolder + "/" + record.HdrFilename,
                    "EXR");
            }
            AssetDatabase.ImportAsset(
                outputFolder + "/" + manifestName,
                ImportAssetOptions.ForceSynchronousImport);
        }

        private static void VerifyWrittenPng(
            string path,
            byte[] expectedBytes,
            string expectedHash)
        {
            byte[] bytes = File.ReadAllBytes(path);
            VerifyWrittenBytes(path, bytes, expectedBytes, expectedHash);
            if (bytes.Length < 24 ||
                bytes[0] != 0x89 || bytes[1] != 0x50 ||
                bytes[2] != 0x4E || bytes[3] != 0x47 ||
                bytes[12] != 0x49 || bytes[13] != 0x48 ||
                bytes[14] != 0x44 || bytes[15] != 0x52)
            {
                throw new InvalidOperationException("Invalid PNG stream: " + path);
            }
            int width = ReadBigEndianInt32(bytes, 16);
            int height = ReadBigEndianInt32(bytes, 20);
            if (width != CaptureWidth || height != CaptureHeight)
                throw new InvalidOperationException("Unexpected PNG dimensions: " + path);
        }

        private static void VerifyWrittenExr(
            string path,
            byte[] expectedBytes,
            string expectedHash)
        {
            byte[] bytes = File.ReadAllBytes(path);
            VerifyWrittenBytes(path, bytes, expectedBytes, expectedHash);
            if (bytes.Length < 8 || bytes[0] != 0x76 || bytes[1] != 0x2F ||
                bytes[2] != 0x31 || bytes[3] != 0x01)
            {
                throw new InvalidOperationException("Invalid OpenEXR stream: " + path);
            }
        }

        private static void VerifyWrittenBytes(
            string path,
            byte[] actual,
            byte[] expected,
            string expectedHash)
        {
            if (actual.LongLength != expected.LongLength ||
                !string.Equals(
                    ComputeSha256(actual),
                    expectedHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Written bytes differ from the buffered evidence: " + path);
            }
        }

        private static void VerifyImportedTexture(string assetPath, string label)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            // Unity's default NPOT importer may expose a resized Texture2D (for
            // example 1920x1080 source bytes as 2048x1024) even though the
            // written evidence stream is exact. Raw PNG dimensions and both
            // PNG/EXR byte hashes are verified above, so import verification is
            // intentionally limited to successful asset loading.
            if (texture == null)
            {
                throw new InvalidOperationException(
                    $"Imported {label} verification failed: {assetPath}");
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
            string normalized = NormalizePath(assetPath);
            if (!normalized.Equals("Assets", StringComparison.Ordinal) &&
                !normalized.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new ArgumentException("Expected an Assets-relative path.", nameof(assetPath));
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                throw new InvalidOperationException("Unable to resolve the Unity project root.");
            return Path.GetFullPath(
                Path.Combine(
                    projectRoot,
                    normalized.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return BytesToHex(sha.ComputeHash(bytes));
        }

        private static string ComputeFileSha256(string assetPath)
        {
            string absolute = AssetPathToAbsolutePath(assetPath);
            if (!File.Exists(absolute))
                throw new FileNotFoundException("Required source asset is missing.", assetPath);
            using (FileStream stream = File.OpenRead(absolute))
            using (SHA256 sha = SHA256.Create())
                return BytesToHex(sha.ComputeHash(stream));
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

        private static string SanitizeFilename(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return builder.ToString();
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatVector3(Vector3 value)
        {
            return FormatDouble(value.x) + "," +
                   FormatDouble(value.y) + "," +
                   FormatDouble(value.z);
        }

        private static string FormatQuaternion(Quaternion value)
        {
            return FormatDouble(value.x) + "," +
                   FormatDouble(value.y) + "," +
                   FormatDouble(value.z) + "," +
                   FormatDouble(value.w);
        }

        private readonly struct PowerState
        {
            public readonly string Id;
            public readonly bool StartOn;
            public readonly bool AdministrativeOn;

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
            public readonly float Fraction;

            public DoorPose(int percent)
            {
                Percent = percent;
                Fraction = percent / 100f;
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
            }

            public void AttachBaseline(string filename, double mean, double delta)
            {
                if (!string.IsNullOrEmpty(BaselineHdrFilename))
                    throw new InvalidOperationException("Baseline was attached twice.");
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
            public readonly RendererContract Renderers;
            public readonly LightContract Lights;
            public readonly DoorContract Door;

            private PreviewContract(
                GameObject root,
                GameObject startRoom,
                GameObject administrativeRoom,
                GameObject portalTransportRoot,
                Camera[] cameras,
                RendererContract renderers,
                LightContract lights,
                DoorContract door)
            {
                Root = root;
                StartRoom = startRoom;
                AdministrativeRoom = administrativeRoom;
                PortalTransportRoot = portalTransportRoot;
                Cameras = cameras;
                Renderers = renderers;
                Lights = lights;
                Door = door;
            }

            public static PreviewContract Create(GameObject root, Scene previewScene)
            {
                ValidateSourceRootShape(root);
                Transform productionRooms = FindUniqueDescendant(
                    root.transform,
                    ProductionRoomsRootName,
                    true);
                if (productionRooms.childCount != 2)
                {
                    throw new InvalidOperationException(
                        "Production rooms root must contain exactly Start and Admin.");
                }

                GameObject startRoom = FindUniqueDescendant(
                    productionRooms,
                    StartRoomName,
                    true).gameObject;
                GameObject administrativeRoom = FindUniqueDescendant(
                    productionRooms,
                    AdministrativeRoomName,
                    true).gameObject;
                GameObject portalTransport = FindUniqueDescendant(
                    root.transform,
                    PortalTransportRootName,
                    true).gameObject;
                Transform camerasRoot = FindUniqueDescendant(
                    root.transform,
                    CamerasRootName,
                    true);
                if (camerasRoot.childCount != ExpectedCameraCount)
                    throw new InvalidOperationException("Unexpected fixed camera hierarchy.");

                var cameras = new Camera[CameraNames.Length];
                for (int i = 0; i < CameraNames.Length; i++)
                {
                    Camera camera = FindUniqueCamera(camerasRoot, CameraNames[i]);
                    if (camera.enabled)
                        throw new InvalidOperationException("Fixed camera must remain disabled.");
                    FindUniqueUrpCameraData(camera);
                    camera.scene = previewScene;
                    cameras[i] = camera;
                }
                if (root.GetComponentsInChildren<Camera>(true).Length != ExpectedCameraCount)
                    throw new InvalidOperationException("Unexpected clone Camera count.");

                Transform exactDoor = startRoom.transform.Find(AddedDoorRelativePath);
                if (exactDoor == null ||
                    !string.Equals(exactDoor.name, AddedDoorName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Exact added moving-door path is missing.");
                }
                Transform uniqueDoor = FindUniqueDescendant(root.transform, AddedDoorName, false);
                if (uniqueDoor != exactDoor || uniqueDoor.IsChildOf(portalTransport.transform))
                {
                    throw new InvalidOperationException(
                        "The actual moving door must be the unique production-room door, outside PoC.");
                }

                DungeonPortalDoorAngleSource[] angleSources =
                    portalTransport.GetComponentsInChildren<DungeonPortalDoorAngleSource>(true);
                if (angleSources.Length != 1 || !angleSources[0].IsConfigured)
                    throw new InvalidOperationException("Unexpected door-angle source structure.");

                DoorContract door = DoorContract.Capture(exactDoor, angleSources[0]);
                RendererContract renderers = RendererContract.Capture(
                    root,
                    startRoom,
                    administrativeRoom,
                    door);
                LightContract lights = LightContract.Capture(
                    root,
                    startRoom,
                    administrativeRoom);

                return new PreviewContract(
                    root,
                    startRoom,
                    administrativeRoom,
                    portalTransport,
                    cameras,
                    renderers,
                    lights,
                    door);
            }

            public void PrepareDirectOnlyClone()
            {
                Renderers.PrepareDirectOnly();
                Lights.PrepareRealtimeDirect();

                PortalTransportRoot.SetActive(false);
                if (PortalTransportRoot.activeInHierarchy)
                    throw new InvalidOperationException("PoC root did not deactivate on clone.");
                Behaviour[] behaviours =
                    PortalTransportRoot.GetComponentsInChildren<Behaviour>(true);
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] != null && behaviours[i].isActiveAndEnabled)
                        throw new InvalidOperationException("A PoC Behaviour remained active.");
                }
                if (!Door.Root.gameObject.activeInHierarchy ||
                    Door.Root.IsChildOf(PortalTransportRoot.transform))
                {
                    throw new InvalidOperationException("Deactivating PoC affected the real door.");
                }
            }

            public void AssertPreparedState(PowerState power, DoorPose pose)
            {
                if (PortalTransportRoot.activeSelf || PortalTransportRoot.activeInHierarchy)
                    throw new InvalidOperationException("PoC root reactivated during capture.");
                Renderers.AssertDirectOnly();
                Lights.AssertPower(power);
                Door.AssertPose(pose);
                for (int i = 0; i < Cameras.Length; i++)
                {
                    if (Cameras[i] == null || Cameras[i].enabled)
                        throw new InvalidOperationException("A fixed clone camera was enabled.");
                }
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
                        $"Expected exactly {ExpectedRendererCount} production renderers and no PoC " +
                        $"renderers; all={all.Length}, rooms={start.Length + administrative.Length}.");
                }

                var roomSet = new HashSet<Renderer>(start);
                roomSet.UnionWith(administrative);
                if (roomSet.Count != all.Length)
                    throw new InvalidOperationException("Renderer room ownership is ambiguous.");
                for (int i = 0; i < all.Length; i++)
                {
                    if (!roomSet.Contains(all[i]) || !(all[i] is MeshRenderer))
                        throw new InvalidOperationException("Unexpected renderer type/ownership.");
                    if (all[i].HasPropertyBlock())
                    {
                        throw new InvalidOperationException(
                            "Direct reference refuses a renderer with a MaterialPropertyBlock: " +
                            GetHierarchyPath(all[i].transform));
                    }
                }

                door.AssertRendererMembership(roomSet);
                var entries = new RendererEntry[all.Length];
                for (int i = 0; i < all.Length; i++)
                    entries[i] = RendererEntry.Capture(all[i]);
                return new RendererContract(entries);
            }

            public void PrepareDirectOnly()
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].PrepareDirectOnly();
            }

            public void AssertDirectOnly()
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].AssertDirectOnly();
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
            private readonly MaterialState[] materials;
            private readonly MeshFilter meshFilter;
            private readonly Mesh sourceMesh;
            private readonly MeshRenderer meshRenderer;
            private readonly Mesh additionalVertexStreams;
            private readonly Vector4 lightmapScaleOffset;
            private readonly Vector4 realtimeLightmapScaleOffset;
            private readonly Transform probeAnchor;
            private readonly GameObject lightProbeProxyVolumeOverride;

            private RendererEntry(Renderer renderer)
            {
                this.renderer = renderer;
                enabled = renderer.enabled;
                forceRenderingOff = renderer.forceRenderingOff;
                shadowCastingMode = renderer.shadowCastingMode;
                receiveShadows = renderer.receiveShadows;
                renderingLayerMask = renderer.renderingLayerMask;
                gameObjectLayer = renderer.gameObject.layer;
                Material[] shared = renderer.sharedMaterials;
                materials = new MaterialState[shared.Length];
                for (int i = 0; i < shared.Length; i++)
                    materials[i] = MaterialState.Capture(shared[i]);
                meshRenderer = renderer as MeshRenderer;
                meshFilter = meshRenderer != null ? renderer.GetComponent<MeshFilter>() : null;
                sourceMesh = meshFilter != null ? meshFilter.sharedMesh : null;
                additionalVertexStreams =
                    meshRenderer != null ? meshRenderer.additionalVertexStreams : null;
                lightmapScaleOffset = renderer.lightmapScaleOffset;
                realtimeLightmapScaleOffset = renderer.realtimeLightmapScaleOffset;
                probeAnchor = renderer.probeAnchor;
                lightProbeProxyVolumeOverride = renderer.lightProbeProxyVolumeOverride;
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

            public void AssertDirectOnly()
            {
                if (renderer == null || renderer.enabled != enabled ||
                    renderer.forceRenderingOff != forceRenderingOff ||
                    renderer.shadowCastingMode != shadowCastingMode ||
                    renderer.receiveShadows != receiveShadows ||
                    renderer.renderingLayerMask != renderingLayerMask ||
                    renderer.gameObject.layer != gameObjectLayer ||
                    renderer.lightmapIndex != -1 ||
                    renderer.realtimeLightmapIndex != -1 ||
                    renderer.lightProbeUsage != LightProbeUsage.Off ||
                    renderer.reflectionProbeUsage != ReflectionProbeUsage.Off ||
                    renderer.lightmapScaleOffset != lightmapScaleOffset ||
                    renderer.realtimeLightmapScaleOffset != realtimeLightmapScaleOffset ||
                    renderer.probeAnchor != probeAnchor ||
                    renderer.lightProbeProxyVolumeOverride != lightProbeProxyVolumeOverride ||
                    renderer.HasPropertyBlock())
                {
                    throw new InvalidOperationException(
                        "Renderer direct-only/structural contract changed: " +
                        GetHierarchyPath(renderer != null ? renderer.transform : null));
                }

                Material[] current = renderer.sharedMaterials;
                if (current.Length != materials.Length)
                    throw new InvalidOperationException("Renderer material slot count changed.");
                for (int i = 0; i < current.Length; i++)
                    materials[i].AssertUnchanged(current[i]);
                if (meshFilter != null && meshFilter.sharedMesh != sourceMesh)
                    throw new InvalidOperationException("Renderer mesh changed.");
                if (meshRenderer != null &&
                    meshRenderer.additionalVertexStreams != additionalVertexStreams)
                {
                    throw new InvalidOperationException(
                        "Renderer additionalVertexStreams changed.");
                }
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
            }

            public static MaterialState Capture(Material material)
            {
                return new MaterialState(material);
            }

            public void AssertUnchanged(Material current)
            {
                if (current != material)
                    throw new InvalidOperationException("Material reference changed.");
                if (material == null)
                    return;
                if (material.shader != shader || material.renderQueue != renderQueue ||
                    material.enableInstancing != enableInstancing ||
                    material.doubleSidedGI != doubleSidedGi ||
                    material.globalIlluminationFlags != giFlags)
                {
                    throw new InvalidOperationException("Material shader/render state changed.");
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
            }
        }

        private sealed class LightContract
        {
            private readonly LightEntry[] entries;
            public int StartCount { get; }
            public int AdministrativeCount { get; }
            public int StartIgnoredCount { get; }
            public int AdministrativeIgnoredCount { get; }

            private LightContract(
                LightEntry[] entries,
                int startCount,
                int administrativeCount,
                int startIgnoredCount,
                int administrativeIgnoredCount)
            {
                this.entries = entries;
                StartCount = startCount;
                AdministrativeCount = administrativeCount;
                StartIgnoredCount = startIgnoredCount;
                AdministrativeIgnoredCount = administrativeIgnoredCount;
            }

            public static LightContract Capture(
                GameObject root,
                GameObject startRoom,
                GameObject administrativeRoom)
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
                        $"Production Light contract changed: all={all.Length}, " +
                        $"start={start.Length}, admin={administrative.Length}.");
                }

                var startSet = new HashSet<Light>(start);
                var administrativeSet = new HashSet<Light>(administrative);
                var entries = new LightEntry[all.Length];
                int startIgnored = 0;
                int administrativeIgnored = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    Light light = all[i];
                    bool belongsToStart = startSet.Contains(light);
                    bool belongsToAdministrative = administrativeSet.Contains(light);
                    if (belongsToStart == belongsToAdministrative)
                        throw new InvalidOperationException("Light room ownership is ambiguous.");
                    bool ignored = HasEnabledMarkerInParents(light.transform, "IgnoreLightControl");
                    if (ignored && belongsToStart)
                        startIgnored++;
                    if (ignored && belongsToAdministrative)
                        administrativeIgnored++;
                    entries[i] = LightEntry.Capture(
                        light,
                        belongsToStart ? RoomRole.Start : RoomRole.Administrative,
                        ignored);
                }

                if (startIgnored != ExpectedStartIgnoredLightCount ||
                    administrativeIgnored != ExpectedAdministrativeIgnoredLightCount)
                {
                    throw new InvalidOperationException(
                        $"IgnoreLightControl contract changed: start={startIgnored}, " +
                        $"admin={administrativeIgnored}.");
                }

                return new LightContract(
                    entries,
                    start.Length,
                    administrative.Length,
                    startIgnored,
                    administrativeIgnored);
            }

            public void PrepareRealtimeDirect()
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].PrepareRealtimeDirect();
            }

            public void ApplyPower(PowerState state)
            {
                for (int i = 0; i < entries.Length; i++)
                    entries[i].ApplyPower(state);
                AssertPower(state);
            }

            public void AssertPower(PowerState state)
            {
                int enabledStart = 0;
                int enabledAdministrative = 0;
                for (int i = 0; i < entries.Length; i++)
                {
                    entries[i].AssertRealtimeDirect(state);
                    if (!entries[i].Light.enabled)
                        continue;
                    if (entries[i].Room == RoomRole.Start)
                        enabledStart++;
                    else
                        enabledAdministrative++;
                }

                if (enabledStart != ExpectedEnabledStart(state) ||
                    enabledAdministrative != ExpectedEnabledAdministrative(state))
                {
                    throw new InvalidOperationException(
                        $"Unexpected realtime-light power result: start={enabledStart}, " +
                        $"admin={enabledAdministrative}.");
                }
            }

            public int ExpectedEnabledStart(PowerState state)
            {
                return state.StartOn ? StartCount : StartIgnoredCount;
            }

            public int ExpectedEnabledAdministrative(PowerState state)
            {
                return state.AdministrativeOn
                    ? AdministrativeCount
                    : AdministrativeIgnoredCount;
            }
        }

        private enum RoomRole
        {
            Start,
            Administrative
        }

        private sealed class LightEntry
        {
            public readonly Light Light;
            public readonly RoomRole Room;
            private readonly bool ignoreLightControl;
            private readonly bool sourceEnabled;
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
            private readonly Vector3 scale;
            private readonly Component[] components;

            private LightEntry(Light light, RoomRole room, bool ignored)
            {
                Light = light;
                Room = room;
                ignoreLightControl = ignored;
                sourceEnabled = light.enabled;
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
                scale = light.transform.lossyScale;
                components = light.GetComponents<Component>();

                if (!sourceEnabled || !light.gameObject.activeInHierarchy ||
                    light.lightmapBakeType != LightmapBakeType.Baked ||
                    shadows != LightShadows.Soft)
                {
                    throw new InvalidOperationException(
                        "Expected an active, enabled, Baked, Soft-shadow production Light: " +
                        GetHierarchyPath(light.transform));
                }
            }

            public static LightEntry Capture(Light light, RoomRole room, bool ignored)
            {
                return new LightEntry(light, room, ignored);
            }

            public void PrepareRealtimeDirect()
            {
                Light.lightmapBakeType = LightmapBakeType.Realtime;
                Light.bounceIntensity = 0f;
            }

            public void ApplyPower(PowerState state)
            {
                bool roomOn = Room == RoomRole.Start
                    ? state.StartOn
                    : state.AdministrativeOn;
                Light.enabled = ignoreLightControl ? sourceEnabled : roomOn;
            }

            public void AssertRealtimeDirect(PowerState state)
            {
                bool roomOn = Room == RoomRole.Start
                    ? state.StartOn
                    : state.AdministrativeOn;
                bool expectedEnabled = ignoreLightControl ? sourceEnabled : roomOn;
                if (Light == null || Light.enabled != expectedEnabled ||
                    Light.lightmapBakeType != LightmapBakeType.Realtime ||
                    !Mathf.Approximately(Light.bounceIntensity, 0f) ||
                    Light.type != type || Light.shadows != shadows ||
                    Light.color != color || !Mathf.Approximately(Light.intensity, intensity) ||
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
                    Light.transform.position != position ||
                    Light.transform.rotation != rotation ||
                    Light.transform.lossyScale != scale)
                {
                    throw new InvalidOperationException(
                        "Realtime direct Light contract changed: " +
                        GetHierarchyPath(Light != null ? Light.transform : null));
                }

                Component[] currentComponents = Light.GetComponents<Component>();
                if (currentComponents.Length != components.Length)
                    throw new InvalidOperationException("Light component structure changed.");
                for (int i = 0; i < components.Length; i++)
                {
                    if (currentComponents[i] != components[i])
                        throw new InvalidOperationException("Light component ordering changed.");
                }
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

            private readonly Quaternion rootLocalRotation;
            private readonly Vector3 rootLocalPosition;
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
                rootLocalRotation = root.localRotation;
                rootLocalPosition = root.localPosition;
                rootLocalScale = root.localScale;
                this.renderers = renderers;
                this.rendererEnabled = rendererEnabled;
                this.rendererMeshes = rendererMeshes;
            }

            public static DoorContract Capture(
                Transform root,
                DungeonPortalDoorAngleSource angleSource)
            {
                if (root == null || !root.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Actual moving door is inactive.");
                Transform leaf = root.Find(DoorLeafName);
                if (leaf == null || leaf.parent != root || angleSource.DoorLeaf != leaf)
                    throw new InvalidOperationException("Door leaf/angle-source binding changed.");

                ReadDoorConfiguration(
                    angleSource,
                    out Quaternion closed,
                    out Vector3 axis,
                    out float openAngle);
                if (Quaternion.Angle(root.localRotation, Quaternion.Euler(0f, 180f, 0f)) > 0.01f ||
                    Vector3.Distance(
                        root.localPosition,
                        ExpectedDoorRootLocalPosition) > 0.00001f ||
                    root.localScale != Vector3.one ||
                    Quaternion.Angle(leaf.localRotation, closed) > 0.01f ||
                    Quaternion.Angle(closed, Quaternion.identity) > 0.01f ||
                    Vector3.Angle(axis, Vector3.up) > 0.01f ||
                    !Mathf.Approximately(openAngle, 90f))
                {
                    throw new InvalidOperationException("Canonical door transform/hinge contract changed.");
                }

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                MeshRenderer[] meshRenderers = root.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length != ExpectedDoorRendererCount ||
                    meshRenderers.Length != ExpectedDoorRendererCount)
                {
                    throw new InvalidOperationException("Door must retain four MeshRenderers.");
                }

                Renderer leafRenderer = leaf.GetComponent<Renderer>();
                Transform splitRoot = leaf.Find(DoorSplitRootName);
                if (leafRenderer == null || leafRenderer.enabled || splitRoot == null)
                    throw new InvalidOperationException("Door root/split renderer contract changed.");
                Renderer positive = RequireNamedRenderer(splitRoot, DoorPositiveRendererName);
                Renderer negative = RequireNamedRenderer(splitRoot, DoorNegativeRendererName);
                Renderer edge = RequireNamedRenderer(splitRoot, DoorEdgeRendererName);
                if (!positive.enabled || !negative.enabled || !edge.enabled)
                    throw new InvalidOperationException("Door probe-split renderers must remain enabled.");

                Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
                if (colliders.Length != 1 || !(colliders[0] is BoxCollider) ||
                    colliders[0].transform != leaf)
                {
                    throw new InvalidOperationException("Door BoxCollider contract changed.");
                }
                if (CountBehavioursByFullName(root.gameObject, "DunGen.Door") != 1 ||
                    CountBehavioursByTypeName(
                        root.gameObject,
                        "DungeonDoorDualSideProbeReceiver") != 1)
                {
                    throw new InvalidOperationException("Door runtime/probe receiver contract changed.");
                }

                var enabledStates = new bool[renderers.Length];
                var meshes = new Mesh[renderers.Length];
                int enabledCount = 0;
                for (int i = 0; i < renderers.Length; i++)
                {
                    enabledStates[i] = renderers[i].enabled;
                    if (enabledStates[i])
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
                    enabledStates,
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
                    Root.localPosition != rootLocalPosition ||
                    Root.localRotation != rootLocalRotation ||
                    Root.localScale != rootLocalScale ||
                    Quaternion.Angle(Leaf.localRotation, expected) > 0.01f)
                {
                    throw new InvalidOperationException("Door pose/placement contract changed.");
                }

                Renderer[] current = Root.GetComponentsInChildren<Renderer>(true);
                if (current.Length != renderers.Length)
                    throw new InvalidOperationException("Door renderer structure changed.");
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (current[i] != renderers[i] || current[i].enabled != rendererEnabled[i])
                        throw new InvalidOperationException("Door renderer enabled/order changed.");
                    MeshFilter filter = current[i].GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh != rendererMeshes[i])
                        throw new InvalidOperationException("Door mesh changed.");
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
                    throw new InvalidOperationException("Missing split renderer: " + name);
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

        private static int CountBehavioursByFullName(GameObject root, string fullName)
        {
            int count = 0;
            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null &&
                    string.Equals(
                        behaviours[i].GetType().FullName,
                        fullName,
                        StringComparison.Ordinal))
                {
                    count++;
                }
            }
            return count;
        }

        private static int CountBehavioursByTypeName(GameObject root, string typeName)
        {
            int count = 0;
            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null &&
                    string.Equals(
                        behaviours[i].GetType().Name,
                        typeName,
                        StringComparison.Ordinal))
                {
                    count++;
                }
            }
            return count;
        }

        private static bool HasEnabledMarkerInParents(Transform transform, string typeName)
        {
            Transform current = transform;
            while (current != null)
            {
                MonoBehaviour[] behaviours = current.GetComponents<MonoBehaviour>();
                for (int i = 0; i < behaviours.Length; i++)
                {
                    MonoBehaviour behaviour = behaviours[i];
                    if (behaviour != null && behaviour.enabled &&
                        string.Equals(
                            behaviour.GetType().Name,
                            typeName,
                            StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                current = current.parent;
            }
            return false;
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
                        "RealtimeDirect_Presentation_Render",
                        24,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.sRGB,
                        msaa);
                    presentationResolve = CreateTarget(
                        "RealtimeDirect_Presentation_Resolve",
                        0,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.sRGB,
                        1);
                    hdrRender = CreateTarget(
                        "RealtimeDirect_HDR_Render",
                        24,
                        RenderTextureFormat.ARGBHalf,
                        RenderTextureReadWrite.Linear,
                        1);
                    hdrResolve = CreateTarget(
                        "RealtimeDirect_HDR_Resolve",
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

            private GlobalRenderSnapshot(
                LightmapData[] lightmaps,
                LightmapsMode lightmapsMode,
                RenderTexture activeRenderTexture,
                bool srgbWrite)
            {
                this.lightmaps = lightmaps;
                this.lightmapsMode = lightmapsMode;
                this.activeRenderTexture = activeRenderTexture;
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

            public void ApplyNoBakedLightmaps()
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

            public void AssertRestored()
            {
                if (!AreLightmapsRestored() || !IsModeRestored() ||
                    !IsRenderTextureActiveRestored() || !IsSrgbWriteRestored())
                {
                    throw new InvalidOperationException(
                        "Global lightmap/render state was not restored exactly.");
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
            private readonly SourceRendererEntry[] renderers;
            private readonly SourceLightEntry[] lights;
            private readonly SourceCameraEntry[] cameras;
            private readonly Transform doorRoot;
            private readonly Transform doorLeaf;
            private readonly Vector3 doorRootPosition;
            private readonly Quaternion doorRootRotation;
            private readonly Quaternion doorLeafRotation;

            private SourceSceneSnapshot(
                Scene scene,
                SourceRendererEntry[] renderers,
                SourceLightEntry[] lights,
                SourceCameraEntry[] cameras,
                Transform doorRoot,
                Transform doorLeaf)
            {
                this.scene = scene;
                this.renderers = renderers;
                this.lights = lights;
                this.cameras = cameras;
                this.doorRoot = doorRoot;
                this.doorLeaf = doorLeaf;
                doorRootPosition = doorRoot.localPosition;
                doorRootRotation = doorRoot.localRotation;
                doorLeafRotation = doorLeaf.localRotation;
            }

            public static SourceSceneSnapshot Capture(Scene scene, GameObject root)
            {
                Renderer[] sourceRenderers = root.GetComponentsInChildren<Renderer>(true);
                Light[] sourceLights = root.GetComponentsInChildren<Light>(true);
                Camera[] sourceCameras = root.GetComponentsInChildren<Camera>(true);
                if (sourceRenderers.Length != ExpectedRendererCount ||
                    sourceLights.Length != ExpectedTotalLightCount ||
                    sourceCameras.Length != ExpectedCameraCount)
                {
                    throw new InvalidOperationException("Source scene count contract changed.");
                }

                var rendererEntries = new SourceRendererEntry[sourceRenderers.Length];
                for (int i = 0; i < sourceRenderers.Length; i++)
                    rendererEntries[i] = SourceRendererEntry.Capture(sourceRenderers[i]);
                var lightEntries = new SourceLightEntry[sourceLights.Length];
                for (int i = 0; i < sourceLights.Length; i++)
                    lightEntries[i] = SourceLightEntry.Capture(sourceLights[i]);
                var cameraEntries = new SourceCameraEntry[sourceCameras.Length];
                for (int i = 0; i < sourceCameras.Length; i++)
                    cameraEntries[i] = SourceCameraEntry.Capture(sourceCameras[i]);

                Transform startRoom = FindUniqueDescendant(
                    FindUniqueDescendant(root.transform, ProductionRoomsRootName, true),
                    StartRoomName,
                    true);
                Transform doorRoot = startRoom.Find(AddedDoorRelativePath);
                Transform doorLeaf = doorRoot != null ? doorRoot.Find(DoorLeafName) : null;
                if (doorRoot == null || doorLeaf == null)
                    throw new InvalidOperationException("Source moving door is missing.");

                return new SourceSceneSnapshot(
                    scene,
                    rendererEntries,
                    lightEntries,
                    cameraEntries,
                    doorRoot,
                    doorLeaf);
            }

            public void AssertUnchanged()
            {
                if (!scene.IsValid() || !scene.isLoaded || scene.isDirty)
                    throw new InvalidOperationException("Source validation scene changed.");
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].AssertUnchanged();
                for (int i = 0; i < lights.Length; i++)
                    lights[i].AssertUnchanged();
                for (int i = 0; i < cameras.Length; i++)
                    cameras[i].AssertUnchanged();
                if (doorRoot == null || doorLeaf == null ||
                    doorRoot.localPosition != doorRootPosition ||
                    doorRoot.localRotation != doorRootRotation ||
                    doorLeaf.localRotation != doorLeafRotation)
                {
                    throw new InvalidOperationException("Source door transform changed.");
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
            private readonly MaterialState[] materials;
            private readonly bool hadPropertyBlock;
            private readonly MeshFilter meshFilter;
            private readonly Mesh mesh;
            private readonly MeshRenderer meshRenderer;
            private readonly Mesh additionalStreams;

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
                Material[] shared = renderer.sharedMaterials;
                materials = new MaterialState[shared.Length];
                for (int i = 0; i < shared.Length; i++)
                    materials[i] = MaterialState.Capture(shared[i]);
                hadPropertyBlock = renderer.HasPropertyBlock();
                meshRenderer = renderer as MeshRenderer;
                meshFilter = meshRenderer != null ? renderer.GetComponent<MeshFilter>() : null;
                mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                additionalStreams =
                    meshRenderer != null ? meshRenderer.additionalVertexStreams : null;
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
                    renderer.HasPropertyBlock() != hadPropertyBlock)
                {
                    throw new InvalidOperationException("Source renderer state changed.");
                }
                Material[] current = renderer.sharedMaterials;
                if (current.Length != materials.Length)
                    throw new InvalidOperationException("Source material slots changed.");
                for (int i = 0; i < current.Length; i++)
                    materials[i].AssertUnchanged(current[i]);
                if (meshFilter != null && meshFilter.sharedMesh != mesh)
                    throw new InvalidOperationException("Source mesh changed.");
                if (meshRenderer != null &&
                    meshRenderer.additionalVertexStreams != additionalStreams)
                {
                    throw new InvalidOperationException("Source additional streams changed.");
                }
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
            private readonly int renderingLayerMask;
            private readonly int componentCount;

            private SourceLightEntry(Light light)
            {
                this.light = light;
                enabled = light.enabled;
                bakeType = light.lightmapBakeType;
                bounceIntensity = light.bounceIntensity;
                intensity = light.intensity;
                color = light.color;
                renderingLayerMask = light.renderingLayerMask;
                componentCount = light.GetComponents<Component>().Length;
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
                    !Mathf.Approximately(light.intensity, intensity) ||
                    light.color != color ||
                    light.renderingLayerMask != renderingLayerMask ||
                    light.GetComponents<Component>().Length != componentCount)
                {
                    throw new InvalidOperationException("Source Light changed.");
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
            }

            public static SourceCameraEntry Capture(Camera camera)
            {
                if (camera.enabled)
                    throw new InvalidOperationException("Source fixed camera must be disabled.");
                FindUniqueUrpCameraData(camera);
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
                    StartRoomPrefabPath,
                    AdministrativeRoomPrefabPath,
                    DoorPrefabPath
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
                if (AssetDatabase.LoadMainAssetAtPath(path) == null &&
                    !string.Equals(path, ToolSourcePath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Required asset is not imported: " + path);
                }
                return new AssetIdentity(
                    path,
                    AssetDatabase.AssetPathToGUID(path),
                    AssetDatabase.GetAssetDependencyHash(path).ToString(),
                    ComputeFileSha256(path));
            }
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
    }
}
