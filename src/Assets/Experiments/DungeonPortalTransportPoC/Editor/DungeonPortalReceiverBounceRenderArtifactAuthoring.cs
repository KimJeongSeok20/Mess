using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DungeonPortalTransportPoC.Editor
{
    /// <summary>
    /// Persists the Stage A DirectOnly reconstruction produced by
    /// <see cref="DungeonPortalReceiverBounceDirectOnlyRenderSession"/>.  This tool owns
    /// only Generated/RenderArtifacts.  Captures, workspaces, source importers, endpoint
    /// profiles, connections and production assets are read-only inputs.
    /// </summary>
    public static class DungeonPortalReceiverBounceRenderArtifactAuthoring
    {
        private const string RootFolder = "Assets/Experiments/DungeonPortalTransportPoC";
        private const string CaptureRoot = RootFolder + "/Generated/Bounce";
        private const string ArtifactRoot = RootFolder + "/Generated/RenderArtifacts";
        private const string ObjectIdMrtShaderPath =
            RootFolder + "/Shaders/DungeonPortalReceiverBounceObjectIdMrt.shader";
        private const string ToolVersion = "DungeonPortalReceiverBounceRenderArtifactAuthoring/2";
        private const string CurrentFolderName = "Current";

        private static readonly ReceiverSpec StartSpec = new ReceiverSpec("StartRoom_R000");
        private static readonly ReceiverSpec AdminSpec =
            new ReceiverSpec("AdminstrativeSegregation_R000");

        private static bool renderInProgress;

        [MenuItem("Tools/Dungeon/Lighting/Portal Transport PoC/Stage A Render Artifact/StartRoom_R000")]
        public static void RenderStartFromMenu()
        {
            LogResult(RenderStart());
        }

        [MenuItem("Tools/Dungeon/Lighting/Portal Transport PoC/Stage A Render Artifact/AdminstrativeSegregation_R000")]
        public static void RenderAdminFromMenu()
        {
            LogResult(RenderAdmin());
        }

        [MenuItem("Tools/Dungeon/Lighting/Portal Transport PoC/Stage A Render Artifact/All Receivers")]
        public static void RenderAllFromMenu()
        {
            LogResult(RenderAll());
        }

        /// <summary>Unity -executeMethod entry point for the Start receiver.</summary>
        public static void RenderStartCli()
        {
            ThrowIfFailed(RenderStart());
        }

        /// <summary>Unity -executeMethod entry point for the Admin receiver.</summary>
        public static void RenderAdminCli()
        {
            ThrowIfFailed(RenderAdmin());
        }

        /// <summary>Unity -executeMethod entry point for both canonical receivers.</summary>
        public static void RenderAllCli()
        {
            ThrowIfFailed(RenderAll());
        }

        public static string RenderStart()
        {
            return RenderReceiver(StartSpec);
        }

        public static string RenderAdmin()
        {
            return RenderReceiver(AdminSpec);
        }

        public static string RenderAll()
        {
            string start = RenderReceiver(StartSpec);
            if (!IsPass(start))
                return start;

            string admin = RenderReceiver(AdminSpec);
            if (!IsPass(admin))
                return admin;

            return "PASS Stage A render artifacts were transactionally persisted for StartRoom_R000 and " +
                   "AdminstrativeSegregation_R000. Their individual READY/INCOMPLETE states remain " +
                   "derived from the saved evidence; no capture, profile, connection, source importer, bake, production asset " +
                   "or Play Mode was touched.";
        }

        /// <summary>
        /// Revalidates the currently persisted Stage A evidence from disk. Every later
        /// fitting/authoring stage must call this before trusting an artifact reference.
        /// This method is read-only and never attempts interrupted-transaction recovery.
        /// </summary>
        public static bool TryValidatePersistedArtifact(string receiverRoomId, out string failure)
        {
            failure = string.Empty;
            ReceiverSpec spec;
            if (string.Equals(receiverRoomId, StartSpec.RoomId, StringComparison.Ordinal))
                spec = StartSpec;
            else if (string.Equals(receiverRoomId, AdminSpec.RoomId, StringComparison.Ordinal))
                spec = AdminSpec;
            else
            {
                failure = "Unknown canonical Stage A receiver: '" + receiverRoomId + "'.";
                return false;
            }

            try
            {
                DungeonPortalReceiverResponseCapture capture =
                    AssetDatabase.LoadAssetAtPath<DungeonPortalReceiverResponseCapture>(spec.CapturePath);
                Shader maskShader = AssetDatabase.LoadAssetAtPath<Shader>(ObjectIdMrtShaderPath);
                if (capture == null || maskShader == null || EditorUtility.IsDirty(capture) ||
                    EditorUtility.IsDirty(maskShader))
                {
                    throw new InvalidOperationException("A persisted Stage A capture or shader input is missing/dirty.");
                }
                if (!DungeonPortalReceiverBounceBaker.TryValidatePersistedCaptureIntegrity(
                        capture,
                        out string captureFailure))
                {
                    throw new InvalidOperationException("Persisted capture integrity failed: " + captureFailure);
                }

                ReadOnlyInputGuard inputs = ReadOnlyInputGuard.Capture(spec, capture, maskShader);
                DungeonPortalReceiverBounceRenderArtifact artifact =
                    AssetDatabase.LoadAssetAtPath<DungeonPortalReceiverBounceRenderArtifact>(spec.ArtifactPath);
                ArtifactTransaction.AssertPersistedArtifact(artifact, capture, maskShader, inputs, spec);
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }

        private static string RenderReceiver(ReceiverSpec spec)
        {
            if (!TryValidateEditorState(out string preflightFailure))
                return "FAIL: " + preflightFailure;
            if (renderInProgress)
                return "FAIL: another Stage A render-artifact transaction is already in progress.";

            renderInProgress = true;
            DungeonPortalReceiverBounceDirectOnlyRenderSession.RenderedCameraFrame[] frames = null;
            ArtifactTransaction transaction = null;
            try
            {
                DungeonPortalReceiverResponseCapture capture =
                    AssetDatabase.LoadAssetAtPath<DungeonPortalReceiverResponseCapture>(spec.CapturePath);
                if (capture == null ||
                    !string.Equals(AssetDatabase.GetAssetPath(capture), spec.CapturePath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The exact persisted receiver capture is missing: '" + spec.CapturePath + "'.");
                }
                if (EditorUtility.IsDirty(capture))
                    throw new InvalidOperationException("The persisted receiver capture is dirty and was not read.");
                if (!string.Equals(capture.ReceiverRoomId, spec.RoomId, StringComparison.Ordinal) ||
                    !string.Equals(
                        capture.StableDoorwayId,
                        DungeonPortalReceiverBounceDirectOnlyRenderSession.StableDoorwayId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The exact capture does not bind the canonical receiver/doorway.");
                }

                Shader maskShader = AssetDatabase.LoadAssetAtPath<Shader>(ObjectIdMrtShaderPath);
                if (maskShader == null ||
                    !string.Equals(AssetDatabase.GetAssetPath(maskShader), ObjectIdMrtShaderPath, StringComparison.Ordinal) ||
                    EditorUtility.IsDirty(maskShader))
                {
                    throw new InvalidOperationException(
                        "The exact clean Stage A MRT shader is unavailable: '" + ObjectIdMrtShaderPath + "'.");
                }

                ReadOnlyInputGuard inputs = ReadOnlyInputGuard.Capture(spec, capture, maskShader);
                if (!DungeonPortalReceiverBounceBaker.TryValidatePersistedCaptureIntegrity(capture, out string integrityFailure))
                {
                    throw new InvalidOperationException(
                        "Persisted capture integrity validation failed before reconstruction: " + integrityFailure);
                }
                AssertEditorStateStillClean();
                inputs.AssertUnchanged();

                SessionEvidence sessionEvidence;
                DungeonPortalReceiverBounceDirectOnlyRenderSession session = null;
                try
                {
                    if (!DungeonPortalReceiverBounceDirectOnlyRenderSession.TryOpen(
                            capture,
                            maskShader,
                            out session,
                            out string openFailure))
                    {
                        throw new InvalidOperationException("Unable to open isolated DirectOnly render session: " + openFailure);
                    }
                    if (!session.TryRenderAll(out frames, out string renderFailure))
                        throw new InvalidOperationException("DirectOnly fixed-camera render failed: " + renderFailure);

                    sessionEvidence = SessionEvidence.Capture(session);
                }
                finally
                {
                    if (session != null)
                        session.Dispose();
                }

                AssertEditorStateStillClean();
                inputs.AssertUnchanged();

                transaction = ArtifactTransaction.Begin(spec, capture, maskShader, inputs);
                DungeonPortalReceiverBounceRenderArtifact stagedArtifact = transaction.Stage(
                    capture,
                    maskShader,
                    inputs,
                    sessionEvidence,
                    frames);
                inputs.AssertUnchanged();
                AssertEditorStateStillClean();

                DungeonPortalReceiverBounceRenderArtifact persisted = transaction.CommitAndValidate(
                    stagedArtifact,
                    capture,
                    maskShader,
                    inputs);

                string result = "PASS " + persisted.Status + " Stage A render artifact saved for " + spec.RoomId +
                                " at '" + spec.ArtifactPath + "'. artifactHash=" + persisted.ArtifactHash +
                                ". No bake, Play Mode, capture/profile/connection, source-importer or production asset " +
                                "mutation occurred; only transaction-owned generated texture importers were configured.";
                transaction = null;
                return result;
            }
            catch (Exception exception)
            {
                string rollbackFailure = transaction != null ? transaction.TryRollback() : string.Empty;
                string suffix = string.IsNullOrEmpty(rollbackFailure)
                    ? " Existing committed evidence was preserved."
                    : " ROLLBACK FAILURE: " + rollbackFailure;
                return "FAIL: Stage A render artifact for " + spec.RoomId + " was not committed: " +
                       exception.Message + suffix;
            }
            finally
            {
                DungeonPortalReceiverBounceDirectOnlyRenderSession.DisposeFrames(frames);
                if (transaction != null)
                    transaction.Dispose();
                renderInProgress = false;
            }
        }

        private static bool TryValidateEditorState(out string failure)
        {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                failure = "Unity is in or transitioning Play Mode; no scene or asset was touched.";
                return false;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                failure = "Unity is compiling or updating; no scene or asset was touched.";
                return false;
            }
            if (Lightmapping.isRunning)
            {
                failure = "another lightmapping operation is running; no scene or asset was touched.";
                return false;
            }
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                failure = "a Prefab Stage is open; close it before the isolated Stage A render transaction.";
                return false;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;
                if (string.IsNullOrWhiteSpace(scene.path))
                {
                    failure = "a loaded scene is untitled: '" + scene.name + "'. Save it before Stage A authoring.";
                    return false;
                }
                if (scene.isDirty)
                {
                    failure = "a loaded scene is dirty: '" + scene.name + "' (" + scene.path +"). " +
                              "Save or discard it before Stage A authoring.";
                    return false;
                }
            }

            if (SystemInfo.supportedRenderTargetCount < 2 ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.RGBA32))
            {
                failure = "the editor GPU cannot provide the exact HDR plus two-target Stage A render contract.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static void AssertEditorStateStillClean()
        {
            if (!TryValidateEditorState(out string failure))
                throw new InvalidOperationException("Editor state changed during Stage A authoring: " + failure);
        }

        private static DungeonPortalReceiverBounceRenderArtifact.RenderArtifactPayload BuildPayload(
            DungeonPortalReceiverResponseCapture capture,
            Shader maskShader,
            ReadOnlyInputGuard inputs,
            SessionEvidence evidence,
            DungeonPortalReceiverBounceRenderArtifact.CameraArtifact[] cameras)
        {
            DungeonPortalReceiverResponseCapture.CaptureProvenance provenance = capture.Provenance;
            DungeonPortalReceiverResponseCapture.CaptureState directOnly = capture.DirectOnly;
            bool reconstructedAvailable = cameras != null &&
                                          cameras.Length == DungeonPortalReceiverBounceRenderArtifact.FixedCameraCount;
            bool masksAvailable = reconstructedAvailable;
            bool allDriftWithinPolicy = reconstructedAvailable;
            bool allOrientationsVerified = reconstructedAvailable;
            bool allTargetRatiosPassed = reconstructedAvailable;
            bool allUnsupportedSentinelFree = reconstructedAvailable;
            for (int i = 0; i < (cameras != null ? cameras.Length : 0); i++)
            {
                masksAvailable &= cameras[i].stableObjectIdMask.texture != null &&
                                  cameras[i].doorwayLocalZWorldNormalMask.texture != null;
                allDriftWithinPolicy &= cameras[i].drift.withinPolicy;
                allOrientationsVerified &= cameras[i].pixelOrientation.verified;
                allTargetRatiosPassed &= cameras[i].targetSignal.driftBelowTargetRatioPolicy;
                allUnsupportedSentinelFree &= cameras[i].unsupportedSentinelPixelCount == 0;
            }

            bool transparentRejected = HasTransparentOrAlphaTestRejection(evidence.MaskAttestations);
            bool opaqueDepthVerified = masksAvailable && evidence.UnsupportedSubmeshCount == 0;
            bool dynamicDoorProbeAvailable = string.IsNullOrWhiteSpace(evidence.NonReadyReason);
            DungeonPortalReceiverBounceRenderArtifact.MissingPoseGate[] requiredDoorPoseGates =
                BuildMissingPoseGates();
            bool poseGateManifestValid =
                DungeonPortalReceiverBounceRenderArtifact.TryEvaluateRequiredDoorPoseGates(
                    requiredDoorPoseGates,
                    out bool allRequiredDoorPosesPassed,
                    out string poseGateFailure);
            bool ready = reconstructedAvailable && masksAvailable && allDriftWithinPolicy &&
                         allOrientationsVerified && allTargetRatiosPassed && allUnsupportedSentinelFree &&
                         !transparentRejected &&
                         opaqueDepthVerified && dynamicDoorProbeAvailable &&
                         poseGateManifestValid && allRequiredDoorPosesPassed &&
                         evidence.UnsupportedSubmeshCount == 0;

            var blockers = new List<string>();
            if (!reconstructedAvailable) blockers.Add("not all four reconstructed HDR frames are available");
            if (!masksAvailable) blockers.Add(NonEmpty(evidence.FatalMaskFailure, "not all four exact masks are available"));
            if (!allDriftWithinPolicy) blockers.Add("one or more reconstruction-drift gates failed");
            if (!allOrientationsVerified) blockers.Add("pixel-orientation calibration is not verified");
            if (!allTargetRatiosPassed) blockers.Add("one or more drift-to-target-signal ratio gates failed");
            if (!allUnsupportedSentinelFree) blockers.Add("one or more camera masks contain unsupported sentinel pixels");
            if (transparentRejected) blockers.Add("transparent or alpha-tested source geometry is unsupported");
            if (!opaqueDepthVerified) blockers.Add("opaque depth attestation is not complete");
            if (!dynamicDoorProbeAvailable)
                blockers.Add(NonEmpty(evidence.NonReadyReason, "dynamic-door probe reconstruction is unavailable"));
            if (!poseGateManifestValid)
                blockers.Add(NonEmpty(poseGateFailure, "the required door-pose gate manifest is invalid"));
            else if (!allRequiredDoorPosesPassed)
                blockers.Add("required door poses 0/25/50/75 are not all captured and validated");
            if (evidence.UnsupportedSubmeshCount != 0)
                blockers.Add(evidence.UnsupportedSubmeshCount + " source submeshes are explicitly unsupported");

            string decisionReason = ready
                ? "All existing Stage A render-baseline gates passed for the exact persisted capture."
                : "INCOMPLETE: " + string.Join(" | ", blockers);

            return new DungeonPortalReceiverBounceRenderArtifact.RenderArtifactPayload
            {
                receiverRoomId = capture.ReceiverRoomId,
                stableDoorwayId = capture.StableDoorwayId,
                status = ready
                    ? DungeonPortalReceiverBounceRenderArtifact.RenderBaselineReadyStatus
                    : DungeonPortalReceiverBounceRenderArtifact.IncompleteStatus,
                authoredUtcIso8601 = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                toolVersion = ToolVersion,
                unityVersion = Application.unityVersion,
                capture = capture,
                captureAssetPath = inputs.CapturePath,
                captureDependencyHash = inputs.CaptureDependencyHash,
                directOnlyStateHash = directOnly.stateHash,
                workspaceScenePath = provenance.workspaceScenePath,
                workspaceDependencyHash = provenance.canonicalWorkspaceDependencyHash,
                canonicalWorkspaceSetupSignature = provenance.canonicalWorkspaceSetupSignature,
                fullRendererParitySignature = directOnly.fullRendererParitySignature,
                pipelineAssetPath = inputs.PipelinePath,
                pipelineDependencyHash = inputs.PipelineDependencyHash,
                objectIdMrtShader = maskShader,
                objectIdMrtShaderPath = inputs.ShaderPath,
                objectIdMrtShaderDependencyHash = inputs.ShaderDependencyHash,
                doorPose = evidence.DoorPose,
                stableObjectIdManifestHash = evidence.StableObjectIdManifestHash,
                transparentOrAlphaTestRejected = transparentRejected,
                transparentOrAlphaTestReason = transparentRejected
                    ? NonEmpty(evidence.UnsupportedReason, "transparent or alpha-tested source geometry is unsupported")
                    : string.Empty,
                opaqueDepthAttestationVerified = opaqueDepthVerified,
                opaqueDepthAttestedRendererCount = evidence.MaskAttestations.Length,
                opaqueDepthAttestationHash = evidence.OpaqueDepthAttestationHash,
                maskInterpretation = DungeonPortalReceiverBounceRenderArtifact.MaskInterpretationVersion,
                maskRendererAttestations = evidence.MaskAttestations,
                unsupportedMaskSubmeshCount = evidence.UnsupportedSubmeshCount,
                unsupportedMaskReason = evidence.UnsupportedSubmeshCount > 0
                    ? NonEmpty(evidence.UnsupportedReason, "one or more mask submeshes are unsupported")
                    : string.Empty,
                dynamicDoorProbeReconstructionAvailable = dynamicDoorProbeAvailable,
                dynamicDoorProbeReconstructionReason = dynamicDoorProbeAvailable
                    ? string.Empty
                    : NonEmpty(evidence.NonReadyReason, "dynamic-door probe reconstruction is unavailable"),
                reconstructedOffRenderAvailable = reconstructedAvailable,
                masksAvailable = masksAvailable,
                driftWithinPolicy = allDriftWithinPolicy,
                driftMetricDefinitionVersion = DungeonPortalReceiverBounceRenderArtifact.DriftMetricDefinitionVersion,
                targetSignalMetricDefinitionVersion =
                    DungeonPortalReceiverBounceRenderArtifact.TargetSignalMetricDefinitionVersion,
                cameras = cameras ?? Array.Empty<DungeonPortalReceiverBounceRenderArtifact.CameraArtifact>(),
                missingPoseGates = requiredDoorPoseGates,
                decisionReason = decisionReason
            };
        }

        private static DungeonPortalReceiverBounceRenderArtifact.MissingPoseGate[] BuildMissingPoseGates()
        {
            return new[]
            {
                MissingPose("closed_0", "Schema-7 records only the fully-open door pose; closed pose was not captured."),
                MissingPose("intermediate_25", "Schema-7 records only the fully-open door pose; 25% pose was not captured."),
                MissingPose("intermediate_50", "Schema-7 records only the fully-open door pose; 50% pose was not captured."),
                MissingPose("intermediate_75", "Schema-7 records only the fully-open door pose; 75% pose was not captured.")
            };
        }

        private static DungeonPortalReceiverBounceRenderArtifact.MissingPoseGate MissingPose(
            string id,
            string reason)
        {
            return new DungeonPortalReceiverBounceRenderArtifact.MissingPoseGate
            {
                gateId = id,
                available = false,
                passed = false,
                evidenceSignature = string.Empty,
                reason = reason
            };
        }

        private static DungeonPortalReceiverBounceRenderArtifact.CameraBinding ToCameraBinding(
            DungeonPortalReceiverResponseCapture.FixedCameraCapture source)
        {
            return new DungeonPortalReceiverBounceRenderArtifact.CameraBinding
            {
                cameraId = source.cameraId,
                cameraPath = source.cameraPath,
                doorwayLocalPosition = source.doorwayLocalPosition,
                doorwayLocalEulerAngles = source.doorwayLocalEulerAngles,
                fieldOfView = source.fieldOfView,
                nearClipPlane = source.nearClipPlane,
                farClipPlane = source.farClipPlane,
                orthographic = source.orthographic,
                orthographicSize = source.orthographicSize,
                aspect = source.aspect,
                clearFlags = source.clearFlags,
                backgroundColor = source.backgroundColor,
                cullingMask = source.cullingMask,
                renderingPath = source.renderingPath,
                postProcessingEnabled = source.postProcessingEnabled,
                antialiasing = source.antialiasing,
                dithering = source.dithering,
                volumeLayerMask = source.volumeLayerMask,
                allowMsaa = source.allowMsaa,
                allowDynamicResolution = source.allowDynamicResolution,
                useOcclusionCulling = source.useOcclusionCulling,
                urpRendererIndex = source.urpRendererIndex,
                pipelineAssetPath = source.renderPipelineAssetPath,
                pipelineDependencyHash = source.renderPipelineDependencyHash,
                hdr = source.hdr,
                linearPreTonemap = source.linearPreTonemap,
                width = source.width,
                height = source.height,
                textureFormat = source.textureFormat
            };
        }

        private static bool HasTransparentOrAlphaTestRejection(
            DungeonPortalReceiverBounceRenderArtifact.MaskRendererAttestation[] attestations)
        {
            attestations = attestations ?? Array.Empty<DungeonPortalReceiverBounceRenderArtifact.MaskRendererAttestation>();
            for (int i = 0; i < attestations.Length; i++)
            {
                string reason = attestations[i].unsupportedReason ?? string.Empty;
                if (reason.IndexOf("transparent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reason.IndexOf("alpha-test", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reason.IndexOf("alpha test", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reason.IndexOf("alpha-tested", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static string NonEmpty(string value, string fallback)
        {
            return !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;
        }

        private static string SanitizeCameraId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("A Stage A camera id is empty.");
            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    builder.Append(c);
                else
                    throw new InvalidOperationException("A Stage A camera id contains an unsafe path character: '" + value + "'.");
            }
            return builder.ToString();
        }

        private static void EnsureAssetFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
                return;
            string parent = Path.GetDirectoryName(assetFolder);
            string name = Path.GetFileName(assetFolder);
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Invalid asset folder path: '" + assetFolder + "'.");
            parent = parent.Replace('\\', '/');
            EnsureAssetFolder(parent);
            string guid = AssetDatabase.CreateFolder(parent, name);
            if (string.IsNullOrWhiteSpace(guid) && !AssetDatabase.IsValidFolder(assetFolder))
                throw new InvalidOperationException("Unable to create owned PoC folder: '" + assetFolder + "'.");
        }

        private static string GetDependencyHash(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || AssetDatabase.LoadMainAssetAtPath(path) == null)
                throw new InvalidOperationException("Cannot hash a missing asset: '" + path + "'.");
            return AssetDatabase.GetAssetDependencyHash(path).ToString();
        }

        private static string ComputeFileSha256(string assetPath, string requiredRoot)
        {
            string physicalPath = GetOwnedPhysicalPath(assetPath, requiredRoot);
            if (!File.Exists(physicalPath))
                throw new FileNotFoundException("Owned Stage A asset file is missing.", physicalPath);
            using (FileStream stream = File.OpenRead(physicalPath))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static string GetOwnedPhysicalPath(string assetPath, string requiredRoot)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(requiredRoot))
                throw new InvalidOperationException("Owned Stage A path is empty.");
            string normalized = assetPath.Replace('\\', '/');
            string normalizedRoot = requiredRoot.Replace('\\', '/').TrimEnd('/');
            if (!normalized.StartsWith(normalizedRoot + "/", StringComparison.Ordinal) ||
                normalized.IndexOf("..", StringComparison.Ordinal) >= 0 ||
                !normalized.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Path escapes the owned Stage A artifact root: '" + assetPath + "'.");
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string rootPhysical = Path.GetFullPath(Path.Combine(
                projectRoot,
                normalizedRoot.Replace('/', Path.DirectorySeparatorChar)));
            string candidate = Path.GetFullPath(Path.Combine(
                projectRoot,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
            string rootWithSeparator = rootPhysical.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                       Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Physical path escapes the owned Stage A artifact root: '" + assetPath + "'.");
            return candidate;
        }

        private static bool IsPass(string result)
        {
            return !string.IsNullOrEmpty(result) && result.StartsWith("PASS", StringComparison.Ordinal);
        }

        private static void LogResult(string result)
        {
            if (IsPass(result))
                Debug.Log(result);
            else
                Debug.LogError(result);
        }

        private static void ThrowIfFailed(string result)
        {
            if (!IsPass(result))
                throw new InvalidOperationException(result);
            Debug.Log(result);
        }

        private readonly struct ReceiverSpec
        {
            public ReceiverSpec(string roomId)
            {
                RoomId = roomId;
            }

            public string RoomId { get; }
            public string CapturePath => CaptureRoot + "/" + RoomId + "/" + RoomId +
                                         "_ReceiverResponseCapture.asset";
            public string RoomArtifactRoot => ArtifactRoot + "/" + RoomId;
            public string CurrentFolderPath => RoomArtifactRoot + "/" + CurrentFolderName;
            public string ArtifactPath => CurrentFolderPath + "/" + RoomId +
                                          "_DirectOnlyRenderArtifact.asset";
        }

        private readonly struct SessionEvidence
        {
            private SessionEvidence(
                DungeonPortalReceiverBounceRenderArtifact.DoorPoseEvidence doorPose,
                DungeonPortalReceiverBounceRenderArtifact.MaskRendererAttestation[] maskAttestations,
                string stableObjectIdManifestHash,
                string opaqueDepthAttestationHash,
                int unsupportedSubmeshCount,
                string unsupportedReason,
                string fatalMaskFailure,
                string nonReadyReason)
            {
                DoorPose = doorPose;
                MaskAttestations = maskAttestations ??
                                   Array.Empty<DungeonPortalReceiverBounceRenderArtifact.MaskRendererAttestation>();
                StableObjectIdManifestHash = stableObjectIdManifestHash ?? string.Empty;
                OpaqueDepthAttestationHash = opaqueDepthAttestationHash ?? string.Empty;
                UnsupportedSubmeshCount = unsupportedSubmeshCount;
                UnsupportedReason = unsupportedReason ?? string.Empty;
                FatalMaskFailure = fatalMaskFailure ?? string.Empty;
                NonReadyReason = nonReadyReason ?? string.Empty;
            }

            public DungeonPortalReceiverBounceRenderArtifact.DoorPoseEvidence DoorPose { get; }
            public DungeonPortalReceiverBounceRenderArtifact.MaskRendererAttestation[] MaskAttestations { get; }
            public string StableObjectIdManifestHash { get; }
            public string OpaqueDepthAttestationHash { get; }
            public int UnsupportedSubmeshCount { get; }
            public string UnsupportedReason { get; }
            public string FatalMaskFailure { get; }
            public string NonReadyReason { get; }

            public static SessionEvidence Capture(DungeonPortalReceiverBounceDirectOnlyRenderSession session)
            {
                if (session == null)
                    throw new ArgumentNullException(nameof(session));
                return new SessionEvidence(
                    session.DoorPose,
                    session.MaskAttestations,
                    session.StableObjectIdManifestHash,
                    session.OpaqueDepthAttestationHash,
                    session.UnsupportedSubmeshCount,
                    session.UnsupportedReason,
                    session.FatalMaskFailure,
                    session.NonReadyReason);
            }
        }

        private sealed class ReadOnlyInputGuard
        {
            private readonly AssetFingerprint[] fingerprints;

            private ReadOnlyInputGuard(
                string capturePath,
                string captureDependencyHash,
                string shaderPath,
                string shaderDependencyHash,
                string pipelinePath,
                string pipelineDependencyHash,
                AssetFingerprint[] fingerprints)
            {
                CapturePath = capturePath;
                CaptureDependencyHash = captureDependencyHash;
                ShaderPath = shaderPath;
                ShaderDependencyHash = shaderDependencyHash;
                PipelinePath = pipelinePath;
                PipelineDependencyHash = pipelineDependencyHash;
                this.fingerprints = fingerprints;
            }

            public string CapturePath { get; }
            public string CaptureDependencyHash { get; }
            public string ShaderPath { get; }
            public string ShaderDependencyHash { get; }
            public string PipelinePath { get; }
            public string PipelineDependencyHash { get; }

            public static ReadOnlyInputGuard Capture(
                ReceiverSpec spec,
                DungeonPortalReceiverResponseCapture capture,
                Shader maskShader)
            {
                if (capture == null || maskShader == null)
                    throw new ArgumentNullException("Stage A read-only inputs are null.");
                DungeonPortalReceiverResponseCapture.CaptureProvenance provenance = capture.Provenance;
                DungeonPortalReceiverResponseCapture.CaptureState directOnly = capture.DirectOnly;
                DungeonPortalReceiverResponseCapture.FixedCameraCapture[] cameras = directOnly.fixedCameraCaptures ??
                    Array.Empty<DungeonPortalReceiverResponseCapture.FixedCameraCapture>();
                if (cameras.Length != DungeonPortalReceiverBounceRenderArtifact.FixedCameraCount)
                    throw new InvalidOperationException("DirectOnly does not contain the exact four camera contracts.");

                string pipelinePath = cameras[0].renderPipelineAssetPath;
                string pipelineHash = cameras[0].renderPipelineDependencyHash;
                for (int i = 0; i < cameras.Length; i++)
                {
                    if (!string.Equals(cameras[i].renderPipelineAssetPath, pipelinePath, StringComparison.Ordinal) ||
                        !string.Equals(cameras[i].renderPipelineDependencyHash, pipelineHash, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("DirectOnly camera pipeline fingerprints disagree.");
                    }
                }

                var paths = new List<string>
                {
                    spec.CapturePath,
                    ObjectIdMrtShaderPath,
                    provenance.workspaceScenePath,
                    provenance.lightingSettingsClonePath,
                    pipelinePath
                };
                DungeonPortalReceiverResponseCapture.AssetFingerprint[] production = provenance.productionInputs ??
                    Array.Empty<DungeonPortalReceiverResponseCapture.AssetFingerprint>();
                for (int i = 0; i < production.Length; i++)
                {
                    if (production[i].wasDirtyBeforeCapture || production[i].wasDirtyAfterCapture)
                        throw new InvalidOperationException("Capture provenance contains a dirty production input.");
                    paths.Add(production[i].assetPath);
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                var guards = new List<AssetFingerprint>();
                for (int i = 0; i < paths.Count; i++)
                {
                    string path = paths[i];
                    if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
                        continue;
                    UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                    AssetImporter importer = AssetImporter.GetAtPath(path);
                    if (asset == null || EditorUtility.IsDirty(asset) ||
                        (importer != null && EditorUtility.IsDirty(importer)))
                        throw new InvalidOperationException("A required read-only input is missing or dirty: '" + path + "'.");
                    guards.Add(new AssetFingerprint(path, GetDependencyHash(path)));
                }

                string captureHash = GetDependencyHash(spec.CapturePath);
                string shaderHash = GetDependencyHash(ObjectIdMrtShaderPath);
                if (!string.Equals(GetDependencyHash(provenance.workspaceScenePath),
                        provenance.canonicalWorkspaceDependencyHash, StringComparison.Ordinal) ||
                    !string.Equals(GetDependencyHash(provenance.lightingSettingsClonePath),
                        provenance.lightingSettingsCloneDependencyHash, StringComparison.Ordinal) ||
                    !string.Equals(GetDependencyHash(pipelinePath), pipelineHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Capture workspace, LightingSettings or pipeline dependency hash drifted.");
                }
                for (int i = 0; i < production.Length; i++)
                {
                    if (!string.Equals(GetDependencyHash(production[i].assetPath),
                            production[i].dependencyHash, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "A production input dependency hash drifted: '" + production[i].assetPath + "'.");
                    }
                }

                return new ReadOnlyInputGuard(
                    spec.CapturePath,
                    captureHash,
                    ObjectIdMrtShaderPath,
                    shaderHash,
                    pipelinePath,
                    pipelineHash,
                    guards.ToArray());
            }

            public void AssertUnchanged()
            {
                for (int i = 0; i < fingerprints.Length; i++)
                    fingerprints[i].AssertUnchanged();
                if (!string.Equals(GetDependencyHash(CapturePath), CaptureDependencyHash, StringComparison.Ordinal) ||
                    !string.Equals(GetDependencyHash(ShaderPath), ShaderDependencyHash, StringComparison.Ordinal) ||
                    !string.Equals(GetDependencyHash(PipelinePath), PipelineDependencyHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A bound capture/shader/pipeline input changed during Stage A authoring.");
                }
            }

            private readonly struct AssetFingerprint
            {
                public AssetFingerprint(string path, string dependencyHash)
                {
                    Path = path;
                    DependencyHash = dependencyHash;
                }

                private string Path { get; }
                private string DependencyHash { get; }

                public void AssertUnchanged()
                {
                    UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(Path);
                    AssetImporter importer = AssetImporter.GetAtPath(Path);
                    if (asset == null || EditorUtility.IsDirty(asset) ||
                        (importer != null && EditorUtility.IsDirty(importer)) ||
                        !string.Equals(GetDependencyHash(Path), DependencyHash, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Read-only Stage A input changed: '" + Path + "'.");
                    }
                }
            }
        }

        private sealed class ArtifactTransaction : IDisposable
        {
            private readonly ReceiverSpec spec;
            private readonly string token;
            private readonly string stagingFolder;
            private readonly string rollbackFolder;
            private readonly string existingDependencyHash;
            private bool stagingCreated;
            private bool oldMovedToRollback;
            private bool stagingMovedToCurrent;
            private bool committed;
            private bool rollbackAttempted;

            private ArtifactTransaction(
                ReceiverSpec spec,
                string token,
                string stagingFolder,
                string rollbackFolder,
                string existingDependencyHash)
            {
                this.spec = spec;
                this.token = token;
                this.stagingFolder = stagingFolder;
                this.rollbackFolder = rollbackFolder;
                this.existingDependencyHash = existingDependencyHash;
            }

            public static ArtifactTransaction Begin(
                ReceiverSpec spec,
                DungeonPortalReceiverResponseCapture capture,
                Shader maskShader,
                ReadOnlyInputGuard inputs)
            {
                if (capture == null || maskShader == null || inputs == null)
                    throw new ArgumentNullException("Stage A transaction inputs must be non-null.");
                EnsureAssetFolder(ArtifactRoot);
                EnsureAssetFolder(spec.RoomArtifactRoot);
                RecoverInterruptedRollbackIfSafe(spec, capture, maskShader, inputs);
                AssertRoomLayoutBeforeTransaction(spec);

                string existingHash = string.Empty;
                if (AssetDatabase.IsValidFolder(spec.CurrentFolderPath))
                {
                    AssertFolderAssetsClean(spec.CurrentFolderPath);
                    if (AssetDatabase.LoadAssetAtPath<DungeonPortalReceiverBounceRenderArtifact>(spec.ArtifactPath) == null)
                    {
                        throw new InvalidOperationException(
                            "Existing Current folder does not contain the exact Stage A artifact path; it was not touched.");
                    }
                    existingHash = GetDependencyHash(spec.ArtifactPath);
                }

                string token = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) + "_" +
                               Guid.NewGuid().ToString("N");
                string staging = spec.RoomArtifactRoot + "/__Staging_" + token;
                string rollback = spec.RoomArtifactRoot + "/__Rollback_" + token;
                if (AssetDatabase.IsValidFolder(staging) || AssetDatabase.IsValidFolder(rollback))
                    throw new InvalidOperationException("Generated Stage A transaction token collided with an existing folder.");

                string guid = AssetDatabase.CreateFolder(spec.RoomArtifactRoot, "__Staging_" + token);
                if (string.IsNullOrWhiteSpace(guid) || !AssetDatabase.IsValidFolder(staging))
                    throw new InvalidOperationException("Unable to create transaction-owned Stage A staging folder.");

                return new ArtifactTransaction(spec, token, staging, rollback, existingHash)
                {
                    stagingCreated = true
                };
            }

            private static void RecoverInterruptedRollbackIfSafe(
                ReceiverSpec spec,
                DungeonPortalReceiverResponseCapture capture,
                Shader maskShader,
                ReadOnlyInputGuard inputs)
            {
                if (AssetDatabase.IsValidFolder(spec.CurrentFolderPath))
                    return;

                string roomProbe = GetOwnedPhysicalPath(spec.RoomArtifactRoot + "/__Probe", ArtifactRoot);
                string roomPhysical = Path.GetDirectoryName(roomProbe);
                if (string.IsNullOrEmpty(roomPhysical) || !Directory.Exists(roomPhysical))
                    return;

                string[] rollbackDirectories = Directory.GetDirectories(
                    roomPhysical,
                    "__Rollback_*",
                    SearchOption.TopDirectoryOnly);
                if (rollbackDirectories.Length == 0)
                    return;
                if (rollbackDirectories.Length != 1)
                {
                    throw new InvalidOperationException(
                        "Interrupted Stage A recovery found more than one rollback folder; nothing was moved or deleted.");
                }

                string rollbackName = Path.GetFileName(rollbackDirectories[0]);
                string rollbackAssetPath = spec.RoomArtifactRoot + "/" + rollbackName;
                if (!AssetDatabase.IsValidFolder(rollbackAssetPath))
                {
                    throw new InvalidOperationException(
                        "Interrupted Stage A rollback folder is not a valid AssetDatabase folder; nothing was moved.");
                }

                AssertRecoverableRollbackArtifact(
                    rollbackAssetPath,
                    spec,
                    capture,
                    maskShader,
                    inputs);
                string restoreError = AssetDatabase.MoveAsset(rollbackAssetPath, spec.CurrentFolderPath);
                if (!string.IsNullOrEmpty(restoreError))
                {
                    throw new InvalidOperationException(
                        "Validated interrupted Stage A rollback could not be restored: " + restoreError);
                }

                try
                {
                    AssetDatabase.ImportAsset(spec.ArtifactPath, ImportAssetOptions.ForceSynchronousImport);
                    DungeonPortalReceiverBounceRenderArtifact restored =
                        AssetDatabase.LoadAssetAtPath<DungeonPortalReceiverBounceRenderArtifact>(spec.ArtifactPath);
                    AssertPersistedArtifact(restored, capture, maskShader, inputs, spec);
                }
                catch (Exception validationException)
                {
                    string moveBackError = AssetDatabase.MoveAsset(spec.CurrentFolderPath, rollbackAssetPath);
                    if (!string.IsNullOrEmpty(moveBackError))
                    {
                        throw new InvalidOperationException(
                            "Restored rollback failed validation and could not be returned to its original path. " +
                            "Validation: " + validationException.Message + " Move-back: " + moveBackError);
                    }
                    throw new InvalidOperationException(
                        "Restored rollback failed validation and was returned to its original path: " +
                        validationException.Message);
                }
            }

            private static void AssertRecoverableRollbackArtifact(
                string rollbackFolderPath,
                ReceiverSpec spec,
                DungeonPortalReceiverResponseCapture capture,
                Shader maskShader,
                ReadOnlyInputGuard inputs)
            {
                string artifactPath = rollbackFolderPath + "/" + Path.GetFileName(spec.ArtifactPath);
                DungeonPortalReceiverBounceRenderArtifact artifact =
                    AssetDatabase.LoadAssetAtPath<DungeonPortalReceiverBounceRenderArtifact>(artifactPath);
                string validationFailure = artifact == null ? "artifact is null" : string.Empty;
                bool structurallyValid = artifact != null && artifact.TryValidate(out validationFailure);
                if (!structurallyValid || EditorUtility.IsDirty(artifact) || artifact.Capture != capture ||
                    artifact.ObjectIdMrtShader != maskShader ||
                    !string.Equals(artifact.CaptureAssetPath, inputs.CapturePath, StringComparison.Ordinal) ||
                    !string.Equals(artifact.CaptureDependencyHash, inputs.CaptureDependencyHash, StringComparison.Ordinal) ||
                    !string.Equals(artifact.PipelineAssetPath, inputs.PipelinePath, StringComparison.Ordinal) ||
                    !string.Equals(artifact.PipelineDependencyHash, inputs.PipelineDependencyHash, StringComparison.Ordinal) ||
                    !string.Equals(artifact.ObjectIdMrtShaderPath, inputs.ShaderPath, StringComparison.Ordinal) ||
                    !string.Equals(artifact.ObjectIdMrtShaderDependencyHash, inputs.ShaderDependencyHash,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Interrupted Stage A rollback artifact is not structurally/input valid; nothing was moved: " +
                        validationFailure);
                }

                var allowedAssetPaths = new HashSet<string>(StringComparer.Ordinal)
                {
                    rollbackFolderPath,
                    rollbackFolderPath + "/Textures",
                    artifactPath
                };
                DungeonPortalReceiverBounceRenderArtifact.CameraArtifact[] cameras = artifact.Cameras;
                for (int i = 0; i < cameras.Length; i++)
                {
                    allowedAssetPaths.Add(AssertTextureAtRemappedRoot(
                        cameras[i].reconstructedOffHdr,
                        rollbackFolderPath,
                        spec.CurrentFolderPath));
                    if (cameras[i].stableObjectIdMask.texture != null)
                    {
                        allowedAssetPaths.Add(AssertTextureAtRemappedRoot(
                            cameras[i].stableObjectIdMask,
                            rollbackFolderPath,
                            spec.CurrentFolderPath));
                        allowedAssetPaths.Add(AssertTextureAtRemappedRoot(
                            cameras[i].doorwayLocalZWorldNormalMask,
                            rollbackFolderPath,
                            spec.CurrentFolderPath));
                    }
                }

                string[] subFolders = AssetDatabase.GetSubFolders(rollbackFolderPath);
                if (subFolders.Length != 1 ||
                    !string.Equals(subFolders[0], rollbackFolderPath + "/Textures", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Interrupted Stage A rollback contains an unexpected folder layout; nothing was moved.");
                }
                string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { rollbackFolderPath });
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!string.IsNullOrEmpty(path) && !allowedAssetPaths.Contains(path))
                    {
                        throw new InvalidOperationException(
                            "Interrupted Stage A rollback contains an unreferenced asset; nothing was moved: '" +
                            path + "'.");
                    }
                }
                inputs.AssertUnchanged();
            }

            public DungeonPortalReceiverBounceRenderArtifact Stage(
                DungeonPortalReceiverResponseCapture capture,
                Shader maskShader,
                ReadOnlyInputGuard inputs,
                SessionEvidence evidence,
                DungeonPortalReceiverBounceDirectOnlyRenderSession.RenderedCameraFrame[] frames)
            {
                if (!stagingCreated || committed || stagingMovedToCurrent)
                    throw new InvalidOperationException("Stage A transaction is not writable.");
                if (frames == null || frames.Length != DungeonPortalReceiverBounceRenderArtifact.FixedCameraCount)
                    throw new InvalidOperationException("Stage A requires all four rendered camera frames before persistence.");

                bool persistMasks = true;
                for (int i = 0; i < frames.Length; i++)
                {
                    if (frames[i].reconstructedOffHdr == null)
                        throw new InvalidOperationException("A reconstructed HDR frame is missing at index " + i + ".");
                    persistMasks &= frames[i].stableObjectIdMask != null &&
                                    frames[i].doorwayLocalZWorldNormalMask != null;
                }

                string texturesFolder = stagingFolder + "/Textures";
                EnsureAssetFolder(texturesFolder);
                var cameraArtifacts = new DungeonPortalReceiverBounceRenderArtifact.CameraArtifact[frames.Length];
                for (int i = 0; i < frames.Length; i++)
                {
                    DungeonPortalReceiverBounceDirectOnlyRenderSession.RenderedCameraFrame frame = frames[i];
                    string cameraId = SanitizeCameraId(frame.sourceCamera.cameraId);
                    DungeonPortalReceiverBounceRenderArtifact.ArtifactTexture hdr = WriteTexture(
                        frame.reconstructedOffHdr,
                        texturesFolder + "/" + cameraId + "_ReconstructedOff.exr",
                        spec.CurrentFolderPath + "/Textures/" + cameraId + "_ReconstructedOff.exr",
                        "EXR",
                        TextureFormat.RGBAHalf,
                        TextureImporterFormat.RGBAHalf);
                    DungeonPortalReceiverBounceRenderArtifact.ArtifactTexture objectId = default;
                    DungeonPortalReceiverBounceRenderArtifact.ArtifactTexture geometry = default;
                    if (persistMasks)
                    {
                        objectId = WriteTexture(
                            frame.stableObjectIdMask,
                            texturesFolder + "/" + cameraId + "_StableObjectId.png",
                            spec.CurrentFolderPath + "/Textures/" + cameraId + "_StableObjectId.png",
                            "PNG",
                            TextureFormat.RGBA32,
                            TextureImporterFormat.RGBA32);
                        geometry = WriteTexture(
                            frame.doorwayLocalZWorldNormalMask,
                            texturesFolder + "/" + cameraId + "_DoorwayLocalZWorldNormal.exr",
                            spec.CurrentFolderPath + "/Textures/" + cameraId + "_DoorwayLocalZWorldNormal.exr",
                            "EXR",
                            TextureFormat.RGBAHalf,
                            TextureImporterFormat.RGBAHalf);
                    }

                    DungeonPortalReceiverBounceRenderArtifact.TargetSignalEvidence target = persistMasks
                        ? frame.targetSignal
                        : new DungeonPortalReceiverBounceRenderArtifact.TargetSignalEvidence
                        {
                            measured = false,
                            finite = false,
                            sampleCount = 0,
                            unavailableReason = NonEmpty(
                                frame.maskUnavailableReason,
                                "Exact supported-opaque mask is unavailable for this persisted camera.")
                        };
                    cameraArtifacts[i] = new DungeonPortalReceiverBounceRenderArtifact.CameraArtifact
                    {
                        sourceCamera = ToCameraBinding(frame.sourceCamera),
                        reconstructedOffHdr = hdr,
                        stableObjectIdMask = objectId,
                        doorwayLocalZWorldNormalMask = geometry,
                        drift = persistMasks ? frame.supportedOpaqueDrift : frame.fullFrameDrift,
                        driftUsesSupportedOpaqueMask = persistMasks,
                        targetSignal = target,
                        supportedOpaquePixelCount = persistMasks ? frame.supportedOpaquePixelCount : 0,
                        unsupportedSentinelPixelCount = persistMasks ? frame.unsupportedSentinelPixelCount : 0,
                        backgroundPixelCount = persistMasks ? frame.backgroundPixelCount : 0,
                        pixelOrientation = persistMasks
                            ? frame.pixelOrientation
                            : new DungeonPortalReceiverBounceRenderArtifact.PixelOrientationEvidence
                            {
                                verified = false,
                                renderIntoTextureGpuProjection = frame.pixelOrientation.renderIntoTextureGpuProjection,
                                readPixelsBottomLeftOrigin = frame.pixelOrientation.readPixelsBottomLeftOrigin,
                                projectedSampleCount = 0,
                                matchedSampleCount = 0,
                                calibrationSignature = string.Empty,
                                reason = NonEmpty(
                                    frame.maskUnavailableReason,
                                    "Mask output is unavailable, so pixel orientation cannot be proven.")
                            }
                    };
                }

                DungeonPortalReceiverBounceRenderArtifact.RenderArtifactPayload payload = BuildPayload(
                    capture,
                    maskShader,
                    inputs,
                    evidence,
                    cameraArtifacts);
                var artifact = ScriptableObject.CreateInstance<DungeonPortalReceiverBounceRenderArtifact>();
                artifact.name = spec.RoomId + "_DirectOnlyRenderArtifact";
                artifact.ConfigureAuthoring(payload);
                string stagingArtifactPath = stagingFolder + "/" + spec.RoomId +
                                             "_DirectOnlyRenderArtifact.asset";
                AssetDatabase.CreateAsset(artifact, stagingArtifactPath);
                AssetDatabase.SaveAssetIfDirty(artifact);
                if (EditorUtility.IsDirty(artifact))
                    throw new InvalidOperationException("Staged Stage A artifact remained dirty after SaveAssetIfDirty.");
                if (!artifact.TryValidate(out string artifactFailure))
                    throw new InvalidOperationException("Staged Stage A artifact failed structural validation: " + artifactFailure);
                AssertStagedTextureBindings(artifact, stagingFolder, spec.CurrentFolderPath);
                return artifact;
            }

            public DungeonPortalReceiverBounceRenderArtifact CommitAndValidate(
                DungeonPortalReceiverBounceRenderArtifact stagedArtifact,
                DungeonPortalReceiverResponseCapture capture,
                Shader maskShader,
                ReadOnlyInputGuard inputs)
            {
                if (stagedArtifact == null || !stagingCreated || committed)
                    throw new InvalidOperationException("Stage A transaction has no validated staged artifact to commit.");
                inputs.AssertUnchanged();
                AssertEditorStateStillClean();

                if (AssetDatabase.IsValidFolder(spec.CurrentFolderPath))
                {
                    string moveOldError = AssetDatabase.MoveAsset(spec.CurrentFolderPath, rollbackFolder);
                    if (!string.IsNullOrEmpty(moveOldError))
                        throw new InvalidOperationException("Unable to move existing Stage A evidence to rollback: " + moveOldError);
                    oldMovedToRollback = true;
                }

                string moveNewError = AssetDatabase.MoveAsset(stagingFolder, spec.CurrentFolderPath);
                if (!string.IsNullOrEmpty(moveNewError))
                    throw new InvalidOperationException("Unable to promote validated Stage A staging evidence: " + moveNewError);
                stagingCreated = false;
                stagingMovedToCurrent = true;

                AssetDatabase.ImportAsset(spec.ArtifactPath, ImportAssetOptions.ForceSynchronousImport);
                DungeonPortalReceiverBounceRenderArtifact persisted =
                    AssetDatabase.LoadAssetAtPath<DungeonPortalReceiverBounceRenderArtifact>(spec.ArtifactPath);
                AssertPersistedArtifact(persisted, capture, maskShader, inputs, spec);
                AssertEditorStateStillClean();

                if (oldMovedToRollback)
                {
                    if (!AssetDatabase.DeleteAsset(rollbackFolder))
                        throw new InvalidOperationException("Unable to delete the transaction-owned rollback folder after validation.");
                    oldMovedToRollback = false;
                }

                committed = true;
                stagingMovedToCurrent = false;
                return persisted;
            }

            public string TryRollback()
            {
                if (committed || rollbackAttempted)
                    return string.Empty;
                rollbackAttempted = true;
                var failures = new List<string>();

                if (stagingMovedToCurrent && AssetDatabase.IsValidFolder(spec.CurrentFolderPath))
                {
                    if (!AssetDatabase.DeleteAsset(spec.CurrentFolderPath))
                        failures.Add("could not remove the transaction-owned promoted Current folder");
                    else
                        stagingMovedToCurrent = false;
                }
                if (oldMovedToRollback && AssetDatabase.IsValidFolder(rollbackFolder))
                {
                    string restoreError = AssetDatabase.MoveAsset(rollbackFolder, spec.CurrentFolderPath);
                    if (!string.IsNullOrEmpty(restoreError))
                    {
                        failures.Add("could not restore prior Current folder: " + restoreError);
                    }
                    else
                    {
                        oldMovedToRollback = false;
                        if (!string.IsNullOrEmpty(existingDependencyHash) &&
                            !string.Equals(GetDependencyHash(spec.ArtifactPath), existingDependencyHash,
                                StringComparison.Ordinal))
                        {
                            failures.Add("restored prior artifact dependency hash differs from its pre-transaction hash");
                        }
                    }
                }
                if (stagingCreated && AssetDatabase.IsValidFolder(stagingFolder))
                {
                    if (!AssetDatabase.DeleteAsset(stagingFolder))
                        failures.Add("could not remove transaction-owned staging folder");
                    else
                        stagingCreated = false;
                }
                return failures.Count == 0 ? string.Empty : string.Join(" | ", failures);
            }

            public void Dispose()
            {
                if (committed)
                    return;
                string failure = TryRollback();
                if (!string.IsNullOrEmpty(failure))
                    Debug.LogError("Stage A transaction cleanup failed for token " + token + ": " + failure);
            }

            private DungeonPortalReceiverBounceRenderArtifact.ArtifactTexture WriteTexture(
                Texture2D source,
                string stagingPath,
                string finalPath,
                string encoding,
                TextureFormat expectedRuntimeFormat,
                TextureImporterFormat expectedImporterFormat)
            {
                if (source == null || source.width <= 0 || source.height <= 0 ||
                    source.format != expectedRuntimeFormat)
                    throw new InvalidOperationException("A Stage A source texture is null, dimensionless or has the wrong runtime format.");
                if ((string.Equals(encoding, "EXR", StringComparison.Ordinal) &&
                     !stagingPath.EndsWith(".exr", StringComparison.OrdinalIgnoreCase)) ||
                    (string.Equals(encoding, "PNG", StringComparison.Ordinal) &&
                     !stagingPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("Stage A texture encoding does not match its owned file extension.");
                }
                if (AssetDatabase.LoadMainAssetAtPath(stagingPath) != null || File.Exists(GetOwnedPhysicalPath(stagingPath, stagingFolder)))
                    throw new InvalidOperationException("Transaction-owned Stage A texture path already exists: '" + stagingPath + "'.");

                byte[] bytes;
                if (string.Equals(encoding, "EXR", StringComparison.Ordinal))
                    bytes = ImageConversion.EncodeToEXR(source, Texture2D.EXRFlags.None);
                else if (string.Equals(encoding, "PNG", StringComparison.Ordinal))
                    bytes = ImageConversion.EncodeToPNG(source);
                else
                    throw new InvalidOperationException("Unsupported Stage A texture encoding: '" + encoding + "'.");
                if (bytes == null || bytes.Length == 0)
                    throw new InvalidOperationException("Stage A texture encoder returned no bytes for '" + stagingPath + "'.");

                string physicalPath = GetOwnedPhysicalPath(stagingPath, stagingFolder);
                File.WriteAllBytes(physicalPath, bytes);
                AssetDatabase.ImportAsset(stagingPath, ImportAssetOptions.ForceSynchronousImport);
                ConfigureOwnedGeneratedTextureImporter(
                    stagingPath,
                    source.width,
                    source.height,
                    expectedImporterFormat);
                Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(stagingPath);
                if (imported == null || imported.width != source.width || imported.height != source.height ||
                    imported.format != expectedRuntimeFormat || EditorUtility.IsDirty(imported) ||
                    string.Equals(GetDependencyHash(stagingPath), default(Hash128).ToString(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Generated Stage A texture failed exact import/dimension validation: '" +
                                                        stagingPath + "'.");
                }
                AssertOwnedGeneratedTextureImporter(stagingPath, source.width, source.height, expectedImporterFormat);
                AssertImportedPixelsPreserved(source, imported, encoding, stagingPath);

                return new DungeonPortalReceiverBounceRenderArtifact.ArtifactTexture
                {
                    texture = imported,
                    assetPath = finalPath,
                    sha256 = ComputeFileSha256(stagingPath, stagingFolder),
                    width = imported.width,
                    height = imported.height,
                    textureFormat = imported.format.ToString(),
                    encoding = encoding
                };
            }

            private static void ConfigureOwnedGeneratedTextureImporter(
                string assetPath,
                int width,
                int height,
                TextureImporterFormat expectedFormat)
            {
                TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null)
                    throw new InvalidOperationException("Transaction-owned generated texture has no TextureImporter: '" + assetPath + "'.");

                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = false;
                importer.streamingMipmaps = false;
                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.crunchedCompression = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.anisoLevel = 0;
                importer.maxTextureSize = Mathf.Max(width, height);

                TextureImporterPlatformSettings settings = importer.GetDefaultPlatformTextureSettings();
                settings.overridden = true;
                settings.format = expectedFormat;
                settings.maxTextureSize = Mathf.Max(width, height);
                settings.textureCompression = TextureImporterCompression.Uncompressed;
                settings.crunchedCompression = false;
                importer.SetPlatformTextureSettings(settings);
                importer.SaveAndReimport();
            }

            private static void AssertOwnedGeneratedTextureImporter(
                string assetPath,
                int width,
                int height,
                TextureImporterFormat expectedFormat)
            {
                TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                TextureImporterPlatformSettings settings = importer != null
                    ? importer.GetDefaultPlatformTextureSettings()
                    : null;
                if (importer == null || settings == null || importer.textureType != TextureImporterType.Default ||
                    EditorUtility.IsDirty(importer) ||
                    importer.textureShape != TextureImporterShape.Texture2D || importer.sRGBTexture ||
                    importer.mipmapEnabled || importer.streamingMipmaps || !importer.isReadable ||
                    importer.textureCompression != TextureImporterCompression.Uncompressed ||
                    importer.crunchedCompression || importer.npotScale != TextureImporterNPOTScale.None ||
                    importer.alphaSource != TextureImporterAlphaSource.FromInput || importer.alphaIsTransparency ||
                    importer.filterMode != FilterMode.Point || importer.wrapMode != TextureWrapMode.Clamp ||
                    importer.anisoLevel != 0 || importer.maxTextureSize != Mathf.Max(width, height) ||
                    !settings.overridden || settings.format != expectedFormat ||
                    settings.maxTextureSize != Mathf.Max(width, height) ||
                    settings.textureCompression != TextureImporterCompression.Uncompressed ||
                    settings.crunchedCompression)
                {
                    throw new InvalidOperationException(
                        "Transaction-owned generated texture importer is not exact linear/uncompressed/no-mip/point/clamp: '" +
                        assetPath + "'.");
                }
            }

            private static void AssertImportedPixelsPreserved(
                Texture2D source,
                Texture2D imported,
                string encoding,
                string assetPath)
            {
                if (string.Equals(encoding, "PNG", StringComparison.Ordinal))
                {
                    Color32[] sourcePixels = source.GetPixels32();
                    Color32[] importedPixels = imported.GetPixels32();
                    if (sourcePixels.Length != importedPixels.Length)
                        throw new InvalidOperationException("Generated object-ID PNG pixel count changed after import: '" + assetPath + "'.");
                    for (int i = 0; i < sourcePixels.Length; i++)
                    {
                        if (!sourcePixels[i].Equals(importedPixels[i]))
                        {
                            throw new InvalidOperationException(
                                "Generated object-ID PNG bytes changed after import at pixel " + i + ": '" + assetPath + "'.");
                        }
                    }
                    return;
                }

                Color[] sourceHdr = source.GetPixels();
                Color[] importedHdr = imported.GetPixels();
                if (sourceHdr.Length != importedHdr.Length)
                    throw new InvalidOperationException("Generated EXR pixel count changed after import: '" + assetPath + "'.");
                for (int i = 0; i < sourceHdr.Length; i++)
                {
                    Color a = sourceHdr[i];
                    Color b = importedHdr[i];
                    if (!ApproximatelyHalf(a.r, b.r) || !ApproximatelyHalf(a.g, b.g) ||
                        !ApproximatelyHalf(a.b, b.b) || !ApproximatelyHalf(a.a, b.a))
                    {
                        throw new InvalidOperationException(
                            "Generated linear EXR values changed after lossless RGBAHalf import at pixel " + i + ": '" +
                            assetPath + "'.");
                    }
                }
            }

            private static bool ApproximatelyHalf(float left, float right)
            {
                if (float.IsNaN(left) || float.IsInfinity(left) || float.IsNaN(right) || float.IsInfinity(right))
                    return false;
                return Mathf.Abs(left - right) <= Mathf.Max(0.00001f, Mathf.Max(Mathf.Abs(left), Mathf.Abs(right)) * 0.001f);
            }

            private static void AssertRoomLayoutBeforeTransaction(ReceiverSpec spec)
            {
                string roomPhysical = GetOwnedPhysicalPath(spec.RoomArtifactRoot + "/__Probe", ArtifactRoot);
                roomPhysical = Path.GetDirectoryName(roomPhysical);
                if (string.IsNullOrEmpty(roomPhysical) || !Directory.Exists(roomPhysical))
                    return;
                string[] directories = Directory.GetDirectories(roomPhysical);
                for (int i = 0; i < directories.Length; i++)
                {
                    string name = Path.GetFileName(directories[i]);
                    if (!string.Equals(name, CurrentFolderName, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Unexpected prior staging/rollback content exists in the receiver artifact root: '" + name + "'.");
                    }
                }
                string[] files = Directory.GetFiles(roomPhysical);
                for (int i = 0; i < files.Length; i++)
                {
                    if (!files[i].EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Unexpected file exists in the receiver artifact root: '" + files[i] + "'.");
                }
            }

            private static void AssertFolderAssetsClean(string folder)
            {
                string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folder });
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (string.IsNullOrWhiteSpace(path))
                        continue;
                    UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                    AssetImporter importer = AssetImporter.GetAtPath(path);
                    if ((asset != null && EditorUtility.IsDirty(asset)) ||
                        (importer != null && EditorUtility.IsDirty(importer)))
                        throw new InvalidOperationException("Existing Stage A artifact content is dirty: '" + path + "'.");
                }
            }

            private static void AssertStagedTextureBindings(
                DungeonPortalReceiverBounceRenderArtifact artifact,
                string stageFolder,
                string finalFolder)
            {
                DungeonPortalReceiverBounceRenderArtifact.CameraArtifact[] cameras = artifact.Cameras;
                for (int i = 0; i < cameras.Length; i++)
                {
                    AssertStagedTexture(cameras[i].reconstructedOffHdr, stageFolder, finalFolder);
                    if (cameras[i].stableObjectIdMask.texture != null)
                    {
                        AssertStagedTexture(cameras[i].stableObjectIdMask, stageFolder, finalFolder);
                        AssertStagedTexture(cameras[i].doorwayLocalZWorldNormalMask, stageFolder, finalFolder);
                    }
                }
            }

            private static void AssertStagedTexture(
                DungeonPortalReceiverBounceRenderArtifact.ArtifactTexture texture,
                string stageFolder,
                string finalFolder)
            {
                string actual = AssetDatabase.GetAssetPath(texture.texture);
                if (string.IsNullOrWhiteSpace(actual) || !actual.StartsWith(stageFolder + "/", StringComparison.Ordinal) ||
                    !texture.assetPath.StartsWith(finalFolder + "/", StringComparison.Ordinal) ||
                    !string.Equals(ComputeFileSha256(actual, stageFolder), texture.sha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A staged Stage A texture path/hash binding is invalid.");
                }
            }

            internal static void AssertPersistedArtifact(
                DungeonPortalReceiverBounceRenderArtifact artifact,
                DungeonPortalReceiverResponseCapture capture,
                Shader maskShader,
                ReadOnlyInputGuard inputs,
                ReceiverSpec spec)
            {
                string validationFailure = artifact == null ? "artifact is null" : string.Empty;
                bool structurallyValid = artifact != null && artifact.TryValidate(out validationFailure);
                if (artifact == null ||
                    !string.Equals(AssetDatabase.GetAssetPath(artifact), spec.ArtifactPath, StringComparison.Ordinal) ||
                    EditorUtility.IsDirty(artifact) || artifact.Capture != capture || artifact.ObjectIdMrtShader != maskShader ||
                    !structurallyValid)
                {
                    throw new InvalidOperationException(
                        "Promoted Stage A artifact failed exact identity/structural validation: " + validationFailure);
                }
                if (!string.Equals(artifact.CaptureAssetPath, inputs.CapturePath, StringComparison.Ordinal) ||
                    !string.Equals(artifact.CaptureDependencyHash, inputs.CaptureDependencyHash, StringComparison.Ordinal) ||
                    !string.Equals(artifact.PipelineAssetPath, inputs.PipelinePath, StringComparison.Ordinal) ||
                    !string.Equals(artifact.PipelineDependencyHash, inputs.PipelineDependencyHash, StringComparison.Ordinal) ||
                    !string.Equals(artifact.ObjectIdMrtShaderPath, inputs.ShaderPath, StringComparison.Ordinal) ||
                    !string.Equals(artifact.ObjectIdMrtShaderDependencyHash, inputs.ShaderDependencyHash,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Promoted Stage A artifact input dependency bindings drifted.");
                }

                DungeonPortalReceiverBounceRenderArtifact.CameraArtifact[] cameras = artifact.Cameras;
                for (int i = 0; i < cameras.Length; i++)
                {
                    AssertPersistedTexture(cameras[i].reconstructedOffHdr, spec.CurrentFolderPath);
                    if (cameras[i].stableObjectIdMask.texture != null)
                    {
                        AssertPersistedTexture(cameras[i].stableObjectIdMask, spec.CurrentFolderPath);
                        AssertPersistedTexture(cameras[i].doorwayLocalZWorldNormalMask, spec.CurrentFolderPath);
                    }
                }
                string dependencyHash = GetDependencyHash(spec.ArtifactPath);
                if (string.IsNullOrWhiteSpace(dependencyHash) ||
                    string.Equals(dependencyHash, default(Hash128).ToString(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Promoted Stage A artifact has no dependency hash.");
                }
                inputs.AssertUnchanged();
            }

            private static string AssertTextureAtRemappedRoot(
                DungeonPortalReceiverBounceRenderArtifact.ArtifactTexture texture,
                string actualRoot,
                string logicalRoot)
            {
                ResolveTextureContract(
                    texture,
                    out TextureFormat expectedRuntimeFormat,
                    out TextureImporterFormat expectedImporterFormat,
                    out GraphicsFormat expectedGraphicsFormat);
                if (string.IsNullOrWhiteSpace(texture.assetPath) ||
                    !texture.assetPath.StartsWith(logicalRoot + "/", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Interrupted Stage A rollback texture has an invalid logical Current path.");
                }
                string expectedActualPath = actualRoot + texture.assetPath.Substring(logicalRoot.Length);
                if (texture.texture == null ||
                    !string.Equals(AssetDatabase.GetAssetPath(texture.texture), expectedActualPath,
                        StringComparison.Ordinal) ||
                    EditorUtility.IsDirty(texture.texture) || texture.texture.width != texture.width ||
                    texture.texture.height != texture.height || texture.texture.format != expectedRuntimeFormat ||
                    texture.texture.graphicsFormat != expectedGraphicsFormat ||
                    string.Equals(GetDependencyHash(expectedActualPath), default(Hash128).ToString(),
                        StringComparison.Ordinal) ||
                    !string.Equals(ComputeFileSha256(expectedActualPath, actualRoot), texture.sha256,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Interrupted Stage A rollback texture path/hash/format validation failed; nothing was moved.");
                }
                AssertOwnedGeneratedTextureImporter(
                    expectedActualPath,
                    texture.width,
                    texture.height,
                    expectedImporterFormat);
                return expectedActualPath;
            }

            private static void AssertPersistedTexture(
                DungeonPortalReceiverBounceRenderArtifact.ArtifactTexture texture,
                string currentFolder)
            {
                ResolveTextureContract(
                    texture,
                    out TextureFormat expectedRuntimeFormat,
                    out TextureImporterFormat expectedImporterFormat,
                    out GraphicsFormat expectedGraphicsFormat);
                if (texture.texture == null ||
                    !string.Equals(AssetDatabase.GetAssetPath(texture.texture), texture.assetPath, StringComparison.Ordinal) ||
                    !texture.assetPath.StartsWith(currentFolder + "/", StringComparison.Ordinal) ||
                    EditorUtility.IsDirty(texture.texture) || texture.texture.width != texture.width ||
                    texture.texture.height != texture.height || texture.texture.format != expectedRuntimeFormat ||
                    texture.texture.graphicsFormat != expectedGraphicsFormat ||
                    string.Equals(GetDependencyHash(texture.assetPath), default(Hash128).ToString(),
                        StringComparison.Ordinal) ||
                    !string.Equals(ComputeFileSha256(texture.assetPath, currentFolder), texture.sha256,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Promoted Stage A texture path/hash/dimension/format validation failed.");
                }
                AssertOwnedGeneratedTextureImporter(
                    texture.assetPath,
                    texture.width,
                    texture.height,
                    expectedImporterFormat);
            }

            private static void ResolveTextureContract(
                DungeonPortalReceiverBounceRenderArtifact.ArtifactTexture texture,
                out TextureFormat expectedRuntimeFormat,
                out TextureImporterFormat expectedImporterFormat,
                out GraphicsFormat expectedGraphicsFormat)
            {
                if (string.Equals(texture.textureFormat, TextureFormat.RGBA32.ToString(), StringComparison.Ordinal))
                {
                    expectedRuntimeFormat = TextureFormat.RGBA32;
                    expectedImporterFormat = TextureImporterFormat.RGBA32;
                    expectedGraphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
                    if (!string.Equals(texture.encoding, "PNG", StringComparison.Ordinal) ||
                        !texture.assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Promoted RGBA32 Stage A texture is not an exact PNG artifact.");
                }
                else if (string.Equals(texture.textureFormat, TextureFormat.RGBAHalf.ToString(), StringComparison.Ordinal))
                {
                    expectedRuntimeFormat = TextureFormat.RGBAHalf;
                    expectedImporterFormat = TextureImporterFormat.RGBAHalf;
                    expectedGraphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                    if (!string.Equals(texture.encoding, "EXR", StringComparison.Ordinal) ||
                        !texture.assetPath.EndsWith(".exr", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Promoted RGBAHalf Stage A texture is not an exact EXR artifact.");
                }
                else
                {
                    throw new InvalidOperationException("Promoted Stage A texture declares an unsupported runtime format.");
                }
            }
        }
    }
}
