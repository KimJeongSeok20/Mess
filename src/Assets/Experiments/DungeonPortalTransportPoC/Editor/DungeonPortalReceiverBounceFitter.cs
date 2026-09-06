using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DungeonPortalTransportPoC.Editor
{
    /// <summary>
    /// Read-mostly K=1 receiver-side proxy fitting evidence tool.  It never performs a
    /// bake, never changes a capture/workspace, and its normal Fit path only creates or
    /// updates a separate PoC evidence asset.  Candidate response and semantic masks are
    /// deliberately fail-closed: absent render evidence produces K1_REJECTED, never a
    /// guessed endpoint profile change.
    /// </summary>
    public static class DungeonPortalReceiverBounceFitter
    {
        private const string RootFolder = "Assets/Experiments/DungeonPortalTransportPoC";
        private const string CaptureRoot = RootFolder + "/Generated/Bounce";
        private const string EvidenceRoot = RootFolder + "/FitEvidence";
        private const string EndpointProfilesRoot = RootFolder + "/Generated/EndpointProfiles";
        private const string ValidationScenePath =
            RootFolder + "/Scenes/Start_Admin_PortalTransportValidation.unity";
        private const string StableDoorwayId = "Doorways[5]/Door_SM_A[0]/DoorWayPoint[0]";
        private const string ToolVersion = "DungeonPortalReceiverBounceFitter/1";
        private const int ExpectedCullingMask = 66177;
        private const int RequiredRenderingLayerMask = 2;
        private const float ImageEpsilon = 0.002f;
        private const float ShL0Epsilon = 0.0005f;
        private const int RequiredFixedCameraCount = 4;
        private const int RequiredProbeCount = 27;

        private static readonly ReceiverSpec StartSpec = new ReceiverSpec("StartRoom_R000");
        private static readonly ReceiverSpec AdminSpec = new ReceiverSpec("AdminstrativeSegregation_R000");
        private static readonly float[] CandidateXs = { -0.4f, 0f, 0.4f };
        private static readonly float[] CandidateYs = { 0.5f, 1f, 1.5f };
        private static readonly float[] CandidateDepths = { 0.25f, 0.75f, 1.5f };
        private static readonly float[] CandidateOuterAngles = { 80f, 120f, 160f };
        private static readonly float[] CandidateRangeFactors = { 1.05f, 1.5f };

        private static readonly GateDefinition[] GateDefinitions =
        {
            new GateDefinition("drift", false, 0f, true, 0.05f),
            new GateDefinition("sh_nrmse_overall", false, 0f, true, 0.35f),
            new GateDefinition("sh_nrmse_slab", false, 0f, true, 0.45f),
            new GateDefinition("hdr_nrmse_overall", false, 0f, true, 0.20f),
            new GateDefinition("hdr_nrmse_c0", false, 0f, true, 0.25f),
            new GateDefinition("hdr_nrmse_c1", false, 0f, true, 0.25f),
            new GateDefinition("hdr_nrmse_c2", false, 0f, true, 0.25f),
            new GateDefinition("hdr_nrmse_c3", false, 0f, true, 0.25f),
            new GateDefinition("semantic", false, 0f, true, 0.30f),
            new GateDefinition("energy_ratio", true, 0.85f, true, 1.15f),
            new GateDefinition("p99_ratio", false, 0f, true, 1.35f),
            new GateDefinition("dark_off_target", false, 0f, true, 0.02f),
            new GateDefinition("forbidden_energy", false, 0f, true, 0.005f),
            new GateDefinition("forbidden_p99", false, 0f, true, 0.02f),
            new GateDefinition("gain_r", true, 0f, true, 4f),
            new GateDefinition("gain_g", true, 0f, true, 4f),
            new GateDefinition("gain_b", true, 0f, true, 4f)
        };

        private static bool fitInProgress;

        [MenuItem("Tools/Dungeon/Lighting/Portal Transport PoC/Fit Receiver Bounce/StartRoom_R000")]
        public static void FitStartFromMenu()
        {
            LogResult(FitStart());
        }

        [MenuItem("Tools/Dungeon/Lighting/Portal Transport PoC/Fit Receiver Bounce/AdminstrativeSegregation_R000")]
        public static void FitAdminFromMenu()
        {
            LogResult(FitAdmin());
        }

        [MenuItem("Tools/Dungeon/Lighting/Portal Transport PoC/Fit Receiver Bounce/All Receivers")]
        public static void FitAllFromMenu()
        {
            LogResult(FitAll());
        }

        [MenuItem("Tools/Dungeon/Lighting/Portal Transport PoC/Apply Accepted Receiver Bounce/StartRoom_R000")]
        public static void ApplyAcceptedStartFromMenu()
        {
            LogResult(ApplyAcceptedFit(StartSpec));
        }

        [MenuItem("Tools/Dungeon/Lighting/Portal Transport PoC/Apply Accepted Receiver Bounce/AdminstrativeSegregation_R000")]
        public static void ApplyAcceptedAdminFromMenu()
        {
            LogResult(ApplyAcceptedFit(AdminSpec));
        }

        /// <summary>Normal Fit: only the receiver's separate evidence asset is written.</summary>
        public static string FitStart()
        {
            return FitReceiver(StartSpec);
        }

        /// <summary>Normal Fit: only the receiver's separate evidence asset is written.</summary>
        public static string FitAdmin()
        {
            return FitReceiver(AdminSpec);
        }

        public static string FitAll()
        {
            string start = FitReceiver(StartSpec);
            if (!IsPass(start))
                return start;

            string admin = FitReceiver(AdminSpec);
            if (!IsPass(admin))
                return admin;

            return "PASS Receiver bounce fit evidence saved for StartRoom_R000 and " +
                   "AdminstrativeSegregation_R000; endpoint profiles and connection remain unchanged/disabled.";
        }

        /// <summary>
        /// The only profile-writing operation. It is deliberately separate from Fit and
        /// rejects unless a fully comparable, semantically masked, accepted evidence asset
        /// is current for the exact target profile and both persisted captures.
        /// </summary>
        public static string ApplyAcceptedFit(DungeonPortalReceiverBounceFitEvidence evidence)
        {
            if (evidence == null)
                return "FAIL: no K=1 fit evidence asset was provided; no profile or scene was touched.";

            if (!TryGetReceiverSpec(evidence.ReceiverRoomId, out ReceiverSpec spec))
                return "FAIL: fit evidence receiver id is not in the exact two-profile whitelist.";

            return ApplyAcceptedFit(spec, evidence);
        }

        private static string FitReceiver(ReceiverSpec spec)
        {
            if (!TryValidateEditorState(out string preflightFailure))
                return "FAIL: " + preflightFailure;
            if (fitInProgress)
                return "FAIL: another receiver-bounce fit/apply transaction is already in progress.";

            fitInProgress = true;
            EditorSessionSnapshot snapshot = null;
            DungeonPortalReceiverBounceFitEvidence evidence = null;
            bool createdEvidenceThisInvocation = false;
            bool evidenceCreatedOnDisk = false;
            string existingEvidenceRollbackJson = null;
            string existingEvidenceDependencyHash = null;
            string existingEvidencePhysicalHash = null;
            string result = null;
            try
            {
                snapshot = EditorSessionSnapshot.Capture();
                Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;

                if (!TryLoadAndValidateBothPersistedCaptures(
                        spec,
                        out DungeonPortalReceiverResponseCapture receiverCapture,
                        out DungeonPortalReceiverBounceFitEvidence.CaptureIntegrityEvidence integrity,
                        out string integrityFailure))
                {
                    throw new InvalidOperationException(integrityFailure);
                }

                DungeonPortalEndpointProfile targetProfile = LoadTargetProfile(spec);
                if (targetProfile == null)
                    throw new InvalidOperationException("Target endpoint profile is missing at '" + spec.ProfilePath + "'.");
                if (EditorUtility.IsDirty(targetProfile))
                    throw new InvalidOperationException("Target endpoint profile is dirty and was not read for fit: '" + spec.ProfilePath + "'.");
                if (!targetProfile.TryValidate(out string profileFailure))
                    throw new InvalidOperationException("Target endpoint profile is structurally invalid: " + profileFailure);
                if (!MatchesProfileIdentity(targetProfile, spec))
                    throw new InvalidOperationException("Target endpoint profile room/doorway identity does not match '" + spec.RoomId + "'.");

                DungeonPortalReceiverBounceFitEvidence.SignalGateEvidence signals =
                    EvaluateTargetSignal(receiverCapture);
                Vector3 shCenterLocal = ComputeShCenter(receiverCapture.DirectOnly, receiverCapture.Full);
                DungeonPortalReceiverBounceFitEvidence.CandidateSearchEvidence candidates =
                    BuildUnevaluatedCanonicalCandidates(receiverCapture.Full.probes, shCenterLocal);
                DungeonPortalReceiverBounceFitEvidence.SemanticMaskEvidence semanticMasks =
                    BuildUnavailableSemanticMaskEvidence();
                DungeonPortalReceiverBounceFitEvidence.FitGateEvidence fitGates =
                    BuildUnavailableFitGates();

                string decisionReason = BuildRejectedDecisionReason(signals);
                evidence = GetOrCreateEvidenceAsset(spec, out createdEvidenceThisInvocation);
                if (!createdEvidenceThisInvocation)
                {
                    existingEvidenceRollbackJson = EditorJsonUtility.ToJson(evidence, true);
                    existingEvidenceDependencyHash = AssetDatabase.GetAssetDependencyHash(spec.EvidencePath).ToString();
                    existingEvidencePhysicalHash = ComputeAssetFileSha256(spec.EvidencePath);
                }
                ConfigureRejectedEvidence(
                    evidence,
                    spec,
                    receiverCapture,
                    targetProfile,
                    integrity,
                    signals,
                    semanticMasks,
                    candidates,
                    fitGates,
                    decisionReason);

                if (!evidence.TryValidate(out string evidenceFailure))
                    throw new InvalidOperationException("Refusing to save malformed K1_REJECTED evidence: " + evidenceFailure);

                // First-run creation is failure-atomic: validate the in-memory asset
                // before it obtains an AssetDatabase path, then track only this exact
                // invocation's owned path for any necessary cleanup.
                if (createdEvidenceThisInvocation)
                {
                    AssetDatabase.CreateAsset(evidence, spec.EvidencePath);
                    evidenceCreatedOnDisk = true;
                }
                EditorUtility.SetDirty(evidence);
                AssetDatabase.SaveAssetIfDirty(evidence);
                if (EditorUtility.IsDirty(evidence))
                    throw new InvalidOperationException("K1_REJECTED evidence remained dirty after SaveAssetIfDirty.");
                ValidatePersistedEvidenceBinding(spec, evidence, receiverCapture, targetProfile);

                result = "PASS " + spec.RoomId + " signalQualified=" +
                         (signals.allRequiredPassed ? "PASS" : "REJECTED") +
                         "; saved K1_REJECTED evidence because no semantic/forbidden/source-side mask " +
                         "or candidate SH/HDR response evidence exists. No endpoint profile, scene, capture, " +
                         "workspace, importer, or connection was mutated.";
            }
            catch (Exception exception)
            {
                string cleanupFailure = RollbackFitEvidenceMutation(
                    spec,
                    evidence,
                    evidenceCreatedOnDisk,
                    existingEvidenceRollbackJson,
                    existingEvidenceDependencyHash,
                    existingEvidencePhysicalHash);
                result = "FAIL: receiver-bounce Fit made no profile/scene/capture mutation: " + exception.Message + cleanupFailure;
            }
            finally
            {
                try
                {
                    if (snapshot != null)
                    {
                        snapshot.Restore();
                        snapshot.AssertRestoredClean();
                    }
                }
                catch (Exception restoreFailure)
                {
                    string cleanupFailure = RollbackFitEvidenceMutation(
                        spec,
                        evidence,
                        evidenceCreatedOnDisk,
                        existingEvidenceRollbackJson,
                        existingEvidenceDependencyHash,
                        existingEvidencePhysicalHash);
                    result = "FAIL: fitter could not prove exact editor session restoration: " +
                             restoreFailure.Message + cleanupFailure + " Previous result: " + result;
                }
                finally
                {
                    fitInProgress = false;
                }
            }

            return result ?? "FAIL: receiver-bounce Fit ended without a result.";
        }

        private static string RollbackFitEvidenceMutation(
            ReceiverSpec spec,
            DungeonPortalReceiverBounceFitEvidence evidence,
            bool evidenceCreatedOnDisk,
            string existingEvidenceRollbackJson,
            string existingEvidenceDependencyHash,
            string existingEvidencePhysicalHash)
        {
            if (evidence == null)
                return string.Empty;

            string assetPath = AssetDatabase.GetAssetPath(evidence);
            if (evidenceCreatedOnDisk && string.Equals(assetPath, spec.EvidencePath, StringComparison.Ordinal))
            {
                // Delete only the exact new PoC evidence asset created by this invocation.
                try
                {
                    return AssetDatabase.DeleteAsset(spec.EvidencePath)
                        ? string.Empty
                        : " Newly-created evidence cleanup returned false for '" + spec.EvidencePath + "'.";
                }
                catch (Exception deleteFailure)
                {
                    return " Newly-created evidence cleanup failed: " + deleteFailure.Message;
                }
            }

            if (string.IsNullOrEmpty(existingEvidenceRollbackJson) ||
                !string.Equals(assetPath, spec.EvidencePath, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            try
            {
                EditorJsonUtility.FromJsonOverwrite(existingEvidenceRollbackJson, evidence);
                EditorUtility.SetDirty(evidence);
                AssetDatabase.SaveAssetIfDirty(evidence);
                if (EditorUtility.IsDirty(evidence) ||
                    !string.Equals(
                        AssetDatabase.GetAssetDependencyHash(spec.EvidencePath).ToString(),
                        existingEvidenceDependencyHash,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        ComputeAssetFileSha256(spec.EvidencePath),
                        existingEvidencePhysicalHash,
                        StringComparison.Ordinal))
                {
                    return " Existing evidence JSON rollback could not prove its original dependency/physical hash.";
                }
            }
            catch (Exception rollbackFailure)
            {
                return " Existing evidence JSON rollback failed: " + rollbackFailure.Message;
            }

            return string.Empty;
        }

        private static string ApplyAcceptedFit(ReceiverSpec spec)
        {
            DungeonPortalReceiverBounceFitEvidence evidence =
                AssetDatabase.LoadAssetAtPath<DungeonPortalReceiverBounceFitEvidence>(spec.EvidencePath);
            if (evidence == null)
                return "FAIL: no evidence asset exists at '" + spec.EvidencePath + "'; no profile was touched.";
            return ApplyAcceptedFit(spec, evidence);
        }

        private static string ApplyAcceptedFit(
            ReceiverSpec spec,
            DungeonPortalReceiverBounceFitEvidence evidence)
        {
            if (!TryValidateEditorState(out string preflightFailure))
                return "FAIL: " + preflightFailure;
            if (evidence == null ||
                !string.Equals(AssetDatabase.GetAssetPath(evidence), spec.EvidencePath, StringComparison.Ordinal))
            {
                return "FAIL: Apply requires the exact whitelisted evidence asset; no profile or scene was touched.";
            }
            if (fitInProgress)
                return "FAIL: another receiver-bounce fit/apply transaction is already in progress.";

            fitInProgress = true;
            EditorSessionSnapshot snapshot = null;
            string rollbackJson = null;
            DungeonPortalEndpointProfile profile = null;
            string result = null;
            try
            {
                snapshot = EditorSessionSnapshot.Capture();
                Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;

                if (!TryLoadAndValidateBothPersistedCaptures(
                        spec,
                        out DungeonPortalReceiverResponseCapture receiverCapture,
                        out DungeonPortalReceiverBounceFitEvidence.CaptureIntegrityEvidence integrity,
                        out string integrityFailure))
                {
                    throw new InvalidOperationException(integrityFailure);
                }

                // Persisted-integrity validation opens and restores isolated scenes.
                // Reload the canonical evidence asset so a caller-held Unity object
                // cannot become a stale fake-null reference across that transaction.
                evidence = AssetDatabase.LoadAssetAtPath<DungeonPortalReceiverBounceFitEvidence>(spec.EvidencePath);
                if (evidence == null)
                    throw new InvalidOperationException("Whitelisted fit evidence disappeared during capture validation.");

                profile = LoadTargetProfile(spec);
                if (profile == null)
                    throw new InvalidOperationException("Whitelisted target profile is missing: '" + spec.ProfilePath + "'.");
                if (EditorUtility.IsDirty(profile))
                    throw new InvalidOperationException("Target endpoint profile is dirty; atomic Apply refuses an ambiguous baseline.");

                ValidateEvidenceForApply(spec, evidence, receiverCapture, profile, integrity);
                string sceneHashBefore = ComputeAssetFileSha256(ValidationScenePath);
                VerifyConnectionSerializedDisabled();
                if (!string.Equals(sceneHashBefore, ComputeAssetFileSha256(ValidationScenePath), StringComparison.Ordinal))
                    throw new InvalidOperationException("Read-only connection verification changed the validation scene file.");

                DungeonPortalEndpointProfile.PortalDirectLightDescriptor[] directBefore =
                    CloneDirectLights(profile.OutgoingDirectLights);
                DungeonPortalEndpointProfile.PortalBounceLightDescriptor[] bounceBefore =
                    CloneBounceLights(profile.IncomingBounceLights);
                if (bounceBefore.Length != 0)
                    throw new InvalidOperationException("Apply requires a zero-bounce baseline; it will not replace or append existing bounce descriptors.");

                rollbackJson = EditorJsonUtility.ToJson(profile, true);
                DungeonPortalEndpointProfile.PortalBounceLightDescriptor expectedBounce =
                    BuildProfileBounceDescriptor(evidence.AcceptedDescriptor);
                Undo.RecordObject(profile, "Apply accepted receiver bounce K1 evidence");
                profile.ConfigureAuthoring(
                    spec.RoomId,
                    StableDoorwayId,
                    directBefore,
                    new[] { expectedBounce });

                if (!profile.TryValidate(out string appliedProfileFailure))
                    throw new InvalidOperationException("Atomic Apply produced an invalid endpoint profile: " + appliedProfileFailure);
                if (!MatchesProfileIdentity(profile, spec) ||
                    !DirectLightsByteEquivalent(directBefore, profile.OutgoingDirectLights) ||
                    profile.IncomingBounceLights.Length != 1 ||
                    !BounceDescriptorEquivalent(expectedBounce, profile.IncomingBounceLights[0]))
                {
                    throw new InvalidOperationException("Atomic Apply did not preserve the outgoing basis or write exactly its one accepted Spot descriptor.");
                }
                if (profile.IncomingBounceLights[0].type != LightType.Spot)
                    throw new InvalidOperationException("Atomic Apply rejects a Point bounce descriptor.");

                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
                if (EditorUtility.IsDirty(profile))
                    throw new InvalidOperationException("Target endpoint profile remained dirty after SaveAssetIfDirty.");
                if (!string.Equals(sceneHashBefore, ComputeAssetFileSha256(ValidationScenePath), StringComparison.Ordinal))
                    throw new InvalidOperationException("Profile Apply changed the validation scene file; connection remains fail-closed.");

                result = "PASS Applied exactly one accepted K=1 Spot bounce descriptor to '" +
                         spec.ProfilePath + "'. Outgoing direct basis was byte-equivalent and connection stayed disabled.";
            }
            catch (Exception exception)
            {
                if (profile != null && !string.IsNullOrEmpty(rollbackJson))
                {
                    try
                    {
                        EditorJsonUtility.FromJsonOverwrite(rollbackJson, profile);
                        EditorUtility.SetDirty(profile);
                        AssetDatabase.SaveAssetIfDirty(profile);
                    }
                    catch (Exception rollbackFailure)
                    {
                        result = "FAIL: atomic Apply failed and JSON rollback also failed: " + rollbackFailure.Message +
                                 " Original failure: " + exception.Message;
                    }
                }

                if (result == null)
                    result = "FAIL: accepted K1 Apply was rejected/rolled back: " + exception.Message;
            }
            finally
            {
                try
                {
                    if (snapshot != null)
                    {
                        snapshot.Restore();
                        snapshot.AssertRestoredClean();
                    }
                }
                catch (Exception restoreFailure)
                {
                    result = "FAIL: Apply could not prove exact editor-session restoration: " +
                             restoreFailure.Message + " Previous result: " + result;
                }
                finally
                {
                    fitInProgress = false;
                }
            }

            return result ?? "FAIL: receiver-bounce Apply ended without a result.";
        }

        private static void ConfigureRejectedEvidence(
            DungeonPortalReceiverBounceFitEvidence evidence,
            ReceiverSpec spec,
            DungeonPortalReceiverResponseCapture capture,
            DungeonPortalEndpointProfile targetProfile,
            DungeonPortalReceiverBounceFitEvidence.CaptureIntegrityEvidence integrity,
            DungeonPortalReceiverBounceFitEvidence.SignalGateEvidence signals,
            DungeonPortalReceiverBounceFitEvidence.SemanticMaskEvidence semanticMasks,
            DungeonPortalReceiverBounceFitEvidence.CandidateSearchEvidence candidates,
            DungeonPortalReceiverBounceFitEvidence.FitGateEvidence fitGates,
            string decisionReason)
        {
            DungeonPortalReceiverResponseCapture.CaptureState baseline = capture.Baseline;
            DungeonPortalReceiverResponseCapture.CaptureState direct = capture.DirectOnly;
            DungeonPortalReceiverResponseCapture.CaptureState full = capture.Full;
            evidence.ConfigureAuthoring(new DungeonPortalReceiverBounceFitEvidence.FitEvidencePayload
            {
                receiverRoomId = spec.RoomId,
                stableDoorwayId = StableDoorwayId,
                status = DungeonPortalReceiverBounceFitEvidence.RejectedStatus,
                authoredUtcIso8601 = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                fitToolVersion = ToolVersion,
                unityVersion = Application.unityVersion,
                metricDefinitionVersion = DungeonPortalReceiverBounceFitEvidence.MetricDefinitionVersion,
                receiverCapture = capture,
                receiverCaptureAssetPath = spec.CapturePath,
                receiverCaptureDependencyHash = AssetDatabase.GetAssetDependencyHash(spec.CapturePath).ToString(),
                baselineStateHash = baseline.stateHash,
                directOnlyStateHash = direct.stateHash,
                fullStateHash = full.stateHash,
                captureIntegrity = integrity,
                targetProfile = targetProfile,
                targetProfileAssetPath = spec.ProfilePath,
                targetProfileDependencyHash = AssetDatabase.GetAssetDependencyHash(spec.ProfilePath).ToString(),
                signalGates = signals,
                semanticMasks = semanticMasks,
                candidateSearch = candidates,
                fitGates = fitGates,
                acceptedDescriptor = default,
                decisionReason = decisionReason
            });
        }

        private static bool TryLoadAndValidateBothPersistedCaptures(
            ReceiverSpec receiverSpec,
            out DungeonPortalReceiverResponseCapture receiverCapture,
            out DungeonPortalReceiverBounceFitEvidence.CaptureIntegrityEvidence integrity,
            out string error)
        {
            receiverCapture = null;
            integrity = default;
            DungeonPortalReceiverResponseCapture start = LoadCapture(StartSpec);
            if (start == null)
            {
                error = "The Start persisted capture is missing: " + StartSpec.CapturePath;
                return false;
            }

            integrity.startPassed = DungeonPortalReceiverBounceBaker.TryValidatePersistedCaptureIntegrity(
                start, out integrity.startFailure);

            // Persisted-integrity validation opens and restores isolated scenes. Reload
            // each asset immediately before use so a preloaded but otherwise unrooted
            // ScriptableObject cannot become a stale Unity fake-null reference across
            // that scene transaction.
            DungeonPortalReceiverResponseCapture admin = LoadCapture(AdminSpec);
            if (admin == null)
            {
                error = "The Admin persisted capture is missing after Start integrity validation: " +
                        AdminSpec.CapturePath;
                return false;
            }

            integrity.adminPassed = DungeonPortalReceiverBounceBaker.TryValidatePersistedCaptureIntegrity(
                admin, out integrity.adminFailure);

            receiverCapture = LoadCapture(receiverSpec);
            integrity.receiverPassed = receiverSpec.RoomId == StartSpec.RoomId
                ? integrity.startPassed
                : integrity.adminPassed;
            integrity.receiverFailure = receiverSpec.RoomId == StartSpec.RoomId
                ? integrity.startFailure
                : integrity.adminFailure;

            if (!integrity.startPassed || !integrity.adminPassed || !integrity.receiverPassed)
            {
                error = "Persisted capture integrity failed. Start='" + integrity.startFailure +
                        "' Admin='" + integrity.adminFailure + "'.";
                return false;
            }

            if (!receiverCapture.TryValidate(out string structuralFailure) ||
                !string.Equals(receiverCapture.ReceiverRoomId, receiverSpec.RoomId, StringComparison.Ordinal) ||
                !string.Equals(receiverCapture.StableDoorwayId, StableDoorwayId, StringComparison.Ordinal))
            {
                error = "Selected persisted capture no longer matches the canonical receiver/doorway: " + structuralFailure;
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static DungeonPortalReceiverBounceFitEvidence GetOrCreateEvidenceAsset(
            ReceiverSpec spec,
            out bool createdThisInvocation)
        {
            createdThisInvocation = false;
            EnsureAssetFolder(RootFolder);
            EnsureAssetFolder(EvidenceRoot);
            DungeonPortalReceiverBounceFitEvidence existing =
                AssetDatabase.LoadAssetAtPath<DungeonPortalReceiverBounceFitEvidence>(spec.EvidencePath);
            if (existing != null)
            {
                if (EditorUtility.IsDirty(existing))
                    throw new InvalidOperationException("Existing PoC fit evidence is dirty and will not be overwritten: '" + spec.EvidencePath + "'.");
                return existing;
            }

            if (AssetDatabase.LoadMainAssetAtPath(spec.EvidencePath) != null)
                throw new InvalidOperationException("Evidence path is occupied by a non-evidence asset: '" + spec.EvidencePath + "'.");

            createdThisInvocation = true;
            return ScriptableObject.CreateInstance<DungeonPortalReceiverBounceFitEvidence>();
        }

        private static DungeonPortalReceiverResponseCapture LoadCapture(ReceiverSpec spec)
        {
            DungeonPortalReceiverResponseCapture capture =
                AssetDatabase.LoadAssetAtPath<DungeonPortalReceiverResponseCapture>(spec.CapturePath);
            if (capture != null && EditorUtility.IsDirty(capture))
                throw new InvalidOperationException("Persisted capture is dirty and is not trustworthy: '" + spec.CapturePath + "'.");
            return capture;
        }

        private static DungeonPortalEndpointProfile LoadTargetProfile(ReceiverSpec spec)
        {
            return AssetDatabase.LoadAssetAtPath<DungeonPortalEndpointProfile>(spec.ProfilePath);
        }

        private static bool TryGetReceiverSpec(string roomId, out ReceiverSpec spec)
        {
            if (string.Equals(roomId, StartSpec.RoomId, StringComparison.Ordinal))
            {
                spec = StartSpec;
                return true;
            }

            if (string.Equals(roomId, AdminSpec.RoomId, StringComparison.Ordinal))
            {
                spec = AdminSpec;
                return true;
            }

            spec = default;
            return false;
        }

        private static bool MatchesProfileIdentity(DungeonPortalEndpointProfile profile, ReceiverSpec spec)
        {
            return profile != null &&
                   string.Equals(AssetDatabase.GetAssetPath(profile), spec.ProfilePath, StringComparison.Ordinal) &&
                   string.Equals(profile.RoomId, spec.RoomId, StringComparison.Ordinal) &&
                   string.Equals(profile.DoorwayId, StableDoorwayId, StringComparison.Ordinal);
        }

        private static DungeonPortalReceiverBounceFitEvidence.SignalGateEvidence EvaluateTargetSignal(
            DungeonPortalReceiverResponseCapture capture)
        {
            DungeonPortalReceiverResponseCapture.CaptureState baseline = capture.Baseline;
            DungeonPortalReceiverResponseCapture.CaptureState direct = capture.DirectOnly;
            DungeonPortalReceiverResponseCapture.CaptureState full = capture.Full;
            DungeonPortalReceiverBounceFitEvidence.SignalDomainEvidence lightmaps =
                EvaluateRawStoredLightmapRgbSignal(baseline, direct, full);
            DungeonPortalReceiverBounceFitEvidence.SignalDomainEvidence hdr =
                EvaluateFixedCameraLinearHdrSignal(baseline, direct, full);
            DungeonPortalReceiverBounceFitEvidence.ProbeSignalEvidence probes =
                EvaluateProbeL0AndNonDcSignal(baseline, direct, full);
            return new DungeonPortalReceiverBounceFitEvidence.SignalGateEvidence
            {
                imageEpsilon = ImageEpsilon,
                shL0Epsilon = ShL0Epsilon,
                pixelCoverageMetric = DungeonPortalReceiverBounceFitEvidence.PixelCoverageMetric,
                rgbEnergyMetric = DungeonPortalReceiverBounceFitEvidence.RgbEnergyMetric,
                probeL0Metric = DungeonPortalReceiverBounceFitEvidence.ProbeL0Metric,
                probeNonDcMetric = DungeonPortalReceiverBounceFitEvidence.ProbeNonDcMetric,
                lightmaps = lightmaps,
                hdr = hdr,
                probes = probes,
                allRequiredPassed = lightmaps.passed && hdr.passed && probes.passed,
                failureReason = BuildSignalFailureReason(lightmaps, hdr, probes)
            };
        }

        private static DungeonPortalReceiverBounceFitEvidence.SignalDomainEvidence
            EvaluateRawStoredLightmapRgbSignal(
                DungeonPortalReceiverResponseCapture.CaptureState baseline,
                DungeonPortalReceiverResponseCapture.CaptureState direct,
                DungeonPortalReceiverResponseCapture.CaptureState full)
        {
            DungeonPortalReceiverResponseCapture.CaptureLightmap[] baselineMaps = baseline.lightmaps;
            DungeonPortalReceiverResponseCapture.CaptureLightmap[] directMaps = direct.lightmaps;
            DungeonPortalReceiverResponseCapture.CaptureLightmap[] fullMaps = full.lightmaps;
            if (baselineMaps == null || directMaps == null || fullMaps == null ||
                baselineMaps.Length == 0 || baselineMaps.Length != directMaps.Length ||
                baselineMaps.Length != fullMaps.Length)
            {
                return UnevaluatedImageDomain("rawStoredLightmapRgbSignal layout is missing or mismatched.");
            }

            var accumulator = new ImageSignalAccumulator();
            for (int i = 0; i < baselineMaps.Length; i++)
            {
                if (!SameLightmapDimensions(baselineMaps[i], directMaps[i]) ||
                    !SameLightmapDimensions(baselineMaps[i], fullMaps[i]))
                {
                    return UnevaluatedImageDomain("rawStoredLightmapRgbSignal dimensions drifted at atlas " + i + ".");
                }

                if (!TryReadLinearHdrPixels(baselineMaps[i].colorTexture, out Color[] baselinePixels, out string readFailure) ||
                    !TryReadLinearHdrPixels(directMaps[i].colorTexture, out Color[] directPixels, out readFailure) ||
                    !TryReadLinearHdrPixels(fullMaps[i].colorTexture, out Color[] fullPixels, out readFailure) ||
                    baselinePixels.Length != directPixels.Length || baselinePixels.Length != fullPixels.Length)
                {
                    return UnevaluatedImageDomain(
                        "rawStoredLightmapRgbSignal GPU readback failed at atlas " + i + ": " + readFailure);
                }

                for (int pixel = 0; pixel < baselinePixels.Length; pixel++)
                    accumulator.Add(baselinePixels[pixel], directPixels[pixel], fullPixels[pixel], ImageEpsilon);
            }

            return accumulator.ToEvidence();
        }

        private static DungeonPortalReceiverBounceFitEvidence.SignalDomainEvidence
            EvaluateFixedCameraLinearHdrSignal(
                DungeonPortalReceiverResponseCapture.CaptureState baseline,
                DungeonPortalReceiverResponseCapture.CaptureState direct,
                DungeonPortalReceiverResponseCapture.CaptureState full)
        {
            DungeonPortalReceiverResponseCapture.FixedCameraCapture[] baselineCameras = baseline.fixedCameraCaptures;
            DungeonPortalReceiverResponseCapture.FixedCameraCapture[] directCameras = direct.fixedCameraCaptures;
            DungeonPortalReceiverResponseCapture.FixedCameraCapture[] fullCameras = full.fixedCameraCaptures;
            if (baselineCameras == null || directCameras == null || fullCameras == null ||
                baselineCameras.Length != RequiredFixedCameraCount || directCameras.Length != RequiredFixedCameraCount ||
                fullCameras.Length != RequiredFixedCameraCount)
            {
                return UnevaluatedImageDomain("fixedCameraLinearHdr requires the exact four persisted HDR views.");
            }

            var accumulator = new ImageSignalAccumulator();
            for (int camera = 0; camera < RequiredFixedCameraCount; camera++)
            {
                if (!SameCameraContract(baselineCameras[camera], directCameras[camera]) ||
                    !SameCameraContract(baselineCameras[camera], fullCameras[camera]))
                {
                    return UnevaluatedImageDomain("fixedCameraLinearHdr camera contract drifted at ordinal " + camera + ".");
                }

                if (!TryReadLinearHdrPixels(baselineCameras[camera].hdrColorTexture, out Color[] baselinePixels, out string readFailure) ||
                    !TryReadLinearHdrPixels(directCameras[camera].hdrColorTexture, out Color[] directPixels, out readFailure) ||
                    !TryReadLinearHdrPixels(fullCameras[camera].hdrColorTexture, out Color[] fullPixels, out readFailure) ||
                    baselinePixels.Length != directPixels.Length || baselinePixels.Length != fullPixels.Length)
                {
                    return UnevaluatedImageDomain(
                        "fixedCameraLinearHdr GPU readback failed at camera '" +
                        baselineCameras[camera].cameraId + "': " + readFailure);
                }

                for (int pixel = 0; pixel < baselinePixels.Length; pixel++)
                    accumulator.Add(baselinePixels[pixel], directPixels[pixel], fullPixels[pixel], ImageEpsilon);
            }

            return accumulator.ToEvidence();
        }

        private static DungeonPortalReceiverBounceFitEvidence.ProbeSignalEvidence
            EvaluateProbeL0AndNonDcSignal(
                DungeonPortalReceiverResponseCapture.CaptureState baseline,
                DungeonPortalReceiverResponseCapture.CaptureState direct,
                DungeonPortalReceiverResponseCapture.CaptureState full)
        {
            DungeonPortalReceiverResponseCapture.ProbeSample[] baselineProbes = baseline.probes;
            DungeonPortalReceiverResponseCapture.ProbeSample[] directProbes = direct.probes;
            DungeonPortalReceiverResponseCapture.ProbeSample[] fullProbes = full.probes;
            if (baselineProbes == null || directProbes == null || fullProbes == null ||
                baselineProbes.Length != RequiredProbeCount || directProbes.Length != RequiredProbeCount ||
                fullProbes.Length != RequiredProbeCount)
            {
                return UnevaluatedProbeSignal("Probe signal requires the exact 27 doorway-local persisted probes.");
            }

            var accumulator = new ProbeSignalAccumulator();
            for (int probe = 0; probe < RequiredProbeCount; probe++)
            {
                if (baselineProbes[probe].probeIndex != probe || directProbes[probe].probeIndex != probe ||
                    fullProbes[probe].probeIndex != probe ||
                    (baselineProbes[probe].localPosition - directProbes[probe].localPosition).sqrMagnitude > 0.00000001f ||
                    (baselineProbes[probe].localPosition - fullProbes[probe].localPosition).sqrMagnitude > 0.00000001f)
                {
                    return UnevaluatedProbeSignal("Probe local-position contract drifted at ordinal " + probe + ".");
                }

                accumulator.Add(baselineProbes[probe], directProbes[probe], fullProbes[probe], ShL0Epsilon);
            }

            return accumulator.ToEvidence();
        }

        private static DungeonPortalReceiverBounceFitEvidence.SignalDomainEvidence UnevaluatedImageDomain(string failure)
        {
            return new DungeonPortalReceiverBounceFitEvidence.SignalDomainEvidence
            {
                evaluated = false,
                readable = false,
                finite = false,
                passed = false,
                failureReason = failure
            };
        }

        private static DungeonPortalReceiverBounceFitEvidence.ProbeSignalEvidence UnevaluatedProbeSignal(string failure)
        {
            return new DungeonPortalReceiverBounceFitEvidence.ProbeSignalEvidence
            {
                evaluated = false,
                finite = false,
                passed = false,
                failureReason = failure
            };
        }

        private static bool TryReadLinearHdrPixels(Texture2D source, out Color[] pixels, out string error)
        {
            pixels = null;
            error = string.Empty;
            if (source == null || source.width <= 0 || source.height <= 0)
            {
                error = "texture is null or has invalid dimensions";
                return false;
            }

            RenderTexture temporary = null;
            Texture2D readback = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                // The captured EXR importers intentionally remain non-readable. GPU blit
                // followed by a temporary linear half-float readback avoids any importer or
                // source asset mutation while retaining pre-tonemap numeric HDR samples.
                temporary = RenderTexture.GetTemporary(
                    source.width,
                    source.height,
                    0,
                    RenderTextureFormat.ARGBHalf,
                    RenderTextureReadWrite.Linear);
                temporary.filterMode = FilterMode.Point;
                temporary.wrapMode = TextureWrapMode.Clamp;
                Graphics.Blit(source, temporary);
                readback = new Texture2D(source.width, source.height, TextureFormat.RGBAHalf, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                RenderTexture.active = temporary;
                readback.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
                readback.Apply(false, false);
                pixels = readback.GetPixels();
                if (pixels == null || pixels.Length != source.width * source.height)
                {
                    error = "temporary HDR readback produced an unexpected pixel count";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                pixels = null;
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                if (readback != null)
                    UnityEngine.Object.DestroyImmediate(readback);
                if (temporary != null)
                    RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static bool SameLightmapDimensions(
            DungeonPortalReceiverResponseCapture.CaptureLightmap left,
            DungeonPortalReceiverResponseCapture.CaptureLightmap right)
        {
            return left.sourceLightmapIndex == right.sourceLightmapIndex &&
                   left.colorWidth == right.colorWidth && left.colorHeight == right.colorHeight &&
                   left.colorTexture != null && right.colorTexture != null &&
                   left.colorTexture.width == right.colorTexture.width &&
                   left.colorTexture.height == right.colorTexture.height;
        }

        private static bool SameCameraContract(
            DungeonPortalReceiverResponseCapture.FixedCameraCapture left,
            DungeonPortalReceiverResponseCapture.FixedCameraCapture right)
        {
            return string.Equals(left.cameraId, right.cameraId, StringComparison.Ordinal) &&
                   left.width == 512 && left.height == 512 && right.width == 512 && right.height == 512 &&
                   left.hdr && right.hdr && left.linearPreTonemap && right.linearPreTonemap &&
                   Mathf.Abs(left.aspect - 1f) <= 0.0001f && Mathf.Abs(right.aspect - 1f) <= 0.0001f &&
                   Mathf.Abs(left.fieldOfView - 90f) <= 0.0001f && Mathf.Abs(right.fieldOfView - 90f) <= 0.0001f;
        }

        private static Vector3 ComputeShCenter(
            DungeonPortalReceiverResponseCapture.CaptureState direct,
            DungeonPortalReceiverResponseCapture.CaptureState full)
        {
            DungeonPortalReceiverResponseCapture.ProbeSample[] directProbes = direct.probes;
            DungeonPortalReceiverResponseCapture.ProbeSample[] fullProbes = full.probes;
            Vector3 weighted = Vector3.zero;
            float totalWeight = 0f;
            int count = directProbes != null ? directProbes.Length : 0;
            for (int i = 0; i < count && fullProbes != null && i < fullProbes.Length; i++)
            {
                Vector3 delta = fullProbes[i].coefficient0 - directProbes[i].coefficient0;
                float weight = MaxAbs(delta);
                if (!IsFinite(weight) || weight <= 0f)
                    continue;
                weighted += directProbes[i].localPosition * weight;
                totalWeight += weight;
            }

            if (totalWeight > 0f && IsFinite(weighted))
                return weighted / totalWeight;

            Vector3 mean = Vector3.zero;
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                    mean += directProbes[i].localPosition;
                return mean / count;
            }

            return new Vector3(0f, 1f, -1f);
        }

        private static DungeonPortalReceiverBounceFitEvidence.CandidateSearchEvidence
            BuildUnevaluatedCanonicalCandidates(
                DungeonPortalReceiverResponseCapture.ProbeSample[] probes,
                Vector3 shCenterLocal)
        {
            shCenterLocal = EnsureNonCoincidentShCenter(shCenterLocal);
            var result = new DungeonPortalReceiverBounceFitEvidence.CandidateSearchEvidence
            {
                totalCandidateCount = DungeonPortalReceiverBounceFitEvidence.ExpectedCandidateCount,
                pointCandidateCount = 0,
                autoK2CandidateCount = 0,
                spotOnlyPolicy = true,
                autoK2Disabled = true,
                overlapEvidenceAvailable = false,
                sourceSideEvidenceAvailable = false,
                shCenterLocal = shCenterLocal,
                selectedCandidateIndex = -1,
                candidates = new DungeonPortalReceiverBounceFitEvidence.K1CandidateEvidence[
                    DungeonPortalReceiverBounceFitEvidence.ExpectedCandidateCount]
            };

            int index = 0;
            for (int x = 0; x < CandidateXs.Length; x++)
            for (int y = 0; y < CandidateYs.Length; y++)
            for (int depth = 0; depth < CandidateDepths.Length; depth++)
            for (int axis = 0; axis < 2; axis++)
            for (int outer = 0; outer < CandidateOuterAngles.Length; outer++)
            for (int inner = 0; inner < 2; inner++)
            for (int range = 0; range < CandidateRangeFactors.Length; range++)
            {
                Vector3 position = new Vector3(CandidateXs[x], CandidateYs[y], -CandidateDepths[depth]);
                DungeonPortalReceiverBounceFitEvidence.CandidateAxisMode axisMode = axis == 0
                    ? DungeonPortalReceiverBounceFitEvidence.CandidateAxisMode.Inward
                    : DungeonPortalReceiverBounceFitEvidence.CandidateAxisMode.TowardShCenter;
                Vector3 direction = axisMode == DungeonPortalReceiverBounceFitEvidence.CandidateAxisMode.Inward
                    ? Vector3.back
                    : shCenterLocal - position;
                if (direction.sqrMagnitude <= 0.000001f)
                    direction = Vector3.back;
                direction.Normalize();
                float outerAngle = CandidateOuterAngles[outer];
                float innerAngle = inner == 0 ? 0f : outerAngle * 0.5f;
                float baseRange = ComputeCandidateBaseRange(position, probes);
                float angleToInward = Vector3.Angle(direction, Vector3.back);
                float halfCone = outerAngle * 0.5f;
                bool inwardPass = angleToInward + halfCone <= 85.0001f;
                result.candidates[index] = new DungeonPortalReceiverBounceFitEvidence.K1CandidateEvidence
                {
                    candidateIndex = index,
                    type = LightType.Spot,
                    axisMode = axisMode,
                    localPosition = position,
                    localAxis = direction,
                    localEulerAngles = EulerForForward(direction),
                    baseRange = baseRange,
                    range = baseRange * CandidateRangeFactors[range],
                    rangeFactor = CandidateRangeFactors[range],
                    outerSpotAngle = outerAngle,
                    innerSpotAngle = innerAngle,
                    angleToInwardDegrees = angleToInward,
                    halfConeAngleDegrees = halfCone,
                    // This is a doorway-local coordinate contract (z is strictly receiver-inward),
                    // not a substitute for the unavailable 5cm collision/semantic evidence.
                    receiverInteriorGatePassed = true,
                    inwardHalfSpaceGatePassed = inwardPass,
                    overlapGateEvaluated = false,
                    overlapGatePassed = false,
                    sourceSideGateEvaluated = false,
                    sourceSideGatePassed = false,
                    comparableResponseAvailable = false,
                    eligibleForAcceptance = false,
                    fittedRgbGain = Color.black,
                    rejectionReason = "No candidate SH/HDR re-render, 5cm overlap, or source-side semantic-mask evidence is persisted."
                };
                index++;
            }

            if (index != DungeonPortalReceiverBounceFitEvidence.ExpectedCandidateCount)
                throw new InvalidOperationException("Canonical K1 candidate enumeration did not produce 648 entries.");
            return result;
        }

        private static Vector3 EnsureNonCoincidentShCenter(Vector3 value)
        {
            for (int x = 0; x < CandidateXs.Length; x++)
            for (int y = 0; y < CandidateYs.Length; y++)
            for (int depth = 0; depth < CandidateDepths.Length; depth++)
            {
                Vector3 candidatePosition = new Vector3(CandidateXs[x], CandidateYs[y], -CandidateDepths[depth]);
                if ((candidatePosition - value).sqrMagnitude <= 0.00000001f)
                    return value + new Vector3(0.001f, 0f, 0f);
            }

            return value;
        }

        private static float ComputeCandidateBaseRange(
            Vector3 candidatePosition,
            DungeonPortalReceiverResponseCapture.ProbeSample[] probes)
        {
            float furthest = 0f;
            if (probes != null)
            {
                for (int i = 0; i < probes.Length; i++)
                    furthest = Mathf.Max(furthest, Vector3.Distance(candidatePosition, probes[i].localPosition));
            }

            return Mathf.Max(0.1f, furthest + 0.05f);
        }

        private static Vector3 EulerForForward(Vector3 forward)
        {
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(forward, up).eulerAngles;
        }

        private static DungeonPortalReceiverBounceFitEvidence.SemanticMaskEvidence
            BuildUnavailableSemanticMaskEvidence()
        {
            return new DungeonPortalReceiverBounceFitEvidence.SemanticMaskEvidence
            {
                objectIdMasksAvailable = false,
                receiverRegionMaskAvailable = false,
                forbiddenRegionMaskAvailable = false,
                sourceSideMaskAvailable = false,
                fixedCameraRegionMaskAvailable = false,
                policy = "No persisted object-ID/receiver/forbidden/source-side/fixed-camera-region masks exist. " +
                         "Fixed HDR is a four-view linear pre-tonemap target only; rawStoredLightmapRgbSignal " +
                         "excludes directional/shadow visual reconstruction and is not semantic evidence.",
                failureReason = "K1 comparison is fail-closed until semantic and forbidden-region mask evidence is captured."
            };
        }

        private static DungeonPortalReceiverBounceFitEvidence.FitGateEvidence BuildUnavailableFitGates()
        {
            var gates = new DungeonPortalReceiverBounceFitEvidence.FitGate[GateDefinitions.Length];
            for (int i = 0; i < GateDefinitions.Length; i++)
            {
                GateDefinition definition = GateDefinitions[i];
                gates[i] = new DungeonPortalReceiverBounceFitEvidence.FitGate
                {
                    name = definition.Name,
                    evaluated = false,
                    hasMinimum = definition.HasMinimum,
                    hasMaximum = definition.HasMaximum,
                    minimumInclusive = definition.Minimum,
                    maximumInclusive = definition.Maximum,
                    value = 0f,
                    passed = false,
                    failureReason = "No comparable candidate SH/HDR response and semantic-region masks are persisted."
                };
            }

            return new DungeonPortalReceiverBounceFitEvidence.FitGateEvidence
            {
                semanticMasksAvailable = false,
                candidateResponseEvidenceAvailable = false,
                allRequiredPassed = false,
                gates = gates
            };
        }

        private static string BuildSignalFailureReason(
            DungeonPortalReceiverBounceFitEvidence.SignalDomainEvidence lightmaps,
            DungeonPortalReceiverBounceFitEvidence.SignalDomainEvidence hdr,
            DungeonPortalReceiverBounceFitEvidence.ProbeSignalEvidence probes)
        {
            var builder = new StringBuilder();
            if (!lightmaps.passed)
                builder.Append("rawStoredLightmapRgbSignal: ").Append(lightmaps.failureReason).Append(' ');
            if (!hdr.passed)
                builder.Append("fixedCameraLinearHdr: ").Append(hdr.failureReason).Append(' ');
            if (!probes.passed)
                builder.Append("probeL0Dc: ").Append(probes.failureReason).Append(' ');
            return builder.Length == 0 ? string.Empty : builder.ToString().Trim();
        }

        private static string BuildRejectedDecisionReason(
            DungeonPortalReceiverBounceFitEvidence.SignalGateEvidence signals)
        {
            return "K1_REJECTED: target signalQualified=" + (signals.allRequiredPassed ? "PASS" : "REJECTED") +
                   ". The 648 deterministic Spot-only candidates have no persisted candidate SH/HDR re-render, " +
                   "5cm overlap, source-side, object-ID, receiver, forbidden, or fixed-camera-region mask evidence. " +
                   "No profile/connection mutation is authorized. HDR metric is four-view linear pre-tonemap; " +
                   "rawStoredLightmapRgbSignal is copied color-atlas RGB only and does not reconstruct " +
                   "CombinedDirectional surface visual energy. EXR importers were not changed.";
        }

        private static void ValidateEvidenceForApply(
            ReceiverSpec spec,
            DungeonPortalReceiverBounceFitEvidence evidence,
            DungeonPortalReceiverResponseCapture receiverCapture,
            DungeonPortalEndpointProfile profile,
            DungeonPortalReceiverBounceFitEvidence.CaptureIntegrityEvidence integrity)
        {
            if (evidence == null || EditorUtility.IsDirty(evidence) ||
                !string.Equals(AssetDatabase.GetAssetPath(evidence), spec.EvidencePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Apply requires a clean evidence asset at the exact whitelisted evidence path.");
            }

            if (!evidence.IsAccepted)
                throw new InvalidOperationException("Apply only accepts K1_ACCEPTED evidence; this evidence is '" + evidence.Status + "'.");
            if (!evidence.TryValidate(out string evidenceFailure))
            {
                throw new InvalidOperationException(
                    "Apply is fail-closed: evidence is not structurally authorizable in this schema: " + evidenceFailure);
            }

            if (!integrity.startPassed || !integrity.adminPassed || !integrity.receiverPassed ||
                evidence.ReceiverCapture != receiverCapture ||
                !string.Equals(evidence.ReceiverCaptureAssetPath, spec.CapturePath, StringComparison.Ordinal) ||
                !string.Equals(
                    evidence.ReceiverCaptureDependencyHash,
                    AssetDatabase.GetAssetDependencyHash(spec.CapturePath).ToString(),
                    StringComparison.Ordinal) ||
                !string.Equals(evidence.BaselineStateHash, receiverCapture.Baseline.stateHash, StringComparison.Ordinal) ||
                !string.Equals(evidence.DirectOnlyStateHash, receiverCapture.DirectOnly.stateHash, StringComparison.Ordinal) ||
                !string.Equals(evidence.FullStateHash, receiverCapture.Full.stateHash, StringComparison.Ordinal) ||
                !string.Equals(evidence.ReceiverRoomId, spec.RoomId, StringComparison.Ordinal) ||
                !string.Equals(evidence.StableDoorwayId, StableDoorwayId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Apply evidence does not bind to the exact current persisted capture/states/room/doorway.");
            }

            if (evidence.TargetProfile != profile || !MatchesProfileIdentity(profile, spec) ||
                !string.Equals(evidence.TargetProfileAssetPath, spec.ProfilePath, StringComparison.Ordinal) ||
                !string.Equals(
                    evidence.TargetProfileDependencyHash,
                    AssetDatabase.GetAssetDependencyHash(spec.ProfilePath).ToString(),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Apply evidence does not bind to the exact current target endpoint profile/hash.");
            }

            if (!profile.TryValidate(out string profileFailure))
                throw new InvalidOperationException("Target endpoint profile failed structural validation: " + profileFailure);
            ValidateK1DescriptorForProfile(evidence.AcceptedDescriptor);
        }

        private static void ValidatePersistedEvidenceBinding(
            ReceiverSpec spec,
            DungeonPortalReceiverBounceFitEvidence evidence,
            DungeonPortalReceiverResponseCapture capture,
            DungeonPortalEndpointProfile targetProfile)
        {
            string evidenceFailure = string.Empty;
            if (evidence == null || evidence.ReceiverCapture != capture || evidence.TargetProfile != targetProfile ||
                !string.Equals(AssetDatabase.GetAssetPath(evidence), spec.EvidencePath, StringComparison.Ordinal) ||
                !string.Equals(AssetDatabase.GetAssetPath(capture), spec.CapturePath, StringComparison.Ordinal) ||
                !string.Equals(AssetDatabase.GetAssetPath(targetProfile), spec.ProfilePath, StringComparison.Ordinal) ||
                !string.Equals(evidence.ReceiverCaptureAssetPath, spec.CapturePath, StringComparison.Ordinal) ||
                !string.Equals(evidence.TargetProfileAssetPath, spec.ProfilePath, StringComparison.Ordinal) ||
                !string.Equals(
                    evidence.ReceiverCaptureDependencyHash,
                    AssetDatabase.GetAssetDependencyHash(spec.CapturePath).ToString(),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    evidence.TargetProfileDependencyHash,
                    AssetDatabase.GetAssetDependencyHash(spec.ProfilePath).ToString(),
                    StringComparison.Ordinal) ||
                !string.Equals(evidence.ReceiverRoomId, spec.RoomId, StringComparison.Ordinal) ||
                !string.Equals(evidence.StableDoorwayId, StableDoorwayId, StringComparison.Ordinal) ||
                !string.Equals(evidence.BaselineStateHash, capture.Baseline.stateHash, StringComparison.Ordinal) ||
                !string.Equals(evidence.DirectOnlyStateHash, capture.DirectOnly.stateHash, StringComparison.Ordinal) ||
                !string.Equals(evidence.FullStateHash, capture.Full.stateHash, StringComparison.Ordinal) ||
                !evidence.CaptureIntegrity.startPassed || !evidence.CaptureIntegrity.adminPassed ||
                !evidence.CaptureIntegrity.receiverPassed || !evidence.TryValidate(out evidenceFailure))
            {
                throw new InvalidOperationException(
                    "Saved PoC evidence failed exact persisted capture/profile path/hash/state binding: " + evidenceFailure);
            }
        }

        private static void VerifyConnectionSerializedDisabled()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ValidationScenePath) == null)
                throw new InvalidOperationException("Validation scene is missing: '" + ValidationScenePath + "'.");

            Scene scene = EditorSceneManager.OpenScene(ValidationScenePath, OpenSceneMode.Single);
            DungeonPortalTransportConnection[] connections =
                UnityEngine.Object.FindObjectsByType<DungeonPortalTransportConnection>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            if (connections == null || connections.Length != 1)
            {
                throw new InvalidOperationException(
                    "Validation scene must contain exactly one transport connection; found " +
                    (connections != null ? connections.Length : 0) + ".");
            }

            if (connections[0].enabled)
                throw new InvalidOperationException("Validation scene transport connection is enabled; Apply will never enable or modify it.");
            if (scene.isDirty)
                throw new InvalidOperationException("Read-only connection inspection made the validation scene dirty.");
        }

        private static void ValidateK1DescriptorForProfile(
            DungeonPortalReceiverBounceFitEvidence.K1BounceDescriptor descriptor)
        {
            Vector3 axis = Quaternion.Euler(descriptor.localEulerAngles) * Vector3.forward;
            if (descriptor.type != LightType.Spot || !IsFinite(descriptor.localPosition) ||
                !IsFinite(descriptor.localEulerAngles) || descriptor.localPosition.z > -0.05f ||
                !IsFinite(descriptor.rgbGain) || descriptor.rgbGain.r < 0f || descriptor.rgbGain.g < 0f ||
                descriptor.rgbGain.b < 0f || Mathf.Max(descriptor.rgbGain.r, descriptor.rgbGain.g, descriptor.rgbGain.b) <= 0f ||
                descriptor.rgbGain.r > 4f || descriptor.rgbGain.g > 4f || descriptor.rgbGain.b > 4f ||
                !IsFinite(descriptor.range) || descriptor.range < 0.01f ||
                !IsFinite(descriptor.outerSpotAngle) || descriptor.outerSpotAngle < 1f || descriptor.outerSpotAngle > 179f ||
                !IsFinite(descriptor.innerSpotAngle) || descriptor.innerSpotAngle < 0f ||
                descriptor.innerSpotAngle > descriptor.outerSpotAngle || !descriptor.castShadows ||
                !IsFinite(descriptor.shadowStrength) || descriptor.shadowStrength <= 0f || descriptor.shadowStrength > 1f ||
                descriptor.cullingMask != ExpectedCullingMask || descriptor.renderingLayerMask != RequiredRenderingLayerMask ||
                Vector3.Angle(axis, Vector3.back) + descriptor.outerSpotAngle * 0.5f > 85.0001f)
            {
                throw new InvalidOperationException("Accepted descriptor violates the K1 receiver-inward Spot contract.");
            }
        }

        private static DungeonPortalEndpointProfile.PortalBounceLightDescriptor BuildProfileBounceDescriptor(
            DungeonPortalReceiverBounceFitEvidence.K1BounceDescriptor descriptor)
        {
            ValidateK1DescriptorForProfile(descriptor);
            return new DungeonPortalEndpointProfile.PortalBounceLightDescriptor
            {
                label = "ReceiverBounce_K1_Evidenced",
                type = LightType.Spot,
                localPosition = descriptor.localPosition,
                localEulerAngles = descriptor.localEulerAngles,
                responseTint = descriptor.rgbGain,
                responseGainPerUnitSourceRadiance = 1f,
                range = descriptor.range,
                spotAngle = descriptor.outerSpotAngle,
                innerSpotAngle = descriptor.innerSpotAngle,
                castShadows = descriptor.castShadows,
                shadowStrength = descriptor.shadowStrength,
                cullingMask = descriptor.cullingMask,
                renderingLayerMask = descriptor.renderingLayerMask
            };
        }

        private static DungeonPortalEndpointProfile.PortalDirectLightDescriptor[] CloneDirectLights(
            DungeonPortalEndpointProfile.PortalDirectLightDescriptor[] source)
        {
            return source != null
                ? (DungeonPortalEndpointProfile.PortalDirectLightDescriptor[])source.Clone()
                : Array.Empty<DungeonPortalEndpointProfile.PortalDirectLightDescriptor>();
        }

        private static DungeonPortalEndpointProfile.PortalBounceLightDescriptor[] CloneBounceLights(
            DungeonPortalEndpointProfile.PortalBounceLightDescriptor[] source)
        {
            return source != null
                ? (DungeonPortalEndpointProfile.PortalBounceLightDescriptor[])source.Clone()
                : Array.Empty<DungeonPortalEndpointProfile.PortalBounceLightDescriptor>();
        }

        private static bool DirectLightsByteEquivalent(
            DungeonPortalEndpointProfile.PortalDirectLightDescriptor[] left,
            DungeonPortalEndpointProfile.PortalDirectLightDescriptor[] right)
        {
            int leftLength = left != null ? left.Length : 0;
            int rightLength = right != null ? right.Length : 0;
            if (leftLength != rightLength)
                return false;
            for (int i = 0; i < leftLength; i++)
            {
                DungeonPortalEndpointProfile.PortalDirectLightDescriptor a = left[i];
                DungeonPortalEndpointProfile.PortalDirectLightDescriptor b = right[i];
                if (!string.Equals(a.label, b.label, StringComparison.Ordinal) || a.type != b.type ||
                    !VectorBitsEqual(a.localPosition, b.localPosition) || !VectorBitsEqual(a.localEulerAngles, b.localEulerAngles) ||
                    !ColorBitsEqual(a.power0Color, b.power0Color) || !ColorBitsEqual(a.power100Color, b.power100Color) ||
                    !FloatBitsEqual(a.power0Intensity, b.power0Intensity) || !FloatBitsEqual(a.power100Intensity, b.power100Intensity) ||
                    !FloatBitsEqual(a.range, b.range) || !FloatBitsEqual(a.spotAngle, b.spotAngle) ||
                    !FloatBitsEqual(a.innerSpotAngle, b.innerSpotAngle) || a.castShadows != b.castShadows ||
                    !FloatBitsEqual(a.shadowStrength, b.shadowStrength) || a.cullingMask.value != b.cullingMask.value ||
                    a.renderingLayerMask != b.renderingLayerMask || !AssetIdentityEqual(a.cookie, b.cookie))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool BounceDescriptorEquivalent(
            DungeonPortalEndpointProfile.PortalBounceLightDescriptor expected,
            DungeonPortalEndpointProfile.PortalBounceLightDescriptor actual)
        {
            return string.Equals(expected.label, actual.label, StringComparison.Ordinal) && expected.type == actual.type &&
                   VectorBitsEqual(expected.localPosition, actual.localPosition) &&
                   VectorBitsEqual(expected.localEulerAngles, actual.localEulerAngles) &&
                   ColorBitsEqual(expected.responseTint, actual.responseTint) &&
                   FloatBitsEqual(expected.responseGainPerUnitSourceRadiance, actual.responseGainPerUnitSourceRadiance) &&
                   FloatBitsEqual(expected.range, actual.range) && FloatBitsEqual(expected.spotAngle, actual.spotAngle) &&
                   FloatBitsEqual(expected.innerSpotAngle, actual.innerSpotAngle) &&
                   expected.castShadows == actual.castShadows &&
                   FloatBitsEqual(expected.shadowStrength, actual.shadowStrength) &&
                   expected.cullingMask.value == actual.cullingMask.value &&
                   expected.renderingLayerMask == actual.renderingLayerMask;
        }

        private static bool AssetIdentityEqual(UnityEngine.Object left, UnityEngine.Object right)
        {
            if (left != right)
                return false;
            if (left == null)
                return true;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(left, out string leftGuid, out long leftLocalId);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(right, out string rightGuid, out long rightLocalId);
            return string.Equals(leftGuid, rightGuid, StringComparison.Ordinal) && leftLocalId == rightLocalId &&
                   string.Equals(AssetDatabase.GetAssetPath(left), AssetDatabase.GetAssetPath(right), StringComparison.Ordinal);
        }

        private static bool FloatBitsEqual(float left, float right)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(left), 0) ==
                   BitConverter.ToInt32(BitConverter.GetBytes(right), 0);
        }

        private static bool VectorBitsEqual(Vector3 left, Vector3 right)
        {
            return FloatBitsEqual(left.x, right.x) && FloatBitsEqual(left.y, right.y) && FloatBitsEqual(left.z, right.z);
        }

        private static bool ColorBitsEqual(Color left, Color right)
        {
            return FloatBitsEqual(left.r, right.r) && FloatBitsEqual(left.g, right.g) &&
                   FloatBitsEqual(left.b, right.b) && FloatBitsEqual(left.a, right.a);
        }

        private static string ComputeAssetFileSha256(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || assetPath.IndexOf("..", StringComparison.Ordinal) >= 0 ||
                !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to hash a non-Assets or parent-traversal scene path.");
            }

            string assetsRoot = Path.GetFullPath(Application.dataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string projectRoot = Directory.GetParent(assetsRoot.TrimEnd(Path.DirectorySeparatorChar)).FullName;
            string fullPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            if (!fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to hash a path outside this project's Assets root.");
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Asset file is missing for SHA256 guard.", fullPath);
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(fullPath))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return;
            int separator = assetPath.LastIndexOf('/');
            if (separator <= 0)
                throw new InvalidOperationException("Cannot create malformed PoC asset folder: '" + assetPath + "'.");
            string parent = assetPath.Substring(0, separator);
            string child = assetPath.Substring(separator + 1);
            EnsureAssetFolder(parent);
            if (AssetDatabase.CreateFolder(parent, child).Length == 0 && !AssetDatabase.IsValidFolder(assetPath))
                throw new InvalidOperationException("Unable to create PoC evidence folder: '" + assetPath + "'.");
        }

        private sealed class ImageSignalAccumulator
        {
            private int sampleCount;
            private int directActiveCount;
            private int indirectActiveCount;
            private int negativeSampleCount;
            private double positiveEnergy;
            private double negativeEnergy;
            private double directEnergy;
            private double indirectEnergy;
            private float maxAbsoluteChannel;
            private bool finite = true;

            public void Add(Color baseline, Color direct, Color full, float epsilon)
            {
                sampleCount++;
                if (!IsFinite(baseline) || !IsFinite(direct) || !IsFinite(full))
                {
                    finite = false;
                    return;
                }

                Color directDelta = direct - baseline;
                Color indirectDelta = full - direct;
                if (!IsFinite(directDelta) || !IsFinite(indirectDelta))
                {
                    finite = false;
                    return;
                }

                maxAbsoluteChannel = Mathf.Max(
                    maxAbsoluteChannel,
                    MaxAbs(baseline),
                    MaxAbs(direct),
                    MaxAbs(full),
                    MaxAbs(directDelta),
                    MaxAbs(indirectDelta));

                // Coverage is deliberately pixel=max(abs(RGB_delta)), while the
                // independent signed-energy gate below remains per RGB channel.
                if (MaxAbs(directDelta) > epsilon)
                    directActiveCount++;
                if (MaxAbs(indirectDelta) > epsilon)
                    indirectActiveCount++;
                if (HasNegativeBeyond(directDelta, epsilon) || HasNegativeBeyond(indirectDelta, epsilon))
                    negativeSampleCount++;

                AddSignedEnergy(directDelta, ref positiveEnergy, ref negativeEnergy);
                AddSignedEnergy(indirectDelta, ref positiveEnergy, ref negativeEnergy);
                directEnergy += RgbL1(directDelta);
                indirectEnergy += RgbL1(indirectDelta);
            }

            public DungeonPortalReceiverBounceFitEvidence.SignalDomainEvidence ToEvidence()
            {
                if (!finite || sampleCount <= 0)
                    return UnevaluatedImageDomain("non-finite or empty numeric image signal.");

                float directCoverage = (float)directActiveCount / sampleCount;
                float indirectCoverage = (float)indirectActiveCount / sampleCount;
                float negativeFraction = (float)negativeSampleCount / sampleCount;
                float negativeToPositive = SafeRatio(negativeEnergy, positiveEnergy);
                float indirectToDirect = SafeRatio(indirectEnergy, directEnergy);
                bool passed = maxAbsoluteChannel < 64f && directCoverage >= 0.01f &&
                              indirectCoverage >= 0.0025f && negativeFraction <= 0.01f &&
                              negativeToPositive <= 0.05f && indirectToDirect <= 4f;
                return new DungeonPortalReceiverBounceFitEvidence.SignalDomainEvidence
                {
                    evaluated = true,
                    readable = true,
                    sampleCount = sampleCount,
                    directPositiveSampleCount = directActiveCount,
                    indirectPositiveSampleCount = indirectActiveCount,
                    significantNegativeSampleCount = negativeSampleCount,
                    directPositiveCoverage = directCoverage,
                    indirectPositiveCoverage = indirectCoverage,
                    significantNegativeFraction = negativeFraction,
                    negativeToPositiveEnergy = negativeToPositive,
                    indirectToDirectEnergy = indirectToDirect,
                    maxAbsoluteChannel = maxAbsoluteChannel,
                    finite = true,
                    passed = passed,
                    failureReason = passed ? string.Empty :
                        "Coverage/negative-energy/ratio/max-channel signal gate did not meet its canonical threshold."
                };
            }
        }

        private sealed class ProbeSignalAccumulator
        {
            private int probeCount;
            private int directActiveCount;
            private int indirectActiveCount;
            private int negativeProbeCount;
            private double positiveEnergy;
            private double negativeEnergy;
            private double directEnergy;
            private double indirectEnergy;
            private double nonDcSquaredSum;
            private int nonDcComponentCount;
            private float maxAbsoluteL0Coefficient;
            private float maxAbsoluteNonDcCoefficient;
            private bool finite = true;
            private readonly List<ProbeRank> indirectL0Ranks = new List<ProbeRank>(RequiredProbeCount);

            public void Add(
                DungeonPortalReceiverResponseCapture.ProbeSample baseline,
                DungeonPortalReceiverResponseCapture.ProbeSample direct,
                DungeonPortalReceiverResponseCapture.ProbeSample full,
                float epsilon)
            {
                probeCount++;
                Vector3 baselineL0 = baseline.coefficient0;
                Vector3 directL0 = direct.coefficient0;
                Vector3 fullL0 = full.coefficient0;
                if (!IsFinite(baselineL0) || !IsFinite(directL0) || !IsFinite(fullL0))
                {
                    finite = false;
                    return;
                }

                Vector3 directDelta = directL0 - baselineL0;
                Vector3 indirectDelta = fullL0 - directL0;
                maxAbsoluteL0Coefficient = Mathf.Max(
                    maxAbsoluteL0Coefficient,
                    MaxAbs(baselineL0),
                    MaxAbs(directL0),
                    MaxAbs(fullL0),
                    MaxAbs(directDelta),
                    MaxAbs(indirectDelta));
                if (MaxAbs(directDelta) > epsilon)
                    directActiveCount++;
                if (MaxAbs(indirectDelta) > epsilon)
                    indirectActiveCount++;
                if (HasNegativeBeyond(directDelta, epsilon) || HasNegativeBeyond(indirectDelta, epsilon))
                    negativeProbeCount++;
                AddSignedEnergy(directDelta, ref positiveEnergy, ref negativeEnergy);
                AddSignedEnergy(indirectDelta, ref positiveEnergy, ref negativeEnergy);
                directEnergy += RgbL1(directDelta);
                indirectEnergy += RgbL1(indirectDelta);
                indirectL0Ranks.Add(new ProbeRank(baseline.probeIndex, MaxAbs(indirectDelta)));

                // SH coefficients 1..8 are basis-signed. They are retained as L2/RMS
                // diagnostics only and never feed negative/positive energy gates.
                for (int coefficient = 1; coefficient < 9; coefficient++)
                {
                    Vector3 baselineCoefficient = baseline.GetCoefficient(coefficient);
                    Vector3 directCoefficient = direct.GetCoefficient(coefficient);
                    Vector3 fullCoefficient = full.GetCoefficient(coefficient);
                    if (!IsFinite(baselineCoefficient) || !IsFinite(directCoefficient) || !IsFinite(fullCoefficient))
                    {
                        finite = false;
                        return;
                    }

                    Vector3 indirectCoefficient = fullCoefficient - directCoefficient;
                    maxAbsoluteNonDcCoefficient = Mathf.Max(
                        maxAbsoluteNonDcCoefficient,
                        MaxAbs(baselineCoefficient),
                        MaxAbs(directCoefficient),
                        MaxAbs(fullCoefficient),
                        MaxAbs(indirectCoefficient));
                    nonDcSquaredSum += indirectCoefficient.x * indirectCoefficient.x +
                                       indirectCoefficient.y * indirectCoefficient.y +
                                       indirectCoefficient.z * indirectCoefficient.z;
                    nonDcComponentCount += 3;
                }
            }

            public DungeonPortalReceiverBounceFitEvidence.ProbeSignalEvidence ToEvidence()
            {
                if (!finite || probeCount <= 0 || nonDcComponentCount <= 0)
                    return UnevaluatedProbeSignal("non-finite or empty L0/DC/non-DC probe signal.");

                indirectL0Ranks.Sort(ProbeRankComparer.Instance);
                int topCount = Mathf.Min(8, indirectL0Ranks.Count);
                int[] indices = new int[topCount];
                float[] magnitudes = new float[topCount];
                for (int i = 0; i < topCount; i++)
                {
                    indices[i] = indirectL0Ranks[i].Index;
                    magnitudes[i] = indirectL0Ranks[i].Magnitude;
                }

                float directCoverage = (float)directActiveCount / probeCount;
                float indirectCoverage = (float)indirectActiveCount / probeCount;
                float negativeFraction = (float)negativeProbeCount / probeCount;
                float negativeToPositive = SafeRatio(negativeEnergy, positiveEnergy);
                float indirectToDirect = SafeRatio(indirectEnergy, directEnergy);
                float l2Magnitude = (float)Math.Sqrt(nonDcSquaredSum);
                float rms = (float)Math.Sqrt(nonDcSquaredSum / nonDcComponentCount);
                bool passed = maxAbsoluteL0Coefficient < 64f && maxAbsoluteNonDcCoefficient < 64f &&
                              directCoverage >= 0.01f && indirectCoverage >= 0.0025f &&
                              indirectActiveCount >= 3 && negativeFraction <= 0.01f &&
                              negativeToPositive <= 0.05f && indirectToDirect <= 4f;
                return new DungeonPortalReceiverBounceFitEvidence.ProbeSignalEvidence
                {
                    evaluated = true,
                    probeCount = probeCount,
                    directPositiveProbeCount = directActiveCount,
                    indirectPositiveProbeCount = indirectActiveCount,
                    significantNegativeProbeCount = negativeProbeCount,
                    directPositiveCoverage = directCoverage,
                    indirectPositiveCoverage = indirectCoverage,
                    significantNegativeFraction = negativeFraction,
                    negativeToPositiveEnergy = negativeToPositive,
                    indirectToDirectEnergy = indirectToDirect,
                    maxAbsoluteCoefficient = maxAbsoluteL0Coefficient,
                    indirectL2Magnitude = l2Magnitude,
                    indirectNonDcRms = rms,
                    maxAbsoluteNonDcCoefficient = maxAbsoluteNonDcCoefficient,
                    finite = true,
                    passed = passed,
                    topIndirectL0ProbeIndices = indices,
                    topIndirectL0Magnitudes = magnitudes,
                    failureReason = passed ? string.Empty :
                        "L0/DC coverage, L0/DC negative-energy, energy-ratio, probe-count, or finite/max signal gate failed."
                };
            }

            private readonly struct ProbeRank
            {
                public readonly int Index;
                public readonly float Magnitude;

                public ProbeRank(int index, float magnitude)
                {
                    Index = index;
                    Magnitude = magnitude;
                }
            }

            private sealed class ProbeRankComparer : IComparer<ProbeRank>
            {
                public static readonly ProbeRankComparer Instance = new ProbeRankComparer();

                public int Compare(ProbeRank left, ProbeRank right)
                {
                    int descendingMagnitude = right.Magnitude.CompareTo(left.Magnitude);
                    return descendingMagnitude != 0 ? descendingMagnitude : left.Index.CompareTo(right.Index);
                }
            }
        }

        private static void AddSignedEnergy(Vector3 value, ref double positive, ref double negative)
        {
            AddSignedChannel(value.x, ref positive, ref negative);
            AddSignedChannel(value.y, ref positive, ref negative);
            AddSignedChannel(value.z, ref positive, ref negative);
        }

        private static void AddSignedEnergy(Color value, ref double positive, ref double negative)
        {
            AddSignedChannel(value.r, ref positive, ref negative);
            AddSignedChannel(value.g, ref positive, ref negative);
            AddSignedChannel(value.b, ref positive, ref negative);
        }

        private static void AddSignedChannel(float value, ref double positive, ref double negative)
        {
            if (value >= 0f)
                positive += value;
            else
                negative -= value;
        }

        private static float RgbL1(Vector3 value)
        {
            return Mathf.Abs(value.x) + Mathf.Abs(value.y) + Mathf.Abs(value.z);
        }

        private static float RgbL1(Color value)
        {
            return Mathf.Abs(value.r) + Mathf.Abs(value.g) + Mathf.Abs(value.b);
        }

        private static bool HasNegativeBeyond(Vector3 value, float epsilon)
        {
            return value.x < -epsilon || value.y < -epsilon || value.z < -epsilon;
        }

        private static bool HasNegativeBeyond(Color value, float epsilon)
        {
            return value.r < -epsilon || value.g < -epsilon || value.b < -epsilon;
        }

        private static float MaxAbs(Vector3 value)
        {
            return Mathf.Max(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private static float MaxAbs(Color value)
        {
            return Mathf.Max(Mathf.Abs(value.r), Mathf.Abs(value.g), Mathf.Abs(value.b));
        }

        private static float SafeRatio(double numerator, double denominator)
        {
            if (denominator <= 0d)
                return numerator <= 0d ? 0f : float.MaxValue;
            double ratio = numerator / denominator;
            return ratio >= float.MaxValue || double.IsNaN(ratio) || double.IsInfinity(ratio)
                ? float.MaxValue
                : (float)ratio;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Color value)
        {
            return IsFinite(value.r) && IsFinite(value.g) && IsFinite(value.b) && IsFinite(value.a);
        }

        private static bool TryValidateEditorState(out string failure)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                failure = "Unity is in or transitioning Play Mode; no fit/apply transaction was started.";
                return false;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                failure = "Unity is compiling or updating; no fit/apply transaction was started.";
                return false;
            }

            if (Lightmapping.isRunning)
            {
                failure = "A lightmapping bake is already running; fitter never overlaps a bake.";
                return false;
            }

            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                failure = "A Prefab Stage is open; close it before an isolated fit/apply transaction.";
                return false;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;
                if (string.IsNullOrWhiteSpace(scene.path))
                {
                    failure = "A loaded scene is untitled: '" + scene.name + "'.";
                    return false;
                }
                if (scene.isDirty)
                {
                    failure = "A loaded scene is dirty: '" + scene.path + "'.";
                    return false;
                }
            }

            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf))
            {
                failure = "The editor GPU lacks the required temporary ARGBHalf/RGBAHalf readback path.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private sealed class EditorSessionSnapshot
        {
            private readonly SceneSetup[] sceneSetup;
            private readonly string activeScenePath;
            private readonly UnityEngine.Object[] selection;
            private readonly UnityEngine.Object activeSelection;
            private readonly Lightmapping.BakeOnSceneLoadMode bakeOnSceneLoadMode;

            private EditorSessionSnapshot(
                SceneSetup[] sceneSetup,
                string activeScenePath,
                UnityEngine.Object[] selection,
                UnityEngine.Object activeSelection,
                Lightmapping.BakeOnSceneLoadMode bakeOnSceneLoadMode)
            {
                this.sceneSetup = sceneSetup;
                this.activeScenePath = activeScenePath;
                this.selection = selection;
                this.activeSelection = activeSelection;
                this.bakeOnSceneLoadMode = bakeOnSceneLoadMode;
            }

            public static EditorSessionSnapshot Capture()
            {
                Scene active = SceneManager.GetActiveScene();
                return new EditorSessionSnapshot(
                    (SceneSetup[])EditorSceneManager.GetSceneManagerSetup().Clone(),
                    active.path,
                    Selection.objects != null ? (UnityEngine.Object[])Selection.objects.Clone() :
                        Array.Empty<UnityEngine.Object>(),
                    Selection.activeObject,
                    Lightmapping.bakeOnSceneLoad);
            }

            public void Restore()
            {
                Lightmapping.BakeOnSceneLoadMode expectedBakeMode = bakeOnSceneLoadMode;
                Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;
                try
                {
                    EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
                    if (!string.IsNullOrWhiteSpace(activeScenePath))
                    {
                        Scene active = SceneManager.GetSceneByPath(activeScenePath);
                        if (!active.IsValid() || !active.isLoaded ||
                            (!string.Equals(
                                 SceneManager.GetActiveScene().path,
                                 activeScenePath,
                                 StringComparison.Ordinal) &&
                             !EditorSceneManager.SetActiveScene(active)))
                            throw new InvalidOperationException("Unable to restore active scene '" + activeScenePath + "'.");
                    }

                    Selection.objects = selection ?? Array.Empty<UnityEngine.Object>();
                    Selection.activeObject = activeSelection;
                }
                finally
                {
                    Lightmapping.bakeOnSceneLoad = expectedBakeMode;
                }
            }

            public void AssertRestoredClean()
            {
                int expectedLoaded = 0;
                for (int i = 0; i < sceneSetup.Length; i++)
                {
                    SceneSetup expected = sceneSetup[i];
                    if (!expected.isLoaded)
                        continue;
                    expectedLoaded++;
                    Scene scene = SceneManager.GetSceneByPath(expected.path);
                    if (!scene.IsValid() || !scene.isLoaded || scene.isDirty)
                    {
                        throw new InvalidOperationException(
                            "Original SceneSetup was not restored cleanly: '" + expected.path + "'.");
                    }
                }

                if (SceneManager.sceneCount != expectedLoaded ||
                    (!string.IsNullOrWhiteSpace(activeScenePath) &&
                     !string.Equals(SceneManager.GetActiveScene().path, activeScenePath, StringComparison.Ordinal)) ||
                    Lightmapping.bakeOnSceneLoad != bakeOnSceneLoadMode ||
                    Selection.activeObject != activeSelection ||
                    !SameObjectSequence(Selection.objects, selection))
                {
                    throw new InvalidOperationException("Exact scene setup, active scene, selection, or bakeOnSceneLoad was not restored.");
                }
            }

            private static bool SameObjectSequence(UnityEngine.Object[] left, UnityEngine.Object[] right)
            {
                int leftLength = left != null ? left.Length : 0;
                int rightLength = right != null ? right.Length : 0;
                if (leftLength != rightLength)
                    return false;
                for (int i = 0; i < leftLength; i++)
                {
                    if (left[i] != right[i])
                        return false;
                }
                return true;
            }
        }

        private static bool IsPass(string value)
        {
            return !string.IsNullOrEmpty(value) && value.StartsWith("PASS", StringComparison.Ordinal);
        }

        private static void LogResult(string result)
        {
            if (IsPass(result))
                Debug.Log(result);
            else
                Debug.LogError(result);
        }

        private readonly struct ReceiverSpec
        {
            public readonly string RoomId;

            public ReceiverSpec(string roomId)
            {
                RoomId = roomId;
            }

            public string CapturePath => CaptureRoot + "/" + RoomId + "/" + RoomId + "_ReceiverResponseCapture.asset";
            public string ProfilePath => EndpointProfilesRoot + "/" + RoomId + "_EndpointProfile.asset";
            public string EvidencePath => EvidenceRoot + "/" + RoomId + "_K1ReceiverBounceFitEvidence.asset";
        }

        private readonly struct GateDefinition
        {
            public readonly string Name;
            public readonly bool HasMinimum;
            public readonly float Minimum;
            public readonly bool HasMaximum;
            public readonly float Maximum;

            public GateDefinition(string name, bool hasMinimum, float minimum, bool hasMaximum, float maximum)
            {
                Name = name;
                HasMinimum = hasMinimum;
                Minimum = minimum;
                HasMaximum = hasMaximum;
                Maximum = maximum;
            }
        }
    }
}
