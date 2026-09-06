#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DungeonPortalTransportPoC.GroundTruth
{
    /// <summary>
    /// Captures the production per-room P0/P100 baked state on a deep-cloned,
    /// isolated preview hierarchy.  This is intentionally a base reference: it
    /// contains neither adjacent-room transport nor a jointly baked moving-door
    /// shadow.  It must never be described as BAKE/REALTIME equivalence evidence.
    /// </summary>
    public static class DungeonPortalBakedRoomBaseReferenceCapture
    {
        public const string Status = "BAKED_ROOM_BASE_REFERENCE_ONLY";

        private const string ToolSourcePath =
            "Assets/Experiments/DungeonPortalTransportPoC/GroundTruth/BakedRoomState/Editor/" +
            "DungeonPortalBakedRoomBaseReferenceCapture.cs";
        private const string ValidationScenePath =
            "Assets/Experiments/DungeonPortalTransportPoC/Scenes/" +
            "Start_Admin_PortalTransportValidation.unity";
        private const string EvidenceRoot =
            "Assets/Experiments/DungeonPortalTransportPoC/Evidence/GroundTruth/BakedRoomBase";
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
        private const string DoorSplitRootName = "DungeonDoorProbeSplitRoot";
        private const string DoorPositiveRendererName = "DungeonDoorProbe_PositiveZ";
        private const string DoorNegativeRendererName = "DungeonDoorProbe_NegativeZ";
        private const string DoorEdgeRendererName = "DungeonDoorProbe_Edge";
        private const string StartCameraName = "Start_to_Admin_FixedCamera_DISABLED";
        private const string AdministrativeCameraName =
            "Admin_to_Start_FixedCamera_DISABLED";
        private const string OwnedCloneRootName =
            ValidationRootName + "__BakedRoomBaseClone";

        private const int ExpectedValidationRootChildren = 4;
        private const int ExpectedRendererCount = 389;
        private const int ExpectedMappedRendererCount = 383;
        private const int ExpectedUnmappedRendererCount = 6;
        private const int ExpectedTotalLightCount = 78;
        private const int ExpectedStartMapCount = 4;
        private const int ExpectedStartRendererEntryCount = 77;
        private const int ExpectedStartProbeCount = 82;
        private const int ExpectedStartEmissionCount = 6;
        private const int ExpectedAdministrativeMapCount = 12;
        private const int ExpectedAdministrativeRendererEntryCount = 306;
        private const int ExpectedAdministrativeProbeCount = 128;
        private const int ExpectedAdministrativeEmissionCount = 48;
        private const int ExpectedDoorRendererCount = 4;
        private const int ExpectedDoorEnabledRendererCount = 3;
        private const int ExpectedDoorSplitRendererCount = 3;
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

        [MenuItem(
            "Tools/Dungeon/Lighting/Portal Transport PoC/" +
            "Capture BAKED ROOM BASE Reference (Edit Mode)")]
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
            ValidateSourceRootShape(sourceRoot);

            SourceSceneSnapshot sourceSnapshot =
                SourceSceneSnapshot.Capture(sourceScene, sourceRoot);
            SelectionSnapshot selectionSnapshot = SelectionSnapshot.Capture();
            GlobalRenderSnapshot globalSnapshot = GlobalRenderSnapshot.Capture();
            string utcTimestamp = DateTime.UtcNow.ToString(
                "yyyyMMdd'T'HHmmssfff'Z'",
                CultureInfo.InvariantCulture);
            string outputFolder = EvidenceRoot + "/" + utcTimestamp;

            Scene previewScene = default;
            GameObject inactiveContainer = null;
            GameObject previewRoot = null;
            RenderTargets targets = null;
            PreviewContract contract = null;
            var evidence = new List<CaptureEvidence>(
                PowerStates.Length * DoorPoses.Length * ExpectedCameraCount);
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

                inactiveContainer = new GameObject(
                    "__BakedRoomBase_InactiveCloneContainer")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                SceneManager.MoveGameObjectToScene(inactiveContainer, previewScene);
                inactiveContainer.SetActive(false);

                previewRoot = Object.Instantiate(
                    sourceRoot,
                    inactiveContainer.transform,
                    true);
                previewRoot.name = OwnedCloneRootName;
                previewRoot.hideFlags = HideFlags.HideAndDontSave;
                previewRoot.SetActive(false);
                previewRoot.transform.SetParent(null, true);
                SceneManager.MoveGameObjectToScene(previewRoot, previewScene);
                Object.DestroyImmediate(inactiveContainer);
                inactiveContainer = null;
                if (previewRoot.activeSelf || previewRoot.activeInHierarchy ||
                    previewRoot.scene != previewScene)
                {
                    throw new InvalidOperationException(
                        "Baked base clone did not remain inactive and preview-owned.");
                }

                if (sourceScene.isDirty)
                {
                    throw new InvalidOperationException(
                        "Deep cloning unexpectedly dirtied the validation scene.");
                }

                contract = PreviewContract.Create(previewRoot, previewScene);
                contract.PrepareClone();
                contract.ActivatePreparedClone();
                targets = RenderTargets.Create();

                for (int powerIndex = 0; powerIndex < PowerStates.Length; powerIndex++)
                {
                    PowerState power = PowerStates[powerIndex];
                    contract.ApplyPower(power);

                    for (int poseIndex = 0; poseIndex < DoorPoses.Length; poseIndex++)
                    {
                        DoorPose pose = DoorPoses[poseIndex];
                        contract.ApplyDoorPoseAndProbes(pose);
                        contract.AssertState(power, pose);

                        for (int cameraIndex = 0;
                             cameraIndex < contract.Cameras.Length;
                             cameraIndex++)
                        {
                            CaptureEvidence record = CaptureCameraPair(
                                contract.Cameras[cameraIndex],
                                previewScene,
                                power,
                                pose,
                                targets);
                            evidence.Add(record);
                            contract.AssertState(power, pose);
                        }
                    }
                }

                int expected =
                    PowerStates.Length * DoorPoses.Length * ExpectedCameraCount;
                if (evidence.Count != expected)
                {
                    throw new InvalidOperationException(
                        $"Expected {expected} state-camera records, captured " +
                        evidence.Count + ".");
                }
            }
            finally
            {
                // Cleanup stages are deliberately independent: failure in one may
                // not skip global restoration, preview close, or selection restore.
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
                            if (previewRoot != null)
                            {
                                Object.DestroyImmediate(previewRoot);
                                previewRoot = null;
                            }
                            if (inactiveContainer != null)
                            {
                                Object.DestroyImmediate(inactiveContainer);
                                inactiveContainer = null;
                            }
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
            AssertSourceContextRestored(sourceScene);
            globalSnapshot.AssertRestored();
            selectionSnapshot.AssertRestored();
            sourceSnapshot.AssertUnchanged();
            AttachP0Baselines(evidence);

            string manifest = BuildManifest(
                utcTimestamp,
                contract,
                evidence,
                globalSnapshot);

            // First raw evidence write is a token-owned, explicitly non-success
            // staging asset.  It is published to the success path only after raw
            // hashes/imports and a second full source/global assertion pass.
            string stagingFolder = null;
            try
            {
                stagingFolder = WriteEvidenceToStaging(
                    outputFolder,
                    evidence,
                    manifest);

                AssertSourceContextRestored(sourceScene);
                globalSnapshot.AssertRestored();
                selectionSnapshot.AssertRestored();
                sourceSnapshot.AssertUnchanged();

                PublishStagedEvidence(stagingFolder, outputFolder, evidence);
                stagingFolder = null;

                AssertSourceContextRestored(sourceScene);
                globalSnapshot.AssertRestored();
                selectionSnapshot.AssertRestored();
                sourceSnapshot.AssertUnchanged();
            }
            catch (Exception exception)
            {
                PreserveFailedEvidenceMarker(
                    stagingFolder,
                    outputFolder,
                    exception);
                throw;
            }

            return
                Status + "\n" +
                "output=" + outputFolder + "\n" +
                "stateCameraRecords=" + evidence.Count + "\n" +
                "presentationPng=" + evidence.Count + "\n" +
                "linearHdrExr=" + evidence.Count + "\n" +
                "equivalenceVerdict=false";
        }

        private static Scene ValidateEditorPreconditions()
        {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
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

            Scene active = SceneManager.GetActiveScene();
            if (!active.IsValid() || !active.isLoaded ||
                EditorSceneManager.IsPreviewScene(active) ||
                !string.Equals(
                    NormalizePath(active.path),
                    ValidationScenePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The exact validation scene must already be active: " +
                    ValidationScenePath);
            }
            if (active.isDirty)
                throw new InvalidOperationException("The validation scene must be clean.");
            if (SceneManager.sceneCount != 1 || CountLoadedNonPreviewScenes() != 1)
            {
                throw new InvalidOperationException(
                    "Exactly one non-preview validation scene may be loaded.");
            }

            string sceneHash = ComputeAssetFileSha256(ValidationScenePath);
            if (!string.Equals(
                    sceneHash,
                    ExpectedValidationSceneSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Validation scene raw SHA-256 changed. Expected " +
                    ExpectedValidationSceneSha256 + ", actual " + sceneHash + ".");
            }

            AssertNoOwnedCloneArtifacts();

            return active;
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
                    "Owned baked-base clone artifact count must be zero, found " + count + ".");
            }
        }

        private static void AssertSourceContextRestored(Scene sourceScene)
        {
            if (SceneManager.GetActiveScene() != sourceScene)
                throw new InvalidOperationException("The active validation scene changed.");
            if (SceneManager.sceneCount != 1 || CountLoadedNonPreviewScenes() != 1)
                throw new InvalidOperationException("The loaded scene set changed.");
            if (sourceScene.isDirty)
                throw new InvalidOperationException("The validation scene became dirty.");
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
                    throw new InvalidOperationException("Duplicate root: " + name);
                result = roots[i];
            }
            if (result == null)
                throw new InvalidOperationException("Missing root: " + name);
            return result;
        }

        private static void ValidateSourceRootShape(GameObject root)
        {
            if (root == null || !root.activeInHierarchy || root.transform.parent != null ||
                root.transform.childCount != ExpectedValidationRootChildren)
            {
                throw new InvalidOperationException(
                    "Validation root hierarchy no longer matches the fixed contract.");
            }

            FindUniqueDescendant(root.transform, ProductionRoomsRootName, true);
            FindUniqueDescendant(root.transform, PortalTransportRootName, true);
            Transform camerasRoot =
                FindUniqueDescendant(root.transform, CamerasRootName, true);
            FindUniqueDescendant(root.transform, VolumeRootName, true);

            if (root.GetComponentsInChildren<Renderer>(true).Length != ExpectedRendererCount)
                throw new InvalidOperationException("Source renderer count changed.");
            if (root.GetComponentsInChildren<Light>(true).Length != ExpectedTotalLightCount)
                throw new InvalidOperationException("Source production-light count changed.");
            if (root.GetComponentsInChildren<Camera>(true).Length != ExpectedCameraCount)
                throw new InvalidOperationException("Source fixed-camera count changed.");

            ValidateFixedCameraKnownProperties(
                FindUniqueCamera(camerasRoot, StartCameraName),
                0);
            ValidateFixedCameraKnownProperties(
                FindUniqueCamera(camerasRoot, AdministrativeCameraName),
                1);
        }

        private static Transform FindUniqueDescendant(
            Transform root,
            string name,
            bool directChildOnly)
        {
            Transform result = null;
            Transform[] candidates;
            if (directChildOnly)
            {
                candidates = new Transform[root.childCount];
                for (int i = 0; i < root.childCount; i++)
                    candidates[i] = root.GetChild(i);
            }
            else
            {
                candidates = root.GetComponentsInChildren<Transform>(true);
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                Transform candidate = candidates[i];
                if (!string.Equals(candidate.name, name, StringComparison.Ordinal))
                    continue;
                if (result != null)
                    throw new InvalidOperationException("Duplicate hierarchy object: " + name);
                result = candidate;
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
                    "URP renderPostProcessing property is unavailable.");
            }

            bool originalPost = (bool)postProperty.GetValue(cameraData);
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

                postProperty.SetValue(cameraData, originalPost);
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
                ComputeLuminance(
                    hdrReadback,
                    out double meanLuminance,
                    out float maxLuminance);
                byte[] exr = ImageConversion.EncodeToEXR(
                    hdrReadback,
                    Texture2D.EXRFlags.CompressZIP);
                if (exr == null || exr.Length == 0)
                    throw new InvalidOperationException("EXR encoding returned no bytes.");

                string cameraToken = string.Equals(
                    camera.name,
                    StartCameraName,
                    StringComparison.Ordinal)
                    ? "Start_to_Admin"
                    : string.Equals(
                        camera.name,
                        AdministrativeCameraName,
                        StringComparison.Ordinal)
                        ? "Admin_to_Start"
                        : throw new InvalidOperationException(
                            "Unexpected fixed-camera name during filename creation.");
                string stem = power.Id + "_D" + pose.Percent.ToString("000") +
                              "__" + cameraToken;
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
                    meanLuminance,
                    maxLuminance,
                    originalPost,
                    camera.transform.position,
                    camera.transform.rotation);
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

        private static Component FindUniqueUrpCameraData(Camera camera)
        {
            Component result = null;
            Component[] components = camera.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || !string.Equals(
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
                throw new InvalidOperationException("Missing URP camera data.");
            return result;
        }

        private static void ComputeLuminance(
            Texture2D texture,
            out double mean,
            out float maximum)
        {
            Color[] pixels = texture.GetPixels();
            if (pixels == null || pixels.Length != CaptureWidth * CaptureHeight)
                throw new InvalidOperationException("Unexpected HDR pixel count.");

            double sum = 0d;
            maximum = 0f;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color pixel = pixels[i];
                float luminance = Mathf.Max(
                    0f,
                    pixel.r * 0.2126f + pixel.g * 0.7152f + pixel.b * 0.0722f);
                sum += luminance;
                maximum = Mathf.Max(maximum, luminance);
            }
            mean = pixels.Length > 0 ? sum / pixels.Length : 0d;
        }

        private static void AttachP0Baselines(List<CaptureEvidence> evidence)
        {
            var baselines = new Dictionary<string, CaptureEvidence>(StringComparer.Ordinal);
            for (int i = 0; i < evidence.Count; i++)
            {
                CaptureEvidence record = evidence[i];
                if (!record.Power.StartOn && !record.Power.AdministrativeOn)
                {
                    baselines.Add(record.Door.Percent + "|" + record.CameraName, record);
                }
            }

            for (int i = 0; i < evidence.Count; i++)
            {
                CaptureEvidence record = evidence[i];
                string key = record.Door.Percent + "|" + record.CameraName;
                if (!baselines.TryGetValue(key, out CaptureEvidence baseline))
                    throw new InvalidOperationException("Missing same-pose P0/P0 baseline.");
                record.AttachBaseline(baseline);
            }
        }

        private static string BuildManifest(
            string utcTimestamp,
            PreviewContract contract,
            List<CaptureEvidence> evidence,
            GlobalRenderSnapshot globalSnapshot)
        {
            var builder = new StringBuilder(65536);
            builder.AppendLine("status=" + Status);
            builder.AppendLine("createdUtc=" + utcTimestamp);
            builder.AppendLine("equivalenceVerdict=false");
            builder.AppendLine("scope=Production per-room P0/P100 baked state base only.");
            builder.AppendLine("adjacentSpillCovered=false");
            builder.AppendLine("adjacentBounceCovered=false");
            builder.AppendLine("bakedMovingDoorShadowCovered=false");
            builder.AppendLine("finalGroundTruth=false");
            builder.AppendLine("realtimeEquivalenceClaimed=false");
            builder.AppendLine("validationScene=" + ValidationScenePath);
            builder.AppendLine("sourceSceneOpened=false");
            builder.AppendLine("sourceSceneSaved=false");
            builder.AppendLine("playModeChanged=false");
            builder.AppendLine("lightmappingInvoked=false");
            builder.AppendLine("productionAssetWrites=false");
            builder.AppendLine("previewSceneDeepClone=true");
            builder.AppendLine("previewClonePreparedWhileInactive=true");
            builder.AppendLine("previewSceneClosedBeforeEvidenceWrite=true");
            builder.AppendLine("evidencePublication=Token-owned __STAGING_NOT_SUCCESS__ asset, raw hash/import verified, source/global reverified, then AssetDatabase rename.");
            builder.AppendLine("failedEvidencePolicy=Staging name retained or FAILED_NOT_ACCEPTED marker written; ambiguous success folder forbidden.");
            builder.AppendLine("portalTransportRootDisabledOnClone=true");
            builder.AppendLine("allClonedProductionLightsDisabled=true");
            builder.AppendLine("actualMovingDoorPreserved=true");
            builder.AppendLine("doorPosePercents=0,25,50,75,100");
            builder.AppendLine("powerStates=P000_P000,P100_P000,P000_P100,P100_P100");
            builder.AppendLine("lightmapRegistration=clone-only exact color+direction references");
            builder.AppendLine("lightmapSwitcherMethodsInvoked=false");
            builder.AppendLine("switcherApplyOnAwakeSuppressedBeforeCloneActivation=true");
            builder.AppendLine("clonedSwitchersDisabledBeforeActivation=true");
            builder.AppendLine("clonedDoorProbeReceiverDisabledBeforeActivation=true");
            builder.AppendLine("emissionPolicy=exact serialized P0/P100 material variants only");
            builder.AppendLine("roomRendererMaterialPropertyBlocksAdded=false");
            builder.AppendLine("dynamicReceiverSh=clone-only four-nearest inverse-distance subset; not full production registry semantics");
            builder.AppendLine("dynamicReceiverDoorwaySpatialBlendCovered=false");
            builder.AppendLine("dynamicReceiverDoorwaySpatialBlendGate=Fail closed inside production default zone expanded by 0.5m safety margin.");
            builder.AppendLine("dynamicReceiverTimeSmoothingCovered=false");
            builder.AppendLine("doorProbeTimeSmoothingCovered=false");
            builder.AppendLine("probeApplicationTiming=Instant absolute clone-only SH per captured state.");
            builder.AppendLine("doorSh=TileA/TileB plus-or-minus 0.35m, edge average, animated-pose blend");
            builder.AppendLine("doorMaterialsPreserved=true");
            builder.AppendLine("doorMpbExistingValuesPreserved=true");
            builder.AppendLine("rendererMeshesPreserved=true");
            builder.AppendLine("rendererShaderAssetsMutated=false");
            builder.AppendLine("rendererShaderKeywordsMutated=false");
            builder.AppendLine("rendererRenderQueuesMutated=false");
            builder.AppendLine("additionalVertexStreamsMutated=false");
            builder.AppendLine("additionalRenderersCreated=false");
            builder.AppendLine("p0ReflectionPolicy=Production-exact disableReflectionProbesOnPower0=true; all room probes disabled.");
            builder.AppendLine("reflectionProbeBucketSentinel=probeBucketIndex -1 means no base probe bucket is touched; values below -1 fail closed.");
            builder.AppendLine("p0ResidualCubemapIncluded=false");
            builder.AppendLine("desiredP0Residual=DESIRED_P0_RESIDUAL_CANDIDATE overlay only; deliberately excluded from production-exact base.");
            builder.AppendLine("sceneEnvironment=Same limitation as REALTIME_DIRECT preview: cloned hierarchy Volume is retained, but preview-scene RenderSettings identity is not asserted; compare same-pose captures.");
            builder.AppendLine("presentationPng=URP fixed-camera output with cloned camera post-processing setting preserved.");
            builder.AppendLine("linearHdrExr=ARGBHalf linear target to RGBAHalf linear readback, post-processing disabled, ZIP-compressed EXR.");
            builder.AppendLine("linearHdrCaveat=Useful for comparison; not a calibrated radiometric measurement.");
            builder.AppendLine("captureWidth=" + CaptureWidth);
            builder.AppendLine("captureHeight=" + CaptureHeight);
            builder.AppendLine("stateCameraRecordCount=" + evidence.Count);
            builder.AppendLine("sourceRendererCount=" + contract.RendererCount);
            builder.AppendLine("mappedRendererCount=" + contract.MappedRendererCount);
            builder.AppendLine("unmappedRendererCount=" + contract.UnmappedRendererCount);
            builder.AppendLine("unmappedDoorRendererCount=4");
            builder.AppendLine("unmappedFailClosedRemainderCount=2");
            builder.AppendLine("disabledProductionLightCount=" + contract.LightCount);
            builder.AppendLine("startP100MapCount=" + contract.Start.P100MapCount);
            builder.AppendLine("startP0MapCount=" + contract.Start.P0MapCount);
            builder.AppendLine("administrativeP100MapCount=" + contract.Administrative.P100MapCount);
            builder.AppendLine("administrativeP0MapCount=" + contract.Administrative.P0MapCount);
            builder.AppendLine("startRendererEntries=" + contract.Start.RendererEntryCount);
            builder.AppendLine("administrativeRendererEntries=" + contract.Administrative.RendererEntryCount);
            builder.AppendLine("startProbeEntries=" + contract.Start.ProbeEntryCount);
            builder.AppendLine("administrativeProbeEntries=" + contract.Administrative.ProbeEntryCount);
            builder.AppendLine("startEmissionEntries=" + contract.Start.EmissionEntryCount);
            builder.AppendLine("administrativeEmissionEntries=" + contract.Administrative.EmissionEntryCount);
            builder.AppendLine("dynamicReceiverCount=" + contract.DynamicReceiverCount);
            builder.AppendLine("doorSplitRendererCount=" + contract.Door.SplitRendererCount);
            builder.AppendLine("globalLightmapsRestored=" + globalSnapshot.AreLightmapsRestored());
            builder.AppendLine("globalLightmapsModeRestored=" + globalSnapshot.IsModeRestored());
            builder.AppendLine("lightingDataAssetRestored=" + globalSnapshot.IsLightingDataRestored());
            builder.AppendLine("renderTextureActiveRestored=" + globalSnapshot.IsRenderTextureActiveRestored());
            builder.AppendLine("glSrgbWriteRestored=" + globalSnapshot.IsSrgbWriteRestored());
            builder.AppendLine("unityVersion=" + Application.unityVersion);
            builder.AppendLine("toolSource=" + ToolSourcePath);
            builder.AppendLine("toolSourceSha256=" + ComputeAssetFileSha256(ToolSourcePath));
            builder.AppendLine("validationSceneSha256=" + ComputeAssetFileSha256(ValidationScenePath));
            builder.AppendLine("validationSceneExpectedSha256=" + ExpectedValidationSceneSha256);

            for (int i = 0; i < contract.CameraFingerprints.Length; i++)
            {
                FixedCameraFingerprint fingerprint = contract.CameraFingerprints[i];
                string prefix = "fixedCamera[" + i + "].";
                builder.AppendLine(prefix + "name=" + fingerprint.Name);
                builder.AppendLine(prefix + "fingerprintSha256=" + fingerprint.Sha256);
                builder.AppendLine(prefix + "payload=" + fingerprint.Payload);
            }

            for (int i = 0; i < PowerStates.Length; i++)
            {
                PowerState state = PowerStates[i];
                string prefix = "powerState[" + i + "].";
                builder.AppendLine(prefix + "id=" + state.Id);
                builder.AppendLine(prefix + "start=" + (state.StartOn ? "P100" : "P0"));
                builder.AppendLine(prefix + "administrative=" +
                                   (state.AdministrativeOn ? "P100" : "P0"));
            }

            for (int i = 0; i < evidence.Count; i++)
            {
                CaptureEvidence record = evidence[i];
                string prefix = "capture[" + i + "].";
                builder.AppendLine(prefix + "status=" + Status);
                builder.AppendLine(prefix + "power=" + record.Power.Id);
                builder.AppendLine(prefix + "doorPercent=" + record.Door.Percent);
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
                builder.AppendLine(prefix + "p0p0BaselineExr=" +
                                   record.BaselineHdrFilename);
                builder.AppendLine(prefix + "meanLinearLuminanceDeltaFromP0P0=" +
                                   FormatDouble(record.MeanLinearLuminanceDelta));
                builder.AppendLine(prefix + "cameraPosition=" +
                                   FormatVector3(record.CameraPosition));
                builder.AppendLine(prefix + "cameraRotation=" +
                                   FormatQuaternion(record.CameraRotation));
            }

            return builder.ToString();
        }

        private static string WriteEvidenceToStaging(
            string finalOutputFolder,
            List<CaptureEvidence> evidence,
            string manifest)
        {
            if (Directory.Exists(finalOutputFolder))
            {
                throw new InvalidOperationException(
                    "Final evidence folder already exists: " + finalOutputFolder);
            }

            string stagingFolder = NormalizePath(
                finalOutputFolder + "__STAGING_NOT_SUCCESS__" +
                Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture).Substring(0, 8));
            if (Directory.Exists(stagingFolder))
                throw new InvalidOperationException("Staging token collision.");

            Directory.CreateDirectory(stagingFolder);
            for (int i = 0; i < evidence.Count; i++)
            {
                CaptureEvidence record = evidence[i];
                string pngPath = NormalizePath(stagingFolder + "/" + record.PngFilename);
                string exrPath = NormalizePath(stagingFolder + "/" + record.HdrFilename);
                File.WriteAllBytes(pngPath, record.PngBytes);
                File.WriteAllBytes(exrPath, record.HdrBytes);
                VerifyRawPng(pngPath, record.PngSha256);
                VerifyRawExr(exrPath, record.HdrSha256);
            }

            string manifestPath = NormalizePath(
                stagingFolder + "/manifest_" + Status + ".txt");
            File.WriteAllText(manifestPath, manifest, new UTF8Encoding(false));

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            VerifyEvidenceFolder(stagingFolder, evidence, true);
            return stagingFolder;
        }

        private static void PublishStagedEvidence(
            string stagingFolder,
            string finalOutputFolder,
            List<CaptureEvidence> evidence)
        {
            if (string.IsNullOrWhiteSpace(stagingFolder) ||
                !Directory.Exists(stagingFolder) ||
                Directory.Exists(finalOutputFolder))
            {
                throw new InvalidOperationException(
                    "Evidence publish precondition failed.");
            }

            string moveError = AssetDatabase.MoveAsset(
                stagingFolder,
                finalOutputFolder);
            if (!string.IsNullOrEmpty(moveError))
                throw new InvalidOperationException("Evidence publish failed: " + moveError);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            VerifyEvidenceFolder(finalOutputFolder, evidence, true);
        }

        private static void VerifyEvidenceFolder(
            string folder,
            List<CaptureEvidence> evidence,
            bool verifyImportedLoad)
        {
            for (int i = 0; i < evidence.Count; i++)
            {
                CaptureEvidence record = evidence[i];
                string pngPath = NormalizePath(folder + "/" + record.PngFilename);
                string exrPath = NormalizePath(folder + "/" + record.HdrFilename);
                if (verifyImportedLoad &&
                    AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath) == null)
                {
                    throw new InvalidOperationException(
                        "Imported PNG failed load-only verification.");
                }
                if (verifyImportedLoad &&
                    AssetDatabase.LoadAssetAtPath<Texture2D>(exrPath) == null)
                {
                    throw new InvalidOperationException(
                        "Imported EXR failed load-only verification.");
                }
                // Imported dimensions are intentionally not used: NPOT import
                // settings may expose platform dimensions unlike the raw stream.
                VerifyRawPng(pngPath, record.PngSha256);
                VerifyRawExr(exrPath, record.HdrSha256);
            }
        }

        private static void PreserveFailedEvidenceMarker(
            string stagingFolder,
            string finalOutputFolder,
            Exception exception)
        {
            try
            {
                string failedFolder = Directory.Exists(finalOutputFolder)
                    ? finalOutputFolder
                    : stagingFolder;
                if (string.IsNullOrWhiteSpace(failedFolder) ||
                    !Directory.Exists(failedFolder))
                {
                    return;
                }

                string marker = NormalizePath(
                    failedFolder + "/FAILED_NOT_ACCEPTED.txt");
                File.WriteAllText(
                    marker,
                    "status=FAILED\naccepted=false\nexception=" +
                    exception.GetType().FullName + "\nmessage=" +
                    exception.Message + "\n",
                    new UTF8Encoding(false));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            catch
            {
                // The __STAGING_NOT_SUCCESS__ path remains an unambiguous failure
                // even if the secondary marker itself cannot be written.
            }
        }

        private static void VerifyRawPng(string path, string expectedHash)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 24 || bytes[0] != 0x89 || bytes[1] != 0x50 ||
                bytes[2] != 0x4E || bytes[3] != 0x47)
            {
                throw new InvalidOperationException("Invalid PNG stream: " + path);
            }
            int width = ReadBigEndianInt32(bytes, 16);
            int height = ReadBigEndianInt32(bytes, 20);
            if (width != CaptureWidth || height != CaptureHeight)
                throw new InvalidOperationException("Unexpected raw PNG IHDR size: " + path);
            if (!string.Equals(ComputeSha256(bytes), expectedHash, StringComparison.Ordinal))
                throw new InvalidOperationException("PNG hash mismatch: " + path);
        }

        private static void VerifyRawExr(string path, string expectedHash)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 8 || bytes[0] != 0x76 || bytes[1] != 0x2F ||
                bytes[2] != 0x31 || bytes[3] != 0x01)
            {
                throw new InvalidOperationException("Invalid EXR stream: " + path);
            }
            if (!string.Equals(ComputeSha256(bytes), expectedHash, StringComparison.Ordinal))
                throw new InvalidOperationException("EXR hash mismatch: " + path);
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) |
                   (bytes[offset + 2] << 8) | bytes[offset + 3];
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
            public float Fraction => Percent / 100f;

            public DoorPose(int percent)
            {
                Percent = percent;
            }
        }

        public readonly struct FixedCameraFingerprint
        {
            public readonly string Name;
            public readonly string Payload;
            public readonly string Sha256;

            public FixedCameraFingerprint(
                string name,
                string payload,
                string sha256)
            {
                Name = name;
                Payload = payload;
                Sha256 = sha256;
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
            public string BaselineHdrFilename { get; private set; }
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
                Vector3 cameraPosition,
                Quaternion cameraRotation)
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
                BaselineHdrFilename = string.Empty;
                MeanLinearLuminanceDelta = 0d;
            }

            public void AttachBaseline(CaptureEvidence baseline)
            {
                BaselineHdrFilename = baseline.HdrFilename;
                MeanLinearLuminanceDelta =
                    MeanLinearLuminance - baseline.MeanLinearLuminance;
            }
        }

        private sealed class PreviewContract
        {
            private readonly Scene previewScene;
            private readonly GameObject root;
            private readonly Transform productionRooms;
            private readonly Transform portalTransport;
            private readonly Light[] lights;
            private readonly RendererInvariant[] rendererInvariants;
            private readonly MaterialAssetInvariant[] materialInvariants;
            private readonly HashSet<Renderer> mappedRenderers;
            private readonly HashSet<Renderer> doorRenderers;
            private PowerState currentPower;
            private DoorPose currentPose;
            private bool hasPower;
            private bool hasPose;

            public readonly RoomAdapter Start;
            public readonly RoomAdapter Administrative;
            public readonly DoorProbeAdapter Door;
            public readonly CloneProbeAdapter Probes;
            public readonly Camera[] Cameras;
            public readonly FixedCameraFingerprint[] CameraFingerprints;

            public int RendererCount => rendererInvariants.Length;
            public int MappedRendererCount => mappedRenderers.Count;
            public int UnmappedRendererCount => RendererCount - MappedRendererCount;
            public int LightCount => lights.Length;
            public int DynamicReceiverCount => Probes.DynamicReceiverCount;

            private PreviewContract(
                Scene previewScene,
                GameObject root,
                Transform productionRooms,
                Transform portalTransport,
                Light[] lights,
                RendererInvariant[] rendererInvariants,
                MaterialAssetInvariant[] materialInvariants,
                HashSet<Renderer> mappedRenderers,
                HashSet<Renderer> doorRenderers,
                RoomAdapter start,
                RoomAdapter administrative,
                DoorProbeAdapter door,
                CloneProbeAdapter probes,
                Camera[] cameras,
                FixedCameraFingerprint[] cameraFingerprints)
            {
                this.previewScene = previewScene;
                this.root = root;
                this.productionRooms = productionRooms;
                this.portalTransport = portalTransport;
                this.lights = lights;
                this.rendererInvariants = rendererInvariants;
                this.materialInvariants = materialInvariants;
                this.mappedRenderers = mappedRenderers;
                this.doorRenderers = doorRenderers;
                Start = start;
                Administrative = administrative;
                Door = door;
                Probes = probes;
                Cameras = cameras;
                CameraFingerprints = cameraFingerprints;
            }

            public static PreviewContract Create(GameObject root, Scene previewScene)
            {
                if (root == null || root.scene != previewScene ||
                    !EditorSceneManager.IsPreviewScene(previewScene))
                {
                    throw new InvalidOperationException("Invalid preview clone ownership.");
                }

                Transform production = FindUniqueDescendant(
                    root.transform,
                    ProductionRoomsRootName,
                    true);
                Transform portal = FindUniqueDescendant(
                    root.transform,
                    PortalTransportRootName,
                    true);
                Transform camerasRoot = FindUniqueDescendant(
                    root.transform,
                    CamerasRootName,
                    true);
                FindUniqueDescendant(root.transform, VolumeRootName, true);

                Transform startRoot = FindUniqueDescendant(
                    production,
                    StartRoomName,
                    true);
                Transform administrativeRoot = FindUniqueDescendant(
                    production,
                    AdministrativeRoomName,
                    true);

                RoomAdapter start = RoomAdapter.Create(
                    "Start",
                    startRoot,
                    ExpectedStartMapCount,
                    ExpectedStartRendererEntryCount,
                    ExpectedStartProbeCount,
                    ExpectedStartEmissionCount,
                    previewScene);
                RoomAdapter administrative = RoomAdapter.Create(
                    "Administrative",
                    administrativeRoot,
                    ExpectedAdministrativeMapCount,
                    ExpectedAdministrativeRendererEntryCount,
                    ExpectedAdministrativeProbeCount,
                    ExpectedAdministrativeEmissionCount,
                    previewScene);

                Transform exactDoor = startRoot.Find(AddedDoorRelativePath);
                if (exactDoor == null || !string.Equals(
                        exactDoor.name,
                        AddedDoorName,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Actual moving door path changed.");
                }
                Transform uniqueDoor = FindUniqueDescendant(
                    production,
                    AddedDoorName,
                    false);
                if (uniqueDoor != exactDoor || uniqueDoor.IsChildOf(portal))
                    throw new InvalidOperationException("Actual door ownership changed.");

                DungeonPortalDoorAngleSource[] angleSources =
                    portal.GetComponentsInChildren<DungeonPortalDoorAngleSource>(true);
                if (angleSources.Length != 1)
                    throw new InvalidOperationException("Expected one cloned door-angle source.");

                DoorProbeAdapter door = DoorProbeAdapter.Create(
                    exactDoor,
                    angleSources[0],
                    start,
                    administrative,
                    previewScene);
                CloneProbeAdapter probes = CloneProbeAdapter.Create(
                    production,
                    start,
                    administrative,
                    door);

                var cameras = new[]
                {
                    FindUniqueCamera(camerasRoot, StartCameraName),
                    FindUniqueCamera(camerasRoot, AdministrativeCameraName)
                };
                var cameraFingerprints = new FixedCameraFingerprint[cameras.Length];
                for (int i = 0; i < cameras.Length; i++)
                {
                    cameras[i].scene = previewScene;
                    if (cameras[i].enabled || cameras[i].gameObject.scene != previewScene)
                        throw new InvalidOperationException("Fixed clone camera contract changed.");
                    if (cameras[i].scene != previewScene)
                        throw new InvalidOperationException("Fixed clone Camera.scene assignment failed.");
                    FindUniqueUrpCameraData(cameras[i]);
                    cameraFingerprints[i] =
                        CaptureAndValidateFixedCameraFingerprint(cameras[i], i, previewScene);
                }

                Light[] lights = production.GetComponentsInChildren<Light>(true);
                if (lights.Length != ExpectedTotalLightCount)
                    throw new InvalidOperationException("Cloned production-light count changed.");

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length != ExpectedRendererCount)
                    throw new InvalidOperationException("Cloned renderer count changed.");
                var rendererInvariants = new RendererInvariant[renderers.Length];
                for (int i = 0; i < renderers.Length; i++)
                    rendererInvariants[i] = RendererInvariant.Capture(renderers[i]);

                var mapped = new HashSet<Renderer>(start.MappedRenderers);
                mapped.UnionWith(administrative.MappedRenderers);
                if (mapped.Count != ExpectedMappedRendererCount)
                    throw new InvalidOperationException("Mapped renderer count changed.");

                var doorRenderers = new HashSet<Renderer>(door.AllRenderers);
                if (doorRenderers.Count != ExpectedDoorRendererCount)
                    throw new InvalidOperationException("Door renderer count changed.");
                if (mapped.Overlaps(doorRenderers) ||
                    renderers.Length - mapped.Count - doorRenderers.Count != 2)
                {
                    throw new InvalidOperationException(
                        "Expected six unmapped renderers: four door plus two fail-closed remainder.");
                }

                MaterialAssetInvariant[] materials = CaptureMaterialAssets(
                    renderers,
                    start,
                    administrative);

                return new PreviewContract(
                    previewScene,
                    root,
                    production,
                    portal,
                    lights,
                    rendererInvariants,
                    materials,
                    mapped,
                    doorRenderers,
                    start,
                    administrative,
                    door,
                    probes,
                    cameras,
                    cameraFingerprints);
            }

            public void PrepareClone()
            {
                if (root.activeSelf || root.activeInHierarchy)
                {
                    throw new InvalidOperationException(
                        "Baked base clone must be prepared while inactive.");
                }
                if (!portalTransport.gameObject.activeSelf)
                    throw new InvalidOperationException("PoC clone root baseline changed.");
                portalTransport.gameObject.SetActive(false);

                Start.PrepareForManualCaptureWhileInactive();
                Administrative.PrepareForManualCaptureWhileInactive();
                Door.PrepareForManualCaptureWhileInactive();

                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i] == null || !lights[i].transform.IsChildOf(productionRooms))
                        throw new InvalidOperationException("Production Light ownership changed.");
                    lights[i].enabled = false;
                }

                Start.AssertRoomMappedRenderersHaveNoPropertyBlocks();
                Administrative.AssertRoomMappedRenderersHaveNoPropertyBlocks();
                AssertInvariantStructure(false);
            }

            public void ActivatePreparedClone()
            {
                if (root.activeSelf || root.activeInHierarchy ||
                    portalTransport.gameObject.activeSelf)
                {
                    throw new InvalidOperationException(
                        "Baked base clone activation precondition changed.");
                }

                root.SetActive(true);
                if (!root.activeInHierarchy || portalTransport.gameObject.activeInHierarchy)
                {
                    throw new InvalidOperationException(
                        "Prepared baked base clone did not activate in isolation.");
                }
                Start.AssertManualCaptureControl();
                Administrative.AssertManualCaptureControl();
                Door.AssertManualCaptureControl();
            }

            public void ApplyPower(PowerState power)
            {
                DungeonTileBakeData startData = Start.ResolveData(power.StartOn);
                DungeonTileBakeData administrativeData =
                    Administrative.ResolveData(power.AdministrativeOn);

                LightmapsMode mode = ResolveCommonLightmapsMode(
                    startData,
                    administrativeData);
                var registry = new CloneLightmapRegistry();
                int[] startRemap = registry.Register(startData);
                int[] administrativeRemap = registry.Register(administrativeData);

                LightmapSettings.lightmapsMode = mode;
                LightmapSettings.lightmaps = registry.ToArray();
                Start.ApplyState(power.StartOn, startData, startRemap);
                Administrative.ApplyState(
                    power.AdministrativeOn,
                    administrativeData,
                    administrativeRemap);

                if (LightmapSettings.lightmaps.Length !=
                    ExpectedStartMapCount + ExpectedAdministrativeMapCount)
                {
                    throw new InvalidOperationException(
                        "Unexpected merged baked lightmap count.");
                }

                Start.AssertRoomMappedRenderersHaveNoPropertyBlocks();
                Administrative.AssertRoomMappedRenderersHaveNoPropertyBlocks();
                currentPower = power;
                hasPower = true;
                hasPose = false;
                AssertInvariantStructure(true);
            }

            public void ApplyDoorPoseAndProbes(DoorPose pose)
            {
                if (!hasPower)
                    throw new InvalidOperationException("Apply a power state first.");

                Door.ApplyPose(pose);
                Probes.Apply(Start.CurrentData, Administrative.CurrentData);
                currentPose = pose;
                hasPose = true;
            }

            public void AssertState(PowerState power, DoorPose pose)
            {
                if (!hasPower || !hasPose || currentPower.Id != power.Id ||
                    currentPose.Percent != pose.Percent)
                {
                    throw new InvalidOperationException("Preview state tracking mismatch.");
                }
                if (!root.activeInHierarchy || portalTransport.gameObject.activeSelf)
                    throw new InvalidOperationException("Preview root isolation changed.");
                if (root.scene != previewScene || !EditorSceneManager.IsPreviewScene(previewScene))
                    throw new InvalidOperationException("Preview scene ownership changed.");

                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i] == null || lights[i].enabled)
                        throw new InvalidOperationException("A cloned production Light became enabled.");
                }
                for (int i = 0; i < Cameras.Length; i++)
                {
                    if (Cameras[i] == null || Cameras[i].enabled ||
                        Cameras[i].targetTexture != null ||
                        Cameras[i].scene != previewScene)
                    {
                        throw new InvalidOperationException("Fixed clone camera state changed.");
                    }
                }

                Start.AssertState(power.StartOn);
                Administrative.AssertState(power.AdministrativeOn);
                Start.AssertManualCaptureControl();
                Administrative.AssertManualCaptureControl();
                Door.AssertManualCaptureControl();
                Start.AssertRoomMappedRenderersHaveNoPropertyBlocks();
                Administrative.AssertRoomMappedRenderersHaveNoPropertyBlocks();
                Door.AssertPoseAndProbeState(pose);
                Probes.AssertApplied();
                AssertInvariantStructure(true);
            }

            private void AssertInvariantStructure(bool allowBakedStateMaterials)
            {
                Renderer[] current = root.GetComponentsInChildren<Renderer>(true);
                if (current.Length != rendererInvariants.Length)
                    throw new InvalidOperationException("Additional renderer appeared on clone.");

                for (int i = 0; i < rendererInvariants.Length; i++)
                {
                    RendererInvariant invariant = rendererInvariants[i];
                    bool allowMaterials = allowBakedStateMaterials &&
                                          mappedRenderers.Contains(invariant.Renderer);
                    invariant.AssertCore(allowMaterials);

                    if (!allowMaterials && !doorRenderers.Contains(invariant.Renderer))
                        invariant.AssertMaterialsUnchanged();
                    if (doorRenderers.Contains(invariant.Renderer))
                        invariant.AssertMaterialsUnchanged();
                    if (!doorRenderers.Contains(invariant.Renderer) &&
                        !Probes.ContainsDynamicRenderer(invariant.Renderer))
                    {
                        invariant.AssertProbeAndPropertyBlockUnchanged();
                    }
                }
                for (int i = 0; i < materialInvariants.Length; i++)
                    materialInvariants[i].AssertUnchanged();
            }

            private static MaterialAssetInvariant[] CaptureMaterialAssets(
                Renderer[] renderers,
                RoomAdapter start,
                RoomAdapter administrative)
            {
                var unique = new HashSet<Material>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    Material[] materials = renderers[i].sharedMaterials;
                    for (int m = 0; m < materials.Length; m++)
                    {
                        if (materials[m] != null)
                            unique.Add(materials[m]);
                    }
                }
                start.AddEmissionMaterials(unique);
                administrative.AddEmissionMaterials(unique);

                var result = new MaterialAssetInvariant[unique.Count];
                int index = 0;
                foreach (Material material in unique)
                    result[index++] = MaterialAssetInvariant.Capture(material);
                return result;
            }

            private static LightmapsMode ResolveCommonLightmapsMode(
                DungeonTileBakeData start,
                DungeonTileBakeData administrative)
            {
                LightmapsMode startMode = ResolveLightmapsMode(start);
                LightmapsMode administrativeMode = ResolveLightmapsMode(administrative);
                if (startMode != administrativeMode)
                {
                    throw new InvalidOperationException(
                        "Start/Admin baked lightmap modes disagree.");
                }
                return startMode;
            }
        }

        private sealed class RoomAdapter
        {
            private readonly string role;
            private readonly Transform root;
            private readonly DungeonTileLightmapSwitcher switcher;
            private readonly DungeonTilePowerBakeSet bakeSet;
            private readonly DungeonTileBakeData p100;
            private readonly DungeonTileBakeData p0;
            private readonly Dictionary<string, List<Renderer>> rendererBuckets;
            private readonly Dictionary<string, List<ReflectionProbe>> reflectionBuckets;
            private readonly Renderer[] mappedRenderers;
            private readonly Dictionary<Renderer, Material[]> baseMaterials;
            private readonly ReflectionPolicy reflectionPolicy;
            private readonly int expectedMapCount;
            private readonly int expectedRendererEntryCount;
            private readonly int expectedProbeCount;
            private readonly int expectedEmissionCount;
            private Dictionary<Renderer, RendererAssignment> currentAssignments;
            private Dictionary<Renderer, Material[]> currentExpectedMaterials;
            private bool currentP100;

            public DungeonTileBakeData CurrentData { get; private set; }
            public IReadOnlyList<Renderer> MappedRenderers => mappedRenderers;
            public Transform Root => root;
            public int P100MapCount => p100.lightmapColors.Length;
            public int P0MapCount => p0.lightmapColors.Length;
            public int RendererEntryCount => p100.rendererEntries.Length;
            public int ProbeEntryCount => p100.lightProbeEntries.Length;
            public int EmissionEntryCount => expectedEmissionCount;

            private RoomAdapter(
                string role,
                Transform root,
                DungeonTileLightmapSwitcher switcher,
                DungeonTilePowerBakeSet bakeSet,
                DungeonTileBakeData p100,
                DungeonTileBakeData p0,
                Dictionary<string, List<Renderer>> rendererBuckets,
                Dictionary<string, List<ReflectionProbe>> reflectionBuckets,
                Renderer[] mappedRenderers,
                Dictionary<Renderer, Material[]> baseMaterials,
                ReflectionPolicy reflectionPolicy,
                int expectedMapCount,
                int expectedRendererEntryCount,
                int expectedProbeCount,
                int expectedEmissionCount)
            {
                this.role = role;
                this.root = root;
                this.switcher = switcher;
                this.bakeSet = bakeSet;
                this.p100 = p100;
                this.p0 = p0;
                this.rendererBuckets = rendererBuckets;
                this.reflectionBuckets = reflectionBuckets;
                this.mappedRenderers = mappedRenderers;
                this.baseMaterials = baseMaterials;
                this.reflectionPolicy = reflectionPolicy;
                this.expectedMapCount = expectedMapCount;
                this.expectedRendererEntryCount = expectedRendererEntryCount;
                this.expectedProbeCount = expectedProbeCount;
                this.expectedEmissionCount = expectedEmissionCount;
            }

            public static RoomAdapter Create(
                string role,
                Transform root,
                int expectedMapCount,
                int expectedRendererEntryCount,
                int expectedProbeCount,
                int expectedEmissionCount,
                Scene previewScene)
            {
                if (root == null || root.gameObject.scene != previewScene)
                    throw new InvalidOperationException(role + " room is not in preview scene.");

                DungeonTileLightmapSwitcher[] switchers =
                    root.GetComponentsInChildren<DungeonTileLightmapSwitcher>(true);
                DungeonTilePowerBakeSet[] sets =
                    root.GetComponentsInChildren<DungeonTilePowerBakeSet>(true);
                if (switchers.Length != 1 || sets.Length != 1 ||
                    switchers[0].transform != sets[0].transform)
                {
                    throw new InvalidOperationException(
                        role + " must have one co-located switcher/bake set.");
                }

                DungeonTileLightmapSwitcher switcher = switchers[0];
                DungeonTilePowerBakeSet set = sets[0];
                DungeonTileBakeData p100 = switcher.GetBakeData(
                    DungeonTileLightmapSwitcher.PowerLevel.P100);
                DungeonTileBakeData p0 = switcher.GetBakeData(
                    DungeonTileLightmapSwitcher.PowerLevel.P0);
                if (p100 == null || p0 == null ||
                    p100 != set.Power100Bake || p0 != set.Power00Bake)
                {
                    throw new InvalidOperationException(
                        role + " switcher public data disagrees with bake-set getters.");
                }

                ValidateBakePair(
                    role,
                    p100,
                    p0,
                    expectedMapCount,
                    expectedRendererEntryCount,
                    expectedProbeCount);
                if (set.EmissionMaterialEntries.Length != expectedEmissionCount)
                    throw new InvalidOperationException(role + " emission entry count changed.");

                Dictionary<string, List<Renderer>> buckets =
                    BuildBuckets<Renderer>(switcher.transform);
                Renderer[] mapped = ResolveMappedRenderers(
                    role,
                    p100.rendererEntries,
                    buckets);
                var baseMaterials = new Dictionary<Renderer, Material[]>();
                Renderer[] all = switcher.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < all.Length; i++)
                    baseMaterials.Add(all[i], (Material[])all[i].sharedMaterials.Clone());

                Dictionary<string, List<ReflectionProbe>> reflectionBuckets =
                    BuildBuckets<ReflectionProbe>(switcher.transform);
                ReflectionPolicy policy = ReflectionPolicy.Capture(
                    switcher,
                    reflectionBuckets,
                    previewScene);
                if (!policy.DisableOnP0)
                {
                    throw new InvalidOperationException(
                        role + " production-exact P0 reflection suppression changed.");
                }

                return new RoomAdapter(
                    role,
                    root,
                    switcher,
                    set,
                    p100,
                    p0,
                    buckets,
                    reflectionBuckets,
                    mapped,
                    baseMaterials,
                    policy,
                    expectedMapCount,
                    expectedRendererEntryCount,
                    expectedProbeCount,
                    expectedEmissionCount);
            }

            public void PrepareForManualCaptureWhileInactive()
            {
                if (root.gameObject.activeInHierarchy || switcher == null || !switcher.enabled)
                {
                    throw new InvalidOperationException(
                        role + " switcher manual-capture precondition changed.");
                }

                var serialized = new SerializedObject(switcher);
                serialized.UpdateIfRequiredOrScript();
                SerializedProperty applyOnAwake = serialized.FindProperty("applyOnAwake");
                if (applyOnAwake == null ||
                    applyOnAwake.propertyType != SerializedPropertyType.Boolean ||
                    !applyOnAwake.boolValue)
                {
                    throw new InvalidOperationException(
                        role + " production applyOnAwake contract changed.");
                }
                applyOnAwake.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                switcher.enabled = false;
                AssertManualCaptureControl();
            }

            public void AssertManualCaptureControl()
            {
                if (switcher == null || switcher.enabled)
                {
                    throw new InvalidOperationException(
                        role + " cloned switcher escaped manual capture control.");
                }
                var serialized = new SerializedObject(switcher);
                serialized.UpdateIfRequiredOrScript();
                SerializedProperty applyOnAwake = serialized.FindProperty("applyOnAwake");
                if (applyOnAwake == null || applyOnAwake.boolValue)
                {
                    throw new InvalidOperationException(
                        role + " cloned switcher applyOnAwake was re-enabled.");
                }
            }

            public DungeonTileBakeData ResolveData(bool p100State)
            {
                DungeonTileBakeData data = switcher.GetBakeData(
                    p100State
                        ? DungeonTileLightmapSwitcher.PowerLevel.P100
                        : DungeonTileLightmapSwitcher.PowerLevel.P0);
                DungeonTileBakeData expected = p100State ? p100 : p0;
                if (data != expected)
                    throw new InvalidOperationException(role + " bake data reference changed.");
                return data;
            }

            public void ApplyState(
                bool p100State,
                DungeonTileBakeData data,
                int[] lightmapRemap)
            {
                if (data != (p100State ? p100 : p0))
                    throw new InvalidOperationException(role + " state/data mismatch.");
                if (lightmapRemap == null || lightmapRemap.Length != expectedMapCount)
                    throw new InvalidOperationException(role + " lightmap remap mismatch.");

                currentAssignments = ApplyRendererEntries(
                    role,
                    data.rendererEntries,
                    rendererBuckets,
                    lightmapRemap);
                currentExpectedMaterials = ApplyEmissionMaterials(p100State);
                reflectionPolicy.Apply(
                    p100State,
                    data,
                    reflectionBuckets,
                    switcher.transform,
                    mappedRenderers);
                CurrentData = data;
                currentP100 = p100State;
                AssertState(p100State);
            }

            public void AssertState(bool p100State)
            {
                if (CurrentData == null || currentP100 != p100State ||
                    CurrentData != (p100State ? p100 : p0))
                {
                    throw new InvalidOperationException(role + " current state mismatch.");
                }
                if (currentAssignments == null ||
                    currentAssignments.Count != expectedRendererEntryCount)
                {
                    throw new InvalidOperationException(role + " renderer assignment count changed.");
                }

                foreach (KeyValuePair<Renderer, RendererAssignment> pair in currentAssignments)
                {
                    Renderer renderer = pair.Key;
                    RendererAssignment assignment = pair.Value;
                    if (renderer == null || renderer.lightmapIndex != assignment.GlobalIndex ||
                        renderer.lightmapScaleOffset != assignment.ScaleOffset)
                    {
                        throw new InvalidOperationException(
                            role + " renderer lightmap assignment drifted.");
                    }
                }

                foreach (KeyValuePair<Renderer, Material[]> pair in currentExpectedMaterials)
                {
                    Material[] current = pair.Key.sharedMaterials;
                    if (!SameReferences(current, pair.Value))
                        throw new InvalidOperationException(role + " emission material drifted.");
                }
                reflectionPolicy.AssertState(p100State, reflectionBuckets);
            }

            public void AssertRoomMappedRenderersHaveNoPropertyBlocks()
            {
                for (int i = 0; i < mappedRenderers.Length; i++)
                {
                    if (mappedRenderers[i] == null || mappedRenderers[i].HasPropertyBlock())
                    {
                        throw new InvalidOperationException(
                            role + " baked room renderer received a MaterialPropertyBlock.");
                    }
                }
            }

            public Bounds ResolveWorldBounds()
            {
                Tile tile = switcher.GetComponent<Tile>();
                if (tile == null)
                    tile = switcher.GetComponentInParent<Tile>();
                if (tile != null && tile.Bounds.size.sqrMagnitude > 0.01f)
                    return tile.Bounds;

                bool has = false;
                Bounds bounds = default;
                for (int i = 0; i < mappedRenderers.Length; i++)
                {
                    Renderer renderer = mappedRenderers[i];
                    if (renderer == null || !renderer.enabled)
                        continue;
                    if (!has)
                    {
                        bounds = renderer.bounds;
                        has = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }
                if (!has)
                    throw new InvalidOperationException(role + " has no world bounds.");
                return bounds;
            }

            public bool OwnsTile(Tile tile)
            {
                return tile != null &&
                       (tile.transform == root || tile.transform.IsChildOf(root));
            }

            public void AddEmissionMaterials(HashSet<Material> destination)
            {
                DungeonTilePowerBakeSet.EmissionMaterialEntry[] entries =
                    bakeSet.EmissionMaterialEntries;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].power100Material != null)
                        destination.Add(entries[i].power100Material);
                    if (entries[i].power00Material != null)
                        destination.Add(entries[i].power00Material);
                }
            }

            private Dictionary<Renderer, Material[]> ApplyEmissionMaterials(bool p100State)
            {
                var expected = new Dictionary<Renderer, Material[]>();
                var controlled = new HashSet<Renderer>();
                foreach (KeyValuePair<Renderer, Material[]> pair in baseMaterials)
                    expected.Add(pair.Key, (Material[])pair.Value.Clone());

                DungeonTilePowerBakeSet.EmissionMaterialEntry[] entries =
                    bakeSet.EmissionMaterialEntries;
                if (entries.Length != expectedEmissionCount)
                    throw new InvalidOperationException(role + " emission schema changed.");

                for (int i = 0; i < entries.Length; i++)
                {
                    DungeonTilePowerBakeSet.EmissionMaterialEntry entry = entries[i];
                    if (string.IsNullOrWhiteSpace(entry.relativePath) ||
                        !rendererBuckets.TryGetValue(entry.relativePath, out List<Renderer> bucket) ||
                        entry.rendererBucketIndex < 0 ||
                        entry.rendererBucketIndex >= bucket.Count)
                    {
                        throw new InvalidOperationException(role + " invalid emission renderer bucket.");
                    }
                    Renderer renderer = bucket[entry.rendererBucketIndex];
                    Material[] materials = expected[renderer];
                    if (entry.materialIndex < 0 || entry.materialIndex >= materials.Length)
                        throw new InvalidOperationException(role + " invalid emission material slot.");

                    IgnoreEmissionControl marker =
                        renderer.GetComponentInParent<IgnoreEmissionControl>(true);
                    if (marker != null && marker.enabled)
                        continue;

                    Material replacement = p100State
                        ? FirstNonNull(entry.power100Material, entry.power00Material)
                        : FirstNonNull(entry.power00Material, entry.power100Material);
                    if (replacement == null)
                        throw new InvalidOperationException(role + " emission variant is null.");
                    materials[entry.materialIndex] = replacement;
                    controlled.Add(renderer);
                }

                foreach (KeyValuePair<Renderer, Material[]> pair in expected)
                {
                    if (controlled.Contains(pair.Key))
                    {
                        if (!SameReferences(pair.Key.sharedMaterials, pair.Value))
                            pair.Key.sharedMaterials = pair.Value;
                    }
                    else if (!SameReferences(pair.Key.sharedMaterials, pair.Value))
                    {
                        throw new InvalidOperationException(
                            role + " non-emission renderer material array drifted.");
                    }
                }
                return expected;
            }

            private static Renderer[] ResolveMappedRenderers(
                string role,
                DungeonTileBakeData.RendererBakeEntry[] entries,
                Dictionary<string, List<Renderer>> buckets)
            {
                var use = new Dictionary<string, int>(StringComparer.Ordinal);
                var result = new Renderer[entries.Length];
                var unique = new HashSet<Renderer>();
                for (int i = 0; i < entries.Length; i++)
                {
                    string path = entries[i].relativePath;
                    if (!buckets.TryGetValue(path, out List<Renderer> bucket))
                        throw new InvalidOperationException(role + " missing renderer path: " + path);
                    use.TryGetValue(path, out int index);
                    if (index < 0 || index >= bucket.Count || bucket[index] == null)
                        throw new InvalidOperationException(role + " exhausted renderer bucket: " + path);
                    result[i] = bucket[index];
                    use[path] = index + 1;
                    if (!unique.Add(result[i]))
                        throw new InvalidOperationException(role + " renderer mapped twice.");
                }
                return result;
            }

            private static void ValidateBakePair(
                string role,
                DungeonTileBakeData p100,
                DungeonTileBakeData p0,
                int expectedMaps,
                int expectedRenderers,
                int expectedProbes)
            {
                ValidateBake(role + " P100", p100, expectedMaps, expectedRenderers, expectedProbes);
                ValidateBake(role + " P0", p0, expectedMaps, expectedRenderers, expectedProbes);

                bool stDiffers = false;
                for (int i = 0; i < expectedRenderers; i++)
                {
                    DungeonTileBakeData.RendererBakeEntry a = p100.rendererEntries[i];
                    DungeonTileBakeData.RendererBakeEntry b = p0.rendererEntries[i];
                    if (!string.Equals(a.relativePath, b.relativePath, StringComparison.Ordinal))
                        throw new InvalidOperationException(role + " P0/P100 path order differs.");
                    if (a.lightmapScaleOffset != b.lightmapScaleOffset)
                        stDiffers = true;
                }
                if (!stDiffers)
                    throw new InvalidOperationException(role + " expected P0/P100 ST variation is absent.");
            }

            private static void ValidateBake(
                string label,
                DungeonTileBakeData data,
                int expectedMaps,
                int expectedRenderers,
                int expectedProbes)
            {
                if (data.lightmapColors == null || data.lightmapColors.Length != expectedMaps ||
                    data.lightmapDirections == null ||
                    data.lightmapDirections.Length != expectedMaps ||
                    data.rendererEntries == null ||
                    data.rendererEntries.Length != expectedRenderers ||
                    data.lightProbeEntries == null ||
                    data.lightProbeEntries.Length != expectedProbes)
                {
                    throw new InvalidOperationException(label + " bake schema/count changed.");
                }
                for (int i = 0; i < expectedMaps; i++)
                {
                    if (data.lightmapColors[i] == null || data.lightmapDirections[i] == null)
                        throw new InvalidOperationException(label + " has null color/direction map.");
                }
            }
        }

        private sealed class ReflectionPolicy
        {
            private const float TileInset = 0.2f;

            private readonly DungeonTileLightmapSwitcher.ReflectionProbeApplyMode mode;
            private readonly ReflectionVariant[] variants;
            private ReflectionState[] expectedStates;

            public readonly bool DisableOnP0;

            private ReflectionPolicy(
                DungeonTileLightmapSwitcher.ReflectionProbeApplyMode mode,
                bool disableOnP0,
                ReflectionVariant[] variants)
            {
                this.mode = mode;
                DisableOnP0 = disableOnP0;
                this.variants = variants;
            }

            public static ReflectionPolicy Capture(
                DungeonTileLightmapSwitcher switcher,
                Dictionary<string, List<ReflectionProbe>> buckets,
                Scene previewScene)
            {
                var serialized = new SerializedObject(switcher);
                serialized.UpdateIfRequiredOrScript();
                SerializedProperty modeProperty =
                    serialized.FindProperty("reflectionProbeApplyMode");
                SerializedProperty disableProperty =
                    serialized.FindProperty("disableReflectionProbesOnPower0");
                SerializedProperty variantsProperty =
                    serialized.FindProperty("reflectionProbeVariantEntries");
                if (modeProperty == null || disableProperty == null ||
                    variantsProperty == null || !variantsProperty.isArray)
                {
                    throw new InvalidOperationException(
                        "Serialized reflection policy schema changed.");
                }

                var mode =
                    (DungeonTileLightmapSwitcher.ReflectionProbeApplyMode)
                    modeProperty.enumValueIndex;
                var variants = new ReflectionVariant[variantsProperty.arraySize];
                for (int i = 0; i < variants.Length; i++)
                {
                    SerializedProperty element = variantsProperty.GetArrayElementAtIndex(i);
                    string path = element.FindPropertyRelative("relativePath").stringValue;
                    int bucket = element.FindPropertyRelative("probeBucketIndex").intValue;
                    ReflectionProbe power100 = element
                        .FindPropertyRelative("power100Probe").objectReferenceValue as ReflectionProbe;
                    ReflectionProbe power0 = element
                        .FindPropertyRelative("power00Probe").objectReferenceValue as ReflectionProbe;
                    if ((power100 != null && power100.gameObject.scene != previewScene) ||
                        (power0 != null && power0.gameObject.scene != previewScene))
                    {
                        throw new InvalidOperationException(
                            "Reflection variant reference escaped the preview clone.");
                    }
                    if (bucket < -1)
                    {
                        throw new InvalidOperationException(
                            "Serialized reflection probeBucketIndex only permits -1 sentinel or >=0.");
                    }
                    if (bucket >= 0 &&
                        (string.IsNullOrWhiteSpace(path) ||
                         !buckets.TryGetValue(path, out List<ReflectionProbe> probeBucket) ||
                         bucket >= probeBucket.Count))
                    {
                        throw new InvalidOperationException(
                            "Serialized reflection variant base bucket is invalid.");
                    }
                    variants[i] = new ReflectionVariant(
                        path,
                        bucket,
                        power100,
                        power0);
                }

                return new ReflectionPolicy(mode, disableProperty.boolValue, variants);
            }

            public void Apply(
                bool p100,
                DungeonTileBakeData data,
                Dictionary<string, List<ReflectionProbe>> buckets,
                Transform tileRoot,
                Renderer[] mappedRenderers)
            {
                if (!p100 && DisableOnP0)
                {
                    SetAllEnabled(buckets, false);
                    expectedStates = CaptureStates(buckets);
                    return;
                }

                if (mode != DungeonTileLightmapSwitcher.ReflectionProbeApplyMode.ProbeVariantSet)
                    ApplyBakeEntries(data.reflectionProbeEntries, buckets);

                for (int i = 0; i < variants.Length; i++)
                {
                    ReflectionVariant entry = variants[i];
                    ReflectionProbe target = mode ==
                                             DungeonTileLightmapSwitcher.ReflectionProbeApplyMode.ProbeVariantSet
                        ? FirstNonNull(
                            p100 ? entry.Power100 : entry.Power0,
                            p100 ? entry.Power0 : entry.Power100)
                        : null;
                    SetVariantEnabled(entry.Power100, entry.Power100 == target);
                    SetVariantEnabled(entry.Power0, entry.Power0 == target);
                    if (mode == DungeonTileLightmapSwitcher.ReflectionProbeApplyMode.ProbeVariantSet &&
                        entry.BucketIndex >= 0)
                    {
                        ReflectionProbe baseProbe = buckets[entry.Path][entry.BucketIndex];
                        if (baseProbe != null)
                            baseProbe.enabled = false;
                    }
                }

                ClampEnabledProbes(buckets, tileRoot, mappedRenderers);
                expectedStates = CaptureStates(buckets);
            }

            public void AssertState(
                bool p100,
                Dictionary<string, List<ReflectionProbe>> buckets)
            {
                if (expectedStates == null)
                    throw new InvalidOperationException("Reflection state was not applied.");
                ReflectionState[] current = CaptureStates(buckets);
                if (current.Length != expectedStates.Length)
                    throw new InvalidOperationException("Reflection probe count changed.");
                for (int i = 0; i < current.Length; i++)
                    expectedStates[i].AssertSame(current[i]);

                if (!p100 && DisableOnP0)
                {
                    for (int i = 0; i < current.Length; i++)
                    {
                        if (current[i].Enabled)
                            throw new InvalidOperationException("P0 reflection residual leaked into base.");
                    }
                }
            }

            private void ApplyBakeEntries(
                DungeonTileBakeData.ReflectionProbeBakeEntry[] entries,
                Dictionary<string, List<ReflectionProbe>> buckets)
            {
                entries ??= Array.Empty<DungeonTileBakeData.ReflectionProbeBakeEntry>();
                var use = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < entries.Length; i++)
                {
                    DungeonTileBakeData.ReflectionProbeBakeEntry entry = entries[i];
                    if (!buckets.TryGetValue(entry.relativePath, out List<ReflectionProbe> bucket))
                        throw new InvalidOperationException("Missing reflection probe path.");
                    use.TryGetValue(entry.relativePath, out int index);
                    if (index < 0 || index >= bucket.Count || bucket[index] == null)
                        throw new InvalidOperationException("Exhausted reflection probe bucket.");
                    ReflectionProbe probe = bucket[index];
                    use[entry.relativePath] = index + 1;
                    probe.enabled = true;
                    ApplyBake(probe, entry.bakedTexture);
                }
            }

            private void ApplyBake(ReflectionProbe probe, Cubemap texture)
            {
                if (mode == DungeonTileLightmapSwitcher.ReflectionProbeApplyMode.ForceBakedMode)
                {
                    probe.customBakedTexture = null;
                    probe.mode = ReflectionProbeMode.Baked;
                    return;
                }
                if (texture != null)
                {
                    probe.customBakedTexture = texture;
                    probe.mode = ReflectionProbeMode.Custom;
                }
                else if (probe.mode == ReflectionProbeMode.Custom ||
                         probe.customBakedTexture != null)
                {
                    probe.customBakedTexture = null;
                    probe.mode = ReflectionProbeMode.Baked;
                }
            }

            private static void SetVariantEnabled(ReflectionProbe probe, bool enabled)
            {
                if (probe == null)
                    return;
                if (enabled && probe.customBakedTexture != null)
                    probe.mode = ReflectionProbeMode.Custom;
                probe.enabled = enabled;
            }

            private static void SetAllEnabled(
                Dictionary<string, List<ReflectionProbe>> buckets,
                bool enabled)
            {
                foreach (List<ReflectionProbe> bucket in buckets.Values)
                {
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        if (bucket[i] != null)
                            bucket[i].enabled = enabled;
                    }
                }
            }

            private static void ClampEnabledProbes(
                Dictionary<string, List<ReflectionProbe>> buckets,
                Transform tileRoot,
                Renderer[] mappedRenderers)
            {
                Bounds tileBounds = ResolveTileBounds(tileRoot, mappedRenderers);
                Bounds inset = tileBounds;
                inset.Expand(-TileInset);
                if (inset.size.x < 0.5f || inset.size.y < 0.5f || inset.size.z < 0.5f)
                    inset = tileBounds;

                var enabled = new List<ReflectionProbe>();
                foreach (List<ReflectionProbe> bucket in buckets.Values)
                {
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        if (bucket[i] != null && bucket[i].enabled)
                            enabled.Add(bucket[i]);
                    }
                }
                for (int i = 0; i < enabled.Count; i++)
                {
                    ReflectionProbe probe = enabled[i];
                    Bounds target = enabled.Count == 1
                        ? inset
                        : Intersect(probe.bounds, inset);
                    if (target.size.x < 0.25f || target.size.y < 0.25f ||
                        target.size.z < 0.25f)
                    {
                        continue;
                    }
                    probe.center = target.center - probe.transform.position;
                    probe.size = target.size;
                }
            }

            private static Bounds ResolveTileBounds(
                Transform root,
                Renderer[] renderers)
            {
                Tile tile = root.GetComponent<Tile>();
                if (tile == null)
                    tile = root.GetComponentInParent<Tile>();
                if (tile != null && tile.Bounds.size.sqrMagnitude > 0.01f)
                    return tile.Bounds;

                bool has = false;
                Bounds bounds = default;
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null || !renderer.enabled)
                        continue;
                    if (!has)
                    {
                        bounds = renderer.bounds;
                        has = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }
                if (!has)
                    throw new InvalidOperationException("Cannot resolve tile bounds.");
                return bounds;
            }

            private static Bounds Intersect(Bounds a, Bounds b)
            {
                Vector3 min = Vector3.Max(a.min, b.min);
                Vector3 max = Vector3.Min(a.max, b.max);
                if (min.x > max.x || min.y > max.y || min.z > max.z)
                    return new Bounds(a.center, Vector3.zero);
                var result = new Bounds();
                result.SetMinMax(min, max);
                return result;
            }

            private static ReflectionState[] CaptureStates(
                Dictionary<string, List<ReflectionProbe>> buckets)
            {
                var result = new List<ReflectionState>();
                foreach (KeyValuePair<string, List<ReflectionProbe>> pair in buckets)
                {
                    for (int i = 0; i < pair.Value.Count; i++)
                    {
                        if (pair.Value[i] != null)
                            result.Add(ReflectionState.Capture(pair.Value[i]));
                    }
                }
                return result.ToArray();
            }

            private readonly struct ReflectionVariant
            {
                public readonly string Path;
                public readonly int BucketIndex;
                public readonly ReflectionProbe Power100;
                public readonly ReflectionProbe Power0;

                public ReflectionVariant(
                    string path,
                    int bucketIndex,
                    ReflectionProbe power100,
                    ReflectionProbe power0)
                {
                    Path = path;
                    BucketIndex = bucketIndex;
                    Power100 = power100;
                    Power0 = power0;
                }
            }

            private readonly struct ReflectionState
            {
                public readonly ReflectionProbe Probe;
                public readonly bool Enabled;
                public readonly ReflectionProbeMode Mode;
                public readonly Texture CustomTexture;
                public readonly Vector3 Center;
                public readonly Vector3 Size;

                private ReflectionState(ReflectionProbe probe)
                {
                    Probe = probe;
                    Enabled = probe.enabled;
                    Mode = probe.mode;
                    CustomTexture = probe.customBakedTexture;
                    Center = probe.center;
                    Size = probe.size;
                }

                public static ReflectionState Capture(ReflectionProbe probe)
                {
                    return new ReflectionState(probe);
                }

                public void AssertSame(ReflectionState other)
                {
                    if (Probe != other.Probe || Enabled != other.Enabled ||
                        Mode != other.Mode || CustomTexture != other.CustomTexture ||
                        Center != other.Center || Size != other.Size)
                    {
                        throw new InvalidOperationException("Reflection probe state drifted.");
                    }
                }
            }
        }

        private sealed class CloneLightmapRegistry
        {
            private readonly List<LightmapData> lightmaps = new List<LightmapData>();

            public int[] Register(DungeonTileBakeData data)
            {
                Texture2D[] colors = data.lightmapColors ?? Array.Empty<Texture2D>();
                Texture2D[] directions =
                    data.lightmapDirections ?? Array.Empty<Texture2D>();
                var remap = new int[colors.Length];
                for (int i = 0; i < colors.Length; i++)
                {
                    Texture2D color = colors[i];
                    Texture2D direction = i < directions.Length ? directions[i] : null;
                    if (color == null || direction == null)
                        throw new InvalidOperationException("Null color/direction lightmap.");
                    int found = Find(color, direction);
                    if (found < 0)
                    {
                        lightmaps.Add(new LightmapData
                        {
                            lightmapColor = color,
                            lightmapDir = direction
                        });
                        found = lightmaps.Count - 1;
                    }
                    remap[i] = found;
                }
                return remap;
            }

            public LightmapData[] ToArray()
            {
                return lightmaps.ToArray();
            }

            private int Find(Texture2D color, Texture2D direction)
            {
                for (int i = 0; i < lightmaps.Count; i++)
                {
                    if (lightmaps[i].lightmapColor == color &&
                        lightmaps[i].lightmapDir == direction)
                    {
                        return i;
                    }
                }
                return -1;
            }
        }

        private readonly struct RendererAssignment
        {
            public readonly int GlobalIndex;
            public readonly Vector4 ScaleOffset;

            public RendererAssignment(int globalIndex, Vector4 scaleOffset)
            {
                GlobalIndex = globalIndex;
                ScaleOffset = scaleOffset;
            }
        }

        private sealed class DoorProbeAdapter
        {
            private const float ProductionDoorwayBlendHalfDepth = 1.35f;
            private const float ProductionDoorwayLateralPadding = 0.75f;
            private const float DynamicReceiverNearZoneSafetyMargin = 0.5f;

            private readonly Transform root;
            private readonly Transform leaf;
            private readonly Quaternion closedLocalRotation;
            private readonly Vector3 hingeAxis;
            private readonly float openAngleDegrees;
            private readonly Tile tileA;
            private readonly Tile tileB;
            private readonly RoomAdapter roomA;
            private readonly RoomAdapter roomB;
            private readonly float sideSampleOffset;
            private readonly DungeonDoorDualSideProbeReceiver receiver;
            private readonly DoorBinding[] bindings;
            private readonly RendererInvariant[] rendererInvariants;
            private DoorPose currentPose;
            private bool hasPose;
            private bool probesApplied;

            public int SplitRendererCount => bindings.Length;
            public Renderer[] AllRenderers { get; }

            private DoorProbeAdapter(
                Transform root,
                Transform leaf,
                Quaternion closedLocalRotation,
                Vector3 hingeAxis,
                float openAngleDegrees,
                Tile tileA,
                Tile tileB,
                RoomAdapter roomA,
                RoomAdapter roomB,
                float sideSampleOffset,
                DungeonDoorDualSideProbeReceiver receiver,
                DoorBinding[] bindings,
                Renderer[] allRenderers,
                RendererInvariant[] rendererInvariants)
            {
                this.root = root;
                this.leaf = leaf;
                this.closedLocalRotation = closedLocalRotation;
                this.hingeAxis = hingeAxis;
                this.openAngleDegrees = openAngleDegrees;
                this.tileA = tileA;
                this.tileB = tileB;
                this.roomA = roomA;
                this.roomB = roomB;
                this.sideSampleOffset = sideSampleOffset;
                this.receiver = receiver;
                this.bindings = bindings;
                AllRenderers = allRenderers;
                this.rendererInvariants = rendererInvariants;
            }

            public static DoorProbeAdapter Create(
                Transform root,
                DungeonPortalDoorAngleSource angleSource,
                RoomAdapter start,
                RoomAdapter administrative,
                Scene previewScene)
            {
                Transform leaf = root.Find(DoorLeafName);
                if (leaf == null || leaf.parent != root || angleSource.DoorLeaf != leaf)
                    throw new InvalidOperationException("Door leaf/angle-source binding changed.");

                SerializedObject angleSerialized = new SerializedObject(angleSource);
                angleSerialized.UpdateIfRequiredOrScript();
                Quaternion closed = angleSerialized
                    .FindProperty("closedLocalRotation").quaternionValue;
                Vector3 axis = angleSerialized
                    .FindProperty("localHingeAxis").vector3Value;
                float angle = angleSerialized
                    .FindProperty("openAngleDegrees").floatValue;
                if (axis.sqrMagnitude <= Mathf.Epsilon || Mathf.Abs(angle) <= 0.001f)
                    throw new InvalidOperationException("Door hinge configuration is invalid.");
                axis.Normalize();

                DunGen.Door[] doorComponents =
                    root.GetComponentsInChildren<DunGen.Door>(true);
                if (doorComponents.Length != 1 || doorComponents[0].transform != leaf)
                {
                    throw new InvalidOperationException(
                        "Expected exactly one DunGen.Door on the production Door_01 leaf.");
                }
                DunGen.Door door = doorComponents[0];
                if (door.TileA == null || door.TileB == null)
                    throw new InvalidOperationException("Door TileA/TileB contract is missing.");
                RoomAdapter roomA = ResolveRoomForTile(door.TileA, start, administrative);
                RoomAdapter roomB = ResolveRoomForTile(door.TileB, start, administrative);
                if (roomA == roomB)
                    throw new InvalidOperationException("Door TileA/TileB resolved to one room.");

                DungeonDoorDualSideProbeReceiver[] receivers =
                    root.GetComponentsInChildren<DungeonDoorDualSideProbeReceiver>(true);
                if (receivers.Length != 1 || receivers[0].transform != leaf)
                {
                    throw new InvalidOperationException(
                        "Expected one dual-side probe receiver on the production Door_01 leaf.");
                }
                DungeonDoorDualSideProbeReceiver receiver = receivers[0];
                SerializedObject receiverSerialized = new SerializedObject(receiver);
                receiverSerialized.UpdateIfRequiredOrScript();
                float offset = receiverSerialized
                    .FindProperty("sideSampleOffset").floatValue;
                bool animated = receiverSerialized
                    .FindProperty("blendSideProbesByAnimatedPose").boolValue;
                if (!Mathf.Approximately(offset, 0.35f) || !animated)
                {
                    throw new InvalidOperationException(
                        "Door +/-0.35m animated probe policy changed.");
                }

                Transform splitRoot = leaf.Find(DoorSplitRootName);
                if (splitRoot == null)
                    throw new InvalidOperationException("Door probe split root is missing.");
                DungeonDoorProbeRendererGroup[] groups =
                    splitRoot.GetComponentsInChildren<DungeonDoorProbeRendererGroup>(true);
                if (groups.Length != ExpectedDoorSplitRendererCount)
                    throw new InvalidOperationException("Door split renderer-group count changed.");

                leaf.localRotation = closed;
                var bindings = new DoorBinding[groups.Length];
                var uniqueGroups = new HashSet<DungeonDoorProbeRendererGroup.Group>();
                for (int i = 0; i < groups.Length; i++)
                {
                    Renderer renderer = groups[i].GetComponent<Renderer>();
                    if (renderer == null || !renderer.enabled ||
                        renderer.gameObject.scene != previewScene ||
                        !uniqueGroups.Add(groups[i].ProbeGroup))
                    {
                        throw new InvalidOperationException("Invalid door split renderer binding.");
                    }
                    ValidateDoorRendererName(renderer, groups[i].ProbeGroup);
                    Vector3 initialNormal = ResolveNormal(groups[i].ProbeGroup, groups[i].transform);
                    Tile fixedTile = groups[i].ProbeGroup ==
                                     DungeonDoorProbeRendererGroup.Group.Edge
                        ? null
                        : ResolveFacingTile(
                            renderer.bounds.center,
                            initialNormal,
                            door.TileA,
                            door.TileB);
                    bindings[i] = new DoorBinding(
                        renderer,
                        groups[i].ProbeGroup,
                        groups[i].transform,
                        fixedTile);
                }

                Renderer[] all = root.GetComponentsInChildren<Renderer>(true);
                int enabledCount = 0;
                var invariants = new RendererInvariant[all.Length];
                for (int i = 0; i < all.Length; i++)
                {
                    invariants[i] = RendererInvariant.Capture(all[i]);
                    if (all[i].enabled)
                        enabledCount++;
                }
                if (all.Length != ExpectedDoorRendererCount ||
                    enabledCount != ExpectedDoorEnabledRendererCount)
                {
                    throw new InvalidOperationException("Door renderer structure changed.");
                }

                return new DoorProbeAdapter(
                    root,
                    leaf,
                    closed,
                    axis,
                    angle,
                    door.TileA,
                    door.TileB,
                    roomA,
                    roomB,
                    offset,
                    receiver,
                    bindings,
                    all,
                    invariants);
            }

            public void PrepareForManualCaptureWhileInactive()
            {
                if (root.gameObject.activeInHierarchy || receiver == null || !receiver.enabled)
                {
                    throw new InvalidOperationException(
                        "Door probe receiver manual-capture precondition changed.");
                }
                receiver.enabled = false;
                AssertManualCaptureControl();
            }

            public void AssertManualCaptureControl()
            {
                if (receiver == null || receiver.enabled || tileA == null || tileB == null ||
                    !roomA.OwnsTile(tileA) || !roomB.OwnsTile(tileB))
                {
                    throw new InvalidOperationException(
                        "Door probe receiver or TileA/TileB manual-capture control drifted.");
                }
            }

            public void ApplyPose(DoorPose pose)
            {
                leaf.localRotation = closedLocalRotation * Quaternion.AngleAxis(
                    openAngleDegrees * pose.Fraction,
                    hingeAxis);
                currentPose = pose;
                hasPose = true;
                probesApplied = false;
            }

            public void ApplyProbes(
                DungeonTileBakeData roomAData,
                DungeonTileBakeData roomBData)
            {
                if (!hasPose)
                    throw new InvalidOperationException("Door pose must be applied first.");
                if (roomA.CurrentData != roomAData || roomB.CurrentData != roomBData)
                    throw new InvalidOperationException("Door room probe data mismatch.");

                for (int i = 0; i < bindings.Length; i++)
                {
                    DoorBinding binding = bindings[i];
                    if (binding.Group == DungeonDoorProbeRendererGroup.Group.Edge)
                    {
                        ApplyEdgeProbe(binding, roomAData, roomBData);
                    }
                    else
                    {
                        ApplyAnimatedSideProbe(binding, roomAData, roomBData);
                    }
                }
                probesApplied = true;
            }

            public void AssertPoseAndProbeState(DoorPose pose)
            {
                if (!hasPose || !probesApplied || currentPose.Percent != pose.Percent)
                    throw new InvalidOperationException("Door pose/probe state was not applied.");
                Quaternion expected = closedLocalRotation * Quaternion.AngleAxis(
                    openAngleDegrees * pose.Fraction,
                    hingeAxis);
                if (Quaternion.Angle(leaf.localRotation, expected) > 0.001f ||
                    !root.gameObject.activeInHierarchy)
                {
                    throw new InvalidOperationException("Door pose drifted.");
                }

                for (int i = 0; i < rendererInvariants.Length; i++)
                {
                    rendererInvariants[i].AssertCore(false);
                    rendererInvariants[i].AssertMaterialsUnchanged();
                }
                for (int i = 0; i < bindings.Length; i++)
                {
                    if (bindings[i].Renderer.lightProbeUsage != LightProbeUsage.CustomProvided ||
                        !bindings[i].Renderer.HasPropertyBlock())
                    {
                        throw new InvalidOperationException(
                            "Door split renderer did not receive clone-only baked SH.");
                    }
                }
            }

            public void ApplyCurrentDoorProbes()
            {
                ApplyProbes(roomA.CurrentData, roomB.CurrentData);
            }

            public bool IsInOrNearProductionDoorwayBlendZone(Vector3 worldPosition)
            {
                Vector3 axis = tileB.Bounds.center - tileA.Bounds.center;
                axis.y = 0f;
                if (axis.sqrMagnitude <= 0.0001f)
                {
                    axis = root.forward;
                    axis.y = 0f;
                }
                if (axis.sqrMagnitude <= 0.0001f)
                    throw new InvalidOperationException("Doorway blend axis is degenerate.");
                axis.Normalize();

                Bounds bounds = ResolveDoorBounds();
                Vector3 center = bounds.center;
                float horizontalExtent = Mathf.Max(bounds.extents.x, bounds.extents.z);
                float halfDepth = ProductionDoorwayBlendHalfDepth +
                                  DynamicReceiverNearZoneSafetyMargin;
                float lateralRadius = Mathf.Max(
                    0.5f,
                    horizontalExtent + ProductionDoorwayLateralPadding) +
                    DynamicReceiverNearZoneSafetyMargin;

                Vector3 offset = worldPosition - center;
                offset.y = 0f;
                float signedDistance = Vector3.Dot(offset, axis);
                if (Mathf.Abs(signedDistance) > halfDepth)
                    return false;
                Vector3 lateral = offset - axis * signedDistance;
                return lateral.sqrMagnitude <= lateralRadius * lateralRadius;
            }

            private Bounds ResolveDoorBounds()
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
                bool has = false;
                Bounds bounds = default;
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null)
                        continue;
                    if (!has)
                    {
                        bounds = renderers[i].bounds;
                        has = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderers[i].bounds);
                    }
                }
                if (!has)
                {
                    for (int i = 0; i < colliders.Length; i++)
                    {
                        if (colliders[i] == null)
                            continue;
                        if (!has)
                        {
                            bounds = colliders[i].bounds;
                            has = true;
                        }
                        else
                        {
                            bounds.Encapsulate(colliders[i].bounds);
                        }
                    }
                }
                if (!has)
                    throw new InvalidOperationException("Cannot resolve actual door bounds.");
                return bounds;
            }

            private void ApplyAnimatedSideProbe(
                DoorBinding binding,
                DungeonTileBakeData dataA,
                DungeonTileBakeData dataB)
            {
                Vector3 center = binding.Renderer.bounds.center;
                Vector3 normal = ResolveNormal(binding.Group, binding.OrientationRoot);
                Vector3 sample = center + normal * sideSampleOffset;
                if (!TrySampleFourNearest(roomA.Root, dataA, sample, out ProbeValue a) ||
                    !TrySampleFourNearest(roomB.Root, dataB, sample, out ProbeValue b))
                {
                    throw new InvalidOperationException("Door side SH sample failed.");
                }

                Vector3 dirA = DirectionToTile(center, tileA);
                Vector3 dirB = DirectionToTile(center, tileB);
                Vector3 axisToA = dirA - dirB;
                float weightA;
                if (axisToA.sqrMagnitude <= 0.0001f)
                {
                    weightA = binding.FixedTargetTile == tileA
                        ? 1f
                        : binding.FixedTargetTile == tileB ? 0f : 0.5f;
                }
                else
                {
                    float signed = Vector3.Dot(normal, axisToA.normalized);
                    weightA = Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.Clamp01(0.5f + signed * 0.5f));
                }
                ApplyProbe(
                    binding.Renderer,
                    BlendProbe(a, b, weightA, 1f - weightA));
            }

            private void ApplyEdgeProbe(
                DoorBinding binding,
                DungeonTileBakeData dataA,
                DungeonTileBakeData dataB)
            {
                Vector3 center = binding.Renderer.bounds.center;
                Vector3 sampleA = center + DirectionToTile(center, tileA) * sideSampleOffset;
                Vector3 sampleB = center + DirectionToTile(center, tileB) * sideSampleOffset;
                if (!TrySampleFourNearest(roomA.Root, dataA, sampleA, out ProbeValue a) ||
                    !TrySampleFourNearest(roomB.Root, dataB, sampleB, out ProbeValue b))
                {
                    throw new InvalidOperationException("Door edge SH sample failed.");
                }
                ApplyProbe(binding.Renderer, BlendProbe(a, b, 0.5f, 0.5f));
            }

            private static RoomAdapter ResolveRoomForTile(
                Tile tile,
                RoomAdapter start,
                RoomAdapter administrative)
            {
                bool inStart = start.OwnsTile(tile);
                bool inAdministrative = administrative.OwnsTile(tile);
                if (inStart == inAdministrative)
                    throw new InvalidOperationException("Cannot uniquely resolve door tile room.");
                return inStart ? start : administrative;
            }

            private static Tile ResolveFacingTile(
                Vector3 center,
                Vector3 normal,
                Tile tileA,
                Tile tileB)
            {
                float dotA = Vector3.Dot(normal, DirectionToTile(center, tileA));
                float dotB = Vector3.Dot(normal, DirectionToTile(center, tileB));
                return dotA >= dotB ? tileA : tileB;
            }

            private static Vector3 ResolveNormal(
                DungeonDoorProbeRendererGroup.Group group,
                Transform orientationRoot)
            {
                Vector3 normal = orientationRoot.TransformDirection(Vector3.forward);
                if (group == DungeonDoorProbeRendererGroup.Group.NegativeZ)
                    normal = -normal;
                if (normal.sqrMagnitude <= 0.0001f)
                    throw new InvalidOperationException("Door renderer normal is degenerate.");
                return normal.normalized;
            }

            private static Vector3 DirectionToTile(Vector3 center, Tile tile)
            {
                Vector3 direction = tile.Bounds.center - center;
                return direction.sqrMagnitude > 0.0001f
                    ? direction.normalized
                    : Vector3.zero;
            }

            private static void ValidateDoorRendererName(
                Renderer renderer,
                DungeonDoorProbeRendererGroup.Group group)
            {
                string expected = group == DungeonDoorProbeRendererGroup.Group.PositiveZ
                    ? DoorPositiveRendererName
                    : group == DungeonDoorProbeRendererGroup.Group.NegativeZ
                        ? DoorNegativeRendererName
                        : DoorEdgeRendererName;
                if (!string.Equals(renderer.name, expected, StringComparison.Ordinal))
                    throw new InvalidOperationException("Door split renderer name/group changed.");
            }

            private readonly struct DoorBinding
            {
                public readonly Renderer Renderer;
                public readonly DungeonDoorProbeRendererGroup.Group Group;
                public readonly Transform OrientationRoot;
                public readonly Tile FixedTargetTile;

                public DoorBinding(
                    Renderer renderer,
                    DungeonDoorProbeRendererGroup.Group group,
                    Transform orientationRoot,
                    Tile fixedTargetTile)
                {
                    Renderer = renderer;
                    Group = group;
                    OrientationRoot = orientationRoot;
                    FixedTargetTile = fixedTargetTile;
                }
            }
        }

        private sealed class CloneProbeAdapter
        {
            private readonly RoomAdapter start;
            private readonly RoomAdapter administrative;
            private readonly DoorProbeAdapter door;
            private readonly DynamicReceiverBinding[] receivers;
            private bool applied;

            public int DynamicReceiverCount => receivers.Length;

            private CloneProbeAdapter(
                RoomAdapter start,
                RoomAdapter administrative,
                DoorProbeAdapter door,
                DynamicReceiverBinding[] receivers)
            {
                this.start = start;
                this.administrative = administrative;
                this.door = door;
                this.receivers = receivers;
            }

            public static CloneProbeAdapter Create(
                Transform production,
                RoomAdapter start,
                RoomAdapter administrative,
                DoorProbeAdapter door)
            {
                DungeonDynamicProbeReceiver[] found =
                    production.GetComponentsInChildren<DungeonDynamicProbeReceiver>(true);
                var bindings = new DynamicReceiverBinding[found.Length];
                for (int i = 0; i < found.Length; i++)
                {
                    if (door.IsInOrNearProductionDoorwayBlendZone(
                            found[i].CurrentSamplePosition))
                    {
                        throw new InvalidOperationException(
                            "Dynamic receiver lies in/near the production doorway spatial-blend " +
                            "zone, which this base adapter deliberately does not emulate.");
                    }
                    Renderer[] renderers = found[i].GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0)
                        throw new InvalidOperationException("Dynamic receiver has no renderers.");
                    bindings[i] = new DynamicReceiverBinding(found[i], renderers);
                }
                return new CloneProbeAdapter(start, administrative, door, bindings);
            }

            public void Apply(
                DungeonTileBakeData startData,
                DungeonTileBakeData administrativeData)
            {
                for (int i = 0; i < receivers.Length; i++)
                {
                    Vector3 position = receivers[i].Receiver.CurrentSamplePosition;
                    RoomAdapter room = ResolveBestRoom(position);
                    DungeonTileBakeData data = room == start
                        ? startData
                        : administrativeData;
                    if (!TrySampleFourNearest(room.Root, data, position, out ProbeValue probe))
                        throw new InvalidOperationException("Dynamic receiver SH sample failed.");
                    for (int r = 0; r < receivers[i].Renderers.Length; r++)
                        ApplyProbe(receivers[i].Renderers[r], probe);
                }

                door.ApplyCurrentDoorProbes();
                applied = true;
            }

            public void AssertApplied()
            {
                if (!applied)
                    throw new InvalidOperationException("Clone-only SH adapter was not applied.");
                for (int i = 0; i < receivers.Length; i++)
                {
                    for (int r = 0; r < receivers[i].Renderers.Length; r++)
                    {
                        Renderer renderer = receivers[i].Renderers[r];
                        if (renderer.lightProbeUsage != LightProbeUsage.CustomProvided ||
                            !renderer.HasPropertyBlock())
                        {
                            throw new InvalidOperationException(
                                "Dynamic receiver did not retain clone-only SH.");
                        }
                    }
                }
            }

            public bool ContainsDynamicRenderer(Renderer renderer)
            {
                for (int i = 0; i < receivers.Length; i++)
                {
                    for (int r = 0; r < receivers[i].Renderers.Length; r++)
                    {
                        if (receivers[i].Renderers[r] == renderer)
                            return true;
                    }
                }
                return false;
            }

            private RoomAdapter ResolveBestRoom(Vector3 position)
            {
                Bounds a = start.ResolveWorldBounds();
                Bounds b = administrative.ResolveWorldBounds();
                float distanceA = a.Contains(position) ? 0f : a.SqrDistance(position);
                float distanceB = b.Contains(position) ? 0f : b.SqrDistance(position);
                return distanceA <= distanceB ? start : administrative;
            }

            private readonly struct DynamicReceiverBinding
            {
                public readonly DungeonDynamicProbeReceiver Receiver;
                public readonly Renderer[] Renderers;

                public DynamicReceiverBinding(
                    DungeonDynamicProbeReceiver receiver,
                    Renderer[] renderers)
                {
                    Receiver = receiver;
                    Renderers = renderers;
                }
            }
        }

        private readonly struct ProbeValue
        {
            public readonly SphericalHarmonicsL2 Harmonics;
            public readonly Vector4 Occlusion;

            public ProbeValue(SphericalHarmonicsL2 harmonics, Vector4 occlusion)
            {
                Harmonics = harmonics;
                Occlusion = occlusion;
            }
        }

        private static bool TrySampleFourNearest(
            Transform roomRoot,
            DungeonTileBakeData data,
            Vector3 worldPosition,
            out ProbeValue value)
        {
            value = default;
            DungeonTileBakeData.LightProbeBakeEntry[] entries =
                data != null
                    ? data.lightProbeEntries
                    : Array.Empty<DungeonTileBakeData.LightProbeBakeEntry>();
            if (roomRoot == null || entries == null || entries.Length == 0)
                return false;

            const int blendCount = 4;
            var distances = new float[blendCount];
            var indices = new int[blendCount];
            for (int i = 0; i < blendCount; i++)
            {
                distances[i] = float.PositiveInfinity;
                indices[i] = -1;
            }

            for (int probeIndex = 0; probeIndex < entries.Length; probeIndex++)
            {
                Vector3 sample = roomRoot.TransformPoint(entries[probeIndex].localPosition);
                float distance = (sample - worldPosition).sqrMagnitude;
                for (int slot = 0; slot < blendCount; slot++)
                {
                    if (distance >= distances[slot])
                        continue;
                    for (int shift = blendCount - 1; shift > slot; shift--)
                    {
                        distances[shift] = distances[shift - 1];
                        indices[shift] = indices[shift - 1];
                    }
                    distances[slot] = distance;
                    indices[slot] = probeIndex;
                    break;
                }
            }

            SphericalHarmonicsL2 harmonics = default;
            Vector4 occlusion = Vector4.zero;
            float totalWeight = 0f;
            for (int i = 0; i < blendCount; i++)
            {
                if (indices[i] < 0)
                    continue;
                float distance = Mathf.Sqrt(Mathf.Max(0f, distances[i]));
                float weight = 1f / Mathf.Max(0.05f, distance + 0.05f);
                DungeonTileBakeData.LightProbeBakeEntry entry = entries[indices[i]];
                AddWeightedProbe(ref harmonics, entry.ToSphericalHarmonics(), weight);
                occlusion += entry.occlusion * weight;
                totalWeight += weight;
            }
            if (totalWeight <= 0f)
                return false;
            ScaleProbe(ref harmonics, 1f / totalWeight);
            value = new ProbeValue(harmonics, occlusion / totalWeight);
            return true;
        }

        private static ProbeValue BlendProbe(
            ProbeValue a,
            ProbeValue b,
            float weightA,
            float weightB)
        {
            SphericalHarmonicsL2 result = default;
            for (int rgb = 0; rgb < 3; rgb++)
            {
                for (int coefficient = 0; coefficient < 9; coefficient++)
                {
                    result[rgb, coefficient] =
                        a.Harmonics[rgb, coefficient] * weightA +
                        b.Harmonics[rgb, coefficient] * weightB;
                }
            }
            return new ProbeValue(
                result,
                a.Occlusion * weightA + b.Occlusion * weightB);
        }

        private static void ApplyProbe(Renderer renderer, ProbeValue probe)
        {
            if (renderer == null)
                throw new InvalidOperationException("Cannot apply SH to null renderer.");
            renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
            var harmonics = new[] { probe.Harmonics };
            var occlusion = new[] { probe.Occlusion };
            var block = new MaterialPropertyBlock();
            int materialCount = renderer.sharedMaterials != null
                ? renderer.sharedMaterials.Length
                : 0;
            if (materialCount <= 0)
            {
                renderer.GetPropertyBlock(block);
                block.CopySHCoefficientArraysFrom(harmonics);
                block.CopyProbeOcclusionArrayFrom(occlusion);
                renderer.SetPropertyBlock(block);
                return;
            }
            for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
            {
                renderer.GetPropertyBlock(block, materialIndex);
                block.CopySHCoefficientArraysFrom(harmonics);
                block.CopyProbeOcclusionArrayFrom(occlusion);
                renderer.SetPropertyBlock(block, materialIndex);
            }
        }

        private static void AddWeightedProbe(
            ref SphericalHarmonicsL2 destination,
            SphericalHarmonicsL2 source,
            float weight)
        {
            for (int rgb = 0; rgb < 3; rgb++)
            {
                for (int coefficient = 0; coefficient < 9; coefficient++)
                    destination[rgb, coefficient] += source[rgb, coefficient] * weight;
            }
        }

        private static void ScaleProbe(ref SphericalHarmonicsL2 probe, float scale)
        {
            for (int rgb = 0; rgb < 3; rgb++)
            {
                for (int coefficient = 0; coefficient < 9; coefficient++)
                    probe[rgb, coefficient] *= scale;
            }
        }

        private sealed class RendererInvariant
        {
            public readonly Renderer Renderer;
            private readonly bool enabled;
            private readonly Mesh mesh;
            private readonly Mesh additionalVertexStreams;
            private readonly Material[] materials;
            private readonly LightProbeUsage lightProbeUsage;
            private readonly bool hasPropertyBlock;

            private RendererInvariant(Renderer renderer)
            {
                Renderer = renderer;
                enabled = renderer.enabled;
                mesh = GetSharedMesh(renderer);
                additionalVertexStreams = renderer is MeshRenderer meshRenderer
                    ? meshRenderer.additionalVertexStreams
                    : null;
                materials = (Material[])renderer.sharedMaterials.Clone();
                lightProbeUsage = renderer.lightProbeUsage;
                hasPropertyBlock = renderer.HasPropertyBlock();
            }

            public static RendererInvariant Capture(Renderer renderer)
            {
                if (renderer == null)
                    throw new InvalidOperationException("Cannot capture null renderer.");
                return new RendererInvariant(renderer);
            }

            public void AssertCore(bool allowMaterials)
            {
                if (Renderer == null || Renderer.enabled != enabled ||
                    GetSharedMesh(Renderer) != mesh)
                {
                    throw new InvalidOperationException("Renderer core structure changed.");
                }
                if (Renderer is MeshRenderer meshRenderer &&
                    meshRenderer.additionalVertexStreams != additionalVertexStreams)
                {
                    throw new InvalidOperationException("Additional vertex streams changed.");
                }
                if (!allowMaterials)
                    AssertMaterialsUnchanged();
            }

            public void AssertMaterialsUnchanged()
            {
                if (!SameReferences(Renderer.sharedMaterials, materials))
                    throw new InvalidOperationException("Renderer material array changed.");
            }

            public void AssertProbeAndPropertyBlockUnchanged()
            {
                if (Renderer.lightProbeUsage != lightProbeUsage ||
                    Renderer.HasPropertyBlock() != hasPropertyBlock)
                {
                    throw new InvalidOperationException(
                        "Non-receiver renderer probe/MaterialPropertyBlock state changed.");
                }
            }
        }

        private sealed class MaterialAssetInvariant
        {
            private readonly Material material;
            private readonly Shader shader;
            private readonly int renderQueue;
            private readonly string[] keywords;

            private MaterialAssetInvariant(Material material)
            {
                this.material = material;
                shader = material.shader;
                renderQueue = material.renderQueue;
                keywords = (string[])material.shaderKeywords.Clone();
            }

            public static MaterialAssetInvariant Capture(Material material)
            {
                return new MaterialAssetInvariant(material);
            }

            public void AssertUnchanged()
            {
                if (material == null || material.shader != shader ||
                    material.renderQueue != renderQueue ||
                    !SameStrings(material.shaderKeywords, keywords))
                {
                    throw new InvalidOperationException(
                        "Material shader/keyword/render-queue asset state changed.");
                }
            }
        }

        private sealed class SourceSceneSnapshot
        {
            private readonly Scene scene;
            private readonly GameObject root;
            private readonly bool rootActive;
            private readonly Vector3 rootPosition;
            private readonly Quaternion rootRotation;
            private readonly Vector3 rootScale;
            private readonly SourceRendererState[] renderers;
            private readonly SourceLightState[] lights;
            private readonly SourceCameraState[] cameras;
            private readonly Transform doorLeaf;
            private readonly Quaternion doorLeafRotation;

            private SourceSceneSnapshot(
                Scene scene,
                GameObject root,
                SourceRendererState[] renderers,
                SourceLightState[] lights,
                SourceCameraState[] cameras,
                Transform doorLeaf)
            {
                this.scene = scene;
                this.root = root;
                rootActive = root.activeSelf;
                rootPosition = root.transform.position;
                rootRotation = root.transform.rotation;
                rootScale = root.transform.localScale;
                this.renderers = renderers;
                this.lights = lights;
                this.cameras = cameras;
                this.doorLeaf = doorLeaf;
                doorLeafRotation = doorLeaf.localRotation;
            }

            public static SourceSceneSnapshot Capture(Scene scene, GameObject root)
            {
                Renderer[] sourceRenderers = root.GetComponentsInChildren<Renderer>(true);
                Light[] sourceLights = root.GetComponentsInChildren<Light>(true);
                Camera[] sourceCameras = root.GetComponentsInChildren<Camera>(true);
                var renderers = new SourceRendererState[sourceRenderers.Length];
                var lights = new SourceLightState[sourceLights.Length];
                var cameras = new SourceCameraState[sourceCameras.Length];
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i] = SourceRendererState.Capture(sourceRenderers[i]);
                for (int i = 0; i < lights.Length; i++)
                    lights[i] = SourceLightState.Capture(sourceLights[i]);
                for (int i = 0; i < cameras.Length; i++)
                    cameras[i] = SourceCameraState.Capture(sourceCameras[i]);

                Transform start = FindUniqueDescendant(
                    FindUniqueDescendant(root.transform, ProductionRoomsRootName, true),
                    StartRoomName,
                    true);
                Transform door = start.Find(AddedDoorRelativePath);
                Transform leaf = door != null ? door.Find(DoorLeafName) : null;
                if (leaf == null)
                    throw new InvalidOperationException("Source door leaf is missing.");
                return new SourceSceneSnapshot(
                    scene,
                    root,
                    renderers,
                    lights,
                    cameras,
                    leaf);
            }

            public void AssertUnchanged()
            {
                if (!scene.IsValid() || !scene.isLoaded || scene.isDirty ||
                    root == null || root.scene != scene || root.activeSelf != rootActive ||
                    root.transform.position != rootPosition ||
                    root.transform.rotation != rootRotation ||
                    root.transform.localScale != rootScale ||
                    doorLeaf == null || doorLeaf.localRotation != doorLeafRotation)
                {
                    throw new InvalidOperationException("Source scene/root/door state changed.");
                }
                if (root.GetComponentsInChildren<Renderer>(true).Length != renderers.Length ||
                    root.GetComponentsInChildren<Light>(true).Length != lights.Length ||
                    root.GetComponentsInChildren<Camera>(true).Length != cameras.Length)
                {
                    throw new InvalidOperationException("Source component counts changed.");
                }
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].AssertUnchanged();
                for (int i = 0; i < lights.Length; i++)
                    lights[i].AssertUnchanged();
                for (int i = 0; i < cameras.Length; i++)
                    cameras[i].AssertUnchanged();
            }
        }

        private sealed class SourceRendererState
        {
            private readonly RendererInvariant invariant;
            private readonly int lightmapIndex;
            private readonly Vector4 lightmapScaleOffset;
            private readonly int realtimeLightmapIndex;
            private readonly Vector4 realtimeLightmapScaleOffset;
            private readonly LightProbeUsage lightProbeUsage;
            private readonly ReflectionProbeUsage reflectionProbeUsage;
            private readonly bool hasPropertyBlock;

            private SourceRendererState(Renderer renderer)
            {
                invariant = RendererInvariant.Capture(renderer);
                lightmapIndex = renderer.lightmapIndex;
                lightmapScaleOffset = renderer.lightmapScaleOffset;
                realtimeLightmapIndex = renderer.realtimeLightmapIndex;
                realtimeLightmapScaleOffset = renderer.realtimeLightmapScaleOffset;
                lightProbeUsage = renderer.lightProbeUsage;
                reflectionProbeUsage = renderer.reflectionProbeUsage;
                hasPropertyBlock = renderer.HasPropertyBlock();
            }

            public static SourceRendererState Capture(Renderer renderer)
            {
                return new SourceRendererState(renderer);
            }

            public void AssertUnchanged()
            {
                invariant.AssertCore(false);
                Renderer renderer = invariant.Renderer;
                if (renderer.lightmapIndex != lightmapIndex ||
                    renderer.lightmapScaleOffset != lightmapScaleOffset ||
                    renderer.realtimeLightmapIndex != realtimeLightmapIndex ||
                    renderer.realtimeLightmapScaleOffset != realtimeLightmapScaleOffset ||
                    renderer.lightProbeUsage != lightProbeUsage ||
                    renderer.reflectionProbeUsage != reflectionProbeUsage ||
                    renderer.HasPropertyBlock() != hasPropertyBlock)
                {
                    throw new InvalidOperationException("Source renderer state changed.");
                }
            }
        }

        private sealed class SourceLightState
        {
            private readonly Light light;
            private readonly bool enabled;
            private readonly LightmapBakeType bakeType;
            private readonly float intensity;
            private readonly Color color;

            private SourceLightState(Light light)
            {
                this.light = light;
                enabled = light.enabled;
                bakeType = light.lightmapBakeType;
                intensity = light.intensity;
                color = light.color;
            }

            public static SourceLightState Capture(Light light)
            {
                return new SourceLightState(light);
            }

            public void AssertUnchanged()
            {
                if (light == null || light.enabled != enabled ||
                    light.lightmapBakeType != bakeType ||
                    !Mathf.Approximately(light.intensity, intensity) ||
                    light.color != color)
                {
                    throw new InvalidOperationException("Source Light state changed.");
                }
            }
        }

        private sealed class SourceCameraState
        {
            private readonly Camera camera;
            private readonly bool enabled;
            private readonly RenderTexture targetTexture;
            private readonly float aspect;
            private readonly Matrix4x4 projection;
            private readonly Component cameraData;
            private readonly PropertyInfo postProperty;
            private readonly bool postProcessing;

            private SourceCameraState(Camera camera)
            {
                this.camera = camera;
                enabled = camera.enabled;
                targetTexture = camera.targetTexture;
                aspect = camera.aspect;
                projection = camera.projectionMatrix;
                cameraData = FindUniqueUrpCameraData(camera);
                postProperty = cameraData.GetType().GetProperty(
                    "renderPostProcessing",
                    BindingFlags.Instance | BindingFlags.Public);
                postProcessing = (bool)postProperty.GetValue(cameraData);
            }

            public static SourceCameraState Capture(Camera camera)
            {
                return new SourceCameraState(camera);
            }

            public void AssertUnchanged()
            {
                if (camera == null || camera.enabled != enabled ||
                    camera.targetTexture != targetTexture ||
                    camera.aspect != aspect || camera.projectionMatrix != projection ||
                    (bool)postProperty.GetValue(cameraData) != postProcessing)
                {
                    throw new InvalidOperationException("Source Camera state changed.");
                }
            }
        }

        private sealed class SelectionSnapshot
        {
            private readonly Object[] objects;
            private readonly Object active;

            private SelectionSnapshot(Object[] objects, Object active)
            {
                this.objects = objects != null ? (Object[])objects.Clone() : Array.Empty<Object>();
                this.active = active;
            }

            public static SelectionSnapshot Capture()
            {
                return new SelectionSnapshot(Selection.objects, Selection.activeObject);
            }

            public void RestoreIfChanged()
            {
                if (!SameReferences(Selection.objects, objects))
                    Selection.objects = objects;
                if (Selection.activeObject != active)
                    Selection.activeObject = active;
            }

            public void AssertRestored()
            {
                if (!SameReferences(Selection.objects, objects) ||
                    Selection.activeObject != active)
                {
                    throw new InvalidOperationException("Editor selection was not restored.");
                }
            }
        }

        private sealed class GlobalRenderSnapshot
        {
            private readonly LightmapData[] lightmaps;
            private readonly LightmapsMode mode;
            private readonly LightingDataAsset lightingData;
            private readonly RenderTexture activeRenderTexture;
            private readonly bool srgbWrite;

            private GlobalRenderSnapshot(
                LightmapData[] lightmaps,
                LightmapsMode mode,
                LightingDataAsset lightingData,
                RenderTexture activeRenderTexture,
                bool srgbWrite)
            {
                this.lightmaps = lightmaps;
                this.mode = mode;
                this.lightingData = lightingData;
                this.activeRenderTexture = activeRenderTexture;
                this.srgbWrite = srgbWrite;
            }

            public static GlobalRenderSnapshot Capture()
            {
                LightmapData[] current =
                    LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
                return new GlobalRenderSnapshot(
                    (LightmapData[])current.Clone(),
                    LightmapSettings.lightmapsMode,
                    Lightmapping.lightingDataAsset,
                    RenderTexture.active,
                    GL.sRGBWrite);
            }

            public void Restore()
            {
                LightmapSettings.lightmaps = (LightmapData[])lightmaps.Clone();
                LightmapSettings.lightmapsMode = mode;
                Lightmapping.lightingDataAsset = lightingData;
                RenderTexture.active = activeRenderTexture;
                GL.sRGBWrite = srgbWrite;
            }

            public bool AreLightmapsRestored()
            {
                LightmapData[] current =
                    LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
                if (current.Length != lightmaps.Length)
                    return false;
                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i].lightmapColor != lightmaps[i].lightmapColor ||
                        current[i].lightmapDir != lightmaps[i].lightmapDir ||
                        current[i].shadowMask != lightmaps[i].shadowMask)
                    {
                        return false;
                    }
                }
                return true;
            }

            public bool IsModeRestored() => LightmapSettings.lightmapsMode == mode;
            public bool IsLightingDataRestored() =>
                Lightmapping.lightingDataAsset == lightingData;
            public bool IsRenderTextureActiveRestored() =>
                RenderTexture.active == activeRenderTexture;
            public bool IsSrgbWriteRestored() => GL.sRGBWrite == srgbWrite;

            public void AssertRestored()
            {
                if (!AreLightmapsRestored() || !IsModeRestored() ||
                    !IsLightingDataRestored() ||
                    !IsRenderTextureActiveRestored() || !IsSrgbWriteRestored())
                {
                    throw new InvalidOperationException("Global render state was not restored.");
                }
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
                        "BakedRoomBase_Presentation_Render",
                        24,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.sRGB,
                        msaa);
                    presentationResolve = CreateTarget(
                        "BakedRoomBase_Presentation_Resolve",
                        0,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.sRGB,
                        1);
                    hdrRender = CreateTarget(
                        "BakedRoomBase_HDR_Render",
                        24,
                        RenderTextureFormat.ARGBHalf,
                        RenderTextureReadWrite.Linear,
                        1);
                    hdrResolve = CreateTarget(
                        "BakedRoomBase_HDR_Resolve",
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
                    throw new InvalidOperationException("Unable to create render target: " + name);
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

        private static Dictionary<string, List<T>> BuildBuckets<T>(Transform root)
            where T : Component
        {
            var result = new Dictionary<string, List<T>>(StringComparer.Ordinal);
            T[] components = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < components.Length; i++)
            {
                T component = components[i];
                string path = GetRelativePath(root, component.transform);
                if (!result.TryGetValue(path, out List<T> bucket))
                {
                    bucket = new List<T>();
                    result.Add(path, bucket);
                }
                bucket.Add(component);
            }
            return result;
        }

        private static Dictionary<Renderer, RendererAssignment> ApplyRendererEntries(
            string role,
            DungeonTileBakeData.RendererBakeEntry[] entries,
            Dictionary<string, List<Renderer>> buckets,
            int[] remap)
        {
            var result = new Dictionary<Renderer, RendererAssignment>();
            var use = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                DungeonTileBakeData.RendererBakeEntry entry = entries[i];
                if (!buckets.TryGetValue(entry.relativePath, out List<Renderer> bucket))
                    throw new InvalidOperationException(role + " missing renderer path.");
                use.TryGetValue(entry.relativePath, out int bucketIndex);
                if (bucketIndex < 0 || bucketIndex >= bucket.Count)
                    throw new InvalidOperationException(role + " exhausted renderer bucket.");
                Renderer renderer = bucket[bucketIndex];
                use[entry.relativePath] = bucketIndex + 1;

                int globalIndex;
                if (entry.lightmapIndex < 0)
                {
                    globalIndex = -1;
                }
                else
                {
                    if (entry.lightmapIndex >= remap.Length)
                        throw new InvalidOperationException(role + " invalid local lightmap index.");
                    globalIndex = remap[entry.lightmapIndex];
                }
                renderer.lightmapIndex = globalIndex;
                renderer.lightmapScaleOffset = entry.lightmapScaleOffset;
                if (result.ContainsKey(renderer))
                    throw new InvalidOperationException(role + " renderer assigned twice.");
                result.Add(
                    renderer,
                    new RendererAssignment(globalIndex, entry.lightmapScaleOffset));
            }
            return result;
        }

        private static LightmapsMode ResolveLightmapsMode(DungeonTileBakeData data)
        {
            if (data.lightmapsMode == LightmapsMode.NonDirectional ||
                data.lightmapsMode == LightmapsMode.CombinedDirectional)
            {
                return data.lightmapsMode;
            }
            Texture2D[] directions =
                data.lightmapDirections ?? Array.Empty<Texture2D>();
            for (int i = 0; i < directions.Length; i++)
            {
                if (directions[i] != null)
                    return LightmapsMode.CombinedDirectional;
            }
            return LightmapsMode.NonDirectional;
        }

        private static Material FirstNonNull(Material first, Material second)
        {
            return first != null ? first : second;
        }

        private static ReflectionProbe FirstNonNull(
            ReflectionProbe first,
            ReflectionProbe second)
        {
            return first != null ? first : second;
        }

        private static Mesh GetSharedMesh(Renderer renderer)
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

        private static string GetRelativePath(Transform root, Transform target)
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
                throw new InvalidOperationException("Component is outside its room root.");
            return string.Join("/", names);
        }

        private static bool SameReferences<T>(T[] left, T[] right)
            where T : Object
        {
            left ??= Array.Empty<T>();
            right ??= Array.Empty<T>();
            if (left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }
            return true;
        }

        private static bool SameStrings(string[] left, string[] right)
        {
            left ??= Array.Empty<string>();
            right ??= Array.Empty<string>();
            if (left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static FixedCameraFingerprint CaptureAndValidateFixedCameraFingerprint(
            Camera camera,
            int index,
            Scene previewScene)
        {
            ValidateFixedCameraKnownProperties(camera, index);
            if (camera.scene != previewScene || camera.gameObject.scene != previewScene)
                throw new InvalidOperationException("Fixed camera is not preview-owned.");

            float originalAspect = camera.aspect;
            Matrix4x4 originalProjection = camera.projectionMatrix;
            try
            {
                camera.aspect = CaptureWidth / (float)CaptureHeight;
                camera.ResetProjectionMatrix();
                Matrix4x4 projection = camera.projectionMatrix;
                Matrix4x4 expected = Matrix4x4.Perspective(
                    camera.fieldOfView,
                    camera.aspect,
                    camera.nearClipPlane,
                    camera.farClipPlane);
                if (!Approximately(projection, expected, 0.00001f))
                {
                    throw new InvalidOperationException(
                        "Fixed camera capture projection is no longer standard perspective.");
                }

                string payload =
                    "name=" + camera.name +
                    ";position=" + FormatVector3(camera.transform.position) +
                    ";rotation=" + FormatQuaternion(camera.transform.rotation) +
                    ";fov=" + FormatDouble(camera.fieldOfView) +
                    ";near=" + FormatDouble(camera.nearClipPlane) +
                    ";far=" + FormatDouble(camera.farClipPlane) +
                    ";mask=" + camera.cullingMask +
                    ";aspect=" + FormatDouble(camera.aspect) +
                    ";projection=" + FormatMatrix(projection);
                return new FixedCameraFingerprint(
                    camera.name,
                    payload,
                    ComputeSha256(Encoding.UTF8.GetBytes(payload)));
            }
            finally
            {
                camera.aspect = originalAspect;
                camera.projectionMatrix = originalProjection;
            }
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
                camera.enabled || camera.orthographic || camera.usePhysicalProperties ||
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

        private static bool Approximately(
            Matrix4x4 left,
            Matrix4x4 right,
            float tolerance)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (Mathf.Abs(left[row, column] - right[row, column]) > tolerance)
                        return false;
                }
            }
            return true;
        }

        private static string FormatMatrix(Matrix4x4 value)
        {
            var builder = new StringBuilder(256);
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (builder.Length > 0)
                        builder.Append(',');
                    builder.Append(FormatDouble(value[row, column]));
                }
            }
            return builder.ToString();
        }

        private static string SanitizeFilename(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
            }
            return builder.ToString();
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

        private static string ComputeAssetFileSha256(string assetPath)
        {
            string normalized = NormalizePath(assetPath);
            if (!File.Exists(normalized))
                return "MISSING";
            return ComputeSha256(File.ReadAllBytes(normalized));
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    builder.Append(hash[i].ToString("X2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatVector3(Vector3 value)
        {
            return "(" + FormatDouble(value.x) + "," + FormatDouble(value.y) +
                   "," + FormatDouble(value.z) + ")";
        }

        private static string FormatQuaternion(Quaternion value)
        {
            return "(" + FormatDouble(value.x) + "," + FormatDouble(value.y) +
                   "," + FormatDouble(value.z) + "," + FormatDouble(value.w) + ")";
        }
    }
}
#endif
